// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonTTS

#pragma warning disable CA2255

namespace Season.TTS.GGML;

/// <summary>
/// Resolves the qwentts.cpp shared object on Linux desktop.
///
/// ld.so resolves DT_NEEDED entries from the loader search path only - it never
/// looks next to the library being opened - so opening libqwen.so by name fails
/// unless libggml.so.0 / libggml-base.so.0 are already in the process. The resolver
/// therefore dlopen's the ggml chain first, then libqwen.so, by absolute path;
/// glibc registers each SONAME on load, which then satisfies the NEEDED entries.
/// Windows resolves through the DLL search path (the MSBuild Content items copy the
/// .dll files to the output root) and the Apple platforms link or bundle the
/// runtime, so only Linux needs this. Android needs nothing here either: the .so
/// files ship in the APK's jniLibs and the platform linker searches the app's
/// native library directory for the library and its DT_NEEDED entries alike, so
/// default probing resolves the chain.
/// </summary>
internal static class QwenNativeResolver
{
    [ModuleInitializer]
    public static void Init()
    {
        if (OperatingSystem.IsLinux())
        {
            NativeLibrary.SetDllImportResolver(typeof(QwenNativeResolver).Assembly, Resolve);
        }
    }

    // QwenNative P/Invokes "libqwen" on Linux (/ "qwen" on Windows); both spellings
    // are intercepted so the resolver keeps working if the const flips.
    private static readonly HashSet<string> ManagedLibraries = ["qwen", "libqwen"];

    private static readonly object s_lock = new();
    private static readonly Dictionary<string, IntPtr> Handles = new();

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!ManagedLibraries.Contains(libraryName))
        {
            return IntPtr.Zero; // let the runtime handle it
        }

        lock (s_lock)
        {
            if (Handles.TryGetValue(libraryName, out var cached) && cached != IntPtr.Zero)
            {
                return cached;
            }

            var directories = EnumerateNativeSearchDirectories();

            // Dependencies first, in NEEDED order. Preloading is idempotent (dlopen
            // returns the existing mapping), so repeating it per library is fine.
            Preload(directories, "libggml-base.so", assembly, searchPath);
            Preload(directories, "libggml.so", assembly, searchPath);

            foreach (var directory in directories)
            {
                var candidate = Path.Combine(directory, "libqwen.so");
                if (File.Exists(candidate) &&
                    NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                {
                    Handles[libraryName] = handle;
                    return handle;
                }
            }
        }

        // Fall back to the OS default probing (e.g. the library sits on the system
        // loader search path).
        return IntPtr.Zero;
    }

    private static void Preload(string[] directories, string fileName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                NativeLibrary.TryLoad(candidate, assembly, searchPath, out _);
                return;
            }
        }
    }

    /// <summary>
    /// The two layouts a consumer sees: the flat output root (<c>Content</c> items
    /// with <c>Link</c> flatten the RID folders) and the RID layout preserved inside
    /// the NuGet package.
    /// </summary>
    private static string[] EnumerateNativeSearchDirectories()
    {
        var baseDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var directories = new List<string>(2) { baseDirectory };

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };
        if (arch is not null)
        {
            directories.Add(Path.Combine(baseDirectory, "runtimes", $"linux-{arch}", "native"));
        }

        return directories.ToArray();
    }
}

#pragma warning restore CA2255

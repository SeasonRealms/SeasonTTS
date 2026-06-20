
// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonTTS

namespace SeasonTTS.GGML;

internal static class QwenNativeResolver
{
    //private static readonly HashSet<string> ManagedLibraries =
    //[
    //    "qwen",
    //    "libqwen",
    //    "libggml-cpu",
    //    "libggml-cuda",
    //    //"libggml-vulkan",
    //    "libggml-metal",
    //    "libggml-blas",
    //];

    // Only intercept "qwen" (the library we P/Invoke).
    // ggml.dll and ggml-backend DLLs are loaded natively by qwen.dll
    // via LoadLibrary — the managed resolver CANNOT intercept those.
    // To force CPU backend, delete ggml-vulkan.dll from the output
    // directory (see Sample.csproj build target).
    private static readonly HashSet<string> ManagedLibraries = ["qwen"];

    private static readonly Dictionary<string, IntPtr> Handles = new();

    [ModuleInitializer]
    public static void Init()
    {
#if LINUX
        NativeLibrary.SetDllImportResolver(typeof(QwenNativeResolver).Assembly, Resolve1);
#elif WINDOWS

#endif
    }

    private static IntPtr Resolve1(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!ManagedLibraries.Contains(libraryName))
            return IntPtr.Zero; // let runtime handle

        if (Handles.TryGetValue(libraryName, out IntPtr cached) && cached != IntPtr.Zero)
            return cached;

        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // Canonical NuGet layout: runtimes/<rid>/native/
        string rid = OperatingSystem.IsLinux()
            ? $"linux-{arch}"
            : $"osx-{arch}";

        string ext = OperatingSystem.IsLinux() ? ".so" : ".dylib";
        string runtimeLibPath = Path.Combine(baseDir, "runtimes", rid, "native", $"{libraryName}{ext}");

        IntPtr handle = IntPtr.Zero;

        if (File.Exists(runtimeLibPath))
            NativeLibrary.TryLoad(runtimeLibPath, assembly, searchPath, out handle);

        // Fallback to OS default
        if (handle == IntPtr.Zero)
            NativeLibrary.TryLoad(libraryName, assembly, searchPath, out handle);

        if (handle != IntPtr.Zero)
            Handles[libraryName] = handle;

        return handle;
    }
}

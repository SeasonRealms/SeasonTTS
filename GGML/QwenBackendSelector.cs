
// Copyright (c) SeasonEngine and contributors.
// Licensed under the MIT License.
// https://github.com/SeasonRealms/SeasonTTS

namespace SeasonTTS.GGML;

/// <summary>
/// Dynamically enables or disables the GGML Vulkan backend at runtime
/// based on GPU vendor detection.
///
/// <para><b>Architecture:</b> <c>ggml-vulkan.dll</c> is shipped in a
/// <c>gpu-backends\</c> subdirectory (not in the EXE root).  At
/// runtime we stage it into a writable <c>%LocalAppData%</c> folder
/// and call <c>SetDllDirectory</c> to add that folder to the Windows
/// DLL search path.  GGML's native <c>LoadLibrary("ggml-vulkan.dll")</c>
/// then finds (or doesn't find) the DLL based on the GPU vendor:</para>
///
/// <list type="bullet">
///   <item><b>Intel GPU</b> → Vulkan DLL stays in <c>gpu-backends\</c>
///        (not copied to the writable staging dir).  GGML probes for
///        <c>ggml-vulkan.dll</c>, misses it, and falls back to CPU.</item>
///   <item><b>NVIDIA / AMD GPU</b> → Vulkan DLL is copied from
///        <c>gpu-backends\</c> into the writable staging dir.  GGML
///        finds it via <c>SetDllDirectory</c> and uses Vulkan.</item>
/// </list>
///
/// <para>This approach <b>never modifies files in the application
/// directory</b>, making it safe for MSIX-packaged WinUI 3 apps where
/// the install directory is read-only.</para>
///
/// <para>Must be called once before any <see cref="QwenEngine"/> is
/// constructed.</para>
/// </summary>
public static class QwenBackendSelector
{
    private static bool _selected;
    private static string? _stagingDir;

    /// <summary>
    /// Stage the Vulkan backend DLL into a writable directory (or not,
    /// depending on GPU vendor) and configure the Windows DLL search
    /// path so GGML can find it.
    ///
    /// <para>Safe to call multiple times — the decision is cached.</para>
    /// </summary>
    public static void SelectBestBackend()
    {
        if (_selected)
            return;
        _selected = true;

#if WINDOWS
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string sourceVulkan = Path.Combine(baseDir, "vulkan", "ggml-vulkan.dll");

        if (!File.Exists(sourceVulkan))
        {
            // No Vulkan DLL shipped at all (e.g. CPU-only build).
            // Nothing to stage — GGML will use CPU naturally.
            System.Diagnostics.Debug.WriteLine(
                "[SeasonTTS2] QwenBackendSelector no gpu-backends\\ggml-vulkan.dll — CPU only");
            return;
        }

        bool intel = IsIntelGpuPrimary();

        // Writable staging directory under LocalAppData.
        //_stagingDir = Path.Combine(
        //    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        //    "SeasonEngine", "backend");
        //Directory.CreateDirectory(_stagingDir);

        //string stagingVulkan = Path.Combine(_stagingDir, "ggml-vulkan.dll");

        if (intel)
        {
            // Intel GPU → ensure NO Vulkan DLL in the staging dir.
            //TryDelete(stagingVulkan);
        }
        else
        {
            // NVIDIA / AMD → copy Vulkan DLL to staging dir.
            //CopyIfNewer(sourceVulkan, stagingVulkan);

            SetDllDirectoryW(sourceVulkan);
        }

        // Add the staging directory to Windows' DLL search path.
        // GGML's native LoadLibrary("ggml-vulkan.dll") will search
        // there and find (or not find) the DLL accordingly.

        System.Diagnostics.Debug.WriteLine(
            $"[SeasonTTS2] QwenBackendSelector GPU={(intel ? "Intel" : "non-Intel")} " +
            $"vulkanEnabled={!intel} staging='{_stagingDir}'");
#else
        // Non-Windows: backend selection is handled by the platform's
        // native loader / env vars.  No-op.
#endif
    }

    /// <summary>
    /// Detect whether the primary display adapter is an Intel GPU
    /// by scanning the Windows display adapter registry keys for the
    /// Intel vendor ID (<c>VEN_8086</c>).
    /// </summary>
    private static bool IsIntelGpuPrimary()
    {
#if WINDOWS
        try
        {
            const string adapterKey =
                @"SYSTEM\CurrentControlSet\Control\Class\" +
                @"{4d36e968-e325-11ce-bfc1-08002be10318}";

            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(adapterKey);
            if (key is null)
                return false; // can't read → safe default: assume non-Intel

            foreach (string subKeyName in key.GetSubKeyNames())
            {
                // Skip non-numeric subkeys (e.g. "Properties")
                if (!int.TryParse(subKeyName, out _))
                    continue;

                using var subKey = key.OpenSubKey(subKeyName);
                if (subKey is null)
                    continue;

                // MatchingDeviceId looks like: pci\ven_8086&dev_...
                var deviceId = subKey.GetValue("MatchingDeviceId") as string;
                if (!string.IsNullOrEmpty(deviceId) &&
                    deviceId.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // If registry access fails (e.g. sandboxed), default to
            // safe CPU-only mode — Vulkan stays disabled.
            return true;
        }
#endif
        // Non-Windows: Vulkan is typically fine; detection is left to
        // platform-specific paths (Android, Linux).
        return false;
    }

    // ── Windows DLL search path control ────────────────────────────

#if WINDOWS
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectoryW(string lpPathName);
#endif

    // ── File helpers ───────────────────────────────────────────────

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort — if we can't delete a stale file, GGML
            // might still load Vulkan.  The user can manually clean
            // %LocalAppData%\SeasonEngine\backend\ if needed.
        }
    }

    /// <summary>
    /// Copy <paramref name="source"/> to <paramref name="dest"/> only
    /// if the destination is missing or older than the source.
    /// </summary>
    private static void CopyIfNewer(string source, string dest)
    {
        try
        {
            if (!File.Exists(dest) ||
                File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(dest))
            {
                File.Copy(source, dest, overwrite: true);
            }
        }
        catch
        {
            // If copy fails (disk full, permissions), GGML won't find
            // Vulkan and will fall back to CPU — safe default.
        }
    }
}

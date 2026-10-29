using System.Diagnostics;
using Typrite.Contracts.Releases;

namespace Typrite.Agent.Updates;

/// <summary>
/// Downloads a verified package and hands it to msiexec. Only runs on Windows; elsewhere it stops after verification
/// and reports what it would have done. One attempt per version so a failing installer cannot loop.
/// </summary>
public sealed partial class UpdateInstaller(Identity.DeviceApiClient api, string stagingDirectory, ILogger<UpdateInstaller> log)
{
    private const long MaxPackageBytes = 512L * 1024 * 1024;

    public async Task<bool> InstallAsync(ReleaseManifest manifest, CancellationToken ct)
    {
        Directory.CreateDirectory(stagingDirectory);
        var bytes = await api.DownloadAsync(manifest.PackageUrl, MaxPackageBytes, ct).ConfigureAwait(false);
        if (!UpdateDecider.PackageMatches(manifest, bytes))
        {
            LogHashMismatch(log, manifest.Version);
            return false;
        }

        var path = Path.Combine(stagingDirectory, $"typrite-{manifest.Version}-{manifest.CpuArchitecture}.msi");
        await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);

        if (!OperatingSystem.IsWindows())
        {
            LogVerifiedOnly(log, manifest.Version, path);
            return false;
        }

        var logPath = Path.Combine(stagingDirectory, $"install-{manifest.Version}.log");
        using var process = Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{path}\" /qn /norestart /l*v \"{logPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("msiexec did not start");
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        LogInstallerExit(log, manifest.Version, process.ExitCode);
        return process.ExitCode is 0 or 3010; // 3010: success, reboot required
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Package for {Version} did not match the manifest hash; discarded")]
    private static partial void LogHashMismatch(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Verified package {Version} at {Path}; installation only runs on Windows")]
    private static partial void LogVerifiedOnly(ILogger logger, string version, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "msiexec for {Version} exited with {ExitCode}")]
    private static partial void LogInstallerExit(ILogger logger, string version, int exitCode);
}

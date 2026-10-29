using System.Diagnostics;
using System.Security.Cryptography;
using Typrite.Contracts.Releases;

namespace Typrite.Agent.Updates;

/// <summary>
/// Downloads a verified package and hands it to msiexec. The package is written under a random name through a handle
/// that stays open (readable, not writable by anyone else) until the installer exits, and is re-hashed from that same
/// handle immediately before launch, so nothing can be swapped between verification and execution.
/// Only runs on Windows; elsewhere it stops after staging. One attempt per version so a failing installer cannot loop.
/// </summary>
public sealed partial class UpdateInstaller(Identity.DeviceApiClient api, string stagingDirectory, ILogger<UpdateInstaller> log)
{
    private const long MaxPackageBytes = 512L * 1024 * 1024;

    public async Task<bool> InstallAsync(ReleaseManifest manifest, CancellationToken ct)
    {
        var bytes = await api.DownloadAsync(manifest.PackageUrl, MaxPackageBytes, ct).ConfigureAwait(false);
        if (!UpdateDecider.PackageMatches(manifest, bytes))
        {
            LogHashMismatch(log, manifest.Version);
            return false;
        }

        var staging = StagingDirectory.Prepare(stagingDirectory);
        var path = Path.Combine(staging, $"{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}.msi");

        // FileShare.Read: msiexec can read; nobody can open it for write or delete while this handle lives.
        await using var handle = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 1 << 16, FileOptions.Asynchronous);
        await handle.WriteAsync(bytes, ct).ConfigureAwait(false);
        await handle.FlushAsync(ct).ConfigureAwait(false);

        handle.Position = 0;
        var onDisk = Convert.ToHexStringLower(await SHA256.HashDataAsync(handle, ct).ConfigureAwait(false));
        if (!onDisk.Equals(manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            LogHashMismatch(log, manifest.Version);
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            LogVerifiedOnly(log, manifest.Version, path);
            return false;
        }

        var logPath = Path.Combine(staging, $"install-{manifest.Version}.log");
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Verified and staged package {Version} at {Path}; installation only runs on Windows")]
    private static partial void LogVerifiedOnly(ILogger logger, string version, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "msiexec for {Version} exited with {ExitCode}")]
    private static partial void LogInstallerExit(ILogger logger, string version, int exitCode);
}

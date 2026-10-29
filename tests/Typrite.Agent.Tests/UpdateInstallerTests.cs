using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Typrite.Agent.Identity;
using Typrite.Agent.Updates;
using Typrite.Contracts.Releases;

namespace Typrite.Agent.Tests;

public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _staging = Path.Combine(Path.GetTempPath(), $"typrite-staging-{Guid.NewGuid():N}");

    private sealed class FakeHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private UpdateInstaller Installer(byte[] package) =>
        new(new DeviceApiClient(new HttpClient(new FakeHandler(package)) { BaseAddress = new Uri("https://api.test/") }, TimeProvider.System),
            _staging, NullLogger<UpdateInstaller>.Instance);

    private static ReleaseManifest Manifest(byte[] package) =>
        new("1.0.0", "x64", Convert.ToHexStringLower(SHA256.HashData(package)), new Uri("https://cdn.test/t.msi"), 100);

    public void Dispose()
    {
        if (Directory.Exists(_staging))
        {
            Directory.Delete(_staging, recursive: true);
        }
    }

    [Fact]
    public async Task Package_is_staged_under_an_unpredictable_name_and_verified_from_the_open_handle()
    {
        byte[] package = [1, 2, 3, 4];

        var installed = await Installer(package).InstallAsync(Manifest(package), CancellationToken.None);

        var files = Directory.GetFiles(_staging, "*.msi");
        var staged = Assert.Single(files);
        Assert.DoesNotContain("1.0.0", Path.GetFileName(staged));
        Assert.Equal(package, await File.ReadAllBytesAsync(staged));
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(installed); // staging and verification only; msiexec runs on Windows
        }
    }

    [Fact]
    public async Task Tampered_download_is_discarded_and_nothing_is_staged()
    {
        byte[] package = [1, 2, 3, 4];
        var manifest = Manifest(package) with { PackageSha256 = new string('f', 64) };

        var installed = await Installer(package).InstallAsync(manifest, CancellationToken.None);

        Assert.False(installed);
        Assert.Empty(Directory.Exists(_staging) ? Directory.GetFiles(_staging, "*.msi") : []);
    }

    [Fact]
    public void Staging_path_that_is_a_reparse_point_is_refused()
    {
        var target = Path.Combine(Path.GetTempPath(), $"typrite-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(_staging, target);

            Assert.Throws<InvalidOperationException>(() => StagingDirectory.Prepare(_staging));
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }
}

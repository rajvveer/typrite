using System.Security.Cryptography;
using Typrite.Domain.Devices;

namespace Typrite.Domain.Tests;

public class ProofOfPossessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Signed_proof_verifies_with_the_matching_public_key()
    {
        using var rsa = RSA.Create(2048);
        var deviceId = Guid.NewGuid();
        var proof = ProofOfPossession.Create(deviceId, Now, rsa);
        var spki = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());

        Assert.True(ProofOfPossession.Verify(deviceId, proof, spki, Now.AddSeconds(30)));
    }

    [Fact]
    public void Different_key_fails()
    {
        using var rsa = RSA.Create(2048);
        using var other = RSA.Create(2048);
        var deviceId = Guid.NewGuid();
        var proof = ProofOfPossession.Create(deviceId, Now, rsa);

        Assert.False(ProofOfPossession.Verify(deviceId, proof, Convert.ToBase64String(other.ExportSubjectPublicKeyInfo()), Now));
    }

    [Fact]
    public void Proof_for_another_device_fails()
    {
        using var rsa = RSA.Create(2048);
        var proof = ProofOfPossession.Create(Guid.NewGuid(), Now, rsa);

        Assert.False(ProofOfPossession.Verify(Guid.NewGuid(), proof, Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), Now));
    }

    [Fact]
    public void Stale_or_future_timestamps_fail()
    {
        using var rsa = RSA.Create(2048);
        var deviceId = Guid.NewGuid();
        var spki = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());

        Assert.False(ProofOfPossession.Verify(deviceId, ProofOfPossession.Create(deviceId, Now.AddMinutes(-10), rsa), spki, Now));
        Assert.False(ProofOfPossession.Verify(deviceId, ProofOfPossession.Create(deviceId, Now.AddMinutes(10), rsa), spki, Now));
    }

    [Fact]
    public void Tampered_nonce_fails()
    {
        using var rsa = RSA.Create(2048);
        var deviceId = Guid.NewGuid();
        var proof = ProofOfPossession.Create(deviceId, Now, rsa) with { Nonce = "ffff" };

        Assert.False(ProofOfPossession.Verify(deviceId, proof, Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), Now));
    }

    [Fact]
    public void Garbage_public_key_fails_instead_of_throwing()
    {
        using var rsa = RSA.Create(2048);
        var deviceId = Guid.NewGuid();
        var proof = ProofOfPossession.Create(deviceId, Now, rsa);

        Assert.False(ProofOfPossession.Verify(deviceId, proof, "not-a-key", Now));
    }
}

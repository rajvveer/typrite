using System.Security.Cryptography;
using System.Text;

namespace Typrite.Domain.Devices;

/// <summary>Agent proves it holds the device private key when asking for a short-lived token.</summary>
public sealed record DeviceProof(DateTimeOffset TimestampUtc, string Nonce, string SignatureBase64);

public static class ProofOfPossession
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public static DeviceProof Create(Guid deviceId, DateTimeOffset timestampUtc, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var signature = privateKey.SignData(Message(deviceId, timestampUtc, nonce), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return new DeviceProof(timestampUtc, nonce, Convert.ToBase64String(signature));
    }

    public static bool Verify(Guid deviceId, DeviceProof proof, string publicKeySpkiBase64, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(proof);
        if (proof.TimestampUtc < now - Window || proof.TimestampUtc > now + Window)
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeySpkiBase64), out _);
            return rsa.VerifyData(Message(deviceId, proof.TimestampUtc, proof.Nonce), Convert.FromBase64String(proof.SignatureBase64),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }

    private static byte[] Message(Guid deviceId, DateTimeOffset timestampUtc, string nonce) =>
        Encoding.UTF8.GetBytes($"typrite-pop|{deviceId}|{timestampUtc.ToUnixTimeSeconds()}|{nonce}");
}

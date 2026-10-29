using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Typrite.Agent.Identity;

/// <summary>
/// The device private key. On Windows it lives in CNG, preferring the TPM-backed Platform Crypto Provider and falling
/// back to the software KSP, as a machine key the agent's service account owns. Elsewhere (development) it is a PEM
/// file in the data directory. The key never leaves the store; callers only sign.
/// </summary>
public sealed class DeviceKeyStore : IDisposable
{
    public const string KeyName = "Typrite.Agent.DeviceKey";
    private readonly RSA _rsa;

    private DeviceKeyStore(RSA rsa) => _rsa = rsa;

    public string PublicKeySpkiBase64 => Convert.ToBase64String(_rsa.ExportSubjectPublicKeyInfo());

    public RSA Key => _rsa;

    public static DeviceKeyStore Open(string dataDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return new DeviceKeyStore(OpenCng());
        }

        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "device-key.pem");
        var rsa = RSA.Create(2048);
        if (File.Exists(path))
        {
            rsa.ImportFromPem(File.ReadAllText(path));
        }
        else
        {
            File.WriteAllText(path, rsa.ExportPkcs8PrivateKeyPem());
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        return new DeviceKeyStore(rsa);
    }

    public void Dispose() => _rsa.Dispose();

    [SupportedOSPlatform("windows")]
    private static RSACng OpenCng()
    {
        if (CngKey.Exists(KeyName, CngProvider.MicrosoftPlatformCryptoProvider, CngKeyOpenOptions.MachineKey))
        {
            return new RSACng(CngKey.Open(KeyName, CngProvider.MicrosoftPlatformCryptoProvider, CngKeyOpenOptions.MachineKey));
        }

        if (CngKey.Exists(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey))
        {
            return new RSACng(CngKey.Open(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey));
        }

        foreach (var provider in new[] { CngProvider.MicrosoftPlatformCryptoProvider, CngProvider.MicrosoftSoftwareKeyStorageProvider })
        {
            try
            {
                var parameters = new CngKeyCreationParameters
                {
                    Provider = provider,
                    KeyCreationOptions = CngKeyCreationOptions.MachineKey,
                    ExportPolicy = CngExportPolicies.None,
                    KeyUsage = CngKeyUsages.Signing,
                };
                parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
                return new RSACng(CngKey.Create(CngAlgorithm.Rsa, KeyName, parameters));
            }
            catch (CryptographicException) when (provider == CngProvider.MicrosoftPlatformCryptoProvider)
            {
                // No usable TPM; fall through to the software provider.
            }
        }

        throw new CryptographicException("Could not create the device key in any CNG provider.");
    }
}

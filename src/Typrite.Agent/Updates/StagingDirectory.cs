using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Typrite.Agent.Updates;

/// <summary>
/// The update staging folder is where a SYSTEM-level installer reads from, so it must not be writable by users.
/// On Windows it is created with inheritance disabled and full control for SYSTEM, Administrators and the agent's own
/// account only; an existing folder owned by anyone else, or any reparse point, is refused rather than repaired.
/// </summary>
public static class StagingDirectory
{
    public static string Prepare(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Directory.Exists(path) || File.Exists(path))
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException($"Staging path '{path}' is a reparse point; refusing to use it.");
            }

            if (!Directory.Exists(path))
            {
                throw new InvalidOperationException($"Staging path '{path}' exists but is not a directory.");
            }

            if (OperatingSystem.IsWindows())
            {
                VerifyOwnerWindows(path);
            }

            return path;
        }

        if (OperatingSystem.IsWindows())
        {
            CreateWindows(path);
        }
        else
        {
            Directory.CreateDirectory(path);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    [SupportedOSPlatform("windows")]
    private static void CreateWindows(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var flags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in TrustedSids())
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, flags, PropagationFlags.None, AccessControlType.Allow));
        }

        new DirectoryInfo(path).Create(security);
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyOwnerWindows(string path)
    {
        var owner = new DirectoryInfo(path).GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is null || !TrustedSids().Any(t => t.Equals(owner)))
        {
            throw new InvalidOperationException($"Staging directory '{path}' is owned by {owner}; refusing to stage installers there.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<SecurityIdentifier> TrustedSids()
    {
        yield return new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        yield return new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        if (WindowsIdentity.GetCurrent().User is { } me)
        {
            yield return me;
        }
    }
}

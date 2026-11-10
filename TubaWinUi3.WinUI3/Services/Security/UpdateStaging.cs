using System.Security.AccessControl;
using System.Security.Principal;

namespace TubaWinUi3.Services;

internal static class UpdateStaging
{
    internal static DirectorySecurity CreateSecurity()
    {
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var security = new DirectorySecurity();
        security.SetOwner(administrators);
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { administrators, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    internal static bool IsProtected(DirectorySecurity security) => security.AreAccessRulesProtected
        && security.GetOwner(typeof(SecurityIdentifier)).Value is "S-1-5-32-544" or "S-1-5-18"
        && !security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Any(rule =>
            rule.AccessControlType == AccessControlType.Allow && rule.IdentityReference.Value is not ("S-1-5-32-544" or "S-1-5-18"));

    internal static async Task<FileStream> PrepareAsync(string source, CancellationToken ct)
    {
        await using var verifiedSource = await UpdatePackageTrust.OpenVerifiedAsync(source, ct, DownloadIntegrityDialog.ConfirmAsync);
        // TEMP's owner can rename its children. Use an administrator-owned ProgramData directory,
        // with a fresh private child and no attacker-supplied adjacent DLLs.
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TubaWinUi3.SecureUpdates");
        SafeSharePath.RejectReparsePoints(root);
        var directory = new DirectoryInfo(root);
        directory.Create(CreateSecurity());
        if (!IsProtected(directory.GetAccessControl())) throw new UnauthorizedAccessException("更新暂存目录权限不安全，已阻止安装");
        var child = new DirectoryInfo(Path.Combine(root, Guid.NewGuid().ToString("N")));
        child.Create(CreateSecurity());
        var target = Path.Combine(child.FullName, Path.GetFileName(source));
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            await verifiedSource.CopyToAsync(output, ct);
        var staged = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        try
        {
            verifiedSource.Position = 0;
            var digest = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(verifiedSource, ct));
            await TrustedDownloads.VerifyAsync(staged, digest, ct);
            return staged;
        }
        catch { await staged.DisposeAsync(); throw; }
    }
}

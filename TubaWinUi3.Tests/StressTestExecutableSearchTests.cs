using System.Security.AccessControl;
using System.Security.Principal;
using TubaWinUi3.Controls;

namespace TubaWinUi3.Tests
{
    /// <summary>
    /// 一键三烤查找 Prime95（StressTestControl.TryFindFile）的回归测试。
    ///
    /// 背景：查找先在 Tools 下找，找不到时回退递归扫描 Program Files；
    /// 原实现用 <c>Directory.GetFiles(..., SearchOption.AllDirectories)</c>，该重载
    /// IgnoreInaccessible=false——机器上只要存在一个拒绝访问的目录
    /// （如系统 ReparsePoint「C:\Program Files\Windows NT\附件」），整个查找就抛
    /// UnauthorizedAccessException，点「一键三烤」直接报错。
    /// </summary>
    public class StressTestExecutableSearchTests
    {
        [Fact]
        public void TryFindFile_SkipsDeniedSubdirectory_InsteadOfThrowing()
        {
            var root = Path.Combine(Path.GetTempPath(), "TubaStressFind_" + Guid.NewGuid().ToString("N"));
            var deniedDir = new DirectoryInfo(Path.Combine(root, "denied"));
            Directory.CreateDirectory(deniedDir.FullName);

            var rule = new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User!,
                FileSystemRights.ListDirectory,
                AccessControlType.Deny);
            var security = deniedDir.GetAccessControl();
            security.AddAccessRule(rule);
            deniedDir.SetAccessControl(security);

            try
            {
                // 目录树里只有拒绝访问的子目录：旧实现抛 UnauthorizedAccessException，现在应安静返回 null
                Assert.Null(StressTestControl.TryFindFile(root, "prime95.exe"));
            }
            finally
            {
                var restore = deniedDir.GetAccessControl();
                restore.RemoveAccessRule(rule);
                deniedDir.SetAccessControl(restore);
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void TryFindFile_FindsExecutable_InNestedDirectory()
        {
            var root = Path.Combine(Path.GetTempPath(), "TubaStressFind_" + Guid.NewGuid().ToString("N"));
            var nested = Path.Combine(root, "处理器工具", "Prime95");
            Directory.CreateDirectory(nested);
            var target = Path.Combine(nested, "prime95.exe");
            File.WriteAllText(target, "");

            try
            {
                Assert.Equal(target, StressTestControl.TryFindFile(root, "prime95.exe"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

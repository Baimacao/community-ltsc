using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// LTSC D-01: 自动更新功能已整体移除。
    ///
    /// 原实现 2792 行：dev-api.dy.ci 与 GitHub Releases 两个来源 → 多线路下载
    /// 安装包 → 校验 → 静默时段判断 → 关掉主程序 → 起 update-mode 子进程替换文件。
    /// 所有者裁决「建议去掉整个更新功能」。
    ///
    /// 这个类只保留启动兼容与占位成员。**所有联网、下载、替换文件的能力都已删除**，
    /// 留下的成员一律返回「没有更新 / 不可用」：
    ///   - HandleUpdateModeStartup：恒 false，旧版残留的 --update-mode 参数按正常启动处理
    ///   - DeleteUpdatesFolder：仍清理旧版留下的 AutoUpdate 目录（纯本地文件操作）
    ///   - 其余：恒 false / null / 空集合
    ///
    /// 保留这些签名而不是把类型整体删掉，是因为 App_Startup、MainWindow、
    /// MW_Timer 里还有调用点；逐个删调用面比留空实现风险大，而空实现的行为
    /// 是「永不更新」，正是本次决策要的结果。
    /// </summary>
    public static class AutoUpdateHelper
    {
        /// <summary>
        /// 更新线路分组。保留类型是因为 Settings 与旧配置文件里可能出现该字段。
        /// </summary>
        public class UpdateLineGroup
        {
            public string GroupName { get; set; } = "";
            public string Name { get; set; } = "";
            public string Url { get; set; } = "";
            public int Priority { get; set; } = 0;
        }

        public static ObservableCollection<string> Hours { get; set; } = new ObservableCollection<string>();
        public static ObservableCollection<string> Minutes { get; set; } = new ObservableCollection<string>();

        public static readonly Dictionary<UpdateChannel, List<UpdateLineGroup>> ChannelLineGroups
            = new Dictionary<UpdateChannel, List<UpdateLineGroup>>();

        public static void RequestCancelDownload() { }
        public static void InvalidateOrderedGroupsCache() { }

        public static bool HandleUpdateModeStartup(string[] args)
        {
            if (args != null)
            {
                foreach (var a in args)
                {
                    if (string.Equals(a, "--update-mode", StringComparison.OrdinalIgnoreCase))
                    {
                        LogHelper.WriteLogToFile(
                            "AutoUpdate | 检测到旧版更新模式参数（--update-mode），本版本已移除更新功能，按正常启动继续",
                            LogHelper.LogType.Warning);
                        break;
                    }
                }
            }
            return false;
        }

        public static void DeleteUpdatesFolder()
        {
            try
            {
                string dir = Path.Combine(App.RootPath ?? AppDomain.CurrentDomain.BaseDirectory, "AutoUpdate");
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                    LogHelper.WriteLogToFile("AutoUpdate | 已清理旧版更新残留目录");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"AutoUpdate | 清理旧版更新残留目录失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        public static bool IsX64UpdatePackageSelected() => false;
        public static string AppendX64SuffixBeforeZipExtension(string url) => url;
        public static string GetUpdateZipFileName(string version) => null;
        public static string GetUpdateDownloadStatusFilePath(string version) => null;
        public static string GetLocalUpdateZipFilePath(string version) => null;

        public static Task<List<UpdateLineGroup>> GetAvailableLineGroupsOrdered(UpdateChannel channel)
            => Task.FromResult(new List<UpdateLineGroup>());

        public static Task<(string remoteVersion, UpdateLineGroup lineGroup, string releaseNotes)>
            CheckForUpdates(UpdateChannel channel = UpdateChannel.Release, bool alwaysGetRemote = false, bool isVersionFix = false)
            => Task.FromResult<(string, UpdateLineGroup, string)>((null, null, null));

        public static Task<bool> DownloadSetupFile(string version, UpdateLineGroup group)
            => Task.FromResult(false);

        public static Task<bool> DownloadSetupFileWithFallback(string version, List<UpdateLineGroup> groups, Action<double, string> progressCallback = null)
            => Task.FromResult(false);

        public static Task<bool> DownloadFile(string fileUrl, string destinationPath, Action<double, string> progressCallback = null)
            => Task.FromResult(false);

        public static void InstallNewVersionApp(string version, bool isInSilence) { }

        public static Task<string> GetRemoteContent(string fileUrl) => Task.FromResult(string.Empty);
        public static Task<string> GetUpdateLogWithLineGroup(UpdateLineGroup group) => Task.FromResult(string.Empty);
        public static Task<string> GetUpdateLog(UpdateChannel channel = UpdateChannel.Release) => Task.FromResult(string.Empty);

        public static Task<bool> FixVersion(UpdateChannel channel = UpdateChannel.Release) => Task.FromResult(false);

        public static Task<List<(string version, string downloadUrl, string releaseNotes)>>
            GetAllGithubReleases(UpdateChannel channel = UpdateChannel.Release)
            => Task.FromResult(new List<(string, string, string)>());

        public static Task<bool> TestWindows7TlsConnection() => Task.FromResult(false);

        public static Task<DateTime?> GetVersionReleaseTime(string version, UpdateChannel channel = UpdateChannel.Release)
            => Task.FromResult<DateTime?>(null);

        public static Task<bool> StartManualDownloadAndInstall(string version, UpdateChannel channel, Action<double, string> progressCallback = null)
            => Task.FromResult(false);

        public static void InitializeAutoUpdateWithSilenceTimeComboBoxOptions(ComboBox startTimeComboBox, ComboBox endTimeComboBox)
        {
        }

        public static bool CheckIsInSilencePeriod(string startTime, string endTime) => false;
    }
}

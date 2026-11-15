using Ink_Canvas.Helpers;
using Ink_Canvas.WorkflowAutomation.Abstractions;
using Ink_Canvas.WorkflowAutomation.Triggers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ink_Canvas.WorkflowAutomation.SettingsControls
{
    /// <summary>
    /// 「进程检测」触发器的设置界面。
    ///
    /// 原来用的是自动生成的默认界面——只有一个字符串输入框，让老师手打进程名。
    /// 实际上没人知道要打什么，打错了也不报错，看起来配好了但永远不触发。
    ///
    /// 现在给三样东西：
    ///   1. 从**正在运行的程序**里选（主要方式）
    ///   2. 手动输入（补充，应对程序没开时先配好）
    ///   3. 明确的空配置 / 「已在运行」提示
    /// </summary>
    public partial class ProcessDetectedSettingsControl : TriggerSettingsControlBase
    {
        /// <summary>界面正在回填数据时不要反向写回设置，避免抖动。</summary>
        private bool _suppressWriteBack;

        /// <summary>当前设置对象（基类是非泛型的，这里自己持有强类型引用）。</summary>
        private ProcessDetectedSettings TypedSettings { get; set; } = new ProcessDetectedSettings();

        public ProcessDetectedSettingsControl()
        {
            InitializeComponent();
            RefreshRunningApps();
        }

        public override object Settings
        {
            get => TypedSettings;
            set
            {
                TypedSettings = value as ProcessDetectedSettings ?? new ProcessDetectedSettings();

                _suppressWriteBack = true;
                try
                {
                    TextBoxManual.Text = TypedSettings.ProcessName ?? string.Empty;
                    UpdateCurrentLabel();
                    UpdateHint();
                }
                finally
                {
                    _suppressWriteBack = false;
                }
            }
        }

        /// <summary>刷新「正在运行的程序」下拉列表。</summary>
        private void RefreshRunningApps()
        {
            try
            {
                var current = TypedSettings?.ProcessName ?? string.Empty;

                // 只列有可见主窗口的进程。后台服务对老师没意义，
                // 混进来只会让列表变长、找不到目标。
                var apps = new List<(string ProcessName, string Title)>();
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.MainWindowHandle == IntPtr.Zero) continue;
                        var title = p.MainWindowTitle;
                        if (string.IsNullOrWhiteSpace(title)) continue;
                        if (string.Equals(p.ProcessName, "ICCCELite", StringComparison.OrdinalIgnoreCase))
                            continue;
                        apps.Add((p.ProcessName, title));
                    }
                    catch { }
                    finally { p.Dispose(); }
                }

                var items = apps
                    .GroupBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                ComboBoxRunningApps.Items.Clear();
                foreach (var app in items)
                {
                    var label = string.IsNullOrWhiteSpace(app.Title)
                        ? app.ProcessName
                        : app.ProcessName + "  —  " + Shorten(app.Title, 40);

                    ComboBoxRunningApps.Items.Add(new ComboBoxItem
                    {
                        Content = label,
                        Tag = app.ProcessName
                    });
                }

                // 回填当前值
                if (!string.IsNullOrEmpty(current))
                {
                    var match = ComboBoxRunningApps.Items
                        .OfType<ComboBoxItem>()
                        .FirstOrDefault(it => string.Equals(it.Tag as string, current,
                            StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        _suppressWriteBack = true;
                        ComboBoxRunningApps.SelectedItem = match;
                        _suppressWriteBack = false;
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"刷新运行中程序列表失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
            return text.Substring(0, max - 1) + "…";
        }

        private void ComboBoxRunningApps_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressWriteBack) return;
            if (ComboBoxRunningApps.SelectedItem is not ComboBoxItem item) return;

            var name = item.Tag as string;
            if (string.IsNullOrEmpty(name)) return;

            _suppressWriteBack = true;
            TextBoxManual.Text = name;
            _suppressWriteBack = false;

            TypedSettings.ProcessName = name;
            TypedSettings.DisplayName = item.Content as string ?? name;

            UpdateCurrentLabel();
            UpdateHint();
        }

        private void TextBoxManual_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressWriteBack) return;

            var text = TextBoxManual.Text?.Trim() ?? string.Empty;
            // 用户可能连 .exe 一起粘进来
            if (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(0, text.Length - 4);

            TypedSettings.ProcessName = text;
            TypedSettings.DisplayName = string.Empty;

            UpdateCurrentLabel();
            UpdateHint();
        }

        private void UpdateCurrentLabel()
        {
            var name = TypedSettings?.ProcessName ?? string.Empty;
            TextBlockCurrent.Text = string.IsNullOrWhiteSpace(name)
                ? "（未选择 —— 这个触发器现在不会触发）"
                : name;
            TextBlockCurrent.Opacity = string.IsNullOrWhiteSpace(name) ? 0.55 : 1.0;
        }

        /// <summary>
        /// 根据当前配置给提示。
        ///
        /// 三种情况要说清楚，否则老师会以为「配好了但不管用」：
        ///   1. 没配进程名 → 永远不会触发
        ///   2. 配了但该程序此刻正在运行 → 现在不会触发，要关掉重开
        ///   3. 配了、程序没在跑 → 正常
        /// </summary>
        private void UpdateHint()
        {
            var name = TypedSettings?.ProcessName ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                ShowHint("还没有选择要监控的程序，这个触发器不会触发。", isWarning: true);
                return;
            }

            bool running;
            try
            {
                running = Process.GetProcessesByName(name).Length > 0;
            }
            catch
            {
                running = false;
            }

            if (running)
            {
                ShowHint(
                    "「" + name + "」现在正在运行。触发器只在程序从「没运行」变成「运行」的那一刻触发，" +
                    "所以现在不会触发 —— 把程序关掉再打开一次就能看到效果。",
                    isWarning: true);
            }
            else
            {
                ShowHint("已设置监控「" + name + "」，它启动时会触发。", isWarning: false);
            }
        }

        private void ShowHint(string text, bool isWarning)
        {
            TextBlockHint.Text = text;
            BorderHint.Visibility = Visibility.Visible;

            BorderHint.BorderBrush = new SolidColorBrush(isWarning
                ? Color.FromArgb(0x40, 0xF0, 0xA0, 0x20)
                : Color.FromArgb(0x40, 0x10, 0x80, 0x30));
            BorderHint.Background = new SolidColorBrush(isWarning
                ? Color.FromArgb(0x1F, 0xF0, 0xA0, 0x20)
                : Color.FromArgb(0x1F, 0x10, 0x80, 0x30));
        }

        private void ButtonRefresh_Click(object sender, RoutedEventArgs e) => RefreshRunningApps();
    }
}

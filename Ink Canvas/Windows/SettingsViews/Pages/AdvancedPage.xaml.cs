using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using iNKORE.UI.WPF.Modern.Controls;
using OSVersionExtension;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ContentDialog = iNKORE.UI.WPF.Modern.Controls.ContentDialog;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class AdvancedPage : Page
    {
        private bool _isLoaded = false;
        private bool _isRefreshingConfigProfileList = false;
        private string _lastAppliedProfileName;

        // 自动校准相关
        private int _calibrateStep = 0;
        private double _nibTouchWidth = 0;
        private double _fingerTouchWidth = 0;
        private double _palmTouchWidth = 0;

        public AdvancedPage()
        {
            InitializeComponent();
            Loaded += Page_Loaded;
            Unloaded += Page_Unloaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            LoadExperimentalSettings();
            _isLoaded = true;
            RefreshConfigProfileList();
            UpdateAllSliderTexts();
            SliderTouchHelper.AddTouchSupportToAllSliders(this);
        }

        private void UpdateAllSliderTexts()
        {
            UpdateSliderText(TouchMultiplierSlider, TouchMultiplierText, "{0:F2}");
            UpdateSliderText(NibModeBoundsWidthSlider, NibModeBoundsWidthText, "{0:0}");
            UpdateSliderText(FingerModeBoundsWidthSlider, FingerModeBoundsWidthText, "{0:0}");
        }

        private void UpdateSliderText(Slider slider, TextBlock textBlock, string format)
        {
            if (slider == null || textBlock == null) return;
            textBlock.Text = string.Format(format, slider.Value);
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
        }

        private MainWindow GetMainWindow() => Application.Current.MainWindow as MainWindow;

        private void LoadSettings()
        {
            var settings = SettingsManager.Settings;
            if (settings?.Advanced == null) return;

            ToggleSwitchIsSpecialScreen.IsOn = settings.Advanced.IsSpecialScreen;
            ToggleSwitchDisableHardwareAcceleration.IsOn = !settings.Canvas.UseHardwareAcceleration;
            try { ComboAdminLaunch.SelectedIndex = Math.Max(0, Math.Min(2, settings.Advanced.AdminLaunchAction)); } catch { ComboAdminLaunch.SelectedIndex = 0; }
            TouchMultiplierSlider.Value = settings.Advanced.TouchMultiplier;
            ToggleSwitchEraserBindTouchMultiplier.IsOn = settings.Advanced.EraserBindTouchMultiplier;
            NibModeBoundsWidthSlider.Value = settings.Advanced.NibModeBoundsWidth;
            FingerModeBoundsWidthSlider.Value = settings.Advanced.FingerModeBoundsWidth;
            ToggleSwitchIsQuadIR.IsOn = settings.Advanced.IsQuadIR;
            ToggleSwitchIsLogEnabled.IsOn = settings.Advanced.IsLogEnabled;
            ToggleSwitchIsSaveLogByDate.IsOn = settings.Advanced.IsSaveLogByDate;
            ToggleSwitchIsVerbosePptLogEnabled.IsOn = settings.Advanced.IsVerbosePptLogEnabled;
            ToggleSwitchIsSecondConfimeWhenShutdownApp.IsOn = settings.Advanced.IsSecondConfirmWhenShutdownApp;

            CardTouchMultiplier.IsExpanded = settings.Advanced.IsSpecialScreen;
        }

        #region Special Screen & Touch Multiplier

        private void ToggleSwitchIsSpecialScreen_OnToggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.IsSpecialScreen = ToggleSwitchIsSpecialScreen.IsOn;
            CardTouchMultiplier.IsExpanded = ToggleSwitchIsSpecialScreen.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchDisableHardwareAcceleration_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Canvas.UseHardwareAcceleration = !ToggleSwitchDisableHardwareAcceleration.IsOn;
            SettingsActionHub.OnHardwareAccelerationChanged();
            SettingsManager.SaveSettingsToFile();
        }

        private void TouchMultiplierSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(TouchMultiplierSlider, TouchMultiplierText, "{0:F2}");
            if (!_isLoaded) return;
            var val = Math.Round(TouchMultiplierSlider.Value, 2);
            TouchMultiplierSlider.Value = val;
            SettingsManager.Settings.Advanced.TouchMultiplier = val;
            SettingsManager.SaveSettingsToFile();
        }

        private void BorderCalculateMultiplier_TouchDown(object sender, TouchEventArgs e)
        {
            var args = e.GetTouchPoint(null).Bounds;
            double value;
            if (!SettingsManager.Settings.Advanced.IsQuadIR) value = args.Width;
            else value = Math.Sqrt(args.Width * args.Height);

            TextBlockShowRawValue.Text = value.ToString();
            TextBlockShowCalculatedMultiplier.Text = (5 / (value * 1.1)).ToString();
        }

        private void ComboAdminLaunch_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (ComboAdminLaunch == null || ComboAdminLaunch.SelectedIndex < 0) return;
            SettingsManager.Settings.Advanced.AdminLaunchAction = ComboAdminLaunch.SelectedIndex;
            SettingsManager.SaveSettingsToFile();
        }

        /// <summary>上一次检查找到的快捷方式路径；null 表示没有或还没查过。</summary>
        private string _adminShortcutFound;

        /// <summary>
        /// 主动检查快捷方式上有没有「以管理员身份运行」标记。
        ///
        /// 为什么放在设置里而不是启动时：全盘扫开始菜单可能有几百个 .lnk，
        /// 而 app.manifest 是 asInvoker，绝大多数机器上根本扫不出东西。
        /// 为了一次大概率空跑的扫描去拖慢每次启动，不划算。
        ///
        /// 这个标记为什么重要：ICC 被提权启动后，Windows 的 UIPI 会切断它和
        /// 普通权限 PowerPoint 之间的 COM —— PPT 联动整体失效，
        /// 而且界面上没有任何提示（2026-11-18 班测）。
        /// </summary>
        private void BtnCheckShortcutAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (BtnCheckShortcutAdmin != null) BtnCheckShortcutAdmin.IsEnabled = false;
            if (BtnFixShortcutAdmin != null) BtnFixShortcutAdmin.Visibility = Visibility.Collapsed;

            try
            {
                if (TextBlockShortcutStatus != null)
                    TextBlockShortcutStatus.Text = "正在检查…";

                var sw = System.Diagnostics.Stopwatch.StartNew();
                _adminShortcutFound = Ink_Canvas.Helpers.ShortcutAdminFlag.FindRunAsAdminShortcutForSelf();
                sw.Stop();

                if (!string.IsNullOrEmpty(_adminShortcutFound))
                {
                    if (TextBlockShortcutStatus != null)
                        TextBlockShortcutStatus.Text = "找到：" + System.IO.Path.GetFileName(_adminShortcutFound)
                                                       + "（勾了管理员运行）";
                    if (BtnFixShortcutAdmin != null) BtnFixShortcutAdmin.Visibility = Visibility.Visible;

                    LogHelper.WriteLogToFile(
                        $"设置页检查：找到勾了管理员运行的本程序快捷方式 {_adminShortcutFound}（耗时 {sw.ElapsedMilliseconds}ms）",
                        LogHelper.LogType.Event);
                }
                else
                {
                    if (TextBlockShortcutStatus != null)
                        TextBlockShortcutStatus.Text = "没有发现勾了管理员运行的快捷方式";

                    LogHelper.WriteLogToFile(
                        $"设置页检查：未找到勾了管理员运行的本程序快捷方式（耗时 {sw.ElapsedMilliseconds}ms）",
                        LogHelper.LogType.Trace);

                    // 没找到要说清楚「这不代表你没被提权」——
                    // 提权也可能来自任务计划、AppCompatFlags 注册表项等其他途径。
                    MessageBox.Show(
                        "没有在桌面和开始菜单里找到「勾了以管理员身份运行」的本程序快捷方式。\n\n" +
                        "注意：程序当前" + (AppRestartHelper.IsRunningAsAdmin() ? "确实以管理员权限运行" : "以普通权限运行") + "。\n" +
                        (AppRestartHelper.IsRunningAsAdmin()
                            ? "如果仍然是管理员权限，说明提权来自其他途径（例如任务计划、\n或兼容性疑难解答自动加的注册表项），需要手动排查。"
                            : "当前权限正常，PPT 联动不会被权限阻断。"),
                        "检查完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"设置页检查快捷方式异常：{ex}", LogHelper.LogType.Error);
                if (TextBlockShortcutStatus != null) TextBlockShortcutStatus.Text = "检查失败";
                MessageBox.Show("检查时出错：" + ex.Message,
                    "检查失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (BtnCheckShortcutAdmin != null) BtnCheckShortcutAdmin.IsEnabled = true;
            }
        }

        /// <summary>
        /// 清掉找到的那个快捷方式上的管理员标记。
        /// </summary>
        private void BtnFixShortcutAdmin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_adminShortcutFound))
                {
                    MessageBox.Show("还没有检查过，或者没有找到需要清理的快捷方式。请先点「检查」。",
                        "无需清理", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                bool ok = Ink_Canvas.Helpers.ShortcutAdminFlag.ClearRunAsAdmin(_adminShortcutFound);

                if (ok)
                {
                    if (TextBlockShortcutStatus != null)
                        TextBlockShortcutStatus.Text = "已清理";
                    if (BtnFixShortcutAdmin != null) BtnFixShortcutAdmin.Visibility = Visibility.Collapsed;

                    MessageBox.Show(
                        "已清掉快捷方式上的「以管理员身份运行」标记：\n" + _adminShortcutFound + "\n\n" +
                        "下次从这个图标启动时就是普通权限，PPT 联动会恢复正常。\n" +
                        "建议现在以普通权限重启一次。",
                        "修复成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    _adminShortcutFound = null;
                }
                else
                {
                    MessageBox.Show(
                        "没能修改这个快捷方式：\n" + _adminShortcutFound + "\n\n" +
                        "可能是文件被占用或没有写权限。请手动处理：\n" +
                        "右键该图标 → 属性 → 兼容性 → 取消勾选「以管理员身份运行」。",
                        "修复失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"设置页清理快捷方式异常：{ex}", LogHelper.LogType.Error);
                MessageBox.Show("清理时出错：" + ex.Message,
                    "修复失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion

        #region Eraser & Bounds Width

        private void ToggleSwitchEraserBindTouchMultiplier_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.EraserBindTouchMultiplier = ToggleSwitchEraserBindTouchMultiplier.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void NibModeBoundsWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(NibModeBoundsWidthSlider, NibModeBoundsWidthText, "{0:0}");
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.NibModeBoundsWidth = (int)e.NewValue;
            SettingsActionHub.OnNibModeBoundsWidthChanged();
            SettingsManager.SaveSettingsToFile();
        }

        private void FingerModeBoundsWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(FingerModeBoundsWidthSlider, FingerModeBoundsWidthText, "{0:0}");
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.FingerModeBoundsWidth = (int)e.NewValue;
            SettingsActionHub.OnFingerModeBoundsWidthChanged();
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchIsQuadIR_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.IsQuadIR = ToggleSwitchIsQuadIR.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion

        #region Auto Calibrate

        private void BtnStartCalibrate_Click(object sender, RoutedEventArgs e)
        {
            _calibrateStep = 1;
            _nibTouchWidth = 0;
            _fingerTouchWidth = 0;
            _palmTouchWidth = 0;

            BtnStartCalibrate.IsEnabled = false;
            BorderCalibrate.IsEnabled = true;
            TextCalibrateHint.Text = AdvancedStrings.Adv_CalibrateTapPen;
            TextNibCalibrated.Text = AdvancedStrings.Adv_NibValueWaiting;
            TextFingerCalibrated.Text = AdvancedStrings.Adv_FingerValueUncalibrated;
            TextPalmCalibrated.Text = AdvancedStrings.Adv_PalmValueUncalibrated;
            TextCalibrateResult.Text = "";
        }

        private void BorderCalibrate_TouchDown(object sender, TouchEventArgs e)
        {
            var args = e.GetTouchPoint(null).Bounds;
            double touchWidth;
            if (!SettingsManager.Settings.Advanced.IsQuadIR)
                touchWidth = args.Width;
            else
                touchWidth = Math.Sqrt(args.Width * args.Height);

            switch (_calibrateStep)
            {
                case 1:
                    _nibTouchWidth = touchWidth;
                    TextNibCalibrated.Text = string.Format(AdvancedStrings.Adv_NibValueFormat, touchWidth);
                    TextCalibrateHint.Text = AdvancedStrings.Adv_CalibrateTapFinger;
                    _calibrateStep = 2;
                    break;

                case 2:
                    _fingerTouchWidth = touchWidth;
                    TextFingerCalibrated.Text = string.Format(AdvancedStrings.Adv_FingerValueFormat, touchWidth);
                    TextCalibrateHint.Text = AdvancedStrings.Adv_CalibrateTapPalm;
                    _calibrateStep = 3;
                    break;

                case 3:
                    _palmTouchWidth = touchWidth;
                    TextPalmCalibrated.Text = string.Format(AdvancedStrings.Adv_PalmValueFormat, touchWidth);
                    ApplyCalibratedSettings();
                    _calibrateStep = 0;
                    BtnStartCalibrate.IsEnabled = true;
                    BorderCalibrate.IsEnabled = false;
                    TextCalibrateHint.Text = AdvancedStrings.Adv_CalibrateDone;
                    break;
            }

            e.Handled = true;
        }

        private void ApplyCalibratedSettings()
        {
            if (!_isLoaded) return;

            // 计算笔尖模式阈值：笔尖值 × 1.5，在 1-50 之间
            double nibThreshold = _nibTouchWidth * 1.5;
            nibThreshold = Math.Max(1, Math.Min(50, nibThreshold));
            SettingsManager.Settings.Advanced.NibModeBoundsWidth = (int)Math.Round(nibThreshold);
            NibModeBoundsWidthSlider.Value = SettingsManager.Settings.Advanced.NibModeBoundsWidth;

            // 计算手指模式阈值：手指值 × 1.5，在 1-50 之间
            double fingerThreshold = _fingerTouchWidth * 1.5;
            fingerThreshold = Math.Max(1, Math.Min(50, fingerThreshold));
            SettingsManager.Settings.Advanced.FingerModeBoundsWidth = (int)Math.Round(fingerThreshold);
            FingerModeBoundsWidthSlider.Value = SettingsManager.Settings.Advanced.FingerModeBoundsWidth;

            // 计算触摸倍率：使用手指值，和原来的公式一致
            double touchMultiplier = 5 / (_fingerTouchWidth * 1.1);
            touchMultiplier = Math.Max(0, Math.Min(2, touchMultiplier));
            SettingsManager.Settings.Advanced.TouchMultiplier = Math.Round(touchMultiplier, 2);
            TouchMultiplierSlider.Value = SettingsManager.Settings.Advanced.TouchMultiplier;

            UpdateAllSliderTexts();
            SettingsActionHub.OnNibModeBoundsWidthChanged();
            SettingsActionHub.OnFingerModeBoundsWidthChanged();
            SettingsManager.SaveSettingsToFile();

            TextCalibrateResult.Text = $"校准成功！笔尖阈值={(int)nibThreshold}，手指阈值={(int)fingerThreshold}，触摸倍率={touchMultiplier:F2}";
        }

        #endregion

        #region Logging & Exit

        private void ToggleSwitchIsLogEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.IsLogEnabled = ToggleSwitchIsLogEnabled.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchIsSaveLogByDate_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.IsSaveLogByDate = ToggleSwitchIsSaveLogByDate.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        /// <summary>
        /// PPT 联动详细日志开关。
        ///
        /// LTSC（2026-11-19）：这个设置在代码里存在很久了（Settings.Advanced.IsVerbosePptLogEnabled），
        /// 但界面从来没有入口，默认又是 false。PptBridge.VerboseLog 的前置条件就是它，
        /// 于是班测反馈「PPT 翻页栏消失」时，日志里一条 PPT 取数细节都没有，
        /// 只能靠读源码倒推。
        ///
        /// 现在放到 高级 → 日志 分组里。不开的时候日志不膨胀，
        /// 出问题时用户自己能打开，下次的日志就有内容可查。
        /// </summary>
        private void ToggleSwitchIsVerbosePptLogEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.IsVerbosePptLogEnabled = ToggleSwitchIsVerbosePptLogEnabled.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchIsSecondConfimeWhenShutdownApp_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.IsSecondConfirmWhenShutdownApp = ToggleSwitchIsSecondConfimeWhenShutdownApp.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion

        #region Config Profiles

        private void RefreshConfigProfileList()
        {
            try
            {
                if (ComboBoxConfigProfile == null) return;
                _isRefreshingConfigProfileList = true;
                try
                {
                    var names = ConfigProfileManager.ListProfileNames();
                    ComboBoxConfigProfile.ItemsSource = names;
                    if (names.Count == 0)
                    {
                        ComboBoxConfigProfile.SelectedItem = null;
                    }
                    else if (_lastAppliedProfileName != null && names.Contains(_lastAppliedProfileName))
                    {
                        ComboBoxConfigProfile.SelectedItem = _lastAppliedProfileName;
                    }
                    else
                    {
                        var selected = ComboBoxConfigProfile.SelectedItem as string;
                        if (selected != null && names.Contains(selected))
                            ComboBoxConfigProfile.SelectedItem = selected;
                        else
                            ComboBoxConfigProfile.SelectedIndex = 0;
                    }
                    if (BtnDeleteConfigProfile != null)
                        BtnDeleteConfigProfile.IsEnabled = ComboBoxConfigProfile.SelectedItem != null;
                }
                finally
                {
                    _isRefreshingConfigProfileList = false;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"刷新配置方案列表失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private void ComboBoxConfigProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BtnDeleteConfigProfile != null)
                BtnDeleteConfigProfile.IsEnabled = ComboBoxConfigProfile?.SelectedItem != null;
            if (!_isLoaded || _isRefreshingConfigProfileList) return;
            var name = ComboBoxConfigProfile?.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                if (ConfigProfileManager.ApplyProfile(name))
                {
                    _lastAppliedProfileName = name;
                    var mw = GetMainWindow();
                    if (mw != null)
                    {
                        mw.ReloadSettingsFromFile();
                        mw.ShowNotification(string.Format(ConfigStrings.SwitchedToProfile, name));
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"切换配置方案失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private async void BtnSaveAsConfigProfile_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var input = new System.Windows.Controls.TextBox
            {
                MinWidth = 260,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var label = new System.Windows.Controls.TextBlock
            {
                Text = ConfigStrings.ProfileNameLabel,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var content = new iNKORE.UI.WPF.Controls.SimpleStackPanel { Spacing = 6 };
            content.Children.Add(label);
            content.Children.Add(input);
            var dialog = new ContentDialog
            {
                Title = ConfigStrings.SaveAsProfileTitle,
                Content = content,
                PrimaryButtonText = FloatingBarStrings.Tools_Save,
                SecondaryButtonText = CommonStrings.Common_Cancel,
                Owner = Window.GetWindow(this) ?? GetMainWindow()
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
            var name = input.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(ConfigStrings.SaveAs_EnterName, ConfigStrings.SaveAsProfileTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(SettingsManager.Settings, Newtonsoft.Json.Formatting.Indented);
                if (ConfigProfileManager.SaveAsProfile(name, json))
                {
                    _lastAppliedProfileName = name;
                    RefreshConfigProfileList();
                    var mw = GetMainWindow();
                    if (mw != null) mw.ShowNotification(string.Format(ConfigStrings.SavedAsProfile, name));
                }
                else
                    MessageBox.Show(ConfigStrings.SaveAs_Failed, ConfigStrings.SaveAsProfileTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"另存为方案失败: {ex.Message}", LogHelper.LogType.Error);
                MessageBox.Show(string.Format(ConfigStrings.SaveAs_FailedMsg, ex.Message), ConfigStrings.SaveAsProfileTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteConfigProfile_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var name = ComboBoxConfigProfile?.SelectedItem as string;
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(ConfigStrings.Delete_SelectFirst, ConfigStrings.SaveAsProfileTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                if (MessageBox.Show(string.Format(ConfigStrings.Delete_ConfirmMsg, name), ConfigStrings.Delete_ConfirmTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
                if (ConfigProfileManager.DeleteProfile(name))
                {
                    RefreshConfigProfileList();
                    var nextName = ComboBoxConfigProfile?.SelectedItem as string;
                    var mw = GetMainWindow();
                    if (!string.IsNullOrEmpty(nextName) && ConfigProfileManager.ApplyProfile(nextName))
                    {
                        _lastAppliedProfileName = nextName;
                        if (mw != null)
                        {
                            mw.ReloadSettingsFromFile();
                            mw.ShowNotification(string.Format(ConfigStrings.DeletedAndSwitched, name, nextName));
                        }
                    }
                    else
                    {
                        if (mw != null) mw.ShowNotification(string.Format(ConfigStrings.DeletedProfile, name));
                    }
                }
                else
                    MessageBox.Show(ConfigStrings.Delete_Failed, ConfigStrings.SaveAsProfileTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"删除配置文件失败: {ex.Message}", LogHelper.LogType.Error);
                MessageBox.Show(string.Format(ConfigStrings.Delete_FailedMsg, ex.Message), ConfigStrings.SaveAsProfileTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

private void LoadExperimentalSettings()
        {
            _isLoaded = false;

            try
            {
                var settings = SettingsManager.Settings;
                if (settings.Advanced != null)
                {
                    CardFullScreenHelper.IsOn = settings.Advanced.IsEnableFullScreenHelper;
                    CardEdgeGestureUtil.IsOn = settings.Advanced.IsEnableEdgeGestureUtil;
                    CardForceFullScreen.IsOn = settings.Advanced.IsEnableForceFullScreen;
                    CardDPIChangeDetection.IsOn = settings.Advanced.IsEnableDPIChangeDetection;
                    CardResolutionChangeDetection.IsOn = settings.Advanced.IsEnableResolutionChangeDetection;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载实验性选项时出错: {ex.Message}");
            }

            _isLoaded = true;
        }

        private void ToggleSwitchFullScreenHelper_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            try
            {
                SettingsManager.Settings.Advanced.IsEnableFullScreenHelper = CardFullScreenHelper.IsOn;
                SettingsManager.SaveSettingsToFile();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置全屏助手时出错: {ex.Message}");
            }
        }

        private void ToggleSwitchEdgeGestureUtil_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            try
            {
                SettingsManager.Settings.Advanced.IsEnableEdgeGestureUtil = CardEdgeGestureUtil.IsOn;
                SettingsManager.SaveSettingsToFile();

                if (OSVersion.GetOperatingSystem() >= OSVersionExtension.OperatingSystem.Windows10)
                {
                    var window = Application.Current.MainWindow;
                    if (window != null)
                    {
                        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                        EdgeGestureUtil.DisableEdgeGestures(handle, CardEdgeGestureUtil.IsOn);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置边缘手势时出错: {ex.Message}");
            }
        }

        private void ToggleSwitchForceFullScreen_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            try
            {
                SettingsManager.Settings.Advanced.IsEnableForceFullScreen = CardForceFullScreen.IsOn;
                SettingsManager.SaveSettingsToFile();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置强制全屏时出错: {ex.Message}");
            }
        }

        private void ToggleSwitchDPIChangeDetection_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            try
            {
                SettingsManager.Settings.Advanced.IsEnableDPIChangeDetection = CardDPIChangeDetection.IsOn;
                SettingsManager.SaveSettingsToFile();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置DPI变化检测时出错: {ex.Message}");
            }
        }

        private void ToggleSwitchResolutionChangeDetection_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            try
            {
                SettingsManager.Settings.Advanced.IsEnableResolutionChangeDetection = CardResolutionChangeDetection.IsOn;
                SettingsManager.SaveSettingsToFile();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置分辨率变化检测时出错: {ex.Message}");
            }
        }

private void ToggleSwitchDebugConsole_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            bool isOn = ToggleSwitchDebugConsole.IsOn;
            SettingsManager.Settings.Advanced.IsDebugConsoleEnabled = isOn;
            SettingsManager.SaveSettingsToFile();

            if (isOn) DebugConsoleManager.Show();
            else DebugConsoleManager.Hide();
        }

        private void ToggleSwitchPPTComDebugProbe_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.IsPPTComDebugProbeEnabled = ToggleSwitchPPTComDebugProbe.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchPPTPageFlipPreview_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.IsPPTPageFlipPreviewVisible = ToggleSwitchPPTPageFlipPreview.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void BtnTestCrash_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "确定要触发一次模拟崩溃吗？\n\n应用将立即退出并尝试自动重启。",
                "崩溃测试确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            LogHelper.WriteLogToFile("[Debug] 用户手动触发模拟崩溃", LogHelper.LogType.Warning);
            throw new System.InvalidOperationException("[Debug] 用户手动触发的模拟崩溃异常");
        }

        private void BtnTestHeartbeatTimeout_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "确定要停止心跳计时器吗？\n\n约10秒后守护检查将检测到心跳超时并触发重启。",
                "心跳超时测试确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            App.DebugStopHeartbeat();
        }

        private void BtnResetStartupCount_Click(object sender, RoutedEventArgs e)
        {
            StartupCount.Reset();
            LogHelper.WriteLogToFile("[Debug] 熔断计数器已手动重置", LogHelper.LogType.Warning);
            MessageBox.Show("熔断计数器已重置为 0。", "重置完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}

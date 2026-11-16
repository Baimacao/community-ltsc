using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ContentDialog = iNKORE.UI.WPF.Modern.Controls.ContentDialog;
using ContentDialogButton = iNKORE.UI.WPF.Modern.Controls.ContentDialogButton;
using ContentDialogResult = iNKORE.UI.WPF.Modern.Controls.ContentDialogResult;

namespace Ink_Canvas.Windows.SettingsViews.Helpers
{
    public static class SettingsActionHub
    {
        private static MainWindow GetMainWindow() => Application.Current.MainWindow as MainWindow;

        #region Canvas

        public static void OnIsShowCursorChanged(bool value)
        {
            var mw = GetMainWindow();
            if (mw != null && mw.inkCanvas != null)
            {
                mw.inkCanvas.ForceCursor = value;
                mw.SetCursorBasedOnEditingMode(mw.inkCanvas);
            }
        }

        public static void OnPenCursorTypeChanged(int selectedIndex)
        {
            var mw = GetMainWindow();
            if (mw != null && mw.inkCanvas != null)
                mw.SetCursorBasedOnEditingMode(mw.inkCanvas);
        }

        public static void OnCustomPenCursorPathChanged()
        {
            var mw = GetMainWindow();
            if (mw != null && mw.inkCanvas != null)
            {
                MainWindow.ClearCustomCursorCache();
                mw.SetCursorBasedOnEditingMode(mw.inkCanvas);
            }
        }

        public static void OnEnablePressureTouchModeChanged(bool value)
        {
            if (value && SettingsManager.Settings.Canvas.DisablePressure)
            {
                SettingsManager.Settings.Canvas.DisablePressure = false;
                OnDisablePressureChanged(false);
            }
        }

        public static void OnDisablePressureChanged(bool value)
        {
            if (value && SettingsManager.Settings.Canvas.EnablePressureTouchMode)
            {
                SettingsManager.Settings.Canvas.EnablePressureTouchMode = false;
            }
            var mw = GetMainWindow();
            if (mw != null && mw.inkCanvas != null)
                mw.inkCanvas.DefaultDrawingAttributes.IgnorePressure = value;
        }

        public static void OnEraserSizeChanged(int selectedIndex)
        {
            var mw = GetMainWindow();
            if (mw != null)
            {
                if (mw.ComboBoxEraserSizeFloatingBar != null)
                    mw.ComboBoxEraserSizeFloatingBar.SelectedIndex = selectedIndex;
                if (mw.BoardComboBoxEraserSize != null)
                    mw.BoardComboBoxEraserSize.SelectedIndex = selectedIndex;
            }
        }

        public static void OnCurveSmoothingModeChanged(bool fitToCurve, bool useAdvancedBezier)
        {
            var mw = GetMainWindow();
            if (mw != null && mw.inkCanvas != null)
            {
                if (useAdvancedBezier)
                    mw.inkCanvas.DefaultDrawingAttributes.FitToCurve = false;
                else
                    mw.inkCanvas.DefaultDrawingAttributes.FitToCurve = fitToCurve;
            }
        }

        #endregion

        #region Appearance

        public static void OnThemeChanged(int themeIndex)
        {
            var mw = GetMainWindow();
            if (mw != null) mw.ApplyTheme(themeIndex);
        }

        /// <summary>
        /// 界面语言变更。保存设置 + 提示重启。
        ///
        /// 为什么不做热切换：WPF 的 XAML 里文案是 {x:Static props:XXXStrings.Key}，
        /// 那是编译期就固化成常量的，运行时把 Culture 换掉它也不会变。
        /// 曾经试过 new MainWindow + Close 旧窗口来重建界面，但设置窗口和 OOBE
        /// 都挂在主窗口下（Owner），父窗口一关它们跟着消失，用户在设置页点一下
        /// 语言，窗口直接没了 —— 比不热切换更糟。
        ///
        /// 要真做热切换，得把 74 处 {x:Static} 全换成运行时绑定（DynamicResource
        /// 或 I18nExtension），再把每个页面加一遍刷新逻辑。代价大、改动面广，
        /// 而这个场景（换语言）一次安装也就发生一两次，重启成本远低于维护成本。
        /// 所以这里走重启路线，并明确告诉用户。
        /// </summary>
        public static async void OnLanguageChanged(string language)
        {
            LocalizationHelper.TrySetCulture(language);

            // 已经在弹重启提示了就不叠加。用户在设置页来回切几次语言时会发生。
            if (_languageRestartPrompting) return;

            // OOBE 里也立刻提示重启。
            //
            // 一开始我让 OOBE 里只存设置、等走完引导再提示，理由是"模态窗口中途弹框
            // 会把流程卡住"。但这个理由站不住：OOBE 第一屏就是选语言的地方，而后面
            // 每一页仍是旧语言 —— 老师选了繁體，接下来看到的还是简体，等于白选。
            // 与其让他硬着头皮走完一屏看不懂的引导，不如当场问一句要不要重启。
            //
            // 走重启的话，重启后 HasShownOobe 是 false，会重新弹一遍 OOBE，
            // 这次每一页都是新语言。选择"稍后"则继续走完当前这遍（旧语言），
            // 走完还会再问一次。
            if (App.IsOobeShowing)
            {
                // OOBE 里换语言意味着"重新引导"这件事要发生一次，
                // 所以先把完成标记清掉，重启后才会再弹。
                ResetOobeCompletionForLanguageChange();

                if (!TrySaveSettingsForLanguageChange()) return;

                await PromptRestartForLanguageChangeAsync();
                return;
            }

            await PromptRestartForLanguageChangeAsync();
        }

        /// <summary>
        /// 重启提示。用 ContentDialog 而不是系统 MessageBox —— 后者是 Win32 老样式，
        /// 在这个全 Fluent 界面里出现一次就像从别的程序里弹出来的。
        /// </summary>
        private static async Task PromptRestartForLanguageChangeAsync()
        {
            _languageRestartPrompting = true;
            try
            {
                var ownerWindow = ResolveActiveWindow();
                var dialog = new ContentDialog
                {
                    Title = ThemeStrings.Theme_Language_Restart_Title,
                    Content = new TextBlock
                    {
                        Text = ThemeStrings.Theme_Language_Restart_Body,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 420
                    },
                    PrimaryButtonText = ThemeStrings.Theme_Language_Restart_Now,
                    CloseButtonText = ThemeStrings.Theme_Language_Restart_Later,
                    DefaultButton = ContentDialogButton.Primary
                };

                // Owner 必须是当前活跃窗口：在设置页里就挂设置页，
                // 否则对话框可能出现在主窗口后面，用户以为没反应。
                if (ownerWindow != null) dialog.Owner = ownerWindow;

                var result = await dialog.ShowAsync();

                if (result != ContentDialogResult.Primary)
                {
                    // 用户选了稍后。必须复位标记 —— OOBE 里就是这个情况：
                    // 第一屏选了语言、点了稍后，走完引导后还要再问一次。
                    // 不复位的话第二次直接 return，他永远等不到那个提示。
                    _languageRestartPrompting = false;
                    return;
                }

                if (!TrySaveSettingsForLanguageChange())
                {
                    _languageRestartPrompting = false;
                    await ShowSaveFailedAsync();
                    return;
                }

                // 走到这里进程会被重启掉，标记不用复位。
                AppRestartHelper.RestartWithCurrentPrivileges();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"语言切换重启提示失败: {ex.Message}", LogHelper.LogType.Error);
                _languageRestartPrompting = false;
            }
        }

        /// <summary>
        /// 找出该挂哪个窗口。设置窗口、OOBE 都可能在最前面，
        /// 直接挂主窗口会让对话框藏到它们后面去。
        /// </summary>
        private static Window ResolveActiveWindow()
        {
            var mw = GetMainWindow();

            if (mw != null)
            {
                foreach (Window owned in mw.OwnedWindows)
                {
                    if (owned.IsActive) return owned;
                }
            }

            if (mw != null && mw.IsActive) return mw;

            foreach (Window w in Application.Current.Windows)
            {
                if (w.IsActive && w.IsVisible) return w;
            }

            return mw as Window;
        }

        /// <summary>
        /// 保存失败时的提示。这条不能省：设置没存下来的话重启等于白重启，
        /// 用户回来还是旧语言，会以为功能坏了。
        /// </summary>
        private static async Task ShowSaveFailedAsync()
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = ThemeStrings.Theme_Language_Restart_Title,
                    Content = new TextBlock
                    {
                        Text = "语言已切换，但设置没能保存。重启后会回到原来的语言。\n\n这次不重启了，请检查设置目录的写入权限。",
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 420
                    },
                    CloseButtonText = CommonStrings.Common_OK,
                    DefaultButton = ContentDialogButton.Close
                };

                var ownerWindow = ResolveActiveWindow();
                if (ownerWindow != null) dialog.Owner = ownerWindow;

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"显示保存失败提示时出错: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        /// <summary>
        /// OOBE 走完后调用：引导过程中如果改过语言，这里补一次重启提示。
        ///
        /// 为什么不在 OOBE 里提示：那是模态窗口，中途弹框会把引导卡在半路。
        /// 引导结束后提示时机正好 —— 用户刚点完「完成」，看到的就是旧语言界面，
        /// 这时候告诉他重启才生效，他能对上号。
        /// </summary>
        /// <summary>
        /// OOBE 走完后调用。只在"引导里改过语言、但当时选了稍后重启"的情况下补问一次。
        ///
        /// 正常情况下 OOBE 里一改语言就当场问过了，用户点重启就直接重启了，
        /// 走不到这里。只有他点了"稍后"才会进到引导结束，这时候再给他一次机会 ——
        /// 否则他刚看完一屏旧语言的引导，就这么进主界面了。
        /// </summary>
        public static async Task PromptRestartIfLanguageChangedDuringOobeAsync(string languageBeforeOobe)
        {
            var current = SettingsManager.Settings?.Appearance?.Language;
            if (string.Equals(current ?? string.Empty, languageBeforeOobe ?? string.Empty, StringComparison.Ordinal))
                return;

            // 已经在重启流程里了（用户点了重启），不用再问。
            if (_languageRestartPrompting) return;

            if (!TrySaveSettingsForLanguageChange()) return;

            await PromptRestartForLanguageChangeAsync();
        }

        /// <summary>
        /// 清掉「已看过引导」标记，让下次启动重新弹 OOBE。
        /// OOBE 结束时 OnOobeCompleted 会把它设回 true 并落盘。
        /// </summary>
        private static void ResetOobeCompletionForLanguageChange()
        {
            try
            {
                // 这个标志拦住 OnOobeCompleted 把标记写回 true。
                // 只在 OOBE 还开着的时候有意义；OOBE 一关，MainWindow 里那块代码就会读它。
                MainWindow._oobeInterruptedForLanguageChange = true;

                if (SettingsManager.Settings?.Startup == null) return;
                SettingsManager.Settings.Startup.HasShownOobe = false;
                LogHelper.WriteLogToFile(
                    "OOBE 里换过语言，已重置引导完成标记，重启后会重新走一遍引导",
                    LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"重置引导完成标记失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        /// <summary>
        /// 语言选择必须落盘，否则重启回来还是旧语言，用户会以为白重启了。
        /// 返回 false 表示保存失败，调用方不能继续重启。
        /// </summary>
        private static bool TrySaveSettingsForLanguageChange()
        {
            try
            {
                Helpers.SettingsManager.SaveSettingsToFile();
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"切换语言后保存设置失败: {ex.Message}", LogHelper.LogType.Error);
                return false;
            }
        }

        /// <summary>
        /// 防止重启提示叠加。重启成功后进程会退出；重启失败时 AppRestartHelper
        /// 会保留原进程，所以这里在失败路径上要复位。
        /// </summary>
        private static bool _languageRestartPrompting;

        public static void OnFloatingBarImgChanged()
        {
            var mw = GetMainWindow();
            if (mw != null) mw.UpdateFloatingBarIcon();
        }

        public static void OnBlackBoardScaleChanged(double value)
        {
            var mw = GetMainWindow();
            if (mw != null)
            {
                mw.ViewboxBlackboardCenterSideScaleTransform.ScaleX = value;
                mw.ViewboxBlackboardCenterSideScaleTransform.ScaleY = value;
            }
        }

        public static void OnBlackBoardLeftScaleChanged(double value)
        {
            var mw = GetMainWindow();
            if (mw != null)
            {
                mw.ViewboxBlackboardLeftSideScaleTransform.ScaleX = value;
                mw.ViewboxBlackboardLeftSideScaleTransform.ScaleY = value;
            }
        }

        public static void OnBlackBoardRightScaleChanged(double value)
        {
            var mw = GetMainWindow();
            if (mw != null)
            {
                mw.ViewboxBlackboardRightSideScaleTransform.ScaleX = value;
                mw.ViewboxBlackboardRightSideScaleTransform.ScaleY = value;
            }
        }

        public static void OnBoardToolbarLeftOpacityChanged(double value)
        {
            var mw = GetMainWindow();
            if (mw != null) mw.ViewboxBlackboardLeftSide.Opacity = value;
        }

        public static void OnBoardToolbarCenterOpacityChanged(double value)
        {
            var mw = GetMainWindow();
            if (mw != null) mw.ViewboxBlackboardCenterSide.Opacity = value;
        }

        public static void OnBoardToolbarRightOpacityChanged(double value)
        {
            var mw = GetMainWindow();
            if (mw != null) mw.ViewboxBlackboardRightSide.Opacity = value;
        }

        public static void OnFloatingBarMenuOpacityChanged(double value)
        {
            var mw = GetMainWindow();
            if (mw != null) mw.ApplyFloatingBarMenuOpacity();
        }

        public static void OnFloatingBarMenuOpacityInPPTChanged(double value)
        {
            var mw = GetMainWindow();
            if (mw != null && mw.IsInPPTPresentationMode)
                mw.ApplyFloatingBarMenuOpacity();
        }

        public static void OnBoardMenuOpacityChanged(double value)
        {
            var mw = GetMainWindow();
            if (mw != null) mw.ApplyBoardMenuOpacity();
        }

        public static void OnTimeDisplayInWhiteboardChanged(bool isOn)
        {
            var mw = GetMainWindow();
            if (mw != null && mw.currentMode == 1)
            {
                var vis = isOn ? Visibility.Visible : Visibility.Collapsed;
                mw.WaterMarkTime.Visibility = vis;
                mw.WaterMarkDate.Visibility = vis;
            }
        }

        public static void OnChickenSoupInWhiteboardChanged(bool isOn, bool isTimeDisplayOn)
        {
            var mw = GetMainWindow();
            if (mw != null && mw.currentMode == 1 && isTimeDisplayOn)
            {
                mw.BlackBoardWaterMark.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
            }

            // Start/stop auto-rotation based on master toggle
            if (mw != null)
            {
                if (isOn)
                {
                    mw.StartChickenSoupAutoRotation();
                }
                else
                {
                    mw.StopChickenSoupAutoRotation();
                }
            }
        }

        public static void OnChickenSoupSchemesChanged()
        {
            var mw = GetMainWindow();
            if (mw != null) mw.UpdateChickenSoupTextAsync().ConfigureAwait(false);
        }

        public static void OnChickenSoupAutoRotationChanged()
        {
            var mw = GetMainWindow();
            if (mw != null)
            {
                mw.RestartChickenSoupAutoRotation();
            }
        }

        public static void OnChickenSoupSourceChanged()
        {
            OnChickenSoupSchemesChanged();
        }

        public static void OnChickenSoupPositionChanged()
        {
            var mw = GetMainWindow();
            if (mw != null) mw.ApplyChickenSoupPosition();
        }

        public static void OnQuickPanelBottomOffsetChanged(double value)
        {
            var mw = GetMainWindow();
            if (mw != null) mw.ApplyQuickPanelBottomOffset(value);
        }

        public static void OnQuickPanelOpacityChanged(double value)
        {
            var mw = GetMainWindow();
            if (mw != null) mw.ApplyQuickPanelOpacity(value);
        }

        public static void OnAutoCollapseQuickPanelChanged()
        {
            var mw = GetMainWindow();
            if (mw != null) mw.UpdateAutoCollapseQuickPanelTimer();
        }

        public static void OnUnFoldButtonImageTypeChanged(int selectedIndex)
        {
            var mw = GetMainWindow();
            if (mw == null) return;

            mw.LeftSidePanel?.ApplySettings();
            mw.RightSidePanel?.ApplySettings();

            if (selectedIndex == 0)
            {
                mw.RightUnFoldBtnImgChevron.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Resources/new-icons/unfold-chevron.png"));
                mw.RightUnFoldBtnImgChevron.Width = 14; mw.RightUnFoldBtnImgChevron.Height = 14;
                mw.RightUnFoldBtnImgChevron.RenderTransform = new System.Windows.Media.RotateTransform(180);
                mw.LeftUnFoldBtnImgChevron.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Resources/new-icons/unfold-chevron.png"));
                mw.LeftUnFoldBtnImgChevron.Width = 14; mw.LeftUnFoldBtnImgChevron.Height = 14;
                mw.LeftUnFoldBtnImgChevron.RenderTransform = null;
            }
            else if (selectedIndex == 1)
            {
                mw.LeftUnFoldBtnImgChevron.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Resources/new-icons/pen-white.png"));
                mw.LeftUnFoldBtnImgChevron.Width = 18; mw.LeftUnFoldBtnImgChevron.Height = 18;
                mw.LeftUnFoldBtnImgChevron.RenderTransformOrigin = new Point(0.5, 0.5);
                mw.LeftUnFoldBtnImgChevron.RenderTransform = new System.Windows.Media.ScaleTransform(-1, 1);
                mw.RightUnFoldBtnImgChevron.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Resources/new-icons/pen-white.png"));
                mw.RightUnFoldBtnImgChevron.Width = 18; mw.RightUnFoldBtnImgChevron.Height = 18;
                mw.RightUnFoldBtnImgChevron.RenderTransform = null;
            }
        }

        public static void OnFloatingBarScaleChanged(double actualScale)
        {
            var mw = GetMainWindow();
            if (mw != null)
            {
                mw._userHasDraggedFloatingBar = false;
                mw.pointDesktop = new Point(-1, -1);
                mw.pointPPT = new Point(-1, -1);
                // 紧凑模式叠加缩放因子
                double effectiveScale = actualScale;
                if (SettingsManager.Settings.Appearance.CompactFloatingBar)
                    effectiveScale = actualScale * MainWindow.CompactFloatingBarScaleFactor;
                mw.ViewboxFloatingBarScaleTransform.ScaleX = effectiveScale;
                mw.ViewboxFloatingBarScaleTransform.ScaleY = effectiveScale;
                if (mw.IsInPPTPresentationMode)
                    mw.ViewboxFloatingBarMarginAnimation(60);
                else
                    mw.ViewboxFloatingBarMarginAnimation(100, true);
            }
        }

        public static void OnFloatingBarOpacityChanged(double value)
        {
            var mw = GetMainWindow();
            if (mw != null && !Ink_Canvas.Helpers.FloatingBarIntroAnimation.IsPlaying) mw.ViewboxFloatingBar.Opacity = value;
        }

        public static void OnFloatingBarOpacityInPPTChanged(double value)
        {
            var mw = GetMainWindow();
            if (mw != null && mw.IsInPPTPresentationMode)
                if (!Ink_Canvas.Helpers.FloatingBarIntroAnimation.IsPlaying) mw.ViewboxFloatingBar.Opacity = value;
        }

        public static void OnToolbarPositionChanged(ToolbarPosition position)
        {
            var mw = GetMainWindow();
            if (mw != null) mw.UpdateToolbarPosition();
        }

        public static void OnReverseToolbarContentChanged(bool isOn)
        {
            var mw = GetMainWindow();
            if (mw != null) mw.UpdateToolbarPosition();
        }

        public static void OnCompactFloatingBarChanged(bool isOn)
        {
            var mw = GetMainWindow();
            if (mw != null) mw.ApplyCompactFloatingBarMode(isOn);
        }

        public static void OnHideFloatingBarBorderChanged(bool isOn)
        {
            var mw = GetMainWindow();
            if (mw != null) mw.ApplyHideFloatingBarBorder(isOn);
        }

        #endregion

        #region Advanced

        public static void OnHardwareAccelerationChanged()
        {
            var mw = GetMainWindow();
            if (mw != null) mw.UpdateInkSmoothingConfig();
        }

        public static void OnNibModeBoundsWidthChanged()
        {
            var mw = GetMainWindow();
            if (mw != null)
            {
                if (SettingsManager.Settings.Startup.IsEnableNibMode)
                    mw.BoundsWidth = SettingsManager.Settings.Advanced.NibModeBoundsWidth;
                else
                    mw.BoundsWidth = SettingsManager.Settings.Advanced.FingerModeBoundsWidth;
            }
        }

        public static void OnFingerModeBoundsWidthChanged()
        {
            OnNibModeBoundsWidthChanged();
        }

        #endregion

        #region Automation

        public static void OnAutoFoldChanged()
        {
            var mw = GetMainWindow();
            if (mw != null) mw.StartOrStoptimerCheckAutoFold();
        }

        public static void OnAutoKillChanged()
        {
            var mw = GetMainWindow();
            if (mw == null) return;
            var auto = SettingsManager.Settings.Automation;
            bool anyKill = auto.IsAutoKillEasiNote || auto.IsAutoKillPPTService ||
                auto.IsAutoKillHiteAnnotation || auto.IsAutoKillInkCanvas ||
                auto.IsAutoKillICA || auto.IsAutoKillIDT || auto.IsAutoKillVComYouJiao ||
                auto.IsAutoKillSeewoLauncher2DesktopAnnotation;
            mw.UpdateAutoKillProcessTimer(anyKill);
        }

        public static void OnAutoSaveStrokesChanged()
        {
            var mw = GetMainWindow();
            if (mw != null) mw.UpdateAutoSaveStrokesTimer();
        }

        public static void OnFloatingWindowInterceptorRuleChanged(string ruleKey, bool isOn)
        {
            var mw = GetMainWindow();
            if (mw != null)
            {
                if (Enum.TryParse<FloatingWindowInterceptor.InterceptType>(ruleKey, out var interceptType))
                    mw.SetInterceptRule(interceptType, isOn);
            }
        }

        public static void OnFloatingWindowInterceptorEnabledCheck(bool anyOn)
        {
            var mw = GetMainWindow();
            if (mw == null) return;
            SettingsManager.Settings.Automation.FloatingWindowInterceptor.IsEnabled = anyOn;
            if (mw._floatingWindowInterceptorManager != null)
            {
                if (anyOn)
                    mw._floatingWindowInterceptorManager.Start();
                else
                    mw._floatingWindowInterceptorManager.Stop();
            }
            SettingsManager.SaveSettingsToFile();
        }

        #endregion

        #region Startup

        public static void OnNibModeChanged(bool isOn)
        {
            var mw = GetMainWindow();
            if (mw != null)
            {
                if (isOn)
                    mw.BoundsWidth = SettingsManager.Settings.Advanced.NibModeBoundsWidth;
                else
                    mw.BoundsWidth = SettingsManager.Settings.Advanced.FingerModeBoundsWidth;
            }
        }

        #endregion

        #region PowerPoint

        public static void OnPPTSupportChanged(bool isOn)
        {
            var mw = GetMainWindow();
            if (mw == null) return;
            var ppt = SettingsManager.Settings.PowerPointSettings;
            if (!isOn && ppt.IsSupportWPS)
            {
                ppt.IsSupportWPS = false;
                if (mw.PPTManager != null) mw.PPTManager.IsSupportWPS = false;
            }
            if (isOn)
            {
                if (mw.PPTManager == null) mw.InitializePPTManagers();
                mw.StartPPTMonitoring();
            }
            else
            {
                mw.StopPPTMonitoring();
            }
        }

        public static void OnPPTEnhancementChanged(bool isOn)
        {
            var mw = GetMainWindow();
            if (mw == null) return;
            var ppt = SettingsManager.Settings.PowerPointSettings;
            if (isOn)
            {
                ppt.IsSupportWPS = false;
                if (mw.PPTManager != null) mw.PPTManager.IsSupportWPS = false;
                mw.StartPowerPointProcessMonitoring();
            }
            else
            {
                mw.StopPowerPointProcessMonitoring();
            }
        }

        public static void OnSkipAnimationsWhenGoNextChanged(bool isOn)
        {
            var mw = GetMainWindow();
            if (mw?.PPTManager != null)
                mw.PPTManager.SkipAnimationsWhenNavigating = isOn;
        }

        public static void OnSupportWPSChanged()
        {
            var mw = GetMainWindow();
            if (mw == null) return;
            var ppt = SettingsManager.Settings.PowerPointSettings;
            if (ppt.IsSupportWPS)
            {
                if (!ppt.PowerPointSupport)
                {
                    ppt.PowerPointSupport = true;
                    if (mw.PPTManager == null) mw.InitializePPTManagers();
                    mw.StartPPTMonitoring();
                }
                if (ppt.EnablePowerPointEnhancement)
                {
                    ppt.EnablePowerPointEnhancement = false;
                    mw.StopPowerPointProcessMonitoring();
                }
            }
            if (mw.PPTManager != null)
            {
                mw.PPTManager.IsSupportWPS = ppt.IsSupportWPS;
                mw.PPTManager.SkipAnimationsWhenNavigating = ppt.SkipAnimationsWhenGoNext;
            }
        }

        public static void OnShowPPTButtonChanged(bool isOn)
        {
            var mw = GetMainWindow();
            if (mw?.PPTUIManager != null)
            {
                mw.PPTUIManager.ShowPPTButton = isOn;
                mw.PPTUIManager.UpdateNavigationPanelsVisibility();
            }
            mw?.UpdatePPTBtnPreview();
        }

        public static void OnShowPPTSidebarByDefaultChanged()
        {
            var mw = GetMainWindow();
            if (mw != null && mw.IsInPPTPresentationMode)
                mw.UpdatePPTQuickPanelVisibility();
        }

        public static void OnPPTButtonPositionChanged()
        {
            var mw = GetMainWindow();
            mw?.UpdatePPTBtnSlidersStatus();
            mw?.UpdatePPTUIManagerSettings();
            mw?.UpdatePPTBtnPreview();
        }

        public static void OnPPTButtonOpacityChanged(string buttonKey, double value)
        {
            var mw = GetMainWindow();
            if (mw?.PPTUIManager != null)
            {
                switch (buttonKey)
                {
                    case "LS": mw.PPTUIManager.PPTLSButtonOpacity = value; break;
                    case "RS": mw.PPTUIManager.PPTRSButtonOpacity = value; break;
                    case "LB": mw.PPTUIManager.PPTLBButtonOpacity = value; break;
                    case "RB": mw.PPTUIManager.PPTRBButtonOpacity = value; break;
                }
                mw.PPTUIManager.UpdateNavigationButtonStyles();
            }
            mw?.UpdatePPTBtnPreview();
        }

        public static void OnPPTButtonsDisplayOptionChanged()
        {
            var mw = GetMainWindow();
            if (mw?.PPTUIManager != null && mw.IsInPPTPresentationMode)
            {
                mw.PPTUIManager.PPTButtonsDisplayOption = SettingsManager.Settings.PowerPointSettings.PPTButtonsDisplayOption;
                mw.PPTUIManager.UpdateNavigationPanelsVisibility();
            }
            mw?.UpdatePPTBtnPreview();
        }

        public static void OnPPTSButtonsOptionChanged()
        {
            var mw = GetMainWindow();
            if (mw?.PPTUIManager != null && mw.IsInPPTPresentationMode)
            {
                mw.PPTUIManager.PPTSButtonsOption = SettingsManager.Settings.PowerPointSettings.PPTSButtonsOption;
                mw.PPTUIManager.UpdateNavigationButtonStyles();
            }
            mw?.UpdatePPTBtnPreview();
        }

        public static void OnPPTSButtonsOptionWithOpacityChanged()
        {
            var mw = GetMainWindow();
            var ppt = SettingsManager.Settings.PowerPointSettings;
            if (mw?.PPTUIManager != null && mw.IsInPPTPresentationMode)
            {
                mw.PPTUIManager.PPTSButtonsOption = ppt.PPTSButtonsOption;
                mw.PPTUIManager.PPTLSButtonOpacity = ppt.PPTLSButtonOpacity;
                mw.PPTUIManager.PPTRSButtonOpacity = ppt.PPTRSButtonOpacity;
                mw.PPTUIManager.UpdateNavigationButtonStyles();
            }
            mw?.UpdatePPTBtnPreview();
        }

        public static void OnPPTBButtonsOptionChanged()
        {
            var mw = GetMainWindow();
            if (mw?.PPTUIManager != null && mw.IsInPPTPresentationMode)
            {
                mw.PPTUIManager.PPTBButtonsOption = SettingsManager.Settings.PowerPointSettings.PPTBButtonsOption;
                mw.PPTUIManager.UpdateNavigationButtonStyles();
            }
            mw?.UpdatePPTBtnPreview();
        }

        public static void OnPPTBButtonsOptionWithOpacityChanged()
        {
            var mw = GetMainWindow();
            var ppt = SettingsManager.Settings.PowerPointSettings;
            if (mw?.PPTUIManager != null && mw.IsInPPTPresentationMode)
            {
                mw.PPTUIManager.PPTBButtonsOption = ppt.PPTBButtonsOption;
                mw.PPTUIManager.PPTLBButtonOpacity = ppt.PPTLBButtonOpacity;
                mw.PPTUIManager.PPTRBButtonOpacity = ppt.PPTRBButtonOpacity;
                mw.PPTUIManager.UpdateNavigationButtonStyles();
            }
            mw?.UpdatePPTBtnPreview();
        }

        public static void OnPPTNavBarScaleChanged(double scale)
        {
            var mw = GetMainWindow();
            if (mw?.PPTUIManager != null)
            {
                mw.PPTUIManager.PPTNavBarScale = scale;
                mw.PPTUIManager.UpdateNavigationButtonStyles();
            }
            mw?.UpdatePPTBtnPreview();
        }

        /// <summary>
        /// 全局默认设置或某位置"使用全局设置"开关变更时，全量同步有效值到运行时与预览。
        /// 有效值规则：位置 i 若 UseGlobalSettings=true，则采用全局字段值，否则采用位置自身字段值。
        /// </summary>
        public static void OnPPTGlobalSettingsChanged()
        {
            var mw = GetMainWindow();
            mw?.UpdatePPTUIManagerSettings();
            mw?.UpdatePPTBtnPreview();
            PPTPageFlipPreviewWindow.ActiveInstance?.UpdatePreview();
        }

        #endregion

        #region InkRecognition

        public static void OnInkToShapeEnabledChanged(bool isOn)
        {
            var mw = GetMainWindow();
            if (mw != null)
            {
                if (mw.FloatingBarToggleSwitchEnableInkToShape != null)
                    mw.FloatingBarToggleSwitchEnableInkToShape.IsOn = isOn;
                if (mw.BoardToggleSwitchEnableInkToShape != null)
                    mw.BoardToggleSwitchEnableInkToShape.IsOn = isOn;
            }
        }

        #endregion

        #region Update

        public static void OnSmartUpdateChanged()
        {
            if (!SettingsManager.Settings.Startup.IsAutoUpdate) return;
            var mw = GetMainWindow();
            if (mw != null)
            {
                mw.ResetUpdateCheckRetry();
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() => mw.AutoUpdate());
            }
        }

        public static void OnUpdateChannelChanged()
        {
            if (!SettingsManager.Settings.Startup.IsAutoUpdate) return;
            var mw = GetMainWindow();
            if (mw != null)
            {
                mw.ResetUpdateCheckRetry();
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => mw.AutoUpdate());
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"AutoUpdate | Error during channel switch update check: {ex.Message}", LogHelper.LogType.Error);
                    }
                });
            }
        }

        public static void OnStartSilentUpdateTimer()
        {
            var mw = GetMainWindow();
            if (mw != null) mw.StartSilentUpdateTimer();
        }

        public static void OnReloadSettingsFromFile()
        {
            var mw = GetMainWindow();
            if (mw != null) mw.ReloadSettingsFromFile();
        }

        #endregion

        #region Home

        public static void OnRestartApplication(object sender, RoutedEventArgs e)
        {
            var mw = GetMainWindow();
            mw?.BtnRestart_Click(sender, e);
        }

        public static void OnResetToSuggestion(object sender, RoutedEventArgs e)
        {
            var mw = GetMainWindow();
            mw?.BtnResetToSuggestion_Click(sender, e);
        }

        public static void OnExitApplication(object sender, RoutedEventArgs e)
        {
            var mw = GetMainWindow();
            mw?.ExitApplication(sender, e);
        }

        #endregion
    }
}

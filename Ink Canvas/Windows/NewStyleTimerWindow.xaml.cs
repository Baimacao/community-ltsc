using Ink_Canvas.Helpers;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Ink_Canvas.Windows
{
    /// <summary>
    /// 最近计时记录数据模型
    /// </summary>
    public class RecentTimersData
    {
        public string RecentTimer1 { get; set; } = "--:--";
        public string RecentTimer2 { get; set; } = "--:--";
        public string RecentTimer3 { get; set; } = "--:--";
        public string RecentTimer4 { get; set; } = "--:--";
        public string RecentTimer5 { get; set; } = "--:--";
        public string RecentTimer6 { get; set; } = "--:--";
    }

    /// <summary>
    /// 新计时器UI风格的倒计时器窗口
    /// </summary>
    public partial class NewStyleTimerWindow : Window
    {
        public NewStyleTimerWindow()
        {
            InitializeComponent();

            timer.Elapsed += Timer_Elapsed;
            timer.Interval = 50;
            InitializeUI();

            // 应用主题
            ApplyTheme();

            // 初始化隐藏定时器
            hideTimer = new Timer(1000); // 每秒检查一次
            hideTimer.Elapsed += HideTimer_Elapsed;
            lastActivityTime = DateTime.Now;

            // 监听主题变化事件
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

            // 监听关闭事件，清理资源
            Closed += NewStyleTimerWindow_Closed;

            // 监听加载事件，调整DPI相关的尺寸
            Loaded += NewStyleTimerWindow_Loaded;
        }

        private void NewStyleTimerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 根据DPI缩放因子调整Viewbox的最大尺寸，确保在高DPI屏幕上不会过大
            try
            {
                var source = System.Windows.PresentationSource.FromVisual(this);
                if (source != null)
                {
                    var dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                    var dpiScaleY = source.CompositionTarget.TransformToDevice.M22;

                    // 如果DPI缩放因子大于1.25，则适当缩小最大尺寸
                    // 这样可以确保在高DPI屏幕上，计时器窗口的物理像素大小不会过大
                    if (dpiScaleX > 1.25 || dpiScaleY > 1.25)
                    {
                        // 使用较小的缩放因子来限制最大尺寸
                        double scaleFactor = Math.Min(dpiScaleX, dpiScaleY);

                        if (MainViewController != null)
                        {
                            // 计算目标物理像素大小（约1350x750物理像素）
                            // 然后转换为逻辑像素
                            double targetPhysicalWidth = 1350;
                            double targetPhysicalHeight = 750;

                            // 转换为逻辑像素
                            double maxWidth = targetPhysicalWidth / scaleFactor;
                            double maxHeight = targetPhysicalHeight / scaleFactor;

                            // 确保不会小于原始尺寸的70%
                            maxWidth = Math.Max(maxWidth, 900 * 0.7);
                            maxHeight = Math.Max(maxHeight, 500 * 0.7);

                            MainViewController.MaxWidth = maxWidth;
                            MainViewController.MaxHeight = maxHeight;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"调整计时器窗口DPI尺寸失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private void NewStyleTimerWindow_Closed(object sender, EventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            timer?.Stop();
            timer?.Dispose();
            hideTimer?.Stop();
            hideTimer?.Dispose();
            mediaPlayer?.Close();
            if (QuickOptionsTransform != null) SpringAnimator.Stop(QuickOptionsTransform);
            if (MainDisplayTransform != null) SpringAnimator.Stop(MainDisplayTransform);
            foreach (var key in _springScalarKeys) SpringAnimator.StopScalar(key);
            _springScalarKeys.Clear();
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // 当主题变化时，重新应用主题
            Application.Current.Dispatcher.Invoke(() =>
            {
                RefreshTheme();
            });
        }

        /// <summary>
        /// 刷新主题
        /// </summary>
        public void RefreshTheme()
        {
            try
            {
                // 重新应用主题
                ApplyTheme();

                // 强制刷新UI
                InvalidateVisual();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"刷新计时器窗口主题出错: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        #region 事件定义
        /// <summary>
        /// 计时器完成事件
        /// </summary>
        public event EventHandler TimerCompleted;

        #endregion


        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!isTimerRunning || isPaused)
            {
                timer.Stop();
                return;
            }

            TimeSpan timeSpan = DateTime.Now - startTime;
            TimeSpan totalTimeSpan = new TimeSpan(hour, minute, second);
            double spentTimePercent = timeSpan.TotalMilliseconds / (totalTimeSpan.TotalMilliseconds);

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!isOvertimeMode)
                {
                    TimeSpan leftTimeSpan = totalTimeSpan - timeSpan;
                    if (leftTimeSpan.Milliseconds > 0) leftTimeSpan += new TimeSpan(0, 0, 1);

                    int totalHours = (int)leftTimeSpan.TotalHours;
                    int displayHours = totalHours;

                    if (displayHours > 99) displayHours = 99;

                    SetDigitDisplay("Digit1Display", displayHours / 10);
                    SetDigitDisplay("Digit2Display", displayHours % 10);
                    SetDigitDisplay("Digit3Display", leftTimeSpan.Minutes / 10);
                    SetDigitDisplay("Digit4Display", leftTimeSpan.Minutes % 10);
                    SetDigitDisplay("Digit5Display", leftTimeSpan.Seconds / 10);
                    SetDigitDisplay("Digit6Display", leftTimeSpan.Seconds % 10);

                    SetColonDisplay(false);

                    if (leftTimeSpan.TotalSeconds <= 6 && leftTimeSpan.TotalSeconds > 0 &&
                        MainWindow.Settings.RandSettings?.EnableProgressiveReminder == true &&
                        !hasPlayedProgressiveReminder)
                    {
                        PlayProgressiveReminderSound();
                        hasPlayedProgressiveReminder = true;
                    }

                    if (leftTimeSpan.TotalSeconds <= 0 && MainWindow.Settings.RandSettings?.EnableOvertimeCountUp == true)
                    {
                        isOvertimeMode = true;
                        PlayTimerSound();
                    }
                    else if (leftTimeSpan.TotalSeconds <= 0)
                    {
                        timer.Stop();
                        isTimerRunning = false;
                        isPaused = false;
                        isOvertimeMode = false;

                        ApplyResetStateAfterStop();

                        PlayTimerSound();

                        TimerCompleted?.Invoke(this, EventArgs.Empty);
                        HandleTimerCompletion();
                    }
                }
                else
                {
                    TimeSpan overtimeSpan = timeSpan - totalTimeSpan;
                    int totalHours = (int)overtimeSpan.TotalHours;
                    int displayHours = totalHours;

                    if (displayHours > 99) displayHours = 99;
                    if (displayHours < 0) displayHours = 0;

                    bool shouldShowRed = MainWindow.Settings.RandSettings?.EnableOvertimeRedText == true;

                    int hoursTens = Math.Max(0, Math.Min(9, Math.Abs(displayHours / 10) % 10));
                    int hoursOnes = Math.Max(0, Math.Min(9, (displayHours % 10 + 10) % 10));
                    int minutesTens = Math.Max(0, Math.Min(9, Math.Abs(overtimeSpan.Minutes) / 10));
                    int minutesOnes = Math.Max(0, Math.Min(9, Math.Abs(overtimeSpan.Minutes) % 10));
                    int secondsTens = Math.Max(0, Math.Min(9, Math.Abs(overtimeSpan.Seconds) / 10));
                    int secondsOnes = Math.Max(0, Math.Min(9, Math.Abs(overtimeSpan.Seconds) % 10));

                    SetDigitDisplay("Digit1Display", hoursTens, shouldShowRed);
                    SetDigitDisplay("Digit2Display", hoursOnes, shouldShowRed);
                    SetDigitDisplay("Digit3Display", minutesTens, shouldShowRed);
                    SetDigitDisplay("Digit4Display", minutesOnes, shouldShowRed);
                    SetDigitDisplay("Digit5Display", secondsTens, shouldShowRed);
                    SetDigitDisplay("Digit6Display", secondsOnes, shouldShowRed);

                    SetColonDisplay(shouldShowRed);
                }
            });
        }

        SoundPlayer player = new SoundPlayer();
        MediaPlayer mediaPlayer = new MediaPlayer();

        int hour = 0;
        int minute = 5;
        int second = 0;
        int cachedStartHour = 0;
        int cachedStartMinute = 5;
        int cachedStartSecond = 0;

        DateTime startTime = DateTime.Now;
        DateTime pauseTime = DateTime.Now;

        bool isTimerRunning = false;
        bool isPaused = false;
        bool isOvertimeMode = false;
        TimeSpan remainingTime = TimeSpan.Zero;
        bool hasPlayedProgressiveReminder = false;

        Timer timer = new Timer();
        private Timer hideTimer;

        /// <summary>进入倒计时后，闲置多少秒折叠调时间组件。</summary>
        private const double AdjustFoldDelaySeconds = 5;
        private DateTime lastActivityTime;
        public TimeSpan? GetTotalTimeSpan()
        {
            return new TimeSpan(hour, minute, second);
        }

        public TimeSpan? GetElapsedTime()
        {
            if (isPaused) return null;

            return DateTime.Now - startTime;
        }

        // 最近计时记录（使用数组简化重复代码）
        private readonly string[] _recentTimers = new string[] { "--:--", "--:--", "--:--", "--:--", "--:--", "--:--" };

        // JSON文件路径
        private static readonly string ConfigsFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs");
        private static readonly string RecentTimersJsonPath = System.IO.Path.Combine(ConfigsFolder, "RecentTimers.json");
        private bool _isRecentTimersTabSelected;

        private void InitializeUI()
        {
            UpdateDigitDisplays();
            LoadRecentTimers();
            UpdateRecentTimerDisplays();
            InitializeTabState();
        }

        private void InitializeTabState()
        {
            _isRecentTimersTabSelected = false;
            if (TimerQuickTabControl != null)
            {
                TimerQuickTabControl.SelectedIndex = 0;
            }
        }

        private void ApplyTheme()
        {
            try
            {
                // 应用主题设置
                if (MainWindow.Settings != null)
                {
                    ApplyTheme(MainWindow.Settings);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"应用新计时器UI倒计时窗口主题出错: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private void ApplyTheme(Settings settings)
        {
            ThemeHelper.ApplyTheme(this, settings, theme =>
            {
                RefreshModernBorder();
                WindowBackdropHelper.Apply(this, settings);
                // 复用当前状态下的显示逻辑，避免把暂停/超时/运行中的读数重置为初始设定时间。
                RefreshDigitDisplayForCurrentState();
            });
        }

        /// <summary>
        /// 依据当前计时器状态刷新数字显示：
        /// 运行中按 DateTime.Now、暂停中按 pauseTime 推算；处于超时模式时按超时值渲染；未启动时回退到初始设定值。
        /// 供主题切换等场景直接复用 Timer_Elapsed 的渲染分支。
        /// </summary>
        private void RefreshDigitDisplayForCurrentState()
        {
            if (!isTimerRunning)
            {
                UpdateDigitDisplays();
                return;
            }

            DateTime referenceTime = isPaused ? pauseTime : DateTime.Now;
            TimeSpan timeSpan = referenceTime - startTime;
            TimeSpan totalTimeSpan = new TimeSpan(hour, minute, second);

            if (!isOvertimeMode)
            {
                TimeSpan leftTimeSpan = totalTimeSpan - timeSpan;
                if (leftTimeSpan.Milliseconds > 0) leftTimeSpan += new TimeSpan(0, 0, 1);
                if (leftTimeSpan < TimeSpan.Zero) leftTimeSpan = TimeSpan.Zero;

                int displayHours = Math.Min(99, (int)leftTimeSpan.TotalHours);
                SetDigitDisplay("Digit1Display", displayHours / 10);
                SetDigitDisplay("Digit2Display", displayHours % 10);
                SetDigitDisplay("Digit3Display", leftTimeSpan.Minutes / 10);
                SetDigitDisplay("Digit4Display", leftTimeSpan.Minutes % 10);
                SetDigitDisplay("Digit5Display", leftTimeSpan.Seconds / 10);
                SetDigitDisplay("Digit6Display", leftTimeSpan.Seconds % 10);
                SetColonDisplay(false);
            }
            else
            {
                TimeSpan overtimeSpan = timeSpan - totalTimeSpan;
                if (overtimeSpan < TimeSpan.Zero) overtimeSpan = TimeSpan.Zero;

                int displayHours = Math.Max(0, Math.Min(99, (int)overtimeSpan.TotalHours));
                bool shouldShowRed = MainWindow.Settings?.RandSettings?.EnableOvertimeRedText == true;

                int hoursTens = Math.Max(0, Math.Min(9, Math.Abs(displayHours / 10) % 10));
                int hoursOnes = Math.Max(0, Math.Min(9, (displayHours % 10 + 10) % 10));
                int minutesTens = Math.Max(0, Math.Min(9, Math.Abs(overtimeSpan.Minutes) / 10));
                int minutesOnes = Math.Max(0, Math.Min(9, Math.Abs(overtimeSpan.Minutes) % 10));
                int secondsTens = Math.Max(0, Math.Min(9, Math.Abs(overtimeSpan.Seconds) / 10));
                int secondsOnes = Math.Max(0, Math.Min(9, Math.Abs(overtimeSpan.Seconds) % 10));

                SetDigitDisplay("Digit1Display", hoursTens, shouldShowRed);
                SetDigitDisplay("Digit2Display", hoursOnes, shouldShowRed);
                SetDigitDisplay("Digit3Display", minutesTens, shouldShowRed);
                SetDigitDisplay("Digit4Display", minutesOnes, shouldShowRed);
                SetDigitDisplay("Digit5Display", secondsTens, shouldShowRed);
                SetDigitDisplay("Digit6Display", secondsOnes, shouldShowRed);
                SetColonDisplay(shouldShowRed);
            }
        }

        private void UpdateDigitDisplays()
        {
            SetDigitDisplay("Digit1Display", hour / 10);
            SetDigitDisplay("Digit2Display", hour % 10);
            SetDigitDisplay("Digit3Display", minute / 10);
            SetDigitDisplay("Digit4Display", minute % 10);
            SetDigitDisplay("Digit5Display", second / 10);
            SetDigitDisplay("Digit6Display", second % 10);

            SetColonDisplay(false);
        }

        // 更新剩余时间
        private void UpdateRemainingTime()
        {
            if (isTimerRunning && !isPaused)
            {
                // 获取当前剩余时间
                TimeSpan? currentRemaining = GetRemainingTime();
                if (currentRemaining.HasValue)
                {
                    // 计算已经过去的时间
                    TimeSpan elapsedTime = DateTime.Now - startTime;

                    // 计算新的总时间
                    TimeSpan newTotalTime = new TimeSpan(hour, minute, second);

                    // 如果新设置的时间小于已经过去的时间，则设置为0
                    if (newTotalTime <= elapsedTime)
                    {
                        remainingTime = TimeSpan.Zero;
                    }
                    else
                    {
                        // 否则，剩余时间 = 新总时间 - 已经过去的时间
                        remainingTime = newTotalTime - elapsedTime;
                    }
                }
                else
                {
                    // 如果没有剩余时间信息，直接设置新的剩余时间
                    remainingTime = new TimeSpan(hour, minute, second);
                }
            }
        }

        // 更新特定时间单位的剩余时间
        private void UpdateSpecificTimeUnit(int newHour, int newMinute, int newSecond)
        {
            if (isTimerRunning && !isPaused)
            {
                // 获取当前剩余时间
                TimeSpan? currentRemaining = GetRemainingTime();
                if (currentRemaining.HasValue)
                {
                    // 计算已经过去的时间
                    TimeSpan elapsedTime = DateTime.Now - startTime;

                    // 计算新的总时间
                    TimeSpan newTotalTime = new TimeSpan(newHour, newMinute, newSecond);

                    // 如果新设置的时间小于已经过去的时间，则设置为0
                    if (newTotalTime <= elapsedTime)
                    {
                        remainingTime = TimeSpan.Zero;
                    }
                    else
                    {
                        // 否则，剩余时间 = 新总时间 - 已经过去的时间
                        remainingTime = newTotalTime - elapsedTime;
                    }
                }
                else
                {
                    // 如果没有剩余时间信息，直接设置新的剩余时间
                    remainingTime = new TimeSpan(newHour, newMinute, newSecond);
                }
            }
        }

        public bool IsTimerRunning => isTimerRunning;

        public TimeSpan? GetRemainingTime()
        {
            if (isPaused) return null;

            var elapsed = DateTime.Now - startTime;
            var totalTimeSpan = new TimeSpan(hour, minute, second);
            var leftTimeSpan = totalTimeSpan - elapsed;

            if (leftTimeSpan.Milliseconds > 0) leftTimeSpan += new TimeSpan(0, 0, 1);

            return leftTimeSpan;
        }

        public void StopTimer()
        {
            timer.Stop();
            isTimerRunning = false;
            StartPauseIcon.Data = Geometry.Parse(PlayIconData);
        }


        /// <summary>
        /// 根据数字值设置SVG数字显示
        /// </summary>
        /// <param name="pathName">Path控件的名称</param>
        /// <param name="digit">要显示的数字(0-9)</param>
        /// <param name="isRed">是否显示为红色</param>
        private void SetDigitDisplay(string pathName, int digit, bool isRed = false)
        {
            var path = this.FindName(pathName) as System.Windows.Shapes.Path;
            if (path != null)
            {
                digit = Math.Max(0, Math.Min(9, digit));

                string resourceKey = $"Digit{digit}";
                var geometry = this.FindResource(resourceKey) as Geometry;
                if (geometry != null)
                {
                    path.Data = geometry;
                }

                if (isRed)
                {
                    path.Fill = Brushes.Red;
                }
                else
                {
                    var defaultBrush = this.TryFindResource("NewTimerWindowDigitForeground") as Brush;
                    if (defaultBrush != null)
                    {
                        path.Fill = defaultBrush;
                    }
                    else
                    {
                        path.Fill = Brushes.White;
                    }
                }
            }
        }

        /// <summary>
        /// 设置冒号显示颜色
        /// </summary>
        /// <param name="isRed">是否显示为红色</param>
        private void SetColonDisplay(bool isRed = false)
        {
            var colon1 = this.FindName("Colon1Display") as TextBlock;
            var colon2 = this.FindName("Colon2Display") as TextBlock;

            if (colon1 != null)
            {
                if (isRed)
                {
                    colon1.Foreground = Brushes.Red;
                }
                else
                {
                    var defaultBrush = this.TryFindResource("NewTimerWindowDigitForeground") as Brush;
                    if (defaultBrush != null)
                    {
                        colon1.Foreground = defaultBrush;
                    }
                    else
                    {
                        colon1.Foreground = Brushes.White;
                    }
                }
            }

            if (colon2 != null)
            {
                if (isRed)
                {
                    colon2.Foreground = Brushes.Red;
                }
                else
                {
                    var defaultBrush = this.TryFindResource("NewTimerWindowDigitForeground") as Brush;
                    if (defaultBrush != null)
                    {
                        colon2.Foreground = defaultBrush;
                    }
                    else
                    {
                        colon2.Foreground = Brushes.White;
                    }
                }
            }
        }

        // 第1位数字（小时十位）
        private void Digit1Plus_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning) return;
            UpdateActivityTime();
            int currentHour = hour;
            int hourTens = currentHour / 10;
            int hourOnes = currentHour % 10;

            hourTens++;
            if (hourTens >= 10) hourTens = 0;

            hour = hourTens * 10 + hourOnes;
            UpdateDigitDisplays();
        }

        private void Digit1Minus_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning) return;
            UpdateActivityTime();
            int currentHour = hour;
            int hourTens = currentHour / 10;
            int hourOnes = currentHour % 10;

            hourTens--;
            if (hourTens < 0) hourTens = 9;

            hour = hourTens * 10 + hourOnes;
            UpdateDigitDisplays();
        }

        // 第2位数字（小时个位）
        private void Digit2Plus_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning) return;
            UpdateActivityTime();
            int currentHour = hour;
            int hourTens = currentHour / 10;
            int hourOnes = currentHour % 10;

            hourOnes++;
            if (hourOnes >= 10)
            {
                hourOnes = 0;
                hourTens++;
                if (hourTens >= 10) hourTens = 0;
            }

            hour = hourTens * 10 + hourOnes;
            UpdateDigitDisplays();
        }

        private void Digit2Minus_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning) return;
            UpdateActivityTime();
            int currentHour = hour;
            int hourTens = currentHour / 10;
            int hourOnes = currentHour % 10;

            hourOnes--;
            if (hourOnes < 0)
            {
                hourOnes = 9;
                hourTens--;
                if (hourTens < 0) hourTens = 9;
            }

            hour = hourTens * 10 + hourOnes;
            UpdateDigitDisplays();
        }

        // 第3位数字（分钟十位）
        private void Digit3Plus_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning) return;
            UpdateActivityTime();
            int currentMinute = minute;
            int minuteTens = currentMinute / 10;
            int minuteOnes = currentMinute % 10;

            minuteTens++;
            if (minuteTens >= 6) minuteTens = 0;

            minute = minuteTens * 10 + minuteOnes;
            UpdateDigitDisplays();
        }

        private void Digit3Minus_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning) return;
            UpdateActivityTime();
            int currentMinute = minute;
            int minuteTens = currentMinute / 10;
            int minuteOnes = currentMinute % 10;

            minuteTens--;
            if (minuteTens < 0) minuteTens = 5;

            minute = minuteTens * 10 + minuteOnes;
            UpdateDigitDisplays();
        }

        // 第4位数字（分钟个位）
        private void Digit4Plus_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning) return;
            UpdateActivityTime();
            int currentMinute = minute;
            int minuteTens = currentMinute / 10;
            int minuteOnes = currentMinute % 10;

            minuteOnes++;
            if (minuteOnes >= 10)
            {
                minuteOnes = 0;
                minuteTens++;
                if (minuteTens >= 6) minuteTens = 0;
            }

            minute = minuteTens * 10 + minuteOnes;
            UpdateDigitDisplays();
        }

        private void Digit4Minus_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning) return;
            UpdateActivityTime();
            int currentMinute = minute;
            int minuteTens = currentMinute / 10;
            int minuteOnes = currentMinute % 10;

            minuteOnes--;
            if (minuteOnes < 0)
            {
                minuteOnes = 9;
                minuteTens--;
                if (minuteTens < 0) minuteTens = 5;
            }

            minute = minuteTens * 10 + minuteOnes;
            UpdateDigitDisplays();
        }

        // 第5位数字（秒十位）
        private void Digit5Plus_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning) return;
            UpdateActivityTime();
            int currentSecond = second;
            int secondTens = currentSecond / 10;
            int secondOnes = currentSecond % 10;

            secondTens++;
            if (secondTens >= 6) secondTens = 0;

            second = secondTens * 10 + secondOnes;
            UpdateDigitDisplays();
        }

        private void Digit5Minus_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning) return;
            UpdateActivityTime();
            int currentSecond = second;
            int secondTens = currentSecond / 10;
            int secondOnes = currentSecond % 10;

            secondTens--;
            if (secondTens < 0) secondTens = 5;

            second = secondTens * 10 + secondOnes;
            UpdateDigitDisplays();
        }

        // 第6位数字（秒个位）
        private void Digit6Plus_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning) return;
            UpdateActivityTime();
            int currentSecond = second;
            int secondTens = currentSecond / 10;
            int secondOnes = currentSecond % 10;

            secondOnes++;
            if (secondOnes >= 10)
            {
                secondOnes = 0;
                secondTens++;
                if (secondTens >= 6) secondTens = 0;
            }

            second = secondTens * 10 + secondOnes;
            UpdateDigitDisplays();
        }

        private void Digit6Minus_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning) return;
            UpdateActivityTime();
            int currentSecond = second;
            int secondTens = currentSecond / 10;
            int secondOnes = currentSecond % 10;

            secondOnes--;
            if (secondOnes < 0)
            {
                secondOnes = 9;
                secondTens--;
                if (secondTens < 0) secondTens = 5;
            }

            second = secondTens * 10 + secondOnes;
            UpdateDigitDisplays();
        }

        // 图标数据常量
        private const string PlayIconData = "M6.5 4.00004V20C6.49995 20.178 6.54737 20.3527 6.63738 20.5062C6.72739 20.6597 6.85672 20.7864 7.01202 20.8732C7.16733 20.96 7.34299 21.0038 7.52088 21.0001C7.69878 20.9964 7.87245 20.9453 8.024 20.852L21.024 12.852C21.1696 12.7626 21.2898 12.6373 21.3733 12.4881C21.4567 12.339 21.5005 12.1709 21.5005 12C21.5005 11.8291 21.4567 11.6611 21.3733 11.512C21.2898 11.3628 21.1696 11.2375 21.024 11.148L8.024 3.14804C7.87245 3.0548 7.69878 3.00369 7.52088 2.99997C7.34299 2.99626 7.16733 3.04007 7.01202 3.1269C6.85672 3.21372 6.72739 3.34042 6.63738 3.4939C6.54737 3.64739 6.49995 3.82211 6.5 4.00004Z";
        private const string PauseIconData = "M9.5 4H7.5C6.96957 4 6.46086 4.21071 6.08579 4.58579C5.71071 4.96086 5.5 5.46957 5.5 6V18C5.5 18.5304 5.71071 19.0391 6.08579 19.4142C6.46086 19.7893 6.96957 20 7.5 20H9.5C10.0304 20 10.5391 19.7893 10.9142 19.4142C11.2893 19.0391 11.5 18.5304 11.5 18V6C11.5 5.46957 11.2893 4.96086 10.9142 4.58579C10.5391 4.21071 10.0304 4 9.5 4Z M17.5 4H15.5C14.9696 4 14.4609 4.21071 14.0858 4.58579C13.7107 4.96086 13.5 5.46957 13.5 6V18C13.5 18.5304 13.7107 19.0391 14.0858 19.4142C14.4609 19.7893 14.9696 20 15.5 20H17.5C18.0304 20 18.5391 19.7893 18.9142 19.4142C19.2893 19.0391 19.5 18.5304 19.5 18V6C19.5 5.46957 19.2893 4.96086 18.9142 4.58579C18.5391 4.21071 18.0304 4 17.5 4Z";

        private void StartPause_Click(object sender, RoutedEventArgs e)
        {
            UpdateActivityTime();
            if (isPaused && isTimerRunning)
            {
                // 继续计时
                startTime += DateTime.Now - pauseTime;
                StartPauseIcon.Data = Geometry.Parse(PauseIconData);
                isPaused = false;
                timer.Start();
                StartAdjustIdleCountdown();
            }
            else if (isTimerRunning)
            {
                // 暂停计时
                pauseTime = DateTime.Now;
                StartPauseIcon.Data = Geometry.Parse(PlayIconData);
                isPaused = true;
                timer.Stop();
                UnfoldAdjustPanel();
            }
            else
            {
                // 开始计时
                if (hour == 0 && minute == 0 && second == 0)
                {
                    second = 1;
                    UpdateDigitDisplays();
                }

                cachedStartHour = hour;
                cachedStartMinute = minute;
                cachedStartSecond = second;

                startTime = DateTime.Now;
                StartPauseIcon.Data = Geometry.Parse(PauseIconData);
                isPaused = false;
                isTimerRunning = true;
                isOvertimeMode = false;
                hasPlayedProgressiveReminder = false;
                timer.Start();

                // 进入倒计时状态：开始计 5 秒闲置，到点折叠调时间组件
                StartAdjustIdleCountdown();

                // 保存到最近计时记录
                SaveRecentTimer();

                // 启用全屏按钮
                if (FullscreenBtn != null)
                {
                    FullscreenBtn.IsEnabled = true;
                }
            }
        }

        private void ApplyResetStateAfterStop()
        {
            UpdateDigitDisplays();
            SetColonDisplay(false);

            if (StartPauseIcon != null)
            {
                StartPauseIcon.Data = Geometry.Parse(PlayIconData);
            }

            hasPlayedProgressiveReminder = false;

            // 禁用全屏按钮
            if (FullscreenBtn != null)
            {
                FullscreenBtn.IsEnabled = false;
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            UpdateActivityTime();

            if (isTimerRunning)
            {
                // 停止计时器
                timer.Stop();
                isTimerRunning = false;
                isPaused = false;

                StopAdjustIdleCountdown();
            }

            UnfoldAdjustPanel();

            isOvertimeMode = false;

            ApplyResetStateAfterStop();
        }

        private void PlayTimerSound()
        {
            try
            {
                double volume = MainWindow.Settings.RandSettings?.TimerVolume ?? 1.0;
                mediaPlayer.Volume = volume;

                if (!string.IsNullOrEmpty(MainWindow.Settings.RandSettings?.CustomTimerSoundPath) &&
                    System.IO.File.Exists(MainWindow.Settings.RandSettings.CustomTimerSoundPath))
                {
                    mediaPlayer.Open(new Uri(MainWindow.Settings.RandSettings.CustomTimerSoundPath));
                }
                else
                {
                    string tempPath = System.IO.Path.GetTempFileName() + ".wav";
                    using (var stream = Properties.Resources.TimerDownNotice)
                    {
                        using (var fileStream = new System.IO.FileStream(tempPath, System.IO.FileMode.Create))
                        {
                            stream.CopyTo(fileStream);
                        }
                    }
                    mediaPlayer.Open(new Uri(tempPath));
                }

                mediaPlayer.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"播放计时器铃声失败: {ex.Message}");
            }
        }

        private void PlayProgressiveReminderSound()
        {
            try
            {
                double volume = MainWindow.Settings.RandSettings?.ProgressiveReminderVolume ?? 1.0;
                mediaPlayer.Volume = volume;

                if (!string.IsNullOrEmpty(MainWindow.Settings.RandSettings?.ProgressiveReminderSoundPath) &&
                    System.IO.File.Exists(MainWindow.Settings.RandSettings.ProgressiveReminderSoundPath))
                {
                    mediaPlayer.Open(new Uri(MainWindow.Settings.RandSettings.ProgressiveReminderSoundPath));
                }
                else
                {
                    string tempPath = System.IO.Path.GetTempFileName() + ".wav";
                    using (var stream = Properties.Resources.ProgressiveAudio)
                    {
                        using (var fileStream = new System.IO.FileStream(tempPath, System.IO.FileMode.Create))
                        {
                            stream.CopyTo(fileStream);
                        }
                    }
                    mediaPlayer.Open(new Uri(tempPath));
                }

                mediaPlayer.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"播放渐进提醒音频失败: {ex.Message}");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            StopTimer();
            Close();
        }

        private void TimerQuickTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource != TimerQuickTabControl) return;
            UpdateActivityTime();
            _isRecentTimersTabSelected = TimerQuickTabControl.SelectedIndex == 1;
        }

        // 常用计时事件处理
        private void Common5Min_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning && !isPaused) return;
            UpdateActivityTime();
            SetQuickTime(0, 5, 0);
        }

        private void Common10Min_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning && !isPaused) return;
            UpdateActivityTime();
            SetQuickTime(0, 10, 0);
        }

        private void Common15Min_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning && !isPaused) return;
            UpdateActivityTime();
            SetQuickTime(0, 15, 0);
        }

        private void Common30Min_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning && !isPaused) return;
            UpdateActivityTime();
            SetQuickTime(0, 30, 0);
        }

        private void Common45Min_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning && !isPaused) return;
            UpdateActivityTime();
            SetQuickTime(0, 45, 0);
        }

        private void Common60Min_Click(object sender, RoutedEventArgs e)
        {
            if (isTimerRunning && !isPaused) return;
            UpdateActivityTime();
            SetQuickTime(1, 0, 0);
        }

        // 最近计时事件处理（统一处理方法）
        private void HandleRecentTimerClick(int index)
        {
            if ((isTimerRunning && !isPaused) || _recentTimers[index] == "--:--") return;
            UpdateActivityTime();
            ApplyRecentTimer(_recentTimers[index]);
        }

        private void RecentTimer1_Click(object sender, RoutedEventArgs e) => HandleRecentTimerClick(0);
        private void RecentTimer2_Click(object sender, RoutedEventArgs e) => HandleRecentTimerClick(1);
        private void RecentTimer3_Click(object sender, RoutedEventArgs e) => HandleRecentTimerClick(2);
        private void RecentTimer4_Click(object sender, RoutedEventArgs e) => HandleRecentTimerClick(3);
        private void RecentTimer5_Click(object sender, RoutedEventArgs e) => HandleRecentTimerClick(4);
        private void RecentTimer6_Click(object sender, RoutedEventArgs e) => HandleRecentTimerClick(5);

        // 设置快捷时间
        private void SetQuickTime(int h, int m, int s)
        {
            hour = h;
            minute = m;
            second = s;
            UpdateDigitDisplays();
        }

        // 应用最近计时
        private void ApplyRecentTimer(string timeString)
        {
            if (timeString == "--:--") return;

            try
            {
                var parts = timeString.Split(':');
                if (parts.Length == 2)
                {
                    int minutes = int.Parse(parts[0]);
                    int seconds = int.Parse(parts[1]);
                    SetQuickTime(0, minutes, seconds);
                }
            }
            catch
            {
                // 如果解析失败，忽略
            }
        }

        // 保存最近计时记录
        private void SaveRecentTimer()
        {
            if (hour == 0 && minute == 0 && second == 0) return;

            string currentTime = $"{minute:D2}:{second:D2}";

            // 检查是否已存在相同的时间
            int existingIndex = Array.IndexOf(_recentTimers, currentTime);

            if (existingIndex >= 0)
            {
                // 如果存在重复，将其移到最前面
                string duplicateTimer = _recentTimers[existingIndex];
                // 移除重复项
                for (int i = existingIndex; i < _recentTimers.Length - 1; i++)
                {
                    _recentTimers[i] = _recentTimers[i + 1];
                }
                _recentTimers[_recentTimers.Length - 1] = "--:--";
                // 右移所有元素，将重复项添加到最前面
                Array.Copy(_recentTimers, 0, _recentTimers, 1, _recentTimers.Length - 1);
                _recentTimers[0] = duplicateTimer;
            }
            else
            {
                // 右移所有元素，添加新记录到最前面
                Array.Copy(_recentTimers, 0, _recentTimers, 1, _recentTimers.Length - 1);
                _recentTimers[0] = currentTime;
            }

            UpdateRecentTimerDisplays();
            SaveRecentTimersToRegistry();
        }

        // 更新最近计时显示
        private void UpdateRecentTimerDisplays()
        {
            try
            {
                for (int i = 0; i < _recentTimers.Length; i++)
                {
                    var timerText = this.FindName($"RecentTimer{i + 1}Text") as TextBlock;
                    if (timerText != null)
                    {
                        timerText.Text = _recentTimers[i];
                    }
                }
            }
            catch
            {
            }
        }

        // 从JSON文件加载最近计时记录
        private void LoadRecentTimers()
        {
            try
            {
                // 确保Configs文件夹存在
                if (!Directory.Exists(ConfigsFolder))
                {
                    Directory.CreateDirectory(ConfigsFolder);
                }

                if (!File.Exists(RecentTimersJsonPath))
                {
                    Array.Fill(_recentTimers, "--:--");
                    return;
                }

                // 读取JSON文件
                string jsonContent = File.ReadAllText(RecentTimersJsonPath);
                var data = JsonConvert.DeserializeObject<RecentTimersData>(jsonContent);

                if (data != null)
                {
                    _recentTimers[0] = data.RecentTimer1 ?? "--:--";
                    _recentTimers[1] = data.RecentTimer2 ?? "--:--";
                    _recentTimers[2] = data.RecentTimer3 ?? "--:--";
                    _recentTimers[3] = data.RecentTimer4 ?? "--:--";
                    _recentTimers[4] = data.RecentTimer5 ?? "--:--";
                    _recentTimers[5] = data.RecentTimer6 ?? "--:--";
                }
                else
                {
                    Array.Fill(_recentTimers, "--:--");
                }
            }
            catch (Exception)
            {
                Array.Fill(_recentTimers, "--:--");
            }
        }

        // 保存最近计时记录到JSON文件
        private void SaveRecentTimersToRegistry()
        {
            try
            {
                // 确保Configs文件夹存在
                if (!Directory.Exists(ConfigsFolder))
                {
                    Directory.CreateDirectory(ConfigsFolder);
                }

                // 创建数据对象
                var data = new RecentTimersData
                {
                    RecentTimer1 = _recentTimers[0],
                    RecentTimer2 = _recentTimers[1],
                    RecentTimer3 = _recentTimers[2],
                    RecentTimer4 = _recentTimers[3],
                    RecentTimer5 = _recentTimers[4],
                    RecentTimer6 = _recentTimers[5]
                };

                // 序列化为JSON并保存到文件
                string jsonContent = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(RecentTimersJsonPath, jsonContent);
            }
            catch (Exception)
            {
            }
        }

        private void RefreshModernBorder()
        {
            try
            {
                if (MainBorder != null)
                {
                    MainBorder.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
                }
            }
            catch
            {
            }
        }

        private FullscreenTimerWindow fullscreenWindow;

        public bool IsFullscreenWindowOpen => fullscreenWindow != null && fullscreenWindow.IsVisible;

        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (fullscreenWindow != null && fullscreenWindow.IsVisible)
            {
                fullscreenWindow.Close();
                fullscreenWindow = null;
                return;
            }

            if (isTimerRunning && !isPaused)
            {
                fullscreenWindow = new FullscreenTimerWindow(this);
                fullscreenWindow.Closed += (s, args) => { fullscreenWindow = null; };
                fullscreenWindow.Show();
            }
        }

        private void MainBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            UpdateActivityTime();
            DragTimerWindow(e);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            UpdateActivityTime();
            DragTimerWindow(e);
        }

        private void DragTimerWindow(MouseButtonEventArgs e)
        {
            if (e.ClickCount != 1 || e.LeftButton != MouseButtonState.Pressed) return;

            try
            {
                DragMove();
                e.Handled = true;
            }
            catch
            {
            }
        }

        private void HandleTimerCompletion()
        {
            ResetTimerState();
        }

        /// <summary>
        /// 重置计时器状态
        /// </summary>
        public void ResetTimerState()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 停止计时器
                if (isTimerRunning)
                {
                    timer.Stop();
                    isTimerRunning = false;
                    isPaused = false;

                    if (hideTimer != null)
                    {
                        hideTimer.Stop();
                    }
                }

                // 重置时间到默认值
                hour = cachedStartHour;
                minute = cachedStartMinute;
                second = cachedStartSecond;

                // 更新显示
                UpdateDigitDisplays();
                SetColonDisplay(false);

                // 重置图标
                if (StartPauseIcon != null)
                {
                    StartPauseIcon.Data = Geometry.Parse(PlayIconData);
                }

                // 重置状态标志
                isOvertimeMode = false;
                hasPlayedProgressiveReminder = false;

                // 禁用全屏按钮
                if (FullscreenBtn != null)
                {
                    FullscreenBtn.IsEnabled = false;
                }
            });
        }

        /// <summary>
        /// 闲置计时。原来这里是「闲置 5 秒弹一个独立迷你窗」——
        /// 主窗口还在，等于又叠了一层，不是需求里说的「折叠调时间的组件」。
        /// 现在改成：进入倒计时后闲置满 5 秒，把调时间组件原地收起，
        /// 主窗口不动，只留大数字 + 底部控制条 + 一个显式展开入口。
        ///
        /// 全屏模式下不折叠：全屏窗是独立的展示面，主窗口在后台，
        /// 折叠它没有意义，还会让回来的时候状态对不上。
        /// </summary>
        private void HideTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!isTimerRunning || isPaused) return;
            if (IsFullscreenWindowOpen) return;

            var timeSinceLastActivity = DateTime.Now - lastActivityTime;
            if (timeSinceLastActivity.TotalSeconds < AdjustFoldDelaySeconds) return;

            Dispatcher.Invoke(() =>
            {
                try
                {
                    StopAdjustIdleCountdown();
                    FoldAdjustPanel();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"折叠调时间组件失败: {ex.Message}", LogHelper.LogType.Error);
                }
            });
        }

        public void UpdateActivityTime()
        {
            lastActivityTime = DateTime.Now;
        }

        // ── 折叠态：整窗只剩数字 ──────────────────────────────────
        //
        // 需求（ta 2026-11-16）：
        //   「其他除了时间显示的控件都可以收纳（就直接整个窗口都是时间显示），
        //     让它被点击时恢复就行，收纳状态也要可以拖动」
        //
        // 所以折叠态 = 只有大数字。标题、右侧快捷时长、十二个步进按钮、
        // 中间竖分隔线、底部控制条，全部收起，数字本身放大并居中。
        //
        // 交互：
        //   · 折叠态点窗口任意位置 → 恢复调时间
        //   · 折叠态按住拖动 → 仍然能挪窗口（老师可能把它挪到角落挂着）
        //   · 拖动结束不算点击，不会误触恢复
        //
        // 动画只用 SpringAnimator 的 Opacity + TranslateTransform + ScaleTransform，
        // **不动 Width/Height**：改尺寸会触发父级反复重排，数字全程在抖。

        /// <summary>是否处于折叠态（整窗只剩数字）。</summary>
        private bool _isAdjustPanelFolded;

        /// <summary>折叠态下数字放大到多少。1.9 让 6 位数基本铺满原窗口宽度。</summary>
        private const double FoldedDigitScale = 1.9;

        /// <summary>折叠态下数字上移多少，抵消放大后偏下的视觉重心。</summary>
        private const double FoldedDigitOffsetY = 0;

        /// <summary>折叠时各控件向外滑出的距离，让「收起来」有方向感。</summary>
        private const double FoldedSlideOutX = 40;
        private const double FoldedSlideOutY = 30;

        /// <summary>按下时的屏幕坐标，用来区分「点击」和「拖动」。</summary>
        private Point _foldPressPoint;
        private bool _foldPressStarted;

        /// <summary>
        /// 拖动位移超过这个距离就算拖动，不算点击。
        ///
        /// 鼠标和触摸分开：手指按下去本身就会有抖动，用鼠标的 6px 会让
        /// 「想点一下」在触摸屏上大概率被判成拖动，点了没反应。
        /// Windows 自身触摸轻扫的默认阈值也在 16px 附近。
        /// </summary>
        private const double FoldClickThreshold = 6;
        private const double FoldTouchClickThreshold = 16;

        /// <summary>
        /// 本窗口用过的弹簧标量 key。
        /// SpringAnimator 的标量缓存是静态字典，窗口关了不清就一直在，
        /// 开一次计时器漏一批 —— 关窗时统一 StopScalar。
        /// </summary>
        private readonly List<object> _springScalarKeys = new List<object>();

        /// <summary>记住一个弹簧标量 key，供关窗时清理。</summary>
        private void TrackSpringKey(object key)
        {
            if (key == null || _springScalarKeys.Contains(key)) return;
            _springScalarKeys.Add(key);
        }

        /// <summary>进入倒计时状态后开始计 5 秒闲置。已在跑就不重开。</summary>
        private void StartAdjustIdleCountdown()
        {
            UpdateActivityTime();
            if (hideTimer == null) return;
            hideTimer.Stop();
            hideTimer.Start();
        }

        /// <summary>停止闲置计数（暂停、重置、完成、关闭时调用）。</summary>
        private void StopAdjustIdleCountdown()
        {
            hideTimer?.Stop();
        }

        /// <summary>折叠成「只有数字」。重复调用安全。</summary>
        private void FoldAdjustPanel()
        {
            if (_isAdjustPanelFolded) return;
            _isAdjustPanelFolded = true;

            try
            {
                // 六个 +/- 步进按钮：整组淡出，但**不摘布局**
                FoldStepButtons(true);

                // 右侧快捷时长整块：向右滑出 + 淡出
                AnimateSlideFade(QuickOptionsHost, QuickOptionsTransform,
                    FoldedSlideOutX, 0, false, SpringAnimator.Snappy);

                // 竖分隔线
                AnimateOpacity(DividerAdjustSeparator, 0, SpringAnimator.Smooth);

                // 标题栏：向上滑出
                AnimateSlideFade(TitleBar, TitleBarTransform,
                    0, -FoldedSlideOutY, false, SpringAnimator.Snappy);

                // 底部控制条：向下滑出
                AnimateSlideFade(BottomControlBar, BottomControlTransform,
                    0, FoldedSlideOutY, false, SpringAnimator.Snappy);

                // 大数字：放大 + 居中
                AnimateDigitLayout(true);

                // 冒号的像素补偿在缩放后会错位，折叠态归零
                ApplyColonOffset(true);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"折叠成纯数字失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>从「只有数字」恢复到调时间状态。重复调用安全。</summary>
        private void UnfoldAdjustPanel()
        {
            StopAdjustIdleCountdown();
            UpdateActivityTime();

            if (!_isAdjustPanelFolded) return;
            _isAdjustPanelFolded = false;

            try
            {
                FoldStepButtons(false);

                AnimateSlideFade(QuickOptionsHost, QuickOptionsTransform,
                    0, 0, true, SpringAnimator.Snappy);

                AnimateOpacity(DividerAdjustSeparator, 0.3, SpringAnimator.Smooth);

                AnimateSlideFade(TitleBar, TitleBarTransform,
                    0, 0, true, SpringAnimator.Snappy);

                AnimateSlideFade(BottomControlBar, BottomControlTransform,
                    0, 0, true, SpringAnimator.Snappy);

                AnimateDigitLayout(false);

                ApplyColonOffset(false);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"恢复调时间组件失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 大数字在两种形态之间的位置与缩放。
        ///
        /// 折叠态：放大到 FoldedDigitScale，水平居中。
        /// 展开态：原尺寸，回到原本左偏 10 的位置。
        ///
        /// 用 RenderTransform 而不是改 Width/Margin —— 改布局属性会触发父级测量，
        /// 数字会在整个动画过程中抖动，而且放大后居中的位置算不准。
        /// </summary>
        private void AnimateDigitLayout(bool folded)
        {
            if (MainDisplayGrid == null) return;

            // 瘦窗态下缩放由 AnimateDigitScale 单独控制，这里不抢
            double targetScale = folded
                ? (_isSlimMode ? CalcSlimDigitScale() : FoldedDigitScale)
                : (_isSlimMode ? CalcSlimDigitScale() : 1.0);

            // 平移量：把数字区中心对到窗口中心。
            // RenderTransformOrigin=0.5,0.5 时缩放绕自身中心，所以只需要补左偏的那部分。
            double targetX = 0;
            if (folded)
            {
                double rootWidth = 900;
                double displayWidth = MainDisplayGrid.ActualWidth > 0 ? MainDisplayGrid.ActualWidth : 500;
                double baseLeft = MainDisplayGrid.Margin.Left;
                targetX = (rootWidth - displayWidth) / 2 - baseLeft;
            }

            if (MainDisplayScale != null)
            {
                TrackSpringKey(MainDisplayScale);
                SpringAnimator.AnimateScalar(MainDisplayScale, targetScale, v =>
                {
                    try
                    {
                        double sc = Math.Max(0.5, v);
                        MainDisplayScale.ScaleX = sc;
                        MainDisplayScale.ScaleY = sc;
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"计时器缩放动画失败: {ex.Message}", LogHelper.LogType.Warning);
                    }
                }, SpringAnimator.Smooth);
            }

            if (MainDisplayTransform != null)
            {
                SpringAnimator.Animate(MainDisplayTransform,
                    (float)targetX, (float)FoldedDigitOffsetY,
                    SpringAnimator.Smooth, SpringAnimator.Smooth);
            }
        }

        /// <summary>
        /// 一个元素「滑出去 + 淡出」（或反过来）。
        /// 折叠时落定后切 Collapsed，把布局也让出来；
        /// 展开时先恢复 Visible 再动，否则没有起点。
        /// </summary>
        private void AnimateSlideFade(System.Windows.FrameworkElement element,
            TranslateTransform transform, double targetX, double targetY, bool show,
            SpringAnimator.SpringPreset preset)
        {
            if (element == null) return;

            if (show && element.Visibility != Visibility.Visible)
            {
                element.Visibility = Visibility.Visible;
            }

            if (transform != null)
            {
                SpringAnimator.Animate(transform, (float)targetX, (float)targetY, preset, preset);
            }

            AnimateOpacity(element, show ? 1 : 0, preset, () =>
            {
                if (element == null) return;
                // 只有「还是折叠态」时才允许摘出布局，避免打断动画中途的反向操作
                bool shouldHide = !show && _isAdjustPanelFolded;
                bool shouldShow = show && !_isAdjustPanelFolded;
                if (shouldHide) element.Visibility = Visibility.Collapsed;
                else if (shouldShow) element.Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// 十二个 +/- 步进按钮一起淡出/淡入。
        ///
        /// **不切 Visibility，只切 Opacity + IsHitTestVisible**：
        /// Collapsed 会让按钮退出布局，StackPanel 高度一变，
        /// 中间那排数字就会在整个折叠过程中上下抖。
        /// </summary>
        private void FoldStepButtons(bool fold)
        {
            Button[] buttons =
            {
                Digit1PlusBtn, Digit1MinusBtn, Digit2PlusBtn, Digit2MinusBtn,
                Digit3PlusBtn, Digit3MinusBtn, Digit4PlusBtn, Digit4MinusBtn,
                Digit5PlusBtn, Digit5MinusBtn, Digit6PlusBtn, Digit6MinusBtn
            };

            foreach (var btn in buttons)
            {
                if (btn == null) continue;
                btn.IsHitTestVisible = !fold;
                AnimateOpacity(btn, fold ? 0 : 1, SpringAnimator.Snappy);
            }
        }

        // ── 瘦小窗口 ─────────────────────────────────────────────
        //
        // 折叠只收起调时间控件，窗口还是 860×520，投影时会挡住课件。
        // 瘦窗是把整个窗口缩成一条：只留数字，贴在角落不碍事。
        //
        // 瘦窗和折叠是两个独立状态，但瘦窗优先：
        //   · 进瘦窗 = 先折叠（收起所有控件），再把窗口缩到 240×86
        //   · 出瘦窗 = 恢复原尺寸，再展开（回到调时间状态）
        // 所以用户点「小窗」时不需要先手动折叠。
        //
        // 窗口尺寸没法做弹簧动画（WPF 改 Window.Width/Height 是逐帧重排外框，
        // 高 DPI 下会抖且掉帧），这里直接设值，但数字的缩放仍然走弹簧，
        // 视觉上「窗口一步到位、数字弹过去」，比全程抖要好。

        /// <summary>是否处于瘦窗态。</summary>
        private bool _isSlimMode;

        /// <summary>
        /// 瘦窗尺寸：原窗口的一半。
        /// 860/2=430，520/2=260。但瘦窗里只留数字，高度按数字实际需要收紧到一半左右即可，
        /// 留 260 会有一条空白，所以高度取 260 的 0.8 倍。
        /// </summary>
        private const double SlimWindowWidth = 430;
        private const double SlimWindowHeight = 208;

        /// <summary>
        /// 瘦窗里数字区四周留的边距。铺满但不贴死，留一点呼吸。
        /// </summary>
        private const double SlimDigitPadding = 14;

        /// <summary>进瘦窗之前记住原尺寸与位置，退出时还原。</summary>
        private Rect _rectBeforeSlim;
        private bool _hasRectBeforeSlim;

        private void BtnSlimMode_Click(object sender, RoutedEventArgs e)
        {
            ToggleSlimMode();
        }

        /// <summary>切换瘦窗。重复调用安全。</summary>
        private void ToggleSlimMode()
        {
            if (_isSlimMode) ExitSlimMode();
            else EnterSlimMode();
        }

        /// <summary>进入瘦窗态：先折叠，再缩窗口，贴到屏幕右下角。</summary>
        private void EnterSlimMode()
        {
            if (_isSlimMode) return;

            try
            {
                // 记住原尺寸位置
                _rectBeforeSlim = new Rect(Left, Top, Width, Height);
                _hasRectBeforeSlim = true;

                _isSlimMode = true;

                // 先折叠（收起所有控件，只留数字）
                FoldAdjustPanel();
                StopAdjustIdleCountdown();

                // 去掉标题栏和边框：瘦窗是整个窗口直接拖动，
                // 留着标题栏既占地方又让人以为要去拖那一小条。
                iNKORE.UI.WPF.Modern.Controls.Helpers.WindowHelper.SetUseModernWindowStyle(this, false);
                WindowStyle = WindowStyle.None;

                // 缩窗口
                MinWidth = 120;
                MinHeight = 60;
                Width = SlimWindowWidth;
                Height = SlimWindowHeight;

                // 贴到屏幕上边缘居中。
                // 放上面是为了让后排同学也看得见 —— 屏幕下方通常被讲台/人挡住。
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Left + (workArea.Width - SlimWindowWidth) / 2;
                Top = workArea.Top + 8;

                // 数字缩到能塞进小窗
                AnimateDigitScale(CalcSlimDigitScale());

                LogHelper.WriteLogToFile($"计时器 | 进入瘦窗态 {SlimWindowWidth}x{SlimWindowHeight}", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"进入瘦窗态失败: {ex.Message}", LogHelper.LogType.Error);
                _isSlimMode = false;
            }
        }

        /// <summary>退出瘦窗：还原尺寸位置，展开调时间。</summary>
        private void ExitSlimMode()
        {
            if (!_isSlimMode) return;

            try
            {
                _isSlimMode = false;

                // 恢复标题栏和边框
                iNKORE.UI.WPF.Modern.Controls.Helpers.WindowHelper.SetUseModernWindowStyle(this, true);
                WindowStyle = WindowStyle.SingleBorderWindow;

                MinWidth = 220;
                MinHeight = 70;

                if (_hasRectBeforeSlim)
                {
                    Width = _rectBeforeSlim.Width;
                    Height = _rectBeforeSlim.Height;
                    Left = _rectBeforeSlim.X;
                    Top = _rectBeforeSlim.Y;
                }
                else
                {
                    Width = 860;
                    Height = 520;
                }

                UnfoldAdjustPanel();

                LogHelper.WriteLogToFile("计时器 | 退出瘦窗态", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"退出瘦窗态失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 切换冒号的上移补偿。
        ///
        /// 展开态冒号靠 Margin 的 -55 上移对齐数字中线；
        /// 折叠态整体等比缩放，-55 会被放大成 -55*scale，冒号就飘到数字上方。
        /// 折叠时归零，让缩放自己带位置；展开时还原。
        /// </summary>
        private void ApplyColonOffset(bool folded)
        {
            double top = folded ? 0 : -55;

            foreach (var name in new[] { "Colon1Display", "Colon2Display" })
            {
                if (!(FindName(name) is System.Windows.Controls.TextBlock colon)) continue;
                var m = colon.Margin;
                colon.Margin = new Thickness(m.Left, top, m.Right, m.Bottom);
            }
        }

        /// <summary>
        /// 算出瘦窗里数字要放大到多少才铺满。
        ///
        /// 判据是「铺满但不贴死」：取窗口可用区域和数字区原尺寸的比值，
        /// 再留 SlimDigitPadding 的边距。
        ///
        /// 为什么不在 XAML 里用 Viewbox 硬拉：数字区外面还套着
        /// MainViewController（Viewbox，StretchDirection=DownOnly），
        /// 它在窗口缩小后会先缩放一次，再叠一层 Viewbox 会双重缩放、算不准。
        /// 直接算倍数更可控。
        /// </summary>
        private double CalcSlimDigitScale()
        {
            try
            {
                if (MainDisplayGrid == null) return 1.0;

                // 数字区当前实际尺寸（受 Viewbox 缩放后的视觉尺寸）
                double visualW = MainDisplayGrid.ActualWidth;
                double visualH = MainDisplayGrid.ActualHeight;

                if (visualW < 1 || visualH < 1)
                {
                    // 还没布局过，用设计值兜底
                    visualW = 500;
                    visualH = 166;
                }

                // 窗口内可用区域：去掉标题栏、内边距
                double availW = Width - SlimDigitPadding * 2;
                double availH = Height - SlimDigitPadding * 2;

                if (availW < 1 || availH < 1) return 1.0;

                // 取两个方向的较小值，保证不溢出
                double scale = Math.Min(availW / visualW, availH / visualH);

                // 夹到合理范围：太小看不清，太大没意义
                return Math.Max(0.5, Math.Min(4.0, scale));
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"计算瘦窗缩放失败: {ex.Message}", LogHelper.LogType.Warning);
                return 1.0;
            }
        }

        /// <summary>只动缩放，不动位置。瘦窗进出用。</summary>
        private void AnimateDigitScale(double targetScale)
        {
            if (MainDisplayScale == null) return;

            TrackSpringKey(MainDisplayScale);
            SpringAnimator.AnimateScalar(MainDisplayScale, targetScale, v =>
            {
                try
                {
                    double sc = Math.Max(0.2, v);
                    MainDisplayScale.ScaleX = sc;
                    MainDisplayScale.ScaleY = sc;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"计时器缩放动画失败: {ex.Message}", LogHelper.LogType.Warning);
                }
            }, SpringAnimator.Smooth);
        }

        /// <summary>对元素透明度做弹簧驱动。</summary>
        private void AnimateOpacity(System.Windows.UIElement element, double target,
            SpringAnimator.SpringPreset preset, Action finished = null)
        {
            if (element == null) return;
            TrackSpringKey(element);
            SpringAnimator.AnimateScalar(element, target, v =>
            {
                try { element.Opacity = Math.Max(0, Math.Min(1, v)); }
                catch (Exception ex) { LogHelper.WriteLogToFile($"计时器透明度动画失败: {ex.Message}", LogHelper.LogType.Warning); }
            }, preset, finished);
        }

        // ── 折叠态的点击恢复 + 拖动 ───────────────────────────────
        //
        // 折叠后整窗只剩数字，用户可能想把它拖到屏幕角落挂着。
        // 所以折叠态的拖动不能再用原来「拖标题栏」那套（标题栏已经收起来了），
        // 改成整窗都能拖：按下记坐标，移动超过阈值算拖动，没超过就是点击。
        //
        // 拖动实现直接用窗口自带的 DragMove()，行为和拖标题栏一致，
        // 不自己算 Win32 SetWindowPos —— 那套在多屏 + DPI 缩放下容易偏。

        // ── 折叠态 / 瘦窗的点击与拖动 ──────────────────────────────
        //
        // 瘦窗没有标题栏，所以必须整窗可拖；折叠态也是同一套手势。
        //
        // 判据是位移阈值：按下后移动超过 FoldClickThreshold 算拖动，
        // 否则算点击。不这么分，拖动结束时必然触发一次恢复。
        //
        // 鼠标和触摸各走一套事件 —— 触摸屏不会产生 MouseMove，
        // 只接鼠标的话老师在白板上根本拖不动。

        private void MainBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isAdjustPanelFolded && !_isSlimMode) return;
            _foldPressPoint = e.GetPosition(this);
            _foldPressStarted = true;
        }

        private void MainBorder_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_foldPressStarted) return;
            if (!_isAdjustPanelFolded && !_isSlimMode) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;

            TryStartDrag(e.GetPosition(this), FoldClickThreshold);
        }

        private void MainBorder_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_foldPressStarted) return;
            _foldPressStarted = false;
            HandleTap();
        }

        // ── 触摸路径 ──

        private void MainBorder_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            if (!_isAdjustPanelFolded && !_isSlimMode) return;
            _foldPressPoint = e.GetTouchPoint(this).Position;
            _foldPressStarted = true;
        }

        private void MainBorder_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            if (!_foldPressStarted) return;
            if (!_isAdjustPanelFolded && !_isSlimMode) return;

            if (TryStartDrag(e.GetTouchPoint(this).Position, FoldTouchClickThreshold))
            {
                e.Handled = true;
            }
        }

        private void MainBorder_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            if (!_foldPressStarted) return;
            _foldPressStarted = false;
            HandleTap();
        }

        /// <summary>
        /// 位移超过阈值就进入拖动。返回是否已开始拖动。
        /// DragMove 是阻塞的 —— 它会一直跑到鼠标松开，所以返回后手势已经结束。
        /// </summary>
        private bool TryStartDrag(Point now, double threshold)
        {
            if (Math.Abs(now.X - _foldPressPoint.X) < threshold &&
                Math.Abs(now.Y - _foldPressPoint.Y) < threshold)
            {
                return false;
            }

            // 先清标记：DragMove 阻塞期间不会再进这里
            _foldPressStarted = false;

            try
            {
                DragMove();
                return true;
            }
            catch (Exception ex)
            {
                // DragMove 在没有按下鼠标时调用会抛 InvalidOperationException，
                // 属于「手势已经结束」的正常情况，记 Warning 就够。
                LogHelper.WriteLogToFile($"计时器拖动窗口失败: {ex.Message}", LogHelper.LogType.Warning);
                return false;
            }
        }

        /// <summary>没拖动过就是一次点击：瘦窗回大窗，折叠态回调时间。</summary>
        private void HandleTap()
        {
            if (_isSlimMode)
            {
                ExitSlimMode();
                return;
            }

            if (_isAdjustPanelFolded)
            {
                UnfoldAdjustPanel();
            }
        }
    }
}

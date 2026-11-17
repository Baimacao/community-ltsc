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
    public partial class NewStyleTimerWindow : Window
    {
    // LTSC（2026-11-17）：从 NewStyleTimerWindow.xaml.cs 拆出。
    //
    // 这一段是「最近用过的计时时长」的存取：套用、保存、读回、刷新显示。
    // 纯存档逻辑，不碰计时器状态机，也不碰折叠动画。

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
    }
}

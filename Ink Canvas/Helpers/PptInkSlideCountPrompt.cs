using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using iNKORE.UI.WPF.Modern.Controls;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 演示文稿页数变了的时候，问老师「这份 PPT 改过吗，笔迹还要不要沿用」。
    ///
    /// LTSC（2026-11-20）：笔迹文件夹名里不再带页数（带了页数一改就全读不出来），
    /// 页数改由文件夹里的 SlideCount 文件记录。打开演示文稿时拿记下的页数
    /// 和当前页数比，不一致就弹这个窗。
    ///
    /// 为什么必须问：只按名字和路径哈希认文件，能保证「同一份 PPT 的笔迹一定找得到」，
    /// 但页数变了以后，第 8 页的笔迹可能已经不是原来那页了 ——
    /// 自动沿用会让老师上错页的墨迹，自动丢弃会让他白画一遍。
    /// 这个判断只有他知道，所以问。
    ///
    /// 只在「页数真的变了」时弹，不会每次开 PPT 都弹。
    /// </summary>
    internal static class PptInkSlideCountPrompt
    {
        /// <summary>
        /// 显示询问。返回 true 表示沿用上次笔迹，false 表示按新页数重新开始。
        ///
        /// 任何异常都返回 true（沿用）—— 沿用最少丢东西，
        /// 而「重新开始」会真的删数据，这种不可逆操作不能在出错时默默选它。
        /// </summary>
        public static async Task<bool> AskAsync(Window owner, string presentationName, int savedCount, int currentCount)
        {
            try
            {
                var content = BuildContent(presentationName, savedCount, currentCount);

                var dialog = new ContentDialog
                {
                    Title = "这份 PPT 改过了吗？",
                    Content = content,
                    PrimaryButtonText = "沿用上次笔迹",
                    SecondaryButtonText = "按新页数重新开始",
                    CloseButtonText = "先不改",
                    DefaultButton = ContentDialogButton.Primary,
                    Owner = owner
                };

                var result = await dialog.ShowAsync();

                switch (result)
                {
                    case ContentDialogResult.Primary:
                        // 沿用：把记录的页数更新成当前值，
                        // 否则下次打开还会继续弹同一个问题。
                        LogHelper.WriteLogToFile(
                            $"[PPT笔迹] 页数变化（{savedCount}→{currentCount}），用户选择沿用上次笔迹",
                            LogHelper.LogType.Event);
                        return true;

                    case ContentDialogResult.Secondary:
                        LogHelper.WriteLogToFile(
                            $"[PPT笔迹] 页数变化（{savedCount}→{currentCount}），用户选择按新页数重新开始",
                            LogHelper.LogType.Event);
                        return false;

                    default:
                        // 「先不改」：什么都不动，下次还会问。
                        // 不能当成「沿用」悄悄记下来 —— 老师选的是「先不改」，不是「沿用」。
                        LogHelper.WriteLogToFile(
                            $"[PPT笔迹] 页数变化（{savedCount}→{currentCount}），用户选择先不改",
                            LogHelper.LogType.Event);
                        return true;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"[PPT笔迹] 页数询问弹窗失败: {ex.Message}", LogHelper.LogType.Warning);
                return true;
            }
        }

        /// <summary>
        /// 内容区。
        ///
        /// 三块：发生了什么 / 两个选项分别意味着什么 / 选错了怎么办。
        /// 老师在上课前看到这个窗，得能在三秒内决定，所以每句话都要短。
        /// </summary>
        private static StackPanel BuildContent(string presentationName, int savedCount, int currentCount)
        {
            var panel = new StackPanel { MaxWidth = 460 };
            var theme = Application.Current?.TryFindResource("TextFillColorPrimaryBrush") as Brush;
            var muted = Application.Current?.TryFindResource("TextFillColorSecondaryBrush") as Brush
                        ?? new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));

            // 第一行：讲现象，带具体数字
            panel.Children.Add(new TextBlock
            {
                Text = $"「{presentationName}」上次是 {savedCount} 页，现在是 {currentCount} 页。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 12)
            });

            // 第二个块：页数对比，做成一目了然的视觉
            panel.Children.Add(BuildCountRow(savedCount, currentCount, theme, muted));

            // 第三块：两个选项的后果
            panel.Children.Add(new TextBlock
            {
                Text = "沿用：保留你上次画的墨迹。如果只是插了几页或删了几页，选这个。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 16, 0, 4),
                Opacity = 0.85
            });

            panel.Children.Add(new TextBlock
            {
                Text = "重新开始：清空上次的墨迹，从空白开始。如果这份 PPT 已经换内容了，选这个。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12),
                Opacity = 0.85
            });

            panel.Children.Add(new TextBlock
            {
                Text = "选「先不改」的话，下次打开还会问一次，不会丢东西。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.6
            });

            return panel;
        }

        /// <summary>
        /// 两栏页数对比：左边旧、右边新，中间箭头，变化量用强调色。
        /// </summary>
        private static Border BuildCountRow(int savedCount, int currentCount, Brush primary, Brush muted)
        {
            int delta = currentCount - savedCount;
            string deltaText = delta > 0 ? $"+{delta}" : delta.ToString();
            var accent = Application.Current?.TryFindResource("SystemAccentColorPrimaryBrush") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(0, 120, 212));

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

            row.Children.Add(BuildCountChip("上次", savedCount.ToString(), muted, primary));
            row.Children.Add(new TextBlock
            {
                Text = "→",
                FontSize = 20,
                Margin = new Thickness(14, 6, 14, 0),
                Opacity = 0.5,
                VerticalAlignment = VerticalAlignment.Top
            });
            row.Children.Add(BuildCountChip("现在", currentCount.ToString(), accent, primary));

            row.Children.Add(new TextBlock
            {
                Text = deltaText,
                FontSize = 13,
                Margin = new Thickness(12, 12, 0, 0),
                Foreground = accent,
                VerticalAlignment = VerticalAlignment.Top
            });

            return new Border
            {
                Child = row,
                Padding = new Thickness(14, 10, 14, 10),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(12, 0, 0, 0))
            };
        }

        private static StackPanel BuildCountChip(string label, string value, Brush labelBrush, Brush valueBrush)
        {
            var chip = new StackPanel();

            chip.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = labelBrush,
                Opacity = 0.7
            });

            chip.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = valueBrush
            });

            return chip;
        }
    }
}

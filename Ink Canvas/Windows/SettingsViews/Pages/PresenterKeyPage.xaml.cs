using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ink_Canvas.Helpers;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    /// <summary>
    /// 翻页笔适配设置页。
    ///
    /// 三件事：
    ///   1. 总开关（开 = 配对功能正常工作；关 = 只拦截不响应）
    ///   2. 按键配对表（可录入、可恢复默认）
    ///   3. 冲突检测（同一个键被配给两个功能时拒绝保存）
    ///
    /// 录入方式沿用 Windows/HotkeyItem.xaml.cs 那套：捕获 KeyDown、过滤纯修饰键。
    /// </summary>
    public partial class PresenterKeyPage : Page
    {
        /// <summary>界面绑定的行数据（包装 Binding，让 KeyText 变化能通知 UI）。</summary>
        private sealed class Row : System.ComponentModel.INotifyPropertyChanged
        {
            public PresenterKeyAdapter.Binding Source { get; }

            public Row(PresenterKeyAdapter.Binding source) => Source = source;

            public string DisplayName => Source.DisplayName;
            public string Hint => Source.Hint;
            public string KeyText => Source.KeyText;

            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

            public void NotifyKeyChanged()
            {
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(KeyText)));
            }
        }

        private readonly ObservableCollection<Row> _rows = new ObservableCollection<Row>();

        /// <summary>正在录入的那一行。null = 没在录入。</summary>
        private Row _capturing;

        public PresenterKeyPage()
        {
            InitializeComponent();

            foreach (var b in PresenterKeyAdapter.Bindings)
                _rows.Add(new Row(b));

            BindingsItemsControl.ItemsSource = _rows;

            Loaded += (_, __) =>
            {
                ToggleEnableAdapter.IsOn = PresenterKeyAdapter.IsEnabled;
            };

            // 录入期间在这里收键。挂到 Page 上而不是按钮上：
            // 按钮点击后焦点会跑，挂在按钮上收不到后续按键。
            PreviewKeyDown += Page_PreviewKeyDown;
        }

        private void ToggleEnableAdapter_Toggled(object sender, RoutedEventArgs e)
        {
            if (ToggleEnableAdapter.IsOn)
                PresenterKeyAdapter.EnableAdapter();
            else
                PresenterKeyAdapter.DisableAdapter();
        }

        private void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Row row) return;

            _capturing = row;
            btn.Content = "请按键...";
            HideConflict();

            Focus();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Row row) return;

            row.Source.ResetToDefault();
            row.NotifyKeyChanged();

            if (HasConflict(out var conflict))
            {
                ShowConflict(conflict);
            }
            else
            {
                HideConflict();
            }
        }

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_capturing == null) return;

            // 纯修饰键不算，等真正的键
            if (e.Key is Key.LeftShift or Key.RightShift
                or Key.LeftCtrl or Key.RightCtrl
                or Key.LeftAlt or Key.RightAlt
                or Key.LWin or Key.RWin)
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;

            var modifiers = ModifierKeys.None;
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) modifiers |= ModifierKeys.Control;
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) modifiers |= ModifierKeys.Shift;
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) modifiers |= ModifierKeys.Alt;
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) modifiers |= ModifierKeys.Windows;

            var target = _capturing;
            _capturing = null;

            target.Source.Key = e.Key;
            target.Source.Modifiers = modifiers;
            target.NotifyKeyChanged();

            // 冲突检测（Q4-8）：同一个键被配给两个功能时提示并回滚
            if (HasConflict(out var conflict))
            {
                ShowConflict(conflict);
                target.Source.ResetToDefault();
                target.NotifyKeyChanged();
            }
            else
            {
                HideConflict();
            }
        }

        /// <summary>检查当前配对里有没有重复的键。</summary>
        private bool HasConflict(out string message)
        {
            message = null;

            for (int i = 0; i < PresenterKeyAdapter.Bindings.Count; i++)
            {
                for (int j = i + 1; j < PresenterKeyAdapter.Bindings.Count; j++)
                {
                    var a = PresenterKeyAdapter.Bindings[i];
                    var b = PresenterKeyAdapter.Bindings[j];

                    if (a.Key == b.Key && a.Modifiers == b.Modifiers)
                    {
                        message = $"「{a.KeyText}」同时配给了「{a.DisplayName}」和「{b.DisplayName}」。" +
                                  "同一个键不能对应两个功能，已经改回默认值。";
                        return true;
                    }
                }
            }

            return false;
        }

        private void ShowConflict(string message)
        {
            TextBlockConflict.Text = message;
            BorderConflict.Visibility = Visibility.Visible;
        }

        private void HideConflict()
        {
            BorderConflict.Visibility = Visibility.Collapsed;
        }
    }
}
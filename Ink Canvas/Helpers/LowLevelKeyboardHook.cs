using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 低层键盘钩子（WH_KEYBOARD_LL）。
    ///
    /// 用途：翻页笔适配。翻页笔的特殊键会触发快捷键，需要在 PPT 联动模式下
    /// **消费掉**这些键，不让它们传给系统/PPT。
    ///
    /// 为什么用 WH_KEYBOARD_LL 而不是 RawInput：
    /// RawInput 只能读取键盘事件，没法阻止它继续传递。要「拦截」必须用钩子。
    ///
    /// 安全约束（非常重要）：
    ///   1. 只在 PPT 联动模式下挂载；其余时间必须完全卸载。
    ///   2. 实现 IDisposable，任何异常路径都要能卸载。
    ///   3. 绝不出现「ICC 退出了还在拦键盘」——那会让老师的整个系统键盘失灵。
    /// </summary>
    internal sealed class LowLevelKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private IntPtr _hookId = IntPtr.Zero;
        // 必须用字段持有委托，否则 GC 会回收它 → 回调时崩溃
        private readonly LowLevelKeyboardProc _proc;
        private bool _disposed;

        /// <summary>
        /// 按键到达时调用。返回 true 表示「吃掉这个键」，不再传给系统。
        /// 
        /// 注意：这个回调在**钩子线程**上执行，不是 UI 线程。
        /// 里面只能做判断，不能直接操作 WPF 控件 ——
        /// 需要动界面时用 Dispatcher.BeginInvoke 排回去。
        /// </summary>
        public Func<Key, ModifierKeys, bool> ShouldSwallow { get; set; }

        /// <summary>钩子是否已挂上。</summary>
        public bool IsHooked => _hookId != IntPtr.Zero;

        public LowLevelKeyboardHook()
        {
            _proc = HookCallback;
        }

        /// <summary>挂上钩子。</summary>
        public bool Install()
        {
            if (_disposed) return false;
            if (_hookId != IntPtr.Zero) return true;

            try
            {
                using (var currentProcess = Process.GetCurrentProcess())
                using (var currentModule = currentProcess.MainModule)
                {
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
                        GetModuleHandle(currentModule?.ModuleName), 0);
                }

                if (_hookId == IntPtr.Zero)
                {
                    LogHelper.WriteLogToFile(
                        $"键盘钩子挂载失败（Win32 错误 {Marshal.GetLastWin32Error()}）",
                        LogHelper.LogType.Warning);
                    return false;
                }

                LogHelper.WriteLogToFile("键盘钩子已挂载", LogHelper.LogType.Event);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"键盘钩子挂载异常: {ex.Message}", LogHelper.LogType.Error);
                return false;
            }
        }

        /// <summary>卸载钩子。必须在退出 PPT 联动 / 程序关闭时调用。</summary>
        public void Uninstall()
        {
            if (_hookId == IntPtr.Zero) return;

            try
            {
                UnhookWindowsHookEx(_hookId);
                LogHelper.WriteLogToFile("键盘钩子已卸载", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"键盘钩子卸载异常: {ex.Message}", LogHelper.LogType.Error);
            }
            finally
            {
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            try
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    var key = KeyInterop.KeyFromVirtualKey((int)data.vkCode);

                    // 修饰键状态从当前键盘读。钩子里拿不到 WPF 的 Keyboard.Modifiers
                    // （那是 UI 线程的状态），所以直接查键表。
                    var modifiers = GetCurrentModifiers();

                    if (ShouldSwallow != null && ShouldSwallow(key, modifiers))
                    {
                        // 返回非零 = 吃掉这个键，不往下传
                        return new IntPtr(1);
                    }
                }
            }
            catch (Exception ex)
            {
                // 钩子里绝不能抛异常 —— 会让整个键盘输入卡住
                LogHelper.WriteLogToFile($"键盘钩子回调异常: {ex.Message}", LogHelper.LogType.Error);
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static ModifierKeys GetCurrentModifiers()
        {
            var mods = ModifierKeys.None;
            if (IsKeyDown(0x11)) mods |= ModifierKeys.Control;   // VK_CONTROL
            if (IsKeyDown(0x10)) mods |= ModifierKeys.Shift;     // VK_SHIFT
            if (IsKeyDown(0x12)) mods |= ModifierKeys.Alt;       // VK_MENU
            if (IsKeyDown(0x5B) || IsKeyDown(0x5C)) mods |= ModifierKeys.Windows;
            return mods;
        }

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private static bool IsKeyDown(int vk)
        {
            return (GetKeyState(vk) & 0x8000) != 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Uninstall();
        }
    }
}
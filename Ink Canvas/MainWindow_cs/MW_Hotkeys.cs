using System;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        /// <summary>
        /// 鼠标滚轮事件处理，用于PPT翻页
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标滚轮事件参数</param>
        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!IsInPPTPresentationMode || currentMode != 0) return;
            if (e.Delta >= 120)
            {
                BtnPPTSlidesUp_Click(null, null);
            }
            else if (e.Delta <= -120)
            {
                BtnPPTSlidesDown_Click(null, null);
            }
        }

        /// <summary>
        /// 键盘按键预览事件处理，用于PPT翻页
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">键盘事件参数</param>
        private void Main_Grid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsInPPTPresentationMode || currentMode != 0) return;

            if (e.Key == Key.Down || e.Key == Key.PageDown || e.Key == Key.Right || e.Key == Key.N || e.Key == Key.Space)
            {
                BtnPPTSlidesDown_Click(null, null);
            }
            if (e.Key == Key.Up || e.Key == Key.PageUp || e.Key == Key.Left || e.Key == Key.P)
            {
                BtnPPTSlidesUp_Click(null, null);
            }
        }


        /// <summary>
        /// 撤销操作热键处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">执行路由事件参数</param>
        private void HotKey_Undo(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                SymbolIconUndo_MouseUp(lastBorderMouseDownObject, null);
            }
            catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
        }

        /// <summary>
        /// 重做操作热键处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">执行路由事件参数</param>
        private void HotKey_Redo(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                SymbolIconRedo_MouseUp(lastBorderMouseDownObject, null);
            }
            catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
        }

        /// <summary>
        /// 清空画布热键处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">执行路由事件参数</param>
        private void HotKey_Clear(object sender, ExecutedRoutedEventArgs e)
        {
            SymbolIconDelete_MouseUp(lastBorderMouseDownObject, null);
        }


        /// <summary>
        /// 退出PPT放映热键处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">执行路由事件参数</param>
        internal async void KeyExit(object sender, ExecutedRoutedEventArgs e)
        {
            if (currentMode != 0)
            {
                ImageBlackboard_MouseUp(lastBorderMouseDownObject, null);
                return;
            }

            if (IsInPPTPresentationMode) await ExitPPTPresentation();
        }

        /// <summary>
        /// 切换到绘图工具热键处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">执行路由事件参数</param>
        private async void KeyChangeToDrawTool(object sender, ExecutedRoutedEventArgs e)
        {
            if (isFloatingBarFolded)
            {
                await UnFoldFloatingBar(new object());
            }
            PenIcon_Click(lastBorderMouseDownObject, null);
        }

        /// <summary>
        /// 退出绘图工具热键处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">执行路由事件参数</param>
        /// <remarks>
        /// 在白板模式下，alt+q 退出白板模式
        /// 在非白板模式下，alt+q 切换到鼠标模式
        /// </remarks>
        internal void KeyChangeToQuitDrawTool(object sender, ExecutedRoutedEventArgs e)
        {
            if (currentMode != 0)
            {
                // 在白板模式下，alt+q 退出白板模式
                ImageBlackboard_MouseUp(lastBorderMouseDownObject, null);
            }
            else
            {
                // 在非白板模式下，alt+q 切换到鼠标模式
                CursorIcon_Click(lastBorderMouseDownObject, null);
            }
        }

        /// <summary>
        /// 切换到选择工具热键处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">执行路由事件参数</param>
        /// <remarks>仅当画布控件面板可见时生效</remarks>
        private void KeyChangeToSelect(object sender, ExecutedRoutedEventArgs e)
        {
            if (!IsAnnotating)
            {
                PenIcon_Click(lastBorderMouseDownObject, null);
            }
            SymbolIconSelect_MouseUp(lastBorderMouseDownObject, null);
        }

        /// <summary>
        /// 切换到橡皮擦工具热键处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">执行路由事件参数</param>
        /// <remarks>仅当画布控件面板可见时生效，根据当前橡皮擦状态选择相应的橡皮擦模式</remarks>
        private async void KeyChangeToEraser(object sender, ExecutedRoutedEventArgs e)
        {
            if (isFloatingBarFolded)
            {
                await UnFoldFloatingBar(new object());
            }

            if (!IsAnnotating)
            {
                PenIcon_Click(lastBorderMouseDownObject, null);
            }

            if (Eraser_Icon.Background != null)
                EraserIconByStrokes_Click(lastBorderMouseDownObject, null);
            else
                EraserIcon_Click(lastBorderMouseDownObject, null);
        }

        /// <summary>
        /// 切换到白板模式热键处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">执行路由事件参数</param>
        private void KeyChangeToBoard(object sender, ExecutedRoutedEventArgs e)
        {
            ImageBlackboard_MouseUp(lastBorderMouseDownObject, null);
        }

        /// <summary>
        /// 屏幕截图热键处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">执行路由事件参数</param>
        private void KeyCapture(object sender, ExecutedRoutedEventArgs e)
        {
            SaveScreenShotToDesktop();
        }

        /// <summary>
        /// 绘制直线热键处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">执行路由事件参数</param>
        /// <remarks>仅当画布控件面板可见时生效</remarks>
        private void KeyDrawLine(object sender, ExecutedRoutedEventArgs e)
        {
            DrawLineFromHotkey();
        }

        internal async void DrawLineFromHotkey()
        {
            if (isFloatingBarFolded)
            {
                await UnFoldFloatingBar(new object());
            }

            if (!IsAnnotating)
            {
                PenIcon_Click(lastBorderMouseDownObject, null);
            }

            BtnDrawLine_Click(lastMouseDownSender, null);
        }

        internal async void SwitchToEraserFromHotkey()
        {
            if (isFloatingBarFolded)
            {
                await UnFoldFloatingBar(new object());
            }

            if (!IsAnnotating)
            {
                PenIcon_Click(lastBorderMouseDownObject, null);
            }

            EraserIcon_Click(lastBorderMouseDownObject, null);
        }

        /// <summary>
        /// LTSC P2（2026-11-12）：画笔快捷键。
        /// </summary>
        /// <remarks>
        /// 原来 DrawTool 直接绑 PenIcon_Click。那个方法一个入口干两件事：
        ///   - 没进批注模式时：切到画笔、进入批注模式（分支 A）
        ///   - 已在批注模式时：开合笔调色板（分支 B，3594-3612 行）
        /// 于是「按一次进批注、按第二次弹出笔菜单」，跟快捷键该有的行为不一样。
        ///
        /// 这里照 SwitchToSelectFromHotkey / SwitchToEraserFromHotkey 的写法：
        /// 只在还没进批注时调 PenIcon_Click 进入模式，之后只做「切到画笔」，
        /// 不碰调色板。
        /// </remarks>
        internal async void SwitchToPenFromHotkey()
        {
            if (isFloatingBarFolded)
            {
                await UnFoldFloatingBar(new object());
            }

            if (!IsAnnotating)
            {
                // 未进批注：走 PenIcon_Click 的进入分支（分支 A）
                PenIcon_Click(lastBorderMouseDownObject, null);
                return;
            }

            // 已在批注：只保证当前工具是画笔，不开合调色板。
            if (drawingShapeMode != 0 && !isLongPressSelected)
            {
                return;
            }

            if (currentSelectedElement != null)
            {
                UnselectElement(currentSelectedElement);
                currentSelectedElement = null;
            }
            DisableEraserOverlay();
            StopEraserAutoSwitchBackTimer();

            SetCurrentToolMode(InkCanvasEditingMode.Ink);
            UpdateCurrentToolMode("pen");
            SetFloatingBarHighlightPosition("pen");

            forceEraser = false;
            forcePointEraser = false;
            drawingShapeMode = 0;

            if (!drawingAttributes.IsHighlighter && penType != 2)
            {
                penType = 0;
                drawingAttributes.IsHighlighter = false;
                drawingAttributes.StylusTip = StylusTip.Ellipse;
            }

            HideSubPanels("pen", true);
        }

        internal async void SwitchToSelectFromHotkey()
        {
            if (isFloatingBarFolded)
            {
                await UnFoldFloatingBar(new object());
            }

            if (!IsAnnotating)
            {
                PenIcon_Click(lastBorderMouseDownObject, null);
            }

            SymbolIconSelect_MouseUp(lastBorderMouseDownObject, null);
        }

        /// <summary>
        /// 隐藏工具栏热键处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">执行路由事件参数</param>
        private void KeyHide(object sender, ExecutedRoutedEventArgs e)
        {
            SymbolIconEmoji_MouseUp(null, null);
        }
    }
}

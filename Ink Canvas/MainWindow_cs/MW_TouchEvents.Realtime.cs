using Ink_Canvas.Controls;
using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        // LTSC（2026-11-17）：从 MW_TouchEvents.cs 拆出。
        //
        // 这一整段是实时笔刷渲染：触摸点的插值、压感换算、
        // StrokeVisual 的增删与管理。它服务的是「手指划过去墨迹怎么长出来」，
        // 和下面处理触摸事件本身的逻辑是两件事。
        private static long RealtimeNowMs() => Environment.TickCount64;

        private static float RealtimeClamp(float x, float min, float max)
        {
            if (x < min) return min;
            if (x > max) return max;
            return x;
        }

        private static float WidthToPressure(float width, float baseWidth)
        {
            if (baseWidth <= 1e-4f) return 0.5f;
            var scale = width / baseWidth;
            return RealtimeClamp((scale - 0.42f) / 1.16f, 0.08f, 1f);
        }

        private bool ShouldUseRealtimeVelocityBrushTip()
        {
            return Settings.Canvas.InkStyle == 3
                && Settings.Canvas.VelocityBrushTipMix > 0
                && !Settings.Canvas.DisablePressure;
        }

        /// <summary>
        /// P2：清掉收不到 TouchUp 的僵尸触摸 id。
        /// </summary>
        /// <remarks>
        /// 真机实测：dec 峰值涨到 9（正常最多等于手指数），
        /// 「静默释放」计数器 8 分钟涨到 50。那些 id 再也收不到 TouchUp，
        /// 一直留在 dec 里，把双指手势的判定搅乱。
        /// 根因已经在 TouchUp 里改成只释放当前那一根，这里再兜一层：
        /// 任何超过 StaleTouchSeconds 没动静的 id 一律清掉。
        /// </remarks>
        private void PruneStaleTouchIds()
        {
            if (_touchIdStamp.Count == 0) return;
            var now = DateTime.UtcNow;
            var stale = new List<int>();
            foreach (var kv in _touchIdStamp)
            {
                if ((now - kv.Value).TotalSeconds > StaleTouchSeconds) stale.Add(kv.Key);
            }
            foreach (var id in stale)
            {
                _touchIdStamp.Remove(id);
                dec.Remove(id);
            }
        }

        private bool ShouldUseRealtimeVelocityBrushTipForTouch()
        {
            return Settings.Canvas.InkStyle == 3
                && Settings.Canvas.VelocityBrushTipMix > 0
                && !Settings.Canvas.DisablePressure
                && drawingShapeMode == 0
                && !isPalmEraserActive;
        }

        internal bool ShouldUseRealtimeVelocityBrushTipForMouse()
        {
            return ShouldUseRealtimeVelocityBrushTip()
                   && drawingShapeMode == 0
                   && !isPalmEraserActive;
        }

        private static bool IsTouchStylusDevice(StylusDevice stylusDevice)
        {
            return stylusDevice?.TabletDevice?.Type == TabletDeviceType.Touch;
        }

        private void BeginTouchInkInput()
        {
            if (!_hasStoredInkCanvasManipulationStateForTouchInk)
            {
                _isInkCanvasManipulationEnabledBeforeTouchInk = inkCanvas.IsManipulationEnabled;
                _hasStoredInkCanvasManipulationStateForTouchInk = true;
            }
            inkCanvas.IsManipulationEnabled = false;
        }

        private void EndTouchInkInputIfIdle()
        {
            if (_activeRealtimeTouchStrokeIds.Count != 0 || _activeRealtimeStylusStrokeIds.Count != 0 || _activeTouchStrokeIds.Count != 0)
                return;
            if (!_hasStoredInkCanvasManipulationStateForTouchInk)
                return;
            inkCanvas.IsManipulationEnabled = _isInkCanvasManipulationEnabledBeforeTouchInk;
            _hasStoredInkCanvasManipulationStateForTouchInk = false;
        }

        internal void EnsureRealtimeStylusPipelineBinding()
        {
            if (inkCanvas == null) return;

            inkCanvas.StylusDown -= MainWindow_StylusDown;
            inkCanvas.StylusMove -= MainWindow_StylusMove;
            inkCanvas.StylusUp -= MainWindow_StylusUp;

            inkCanvas.StylusDown += MainWindow_StylusDown;
            inkCanvas.StylusMove += MainWindow_StylusMove;
            inkCanvas.StylusUp += MainWindow_StylusUp;

            if (ShouldUseRealtimeVelocityBrushTip()
                && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByPoint
                && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByStroke
                && inkCanvas.EditingMode != InkCanvasEditingMode.Select)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
            }
            else if (!ShouldUseRealtimeVelocityBrushTip()
                     && inkCanvas.EditingMode == InkCanvasEditingMode.None)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            }

            SetDynamicRendererEnabled(inkCanvas, inkCanvas.EditingMode == InkCanvasEditingMode.Ink);
        }

        private void InitializeRealtimeBrushTipState(int stylusId, StylusDownEventArgs e)
        {
            if (!ShouldUseRealtimeVelocityBrushTip())
            {
                _realtimeBrushTipStates.Remove(stylusId);
                return;
            }

            var startPoint = e.GetPosition(inkCanvas);
            _realtimeBrushTipStates[stylusId] = new RealtimeBrushTipState
            {
                LastRawX = (float)startPoint.X,
                LastRawY = (float)startPoint.Y,
                LastTimestampMs = RealtimeNowMs(),
                HasTouchPoint = true,
                LastTouchPoint = startPoint
            };
        }

        private void InitializeRealtimeBrushTipStateFromPoint(int strokeId, Point startPoint)
        {
            if (!ShouldUseRealtimeVelocityBrushTipForTouch() && strokeId != MouseRealtimeStrokeId)
            {
                _realtimeBrushTipStates.Remove(strokeId);
                return;
            }
            if (!ShouldUseRealtimeVelocityBrushTipForMouse() && strokeId == MouseRealtimeStrokeId)
            {
                _realtimeBrushTipStates.Remove(strokeId);
                return;
            }

            _realtimeBrushTipStates[strokeId] = new RealtimeBrushTipState
            {
                LastRawX = (float)startPoint.X,
                LastRawY = (float)startPoint.Y,
                LastTimestampMs = RealtimeNowMs(),
                HasTouchPoint = true,
                LastTouchPoint = startPoint
            };
        }

        private void CleanupRealtimeBrushTipState(int stylusId)
        {
            _realtimeBrushTipStates.Remove(stylusId);
        }

        private float GetRealtimeBrushTipMinDistance(float smoothedSampleRateHz)
        {
            var baseMinDist = smoothedSampleRateHz > 160f ? 0.35f
                : smoothedSampleRateHz > 90f ? 0.25f
                : 0.15f;
            var scale = RealtimeClamp((float)Settings.Canvas.RealtimeBrushTipMinDistanceScale, 0f, 2f);
            return baseMinDist * scale;
        }

        private static IEnumerable<Point> InterpolateTouchPoints(Point from, Point to)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var steps = Math.Min(24, Math.Max(1, (int)Math.Ceiling(distance / 1.2)));
            for (var i = 1; i <= steps; i++)
            {
                var t = (double)i / steps;
                yield return new Point(from.X + dx * t, from.Y + dy * t);
            }
        }

        private static IEnumerable<Point> InterpolateTouchPoints(RealtimeBrushTipState state, Point to)
        {
            if (!state.HasTouchDirection)
            {
                foreach (var p in InterpolateTouchPoints(state.LastTouchPoint, to))
                    yield return p;
                yield break;
            }

            var from = state.LastTouchPoint;
            var chord = to - from;
            var distance = chord.Length;
            if (distance < 0.1)
                yield break;

            var incoming = state.LastTouchDirection;
            if (incoming.LengthSquared > 0.0001)
            {
                incoming.Normalize();
                var current = chord;
                current.Normalize();
                var dot = Math.Max(-1, Math.Min(1, incoming.X * current.X + incoming.Y * current.Y));
                var angle = Math.Acos(dot);
                if (angle < Math.PI * 0.72)
                {
                    var tangentLength = Math.Min(distance * 0.45, 18);
                    var c1 = from + incoming * tangentLength;
                    var c2 = to - current * tangentLength;
                    var steps = Math.Min(24, Math.Max(1, (int)Math.Ceiling(distance / 1.2)));
                    for (var i = 1; i <= steps; i++)
                    {
                        var t = (double)i / steps;
                        var u = 1 - t;
                        yield return new Point(
                            u * u * u * from.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * to.X,
                            u * u * u * from.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * to.Y);
                    }
                    yield break;
                }
            }

            foreach (var p in InterpolateTouchPoints(from, to))
                yield return p;
        }

        private static void UpdateTouchInterpolationState(RealtimeBrushTipState state, Point point)
        {
            var delta = point - state.LastTouchPoint;
            if (delta.LengthSquared > 0.0001)
            {
                state.PreviousTouchPoint = state.LastTouchPoint;
                state.LastTouchDirection = delta;
                state.HasTouchDirection = true;
            }
            state.LastTouchPoint = point;
        }

        private void AppendInterpolatedTouchPoints(StrokeVisual strokeVisual, int strokeId, Point point)
        {
            if (strokeVisual == null) return;
            if (!_realtimeBrushTipStates.TryGetValue(strokeId, out var state))
            {
                state = new RealtimeBrushTipState { HasTouchPoint = true, LastTouchPoint = point };
                _realtimeBrushTipStates[strokeId] = state;
                strokeVisual.Add(new StylusPoint(point.X, point.Y, 0.5f));
                return;
            }

            if (!state.HasTouchPoint)
            {
                state.HasTouchPoint = true;
                state.LastTouchPoint = point;
                strokeVisual.Add(new StylusPoint(point.X, point.Y, 0.5f));
                return;
            }

            foreach (var p in InterpolateTouchPoints(state, point))
            {
                strokeVisual.Add(new StylusPoint(p.X, p.Y, 0.5f));
            }
            UpdateTouchInterpolationState(state, point);
        }

        private bool TryAppendRealtimeVelocityBrushTipPoints(StrokeVisual strokeVisual, StylusEventArgs e)
        {
            if (!ShouldUseRealtimeVelocityBrushTip() || strokeVisual == null || e?.StylusDevice == null)
                return false;

            if (!_realtimeBrushTipStates.TryGetValue(e.StylusDevice.Id, out var state))
                return false;

            var stylusPointCollection = e.GetStylusPoints(this);
            if (stylusPointCollection == null || stylusPointCollection.Count == 0)
                return true;

            var mix = RealtimeClamp((float)Settings.Canvas.VelocityBrushTipMix, 0f, 1f);
            var appended = false;
            var baseWidth = (float)Math.Max(0.35,
                strokeVisual.Stroke?.DrawingAttributes?.Width ?? inkCanvas.DefaultDrawingAttributes.Width);

            foreach (StylusPoint rawPoint in stylusPointCollection)
            {
                var nowMs = RealtimeNowMs();
                var dtMs = Math.Max(1L, nowMs - state.LastTimestampMs);
                var dt = dtMs / 1000f;
                var sampleRate = 1f / Math.Max(1e-4f, dt);
                state.SmoothedSampleRateHz = state.SmoothedSampleRateHz * 0.85f + sampleRate * 0.15f;

                var rawX = (float)rawPoint.X;
                var rawY = (float)rawPoint.Y;
                var dx = rawX - state.LastRawX;
                var dy = rawY - state.LastRawY;
                var dist = (float)Math.Sqrt(dx * dx + dy * dy);
                var speed = dist / dt;

                var filteredX = state.FilterX.Filter(rawX, dt, speed);
                var filteredY = state.FilterY.Filter(rawY, dt, speed);

                var hwPressure = RealtimeClamp((float)rawPoint.PressureFactor, 0f, 1f);
                if (Math.Abs(hwPressure - 0.5f) > 0.02f)
                    state.SawPressureVariation = true;
                var usePressure = state.SawPressureVariation && hwPressure > 0f;

                var width = baseWidth;
                if (usePressure)
                    width *= 0.25f + 0.75f * hwPressure;
                var speedNormalization = 1800f + state.SmoothedSampleRateHz * 3.5f;
                width *= RealtimeClamp(1.15f - (speed / speedNormalization), 0.45f, 1.25f);
                var speedPressure = WidthToPressure(width, baseWidth);

                var pressure = usePressure
                    ? ((1f - mix) * hwPressure + mix * speedPressure)
                    : speedPressure;
                pressure = RealtimeClamp(pressure, 0.08f, 1f);
                pressure = state.FilterPressure.Filter(pressure, dt, speed);

                var minDist = GetRealtimeBrushTipMinDistance(state.SmoothedSampleRateHz);
                if (dist < minDist && state.HasSeed)
                {
                    state.LastRawX = rawX;
                    state.LastRawY = rawY;
                    state.LastTimestampMs = nowMs;
                    continue;
                }

                if (!state.HasSeed)
                {
                    state.HasSeed = true;
                    state.LastSmoothX = filteredX;
                    state.LastSmoothY = filteredY;
                    state.LastSmoothPressure = pressure;
                    strokeVisual.Add(new StylusPoint(filteredX, filteredY, pressure));
                }
                else
                {
                    // 采用中点链减抖：保持实时笔锋同时降低折线锯齿
                    var midX = (state.LastSmoothX + filteredX) * 0.5f;
                    var midY = (state.LastSmoothY + filteredY) * 0.5f;
                    var midPressure = (state.LastSmoothPressure + pressure) * 0.5f;
                    strokeVisual.Add(new StylusPoint(midX, midY, midPressure));
                    state.LastSmoothX = filteredX;
                    state.LastSmoothY = filteredY;
                    state.LastSmoothPressure = pressure;
                }

                state.LastRawX = rawX;
                state.LastRawY = rawY;
                state.LastTimestampMs = nowMs;
                appended = true;
            }

            var committedStroke = strokeVisual.Stroke;
            if (appended && committedStroke != null)
            {
                if (committedStroke.DrawingAttributes != null)
                    committedStroke.DrawingAttributes.IgnorePressure = false;
                if (!committedStroke.ContainsPropertyData(RealtimeVelocityBrushTipAppliedGuid))
                    committedStroke.AddPropertyData(RealtimeVelocityBrushTipAppliedGuid, true);
            }

            return true;
        }

        private bool TryAppendRealtimeVelocityBrushTipPoint(StrokeVisual strokeVisual, int strokeId, Point point, float rawPressure = 0.5f)
        {
            var allow = strokeId == MouseRealtimeStrokeId
                ? ShouldUseRealtimeVelocityBrushTipForMouse()
                : ShouldUseRealtimeVelocityBrushTipForTouch();
            if (!allow || strokeVisual == null)
                return false;
            if (!_realtimeBrushTipStates.TryGetValue(strokeId, out var state))
                return false;

            var mix = RealtimeClamp((float)Settings.Canvas.VelocityBrushTipMix, 0f, 1f);
            var nowMs = RealtimeNowMs();
            var dtMs = Math.Max(1L, nowMs - state.LastTimestampMs);
            var dt = dtMs / 1000f;
            var sampleRate = 1f / Math.Max(1e-4f, dt);
            state.SmoothedSampleRateHz = state.SmoothedSampleRateHz * 0.85f + sampleRate * 0.15f;
            var baseWidth = (float)Math.Max(0.35,
                strokeVisual.Stroke?.DrawingAttributes?.Width ?? inkCanvas.DefaultDrawingAttributes.Width);

            var rawX = (float)point.X;
            var rawY = (float)point.Y;
            var dx = rawX - state.LastRawX;
            var dy = rawY - state.LastRawY;
            var dist = (float)Math.Sqrt(dx * dx + dy * dy);
            var speed = dist / dt;

            var filteredX = state.FilterX.Filter(rawX, dt, speed);
            var filteredY = state.FilterY.Filter(rawY, dt, speed);

            rawPressure = RealtimeClamp(rawPressure, 0f, 1f);
            if (Math.Abs(rawPressure - 0.5f) > 0.02f)
                state.SawPressureVariation = true;
            var usePressure = state.SawPressureVariation && rawPressure > 0f;

            var width = baseWidth;
            if (usePressure)
                width *= 0.25f + 0.75f * rawPressure;
            var speedNormalization = 1800f + state.SmoothedSampleRateHz * 3.5f;
            width *= RealtimeClamp(1.15f - (speed / speedNormalization), 0.45f, 1.25f);
            var speedPressure = WidthToPressure(width, baseWidth);

            var pressure = usePressure
                ? ((1f - mix) * rawPressure + mix * speedPressure)
                : speedPressure;
            pressure = RealtimeClamp(pressure, 0.08f, 1f);
            pressure = state.FilterPressure.Filter(pressure, dt, speed);

            var minDist = GetRealtimeBrushTipMinDistance(state.SmoothedSampleRateHz);
            if (dist < minDist && state.HasSeed && strokeId == MouseRealtimeStrokeId)
            {
                state.LastRawX = rawX;
                state.LastRawY = rawY;
                state.LastTimestampMs = nowMs;
                return true;
            }

            if (!state.HasSeed)
            {
                state.HasSeed = true;
                state.LastSmoothX = filteredX;
                state.LastSmoothY = filteredY;
                state.LastSmoothPressure = pressure;
                strokeVisual.Add(new StylusPoint(filteredX, filteredY, pressure));
            }
            else
            {
                var midX = (state.LastSmoothX + filteredX) * 0.5f;
                var midY = (state.LastSmoothY + filteredY) * 0.5f;
                var midPressure = (state.LastSmoothPressure + pressure) * 0.5f;
                strokeVisual.Add(new StylusPoint(midX, midY, midPressure));
                state.LastSmoothX = filteredX;
                state.LastSmoothY = filteredY;
                state.LastSmoothPressure = pressure;
            }

            state.LastRawX = rawX;
            state.LastRawY = rawY;
            state.LastTimestampMs = nowMs;
            return true;
        }

        private bool TryAppendRealtimeVelocityBrushTipInterpolatedPoints(StrokeVisual strokeVisual, int strokeId, Point point, float rawPressure = 0.5f)
        {
            if (!_realtimeBrushTipStates.TryGetValue(strokeId, out var state))
                return TryAppendRealtimeVelocityBrushTipPoint(strokeVisual, strokeId, point, rawPressure);

            var appended = false;
            if (!state.HasTouchPoint)
            {
                state.HasTouchPoint = true;
                state.LastTouchPoint = point;
                return TryAppendRealtimeVelocityBrushTipPoint(strokeVisual, strokeId, point, rawPressure);
            }

            foreach (var p in InterpolateTouchPoints(state, point))
            {
                appended |= TryAppendRealtimeVelocityBrushTipPoint(strokeVisual, strokeId, p, rawPressure);
            }
            UpdateTouchInterpolationState(state, point);
            return appended;
        }

        /// <summary>
        /// 保存画布上的非笔画元素（如图片、媒体元素等）
        /// </summary>
        /// <returns>返回保存的非笔画元素列表</returns>
        private List<UIElement> PreserveNonStrokeElements()
        {
            var preservedElements = new List<UIElement>();

            // 遍历inkCanvas的所有子元素，创建副本而不是直接引用
            for (int i = inkCanvas.Children.Count - 1; i >= 0; i--)
            {
                var child = inkCanvas.Children[i];

                // 保存图片、媒体元素等非笔画相关的UI元素
                if (child is Image || child is MediaElement || child is CanvasMediaControl ||
                    (child is Border border && border.Name != "EraserOverlayCanvas"))
                {
                    // CanvasMediaControl 直接保留原始引用，避免克隆导致播放状态丢失
                    if (child is CanvasMediaControl)
                    {
                        preservedElements.Add(child);
                        continue;
                    }

                    // 创建元素的深拷贝，避免直接引用导致的问题
                    var clonedElement = CloneUIElement(child);
                    if (clonedElement != null)
                    {
                        preservedElements.Add(clonedElement);
                    }
                }
            }

            return preservedElements;
        }

        /// <summary>
        /// 克隆UI元素，创建深拷贝
        /// </summary>
        private UIElement CloneUIElement(UIElement originalElement)
        {
            try
            {
                if (originalElement is Image originalImage)
                {
                    var clonedImage = new Image();

                    // 复制图片源
                    if (originalImage.Source is BitmapSource bitmapSource)
                    {
                        clonedImage.Source = bitmapSource;
                    }

                    // 复制属性
                    clonedImage.Width = originalImage.Width;
                    clonedImage.Height = originalImage.Height;
                    clonedImage.Stretch = originalImage.Stretch;
                    clonedImage.StretchDirection = originalImage.StretchDirection;
                    clonedImage.Name = originalImage.Name;
                    clonedImage.IsHitTestVisible = originalImage.IsHitTestVisible;
                    clonedImage.Focusable = originalImage.Focusable;
                    clonedImage.Cursor = originalImage.Cursor;
                    clonedImage.IsManipulationEnabled = originalImage.IsManipulationEnabled;

                    // 复制位置
                    InkCanvas.SetLeft(clonedImage, InkCanvas.GetLeft(originalImage));
                    InkCanvas.SetTop(clonedImage, InkCanvas.GetTop(originalImage));

                    // 复制变换
                    if (originalImage.RenderTransform != null)
                    {
                        clonedImage.RenderTransform = originalImage.RenderTransform.Clone();
                    }

                    return clonedImage;
                }
                else if (originalElement is CanvasMediaControl originalMediaControl)
                {
                    var clonedMediaControl = new CanvasMediaControl
                    {
                        Width = originalMediaControl.Width,
                        Height = originalMediaControl.Height,
                        Name = originalMediaControl.Name,
                        IsHitTestVisible = originalMediaControl.IsHitTestVisible,
                        Focusable = originalMediaControl.Focusable,
                        RenderTransform = originalMediaControl.RenderTransform?.Clone()
                    };

                    if (!string.IsNullOrWhiteSpace(originalMediaControl.SourcePath))
                    {
                        clonedMediaControl.Initialize(originalMediaControl.SourcePath, originalMediaControl.DisplayName);
                    }
                    clonedMediaControl.SetPlaybackRate(originalMediaControl.PlaybackRate);
                    clonedMediaControl.SetVolumeLevel(originalMediaControl.VolumeLevel);
                    var playbackPosition = originalMediaControl.GetPlaybackPositionOrNull();
                    if (playbackPosition.HasValue)
                    {
                        clonedMediaControl.SetPlaybackPosition(playbackPosition.Value);
                    }

                    InkCanvas.SetLeft(clonedMediaControl, InkCanvas.GetLeft(originalMediaControl));
                    InkCanvas.SetTop(clonedMediaControl, InkCanvas.GetTop(originalMediaControl));

                    return clonedMediaControl;
                }
                else if (originalElement is MediaElement originalMedia)
                {
                    var clonedMedia = new MediaElement
                    {
                        Source = originalMedia.Source,
                        Width = originalMedia.Width,
                        Height = originalMedia.Height,
                        Name = originalMedia.Name,
                        IsHitTestVisible = originalMedia.IsHitTestVisible,
                        Focusable = originalMedia.Focusable,
                        RenderTransform = originalMedia.RenderTransform?.Clone()
                    };

                    // 复制位置
                    InkCanvas.SetLeft(clonedMedia, InkCanvas.GetLeft(originalMedia));
                    InkCanvas.SetTop(clonedMedia, InkCanvas.GetTop(originalMedia));

                    return clonedMedia;
                }
                else if (originalElement is Border originalBorder)
                {
                    var clonedBorder = new Border
                    {
                        Width = originalBorder.Width,
                        Height = originalBorder.Height,
                        Name = originalBorder.Name,
                        IsHitTestVisible = originalBorder.IsHitTestVisible,
                        Focusable = originalBorder.Focusable,
                        Background = originalBorder.Background,
                        BorderBrush = originalBorder.BorderBrush,
                        BorderThickness = originalBorder.BorderThickness,
                        CornerRadius = originalBorder.CornerRadius,
                        RenderTransform = originalBorder.RenderTransform?.Clone()
                    };

                    // 复制位置
                    InkCanvas.SetLeft(clonedBorder, InkCanvas.GetLeft(originalBorder));
                    InkCanvas.SetTop(clonedBorder, InkCanvas.GetTop(originalBorder));

                    return clonedBorder;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"克隆UI元素失败: {ex.Message}", LogHelper.LogType.Error);
            }

            return null;
        }

        /// <summary>
        /// 恢复之前保存的非笔画元素到画布
        /// </summary>
        private void RestoreNonStrokeElements(List<UIElement> preservedElements)
        {
            if (preservedElements == null) return;

            foreach (var element in preservedElements)
            {
                try
                {
                    // 由于现在使用的是克隆的元素，不需要检查Parent属性
                    inkCanvas.Children.Add(element);
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"恢复非笔画元素失败: {ex.Message}", LogHelper.LogType.Error);
                }
            }
        }
    }
}

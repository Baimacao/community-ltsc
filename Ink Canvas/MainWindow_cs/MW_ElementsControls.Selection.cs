using Ink_Canvas.Controls;
using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        // LTSC（2026-11-17）：从 MW_ElementsControls.cs 按 #region 拆出。
        // 四段都是图片选中相关的交互，和主文件里「元素插入」那条主线不同。
        // 只搬代码，不改逻辑。using 区整块复制。
        #region Media
        /// <summary>
        /// 处理媒体插入按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        private async void BtnMediaInsert_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = MainWindowStrings.Main_MediaInsert_OpenDialogFilter;

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;

                FrameworkElement mediaElement = await CreateMediaElementAsync(filePath);

                if (mediaElement != null)
                {
                    InitializeInkCanvasSelectionSettings();
                    InitializeElementTransform(mediaElement);
                    CenterAndScaleElement(mediaElement);
                    BindElementEvents(mediaElement);

                    InkCanvas.SetLeft(mediaElement, 0);
                    InkCanvas.SetTop(mediaElement, 0);
                    inkCanvas.Children.Add(mediaElement);

                    timeMachine.CommitElementInsertHistory(mediaElement);

                    SetCurrentToolMode(InkCanvasEditingMode.Select);
                    UpdateCurrentToolMode("select");
                    HideSubPanels("select");
                }
            }
        }

        /// <summary>
        /// 创建画布媒体控件
        /// </summary>
        /// <param name="filePath">媒体文件路径</param>
        /// <returns>创建的媒体控件对象</returns>
        private async Task<FrameworkElement> CreateMediaElementAsync(string filePath)
        {
            string fileExtension = Path.GetExtension(filePath);
            if (!IsSupportedMediaExtension(fileExtension))
            {
                ShowNotification(MainWindowStrings.Main_MediaInsert_UnsupportedFormat);
                return null;
            }

            try
            {
                var autoSavedPath = Settings?.Automation?.AutoSavedStrokesLocation;
                if (string.IsNullOrEmpty(autoSavedPath))
                {
                    ShowNotification(MainWindowStrings.Main_MediaInsert_InsertFailed);
                    return null;
                }

                string savePath = Path.Combine(autoSavedPath, "File Dependency");
                if (!Directory.Exists(savePath))
                {
                    Directory.CreateDirectory(savePath);
                }

                string timestamp = "media_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff");
                string newFilePath = Path.Combine(savePath, timestamp + fileExtension);
                await Task.Run(() => File.Copy(filePath, newFilePath, true));

                // Copy matching LRC file if present (non-fatal)
                try
                {
                    var originalLrcPath = Path.ChangeExtension(filePath, ".lrc");
                    if (File.Exists(originalLrcPath))
                    {
                        var newLrcPath = Path.Combine(savePath, timestamp + ".lrc");
                        await Task.Run(() => File.Copy(originalLrcPath, newLrcPath, true));
                    }
                }
                catch
                {
                    // LRC copy failure should not prevent media insertion
                }

                return await Dispatcher.InvokeAsync(() =>
                {
                    var mediaControl = new CanvasMediaControl
                    {
                        Name = timestamp,
                        ToolTip = Path.GetFileName(filePath)
                    };
                    mediaControl.Initialize(newFilePath, Path.GetFileName(filePath));
                    AttachMediaFailureNotification(mediaControl);
                    return (FrameworkElement)mediaControl;
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"插入媒体失败: {ex}", LogHelper.LogType.Error);
                ShowNotification(string.Format(MainWindowStrings.Main_MediaInsert_InsertFailed, ex.Message));
                return null;
            }
        }
        #endregion

        #region Image Operations

        /// <summary>
        /// 旋转图片
        /// </summary>
        /// <param name="image">要旋转的图片</param>
        /// <param name="angle">旋转角度（正数为顺时针，负数为逆时针）</param>
        private void RotateImage(Image image, double angle)
        {
            if (image == null) return;

            try
            {
                // 获取当前的变换
                var transformGroup = image.RenderTransform as TransformGroup ?? new TransformGroup();

                // 查找现有的旋转变换
                RotateTransform rotateTransform = null;
                foreach (Transform transform in transformGroup.Children)
                {
                    if (transform is RotateTransform rt)
                    {
                        rotateTransform = rt;
                        break;
                    }
                }

                // 如果没有旋转变换，创建一个新的
                if (rotateTransform == null)
                {
                    rotateTransform = new RotateTransform();
                    transformGroup.Children.Add(rotateTransform);
                }

                // 设置旋转中心为图片中心
                rotateTransform.CenterX = image.ActualWidth / 2;
                rotateTransform.CenterY = image.ActualHeight / 2;

                // 累加旋转角度
                rotateTransform.Angle = (rotateTransform.Angle + angle) % 360;

                // 应用变换
                image.RenderTransform = transformGroup;

                // 提交到时间机器以支持撤销
                // 注意：旋转操作目前不支持撤销，因为需要更复杂的历史记录机制
            }
            catch (Exception ex)
            {
                // 记录错误但不中断程序
                Debug.WriteLine($"旋转图片时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 克隆图片
        /// </summary>
        /// <param name="image">要克隆的图片</param>
        private Image CloneImage(Image image)
        {
            if (image == null) return null;

            try
            {
                // 创建图片的副本
                var clonedImage = new Image
                {
                    Source = image.Source,
                    Width = image.Width,
                    Height = image.Height,
                    Stretch = image.Stretch,
                    RenderTransform = image.RenderTransform?.Clone()
                };

                // 设置位置，稍微偏移以避免重叠
                InkCanvas.SetLeft(clonedImage, InkCanvas.GetLeft(image) + 20);
                InkCanvas.SetTop(clonedImage, InkCanvas.GetTop(image) + 20);

                // 设置图片属性，避免被InkCanvas选择系统处理
                clonedImage.IsHitTestVisible = true;
                clonedImage.Focusable = false;

                // 初始化变换
                InitializeElementTransform(clonedImage);

                // 绑定事件
                BindElementEvents(clonedImage);

                // 添加到画布
                inkCanvas.Children.Add(clonedImage);

                // 提交到时间机器以支持撤销
                timeMachine.CommitElementInsertHistory(clonedImage);

                return clonedImage;
            }
            catch (Exception ex)
            {
                // 记录错误但不中断程序
                LogHelper.WriteLogToFile($"克隆图片时发生错误: {ex.Message}", LogHelper.LogType.Error);
                return null;
            }
        }

        /// <summary>
        /// 克隆图片到新页面
        /// </summary>
        /// <param name="image">要克隆的图片</param>
        private void CloneImageToNewBoard(Image image)
        {
            if (image == null) return;

            try
            {
                // 创建图片的副本
                var clonedImage = new Image
                {
                    Source = image.Source,
                    Width = image.Width,
                    Height = image.Height,
                    Stretch = image.Stretch,
                    RenderTransform = image.RenderTransform?.Clone()
                };

                // 设置位置，稍微偏移以避免重叠
                InkCanvas.SetLeft(clonedImage, InkCanvas.GetLeft(image) + 20);
                InkCanvas.SetTop(clonedImage, InkCanvas.GetTop(image) + 20);

                // 创建新页面
                BtnWhiteBoardAdd_Click(null, null);

                // 添加到新页面的画布
                inkCanvas.Children.Add(clonedImage);

                // 提交到时间机器以支持撤销
                timeMachine.CommitElementInsertHistory(clonedImage);
            }
            catch (Exception ex)
            {
                // 记录错误但不中断程序
                Debug.WriteLine($"克隆图片到新页面时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 缩放图片
        /// </summary>
        /// <param name="image">要缩放的图片</param>
        /// <param name="scaleFactor">缩放因子（大于1为放大，小于1为缩小）</param>
        private void ScaleImage(Image image, double scaleFactor)
        {
            if (image == null) return;

            try
            {
                // 获取当前的变换
                var transformGroup = image.RenderTransform as TransformGroup ?? new TransformGroup();

                // 查找现有的缩放变换
                ScaleTransform scaleTransform = null;
                foreach (Transform transform in transformGroup.Children)
                {
                    if (transform is ScaleTransform st)
                    {
                        scaleTransform = st;
                        break;
                    }
                }

                // 如果没有缩放变换，创建一个新的
                if (scaleTransform == null)
                {
                    scaleTransform = new ScaleTransform();
                    transformGroup.Children.Add(scaleTransform);
                }

                // 设置缩放中心为图片中心
                scaleTransform.CenterX = image.ActualWidth / 2;
                scaleTransform.CenterY = image.ActualHeight / 2;

                // 应用缩放因子
                scaleTransform.ScaleX *= scaleFactor;
                scaleTransform.ScaleY *= scaleFactor;

                // 应用变换
                image.RenderTransform = transformGroup;

                // 提交到时间机器以支持撤销
                // 注意：缩放操作目前不支持撤销，因为需要更复杂的历史记录机制
            }
            catch (Exception ex)
            {
                // 记录错误但不中断程序
                Debug.WriteLine($"缩放图片时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 删除图片
        /// </summary>
        /// <param name="image">要删除的图片</param>
        private void DeleteImage(Image image)
        {
            if (image == null) return;

            try
            {
                // 从画布中移除图片
                if (inkCanvas.Children.Contains(image))
                {
                    inkCanvas.Children.Remove(image);

                    // 提交到时间机器以支持撤销
                    timeMachine.CommitElementRemoveHistory(image);
                }
            }
            catch (Exception ex)
            {
                // 记录错误但不中断程序
                Debug.WriteLine($"删除图片时发生错误: {ex.Message}");
            }
        }

        #endregion

        #region Image Selection Toolbar Event Handlers

        /// <summary>
        /// 处理图片克隆功能
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// - 检查当前选中元素是否为图片
        /// - 创建克隆图片
        /// - 添加到画布
        /// - 初始化变换
        /// - 绑定事件
        /// - 记录历史
        /// - 包含异常处理
        /// </remarks>
        private void BorderImageClone_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("克隆图片")) return;
            try
            {
                if (currentSelectedElement is Image originalImage)
                {
                    // 创建克隆图片
                    Image clonedImage = CloneImage(originalImage);

                    // 添加到画布
                    inkCanvas.Children.Add(clonedImage);

                    // 初始化变换
                    InitializeElementTransform(clonedImage);

                    // 绑定事件
                    BindElementEvents(clonedImage);

                    // 记录历史
                    timeMachine.CommitElementInsertHistory(clonedImage);

                    LogHelper.WriteLogToFile($"图片克隆完成: {clonedImage.Name}");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"图片克隆失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理图片克隆到新页面功能
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// - 检查当前选中元素是否为图片
        /// - 创建新页面
        /// - 创建克隆图片
        /// - 设置图片属性，避免被InkCanvas选择系统处理
        /// - 初始化变换
        /// - 绑定事件
        /// - 添加到新页面的画布
        /// - 记录历史
        /// - 包含异常处理
        /// </remarks>
        private void BorderImageCloneToNewBoard_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("克隆图片到新页面")) return;
            try
            {
                if (currentSelectedElement is Image originalImage)
                {
                    // 创建新页面
                    BtnWhiteBoardAdd_Click(null, null);

                    // 创建克隆图片（不添加到当前画布，因为已经创建了新页面）
                    Image clonedImage = CreateClonedImage(originalImage);

                    if (clonedImage != null)
                    {
                        // 设置图片属性，避免被InkCanvas选择系统处理
                        clonedImage.IsHitTestVisible = true;
                        clonedImage.Focusable = false;

                        // 初始化变换
                        InitializeElementTransform(clonedImage);

                        // 绑定事件
                        BindElementEvents(clonedImage);

                        // 添加到新页面的画布
                        inkCanvas.Children.Add(clonedImage);

                        // 记录历史
                        timeMachine.CommitElementInsertHistory(clonedImage);

                        LogHelper.WriteLogToFile($"图片克隆到新页面完成: {clonedImage.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"图片克隆到新页面失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理图片左旋转功能
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// - 检查当前是否有选中元素
        /// - 应用旋转变换（向左旋转45度）
        /// - 如果是图片元素，更新工具栏位置
        /// - 包含异常处理
        /// </remarks>
        private void BorderImageRotateLeft_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("旋转图片")) return;
            try
            {
                if (currentSelectedElement != null)
                {
                    ApplyRotateTransform(currentSelectedElement, -45);

                    // 更新工具栏位置
                    if (IsBitmapLikeCanvasElement(currentSelectedElement) && BorderImageSelectionControl?.Visibility == Visibility.Visible)
                    {
                        UpdateImageSelectionToolbarPosition(currentSelectedElement);
                    }

                    LogHelper.WriteLogToFile("图片左旋转完成");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"图片左旋转失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理图片右旋转功能
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// - 检查当前是否有选中元素
        /// - 应用旋转变换（向右旋转45度）
        /// - 如果是图片元素，更新工具栏位置
        /// - 包含异常处理
        /// </remarks>
        private void BorderImageRotateRight_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("旋转图片")) return;
            try
            {
                if (currentSelectedElement != null)
                {
                    ApplyRotateTransform(currentSelectedElement, 45);

                    // 更新工具栏位置
                    if (IsBitmapLikeCanvasElement(currentSelectedElement) && BorderImageSelectionControl?.Visibility == Visibility.Visible)
                    {
                        UpdateImageSelectionToolbarPosition(currentSelectedElement);
                    }

                    LogHelper.WriteLogToFile("图片右旋转完成");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"图片右旋转失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理图片缩放减小功能
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// - 检查当前是否有选中元素
        /// - 计算元素中心点
        /// - 应用缩放变换（缩小到0.9倍）
        /// - 如果是图片元素，更新工具栏位置
        /// - 包含异常处理
        /// </remarks>
        private void GridImageScaleDecrease_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("缩放图片")) return;
            try
            {
                if (currentSelectedElement != null)
                {
                    var elementCenter = new Point(currentSelectedElement.ActualWidth / 2, currentSelectedElement.ActualHeight / 2);
                    ApplyScaleTransform(currentSelectedElement, 0.9, elementCenter);

                    // 更新工具栏位置
                    if (IsBitmapLikeCanvasElement(currentSelectedElement) && BorderImageSelectionControl?.Visibility == Visibility.Visible)
                    {
                        UpdateImageSelectionToolbarPosition(currentSelectedElement);
                    }

                    LogHelper.WriteLogToFile("图片缩放减小完成");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"图片缩放减小失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理图片缩放增大功能
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// - 检查当前是否有选中元素
        /// - 计算元素中心点
        /// - 应用缩放变换（放大到1.1倍）
        /// - 如果是图片元素，更新工具栏位置
        /// - 包含异常处理
        /// </remarks>
        private void GridImageScaleIncrease_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("缩放图片")) return;
            try
            {
                if (currentSelectedElement != null)
                {
                    var elementCenter = new Point(currentSelectedElement.ActualWidth / 2, currentSelectedElement.ActualHeight / 2);
                    ApplyScaleTransform(currentSelectedElement, 1.1, elementCenter);

                    // 更新工具栏位置
                    if (IsBitmapLikeCanvasElement(currentSelectedElement) && BorderImageSelectionControl?.Visibility == Visibility.Visible)
                    {
                        UpdateImageSelectionToolbarPosition(currentSelectedElement);
                    }

                    LogHelper.WriteLogToFile("图片缩放增大完成");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"图片缩放增大失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private void ResetPdfSidebarToIdle()
        {
            if (TextBlockPdfSidebarPageLabel != null)
            {
                TextBlockPdfSidebarPageLabel.Text = "— / —";
                TextBlockPdfSidebarPageLabel.Opacity = 0.55;
            }

            if (BorderPdfSidebarPagePrev != null)
            {
                BorderPdfSidebarPagePrev.Opacity = 0.35;
                BorderPdfSidebarPagePrev.IsHitTestVisible = false;
            }

            if (BorderPdfSidebarPageNext != null)
            {
                BorderPdfSidebarPageNext.Opacity = 0.35;
                BorderPdfSidebarPageNext.IsHitTestVisible = false;
            }
        }

        private void UpdatePdfSidebarFromPdf(PdfEmbeddedView pdf)
        {
            if (pdf == null) return;

            if (TextBlockPdfSidebarPageLabel != null)
            {
                TextBlockPdfSidebarPageLabel.Text = pdf.PageLabelText;
                TextBlockPdfSidebarPageLabel.Opacity = 1.0;
            }

            bool prevOk = pdf.CanGoPrevious;
            bool nextOk = pdf.CanGoNext;
            if (BorderPdfSidebarPagePrev != null)
            {
                BorderPdfSidebarPagePrev.Opacity = prevOk ? 1.0 : 0.35;
                BorderPdfSidebarPagePrev.IsHitTestVisible = prevOk;
            }

            if (BorderPdfSidebarPageNext != null)
            {
                BorderPdfSidebarPageNext.Opacity = nextOk ? 1.0 : 0.35;
                BorderPdfSidebarPageNext.IsHitTestVisible = nextOk;
            }
        }

        private void SelectedPdf_PageNavigationStateChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (sender is PdfEmbeddedView pdf)
                {
                    if (!inkCanvas.Children.Contains(pdf))
                    {
                        SyncPdfPageSidebarWithCanvas();
                        return;
                    }

                    UpdatePdfSidebarFromPdf(pdf);
                    UpdatePdfPageSidebarPosition(pdf);
                    RequestPdfSidebarPositionRefresh();
                }
                if (currentSelectedElement != null && IsBitmapLikeCanvasElement(currentSelectedElement))
                {
                    UpdateImageSelectionToolbarPosition(currentSelectedElement);
                    if (ImageSelectionOverlay?.Visibility == Visibility.Visible)
                        UpdateImageResizeHandlesPosition(GetElementActualBounds(currentSelectedElement));
                }
            }), DispatcherPriority.Loaded);
        }

        private async void BorderPdfSidebarPagePrev_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("切换 PDF 页")) return;
            try
            {
                var pdf = GetPdfSidebarTargetElement();
                if (pdf != null && pdf.CanGoPrevious)
                    await pdf.GoToPreviousPageAsync();
                SyncPdfPageSidebarWithCanvas();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PDF 上一页失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private async void BorderPdfSidebarPageNext_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("切换 PDF 页")) return;
            try
            {
                var pdf = GetPdfSidebarTargetElement();
                if (pdf != null && pdf.CanGoNext)
                    await pdf.GoToNextPageAsync();
                SyncPdfPageSidebarWithCanvas();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PDF 下一页失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理图片删除功能
        /// </summary>
        private void BorderImageDelete_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("删除图片")) return;
            try
            {
                if (currentSelectedElement != null)
                {
                    // 保存删除前的编辑模式
                    var previousEditingMode = inkCanvas.EditingMode;

                    // 记录删除历史
                    if (currentSelectedElement is CanvasMediaControl mediaControl)
                    {
                        mediaControl.PausePlayback();
                    }
                    timeMachine.CommitElementRemoveHistory(currentSelectedElement);

                    var toRemove = currentSelectedElement;
                    // 从画布中移除
                    inkCanvas.Children.Remove(toRemove);

                    // 清除选中状态
                    UnselectElement(toRemove);
                    currentSelectedElement = null;

                    // 恢复到删除前的编辑模式
                    inkCanvas.EditingMode = previousEditingMode;

                    SyncPdfPageSidebarWithCanvas();

                    LogHelper.WriteLogToFile($"图片删除完成，已恢复到编辑模式: {previousEditingMode}");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"图片删除失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        // 克隆图片的辅助方法（只创建图片，不添加到画布）
        private Image CreateClonedImage(Image originalImage)
        {
            try
            {
                Image clonedImage = new Image();

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

                // 复制位置（在新页面中居中显示）
                double left = InkCanvas.GetLeft(originalImage);
                double top = InkCanvas.GetTop(originalImage);
                InkCanvas.SetLeft(clonedImage, left + 20); // 稍微偏移位置
                InkCanvas.SetTop(clonedImage, top + 20);

                // 复制变换
                if (originalImage.RenderTransform is TransformGroup originalTransformGroup)
                {
                    clonedImage.RenderTransform = originalTransformGroup.Clone();
                }

                // 设置名称
                string timestamp = "img_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff");
                clonedImage.Name = timestamp;

                return clonedImage;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"克隆图片失败: {ex.Message}", LogHelper.LogType.Error);
                return null;
            }
        }

        /// <summary>
        /// 克隆墨迹集合
        /// </summary>
        /// <param name="strokes">要克隆的墨迹集合</param>
        /// <returns>克隆后的墨迹集合</returns>
        private StrokeCollection CloneStrokes(StrokeCollection strokes)
        {
            if (strokes == null || strokes.Count == 0) return new StrokeCollection();

            try
            {
                // 创建墨迹集合的副本
                var clonedStrokes = strokes.Clone();

                // 为每个墨迹添加位置偏移以避免重叠
                foreach (var stroke in clonedStrokes)
                {
                    var offsetPoints = new StylusPointCollection();
                    foreach (var point in stroke.StylusPoints)
                    {
                        offsetPoints.Add(new StylusPoint(point.X + 20, point.Y + 20, point.PressureFactor));
                    }
                    stroke.StylusPoints = offsetPoints;
                }

                // 添加到画布
                inkCanvas.Strokes.Add(clonedStrokes);

                // 提交到时间机器以支持撤销
                timeMachine.CommitStrokeUserInputHistory(clonedStrokes);

                LogHelper.WriteLogToFile($"墨迹克隆完成: {clonedStrokes.Count} 个墨迹");
                return clonedStrokes;
            }
            catch (Exception ex)
            {
                // 记录错误但不中断程序
                LogHelper.WriteLogToFile($"克隆墨迹时发生错误: {ex.Message}", LogHelper.LogType.Error);
                return new StrokeCollection();
            }
        }

        /// <summary>
        /// 克隆墨迹集合到新页面
        /// </summary>
        /// <param name="strokes">要克隆的墨迹集合</param>
        private void CloneStrokesToNewBoard(StrokeCollection strokes)
        {
            if (strokes == null || strokes.Count == 0) return;

            try
            {
                // 创建墨迹集合的副本
                var clonedStrokes = strokes.Clone();

                // 为每个墨迹添加位置偏移以避免重叠
                foreach (var stroke in clonedStrokes)
                {
                    var offsetPoints = new StylusPointCollection();
                    foreach (var point in stroke.StylusPoints)
                    {
                        offsetPoints.Add(new StylusPoint(point.X + 20, point.Y + 20, point.PressureFactor));
                    }
                    stroke.StylusPoints = offsetPoints;
                }

                // 创建新页面
                BtnWhiteBoardAdd_Click(null, null);

                // 添加到新页面的画布
                inkCanvas.Strokes.Add(clonedStrokes);

                // 提交到时间机器以支持撤销
                timeMachine.CommitStrokeUserInputHistory(clonedStrokes);

                LogHelper.WriteLogToFile($"墨迹克隆到新页面完成: {clonedStrokes.Count} 个墨迹");
            }
            catch (Exception ex)
            {
                // 记录错误但不中断程序
                LogHelper.WriteLogToFile($"克隆墨迹到新页面时发生错误: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        #endregion

        #region Image Selection Overlay

        private bool _imageOverlayHooked;
        private FrameworkElement _overlayTrackedElement;

        private void EnsureImageOverlayHooks()
        {
            if (_imageOverlayHooked || ImageSelectionOverlay == null) return;
            ImageSelectionOverlay.CoordinateSource = inkCanvas;
            ImageSelectionOverlay.ResizeDelta += ImageSelectionOverlay_ResizeDelta;
            ImageSelectionOverlay.MoveDelta += ImageSelectionOverlay_MoveDelta;
            ImageSelectionOverlay.RotateDelta += ImageSelectionOverlay_RotateDelta;
            _imageOverlayHooked = true;
        }

        private void AttachOverlayTracking(FrameworkElement element)
        {
            if (_overlayTrackedElement == element) return;
            DetachOverlayTracking();
            _overlayTrackedElement = element;
            if (element != null) element.LayoutUpdated += OverlayTrackedElement_LayoutUpdated;
        }

        private void DetachOverlayTracking()
        {
            if (_overlayTrackedElement != null)
            {
                _overlayTrackedElement.LayoutUpdated -= OverlayTrackedElement_LayoutUpdated;
                _overlayTrackedElement = null;
            }
        }

        private void OverlayTrackedElement_LayoutUpdated(object sender, EventArgs e)
        {
            if (ImageSelectionOverlay?.Visibility != Visibility.Visible) return;
            if (currentSelectedElement == null || _overlayTrackedElement == null) return;
            if (!ReferenceEquals(currentSelectedElement, _overlayTrackedElement)) return;
            UpdateImageResizeHandlesPosition(default);
        }

        private void ShowImageResizeHandles(FrameworkElement element)
        {
            try
            {
                if (ImageSelectionOverlay == null || element == null) return;
                EnsureImageOverlayHooks();
                AttachOverlayTracking(element);
                UpdateImageResizeHandlesPosition(default);
                ImageSelectionOverlay.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"显示图片选中框失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private void HideImageResizeHandles()
        {
            try
            {
                DetachOverlayTracking();
                if (ImageSelectionOverlay != null)
                    ImageSelectionOverlay.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"隐藏图片选中框失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        // elementBounds parameter is ignored — overlay needs the UNROTATED bounds of the element,
        // computed directly from the element's own state so rotation never distorts the frame.
        private void UpdateImageResizeHandlesPosition(Rect elementBounds)
        {
            try
            {
                if (ImageSelectionOverlay == null || currentSelectedElement == null) return;

                var element = currentSelectedElement;
                var (ox, oy, visW, visH) = GetElementVisualBox(element);
                if (visW <= 0 || visH <= 0) return;

                double scaleX = 1, scaleY = 1, angle = 0;
                if (element.RenderTransform is TransformGroup tg)
                {
                    var st = tg.Children.OfType<ScaleTransform>().FirstOrDefault();
                    var rt = tg.Children.OfType<RotateTransform>().FirstOrDefault();
                    if (st != null) { scaleX = st.ScaleX; scaleY = st.ScaleY; }
                    if (rt != null) angle = rt.Angle;
                }

                // Compute the visual center directly from the element's actual transform,
                // so we don't have to model the transform chain ourselves.
                Point visualCenterLocal = new Point(ox + visW / 2, oy + visH / 2);
                Point centerCanvas;
                try
                {
                    var t = element.TransformToAncestor(inkCanvas);
                    centerCanvas = t.Transform(visualCenterLocal);
                    // Cancel out rotation: TransformToAncestor includes Rotate; we need pre-rotation
                    // center in canvas coords for the overlay (overlay applies rotation itself).
                    // The visual center is invariant under rotation around itself, so this is the
                    // same point in canvas coords either way.
                }
                catch
                {
                    double left = InkCanvas.GetLeft(element); if (double.IsNaN(left)) left = 0;
                    double top = InkCanvas.GetTop(element); if (double.IsNaN(top)) top = 0;
                    double tx = 0, ty = 0;
                    if (element.RenderTransform is TransformGroup tg2)
                    {
                        var tt = tg2.Children.OfType<TranslateTransform>().FirstOrDefault();
                        if (tt != null) { tx = tt.X; ty = tt.Y; }
                    }
                    centerCanvas = new Point(left + tx + (ox + visW / 2) * scaleX,
                                             top + ty + (oy + visH / 2) * scaleY);
                }

                double scaledW = visW * scaleX;
                double scaledH = visH * scaleY;

                ImageSelectionOverlay.UpdateFrame(centerCanvas, scaledW, scaledH, angle);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"更新图片选中框位置失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private double GetElementRotationAngle(FrameworkElement element)
        {
            if (element?.RenderTransform is TransformGroup tg)
            {
                var rt = tg.Children.OfType<RotateTransform>().FirstOrDefault();
                if (rt != null) return rt.Angle;
            }
            return 0;
        }

        // Visual box of the rendered content inside element.ActualWidth/Height,
        // accounting for Stretch=Uniform letterboxing on Image elements.
        // Returns (offsetX, offsetY, visibleW, visibleH) in base (unscaled) coords.
        private static (double ox, double oy, double w, double h) GetElementVisualBox(FrameworkElement element)
        {
            double boxW = element.ActualWidth > 0 ? element.ActualWidth : element.Width;
            double boxH = element.ActualHeight > 0 ? element.ActualHeight : element.Height;
            if (double.IsNaN(boxW) || double.IsNaN(boxH) || boxW <= 0 || boxH <= 0)
                return (0, 0, 0, 0);

            if (element is Image img && img.Stretch == Stretch.Uniform && img.Source is BitmapSource bs
                && bs.PixelWidth > 0 && bs.PixelHeight > 0)
            {
                double srcAspect = (double)bs.PixelWidth / bs.PixelHeight;
                double boxAspect = boxW / boxH;
                double vW, vH;
                if (srcAspect > boxAspect) { vW = boxW; vH = boxW / srcAspect; }
                else { vH = boxH; vW = boxH * srcAspect; }
                return ((boxW - vW) / 2, (boxH - vH) / 2, vW, vH);
            }

            return (0, 0, boxW, boxH);
        }

        // Rotate a canvas-space vector back into the element's local (unrotated) space.
        private Vector CanvasVectorToLocal(Vector canvasDelta, double angleDegrees)
        {
            double rad = -angleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            return new Vector(canvasDelta.X * cos - canvasDelta.Y * sin,
                              canvasDelta.X * sin + canvasDelta.Y * cos);
        }

        private void ImageSelectionOverlay_ResizeDelta(object sender, ImageResizeDeltaEventArgs e)
        {
            if (TryBlockFrozenPageMutation("缩放图片")) return;
            try
            {
                if (!IsBitmapLikeCanvasElement(currentSelectedElement)) return;
                ResizeImageByCorner(currentSelectedElement, e.CanvasDelta, e.Corner, e.LockAspectRatio);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"图片缩放失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private void ImageSelectionOverlay_MoveDelta(object sender, ImageMoveDeltaEventArgs e)
        {
            if (TryBlockFrozenPageMutation("移动图片")) return;
            try
            {
                if (currentSelectedElement == null) return;
                if (currentSelectedElement.RenderTransform is TransformGroup tg)
                {
                    var tt = tg.Children.OfType<TranslateTransform>().FirstOrDefault();
                    var rt = tg.Children.OfType<RotateTransform>().FirstOrDefault();
                    if (tt != null)
                    {
                        tt.X += e.CanvasDelta.X;
                        tt.Y += e.CanvasDelta.Y;
                        // Keep rotation center locked to the visual center so the element
                        // translates rigidly instead of swinging around an old pivot.
                        if (rt != null)
                        {
                            rt.CenterX += e.CanvasDelta.X;
                            rt.CenterY += e.CanvasDelta.Y;
                        }
                    }
                }
                UpdateImageResizeHandlesPosition(default);
                if (BorderImageSelectionControl?.Visibility == Visibility.Visible)
                    UpdateImageSelectionToolbarPosition(currentSelectedElement);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"图片拖动失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private void ImageSelectionOverlay_RotateDelta(object sender, ImageRotateDeltaEventArgs e)
        {
            if (TryBlockFrozenPageMutation("旋转图片")) return;
            try
            {
                if (currentSelectedElement == null) return;
                ApplyRotateTransform(currentSelectedElement, e.AngleDelta);
                UpdateImageResizeHandlesPosition(default);
                if (BorderImageSelectionControl?.Visibility == Visibility.Visible)
                    UpdateImageSelectionToolbarPosition(currentSelectedElement);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"图片旋转失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private void ResizeImageByCorner(FrameworkElement element, Vector canvasDelta,
                                         ImageResizeCorner corner, bool lockAspect)
        {
            if (element is CanvasMediaControl mediaControl)
            {
                ResizeCanvasMediaControlByCorner(mediaControl, canvasDelta, corner, lockAspect);
                return;
            }

            if (element is PdfEmbeddedView)
                lockAspect = true;

            if (!(element.RenderTransform is TransformGroup transformGroup)) return;
            var scaleTransform = transformGroup.Children.OfType<ScaleTransform>().FirstOrDefault();
            var translateTransform = transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault();
            var rotateTransform = transformGroup.Children.OfType<RotateTransform>().FirstOrDefault();
            if (scaleTransform == null || translateTransform == null) return;

            double angle = rotateTransform?.Angle ?? 0;

            var (ox, oy, visW, visH) = GetElementVisualBox(element);
            if (visW <= 0 || visH <= 0) return;

            double left = InkCanvas.GetLeft(element); if (double.IsNaN(left)) left = 0;
            double top = InkCanvas.GetTop(element); if (double.IsNaN(top)) top = 0;

            double curW = visW * scaleTransform.ScaleX;
            double curH = visH * scaleTransform.ScaleY;

            // Drag delta → local (unrotated) space so corner tracks cursor under any rotation.
            Vector local = CanvasVectorToLocal(canvasDelta, angle);

            double newW = curW, newH = curH;
            double pivotFracX = 0, pivotFracY = 0; // opposite visual corner
            switch (corner)
            {
                case ImageResizeCorner.TopLeft:
                    newW = curW - local.X; newH = curH - local.Y;
                    pivotFracX = 1; pivotFracY = 1;
                    break;
                case ImageResizeCorner.TopRight:
                    newW = curW + local.X; newH = curH - local.Y;
                    pivotFracX = 0; pivotFracY = 1;
                    break;
                case ImageResizeCorner.BottomLeft:
                    newW = curW - local.X; newH = curH + local.Y;
                    pivotFracX = 1; pivotFracY = 0;
                    break;
                case ImageResizeCorner.BottomRight:
                    newW = curW + local.X; newH = curH + local.Y;
                    pivotFracX = 0; pivotFracY = 0;
                    break;
            }

            if (lockAspect && curW > 0 && curH > 0)
            {
                double uniform = Math.Min(newW / curW, newH / curH);
                newW = curW * uniform;
                newH = curH * uniform;
            }

            double newScaleX = newW / visW;
            double newScaleY = newH / visH;
            newScaleX = Math.Max(0.1, Math.Min(newScaleX, 10.0));
            newScaleY = Math.Max(0.1, Math.Min(newScaleY, 10.0));
            newW = visW * newScaleX;
            newH = visH * newScaleY;

            double tx = translateTransform.X;
            double ty = translateTransform.Y;

            // Visual pivot canvas position BEFORE the change.
            // Visual box origin (pre-scale) is (ox, oy); after scale its top-left in post-scale
            // space (before rotation/translate) is (ox*sx, oy*sy), size (visW*sx, visH*sy).
            // Pivot pre-rotation = (tx + (ox + pivotFrac*visW) * sx,  ty + (oy + pivotFrac*visH) * sy).
            // Rotation center is the element visual center, which is the SAME pre-rotation anchor
            // we derive below — so pre-rotation coord == canvas coord relative to (left, top) up
            // to a rotation around that center. Because we rotate around the center and both
            // before/after share the same center (we'll update it to newCenter below after shift),
            // we must match canvas positions WITH rotation applied.
            Point pivotBefore = ApplyCanvasTransform(ox + pivotFracX * visW, oy + pivotFracY * visH,
                                                     ox + visW / 2, oy + visH / 2,
                                                     scaleTransform.ScaleX, scaleTransform.ScaleY,
                                                     tx, ty, angle, left, top);

            Point pivotAfter = ApplyCanvasTransform(ox + pivotFracX * visW, oy + pivotFracY * visH,
                                                    ox + visW / 2, oy + visH / 2,
                                                    newScaleX, newScaleY,
                                                    tx, ty, angle, left, top);

            Vector canvasDrift = pivotBefore - pivotAfter;
            translateTransform.X += canvasDrift.X;
            translateTransform.Y += canvasDrift.Y;

            scaleTransform.ScaleX = newScaleX;
            scaleTransform.ScaleY = newScaleY;

            // Update rotation center to the new visual center so future rotations behave.
            if (rotateTransform != null)
            {
                rotateTransform.CenterX = translateTransform.X + (ox + visW / 2) * newScaleX;
                rotateTransform.CenterY = translateTransform.Y + (oy + visH / 2) * newScaleY;
            }

            UpdateImageResizeHandlesPosition(default);
            if (BorderImageSelectionControl?.Visibility == Visibility.Visible)
                UpdateImageSelectionToolbarPosition(element);
        }

        // Canvas position of element-local point (lx, ly) under the given transform.
        // Model: P_canvas = (left, top) + Rotate_center(S * (lx,ly) + T)
        // where center = S * (cx, cy) + T, and rotation angle is angleDeg.
        private static Point ApplyCanvasTransform(double lx, double ly, double cx, double cy,
                                                  double sx, double sy, double tx, double ty,
                                                  double angleDeg, double left, double top)
        {
            double preX = sx * lx + tx;
            double preY = sy * ly + ty;
            double centerX = sx * cx + tx;
            double centerY = sy * cy + ty;
            double rad = angleDeg * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            double relX = preX - centerX;
            double relY = preY - centerY;
            double rotX = relX * cos - relY * sin + centerX;
            double rotY = relX * sin + relY * cos + centerY;
            return new Point(left + rotX, top + rotY);
        }

        #endregion

    }
}

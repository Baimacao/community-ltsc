using Ink_Canvas.Controls;
using Ink_Canvas.Helpers;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SystemEvents = Microsoft.Win32.SystemEvents;

namespace Ink_Canvas.Windows
{
    public partial class PPTQuickPanel : UserControl
    {
    // LTSC（2026-11-17）：从 PPTQuickPanel.xaml.cs 拆出。
    //
    // 这一段是图片路径的持久化：算文件夹、算演示文稿 ID、算文件哈希、
    // 存/读每页的图片路径、按页加载图片。全是文件读写，不碰 UI 状态。
    // 原文件和面板交互、音量控制混在一起，但它们不会互相调用。

        /// <summary>
        /// 获取PPT演示文稿的文件夹路径
        /// </summary>
        private string GetPresentationFolderPath()
        {
            try
            {
                if (_mainWindow == null) return null;

                // 获取PPTInkManager
                var singlePPTInkManagerField = _mainWindow.GetType().GetField("_singlePPTInkManager",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var singlePPTInkManager = singlePPTInkManagerField?.GetValue(_mainWindow);

                if (singlePPTInkManager != null)
                {
                    // 使用反射获取AutoSaveLocation
                    var autoSaveLocationProperty = singlePPTInkManager.GetType().GetProperty("AutoSaveLocation");
                    var autoSaveLocation = autoSaveLocationProperty?.GetValue(singlePPTInkManager) as string;

                    if (!string.IsNullOrEmpty(autoSaveLocation))
                    {
                        // 获取PPTManager以获取当前演示文稿信息
                        var pptManagerProperty = _mainWindow.GetType().GetProperty("PPTManager",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                        var pptManager = pptManagerProperty?.GetValue(_mainWindow);

                        if (pptManager != null)
                        {
                            // 尝试获取当前演示文稿，使用重试机制
                            Microsoft.Office.Interop.PowerPoint.Presentation presentation = null;

                            // 首先尝试使用 CurrentPresentation 属性（可能是缓存的）
                            try
                            {
                                var currentPresentationProperty = pptManager.GetType().GetProperty("CurrentPresentation",
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                presentation = currentPresentationProperty?.GetValue(pptManager) as Microsoft.Office.Interop.PowerPoint.Presentation;
                            }
                            catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }

                            // 如果 CurrentPresentation 不可用，尝试 GetCurrentActivePresentation
                            if (presentation == null)
                            {
                                try
                                {
                                    var getCurrentActivePresentationMethod = pptManager.GetType().GetMethod("GetCurrentActivePresentation");
                                    presentation = getCurrentActivePresentationMethod?.Invoke(pptManager, null) as Microsoft.Office.Interop.PowerPoint.Presentation;
                                }
                                catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                            }

                            if (presentation != null)
                            {
                                // LTSC P-4：这个 presentation 是刚取到的独立 RCW
                                // （GetCurrentActivePresentation 里是 app.ActivePresentation，
                                //  每次取值都是一份新引用），用完必须还。
                                // 原来整个方法里从不 ReleaseComObject——面板每打开一次漏一份，
                                // 开关几次 PowerPoint 就退不出去。用 finally 保证所有 return 分支都还。
                                try
                                {
                                    // 立即检查COM对象是否仍然有效
                                    if (!System.Runtime.InteropServices.Marshal.IsComObject(presentation))
                                    {
                                        return null;
                                    }

                                    // 尝试访问对象以验证其有效性
                                    try
                                    {
                                        var _ = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(presentation);
                                        System.Runtime.InteropServices.Marshal.Release(_);
                                    }
                                    catch (System.Runtime.InteropServices.InvalidComObjectException)
                                    {
                                        // COM对象已失效，静默返回
                                        return null;
                                    }

                                    // 立即生成演示文稿ID（在对象失效前）
                                    string presentationId = GeneratePresentationId(presentation);
                                    if (string.IsNullOrEmpty(presentationId) || presentationId.StartsWith("unknown_") || presentationId.StartsWith("invalid_") || presentationId.StartsWith("com_error_"))
                                    {
                                        // 生成ID失败，返回null而不是抛出异常
                                        return null;
                                    }

                                    return Path.Combine(autoSaveLocation, "Auto Saved - Presentations", presentationId);
                                }
                                catch (System.Runtime.InteropServices.InvalidComObjectException)
                                {
                                    // COM对象已失效，静默返回（不记录错误日志）
                                    return null;
                                }
                                catch (System.Runtime.InteropServices.COMException)
                                {
                                    // COM异常，对象可能已失效，静默返回（不记录错误日志）
                                    return null;
                                }
                                catch (Exception ex)
                                {
                                    // 其他异常，只记录非COM相关异常
                                    if (!(ex is System.Runtime.InteropServices.InvalidComObjectException) &&
                                        !(ex is System.Runtime.InteropServices.COMException))
                                    {
                                        LogHelper.WriteLogToFile($"获取PPT文件夹路径时发生非COM异常: {ex.Message}", LogHelper.LogType.Warning);
                                    }
                                    return null;
                                }
                                finally
                                {
                                    // LTSC P-4：谁取谁还。GetCurrentActivePresentation 每次给的是
                                    // 新 RCW，这里不还可能就再也没人还了。
                                    try
                                    {
                                        if (presentation != null &&
                                            System.Runtime.InteropServices.Marshal.IsComObject(presentation))
                                        {
                                            System.Runtime.InteropServices.Marshal.ReleaseComObject(presentation);
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Runtime.InteropServices.InvalidComObjectException)
            {
                // COM对象已失效，静默返回（不记录错误日志）
                return null;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // COM异常，对象可能已失效，静默返回（不记录错误日志）
                return null;
            }
            catch (Exception ex)
            {
                // 只记录非COM相关异常
                if (!(ex is System.Runtime.InteropServices.InvalidComObjectException) &&
                    !(ex is System.Runtime.InteropServices.COMException))
                {
                    LogHelper.WriteLogToFile($"获取PPT文件夹路径失败: {ex.Message}", LogHelper.LogType.Error);
                }
            }

            return null;
        }

        /// <summary>
        /// 生成演示文稿ID（与PPTInkManager一致）
        /// </summary>
        private string GeneratePresentationId(Microsoft.Office.Interop.PowerPoint.Presentation presentation)
        {
            if (presentation == null)
            {
                return $"unknown_{DateTime.Now.Ticks}";
            }

            try
            {
                // 检查COM对象是否仍然有效
                if (!System.Runtime.InteropServices.Marshal.IsComObject(presentation))
                {
                    return $"unknown_{DateTime.Now.Ticks}";
                }

                // 验证COM对象有效性
                try
                {
                    var _ = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(presentation);
                    System.Runtime.InteropServices.Marshal.Release(_);
                }
                catch (System.Runtime.InteropServices.InvalidComObjectException)
                {
                    // COM对象已失效
                    return $"unknown_{DateTime.Now.Ticks}";
                }

                // 逐个访问属性，每个都进行异常处理
                string presentationName = null;
                string presentationPath = null;
                int slidesCount = 0;

                try
                {
                    presentationName = presentation.Name;
                }
                catch (System.Runtime.InteropServices.InvalidComObjectException)
                {
                    return $"unknown_{DateTime.Now.Ticks}";
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    return $"unknown_{DateTime.Now.Ticks}";
                }

                try
                {
                    presentationPath = presentation.FullName;
                }
                catch (System.Runtime.InteropServices.InvalidComObjectException)
                {
                    return $"unknown_{DateTime.Now.Ticks}";
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    return $"unknown_{DateTime.Now.Ticks}";
                }

                try
                {
                    slidesCount = presentation.Slides.Count;
                }
                catch (System.Runtime.InteropServices.InvalidComObjectException)
                {
                    return $"unknown_{DateTime.Now.Ticks}";
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    return $"unknown_{DateTime.Now.Ticks}";
                }

                var fileHash = GetFileHash(presentationPath);
                return $"{presentationName}_{slidesCount}_{fileHash}";
            }
            catch (System.Runtime.InteropServices.InvalidComObjectException)
            {
                // COM对象已失效
                return $"unknown_{DateTime.Now.Ticks}";
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // COM异常，对象可能已失效
                return $"unknown_{DateTime.Now.Ticks}";
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"生成演示文稿ID失败: {ex}", LogHelper.LogType.Error);
                return $"unknown_{DateTime.Now.Ticks}";
            }
        }

        /// <summary>
        /// 计算文件哈希值
        /// </summary>
        private string GetFileHash(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return "unknown";

                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] hashBytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(filePath));
                    return BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 8);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"计算文件哈希值失败: {ex}", LogHelper.LogType.Error);
                return "error";
            }
        }

        /// <summary>
        /// 保存指定页面的图片路径到JSON文件
        /// </summary>
        private void SavePPTImagePaths(int slideIndex)
        {
            try
            {
                if (slideIndex <= 0 || !_pptImagePaths.ContainsKey(slideIndex)) return;

                var folderPath = GetPresentationFolderPath();
                if (string.IsNullOrEmpty(folderPath)) return;

                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                var jsonFilePath = Path.Combine(folderPath, slideIndex.ToString("0000") + ".images.json");
                var imagePaths = _pptImagePaths[slideIndex];

                string json = JsonConvert.SerializeObject(imagePaths, Formatting.Indented);
                File.WriteAllText(jsonFilePath, json);

                LogHelper.WriteLogToFile($"已保存第{slideIndex}页图片路径到JSON: {jsonFilePath}");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"保存PPT图片路径失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 从JSON文件加载指定页面的图片路径
        /// </summary>
        private List<string> LoadPPTImagePaths(int slideIndex)
        {
            try
            {
                if (slideIndex <= 0) return new List<string>();

                var folderPath = GetPresentationFolderPath();
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                    return new List<string>();

                var jsonFilePath = Path.Combine(folderPath, slideIndex.ToString("0000") + ".images.json");
                if (!File.Exists(jsonFilePath))
                    return new List<string>();

                string json = File.ReadAllText(jsonFilePath);
                var imagePaths = JsonConvert.DeserializeObject<List<string>>(json);

                return imagePaths ?? new List<string>();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"加载PPT图片路径失败: {ex.Message}", LogHelper.LogType.Error);
                return new List<string>();
            }
        }

        /// <summary>
        /// 加载指定页面的图片
        /// </summary>
        private async System.Threading.Tasks.Task LoadPPTImages(int slideIndex)
        {
            try
            {
                if (_mainWindow == null || slideIndex <= 0) return;

                var imagePaths = LoadPPTImagePaths(slideIndex);
                if (imagePaths == null || imagePaths.Count == 0) return;

                // 获取inkCanvas，检查是否已有图片
                System.Windows.Controls.InkCanvas inkCanvas = null;
                var inkCanvasField = _mainWindow.GetType().GetField("inkCanvas",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (inkCanvasField != null)
                {
                    inkCanvas = inkCanvasField.GetValue(_mainWindow) as System.Windows.Controls.InkCanvas;
                }
                if (inkCanvas == null)
                {
                    var inkCanvasProperty = _mainWindow.GetType().GetProperty("inkCanvas",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    inkCanvas = inkCanvasProperty?.GetValue(_mainWindow) as System.Windows.Controls.InkCanvas;
                }

                if (inkCanvas == null) return;

                // 检查已存在的图片路径（通过Tag）
                var existingImagePaths = new HashSet<string>();
                foreach (var existingImage in inkCanvas.Children.OfType<System.Windows.Controls.Image>())
                {
                    if (existingImage.Tag is string tagPath && !string.IsNullOrEmpty(tagPath))
                        existingImagePaths.Add(tagPath);
                }

                foreach (var existingPdf in inkCanvas.Children.OfType<PdfEmbeddedView>())
                {
                    if (existingPdf.Tag is string tagPath && !string.IsNullOrEmpty(tagPath))
                        existingImagePaths.Add(tagPath);
                }

                // 使用反射调用MainWindow的CreateAndCompressImageAsync方法
                var createImageMethod = _mainWindow.GetType().GetMethod("CreateAndCompressImageAsync",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (createImageMethod == null)
                {
                    LogHelper.WriteLogToFile("无法找到CreateAndCompressImageAsync方法", LogHelper.LogType.Warning);
                    return;
                }

                foreach (var imagePath in imagePaths)
                {
                    try
                    {
                        // 如果图片已存在，跳过
                        if (existingImagePaths.Contains(imagePath))
                        {
                            continue;
                        }

                        if (!File.Exists(imagePath))
                        {
                            LogHelper.WriteLogToFile($"图片文件不存在: {imagePath}", LogHelper.LogType.Warning);
                            continue;
                        }

                        var imageTask = createImageMethod.Invoke(_mainWindow, new object[] { imagePath }) as System.Threading.Tasks.Task<FrameworkElement>;
                        if (imageTask != null)
                        {
                            var inserted = await imageTask;
                            if (inserted != null)
                            {
                                // 保存原始文件路径到Tag
                                inserted.Tag = imagePath;

                                // 插入图片（不保存路径，因为已经存在）
                                await InsertImageToMainWindow(inserted, imagePath, false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"加载图片失败: {imagePath}, 错误: {ex.Message}", LogHelper.LogType.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"加载PPT图片失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 加载所有页面的图片路径（在PPT打开时调用）
        /// </summary>
        public void LoadAllPPTImagePaths()
        {
            try
            {
                if (_mainWindow == null) return;

                var folderPath = GetPresentationFolderPath();
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                    return;

                // 清空现有数据
                _pptImagePaths.Clear();

                // 查找所有图片JSON文件
                var jsonFiles = Directory.GetFiles(folderPath, "*.images.json");
                foreach (var jsonFile in jsonFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileNameWithoutExtension(jsonFile);
                        fileName = fileName.Replace(".images", ""); // 移除.images后缀

                        if (int.TryParse(fileName, out int slideIndex) && slideIndex > 0)
                        {
                            var imagePaths = LoadPPTImagePaths(slideIndex);
                            if (imagePaths != null && imagePaths.Count > 0)
                            {
                                _pptImagePaths[slideIndex] = imagePaths;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"加载图片路径文件失败: {jsonFile}, 错误: {ex.Message}", LogHelper.LogType.Error);
                    }
                }

                LogHelper.WriteLogToFile($"已加载{_pptImagePaths.Count}个页面的图片路径");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"加载所有PPT图片路径失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }
    }
}

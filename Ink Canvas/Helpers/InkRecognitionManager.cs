using System;
using System.Threading.Tasks;
using System.Windows.Ink;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 墨迹形状识别的统一入口。
    ///
    /// LTSC（2026-11-12）：WinRT 后端已整体移除，只保留 IACore（经 IPC 调用 x86 辅助进程）。
    /// 移除原因见 <see cref="ShapeRecognitionRouter"/> 的注释：WinRT 每画一笔净漏一个内核 Event 句柄。
    /// </summary>
    public sealed class InkRecognitionManager
    {
        private static InkRecognitionManager _instance;
        private static readonly object _lock = new object();

        public static InkRecognitionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new InkRecognitionManager();
                    }
                }

                return _instance;
            }
        }

        private InkRecognitionManager() { }

        public Task<InkShapeRecognitionResult> RecognizeShapeAsync(StrokeCollection strokes)
        {
            if (strokes == null || strokes.Count == 0)
                return Task.FromResult(InkShapeRecognitionResult.Empty);

            try
            {
                // IACore 必须走 IPC 辅助进程（x86/.NET 4.7.2）。
                // 在 .NET 6 x64 主进程中本地加载 IAWinFX 会失败，故不提供本地回退。
                var ipcResult = IpcIACoreClient.Instance.Recognize(strokes);
                return Task.FromResult(ipcResult);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("墨迹形状识别失败: " + ex.Message, LogHelper.LogType.Error);
                return Task.FromResult(InkShapeRecognitionResult.Empty);
            }
        }

        public bool IsValidShapeType(string shapeName)
        {
            return !string.IsNullOrEmpty(shapeName)
                   && (shapeName.Contains("Triangle") || shapeName.Contains("Circle")
                       || shapeName.Contains("Rectangle") || shapeName.Contains("Diamond")
                       || shapeName.Contains("Parallelogram") || shapeName.Contains("Square")
                       || shapeName.Contains("Ellipse") || shapeName.Contains("Line")
                       || shapeName.Contains("Arrow"));
        }

        public string GetSystemInfo()
        {
            return $"墨迹识别系统 (IACore via IPC) - 进程架构: {Environment.Is64BitProcess}";
        }

        public void Dispose()
        {
        }
    }
}

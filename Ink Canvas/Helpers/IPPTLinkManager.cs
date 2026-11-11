using System;
using System.Collections.Generic;

namespace Ink_Canvas.Helpers
{
    public sealed class PPTSlideThumbnail
    {
        public int SlideNumber { get; set; }
        public byte[] PngBytes { get; set; }
    }

    public interface IPPTLinkManager : IDisposable
    {
        event Action<object> SlideShowBegin;
        event Action<object> SlideShowNextSlide;
        event Action<object> SlideShowEnd;
        event Action<object> PresentationOpen;
        event Action<object> PresentationClose;
        event Action<bool> PPTConnectionChanged;
        event Action<bool> SlideShowStateChanged;

        bool IsConnected { get; }
        bool IsInSlideShow { get; }
        bool IsSupportWPS { get; set; }
        bool SkipAnimationsWhenNavigating { get; set; }
        int SlidesCount { get; }

        object PPTApplication { get; }

        /// <summary>
        /// 开始监视与 PowerPoint 的连接以及幻灯片放映相关状态，并在状态变化时触发对应事件。
        /// </summary>
        void StartMonitoring();
        void StopMonitoring(bool isShutdown = false);

        /// <summary>
        /// 重新加载或重建与 PowerPoint 的连接。
        /// </summary>
        /// <remarks>
        /// 调用后实现应刷新内部连接与状态，必要时重建与 PowerPoint 的会话；此操作可能导致 IsConnected 变化并触发 PPTConnectionChanged 或其他相关事件（例如 SlideShowStateChanged）。
        /// </remarks>
        void ReloadConnection();

        /// <summary>
        /// 尝试启动当前演示文稿的放映模式。
        /// </summary>
        /// <returns><c>true</c> 如果放映已成功启动，<c>false</c> 否则。</returns>
        bool TryStartSlideShow();
        /// <summary>
        /// 尝试结束当前正在进行的幻灯片放映。
        /// </summary>
        /// <returns><c>true</c> 如果放映已成功结束，<c>false</c> 否则。</returns>
        bool TryEndSlideShow();

        // 导航控制
        bool TryNavigateToSlide(int slideNumber);
        bool TryNavigateNext();
        bool TryNavigatePrevious();

        // 查询
        int GetCurrentSlideNumber();
        string GetPresentationName();

        /// <summary>
        /// 取当前演示文稿的完整文件路径。取不到返回 null。
        /// </summary>
        /// <remarks>
        /// LTSC P1：旧代码用 pptApplication.SlideShowWindows[1].Presentation.FullName 取路径，
        /// 那是放映窗口的演示文稿；和 GetPresentationName()（ActivePresentation）不同源。
        /// 放映中有隐藏页、或同时开多个演示文稿时，同一次保存里名字和路径会来自不同文档。
        /// 这里统一来源，保证「名字、页数、路径」三者一致。
        /// </remarks>
        string GetPresentationPath();
        bool TryShowSlideNavigation();
        object GetCurrentActivePresentation();
        List<PPTSlideThumbnail> ExportSlideThumbnails(int width, int height, IProgress<double> progress = null);
    }
}


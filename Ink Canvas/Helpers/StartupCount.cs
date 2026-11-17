using System;
using System.IO;

namespace Ink_Canvas.Helpers
{
    public static class StartupCount
    {
        private static readonly string CountFilePath = Path.Combine(App.RootPath, "startup-count");
        private static readonly object fileLock = new object();

        public static int GetCount()
        {
            try
            {
                if (File.Exists(CountFilePath))
                {
                    var text = File.ReadAllText(CountFilePath).Trim();
                    if (int.TryParse(text, out int count))
                        return count;
                }
            }
            catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
            return 0;
        }

        public static void Increment()
        {
            lock (fileLock)
            {
                int count = GetCount() + 1;
                try
                {
                    File.WriteAllText(CountFilePath, count.ToString());
                }
                catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
            }
        }

        /// <summary>
        /// 距离上次记录重启时间过去了多久。没记录过返回 null。
        /// 用来区分「刚起来就崩」和「跑了半小时才崩」——
        /// 后者不该继续累积熔断计数。
        /// </summary>
        public static TimeSpan? TimeSinceLastRestart()
        {
            try
            {
                if (!File.Exists(CountFilePath)) return null;
                var last = File.GetLastWriteTimeUtc(CountFilePath);
                if (last == DateTime.MinValue) return null;
                var span = DateTime.UtcNow - last;
                // 时钟回拨等异常情况，当作刚重启
                return span < TimeSpan.Zero ? TimeSpan.Zero : span;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return null;
            }
        }

        public static void Reset()
        {
            lock (fileLock)
            {
                try
                {
                    if (File.Exists(CountFilePath))
                        File.Delete(CountFilePath);
                }
                catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
            }
        }
    }
}

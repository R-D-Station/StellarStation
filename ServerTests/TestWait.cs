using System;
using System.Diagnostics;
using System.Threading;

namespace ServerTests
{
    internal static class TestWait
    {
        public const int DefaultTimeoutMs = 15000;

        public static void Until(Func<bool> condition, int timeoutMs = DefaultTimeoutMs, string what = null)
            => Until(condition, null, timeoutMs, what);

        public static void Until(Func<bool> condition, Action pump, int timeoutMs = DefaultTimeoutMs, string what = null)
        {
            var sw = Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.ElapsedMilliseconds >= timeoutMs)
                    throw new TimeoutException(
                        $"TestWait.Until timed out after {timeoutMs}ms waiting for: {what ?? "condition"}");
                pump?.Invoke();
                Thread.Sleep(5);
            }
        }
    }
}

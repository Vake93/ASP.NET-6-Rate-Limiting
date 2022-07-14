using System;
using System.Threading.Tasks;

namespace AspNetCore6.RateLimiting;

public static class TaskExtensions
{
#if DEBUG
    // Shorter duration when running tests with debug.
    // Less time waiting for hang unit tests to fail in aspnetcore solution.
    public const int DefaultTimeoutDuration = 5 * 1000;
#else
    public const int DefaultTimeoutDuration = 30 * 1000;
#endif
    public static Task DefaultTimeout(this Task task, int milliseconds = DefaultTimeoutDuration)
    {
        return task.WaitAsync(TimeSpan.FromMilliseconds(milliseconds));
    }
}

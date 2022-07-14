// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AspNetCore6.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace RateLimitingSample;

public class SampleRateLimiterPolicy : IRateLimiterPolicy<string>
{
    private Func<OnRejectedContext, CancellationToken, ValueTask>? _onRejected;

    public SampleRateLimiterPolicy(ILogger<SampleRateLimiterPolicy> logger)
    {
        _onRejected = (context, token) =>
        {
            context.HttpContext.Response.StatusCode = 429;
            logger.LogInformation($"Request rejected by {nameof(SampleRateLimiterPolicy)}");
            return ValueTask.CompletedTask;
        };
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get => _onRejected; }

    // Use a sliding window limiter allowing 1 request every 10 seconds
    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        return RateLimitPartition.CreateSlidingWindowLimiter(string.Empty, key => new SlidingWindowRateLimiterOptions(1, QueueProcessingOrder.OldestFirst, 1, TimeSpan.FromSeconds(5), 1));
    }
}

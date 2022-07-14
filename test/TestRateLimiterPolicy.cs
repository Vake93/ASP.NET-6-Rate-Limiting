// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace AspNetCore6.RateLimiting;

internal class TestRateLimiterPolicy : IRateLimiterPolicy<string>
{
    private readonly string _key;
    private readonly bool _alwaysAccept;
    private readonly Func<OnRejectedContext, CancellationToken, ValueTask> _onRejected;

    public TestRateLimiterPolicy(string key, int statusCode, bool alwaysAccept)
    {
        _key = key;
        _alwaysAccept = alwaysAccept;

        _onRejected = (context, token) =>
        {
            context.HttpContext.Response.StatusCode = statusCode;
            return ValueTask.CompletedTask;
        };
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask> OnRejected { get => _onRejected; }

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        return RateLimitPartition.Create(_key, (key =>
        {
            return new TestRateLimiter(_alwaysAccept);
        }));
    }
}

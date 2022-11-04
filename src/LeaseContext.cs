// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.RateLimiting;

namespace AspNetCore6.RateLimiting;

internal struct LeaseContext : IDisposable
{
    public RequestRejectionReason? RequestRejectionReason { get; init; }

    public RateLimitLease? Lease { get; init; }

    public void Dispose()
    {
        Lease?.Dispose();
    }
}

internal enum RequestRejectionReason
{
    EndpointLimiter,
    GlobalLimiter,
    RequestCanceled
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace AspNetCore6.RateLimiting;

/// <summary>
/// An interface which can be used to identify a type which provides metadata needed for enabling request rate limiting support.
/// </summary>
internal interface IRateLimiterMetadata
{
    /// <summary>
    /// The name of the policy which needs to be applied.
    /// </summary>
    string PolicyName { get; }
}

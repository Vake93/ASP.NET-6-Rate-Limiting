// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.RateLimiting;
using Xunit;

namespace AspNetCore6.RateLimiting;

public class RateLimitingOptionsTests
{
    [Fact]
    public void AddPolicy_ThrowsOnNullPolicyName()
    {
        var options = new RateLimiterOptions();
        Assert.Throws<ArgumentNullException>(() => options.AddPolicy(null!, context => RateLimitPartition.CreateNoLimiter("myKey")));
    }

    [Fact]
    public void AddPolicy_ThrowsOnNullPartitioner()
    {
        var options = new RateLimiterOptions();
        Assert.Throws<ArgumentNullException>(() => options.AddPolicy<string>("myKey", partitioner: null!));
    }

    [Fact]
    public void AddPolicy_ThrowsOnNullPolicy()
    {
        var options = new RateLimiterOptions();
        Assert.Throws<ArgumentNullException>(() => options.AddPolicy<string>("myKey", policy: null!));
    }

    [Fact]
    public void AddPolicy_ThrowsOnDuplicateName()
    {
        var options = new RateLimiterOptions();
        options.AddPolicy("myKey", context => RateLimitPartition.CreateNoLimiter("myKey"));
        Assert.Throws<ArgumentException>(() => options.AddPolicy("myKey", context => RateLimitPartition.CreateNoLimiter("yourKey")));
    }

    [Fact]
    public void AddPolicy_Generic_ThrowsOnDuplicateName()
    {
        var options = new RateLimiterOptions();
        options.AddPolicy("myKey", context => RateLimitPartition.CreateNoLimiter("myKey"));
        Assert.Throws<ArgumentException>(() => options.AddPolicy<string, TestRateLimiterPolicy>("myKey"));
    }
}

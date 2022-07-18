// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace AspNetCore6.RateLimiting;

/// <summary>
/// Limits the rate of requests allowed in the application, based on limits set by a user-provided <see cref="PartitionedRateLimiter{TResource}"/>.
/// </summary>
internal sealed partial class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly Func<OnRejectedContext, CancellationToken, ValueTask>? _defaultOnRejected;
    private readonly ILogger _logger;
    private readonly PartitionedRateLimiter<HttpContext>? _globalLimiter;
    private readonly PartitionedRateLimiter<HttpContext> _endpointLimiter;
    private readonly int _rejectionStatusCode;
    private readonly Dictionary<string, DefaultRateLimiterPolicy> _policyMap;
    private readonly DefaultKeyType _defaultPolicyKey = new DefaultKeyType("__defaultPolicy", new PolicyNameKey { PolicyName = "__defaultPolicyKey" });

    /// <summary>
    /// Creates a new <see cref="RateLimitingMiddleware"/>.
    /// </summary>
    /// <param name="next">The <see cref="RequestDelegate"/> representing the next middleware in the pipeline.</param>
    /// <param name="logger">The <see cref="ILogger"/> used for logging.</param>
    /// <param name="options">The options for the middleware.</param>
    /// <param name="serviceProvider">The service provider.</param>
    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger, IOptions<RateLimiterOptions> options, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _next = next;
        _logger = logger;
        _defaultOnRejected = options.Value.OnRejected;
        _rejectionStatusCode = options.Value.RejectionStatusCode;
        _policyMap = new Dictionary<string, DefaultRateLimiterPolicy>(options.Value.PolicyMap);

        // Activate policies passed to AddPolicy<TPartitionKey, TPolicy>
        foreach (var unactivatedPolicy in options.Value.UnactivatedPolicyMap)
        {
            _policyMap.Add(unactivatedPolicy.Key, unactivatedPolicy.Value(serviceProvider));
        }

        _globalLimiter = options.Value.GlobalLimiter;
        _endpointLimiter = CreateEndpointLimiter();

    }

    // TODO - EventSource?
    /// <summary>
    /// Invokes the logic of the middleware.
    /// </summary>
    /// <param name="context">The <see cref="HttpContext"/>.</param>
    /// <returns>A <see cref="Task"/> that completes when the request leaves.</returns>
    public async Task Invoke(HttpContext context)
    {
        using var leaseContext = await TryAcquireAsync(context);
        if (leaseContext.Lease.IsAcquired)
        {
            await _next(context);
        }
        else
        {
            var thisRequestOnRejected = _defaultOnRejected;
            RateLimiterLog.RequestRejectedLimitsExceeded(_logger);
            // OnRejected "wins" over DefaultRejectionStatusCode - we set DefaultRejectionStatusCode first,
            // then call OnRejected in case it wants to do any further modification of the status code.
            context.Response.StatusCode = _rejectionStatusCode;

            // If this request was rejected by the endpoint limiter, use its OnRejected if available.
            if (leaseContext.GlobalRejected == false)
            {
                DefaultRateLimiterPolicy? policy;
                var policyName = context.GetEndpoint()?.Metadata.GetMetadata<IRateLimiterMetadata>()?.PolicyName;
                // Use custom policy OnRejected if available, else use OnRejected from the Options if available.
                if (policyName is not null && _policyMap.TryGetValue(policyName, out policy) && policy.OnRejected is not null)
                {
                    thisRequestOnRejected = policy.OnRejected;
                }
            }
            if (thisRequestOnRejected is not null)
            {
                await thisRequestOnRejected(new OnRejectedContext() { HttpContext = context, Lease = leaseContext.Lease }, context.RequestAborted);
            }
        }
    }

    private ValueTask<LeaseContext> TryAcquireAsync(HttpContext context)
    {
        var leaseContext = CombinedAcquire(context);
        if (leaseContext.Lease.IsAcquired)
        {
            return ValueTask.FromResult(leaseContext);
        }

        return CombinedWaitAsync(context, context.RequestAborted);
    }

    private LeaseContext CombinedAcquire(HttpContext context)
    {
        RateLimitLease? globalLease = null;
        RateLimitLease? endpointLease = null;

        try
        {
            if (_globalLimiter is not null)
            {
                globalLease = _globalLimiter.Acquire(context);
                if (!globalLease.IsAcquired)
                {
                    return new LeaseContext() { GlobalRejected = true, Lease = globalLease };
                }
            }
            endpointLease = _endpointLimiter.Acquire(context);
            if (!endpointLease.IsAcquired)
            {
                globalLease?.Dispose();
                return new LeaseContext() { GlobalRejected = false, Lease = endpointLease };
            }
        }
        catch (Exception)
        {
            endpointLease?.Dispose();
            globalLease?.Dispose();
            throw;
        }
        return globalLease is null ? new LeaseContext() { Lease = endpointLease } : new LeaseContext() { Lease = new DefaultCombinedLease(globalLease, endpointLease) };
    }

    private async ValueTask<LeaseContext> CombinedWaitAsync(HttpContext context, CancellationToken cancellationToken)
    {
        RateLimitLease? globalLease = null;
        RateLimitLease? endpointLease = null;

        try
        {
            if (_globalLimiter is not null)
            {
                globalLease = await _globalLimiter.WaitAsync(context, cancellationToken: cancellationToken);
                if (!globalLease.IsAcquired)
                {
                    return new LeaseContext() { GlobalRejected = true, Lease = globalLease };
                }
            }
            endpointLease = await _endpointLimiter.WaitAsync(context, cancellationToken: cancellationToken);
            if (!endpointLease.IsAcquired)
            {
                globalLease?.Dispose();
                return new LeaseContext() { GlobalRejected = false, Lease = endpointLease };
            }
        }
        catch (Exception)
        {
            endpointLease?.Dispose();
            globalLease?.Dispose();
            throw;
        }

        return globalLease is null ? new LeaseContext() { Lease = endpointLease } : new LeaseContext() { Lease = new DefaultCombinedLease(globalLease, endpointLease) };
    }

    // Create the endpoint-specific PartitionedRateLimiter
    private PartitionedRateLimiter<HttpContext> CreateEndpointLimiter()
    {
        // If we have a policy for this endpoint, use its partitioner. Else use a NoLimiter.
        return PartitionedRateLimiter.Create<HttpContext, DefaultKeyType>(context =>
        {
            var name = context.GetEndpoint()?.Metadata.GetMetadata<IRateLimiterMetadata>()?.PolicyName;
            if (name is not null)
            {
                if (_policyMap.TryGetValue(name, out var policy))
                {
                    return policy.GetPartition(context);
                }
                else
                {
                    throw new InvalidOperationException($"This endpoint requires a rate limiting policy with name {name}, but no such policy exists.");
                }
            }
            return RateLimitPartition.CreateNoLimiter(_defaultPolicyKey);
        }, new DefaultKeyTypeEqualityComparer());
    }

    private static partial class RateLimiterLog
    {
        [LoggerMessage(1, LogLevel.Debug, "Rate limits exceeded, rejecting this request.", EventName = "RequestRejectedLimitsExceeded")]
        internal static partial void RequestRejectedLimitsExceeded(ILogger logger);

        [LoggerMessage(2, LogLevel.Debug, "This endpoint requires a rate limiting policy with name {PolicyName}, but no such policy exists.", EventName = "WarnMissingPolicy")]
        internal static partial void WarnMissingPolicy(ILogger logger, string policyName);
    }
}

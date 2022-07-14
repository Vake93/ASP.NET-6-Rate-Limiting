// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace AspNetCore6.RateLimiting;

internal sealed class PolicyNameKey
{
    public string PolicyName { get; init; } = null!;

    public override bool Equals(object? obj)
    {
        if (obj is PolicyNameKey key)
        {
            return PolicyName == key.PolicyName;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return PolicyName.GetHashCode();
    }
}

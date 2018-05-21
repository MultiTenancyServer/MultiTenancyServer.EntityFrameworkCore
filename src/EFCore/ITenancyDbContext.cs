// Copyright (c) Kris Penner. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System;

namespace MultiTenancyServer.EntityFramework
{
    /// <summary>
    /// Abstraction interface for the Entity Framework database context used for tenancy references.
    /// </summary>
    public interface ITenancyDbContext : ITenancyDbContext<string>
    {
    }

    /// <summary>
    /// Abstraction interface for the Entity Framework database context used for tenancy references.
    /// </summary>
    /// <typeparam name="TKey">The type of the primary key for tenants.</typeparam>
    public interface ITenancyDbContext<TKey>
        where TKey : IEquatable<TKey>
    {
        /// <summary>
        /// Gets ID or key of the current tenant.
        /// </summary>
        TKey TenantId { get; }
    }
}
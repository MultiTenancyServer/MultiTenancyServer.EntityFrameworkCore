// Copyright (c) Kris Penner. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using KodeAid;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using MultiTenancyServer;
using MultiTenancyServer.Options;

namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    /// Extensions methods for configuring and implementing multi-tenancy in Entity Framework Core.
    /// </summary>
    public static class TenancyEFExtensions
    {
        /// <summary>
        /// Configures a <see cref="ModelBuilder"/> for multi-tenancy.
        /// </summary>
        /// <typeparam name="TKey">Type that represents the tenant key.</typeparam>
        /// <param name="builder">Builder describing the DbContext model.</param>
        /// <param name="options">Options describing how tenanted entities reference their owner tenant.</param>
        /// <param name="staticTenancyModelState">A static field on the DbContext that will store state about the multi-tenancy configuration for the context.</param>
        public static void HasTenancy<TKey>(
            this ModelBuilder builder,
            TenantReferenceOptions options,
            out object staticTenancyModelState)
            where TKey : IEquatable<TKey>
        {
            staticTenancyModelState = new TenancyModelState()
            {
                PropertyType = typeof(TKey),
                Options = options
            };
        }

        /// <summary>
        /// Configures an entity to be tenanted (owned by a tenant).
        /// </summary>
        /// <typeparam name="TEntity">Type that represents the entity.</typeparam>
        /// <typeparam name="TKey">Type that represents the tenant key.</typeparam>
        /// <param name="builder">Builder describing the entity.</param>
        /// <param name="tenantId">Expression to read the currently scoped tenant ID.</param>
        /// <param name="staticTenancyModelState">The same state object that was passed out of <see cref="ModelBuilder"/>.HasTenancy().</param>
        /// <param name="propertyExpression">Expression to access the property on the entity that references the ID of the owning tenant.</param>
        /// <param name="maxLength">Max length used for any keys, this will override any previously configured value in <see cref="TenantReferenceOptions"/>.</param>
        /// <param name="hasIndex">True if the tenant ID reference column should be indexed in the database, this will override any previously configured value in <see cref="TenantReferenceOptions"/>.</param>
        /// <param name="indexNameFormat">Format or name of the index, only applicable if <paramref name="hasIndex"/> is true, this will override any previously configured value in <see cref="TenantReferenceOptions"/>.</param>
        public static void HasTenancy<TEntity, TKey>(
            this EntityTypeBuilder<TEntity> builder,
            Expression<Func<TKey>> tenantId,
            object staticTenancyModelState,
            Expression<Func<TEntity, TKey>> propertyExpression,
            int? maxLength = null,
            bool? hasIndex = null,
            string indexNameFormat = null)
            where TEntity : class
            where TKey : IEquatable<TKey>
        {
            ArgCheck.NotNull(nameof(builder), builder);
            ArgCheck.NotNull(nameof(staticTenancyModelState), staticTenancyModelState);
            ArgCheck.NotNull(nameof(propertyExpression), propertyExpression);
            var propertyName = builder.Property(propertyExpression).Metadata.Name;
            builder.HasTenancy(tenantId, staticTenancyModelState, propertyName, maxLength, hasIndex, indexNameFormat);
        }

        /// <summary>
        /// Configures an entity to be tenanted (owned by a tenant).
        /// </summary>
        /// <typeparam name="TEntity">Type that represents the entity.</typeparam>
        /// <typeparam name="TKey">Type that represents the tenant key.</typeparam>
        /// <param name="builder">Builder describing the entity.</param>
        /// <param name="tenantId">Expression to read the currently scoped tenant ID.</param>
        /// <param name="staticTenancyModelState">The same state object that was passed out of <see cref="ModelBuilder"/>.HasTenancy().</param>
        /// <param name="propertyName">Name of the property on the entity that references the ID of the owning tenant, if it does not exist a shadow property will be added to the entity's model.</param>
        /// <param name="maxLength">Max length used for any keys, this will override any previously configured value in <see cref="TenantReferenceOptions"/>.</param>
        /// <param name="hasIndex">True if the tenant ID reference column should be indexed in the database, this will override any previously configured value in <see cref="TenantReferenceOptions"/>.</param>
        /// <param name="indexNameFormat">Format or name of the index, only applicable if <paramref name="hasIndex"/> is true, this will override any previously configured value in <see cref="TenantReferenceOptions"/>.</param>
        public static void HasTenancy<TEntity, TKey>(
            this EntityTypeBuilder<TEntity> builder,
            Expression<Func<TKey>> tenantId,
            object staticTenancyModelState,
            string propertyName = null,
            int? maxLength = null,
            bool? hasIndex = null,
            string indexNameFormat = null)
            where TEntity : class
            where TKey : IEquatable<TKey>
        {
            ArgCheck.NotNull(nameof(builder), builder);
            ArgCheck.NotNull(nameof(staticTenancyModelState), staticTenancyModelState);
            var modelState = (staticTenancyModelState as TenancyModelState) ??
              throw new InvalidOperationException($"{nameof(HasTenancy)} must be called on the {nameof(ModelBuilder)} first.");

            // get overrides or defaults
            propertyName = propertyName ?? modelState.Options.ReferenceName ?? throw new ArgumentNullException(nameof(propertyName));
            maxLength = maxLength ?? modelState.Options.MaxLengthForKeys;
            hasIndex = hasIndex ?? modelState.Options.IndexReferences;
            indexNameFormat = indexNameFormat ?? modelState.Options.IndexNameFormat;

            var property = builder.Property(modelState.PropertyType, propertyName).IsRequired();
            if (property.Metadata.ClrType == typeof(string) && maxLength.HasValue)
                property.HasMaxLength(maxLength.Value);
            if (hasIndex.Value)
            {
                var index = builder.HasIndex(propertyName);
                if (!string.IsNullOrEmpty(indexNameFormat))
                    index.HasName(string.Format(indexNameFormat, propertyName));
            }

            modelState.Properties[typeof(TEntity)] = propertyName;
            var entityParameter = Expression.Parameter(typeof(TEntity), "e");  // eg. User e
            var propertyNameConstant = Expression.Constant(propertyName, typeof(string));  // eg. (string)"TenantId"
            var efPropertyMethod = typeof(EF).GetMethod(nameof(EF.Property), BindingFlags.Public | BindingFlags.Static).MakeGenericMethod(modelState.PropertyType);  // eg. EF.Property()
            var efPropertyMethodCall = Expression.Call(efPropertyMethod, entityParameter, propertyNameConstant);  // EF.Property(e, "TenantId")
            var efTenantPropertyValueEqualsTenantId = Expression.Equal(efPropertyMethodCall, tenantId.Body);
            var lambda = Expression.Lambda(efTenantPropertyValueEqualsTenantId, entityParameter);
            builder.HasQueryFilter(lambda);
        }

        /// <summary>
        /// Ensures all changes on tenanted entities that are about to be saved to the underlying datastore only reference the currently scoped tenant.
        /// </summary>
        /// <typeparam name="TKey">Type that represents the tenant key.</typeparam>
        /// <param name="context">Context that has multi-tenancy configured and is calling SaveChanges() or SaveChangesAsync().</param>
        /// <param name="tenantId">ID of the currently scoped tenant.</param>
        /// <param name="staticTenancyModelState">The same state object that was passed out of <see cref="ModelBuilder"/>.HasTenancy().</param>
        /// <param name="logger">For logging tenancy access related traces.</param>
        public static void EnsureTenancy<TKey>(this DbContext context, TKey tenantId, object staticTenancyModelState, ILogger logger = null)
          where TKey : IEquatable<TKey>
        {
            ArgCheck.NotNull(nameof(context), context);
            ArgCheck.NotNull(nameof(staticTenancyModelState), staticTenancyModelState);
            var modelState = (staticTenancyModelState as TenancyModelState) ??
              throw new InvalidOperationException($"{nameof(HasTenancy)} must be called on the {nameof(ModelBuilder)} first.");

            var tenancyProperties = modelState.Properties;

            var hasTenancyLookup = new Dictionary<Type, string>();
            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.State != EntityState.Added &&
                    entry.State != EntityState.Deleted &&
                    entry.State != EntityState.Modified)
                    continue;
                var type = entry.Entity.GetType();
                if (!hasTenancyLookup.TryGetValue(type, out var hasTenancyPropertyName))
                {
                    tenancyProperties.TryGetValue(type, out hasTenancyPropertyName);
                    hasTenancyLookup.Add(type, hasTenancyPropertyName);
                }
                if (hasTenancyPropertyName == null)
                    continue;
                if (tenantId == null)
                    throw new InvalidOperationException("TenantId is null - possibly because no scoped tenancy was found.");
                var accessedTenantId = (TKey)entry.Property(hasTenancyPropertyName).CurrentValue;
                if (accessedTenantId != null)
                    TenancyAccessHelper.CheckTenancyAccess(tenantId, accessedTenantId, logger);
                else
                    entry.Property(hasTenancyPropertyName).CurrentValue = tenantId;
            }
        }

        private class TenancyModelState
        {
            public Type PropertyType { get; set; }
            public TenantReferenceOptions Options { get; set; }
            public Dictionary<Type, string> Properties { get; } = new Dictionary<Type, string>();
        }
    }
}

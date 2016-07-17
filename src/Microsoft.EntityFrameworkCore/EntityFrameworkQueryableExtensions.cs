// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Extensions.Internal;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Utilities;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
	/// <summary>
	///     Entity Framework LINQ related extension methods.
	/// </summary>
	public static class EntityFrameworkQueryableExtensions
    {
        #region Tracking

        internal static readonly MethodInfo AsNoTrackingMethodInfo
            = typeof(EntityFrameworkQueryableExtensions)
                .GetTypeInfo().GetDeclaredMethod(nameof(AsNoTracking));

        /// <summary>
        ///     <para>
        ///         Returns a new query where the change tracker will not track any of the entities that are returned.
        ///         If the entity instances are modified, this will not be detected by the change tracker and
        ///         <see cref="DbContext.SaveChanges()" /> will not persist those changes to the database.
        ///     </para>
        ///     <para>
        ///         Disabling change tracking is useful for read-only scenarios because it avoids the overhead of setting
        ///         up change tracking for each entity instance. You should not disable change tracking if you want to
        ///         manipulate entity instances and persist those changes to the database using
        ///         <see cref="DbContext.SaveChanges()" />.
        ///     </para>
        ///     <para>
        ///         Identity resolution will still be performed to ensure that all occurrences of an entity with a given key
        ///         in the result set are represented by the same entity instance.
        ///     </para>
        ///     <para>
        ///         The default tracking behavior for queries can be controlled by <see cref="ChangeTracker.QueryTrackingBehavior" />.
        ///     </para>
        /// </summary>
        /// <typeparam name="TEntity"> The type of entity being queried. </typeparam>
        /// <param name="source"> The source query. </param>
        /// <returns>
        ///     A new query where the result set will not be tracked by the context.
        /// </returns>
        public static IQueryable<TEntity> AsNoTracking<TEntity>(
            [NotNull] this IQueryable<TEntity> source)
            where TEntity : class
            => source.Provider.CreateQuery<TEntity>(
                Expression.Call(
                    null,
                    AsNoTrackingMethodInfo
                        .MakeGenericMethod(typeof(TEntity)), source.Expression));

        internal static readonly MethodInfo AsTrackingMethodInfo
            = typeof(EntityFrameworkQueryableExtensions)
                .GetTypeInfo().GetDeclaredMethod(nameof(AsTracking));

        /// <summary>
        ///     <para>
        ///         Returns a new query where the change tracker will keep track of changes for all entities that are returned.
        ///         Any modification to the entity instances will be detected and persisted to the database during
        ///         <see cref="DbContext.SaveChanges()" />.
        ///     </para>
        ///     <para>
        ///         The default tracking behavior for queries can be controlled by <see cref="ChangeTracker.QueryTrackingBehavior" />.
        ///     </para>
        /// </summary>
        /// <typeparam name="TEntity"> The type of entity being queried. </typeparam>
        /// <param name="source"> The source query. </param>
        /// <returns>
        ///     A new query where the result set will not be tracked by the context.
        /// </returns>
        public static IQueryable<TEntity> AsTracking<TEntity>(
            [NotNull] this IQueryable<TEntity> source)
            where TEntity : class
            => source.Provider.CreateQuery<TEntity>(
                Expression.Call(
                    null,
                    AsTrackingMethodInfo
                        .MakeGenericMethod(typeof(TEntity)), source.Expression));

        #endregion

        #region Load

        /// <summary>
        ///     Enumerates the query. When using Entity Framework, this causes the results of the query to
        ///     be loaded into the associated context. This is equivalent to calling ToList
        ///     and then throwing away the list (without the overhead of actually creating the list).
        /// </summary>
        /// <param name="source"> The source query. </param>
        public static void Load<TSource>([NotNull] this IQueryable<TSource> source)
        {
            Check.NotNull(source, nameof(source));

            using (var enumerator = source.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                }
            }
        }

        /// <summary>
        ///     Asynchronously enumerates the query. When using Entity Framework, this causes the results of the query to
        ///     be loaded into the associated context. This is equivalent to calling ToList
        ///     and then throwing away the list (without the overhead of actually creating the list).
        /// </summary>
        /// <param name="source"> The source query. </param>
        /// <param name="cancellationToken">
        ///     A <see cref="CancellationToken" /> to observe while waiting for the task to complete.
        /// </param>
        /// <returns> A task that represents the asynchronous operation. </returns>
        public static async Task LoadAsync<TSource>(
            [NotNull] this IQueryable<TSource> source, CancellationToken cancellationToken = default(CancellationToken))
        {
            Check.NotNull(source, nameof(source));

            var asyncEnumerable = source.AsAsyncEnumerable();

            if (asyncEnumerable != null)
            {
                using (var enumerator = asyncEnumerable.GetEnumerator())
                {
                    while (await enumerator.MoveNext(cancellationToken)) { }
                }
            }
            else
            {
                Load(source);
            }
        }

        #endregion
		
        #region Impl.

        private static Task<TResult> ExecuteAsync<TSource, TResult>(
            MethodInfo operatorMethodInfo,
            IQueryable<TSource> source,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var provider = source.Provider as IAsyncQueryProvider;

            if (provider != null)
            {
                if (operatorMethodInfo.IsGenericMethod)
                {
                    operatorMethodInfo = operatorMethodInfo.MakeGenericMethod(typeof(TSource));
                }

                return provider.ExecuteAsync<TResult>(
                    Expression.Call(null, operatorMethodInfo, source.Expression),
                    cancellationToken);
            }

            throw new InvalidOperationException(CoreStrings.IQueryableProviderNotAsync);
        }

        private static Task<TResult> ExecuteAsync<TSource, TResult>(
            MethodInfo operatorMethodInfo,
            IQueryable<TSource> source,
            LambdaExpression expression,
            CancellationToken cancellationToken = default(CancellationToken))
            => ExecuteAsync<TSource, TResult>(
                operatorMethodInfo, source, Expression.Quote(expression), cancellationToken);

        private static Task<TResult> ExecuteAsync<TSource, TResult>(
            MethodInfo operatorMethodInfo,
            IQueryable<TSource> source,
            Expression expression,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var provider = source.Provider as IAsyncQueryProvider;

            if (provider != null)
            {
                operatorMethodInfo
                    = operatorMethodInfo.GetGenericArguments().Length == 2
                        ? operatorMethodInfo.MakeGenericMethod(typeof(TSource), typeof(TResult))
                        : operatorMethodInfo.MakeGenericMethod(typeof(TSource));

                return provider.ExecuteAsync<TResult>(
                    Expression.Call(
                        null,
                        operatorMethodInfo,
                        new[] { source.Expression, expression }),
                    cancellationToken);
            }

            throw new InvalidOperationException(CoreStrings.IQueryableProviderNotAsync);
        }

        private static MethodInfo GetMethod<TResult>(
            string name, int parameterCount = 0, Func<MethodInfo, bool> predicate = null)
            => GetMethod(
                name,
                parameterCount,
                mi => (mi.ReturnType == typeof(TResult))
                      && ((predicate == null) || predicate(mi)));

        private static MethodInfo GetMethod(
            string name, int parameterCount = 0, Func<MethodInfo, bool> predicate = null)
            => typeof(Queryable).GetTypeInfo().GetDeclaredMethods(name)
                .Single(mi => (mi.GetParameters().Length == parameterCount + 1)
                              && ((predicate == null) || predicate(mi)));

        #endregion
    }
}

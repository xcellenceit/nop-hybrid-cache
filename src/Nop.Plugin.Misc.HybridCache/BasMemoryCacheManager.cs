﻿using System;
using System.Threading.Tasks;
using EasyCaching.Core;
using Nop.Core.Caching;
using Nop.Plugin.Misc.HybridCache.Workers;

namespace BAS.Nop.Plugin.Misc.HybridCache
{
    /// <summary>
    /// Represents a manager for hybrid caching - local cache with in Redis store (http://redis.io/) backplane.
    /// Mostly it'll be used when running in a web farm, AWS or Azure. But of course it can be also used on any server or environment
    /// This is a copy of the NOP MemoryCacheManager in which we change the default cache time to 24 hrs instead of 1 hr and use the HybridCachingProvider instead of the in-memory version.
    /// </summary>
    public class BasMemoryCacheManager : ILocker, IStaticCacheManager
    {
        private readonly IHybridCachingProvider _provider;
        private readonly IBackgroundTaskQueue _backgroundTaskQueue;

        public BasMemoryCacheManager(IHybridCachingProvider provider, IBackgroundTaskQueue backgroundTaskQueue) 
        {
            _provider = provider;
            _backgroundTaskQueue = backgroundTaskQueue;
        }

        #region Methods

        /// <summary>
        /// Get a cached item. If it's not in the cache yet, then load and cache it
        /// </summary>
        /// <typeparam name="T">Type of cached item</typeparam>
        /// <param name="key">Cache key</param>
        /// <param name="acquire">Function to load item if it's not in the cache yet</param>
        /// <param name="cacheTime">Cache time in minutes; pass 0 to do not cache; pass null to use the default time</param>
        /// <returns>The cached value associated with the specified key</returns>
        public T Get<T>(string key, Func<T> acquire, int? cacheTime = null)
        {
            if (cacheTime <= 0)
                return acquire();

            return _provider.Get(key, acquire, TimeSpan.FromMinutes(cacheTime ?? BasCachingDefaults.CacheTime))
                .Value;
        }

        /// <summary>
        /// Get a cached item. If it's not in the cache yet, then load and cache it
        /// </summary>
        /// <typeparam name="T">Type of cached item</typeparam>
        /// <param name="key">Cache key</param>
        /// <param name="acquire">Function to load item if it's not in the cache yet</param>
        /// <param name="cacheTime">Cache time in minutes; pass 0 to do not cache; pass null to use the default time</param>
        /// <returns>The cached value associated with the specified key</returns>
        public async Task<T> GetAsync<T>(string key, Func<Task<T>> acquire, int? cacheTime = null)
        {
            if (cacheTime <= 0)
                return await acquire();

            var t = await _provider.GetAsync(key, acquire, TimeSpan.FromMinutes(cacheTime ?? BasCachingDefaults.CacheTime));
            return t.Value;
        }

        /// <summary>
        /// Adds the specified key and object to the cache
        /// </summary>
        /// <param name="key">Key of cached item</param>
        /// <param name="data">Value for caching</param>
        /// <param name="cacheTime">Cache time in minutes</param>
        public void Set(string key, object data, int cacheTime)
        {
            if (cacheTime <= 0)
                return;

            _provider.Set(key, data, TimeSpan.FromMinutes(cacheTime));
        }

        /// <summary>
        /// Gets a value indicating whether the value associated with the specified key is cached
        /// </summary>
        /// <param name="key">Key of cached item</param>
        /// <returns>True if item already is in cache; otherwise false</returns>
        public bool IsSet(string key)
        {
            return _provider.Exists(key);
        }

        /// <summary>
        /// Perform some action with exclusive in-memory lock
        /// </summary>
        /// <param name="key">The key we are locking on</param>
        /// <param name="expirationTime">The time after which the lock will automatically be expired</param>
        /// <param name="action">Action to be performed with locking</param>
        /// <returns>True if lock was acquired and action was performed; otherwise false</returns>
        public bool PerformActionWithLock(string key, TimeSpan expirationTime, Action action)
        {
            //ensure that lock is acquired
            if (_provider.Exists(key))
                return false;

            try
            {
                _provider.Set(key, key, expirationTime);

                //perform action
                action();

                return true;
            }
            finally
            {
                //release lock even if action fails
                Remove(key);
            }
        }

        /// <summary>
        /// Removes the value with the specified key from the cache
        /// </summary>
        /// <param name="key">Key of cached item</param>
        public void Remove(string key)
        {
            _provider.Remove(key);
        }

        /// <summary>
        /// Removes items by key prefix
        /// </summary>
        /// <param name="prefix">String key prefix</param>
        public void RemoveByPrefix(string prefix)
        {
            _backgroundTaskQueue.QueueBackgroundWorkItem(async token =>
            {
                await _provider.RemoveByPrefixAsync(prefix);
            });
        }

        /// <summary>
        /// Clear all cache data
        /// </summary>
        public void Clear()
        {
            _provider.RemoveAll(new string[0]);
        }

        /// <summary>
        /// Dispose cache manager
        /// </summary>
        public virtual void Dispose()
        {
            //nothing special
        }

        #endregion
    }

    public static partial class BasCachingDefaults
    {
        /// <summary>
        /// Gets the default cache time in minutes
        /// </summary>
        public static int CacheTime => 1440;
    }

}

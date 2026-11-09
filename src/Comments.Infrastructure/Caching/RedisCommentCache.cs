using System.Collections.Concurrent;
using System.Text.Json;
using Comments.Application.Abstractions;
using Comments.Infrastructure.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Comments.Infrastructure.Caching;

/// <summary>
/// Redis read-through cache with versioned regions.
/// <para>
/// Each region has a version counter; keys embed the current version, so invalidating a region is one INCR and old
/// entries simply expire. The writer increments only after its transaction commits, so a reader can at worst store
/// fresh data under an already dead version — never stale data under the live one.
/// </para>
/// <para>
/// Concurrent misses for the same key in one process share a single database query (single flight), so a popular page
/// that was just invalidated does not hit SQL Server once per waiting request.
/// </para>
/// <para>
/// Failures never escape: if Redis is down or slow, reads go to the database and writes skip the cache. That includes
/// <see cref="RedisTimeoutException"/>, which derives from <see cref="TimeoutException"/>, not from <see cref="RedisException"/>.
/// </para>
/// </summary>
internal sealed class RedisCommentCache(IConnectionMultiplexer redis, ILogger<RedisCommentCache> logger) : ICommentCache
{
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _inFlight = new(StringComparer.Ordinal);

    public async Task<T> GetOrCreateAsync<T>(CacheRegion region, string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        string cacheKey;
        try
        {
            var database = redis.GetDatabase();
            var version = (long?)await database.StringGetAsync(VersionKey(region)) ?? 0;
            cacheKey = $"comments:{region.Name}:v{version}:{key}";

            var cached = await database.StringGetAsync(cacheKey);
            if (cached.HasValue)
            {
                return JsonSerializer.Deserialize<T>((byte[])cached!, JsonDefaults.Options)!;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Redis read failed for {Region}/{Key}; reading from the database", region.Name, key);
            return await factory(cancellationToken);
        }

        var own = new Lazy<Task<object?>>(() => LoadAndStoreAsync(cacheKey, region.Lifetime, factory));
        var load = _inFlight.GetOrAdd(cacheKey, own);

        // The request that owns the load runs the factory with its own scoped services (DbContext),
        // so it must not leave before the factory finishes; requests that only wait on it may.
        return ReferenceEquals(load, own)
            ? (T)(await load.Value)!
            : (T)(await load.Value.WaitAsync(cancellationToken))!;
    }

    public async Task InvalidateAsync(CacheRegion region, CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetDatabase().StringIncrementAsync(VersionKey(region));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not invalidate cache region {Region}; entries expire within {Lifetime}", region.Name, region.Lifetime);
        }
    }

    /// <summary>Runs without the caller's token: other requests may be waiting for the same result.</summary>
    private async Task<object?> LoadAndStoreAsync<T>(string cacheKey, TimeSpan lifetime, Func<CancellationToken, Task<T>> factory)
    {
        try
        {
            var value = await factory(CancellationToken.None);
            try
            {
                await redis.GetDatabase().StringSetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options), lifetime);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Redis write failed for {Key}", cacheKey);
            }

            return value;
        }
        finally
        {
            _inFlight.TryRemove(cacheKey, out _);
        }
    }

    private static readonly TimeSpan CounterLifetime = TimeSpan.FromMinutes(10);

    /// <summary>INCR only an existing counter: a missing one is loaded from the database on the next read.</summary>
    private const string IncrementIfExists = "if redis.call('EXISTS', KEYS[1]) == 1 then return redis.call('INCR', KEYS[1]) end return -1";

    public async Task<long> GetCounterAsync(string name, Func<CancellationToken, Task<long>> load, CancellationToken cancellationToken)
    {
        var key = CounterKey(name);
        try
        {
            var cached = await redis.GetDatabase().StringGetAsync(key);
            if (cached.HasValue)
            {
                return (long)cached;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Redis read failed for counter {Name}; counting in the database", name);
            return await load(cancellationToken);
        }

        var own = new Lazy<Task<object?>>(() => LoadCounterAsync(key, load));
        var pending = _inFlight.GetOrAdd(key, own);
        return ReferenceEquals(pending, own)
            ? (long)(await pending.Value)!
            : (long)(await pending.Value.WaitAsync(cancellationToken))!;
    }

    public async Task IncrementCounterAsync(string name)
    {
        try
        {
            await redis.GetDatabase().ScriptEvaluateAsync(IncrementIfExists, [CounterKey(name)]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not increment counter {Name}; it is recounted within {Lifetime}", name, CounterLifetime);
        }
    }

    /// <summary>
    /// SET NX: if an increment created the value meanwhile, it is newer than this count and wins. A count read just before
    /// a commit can be one short until the key expires; for a "threads in total" figure that is acceptable.
    /// </summary>
    private async Task<object?> LoadCounterAsync(string key, Func<CancellationToken, Task<long>> load)
    {
        try
        {
            var value = await load(CancellationToken.None);
            try
            {
                await redis.GetDatabase().StringSetAsync(key, value, CounterLifetime, When.NotExists);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Redis write failed for {Key}", key);
            }

            return value;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private static string CounterKey(string name) => $"comments:counter:{name}";

    private static string VersionKey(CacheRegion region) => $"comments:{region.Name}:version";
}

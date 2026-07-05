using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PdfEr.Core.Application.Interfaces;

namespace PdfEr.Infrastructure.Caching;

public sealed class FileCacheService : ICacheService, IDisposable
{
    private readonly string _basePath;
    private readonly TimeSpan _cleanupInterval;
    private readonly ILogger<FileCacheService> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _memoryCache = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public FileCacheService(string basePath, ILogger<FileCacheService> logger, int cleanupIntervalSeconds = 3600)
    {
        _basePath = Path.GetFullPath(basePath);
        _logger = logger;
        _cleanupInterval = TimeSpan.FromSeconds(cleanupIntervalSeconds);
        Directory.CreateDirectory(_basePath);
        _cleanupTimer = new Timer(_ => ClearExpired(), null, _cleanupInterval, _cleanupInterval);
    }

    public string GetCachePath(string key)
    {
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_basePath, safeKey);
    }

    public bool TryGetValue<T>(string key, out T? value) where T : class
    {
        if (_memoryCache.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            value = (T)entry.Data;
            return true;
        }

        var filePath = GetCachePath(key);
        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var data = System.Text.Json.JsonSerializer.Deserialize<T>(json);
                if (data != null)
                {
                    _memoryCache[key] = new CacheEntry(data, DateTime.UtcNow.Add(_cleanupInterval));
                    value = data;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read cache entry: {Key}", key);
                TryDelete(filePath);
            }
        }

        value = null;
        return false;
    }

    public void SetValue<T>(string key, T value, TimeSpan? expiry = null) where T : class
    {
        var expiryTime = DateTime.UtcNow.Add(expiry ?? _cleanupInterval);
        _memoryCache[key] = new CacheEntry(value, expiryTime);

        var filePath = GetCachePath(key);
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(value);
            var tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write cache entry: {Key}", key);
        }
    }

    public bool Remove(string key)
    {
        _memoryCache.TryRemove(key, out _);
        return TryDelete(GetCachePath(key));
    }

    public void ClearExpired()
    {
        foreach (var kvp in _memoryCache)
        {
            if (kvp.Value.IsExpired)
                _memoryCache.TryRemove(kvp.Key, out _);
        }

        try
        {
            foreach (var file in Directory.GetFiles(_basePath))
            {
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (DateTime.UtcNow - lastWrite > _cleanupInterval)
                    TryDelete(file);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up cache files");
        }
    }

    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch { return false; }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cleanupTimer.Dispose();
            _disposed = true;
        }
    }

    private sealed record CacheEntry(object Data, DateTime ExpiresAt)
    {
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }
}

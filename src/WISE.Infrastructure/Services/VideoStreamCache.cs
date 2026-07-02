using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WISE.Infrastructure.Services;

public class VideoCacheOptions
{
    public int MaxMb { get; set; } = 1024;
}

/// <summary>
/// ビデオファイルの先頭データをメモリにキャッシュする。
/// ネットワークドライブや HDD 上のファイルへのシークコストを削減する。
/// </summary>
public class VideoStreamCache
{
    private record CacheEntry(byte[] Data, DateTime LastAccess);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private long _currentBytes;
    private readonly long _maxBytes;
    private readonly ILogger<VideoStreamCache> _logger;

    // ファイル 1 本あたりのキャッシュ上限（先頭 N バイト）
    private const int PerFileMaxBytes = 32 * 1024 * 1024; // 32MB

    public VideoStreamCache(IOptions<VideoCacheOptions> options, ILogger<VideoStreamCache> logger)
    {
        _maxBytes = (long)options.Value.MaxMb * 1024 * 1024;
        _logger = logger;
        _logger.LogInformation("[VideoCache] Initialized. MaxMb={MaxMb}", options.Value.MaxMb);
    }

    /// <summary>
    /// キャッシュから指定範囲のデータを取得する。
    /// キャッシュミスの場合は null を返す（呼び出し元がファイルから直接読む）。
    /// </summary>
    public byte[]? TryGetRange(string filePath, long offset, int count)
    {
        if (!_cache.TryGetValue(filePath, out var entry)) return null;

        // 要求範囲がキャッシュ内に収まっているか
        if (offset < 0 || offset + count > entry.Data.Length) return null;

        // LRU: 最終アクセス時刻を更新
        _cache[filePath] = entry with { LastAccess = DateTime.UtcNow };

        var result = new byte[count];
        Buffer.BlockCopy(entry.Data, (int)offset, result, 0, count);
        return result;
    }

    /// <summary>
    /// ファイルの先頭データをキャッシュに登録する（既にある場合はスキップ）。
    /// </summary>
    public async Task WarmAsync(string filePath, CancellationToken ct = default)
    {
        if (_cache.ContainsKey(filePath)) return;

        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists) return;

            var readBytes = (int)Math.Min(info.Length, PerFileMaxBytes);

            // キャッシュ容量が足りない場合、古いエントリを削除
            while (_currentBytes + readBytes > _maxBytes && _cache.Count > 0)
                Evict();

            if (_currentBytes + readBytes > _maxBytes) return; // まだ足りなければスキップ

            var buffer = new byte[readBytes];
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, useAsync: true);
            var totalRead = 0;
            while (totalRead < readBytes)
            {
                var n = await fs.ReadAsync(buffer.AsMemory(totalRead, readBytes - totalRead), ct);
                if (n == 0) break;
                totalRead += n;
            }

            if (totalRead == 0) return;

            var data = totalRead == buffer.Length ? buffer : buffer[..totalRead];
            if (_cache.TryAdd(filePath, new CacheEntry(data, DateTime.UtcNow)))
            {
                Interlocked.Add(ref _currentBytes, data.Length);
                _logger.LogDebug("[VideoCache] Cached {Mb:F1}MB of {File}",
                    data.Length / 1048576.0, Path.GetFileName(filePath));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[VideoCache] Warm failed for {File}", filePath);
        }
    }

    public void Invalidate(string filePath) => Remove(filePath);

    private void Evict()
    {
        // 最も古いエントリを削除
        string? oldest = null;
        var oldestTime = DateTime.MaxValue;
        foreach (var kv in _cache)
        {
            if (kv.Value.LastAccess < oldestTime)
            {
                oldestTime = kv.Value.LastAccess;
                oldest = kv.Key;
            }
        }
        if (oldest != null) Remove(oldest);
    }

    private void Remove(string filePath)
    {
        if (_cache.TryRemove(filePath, out var removed))
        {
            Interlocked.Add(ref _currentBytes, -removed.Data.Length);
            _logger.LogDebug("[VideoCache] Evicted {File}", Path.GetFileName(filePath));
        }
    }
}

using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace TaskbarMonitorWinFormX.Models;

public sealed record MonitoringOptions
{
    public int UpdateIntervalMs { get; init; } = 1000;
    public int HistorySize { get; init; } = 20;
    public int NetworkThresholdMbps { get; init; } = 10;
    public int IconSize { get; init; } = 16;
    public int GraphWidth { get; init; } = 14;
    public int GraphHeight { get; init; } = 14;
    public int GraphPadding { get; init; } = 1;
    public int IconCacheBucketSize { get; init; } = 5; // Cache icons in 5% buckets
}

public readonly record struct SystemMetrics(
    int CpuUsagePercent,
    int RamUsagePercent,
    int NetworkSpeedMbps,
    int AverageNetworkSpeedMbps,
    DateTime Timestamp)
{
    public static SystemMetrics Empty => new(0, 0, 0, 0, DateTime.UtcNow);

    public string TooltipText =>
        $"CPU: {CpuUsagePercent}% | RAM: {RamUsagePercent}% | Net: {NetworkSpeedMbps} MB/s";
}

// High-performance metrics history using span operations
public sealed class MetricsHistory : IDisposable
{
    private readonly int[] _values;
    private readonly int _maxSize;
    private int _currentIndex;
    private int _count;
    private int _cachedSum = 0;
    private bool _sumInvalid = true;

    public MetricsHistory(int maxSize)
    {
        _maxSize = maxSize;
        _values = GC.AllocateUninitializedArray<int>(maxSize, pinned: true);
        _currentIndex = 0;
        _count = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(int value)
    {
        // Remove old value from sum if buffer is full
        if (_count == _maxSize)
        {
            _cachedSum -= _values[_currentIndex];
        }

        _values[_currentIndex] = value;
        _cachedSum += value;

        _currentIndex = (_currentIndex + 1) % _maxSize;
        if (_count < _maxSize) _count++;

        _sumInvalid = false;
    }

    public int Average => _count > 0 ? _cachedSum / _count : 0;

    public int Current => _count > 0 ? _values[(_currentIndex - 1 + _maxSize) % _maxSize] : 0;

    // Zero-allocation access to values
    public ReadOnlySpan<int> ValuesSpan => _values.AsSpan(0, _count);

    // For compatibility with existing code that expects array
    public int[] Values => ValuesSpan.ToArray();

    public int Count => _count;

    // High-performance rendering method
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CopyValuesForRendering(Span<int> destination, int columns)
    {
        if (_count == 0) return;

        var source = ValuesSpan;
        var sourceLength = source.Length;
        var copyCount = Math.Min(columns, sourceLength);

        // Copy the most recent values (tail-aligned)
        var startIndex = Math.Max(0, sourceLength - copyCount);
        source.Slice(startIndex, copyCount).CopyTo(destination);
    }

    public void Dispose()
    {
        // Values array will be collected by GC
        GC.SuppressFinalize(this);
    }
}

// High-performance object pool for bitmaps
public sealed class BitmapPool : IDisposable
{
    private readonly ObjectPool<Bitmap> _pool;
    private readonly int _size;

    public BitmapPool(int size, int initialCapacity = 8)
    {
        _size = size;
        _pool = new DefaultObjectPool<Bitmap>(
            new BitmapPoolPolicy(size),
            initialCapacity);
    }

    public PooledBitmap Rent() => new(_pool.Get(), _pool);

    public void Dispose()
    {
        if (_pool is IDisposable disposable)
            disposable.Dispose();
    }

    private sealed class BitmapPoolPolicy : IPooledObjectPolicy<Bitmap>
    {
        private readonly int _size;

        public BitmapPoolPolicy(int size) => _size = size;

        public Bitmap Create() => new(_size, _size);

        public bool Return(Bitmap obj)
        {
            // Clear the bitmap for reuse
            using var g = Graphics.FromImage(obj);
            g.Clear(Color.Transparent);
            return true;
        }
    }
}

// RAII wrapper for pooled bitmaps
public readonly struct PooledBitmap : IDisposable
{
    private readonly Bitmap _bitmap;
    private readonly ObjectPool<Bitmap> _pool;

    public PooledBitmap(Bitmap bitmap, ObjectPool<Bitmap> pool)
    {
        _bitmap = bitmap;
        _pool = pool;
    }

    public Bitmap Bitmap => _bitmap;

    public void Dispose() => _pool.Return(_bitmap);
}

// Icon cache for reducing GC pressure
public sealed class IconCache : IDisposable
{
    private readonly Dictionary<IconCacheKey, Icon> _cache = new();
    private readonly object _lock = new();
    private readonly int _bucketSize;

    public IconCache(int bucketSize = 5)
    {
        _bucketSize = bucketSize;
    }

    public Icon GetOrCreate(int value, IconType type, Func<Icon> factory)
    {
        var key = new IconCacheKey(RoundToBucket(value), type);

        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cachedIcon))
                return cachedIcon;

            var newIcon = factory();
            _cache[key] = newIcon;
            return newIcon;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int RoundToBucket(int value) => (value / _bucketSize) * _bucketSize;

    public void Clear()
    {
        lock (_lock)
        {
            foreach (var icon in _cache.Values)
                icon.Dispose();
            _cache.Clear();
        }
    }

    public void Dispose()
    {
        Clear();
        GC.SuppressFinalize(this);
    }

    private readonly record struct IconCacheKey(int Value, IconType Type);
}

public enum IconType : byte
{
    Cpu = 0,
    Ram = 1,
    Network = 2
}

public record NetworkStats(float UploadBps, float DownloadBps, int TotalBps, string InterfaceName);

// Object pool interfaces (simplified version of Microsoft.Extensions.ObjectPool)
public interface IPooledObjectPolicy<T>
{
    T Create();
    bool Return(T obj);
}

public interface ObjectPool<T> where T : class
{
    T Get();
    void Return(T obj);
}

public sealed class DefaultObjectPool<T> : ObjectPool<T>, IDisposable where T : class
{
    private readonly IPooledObjectPolicy<T> _policy;
    private readonly ConcurrentQueue<T> _objects = new();
    private readonly int _maxCapacity;

    public DefaultObjectPool(IPooledObjectPolicy<T> policy, int maxCapacity = 32)
    {
        _policy = policy;
        _maxCapacity = maxCapacity;
    }

    public T Get()
    {
        return _objects.TryDequeue(out var obj) ? obj : _policy.Create();
    }

    public void Return(T obj)
    {
        if (_policy.Return(obj) && _objects.Count < _maxCapacity)
        {
            _objects.Enqueue(obj);
        }
    }

    public void Dispose()
    {
        while (_objects.TryDequeue(out var obj))
        {
            if (obj is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
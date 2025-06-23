using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace TaskbarMonitorWinFormX.Models;

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
// High-performance object pool for bitmaps - FIXED VERSION
public sealed class BitmapPool : IDisposable
{
    private readonly ObjectPool<Bitmap> _pool;
    private readonly int _size;
    private readonly object _disposalLock = new();
    private bool _disposed;

    public BitmapPool(int size, int initialCapacity = 8)
    {
        _size = size;
        _pool = new DefaultObjectPool<Bitmap>(
            new BitmapPoolPolicy(size),
            Math.Min(initialCapacity, 32)); // Cap the initial capacity
    }

    public PooledBitmap Rent()
    {
        lock (_disposalLock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BitmapPool));

            return new PooledBitmap(_pool.Get(), _pool);
        }
    }

    public void Dispose()
    {
        lock (_disposalLock)
        {
            if (_disposed) return;
            _disposed = true;

            if (_pool is IDisposable disposable)
                disposable.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private sealed class BitmapPoolPolicy : IPooledObjectPolicy<Bitmap>
    {
        private readonly int _size;

        public BitmapPoolPolicy(int size) => _size = size;

        public Bitmap Create() => new(_size, _size);

        public bool Return(Bitmap obj)
        {
            if (obj == null || obj.Width != _size || obj.Height != _size)
                return false;

            try
            {
                // Clear the bitmap for reuse with safer approach
                using var g = Graphics.FromImage(obj);
                g.Clear(Color.Transparent);
                return true;
            }
            catch
            {
                // If clearing fails, don't return to pool
                obj?.Dispose();
                return false;
            }
        }
    }
}

// RAII wrapper for pooled bitmaps - FIXED VERSION
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

    public void Dispose()
    {
        try
        {
            _pool?.Return(_bitmap);
        }
        catch
        {
            // If return fails, dispose the bitmap directly
            _bitmap?.Dispose();
        }
    }
}

// Icon cache for reducing GC pressure
public sealed class IconCache : IDisposable
{
    private readonly Dictionary<IconCacheKey, Icon> _cache = new();
    private readonly object _lock = new();
    private readonly int _bucketSize;
    private bool _disposed;

    // Limit cache size to prevent memory issues
    private const int MaxCacheSize = 100;

    public IconCache(int bucketSize = 5)
    {
        _bucketSize = bucketSize;
    }

    public Icon GetOrCreate(int value, IconType type, Func<Icon> factory)
    {
        var key = new IconCacheKey(RoundToBucket(value), type);

        lock (_lock)
        {
            if (_disposed)
                return factory(); // Don't cache if disposed

            if (_cache.TryGetValue(key, out var cachedIcon))
                return cachedIcon;

            // Limit cache size
            if (_cache.Count >= MaxCacheSize)
            {
                // Remove oldest entries (simple LRU approximation)
                var keysToRemove = _cache.Keys.Take(_cache.Count - MaxCacheSize + 10).ToList();
                foreach (var keyToRemove in keysToRemove)
                {
                    if (_cache.Remove(keyToRemove, out var iconToDispose))
                    {
                        try
                        {
                            iconToDispose?.Dispose();
                        }
                        catch
                        {
                            // Ignore disposal errors
                        }
                    }
                }
            }

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
            {
                try
                {
                    icon?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors during clearing
                }
            }
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
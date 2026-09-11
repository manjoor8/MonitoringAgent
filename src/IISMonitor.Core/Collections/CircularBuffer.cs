namespace IISMonitor.Core.Collections;

/// <summary>
/// Pre-allocated, thread-safe circular buffer for storing rolling baseline metrics per Sections 3 and 22.
/// Overwrites oldest samples when capacity is reached, guaranteeing bounded memory usage.
/// </summary>
public class CircularBuffer<T>
{
    private readonly T[] _buffer;
    private readonly object _syncRoot = new();
    private int _head;
    private int _tail;
    private int _count;

    public int Capacity { get; }
    public int Count
    {
        get
        {
            lock (_syncRoot) return _count;
        }
    }

    public CircularBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");

        Capacity = capacity;
        _buffer = new T[capacity];
    }

    /// <summary>
    /// Appends a new item to the buffer, overwriting the oldest item if capacity is reached.
    /// </summary>
    public void Push(T item)
    {
        lock (_syncRoot)
        {
            _buffer[_tail] = item;
            _tail = (_tail + 1) % Capacity;

            if (_count == Capacity)
            {
                _head = (_head + 1) % Capacity;
            }
            else
            {
                _count++;
            }
        }
    }

    /// <summary>
    /// Returns a chronological snapshot of items (oldest to newest) without mutating the buffer.
    /// </summary>
    public T[] Snapshot()
    {
        lock (_syncRoot)
        {
            if (_count == 0)
                return Array.Empty<T>();

            var result = new T[_count];

            if (_head < _tail)
            {
                Array.Copy(_buffer, _head, result, 0, _count);
            }
            else
            {
                int firstChunk = Capacity - _head;
                Array.Copy(_buffer, _head, result, 0, firstChunk);
                Array.Copy(_buffer, 0, result, firstChunk, _tail);
            }

            return result;
        }
    }

    /// <summary>
    /// Returns the most recent N items, or all items if N > Count.
    /// </summary>
    public T[] TakeLatest(int count)
    {
        lock (_syncRoot)
        {
            if (_count == 0 || count <= 0)
                return Array.Empty<T>();

            int takeCount = Math.Min(count, _count);
            var snapshot = Snapshot();
            var result = new T[takeCount];
            Array.Copy(snapshot, snapshot.Length - takeCount, result, 0, takeCount);
            return result;
        }
    }

    /// <summary>
    /// Clears the buffer.
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }
}

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using CommunityToolkit.HighPerformance.Buffers;
using CommunityToolkit.HighPerformance.Enumerables;
using DotNext.Buffers;
using DotNext.Runtime;

namespace NameSequence;

public enum SlotType 
{
    None,
    Value,
    Padding
}

[StructLayout(LayoutKind.Auto, Pack = sizeof(short), Size = 32)]
public struct Slot : IEquatable<Slot>, IComparable<Slot>
{
    public int NewStart;
    public int Origin;
    public readonly int Length;
    public readonly SlotType Kind;
    public readonly int Id;

    public Slot(int origin, int length, SlotType kind, short id, int spanIdx)
    {
        NewStart = -1;
        Length = length;
        Kind = kind;
        Id = id;
        Origin = origin;
    }

    public override string ToString()
    {
        return $"ID: {Id} - TYPE: <{Kind}> - LENGTH: {Length}  |  Start: {Origin}, Swapped to: {NewStart}";
    }

    public override bool Equals(object? obj)
    {
        return obj is Slot other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(NewStart, Origin, Length, (int)Kind, Id);
    }

    public bool Equals(Slot other)
    {
        return Origin == other.Origin;
    }

    public int CompareTo(Slot other)
    {
        return Id.CompareTo(other.Id);
    }

    [UnscopedRef] public ref int Position => ref NewStart == -1 ? ref Origin : ref NewStart;
}

public readonly struct MemoryRentalSize
{
    public readonly int MaxStackSize;
    public readonly int Remain;

    public MemoryRentalSize(int maxStackSize, int remain)
    {
        MaxStackSize = maxStackSize;
        Remain = remain;
    }
}

public ref struct MemorySplit<T> where T : unmanaged, INumber<T>
{
    private MemoryRental<T> _m0, _m1;
    private Span<T> _s0, _s1;
    private int position, _tmpDestStart = 0;
    private bool _usePooledMem;

    [UnscopedRef] public Span<T> Span => !_usePooledMem ? _s0 : _s1;

    public int RestToRent { get; private set; }

    public const int stackallocThreshold = 2048 * 1;

    /// <summary>
    /// <param name="rest">this variable should be always >= than the stackalloc-buffer you pass!</param>
    /// </summary>
    /// <param name="stackBuffer">A stack-allocated buffer (safe amount is ~2KB) which at best, has the same size as "count" </param>
    /// <param name="rest"></param>
    public MemorySplit(Span<T> stackBuffer, int rest)
    {
        position = 0;
        RestToRent = rest;
        _usePooledMem = false;

        _m0 = new MemoryRental<T>(stackBuffer, stackBuffer.Length);
        _s0 = _m0.Span;

        if (rest > 0)
        {
            _m1 = new MemoryRental<T>(rest);
            _s1 = _m1.Span;
        }
    }

    public static MemoryRentalSize EnsureCapacity(int initialSize)
    {
        int diff = stackallocThreshold - initialSize;
        int properSize = (diff >= 0) ? initialSize : stackallocThreshold;
        int remain = diff < 0 ? -diff : 0;
        MemoryRentalSize size = new(properSize, remain);
        return size;
    }

    public void Write(Span<T> data)
    {
        if ((position + data.Length) > Span.Length)
        {
            _usePooledMem = true;
            position = 0;
        }

        data.CopyTo(Span[position..]);
        position += data.Length;
    }

    public void CopyTo(Span<T> dest)
    {
        _s0.CopyTo(dest);
        _s1.CopyTo(dest[_s0.Length..]);
    }

    public void Dispose()
    {
        _m0.Dispose();
        _m1.Dispose();
        RestToRent = 0;
        //Console.WriteLine("total count after returning to the pool is?  " + RestToRent);
    }
}


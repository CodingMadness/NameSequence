using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNext.Reflection;
using DotNext.Runtime;

namespace NameSequence;

public static class Utility
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<T> AsSpan<T>(this ReadOnlySpan<T> span)
        => span.Length > 0 ? MemoryMarshal.CreateSpan(ref Unsafe.AsRef(span[0]), span.Length) : Span<T>.Empty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<T> AsReadOnlySpan<T>(this Span<T> span)
        => span.Length > 0 ? MemoryMarshal.CreateSpan(ref Unsafe.AsRef(span[0]), span.Length) : Span<T>.Empty;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Move<T>(this ReadOnlySpan<T> input, ReadOnlySpan<T> sliceToMove, Index index)
        where T: struct, IEquatable<T>
    {
        Range copyArea = index.IsFromEnd ? index.. : ..index;

        var dest = input[copyArea].AsSpan();

        if (sliceToMove.Length <= dest.Length)
        {
            sliceToMove.CopyTo(dest);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Move<T>(this ReadOnlySpan<T> input, ReadOnlySpan<T> sliceToMove, int dest)
        where T: struct, IEquatable<T>
    {
        var areaToCopyInto = input.Slice(dest, sliceToMove.Length);
        sliceToMove.CopyTo(areaToCopyInto.AsSpan());
    }
    
    /// <summary>
    /// Swaps 2 different slices within a span and returns back to the caller if x or y is greater!
    /// </summary>
    /// <param name="input">the base span to modify</param>
    /// <param name="x">the first in order, means x comes before y in the span</param>
    /// <param name="y">the last in order, means y comes before x in the span</param>
    /// <param name="emptyPlaceHolder">An object used to describe a slice within the span which shall have a certain value, this is an
    /// implementation detail and shall not be public to the caller!</param>
    /// <typeparam name="T">The type of the span</typeparam>
    /// <returns>gives back to the caller if either x or y was greater when compared to each-other in length!
    /// since we use the "x - y" comparison, the result: -1 means that y > x and 1 means x > y !</returns>
    public static short Swap<T>(this ReadOnlySpan<T> input, scoped ReadOnlySpan<T> x, scoped ReadOnlySpan<T> y, T emptyPlaceHolder=default) 
        where T: unmanaged, IEquatable<T>, IComparable<T>
    {
        int yLoc = input.IndexOf(y);
        int xLoc = input.IndexOf(x);

        // bool areBothTrulyTheSame = x.SequenceEqual(y);
        
        if (xLoc == yLoc)
            return 0;

        int minStart, maxStart, maxEnd;
        scoped ReadOnlySpan<T> first, last;
        int beginOfFirst, beginOfLast;
        
        if (yLoc > xLoc)
        {
            minStart = xLoc;
            maxStart = xLoc + x.Length;
            
            maxEnd = yLoc + y.Length;
            first = x;
            last = y;
            beginOfFirst = xLoc;
            beginOfLast = yLoc;
        }
        else
        {
            minStart = yLoc;
            maxStart = yLoc + y.Length;
            maxEnd = xLoc + x.Length;
            first = y;
            last = x;
            beginOfFirst = yLoc;
            beginOfLast = xLoc;
        }
        
        int diffToMove = first.Length - last.Length;

        //compare the length and decide which one to copy where!
        if (first.Length > last.Length)
        {
            //store a copy of the smaller one
            scoped Span<T> smallOne = stackalloc T[last.Length];
            last.CopyTo(smallOne);
            
            //store a copy of the larger one
            scoped Span<T> largeOne = stackalloc T[first.Length];
            first.CopyTo(largeOne);
            
            //we have to clear the "smallOne" from the input, before we can copy in order for .IndexOf() 
            //to find the match of the "largeOne" because otherwise there will be 2x
            input.Slice(beginOfFirst, first.Length).AsSpan().Fill(emptyPlaceHolder);
            smallOne.CopyTo(input.Slice(beginOfFirst, first.Length).AsSpan());
            var tmp = input[minStart..maxEnd];
            int lastIdxOfEmpty = tmp.LastIndexOf(emptyPlaceHolder);
            tmp.Move(tmp[(lastIdxOfEmpty+1)..], smallOne.Length);
            largeOne.CopyTo(input.Slice(beginOfLast - diffToMove, largeOne.Length).AsSpan());
            
            return (short)(largeOne.Length - smallOne.Length);
        }
        else if (first.Length == last.Length)
        {
            //store a copy of the smaller one
            scoped Span<T> lastCopy = stackalloc T[last.Length];
            last.CopyTo(lastCopy);
            
            first.CopyTo(input.Slice(beginOfLast, first.Length).AsSpan());
            lastCopy.CopyTo(input.Slice(beginOfFirst, lastCopy.Length).AsSpan());

            return 0;
        }
        else
        {
            //store a copy of the smaller one
            scoped Span<T> smallOne = stackalloc T[first.Length];
            first.CopyTo(smallOne);
            
            //store a copy of the larger one
            scoped Span<T> largeOne = stackalloc T[last.Length];
            last.CopyTo(largeOne);

            var min2MaxSliceExceptDiff = input[maxStart..maxEnd][..^(smallOne.Length)];
            
            int absDiff = Math.Abs(diffToMove);
            var exceptAbsDiff = min2MaxSliceExceptDiff[..^absDiff];
            min2MaxSliceExceptDiff.Move(exceptAbsDiff, ^exceptAbsDiff.Length);
            largeOne.CopyTo(input[beginOfFirst..].AsSpan());
            smallOne.CopyTo(input.Slice(beginOfLast + absDiff, largeOne.Length).AsSpan());
            
            return (short)(smallOne.Length - largeOne.Length);
        }
    }
    
     public static short Swap(this ReadOnlySpan<char> input, scoped ref Slot x, scoped ref Slot y)
     {
         int yLoc = y.Position;
         int xLoc = x.Position;

        if (xLoc == yLoc)
            return 0;

        int minStart, maxStart, maxEnd;
        scoped ReadOnlySpan<char> first, last;
        int beginOfFirst, beginOfLast;
        
        if (yLoc > xLoc)
        {
            minStart = xLoc;
            maxStart = xLoc + x.Length;
            maxEnd = yLoc + y.Length;
            first = input.Slice(ref x);
            last = input.Slice(ref y);
            beginOfFirst = xLoc;
            beginOfLast = yLoc;
        }
        else
        {
            minStart = yLoc;
            maxStart = yLoc + y.Length;
            maxEnd = xLoc + x.Length;
            first = input.Slice(ref y);
            last = input.Slice(ref x);
            beginOfFirst = yLoc;
            beginOfLast = xLoc;
        }
        
        int diffToMove = first.Length - last.Length;

        //compare the length and decide which one to copy where!
        if (first.Length > last.Length)
        {
            //store a copy of the smaller one
            scoped Span<char> smallOne = stackalloc char[last.Length];
            last.CopyTo(smallOne);
            
            //store a copy of the larger one
            scoped Span<char> largeOne = stackalloc char[first.Length];
            first.CopyTo(largeOne);
            
            //we have to clear the "smallOne" from the input, before we can copy in order for .IndexOf() 
            //to find the match of the "largeOne" because otherwise there will be 2x
            input.Slice(beginOfFirst, first.Length).AsSpan().Fill(' ');
            smallOne.CopyTo(input.Slice(beginOfFirst, first.Length).AsSpan());
            var tmp = input[minStart..maxEnd];
            int lastIdxOfEmpty = tmp.LastIndexOf(' ');
            tmp.Move(tmp[(lastIdxOfEmpty+1)..], smallOne.Length);
            largeOne.CopyTo(input.Slice(beginOfLast - diffToMove, largeOne.Length).AsSpan());
            
            return (short)(largeOne.Length - smallOne.Length);
        }
        else if (first.Length == last.Length)
        {
            //store a copy of the smaller one
            scoped Span<char> lastCopy = stackalloc char[last.Length];
            last.CopyTo(lastCopy);
            
            first.CopyTo(input.Slice(beginOfLast, first.Length).AsSpan());
            lastCopy.CopyTo(input.Slice(beginOfFirst, lastCopy.Length).AsSpan());

            return 0;
        }
        else
        {
            //store a copy of the smaller one
            scoped Span<char> smallOne = stackalloc char[first.Length];
            first.CopyTo(smallOne);
            
            //store a copy of the larger one
            scoped Span<char> largeOne = stackalloc char[last.Length];
            last.CopyTo(largeOne);

            var min2MaxSliceExceptDiff = input[maxStart..maxEnd][..^(smallOne.Length)];
            
            int absDiff = Math.Abs(diffToMove);
            var exceptAbsDiff = min2MaxSliceExceptDiff[..^absDiff];
            min2MaxSliceExceptDiff.Move(exceptAbsDiff, ^exceptAbsDiff.Length);
            largeOne.CopyTo(input[beginOfFirst..].AsSpan());
            smallOne.CopyTo(input.Slice(beginOfLast + absDiff, largeOne.Length).AsSpan());
            
            return (short)(smallOne.Length - largeOne.Length);
        }
    }
    
    /// <summary>
    /// Swaps 2 different slices within a span and returns back to the caller if x or y is greater!
    /// </summary>
    /// <param name="input">the base span to modify</param>
    /// <param name="x">the first in order, means x comes before y in the span</param>
    /// <param name="y">the last in order, means y comes before x in the span</param>
    /// <typeparam name="T">The type of the span</typeparam>
    /// <returns>gives back to the caller if either x or y was greater when compared to each-other in length!
    /// since we use the "x - y" comparison, the result: -1 means that y > x and 1 means x > y !</returns>
    
    public static short Swap(this ReadOnlySpan<char> input, ReadOnlySpan<char> x, ReadOnlySpan<char> y)
        => input.Swap(x, y, ' ');
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<char> Slice(this ReadOnlySpan<char> input, ref Slot slot) =>
        input.Slice(slot.Position, slot.Length);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<char> Slice(this Span<char> input, ref Slot slot) =>
        input.Slice(slot.Position, slot.Length);
}
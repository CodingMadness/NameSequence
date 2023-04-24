using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance.Buffers;
using DotNext.Buffers;
using NoAlloq;
using DotNext.Reflection;
using DotNetMemoryBuffer = CommunityToolkit.HighPerformance.Buffers.MemoryOwner<NameSequence.Slot>;

namespace NameSequence;

public ref struct FastSpanEnumerator<TItem>
{
    private ref TItem _currentItem;
    private readonly ref TItem _lastItemOffsetByOne;

    public FastSpanEnumerator(ReadOnlySpan<TItem> span)
        : this(ref MemoryMarshal.GetReference(span), span.Length)
    {
    }

    public FastSpanEnumerator(Span<TItem> span) :
        this(ref MemoryMarshal.GetReference(span), span.Length)
    {
    }

    private FastSpanEnumerator(ref TItem item, nint length)
    {
        _currentItem = ref Unsafe.Subtract(ref item, 1);
        _lastItemOffsetByOne = ref Unsafe.Add(ref _currentItem, length + 1);
    }

    [UnscopedRef] public ref TItem Current => ref _currentItem;

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public bool MoveNext()
    {
        _currentItem = ref Unsafe.Add(ref _currentItem, 1);
        return Unsafe.IsAddressLessThan(ref _currentItem,
            ref _lastItemOffsetByOne); //!Unsafe.AreSame(ref _currentItem, ref _lastItemOffsetByOne);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public bool MoveBack()
    {
        _currentItem = ref Unsafe.Subtract(ref _currentItem, 1);
        return Unsafe.IsAddressLessThan(ref _currentItem, ref _lastItemOffsetByOne);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [UnscopedRef]
    public ref FastSpanEnumerator<TItem> GetEnumerator()
    {
        return ref this;
    }
}

public ref struct NameEnumerator<T>
{
    private Span<char> _current;
    private int _idx;
    private static DynamicInvoker? cachedFieldInvoker;
    private static Func<T, string>? GetGetMethod;

    public readonly Span<Slot> SafeLayout;
    internal MemoryRental<Slot> Layout;
    internal MemoryRental<char> DebugBuffer;
    internal readonly int PadsLen;
    public readonly int ValuesLen;
    public int Length => ValuesLen + PadsLen;
    public int MembersCount { get; }
    public Span<char> Current => _current;
    public ReadOnlySpan<char> ArrayLayout, OnlyValues, FirstValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetMemberValue(in T item)
    {
        return cachedFieldInvoker is not null
            ? (string)cachedFieldInvoker.Invoke(item, Span<object?>.Empty)!
            : GetGetMethod!.Invoke(item);
    }

    public NameEnumerator(T[] classes, string memberName, bool isProperty, bool areStringsFromArray)
    {
        //Find the string field of each class inside the array via "DotNext.Reflection" then store 
        //inside the string"StringPool" 
        var bindings = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        if (isProperty)
        {
            var property = Type<T>.Property<string>.GetGetter(memberName);
            GetGetMethod = property!.Unreflect<Func<T, string>>()!;
        }
        else
        {
            cachedFieldInvoker = typeof(T).GetField(memberName, bindings)!.Unreflect(bindings);
        }

        MembersCount = classes.Length;
        Layout = new((MembersCount - 0) * 2);
        short id = 0;
        int segmentLen = 0;
        int valueIdx = 0;
        ref char veryBeginning = ref Unsafe.NullRef<char>();
        var layoutSpan = Layout.Span;
        SafeLayout = layoutSpan;

        Span<Slot> FillLastValueSlot(Span<Slot> layoutSpan)
        {
            //skip the 1. Segment because this one is not allowed to be touched!! Due to Segmentation-fault/AcessViolation!
            //we add these 2 lines of code because the last 2 slots cannot be filled, and since the last segment 
            //in the string does only have a <value> we fill it explicitly to avoid checking in the for-loop for it!
            string lastValue = GetMemberValue(classes[^1]);
            Index end = (areStringsFromArray ? ^2 : ^4);
            layoutSpan[end] = new(segmentLen, lastValue.Length, SlotType.Value, id, end.Value);
            Index end2 = new(end.Value - 1, true);
            return layoutSpan[..end2];
        }

        FastSpanEnumerator<T> enumerator = new(classes.AsSpan()[..^1]);
        enumerator.MoveNext();
        FirstValue = GetMemberValue(enumerator.Current);
        veryBeginning = ref Unsafe.AsRef(FirstValue.GetPinnableReference());
        var test2 = MemoryMarshal.CreateSpan(ref veryBeginning, 1024 * 25);
        DebugBuffer = new(test2.Length);
        test2.CopyTo(DebugBuffer.Span);
        int idxOfX = -1;

        do
        {
            //get all the needed refs!
            ref Slot valueSlot = ref layoutSpan[valueIdx];
            ref Slot padSlot = ref layoutSpan[valueIdx + 1];
            T item = enumerator.Current;
            enumerator.MoveNext();
            T nextItem = enumerator.Current;
            enumerator.MoveBack();
            string firstStr = GetMemberValue(item);
            string nextStr = GetMemberValue(nextItem);
            ref char first = ref Unsafe.AsRef(firstStr.GetPinnableReference());
            ref char next = ref Unsafe.AsRef(nextStr.GetPinnableReference());
            ref char endOfFirst = ref Unsafe.Add(ref first, firstStr.Length);
            endOfFirst = (char)(id + 48);
            int paddingLen = (int)(Unsafe.ByteOffset(ref endOfFirst, ref next)) / sizeof(char);

            //build the slots!
            valueSlot = new(segmentLen, firstStr.Length + 1, SlotType.Value, id, idxOfX + 1);
            padSlot = new(segmentLen + valueSlot.Length, paddingLen - 1, SlotType.Padding, id, idxOfX + 2);
            //inc all needed data!
            DebugBuffer.Span.Slice(ref padSlot).Fill((char)(id + 48));

            idxOfX += 2;
            ValuesLen += valueSlot.Length;
            segmentLen += valueSlot.Length + padSlot.Length;
            valueIdx += 2;
            id++;
        } while (enumerator.MoveNext());

        SafeLayout = FillLastValueSlot(layoutSpan);
        ArrayLayout = MemoryMarshal.CreateSpan(ref veryBeginning, segmentLen + SafeLayout[^1].Length);
        PadsLen = ArrayLayout.Length - ValuesLen;
    }

    public bool MoveBack()
    {
        //use bool for switch oin which segment to iterate over!
        if (_idx > 0)
        {
            var rented = Layout.Span;
            ref var slot = ref rented[_idx--];
            _current = ArrayLayout.Slice(ref slot).AsSpan();
            return true;
        }

        return false;
    }

    public bool MoveNext()
    {
        if (_idx < Layout.Span.Length)
        {
            var rented = Layout.Span;
            ref var slot = ref rented[_idx++];
            _current = ArrayLayout.Slice(ref slot).AsSpan();
            return true;
        }

        return false;
    }

    public void Reset()
    {
        _idx = 0;
    }

    public void Dispose()
    {
        DebugBuffer.Dispose();
        Layout.Dispose();
    }

    public readonly NameEnumerator<T> GetEnumerator()
    {
        return this;
    }
}

public ref struct NameSequence<T>
{
    private NameEnumerator<T> _nameEnumerator;
    public readonly ReadOnlySpan<char> Source;
    public ReadOnlySpan<char> OnlyValues { get; private set; }
    public MemoryRental<char> DebugBuffer;

    public NameSequence(T[] src, string nameOfMember, bool isProperty, bool areStringsFromArray)
    {
        _nameEnumerator = new NameEnumerator<T>(src, nameOfMember, isProperty, areStringsFromArray);
        Source = _nameEnumerator.ArrayLayout;
        DebugBuffer = _nameEnumerator.DebugBuffer.Span[..(_nameEnumerator.ValuesLen + _nameEnumerator.PadsLen)];
    }

    public void PrintAllSlots(Range r, SlotType onlyThisType)
    {
        for (int index = 0; index < _nameEnumerator.SafeLayout[r].Length; index++)
        {
            ref var slot = ref _nameEnumerator.SafeLayout[r][index];

            if (slot.Kind != onlyThisType)
                continue;

            var slice = DebugBuffer.Span.Slice(ref slot);
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write(slice.ToString());
            Console.ResetColor();
            Console.WriteLine("       with a length of:  " + slot.Length + " and with ID:  " + slot.Id);
        }

        Console.WriteLine();
    }

    public unsafe void BuildSequence()
    {
        void AdjustPositions(scoped Span<Slot> layout, short finalDirection, int startOfUpdate, int oneMoreEachCycle)
        {
            if (finalDirection == 0)
                return;

            int from = startOfUpdate;

            //the extra one will be ignored so in order to get the slots[to] we need to do +1
            int to = from + oneMoreEachCycle + 1;

            FastSpanEnumerator<Slot> enumerator = new(layout[from..to]);

            foreach (ref var slot in enumerator)
                slot.Position -= finalDirection;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SwapPositions(scoped ref Slot nextSegmentValue, scoped ref Slot currPadding)
        {
            (nextSegmentValue.NewStart, currPadding.Position) = (currPadding.Position, nextSegmentValue.NewStart);
            currPadding.NewStart = nextSegmentValue.Origin;
            currPadding.Origin = nextSegmentValue.NewStart;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SwapSlots(scoped ref Slot nextSegmentValue, scoped ref Slot currPadding)
        {
            (nextSegmentValue, currPadding) = (currPadding, nextSegmentValue);
        }

        scoped Span<Slot> layout = _nameEnumerator.SafeLayout;
        var tmpSrc = DebugBuffer.Span;
        int? startOfUpdate = null;

        void Swap(int slotIdx, scoped Span<Slot> layout, scoped Span<char> src)
        {
            //we always have to go 2x slotIdx, to find the value we wanna swap behind!
            int safeNextValueIdx = slotIdx * 2;
            startOfUpdate ??= safeNextValueIdx;
            ref Slot nextSegmentValue = ref layout[safeNextValueIdx];
            ref Slot currPadding = ref layout[slotIdx];

            //DO NOT CHANGE THE <EXECUTION-ORDER> OF THIS CODE BLOCK! IT WORKS INTENDED IN THIS ORDER!
            short shift = src.AsReadOnlySpan().Swap(ref currPadding, ref nextSegmentValue);
            SwapPositions(ref nextSegmentValue, ref currPadding);
            SwapSlots(ref nextSegmentValue, ref currPadding);
            AdjustPositions(layout, shift, (int)startOfUpdate, slotIdx - 1);
        }
        /*this entire loop can be parallelized-async!*/
        //slotIdx has to begin always at 1, cause we skip the very first <Value0>, it can stay where it is!

        fixed (char* firstChr = tmpSrc)
        {
            int half = layout.Length / 2;
            byte slotIdx = 1;
            Slot* firstSlot = (Slot*)Unsafe.AsPointer(ref layout[0]);
            int layoutLen = layout.Length;
            int stringLen = tmpSrc.Length;
            char* lambda_firstChr = (char*)Unsafe.AsPointer(ref *firstChr);

            void Parallel_AdjustPositions(short shift, int begin, int oneMoreEachCycle)
            {
                short lambda_shift = shift;
                int lambda_startOfUpdate = begin;
                var partition = Partitioner.Create(0, layoutLen);
                int lambda_oneMoreEachCycle = oneMoreEachCycle;

                Parallel.ForEach(partition, (range) =>
                {
                    Span<Slot> lambdaLayout = new(firstSlot, layoutLen);

                    if (lambda_shift == 0)
                        return;

                    int from = lambda_startOfUpdate;

                    //the extra one will be ignored so in order to get the slots[to] we need to do +1
                    int to = from + lambda_oneMoreEachCycle + 1;

                    FastSpanEnumerator<Slot> enumerator = new(lambdaLayout[from..to]);

                    //foreach (ref var slot in enumerator)
                    enumerator.MoveNext();
                    ref Slot slot = ref enumerator.Current;
                    slot.Position -= lambda_shift;
                });
            }

            for (slotIdx = 1; slotIdx < half; slotIdx++)
            {
                //we always have to go 2x slotIdx, to find the value we wanna swap behind!
                int safeNextValueIdx = slotIdx * 2;
                startOfUpdate ??= safeNextValueIdx;
                ref Slot nextSegmentValue = ref layout[safeNextValueIdx];
                ref Slot currPadding = ref layout[slotIdx];

                //DO NOT CHANGE THE <EXECUTION-ORDER> OF THIS CODE BLOCK! IT WORKS INTENDED IN THIS ORDER!
                short shift = tmpSrc.AsReadOnlySpan().Swap(ref currPadding, ref nextSegmentValue);
                SwapPositions(ref nextSegmentValue, ref currPadding);
                SwapSlots(ref nextSegmentValue, ref currPadding);
                AdjustPositions(layout, shift, (int)startOfUpdate++, slotIdx - 1);
            }

            // Span<char> newSrc = new(lambda_firstChr, )
            OnlyValues = tmpSrc[.._nameEnumerator.ValuesLen];
        }
    }

    /// <summary>
    /// This function has to be called, after you are done using the result span of the BeginSequence() method,
    /// because the "BeginSequence()" actually destroys the internal layout of the passed array, and "EndSequence()"
    /// is needed to "ReOrder()" that layout
    /// </summary>
    public unsafe void Restore()
    {
        short Swap(scoped ReadOnlySpan<char> source, ref Slot x, ref Slot y)
        {
            short result = source.Swap(ref x, ref y);
            (x.Position, y.Position) = (y.Position, x.Position);
            (x, y) = (y, x);
            return result;
        }

        void UpdateLayout(scoped Span<Slot> layout, scoped ref Slot yAfterSwap, scoped ref Slot xAfterSwap,
            short finalDirection)
        {
            //we dont need to update anything here, cause the swap did not affect the layout!
            if (finalDirection == 0)
                return;

            //"nextPadding" == y, but we want (y+1) 
            int idxOfY = layout.IndexOf(yAfterSwap) + 1;
            //the extra one will be ignored so in order to get the slots[to] we need to do +1
            int idxOfX = layout.IndexOf(xAfterSwap) + 1;

            Range range = (idxOfY < idxOfX) ? idxOfY..idxOfX : (idxOfX..idxOfY);
            FastSpanEnumerator<Slot> enumerator = new(layout[range]);

            foreach (ref var slot in enumerator)
                slot.Position -= finalDirection;
        }

        ref Slot FindCorrectPadding(Span<Slot> layout, int valueID, SlotType type)
        {
            FastSpanEnumerator<Slot> enumerator = new(layout);

            foreach (ref var slot in enumerator)
                if (slot.Id == valueID && slot.Kind == type)
                    return ref slot;

            return ref Unsafe.NullRef<Slot>();
        }

        scoped var src = DebugBuffer;
        scoped var layout = _nameEnumerator.SafeLayout;
        scoped ref Slot x = ref layout[0], y = ref Unsafe.NullRef<Slot>();

        short segmentID = 0;
        int index = 1;

        fixed (char* _ = _nameEnumerator.DebugBuffer)
        {
            while (!Unsafe.AreSame(ref x, ref y))
            {
                //each (index % 2 must be == 0) and we check if we have a value of that "segmentID" right now at 
                //the current "index", if not we find the value based on "segmentID"!
                ref var value2Move = ref layout[index];
                ref var value2ReplaceWith = ref FindCorrectPadding(layout, segmentID, SlotType.Padding);
                bool canSwapDirectly = index == 1 || layout[index - 1].Id == value2ReplaceWith.Id;

                if (!canSwapDirectly)
                {
                    y = ref FindCorrectPadding(layout, segmentID, SlotType.Value);
                    x = ref value2Move;
                }
                else
                {
                    //"direction1" > 0 ===> we do "NewStart -= X" by that absolute value, so we move "left"
                    //"direction1" < 0 ===> we increase "NewStart += X" by that absolute value,so we move "right"
                    segmentID++;
                    x = ref value2Move;
                    y = ref value2ReplaceWith;
                }

                short shift = Swap(src.Span, ref x, ref y);
                UpdateLayout(layout, ref x, ref y, shift);
                index++;
            }
        }
    }

    public void Dispose()
    {
        _nameEnumerator.Dispose();
        DebugBuffer.Dispose();
        //Console.WriteLine("Were I freed?   " + _nameEnumerator.Layout.Length);
    }

    public ReadOnlySpan<char> this[int index] => Source.Slice(ref _nameEnumerator.SafeLayout[index]);

    public ReadOnlySpan<char> Current => _nameEnumerator.Current;

    public bool MoveNext() => _nameEnumerator.MoveNext();

    public readonly NameSequence<T> GetEnumerator() => this;
}
using System;

namespace Networking
{
    // TODO : Flip back to use ushort
    public static class Sequence
    {
        // In order to reduce the number of bits we use for sequence numbers we 
        // only send the lower 16 bits and then restore the integer sequence number
        // based on the baseline value. In order to partially protect against weird
        // errors we will only allow the diffs to be 1/4 of the size of ushort which
        // for an update rate of 60 is about 4 minutes

        public static uint Invalid = 0xffffffff;

        private const uint Modulo = ushort.MaxValue + 1;
        private const ushort MaxUint16Diff = ushort.MaxValue / 4;

        public static ushort ToUInt16(int value)
        {
            GameDebug.Assert(value >= 0);
            return (ushort) (value & 0xffff);
        }

        // TODO : We can probably implement this more elegantly?
        public static int FromUInt16(ushort value, int baseline)
        {
            ushort b = ToUInt16(baseline);

            if (value <= b)
            {
                var diff = b - value;
                if (diff < MaxUint16Diff)
                {
                    return baseline - diff;
                }

                var diff2 = Modulo - b + value;
                if (diff2 < MaxUint16Diff)
                {
                    return (int) (baseline + diff2);
                }

                return -1;
            }
            else
            {
                var diff = value - b;
                if (diff < MaxUint16Diff)
                {
                    return baseline + diff;
                }

                var diff2 = Modulo - value + b;
                if (diff2 < MaxUint16Diff)
                {
                    return (int) (baseline - diff2);
                }

                return -1;
            }
        }
    }

    public class SequenceBuffer<T> where T : class
    {
        public readonly int[] Sequences;
        public readonly T[] Elements;

        public int capacity => Sequences.Length;

        public SequenceBuffer(int capacity, Func<T> factory)
        {
            Sequences = new int[capacity];
            Elements = new T[capacity];

            for (var i = 0; i < capacity; ++i)
            {
                Sequences[i] = -1;
                Elements[i] = factory();
            }
        }

        public T this[int sequence]
        {
            get
            {
                var index = sequence % Elements.Length;
                var elementSequence = Sequences[index];

                if (elementSequence != sequence)
                {
                    var message = $"Invalid sequence. Looking for {sequence} but slot has {elementSequence}";
                    throw new ArgumentException(message);
                }

                return Elements[index];
            }
        }

        public T TryGetByIndex(int index, out int sequence)
        {
            GameDebug.Assert(index >= 0 && index < Elements.Length);
            if (Sequences[index] != -1)
            {
                sequence = Sequences[index];
                return Elements[index];
            }

            sequence = 0;
            return null;
        }

        public bool TryGetValue(int sequence, out T result)
        {
            var index = sequence % Elements.Length;
            var elementSequence = Sequences[index];

            if (elementSequence == sequence)
            {
                result = Elements[index];
                return true;
            }

            result = default;
            return false;
        }

        public void Clear()
        {
            for (var i = 0; i < Sequences.Length; ++i)
            {
                Sequences[i] = -1;
            }
        }

        public void Remove(int sequence)
        {
            var index = sequence % Sequences.Length;
            if (Sequences[index] == sequence)
            {
                Sequences[index] = -1;
            }
        }

        public bool Available(int sequence)
        {
            var index = sequence % Sequences.Length;
            return Sequences[index] == -1;
        }

        public bool Exists(int sequence)
        {
            var index = sequence % Sequences.Length;
            return Sequences[index] == sequence;
        }

        public T Acquire(int sequence)
        {
            var index = sequence % Sequences.Length;
            Sequences[index] = sequence;
            return Elements[index];
        }

        public override string ToString()
        {
            return string.Join(",", Sequences);
        }
    }
}
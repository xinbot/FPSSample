using System;

namespace Networking
{
    public class SparseSequenceBuffer
    {
        private int _count;

        private readonly int[] _sequences;
        private readonly uint[][] _elements;

        public SparseSequenceBuffer(int size, int snapSize)
        {
            _sequences = new int[size];
            _elements = new uint[size][];

            for (int i = 0; i < _elements.Length; ++i)
            {
                _elements[i] = new uint[snapSize];
            }
        }

        public uint[] Insert(int sequence)
        {
            if (_count == _sequences.Length)
            {
                Remove(_sequences[0]);
            }

            if (_count == 0 || _sequences[_count - 1] < sequence)
            {
                _sequences[_count] = sequence;
                var result = _elements[_count];
                ++_count;
                return result;
            }

            for (int i = 0; i < _count; ++i)
            {
                if (_sequences[i] == sequence)
                {
                    return _elements[i];
                }

                if (_sequences[i] > sequence)
                {
                    var tmp = _elements[_count];
                    for (int j = _count; j > i; --j)
                    {
                        _sequences[j] = _sequences[j - 1];
                        _elements[j] = _elements[j - 1];
                    }

                    _elements[i] = tmp;
                    ++_count;
                    return tmp;
                }
            }

            // Should never reach this point
            throw new InvalidOperationException();
        }

        public bool Remove(int sequence)
        {
            for (int i = 0; i < _count; ++i)
            {
                if (_sequences[i] == sequence)
                {
                    var tmpElement = _elements[i];
                    for (var j = i; j < _count - 1; ++j)
                    {
                        _sequences[j] = _sequences[j + 1];
                        _elements[j] = _elements[j + 1];
                    }

                    _elements[_count - 1] = tmpElement;
                    --_count;
                    return true;
                }
            }

            return false;
        }

        public uint[] FindMax(int sequence)
        {
            var index = -1;
            for (int i = 0; i < _count; ++i)
            {
                if (_sequences[i] <= sequence)
                {
                    index = i;
                }
                else
                {
                    break;
                }
            }

            return index != -1 ? _elements[index] : null;
        }

        public uint[] FindMin(int sequence)
        {
            var index = -1;
            for (int i = _count - 1; i >= 0; --i)
            {
                if (_sequences[i] >= sequence)
                {
                    index = i;
                }
                else
                {
                    break;
                }
            }

            return index != -1 ? _elements[index] : null;
        }

        public uint[] TryGetValue(int sequence)
        {
            for (int i = 0; i < _count; ++i)
            {
                if (_sequences[i] == sequence)
                {
                    return _elements[i];
                }

                if (_sequences[i] > sequence)
                {
                    return null;
                }
            }

            return null;
        }

        public void Clear()
        {
            _count = 0;
        }

        public int GetSize()
        {
            return _count;
        }
    }
}
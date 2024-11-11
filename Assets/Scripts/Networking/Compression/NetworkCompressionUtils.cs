namespace Networking.Compression
{
    public static class NetworkCompressionUtils
    {
        private class Node
        {
            public byte Symbol;
            public int Frequency;
            public Node LeftChild;
            public Node RightChild;
        }

        private struct SortEntry
        {
            public byte Symbol;
            public int Frequency;
        }

        public static int CalculateBucket(uint value)
        {
            int bucketIndex = 0;
            // TODO: use CountLeadingZeros to do this in constant time
            while (bucketIndex + 1 < NetworkCompressionConstants.KNumBuckets &&
                   value >= NetworkCompressionConstants.KBucketOffsets[bucketIndex + 1]
            )
            {
                bucketIndex++;
            }

            return bucketIndex;
        }

        public static int CountLeadingZeros(uint value)
        {
            int n = 0;
            while (value != 0)
            {
                value >>= 1;
                n++;
            }

            return 32 - n;
        }

        public static int CalculateNumGammaBits(long value)
        {
            int numOutputBits = 1;
            int numPrefixBits = 0;
            while (value >= (1L << numOutputBits)
            ) // RUTODO: Unroll this and merge with bit output. How do we actually verify inlining in C#?
            {
                value -= (1L << numOutputBits);
                numOutputBits += 2;
                numPrefixBits++;
            }

            return numPrefixBits + 1 + numOutputBits;
        }

        public static void GenerateLengthLimitedHuffmanCodeLengths(byte[] symbolLengths, int symbolLengthsOffset,
            int[] symbolFrequencies, int alphabetSize, int maxCodeLength)
        {
            GameDebug.Assert(alphabetSize <= 255);
            GameDebug.Assert(maxCodeLength <= 8);

            var sortEntries = new SortEntry[alphabetSize];
            int lastNonZeroIndex = 0;
            int numSortEntries = 0;
            for (int symbol = 0; symbol < alphabetSize; symbol++)
            {
                symbolLengths[symbol + symbolLengthsOffset] = 0;

                int frequency = symbolFrequencies[symbol];
                if (frequency > 0)
                {
                    lastNonZeroIndex = symbol;
                    sortEntries[numSortEntries].Frequency = frequency;
                    sortEntries[numSortEntries].Symbol = (byte) symbol;
                    numSortEntries++;
                }
            }

            if (numSortEntries == 1)
            {
                symbolLengths[lastNonZeroIndex] = 1;
                return;
            }

            System.Array.Resize(ref sortEntries, numSortEntries);
            System.Array.Sort(sortEntries, (a, b) => a.Frequency - b.Frequency);

            int numNodes = alphabetSize * maxCodeLength * 2;
            var nodes = new Node[numNodes];
            for (int i = 0; i < numNodes; i++)
                nodes[i] = new Node();

            int nodesPointer = 0;
            int numPrevNodes = 0;
            int prevNodesPointer = 0;
            for (int length = 1; length <= maxCodeLength; length++)
            {
                int numA = numPrevNodes;
                int numB = numSortEntries;

                int aPointer = prevNodesPointer;
                prevNodesPointer = nodesPointer;
                numPrevNodes = 0;
                while (numA >= 2 || numB > 0)
                {
                    if (numB > 0 && (numA < 2 || sortEntries[numSortEntries - numB].Frequency <=
                        nodes[aPointer + 0].Frequency + nodes[aPointer + 1].Frequency))
                    {
                        var e = sortEntries[numSortEntries - numB];
                        var node = nodes[nodesPointer++];
                        node.Frequency = e.Frequency;
                        node.Symbol = e.Symbol;
                        numB--;
                    }
                    else
                    {
                        var node = nodes[nodesPointer++];
                        node.Frequency = nodes[aPointer + 0].Frequency + nodes[aPointer + 1].Frequency;
                        node.Symbol = 0xFF;
                        node.LeftChild = nodes[aPointer + 0];
                        node.RightChild = nodes[aPointer + 1];
                        numA -= 2;
                        aPointer += 2;
                    }

                    numPrevNodes++;
                }
            }

            int numActive = 2 * numSortEntries - 2;
            for (int i = 0; i < numActive; i++)
            {
                GenerateLengthLimitedHuffmanCodeLengthsRecursive(nodes[prevNodesPointer + i], symbolLengths);
            }
        }

        public static void GenerateHuffmanCodes(byte[] symbolCodes, int symbolCodesOffset, byte[] symbolLengths,
            int symbolLengthsOffset, int alphabetSize, int maxCodeLength)
        {
            GameDebug.Assert(alphabetSize <= 256);
            GameDebug.Assert(maxCodeLength <= 8);

            var lengthCounts = new byte[maxCodeLength + 1];
            var symbolList = new byte[maxCodeLength + 1][];
            for (var index = 0; index < maxCodeLength + 1; index++)
            {
                symbolList[index] = new byte[alphabetSize];
            }

            for (var symbol = 0; symbol < alphabetSize; symbol++)
            {
                int length = symbolLengths[symbol + symbolLengthsOffset];
                GameDebug.Assert(length <= maxCodeLength);
                symbolList[length][lengthCounts[length]++] = (byte) symbol;
            }

            /*
             * symbolLengths [16] : {2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6, 6, 6, 6}
             * 
             * lengthCounts [7] : {0, 0, 1, 3, 3, 3, 6}
             * 
             * symbolList [7][16] :
             * {
	         *      [0] : {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}
	         *      [1] : {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}
	         *      [2] : {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}
	         *      [3] : {1, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}
	         *      [4] : {4, 5, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}
	         *      [5] : {7, 8, 9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}
	         *      [6] : {10, 11, 12, 13, 14, 15, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}
             * }
             */

            uint nextCodeWord = 0;
            for (var length = 1; length <= maxCodeLength; length++)
            {
                int lengthCount = lengthCounts[length];
                for (var i = 0; i < lengthCount; i++)
                {
                    int symbol = symbolList[length][i];
                    GameDebug.Assert(symbolLengths[symbol + symbolLengthsOffset] == length);
                    symbolCodes[symbol + symbolCodesOffset] = (byte) ReverseBits(nextCodeWord++, length);
                }

                nextCodeWord <<= 1;
            }

            // symbolCodes [16] : {0, 2, 6, 1, 5, 13, 3, 11, 27, 7, 23, 55, 15, 47, 31, 63}
        }

        // decode table entries: (symbol << 8) | length
        public static void GenerateHuffmanDecodeTable(ushort[] decodeTable, int decodeTableOffset, byte[] symbolLengths,
            byte[] symbolCodes, int alphabetSize, int maxCodeLength)
        {
            GameDebug.Assert(alphabetSize <= 256);
            GameDebug.Assert(maxCodeLength <= 8);

            // symbolLengths [16] : {2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6, 6, 6, 6}
            uint maxCode = 1u << maxCodeLength;
            for (int symbol = 0; symbol < alphabetSize; symbol++)
            {
                int length = symbolLengths[symbol];
                GameDebug.Assert(length <= maxCodeLength);
                if (length > 0)
                {
                    uint code = symbolCodes[symbol];
                    uint step = 1u << length;
                    do
                    {
                        decodeTable[decodeTableOffset + code] = (ushort) (symbol << 8 | length);
                        code += step;
                    } while (code < maxCode);
                }
            }
        }

        private static uint ReverseBits(uint value, int numBits)
        {
            // 0x55555555u = 0101 0101 0101 0101 0101 0101 0101 0101
            // 0xAAAAAAAAu = 1010 1010 1010 1010 1010 1010 1010 1010
            value = ((value & 0x55555555u) << 1) | ((value & 0xAAAAAAAAu) >> 1);

            // 0x33333333u = 0011 0011 0011 0011 0011 0011 0011 0011
            // 0xCCCCCCCCu = 1100 1100 1100 1100 1100 1100 1100 1100
            value = ((value & 0x33333333u) << 2) | ((value & 0xCCCCCCCCu) >> 2);

            // 0x0F0F0F0Fu = 0000 1111 0000 1111 0000 1111 0000 1111
            // 0xF0F0F0F0u = 1111 0000 1111 0000 1111 0000 1111 0000
            value = ((value & 0x0F0F0F0Fu) << 4) | ((value & 0xF0F0F0F0u) >> 4);

            // 0x00FF00FFu = 0000 0000 1111 1111 0000 0000 1111 1111
            // 0xFF00FF00u = 1111 1111 0000 0000 1111 1111 0000 0000
            value = ((value & 0x00FF00FFu) << 8) | ((value & 0xFF00FF00u) >> 8);

            value = (value << 16) | (value >> 16);
            return value >> (32 - numBits);
        }

        private static void GenerateLengthLimitedHuffmanCodeLengthsRecursive(Node node, byte[] symbolLengths)
        {
            if (node.Symbol == 0xFF)
            {
                GenerateLengthLimitedHuffmanCodeLengthsRecursive(node.LeftChild, symbolLengths);
                GenerateLengthLimitedHuffmanCodeLengthsRecursive(node.RightChild, symbolLengths);
            }
            else
            {
                symbolLengths[node.Symbol]++;
            }
        }
    }
}
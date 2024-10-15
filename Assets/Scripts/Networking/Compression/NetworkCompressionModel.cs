using UnityEngine;

namespace Networking.Compression
{
    public class NetworkCompressionModel
    {
        public NetworkCompressionModel(byte[] modelData)
        {
            if (modelData == null)
                modelData = m_DefaultModelData;

            int numContexts = NetworkConfig.MAXContexts;
            int alphabetSize = 16;
            byte[,] symbolLengths = new byte[numContexts, alphabetSize];

            int readOffset = 0;
            {
                // default model
                int defaultModelAlphabetSize = modelData[readOffset++];
                Debug.Assert(defaultModelAlphabetSize == alphabetSize);

                for (int i = 0; i < alphabetSize; i++)
                {
                    byte length = modelData[readOffset++];
                    for (int context = 0; context < numContexts; context++)
                    {
                        symbolLengths[context, i] = length;
                    }
                }

                // additional models
                int numModels = modelData[readOffset] | (modelData[readOffset + 1] << 8);
                readOffset += 2;
                for (int model = 0; model < numModels; model++)
                {
                    int context = modelData[readOffset] | (modelData[readOffset + 1] << 8);
                    readOffset += 2;

                    int modelAlphabetSize = modelData[readOffset++];
                    Debug.Assert(modelAlphabetSize == alphabetSize);
                    for (int i = 0; i < alphabetSize; i++)
                    {
                        byte length = modelData[readOffset++];
                        symbolLengths[context, i] = length;
                    }
                }
            }

            // generate tables
            encodeTable = new ushort[numContexts, alphabetSize];
            decodeTable = new ushort[numContexts, 1 << NetworkCompressionConstants.KMaxHuffmanSymbolLength];

            var tmpSymbolLengths = new byte[alphabetSize];
            var tmpSymbolDecodeTable = new ushort[1 << NetworkCompressionConstants.KMaxHuffmanSymbolLength];
            var symbolCodes = new byte[alphabetSize];

            for (int context = 0; context < numContexts; context++)
            {
                for (int i = 0; i < alphabetSize; i++)
                    tmpSymbolLengths[i] = symbolLengths[context, i];

                NetworkCompressionUtils.GenerateHuffmanCodes(symbolCodes, 0, tmpSymbolLengths, 0, alphabetSize, NetworkCompressionConstants.KMaxHuffmanSymbolLength);
                NetworkCompressionUtils.GenerateHuffmanDecodeTable(tmpSymbolDecodeTable, 0, tmpSymbolLengths, symbolCodes, alphabetSize, NetworkCompressionConstants.KMaxHuffmanSymbolLength);
                for (int i = 0; i < alphabetSize; i++)
                {
                    encodeTable[context, i] = (ushort)((symbolCodes[i] << 8) | symbolLengths[context, i]);
                }
                for (int i = 0; i < (1 << NetworkCompressionConstants.KMaxHuffmanSymbolLength); i++)
                {
                    decodeTable[context, i] = tmpSymbolDecodeTable[i];
                }
            }
            this.modelData = modelData;
        }

        public byte[] modelData;
        public ushort[,] encodeTable;      // (code << 8) | length
        public ushort[,] decodeTable;      // (symbol << 8) | length

        private static byte[] m_DefaultModelData = new byte[] { 16, // 16 symbols
                                                         2, 3, 3, 3,   4, 4, 4, 5,     5, 5, 6, 6,     6, 6, 6, 6,
                                                         0, 0 };  // no additional models / contexts

        public static NetworkCompressionModel DefaultModel = new NetworkCompressionModel(null);
    }
}
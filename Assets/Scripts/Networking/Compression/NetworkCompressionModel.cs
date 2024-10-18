using UnityEngine;

namespace Networking.Compression
{
    public class NetworkCompressionModel
    {
        public readonly byte[] ModelData;

        // (code << 8) | length
        public readonly ushort[,] EncodeTable;

        // (symbol << 8) | length
        public readonly ushort[,] DecodeTable;

        private static readonly byte[] DefaultModelData =
        {
            16, // 16 symbols
            2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6, 6, 6, 6,
            0, 0
        }; // no additional models / contexts

        public static readonly NetworkCompressionModel DefaultModel = new NetworkCompressionModel(null);

        public NetworkCompressionModel(byte[] modelData)
        {
            if (modelData == null)
            {
                modelData = DefaultModelData;
            }

            int numContexts = NetworkConfig.MAXContexts;
            int alphabetSize = 16;
            byte[][] symbolLengths = new byte[numContexts][];
            for (var index = 0; index < numContexts; index++)
            {
                symbolLengths[index] = new byte[alphabetSize];
            }

            int readOffset = 0;
            // default model
            int defaultModelAlphabetSize = modelData[readOffset++];
            Debug.Assert(defaultModelAlphabetSize == alphabetSize);

            for (var i = 0; i < alphabetSize; i++)
            {
                byte length = modelData[readOffset++];
                for (var context = 0; context < numContexts; context++)
                {
                    symbolLengths[context][i] = length;
                }
            }

            // additional models
            int numModels = modelData[readOffset] | (modelData[readOffset + 1] << 8);
            readOffset += 2;
            for (var model = 0; model < numModels; model++)
            {
                int context = modelData[readOffset] | (modelData[readOffset + 1] << 8);
                readOffset += 2;

                int modelAlphabetSize = modelData[readOffset++];
                Debug.Assert(modelAlphabetSize == alphabetSize);
                for (var i = 0; i < alphabetSize; i++)
                {
                    byte length = modelData[readOffset++];
                    symbolLengths[context][i] = length;
                }
            }

            // generate tables
            EncodeTable = new ushort[numContexts, alphabetSize];
            DecodeTable = new ushort[numContexts, 1 << NetworkCompressionConstants.KMaxHuffmanSymbolLength];

            var tmpSymbolLengths = new byte[alphabetSize];
            var tmpSymbolDecodeTable = new ushort[1 << NetworkCompressionConstants.KMaxHuffmanSymbolLength];
            var symbolCodes = new byte[alphabetSize];

            for (var context = 0; context < numContexts; context++)
            {
                for (var i = 0; i < alphabetSize; i++)
                {
                    tmpSymbolLengths[i] = symbolLengths[context][i];
                }
                
                NetworkCompressionUtils.GenerateHuffmanCodes(symbolCodes, 0, tmpSymbolLengths, 0, alphabetSize,
                    NetworkCompressionConstants.KMaxHuffmanSymbolLength);

                NetworkCompressionUtils.GenerateHuffmanDecodeTable(tmpSymbolDecodeTable, 0, tmpSymbolLengths,
                    symbolCodes, alphabetSize, NetworkCompressionConstants.KMaxHuffmanSymbolLength);

                for (var i = 0; i < alphabetSize; i++)
                {
                    EncodeTable[context, i] = (ushort) ((symbolCodes[i] << 8) | symbolLengths[context][i]);
                }

                for (var i = 0; i < (1 << NetworkCompressionConstants.KMaxHuffmanSymbolLength); i++)
                {
                    DecodeTable[context, i] = tmpSymbolDecodeTable[i];
                }
            }

            ModelData = modelData;
        }
    }
}
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ClassicUO.Utility
{
    public static class BwtDecompress
    {
        // ReX: TazUO 26.10 fast path (#1116). Toggled from Options > Custom > TazUO ports.
        public static bool FastPath = true;

        public static byte[] Decompress(byte[] buffer)
        {
            byte[] output = null;

            using (var reader = new BinaryReader(new MemoryStream(buffer)))
            {
                var header = reader.ReadUInt32();
                var len = 0u;

                var firstChar = reader.ReadByte();

                // ReX / TazUO 26.10: the sorted table built from firstChar is always the identity and only
                // its first 256 entries are read (the index is a byte), so the fast path builds just those.
                bool fast = FastPath;
                Span<ushort> table = fast ? stackalloc ushort[256] : new ushort[256 * 256];

                if (fast)
                {
                    for (int j = 0; j < 256; j++)
                    {
                        table[j] = (ushort)j;
                    }
                }
                else
                {
                    BuildTable(table, firstChar);
                }

                var list = new byte[reader.BaseStream.Length - 4];
                var i = 0;
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    var currentValue = firstChar;
                    var value = table[currentValue];
                    if (currentValue > 0)
                    {
                        if (fast && currentValue > 32)
                        {
                            // overlap-safe memmove beats the scalar loop for large shifts
                            table.Slice(0, currentValue).CopyTo(table.Slice(1));
                        }
                        else
                        {
                            do
                            {
                                table[currentValue] = table[currentValue - 1];
                            } while (--currentValue > 0);
                        }
                    }

                    table[0] = value;

                    list[i++] = (byte)value;
                    firstChar = reader.ReadByte();
                }

                output = InternalDecompress(list, len);
            }

            return output;
        }

        static void BuildTable(Span<ushort> table, byte startValue)
        {
            int index = 0;
            byte firstByte = startValue;
            byte secondByte = 0;
            for (int i = 0; i < 256 * 256; i++)
            {
                var val = (ushort)(firstByte + (secondByte << 8));
                table[index++] = val;

                firstByte++;
                if (firstByte == 0)
                {
                    secondByte++;
                }
            }

            table.Sort();
        }

        static byte[] InternalDecompress(Span<byte> input, uint len)
        {
            Span<char> symbolTable = stackalloc char[256];
            Span<char> frequency = stackalloc char[256];
            Span<int> partialInput = stackalloc int[256 * 3];
            partialInput.Clear();

            for (var i = 0; i < 256; i++)
                symbolTable[i] = (char)i;

            input.Slice(0, 1024).CopyTo(MemoryMarshal.AsBytes(partialInput));

            var sum = 0;
            for (var i = 0; i < 256; i++)
                sum += partialInput[i];

            if (len == 0)
            {
                len = (uint)sum;
            }

            if (sum != len)
                return Array.Empty<byte>();

            var output = new byte[len];

            var count = 0;
            var nonZeroCount = 0;

            for (var i = 0; i < 256; i++)
            {
                if (partialInput[i] != 0)
                    nonZeroCount++;
            }

            Frequency(partialInput, frequency);

            for (int i = 0, m = 0; i < nonZeroCount; ++i)
            {
                var freq = (byte)frequency[i];
                symbolTable[input[m + 1024]] = (char)freq;
                partialInput[freq + 256] = m + 1;
                m += partialInput[freq];
                partialInput[freq + 512] = m;
            }

            var val = (byte)symbolTable[0];

            if (len != 0)
            {
                do
                {
                    ref var firstValRef = ref partialInput[val + 256];
                    output[count] = val;

                    if (firstValRef >= partialInput[val + 512])
                    {
                        if (nonZeroCount-- > 0)
                        {
                            ShiftLeft(symbolTable, nonZeroCount);
                            val = (byte)symbolTable[0];
                        }
                    }
                    else
                    {
                        var idx = (char)input[firstValRef + 1024];
                        firstValRef++;

                        if (idx != 0)
                        {
                            ShiftLeft(symbolTable, idx);
                            symbolTable[(byte)idx] = (char)val;
                            val = (byte)symbolTable[0];
                        }
                    }

                    count++;
                } while (count < len);
            }

            return output;
        }

        static void Frequency(Span<int> input, Span<char> output)
        {
            Span<int> tmp = stackalloc int[256];
            input.Slice(0, tmp.Length).CopyTo(tmp);

            for (var i = 0; i < 256; i++)
            {
                uint value = 0;
                byte index = 0;

                for (var j = 0; j < 256; j++)
                {
                    if (tmp[j] > value)
                    {
                        index = (byte)j;
                        value = (uint)tmp[j];
                    }
                }

                if (value == 0)
                    break;

                output[i] = (char)index;
                tmp[index] = 0;
            }
        }

        static void ShiftLeft(Span<char> input, int max)
        {
            if (FastPath && max > 32)
            {
                input.Slice(1, max).CopyTo(input);
                return;
            }

            for (var i = 0; i < max; ++i)
                input[i] = input[i + 1];
        }
    }
}
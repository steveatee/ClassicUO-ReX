using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ClassicUO.Utility;
using Xunit;
using Xunit.Abstractions;

namespace ClassicUO.UnitTests.Utility
{
    // The TazUO 26.10 BWT fast path must give exactly the same output as the original decoder.
    // Real samples (zlib-inflated ZlibBwt entries from gumpartLegacyMUL.uop) are read from the file
    // named by BWT_SAMPLES: [uint32 length][bytes]... The test is skipped when it isn't set.
    public class BwtDecompressFastPathTests
    {
        private readonly ITestOutputHelper _out;

        public BwtDecompressFastPathTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void FastPath_matches_original_on_real_samples()
        {
            string path = Environment.GetEnvironmentVariable("BWT_SAMPLES");

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            var samples = new List<byte[]>();

            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                while (r.BaseStream.Position < r.BaseStream.Length)
                {
                    samples.Add(r.ReadBytes((int)r.ReadUInt32()));
                }
            }

            bool saved = BwtDecompress.FastPath;

            try
            {
                var slow = Stopwatch.StartNew();
                BwtDecompress.FastPath = false;
                var expected = samples.ConvertAll(BwtDecompress.Decompress);
                slow.Stop();

                var fast = Stopwatch.StartNew();
                BwtDecompress.FastPath = true;
                var actual = samples.ConvertAll(BwtDecompress.Decompress);
                fast.Stop();

                for (int i = 0; i < samples.Count; i++)
                {
                    Assert.Equal(expected[i], actual[i]);
                }

                _out.WriteLine($"{samples.Count} samples identical; original {slow.ElapsedMilliseconds} ms, fast path {fast.ElapsedMilliseconds} ms");
            }
            finally
            {
                BwtDecompress.FastPath = saved;
            }
        }
    }
}

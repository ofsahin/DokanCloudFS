﻿/*
The MIT License(MIT)

Copyright(c) 2015 IgorSoft

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IgorSoft.DokanCloudFS.IO;

namespace IgorSoft.DokanCloudFS.Tests
{
    public partial class ScatterGatherStreamTests
    {
        private sealed class Fixture
        {
            private sealed class FakeGatherStream : GatherStream
            {
                public FakeGatherStream(byte[] buffer, BlockMap assignedBlocks, TimeSpan timeout) : base(buffer, assignedBlocks, timeout)
                {
                }
            }

            private sealed class FakeScatterStream : ScatterStream
            {
                public FakeScatterStream(byte[] buffer, BlockMap assignedBlocks, TimeSpan timeout) : base(buffer, assignedBlocks, timeout)
                {
                }
            }

            public GatherStream CreateGatherStream(byte[] buffer, BlockMap assignedBlocks)
            {
                return new FakeGatherStream(buffer, assignedBlocks, TimeSpan.FromSeconds(1));
            }

            public ScatterStream CreateScatterStream(byte[] buffer, BlockMap assignedBlocks)
            {
                return new FakeScatterStream(buffer, assignedBlocks, TimeSpan.FromSeconds(1));
            }

            public byte[] InitializeBuffer(int size)
            {
                var buffer = new byte[size];
                for (int i = 0; i < buffer.Length; ++i)
                    buffer[i] = (byte)(i % 253);
                return buffer;
            }

            public byte[] CopyBufferConcurrently(byte[] sourceBuffer, TimeSpan initialWriteDelay, int writeChunkSize, TimeSpan writeDelay, TimeSpan initialReadDelay, int readChunkSize, TimeSpan readDelay, bool flush = true)
            {
                var targetBuffer = new byte[sourceBuffer.Length];

                var scatterStream = default(Stream);
                var gatherStream = default(Stream);
                ScatterGatherStreamFactory.CreateScatterGatherStreams(sourceBuffer.Length, out scatterStream, out gatherStream);

                var writeTask = WriteAsync(scatterStream, sourceBuffer, initialWriteDelay, writeChunkSize, writeDelay, flush: flush);
                var readTask = ReadAsync(gatherStream, targetBuffer, initialReadDelay, readChunkSize, readDelay);

                Task.WaitAll(new Task[] { writeTask, readTask });

                return targetBuffer;
            }

            public byte[] CopyBufferConcurrentlyByPermutation(byte[] sourceBuffer, TimeSpan initialWriteDelay, int writeChunkSize, TimeSpan writeDelay, int[] writePermutation, TimeSpan initialReadDelay, int readChunkSize, TimeSpan readDelay, int[] readPermutation, bool flush = true)
            {
                var targetBuffer = new byte[sourceBuffer.Length];

                var scatterStream = default(Stream);
                var gatherStream = default(Stream);
                ScatterGatherStreamFactory.CreateScatterGatherStreams(sourceBuffer.Length, out scatterStream, out gatherStream);

                var writeTask = WriteAsync(scatterStream, sourceBuffer, initialWriteDelay, writeChunkSize, writeDelay, writePermutation, flush);
                var readTask = ReadAsync(gatherStream, targetBuffer, initialReadDelay, readChunkSize, readDelay, readPermutation);

                Task.WaitAll(new Task[] { writeTask, readTask });

                return targetBuffer;
            }

            public byte[] ReadBufferConcurrently(byte[] sourceBuffer, int readChunkSize, TimeSpan readDelay, TimeSpan timeout, out Stream scatterStream)
            {
                var targetBuffer = new byte[sourceBuffer.Length];

                var gatherStream = default(Stream);
                ScatterGatherStreamFactory.CreateScatterGatherStreams(sourceBuffer.Length, timeout, out scatterStream, out gatherStream);
                var readTask = ReadAsync(gatherStream, targetBuffer, TimeSpan.Zero, readChunkSize, readDelay);

                readTask.Wait(timeout);

                return targetBuffer;
            }

            public void WriteBufferConcurrently(byte[] sourceBuffer, byte[] targetBuffer, int writeChunkSize, TimeSpan writeDelay, TimeSpan timeout, out Stream gatherStream)
            {
                var scatterStream = default(Stream);
                ScatterGatherStreamFactory.CreateScatterGatherStreams(targetBuffer.Length, timeout, out scatterStream, out gatherStream);
                var writeTask = WriteAsync(scatterStream, sourceBuffer, TimeSpan.Zero, writeChunkSize, writeDelay);

                writeTask.Wait(timeout);
            }

            public async Task<int> ReadAsync(Stream stream, byte[] buffer, TimeSpan initialDelay, int chunkSize, TimeSpan readDelay, int[] readPermutation = null)
            {
                Console.WriteLine($"{nameof(ReadAsync)} Start".ToString(CultureInfo.CurrentCulture));

                if (readPermutation == null)
                    readPermutation = Enumerable.Range(0, buffer.Length / chunkSize).ToArray();

                await Task.Delay(initialDelay);

                var volume = 0;
                for (int i = 0, bytesRead = 0; volume < buffer.Length; ++i) {
                    bytesRead = 0;
                    stream.Position = readPermutation[i] * chunkSize;
                    do {
                        bytesRead += buffer.Length - volume > 0 ? await stream.ReadAsync(buffer, readPermutation[i] * chunkSize + bytesRead, Math.Min(chunkSize - bytesRead, buffer.Length - volume)) : 0;
                        await Task.Delay(readDelay);
                    } while (bytesRead < chunkSize);
                    volume += bytesRead;
                    Console.WriteLine($"{nameof(ReadAsync)}[{readPermutation[i] * chunkSize}] <- {bytesRead}".ToString(CultureInfo.CurrentCulture));

                    if (bytesRead == 0)
                        break;
                }

                Console.WriteLine($"{nameof(ReadAsync)} End".ToString(CultureInfo.CurrentCulture));

                return volume;
            }

            public async Task<bool> WriteAsync(Stream stream, byte[] buffer, TimeSpan initialDelay, int chunkSize, TimeSpan writeDelay, int[] writePermutation = null, bool flush = true)
            {
                Console.WriteLine($"{nameof(WriteAsync)} Start".ToString(CultureInfo.CurrentCulture));

                if (writePermutation == null)
                    writePermutation = Enumerable.Range(0, buffer.Length / chunkSize).ToArray();

                await Task.Delay(initialDelay);

                var volume = 0;
                for (int i = 0; volume < buffer.Length; ++i) {
                    stream.Position = writePermutation[i] * chunkSize;
                    await stream.WriteAsync(buffer, writePermutation[i] * chunkSize, chunkSize);
                    volume += chunkSize;
                    Console.WriteLine($"{nameof(WriteAsync)}[{writePermutation[i] * chunkSize}] -> {chunkSize}".ToString(CultureInfo.CurrentCulture));
                    await Task.Delay(writeDelay);
                }

                if (flush) {
                    Console.WriteLine($"{nameof(WriteAsync)} Flush".ToString(CultureInfo.CurrentCulture));
                    await stream.FlushAsync();
                }

                Console.WriteLine($"{nameof(WriteAsync)} End".ToString(CultureInfo.CurrentCulture));

                return true;
            }
        }
    }
}

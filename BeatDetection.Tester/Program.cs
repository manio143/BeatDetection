using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Stride.Core;
using Stride.Core.Serialization;

namespace BeatDetection
{
    class Program
    {
        static int Main(string[] args)
        {
            if(args.Length < 2)
            {
                Console.WriteLine("Test beat detection\nUsage: dotnet run (v1/v2) file.wav");
                return 0;
            }

            int algorithmVersion = Convert.ToInt32(args[0][1..]);
            var filename = args[1];
            using (var stream = File.OpenRead(filename))
            {
                // Read WAV file header (assuming PCM and that any INFO is at the end)
                // see http://www-mmsp.ece.mcgill.ca/Documents/AudioFormats/WAVE/WAVE.html
                var reader = new BinarySerializationReader(stream);
                reader.ReadInt32(); // skip RIFF
                reader.ReadInt32(); // skip size
                reader.ReadInt32(); // skip WAVE
                reader.ReadInt32(); // skip "fmt "
                var fmtLength = reader.ReadInt32();
                Debug.Assert(fmtLength == 16);
                var formatTag = reader.ReadInt16();
                var channels = reader.ReadInt16();
                var sampleRate = reader.ReadInt32();
                var avgBytesPerSec = reader.ReadInt32();
                var blockAlign = reader.ReadInt16();
                var bitsPerSample = reader.ReadInt16();

                reader.ReadInt32(); // skip "data"
                var dataLen = reader.ReadInt32();
                if (dataLen % 2 == 1) dataLen++;

                // read data from file into memory
                using var memory = new UnmanagedArray<byte>(dataLen);
                var buffer = new byte[1024];
                var bytesLeft = dataLen;
                var offset = 0;
                while (bytesLeft > 0)
                {
                    var read = stream.Read(buffer, 0, Math.Min(buffer.Length, bytesLeft));
                    memory.Write(buffer, offset, 0, read);
                    offset += read;
                    bytesLeft -= read;
                }

                List<DetectedBeat> beats;
                unsafe
                {
                    var memoryAsSpan = new Span<short>((void*)memory.Pointer, dataLen / sizeof(short));

                    IBeatDetection detection = algorithmVersion switch
                    {
                        1 => new BeatDetection(sampleRate),
                        2 => new BeatDetectionV2(sampleRate, 65, 130), // provide BPM detection range
                        _ => throw new NotSupportedException("Invalid version"),
                    };
                    detection.Detect(memoryAsSpan);
                    beats = detection.GetBeats();
                }

                Console.WriteLine("Press enter key to quit");
                using(var audio = new StrideAudio(memory, dataLen, sampleRate))
                {
                    WriteBeats(beats);
                    Thread.Sleep(500);
                    audio.Play();

                    Console.ReadLine();

                    audio.Stop();
                }

                memory.Dispose();
            }

            return 0;
        }

        // Output beats while audio is playing
        static async void WriteBeats(List<DetectedBeat> beats)
        {
            await Task.Delay(1); // wait for audio to start
            TimeSpan prev = new TimeSpan();
            foreach(var beat in beats)
            {
                await Task.Delay(beat.TimeOffset - prev);
                prev = beat.TimeOffset;
                Console.WriteLine("Beat at: {0}.{1}\t freq: {2:0.000}", prev, prev.Milliseconds, beat.StrongestFrequency);
            }
        }
    }
}

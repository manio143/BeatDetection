using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using Stride.Audio;
using Stride.Core;
using Stride.Core.IO;
using Stride.Core.Serialization;
using Stride.Core.Serialization.Contents;

namespace wave_beat
{
    class Program
    {
        static int Main(string[] args)
        {
            if(args.Length < 1)
            {
                Console.WriteLine("Test beat detection\nUsage: dotnet run file.wav");
                return 0;
            }

            var filename = args[0];
            using(var stream = File.OpenRead(filename))
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
                if(dataLen % 2 == 1) dataLen++;

                // read data from file into memory
                var memory = new UnmanagedArray<byte>(dataLen);
                var buffer = new byte[1024];
                var bytesLeft = dataLen;
                var offset = 0;
                while(bytesLeft > 0)
                {
                    var read = stream.Read(buffer, 0, Math.Min(buffer.Length, bytesLeft));
                    memory.Write(buffer, offset, 0, read);
                    offset += read;
                    bytesLeft -= read;
                }

                // detect beats in data
                var detection = new BeatDetection(memory, dataLen, sampleRate);
                detection.Detect();
                var beats = detection.GetBeats();

                Console.WriteLine("Press enter key to quit");
                using(var audio = new StrideAudio(memory, dataLen, sampleRate))
                {
                    audio.Play();
                    WriteBeats(beats);

                    Console.ReadLine();

                    audio.Stop();
                }

                memory.Dispose();
            }

            return 0;
        }

        // Output beats while audio is playing
        static async void WriteBeats(List<Beat> beats)
        {
            await Task.Delay(100); // wait for audio to start
            TimeSpan prev = new TimeSpan();
            foreach(var beat in beats)
            {
                await Task.Delay(beat.AsTime - prev);
                prev = beat.AsTime;
                Console.WriteLine("Beat at: {0}.{1}\t bands: {2}", prev, prev.Milliseconds, beat.Bands);
            }
        }
    }
}

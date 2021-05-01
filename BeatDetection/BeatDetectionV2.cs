using System;
using System.Collections.Generic;

namespace BeatDetection
{
    public class BeatDetectionV2 : IBeatDetection
    {
        const int TwoChannelWindowLen = 512 * 2;

        BeatDetektor detektor;
        List<DetectedBeat> beats = new List<DetectedBeat>();
        int sampleRate;
        TimeSpan time;
        int beatCounter = 0;

        public int WindowSize { get; set; } = TwoChannelWindowLen;

        public bool ApplyBinaryReindexingToWindow { get; set; }

        public BeatDetectionV2(int sampleRate, int minBpm = 90, int maxBpm = 180)
        {
            this.sampleRate = sampleRate;
            this.detektor = new BeatDetektor(minBpm, maxBpm);
        }

        public void Detect(Span<short> memory)
        {
            float[] converted = new float[memory.Length];
            
            for (int i = 0; i < memory.Length; i++)
            {
                converted[i] = memory[i] / 32767f; // normalize
            }

            Detect(converted);
        }


        public void Detect(Span<float> memory)
        {
            Span<float> window = stackalloc float[WindowSize];

            for (int offset = 0; offset < memory.Length - WindowSize; offset += WindowSize)
            {
                for (int i = 0; i < WindowSize; i++)
                    window[i] = memory[offset + i];

                DetectStep(window);
            }
            Console.WriteLine("Detection finished: avg BPM: {0}", detektor.win_bpm_int / 10);
        }

        /// <summary>
        /// Perform a single detection step for data of size <see cref="TwoChannelWindowLen"/>.
        /// </summary>
        public void DetectStep(Span<float> window)
        {
            if (window.Length != WindowSize)
                throw new InvalidOperationException("Provided data window ");

            // time points to the end of the window
            time += new TimeSpan((WindowSize / 2) * (TimeSpan.TicksPerSecond / sampleRate));

            // reverse-binary reindexing (not sure if this is needed?)
            if (ApplyBinaryReindexingToWindow)
            {
                int n = (WindowSize / 2) << 1;
                int j = 1;
                for (int i = 1; i < n; i += 2)
                {
                    if (j > i)
                    {
                        Swap(ref window[j - 1], ref window[i - 1]);
                        Swap(ref window[j], ref window[i]);
                    }
                    int m = (WindowSize / 2);
                    while (m >= 2 && j > m)
                    {
                        j -= m;
                        m >>= 1;
                    }
                    j += m;
                }
            }

            LanczosFFT.Apply(WindowSize / 2, window);

            detektor.process((float)time.TotalSeconds, window);

            if (beatCounter < detektor.beat_counter)
            {
                beats.Add(new DetectedBeat
                {
                    TimeOffset = time,
                    StrongestFrequency = GetStrongestFrequency(),
                });
                beatCounter = detektor.beat_counter;
            }
        }

        public List<DetectedBeat> GetBeats() => beats;

        private double GetStrongestFrequency()
        {
            double m = 0;
            double mIdx = 0;
            for (int i = 0; i < detektor.a_freq_range.Length; i++)
            {
                if (detektor.a_freq_range[i] > m)
                {
                    m = detektor.a_freq_range[i];
                    mIdx = (double)i / (double)detektor.a_freq_range.Length;
                }
            }

            return mIdx;
        }

        private static void Swap<T>(ref T lhs, ref T rhs)
        {
            T temp = lhs;
            lhs = rhs;
            rhs = temp;
        }
    }
}

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


        public bool ApplyBinaryReindexingToWindow { get; set; }

        public BeatDetectionV2(int sampleRate, int minBpm = 90, int maxBpm = 180)
        {
            this.sampleRate = sampleRate;
            this.detektor = new BeatDetektor(minBpm, maxBpm);
        }

        public void Detect(Span<short> memory)
        {
            TimeSpan time;
            int beatCounter = 0;
            Span<float> window = stackalloc float[TwoChannelWindowLen];

            for (int offset = 0; offset < memory.Length - TwoChannelWindowLen; offset += TwoChannelWindowLen)
            {
                for (int i = 0; i < TwoChannelWindowLen; i++)
                    window[i] = memory[offset + i];

                // divide offset by 2, to get single channel 
                // time points to the end of the window
                time = new TimeSpan((offset / 2 + TwoChannelWindowLen / 2) * (TimeSpan.TicksPerSecond / sampleRate));

                // reverse-binary reindexing (not sure if this is needed?)
                if (ApplyBinaryReindexingToWindow)
                {
                    int n = (TwoChannelWindowLen / 2) << 1;
                    int j = 1;
                    for (int i = 1; i < n; i += 2)
                    {
                        if (j > i)
                        {
                            Swap(ref window[j - 1], ref window[i - 1]);
                            Swap(ref window[j], ref window[i]);
                        }
                        int m = (TwoChannelWindowLen / 2);
                        while (m >= 2 && j > m)
                        {
                            j -= m;
                            m >>= 1;
                        }
                        j += m;
                    }
                }

                LanczosFFT.Apply(TwoChannelWindowLen / 2, window);

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
            Console.WriteLine("Detection finished: avg BPM: {0}", detektor.win_bpm_int / 10);
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

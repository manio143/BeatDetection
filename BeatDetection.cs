using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using Stride.Core;

namespace wave_beat
{
    struct Beat
    {
        public TimeSpan AsTime;
        public int AsOffset;
        public ulong Bands; // bit mask
    }

    // Following https://www.gamedev.net/tutorials/_/technical/math-and-physics/beat-detection-algorithms-r1952/
    // Below I implemented _Frequency selected sound energy algorithm #2_
    // with energy variance. It can handle clear percussion but fails with vocals.
    unsafe class BeatDetection
    {
        double[] FourierScore; // B
        double[] CurrentWindowEnergy; // Es
        double[][] HistoricEnergy; // Ei
        double[] AverageEnergy; // <Ei>
        Complex[] fftData;
        double[] EnergyVariance; // V
        int[] bandWidths; // w
        const double C = 250;
        const double V0 = 150;
        const int windowSize = 1024; // has to be power of 2
        const int stepSize = 256; // 0.25 * windows size
        const int bandCount = 64; // between 20 and 64
        const int historySize = 24; // ~0.56 sec because 43 * 1024 ~ 1 sec

        // bandWidths[i] = a * (i+1) + b
        // such that sum of bandWidths = windowSize
        const double bandWb = (4160.0 - 1024.0) / 2016.0;
        const double bandWa = 2.0 - bandWb;

        List<Beat> beats = new List<Beat>();
        int sampleRate;

        // input data
        short[] leftChannel;
        short[] rightChannel;

        public BeatDetection(UnmanagedArray<byte> memory, int dataLen, int sampleRate)
        {
            this.sampleRate = sampleRate;
            leftChannel = new short[dataLen / 2 / sizeof(short)];
            rightChannel = new short[dataLen / 2 / sizeof(short)];

            // copy pcm data into left/right channel arrays
            unsafe {
                byte* ptr = (byte*)memory.Pointer;
                int idx = 0;
                for (int i = 0; i < dataLen; i += 2 * sizeof(short))
                {
                    leftChannel[idx] = *(short*)(ptr + i);
                    rightChannel[idx] = *(short*)(ptr + i + sizeof(short));
                    idx++;
                }
            }

            FourierScore = new double[windowSize];
            CurrentWindowEnergy = new double[bandCount];
            HistoricEnergy = new double[bandCount][];
            for (int i = 0; i < bandCount; i++) HistoricEnergy[i] = new double[historySize];
            AverageEnergy = new double[bandCount];
            EnergyVariance = new double[bandCount];

            fftData = new Complex[windowSize];
            bandWidths = new int[bandCount];
            for (int i = 0; i < bandCount; i++) bandWidths[i] = (int)Math.Round(bandWa * (i+1) + bandWb);
            Debug.Assert(bandWidths.Sum() == 1024, $"{bandWidths.Sum()} != 1024");
        }

        public void Detect()
        {
            for (int offset = 0; offset < leftChannel.Length - windowSize; offset += stepSize)
            {
                var windowL = new Span<short>(leftChannel, offset, windowSize);
                var windowR = new Span<short>(rightChannel, offset, windowSize);

                FillFFTData(windowL, windowR, fftData);

                Fourier.FFT(windowSize, fftData);

                ComputeFourierScore_FFTModulusSquared(fftData);

                ComputeEnergyPerBand();

                ComputeAvgEnergyFromHistory();

                ComputeEnergyVarianceFromHistory();

                UpdateHistory();

                var bandsAbove = CurrentEnergyAboveLocalLimit();
                if(bandsAbove > 0)
                    beats.Add(new Beat {
                        AsOffset = offset,
                        AsTime = new TimeSpan((offset + windowSize/2) * (TimeSpan.TicksPerSecond / sampleRate)),
                        Bands = bandsAbove,
                    });
            }
        }

        public List<Beat> GetBeats()
        {
            return beats;
        }

        private ulong CurrentEnergyAboveLocalLimit()
        {
            ulong bands = 0;
            ulong bit = 1;
            for (int i = 0; i < bandCount; i++)
            {
                if (CurrentWindowEnergy[i] > C * AverageEnergy[i] && EnergyVariance[i] > V0)
                    bands |= bit;
                bit <<= 1;
            }
            return bands;
        }

        private void UpdateHistory()
        {
            // shift Ei to the right and update
            for (int i = 0; i < bandCount; i++)
            {
                var prev = HistoricEnergy[i];
                HistoricEnergy[i] = new double[historySize];
                Array.Copy(prev, 0, HistoricEnergy[i], 1, historySize - 1);
                HistoricEnergy[i][0] = CurrentWindowEnergy[i];
            }
        }

        private void ComputeEnergyVarianceFromHistory()
        {
            for (int i = 0; i < bandCount; i++)
            {
                double sum = 0;
                for (int k = 0; k < historySize; k++)
                {
                    var delta = HistoricEnergy[i][k] - AverageEnergy[i];
                    sum += delta * delta;
                }
                EnergyVariance[i] = sum / historySize;
            }
        }

        private void ComputeAvgEnergyFromHistory()
        {
            for (int i = 0; i < bandCount; i++)
            {
                double sum = 0;
                for (int k = 0; k < historySize; k++)
                    sum += HistoricEnergy[i][k];
                AverageEnergy[i] = sum / historySize;
            }
        }

        private void ComputeEnergyPerBand()
        {
            var kBase = 0;
            for (int i = 0; i < bandCount; i++)
            {
                double sum = 0;
                for (int k = 0; k < bandWidths[i]; k++)
                    sum += FourierScore[k + kBase];
                CurrentWindowEnergy[i] = bandWidths[i] * (sum / windowSize);

                kBase += bandWidths[i];
            }
        }

        private void ComputeFourierScore_FFTModulusSquared(Complex[] fftData)
        {
            for (int k = 0; k < windowSize; k++)
                FourierScore[k] = fftData[k].Real * fftData[k].Real + fftData[k].Imaginary * fftData[k].Imaginary;
        }

        private static void FillFFTData(Span<short> windowL, Span<short> windowR, Complex[] fftData)
        {
            for (int k = 0; k < windowSize; k++)
                fftData[k] = new Complex { Real = windowL[k], Imaginary = windowR[k] };
        }
    }
}
using System;

namespace BeatDetection
{
    internal static class LanczosFFT
    {
        public static void Apply(int N, Span<float> data)
        {
			if (N == 1) return;

			Apply(N/2, data);
			Apply(N/2, data.Slice(N));

			float wtemp, tempr, tempi, wr, wi, wpr, wpi;
			//		wtemp = Sin<N,1,T>::value();
			wtemp = (float)Math.Sin(Math.PI / N);
			wpr = -2.0f * wtemp * wtemp;
			//		wpi = -Sin<N,2,T>::value();
			wpi = -(float)Math.Sin(2 * Math.PI / N);
			wr = 1.0f;
			wi = 0.0f;
			for (int i = 0; i < N; i += 2)
			{
				tempr = data[i + N] * wr - data[i + N + 1] * wi;
				tempi = data[i + N] * wi + data[i + N + 1] * wr;
				
				data[i + N] = data[i] - tempr;
				data[i + N + 1] = data[i + 1] - tempi;
				data[i] += tempr;
				data[i + 1] += tempi;

				wtemp = wr;
				wr += wr * wpr - wi * wpi;
				wi += wi * wpr + wtemp * wpi;
			}
		}
    }
}

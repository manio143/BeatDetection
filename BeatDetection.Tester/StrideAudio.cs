using System;
using System.Threading.Tasks;
using Stride.Audio;
using Stride.Core;

namespace BeatDetection
{
    class StrideAudio : IDisposable
    {
        AudioLayer.Device Device;
        AudioLayer.Listener Listener;
        AudioLayer.Source Source;
        AudioLayer.Buffer PreloadedBuffer;

        public StrideAudio(UnmanagedArray<byte> pcmData, int dataLen, int sampleRate)
        {
            if (!AudioLayer.Init())
            {
                throw new Exception("Failed to initialize the audio native layer.");
            }
            Device = AudioLayer.Create(null, AudioLayer.DeviceFlags.None);
            Listener = AudioLayer.ListenerCreate(Device);
            AudioLayer.ListenerEnable(Listener);
            Source = AudioLayer.SourceCreate(Listener, sampleRate, 1, mono: false, spatialized: false, streamed: false, hrtf: false, hrtfDirectionFactor: 0f, environment: HrtfEnvironment.Small);

            PreloadedBuffer = AudioLayer.BufferCreate(dataLen);
            AudioLayer.BufferFill(PreloadedBuffer, pcmData.Pointer, dataLen, sampleRate, mono: false);

            AudioLayer.SourceSetBuffer(Source, PreloadedBuffer);
        }

        public void Play() => AudioLayer.SourcePlay(Source);
        public void Stop() => Task.Run(() => AudioLayer.SourceStop(Source));

        public void Dispose()
        {
            AudioLayer.SourceDestroy(Source);
            AudioLayer.ListenerDisable(Listener);
            AudioLayer.ListenerDestroy(Listener);
            AudioLayer.Destroy(Device);
        }
    }
}
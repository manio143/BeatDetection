using System;
using System.Collections.Generic;

namespace BeatDetection
{
    public interface IBeatDetection
    {
        void Detect(Span<short> memory);

        List<DetectedBeat> GetBeats();
    }
}

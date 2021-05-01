# Beat detection algorithm

## V1
This is a C# implementation of a beat detection algorithm described in [this article on GameDev.net](https://www.gamedev.net/tutorials/_/technical/math-and-physics/beat-detection-algorithms-r1952/).

It came up as part of my research for [Rise of the Undeaf](//github.com/manio143/RiseOfTheUndeaf).

License is MIT. The Fourier algorithm has been copied from <https://github.com/hughpyle/inguz-DSPUtil> under MIT license.

Quality of V1 detection is quite bad, except for fairly specific setups.

## V2
Licence for V2 is LGPL. The BeatDetektor and LanczosFFT have been ported (C++ to C#) from <https://sourceforge.net/p/beatdetektor/> under LGPL license.

Due to the licensing split here (which I'm not completely sure how to handle) I suppose this has to be treated as a minimal standalone DLL, should it be incorporated into any other project.
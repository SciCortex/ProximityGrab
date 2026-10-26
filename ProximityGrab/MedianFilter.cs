/*
SPDX-License-Identifier: LGPL-3.0-only
Copyright (C) 2026 Shawn Betts
*/

using System;

namespace ProximityGrab;

// Running median over the last few frames of one gesture reading. Hand
// tracking glitches show up as short spikes (a finger snapping straight for a
// couple of frames); a median drops any spike shorter than half the window
// outright, where an average would only spread it out. Costs half the window
// in latency on real changes.
internal sealed class MedianFilter
{
    public const int MaxFrames = 15;

    private readonly float[] _values = new float[MaxFrames];
    private int _count;
    private int _next;
    private int _frames;

    // Adds this frame's reading and returns the median of the last `frames`
    // readings (fewer right after a reset). An untracked (NaN) reading
    // restarts the window and passes through.
    public float Push(float value, int frames)
    {
        frames = Math.Clamp(frames, 1, MaxFrames);
        if (float.IsNaN(value) || frames != _frames)
        {
            Reset();
            _frames = frames;
            if (float.IsNaN(value))
                return value;
        }
        _values[_next] = value;
        _next = (_next + 1) % frames;
        if (_count < frames)
            _count++;

        Span<float> sorted = stackalloc float[MaxFrames];
        sorted = sorted.Slice(0, _count);
        _values.AsSpan(0, _count).CopyTo(sorted);
        sorted.Sort();
        return sorted[(_count - 1) / 2];
    }

    public void Reset()
    {
        _count = 0;
        _next = 0;
    }
}

/*
SPDX-License-Identifier: LGPL-3.0-only
Copyright (C) 2026 Shawn Betts
*/

using System.Collections.Generic;
using FrooxEngine;

namespace ProximityGrab;

// Tells when physical controllers are in use, so the gestures can stand down.
// Seen on Quest 2 (ProximityGrab's InputSourceLog): picking up a controller
// takes the hand-tracking devices untracked for a moment, then a controller
// device tracks (some sessions make a new set of devices, some reuse the
// same ones). In hand tracking the controllers are stand-ins that ride on the
// hands and never report a touch; real ones report touch, grip or trigger
// from the first frame they're held. So a controller that has reported input
// since it last became tracked is a real one, and while any real one is
// tracked, the controllers are in use. The flag sticks while the device stays
// tracked, because the touch sensors flicker while it's held, and clears when
// it goes untracked, which every switch between the two modes passes through.
internal static class ControllerGuard
{
    private const float AnalogOn = 0.1f;

    private static readonly List<ControllerBase> Controllers = new();
    private static readonly HashSet<ControllerBase> Real = new();

    public static bool InUse(InputInterface input)
    {
        bool inUse = false;
        Controllers.Clear();
        input.GetDevices(Controllers);
        foreach (var c in Controllers)
        {
            if (!c.IsTracking)
            {
                Real.Remove(c);
                continue;
            }
            if (!Real.Contains(c) && HasInput(c))
                Real.Add(c);
            if (Real.Contains(c))
                inUse = true;
        }
        Controllers.Clear();
        return inUse;
    }

    private static bool HasInput(ControllerBase c)
    {
        if (c is TouchController t)
            return t.TriggerTouch.Held || t.JoystickTouch.Held || t.ThumbRestTouch.Held
                || t.ButtonXA_Touch.Held || t.ButtonYB_Touch.Held
                || t.Grip.Value > AnalogOn || t.Trigger.Value > AnalogOn;
        return c.TouchingStrength.Held || c.TouchingAxis.Held || c.Strength.Value > AnalogOn
            || c.ActionGrab.Held || c.ActionPrimary.Held;
    }
}

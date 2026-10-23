/*
SPDX-License-Identifier: LGPL-3.0-only
Copyright (C) 2026 Shawn Betts
Portions © 2025 XDelta (Resonite Mod Template ExampleMod)
*/

using System.Reflection;
using FrooxEngine;
using HarmonyLib;

namespace ProximityGrab;

internal static class TwoHandGrab
{
    private static readonly FieldInfo ScaleReference = AccessTools.Field(typeof(Grabber), "_scaleReference")!;

    // A Grabber that fails to grab an object held by the user's other Grabber
    // turns into that Grabber's scale reference (Grabber.Grab, the
    // _scaleReference branch) and reports success. Gesture grabs never scale:
    // drop the reference before Grabber.Update applies it, and report a grab
    // only if something was actually picked up.
    internal static bool SuppressScaling(InteractionHandler handler, bool grabbed)
    {
        Grabber? grabber = handler.Grabber;
        if (grabber == null || !grabber.IsScaling)
            return grabbed;
        ((SyncRef<Grabber>)ScaleReference.GetValue(grabber)!).Target = null;
        return grabber.IsHoldingObjects;
    }
}

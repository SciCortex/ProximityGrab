/* 
SPDX-License-Identifier: LGPL-3.0-only
Copyright (C) 2026 Shawn Betts
Portions © 2025 XDelta (Resonite Mod Template ExampleMod)
*/

using System.Runtime.CompilerServices;
using FrooxEngine;
using Renderite.Shared;

namespace ProximityGrab;

internal enum GrabGestureKind
{
    None,
    Fist,
    Pinch,
}

internal sealed class ProximityGrabState
{
    public bool GestureDriven;
    public bool HasTrackingHands;
    public bool ProximityGrabActive;
    public GrabGestureKind ActiveGesture;
    public bool FistEngaged;
    public bool PinchEngagedIndex;
    public bool PinchEngagedMiddle;
    public bool PinchEngagedRing;
    public FingerType GestureFinger = FingerType.Index;
    public Hand? GestureHand;
    public bool LastGrabResult;

    // Triple-grab chain (see TripleGrab).
    public int TripleCount;
    public GrabGestureKind TripleKind;
    public double TripleLastTime;

    // Missed-pinch feedback rig (local slots, built on first miss).
    public MissEffect? MissEffect;

    // Triple-grab stash confirmation (local slots, built on first stash).
    public StashEffect? StashEffect;

    private static readonly ConditionalWeakTable<InteractionHandler, ProximityGrabState> Table = new();

    public static ProximityGrabState Get(InteractionHandler handler)
    {
        return Table.GetValue(handler, _ => new ProximityGrabState());
    }
}

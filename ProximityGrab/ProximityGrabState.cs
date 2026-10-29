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

    // Tracking-glitch tolerance (see MedianFilter, FistGesture.Update).
    public readonly MedianFilter FistAverage = new();
    public readonly MedianFilter FistMin = new();
    public readonly MedianFilter FistMinJoint = new();
    public readonly MedianFilter[] PinchDistance = { new(), new(), new() };   // index, middle, ring
    public readonly MedianFilter[] PinchCurl = { new(), new(), new() };
    public bool FilterDisagrees;    // this frame: a raw reading would have flipped a gesture the median held
    public int FilterHiddenFrames;  // length of the current run of such frames
    public double DropStart = -1;   // world time the held gesture went away; -1 = not pending

    // Last gesture written to the input-source log (see InputSourceLog).
    public GrabGestureKind LoggedGesture;

    // Double grab: a first grab waiting for its second (see DoubleGrab).
    public bool DoubleGrabArmed;
    public GrabGestureKind DoubleGrabKind;
    public double DoubleGrabTime;

    // Two-hand hold this hand takes part in (see TwoHandGrab); shared by both hands.
    public TwoHandSession? TwoHand;

    // Two-hand tether line (local slots, built on first two-hand hold).
    public TetherEffect? Tether;

    // Missed-pinch feedback rig (local slots, built on first miss).
    public MissEffect? MissEffect;

    // Double-grab stash confirmation (local slots, built on first stash).
    public StashEffect? StashEffect;

    private static readonly ConditionalWeakTable<InteractionHandler, ProximityGrabState> Table = new();

    public static ProximityGrabState Get(InteractionHandler handler)
    {
        return Table.GetValue(handler, _ => new ProximityGrabState());
    }
}

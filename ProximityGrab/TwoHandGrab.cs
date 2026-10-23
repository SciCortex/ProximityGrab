/*
SPDX-License-Identifier: LGPL-3.0-only
Copyright (C) 2026 Shawn Betts
Portions © 2025 XDelta (Resonite Mod Template ExampleMod)
*/

using System.Collections.Generic;
using System.Reflection;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using Renderite.Shared;

namespace ProximityGrab;

// One object held by both hands. The objects stay parented under the
// primary (first) hand's Grabber; anchors and up vectors are stored in each
// hand's Grabber slot space so they ride rigidly with the hands.
internal sealed class TwoHandSession
{
    public struct Held
    {
        public Grabbable Grabbable;
        // Pose relative to the anchor frame at session start.
        public float3 Position;
        public floatQ Rotation;
    }

    public readonly InteractionHandler Primary;
    public readonly InteractionHandler Secondary;
    public readonly List<Held> Objects = new();
    public float3 PrimaryAnchor;
    public float3 SecondaryAnchor;
    public float3 PrimaryUp;
    public float3 SecondaryUp;

    public TwoHandSession(InteractionHandler primary, InteractionHandler secondary)
    {
        Primary = primary;
        Secondary = secondary;
    }
}

// Two-hand grab: a gesture grab with the free hand on an object the other
// hand is gesture-holding adds a second anchor instead of the engine's
// two-hand scaling. While both hold, the object pivots on the primary anchor
// and aims along the line to the secondary anchor; roll about that line is
// the average of both hands' twist (each hand carries the starting up vector
// rigidly, and the two are summed).
internal static class TwoHandGrab
{
    private static readonly FieldInfo ScaleReference = AccessTools.Field(typeof(Grabber), "_scaleReference")!;

    // Anchors closer than this (user space, m) give no usable line.
    private const float MinSpan = 0.01f;

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
        ((SyncRef<Grabber>)ScaleReference.GetValue(grabber)!).Target = null!;
        return grabber.IsHoldingObjects;
    }

    // Called for a gesture grab before the normal grab runs. Starts a session
    // when this hand's grab sweep touches something the other hand holds.
    internal static bool TryStart(InteractionHandler handler, ProximityGrabState state)
    {
        if (state.TwoHand != null || !TryGetOther(handler, out InteractionHandler? other, out ProximityGrabState? otherState))
            return false;
        Grabber? grabber = handler.Grabber;
        Grabber? otherGrabber = other.Grabber;
        if (grabber == null || otherGrabber == null || otherState.TwoHand != null || !otherGrabber.IsHoldingObjects)
            return false;
        if (!TryGetGrabPoint(handler, state, out float3 point, out float radius)
            || !TouchesHeld(handler, point, radius, otherGrabber)
            || !TryGetGrabPoint(other, otherState, out float3 otherPoint, out _))
            return false;
        TwoHandSession? session = Begin(other, otherGrabber, otherPoint, handler, grabber, point);
        if (session == null)
            return false;
        state.TwoHand = session;
        otherState.TwoHand = session;
        return true;
    }

    // Runs every frame for each local handler; only the primary drives the pose.
    internal static void Update(InteractionHandler handler)
    {
        ProximityGrabState state = ProximityGrabState.Get(handler);
        TwoHandSession? session = state.TwoHand;
        if (session == null || session.Primary != handler)
            return;
        Grabber? primary = session.Primary.Grabber;
        Grabber? secondary = session.Secondary.Grabber;
        if (primary == null || secondary == null || session.Secondary.IsRemoved
            || !ProximityGrabState.Get(session.Secondary).ProximityGrabActive)
        {
            End(session);
            return;
        }
        // Dropped, destroyed or taken by someone else: stop posing it.
        session.Objects.RemoveAll(h => h.Grabbable.IsRemoved || h.Grabbable.Grabber != primary);
        if (session.Objects.Count == 0)
        {
            End(session);
            return;
        }
        float3 a1 = primary.Slot.LocalPointToGlobal(session.PrimaryAnchor);
        float3 a2 = secondary.Slot.LocalPointToGlobal(session.SecondaryAnchor);
        float3 u1 = primary.Slot.GlobalRotation * session.PrimaryUp;
        float3 u2 = secondary.Slot.GlobalRotation * session.SecondaryUp;
        if (!TryFrame(a1, a2, u1 + u2, u1, MinSpan * (handler.LocalUserRoot?.GlobalScale ?? 1f), out floatQ frame))
            return;
        foreach (TwoHandSession.Held held in session.Objects)
        {
            Slot slot = held.Grabbable.Slot;
            slot.GlobalPosition = a1 + frame * held.Position;
            slot.GlobalRotation = frame * held.Rotation;
        }
        if (ProximityGrabMod.DebugShowPinch && handler.World != Userspace.UserspaceWorld)
        {
            colorX color = colorX.Yellow;
            handler.Debug.Line(in a1, in a2, in color, 0.002f * (handler.LocalUserRoot?.GlobalScale ?? 1f), local: true);
        }
    }

    // Called when a hand's gesture grab ends, before EndGrab. Either hand
    // letting go ends the session; the primary still holds the objects.
    internal static void OnRelease(InteractionHandler handler, ProximityGrabState state)
    {
        TwoHandSession? session = state.TwoHand;
        if (session == null)
            return;
        End(session);
    }

    private static TwoHandSession? Begin(InteractionHandler primary, Grabber primaryGrabber, float3 primaryPoint,
        InteractionHandler secondary, Grabber secondaryGrabber, float3 secondaryPoint)
    {
        floatQ h1 = primaryGrabber.Slot.GlobalRotation;
        floatQ h2 = secondaryGrabber.Slot.GlobalRotation;
        float3 handUp = h1 * float3.Up;
        float3 handForward = h1 * float3.Forward;
        float minSpan = MinSpan * (primary.LocalUserRoot?.GlobalScale ?? 1f);
        if (!TryFrame(primaryPoint, secondaryPoint, handUp, handForward, minSpan, out floatQ frame))
            return null;
        float3 up = frame * float3.Up;
        var session = new TwoHandSession(primary, secondary)
        {
            PrimaryAnchor = primaryGrabber.Slot.GlobalPointToLocal(primaryPoint),
            SecondaryAnchor = secondaryGrabber.Slot.GlobalPointToLocal(secondaryPoint),
            PrimaryUp = h1.Inverted * up,
            SecondaryUp = h2.Inverted * up,
        };
        floatQ inverse = frame.Inverted;
        foreach (IGrabbable held in primaryGrabber.GrabbedObjects)
        {
            if (held is not Grabbable grabbable || grabbable.IsRemoved || grabbable.Grabber != primaryGrabber)
                continue;
            Slot slot = grabbable.Slot;
            session.Objects.Add(new TwoHandSession.Held
            {
                Grabbable = grabbable,
                Position = inverse * (slot.GlobalPosition - primaryPoint),
                Rotation = inverse * slot.GlobalRotation,
            });
        }
        return session.Objects.Count > 0 ? session : null;
    }

    private static void End(TwoHandSession session)
    {
        ProximityGrabState primary = ProximityGrabState.Get(session.Primary);
        if (primary.TwoHand == session)
            primary.TwoHand = null;
        ProximityGrabState secondary = ProximityGrabState.Get(session.Secondary);
        if (secondary.TwoHand == session)
            secondary.TwoHand = null;
    }

    // Frame looking down the a1->a2 line, rolled toward `up` (or `fallbackUp`
    // when `up` is parallel to the line or degenerate).
    private static bool TryFrame(float3 a1, float3 a2, float3 up, float3 fallbackUp, float minSpan, out floatQ frame)
    {
        frame = floatQ.Identity;
        float3 line = a2 - a1;
        float length = line.Magnitude;
        if (length < minSpan)
            return false;
        float3 forward = line / length;
        if (!TryPerpendicular(up, forward, out float3 rollUp) && !TryPerpendicular(fallbackUp, forward, out rollUp))
            return false;
        frame = floatQ.LookRotation(in forward, in rollUp);
        return true;
    }

    private static bool TryPerpendicular(float3 v, float3 axis, out float3 result)
    {
        result = v - axis * MathX.Dot(in v, in axis);
        float length = result.Magnitude;
        if (length < 1e-3f)
            return false;
        result /= length;
        return true;
    }

    private static bool TryGetOther(InteractionHandler handler, out InteractionHandler other, out ProximityGrabState otherState)
    {
        Chirality otherSide = handler.Side.Value == Chirality.Left ? Chirality.Right : Chirality.Left;
        other = handler.World.LocalUser.GetInteractionHandler(otherSide);
        otherState = null!;
        if (other == null || other == handler || other.IsRemoved)
            return false;
        otherState = ProximityGrabState.Get(other);
        return otherState.ProximityGrabActive;
    }

    // Where this hand's gesture grabs: the pinch point for pinches, the
    // Grabber (the engine's palm grab sphere center) for fists. The radius
    // matches the corresponding grab sweep.
    private static bool TryGetGrabPoint(InteractionHandler handler, ProximityGrabState state, out float3 point, out float radius)
    {
        float scale = handler.LocalUserRoot?.GlobalScale ?? 1f;
        if (state.ActiveGesture == GrabGestureKind.Pinch && state.GestureHand != null
            && PrecisionGrab.TryGetPinchPoints(handler, state.GestureHand, state.GestureFinger, out float3 tip, out float3 thumb))
        {
            point = MathX.Lerp(tip, thumb, 0.5f);
            radius = ProximityGrabMod.PrecisionMaxRadius * scale;
            return true;
        }
        Grabber? grabber = handler.Grabber;
        point = grabber?.Slot.GlobalPosition ?? float3.Zero;
        radius = InteractionHandler.GRAB_RADIUS * scale;
        return grabber != null;
    }

    private static bool TouchesHeld(InteractionHandler handler, float3 point, float radius, Grabber otherGrabber)
    {
        List<ICollider> colliders = Pool.BorrowList<ICollider>();
        try
        {
            handler.World.Physics.SphereOverlap(in point, radius, colliders);
            foreach (ICollider collider in colliders)
            {
                Slot? slot = collider.Slot;
                if (slot == null)
                    continue;
                foreach (IGrabbable held in otherGrabber.GrabbedObjects)
                {
                    if (held is Grabbable && !held.IsRemoved && slot.IsChildOf(held.Slot, includeSelf: true))
                        return true;
                }
            }
            return false;
        }
        finally
        {
            Pool.Return(ref colliders);
        }
    }
}

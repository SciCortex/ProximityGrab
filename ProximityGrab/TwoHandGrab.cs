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
// primary (first) hand's Grabber; anchors are stored in each hand's Grabber
// slot space so they ride rigidly with the hands.
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
    // Anchor frame (global), looking down the anchor line; updated each frame.
    public floatQ Frame;
    // Grabber rotations (global) as of the last update, for per-frame twist.
    public floatQ PrimaryRotation;
    public floatQ SecondaryRotation;
    public bool TetherShown;

    public TwoHandSession(InteractionHandler primary, InteractionHandler secondary)
    {
        Primary = primary;
        Secondary = secondary;
    }
}

// Two-hand grab: a gesture grab with the free hand on an object the other
// hand is gesture-holding adds a second anchor instead of the engine's
// two-hand scaling. While both hold, the object pivots on the primary anchor
// and aims along the line to the secondary anchor. Roll about that line is
// accumulated frame by frame from the hands' wrist twist about it (see
// CombineTwist), so twisting either wrist turns the object with it.
internal static class TwoHandGrab
{
    private static readonly FieldInfo ScaleReference = AccessTools.Field(typeof(Grabber), "_scaleReference")!;
    private static readonly FieldInfo GrabbedObjects = AccessTools.Field(typeof(Grabber), "_grabbedObjects")!;
    private static readonly FieldInfo GrabbableGrabber = AccessTools.Field(typeof(Grabbable), "_grabber")!;

    // Anchors closer than this (user space, m) give no usable line.
    private const float MinSpan = 0.01f;

    // Fraction of the grab reach the secondary hand must come back within to
    // hide a shown tether.
    private const float TetherHideReach = 0.5f;

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
            || !TouchesHeld(handler, point, radius, otherGrabber, grabber)
            || !TryGetGrabPoint(other, otherState, out float3 otherPoint, out _))
            return false;
        TwoHandSession? session = Begin(other, otherGrabber, otherPoint, handler, grabber, point);
        if (session == null)
            return false;
        state.TwoHand = session;
        otherState.TwoHand = session;
        return true;
    }

    // Runs every frame for each local handler; only the primary drives the
    // pose and shows the tether.
    internal static void Update(InteractionHandler handler)
    {
        ProximityGrabState state = ProximityGrabState.Get(handler);
        if (!Drive(handler, state))
            TetherEffect.Hide(state);
    }

    // True while this hand is the primary of a live session.
    private static bool Drive(InteractionHandler handler, ProximityGrabState state)
    {
        TwoHandSession? session = state.TwoHand;
        if (session == null || session.Primary != handler)
            return false;
        Grabber? primary = session.Primary.Grabber;
        Grabber? secondary = session.Secondary.Grabber;
        if (primary == null || secondary == null || session.Secondary.IsRemoved
            || !ProximityGrabState.Get(session.Secondary).ProximityGrabActive)
        {
            End(session);
            return false;
        }
        // Dropped, destroyed or taken by someone else: stop posing it.
        session.Objects.RemoveAll(h => h.Grabbable.IsRemoved || h.Grabbable.Grabber != primary);
        if (session.Objects.Count == 0)
        {
            End(session);
            return false;
        }
        float3 a1 = primary.Slot.LocalPointToGlobal(session.PrimaryAnchor);
        float3 a2 = secondary.Slot.LocalPointToGlobal(session.SecondaryAnchor);
        floatQ h1 = primary.Slot.GlobalRotation;
        floatQ h2 = secondary.Slot.GlobalRotation;
        float3 line = a2 - a1;
        float length = line.Magnitude;
        if (length >= MinSpan * (handler.LocalUserRoot?.GlobalScale ?? 1f))
        {
            float3 forward = line / length;
            float roll = CombineTwist(
                Twist(h1 * session.PrimaryRotation.Inverted, forward),
                Twist(h2 * session.SecondaryRotation.Inverted, forward));
            // Swing the frame onto the new line by the shortest rotation, then
            // roll it about the line.
            floatQ frame = floatQ.AxisAngleRad(forward, roll)
                * floatQ.FromToRotation(session.Frame * float3.Forward, forward)
                * session.Frame;
            // Re-square it so float error can't build up over the hold.
            float3 up = frame * float3.Up;
            session.Frame = floatQ.LookRotation(in forward, in up);
            foreach (TwoHandSession.Held held in session.Objects)
            {
                Slot slot = held.Grabbable.Slot;
                slot.GlobalPosition = a1 + session.Frame * held.Position;
                slot.GlobalRotation = session.Frame * held.Rotation;
            }
        }
        session.PrimaryRotation = h1;
        session.SecondaryRotation = h2;
        if (!ProximityGrabMod.TwoHandTether || handler.World == Userspace.UserspaceWorld)
            return false;
        // The tether joins where the hands are grabbing now (the live pinch
        // points), not the hand-rigid anchors, so it stays on the fingers.
        if (!TryGetGrabPoint(session.Primary, state, out float3 from, out _))
            from = a1;
        if (!TryGetGrabPoint(session.Secondary, ProximityGrabState.Get(session.Secondary), out float3 to, out float reach))
            to = a2;
        // Only while the secondary hand has drifted out of grab reach of the
        // object, where the hold otherwise looks like action at a distance.
        // Once shown, it takes a tighter reach to hide again, so it doesn't
        // flicker at the edge.
        float hideReach = session.TetherShown ? reach * TetherHideReach : reach;
        session.TetherShown = !TouchesHeld(handler, to, hideReach, primary);
        if (!session.TetherShown)
            return false;
        TetherEffect.Show(handler, state, from, to);
        return true;
    }

    // Called when a hand's gesture grab ends, before EndGrab. Either hand
    // letting go ends the session. The primary already holds the objects;
    // when it is the one letting go they move to the secondary first, so
    // EndGrab finds nothing to drop.
    internal static void OnRelease(InteractionHandler handler, ProximityGrabState state)
    {
        TwoHandSession? session = state.TwoHand;
        if (session == null)
            return;
        End(session);
        if (session.Primary != handler || session.Secondary.IsRemoved
            || !ProximityGrabState.Get(session.Secondary).ProximityGrabActive)
            return;
        Grabber? from = session.Primary.Grabber;
        Grabber? to = session.Secondary.Grabber;
        if (from != null && to != null)
            HandOff(from, to, session.Objects);
    }

    // Moves held objects between the user's own Grabbers without a release
    // and re-grab: Grabbable.Release reparents (or destroys, with
    // DestroyOnRelease) even with events suppressed, and both ends would fire
    // grab events and undo steps. The Grabbable keeps its remembered parent,
    // so the eventual release drops it as if the first hand had.
    private static void HandOff(Grabber from, Grabber to, List<TwoHandSession.Held> objects)
    {
        var fromList = (List<IGrabbable>)GrabbedObjects.GetValue(from)!;
        var toList = (List<IGrabbable>)GrabbedObjects.GetValue(to)!;
        foreach (TwoHandSession.Held held in objects)
        {
            Grabbable grabbable = held.Grabbable;
            if (grabbable.IsRemoved || grabbable.Grabber != from)
                continue;
            ((SyncRef<Grabber>)GrabbableGrabber.GetValue(grabbable)!).Target = to;
            grabbable.Slot.SetParent(to.HolderSlot, keepGlobalTransform: true);
            fromList.Remove(grabbable);
            toList.Add(grabbable);
        }
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
        var session = new TwoHandSession(primary, secondary)
        {
            PrimaryAnchor = primaryGrabber.Slot.GlobalPointToLocal(primaryPoint),
            SecondaryAnchor = secondaryGrabber.Slot.GlobalPointToLocal(secondaryPoint),
            Frame = frame,
            PrimaryRotation = h1,
            SecondaryRotation = h2,
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

    // Rotation angle (radians) of `delta` about the unit `axis`: the twist
    // part of its swing-twist decomposition, taken the short way round.
    private static float Twist(floatQ delta, float3 axis)
    {
        float along = delta.x * axis.x + delta.y * axis.y + delta.z * axis.z;
        float w = delta.w;
        if (w < 0f)
        {
            along = -along;
            w = -w;
        }
        return 2f * MathX.Atan2(along, w);
    }

    // One frame's roll from both hands' twist. Opposite twists add, so they
    // cancel; twists the same way count once, at the larger, so turning both
    // hands together (rolling a bar, or turning the body) rolls the object
    // with them rather than twice as far. Either hand twisting alone rolls it
    // fully.
    private static float CombineTwist(float t1, float t2)
    {
        if (t1 * t2 <= 0f)
            return t1 + t2;
        return MathX.Abs(t1) > MathX.Abs(t2) ? t1 : t2;
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

    // Whether the sphere touches anything under an object `otherGrabber`
    // holds. With `grabber`, a separately grabbable part of the held object
    // (a pen's lid) is left to the normal grab: if the sphere touches one,
    // this is false, as the engine's grab would take that part.
    private static bool TouchesHeld(InteractionHandler handler, float3 point, float radius, Grabber otherGrabber, Grabber? grabber = null)
    {
        List<ICollider> colliders = Pool.BorrowList<ICollider>();
        try
        {
            handler.World.Physics.SphereOverlap(in point, radius, colliders);
            bool touches = false;
            foreach (ICollider collider in colliders)
            {
                Slot? slot = collider.Slot;
                if (slot == null)
                    continue;
                switch (Classify(slot, otherGrabber, grabber))
                {
                    case Touch.Part:
                        return false;
                    case Touch.Held:
                        touches = true;
                        break;
                }
            }
            return touches;
        }
        finally
        {
            Pool.Return(ref colliders);
        }
    }

    private enum Touch
    {
        None,
        Held,
        Part,
    }

    // Walks up from a touched collider's slot like Grabber.FindGrabbableInParents:
    // the first grabbable found is what a grab there would take. Reaching a
    // held object's slot first means the held object itself; an enabled
    // Grabbable `grabber` can take on the way up is a part of it.
    private static Touch Classify(Slot slot, Grabber otherGrabber, Grabber? grabber)
    {
        bool underHeld = false;
        foreach (IGrabbable held in otherGrabber.GrabbedObjects)
        {
            if (held is Grabbable && !held.IsRemoved && slot.IsChildOf(held.Slot, includeSelf: true))
            {
                underHeld = true;
                break;
            }
        }
        if (!underHeld)
            return Touch.None;
        if (grabber == null)
            return Touch.Held;
        for (Slot? s = slot; s != null && !s.IsRootSlot; s = s.Parent)
        {
            foreach (IGrabbable held in otherGrabber.GrabbedObjects)
            {
                if (held is Grabbable && !held.IsRemoved && held.Slot == s)
                    return Touch.Held;
            }
            if (s.GetComponent<IGrabBlock>(null!, excludeDisabled: true) != null)
                return Touch.Held;
            if (s.GetComponent((IGrabbable g) => g.CanGrab(grabber), excludeDisabled: true) != null)
                return Touch.Part;
        }
        return Touch.Held;
    }
}

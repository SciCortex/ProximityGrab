/*
SPDX-License-Identifier: LGPL-3.0-only
Copyright (C) 2026 Shawn Betts
Portions © 2025 XDelta (Resonite Mod Template ExampleMod)
*/

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;

namespace ProximityGrab;

// Triple grab: TripleGrabCount (2 or 3, default 3) successful gesture grabs of the same kind (fist or pinch),
// each within TripleGrabSeconds of the previous one, stash the held object on
// the user root so it follows the user around. The stash works by retargeting
// the Grabbable's remembered pre-grab parent, so the engine's own release path
// drops the object under the user root. The real parent is remembered here and
// put back as soon as the object is grabbed again (Patch_Grabbable_Grab).
internal static class TripleGrab
{
    private sealed class StashInfo
    {
        public Slot? OriginalParent;
    }

    private static readonly ConditionalWeakTable<Slot, StashInfo> Stashed = new();

    private static readonly FieldInfo LastParent = AccessTools.Field(typeof(Grabbable), "_lastParent")!;
    private static readonly FieldInfo LastParentIsUserSpace = AccessTools.Field(typeof(Grabbable), "_lastParentIsUserSpace")!;

    // Called right after a gesture-driven grab succeeded.
    internal static void OnGestureGrab(InteractionHandler handler, ProximityGrabState state, GrabGestureKind kind)
    {
        if (!ProximityGrabMod.TripleGrabEnabled || handler.World == Userspace.UserspaceWorld)
        {
            state.TripleCount = 0;
            return;
        }
        double now = handler.Time.WorldTime;
        bool chained = state.TripleCount > 0
            && state.TripleKind == kind
            && now - state.TripleLastTime <= ProximityGrabMod.TripleGrabSeconds;
        state.TripleCount = chained ? state.TripleCount + 1 : 1;
        state.TripleKind = kind;
        state.TripleLastTime = now;
        if (state.TripleCount < ProximityGrabMod.TripleGrabCount)
            return;
        state.TripleCount = 0;
        Stash(handler);
    }

    // A miss breaks the chain.
    internal static void OnGestureMiss(ProximityGrabState state) => state.TripleCount = 0;

    private static void Stash(InteractionHandler handler)
    {
        var root = handler.LocalUserRoot;
        var grabber = handler.Grabber;
        if (root == null || grabber == null)
            return;
        foreach (IGrabbable held in grabber.GrabbedObjects)
        {
            if (held is not Grabbable grabbable || grabbable.IsRemoved || !grabbable.IsGrabbed || !grabbable.ReparentOnRelease.Value)
                continue;
            // Never put an object under something that is itself inside it.
            if (root.Slot == grabbable.Slot || root.Slot.IsChildOf(grabbable.Slot))
                continue;
            var lastParent = (SyncRef<Slot>)LastParent.GetValue(grabbable)!;
            if (lastParent.Target == root.Slot)
                continue;
            Stashed.Remove(grabbable.Slot);
            Stashed.Add(grabbable.Slot, new StashInfo { OriginalParent = lastParent.Target });
            lastParent.Target = root.Slot;
            ((Sync<bool>)LastParentIsUserSpace.GetValue(grabbable)!).Value = false;
            StashEffect.Trigger(handler, ProximityGrabState.Get(handler), grabbable.Slot);
        }
    }

    // After any grab of a stashed object: forget the stash and make the engine
    // drop it back where it came from on the next release.
    internal static void OnGrabbed(Grabbable grabbable)
    {
        if (!Stashed.TryGetValue(grabbable.Slot, out StashInfo? info))
            return;
        var lastParent = (SyncRef<Slot>)LastParent.GetValue(grabbable)!;
        var userRoot = grabbable.World.LocalUser?.Root;
        // Only undo our own stash: if something else reparented it meanwhile, leave it be.
        if (userRoot == null || lastParent.Target != userRoot.Slot)
        {
            Stashed.Remove(grabbable.Slot);
            return;
        }
        Slot? original = info.OriginalParent;
        if (original == null || original.IsRemoved || original.IsChildOf(grabbable.Slot))
            original = grabbable.LocalUserSpace;
        lastParent.Target = original;
        ((Sync<bool>)LastParentIsUserSpace.GetValue(grabbable)!).Value = original == grabbable.LocalUserSpace;
        Stashed.Remove(grabbable.Slot);
    }
}

[HarmonyPatch(typeof(Grabbable))]
[HarmonyPatch("Grab")]
internal static class Patch_Grabbable_Grab
{
    [HarmonyPostfix]
    private static void Postfix(Grabbable __instance, IGrabbable __result)
    {
        if (__result == null)
            return;
        try
        {
            TripleGrab.OnGrabbed(__instance);
        }
        catch (Exception e)
        {
            UniLog.Error($"ProximityGrab: failed to restore stashed parent for {__instance}: {e}");
        }
    }
}

/*
SPDX-License-Identifier: LGPL-3.0-only
Copyright (C) 2026 Shawn Betts
Portions © 2025 XDelta (Resonite Mod Template ExampleMod)
*/

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;

namespace ProximityGrab;

internal static class Methods
{
    internal static Harmony? HarmonyInstance;

    private static readonly MethodInfo[] GrabMethods = typeof(InteractionHandler)
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        .Where(m => m.Name == "Grab")
        .ToArray();

    internal static readonly MethodInfo StartGrab = AccessTools.Method(typeof(InteractionHandler), "StartGrab")!;
    internal static readonly MethodInfo HoldGrab = AccessTools.Method(typeof(InteractionHandler), "HoldGrab")!;
    internal static readonly MethodInfo EndGrab = AccessTools.Method(typeof(InteractionHandler), "EndGrab")!;
    internal static readonly MethodInfo GrabNoLaser = GrabMethods.First(m => m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(bool));
    internal static readonly FieldInfo GrabBlockActions = AccessTools.Field(typeof(InteractionHandler), "_grabBlockActions")!;
}

// Runs the fist-gesture detector every frame for the local user.
[HarmonyPatch(typeof(InteractionHandler))]
[HarmonyPatch("OnCommonUpdate")]
internal static class Patch_InteractionHandler_OnCommonUpdate
{
    [HarmonyPostfix]
    private static void Postfix(InteractionHandler __instance)
    {
        try
        {
            if (!__instance.IsOwnedByLocalUser)
                return;
            if (__instance.World.Focus == World.WorldFocus.Background)
                return;
            FistGesture.Update(__instance);
            TwoHandGrab.Update(__instance);
        }
        catch (System.Exception e)
        {
            UniLog.Error($"ProximityGrab: failed to update gesture for {__instance}: {e}");
        }
    }
}

// No hands tracked: stock behavior passed straight through.
// Fist gesture: proximity grab only (never laser).
// Pinch gesture: precision grab at the tracked index/thumb pinch point.
// Other grab commands while hands tracked (gamepad/keyboard keep their bindings): stock path.
[HarmonyPatch(typeof(InteractionHandler))]
[HarmonyPatch("Grab")]
[HarmonyPatch(new System.Type[0])]
internal static class Patch_InteractionHandler_Grab
{
    [HarmonyPrefix]
    private static bool Prefix(InteractionHandler __instance, ref bool __result)
    {
        var state = ProximityGrabState.Get(__instance);
        if (!state.GestureDriven)
            return true;
        state.GestureDriven = false;
        try
        {
            if (TwoHandGrab.TryStart(__instance, state))
            {
                __result = true;
                state.LastGrabResult = true;
                return false;
            }
            __result = state.ActiveGesture == GrabGestureKind.Pinch
                ? PrecisionGrab.TryGrab(__instance, state.GestureHand, state.GestureFinger)
                : (bool)(Methods.GrabNoLaser.Invoke(__instance, new object[] { false, null! }) ?? false);
            __result = TwoHandGrab.SuppressScaling(__instance, __result);
        }
        catch (System.Exception e)
        {
            UniLog.Error($"ProximityGrab: gesture grab failed: {e}");
            __result = false;
        }
        state.LastGrabResult = (bool)__result;
        // if (!state.LastGrabResult
        //     && state.ActiveGesture == GrabGestureKind.Pinch
        //     && ProximityGrabMod.DebugShowPinch)
        // {
        //     UniLog.Log($"ProximityGrab: pinch grab failed: {PrecisionGrab.LastAttemptDetail}");
        // }
        return false;
    }
}

// While hands are tracked and gesture mode is on, unbind only VR-controller grab
// sources so the grab command can never fire from a controller/skeleton false
// positive; gamepad and keyboard/mouse grab bindings are kept. With gesture mode
// off the stock controller grip bindings are left intact (fully-stock behavior).
[HarmonyPatch(typeof(InputInterface))]
[HarmonyPatch("Bind")]
[HarmonyPatch(new System.Type[] { typeof(InputGroup) })]
internal static class Patch_InputInterface_Bind
{
    [HarmonyPostfix]
    private static void Postfix(InputGroup group)
    {
        try
        {
            if (!ProximityGrabMod.GestureMode)
                return;
            if (!FistGesture.HasTrackingHands)
                return;
            if (group is not InteractionHandlerInputs handlerInputs)
                return;
            if (group.Owner is not InteractionHandler handler || !handler.IsOwnedByLocalUser)
                return;
            RemoveControllerBindings(handlerInputs.Grab);
        }
        catch (System.Exception e)
        {
            UniLog.Error($"ProximityGrab: failed to strip grab bindings: {e}");
        }
    }

    internal static void RemoveControllerBindings(DigitalAction grab)
    {
        var bindingsField = typeof(InputAction<bool>).GetField("_bindings", BindingFlags.NonPublic | BindingFlags.Instance);
        if (bindingsField?.GetValue(grab) is not List<InputBinding<bool>> bindings)
            return;
        bindings.RemoveAll(b => b.ImplicitDevice is ControllerBase);
    }
}

// "Proximity Gesture Grab" toggle in the hand's Grabbing context submenu.
// The engine menu items are built inside an async continuation, so instead of
// patching the builder we record the requested submenu (sync prefix on
// OpenContextMenu) and append our item once the menu is populated (sync prefix
// on PositionContextMenu, which runs after the items are added).
// The item is a live AddToggleItem bound to a per-hand ValueField<bool> that
// mirrors the RML GestureMode key, so it flips in place exactly like StickyGrab.
internal static class MenuPatches
{
    private static Type? MenuOptionsType;
    private static int GrabbingOption = -1;

    private sealed class MenuOptionsHolder
    {
        public int Value = -1;
    }

    private static readonly ConditionalWeakTable<InteractionHandler, MenuOptionsHolder> LastMenuOptions = new();

    private static readonly ConditionalWeakTable<InteractionHandler, ValueField<bool>> GestureFields = new();

    internal static void Apply(Harmony harmony)
    {
        MenuOptionsType = AccessTools.Inner(typeof(InteractionHandler), "MenuOptions");
        if (MenuOptionsType != null)
        {
            try
            {
                GrabbingOption = (int)Enum.Parse(MenuOptionsType, "Grabbing");
            }
            catch (System.Exception e)
            {
                UniLog.Error($"ProximityGrab: could not resolve Grabbing menu option: {e}");
            }
        }
        var openMenu = MenuOptionsType == null ? null : AccessTools.Method(typeof(InteractionHandler), "OpenContextMenu", new[] { MenuOptionsType, typeof(float?) });
        var positionMenu = AccessTools.Method(typeof(InteractionHandler), "PositionContextMenu", new[] { typeof(ContextMenu) });
        if (openMenu == null || positionMenu == null || GrabbingOption < 0)
        {
            UniLog.Error("ProximityGrab: could not find context menu methods; gesture menu toggle disabled.");
            return;
        }
        harmony.Patch(openMenu, prefix: new HarmonyMethod(AccessTools.Method(typeof(MenuPatches), nameof(OpenContextMenuPrefix))));
        harmony.Patch(positionMenu, prefix: new HarmonyMethod(AccessTools.Method(typeof(MenuPatches), nameof(PositionContextMenuPrefix))));
    }

    private static void OpenContextMenuPrefix(InteractionHandler __instance, object options)
    {
        if (MenuOptionsType == null || options == null || !MenuOptionsType.IsInstanceOfType(options))
            return;
        LastMenuOptions.GetValue(__instance, _ => new MenuOptionsHolder()).Value = (int)options;
    }

    private static void PositionContextMenuPrefix(InteractionHandler __instance, ContextMenu menu)
    {
        try
        {
            if (!__instance.IsOwnedByLocalUser || menu == null)
                return;
            if (!LastMenuOptions.TryGetValue(__instance, out MenuOptionsHolder? holder) || holder.Value != GrabbingOption)
                return;
            ValueField<bool> field = GetGestureField(__instance);
            var onColor = colorX.Cyan;
            var offColor = colorX.Gray;
            menu.AddToggleItem(field.Value, (LocaleString)"Proximity Grab On", (LocaleString)"Proximity Grab Off", in onColor, in offColor);
        }
        catch (System.Exception e)
        {
            UniLog.Error($"ProximityGrab: failed to add gesture menu toggle: {e}");
        }
    }

    // Per-hand live mirror of the RML GestureMode key. Created once per handler;
    // menu taps write through to RML via OnGestureFieldChanged, and FistGesture.Update
    // mirrors RML back so config-UI edits appear within a frame.
    internal static ValueField<bool> GetGestureField(InteractionHandler handler)
    {
        return GestureFields.GetValue(handler, static h =>
        {
            Slot slot = h.Slot.FindChild("ProximityGestureMode") ?? h.Slot.AddSlot("ProximityGestureMode", persistent: false);
            ValueField<bool> field = slot.GetComponent<ValueField<bool>>() ?? slot.AttachComponent<ValueField<bool>>();
            field.Value.Value = ProximityGrabMod.GestureMode;
            field.Value.Changed += OnGestureFieldChanged;
            return field;
        });
    }

    private static void OnGestureFieldChanged(IChangeable changed)
    {
        try
        {
            bool value;
            if (changed is Sync<bool> sync)
                value = sync.Value;
            else if (changed is ValueField<bool> field)
                value = field.Value.Value;
            else
                return;
            if (value == ProximityGrabMod.GestureModeKey.Value)
                return;
            // Write through ModConfiguration so OnThisConfigurationChanged fires.
            // A direct key.Value write bypasses it, leaving the mirrored static stale
            // (and Update would stomp the field back on the next frame).
            if (ProximityGrabMod.Config != null)
                ProximityGrabMod.Config.Set(ProximityGrabMod.GestureModeKey, value);
            else
                ProximityGrabMod.GestureModeKey.Value = value;
            ProximityGrabMod.CopyFromConfig();
        }
        catch (System.Exception e)
        {
            UniLog.Error($"ProximityGrab: failed to mirror gesture mode change: {e}");
        }
    }
}

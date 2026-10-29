/*
SPDX-License-Identifier: LGPL-3.0-only
Copyright (C) 2026 Shawn Betts
*/

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;

namespace ProximityGrab;

// Diagnostic log of the hand and controller devices, for working out how to
// tell when the controllers are picked up. One line per frame whenever any
// on/off state (tracking, touch sensors) changes, and a periodic snapshot
// otherwise; gesture and grab events get their own lines (Event) so the
// timing of a pickup can be lined up against them.
internal static class InputSourceLog
{
    private const double SnapshotSeconds = 0.25;
    // Analog grip/trigger above this counts as a change-worthy "on".
    private const float AnalogOn = 0.1f;

    private static readonly List<Hand> Hands = new();
    private static readonly List<ControllerBase> Controllers = new();
    private static readonly StringBuilder Line = new();
    private static readonly StringBuilder Key = new();

    private static double _lastFrame = double.NaN;
    private static double _lastSnapshot = double.NegativeInfinity;
    private static string _lastKey = "";

    // Once per frame (both hands' handlers call it).
    public static void Update(InteractionHandler handler)
    {
        if (!ProximityGrabMod.InputSourceLog)
            return;
        double now = handler.Time.WorldTime;
        if (now == _lastFrame)
            return;
        _lastFrame = now;

        var input = handler.World.InputInterface;
        Hands.Clear();
        Controllers.Clear();
        input.GetDevices(Hands);
        input.GetDevices(Controllers);
        Line.Clear();
        Key.Clear();
        Line.Append($"vr {(input.VR_Active ? "Y" : "N")}");
        Key.Append(input.VR_Active ? 'V' : 'v');

        foreach (var hand in Hands)
        {
            string side = hand.Chirality == Chirality.Left ? "L" : "R";
            Line.Append($" | hand {side} '{hand.Name}' active {YN(hand.IsDeviceActive)} track {YN(hand.IsTracking)}"
                + $" conf {F(hand.Confidence)} meta {YN(hand.TracksMetacarpals)} prio {hand.Wrist.Priority}");
            Key.Append(side).Append(YN(hand.IsDeviceActive)).Append(YN(hand.IsTracking));
        }

        foreach (var c in Controllers)
        {
            string side = c.Side == Chirality.Left ? "L" : "R";
            Line.Append($" | ctrl {side} '{c.Name}' {c.DeviceModel} active {YN(c.IsDeviceActive)} track {YN(c.IsTracking)}");
            Key.Append(side).Append(YN(c.IsDeviceActive)).Append(YN(c.IsTracking));
            if (c is TouchController t)
            {
                Line.Append($" grip {F(t.Grip.Value)} trig {F(t.Trigger.Value)}"
                    + $" touch trig {YN(t.TriggerTouch.Held)} stick {YN(t.JoystickTouch.Held)}"
                    + $" rest {YN(t.ThumbRestTouch.Held)} xa {YN(t.ButtonXA_Touch.Held)} yb {YN(t.ButtonYB_Touch.Held)}");
                Key.Append(YN(t.Grip.Value > AnalogOn)).Append(YN(t.Trigger.Value > AnalogOn))
                    .Append(YN(t.TriggerTouch.Held)).Append(YN(t.JoystickTouch.Held))
                    .Append(YN(t.ThumbRestTouch.Held)).Append(YN(t.ButtonXA_Touch.Held)).Append(YN(t.ButtonYB_Touch.Held));
            }
            else
            {
                Line.Append($" strength {F(c.Strength.Value)} touch trig {YN(c.TouchingStrength.Held)} axis {YN(c.TouchingAxis.Held)}");
                Key.Append(YN(c.Strength.Value > AnalogOn)).Append(YN(c.TouchingStrength.Held)).Append(YN(c.TouchingAxis.Held));
            }
            // Distance to the tracked hand on the same side: near means the
            // hand skeleton is on (or built from) the controller.
            Hand? near = null;
            foreach (var hand in Hands)
                if (hand.IsTracking && hand.Chirality == c.Side && (near == null || hand.Wrist.Priority > near.Wrist.Priority))
                    near = hand;
            if (near != null && c.IsTracking)
                Line.Append($" to-wrist {MathX.Distance(c.Position, near.Wrist.Position) * 100f:F1}cm");
        }
        Hands.Clear();
        Controllers.Clear();

        string key = Key.ToString();
        bool changed = key != _lastKey;
        if (!changed && now - _lastSnapshot < SnapshotSeconds && now >= _lastSnapshot)
            return;
        _lastKey = key;
        _lastSnapshot = now;
        UniLog.Log($"ProximityGrab input {T(now)}{(changed ? " CHANGE" : "")}: {Line}");
    }

    public static void Event(InteractionHandler handler, string message)
    {
        if (!ProximityGrabMod.InputSourceLog)
            return;
        string side = handler.Side.Value == Chirality.Left ? "L" : "R";
        UniLog.Log($"ProximityGrab input {T(handler.Time.WorldTime)} EVENT {side}: {message}");
    }

    private static string YN(bool b) => b ? "Y" : "N";

    private static string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    private static string T(double t) => t.ToString("0.000", CultureInfo.InvariantCulture);
}

/*
SPDX-License-Identifier: LGPL-3.0-only
Copyright (C) 2026 Shawn Betts
Portions © 2025 XDelta (Resonite Mod Template ExampleMod)
*/

using Elements.Core;
using FrooxEngine;

namespace ProximityGrab;

// Triple-grab confirmation: a faint two-armed spiral of dots swirling on the
// ground under the stashed object. Like MissEffect, it lives on per-handler
// local (unsynced) slots built once and reused. Geometry is a unit-sized
// spiral; the root slot's scale sets the size, so nothing regenerates mid-animation.
internal sealed class StashEffect
{
    private const int Arms = 2;
    private const int DotsPerArm = 9;
    private const float Duration = 1.4f;
    private const float Turns = 1.5f;
    private const float PeakAlpha = 0.35f;

    private Slot _root = null!;
    private Slot _pivot = null!;
    private UnlitMaterial _material = null!;
    private double _start;
    private bool _running;

    private bool IsValid => _root != null && !_root.IsRemoved;

    public static void Trigger(InteractionHandler handler, ProximityGrabState state, Slot target)
    {
        if (state.StashEffect == null || !state.StashEffect.IsValid)
        {
            state.StashEffect?._root?.Destroy();
            state.StashEffect = Build(handler.World);
        }
        state.StashEffect.Start(handler, target);
    }

    public static void Update(InteractionHandler handler, ProximityGrabState state)
    {
        StashEffect? effect = state.StashEffect;
        if (effect == null || !effect.IsValid || !effect._running)
            return;
        effect.Tick(handler);
    }

    private static StashEffect Build(World world)
    {
        var e = new StashEffect();
        e._root = world.AddLocalSlot("ProximityGrab StashEffect");
        e._pivot = e._root.AddSlot("Pivot");
        var sphere = e._pivot.AttachMesh<SphereMesh, UnlitMaterial>();
        // Shared mesh and material; the dots are instances of one dot.
        sphere.mesh.Radius.Value = 1f;
        sphere.mesh.Segments.Value = 8;
        sphere.mesh.Rings.Value = 6;
        sphere.material.BlendMode.Value = BlendMode.Alpha;
        sphere.material.ZWrite.Value = ZWrite.Off;
        sphere.material.TintColor.Value = colorX.Cyan.SetA(0f);
        e._material = sphere.material;
        // The pivot's own renderer would draw a unit sphere at the center; the
        // dots below reuse its mesh and material instead.
        sphere.renderer.Enabled = false;
        for (int arm = 0; arm < Arms; arm++)
        {
            for (int i = 0; i < DotsPerArm; i++)
            {
                float t = (i + 1f) / DotsPerArm;
                float angle = (arm / (float)Arms + t * 0.75f) * MathX.PI * 2f;
                float r = t;
                Slot dot = e._pivot.AddSlot("Dot");
                dot.LocalPosition = new float3(MathX.Cos(angle) * r, 0f, MathX.Sin(angle) * r);
                dot.LocalScale = float3.One * (0.07f * (1.15f - t));
                var renderer = dot.AttachComponent<MeshRenderer>();
                renderer.Mesh.Target = sphere.mesh;
                renderer.Materials.Add(sphere.material);
            }
        }
        e._root.ActiveSelf = false;
        return e;
    }

    private void Start(InteractionHandler handler, Slot target)
    {
        BoundingBox box = target.ComputeBoundingBox();
        float3 center = target.GlobalPosition;
        float radius = 0.12f * (handler.LocalUserRoot?.GlobalScale ?? 1f);
        if (box.IsValid)
        {
            // Ground-plane footprint, centered under the object's lowest point.
            center = new float3(box.Center.x, (box.Center.y - box.Size.y * 0.5f), box.Center.z);
            radius = MathX.Max(radius, MathX.Max(box.Size.x, box.Size.z) * 0.5f + 0.04f);
        }
        _root.GlobalPosition = center + float3.Up * 0.005f;
        _root.GlobalRotation = floatQ.Identity;
        _root.GlobalScale = float3.One * radius;
        _start = handler.Time.WorldTime;
        _root.ActiveSelf = true;
        _running = true;
        Tick(handler);
    }

    private void Tick(InteractionHandler handler)
    {
        float t = (float)((handler.Time.WorldTime - _start) / Duration);
        if (t >= 1f)
        {
            _root.ActiveSelf = false;
            _running = false;
            return;
        }
        // Ease-out spin; alpha rises quickly and fades out.
        float spin = 1f - (1f - t) * (1f - t);
        _pivot.LocalRotation = floatQ.AxisAngle(float3.Up, spin * Turns * 360f);
        float envelope = MathX.Min(t * 6f, 1f) * (1f - t);
        _material.TintColor.Value = colorX.Cyan.SetA(PeakAlpha * envelope);
    }
}

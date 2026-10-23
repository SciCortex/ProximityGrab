/*
SPDX-License-Identifier: LGPL-3.0-only
Copyright (C) 2026 Shawn Betts
Portions © 2025 XDelta (Resonite Mod Template ExampleMod)
*/

using Elements.Core;
using FrooxEngine;
using Renderite.Shared;

namespace ProximityGrab;

// Two-hand tether: a dashed line between the hands' grab points while both
// hold the same object, built like the engine's InteractionLaser (overlay
// BentTubeMesh with the scrolling empty-laser texture). Lives on a
// per-handler local (unsynced) slot built on first use and reused. The tube
// UVs run along its length in slot units, so the slot carries the user's
// scale to keep the dash spacing in user space, like the laser.
internal sealed class TetherEffect
{
    // InteractionLaser's LaserInteractionTexturePreset.Empty values.
    private const float TextureScale = 0.25f;
    private const float TextureSpeed = -4f;
    private const float Radius = 0.002f;

    private Slot _root = null!;
    private BentTubeMesh _mesh = null!;
    private OverlayUnlitMaterial _material = null!;

    public bool IsValid => _root != null && !_root.IsRemoved;

    public static void Show(InteractionHandler handler, ProximityGrabState state, in float3 from, in float3 to)
    {
        if (state.Tether == null || !state.Tether.IsValid)
        {
            state.Tether?._root?.Destroy();
            state.Tether = Build(handler.World);
        }
        state.Tether.Place(handler, from, to);
    }

    public static void Hide(ProximityGrabState state)
    {
        if (state.Tether != null && state.Tether.IsValid && state.Tether._root.ActiveSelf)
            state.Tether._root.ActiveSelf = false;
    }

    private static TetherEffect Build(World world)
    {
        var e = new TetherEffect();
        e._root = world.AddLocalSlot("ProximityGrab Tether");
        var model = e._root.AttachMesh<BentTubeMesh, OverlayUnlitMaterial>();
        model.mesh.Radius.Value = Radius;
        model.mesh.Segments.Value = 3;
        model.mesh.StartPoint.Value = float3.Zero;
        model.material.BlendMode.Value = BlendMode.Additive;
        model.material.UseVertexColors.Value = true;
        model.material.FrontTintColor.Value = new colorX(1f);
        model.material.BehindTintColor.Value = new colorX(0.5f);
        // The local user's own laser queue.
        model.material.RenderQueue.Value = 4010;
        StaticTexture2D texture = e._root.AttachComponent<StaticTexture2D>();
        texture.FilterMode.Value = TextureFilterMode.Point;
        texture.URL.Value = OfficialAssets.Graphics.Laser.Empty;
        model.material.Texture = texture;
        model.material.TextureScale = new float2(1f, TextureScale);
        e._mesh = model.mesh;
        e._material = model.material;
        e._root.ActiveSelf = false;
        return e;
    }

    private void Place(InteractionHandler handler, float3 from, float3 to)
    {
        float scale = handler.LocalUserRoot?.GlobalScale ?? 1f;
        _root.GlobalPosition = from;
        _root.GlobalRotation = floatQ.Identity;
        _root.GlobalScale = float3.One * scale;
        float3 end = (to - from) / scale;
        _mesh.DirectTargetPoint.Value = end;
        _mesh.ActualTargetPoint.Value = end;
        float2 offset = _material.FrontTextureOffset.Value + new float2(0f, TextureSpeed * handler.Time.Delta);
        // The texture wraps every whole UV unit; keep the offset small.
        offset = new float2(0f, MathX.Repeat(offset.y, 1f));
        _material.FrontTextureOffset.Value = offset;
        _material.BehindTextureOffset.Value = offset;
        if (!_root.ActiveSelf)
            _root.ActiveSelf = true;
    }
}

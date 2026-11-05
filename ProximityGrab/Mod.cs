/*
SPDX-License-Identifier: LGPL-3.0-only
Copyright (C) 2026 Shawn Betts
Portions © 2025 XDelta (Resonite Mod Template ExampleMod)
*/

using HarmonyLib;
using ResoniteModLoader;

namespace ProximityGrab;

public class ProximityGrabMod : ResoniteMod
{
    internal const string VERSION_CONSTANT = "0.11.0";

    // RML configuration keys (persisted to rml_config/ProximityGrab.json,
    // editable via config-manager UIs or by editing the JSON while the game is stopped).
    // NOTE: keys must stay declared before the mirrored statics below: the statics
    // initialize from Key.Value, and C# initializes static fields in textual order.
    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> GestureModeKey =
        new("GestureMode", "Proximity Grabs Enabled", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> DisableWithControllersKey =
        new("DisableWithControllers", "Turn the grab gestures off while physical controllers are in use (a tracked controller that has reported a touch or grip; CyberFinger's buttons don't count)", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> FistGrabEnabledKey =
        new("FistGrabEnabled", "Fist gesture enabled", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> PrecisionGrabEnabledKey =
        new("PrecisionGrabEnabled", "Pinch grabs enabled (any finger)", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> DebugShowPinchKey =
        new("DebugShowPinch", "Show grip debug visuals", () => false);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> DebugPinchLinesKey =
        new("DebugPinchLines", "Show fingertip-to-midpoint debug lines", () => false);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<PinchMissEffectKind> PinchMissEffectKey =
        new("PinchMissEffect", "Missed pinch visual effect", () => PinchMissEffectKind.Sparks);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> StashGrabEnabledKey =
        new("StashGrabEnabled", "Grabbing twice in quick succession stashes the object on your avatar", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> StashGrabSecondsKey =
        new("StashGrabSeconds", "Max seconds between the two grabs of a stash grab", () => 0.4f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> TwoHandTetherKey =
        new("TwoHandTether", "Show a dashed line between hands holding the same object", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> GlitchMedianEnabledKey =
        new("GlitchMedianEnabled", "Median-filter gesture readings to ignore tracking flickers", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<int> GlitchMedianFramesKey =
        new("GlitchMedianFrames", "Frames in the gesture median filter (hides flickers under half this)", () => 5, valueValidator: IsMedianFrames);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> ReleaseGraceEnabledKey =
        new("ReleaseGraceEnabled", "Hold on through brief gesture dropouts before releasing", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> ReleaseGraceSecondsKey =
        new("ReleaseGraceSeconds", "Seconds a gesture must stay gone before the grab releases", () => 0.1f, valueValidator: IsNonNegativeFinite);

    internal static ModConfiguration? Config;

    // Gesture toggles (hot-path source of truth, mirrored from config).
    // Initialized from the keys so each default literal exists exactly once, above.
    internal static bool FistGrabEnabled = FistGrabEnabledKey.Value;
    internal static bool PrecisionGrabEnabled = PrecisionGrabEnabledKey.Value;
    // Which fingers pinch with the thumb (set in source only).
    internal static readonly bool IndexPinchEnabled = true;
    internal static readonly bool MiddlePinchEnabled = false;
    internal static readonly bool RingPinchEnabled = false;
    internal static bool DebugPinchLines = DebugPinchLinesKey.Value;

    // Debug visuals; the text overlay (world-space text above each wrist) is
    // set in source only.
    internal static bool DebugShowPinch = DebugShowPinchKey.Value;
    internal static readonly bool DebugText = false;

    // Missed-pinch feedback (brief local sphere at the pinch sweep)
    internal const float PinchMissFlashSeconds = 0.25f;
    internal static PinchMissEffectKind PinchMissEffect = PinchMissEffectKey.Value;

    // Gesture mode (global on/off for both hands, toggled from the HandGrab menu)
    internal static bool GestureMode = GestureModeKey.Value;
    internal static bool DisableWithControllers = DisableWithControllersKey.Value;

    // Fist gesture (average curl, degrees + per-finger minimum, degrees). Not
    // configurable.
    internal const float FistEngageDegrees = 150f;
    internal const float FistReleaseDegrees = 100f;
    internal const float FistMinEngageDegrees = 90f;
    internal const float FistMinReleaseDegrees = 70f;
    internal const float FistMinJointEngageDegrees = 30f;
    internal const float FistMinJointReleaseDegrees = 20f;

    // Pinch gesture (scale-normalized index <-> thumb tip distance, m). Not
    // configurable.
    internal const float PinchEngageDistance = 0.030f;
    internal const float PinchReleaseDistance = 0.050f;
    internal const float PinchMaxIndexCurlDegrees = 140f;

    // Stash grab (stash held object under the user root)
    internal static bool StashGrabEnabled = StashGrabEnabledKey.Value;
    internal static float StashGrabSeconds = StashGrabSecondsKey.Value;

    // Two-hand hold tether line
    internal static bool TwoHandTether = TwoHandTetherKey.Value;

    // Tracking-glitch tolerance (median filter on gesture readings, delayed release)
    internal static bool GlitchMedianEnabled = GlitchMedianEnabledKey.Value;
    internal static int GlitchMedianFrames = GlitchMedianFramesKey.Value;
    internal static bool ReleaseGraceEnabled = ReleaseGraceEnabledKey.Value;
    internal static float ReleaseGraceSeconds = ReleaseGraceSecondsKey.Value;

    // Diagnostic logging, set in source only: gesture flickers and release
    // delays (GlitchLog); hand/controller device states and grab events
    // (InputSourceLog).
    internal static readonly bool GlitchLog = true;
    internal static readonly bool InputSourceLog = false;

    // Precision grab sphere sweep (m). Not configurable.
    internal const float PrecisionMinRadius = 0.004f;
    internal const float PrecisionMaxRadius = 0.04f;
    internal const float PrecisionRadiusStep = 0.01f;

    public override string Name => "ProximityGrab";
    public override string Author => "Shawn Betts";
    public override string Version => VERSION_CONSTANT;
    public override string Link => "https://github.com/shawn-scicortex/ProximityGrab/";

    public override void OnEngineInit()
    {
        Msg("ProximityGrab loading...");
        Config = GetConfiguration();
        if (Config == null)
        {
            Warn("No ModConfiguration (no config keys registered); using built-in defaults.");
        }
        else
        {
            CopyFromConfig();
            Config.OnThisConfigurationChanged += _ => CopyFromConfig();
        }
        var harmony = new Harmony("YourName.ProximityGrab");
        Methods.HarmonyInstance = harmony;
        harmony.PatchAll();
        MenuPatches.Apply(harmony);
        Msg("ProximityGrab loaded. Fist = proximity grab, pinch = precision grab (fist wins) while hands are tracked.");
    }

    private static bool IsMedianFrames(int value) => value >= 1 && value <= MedianFilter.MaxFrames;

    private static bool IsNonNegativeFinite(float value) =>
        float.IsFinite(value) && value >= 0f;

    internal static void CopyFromConfig()
    {
        FistGrabEnabled = FistGrabEnabledKey.Value;
        PrecisionGrabEnabled = PrecisionGrabEnabledKey.Value;
        DebugPinchLines = DebugPinchLinesKey.Value;
        DebugShowPinch = DebugShowPinchKey.Value;
        PinchMissEffect = PinchMissEffectKey.Value;
        GestureMode = GestureModeKey.Value;
        DisableWithControllers = DisableWithControllersKey.Value;
        StashGrabEnabled = StashGrabEnabledKey.Value;
        StashGrabSeconds = StashGrabSecondsKey.Value;
        TwoHandTether = TwoHandTetherKey.Value;
        GlitchMedianEnabled = GlitchMedianEnabledKey.Value;
        GlitchMedianFrames = GlitchMedianFramesKey.Value;
        ReleaseGraceEnabled = ReleaseGraceEnabledKey.Value;
        ReleaseGraceSeconds = ReleaseGraceSecondsKey.Value;
    }
}

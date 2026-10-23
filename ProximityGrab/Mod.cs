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
    internal static readonly ModConfigurationKey<bool> FistGrabEnabledKey =
        new("FistGrabEnabled", "Fist gesture enabled", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> PrecisionGrabEnabledKey =
        new("PrecisionGrabEnabled", "Pinch grabs enabled (any finger)", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> IndexPinchEnabledKey =
        new("IndexPinch", "Index-thumb pinch triggers precision grab", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> MiddlePinchEnabledKey =
        new("MiddlePinch", "Middle-thumb pinch triggers precision grab", () => false);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> RingPinchEnabledKey =
        new("RingPinch", "Ring-thumb pinch triggers precision grab", () => false);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> DebugShowPinchKey =
        new("DebugShowPinch", "Show grip debug visuals", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> DebugTextKey =
        new("DebugText", "Show grip debug text overlay", () => false);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> DebugPinchLinesKey =
        new("DebugPinchLines", "Show fingertip-to-midpoint debug lines", () => false);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<PinchMissEffectKind> PinchMissEffectKey =
        new("PinchMissEffect", "Missed pinch visual effect", () => PinchMissEffectKind.Random);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> PinchMissFlashSecondsKey =
        new("PinchMissFlashSeconds", "Seconds the missed-pinch effect lasts", () => 0.25f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> FistEngageDegreesKey =
        new("FistEngageDegrees", "Avg finger curl degrees to engage fist grab", () => 150f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> FistReleaseDegreesKey =
        new("FistReleaseDegrees", "Avg finger curl degrees to release fist grab", () => 100f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> FistMinEngageDegreesKey =
        new("FistMinEngageDegrees", "Finger min curl degrees to engage fist grab", () => 90f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> FistMinReleaseDegreesKey =
        new("FistMinReleaseDegrees", "Finger min curl degrees to release fist grab", () => 70f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> FistMinJointEngageDegreesKey =
        new("FistMinJointEngageDegrees", "Joint min angle to engage fist grab", () => 30f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> FistMinJointReleaseDegreesKey =
        new("FistMinJointReleaseDegrees", "Joint min angle to release fist grab", () => 20f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> PinchEngageDistanceKey =
        new("PinchEngageDistance", "Index-thumb tip distance to engage pinch", () => 0.030f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> PinchReleaseDistanceKey =
        new("PinchReleaseDistance", "Index-thumb tip distance to release pinch", () => 0.050f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> PinchMaxIndexCurlDegreesKey =
        new("PinchMaxIndexCurlDegrees", "Pinching-finger curl above which pinch is rejected", () => 140f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> PrecisionMinRadiusKey =
        new("PrecisionMinRadius", "Pinch grab sphere sweep min radius", () => 0.004f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> PrecisionMaxRadiusKey =
        new("PrecisionMaxRadius", "Pinch grab sphere sweep max radius", () => 0.04f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> PrecisionRadiusStepKey =
        new("PrecisionRadiusStep", "Pinch grab sphere sweep radius step", () => 0.01f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> TripleGrabEnabledKey =
        new("TripleGrabEnabled", "Triple grab stashes the object on your avatar", () => true);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<int> TripleGrabCountKey =
        new("TripleGrabCount", "Grabs in a row that stash the object (2 or 3)", () => 2, valueValidator: IsTwoOrThree);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<float> TripleGrabSecondsKey =
        new("TripleGrabSeconds", "Max seconds between grabs of a triple grab", () => 0.4f, valueValidator: IsNonNegativeFinite);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> TwoHandTetherKey =
        new("TwoHandTether", "Show a dashed line between hands holding the same object", () => true);

    internal static ModConfiguration? Config;

    // Gesture toggles (hot-path source of truth, mirrored from config).
    // Initialized from the keys so each default literal exists exactly once, above.
    internal static bool FistGrabEnabled = FistGrabEnabledKey.Value;
    internal static bool PrecisionGrabEnabled = PrecisionGrabEnabledKey.Value;
    internal static bool IndexPinchEnabled = IndexPinchEnabledKey.Value;
    internal static bool MiddlePinchEnabled = MiddlePinchEnabledKey.Value;
    internal static bool RingPinchEnabled = RingPinchEnabledKey.Value;
    internal static bool DebugPinchLines = DebugPinchLinesKey.Value;

    // Debug overlay (world-space text above each wrist)
    internal static bool DebugShowPinch = DebugShowPinchKey.Value;
    internal static bool DebugText = DebugTextKey.Value;

    // Missed-pinch feedback (brief local sphere at the pinch sweep)
    internal static float PinchMissFlashSeconds = PinchMissFlashSecondsKey.Value;
    internal static PinchMissEffectKind PinchMissEffect = PinchMissEffectKey.Value;

    // Gesture mode (global on/off for both hands, toggled from the HandGrab menu)
    internal static bool GestureMode = GestureModeKey.Value;

    // Fist gesture (average curl, degrees + per-finger minimum, degrees)
    internal static float FistEngageDegrees = FistEngageDegreesKey.Value;
    internal static float FistReleaseDegrees = FistReleaseDegreesKey.Value;
    internal static float FistMinEngageDegrees = FistMinEngageDegreesKey.Value;
    internal static float FistMinReleaseDegrees = FistMinReleaseDegreesKey.Value;
    internal static float FistMinJointEngageDegrees = FistMinJointEngageDegreesKey.Value;
    internal static float FistMinJointReleaseDegrees = FistMinJointReleaseDegreesKey.Value;

    // Pinch gesture (scale-normalized index <-> thumb tip distance, m)
    internal static float PinchEngageDistance = PinchEngageDistanceKey.Value;
    internal static float PinchReleaseDistance = PinchReleaseDistanceKey.Value;
    internal static float PinchMaxIndexCurlDegrees = PinchMaxIndexCurlDegreesKey.Value;

    // Triple grab (stash held object under the user root)
    internal static bool TripleGrabEnabled = TripleGrabEnabledKey.Value;
    internal static int TripleGrabCount = TripleGrabCountKey.Value;
    internal static float TripleGrabSeconds = TripleGrabSecondsKey.Value;

    // Two-hand hold tether line
    internal static bool TwoHandTether = TwoHandTetherKey.Value;

    // Precision grab sphere sweep
    internal static float PrecisionMinRadius = PrecisionMinRadiusKey.Value;
    internal static float PrecisionMaxRadius = PrecisionMaxRadiusKey.Value;
    internal static float PrecisionRadiusStep = PrecisionRadiusStepKey.Value;

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

    private static bool IsTwoOrThree(int value) => value is 2 or 3;

    private static bool IsNonNegativeFinite(float value) =>
        float.IsFinite(value) && value >= 0f;

    internal static void CopyFromConfig()
    {
        FistGrabEnabled = FistGrabEnabledKey.Value;
        PrecisionGrabEnabled = PrecisionGrabEnabledKey.Value;
        IndexPinchEnabled = IndexPinchEnabledKey.Value;
        MiddlePinchEnabled = MiddlePinchEnabledKey.Value;
        RingPinchEnabled = RingPinchEnabledKey.Value;
        DebugPinchLines = DebugPinchLinesKey.Value;
        DebugShowPinch = DebugShowPinchKey.Value;
        DebugText = DebugTextKey.Value;
        PinchMissFlashSeconds = PinchMissFlashSecondsKey.Value;
        PinchMissEffect = PinchMissEffectKey.Value;
        GestureMode = GestureModeKey.Value;
        FistEngageDegrees = FistEngageDegreesKey.Value;
        FistReleaseDegrees = FistReleaseDegreesKey.Value;
        FistMinEngageDegrees = FistMinEngageDegreesKey.Value;
        FistMinReleaseDegrees = FistMinReleaseDegreesKey.Value;
        FistMinJointEngageDegrees = FistMinJointEngageDegreesKey.Value;
        FistMinJointReleaseDegrees = FistMinJointReleaseDegreesKey.Value;
        PinchEngageDistance = PinchEngageDistanceKey.Value;
        PinchReleaseDistance = PinchReleaseDistanceKey.Value;
        PinchMaxIndexCurlDegrees = PinchMaxIndexCurlDegreesKey.Value;
        TripleGrabEnabled = TripleGrabEnabledKey.Value;
        TripleGrabCount = TripleGrabCountKey.Value;
        TripleGrabSeconds = TripleGrabSecondsKey.Value;
        TwoHandTether = TwoHandTetherKey.Value;
        PrecisionMinRadius = PrecisionMinRadiusKey.Value;
        PrecisionMaxRadius = PrecisionMaxRadiusKey.Value;
        PrecisionRadiusStep = Math.Max(PrecisionRadiusStepKey.Value, 0.0005f);
        if (PinchEngageDistance >= PinchReleaseDistance)
            PinchEngageDistance = PinchReleaseDistance * 0.5f;
        if (PrecisionMinRadius >= PrecisionMaxRadius)
            PrecisionMinRadius = PrecisionMaxRadius * 0.5f;
    }
}

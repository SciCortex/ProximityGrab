# Could ProximityGrab be done in ProtoFlux?

Notes from looking into whether a ProtoFlux module on an avatar could stand in for the mod.
Node names come from the installed `ProtoFlux.Nodes.FrooxEngine.dll`; their inputs and outputs
haven't been checked.

## Short answer

A ProtoFlux pinch grab could be a nice add-on, and could be shared with people who don't run
mods. It can't replace ProximityGrab, because flux can't take away the stock grab.

## What flux could do

- **Pinch detection.** `BodyNodeSlot` and the finger-pose nodes (`FingerPose`, `FingerNode`, ...)
  give the thumb-tip and index-tip slots. Global position → distance → engage/release thresholds
  gives a pinch with hysteresis, like `PinchEngageDistance` / `PinchReleaseDistance` in `Mod.cs`.
  A fist is harder (curl angles from bone rotations) but doable.
- **Finding the target.** There are `Raycast` / `RaycastOne` / `RaycastAll` nodes. No sphere-cast
  or overlap query showed up, so the mod's growing sphere sweep (`PrecisionGrab.cs`) would become
  a short ray, or a few rays, from the pinch point.
- **Grabbing.** There are `Grab`, `Grabber`, `GrabbableGrabbed`, `IsGrabbed` and
  `ReleaseAllGrabbed` nodes.
  - If `Grab` takes the hand's Grabber and a target, a flux grab can be a real grab.
  - If not, the fallback is `SetParent` to the hand. That's a fake grab: no Grabbable events, and
    it skips the grab permission checks (see below).
- **Extras.** The stash (`SetParent` onto an avatar slot), miss sparks and the two-hand tether
  line would all be straightforward.

## What flux can't do

- **Change the stock grab.** The core of the mod is changing how Resonite's own grab works:
  - grabs need a deliberate gesture;
  - they stay near the hand instead of following the laser;
  - fist and pinch are separate grab sources.

  That logic lives in `InteractionHandler` and flux can't patch it. A flux pinch grab would add
  another way to grab, and the built-in grab would keep firing.
- **Detect controllers.** `ControllerGuard` reads the input devices directly, and flux can't see
  those.
- **Work everywhere.** Flux on an avatar only runs in worlds where you're wearing that avatar. It
  doesn't run in userspace (the dash), and a world's permissions can block it. The mod works in
  every world.

## Putting flux on an avatar

1. Build the flux under a slot in some world, e.g. a slot called `PinchGrab`.
2. Parent that slot under the avatar's root.
3. Save the avatar. Everything under the root is saved with it.

Avatar flux runs on every client in the session, so gate the logic on "the local user is the
avatar's owner". Otherwise every client tries to grab with your hands.

## Aside: world permissions in the mod

The mod's grab follows the same permission rules as a stock grab:

- `PrecisionGrab.cs` calls the engine's `Grabber.Grab(colliders, filter, ...)` with
  `InteractionHandler.FilterGrabbable`, the filter a stock grab uses.
- Inside `Grabber.Grab`, the engine also drops anything that fails `CanGrab`, which is where grab
  permissions and grab blocks are enforced.

So anything a world doesn't let you grab, a pinch or fist won't grab either.

The mod moves objects itself in two places, both after a legitimate grab:

- **Two-hand handoff** (`TwoHandGrab.HandOff`): passes an object you're already holding from one
  of your hands to the other. There's nothing new to permit.
- **Stash** (`StashGrab.Stash`): the one gray area. A stock release returns an object to where it
  came from. A stash changes that destination to your user root, so the object travels with you.
  - It only acts on objects you were allowed to grab, and only ones whose `ReparentOnRelease` is on.
  - Nothing checks whether the world wants that object carried off, e.g. a game piece.
  - A possible follow-up: skip objects that look world-owned or locked, such as ones with grab
    permissions configured.

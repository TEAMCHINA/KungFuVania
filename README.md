# Kung Fu Vania

2D side-scrolling metroidvania built in Unity (2D URP), with hand-drawn cartoony sprites in a
1970s wuxia kung-fu-movie aesthetic.

The full architecture plan — state machines, combat system, stats, abilities, save system,
boss system, UI, and build order — lives in [`GAME_PLAN.md`](./GAME_PLAN.md). That document is
the source of truth for every system's architecture and the feature checklist; this README covers
project setup plus day-to-day process documentation — how to actually *do* recurring authoring
tasks in the Editor.

## Opening the project

- Unity Editor **6000.5.1f1** (via Unity Hub)
- 2D (URP) render pipeline
- Open this repository's root folder as the Unity project

## AI assistant setup (optional)

This repo includes the **MCP for Unity** package (`com.coplaydev.unity-mcp`, already listed in
`Packages/manifest.json`), which lets an AI coding assistant (e.g. Claude Code) drive the Unity
Editor directly — read console output, inspect the scene, run tests, edit scripts, etc. It's a
nice-to-have for AI-assisted development, not a build/run dependency — skip this section if you
don't use an AI assistant.

The registration is per-machine and per-user (stored in your local Claude config / home
directory), so it needs to be redone after cloning onto a new machine. It is not checked into
this repo.

1. Open this project in Unity, then open **Window → MCP for Unity → Toggle MCP Window**
   (`Ctrl+Shift+M` / `Cmd+Shift+M`). Unity auto-starts the local MCP bridge server (shown in the
   window, e.g. `http://127.0.0.1:8080`).
2. In the window's client list, select **Claude Code** and click **Configure**. This runs
   `claude mcp add` under the hood to register the `UnityMCP` server for the current project.
3. Click **Install Skills** to sync the `unity-mcp-skill` skill (usage guide for the MCP tools)
   into `~/.claude/skills/`.
4. If you have a Claude Code session already open, run `/mcp` in it to reconnect and pick up the
   new registration (a fresh session picks it up automatically).

## Development Workflows

How-to guides for recurring authoring tasks, written up as they get worked out — not architecture
(that's `GAME_PLAN.md`), just the steps.

### Keyframing an Attack's Hitbox

Hitbox reach (position, rotation, size, offset) is authored per-attack directly in the Animation
window, not in code — see `GAME_PLAN.md` §3n ("Animation → Combat Decoupling") for why. To set or
change one:

1. Select the actor (e.g. `Player`) in the Hierarchy, open `Window > Animation > Animation`
   (Ctrl+6), and pick the attack's clip from the dropdown. The window stays bound to that
   Animator's context as long as you keep selecting objects inside that same hierarchy — if it
   ever looks wrong, reselect the actor and re-pick the clip.
2. Click the `Hitbox` child in the Hierarchy — its animatable properties (Position, Rotation, and
   under Box Collider 2D: Size, Offset) now show as tracks in the window.
3. Turn on **Record** (red circle, top-left). Nothing changed in the Inspector becomes a keyframe
   without it.
4. Scrub the playhead onto the frame where the reach should be set (for a held-pose attack, any
   frame in the active window works, since the value should stay constant — see step 6).
5. Set the geometry:
   - **Position** — type X/Y into the Transform's Position fields, or drag the object in Scene view.
   - **Rotation** (for a non-axis-aligned reach) — type a Z value into Rotation, or press `E` and
     drag the rotate ring in Scene view; the live degree readout makes it easy to eyeball against
     the rendered sprite.
   - **Size/Offset** — type into Box Collider 2D's fields, or click the small square-handle icon at
     the top of that component (**Edit Collider**) and drag the green handles directly over the
     sprite in Scene view.
6. **Keyframe both ends, not just one.** A single keyframe technically already holds its value for
   the whole clip (Unity's curves default to Clamp — hold-forever — outside their first/last key),
   but a second identical keyframe at the clip's end is what stops a *later*, unrelated edit on that
   same track from silently turning "constant" into an interpolated ramp — nothing can drift toward
   a value that's already pinned at both ends. Scrub to the other existing keyframe and retype the
   same numbers with Record still on — simplest and most reliable way to keep the two in sync.
7. Turn Record **off**, then scrub across the clip and watch the Scene view — the collider should
   track the sprite at every frame, with no risk of creating stray keyframes while just looking.
8. `OnHitboxActive` / `OnHitboxInactive` (parameterless) mark when the collider is actually
   enabled, on their own thin row near the bottom of the timeline. Every existing attack clip
   already has a working pair (e.g. `ATTACK_1.anim` at 0.05s/0.15s) — cheapest way to get this
   right on a new clip is to open one of those as a reference for where they sit relative to the
   reach curves, rather than guessing at placement from scratch.

**If a property shows yellow "Missing!"** despite the path clearly being correct: this is a stale
Animation-window cache, not broken data — confirmed by asking Unity's own
`AnimationUtility.GetAnimatedObject` to resolve every binding directly against the live GameObject,
which succeeds even when the window insists otherwise. Close and reopen the Animation window tab
(reselecting the GameObject alone isn't enough); if that doesn't clear it, right-click the clip
asset in the Project window → **Reimport**.

**No live art to look at?** Sprite import settings (`spritePivot`, `spritePixelsToUnits`, and the
sprite's pixel `rect`, all in the `.png.meta` file) convert a pixel position in the source art to a
local-space offset: `local = (pixel − pivotPixel) / spritePixelsToUnits`, with Y measured up from
the bottom of the rect. Useful for placing a hitbox from a written spec, but eyeballing it live
against the Scene view is simpler whenever the art's actually in front of you.

## Status

Currently bootstrapping. See `GAME_PLAN.md` Section 12 ("Build Order") for the implementation
sequence — `EventBus.cs` is first and lives at `Assets/_Project/Scripts/Core/EventBus.cs`.

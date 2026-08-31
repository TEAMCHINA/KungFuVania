# Kung Fu Vania

2D side-scrolling metroidvania built in Unity (2D URP), with hand-drawn cartoony sprites in a
1970s wuxia kung-fu-movie aesthetic.

The full architecture plan — state machines, combat system, stats, abilities, save system,
boss system, UI, and build order — lives in [`GAME_PLAN.md`](./GAME_PLAN.md). That document is
the source of truth for every system's design; this README only covers project setup.

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

## Status

Currently bootstrapping. See `GAME_PLAN.md` Section 12 ("Build Order") for the implementation
sequence — `EventBus.cs` is first and lives at `Assets/_Project/Scripts/Core/EventBus.cs`.

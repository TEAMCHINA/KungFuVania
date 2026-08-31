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

## Status

Currently bootstrapping. See `GAME_PLAN.md` Section 12 ("Build Order") for the implementation
sequence — `EventBus.cs` is first and lives at `Assets/_Project/Scripts/Core/EventBus.cs`.

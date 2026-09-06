# Kung Fu Vania — Full Architecture Plan

---

## Engine

**Unity** with the **2D URP (Universal Render Pipeline)** template.

Unreal's 2D support (Paper2D) is underdeveloped and largely unmaintained. Unity is the proven
choice for 2D metroidvanias (Hollow Knight, Dead Cells, Ori) and its tooling aligns directly
with the needs of this project: hand-drawn sprite rigging, Cinemachine for boss cinematics,
Timeline for cutscene sequencing, and a mature 2D physics pipeline for hitbox/hurtbox work.

---

## 1. Project Structure

```
Assets/
├── _Project/
│   ├── Scripts/
│   │   ├── Core/           GameManager, EventBus, SceneLoader, ServiceLocator, CameraManager,
│   │   │                   DialogueManager, ShopManager
│   │   ├── Player/         PlayerController, PlayerStateMachine, PlayerCombat, PlayerAbilities, PlayerStats,
│   │   │                   AnimatorSpeedSync, CameraTarget
│   │   ├── Combat/         HitboxController, HurtboxController, ComboSystem, ParrySystem,
│   │   │                   DodgeSystem, DamageCalculator, StaggerMeter, InputBuffer,
│   │   │                   MotionInputDetector
│   │   ├── Projectiles/    ProjectileController, ProjectileManager, ProjectileHitBehaviorSO,
│   │   │                   ProjectileMultiSpawnSO
│   │   ├── Auras/          AuraManager, AuraVisualController, GameTickManager, ActiveAura
│   │   ├── Stats/          StatSheet, StatRegistry
│   │   ├── Abilities/      TriggerSO, EffectSO, TriggerEffectSO, MotionInputTriggerSO,
│   │   │                   AbilityEffectSO, AbilityExecutionContext, AbilityModifierSO,
│   │   │                   NumericModifierSO, EquipmentManager
│   │   ├── MovementAbilities/ DoubleJumpAbility, AirDashAbility, GrappleAbility
│   │   ├── Cosmetics/      CharacterCustomizationController, CosmeticRegistry
│   │   ├── NPCs/           NPCController
│   │   ├── Enemies/        EnemyAIBase, EnemyStateMachine, Bosses/, Variants/
│   │   ├── World/          RoomManager, RoomTransition, WorldStateManager, AbilityGate, GrapplePoint
│   │   ├── Cinematics/     CinematicDirector, LetterboxController
│   │   ├── Input/          InputReader (ScriptableObject)
│   │   ├── Audio/          AudioManager, MusicController, SFXCatalog
│   │   └── UI/             HUDController, HealthBar, StaminaBar, BossHealthBar,
│   │                       MinimapController, AbilityIcons, ComboDisplay
│   ├── Data/
│   │   └── ScriptableObjects/
│   │       ├── Abilities/
│   │       ├── AbilityModifiers/
│   │       ├── MotionInputs/       TriggerEffectSO/MotionInputTriggerSO assets, StickInputConfigSO
│   │       ├── Auras/
│   │       ├── Cosmetics/      CosmeticSlotSO assets, CosmeticOptionSO assets, CosmeticRegistry
│   │       ├── Enemies/
│   │       ├── Combos/
│   │       ├── Rooms/
│   │       ├── Stats/              StatConfigSO (dexterity coefficients, level scaling curve, etc.)
│   │       ├── Camera/             CameraConfigSO
│   │       ├── Dialogue/           NPCDataSO assets, DialogueSO assets
│   │       ├── Shop/               ShopInventorySO assets
│   │       └── ParrySettings/
│   └── Art/, Audio/, Prefabs/, Animations/
```

### Conventions
- All game scripts live under `_Project/` to sort above Unity built-ins.
- Every major system has a corresponding ScriptableObject data layer.
- Scenes split into persistent (Core) and additive (rooms).
- Prefabs are kept separate from art so programmers can iterate without touching sprite atlases.

---

## 2. Core State Machines

### Top-Level Game States

```
BOOT → MAIN_MENU → LOADING → GAMEPLAY
                              ↕
                           PAUSED
                           CINEMATIC → BOSS_INTRO / BOSS_KILL
                           GAME_OVER
```

Transitions driven by EventBus events, not direct method calls. States are C# classes
implementing `IGameState` (Enter / Update / Exit interface).

---

### Player: Two Concurrent Layers

The player runs two parallel state machines — Locomotion and Combat. This allows attacks while
moving or jumping without combinatorial state explosion.

#### Locomotion Layer

```
IDLE → WALK → RUN → JUMP → FALL → WALL_SLIDE → CROUCH → BLOCKING → HURT
                                                                   → DOWN → DOWN_RECOVERY
                                                                   → PLAYER_DEATH  (terminal — no exit except respawn)
```

`PLAYER_DEATH` is entered when `StatSheet.CurrentHealth` reaches 0. It is the only locomotion
state that cannot be interrupted by any input. Exit only occurs via `RespawnManager` after the
full cinematic death sequence completes.

`WALL_SLIDE` is entered from `FALL` when the player is airborne, moving into a wall (contact
against a `groundLayer`-tagged collider on the player's facing side), and already descending
(`Controller.GetVelocity().y <= 0`). Descent is clamped to a slower speed and a slide animation
plays. Exits back to `FALL` if the player lets go of the wall direction or the wall ends; exits
to `IDLE`/`WALK`/`RUN` on landing, same as `FALL`. See 3k for `WALL_JUMP` — jumping while in
`WALL_SLIDE` launches the player up and away from the wall using its own charge pool, independent
of double/triple jump charges (see Sequence Breaks note in 3k).

`BLOCKING` is a held locomotion state entered when the defensive button is held and no parry
triggered on the initial press (see Combat System). Player can move slowly while blocking
(configurable). Releasing exits back to the appropriate locomotion state.

`DOWN` is entered when a knockdown attack lands (see 3a `onHitTargetState` and 3m).
`DOWN_RECOVERY` is the get-up animation; input during DOWN can trigger an early tech roll
that skips to a brief neutral recovery stance instead.

#### Combat Layer

```
NONE
ATTACK_1 / ATTACK_2 / ATTACK_3 / AERIAL_ATTACK
PARRY_STARTUP → PARRY_ACTIVE         → PARRY_RECOVERY
              ↘ PERFECT_PARRY
              ↘ PERFECT_PARRY_COUNTER (auto-fires when hasPerfectParryCounter = true)
DODGE → PERFECT_DODGE (if timed within perfectDodgeWindowBefore of impactFrame)
      → DODGE_RECOVERY
STAGGER
```

Each combat state is authored as a ScriptableObject containing:
- Startup / active / recovery frame counts
- Cancellable frame (the point at which combo input buffer and defensive cancels are checked)
- Hitbox reference
- Stamina cost
- `isDefensiveCancellable` flag
- `isComboBufferCancellable` flag

---

### Enemy State Machine

```
PATROL → DETECT → CHASE → ATTACK sub-states
                         → ATTACK_RECOVERY (after every attack, before next action)
                         → STAGGERED (stagger bar filled)
                         → DOWN → DOWN_RECOVERY (knockdown attacks)
                         → GUARD / GUARD_BREAK  (canBlock enemies only)
                         → SEARCH (LOS lost during CHASE)
                         → DEATH
SEARCH → PATROL (search timer expires) | CHASE (player re-detected)
```

`ATTACK_RECOVERY` is a mandatory post-attack state. The enemy cannot block, dodge, or chain
into another attack until it completes. Duration is defined per attack in `AttackPatternSO`
via `attackRecoveryFrames`. This prevents enemies from attacking and immediately retreating
into a guard, and is what makes parry counters always land cleanly.

#### AttackPatternSO — Key Fields

```csharp
HitboxDataSO hitboxData              // damage, parry windows, stagger values
AnimationClip attackAnimation
int     attackRecoveryFrames         // mandatory cooldown after the attack completes
bool    superArmor                   // default false — taking damage does not interrupt;
                                     // full damage applies, stagger builds normally;
                                     // stagger bar filling is the ONLY interrupt — see 3d
float   usageWeight                  // relative probability in weighted random selection
float   lastUsedCooldown             // seconds before this attack can be selected again
```

#### EnemyDataSO

```csharp
// Identity
string  enemyId
string  displayName

// Stats (feed into StatSheet — WeaponDamage and Armor are native here, not from equipment)
int     strength
int     chi
int     dexterity
int     constitution
int     level
int     weaponDamage
int     armor

// Stagger
float   maxStagger
float   staggerDecayRate
float   staggeredDuration

// Combat capability
bool    canBlock            // default false — most standard enemies have no guard state
float   guardChance         // canBlock enemies only — probability of raising guard on hit
float   guardDuration       // max time guard is held before lowering (even if not attacked)

// Attack behavior
List<AttackPatternSO> attackPool        // available attacks for this enemy
float   mistakeChance                   // 0.0–1.0; how often attack conditions are ignored
                                        // bosses: ~0.0, elites: ~0.05, grunts: ~0.15–0.25
bool    useOrderedSequence              // if true, attackPool plays in authored order (index wraps)
                                        // for scripted encounters; mistake roll picks randomly instead
float   aiTickInterval                  // seconds between AI decisions (e.g. 0.2s)
                                        // physics contacts still resolve every FixedUpdate

// Spacing
CombatProfile combatProfile

// Detection
float     alarmRadius                   // always-on close-range radius — no cone/LOS check
float     visionRange                   // max distance of the vision cone
float     visionAngle                   // cone half-angle in degrees (e.g. 60° = 120° total arc)
float     detectReactionTime            // DETECT state duration before entering CHASE (e.g. 0.4s)
LayerMask visionBlockers                // layers that block LOS (typically: Environment)

// Patrol / Search
float   searchDuration                  // seconds to scan at last known position before deaggro

// Permanence
bool    isUnique                // default false — if true, death is written to worldFlags
                                // (permanent kill, survives respawn). Set on bosses and named elites.
                                // Regular enemies (isUnique = false) are recorded in RoomState.deadEnemies
                                // which WorldStateManager.ResetEnemies() clears on player respawn.

// Loot
LootTableSO lootTable   // this enemy's drop table; null = use GameConfigSO.globalLootTable fallback
int         goldMin     // minimum gold dropped on death (0 = no gold drop)
int         goldMax     // maximum gold dropped on death

// Progression
int  xpReward           // XP awarded to the player on this enemy's death
                        // bosses have a high value set directly here — no separate boss-XP system
```

Only shield enemies, elites, and bosses set `canBlock = true`. Enemies where `canBlock = false`
never enter `GUARD` or `GUARD_BREAK` — those states are simply absent from their state machine.

#### Boss (extends standard)

```
PHASE_TRANSITION
PHASE_N_IDLE / PHASE_N_ATTACK_SET
ENRAGE
CINEMATIC_KILL
```

---

#### Enemy AI Behavior

##### Detection

Detection is evaluated on each AI tick while in `PATROL` or `SEARCH`:

```
float detMult         = player.StatSheet.DetectionRangeMultiplier   // gear modifier, default 1.0
float effectiveAlarm  = alarmRadius  * detMult
float effectiveVision = visionRange  * detMult

1. if distance(player) <= effectiveAlarm
     → DETECT immediately (no cone or LOS check — player is right there)
2. elif player within effectiveVision AND within visionAngle of enemy forward:
     Physics2D.Raycast(enemyEyes → player, distance, visionBlockers)
     → clear  → DETECT
     → blocked → no detection this tick
```

`visionAngle` is not scaled — stealth gear makes the player harder to spot at range, not
easier to flank with.

`DETECT` is a brief reaction state (`detectReactionTime` on `EnemyDataSO`, e.g. 0.4s) where
the enemy plays a "!" animation before entering `CHASE`, telegraphing aggression to the player.

##### PATROL

`EnemyAIBase` holds scene-specific patrol data (not in `EnemyDataSO` — positions are
instance-specific):

```csharp
List<Transform> patrolWaypoints    // ordered; enemy walks between them sequentially, wrapping
float           patrolWaitTime     // seconds to idle at each waypoint before moving to next
```

Empty `patrolWaypoints` → stationary guard (idles in place indefinitely).

##### SEARCH

Entered when the enemy loses LOS during `CHASE` (player exits cone AND alarm radius):

```
1. Pathfind to last known player position
2. Play scan animation; wait searchDuration seconds
3. Player re-detected → CHASE
4. searchDuration expires → resume PATROL from nearest waypoint
```

##### CHASE and Spacing

`EnemyDataSO` carries a `CombatProfile` struct:

```csharp
struct CombatProfile {
    float preferredRange           // desired player distance (melee ~2m, ranged ~8m)
    float approachTolerance        // repositions if outside preferredRange ± this value
    bool  retreatAfterAttack       // backs away after every attack
    float retreatDistance          // distance to create post-attack (if retreatAfterAttack = true)
    float retreatSpeedMultiplier   // speed scalar while retreating (default 0.5)
                                   // makes ranged enemies feel sluggish rather than annoyingly nimble
    float stumbleChance            // probability per retreat tick of stumbling (default 0)
                                   // plays stumble animation + briefly halts movement
                                   // use on nervous or physically unsteady enemy types
    bool  paceWhileWaiting         // moves laterally at preferred range while waiting to attack
    float paceSpeed                // lateral speed while pacing
}
```

CHASE logic each AI tick:

```
dist = distance(player)
if dist > preferredRange + approachTolerance  → pathfind toward player (full speed)
elif dist < preferredRange - approachTolerance → pathfind away from player
                                                  (retreatSpeedMultiplier applied; stumble check)
else (in acceptable band)
    if paceWhileWaiting → move laterally (reverse direction at random intervals)
    → evaluate attack selection
```

##### Attack Selection

On each decision tick while in `CHASE` or after `ATTACK_RECOVERY`:

New fields added to `AttackPatternSO`:

```csharp
float minRange                              // 0 = no min; attack only valid when player >= this far
float maxRange                              // 0 = no max; attack only valid when player <= this far
TargetStateRequirement requiredPlayerState  // None = any; reuses enum from HitboxDataSO
```

Selection algorithm:

```
bool mistake = Random.value < mistakeChance

if useOrderedSequence && !mistake:
    selected = attackPool[sequenceIndex % attackPool.Count]; sequenceIndex++
elif mistake:
    selected = WeightedRandom(attackPool, respectCooldowns: true)
else:
    candidates = attackPool where: IsInRange(dist) && requiredPlayerState.Satisfies(playerState)
                                   && lastUsedCooldown elapsed
    if candidates empty → skip this tick, retry next
    selected = WeightedRandom(candidates)

→ enter ATTACK state with selected AttackPatternSO
```

`useOrderedSequence = true` on scripted encounters — attacks play in authored pool order.
A mistake roll during a scripted sequence picks randomly instead of next-in-order (rare
interruption that keeps scripted fights from feeling mechanical on repeat).

##### GUARD Behavior (canBlock enemies only)

Guard is raised reactively when a hit lands during `CHASE` or `ATTACK_RECOVERY`:

```
on hit received: if Random.value < guardChance → enter GUARD
GUARD held until: guardDuration expires OR isUnblockable hit lands (GUARD_BREAK)
```

##### AI Decision Tick

All AI decisions (detection, repositioning, attack selection) run on `aiTickInterval`, not
every frame. Physics contacts resolve every `FixedUpdate` independently.

Random jitter (`±0.05s` per enemy) prevents synchronized group decision waves.

---

## 3. Combat System

### 3a. HitboxDataSO — Full Field List

```csharp
// Damage formula (Option 2 — multi-stat weighted)
// FinalDamage = ((Strength * strengthWeight) + (Chi * chiWeight) + (WeaponDamage * weaponWeight))
//               * baseMultiplier * levelScale * contextModifiers
float strengthWeight                  // physical contribution (e.g. punch = 0.9, chi blast = 0.1)
float chiWeight                       // chi/energy contribution (e.g. punch = 0.0, chi blast = 1.2)
float weaponWeight                    // weapon contribution (e.g. sword strike = 0.8, kick = 0.0)
float baseMultiplier                  // per-attack scalar (e.g. light = 0.8, flying kick = 1.4, finisher = 2.2)
float armorPenetration                // 0.0–1.0, default 0 — fraction of target Armor ignored
                                      // e.g. 0.5 = ignores half of target's armor

float blockDamagePercent              // default 0.5 — damage dealt through block
bool  isUnblockable                   // bypasses block entirely, full damage, guard break
bool  hitsFriendlies                  // default false — attack can damage same-team entities
                                      // EnemyHitbox ↔ EnemyHurtbox is always enabled in the physics
                                      // layer matrix; HurtboxController drops same-team contacts
                                      // silently when false. Set true on AOE/sweep attacks.
bool  piercesDodgeIFrames             // default false — i-frames don't protect; dodge movement still occurs
                                      // also prevents perfect dodge from triggering

// Dodge window (relative to impactFrame, only evaluated if !piercesDodgeIFrames)
int   perfectDodgeWindowBefore        // frames before impactFrame where a dodge counts as perfect

// Stagger
float staggerDamage                   // stagger buildup on a normal hit
float parryStaggerDamage              // stagger dealt when this attack is parried
float perfectParryStaggerMultiplier   // multiplier on parryStaggerDamage for perfect parry (e.g. 2.0)

// Parry windows (all relative to impactFrame)
// Window layout: [——standard——|——perfect——|IMPACT|——standard——]
int   impactFrame                     // frame the hitbox activates / damage is dealt
int   standardParryWindowBefore       // outer early window — standard parry (e.g. 7 frames)
int   perfectParryWindowBefore        // inner window closest to impact — perfect parry (e.g. 5 frames)
int   standardParryWindowAfter        // reaction window after impact — standard parry (e.g. 4 frames)

// Perfect parry counter (optional, configurable per attack)
bool          hasPerfectParryCounter          // default false — jabs just parry, haymakers counter
float         perfectParryCounterDamage       // damage dealt by the automatic counter hit
AnimationClip perfectParryCounterAnimation    // e.g. the 2-inch punch clip
bool          perfectParryCounterBulletTime   // default false — triggers slow-mo zoom on counter
float         perfectParryCounterTimeScale    // e.g. 0.2 (only used if bulletTime = true)
float         perfectParryCounterZoomAmount   // Cinemachine FOV delta (only used if bulletTime = true)

// Parry attacker auras — applied to the ATTACKER when this attack is successfully parried
// Both default to empty; most attacks leave these unset
List<AuraSO>  onParryAttackerAuras            // applied on standard parry (e.g. brief stagger aura)
List<AuraSO>  onPerfectParryAttackerAuras     // applied on perfect parry (typically a superset)

// Parry defender auras — applied to the DEFENDER (parryer) when this attack is successfully parried
// Both default to empty; most attacks leave these unset
// Universal parry rewards (e.g. 3s damage boost on any parry) live on ParrySettingsSO instead
List<AuraSO>  onParryDefenderAuras            // applied on standard parry
List<AuraSO>  onPerfectParryDefenderAuras     // applied on perfect parry

// Target state precondition — checked before damage resolution; whiffs if not satisfied
TargetStateRequirement targetRequirement  // default None — no restriction
                                          // see enum below; use flags for multi-state attacks

// On-hit result — determines which hurt state the target enters
OnHitTargetState onHitTargetState        // default Hurt — see enum below

// Feel
float hitstunDuration
Vector2 knockbackVector
```

**`TargetStateRequirement`** — C# flags enum, checked against target's current locomotion state:

```csharp
[System.Flags]
enum TargetStateRequirement {
    None      = 0,       // no restriction — hitbox lands against any state
    Grounded  = 1 << 0,  // target must be grounded (not in JUMP/FALL/airborne)
    Airborne  = 1 << 1,  // target must be airborne (JUMP or FALL)
    Crouching = 1 << 2,  // target must be in CROUCH
    Standing  = 1 << 3,  // target must be in IDLE/WALK/RUN (upright, not crouching, not airborne)
    Downed    = 1 << 4,  // OTG — target must be in DOWN state; normal hits whiff vs. downed targets
}
// Flags can combine: Grounded | Standing means standing (not crouching, not airborne)
// Omitting Downed from any Grounded attack means it won't auto-OTG — intentional default
```

**`OnHitTargetState`** — determines which state the target enters when hit:

```csharp
enum OnHitTargetState {
    Hurt,       // standard hitstun — target enters HURT, plays pain animation
    Down,       // knockdown — target enters DOWN (floor tumble/slide), then DOWN_RECOVERY
    Launched,   // juggle starter — target enters FALL with strong upward knockback vector
}
// New states (Crumple, WallBounce, etc.) are added here without touching HitboxDataSO logic
```

---

### 3b. Defensive System

One button. The timing of the **initial press** determines the outcome. Holding is the fallback.

| Press timing | Button released | Result |
|---|---|---|
| Within perfect window | Either | `PERFECT_PARRY` |
| Within standard window | Either | `PARRY_ACTIVE` |
| Outside window | Released quickly | Brief whiff, return to locomotion |
| Outside window | Held | `BLOCKING` (after 3–5 frame hold threshold) |

Pressing early and holding naturally falls into block — no punishment for trying to parry and
mistiming it, just a graceful fallback into a defensive stance.

#### Parry Recovery (Spam Deterrent)

After a **missed** parry attempt, a recovery window opens. The outcome depends on whether the
player holds the button:

```
Missed parry press — recovery timer starts
  ├─ Button held until timer completes → no lockout, clean exit to locomotion on release
  └─ Button released before timer completes → PARRY_RECOVERY (locked for remaining time)
```

Holding block through the recovery window waives the lockout entirely — committing to defense
is never punished, only spam-tapping is. Successful parries (PARRY_ACTIVE, PERFECT_PARRY) use
their own state durations as natural recovery; this system applies to **missed attempts only**.

`ParrySettingsSO`:
```csharp
float  parryRecoveryDuration    // e.g. 0.4s — hold >= this = no lockout

// Universal defender rewards — applied on ANY successful parry of that tier
// null = no global reward; per-attack HitboxDataSO defender auras stack on top of these
AuraSO onStandardParryDefenderAura     // e.g. 3s damage boost aura
AuraSO onPerfectParryDefenderAura      // e.g. perfect-parry follow-up window aura
```

`ParrySystem` (or a lightweight component on the player) subscribes to `OnParry` /
`OnPerfectParry` / `OnPerfectParryCounter` EventBus events and calls
`playerAuraManager.ApplyAura(parrySettings.onXxxDefenderAura)` if non-null.
`DamageCalculator` never reads `ParrySettingsSO` — the EventBus keeps them decoupled.

#### DamageCalculator Resolution Order

`HurtboxController` reads `combatProvider.CurrentCombatState` (via `ICombatStateProvider` on the
actor root) and passes it into `DamageCalculator.Resolve` at resolution time. `PlayerStateMachine`
and `EnemyStateMachine` implement `ICombatStateProvider`.

```csharp
DamageCalculator.Resolve(
    AbilityExecutionContext ctx,          // damage weights, onHitAuras, selfAuraOnHit
    StatSheet               attackerStats,
    AuraManager             attackerAuras,  // for selfAuraOnHit + parry attacker debuffs
    StatSheet               targetStats,
    AuraManager             targetAuras,    // for onHitAuras
    CombatLayerState        targetCombatState
)
```

```
// 1. Precondition — whiff check
if (!attack.targetRequirement.Satisfies(targetLocomotionState))
    → silent whiff; return
    // e.g. command throw whiffs vs. airborne; low sweep whiffs vs. jumping; OTG whiffs vs. standing

// 2. Parry resolution — checked before block or damage
if (targetCombatState == PERFECT_PARRY && hasPerfectParryCounter)
    → 0 damage to target
    → apply parryStaggerDamage * perfectParryStaggerMultiplier to attacker StaggerMeter
    → attackerAuras.ApplyAll(onPerfectParryAttackerAuras)
    → targetAuras.ApplyAll(onPerfectParryDefenderAuras)
    → deal perfectParryCounterDamage to attacker health (raw, no armor, no block)
    → EventBus.Publish(OnPerfectParryCounter { attacker, defender, hitboxData })
      (subscribers: combat layer → PERFECT_PARRY_COUNTER state, CinematicDirector → bullet
       time if perfectParryCounterBulletTime, AudioManager → parry clang SFX)
    → return

if (targetCombatState == PERFECT_PARRY)
    → 0 damage to target
    → apply parryStaggerDamage * perfectParryStaggerMultiplier to attacker StaggerMeter
    → attackerAuras.ApplyAll(onPerfectParryAttackerAuras)
    → targetAuras.ApplyAll(onPerfectParryDefenderAuras)
    → EventBus.Publish(OnPerfectParry { attacker, defender })
      (subscribers: combat layer → counter window opens)
    → return

if (targetCombatState == PARRY_ACTIVE)
    → 0 damage to target
    → apply parryStaggerDamage to attacker StaggerMeter
    → attackerAuras.ApplyAll(onParryAttackerAuras)
    → targetAuras.ApplyAll(onParryDefenderAuras)
    → EventBus.Publish(OnParry { attacker, defender })
    → return

// 3. Block resolution
// Block is determined by physical Hurtbox_Block contact + ray validation (see 3n).
// BLOCKING locomotion state alone does not reduce damage — the shield must be hit.
// blockResult is Pending, Blocked, or Unblocked; Pending treated as Unblocked at resolution time.
if (blockResult == Blocked && isUnblockable)
    → full damage + EventBus.Publish(OnGuardBreak { target })
elif (blockResult == Blocked)
    → RawDamage = full * blockDamagePercent
else
    → RawDamage = full

// 4. Armor — applied after all parry/block resolution, for both player and enemy targets
EffectiveArmor = targetStats.Armor * (1 - ctx.armorPenetration)
FinalDamage    = max(1, RawDamage - EffectiveArmor)
// max(1) ensures at least 1 damage always lands — attacks never feel completely negated

// 5. Apply damage + stagger (non-parry path only)
target health -= FinalDamage
target StaggerMeter += ctx.staggerDamage   // 0 for blocked hits if reduced stagger applies

// 6. Super armor / hurt state interrupt (non-parry path only)
if (staggerBarJustFilled)
    → STAGGERED — overrides super armor; deactivate all hitboxes immediately (no trade)
elif (!superArmor)
    → target enters onHitTargetState:
          Hurt     → HURT (standard hitstun)
          Down     → DOWN (knockdown floor animation, then DOWN_RECOVERY)
          Launched → FALL with upward knockback vector (juggle)

// 7. On-hit auras (non-parry path only)
targetAuras.ApplyAll(ctx.onHitAuras)
if (ctx.selfAuraOnHit != null) attackerAuras.Apply(ctx.selfAuraOnHit)

// 8. Publish
EventBus.Publish(OnEntityDamaged { target, finalDamage, ... })
```

---

### 3c. Parry Window — Impact Frame Based

Windows are relative to `impactFrame`, not the start of the attack animation. Each attack
defines its own window sizes — a slow telegraphed wind-up can have a generous perfect parry
window while a fast jab has a tight one.

Perfect parry is always **closest to impact**. Standard parry is the outer/earlier window.
Pressing early rewards reading the telegraph; pressing late (but before impact) rewards
reaction. The tightest, highest-reward zone is right before the hit lands.

```
Slow wind-up (standardBefore=7, perfectBefore=5, standardAfter=4):
[...startup animation...|——standard——|——perfect——|IMPACT|——std——|]
                          -12 to -6    -5 to -1           +1 to +4

Fast jab (standardBefore=3, perfectBefore=2, standardAfter=2):
[startup|——std——|—prf—|IMPACT|—std—|]
          -5 to -3  -2 to -1        +1 to +2
```

`ParrySystem` records the press timestamp. When a hit is registered it compares that timestamp
against the attacker's `impactFrame` timestamp to determine tier.

---

### 3d. Stagger Bar

Each enemy has a `StaggerMeter` component:

```csharp
float maxStagger
float currentStagger
float staggerDecayRate      // per second, decays when not being hit
float staggeredDuration     // how long STAGGERED state lasts
```

**Stagger sources:**
- Normal hit: `staggerDamage`
- Standard parry: `parryStaggerDamage`
- Perfect parry: `parryStaggerDamage * perfectParryStaggerMultiplier`
- Blocked hit: optional reduced stagger (e.g. `0.25x`), configurable

On `currentStagger >= maxStagger`: entity enters `STAGGERED` (animation locked, takes bonus
damage), bar resets to 0.

#### Super Armor and Stagger

An attack flagged `superArmor = true` (on `ComboStep` or `AttackPatternSO`) absorbs
incoming damage without interrupting the animation. Every hit taken during the armor window
still builds stagger normally. When the bar fills:

1. `STAGGERED` fires immediately — armor breaks mid-attack with no delay
2. All active hitboxes deactivate instantly — the hit that caused the stagger break does
   **not** trade; the staggered entity cannot land their pending hit
3. The capitalize window opens clean — punish without eating a counter-hit

Super armor is a commitment mechanic, not a get-out-of-jail card. Opponents can race to
fill the stagger bar rather than retreating.

---

### 3e. Combo System + Input Buffer

Combos are defined as `ComboSO` ScriptableObjects — an ordered list of `ComboStep` entries.

#### ComboStep Fields

```csharp
// Input
InputType requiredInput              // light, heavy, directional modifier
float     inputBufferWindow          // how early before the cancellable frame a press is accepted (e.g. 0.2s)
float     inputExpireWindow          // how long after the cancellable frame an input stays valid (e.g. 0.15s)

// State
string    nextStateName              // combat state to transition into
AnimationClip animationOverride      // combo-specific animation variant

// Cancels
bool isDefensiveCancellable          // default true — parry/block press immediately interrupts
bool isComboBufferCancellable        // default true — buffered combo input fires at cancellable frame
bool canCancelIntoSpecial            // default false — attack can 2-in-1 cancel into a motion input
                                     // special ON HIT only; requires advancedCombatActive naturally
                                     // since specials already gate on it

// Armor
bool superArmor                      // default false — taking damage does not interrupt this attack
                                     // full damage still applies; stagger still builds normally
                                     // stagger bar filling is the ONLY interrupt — see 3d

// Polish
bool hasCinematicFlourish            // brief zoom / impact freeze on this step
float staminaCost
```

#### Input Buffer

A timestamped ring buffer (16 entries) on the player stores inputs ahead of execution.

- **Press during non-cancellable frames** → stored in buffer, executes at the first valid
  (cancellable) frame as long as it hasn't expired
- **Press too early** → buffered and held until the cancellable frame (responsive feel,
  rewards rhythm without requiring frame-perfect timing)
- **Press too late / expired** → dropped, player must press again

This gives the "slightly sloppy but still rewarded" feel that makes combos feel frenetic
rather than clinical.

#### Cancel Priority (each frame at cancellable point)

```
During any attack:
  ├─ Damage received + superArmor = false → HURT state (attack cancelled immediately)
  ├─ Damage received + superArmor = true  → damage/stagger applied; animation continues
  │   (stagger bar filling overrides super armor → immediate STAGGERED, hitboxes off)
  ├─ Parry/block pressed + isDefensiveCancellable = true  → immediate defensive state
  ├─ Parry/block pressed + isDefensiveCancellable = false → ignored, attack plays out
  ├─ Buffered attack input present + isComboBufferCancellable = true → fire next combo step
  └─ No valid input → attack plays to end, Combat Layer → NONE
```

Heavy finishers, grab attacks, and special moves set `isDefensiveCancellable = false` —
committing to them is a deliberate risk. Standard light/medium attacks default to cancellable,
keeping the flow frenetic: **attack → attack → parry → counter → attack**.

#### 2-in-1 Cancel (SF2-Style)

When `canCancelIntoSpecial = true` on a `ComboStep`, the attack can be cancelled into a
motion input special **on hit only**. This is the Street Fighter 2 "2-in-1" cancel — the
special fires during the active or early recovery frames of the attack, but only if the
hit actually connected.

**How it works:**

```
Attack with canCancelIntoSpecial = true swings:
  ├─ Hit connects (HurtboxController overlap confirmed)
  │   → hitConnected flag set for this ComboStep
  │   → MotionInputDetector checks buffer for a valid motion pattern
  │     ├─ Pattern matched + advancedCombatActive = true → special fires, cancels recovery
  │     └─ No match → attack plays out normally
  └─ Whiff (no hit confirmed)
      → canCancelIntoSpecial is ignored; attack plays to end
```

The `advancedCombatActive` requirement is **not an additional gate** — it is already
enforced naturally, since only motion input specials are valid cancel targets, and those
already require `advancedCombatActive` to detect. Players without `ADVANCED_COMBAT`
unlocked simply cannot perform the cancel, with no extra logic needed.

**Design intent:**
- Whiff punishing is preserved — specials cannot be thrown out safely without landing
- Rewards players who learn which normals cancel into which specials
- Baked-in combo chains (`isComboBufferCancellable`) chain naturally and do not use this
  system; 2-in-1 is exclusively for chaining a normal into a motion special mid-combo

---

### 3f. Hit Pause

On any hit: `Time.timeScale = 0` for 2–6 frames (configurable per attack weight in
`HitboxDataSO`), paired with Cinemachine impulse shake and a chromatic aberration pulse.

---

### 3g. Dodge

**`DodgeSO`** full field list:

```csharp
// I-frames
int   iFrameStartFrame
int   iFrameEndFrame

// Movement
float totalDuration
AnimationCurve speedCurve
float staminaCost

// Recovery
int   recoveryFrames

// Perfect dodge
bool   perfectDodgeBulletTime          // default true
float  perfectDodgeTimeScale           // e.g. 0.2
float  perfectDodgeZoomAmount          // Cinemachine FOV delta
AuraSO perfectDodgeAura                // aura applied on perfect dodge (assigned in Inspector)
```

- I-frames call `hurtboxController.SetInvulnerable(true)` at `iFrameStartFrame` and
  `SetInvulnerable(false)` at `iFrameEndFrame`, deactivating the body hurtbox GameObject so
  attacks physically whiff. Attacks with `piercesDodgeIFrames = true` bypass this via a
  separate `Hurtbox_Pierce` collider that is never deactivated (see 3n — I-Frames).
- Position delta via direct transform + collision check (not physics) for precise frame control.
- `DODGE_RECOVERY` window after the dodge prevents spam.

**Perfect dodge** triggers when:
1. Dodge input fires within `perfectDodgeWindowBefore` frames of an incoming attack's `impactFrame`
2. The attacker's hitbox would have overlapped the player hurtbox (i-frames actually saved them)
3. `piercesDodgeIFrames = false` on the incoming attack

On perfect dodge:
```
PERFECT_DODGE state entered
  → timeScale drops to perfectDodgeTimeScale (if perfectDodgeBulletTime = true)
  → Cinemachine zooms by perfectDodgeZoomAmount
  → AuraManager.ApplyAura(perfectDodgeAura) on player
  → dodge animation completes at reduced timescale
  → timescale and camera restore
  → DODGE_RECOVERY
```

The aura's own `duration` governs the follow-up window. Any unlocked ability that requires
the aura checks `AuraManager.HasAura()` independently — the dodge system has no knowledge
of what consumes it.

---

### 3h. Aura System

Auras share the same architecture — a `AuraSO` with positive or negative effects,
applied to any entity carrying a `AuraManager` component (player, enemy, boss).

### AuraSO — Base Fields

```csharp
string    auraId                 // unique identifier, used for HasAura() checks
string    displayName
Sprite    icon
float     duration               // seconds; -1 = permanent until explicitly removed
bool      stackable              // can multiple instances apply simultaneously
int       maxStacks
bool      isNegative               // cosmetic flag for UI tinting, not logic
AuraVisualEffectSO visualEffect  // null = no visual change
```

Each `AuraSO` is self-contained. It subscribes to EventBus events **when applied** and
unsubscribes **when removed**. `AuraManager` owns the active list and lifetime — it does
not need to know what any individual aura does.

**Trigger examples:**
```
PoisonAuraSO          → OnGameTick — deals damage every 10 ticks
DamageBoostAuraSO       → OnPlayerAttackLanded — applies damage multiplier for duration
PerfectDodgeReadyAuraSO → expires by duration; abilities poll HasAura() each frame
BlockBreakAuraSO      → OnPlayerHit — increases damage taken temporarily
```

### AuraManager

Component on any entity that can receive auras (player, enemies, bosses):

```csharp
List<ActiveAura> activeAuras    // runtime: AuraSO + remaining duration + stack count

void ApplyAura(AuraSO)          // adds instance, aura subscribes to its triggers
void RemoveAura(string auraId)  // removes instance, aura unsubscribes
bool HasAura(string auraId)     // queried by abilities, DamageCalculator, AI, etc.
```

Broadcasts via EventBus:
- `OnAuraApplied(auraId)` — for UI, audio, visual controller
- `OnAuraRemoved(auraId)` — for UI, visual controller cleanup
- `OnAuraStackChanged(auraId, stacks)` — for UI stack indicators

### GameTickManager

Lightweight singleton that fires `OnGameTick` at a fixed interval. Tick-based auras subscribe
here rather than `Update`, keeping effects deterministic and frame-rate independent.

```csharp
int tickIntervalFrames    // FixedUpdate frames per game tick (e.g. 4)
event Action OnGameTick   // broadcast via EventBus
```

### AuraVisualEffectSO

Assigned per aura in the Inspector. `AuraVisualController` (component on the player and any
enemy that can receive visible auras) listens to `OnAuraApplied` / `OnAuraRemoved` and
drives the visual response.

```csharp
Color      spriteColorTint       // e.g. green for poison, red for enrage
Material   materialOverride      // optional shader swap (glow, outline, dissolve)
GameObject particlePrefab        // optional looping particle effect, spawned as child
float      pulseSpeed            // > 0 = tint pulses rather than being static
```

**`AuraVisualController`** on apply:
- Lerps `SpriteRenderer.color` to `spriteColorTint`
- Swaps material if `materialOverride` is set (via `material.SetColor()` at runtime for
  shader-driven glow intensity)
- Spawns `particlePrefab` as a child GameObject

On remove:
- Lerps color back to default
- Restores original material
- Destroys particle instance

Multiple simultaneous auras are handled by priority order or additive blending (configurable
per `AuraVisualEffectSO`).

---

### 3i. Stat System

Stats are split into two categories: **primary stats** (innate to the character, grow via
levelling and upgrades) and **equipment stats** (contributed by equipped items). Both feed
into derived values used at runtime by the damage formula, health system, and chi pool.

#### Primary Stats

| Stat | Intent |
|---|---|
| `Strength` | Scales physical attack damage — punches, kicks, weapon strikes |
| `Chi` | Scales chi/energy attack damage; also governs max chi pool size |
| `Dexterity` | Raw agility stat — drives AttackSpeedMultiplier and MovementSpeedMultiplier as computed properties |
| `Constitution` | Governs max health pool size |

#### Equipment Stats (player only — sourced from equipped items, not innate)

| Stat | Intent |
|---|---|
| `WeaponDamage` | Flat damage contribution from the equipped weapon; 0 when unarmed |
| `Armor` | Flat damage reduction applied to incoming hits; 0 when no armor equipped |

> **Enemies** do not have an inventory. Both `WeaponDamage` and `Armor` are native stats
> on `EnemyDataSO`, authored directly in the Inspector alongside Strength, Chi, etc.

#### Derived Stats (computed, never set directly)

| Derived | Formula (intent — coefficients TBD during balancing) |
|---|---|
| `MaxHealth` | f(Constitution, Level) |
| `MaxChiPool` | f(Chi, Level) |
| `AttackSpeedMultiplier` | f(Dexterity) — read by `AnimatorSpeedSync` to set `animator.speed`; also scales recovery frame durations |
| `MovementSpeedMultiplier` | f(Dexterity) — read by `PlayerController` to scale movement velocity |
| `LevelScale` | f(Level) — global damage scalar applied to all attacks |
| `DetectionRangeMultiplier` | From equipment bonus overlay; default 1.0 — stealth gear reduces below 1.0; read by enemy detection to scale `visionRange` and `alarmRadius` |

#### StatSheet

`StatSheet` is a component on the player (and on enemies for their own damage formulas).
It holds the raw primary stat values and exposes computed derived values as properties.
Equipment bonuses are aggregated into a separate `equipmentBonuses` struct and added
on top at read time — equipping and unequipping items never mutates base stats.

```csharp
// Primary (base values, modified by level-up and permanent upgrades)
int strength
int chi
int dexterity
int constitution
int level

// Equipment overlay (player only — aggregated from all equipped items by EquipmentManager)
// Enemies leave this zeroed out; their WeaponDamage and Armor are set directly as native stats
StatBonus equipmentBonuses     // additive on top of base stats
                               // see StatBonus struct below

// Derived (computed properties — never set directly, always calculated from raw stats)
float MaxHealth                  // => f(constitution, level)
float MaxChiPool                 // => f(chi, level)
float AttackSpeedMultiplier      // => 1f + (dexterity * attackSpeedCoefficient)
float MovementSpeedMultiplier    // => 1f + (dexterity * movementSpeedCoefficient)
float LevelScale                 // => f(level)
int   WeaponDamage                   // player: from equipmentBonuses; enemy: native stat, set directly
int   Armor                          // player: from equipmentBonuses; enemy: native stat, set directly
float DetectionRangeMultiplier       // from equipmentBonuses; default 1.0; stealth gear reduces below 1.0
                                     // read by enemy AI detection to scale visionRange + alarmRadius
// coefficients are configurable on a StatConfigSO, not hardcoded
```

#### StatBonus

Aggregated by `EquipmentManager` from all equipped items. Cleared and rebuilt on every equip/unequip.
`detectionRangeMultiplier` is multiplicative (1.0 = no change); all other fields are additive.

```csharp
struct StatBonus {
    // Existing fields
    int   weaponDamage               // flat bonus to WeaponDamage
    int   armor                      // flat bonus to Armor
    float detectionRangeMultiplier   // multiplicative; 1.0 = no change, 0.5 = half detection range

    // Equipment-derived bonuses
    int   maxHealth          // flat bonus added on top of constitution-derived MaxHealth
    int   maxChiPool         // flat bonus added on top of chi-derived MaxChiPool
    int   strength           // adds to base primary stat; flows through all derived formulas
    int   chi
    int   dexterity
    int   constitution
    float movementSpeedBonus // additive bonus to MovementSpeedMultiplier (separate from DEX scaling)
    float attackSpeedBonus   // additive bonus to AttackSpeedMultiplier (separate from DEX scaling)
}
```

`MaxHealth` and `MaxChiPool` read `equipmentBonuses.maxHealth` / `equipmentBonuses.maxChiPool`
as a flat addend after the primary stat formula. Primary stat properties (`Strength`, `Chi`, etc.)
return `baseValue + equipmentBonuses.strength` (etc.) so all downstream formulas automatically benefit.

`StatSheet` broadcasts `OnStatsChanged` via EventBus whenever any value changes, allowing
health bars, chi bars, and UI to react without polling.

> The primary stat fields (`strength`, `chi`, `dexterity`, `constitution`, `level`) on
> `StatSheet` are runtime values. They are initialized from `PlayerPersistentData` on game
> load and written back before every save. Equipment overlays (`equipmentBonuses`) are
> re-derived from `EquipmentManager` on load — they are never stored directly in the save file.

```csharp
void IncrementStat(PrimaryStatType stat, int points)
    // Adds `points` to the specified base stat field.
    // Calls RefreshEquipmentBonuses() to re-derive all dependent values.
    // Only called by the stat allocation UI — never by any other system.

enum PrimaryStatType { Strength, Chi, Dexterity, Constitution }
```

#### Capability & Damage Modifiers

Separate from `StatBonus` above, deliberately — `StatBonus` covers a small, fixed set of named
primary/derived stat bonuses; this covers open-ended categories that keep growing throughout
development (new charge types, new elements, new ability-specific bonuses) without ever wanting
a new struct field added per one. Both live on `StatSheet`, both follow the same "recompute once
when something changes, never per read" discipline — they're shaped differently because the
problems are different shapes, not because one is more "correct."

```csharp
enum ModifierCategory { Charge, ElementalDamage, AbilityDamage }
enum ChargeType        { AerialJump, WallJump, Dash }   // grows rarely, one line at a time
enum DamageElement     { Physical, Fire, Ice }           // same
```

`StatSheet` carries the aggregate itself (`ModifierAggregate`, populated per §3j):

```csharp
Dictionary<ChargeType, int>         chargeBonuses
Dictionary<DamageElement, float>    elementalDamageBonus     // e.g. 0.10 = +10%, additive within element
Dictionary<AbilityEffectSO, float>  abilityDamageBonus       // e.g. 1.0 = +100%, additive per ability
```

Recomputed from scratch — not incrementally added/subtracted — whenever the underlying source
list changes: an item equipped or unequipped, an aura applied or removed, an ability unlocked.
Equipment is not the only source; see §3j for what actually populates this and §3l for how
unlocked (rather than equipped) sources feed the same aggregate.

**Read pattern matches `StatSheet`'s existing primary-stat formula exactly** — `baseValue +
bonus`, same as `Strength => baseValue + equipmentBonuses.strength` already works today. Charges
default to a base of 0 (no aerial jump, no wall jump, until something grants one), so consumers
just read the aggregate directly — `PlayerController.TryWallJump()` reads
`statSheet.chargeBonuses.GetValueOrDefault(ChargeType.WallJump)` at the point of use instead of a
flat Inspector field. Nothing about `TryWallJump()`'s own logic branches on charge type — it only
ever reads its own slot, same as if that number were still a serialized field. A new charge type
never touches existing consumer code; a new consumer never touches the aggregate.

---

### 3j. Ability Execution Pipeline

Abilities are fully data-driven and unaware of how equipment or upgrades might modify them.
At execution time, a default context is built from the effect's own data, passed through
a modifier pipeline registered by `EquipmentManager`, and then executed against the
final mutated values.

#### AbilityExecutionContext

Plain mutable C# class (not a ScriptableObject — created and discarded per execution):

```csharp
// Damage formula inputs (defaults from AbilityEffectSO / HitboxDataSO, freely mutable)
float  strengthWeight
float  chiWeight
float  weaponWeight
float  baseMultiplier
int    hitCount                 // default 1 — AddHitModifier increments this
float  armorPenetration         // 0.0–1.0, default from HitboxDataSO — equipment can raise this

// Timing
float  hitstunDuration          // can be extended or reduced by modifiers

// On-hit effects
List<AuraSO> onHitAuras       // applied to target on each hit
AuraSO       selfAuraOnHit      // applied to caster on hit (null = none)

// Read-only reference (modifiers may read this to compute values)
StatSheet    casterStats
```

No `abilityId` field — see §3l, `TriggerEffectSO`/`AbilityEffectSO` are real object references
now, not strings, so `EquipmentManager` keys its registries on the effect object directly
(below) rather than needing an ID stored back on the context.

**Decided: snapshot, don't hold a live reference, for anything that can resolve later.** A melee
hit resolves the same frame `BuildContext` runs, so reading `casterStats` live costs nothing. A
projectile does not — it can still be in flight when `DamageCalculator.Resolve()` finally runs
against it. For that case, the stat contributions `casterStats` would otherwise supply must be
baked into concrete numbers on the context at `BuildContext` time, same as equipment modifiers
already are, rather than resolved live off the reference at impact. This keeps a thrown effect
fully self-contained the instant it leaves the caster — no dependency on the caster still existing
or being in the same state by the time it lands, and no risk of it retroactively getting
stronger or weaker from something that happened after the throw. Applies generally to anything
in this project that detaches from its creator and acts later, not just projectiles.

#### AbilityModifierSO

Abstract ScriptableObject. Contributes to `StatSheet`'s `ModifierAggregate` (§3i) once,
whenever the active modifier list changes — **not** invoked at ability execution time. Earlier
drafts of this pipeline called `Modify(ctx)` on every registered modifier on every single hit,
which is exactly the "recompute on every read instead of on change" pattern avoided everywhere
else in this design. Recomputing once per equip/unequip/aura-change/unlock and reading a plain
precomputed value at execution time removes a live iteration from the hottest path in combat.

```csharp
public abstract void ContributeTo(ModifierAggregate aggregate, AbilityEffectSO targetEffect);
```

**Two families, split by whether the contribution is a number or a behavior.**

`NumericModifierSO` — one concrete class, never subclassed. Designers create *instances* of it
directly. Covers every purely-additive case — charges, elemental damage, per-ability damage:

```csharp
public class NumericModifierSO : AbilityModifierSO {
    ModifierCategory category;
    ChargeType       chargeTarget;    // relevant when category == Charge
    DamageElement    elementTarget;   // relevant when category == ElementalDamage
    // targetEffect (passed into ContributeTo) is the target when category == AbilityDamage
    float            value;
}
```

Its `ContributeTo` is the same few lines regardless of what a designer authors — write `value`
into `aggregate.chargeBonuses[chargeTarget]`, `aggregate.elementalDamageBonus[elementTarget]`,
or `aggregate.abilityDamageBonus[targetEffect]` depending on `category`. "+1 wall jump charge,"
"+10% fire damage," "+100% Hadouken damage" are all just instances of this one class with
different field values — zero new code, ever, as long as the target category/enum value already
exists (see §3i). A genuinely new category (a new `ChargeType`/`DamageElement` value) is a
one-line, isolated addition, not a new class, and only needs to happen once per new *concept* —
never again per new item that grants it.

The remaining family is genuine behavior swaps — not reducible to a number, so each still needs
its own subclass, same "new subclass, zero changes elsewhere" property as everything else
composed in this design:

| Class | What it does |
|---|---|
| `ApplyAuraOnHitModifierSO` | contributes an aura to `aggregate.onHitAurasFor[targetEffect]` |
| `ApplySelfAuraOnHitModifierSO` | contributes an aura to `aggregate.selfAuraOnHitFor[targetEffect]` |
| `ExtendHitstunModifierSO` | contributes seconds to `aggregate.bonusHitstunFor[targetEffect]` |
| `AddHitModifierSO` | contributes to `aggregate.bonusHitCountFor[targetEffect]` |
| `ShiftStatWeightModifierSO` | adjusts chi/strength/weapon weighting for a specific effect (e.g. make flying kick chi-scaled) |
| `OverrideProjectileHitBehaviorModifierSO` | swaps `ProjectileHitBehaviorSO` for a projectile-spawning effect (§3l) — e.g. gloves that make a fireball pierce instead of despawning on first contact. Sibling fields for size/range overrides on the same modifier once `AbilityExecutionContext` actually carries projectile fields — noted as a gap, not built |
| `OverrideEffectModifierSO` | swaps which `EffectSO` fires for a given trigger entirely — e.g. boots that turn a melee roundhouse into a thrown fireball. Checked at the point an effect would otherwise fire (§3l's trie completion, for motion-triggered abilities); no match falls through to the original effect unchanged |

New qualitative behaviors still need a programmer to write a new subclass — inherent to inventing
a mechanic that isn't just a number, not a process failure. It's a one-time cost per *kind* of
behavior, never per item that grants it. Item and upgrade designers assign instances of either
family to item/passive assets — the aggregation pass never needs to know which kind it's
looking at, it just calls `ContributeTo` on everything in the list.

**Gap, not yet resolved:** `aggregate.elementalDamageBonus` needs an attack's damage element to
apply against, and nothing carries one yet — `HitboxDataSO` has no `DamageElement` field today,
so every attack is implicitly `Physical`. Small, mechanical addition when elemental damage
actually gets built; not blocking anything designed so far.

#### EquipmentManager

Singleton (or ServiceLocator-resolved) component on the player. Owns the modifier registry
and handles equip/unequip lifecycle:

```csharp
// Registry: which AbilityEffectSO → modifiers contributed by currently equipped items
Dictionary<AbilityEffectSO, List<AbilityModifierSO>> modifiersByEffect

void RegisterModifiers(EquipmentSO item)     // called on equip
void UnregisterModifiers(EquipmentSO item)   // called on unequip

// Rebuilds StatSheet's ModifierAggregate from scratch — not incrementally — using every
// currently-registered modifier across modifiersByEffect. Equipment is not the only source
// that feeds this: unlocked passives and active auras contribute here too (§3l), so this walks
// all three, not just modifiersByEffect. Recomputing from the full list rather than adding/
// subtracting deltas means nothing can drift, and multiplicative or order-dependent modifiers
// stay correct regardless of what order items get equipped or removed in.
void RecalculateAggregate()

// Called by an effect at execution time — O(1): copies already-computed aggregate values into
// a fresh context, no iteration happens here
AbilityExecutionContext BuildContext(AbilityEffectSO effect, AbilityExecutionContext defaults)
```

`RegisterModifiers`/`UnregisterModifiers` update `modifiersByEffect`, then call
`RecalculateAggregate()`. `BuildContext` clones `defaults` and layers the aggregate's
already-computed values for `effect` on top — `ctx.baseMultiplier *= (1 +
aggregate.abilityDamageBonus.GetValueOrDefault(effect))`, `ctx.onHitAuras =
aggregate.onHitAurasFor.GetValueOrDefault(effect)`, and so on — a fixed handful of dictionary
reads, not a loop. The effect never sees the modifier list at all, only the finished context.

#### Execution Flow

```
Trigger fires → TriggerEffectSO.effect.Execute() called (see §3l)
  → effect builds default AbilityExecutionContext from its own AbilityEffectSO/HitboxDataSO data
  → EquipmentManager.BuildContext(this, defaults) called
      → aggregate values already computed for this effect are layered onto the context — no
        modifier iteration happens here, that already happened at RecalculateAggregate() time
  → effect executes using final context values
  → DamageCalculator.Resolve(ctx, attackerStats, attackerAuras, targetStats, targetAuras, targetCombatState) called per hit
      → full resolution order: parry check → block check → armor → damage + stagger → hurt state → on-hit auras → EventBus.Publish
      // (see 3b for complete resolution pseudocode)
```

Not every `AbilityEffectSO` necessarily runs through `DamageCalculator` — a self-buff effect
might just call `AuraManager.ApplyAura()` directly, with no need for `AbilityExecutionContext`
at all. The context/modifier pipeline exists specifically for effects whose numbers equipment
should be able to influence; an effect that doesn't deal damage or apply a hit-scaling value
is free to skip it entirely.

---

### 3k. Movement Ability Plugin System

Movement abilities follow the same philosophy as `AbilityModifierSO` — they are
self-contained components; the base player systems have no knowledge of them. Each ability
is a `MonoBehaviour` added to the player prefab. The ability checks its own unlock condition,
manages its own state, and calls exposed hooks on `PlayerController` to affect movement.

Adding a new movement ability never requires touching `PlayerStateMachine` or
`PlayerController`.

#### PlayerController Hooks

`PlayerController` exposes a minimal set of hooks. It has no knowledge of which ability
components are installed:

```csharp
void    ApplyImpulse(Vector2 force)             // adds velocity — air dash, grapple pull
void    SetVerticalVelocity(float velocity)     // sets vertical speed directly (absolute, not
                                                 // additive) — ground jump, aerial charge jumps,
                                                 // and wall jump all use this so every launch is
                                                 // the same regardless of current fall speed
void    ForceLocomotionState(string stateId)    // overrides current locomotion state
Vector2 GetVelocity()                           // read current velocity
bool    IsGrounded()                            // ground contact query
bool    IsAirborne()                            // convenience inverse
```

#### Wall Jump (`WallJumpAbility` — `WALL_JUMP`)

Precursor to Double Jump for vertical access — a single unlock before the player has any
air-jump charges at all (see Ability Gates: `WALL_JUMP` is listed before `DOUBLE_JUMP`).
Uses its own charge pool so unlocking/equipping one never grants the other; the two are
designed to combine, not gate each other (see Sequence Breaks below).

```csharp
int     maxWallJumps       // base value, default 0 until the ability/gear is acquired (matches
                            // PlayerController.JumpCharges' manual-testing pattern) — consecutive
                            // wall jumps off the SAME wall without an intervening ground touch or
                            // contact with another wall
int     remainingWallJumps // runtime counter; restored on landing or on contacting a new wall
float   wallSlideSpeed     // max downward speed while WALL_SLIDE is held (clamped, not instant)
Vector2 wallJumpVelocity   // (outward.x, upward.y) — launches away from the wall, not straight up
float   wallStickTime      // brief input-lockout after leaving the wall so the jump reads as
                            // "off the wall" instead of an instant U-turn back into it
```

- `PlayerStateMachine` enters `WALL_SLIDE` from `FALL` per the condition in 2 above
- `WallJumpAbility` listens to `OnJump` from `InputReader`; only acts while in `WALL_SLIDE`
  and `remainingWallJumps > 0`
- Calls `playerController.SetVerticalVelocity` for the upward component and sets the
  horizontal component outward from the wall the same way — absolute, not additive, for the
  same reason as `JumpState.Enter`: a wall jump should always launch the same amount
  regardless of how fast the player was already sliding/falling
- Decrements `remainingWallJumps`; restored on landing (`OnPlayerLanded`) or on contacting a
  new wall — entirely independent of `DoubleJumpAbility.remainingJumps`; neither pool reads
  or modifies the other
- Base game grants 0 `maxWallJumps` until the ability/gear is acquired, same as
  `PlayerController.JumpCharges` defaulting to 0 until double jump is worth testing

**On sequence breaks:** wall jump, double/triple jump, and air dash are independent, stackable
systems by design. Nothing in this plan gates one on the state or charge count of another —
e.g. a wall jump never requires or consumes a double-jump charge, and nothing stops chaining
wall-jump → double-jump → air-dash in a single airborne sequence if the player has charges for
all three. Emergent sequence breaks from combining them are an accepted, intentional consequence
of this design, not a bug to patch out later.

#### Double Jump (`DoubleJumpAbility`)

```csharp
int   maxExtraJumps       // base value, default 1
int   remainingJumps      // runtime counter; restored on landing
float jumpImpulseForce
```

- Listens to `OnJump` from `InputReader`
- Only acts when `IsAirborne()` and `remainingJumps > 0`
- Calls `playerController.SetVerticalVelocity(jumpImpulseForce)` — absolute, not additive (see
  PlayerController Hooks above); an additive impulse doesn't fully cancel existing fall speed,
  so a charge jump thrown out while already falling would launch weaker than one thrown at
  the apex
- Decrements `remainingJumps`; on `OnPlayerLanded`:
  `remainingJumps = maxExtraJumps + statSheet.chargeBonuses.GetValueOrDefault(ChargeType.AerialJump)`
  (§3i — the same aggregate `TryWallJump` reads from, not a separate charge-bonus system)

#### Air Dash (`AirDashAbility`)

```csharp
int     maxAirDashes        // base value, default 1
int     remainingAirDashes  // runtime counter; restored on landing
DodgeSO airDashData         // reuses existing DodgeSO — same i-frames, speed curve, recovery
```

- Listens to `OnDodge` from `InputReader`
- Only acts when `IsAirborne()` and `remainingAirDashes > 0`
- Delegates entirely to `DodgeSystem` using `airDashData`
- Same i-frame and perfect dodge logic applies unchanged
- On `OnPlayerLanded`:
  `remainingAirDashes = maxAirDashes + statSheet.chargeBonuses.GetValueOrDefault(ChargeType.Dash)`
  (§3i)

#### Grapple / Hookshot (`GrappleAbility` — `WIRE_FU`)

Implemented as a strong directional pull — no pendulum physics, no new locomotion states.
`JUMP` and `FALL` handle the player throughout; the ability stays a pure plugin.

```csharp
float     maxGrappleRange
float     grappleAimToleranceDegrees  // cone half-angle for magnetize (e.g. 35°) — candidates
                                      // outside this cone from movement/aim direction are ignored;
                                      // nearest within cone is chosen
LayerMask grapplePointLayer
float     launchImpulse          // strength of the pull toward the anchor point
float     releaseRedirectStrength // optional velocity nudge on release based on approach angle
```

On `OnGrapple` input:
```
1. Physics2D.OverlapCircle(playerPosition, maxGrappleRange, grapplePointLayer)
     → collect all active GrapplePoints within range
     → filter to those within grappleAimToleranceDegrees of player movement/aim direction
     → select nearest among remaining candidates (magnetize — no precise aim required)
     → if none found → no grapple fires
2. If found → ApplyImpulse(directionToPoint * launchImpulse)
              player flies toward anchor through normal JUMP/FALL states
3. On reaching anchor (proximity check) or second OnGrapple press
   → optionally ApplyImpulse(redirectVector * releaseRedirectStrength)
   → normal aerial locomotion resumes with carried momentum
```

No joint physics, no state overrides, no locomotion lock. The feel is a snappy wuxia wire
launch rather than a pendulum swing — fast, directional, releases cleanly into aerial attacks.

**`GrapplePoint`** — simple component on world anchor rings:

```csharp
bool isActive    // can be toggled (e.g. a boss destroys anchor rings as a phase attack)
```

The future grapple attack upgrade (using the line as a weapon) is handled entirely in the
existing hitbox/damage pipeline — `GrapplePoint` layer never participates in combat collisions.

---

### 3l. Motion Input System

Fighting game-style directional sequences (quarter circles, half circles, etc.) are one kind
of `TriggerSO` — see below. Detection is entirely separate from the combo system — different
resolution path, different SO types, different detector. Once a trigger fires, its paired
effect runs through the same `AbilityExecutionContext` pipeline as any other ability, where
relevant (see §3j — not every effect needs it).

A traveling special (e.g. a fireball) additionally needs a projectile carrier — see
"Projectile System" below. Every existing melee attack's hitbox is a child transform of its
attacker, keyframed on that attacker's own clip; nothing about that model covers a detached,
independently-moving collider.

#### Advanced Combat Mode

Holding the assigned button (e.g. left bumper) sets one flag on `PlayerController`:

```csharp
bool advancedCombatActive
// in movement update: if (!advancedCombatActive) { UpdateFacing(moveInput); }
```

While active:
- Facing direction is locked — the character won't turn around during stick rotations
- `MotionInputDetector` walks its trie against action button presses (see below)
- Movement speed is unchanged

The `ADVANCED_COMBAT` ability must be unlocked before the button does anything.
Pressing it before unlock has no effect — the flag simply never sets.

#### Analog Stick → Zone Mapping

The raw stick `Vector2` is converted to a single integer zone every frame before reaching
the trie walker (see `MotionInputDetector` below). Three steps:

**1. Dead zone check**
If `stick.magnitude < zoneDeadzone` → zone 5 (neutral). Prevents drift registering as input.

**2. Angle-based sector snap**
Beyond the dead zone, `atan2(y, x)` maps the stick to one of 8 sectors:

```
     8 (90°)
  7     9
4    5    6
  1     3
     2 (270°)
```

Each sector is centered on its angle. Default sector width is **45°** per zone.
Setting cardinal zones to **50°** (diagonals shrink to 40°) makes quarter circles more
forgiving — a tuning value on `StickInputConfigSO`, not a code change.

**3. Facing-relative normalization.** Zones with a horizontal component (1, 3, 4, 6, 7, 9)
are mirrored by `FacingRight` before the zone goes anywhere else — 2/8/5 are untouched,
carrying no horizontal component to flip. Every `MotionInputTriggerSO` is authored once, in
"toward/away" terms, rather than absolute screen-space left/right — `QCF` is simply "the
motion toward wherever the player is facing," with no separate mirrored variant needed.
Consistent with how facing is already handled elsewhere in this codebase (the hitbox
pipeline mirrors via `transform.localScale.x` rather than re-deriving signs downstream, see
§3n) — mirror once at the source, not in every consumer.

The decimal stick position is consumed here and never reaches anything downstream. All
motion detection reasons purely in integers, already facing-normalized.

#### Trigger, Effect, and TriggerEffectSO

Generalizes past just combat. Anything in the game that fires an effect off some triggering
condition — a motion input, interacting with a prop, killing a specific enemy — is the same
shape: a trigger, an effect, and a small asset binding one to the other. Only the combat case
(motion inputs) is actually being built right now; the split costs nothing to keep general.

```csharp
public abstract class TriggerSO : ScriptableObject { }

public abstract class EffectSO : ScriptableObject {
    public abstract void Execute(GameObject caster);
}

public class TriggerEffectSO : ScriptableObject {
    public TriggerSO trigger;
    public EffectSO  effect;

    // Always present, even where unused — an environment-prop buff might only ever show a
    // generic "Interact" prompt on screen with no name or icon, but the fields cost nothing
    // to carry, and something will eventually want them (a spellbook UI, at minimum).
    public string displayName;
    public string description;
    public Sprite icon;
}
```

Whether a *different* domain (prop interactions, kill triggers) ends up subclassing these
same `TriggerSO`/`EffectSO` bases, or reimplements the identical shape under its own names,
is a call worth making when one of those actually gets designed — not before. The only
concrete classes that exist today are the combat ones below.

#### MotionInputTriggerSO

`TriggerSO` subclass for fighting-game-style directional sequences. Each step carries a zone
group rather than a single zone, and an optional minimum hold duration for charge inputs:

```csharp
public class MotionInputTriggerSO : TriggerSO {
    struct MotionStep {
        int[]  zones             // any zone in this array satisfies the step
        float  minHoldDuration   // 0 = transition (pass-through); > 0 = charge (must hold)
    }

    MotionStep[] sequence        // ordered steps
    InputAction  confirmButton   // OnAttackLight, OnAttackHeavy, OnAbility1, etc.
    float        sequenceWindow  // max seconds for the WHOLE sequence, from first step to last —
                                  // not per-step; see MotionInputDetector. Kept brief by design.
}
```

**Zone groups decouple intent from exact position.** `zones=[1,4,7]` means "any back
direction" — the player can hold straight back, down-back, or up-back interchangeably.
Diagonal zones naturally carry both their axis components, matching Guile-style flexibility
at no extra cost.

**Common patterns** (authored facing-relative — "forward"/6 always means toward the
opponent):

| Name | Steps | Shorthand |
|---|---|---|
| Quarter Circle Forward | [2,3]→[3,6]→[6] | QCF |
| Dragon Punch | [6]→[2,3]→[3,6] | DP |
| Quarter Circle Back | [2,1]→[1,4]→[4] | QCB |
| Half Circle Back | [6,3]→[3,2]→[2,1]→[1,4]→[4] | HCB |
| Sonic Boom (charge) | [1,4,7] hold 1.2s → [6,3,9] | CB→F |
| Flash Kick (charge) | [1,2,3] hold 1.2s → [7,8,9] | CD→U |
| Back Back Forward | [4,1,7]→[4,1,7]→[6,3,9] | BBF |

#### AbilityEffectSO

`EffectSO` subclass for combat abilities specifically — a domain-specific concrete base, not
a reuse of `EffectSO` directly, so combat effects can share combat-specific plumbing
(`AbilityExecutionContext`, see §3j) without that leaking into unrelated domains.
`SpawnProjectileEffectSO` (see "Projectile System" below), a command-throw effect, a
self-buff effect, and an enemy-debuff effect are all `AbilityEffectSO` subclasses — one
class per distinct kind of thing an ability can actually do, same "new subclass, zero
changes elsewhere" property as every other composed behavior in this design.

#### Skill Loadout

Two tiers, not one flat unlock set:

- **Owned pool** — every `TriggerEffectSO` the player has ever unlocked, unbounded,
  persisted (exploration/story-gated, same as any other ability unlock). Free to grow to
  cover the whole game's roster of specials, since nothing iterates it directly at runtime.
- **Active loadout** — a small, player-curated subset of the owned pool, changeable
  outside combat (a menu, a rest point — never mid-fight), capped at a fixed count. This is
  the *only* thing `MotionInputDetector` ever looks at, filtered to entries whose `trigger`
  is a `MotionInputTriggerSO` — other trigger types, if any exist, are irrelevant to it.

This is a deliberate "spellbook" constraint, not an incidental limitation: it keeps the set
`MotionInputDetector` has to track small and cheap regardless of how much content the game
eventually ships, and it turns "two abilities share an input" from a bug into a content
lever — the only real requirement is no collision *within one loadout*, so different
unlockable specials are free to reuse a satisfying motion across the game's whole roster as
long as the player can never have two of them active at once.

#### MotionInputDetector — Real-Time Trie Resolution

Component on the player. Only evaluates when `advancedCombatActive = true`. A trie is built
from the active loadout's `MotionInputTriggerSO`s and walked forward, incrementally, as zone
changes arrive — there is nothing to scan at button-press time, only a current position to
read.

**Every runtime-needed value is baked into the node at build time — the walk never computes
or looks anything up, only reads:**

```csharp
class TrieNode {
    Dictionary<int, TrieNode> children;   // keyed by zone
    EffectSO effectOnComplete;            // null unless this node terminates some ability's sequence
    float    effectiveWindow;             // precomputed, not derived while walking — see below
}
```

**Build.** Rebuilt whenever the active loadout changes, or on load — cheap, since the
loadout is small and changes rarely, never per-frame. Patterns sharing a prefix (the same
first N steps) share the same trie nodes. Walking/creating a path per `MotionInputTriggerSO`'s
sequence, the final node of that path gets `effectOnComplete` set directly to the paired
`TriggerEffectSO.effect` — no side list, no lookup table, the association lives on the node
itself. `effectiveWindow` on every node along the way is set to the *longest* `sequenceWindow`
among every ability whose sequence passes through that node, so a shared prefix never
short-changes a pattern with a longer configured window than a sibling sharing that prefix.

**Walk.** State is just a current node pointer plus one timestamp (`attemptStartTime`) — no
history buffer, no per-pattern bookkeeping. On each zone change:

```
1. If an attempt is in progress and Time.time - attemptStartTime > currentNode.effectiveWindow:
     reset to root (attempt abandoned — too slow, start over)
2. If the new zone matches one of the current node's children:
     advance to that child
     if this was the first step away from root, set attemptStartTime = Time.time
     if the new node's effectOnComplete is non-null:
       effectOnComplete.Execute(caster)
       reset to root
3. Else (zone doesn't match any child from here):
     stay at the current node — not a reset
```

**On button press:** check whether `effectOnComplete` at the current node is non-null and
its `TriggerEffectSO`'s `confirmButton` matches this press. No match → the press falls
through to `ComboSystem` as a normal attack. Players who haven't unlocked `ADVANCED_COMBAT`,
aren't holding the button, or have nothing in their loadout that matches, experience zero
difference from the base combat system.

**"Stay put, don't reset" on a mismatch reproduces the originally-intended skip-mode
tolerance.** Walking `4→2→4→6` against `BBF` (`[4,1,7]→[4,1,7]→[6,3,9]`): zone 4 advances to
step 1; zone 2 matches nothing at step 2, so the walker just stays there; zone 4 advances to
step 2; zone 6 completes step 3. Same result as scanning with skip-mode tolerance, with
nothing actually scanned — only the timeout can cancel an attempt.

**A shared prefix needs no tie-breaking.** Nothing waits to see whether a deeper node is
coming, so a shared node either fires (it's a completion node) or it doesn't (the walk
continues) — there is never more than one live candidate to track. If two patterns in the
same loadout share a prefix and one completes before the other, the shorter one just fires —
matching how real fighting-game input parsers behave (a Dragon Punch motion can "steal" a
longer special sharing its opening frames), not something requiring disambiguation. Where a
shared node has multiple still-reachable patterns with different `sequenceWindow` values,
the longest of those windows governs at that node — no pattern is short-changed by a
stricter sibling sharing its prefix.

**Timeout is for the whole attempt, not per step, and stays brief.** `attemptStartTime` is
set once, on the first successful step away from root, and never refreshed by later
successful steps — the entire sequence must land inside one short window regardless of step
count. A per-step reset would only bound individual gaps, letting a slow, deliberate player
stretch a long sequence (`HCB`'s 5 steps, say) out arbitrarily; one brief whole-attempt
window is what actually makes idly wandering the stick infeasible, not just discouraged.

**Charge steps are exempt from the brief-window clock during the hold itself.** A charge
requirement (`minHoldDuration`) can be held as long as the player likes — the clock for the
*following* transition steps (the release/flick portion) only starts once the hold
requirement is first satisfied, matching how charge motions actually feel in the genre: hold
as long as you want, but the release has to be brisk. Charge accumulation needs no history
either — an accumulated-hold-time counter plus a last-zone-entry timestamp, updated
incrementally as zone changes arrive, tolerates brief drift between the charge group's zones
the same way the originally-drafted buffer-summing approach did.

#### Projectile System

Carrier for any traveling special (a fireball fired via `SpawnProjectileEffectSO`, most
immediately). Every existing melee attack's hitbox is a permanent child of its attacker,
keyframed on that attacker's own clip — a projectile is a detached, independently-moving
GameObject instead, so it needs its own small set of pieces rather than reusing that model.

**`ProjectileController`** — lives on a per-type prefab (`Fireball.prefab`, etc.), not a
shared prefab with swapped data, since the visual/animation genuinely differs per projectile
type the way it doesn't for a swappable `HitboxDataSO`. Carries its own `HitboxController` —
fully self-contained, so the existing attacker-driven `OnZoneHit`/`LateFixedUpdate`/
`DamageCalculator.Resolve` chain (§3n) works completely unchanged; a projectile is just an
"attacker" that also moves and dies on its own. Damage stays on the same trimmed
`HitboxDataSO` pipeline every melee attack already uses (revisit once `AbilityExecutionContext`
actually flows through projectiles too), assigned to the instance's `HitboxController` at
spawn time exactly like `AttackState.Enter()` already does for melee — not baked into the
prefab, since the same visual/prefab could plausibly back more than one tuning of the same
special later.

```csharp
float                    speed;
float                    maxRange;             // 0 = unbounded; see room-exit note below
bool                     despawnOnTerrainHit;  // default false — passes through terrain
ProjectileHitBehaviorSO  onHitBehavior;        // null = default to despawn on first contact
```

**On-hit behavior is pluggable, same shape as everything else composed in this design:**

```csharp
public abstract class ProjectileHitBehaviorSO : ScriptableObject {
    public abstract void OnHit(ProjectileController projectile, HurtboxController target);
}
```

- `null` on `onHitBehavior` *is* the default — despawn after the first contact. No explicit
  "despawn" subclass is needed for the common case.
- `PierceHitBehaviorSO` — doesn't despawn on hit. Needs no extra "already hit" tracking:
  `HitboxController`'s existing dedup already relies on Unity firing `OnTriggerEnter2D` only
  once per continuous overlap, so a target already hit can't re-fire while still touching it,
  and a still-moving, non-despawned projectile is naturally free to hit new targets as it
  continues.
- `SpawnOnHitBehaviorSO` — spawns one or more other prefabs at the impact point (an
  explosion, a 3-way split into smaller projectiles) instead of despawning cleanly.
  References a `ProjectileMultiSpawnSO` to describe *what* and at *what angles*, kept
  separate so the same spread shape (e.g. an even 3-way fan) is one reusable asset instead
  of duplicated fields on every behavior that wants that same fan. An "explosion" is just a
  projectile with `speed = 0` and a one-shot hitbox — no separate class needed for that case.

**Terrain is a flag, not a pluggable behavior** — unlike on-hit, there's no real variety to
compose over (despawn or don't), so `despawnOnTerrainHit` is a plain bool. The collider stays
on one physics layer that always overlaps terrain regardless of the flag's value; the flag
just gates what the callback does with that contact, so every projectile shares one physics
setup and an ignored contact costs nothing.

**Lifetime, in priority order:** hits something (governed by `onHitBehavior`) → travels
`maxRange` (a short-range special like a point-blank fireball that fizzles a few feet out;
distance-based and speed-independent, unlike a timer) → **TEMPORARY: exceeds a flat
distance/lifetime safety net if `maxRange == 0` (unbounded).** The real intent for the
unbounded case is "despawn on leaving the room," not "despawn near the screen edge" —
camera-relative culling visibly kills projectiles right in front of off-screen enemies, which
reads as a bug to a player, not a design choice. There is no room-bounds concept in the
codebase yet (`CameraManager`/Camera System, §3t, isn't built — see the TODO section);
**replace this temporary safety net with a real room-bounds check once that exists.**
Unbounded-range projectiles are expected to be the exception, not the default, so this gap
is low-stakes in practice.

**Spawning.** `MotionInputDetector` never calls `Instantiate` itself — a matched
`MotionInputTriggerSO` fires `TriggerEffectSO.effect.Execute(caster)`, and a
`SpawnProjectileEffectSO`'s `Execute` is what actually requests a projectile. All spawning
and despawning funnels through one entry point, `ProjectileManager` (a scene-owned component,
not a static utility — the moment pooling is added it needs real state, a pool per prefab,
the same reason `EquipmentManager`/`AuraManager`/`WorldStateManager` are components rather
than static classes):

```csharp
GameObject Spawn(GameObject prefab, Vector2 position, Vector2 direction, HitboxDataSO hitboxData);
void       Despawn(ProjectileController instance);
```

**Plain `Instantiate`/`Destroy` for now, not pooling — a deliberate choice, not an
oversight.** Pooling earns its complexity (careful reset-on-reuse for every field that could
carry state between "lives" — position, velocity, which targets a piercing projectile has
already hit) once spawn volume is high enough for GC pressure to actually matter, which is
bullet-hell territory. A player manually inputting a motion-plus-button combo per special is
nowhere near that throughput. Funneling every spawn/despawn through `ProjectileManager` means
swapping in `UnityEngine.Pool.ObjectPool<T>` later, if profiling ever actually asks for it,
is a contained change inside that one component rather than a rewrite.

#### Passive Unlocks

Deliberately separate from the active skill loadout above — there is no input-collision
concern for a passive, so there's no reason to bound how many a player can have active at once.

No dedicated `PassiveUnlockSO` class — turned out to be redundant the same way `AbilitySO`
was (§3l, earlier). A world-unlocked passive ("+1 wall jump charge" from learning a technique,
as opposed to from a ring) is just a `NumericModifierSO` or `AbilityModifierSO` instance (§3j),
identical in every way to an equipment-granted one — the only difference is which event feeds
it into `StatSheet`'s `ModifierAggregate`: an unlock event instead of an equip event. Reusing
the exact same `ContributeTo`/`ModifierAggregate` machinery means a passive-granting unlock and
an equipment-granting item are authored identically, and both recompute on their respective
change event, never per-frame — same discipline, same types, one fewer parallel system to keep
in sync.

Deliberately **not** unifying *this* with `WorldStateManager.unlockedAbilities` (the
`HashSet<string>` backing world-traversal ability gates like `WALL_JUMP`/`DOUBLE_JUMP`, see
§4 Ability Gates) — that stays a fundamentally different shape of problem, an occasional
boolean query rather than an aggregate recomputed from a list, and forcing both into one
mechanism isn't worth the coupling.

---

### 3m. Knockdown State (DOWN)

`DOWN` is a locomotion state entered when a hit resolves with `onHitTargetState = Down`.
It replaces HURT entirely for the duration — the character is floored, not just reeling.

#### State Flow

```
Hit with onHitTargetState = Down
  → DOWN (floor tumble / slide animation plays, entity cannot act)
    ├─ Tech input detected (any direction + jump during DOWN)
    │    → DOWN_RECOVERY (brief neutral get-up stance, shorter than full recovery)
    └─ No tech input → DOWN_RECOVERY (full get-up animation on timer)
         → IDLE (resumes normal locomotion)
```

#### DOWN State Behavior

- Entity is completely invulnerable to **normal** hits while downed — attacks with
  `targetRequirement` that does not include `Downed` whiff even if hitboxes overlap
- OTG attacks explicitly include `Downed` in their `targetRequirement` — they hit on the
  ground but whiff against standing/airborne targets
- Blocking, parrying, and dodging are unavailable during DOWN and DOWN_RECOVERY
- Stagger bar does not decay during DOWN (enemy is already maximally vulnerable)

#### Per-Entity Tuning

DOWN duration and tech window are configurable per-entity, not per-attack:

```csharp
// On PlayerDataSO / EnemyDataSO:
float downDuration          // total time on the floor before forced DOWN_RECOVERY begins
float techWindowStart       // earliest frame a tech input is accepted (prevents mashing out instantly)
float techWindowEnd         // last frame a tech is accepted (= downDuration; missed = full recovery)
float downRecoveryDuration  // get-up animation length (tech = shortened version)

// On PlayerDataSO only — starting progression (used by WorldStateManager.NewGame())
int  startLevel         // e.g. 1
int  startStrength      // e.g. 5
int  startChi           // e.g. 5
int  startDexterity     // e.g. 5
int  startConstitution  // e.g. 5

// On PlayerDataSO only — dialogue
Sprite defaultPlayerPortrait    // fallback portrait used in dialogue panel when the active
                                // CosmeticOptionSO has no portraitSprite authored
```

The attack's `knockbackVector` determines slide distance and direction while downed —
a sweep sends the target sliding backward, a launcher with Down result goes straight down.

#### Distinction from STAGGERED

| | STAGGERED | DOWN |
|---|---|---|
| Source | Stagger bar fills | Attack `onHitTargetState = Down` |
| Posture | Upright reel animation | Floor tumble/slide |
| Interrupt | Hitboxes deactivate immediately | Invulnerable to non-OTG hits |
| Exit | Timer (staggeredDuration) | Timer or player tech input |
| Can be hit | Yes — bonus damage window | Only by OTG-flagged attacks |

**HurtboxController and physics layers are unchanged during DOWN.** The body hurtbox remains
active — contacts still route to `HurtboxController` and would reach `DamageCalculator` normally.
The only gate is the whiff check: a non-OTG attack's `targetRequirement` does not include `Downed`,
so `DamageCalculator.Resolve` returns early before any damage or state change. No collider
toggling, no new physics layer, and no `HurtboxController` changes are required.

---

### 3n. Hitbox / Hurtbox Runtime Pipeline

#### Authoring

Each actor carries **two hitbox child GameObjects** (primary and secondary) and **one body hurtbox**
with optional child hurtboxes for specific zones:

```
Actor (root)
├── Hitbox_Primary     — BoxCollider2D (trigger), starts disabled, Physics layer: [Actor]Hitbox
├── Hitbox_Secondary   — BoxCollider2D (trigger), starts disabled, Physics layer: [Actor]Hitbox
├── Hurtbox_Body       — BoxCollider2D (trigger), always active,   Physics layer: [Actor]Hurtbox  ← primary body collider
│   ├── Hurtbox_Head   — BoxCollider2D (trigger), optional child,  Physics layer: [Actor]Hurtbox
│   ├── Hurtbox_Block  — BoxCollider2D (trigger), optional child,  Physics layer: [Actor]Hurtbox
│   └── Hurtbox_Low    — BoxCollider2D (trigger), optional child,  Physics layer: [Actor]Hurtbox
├── Hurtbox_Pierce     — BoxCollider2D (trigger), NEVER disabled,  Physics layer: [Actor]HurtboxPierce
└── HitboxEventRelay   — MonoBehaviour, receives animation events, routes to HitboxController
```

Most attacks use only `Hitbox_Primary`. `Hitbox_Secondary` is available for attacks with an unusual
shape or reach (e.g. a wide sweep while the primary covers the forward strike zone).

**`Hurtbox_Head`** — covers the head region. Always active. Attacks that care about headshots opt
in via `HitboxDataSO` modifiers; attacks that don't care ignore the zone entirely with no extra logic.

**`Hurtbox_Block`** — the physical shield or guard zone. **Only enabled during BLOCKING/GUARD state.**
When enabled, `Hurtbox_Body` simultaneously **expands** to physically encompass the block child
collider — guaranteeing that any contact with the shield also contacts the parent body in the same
physics step. This is the mechanism that makes the parent the sole trigger for resolution (see below).
Only `canBlock = true` actors carry this child; non-blocking enemies are never subject to block reduction.

**`Hurtbox_Low`** — covers a low/leg region for actors that want a visibly different reaction to a
low-height hit (e.g. a struck-low pose in response to a sweep or crouch-kick). Optional, same as
`Hurtbox_Head`: most actors skip it, and attacks that aren't aimed low simply never touch it. Sets
`isLowHit = true` only — like `Hurtbox_Head`, it never triggers resolution itself, `Hurtbox_Body`
does (see Damage Resolution below). There's no damage or stagger modifier tied to it today, just an
`isLowHit` flag alongside the normal hit, read by whatever picks the struck pose.

**Current implementation status**: the real `HurtboxZoneForwarder` / `OnZoneHit(zoneType, other)` /
`HitboxController.LateFixedUpdate` pipeline described above is built, but trimmed to `Body` and
`Low` zones only — `Hurtbox_Head`/`Hurtbox_Block`, `DamageCalculator`'s block/head/stagger
modifiers, `ICombatStateProvider`, and `blockResult` don't exist yet. Each zone (`Hurtbox`/
`Hurtbox_Body` and, where present, `Hurtbox_Low`) carries its own `HurtboxZoneForwarder` component
tied to that zone's own collider, so a hit touching multiple zones resolves unambiguously per zone
instead of collapsing into one shared `OnTriggerEnter2D`. Resolution itself is **attacker-driven**,
not defender-driven: a forwarder's `OnTriggerEnter2D` calls `HitboxController.OnZoneHit(zoneType,
target)` on the *attacker's* hitbox, which records per-target Body/Low contact and resolves each
target independently in `LateFixedUpdate` (implemented via Unity's real `LateUpdate`, since no
native `LateFixedUpdate` message exists) — this is what makes "multiple simultaneous attackers"
and "one attack hitting multiple targets" both fall out for free, with no shared inbox on either
side to overwrite. `HurtboxController` itself is minimal today: just the `hurtboxCollider`
reference and `SetInvulnerable`'s reference-counted i-frame gate — it has no `OnTriggerEnter2D`,
no pending-hit state, and no zone-metadata fields, since `Low`'s flag lives on the attacker's
`HitboxController` instead. `DamageCalculator.Resolve(HitboxDataSO, HurtboxController target, bool
isLowHit, object attackerStats = null)` applies `Health.TakeDamage` directly and publishes
`OnEntityDamaged` (target, damage, isLowHit) via `EventBus` — `attackerStats` is an unused,
null-checked stub seam for the future StatSheet system. Swap in the fuller pipeline (block, head,
stagger, `ICombatStateProvider`) once `Hurtbox_Head`/`Hurtbox_Block` are actually built.

#### Hurtbox Pose Matching

`Hurtbox_Body` is **not a static collider**. It must represent the character's actual hittable
silhouette at each moment during an attack or ability — not a fixed standing-idle approximation
baked onto the prefab.

Two failure modes this prevents:
- **Ghost hit**: the default hurtbox covers space the character has vacated (e.g. a leaping kick
  where the feet are now high in the air — the default ground-level box still registers hits at
  shin height where the character no longer is).
- **Phantom immunity**: the character's pose extends beyond the default hurtbox (e.g. a wide arm
  sweep where the extended arm is exposed but the narrow standing box doesn't cover it).

**Animators own the hurtbox directly.** `HurtboxController` exposes offset and size as serialized
fields, making them keyframeable from Unity's Animation window like any other property. Animators
shape the hurtbox in the same tool where they author the sprite — no programmer coordination, no
preset list to maintain:

```csharp
// On HurtboxController — keyframed directly in the Animation window
[SerializeField] public Vector2 hurtboxOffset;   // applied to Hurtbox_Body each FixedUpdate
[SerializeField] public Vector2 hurtboxSize;
```

`HurtboxController` reads these values and applies them to the collider each `FixedUpdate`.
Gameplay data (hurtbox shape) lives in the animation clip rather than purely in the Inspector —
an acceptable coupling for a small team, and appropriate since animators have the most context for
where the character is actually hittable on any given frame.

**Reset convention**: every clip keyframes the default standing values on frame 0. Clips that never
need to reshape the hurtbox do it once on frame 0 and leave it. The idle and locomotion clips
establish the canonical default. Only clips with poses that meaningfully expose or vacate significant
body area need additional keyframes — jabs, light kicks, and other standing-silhouette attacks
require nothing beyond the frame 0 default.

**Non-upright poses (DOWN, crouch, airborne, etc.).** When an entity enters a locomotion state
with a distinct silhouette, the relevant animation clip keyframes new `hurtboxOffset` /
`hurtboxSize` values on `HurtboxController`. The system makes no assumptions about pose shape —
a floor-sliding player, a boss taking a knee, an airborne kick — any posture is handled by
animators authoring appropriate values for that clip. No pose-specific code or presets are
needed; the clip is the source of truth.

**Compound shapes**: `Hurtbox_Body` is the *primary* body collider, not the only one the
architecture permits. Because `HurtboxController` owns resolution rather than any individual
collider, adding a secondary body collider (e.g. a separate wide-short box for the legs during a
split kick) is additive — a new child collider routes through the same resolution path, and
animators keyframe a second offset/size pair on `HurtboxController` independently. No structural
changes to the current design are needed to support this later.

A facing dot product alone is too crude for block detection — an attack from a diagonal angle can
pass the dot check even if it reached the body before the shield. Instead, when `Hurtbox_Block`
contact fires, a ray is cast from the **attacker's active hitbox center** toward the **victim's
body center**. If the ray intersects the block collider's boundary before reaching the body center,
the shield was physically in the attack path.

```
attackerHitboxCenter ──────ray──────► victimBodyCenter
                          passes through Hurtbox_Block boundary?
                             YES → shield was in the path → Blocked
                             NO  → attack bypassed the shield → Unblocked
```

```csharp
Vector2      dir  = (victimRoot.position - attackerHitboxCenter).normalized;
float        dist = Vector2.Distance(attackerHitboxCenter, victimRoot.position);
RaycastHit2D hit  = Physics2D.Raycast(attackerHitboxCenter, dir, dist, blockZoneLayer);
// Blocked if the ray hit the block collider specifically (not any other geometry)
bool shieldInPath = hit.collider == blockHurtboxCollider;
```

No direction vector is needed on the attack SO — all data is derived from current positions.

#### Animation → Combat Decoupling (HitboxEventRelay)

Animations have no knowledge of the combat system. Each attack animation fires **Unity Animation
Events** on a single intermediate MonoBehaviour — `HitboxEventRelay` — mounted on the actor root:

```csharp
// Called by Animation Events only — the animator knows these events, nothing else
void OnHitboxActive()                   // enables the hitbox collider — no index; see below
void OnHitboxInactive()                 // disables it
void OnInvulnerableStart()              // i-frame window begins (dodge handled by DodgeSystem directly)
void OnInvulnerableEnd()                // i-frame window ends
```

**Current implementation**: `OnHitboxActive`/`OnHitboxInactive` are parameterless — there's only
one hitbox (`Hitbox`) today, `Hitbox_Secondary` isn't built yet, and reach/shape no longer come
from an index at all. Instead, the `Hitbox` child's `localPosition` and its `BoxCollider2D`'s
`size`/`offset` are keyframed directly in each attack's own AnimationClip (constant across the
clip for attacks with a fixed reach; nothing stops a future clip from varying them, e.g. for a
non-axis-aligned reach an index-based system couldn't express). The player's facing flip moved
from `SpriteRenderer.flipX` to `transform.localScale.x` on the Player root, so the animated Hitbox
transform mirrors automatically along with everything else parented under the root — matching how
NPCs already flip — instead of `HitboxController` re-deriving a sign per pose from `FacingRight`.

`HitboxEventRelay` forwards hitbox calls to `HitboxController` and invulnerability calls to
`HurtboxController`. Hurtbox shape is not driven by animation events — it is keyframed directly
on `HurtboxController` properties from the Animation window (see Hurtbox Pose Matching above; not
yet built — see status note below the Authoring hierarchy diagram).

Step-by-step Animation-window instructions for keyframing an attack's hitbox live in
[`README.md`](./README.md) under Development Workflows, not here — this document stays
architecture/status, not a how-to.

#### HitboxController

Manages the hitbox collider(s) and owns the current `HitboxDataSO` reference for each active swing
(today: just `Hitbox_Primary` — `Hitbox_Secondary` isn't built yet):

```csharp
HitboxDataSO  activeHitboxData    // set by the AttackSO before the animation plays
void Activate()                    // enables the hitbox collider — geometry comes from the clip's own curves
void Deactivate()                  // disables it
void OnZoneHit(HurtboxZoneType zoneType, HurtboxController target)  // called by that target's forwarder
void LateFixedUpdate()              // resolves each target hit this step exactly once; see Damage Resolution
```

When a combat state is entered, the `AttackSO` (or `ComboStep`) pushes its `HitboxDataSO` to
`HitboxController.activeHitboxData` before any animation events fire. The data is already in place
when the trigger fires.

#### HurtboxController

**Current implementation**: none of this section is built yet except `SetInvulnerable` (real,
reference-counted, gating a single `hurtboxCollider` field rather than a whole `hurtboxBodyObject`
— there's no Head/Block child to cascade to today). No `BlockResult`, `isHeadHit`, or
`ICombatStateProvider`; detection/resolution live on the attacker's `HitboxController` instead (see
Authoring status note above and Damage Resolution below).

```csharp
enum BlockResult {
    Pending,    // Hurtbox_Body fired this step; block child hasn't reported yet
    Blocked,    // Hurtbox_Block fired AND ray check confirmed shield was in the attack path
    Unblocked   // Hurtbox_Block didn't fire this step, or ray check failed
}

BlockResult blockResult    // reset to Pending when Hurtbox_Body fires; promoted by Hurtbox_Block
bool        isHeadHit      // set true when Hurtbox_Head fires; cleared after resolution

// I-frame control — reference-counted so overlapping sources don't interfere
int  invulnerabilityCount = 0
void SetInvulnerable(bool active):
    invulnerabilityCount += active ? 1 : -1
    hurtboxBodyObject.SetActive(invulnerabilityCount == 0)
    // Hurtbox_Head and Hurtbox_Block cascade automatically as children of hurtboxBodyObject
    // Hurtbox_Pierce is NOT a child — it is never touched by SetInvulnerable
```

`HurtboxController` holds an `ICombatStateProvider` reference (set on `Start` via `GetComponent` on
the actor root) and passes `combatProvider.CurrentCombatState` into `DamageCalculator.Resolve` at
resolution time. `PlayerStateMachine` and `EnemyStateMachine` implement `ICombatStateProvider`.

Same-team contacts are filtered before `DamageCalculator.Resolve` is called: if the attacker and
target share the same team tag and `hitboxData.hitsFriendlies = false`, the contact is silently
dropped. This is the only code-side gate for friendly fire — the `EnemyHitbox ↔ EnemyHurtbox`
physics pair is always enabled in the layer matrix.

Each zone child GameObject carries a lightweight `HurtboxZoneForwarder` component that
captures `OnTriggerEnter2D` and routes it to `HurtboxController.OnZoneHit(zoneType, other)`.
This is necessary because Unity fires trigger callbacks on the Rigidbody2D owner's scripts,
not on individual child colliders — the forwarder is the wiring, not logic.

`Hurtbox_Body` contact (`zoneType = Body`) — **the sole trigger for damage resolution**. Sets
`blockResult = Pending` and records the contact for `LateFixedUpdate`.

`Hurtbox_Head` contact (`zoneType = Head`) — sets `isHeadHit = true` only. Never triggers resolution.

`Hurtbox_Block` contact (`zoneType = Block`) — runs the ray check and promotes `blockResult` to
`Blocked` or `Unblocked`. Never triggers resolution. Because `Hurtbox_Body` is expanded to
encompass the block child during BLOCKING, the body contact always arrives in the same
physics step — `LateFixedUpdate` reads a fully-resolved `blockResult` every time.

`Hurtbox_Pierce` contact (`zoneType = Pierce`) — routes to resolution identically to Body.
Only reachable by hitboxes on the pierce physics layer (see Physics Layer Matrix). Exists
solely so `piercesDodgeIFrames = true` attacks still land when the body GameObject is inactive.

#### I-Frames

I-frames are implemented by deactivating `hurtboxBodyObject` via `SetInvulnerable`. With the
GameObject off, no `OnTriggerEnter2D` callbacks fire — attacks physically whiff with no routing
to `DamageCalculator` and no flags to check anywhere.

**Who calls `SetInvulnerable`:**

- **Dodge system** — calls `SetInvulnerable(true)` at `iFrameStartFrame` and
  `SetInvulnerable(false)` at `iFrameEndFrame`, both defined on `DodgeSO`
- **Attack-embedded i-frames** — animator fires `OnInvulnerableStart` / `OnInvulnerableEnd`
  events on `HitboxEventRelay`; relay forwards to `SetInvulnerable`. Used for moves where
  startup or active frames carry invulnerability by design (e.g. a reversal or dive kick).
  Attacks with no embedded i-frames never fire these events — zero overhead.
- **DOWN_RECOVERY** — get-up animation fires `OnInvulnerableStart` on frame 0 of DOWN_RECOVERY
  and `OnInvulnerableEnd` a few frames later, preventing immediate re-combo off a knockdown.
  Duration is authored per-actor in the animation clip.
- **HURT state — player only** — calls `SetInvulnerable(true)` on entering `HURT`,
  `SetInvulnerable(false)` on exiting it. Enemies do **not** get this: enemy `HURT` stays fully
  hittable so a multi-hit player attack (punch-punch-kick, knee-to-kick) lands every hit in
  sequence instead of bouncing off invincibility after the first. For the player, this is the
  juggle-lock fix — a crowd of attackers caps out at one hit landing before the reel ends, instead
  of chain-stunning into a loss of control. Two attackers landing in the same physics step, before
  the gate can react, is an accepted rare edge case rather than something the resolution model
  dedups for. Implemented as an opt-in behavior on the player (not a hardcoded type check), so an
  allied NPC could get the same protection later if one is ever added.

**Reference counting contract:** each `SetInvulnerable(true)` call must be paired with exactly
one `SetInvulnerable(false)`. The body object re-activates only when the count reaches zero —
two overlapping i-frame sources don't cancel each other early.

**`piercesDodgeIFrames`:** attacks flagged with this field on `HitboxDataSO` collide with
`Hurtbox_Pierce` (always active, separate physics layer) rather than `Hurtbox_Body`. They reach
`HurtboxController` via the pierce path and resolve normally. Physics layer pairing for pierce
attacks is specified in the Physics Layer Matrix (Priority 1 TODO).

#### Damage Resolution

```
HitboxController.LateFixedUpdate — runs when Hurtbox_Body contact was recorded this step:
  1. Build damage context from activeHitboxData + attacker StatSheet

  2. Block resolution (blockResult set by Hurtbox_Block in same step, or Pending if not hit):
       Pending or Unblocked:
         → full damage, full stagger
       Blocked, isUnblockable = false:
         → RawDamage = full * blockDamagePercent, reduced stagger
       Blocked, isUnblockable = true:
         → full damage + guard break animation

  3. Head modifiers (isHeadHit — fully opt-in per attack):
       if isHeadHit AND attack has non-default head modifiers:
         ctx.baseMultiplier     *= headHitDamageMultiplier
         ctx.staggerDamage      *= headHitStaggerMultiplier
         ctx.onHitAuras.AddRange(headHitAuras)    // e.g. StunAuraSO
       (default values 1.0 / 1.0 / empty — head zone contact has zero effect)

  4. DamageCalculator.Resolve(ctx, attackerStats, target.GetComponent<StatSheet>())
       → applies final damage to target health
       → applies stagger to target StaggerMeter
       → publishes OnEntityDamaged / OnEntityDowned / etc. via EventBus
         (UI, audio, and hurt-state transitions subscribe independently)

  5. Reset blockResult and isHeadHit; clear pending contact flag
```

**Current implementation**: steps 2–3 (block, head modifiers) and the StatSheet/armor/stagger
parts of step 4 aren't built yet — today's `LateFixedUpdate` only has Body/Low. It walks every
target that got a Body contact this step, and for each calls `DamageCalculator.Resolve(
activeHitboxData, target, isLowHit, attackerStats: null)` where `isLowHit` is just whether that
same target also got a Low contact this step. `Resolve` applies `activeHitboxData.damage` straight
to `target.GetComponent<Health>().TakeDamage(...)` and publishes `OnEntityDamaged { Target, Damage,
IsLowHit }` — no `ctx`/multipliers/stagger/StatSheet yet, those are the seam `attackerStats` (always
null, unused) leaves open for later.

`DamageCalculator` applies damage directly and publishes results via EventBus. No response object
is returned — EventBus subscribers handle state reactions without coupling to the calculator.

#### Single-Hit Guarantee and the Flowing Attack Case

Each hitbox **activation cycle** (Activate → triggers fire → Deactivate) produces at most one
`DamageCalculator.Resolve` call per target. Resolution is triggered only by `Hurtbox_Body`
firing `OnTriggerEnter2D` — which fires **once per entry**, not per frame.

**Flowing attack case** (e.g. a jump kick that enters the head zone and later drifts into the
shield zone during the same activation):

```
Frame N:   Hurtbox_Body.OnTriggerEnter2D fires (hitbox at head level, above shield)
             blockResult = Pending; isHeadHit = true
           Hurtbox_Block not entered (hitbox hasn't reached shield yet)
           LateFixedUpdate: Pending → Unblocked → full damage applied; flags reset

Frame N+M: Hurtbox_Block.OnTriggerEnter2D fires (animation has flowed into shield area)
             → runs ray check, sets blockResult = Blocked or Unblocked
             BUT: Hurtbox_Body was NOT re-entered — hitbox was already inside it from frame N
             → OnTriggerEnter2D does not re-fire; LateFixedUpdate has no body contact → nothing resolves
```

The shield contact on frame N+M is inert. The hit resolved as unblocked on frame N.
Multi-hit attacks use multiple activation cycles (Deactivate then re-Activate between hits),
which re-enables Enter detection for the next target contact cleanly.

#### Head Hit Modifiers on HitboxDataSO

```csharp
// Head hit — defaults = body hit unchanged; set non-defaults to opt in
float        headHitDamageMultiplier    // default 1.0
float        headHitStaggerMultiplier   // default 1.0
List<AuraSO> headHitAuras              // e.g. StunAuraSO; default empty
```

Block resolution requires no per-attack configuration beyond `blockDamagePercent` and
`isUnblockable`, which are already on `HitboxDataSO`. Every attack interacts with the block
zone correctly automatically — the attack data has no knowledge of whether the target has a shield.

---

### 3o. Physics Layer Matrix

#### Layer Definitions

13 custom layers. Unity allows 32 total (0–7 reserved); this leaves 11 slots open for future
systems (projectiles, environmental hazards, etc.).

| Layer | Collider type | Used by |
|---|---|---|
| `PlayerMovement` | Non-trigger | Player movement/platforming collider |
| `EnemyMovement` | Non-trigger | Enemy movement/navigation collider |
| `Environment` | Non-trigger | Static world geometry — floors, walls, platforms |
| `Interactable` | Trigger | Room transitions, ability gates, item pickups, NPC volumes |
| `GrapplePoint` | Trigger | Anchor ring GameObjects — overlap/raycast target only, no collision pairs |
| `PlayerHitbox` | Trigger | Player primary and secondary attack hitboxes |
| `PlayerHitboxPierce` | Trigger | Player pierce-flagged hitboxes (`piercesDodgeIFrames = true`) |
| `EnemyHitbox` | Trigger | Enemy and boss attack hitboxes |
| `EnemyHitboxPierce` | Trigger | Enemy pierce-flagged hitboxes |
| `PlayerHurtbox` | Trigger | `Hurtbox_Body`, `Hurtbox_Head`, `Hurtbox_Block` — routing by collider identity |
| `PlayerHurtboxPierce` | Trigger | `Hurtbox_Pierce` sentinel — never disabled by `SetInvulnerable` |
| `EnemyHurtbox` | Trigger | `Hurtbox_Body`, `Hurtbox_Head`, `Hurtbox_Block` — routing by collider identity |
| `EnemyHurtboxPierce` | Trigger | `Hurtbox_Pierce` sentinel — never disabled |

#### Collision Matrix — Enabled Pairs

All unlisted pairs are **disabled**. Only 8 pairs are active.

| Layer A | Layer B | Purpose |
|---|---|---|
| `PlayerMovement` | `Environment` | Platforming and wall detection |
| `EnemyMovement` | `Environment` | Enemy navigation against world geometry |
| `PlayerMovement` | `Interactable` | Room exits, ability gates, item pickups, NPC triggers |
| `PlayerHitbox` | `EnemyHurtbox` | Player attacks resolve against enemies |
| `PlayerHitboxPierce` | `EnemyHurtboxPierce` | Player pierce attacks against enemy pierce sentinel |
| `EnemyHitbox` | `PlayerHurtbox` | Enemy attacks resolve against player |
| `EnemyHitbox` | `EnemyHurtbox` | Friendly fire — enabled at physics level, gated by `hitsFriendlies` in code |
| `EnemyHitboxPierce` | `PlayerHurtboxPierce` | Enemy pierce attacks bypass player i-frames |

#### Design Notes

**Self-hit prevention** is enforced at the physics level: `PlayerHitbox` never collides with
`PlayerHurtbox`. No code check required.

**Friendly fire** is enabled at the physics level (`EnemyHitbox ↔ EnemyHurtbox`) and gated
per-attack in code via `hitsFriendlies` on `HitboxDataSO`. Boss AOE/sweep attacks set
`hitsFriendlies = true`; standard attacks leave it false and the contact is silently dropped
by `HurtboxController` before reaching `DamageCalculator`.

**Exclusive pierce path**: `EnemyHitboxPierce` collides only with `PlayerHurtboxPierce` — not
with `PlayerHurtbox`. Pierce attacks route exclusively through the always-active `Hurtbox_Pierce`
sentinel. A regular and a pierce attack against the same player can never double-resolve.

**Zone merge**: `Hurtbox_Head` and `Hurtbox_Block` share the `[Actor]Hurtbox` layer with
`Hurtbox_Body`. Distinguishing body vs zone contact is handled entirely by `HurtboxZoneForwarder`
reading the collider reference — physics layer plays no role in routing.

**GrapplePoint** has no collision pairs. `GrappleAbility` uses `Physics2D.OverlapCircle` with
the `GrapplePoint` layer mask to find candidates, then selects the nearest within the aim
tolerance cone. The grapple attack upgrade routes through the existing hitbox/damage pipeline —
`GrapplePoint` layer never participates in combat collisions.

**PlayerMovement / EnemyMovement** do not collide with each other. Enemy spacing relative to
the player is managed by AI behavior, not physics.

---

### 3p. Equipment System

#### SlotType and Rarity

```csharp
enum SlotType { Weapon, Armor, Ring1, Ring2 }
// Ring1 and Ring2 are interchangeable — any ring item fits either slot.
// Adding new slot types (e.g. Head, Body, Legs) requires only: new enum values + UI slots.

enum Rarity { Common, Magic, Rare, Legendary }
// Common  (white) — 0 affixes, proc-gen, mostly low-level drops
// Magic   (blue)  — 1 affix, proc-gen
// Rare    (yellow)— 2–3 affixes, proc-gen, mostly high-level drops
// Legendary       — authored; specific enemy farm targets + random drop tables (no affix rolling)
// Drop probability by player level is configured in LootTableSO (see Priority 3 TODO).
```

---

#### ItemTemplateSO

ScriptableObject. One asset per base item type (e.g. "Iron Sword", "Silk Sash"). Defines the
blueprint for proc-gen drops — legendaries use `LegendaryItemSO` instead.

```csharp
// Identity
string       baseItemId       // unique key (e.g. "iron_sword")
string       displayName      // e.g. "Iron Sword"
Sprite       icon
string       flavourText      // short lore line shown in tooltip

// Slot & requirements
SlotType     slotType
int          minLevel         // player must be >= this level to equip

// Base stats (always granted — applied before any affix contributions)
StatBonus    baseStats

// Base ability modifiers (always granted — authored, not rolled)
// Stored as pairs so one template can modify multiple abilities
List<(AbilityEffectSO effect, AbilityModifierSO modifier)> baseAbilityModifiers

// Proc-gen affix pool (used for Magic and Rare drops from this template)
// Common drops draw 0 affixes; Legendary items ignore this list entirely
List<AffixSO> affixPool
```

---

#### LegendaryItemSO

ScriptableObject. One asset per unique named item. Referenced directly in targeted loot tables
(specific enemy farm sources) and as entries in the random legendary drop pool.

```csharp
string       legendaryId       // unique key (e.g. "dragons_tooth")
string       legendaryName     // unique item name shown to player
string       flavourText       // longer lore text (legendaries get more flavour)
Sprite       icon
SlotType     slotType
int          minLevel

// Fully authored — no affix rolling
StatBonus    stats
List<(AbilityEffectSO effect, AbilityModifierSO modifier)> abilityModifiers
```

---

#### AffixSO

ScriptableObject. One asset per affix type (e.g. "Weapon Damage", "Max Health",
"Flying Kick Bonus"). An affix is either a stat bonus or an ability modifier — not both.

```csharp
string   affixId
string   displayTemplate     // e.g. "+{value} Weapon Damage" — {value} replaced in UI tooltip

// Stat affix (set statType; leave effect + modifier null)
AffixStatType statType       // enum: WeaponDamage, Armor, MaxHealth, MaxChiPool,
                             //       Strength, Chi, Dexterity, Constitution,
                             //       MovementSpeed, AttackSpeed, DetectionRange
float    minValue
float    maxValue
float    levelScaleFactor    // final = lerp(min, max, t) + (itemLevel * levelScaleFactor)

// Ability modifier affix (set effect + modifier; leave statType = None)
AbilityEffectSO   effect     // which ability this affix modifies (null = stat affix)
AbilityModifierSO modifier   // the modifier instance to contribute (null = stat affix)

// Affix availability and weighting
bool     availableOnMagic    // Common gets 0 affixes; eligibility starts at Magic
bool     availableOnRare
float    weight              // used for weighted random selection within the pool
```

---

#### AffixInstance

Plain C# class. Stored on `ItemData` for tooltip display — records exactly what was rolled.

```csharp
string  affixId
float   rolledValue     // resolved value after lerp + level scaling; 0 for ability modifier affixes
```

---

#### ItemData

Plain C# class (not a ScriptableObject). Created at runtime by `ItemGenerator`. This is the
type `EquipmentManager` works with — replaces all prior "EquipmentSO" placeholder references.

```csharp
// Template reference (for save serialization and re-identification)
string       templateId         // references ItemTemplateSO.baseItemId or LegendaryItemSO.legendaryId
bool         isLegendary

// Display (copied from template at generation time)
string       displayName
Sprite       icon
string       flavourText

// Properties
SlotType     slotType
Rarity       rarity
int          itemLevel          // player level at drop time — used for level scaling display
int          minLevel           // copied from template; checked on equip attempt

// Resolved stats (base stats + all affix contributions, computed once at generation time)
StatBonus    stats

// Resolved ability modifiers (base + affix-contributed, collected at generation time)
List<(AbilityEffectSO effect, AbilityModifierSO modifier)> abilityModifiers

// Affix display (for tooltip UI)
List<AffixInstance> rolledAffixes     // empty for Common and Legendary items
```

---

#### Item Generation Flow

`ItemGenerator` is a static utility class called by the loot system when an item drops.

```
LootTableSO determines:
  - rarity (weighted by player level — see Priority 3 TODO)
  - which ItemTemplateSO (or LegendaryItemSO) to use

ItemGenerator.Generate(template, rarity, playerLevel):

  IF legendary:
    → copy all fields from LegendaryItemSO directly into ItemData (no rolling)
    → return ItemData

  ELSE (Common / Magic / Rare):
    1. Copy baseStats from ItemTemplateSO into ItemData.stats (mutable copy)
       Copy baseAbilityModifiers into ItemData.abilityModifiers
    2. Determine affix count:
         Common → 0
         Magic  → 1
         Rare   → Random.Range(2, 4)   // 2 or 3
    3. Filter affixPool by rarity eligibility (availableOnMagic / availableOnRare)
    4. WeightedRandom selection without replacement for N affixes
    5. For each selected AffixSO:
         IF stat affix:
           rolledValue = lerp(minValue, maxValue, Random.value) + (playerLevel * levelScaleFactor)
           → add rolledValue to the matching field in ItemData.stats
           → record AffixInstance(affixId, rolledValue) in rolledAffixes
         IF ability modifier affix:
           → add (effect, modifier) to ItemData.abilityModifiers
           → record AffixInstance(affixId, 0) in rolledAffixes
    6. Return ItemData
```

---

#### EquipmentManager (updated field types)

`EquipmentManager` was previously designed with a placeholder `EquipmentSO` type; all
references update to `ItemData`. Logic and registry structure are unchanged.

```csharp
// Currently equipped items — one ItemData per slot
Dictionary<SlotType, ItemData> equippedItems

void Equip(ItemData item, SlotType slot)
    // 1. Reject if item.minLevel > player.StatSheet.level
    // 2. If slot occupied: UnregisterModifiers(equippedItems[slot])
    // 3. equippedItems[slot] = item
    // 4. RegisterModifiers(item)
    // 5. StatSheet.RefreshEquipmentBonuses()

void Unequip(SlotType slot)
    // 1. UnregisterModifiers(equippedItems[slot])
    // 2. equippedItems.Remove(slot)
    // 3. StatSheet.RefreshEquipmentBonuses()

void RegisterModifiers(ItemData item)
    // iterates item.abilityModifiers; adds each to modifiersByEffect[effect]
    // then calls RecalculateAggregate() to rebuild StatSheet's ModifierAggregate (§3i/§3j) —
    // NumericModifierSO contributions (charges, elemental, ability-damage) included, not just
    // the qualitative AbilityModifierSO subclasses

void UnregisterModifiers(ItemData item)
    // removes item's contributions from modifiersByEffect, then RecalculateAggregate()
```

`StatSheet.RefreshEquipmentBonuses()` clears `equipmentBonuses`, iterates all `equippedItems`
values, and accumulates each `item.stats` field-by-field. `detectionRangeMultiplier` multiplies
rather than adds (start at 1.0, multiply each item's value in). All other fields are additive.
This is separate from `RecalculateAggregate()` above — `StatBonus` (primary/derived stats) and
`ModifierAggregate` (charges/elemental/ability-damage) are two distinct aggregates on the same
`StatSheet`, recomputed by two distinct passes, per §3i.

`BuildContext` and the `modifiersByEffect` registry are unchanged from the existing design in
Section 3j.

---

### 3q. Death & Respawn System

#### Overview

Death and respawn is orchestrated across five components, each owning a clear slice of the flow:

| Component | Responsibility |
|---|---|
| `PlayerDeathTrigger` | Detects HP = 0, enters `PLAYER_DEATH` state, fires `OnPlayerDeath` |
| `CinematicDeathDirector` | Drives slow-mo / vignette / zoom / fade-to-black sequence |
| `DeathScreenUI` | Presents death screen and retry options to the player |
| `RespawnManager` | Owns state restoration and scene/position transition on respawn |
| `CheckpointController` | Physical shrine — activation, save trigger, respawn point registration |

---

#### PlayerDeathTrigger

MonoBehaviour on the player root. Subscribes to `OnEntityDamaged` via EventBus. After each
damage event, checks `StatSheet.CurrentHealth`:

```
StatSheet.CurrentHealth <= 0 AND NOT already in PLAYER_DEATH:
  → player locomotion state machine → PLAYER_DEATH
  → EventBus.Publish(OnPlayerDeath { killer, deathPosition, diedInBossRoom })
```

`PLAYER_DEATH` suppresses all input. The animator plays the death entry animation (brief
stagger/reel into collapse). The state is held until `RespawnManager.Respawn()` forces a
position reset or scene load.

---

#### CinematicDeathDirector

Singleton MonoBehaviour. Subscribes to `OnPlayerDeath`. Drives the full visual sequence using
`Time.unscaledDeltaTime` throughout so all timing survives `timeScale` changes:

**Step 1 — Slow-mo + isolation vignette** (simultaneous):
- Set `Time.timeScale = deathSlowMoScale` (e.g. 0.15).
- Spawn a full-screen black sprite quad in world space at sorting layer "Cinematic", order 500.
  Animate its alpha 0 → `vignetteDarkness` (e.g. 0.88) over `vignetteRampTime` real seconds.
- Promote player SpriteRenderer(s) and killer SpriteRenderer(s) to sorting order 600+. This
  keeps them visually isolated against the darkened world using only sorting order — no shader
  or camera stack needed. Original sorting orders are restored after respawn.

**Step 2 — Camera zoom** (simultaneous with Step 1):
- Activate a `DeathVirtualCamera` (Cinemachine) that has a tighter orthographic size than the
  gameplay camera, focused on the player. Cinemachine blends smoothly in.

**Step 3 — Hold for collapse animation**:
- Wait `collapseHoldDuration` real seconds — long enough for the death animation to play
  through at slow-mo speed.

**Step 4 — Freeze frame**:
- Set `Time.timeScale = 0`. Hold for `freezeFrameDuration` real seconds.

**Step 5 — Fade to black**:
- Restore `Time.timeScale = 1`. Animate vignette alpha → 1.0 (full black) over `fadeToBlackDuration`.

**Step 6 — Trigger death screen**:
- `EventBus.Publish(OnDeathSequenceComplete { diedInBossRoom })`

All timing fields live on `CinematicDeathDirectorSO` (assigned in scene) for designer tuning:

```csharp
float deathSlowMoScale       // e.g. 0.15
float vignetteDarkness       // 0–1, e.g. 0.88
float vignetteRampTime       // real seconds, e.g. 0.3
float collapseHoldDuration   // real seconds, e.g. 1.8
float freezeFrameDuration    // real seconds, e.g. 0.4
float fadeToBlackDuration    // real seconds, e.g. 0.6
```

---

#### DeathScreenUI

Canvas UI component. Subscribes to `OnDeathSequenceComplete`. On receipt:
- Fades in over ~0.3s real time.
- Shows a configurable death message (e.g. "You have fallen").
- Always shows: **"Continue"** → `RespawnManager.Respawn(retryBoss: false)`.
- Conditionally shows: **"Retry Boss"** (only if `diedInBossRoom = true`)
  → `RespawnManager.Respawn(retryBoss: true)`.

"Retry Boss" teleports directly to `RoomDataSO.bossEntranceSpawnPoint` of the boss room
without changing the saved checkpoint.

---

#### RespawnManager

Singleton MonoBehaviour. Owns `Respawn(bool retryBoss)`:

```
1. Restore player stats
     StatSheet.CurrentHealth  = StatSheet.MaxHealth
     StatSheet.CurrentChiPool = StatSheet.MaxChiPool

2. Clear active auras
     AuraManager.ClearAll()     // new method — strips all timed buffs/debuffs from player

3. Reset non-permanent enemies
     WorldStateManager.ResetEnemies()
     // clears deadEnemies in all RoomState entries
     // does NOT touch worldFlags — boss kills and isUnique enemy kills are permanent

4. Determine respawn target
     IF retryBoss:
       room = WorldStateManager.PlayerPersistentData.lastBossRoomAtDeath
       pos  = RoomDataSO(room).bossEntranceSpawnPoint
     ELSE IF lastCheckpointId != null:
       room = WorldStateManager.PlayerPersistentData.lastCheckpointRoomId
       pos  = WorldStateManager.PlayerPersistentData.lastCheckpointPos
     ELSE (new game — no checkpoint activated yet):
       room = GameStartSO.startRoomSceneName
       pos  = GameStartSO.startSpawnPosition

5. Load room if different from current (Addressables / SceneManager); else skip

6. Reposition player → pos
   Deactivate DeathVirtualCamera → gameplay camera resumes
   Restore player SpriteRenderer sorting orders
   Clear lastBossRoomAtDeath from PlayerPersistentData

7. Fade from black → gameplay view

8. EventBus.Publish(OnPlayerRespawn { respawnPosition })
```

`AuraManager.ClearAll()` is a new method. It is the inverse of the existing
`AuraManager.ApplyAll()` and is only called here.

---

#### GameStartSO

ScriptableObject. One asset in the project, assigned to `RespawnManager` in the Inspector.
Defines the player's very first spawn point before any checkpoint has been activated.

```csharp
string  startRoomSceneName    // scene to load on new game (e.g. "Room_Zone01_Entrance")
Vector2 startSpawnPosition    // world position of the player's initial spawn point
```

`RespawnManager` falls back to `GameStartSO` when `PlayerPersistentData.lastCheckpointId`
is null. After the starting scene loads, `lastCheckpointId` remains null until the player
activates a real checkpoint for the first time.

---

#### CheckpointController

MonoBehaviour on each physical shrine/incense-burner in the scene.

```csharp
string     checkpointId      // unique identifier, set in Inspector
bool       isActive          // runtime — true = this is the current respawn point
GameObject activationVFX
AudioClip  activationSFX     // played via FMOD
```

Player enters the trigger zone and presses the interact button (InputReader). If `isActive`,
do nothing. Otherwise:

```
1. EventBus.Publish(OnCheckpointDeactivated { previousId })
   → other CheckpointControllers hear this and set isActive = false / switch to idle visual
2. isActive = true → switch to active visual + play VFX/SFX
3. Write to WorldStateManager.PlayerPersistentData:
     lastCheckpointId     = checkpointId
     lastCheckpointPos    = transform.position
     lastCheckpointRoomId = current scene name
4. WorldStateManager.Save()
5. EventBus.Publish(OnCheckpointActivated { checkpointId })
```

Checkpoint activation does NOT restore HP or chi — healing is the reward for dying and
returning, not for touching the shrine. On scene load, each `CheckpointController` reads
`WorldStateManager.PlayerPersistentData.lastCheckpointId`; if it matches, set `isActive =
true` and apply the active visual silently (no VFX/SFX replay).

---

#### New EventBus Events

```csharp
OnPlayerDeath {
    GameObject killer          // entity that landed the killing blow; null if environmental
    Vector2    deathPosition
    bool       diedInBossRoom  // true if current RoomDataSO.isBossRoom
}

OnDeathSequenceComplete {
    bool diedInBossRoom        // forwarded from OnPlayerDeath for DeathScreenUI
}

OnPlayerRespawn {
    Vector2 respawnPosition
}

OnCheckpointActivated {
    string checkpointId
}

OnCheckpointDeactivated {
    string checkpointId
}
```

---

### 3r. Loot & Item Drops

#### LootTableSO

ScriptableObject. One asset per enemy type, chest type, or breakable type. A single global
fallback asset is referenced by `GameConfigSO.globalLootTable`.

```csharp
List<LootEntry> entries

float dropChance      // 0.0–1.0 — probability that any item drops at all
                      // e.g. 0.25 for common grunts, 1.0 for bosses and chests
int   minDrops        // minimum items to drop if dropChance roll succeeds (usually 0 or 1)
int   maxDrops        // maximum items to drop (usually 1–2; bosses may drop more)
```

```csharp
struct LootEntry {
    // Exactly one of these is set:
    ItemTemplateSO  template         // proc-gen item (Common / Magic / Rare)
    LegendaryItemSO legendary        // authored unique item (forces Legendary rarity)

    float  weight                    // relative probability weight within this table
    Rarity rarityOverride            // if not None, overrides the level-based rarity roll
                                     // use on chests / boss drops to guarantee Rare or higher
}
```

**Global fallback**: `GameConfigSO.globalLootTable` — used when `EnemyDataSO.lootTable` is
null. Holds broad Common/Magic entries across all slot types as a content baseline.

Rarity probability by player level (configurable as `AnimationCurve` weights on `GameConfigSO`):

| Player level | Common | Magic | Rare | Legendary |
|---|---|---|---|---|
| 1–10 | 70% | 25% | 5% | entry must have a `LegendaryItemSO` |
| 11–20 | 40% | 40% | 20% | same |
| 21+ | 15% | 45% | 40% | same |

A level-based roll never produces a Legendary from a template entry — Legendaries only drop
from `LootEntry` rows that explicitly set a `LegendaryItemSO`.

---

#### LootResolver

Static utility class. Called by every drop source.

```
LootResolver.Resolve(LootTableSO table, Vector2 spawnPos, int playerLevel, int goldMin, int goldMax):

  1. Roll dropChance → if fails, return (no drop)
  2. itemCount = Random.Range(minDrops, maxDrops + 1)
  3. For each item to drop:
       a. WeightedRandom(table.entries) → LootEntry
       b. Rarity:
            entry.rarityOverride != None → use it
            else → roll weighted by playerLevel (table above)
       c. ItemGenerator.Generate(entry.template or entry.legendary, rarity, playerLevel) → ItemData
       d. Spawn ItemPickup at spawnPos + small random offset (avoids stacking)
          → register in WorldStateManager.RoomState[currentRoom].pendingPickups
  4. goldAmount = Random.Range(goldMin, goldMax + 1)
     if goldAmount > 0: spawn GoldPickup at spawnPos
```

---

#### Drop Sources

**Enemy death:**
```
Enemy enters DEATH state
  → LootResolver.Resolve(lootTable ?? GlobalLootTable, deathPos, playerLevel, goldMin, goldMax)
```

**Chest / container — `ChestController`:**
```csharp
string         chestId
LootTableSO    chestLootTable
int            goldAmount       // flat; actual amount = Random.Range(0, goldAmount + 1)
bool           isOpen           // restored from RoomState.openedChests on scene load
```
On interact:
```
1. If isOpen → do nothing
2. isOpen = true; RoomState.openedChests.Add(chestId)
3. Play open animation + VFX
4. LootResolver.Resolve(chestLootTable, pos, playerLevel, 0, goldAmount)
```

**Boss guaranteed drop:**
On DEATH / CINEMATIC_KILL state:
```
LootResolver.Resolve(BossPhaseDataSO.bossLootTable, bossPos, playerLevel, goldMin, goldMax)
```
Boss entries should use `rarityOverride = Rare` or include a `LegendaryItemSO` entry.
`bossLootTable` is a new field added to `BossPhaseDataSO` (or `EnemyDataSO` for bosses).

**Breakable environment objects — `BreakableObject`:**
```csharp
string      breakableId
LootTableSO breakableLootTable   // null = breaks but drops nothing
bool        isBroken             // restored from RoomState.brokenObjects on scene load
```
On destruction (hit by attack, or environmental trigger):
```
1. isBroken = true; RoomState.brokenObjects.Add(breakableId)
2. Play break VFX
3. if breakableLootTable != null:
     LootResolver.Resolve(breakableLootTable, pos, playerLevel, 0, 0)
```
Breakables use low `dropChance` (e.g. 0.10) and only Common/Magic entries.

---

#### ItemPickup

MonoBehaviour spawned by `LootResolver`. Uses the `Interactable` physics layer (collides with
`PlayerMovement`).

```csharp
string         pickupId          // assigned at spawn; used for RoomState.pendingPickups key
ItemData       item
SpriteRenderer iconRenderer
SpriteRenderer glowRenderer      // rarity-tinted glow outline
TMP_Text       nameLabel         // shown only when player is in range
```

Rarity glow colors: Common = none, Magic = blue, Rare = yellow, Legendary = orange (pulsing).

**Player enters trigger zone**: show `nameLabel` (name + rarity color) + InputReader interact hint.

**Player presses interact**:
```
1. PlayerInventory.TryAdd(item):
     true  → RoomState.pendingPickups.Remove(pickupId)
              RoomState.collectedPickups.Add(pickupId)
              Destroy(gameObject)
     false → EventBus.Publish(OnInventoryFull) — HUD flashes "Inventory Full"
              ItemPickup stays in world
```

**Persistence across room re-entry**: `pendingPickups` in `RoomState` stores the item
snapshot + position. On scene load, `WorldStateManager` re-spawns each entry as an
`ItemPickup` at its stored position, restoring the `pickupId` so future collection is
correctly tracked.

---

#### GoldPickup

MonoBehaviour. Auto-pickup on contact with `PlayerMovement` (no interact button needed).

```csharp
int goldAmount
```

```
PlayerPersistentData.gold += goldAmount
EventBus.Publish(OnGoldChanged { newTotal: int })
Destroy(gameObject)
```

Gold is ephemeral — not tracked in `RoomState`. If the player leaves the room before
collecting a gold drop it is lost (gold drops are low-value and numerous; persistence
would add noise to RoomState with minimal benefit).

---

#### PlayerInventory

Component on the player root. Reconstructs runtime `List<ItemData>` from `PlayerPersistentData.bag`
(the serialized form) on load.

```csharp
int            inventoryCapacity    // default 20; set on PlayerDataSO
List<ItemData> bag                  // runtime — deserialized from PlayerPersistentData on load
```

```csharp
bool TryAdd(ItemData item):
    if bag.Count >= inventoryCapacity → return false
    bag.Add(item)
    EventBus.Publish(OnInventoryChanged)
    return true

void Drop(ItemData item, Vector2 position):
    bag.Remove(item)
    LootResolver.SpawnPickup(item, position)   // spawns ItemPickup at position
    EventBus.Publish(OnInventoryChanged)

void EquipFromBag(ItemData item, SlotType slot):
    ItemData displaced = EquipmentManager.equippedItems.GetValueOrDefault(slot)
    EquipmentManager.Equip(item, slot)         // also calls StatSheet.RefreshEquipmentBonuses
    bag.Remove(item)
    if displaced != null:
        if !TryAdd(displaced): Drop(displaced, playerFeetPosition)  // bag full — drop at feet
    EventBus.Publish(OnInventoryChanged)
```

On save: `PlayerInventory.bag` is converted to `PlayerPersistentData.bag` (List<SerializableItemData>)
via `ItemDataSerializer.Serialize()` per item. On load: the inverse via `Deserialize()`.

---

#### Inventory & Equipment UI

Opens via the pause menu.

**Equipment panel (left side):**
- Four named slots: Weapon, Armor, Ring 1, Ring 2
- Each shows the equipped item's icon + rarity glow, or an empty placeholder
- Selecting an occupied slot while no bag item is highlighted → tooltip + "Unequip" option
  → `EquipmentManager.Unequip(slot)` + `PlayerInventory.TryAdd(displaced)`
- Selecting an occupied slot while a compatible bag item is highlighted → equip the bag item

**Bag grid panel (right side):**
- 4×5 grid (20 slots)
- Each occupied slot shows item icon + rarity-colored border
- Selecting an item shows a tooltip: name, rarity, stats block, rolled affix list,
  flavour text, minLevel requirement
- Comparison overlay: the currently equipped item in the matching slot shows ▲/▼ per stat
  field (e.g. ▲ +12 Weapon Damage, ▼ −5 Armor)
- "Equip" → `PlayerInventory.EquipFromBag(item, item.slotType)`
- "Drop" → `PlayerInventory.Drop(item, playerFeetPosition)`

---

#### ItemDataSerializer

Static utility. Bridges runtime `ItemData` ↔ JSON-safe `SerializableItemData`.

```csharp
class SerializableItemData {
    string   templateId         // ItemTemplateSO.baseItemId or LegendaryItemSO.legendaryId
    bool     isLegendary
    SlotType slotType
    Rarity   rarity
    int      itemLevel
    List<SerializableAffixInstance> rolledAffixes
}

struct SerializableAffixInstance {
    string affixId
    float  rolledValue          // the already-rolled value — never re-rolled on load
}
```

**`Serialize(ItemData) → SerializableItemData`**: copy fields; discard Unity object refs
(`Sprite`, `AbilityModifierSO`) — these are re-derived from templates at load time.

**`Deserialize(SerializableItemData, IItemTemplateRegistry) → ItemData`**:
1. Look up template by `templateId` in registry
2. If legendary: copy all fields from `LegendaryItemSO` (no rolling — authored values)
3. If proc-gen: start from `template.baseStats` + `baseAbilityModifiers`, then re-apply each
   saved affix (look up `AffixSO` by `affixId`, apply saved `rolledValue` — no re-roll)
4. Reconstruct full `ItemData` with display data from template

`IItemTemplateRegistry` — implemented by a ScriptableObject holding
`List<ItemTemplateSO>` and `List<LegendaryItemSO>`, the project-wide source of truth for
all authored item assets.

---

#### New EventBus Events (Loot)

```csharp
OnInventoryChanged { }                   // bag or equipment changed — UI refreshes
OnInventoryFull { }                      // pickup attempted when bag at capacity
OnGoldChanged { int newTotal }           // gold picked up or spent
```

---

### 3s. XP & Level System

All XP and level state lives in `ExperienceManager`. Primary stats grow by spending banked
stat points from the pause menu — the game never pauses on level-up. Abilities are unlocked
through exploration/story (`WorldStateManager.unlockedAbilities`) — level only affects stats
and the `LevelScale` damage scalar.

#### LevelConfigSO

ScriptableObject. One asset in the project, referenced by `ExperienceManager`.

```csharp
int   baseXPThreshold      // XP required to reach level 2 (e.g. 100)
float xpGrowthRate         // multiplier applied each level (e.g. 1.4)
                           // XP to reach level N = baseXPThreshold * xpGrowthRate^(N-2)
                           // cumulative XP for level N = sum of thresholds for all prior levels
int   statPointsPerLevel   // always 1; exposed here so designers can experiment during balance
int   hideBarAboveLevel    // hide XP bar when level threshold grows impractically large
```

Example thresholds with `baseXPThreshold = 100, xpGrowthRate = 1.4`:

| Level | XP to reach this level | Cumulative XP |
|---|---|---|
| 2 | 100 | 100 |
| 3 | 140 | 240 |
| 4 | 196 | 436 |
| 5 | 274 | 710 |
| 10 | ~1,035 | ~4,500 |
| 20 | ~5,560 | ~28,000 |

No hard cap — thresholds keep growing. Effective progression plateaus around level 25–30
in a normal playthrough, which aligns with the loot rarity breakpoints (1–10, 11–20, 21+).

---

#### ExperienceManager

Singleton component. Central authority for all XP and level state.

```csharp
int   CurrentLevel         // read from PlayerPersistentData on init
int   CurrentXP            // cumulative XP earned (never decremented)
int   UnspentStatPoints    // banked points not yet allocated
```

```csharp
void AwardXP(int amount):
    CurrentXP += amount
    EventBus.Publish(OnXPGained { amount, newTotal: CurrentXP, progressToNext: float })

    while CurrentXP >= XPThresholdForLevel(CurrentLevel + 1):
        CurrentLevel++
        UnspentStatPoints += LevelConfigSO.statPointsPerLevel
        StatSheet.level = CurrentLevel    // kept in sync immediately each iteration
        EventBus.Publish(OnLevelUp { newLevel: CurrentLevel, unspentPoints: UnspentStatPoints })
        // loop handles multiple level-ups from a single large XP award (boss kill)

int XPThresholdForLevel(int level):
    // Returns cumulative XP needed to reach `level`
    // = sum of (baseXPThreshold * xpGrowthRate^i) for i in 0..(level-2)
    // Computed from LevelConfigSO at runtime — not a lookup table

void SpendStatPoint(PrimaryStatType stat):
    if UnspentStatPoints <= 0: return
    UnspentStatPoints--
    StatSheet.IncrementStat(stat, 1)
    EventBus.Publish(OnStatPointSpent { stat, newStatValue, unspentRemaining })
```

`ExperienceManager` syncs to/from `PlayerPersistentData` on every save and load:
```
Save:  write CurrentLevel, CurrentXP, UnspentStatPoints, and StatSheet base stat values
Load:  read those fields back and apply to StatSheet
```

---

#### XP Award Triggers

**Enemy death:**
```
Enemy enters DEATH state
  → ExperienceManager.AwardXP(EnemyDataSO.xpReward)
```
Boss enemies have a high `xpReward` set in their `EnemyDataSO` — no separate boss-XP system
is needed. Bosses that are `isUnique = true` also write to `worldFlags` on death, which
prevents their XP from being re-awarded if the game re-runs their death sequence somehow.

**Quest / story beats:**
```
NPC dialogue / quest completion event
  → ExperienceManager.AwardXP(questXPAmount)
```
The NPC/dialogue system (Priority 1 TODO) calls `AwardXP`. To prevent double-awarding, the
caller checks `WorldStateManager.worldFlags[$"xp_quest_{questId}_awarded"]` before calling,
then sets that flag after.

---

#### Stat Allocation — Pause Menu Stats Screen

New panel in the pause menu (alongside Equipment/Bag). Accessible any time — points may be
spent immediately on level-up or saved for later.

**Layout:**
- Header: "Level N — N Unspent Points"
- Four stat rows: Strength / Chi / Dexterity / Constitution
- Each row shows: stat name, current base value, a "+" button (enabled only if `unspentStatPoints > 0`)
- Pressing "+" calls `ExperienceManager.SpendStatPoint(stat)` — applies immediately, no confirm step
- Each row also shows the downstream effect in muted text:
  - Strength: "→ affects physical damage"
  - Chi: "→ affects chi damage + max chi pool"
  - Dexterity: "→ affects attack speed + movement speed"
  - Constitution: "→ affects max health"
- XP bar at the bottom of the panel: current XP / XP needed for next level

Points are spent one at a time. There is no undo — once a point is spent it is committed.
The player can close the screen and return later to spend remaining banked points.

---

#### Level-Up HUD Notification

Small component on `ScreenSpaceCanvas`. Subscribes to `OnLevelUp`.

On receipt:
- A banner slides in from the right edge of the screen (ink-brush brushstroke swipe aesthetic).
- Displays: "LEVEL UP  →  Lv. N"
- Holds for ~2.5s, then slides back out.
- If multiple level-ups occur in quick succession (large XP reward), banners queue and display
  sequentially. Does NOT pause gameplay.
- If `unspentPoints > 0`, a small secondary line reads "Stat point available" to remind the
  player to open the stats screen.

---

#### XP Bar — HUD

A thin bar (below the health bar) showing current XP progress toward the next level.
Updates on `OnXPGained` — shows a brief flash fill animation on each award.
On level-up: fills completely, briefly flashes, then resets to show progress toward the next level.
Hidden above `LevelConfigSO.hideBarAboveLevel` (when thresholds grow impractically large).

---

#### New EventBus Events (XP & Level)

```csharp
OnXPGained {
    int   amount            // XP awarded this event
    int   newTotal          // cumulative XP after award
    float progressToNext    // 0.0–1.0 — fraction toward next level threshold
}

OnLevelUp {
    int newLevel
    int unspentPoints       // total banked points after this level-up
}

OnStatPointSpent {
    PrimaryStatType stat
    int             newStatValue
    int             unspentRemaining
}
```

---

### 3t. Camera System

The gameplay camera uses Cinemachine with three virtual cameras (VCs) on the persistent Core
scene. Priority determines which VC is active — higher priority wins. The gameplay VC follows
a `CameraTarget` proxy (not the player directly), which implements lazy vertical tracking.

#### CameraConfigSO

ScriptableObject. One asset, referenced by `CameraManager`.

```csharp
// Gameplay follow
float lookaheadTime             // seconds ahead the camera predicts (e.g. 0.5)
float lookaheadSmoothing        // damping on the lookahead prediction (e.g. 10)
float horizontalDamping         // Cinemachine x-axis damping (e.g. 0.2)
float verticalDamping           // Cinemachine y-axis damping when snapping to new tier (e.g. 0.5)

// Lazy vertical
float verticalSnapThreshold     // min Y delta (world units) to trigger a camera Y snap (e.g. 2.0)
float verticalSnapDuration      // seconds to lerp CameraTarget.y to new tier (e.g. 0.25)

// Orthographic sizes
float defaultOrthoSize          // normal gameplay ortho size (e.g. 6.0)
float bossOrthoSizeMin          // minimum ortho size during boss camera — group framing won't
                                // shrink below this even if player and boss are close (e.g. 7.0)

// Shake
float defaultShakeForce         // baseline impulse force for a standard hit reaction (e.g. 0.3)
```

---

#### CameraTarget

`MonoBehaviour` on a dedicated child of the player root. Cinemachine follows this transform,
not the player directly — decoupled so physics corrections on the player root don't cause
camera jitter.

```
Player (root)
└── CameraTarget   ← Cinemachine follow + look-at target for all gameplay VCs
```

```csharp
// Every Update():
transform.position.x = player.position.x    // X always mirrors player

// OnPlayerLanded (subscribe to EventBus):
float deltaY = player.position.y - transform.position.y
if Mathf.Abs(deltaY) > CameraConfigSO.verticalSnapThreshold:
    StartCoroutine(LerpY(player.position.y, CameraConfigSO.verticalSnapDuration))
// Normal jump arcs don't exceed threshold — Y stays put during a hop

void SnapToPlayer():
    // Called by CameraManager on room transition (under the wipe)
    StopAllCoroutines()
    transform.position = player.position
```

`OnPlayerLanded` is published by the Locomotion state machine when transitioning from
`FALL` → `IDLE` / `WALK` / `RUN` / `CROUCH` (any grounded state).

---

#### Virtual Camera Setup

| VC | Priority | Follow target | Notes |
|---|---|---|---|
| `GameplayVC` | 10 | `CameraTarget` | Default; lookahead via Cinemachine FramingTransposer |
| `BossVC` | 20 | `CinemachineTargetGroup` | Active during boss fights; auto-zooms to frame both |
| `DeathVC` | 30 | player root | Defined in Section 3q; zoom + tilt on death sequence |

**GameplayVC:**
- Body: `CinemachinePositionComposer` — `LookaheadTime` = `CameraConfigSO.lookaheadTime`,
  `LookaheadSmoothing` = `CameraConfigSO.lookaheadSmoothing`; horizontal and vertical damping
  from config
- Extension: `CinemachineConfiner2D` — `BoundingShape2D` updated by `CameraManager` on room load

**BossVC:**
- Follow: `CinemachineTargetGroup` containing player (weight 1.0, radius 1.5) + boss (weight
  1.0, radius set from `BossController.CameraRadius` — boss-specific, authored in Inspector)
- Body: `CinemachinePositionComposer` with auto-framing; orthographic size floor =
  `CameraConfigSO.bossOrthoSizeMin` (prevents excessive zoom-out when boss is compact)
- Extension: same `CinemachineConfiner2D` — boss camera still constrained to room bounds
- No lookahead on BossVC (group framing handles the combined movement prediction)

---

#### CameraManager

Singleton `MonoBehaviour` on the Core scene. Owns all VC and group references.

```csharp
// Subscriptions (wired in OnEnable):
OnRoomConfinementReady   → UpdateConfiner(Collider2D bounds)
OnRoomTransitionComplete → cameraTarget.SnapToPlayer()
OnBossFightStarted       → StartBossFight(BossController boss)
OnBossFightEnded         → EndBossFight()
OnCameraShakeRequested   → impulseSource.GenerateImpulse(force * direction)

void UpdateConfiner(Collider2D bounds):
    confiner.BoundingShape2D = bounds
    confiner.InvalidateCache()   // required by Cinemachine after shape change

void StartBossFight(BossController boss):
    bossTargetGroup.AddMember(boss.transform, weight: 1f, radius: boss.CameraRadius)
    bossVC.Priority = 20         // BossVC activates, blends over GameplayVC

void EndBossFight():
    bossTargetGroup.RemoveMember(boss.transform)
    bossVC.Priority = 0          // GameplayVC resumes
```

---

#### Camera Shake

A `CinemachineImpulseSource` component on the Core camera GameObject. `CameraManager`
subscribes to `OnCameraShakeRequested` and calls `impulseSource.GenerateImpulse()`.

Each hit category uses a different authored `CinemachineImpulseDefinition` asset for distinct
feel (heavy attack = long, low-frequency rumble; parry snap = short, sharp spike).

```csharp
OnCameraShakeRequested {
    float   force       // scale relative to CameraConfigSO.defaultShakeForce
    Vector3 direction   // world-space direction of the impulse
}
```

**Publishers:**
- `HurtboxController` — on any damaging hit; force proportional to damage dealt
- `ParrySystem` — on perfect parry; short sharp impulse
- `DodgeSystem` — on perfect dodge bullet-time exit

---

#### New EventBus Events (Camera)

```csharp
OnRoomConfinementReady {
    Collider2D bounds       // published by RoomCameraConfiner.Awake()
}

OnBossFightStarted {
    BossController boss     // CameraManager adds boss.transform to target group
}

OnBossFightEnded { }

OnCameraShakeRequested {
    float   force
    Vector3 direction
}

OnPlayerLanded { }          // published by Locomotion SM on FALL → grounded transition
                            // CameraTarget evaluates lazy vertical snap on receipt
```

(`OnRoomTransitionComplete` is already defined in Section 4 room transitions.)

#### Parallax Background Layers (not yet designed)

Not yet designed — noted here as a known gap rather than silently discovered later. A single
static background (no parallax) was used for the initial locomotion/combat test scene
(`Assets/_Project/Art/Backgrounds/`), manually aligned to that scene's fixed camera position —
it does not scroll and isn't a template for the real system.

Real parallax needs `CameraManager`/`CameraTarget` to actually move first (they don't yet — see
above); a layer's scroll offset is inherently a fraction of camera movement, so there's nothing
to drive it against until the camera follows the player. When this is designed, expect:
- Multiple background layers (far mountains, mid-ground scenery, near foreground) each moving at
  a different fraction of camera delta-position (0 = fixed/skybox-like, 1 = moves with camera,
  i.e. no parallax, values in between for depth layers behind gameplay).
- Layer draw order via `SpriteRenderer.sortingOrder` on the `Default` sorting layer, most-negative
  = furthest back (the test background above uses `sortingOrder = -10` as its only layer).
- Likely a `ParallaxLayer` component (per-layer scroll factor) driven by `CameraManager` publishing
  its own frame-to-frame movement delta, rather than each layer polling the camera directly.

---

### 3u. NPC / Dialogue System

NPCs deliver lore, teach abilities, and open merchant shops. All dialogue is data-driven via
ScriptableObjects. The game pauses behind a modal two-sided portrait panel; most dialogue is
linear but choice nodes handle key branching moments (quest accept, shop, lore options).

#### NPCDataSO

ScriptableObject. One asset per unique NPC.

```csharp
string  npcId           // unique key — used in worldFlags and EventBus events
string  displayName     // shown as speaker name in the dialogue panel
Sprite  portrait        // NPC portrait; always on the right side of the panel
List<ConditionalDialogue> dialogueStates   // evaluated top-down; first passing entry is used
```

```csharp
[System.Serializable]
class ConditionalDialogue {
    List<string> requiredFlags    // worldFlags that must be true (all required)
    List<string> absentFlags      // worldFlags that must be absent or false (all required)
    DialogueSO   dialogue
}
```

`dialogueStates` is ordered most-specific first (most conditions at the top), default last
(empty condition lists — always passes as a fallback). Evaluation stops at the first match.

---

#### DialogueSO

ScriptableObject. One asset per story-state conversation.

```csharp
List<DialogueNode> nodes    // nodes[0] is always the entry point
```

```csharp
[System.Serializable]
class DialogueNode {
    enum Speaker { Player, NPC }
    Speaker  speaker        // which portrait is highlighted; which name label shows
    string   text           // line to typewriter-reveal in the panel

    // Choices (empty = auto-advance to next node on confirm)
    List<DialogueChoice> choices

    // Action executed after this node's text is confirmed
    DialogueAction actionType     // None | EndDialogue | OpenShop | UnlockAbility | SetWorldFlag
    string         actionPayload  // abilityId (UnlockAbility), flagKey (SetWorldFlag), else empty
    ShopInventorySO shopInventory // populated only when actionType = OpenShop
}

[System.Serializable]
class DialogueChoice {
    string label            // shown in choice button text
    string requiredFlag     // worldFlag that must be true to show this choice; "" = always shown
    int    targetNodeIndex  // index into DialogueSO.nodes to jump to on selection
}

enum DialogueAction { None, EndDialogue, OpenShop, UnlockAbility, SetWorldFlag }
```

A node with no choices and `actionType = None` auto-advances to `nodeIndex + 1`. A node with
`actionType = EndDialogue` closes the panel when confirmed, regardless of what follows.

---

#### NPCController

`MonoBehaviour` in each room scene. One per NPC object.

```csharp
NPCDataSO      npcData
BoxCollider2D  interactionZone   // trigger — overlap shows "Press [Interact]" prompt above NPC
                                 // prompt is hidden when EvaluateDialogueStates() returns null

// Called by PlayerController when interact input fires while player overlaps interactionZone
void OnInteract():
    DialogueSO dialogue = EvaluateDialogueStates()
    if dialogue != null:
        DialogueManager.RunDialogue(dialogue, npcData)

DialogueSO EvaluateDialogueStates():
    foreach ConditionalDialogue cd in npcData.dialogueStates:
        if WorldStateManager.CheckFlags(cd.requiredFlags, cd.absentFlags):
            return cd.dialogue
    return null
```

---

#### WorldStateManager — CheckFlags

New helper method:

```csharp
bool CheckFlags(List<string> required, List<string> absent):
    return required.All(f => worldFlags.ContainsKey(f) && worldFlags[f])
        && absent.All(f => !worldFlags.ContainsKey(f) || !worldFlags[f])
```

---

#### DialogueManager

Singleton `MonoBehaviour` on the Core scene.

```csharp
void RunDialogue(DialogueSO dialogue, NPCDataSO npc):
    currentDialogue = dialogue
    currentNpc = npc
    currentNodeIndex = 0
    GameState → PAUSED
    EventBus.Publish(OnDialogueStarted { npcId: npc.npcId })
    dialoguePanel.Open(npc.portrait, CharacterCustomizationController.GetPlayerPortrait())
    ShowNode(nodes[0])

void ShowNode(DialogueNode node):
    dialoguePanel.SetActiveSpeaker(node.speaker,
        displayName: node.speaker == NPC ? npc.displayName : "Player")
    dialoguePanel.TypewriterReveal(node.text)
    if node.choices.Count > 0:
        dialoguePanel.ShowChoices(FilterChoices(node.choices))

void Advance():
    // Called when confirm is pressed with no choices showing
    if typewriterRunning:
        dialoguePanel.SkipTypewriter()    // first confirm: complete text instantly
        return
    ExecuteAction(currentNode)            // second confirm: run action, then step
    if currentNode.actionType == EndDialogue or atLastNode:
        EndDialogue()
    else:
        ShowNode(nodes[++currentNodeIndex])

void OnChoiceSelected(int targetNodeIndex):
    ShowNode(nodes[targetNodeIndex])

void ExecuteAction(DialogueNode node):
    switch node.actionType:
        OpenShop:      ShopManager.OpenShop(node.shopInventory); return  // EndDialogue implicit
        UnlockAbility: WorldStateManager.UnlockAbility(node.actionPayload)
        SetWorldFlag:  WorldStateManager.SetWorldFlag(node.actionPayload, true)

void EndDialogue():
    dialoguePanel.Close()
    GameState → GAMEPLAY
    EventBus.Publish(OnDialogueEnded { npcId: currentNpc.npcId })

List<DialogueChoice> FilterChoices(List<DialogueChoice> all):
    return all.Where(c => c.requiredFlag == ""
        || (worldFlags.ContainsKey(c.requiredFlag) && worldFlags[c.requiredFlag]))
```

---

#### DialoguePanel

`MonoBehaviour` on `ScreenSpaceCanvas`. Always present in the scene hierarchy; hidden (alpha
0, non-interactive) when no dialogue is running.

**Layout:**
```
┌──────────────────────────────────────────────────────┐
│  [dark semi-transparent overlay — covers gameplay]   │
│                                                      │
│  ┌─────────────┐   Speaker Name        ┌───────────┐ │
│  │             │   ─────────────────── │           │ │
│  │  Player     │   Dialogue text here, │    NPC    │ │
│  │  Portrait   │   typewriter-revealed │  Portrait │ │
│  │  (left)     │                       │  (right)  │ │
│  │             │   [ Choice A ]        │           │ │
│  │             │   [ Choice B ]        │           │ │
│  └─────────────┘                       └───────────┘ │
└──────────────────────────────────────────────────────┘
```

- **Active speaker**: full opacity (1.0), scale 1.05×, name label visible
- **Inactive speaker**: opacity 0.6, scale 1.0, name label hidden
- **Typewriter**: ~40 characters/second. First confirm while animating: skip to full text.
  Second confirm (no choices): calls `DialogueManager.Advance()`
- **Choice buttons**: vertical list below text, D-pad navigable, confirm selects
- **Open**: panel slides up from bottom edge (~0.2s ease-out). Dark overlay fades in.
- **Close**: reverse.

---

#### ShopInventorySO

ScriptableObject. One asset per merchant.

```csharp
string shopId           // unique key — used in worldFlags for one-time purchase tracking
List<ShopEntry> entries

[System.Serializable]
struct ShopEntry {
    ItemTemplateSO template     // sold item; an instance is generated at point of sale
                                // (standard quality, player's current level, Rarity.Common)
    int  goldCost
    bool isUnlimited            // false = sold once; flagged at:
                                // worldFlags[$"shop_{shopId}_sold_{template.baseItemId}"]
}
```

---

#### ShopManager

Singleton `MonoBehaviour` on the Core scene.

```csharp
void OpenShop(ShopInventorySO inventory):
    currentInventory = inventory
    EventBus.Publish(OnShopOpened { inventory })
    // ShopUI activates; game remains PAUSED (was already paused from dialogue)

void BuyItem(ShopEntry entry):
    string soldFlag = $"shop_{currentInventory.shopId}_sold_{entry.template.baseItemId}"
    if PlayerPersistentData.gold < entry.goldCost: return
    if !entry.isUnlimited && WorldStateManager.worldFlags[soldFlag]: return
    ItemData item = ItemGenerator.Generate(entry.template, player.StatSheet.level, Rarity.Common)
    if !PlayerInventory.TryAdd(item):
        EventBus.Publish(OnInventoryFull { })   // ShopUI shows "Bag Full" message; no purchase
        return
    PlayerPersistentData.gold -= entry.goldCost
    EventBus.Publish(OnGoldChanged { newTotal: PlayerPersistentData.gold })
    if !entry.isUnlimited:
        WorldStateManager.SetWorldFlag(soldFlag, true)

void CloseShop():
    EventBus.Publish(OnShopClosed { })
    GameState → GAMEPLAY
```

**ShopUI layout:** Grid of item cards (icon + name + cost). Selecting a card shows a stat
comparison panel versus the currently equipped item in that slot. Confirm calls
`ShopManager.BuyItem()`. One-time entries already purchased are greyed out.

---

#### New EventBus Events (Dialogue & Shop)

```csharp
OnDialogueStarted { string npcId }
OnDialogueEnded   { string npcId }
OnShopOpened      { ShopInventorySO inventory }
OnShopClosed      { }
```

---

## 4. Metroidvania Map / Scene Management

### Scene Structure

- **Persistent Core scene** always loaded: GameManager, AudioManager, InputReader, EventBus,
  UI Canvas
- **Room scenes** loaded additively via `Addressables`; old room unloads after transition

```
Naming convention:
  Room_Zone01_Entrance
  Room_Zone01_Temple_A
  Boss_Zone01_TigerSensei
  Cinematic_Boss_TigerSensei_Kill
```

#### RoomDataSO

ScriptableObject. One asset per room scene, referenced by the minimap system and `RespawnManager`.

```csharp
string   roomId                     // matches scene name exactly
string   displayName                // shown on minimap hover (e.g. "Temple Entrance")
Polygon  minimapPolygon             // shape drawn on minimap (fog of war)

// Respawn / boss support
bool     isBossRoom                 // if true, DeathScreenUI shows "Retry Boss" button on player death
Vector2  bossEntranceSpawnPoint     // world position used by RespawnManager on "Retry Boss"
                                    // set to the spawn point just inside the boss room door
```

Each room scene contains a `RoomCameraConfiner` MonoBehaviour with a `PolygonCollider2D`
matching the playable camera bounds (typically the same shape as `minimapPolygon` but in
world space). On `Awake`, it publishes `OnRoomConfinementReady { Collider2D }`. `CameraManager`
subscribes and updates `CinemachineConfiner2D.BoundingShape2D` so the camera cannot show
outside the current room's walls.

### Room Transitions

1. Player enters `RoomTransition` trigger zone (BoxCollider2D trigger)
2. EventBus fires `OnRoomTransitionRequest(transitionData)`
3. Game state → `LOADING`, ink-splat wipe plays
4. New scene loads additively, player repositioned to matching entrance point
5. Old scene unloads, game state → `GAMEPLAY`

`RoomTransitionData`: target scene name, target entrance ID, transition animation type,
music crossfade flag.

### World State Persistence

`WorldStateManager` persists across loads, maintains:

```csharp
Dictionary<string, RoomState>   // per-room persistent state (see RoomState below)
HashSet<string>                 // unlockedAbilities
Dictionary<string, bool>        // worldFlags — permanent flags (boss kills, story beats)
PlayerPersistentData            // health, position, current room, checkpoint, inventory, gold
```

**`RoomState` fields:**

```csharp
class RoomState {
    HashSet<string> deadEnemies       // instance IDs of killed non-unique enemies (cleared on respawn)
    HashSet<string> collectedPickups  // pickup instance IDs already collected (never re-spawned)
    HashSet<string> openedChests      // chest IDs already opened
    HashSet<string> brokenObjects     // breakable object IDs already destroyed
    HashSet<string> openedDoors       // ability-gate / door IDs that have been opened

    // Pickups spawned in this room but not yet collected — re-spawned on room re-entry
    Dictionary<string, PendingPickup> pendingPickups   // pickupId → (item, position)
}

struct PendingPickup {
    SerializableItemData item       // JSON-safe item snapshot (see Section 3r)
    Vector2              position
}
```

**`PlayerPersistentData` fields:**

```csharp
// Existing
float   currentHealth
float   currentChiPool
Vector2 position
string  currentRoomId

// Checkpoint / respawn
string  lastCheckpointId        // id of the last activated CheckpointController
string  lastCheckpointRoomId    // scene name of the checkpoint's room
Vector2 lastCheckpointPos       // world position of the checkpoint

// Boss retry support
string  lastBossRoomAtDeath     // scene name if player died in a boss room; null otherwise
                                // written by PlayerDeathTrigger on death, cleared on respawn

// Currency
int gold                        // current gold; never reset on death or respawn

// Inventory (JSON-safe form — reconstructed into ItemData at load time via ItemDataSerializer)
List<SerializableItemData>              bag            // up to inventoryCapacity items
Dictionary<SlotType, SerializableItemData> equippedItems  // one entry per slot; absent = empty slot

// Progression
int level               // current player level
int currentXP           // cumulative XP earned (never resets)
int unspentStatPoints   // banked points waiting to be allocated in the stats screen

// Base primary stats (equipment overlay is separate — this is the level-up-growable base)
int statStrength
int statChi
int statDexterity
int statConstitution
```

**`WorldStateManager.ResetEnemies()`** — called by `RespawnManager` on respawn:

```
for each room in RoomState.Values:
    room.deadEnemies.Clear()    // regular enemy kills forgotten — enemies respawn
// worldFlags are NOT touched — boss kills and isUnique enemy kills are permanent
```

Regular enemy deaths are recorded in `RoomState.deadEnemies` (a `HashSet<string>` of enemy
instance IDs). Unique enemies (`EnemyDataSO.isUnique = true`) write their death to
`worldFlags[$"enemy_{enemyId}_defeated"]` instead — `ResetEnemies()` never clears worldFlags.

Serialized to JSON via `Newtonsoft.Json` on: room transition, boss death, `CheckpointController`
activation. ("Respawn point activation" in earlier notes maps to `CheckpointController` activation.)

On room load, `WorldStateManager` broadcasts `OnRoomStateRestored` and individual room
objects self-configure via their own EventBus listeners.

---

### Save System

#### SaveData

Plain serializable C# class. One instance per save slot. Serialized to/from JSON by `SaveManager`.

```csharp
// Meta (shown on main menu slot card)
int      slotIndex
string   saveVersion          // e.g. "1.0" — for future migration compatibility
DateTime timestamp            // wall-clock time of last save
float    playtimeSeconds      // cumulative in-gameplay time (excludes menus and loading screens)
string   displayZoneName      // e.g. "Temple of the Tiger" — from RoomDataSO.displayName at save time

// World state (mirrors WorldStateManager runtime fields)
Dictionary<string, RoomState>   roomStates
HashSet<string>                 unlockedAbilities
Dictionary<string, bool>        worldFlags
PlayerPersistentData            player

// Input
string   inputBindingsJson    // serialized control remapping (InputActionRebindingExtensions)

// Cosmetics
PlayerAppearanceData appearance
```

---

#### SaveSlotMeta

Lightweight struct for main menu display — read without deserializing the full `SaveData`:

```csharp
struct SaveSlotMeta {
    bool     isEmpty
    string   displayZoneName
    float    playtimeSeconds      // formatted in UI as "12h 34m"
    DateTime timestamp            // formatted as "Last played: May 30"
}
```

---

#### SaveManager

Singleton (ServiceLocator-resolved). Owns all file I/O. `WorldStateManager` never writes
files directly — it delegates through `SaveManager`.

**File layout** (`Application.persistentDataPath/saves/`):
```
slot_0.json    slot_1.json    slot_2.json
slot_0.bak     slot_1.bak     slot_2.bak
```

**`Save(int slotIndex, SaveData data)`:**
```
1. If slot_N.json exists → copy to slot_N.bak  (rotate backup before overwriting)
2. Serialize data to JSON (Newtonsoft.Json, pretty-print off)
3. Write atomically: serialize → slot_N.tmp → rename to slot_N.json
   (prevents partial file if game crashes mid-write)
4. EventBus.Publish(OnSaveCompleted { slotIndex })
```

**`Load(int slotIndex) → SaveData?`:**
```
1. Try to deserialize slot_N.json
     success → return SaveData
2. On failure → try slot_N.bak
     success → copy slot_N.bak to slot_N.json (restore backup as primary)
              → show notice: "A previous save was restored for Slot N"
              → return SaveData
3. Both fail → show error: "Save data for Slot N could not be read and has been reset"
             → return null  (caller treats slot as empty)
```

**`ClearSlot(int slotIndex)`** — deletes slot_N.json and slot_N.bak if they exist.

**`ReadMeta(int slotIndex) → SaveSlotMeta`** — parses only meta fields; returns
`SaveSlotMeta { isEmpty = true }` if file is absent or unparseable.

---

#### WorldStateManager — Save/Load Integration

`WorldStateManager` gains two new fields:

```csharp
int    activeSlotIndex      // slot currently in use; -1 while on the main menu
float  sessionStartTime     // Time.realtimeSinceStartup captured at session start, for playtime
```

**`WorldStateManager.Save()`** (updated — delegates to SaveManager):
```
1. Build SaveData from current runtime state:
     playtimeSeconds += (Time.realtimeSinceStartup - sessionStartTime)
     sessionStartTime  = Time.realtimeSinceStartup   // reset session clock
     displayZoneName   = current RoomDataSO.displayName
2. SaveManager.Save(activeSlotIndex, saveData)
```

**`WorldStateManager.LoadSlot(int slotIndex)`** (new — called by main menu on Continue):
```
1. data = SaveManager.Load(slotIndex)
2. If data == null: return (caller handles empty-slot UI)
3. Populate all WorldStateManager runtime fields from data
4. activeSlotIndex    = slotIndex
5. sessionStartTime   = Time.realtimeSinceStartup
```

**`WorldStateManager.NewGame(int slotIndex)`** (new — called by main menu on New Game):
```
1. SaveManager.ClearSlot(slotIndex)
2. Reset all WorldStateManager runtime fields to defaults
3. Initialize PlayerPersistentData from GameStartSO (lastCheckpointId = null)
4. activeSlotIndex    = slotIndex
5. sessionStartTime   = Time.realtimeSinceStartup
6. playtimeSeconds    = 0
```

---

#### Save Triggers (complete list)

| Trigger | Where it fires |
|---|---|
| Checkpoint activation | `CheckpointController` → `WorldStateManager.Save()` |
| Room transition | Room transition flow → `WorldStateManager.Save()` |
| Boss death | Boss phase system → `WorldStateManager.Save()` |
| Application quit | `Application.quitting` hook on `WorldStateManager` |
| Pause → Return to Main Menu | Pause menu "Return to Menu" button |

---

#### Main Menu — SaveSlotUI

One screen with three `SaveSlotCard` components. Each card reads `SaveManager.ReadMeta(slotIndex)`
on menu open.

**Empty card:** "Empty" label. Click → `WorldStateManager.NewGame(slotIndex)` → load start room.

**Occupied card:** shows `displayZoneName`, playtime ("12h 34m"), timestamp ("Last played: May 30").
- Primary click → `WorldStateManager.LoadSlot(slotIndex)` → load saved room.
- Secondary "New Game" affordance → confirmation dialog "This will permanently erase Slot N.
  Continue?" → Yes → `WorldStateManager.NewGame(slotIndex)`.

No separate "New Game" / "Load Game" buttons — both flows are handled contextually by the cards.

---

#### New EventBus Event

```csharp
OnSaveCompleted {
    int slotIndex
}
```

---

### Ability Gates

Each `AbilityGate` holds a `RequiredAbilitySO`. Queries `WorldStateManager.HasAbility()` on
room load and on `OnAbilityUnlocked` events. Opens with an animation and disables its collider.

**Planned abilities:**
- `WALL_JUMP`
- `DOUBLE_JUMP`
- `DASH_UPGRADE`
- `CHI_BLAST`
- `HIGH_KICK` (breaks cracked ceilings)
- `IRON_BODY` (withstands environmental hazards)
- `WIRE_FU` (grapple to ceiling anchor rings)
- `ADVANCED_COMBAT` (unlocks advanced combat mode + motion input detection)
- Additional motion input abilities defined via `TriggerEffectSO` — no code required per ability

---

## 5. Boss System

### Phase Controller

Each boss holds a `List<BossPhaseDataSO>`. Each phase defines:

```csharp
float              hpThreshold                 // e.g. 0.66, 0.33
List<AttackPatternSO> attackPool
MovementBehaviorSO movementBehavior           // see MovementBehaviorSO below
float              musicPhaseParameter
bool               playCinematicOnEntry
```

**`MovementBehaviorSO`** — ScriptableObject defining per-phase boss movement:

```csharp
CombatProfile combatProfile           // shared spacing model (see section 2 — Enemy AI Behavior)

// Boss-specific overrides
bool    hasArenaAnchor                // if true, boss drifts toward arenaAnchor position
Vector2 arenaAnchor                   // world-space anchor (e.g. arena center, throne position)
float   arenaAnchorWeight             // 0–1: 0 = pure player pursuit, 1 = orbit anchor strongly
                                      // e.g. 0.3 = mostly chase player, gradually drift to center
bool    retreatToAnchorOnPhaseEnd     // move to arenaAnchor before phase transition cinematic
```

Attack selection: weighted random from pool filtered by phase + player conditions (proximity,
airborne state), with last-used cooldown to prevent repetition.

### Phase / Kill Cinematic Flow

```
HP threshold hit
  → Boss enters PHASE_TRANSITION
    → BossCinematicTrigger fires OnCinematicStart
      → Input locked, letterbox animates in
        → PlayableDirector runs Timeline
          (camera blends, character animations, audio cue)
            → OnCinematicComplete → new phase begins

HP = 0
  → CINEMATIC_KILL state (suppresses standard death)
    → Slow-mo final blow
      → Freeze frame (timeScale = 0, ~0.5s)
        → Victory pose hold
          → Ability drop, WorldState update, respawn point set
```

---

## 6. Boss Health Bar

**Position:** top-center of screen.

**Structure:** one full-width bar per phase. No segments. The bar represents current phase HP
only, draining as the player deals damage within that phase.

**Two stacked `Image` fills:**
- `backgroundImage` — always full width, tinted the **next** phase color
- `foregroundImage` — Fill Origin: Left, fill amount = `currentPhaseHP / maxPhaseHP`,
  tinted the **current** phase color

As the foreground drains left-to-right, the background color is revealed beneath. When the
phase threshold is hit the bar is entirely the background color — that color naturally becomes
the next phase's foreground with no separate animation needed.

**On `OnBossPhaseChange(int newPhase)`:**
1. Swap foreground color to old background color
2. Set new background color to next-next phase color
3. Reset foreground fill to 1.0

| Phase | Foreground (current HP) | Background (next phase preview) |
|---|---|---|
| Phase 1 | Red | Yellow |
| Phase 2 | Yellow | Green |
| Phase 3 | Green | Dark / black |

---

## 7. Input System

`InputReader` is a **ScriptableObject** wrapping `PlayerInputActions` (`.inputactions` asset).
Exposes C# events (not UnityEvents). Passed via Inspector — no singleton coupling.

```csharp
event Action          OnAttackLight
event Action          OnAttackHeavy
event Action          OnDefensivePress      // parry evaluation happens here
event Action          OnDefensiveRelease    // exits BLOCKING
event Action          OnDodge
event Action          OnJump
event Action          OnJumpCancelled
event Action<Vector2> OnMove
event Action          OnInteract
event Action          OnPause
event Action          OnAbility1
event Action          OnAbility2
event Action          OnGrapple                    // WIRE_FU — fire/release grapple line
event Action          OnAdvancedCombatPressed      // hold to enter advanced combat mode
event Action          OnAdvancedCombatReleased     // release to exit
```

`OnDefensiveHold` is not needed — block state is entered when press is outside the parry
window and the button remains held past the 3–5 frame threshold.

Control remapping via `InputActionRebindingExtensions` API. Bindings saved as JSON in the
save file.

---

## 8. Audio

**FMOD Studio** (`com.fmod.unity`) strongly preferred over Unity's Audio Mixer:
- Parameter-driven adaptive music reacts to combat intensity in real time
- Snapshot system handles per-zone reverb (temple halls, caves, outdoors) with crossfading
- Stinger events for phase transitions without audio seams

### Music Parameters

```
Combat_Intensity   float 0.0–1.0   ramps up on combat start, decays ~8s after combat ends
Phase              int   0–3        set on boss phase change / room entry
```

### SFX Categories

- Combat: sword swings, fist impacts (cloth / flesh / bone variants), parry clang,
  chi blast, kick impacts
- Footsteps: surface material parameter (stone, wood, grass, bamboo)
- Voice: grunts, exertion, pain — supports multiple voice packs
- Ambient: wind, temple bells, insects, distant crowds
- Cinematic: dramatic sting, whoosh reveal, freeze-frame impact boom

`SFXCatalog` ScriptableObject maps string keys to FMOD event paths. All calls go through
`AudioManager.PlaySFX(string key)`.

---

## 9. UI / HUD

### Canvas Architecture

- `WorldSpaceCanvas` (Screen Space - Camera): floating damage numbers, enemy health bars,
  stagger bars — follow world positions
- `ScreenSpaceCanvas` (Screen Space - Overlay): player HUD, fixed to screen

### HUD Elements

| Element | Notes |
|---|---|
| Health bar | Segmented ink-brush style, ghost-health trail lags behind on damage |
| Stamina / Chi | Arc-shaped, flashes when empty, recharges via fill shader |
| Boss bar | Top-center, full-width, phase color system described above |
| Stagger bar | World-space above enemy, fills on hits and parries |
| Minimap | Data-driven from `RoomDataSO` polygons, fog of war, current room pulses |
| Combo counter | Brushstroke font, white → gold → red color tiers, fades 2s after last hit |
| Ability icons | Cooldown clock fill when unavailable, unlock slide-in animation |
| Save indicator | Small ink-stamp icon, corner of screen; fades in/out briefly on `OnSaveCompleted` |
| XP bar | Thin bar below health; fills on `OnXPGained`, resets on level-up; hidden near soft cap |
| Level display | Small "Lv. N" label near health bar; updates on `OnLevelUp` |

### Screen Effects

- Letterbox bars animate in during cinematics (`LetterboxController`)
- Persistent film grain (mild during gameplay, intensified on kill cinematic)
- Chromatic aberration pulse on hard impacts
- Ink-splat full-screen wipe on room transitions

---

## 10. Recommended Unity Packages

| System | Package |
|---|---|
| Input | `com.unity.inputsystem` |
| Camera + shake | `com.unity.cinemachine` |
| Cutscene sequencing | `com.unity.timeline` |
| Sprite rigging | `com.unity.2d.animation` |
| Tilemaps | `com.unity.2d.extras` |
| Post-processing | URP built-in |
| Audio | FMOD for Unity |
| Save serialization | `com.unity.nuget.newtonsoft-json` |
| Async scene loading | `com.unity.addressables` |
| Enemy pathfinding | A* Pathfinding Project (Aron Granberg) |

---

## 11. Key Architectural Principles

**EventBus over direct coupling.** Systems communicate by broadcasting typed events.
`EventBus` is a static generic class: `Subscribe<T>`, `Unsubscribe<T>`, `Publish<T>`.
A boss death should not require `BossAI` to know about `AudioManager`.

**ScriptableObject data layer.** All tunable values live in ScriptableObjects. Designers
iterate on hitbox data, combo definitions, attack patterns, and parry windows without touching
code.

**Additive scene loading.** The persistent Core scene never unloads. Room scenes load and
unload additively. AudioManager and GameManager survive room transitions without
`DontDestroyOnLoad` hacks.

**Frame-data-driven combat.** Startup, active, recovery, cancellable frames, and parry windows
are authored as data. The combo and parry systems have a single source of truth. Balancing
happens in the Inspector, not in code.

**Cinematic state isolation.** When `CINEMATIC` game state is active, non-cinematic gameplay
events are blocked or queued. `CinematicDirector` is the sole authority and signals completion
explicitly.

**Movement abilities as plugins.** Movement abilities (double jump, air dash, grapple) are
self-contained `MonoBehaviour` components that call hooks on `PlayerController`. The base
player systems have no knowledge of installed abilities. Adding a new movement ability is
adding a component — no changes to `PlayerStateMachine` or `PlayerController`.

**Motion inputs are detection-layer only.** `MotionInputDetector` is entirely separate from
`InputBuffer` and `ComboSystem`. A matched trigger fires its paired `EffectSO` directly —
some effects then use the `AbilityExecutionContext` pipeline (§3j), some don't need to. New
motion-triggered abilities are `TriggerEffectSO` assets pairing a `MotionInputTriggerSO` with
whatever effect fits — no code required. Unmatched inputs fall through to the combo system
transparently.

---

## 13. Character Customization

Cosmetic-only sprite layer swapping. All options are pre-drawn assets; there is no procedural
generation. The system supports any number of slots and options per slot — initial scope is two
options per slot to prove the pipeline works.

### Technical Backbone: Unity Sprite Library

`com.unity.2d.animation` (already in the package list) ships a Sprite Library system designed
for exactly this:

- **`SpriteLibraryAsset`** — ScriptableObject that organizes sprites by **Category** (slot
  name, e.g. "Hair") and **Label** (option name, e.g. "Topknot"). One asset for the whole
  player character.
- **`SpriteLibrary`** component on the player root — holds the active `SpriteLibraryAsset`.
- **`SpriteResolver`** component on each layer child GameObject — references a Category and
  resolves to one Label at runtime. Swapping is one call:
  `spriteResolver.SetCategoryAndLabel("Hair", "Topknot")`

The animation rig drives all layers equally. Swapping a sprite label doesn't touch the
skeleton, blend trees, or any animation clip — it is purely a texture swap at the data level.

### Defined Slots (initial)

| Slot ID | Category (Sprite Library) | Initial options |
|---|---|---|
| `hair` | "Hair" | Topknot, Ponytail |
| `face` | "Face" | Default, Marked (face paint / scar) |
| `outfit` | "Outfit" | Robe, Wrapped (fighter wraps) |

New slots are added by authoring new Category entries in the `SpriteLibraryAsset` and a
corresponding `CosmeticSlotSO`. No code changes required.

### Data Layer

#### CosmeticSlotSO

Represents one swappable slot. Registered in `CosmeticRegistry`:

```csharp
string   slotId             // matches Sprite Library Category name exactly (e.g. "Hair")
string   displayName        // shown in customization UI
Sprite   slotIcon           // UI icon for this slot
List<CosmeticOptionSO> options
```

#### CosmeticOptionSO

One selectable option within a slot:

```csharp
string optionId             // matches Sprite Library Label name exactly (e.g. "Topknot")
string displayName
Sprite previewThumbnail     // shown in the option carousel
bool   isUnlockedByDefault  // false = must be unlocked via WorldStateManager
Sprite portraitSprite       // optional; shown in dialogue panel when this cosmetic is equipped
                            // null = DialogueManager falls back to PlayerDataSO.defaultPlayerPortrait
```

#### CosmeticRegistry

ScriptableObject (one asset, assigned in GameManager):

```csharp
List<CosmeticSlotSO> slots  // ordered — defines UI display order
```

Single source of truth for all available slots and options. Adding a new slot is adding an
entry here and drawing the sprites — nothing else.

#### PlayerAppearanceData

Plain serializable C# struct stored inside the save data (`WorldStateManager`):

```csharp
Dictionary<string, string> selections   // slotId → optionId
// e.g. { "hair": "Ponytail", "face": "Default", "outfit": "Robe" }
```

Missing entries fall back to the first option in the slot's list — new slots added in
patches won't break existing saves.

### CharacterCustomizationController

MonoBehaviour on the player prefab root. Holds a reference to one `SpriteResolver` per slot:

```csharp
// Inspector-assigned — one SpriteResolver child per slot, keyed by slotId
Dictionary<string, SpriteResolver> resolvers

void ApplyAppearance(PlayerAppearanceData data)
    // for each slot: resolvers[slotId].SetCategoryAndLabel(slotId, data.selections[slotId])

void ApplySlot(string slotId, string optionId)
    // single-slot live update — called by CustomizationUI during preview

Sprite GetPlayerPortrait()
    // returns portraitSprite of the currently active option for the primary visual slot (e.g. "outfit")
    // falls back to PlayerDataSO.defaultPlayerPortrait if portraitSprite is null
```

Listens to `OnAppearanceChanged` via EventBus to reapply after a scene load (the player
prefab is always in the persistent Core scene, so in practice this fires once on boot).

### Unlock Integration

`CosmeticOptionSO.isUnlockedByDefault = false` options are locked until
`WorldStateManager.UnlockCosmetic(slotId, optionId)` is called. The customization UI
queries `WorldStateManager.IsCosmeticUnlocked(slotId, optionId)` per option and greys out
locked entries with a lock icon. Unlocks can be wired to boss kills, collectibles, or any
other `WorldState` flag — the customization system has no opinion on unlock conditions.

### Customization UI (CustomizationScreen)

Accessible from the pause menu or a hub NPC. Not available mid-combat.

```
┌─ Slot tabs: [Hair] [Face] [Outfit] ... ─────────────────────────────────┐
│                                                                         │
│   Live preview (player idle animation plays with current selections)   │
│                                                                         │
│   Option carousel: ← [Topknot ✓] [Ponytail] [🔒 ???] →               │
│                                                                         │
│                          [Confirm]  [Cancel]                            │
└─────────────────────────────────────────────────────────────────────────┘
```

- Selecting an option calls `ApplySlot()` immediately for live preview; selection is not
  saved until Confirm
- Cancel reverts to `WorldStateManager`-stored selections by calling `ApplyAppearance()`
- Locked options show a lock icon and are not selectable; their `displayName` can be replaced
  with a hint ("Defeat the Tiger Sensei")

### Art Pipeline Constraint

All options within a slot **must be drawn on the same canvas with identical pivot points**.
If a hair option is drawn 3px higher than the default, it will float off the head at runtime.
This is a sprite authoring rule, not a code safeguard — establish it as a template layer in
the art file before drawing any options.

Recommended: keep a master reference layer (body silhouette) in every art file and lock it
while drawing cosmetics, so alignment is always relative to the same anchor.

---

## 12. Build Order (Critical Path)

| Order | File | Why first | Status |
|---|---|---|---|
| 1 | `EventBus.cs` | Everything communicates through this | ✅ Done |
| 2 | `PlayerStateMachine.cs` | Gates all combat work | ✅ Done |
| 3 | `HitboxController.cs` / `HurtboxController.cs` (+ `HurtboxZoneForwarder.cs`, `DamageCalculator.cs`) | Damage pipeline | ✅ Done — attacker-driven resolution, `Health.TakeDamage`/`OnEntityDamaged`, and hitbox reach authored via keyframed AnimationClip curves rather than a code reach-index; Head/Block/stagger/StatSheet still stubbed, see §3n |
| 4 | `InputBuffer.cs` | Required before combo system | ✅ Done — `InputBuffer.cs` (`KungFuVania.Combat`), a capacity-16 timestamped ring buffer on the Player. `PlayerController` buffers a light/heavy attack press only when `TryEnterState` fails because the combat state machine is busy (not for any other rejection reason), then on return to `NONE` re-resolves the crouch/air/ground target state fresh against current locomotion — never a press-time snapshot — before retrying through `TryEnterState`, so a stale buffered attack fails closed instead of firing in a context it's no longer valid for. One flat `[SerializeField]` expiry window (0.4s, tuned to safely outlast the busiest current attack clip), measured from the press itself; no per-step `inputBufferWindow`/`inputExpireWindow` or cancellable-frame gating yet — that's `ComboSystem`/`ComboStep`'s job once combos exist |
| 5 | `MotionInputDetector.cs` (+ skill loadout) | Moved up ahead of Stagger/Stat/Aura/Equipment — a matched pattern only needs to fire *something*, and can do that as a trimmed flat-damage attack today (same trim `DamageCalculator` already uses, see row 3) rather than waiting on the full `AbilityExecutionContext` chain. Facing-relative zone mirroring and the runtime resolution model are now fully designed — see §3l: a real-time trie walk over the player's active skill loadout, replacing the originally-drafted `MotionInputBuffer` ring buffer entirely (it's gone from the design, not just unbuilt). | Not started |
| 6 | `ProjectileController.cs` + `ProjectileManager.cs` | Carrier for any traveling special fired by Motion Input System (e.g. a fireball) — fully designed now, see §3l "Projectile System". Listed after Motion Input System here for build-order bookkeeping only — functionally it needs to land alongside or before it, since a matched motion has nothing to fire without it. | Not started |
| 7 | `StaggerMeter.cs` | Required before combat tuning | Not started |
| 8 | `StatSheet.cs` | Required before damage formula, health system, or chi pool | Not started |
| 9 | `GameTickManager.cs` | Required before any tick-based aura | Not started |
| 10 | `AuraManager.cs` + `AuraVisualController.cs` | Required before dodge, abilities, or status effects | Not started |
| 11 | `EquipmentManager.cs` + `AbilityExecutionContext.cs` | Required before any ability executes damage | Not started |
| 12 | `PlayerController` hooks (`ApplyImpulse`, `ForceLocomotionState`, etc.) | Required before any movement ability component | ✅ Done |
| 13 | `WorldStateManager.cs` | Room persistence and ability unlocks | Not started |
| 14 | `CharacterCustomizationController.cs` | Requires WorldStateManager for unlock queries and save/load | Not started |
| 15 | `CinematicDirector.cs` | Required before any boss content | Not started |

---

## TODO — Systems Not Yet Planned

- **Parallax background layers** (§3t) — blocked on `CameraManager`/`CameraTarget` actually
  moving; see the note in Camera System above.

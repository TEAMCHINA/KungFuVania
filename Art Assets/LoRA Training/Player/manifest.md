# KFV Player LoRA Training Set — Manifest

**Trigger token:** `kfvchar` (nonsense token, first word in every caption, no exceptions)
**Total images:** 78 (78 PNG + 78 paired `.txt` captions)
**Caption style:** comma-separated tags, trigger first, then view/angle, pose/action, notable pose details. Garment ("white kung-fu jacket with black frog buttons, black pants, white socks, black shoes with tan soles") is deliberately **not** repeated in every caption — it's constant across the entire set and never varies, so per LoRA captioning convention it's left uncaptioned and will be learned as the character's baseline appearance.

This file is meant for a visual scan: read the flags below first, then walk the per-action tables and eyeball each thumbnail-sized crop against its caption.

---

## READ THIS FIRST — judgment calls and flags

### 1. Most "already-single-pose" source files were actually multi-frame strips
The task brief's category B (files assumed to need only copying, no splitting) turned out to be wrong for 8 of its 12 top-level files once actually opened: `KFV_Punch.png` (3 frames), `KFV_FrontKick.png` (4), `KFV_AxeKick.png` (8, labeled "AXE KICK" 1–8), `KFV_JumpKick.png` (3), `KFV_CrouchingUppercut.png` (7, labeled "CROUCHING UPPERCUT (MORTAL KOMBAT STYLE)" with per-frame captions), `KFV_Punch_Kick.png` (11, see flag #2 below), `KFV_Roundhouse.png` (12, labeled "ROUNDHOUSE KICK (FULL SWEEP)"), and `KFV_Dodge.png` (6). Only `KFV_Jump_Punch.png` was genuinely a single pose.
**Handling:** I treated every source file by what it actually contains rather than by which category the brief guessed it belonged to, and split all of the above using the same alpha/background connected-component detection method used for Idle/Walk/Jump. This matches the stated goal (one character pose per training image) far better than shipping multi-character strips as single "poses" would have. Frame counts for every split were verified against my own visual count of each source strip before finalizing.

### 2. RESOLVED — `KFV_Punch_Kick.png` was a stale leftover, removed from the training set
Confirmed by the user: `Punch.png` and `FrontKick.png` were split out from the original combined sheet and are the canonical versions; `KFV_Punch_Kick.png` should have been deleted at that point and wasn't. The `PunchKick/` folder (11 frames — a 5-frame "jab" + 6-frame "front kick", not actually a combo move despite the name) has been **removed from this training set** as duplicate/non-canonical. `Punch/` (3 frames) and `FrontKick/` (4 frames) below are the correct versions. The stale source file `Art Assets/Player/KFV_Punch_Kick.png` itself is still sitting in the source folder — not deleted by this process, left for the user to clean up if they want.

### 3. `Run/KFV_Run_06_legs.png` excluded — near-duplicate, not a legs-only crop
Despite the filename, this is a full-body image (head to shoes), not a legs detail crop. Pixel comparison against `KFV_Run_06.png`: same bounding-box region, same silhouette and colors, median pixel difference of 0–10 (i.e. near-identical) with the only large differences concentrated along outline edges — consistent with a resized/re-exported duplicate of the same pose, not a distinct frame. **Excluded from the training set** to avoid feeding the LoRA a near-duplicate; flagging in case the source actually differs in some way this check didn't catch.

### 4. Gray-matte background inconsistency — resolved
Six original source files (`KFV_walk.png`, `KFV_jump.png`, `KFV_AxeKick.png`, `KFV_CrouchingUppercut.png`, `KFV_Roundhouse.png`, `KFV_Dodge.png`) had a baked-in dark-gray matte instead of real alpha transparency, unlike the rest of the set. Resolution, per user decision:
- **AxeKick: left out for now.** Frame 4 (leg raised straight overhead — the sequence's key pose) has a real proportion problem, and it's the frame that matters most, so the whole sequence isn't worth training on as-is. Revisit if/when a corrected version exists; not currently blocking anything else.
- **Roundhouse, CrouchingUppercut: user regenerated both with real transparency** (confirmed live — sampled background-only pixels directly, alpha=0 at every corner/edge sample on both files; the dark vignette visible in a plain image viewer is just how transparency renders there, not a baked-in dark background). Same choreography as the original versions (same frame counts, same pose sequence, same in-frame text labels on CrouchingUppercut), re-split with alpha-based connected-component detection (title text and per-frame number/text labels correctly excluded as separate small components) and re-added below with the same captions as before, since the poses are unchanged.
- **Walk, Jump: replaced with the actual in-game sprites.** `Assets/_Project/Sprites/Walk/KFV_Walk_01-06.png` and `.../Jump/KFV_Jump_01-06.png` are the real, individually-sliced, genuinely-transparent in-game assets, not the "Art Assets" staging versions. Swapped directly (already single frames, no splitting needed) and re-captioned against the real content, since frame count differs from the old source (Walk: 6 vs. the old strip's 8; Jump: 6 vs. 7 — the in-game version is a trimmed/finalized cut, not the same take). Tradeoff worth knowing: noticeably lower resolution than the Art Assets staging files (roughly 190×250 to 370×470 vs. 800-2000px) — still workable for training, just smaller.
- **Dodge: also replaced, same as Walk/Jump.** Initially held back — the in-game `DodgeRoll/` asset's tucked/inverted tumble poses read to me as "white pants instead of black," which I flagged as a possible character-level style inconsistency. **User visually confirmed the sprites are correct**: in a tight tuck, the black pants fabric is mostly hidden behind the legs, so the silhouette is dominated by the white jacket — a foreshortening effect, not an actual style difference. Swapped to `Assets/_Project/Sprites/DodgeRoll/KFV_DodgeRoll_01-06.png`, re-captioned against the real content.

### 5. RESOLVED — two near-identical pairs, confirmed both worth keeping
`Crouch/KFV_Crouch01.png` vs `KFV_Crouch02.png`, and `WallSlide/KFV_WallSlide01.png` vs `KFV_WallSlide02.png`, are each pixel-level near-duplicates. User confirmed both pairs are intentionally distinct, not duplicates like `Run_06_legs`: Crouch has a subtle "breathing" pulse between the two frames, and WallSlide differs slightly in dust-cloud particle positions. Both kept as originally split.

### 6. `KFV_AxeKick.png` frames 5–6 required tuned detection
At default settings the motion-lines trailing frame 5 (leg swinging down) and the dust burst in frame 6 (impact) were close enough to bridge into one connected component, merging what should be two frames. Fixed by lowering the mask-dilation radius; re-verified the resulting 8-way split visually (see `AxeKick/kfv_axekick_05.png` and `_06.png`) — clean separation, nothing clipped.

### 7. Excluded per task instructions (not processed, listed here only for completeness)
- `KFV_Main_Character_Reference.png` — composite model sheet, separate task.
- `Character Concept.png`, `Character Concept v1.0.ai` — concept art, not this pass.
- `KFV_jump_axe_kick_chatgpt.png` — prior ChatGPT-generated attempt with confirmed proportion drift (frame 5 of its axe-kick sequence is visibly larger-scaled than its neighbors). This is the known-bad example the LoRA is meant to fix; not included as a training candidate.
- `.psd`/`.ai` source files — flattened PNG exports used instead.
- `Art Assets/Player/Projectiles/` — fireball VFX, not the character.

### 8. Tooling installed
This environment had no Pillow/numpy/scipy. Installed via `pip install Pillow numpy scipy` to do alpha-channel/connected-component frame detection. No other tooling changes made.

---

## Idle — 6 frames, from `KFV_Idle.png` (2172×724, RGBA transparent, single row)
All 6 are close variations of the same standing guard stance (near-identical — this looks like a subtle idle-breathing loop rather than distinct poses).

| File | Caption |
|---|---|
| kfv_idle_01.png | kfvchar, side view, idle fighting stance, fists raised in guard, standing ready |
| kfv_idle_02.png | kfvchar, side view, idle fighting stance, fists raised in guard, standing ready |
| kfv_idle_03.png | kfvchar, side view, idle fighting stance, fists raised in guard, standing ready |
| kfv_idle_04.png | kfvchar, side view, idle fighting stance, fists raised in guard, standing ready |
| kfv_idle_05.png | kfvchar, side view, idle fighting stance, fists raised in guard, standing ready |
| kfv_idle_06.png | kfvchar, side view, idle fighting stance, fists raised in guard, standing ready |

## Walk — 6 frames, from `Assets/_Project/Sprites/Walk/KFV_Walk_01-06.png` (the actual in-game sprites, ~300×470, real RGBA transparency — see flag #4)

| File | Caption |
|---|---|
| kfv_walk_01.png | kfvchar, side view, walking cycle, front leg stepping forward, fists raised in guard |
| kfv_walk_02.png | kfvchar, side view, walking cycle, legs mid-stride crossing, fists raised in guard |
| kfv_walk_03.png | kfvchar, side view, walking cycle, front knee lifting, fists raised in guard |
| kfv_walk_04.png | kfvchar, side view, walking cycle, fists tucked close to the chest, legs together mid-step |
| kfv_walk_05.png | kfvchar, side view, walking cycle, front leg stepping forward, fists raised in guard |
| kfv_walk_06.png | kfvchar, side view, walking cycle, front knee lifting high, fists tucked close to the chest |

## Jump — 6 frames, from `Assets/_Project/Sprites/Jump/KFV_Jump_01-06.png` (the actual in-game sprites, ~190×250, real RGBA transparency — see flag #4)

| File | Caption |
|---|---|
| kfv_jump_01.png | kfvchar, side view, jump start, crouched launching stance, fist forward |
| kfv_jump_02.png | kfvchar, side view, launching into a jump, motion lines trailing at the feet |
| kfv_jump_03.png | kfvchar, side view, mid-air, rising, knees bent |
| kfv_jump_04.png | kfvchar, side view, mid-air, peak of the jump, knees tucked to the chest |
| kfv_jump_05.png | kfvchar, side view, mid-air, descending, fist raised forward |
| kfv_jump_06.png | kfvchar, side view, landing impact, low crouch on all fours, dust marks at the feet |

## Run — 6 frames, copied as-is (no splitting needed) from `Run/KFV_Run_01.png` … `KFV_Run_06.png`

| File | Source | Caption | Note |
|---|---|---|---|
| kfv_run_01.png | KFV_Run_01.png | kfvchar, side view, running stride, front foot low near the ground, back leg trailing, fists pumping | |
| kfv_run_02.png | KFV_Run_02.png | kfvchar, side view, running stride, front foot low near the ground, back leg trailing, fists pumping | |
| kfv_run_03.png | KFV_Run_03.png | kfvchar, side view, running stride, legs gathered beneath the body, both knees bent, fists pumping | |
| kfv_run_04.png | KFV_Run_04.png | kfvchar, side view, running stride, legs gathered beneath the body, both knees bent, fists pumping | |
| kfv_run_05.png | KFV_Run_05.png | kfvchar, side view, running stride, front knee driving upward, back leg extending behind, fists pumping | |
| kfv_run_06.png | KFV_Run_06.png | kfvchar, side view, running stride, front knee driven high, both feet off the ground, peak stride | |
| *(excluded)* | KFV_Run_06_legs.png | — | Near-duplicate of Run_06 — see flag #3. Not included. |

## Punch — 3 frames, split from `KFV_Punch.png` (869×335, RGBA transparent, single row)

| File | Caption |
|---|---|
| kfv_punch_01.png | kfvchar, side view, fighting stance, fists raised in guard |
| kfv_punch_02.png | kfvchar, side view, jab punch, arm fully extended forward, motion lines |
| kfv_punch_03.png | kfvchar, side view, fighting stance, fists raised in guard |

## FrontKick — 4 frames, split from `KFV_FrontKick.png` (952×326, RGBA transparent, single row)

| File | Caption |
|---|---|
| kfv_frontkick_01.png | kfvchar, side view, fighting stance, fists raised in guard |
| kfv_frontkick_02.png | kfvchar, side view, front kick windup, knee chambered |
| kfv_frontkick_03.png | kfvchar, side view, front kick windup, knee chambered higher |
| kfv_frontkick_04.png | kfvchar, side view, front kick impact, leg fully extended forward, motion lines |

## JumpKick — 3 frames, split from `KFV_JumpKick.png` (1448×1086, RGBA transparent, single row)
Frames 1 and 3 look like the same airborne knee-tuck pose (can't confidently tell ascending vs. descending from the art alone — captioned identically rather than guessing).

| File | Caption |
|---|---|
| kfv_jumpkick_01.png | kfvchar, side view, mid-air, knees tucked, fists raised in guard |
| kfv_jumpkick_02.png | kfvchar, side view, jump kick impact, mid-air, leg fully extended forward |
| kfv_jumpkick_03.png | kfvchar, side view, mid-air, knees tucked, fists raised in guard |

## CrouchingUppercut — 7 frames, split from the regenerated (transparent) `KFV_CrouchingUppercut.png` (2032×774, RGBA transparent, single row, wide spacing — see flag #4)
Source strip is explicitly labeled "CROUCHING UPPERCUT (MORTAL KOMBAT STYLE)" with a per-frame caption under each pose (IDLE / CROUCH / LOAD / UPPERCUT / RECOVER / RETURN / BACK TO IDLE) — same sequence as the original gray-matte version, captions carried over.

| File | Caption |
|---|---|
| kfv_crouchinguppercut_01.png | kfvchar, side view, standing fighting stance, idle before the uppercut |
| kfv_crouchinguppercut_02.png | kfvchar, side view, crouching down, transitioning to a low stance |
| kfv_crouchinguppercut_03.png | kfvchar, side view, coiled low crouch, fist chambered, loading power |
| kfv_crouchinguppercut_04.png | kfvchar, side view, crouching uppercut impact, fist thrust overhead, dust and rock burst |
| kfv_crouchinguppercut_05.png | kfvchar, side view, recovering, low crouch, hand on the ground |
| kfv_crouchinguppercut_06.png | kfvchar, side view, returning crouch, rising up |
| kfv_crouchinguppercut_07.png | kfvchar, side view, standing fighting stance, back to idle |

## Roundhouse — 12 frames, split from the regenerated (transparent) `KFV_Roundhouse.png` (1536×1024, RGBA transparent, 2 rows — see flag #4)
Row-major order (top row 1–6, bottom row 7–12), matching the source's own numbering. Same 360°-spin choreography as the original gray-matte version (frames 3, 5, 6, 7, 8 are back view) — captions carried over.

| File | Caption |
|---|---|
| kfv_roundhouse_01.png | kfvchar, side view, roundhouse kick windup, fighting stance, fists raised in guard |
| kfv_roundhouse_02.png | kfvchar, side view, roundhouse kick windup, knee chambered, beginning to turn |
| kfv_roundhouse_03.png | kfvchar, back view, spinning into the kick, motion line trailing at the feet |
| kfv_roundhouse_04.png | kfvchar, side view, roundhouse kick impact, leg fully extended high, spinning strike, motion swirl |
| kfv_roundhouse_05.png | kfvchar, back view, roundhouse kick, leg lowering after the spin |
| kfv_roundhouse_06.png | kfvchar, back view, roundhouse kick, regaining balance after the spin |
| kfv_roundhouse_07.png | kfvchar, back view, standing fighting stance, facing away |
| kfv_roundhouse_08.png | kfvchar, back view, stepping, beginning to turn back around |
| kfv_roundhouse_09.png | kfvchar, side view, fighting stance, turned back to face forward |
| kfv_roundhouse_10.png | kfvchar, side view, fighting stance, settling after the spin kick |
| kfv_roundhouse_11.png | kfvchar, side view, fighting stance, settling after the spin kick |
| kfv_roundhouse_12.png | kfvchar, side view, fighting stance, return to guard |

## Dodge — 6 frames, from `Assets/_Project/Sprites/DodgeRoll/KFV_DodgeRoll_01-06.png` (the actual in-game sprites, real RGBA transparency — see flag #4)

| File | Caption |
|---|---|
| kfv_dodge_01.png | kfvchar, side view, dodge roll, starting crouch, reaching toward the ground |
| kfv_dodge_02.png | kfvchar, side view, dodge roll, dropping into a low four-point crouch |
| kfv_dodge_03.png | kfvchar, side view, dodge roll, inverted mid-tumble, tucked, black pants foreshortened out of view |
| kfv_dodge_04.png | kfvchar, side view, dodge roll, fully inverted, legs overhead mid-roll |
| kfv_dodge_05.png | kfvchar, side view, dodge roll, prone on the ground, rolling out |
| kfv_dodge_06.png | kfvchar, side view, dodge roll, rising from the ground back into a crouch |

## DodgeForward — 4 frames, split from `KFV_Dodge_Forward.png` (2172×724, RGBA transparent, single row)
All 4 frames are near-identical (same held lunge pose — likely a short hold/loop rather than a moving sequence).

| File | Caption |
|---|---|
| kfv_dodgeforward_01.png | kfvchar, side view, forward dodge, low lunging crouch, fists raised in guard, leaning forward |
| kfv_dodgeforward_02.png | kfvchar, side view, forward dodge, low lunging crouch, fists raised in guard, leaning forward |
| kfv_dodgeforward_03.png | kfvchar, side view, forward dodge, low lunging crouch, fists raised in guard, leaning forward |
| kfv_dodgeforward_04.png | kfvchar, side view, forward dodge, low lunging crouch, fists raised in guard, leaning forward |

## DodgeBack — 4 frames, split from `KFV_Dodge_Back.png` (2172×724, RGBA transparent, single row)
All 4 frames are near-identical (same running pose repeated).

| File | Caption |
|---|---|
| kfv_dodgeback_01.png | kfvchar, side view, backward dodge, running stride, fists raised, retreating |
| kfv_dodgeback_02.png | kfvchar, side view, backward dodge, running stride, fists raised, retreating |
| kfv_dodgeback_03.png | kfvchar, side view, backward dodge, running stride, fists raised, retreating |
| kfv_dodgeback_04.png | kfvchar, side view, backward dodge, running stride, fists raised, retreating |

## JumpPunch — 1 frame, copied as-is from `KFV_Jump_Punch.png` (749×587, RGBA transparent) — genuinely a single pose

| File | Caption |
|---|---|
| kfv_jumppunch.png | kfvchar, three-quarter view, flying punch, diving through the air, fist thrust forward, motion lines trailing, legs tucked behind |

## Fireball — 4 frames, split from `KFV_Fireball.png` (1774×887, RGBA transparent, single row)
This is the character's throwing pose (not the projectile itself, per task scope — projectile sprites live under `Art Assets/Player/Projectiles/` and were correctly excluded).

| File | Caption |
|---|---|
| kfv_fireball_01.png | kfvchar, side view, fireball throw windup, crouched stance, one hand reaching back, charging energy |
| kfv_fireball_02.png | kfvchar, side view, fireball throw, both hands drawn to the chest, cupped, charging energy |
| kfv_fireball_03.png | kfvchar, side view, fireball throw, hands crossed and thrust forward, releasing energy |
| kfv_fireball_04.png | kfvchar, side view, fireball throw, hands extended forward, follow-through after releasing energy |

## Crouch — 2 frames, copied as-is from `Crouch/KFV_Crouch01.png`, `KFV_Crouch02.png`
Near-identical to each other — see flag #5.

| File | Caption |
|---|---|
| kfv_crouch_01.png | kfvchar, side view, low crouch, one hand on the ground, other fist raised |
| kfv_crouch_02.png | kfvchar, side view, low crouch, one hand on the ground, other fist raised |

## CrouchPunch — 1 frame, copied as-is from `Crouch Punch/KFV_Crouch_Punch.png`

| File | Caption |
|---|---|
| kfv_crouchpunch.png | kfvchar, side view, crouching punch, lunging forward, arm extended low, other fist pulled back at the hip |

## CrouchKick — 1 frame, copied as-is from `Crouch Kick/KFC_Crouch_Kick.png`
(Note: source filename has a typo, `KFC_` instead of `KFV_` — cosmetic, doesn't affect output naming.)

| File | Caption |
|---|---|
| kfv_crouchkick.png | kfvchar, side view, crouching low kick, leg swept out low, hand on the ground for balance |

## WallSlide — 2 frames, copied as-is from `WallSlide/KFV_WallSlide01.png`, `KFV_WallSlide02.png`
Same pose in both — see flag #5; only the dust-particle positions differ between the two.

| File | Caption |
|---|---|
| kfv_wallslide_01.png | kfvchar, side view, sliding down a wall, back against the wall, hand pressed out, dust trailing |
| kfv_wallslide_02.png | kfvchar, side view, sliding down a wall, back against the wall, hand pressed out, dust trailing |

---

## Per-action count summary

| Folder | Count | Source handling |
|---|---|---|
| Idle | 6 | split |
| Walk | 6 | copied from `Assets/_Project/Sprites/Walk/` (in-game asset, see flag #4) |
| Jump | 6 | copied from `Assets/_Project/Sprites/Jump/` (in-game asset, see flag #4) |
| Run | 6 | copied (1 source file excluded, see flag #3) |
| Punch | 3 | split |
| FrontKick | 4 | split |
| JumpKick | 3 | split |
| CrouchingUppercut | 7 | split from regenerated transparent source (see flag #4) |
| Roundhouse | 12 | split from regenerated transparent source (see flag #4) |
| Dodge | 6 | copied from `Assets/_Project/Sprites/DodgeRoll/` (in-game asset, see flag #4) |
| DodgeForward | 4 | split |
| DodgeBack | 4 | split |
| JumpPunch | 1 | copied |
| Fireball | 4 | split |
| Crouch | 2 | copied |
| CrouchPunch | 1 | copied |
| CrouchKick | 1 | copied |
| WallSlide | 2 | copied |
| **Total** | **78** | AxeKick left out for now (frame 4 proportion issue on the key pose); PunchKick removed as a stale duplicate (see flags #2, #4) |

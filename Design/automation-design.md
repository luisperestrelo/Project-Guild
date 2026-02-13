# Automation System — Design Document

Status: Design phase. Expect heavy iteration and refactoring as we implement and playtest.

## Table of Contents
- [Core Philosophy](#core-philosophy)
- [Architecture Overview](#architecture-overview)
- [Core Design Rules](#core-design-rules)
- [System Primitives](#system-primitives)
  - [Conditions](#conditions)
  - [Actions](#actions)
  - [Targeting (Combat)](#targeting-combat)
  - [Positioning (Combat)](#positioning-combat)
  - [Rules](#rules)
  - [Rulesets](#rulesets)
- [Evaluation Model](#evaluation-model)
- [Decision Log](#decision-log)
- [Logbook (Player Scratchpad)](#logbook-player-scratchpad)
- [Templates and Scaling](#templates-and-scaling)
- [Raid Logistics: Caravan System](#raid-logistics-caravan-system)
- [Phase Scoping](#phase-scoping)
- [Scenarios and Usage Patterns](#scenarios-and-usage-patterns)
- [Open Questions](#open-questions)
- [Research Findings](#research-findings)

---

## Core Philosophy

- Must be **fun, extensive, and feel good** to use
- Easy to get started, **never feel like programming**
- Complex systems possible for advanced players, **but not required**
- Most players aren't programmers — **UX is paramount**
- Expect heavy iteration and refactoring throughout the game's lifecycle
- Design opinions may evolve during development — stay flexible
- **Automation IS the game** — it's the primary gameplay interface, not a QoL feature
- The core fantasy is **"coach, not player"** — the player is a raid leader who observes, diagnoses, and adjusts

## Architecture Overview

### Hybrid System: Two Layers

**Micro layer (Priority Rules)** — Phase 3+
- IF condition THEN action, evaluated top-to-bottom, first match wins
- Governs how a runner behaves *during* a task
- Same engine for gathering AND combat — combat just adds more condition/action types
- Reusable across tasks
- For combat: two separate sub-lists (Targeting rules + Ability rules)

**Macro layer (Task Queue)** — Phase 7
- Determines *what task* to do next
- A task priority list with conditions
- Handles sequential multi-step loops (raid → deposit → restock → raid)
- Handles time/effort splitting between activities (alternate between fishing and goblin farming)
- Handles conditional task switching (fish until bank has 50 salmon, then switch to goblins)

## Core Design Rules

### "If something is done by micromanaging, then that something is bad for the game, or our automation is bad."
A well-configured automation setup should produce results indistinguishable from (or negligibly worse than) active micromanagement. If it can't, either the automation needs more expressiveness or the mechanic that rewards micromanaging needs redesign. Tiny incidental advantages (equipping a sword drop 30s earlier) are acceptable. Systematic advantages are not.

### Gear locked during raid encounters
Freely swappable everywhere else. Prevents lame raid gear-swap optimization (e.g., swapping to tank gear before every big AoE — busywork, not fun). Preserves interesting overworld trade-offs (mining gear vs combat gear at dangerous nodes).

### "Finish current trip" toggle
Default: ON. When a condition triggers a task switch, the runner completes their current inventory cycle (fills up, deposits) before switching. Players think in trips, not ticks. Can be overridden for urgent switches (FleeToHub ignores this).

### Always compute, never cache
All derived values computed fresh each tick from current state. Conditions always read live state. No snapshotting. Buffs, gear changes, level-ups reflected immediately. (Prevents WoW-vanilla DoT snapshotting exploits.)

### Progressive disclosure
Don't front-load complexity. The automation system is invisible in the first hour (defaults just work). The player's first conscious interaction is combat-focused (the "Culling Frost moment"). Gathering customization is discovered organically. Gate condition/action TYPES as progression rewards (FFXII model), NOT rule slots (DA:O model — universally hated).

---

## System Primitives

### Conditions

A condition is a boolean check against live game state.

#### Gathering / General Conditions
| Condition | Parameters | Example |
|-----------|-----------|---------|
| InventoryFull | — | "When inventory is full" |
| InventorySlots | operator, value | "When < 5 slots remaining" |
| InventoryContains | itemId, operator, count | "When carrying >= 10 Iron Ore" |
| BankContains | itemId, operator, count | "When bank has < 50 Salmon" |
| SkillLevel | skillId, operator, level | "When Mining >= 45" |
| RunnerState | state | "When idle" |
| AtNode | nodeId | "When at Copper Mine" |
| SelfHP | operator, value | "When HP < 30%" |
| Always | — | Fallback, always true |

#### Combat Conditions (Phase 5)
| Condition | Parameters | Example |
|-----------|-----------|---------|
| TargetHP | operator, value | "When target HP < 35%" |
| BossHP | operator, value | "When boss HP < 55%" |
| AllyHP | filter, operator, value | "When tank HP < 50%" |
| AddCount | operator, value | "When add count > 0" |
| CooldownAvailable | abilityId | "When Barrier is off cooldown" |
| TargetIsAttacking | allyFilter | "When target is attacking healer" |
| UntauntedEnemyExists | — | "When an untaunted enemy exists" |
| TimeSincePull | operator, seconds | "When > 30s since pull" |
| BossCasting | abilityId? | "When boss is casting (optionally specific ability)" |

#### Condition Composition
- **Single conditions** work standalone (most gathering rules)
- **AND composition** supported: "BossHP < 55% AND CooldownAvailable(Barrier)"
- **OR and nesting**: Deferred. Most OR cases can be handled by multiple separate rules. Add later if playtesting reveals a need.

### Actions

#### Gathering / General Actions
| Action | Parameters | Notes |
|--------|-----------|-------|
| TravelTo | nodeId | Travel to a specific node |
| GatherAt | nodeId, gatherableIndex? | Travel to node and gather (optional: specific resource) |
| ReturnToHub | — | Travel back to hub |
| DepositAndResume | — | Deposit inventory at hub, then return to previous task |
| Idle | — | Do nothing |
| FleeToHub | — | Emergency return, ignores "finish current trip" |

#### Combat Actions (Phase 5)
| Action | Parameters | Notes |
|--------|-----------|-------|
| UseAbility | abilityId | Cast ability on current target |
| UseConsumable | itemId | Use a consumable from inventory |
| Taunt | targetFilter? | Taunt current/specified target |
| MoveToZone | zoneId | Reposition to a different arena zone |

### Targeting (Combat)

Targeting is a **separate priority list** from abilities. This avoids combinatorial explosion.

**Targeting rules** determine WHO the runner focuses on.
**Ability rules** determine WHAT the runner does to their current target.

These evaluate independently. Targeting picks the target, ability rules pick the action.

#### Example: Mage
```
Targeting:
  1. If add count > 0 → lowest HP add
  2. Always → boss

Abilities:
  1. If target HP < 35% → Culling Frost
  2. Always → Fireball
```

Four rules total. Handles all combinations (adds/no adds × above/below 35% HP) cleanly.

#### Example: Healer
```
Targeting:
  1. If boss targeting tank AND tank HP < 50% → tank
  2. Always → lowest HP ally

Abilities:
  1. If allies below 70% >= 2 → Circle of Healing
  2. If target HP < 30% → Greater Heal
  3. Always → basic heal
```

#### Example: Tank
```
Targeting:
  1. If untaunted enemy attacking healer → that enemy
  2. If untaunted enemy exists → that enemy
  3. Always → boss

Abilities:
  1. If self HP < 60% → Shield Block
  2. Always → auto-attack
```

#### Why Separate Lists?
Without separation, the mage would need:
- "If adds exist, cast Fireball on lowest HP add"
- "If adds exist AND add HP < 35%, cast Culling Frost on add"
- "If no adds, cast Fireball on boss"
- "If no adds AND boss HP < 35%, cast Culling Frost on boss"

That's 4+ rules with duplicated logic vs 4 clean independent rules. Separation scales much better as abilities and targeting options grow.

### Positioning (Combat)

**Zone-based positioning**. Each raid arena has a handful of defined zones (e.g., melee range, ranged, left flank, right flank, safe zone, boss-specific zones like "Dragonling Spawning Pool").

- Zones are **authored per encounter**, not a generic grid. Each boss arena can have unique zones with unique names and spatial meaning.
- Moving between zones costs time (ticks) — creates a real trade-off between safety and uptime.
- `MoveToZone` is a combat action, usable in ability rules.
- Visual: players see runners physically reposition in the 3D scene, making positioning problems diagnosable ("I can SEE Tom didn't move out of the cleave").

#### Example: Dodging a Frontal Cleave
```
Melee DPS Abilities:
  1. If boss casting Cleave → MoveToZone(flank)
  2. If not in zone(melee) AND boss not casting → MoveToZone(melee)
  3. If target HP < 35% → Culling Frost
  4. Always → Fireball
```

The DPS backs off during Cleave (losing some uptime), then moves back in. The tank stays in melee (he can take it, healer keeps him up). This is a coaching decision: is the DPS uptime loss worth the survival, or should the healer just heal through the cleave?

#### Boss Design Tools via Zones
- Frontal cleave → hits melee zone
- Ground AoE → hits a specific zone for N ticks ("don't stand in fire")
- Knockback → forces runners out of their zone
- "Stack" mechanics → reward/require runners to be in the same zone
- Phase transitions → change which zones are safe
- Spawning areas → enemies appear in a specific zone (position a tank there preemptively)

### Rules

A **Rule** is:
- **Condition**: A boolean check (possibly compound with AND)
- **Action**: What to do when the condition matches
- **Enabled/Disabled toggle**: Lets the player deactivate a rule without deleting it
- **"Finish current trip" toggle**: For task-switching rules (gathering). Default: ON.
- **Optional name/label**: Player can annotate rules (e.g., "switch to mithril at 45")

Rules are evaluated **top-to-bottom, first match wins**. A higher-priority rule that matches will prevent lower-priority rules from firing.

#### Rule Suppression Pattern
Higher-priority rules can implicitly suppress lower ones. Example: suppressing Circle of Healing during a specific boss HP window:
```
1. If BossHP < 52% AND CooldownAvailable(Barrier) → Cast Barrier
2. If BossHP < 55% AND BossHP > 49% → basic heal          ← suppresses CoH
3. If allies below 70% >= 2 → Circle of Healing
4. Always → basic heal
```
Rule 2 matches in the 55-49% window, catching before CoH (rule 3) can fire. After 49%, rule 2 stops matching and CoH resumes.

If playtesting shows players struggling with this pattern, we can add a convenience feature: per-rule "only active when <condition>" toggle. But the underlying engine stays the same.

### Rulesets

A **Ruleset** is a collection of rules assigned to a runner:
- **Gathering runners**: One ordered list of rules
- **Combat runners**: Two ordered lists (Targeting + Abilities)
- Saveable as a **template** ("Miner default", "Raid Healer - Troll Caverns")
- **Copyable** between runners
- **Per-runner** (each runner has their own, but can start from a template)
- Future: import/export for community sharing

---

## Evaluation Model

### When Rules Get Checked

**Event-driven triggers:**
- Arrived at node
- Inventory changed
- Skill leveled up
- HP changed (self or observed ally/enemy)
- Enemy spawned / died
- Boss HP crossed a threshold
- Ability came off cooldown
- Runner state changed

**Periodic safety net:** Every ~10 ticks (1 second) to catch anything events missed.

**First match wins:** Iterate top-to-bottom, first rule whose condition is true fires.

### Evaluation Cadence
- **Gathering rules**: Only fire on significant state changes. Low frequency.
- **Combat rules**: May fire every tick or every few ticks. The situation changes rapidly in combat. Same engine, higher trigger frequency.
- This is a tuning question, not an architecture question.

### BeginAutoReturn Refactor
Currently hardcoded in TickGathering: when inventory fills, `BeginAutoReturn()` is called directly. After Phase 3:
- `TickGathering` publishes `InventoryFull` event but does NOT call `BeginAutoReturn`
- The default automation ruleset has: `IF InventoryFull THEN DepositAndResume`
- Players can override with any behavior they want
- Mechanical helpers (`DepositAndReturn`, `StartTravelInternal`, `ResumeGathering`) stay — they're the *how*. Automation is the *when/why*.

---

## Decision Log

System-generated record of what happened and why. Core gameplay tool for the coaching loop.

### What Gets Recorded Per Entry
- **Timestamp**: Tick number + real time (or time since pull for combat)
- **Runner**: Who did it
- **Rule that fired**: Name/number + full condition text
- **Actual values**: "TargetHP was 32%, threshold was 35%"
- **Action taken**: What the runner did
- **Target**: Who/what they did it to (if applicable)

### Must Support
- **Filtering**: By runner, time range, event type (damage, healing, movement, deaths, ability use)
- **Quick scanning**: Player needs to find "what happened at 0:22 when adds spawned" in seconds
- **Color-coding / icons**: By event type for visual scannability
- **Combat replay context**: The log is most useful when reviewing a failed raid attempt

### Example Log Entries
```
0:14  [Tank] Shield Block → Self           Rule #2: Self HP (58%) < 60%
0:15  [Healer] Heal → Tank                 Rule #1: Lowest ally HP (Tank at 42%)
0:22  [Boss] Summoned Troll Add x2
0:23  [Mage] Fireball → Troll Add #1       Rule #1: Add count (2) > 0, target: lowest HP add
0:24  [Troll Add #2] Attack → Healer
0:25  [Healer] Heal → Self                 Rule #1: Lowest ally HP (Self at 67%)
```

---

## Logbook (Player Scratchpad)

Separate from the decision log. This is the player's personal notes tool.

- **Always visible** during combat (can be toggled)
- **Real-time note-taking** — player writes observations as the fight happens, not just after
- **Auto-opens** for the current encounter
- **Tabs / organization**: Player organizes however they want — per boss, per attempt, per strategy
- **Persistent**: Notes are saved, available across sessions
- Essentially a **glorified in-game Notepad** that's context-aware

### Example Usage
```
[Troll Chieftain - Attempt 3]
- Adds spawn at 75% HP, not time-based
- Cleave hits melee zone - Tank can eat it, DPS should dodge
- Enrage at 25% - healer needs to save big CD for this
- TODO: Try having mage save Pyroblast for enrage phase
```

---

## Templates and Scaling

With 15-20+ runners, per-runner management breaks down. The system needs:

### Templates / Presets
- Save any ruleset as a named template
- Apply template to new runners or existing runners
- Built-in defaults: "Gatherer - Basic", "Miner", "Raid Healer", "Raid Tank"
- Player-created templates
- Templates are starting points, not linked — editing a template doesn't change runners already using it (unless we decide otherwise)

### Copy / Paste
- Copy a runner's full ruleset (or just targeting, or just abilities)
- Paste onto another runner
- Bulk paste onto multiple runners

### Team-Level Configuration
- Raid teams share a macro loop (deposit → restock → re-enter)
- Set up the macro loop once for the team, not per-runner
- Individual runners still have their own micro rules (combat behavior)

---

## Raid Logistics: Caravan System

### Problem
Long travel to raids would destroy the coaching iteration loop. Can't have 20+ minute travel between each retry.

### Solution: Caravan
- When sending a team to a raid, they bring a **caravan** (local storage at the raid entrance)
- Caravan holds: gear sets, consumables, space for loot
- **Limited capacity** — progression knob (bigger caravans as an upgrade)
- After a wipe, **raiders respawn at the raid entrance**, not at Hub
- Forced **downtime between attempts (30-60s)** — enough to think, not enough to kill momentum
- If player is actively watching: a "Ready" button, doesn't auto-start until player clicks (or toggle for this)
- If auto-farming: auto-starts after timer

### Bank Access
- **Withdraw-only bank access** at raid entrance as QoL safety net for forgotten items
- Limited to prevent abuse (details TBD — cooldown, gold cost, daily limit, etc.)
- **Cannot deposit** at raid entrance — loot stays in caravan
- This prevents: gatherers abusing raid entrances as deposit points, raiders never needing to go home

### Why Not Full Bank Access?
- Undermines travel mattering entirely
- Raiders would never need to return to Hub
- Distance becomes a one-time cost, not an ongoing consideration
- Creates abuse vectors (any runner depositing at any raid entrance)

### Travel Matters Because
- Loot fills caravan → must travel home to deposit → round-trip cost proportional to distance
- Distant raid = more overhead per resupply cycle
- Nearby raid = cheaper logistics
- Strategic consideration: a nearby raid at 70% success rate might be more efficient than a distant raid at 90%

### Caravan UX
- **Per-raid saved loadouts**: Caravan remembers what you packed for each raid. One click to re-deploy.
- **Auto-pack button** (not default): Available for convenience, but caravan starts empty to incentivize players to think about what they need.
- First time: player spends a minute thinking about what to bring. Every subsequent trip: one click.

### Overnight Farming Macro Loop
```
Travel to raid (once) →
  [Raid → restock from caravan → wait downtime → raid → repeat]
  → caravan loot full OR consumables depleted →
Travel home → deposit loot → restock caravan →
Travel back → repeat outer loop
```

---

## Phase Scoping

### Phase 3: Non-Combat Automation (Next Up)
- Conditions: InventoryFull, InventorySlots, InventoryContains, BankContains, SkillLevel, RunnerState, AtNode, SelfHP, Always
- Actions: TravelTo, GatherAt, ReturnToHub, DepositAndResume, Idle, FleeToHub
- AND composition on conditions
- Single ruleset per runner (no targeting layer)
- Event-driven evaluation + periodic safety net
- Default ruleset for new runners (InventoryFull → DepositAndResume, Always → keep gathering)
- BeginAutoReturn refactor (remove hardcoded call, replace with default rule)
- Decision log (simplified — gathering events only)
- Debug UI for rule management
- Templates / copy-paste
- Tests for the full evaluation pipeline

### Phase 5: Combat Automation (Future)
- Adds combat conditions: TargetHP, BossHP, AllyHP, AddCount, CooldownAvailable, TargetIsAttacking, UntauntedEnemyExists, TimeSincePull, BossCasting
- Adds combat actions: UseAbility, UseConsumable, Taunt, MoveToZone
- Targeting as a separate priority list from abilities
- Zone-based positioning (per-encounter authored zones)
- Higher-frequency evaluation for combat
- Decision log: full combat logging

### Phase 7: Macro Layer (Future)
- Task queue / priority list determining what task to do next
- Sequential multi-step loops (raid → deposit → restock → raid)
- Conditional task switching (fish until bank has 50 salmon, then goblins)
- Time/effort splitting between activities (alternate tasks)
- "Finish current trip" behavior on task switches
- Team-level macro rules for raid groups
- Conditions: resource thresholds, bank state, skill levels
- Fallback behaviors when a loop can't execute (out of health pots → send raiders to do something else)

---

## Scenarios and Usage Patterns

### Scenario 1: First Hour (Tutorial)
- 3 fixed starting pawns, default automation handles gather-deposit loop invisibly
- Player assigns runners to nodes, watches resources flow in
- **"Culling Frost moment"**: First unlocked combat ability obviously needs a conditional rule. Player's first conscious automation interaction. Automation reveals itself as the answer to a question the player is already asking.
- First raid wipe is intentional → prerecording/walkthrough teaches observe → diagnose → adjust loop
- 4th pawn awarded after tutorial → first "manage parallel activities" moment
- See Design/tutorial-flow.md for full detail

### Scenario 2: Mid-Game Gathering (5-8 Runners)
- Player optimizes resource income across multiple runners
- Most runners use default or slightly modified rulesets
- Key automation moments:
  - Threshold-based switching: "Mine iron until bank has 50, then switch to oak logs" (BankContains condition)
  - Skill milestone triggers: "Switch to Mithril Mine when Mining hits 45" (SkillLevel condition)
  - Dangerous gathering nodes: Runners with combat survival rules (SelfHP < 30% → FleeToHub) + gear swapping automation for overworld content
- Templates/copy-paste become important at scale
- Gathering is a background system the player monitors and occasionally tunes

### Scenario 3: First Real Raid (Coaching Loop)
- Player's first unassisted raid. Multiple wipe-adjust-retry cycles.
- Decision log is essential: player reads log to find "mage kept nuking boss while adds killed healer"
- Iterative improvement: 60% → 40% → 25% → kill across attempts
- Each adjustment is 1-2 rule changes (add targeting rule for adds, adjust heal priority, time a cooldown)
- "Best attempt" tracking for visible progress between wipes
- Logbook used in real-time during attempts to note observations

### Scenario 4: Endgame Raid Farming (Overnight Loop)
- Both layers working together: micro (combat rules) + macro (logistics loop)
- Caravan system handles raid entrance logistics
- Overnight summary on login: attempts, wins, wipes, loot collected, consumables burned
- Resource dependencies surface: raid team burns health pots → need a crafter keeping stock up → "systems feed systems"
- Fallback automation: if can't execute raid loop (no pots), runners switch to alternative tasks

---

## Open Questions

- **Gathering automation tutorial**: When/how does the player first consciously modify a gathering rule? Currently only combat rules are tutorialized. Gathering customization discovered organically. Might need a gentle nudge — TBD.
- **OR conditions**: Deferred. Revisit if playtesting shows AND-only is too limiting.
- **Behavior blocks/modes**: The "switch between complete behavior sets based on boss phase" pattern. Currently handled by rule suppression (higher-priority rules in an HP window). May need explicit blocks/modes if suppression pattern proves unintuitive.
- **Limited bank access specifics**: What makes the QoL withdraw-only access at raid entrances limited enough to prevent abuse? Cooldown, gold cost, daily limit, item count cap?
- **Caravan capacity tuning**: How many items? How does it scale with progression? What's the right number of attempts before needing to go home?
- **Downtime between raid attempts**: 30-60s range. Can it be reduced by upgrades? Should it scale with raid difficulty?
- **Automation as progression**: Which condition/action types are gated behind game progress? How are they unlocked?
- **Overnight summary / activity report**: What exactly does this show? How is it presented? How are rare/valuable drops highlighted?
- **Mobs at gathering nodes**: Most nodes safe, some high-value nodes have aggressive mobs? Or all mobs non-aggressive (only engage when attacked)?
- **Ability system**: Level-based unlocks vs itemized drops vs something else?

---

## Research Findings

Based on analysis of automation systems in FFXII, Dragon Age: Origins, RAID: Shadow Legends, Rimworld, Factorio, Melvor Idle, Screeps, and others.

### Universal Patterns
- Priority-ordered IF-THEN rules, top-to-bottom, first match fires. Every successful system converges here.
- **Decision transparency** is the #1 missing feature across all games in this space. Building it from day one is a competitive advantage.

### What Players Love
- Presets/templates (DA:O's "Healer default" universally praised)
- Import/export of configurations (WeakAuras + Wago.io beloved in WoW community)
- Automation as progression reward (FFXII: buy new condition types)
- Seeing a well-tuned setup execute flawlessly

### What Players Hate
- Slot-gating (DA:O starting with 2 slots — universally frustrating)
- Can't express AND conditions
- No visibility into WHY a pawn made a decision
- Per-pawn management that doesn't scale past 4-5 pawns
- "The fun part is 5 minutes of setup, then hours of boring watching" (FFXII criticism — our game avoids this because raids require active iteration)

### Key Lessons
- **Rimworld**: Layer simple systems rather than one complex system
- **Factorio V2**: Building blocks can be TOO simple — find the right granularity
- **Melvor Idle**: Players will automate whether you let them or not — embrace it
- **Screeps**: Full programming works for programmers, impenetrable for everyone else. Enable systems thinking WITHOUT requiring programming.
- **RAID:SL**: Simple priority (1st/2nd/3rd + Opener + Don't Use). Accessible but too limited for deep content.
- **"Automation works only when designed as the primary gameplay loop, not as QoL for existing games."** — This is exactly our case.

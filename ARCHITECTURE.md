# Project Guild — Codebase Architecture & Script Reference

This document explains what each script does, how they connect, and why things are structured the way they are. Think of it as the "talk me through the project" document.

---

## The Big Picture

The project is split into four layers, enforced at compile time by assembly definitions:

```
Simulation (pure C#, no UnityEngine)
    ↑
Data (ScriptableObjects — references Simulation + UnityEngine)
    ↑
Bridge (references Simulation + Data + UnityEngine)
    ↑
View (references Bridge + Simulation + UnityEngine)
```

**Simulation** is the brain. It knows about runners, skills, travel, gathering, combat rules — everything that answers "what is happening in the game world." It has zero Unity dependencies. It could run in a console app.

**Data** is the authoring layer. ScriptableObject wrappers that let designers edit simulation values in the Unity Inspector. Each SO type has a `To*()` method that converts to a plain C# struct/class for the simulation. The simulation never sees SOs — only the plain data they produce.

**Bridge** is thin glue. It drives the simulation forward using Unity's frame loop and handles things that need Unity APIs (file paths, Time.deltaTime, ScriptableObject loading).

**View** is MonoBehaviours that render the game — runner models, animations, camera, UI. The view never modifies simulation state directly — it goes through `AssignRunner()` on GameSimulation.

Data flows one direction: **Simulation produces state and events. Bridge ticks the simulation. View reads state and reacts to events.**

### The Sports Broadcast Analogy

Imagine a football game. The game itself (players, ball, rules, score) is the **Simulation**. The stadium clock that keeps the game moving is **SimulationRunner** (Bridge layer). The TV broadcast team is the **View** layer. The intercom system between the field and the broadcast booth is the **EventBus**.

The game doesn't know or care that it's being televised. It just plays. But whenever something happens (goal scored, foul called), someone on the field shouts into the intercom. The broadcast booth hears it and shows the replay, updates the scoreboard graphic, plays the crowd sound effect.

### The Tick Flow

Every Unity frame, the Bridge layer checks if enough real time has passed to fire one or more simulation ticks. Each tick processes all runners based on their state:

```
Unity Frame Loop (Update)
    │
    ▼
SimulationRunner.Update()              ← Bridge layer (MonoBehaviour)
    │  Accumulates Time.deltaTime
    │  While enough time has passed:
    │
    ▼
GameSimulation.Tick()                  ← Simulation layer (pure C#)
    │  TickCount++
    │  For each runner:
    │    ├─ Non-Idle? → EvaluateMacroRules("Tick") every tick
    │    │    └─ If Immediate rule fires → AssignRunner, skip rest of tick
    │    ├─ Idle? → ExecuteCurrentStep()
    │    │    ├─ EvaluateMacroRules (StepAdvance trigger)
    │    │    └─ Execute current step (TravelTo / Work / Deposit)
    │    │         └─ Work step → EvaluateMicroRules
    │    │              ├─ GatherHere(idx) → StartGathering
    │    │              ├─ FinishTask → advance past Work, ExecuteCurrentStep
    │    │              └─ NoMatch → Publish(NoMicroRuleMatched), stay idle
    │    ├─ Traveling? → TickTravel()
    │    │    ├─ Award Athletics XP (every tick)
    │    │    │    └─ Level up? → Publish(RunnerSkillLeveledUp)
    │    │    └─ Arrived? → set Idle, Publish(RunnerArrivedAtNode)
    │    │         ├─ EvaluateMacroRules (ArrivedAtNode trigger)
    │    │         └─ ExecuteCurrentStep (next step in task sequence)
    │    ├─ Gathering? → TickGathering()
    │    │    ├─ Award skill XP (every tick, decoupled from item production)
    │    │    │    └─ Level up? → Publish(RunnerSkillLeveledUp)
    │    │    └─ Tick accumulator full?
    │    │         ├─ Produce item → Publish(ItemGathered)
    │    │         └─ Inventory full? → Publish(InventoryFull)
    │    │    └─ ReevaluateMicroDuringGathering (every tick, not just after items)
    │    │         ├─ GatherHere(idx) → continue gathering (or switch resource)
    │    │         ├─ FinishTask → advance past Work, ExecuteCurrentStep
    │    │         │    (default micro: IF InventoryFull → FinishTask)
    │    │         └─ NoMatch → Publish(NoMicroRuleMatched), stay stuck
    │    └─ Depositing? → TickDepositing()
    │         └─ Done? → Publish(RunnerDeposited), set Idle
    │              ├─ Non-looping? → HandleSequenceCompleted (idle + macro re-eval)
    │              ├─ ApplyPendingTaskSequence (deferred macro rule)
    │              ├─ EvaluateMacroRules (DepositCompleted trigger)
    │              └─ ExecuteCurrentStep (wraps to TravelTo step)
    │  Publish(SimulationTickCompleted)
    │
    ▼
EventBus.Publish(event)                ← fires synchronously during tick
    │  For each subscriber of this event type:
    │    call handler(event)
    │
    ▼
View handlers run                      ← View layer (MonoBehaviours)
    e.g. Play animation, update UI label, move camera
    │
    ▼
Back to Tick() (continues processing)
```

### Script Roles at a Glance

| Script | Layer | Role |
|--------|-------|------|
| `GameSimulation.cs` | Simulation | The brain. Owns all state, processes ticks, macro/micro rule evaluation |
| `GameState.cs` | Simulation | The data. All saveable state lives here |
| `SimulationConfig.cs` | Simulation | The rulebook. All tuning values |
| `EventBus.cs` | Simulation | The intercom. Type-safe pub/sub |
| `SimulationEvents.cs` | Simulation | The vocabulary. Defines what events exist |
| `EventLogService.cs` | Simulation | The recorder. Subscribes to all events, ring buffer, collapsing, queries |
| `WorldMap.cs` | Simulation | The terrain. Nodes, edges, pathfinding |
| `Runner.cs` | Simulation | The adventurer. State, skills, inventory, ID refs into automation libraries |
| `GatherableConfig.cs` | Simulation | The resource. What a gatherable produces and costs |
| `RuleEvaluator.cs` | Simulation | The referee. Evaluates automation rules against context |
| `DecisionLog.cs` | Simulation | The playback. Ring-buffer log of macro + micro rule decisions |
| `SimulationRunner.cs` | Bridge | The clock. Drives ticks from Unity's frame loop |
| `SimulationConfigAsset.cs` | Data | The inspector. SO wrapper for config values |
| `ItemDefinitionAsset.cs` | Data | The catalog. SO wrapper for item definitions |
| `GatherableConfigAsset.cs` | Data | The field guide. SO wrapper for gatherable configs |
| `WorldNodeAsset.cs` | Data | The pin. SO wrapper for a world node |
| `WorldMapAsset.cs` | Data | The atlas. SO wrapper for the full world map |
| `GameBootstrapper.cs` | View | The director. Wires everything up, debug UI, node-click |
| `VisualSyncSystem.cs` | View | The cameraman. Keeps 3D visuals in sync with sim |
| `NodeMarker.cs` | View | Tags node GameObjects for click-to-select raycasting |
| `BankMarker.cs` | View | Tags the bank GameObject for click-to-open raycasting |

### The Conveyor Belt (Spiral of Death Protection)

Imagine a conveyor belt that delivers one box every 0.1 seconds (the tick rate). A worker (the CPU) unpacks each box. Normally, unpacking takes 0.05 seconds — plenty of time before the next box arrives.

But if a frame hitches (loading, GC spike), boxes pile up. Without protection, the worker tries to unpack ALL of them at once, which takes even longer, causing more boxes to pile up — a death spiral.

`SimulationRunner` caps ticks-per-frame at `_maxTicksPerFrame` (default 3). If more are owed, they're dropped. The simulation skips ahead rather than trying to catch up. For an idle game where precision isn't critical, this is the right trade-off.

---

## Assembly Definitions

| Assembly | Path | noEngineReferences | Purpose |
|----------|------|--------------------|---------|
| `ProjectGuild.Simulation` | `Scripts/Simulation/` | **true** | Pure C# game logic. Cannot reference UnityEngine — compiler enforced. |
| `ProjectGuild.Data` | `Scripts/Data/` | false | ScriptableObjects that reference simulation types. References Simulation. |
| `ProjectGuild.Bridge` | `Scripts/Bridge/` | false | Glue between Unity and Simulation. References Simulation + Data. |
| `ProjectGuild.View` | `Scripts/View/` | false | MonoBehaviours for rendering. References Simulation + Bridge. |
| `ProjectGuild.Tests.Editor` | `Scripts/Tests/Editor/` | false | NUnit edit-mode tests. References Simulation. Editor-only. |

---

## Scripts — Simulation Layer

### `Simulation/Core/SimulationConfig.cs`
All tunable gameplay values in one place. Travel speed, XP curve, passion multipliers, runner generation ranges, death penalty — everything that would otherwise be a magic number lives here. Tests create a default `new SimulationConfig()` to get sensible values. In Unity, a `SimulationConfigAsset` ScriptableObject feeds these values through the inspector.

Also contains:
- `GatheringSpeedFormula` enum — `PowerCurve` or `Hyperbolic`, selectable via config.

Key field groups:
- **Travel**: `BaseTravelSpeed`, `AthleticsSpeedPerLevel`, `AthleticsXpPerTick`
- **Skills/XP**: `PassionEffectivenessMultiplier`, `PassionXpMultiplier`, `XpCurveBase`, `XpCurveGrowth`
- **Runner Generation**: `MinStartingLevel`, `MaxStartingLevel`, `PassionChance`, `EasterEggNameChance`
- **Gathering**: `GlobalGatheringSpeedMultiplier`, `GatheringFormula`, `GatheringSpeedExponent`, `HyperbolicSpeedPerLevel`
- **Automation**: `DecisionLogMaxEntries` (default 2000), `EventLogMaxEntries` (default 500)
- **Items**: `ItemDefinitions[]` (populated from SOs at load time)
- **Inventory**: `InventorySize` (default 28)
- **Live Stats UI**: `LiveStatsXpDisplayWindowSeconds` (5.0f, how long XP bars stay visible), `LiveStatsMaxSkillXpBars` (4, max simultaneous bars)
- **Death**: `DeathRespawnBaseTime`, `DeathRespawnTravelMultiplier`

### `Simulation/Core/SkillType.cs`
Enum of all 15 skills (Melee through Athletics). Values are explicit integers because they're used as array indices — `Skills[(int)SkillType.Mining]` gives direct access. Extension methods (`IsCombat()`, `IsGathering()`, `IsProduction()`) classify skills by category without needing switch statements everywhere.

Skill categories:
- **Combat (7):** Melee, Ranged, Defence, Hitpoints, Magic, Restoration, Execution
- **Gathering (4):** Mining, Woodcutting, Fishing, Foraging
- **Production (3):** Engineering, PotionMaking, Cooking
- **Support (1):** Athletics

### `Simulation/Core/Skill.cs`
One skill on one runner. Holds level, accumulated XP, and whether the runner has passion for it. Key methods:
- `GetEffectiveLevel(config)` — the public-facing number that includes passion bonus (e.g., level 10 with passion = 10.5)
- `AddXp(baseXp, config)` — accumulates XP, applies passion multiplier, auto-levels up (possibly multiple times), returns true if leveled
- `GetXpForLevel(level, config)` — exponential XP curve: `XpCurveBase * XpCurveGrowth^level`. At growth=1.104 (OSRS-like), XP doubles every ~7 levels. "92 is half of 99."
- `GetLevelProgress(config)` — 0-1 float for UI progress bars
- `GetXpToNextLevel(config)` — total XP needed for next level

All methods take a `SimulationConfig` parameter so tuning values are never hardcoded.

### `Simulation/Core/Runner.cs`
The core data class for a runner. Identity (ID, name), current state (Idle/Traveling/Gathering/Depositing), location (which world node), 15 skills, an `Inventory` (28-slot OSRS-style), a `TravelState` for when they're moving, a `GatheringState` for when they're gathering, a `DepositingState` for when depositing. Automation state uses **string ID references** into global libraries on GameState: `TaskSequenceId` (current step sequence), `PendingTaskSequenceId` (deferred macro rule result), `MacroRulesetId` (macro rules). `TaskSequenceCurrentStepIndex` tracks per-runner progress through the shared task sequence template. Micro rulesets are **per-Work-step** (on `TaskStep.MicroRulesetId`), not per-runner. The constructor initializes all skills to level 1 — actual values come from RunnerFactory. Legacy direct-object fields (`TaskSequence`, `MacroRuleset`, `MicroRuleset`) exist for save migration.

**`TravelState`** — tracks from/to nodes, total distance, distance covered, and a `Progress` property (0.0-1.0). Also has optional `StartWorldX`/`StartWorldZ` float fields — when set, the view layer uses these as the travel start position instead of the FromNode's position. Used by the redirect system to prevent visual snapping when a runner changes destination mid-travel. Null means "use the FromNode position as usual."

**`GatheringState`** — tracks the node being gathered, a `GatherableIndex` (which gatherable in the node's array), a tick accumulator, the pre-calculated ticks required per item, and a `GatheringSubState` enum (`Gathering`, `TravelingToBank`, `TravelingToNode`). The sub-state and gatherable index persist across the auto-return deposit loop so the runner knows what it was doing and which resource it was working on.

**`ActiveWarning`** — human-readable string, null when runner is operating normally. Set when the runner gets stuck: `GatheringFailed` (NoGatherablesAtNode, NotEnoughSkill) or `NoMicroRuleMatched` (empty ruleset, no rule matched). Cleared at every productive state transition: `AssignRunner`, `StartGathering`, `StartTravel` (all 3 sites), `ExecuteDepositStep`. The portrait warning badge reads this field — red dot visible when non-null, tooltip shows the message.

### `Simulation/Core/RunnerFactory.cs`
Creates runners via three strategies:
- **`Create(rng, config)`** — fully random, for standard gameplay acquisition. Skills randomized within config range, each skill rolls for passion independently.
- **`CreateFromDefinition(def)`** — exact stats, for hand-tuned starting runners. Uses `RunnerDefinition` with a fluent `.WithSkill()` API for readability.
- **`CreateBiased(rng, config, bias)`** — semi-deterministic, for scripted moments (tutorial reward runner). Starts random then applies constraints via `BiasConstraints`: `PickOneSkillToBoostedAndPassionate` (pick one from a pool for boost + passion), `BoostedSkills` (upper-half starting levels), `WeakenedSkills` (lower-half levels), `WeakenedNoPassionSkills` (lower-half + no passion), `ForcedName` (optional name override).

Also handles name generation with an easter egg name system (configurable chance to roll a special full name instead of random first+last).

### `Simulation/Core/EventBus.cs`
Publish/subscribe system. Dictionary of `Type -> List<Delegate>`. When simulation code calls `Publish(new RunnerArrivedAtNode { ... })`, all handlers registered for that event type fire immediately. Events are structs (not classes) to avoid heap allocations and GC pressure. Iteration is backwards to safely handle unsubscription during callbacks.

### `Simulation/Core/SimulationEvents.cs`
Event definitions — small structs carrying just the data a listener needs:

| Event | Data | Fired when |
|-------|------|------------|
| `RunnerCreated` | RunnerId, RunnerName | Runner added to simulation |
| `RunnerSkillLeveledUp` | RunnerId, Skill, NewLevel | Any skill levels up |
| `RunnerStartedTravel` | RunnerId, FromNodeId, ToNodeId, EstimatedDurationSeconds | Runner begins traveling (including redirects) |
| `RunnerArrivedAtNode` | RunnerId, NodeId | Runner arrives at destination |
| `GatheringStarted` | RunnerId, NodeId, ItemId, Skill | Runner begins gathering |
| `GatheringFailed` | RunnerId, NodeId, ItemId, Skill, RequiredLevel, CurrentLevel | Runner tried to gather but doesn't meet level requirement |
| `ItemGathered` | RunnerId, ItemId, InventoryFreeSlots | One item produced |
| `InventoryFull` | RunnerId | Inventory filled |
| `RunnerDeposited` | RunnerId, ItemsDeposited | Items deposited at bank |
| `TaskSequenceChanged` | RunnerId, TargetNodeId, Reason | Runner's task sequence changed (manual or macro rule) |
| `TaskSequenceStepAdvanced` | RunnerId, StepType, StepIndex | Runner advanced to next step in task sequence |
| `TaskSequenceCompleted` | RunnerId, SequenceName | Non-looping task sequence finished all steps |
| `AutomationRuleFired` | RunnerId, RuleIndex, RuleLabel, TriggerReason, ActionType, WasDeferred | A macro rule's conditions matched |
| `AutomationPendingActionExecuted` | RunnerId, ActionType, ActionDetail | A deferred macro action executed |
| `NoMicroRuleMatched` | RunnerId, RunnerName, NodeId, RulesetIsEmpty, RuleCount | Micro rules don't cover current situation |
| `SimulationTickCompleted` | TickNumber | Every tick |

### `Simulation/Core/GameState.cs`
Root of all saveable state. Holds: list of runners, tick count, total elapsed time, world map, guild Bank, automation DecisionLog, and three **global automation libraries**: `TaskSequenceLibrary`, `MacroRulesetLibrary`, `MicroRulesetLibrary` (all `List<T>` indexed by string ID). Runners reference library entries by ID. Editing a library entry immediately affects all runners/sequences using it. Serializing this one object captures the entire game world.

### `Simulation/Core/GameSimulation.cs`
The orchestrator. Owns `GameState`, `EventBus`, `SimulationConfig`, `ItemRegistry`, and `EventLogService`. Its `Tick()` method processes all runners based on their current state.

`StartNewGame()` has two overloads: one taking explicit `RunnerDefinition[]` and an optional `WorldMap` for full control, one using default placeholders for quick testing. During startup: creates or accepts the world map, and populates the `ItemRegistry` from config.

**The single entry point for runner control is `AssignRunner(runnerId, taskSequence, reason)`**. It cancels current activity, sets the new task sequence, publishes `TaskSequenceChanged`, and starts executing the first step via `ExecuteCurrentStep`. There is no `CommandGather` — what a runner does is entirely determined by their task sequence + micro rules.

**Travel:** `StartTravelInternal()` is the shared helper for starting travel. It handles two cases:

1. **Normal travel (runner is Idle):** Finds the path via `WorldMap.FindPath()`, sets the runner to `Traveling` state, publishes `RunnerStartedTravel`.

2. **Redirect (runner is already Traveling):** Calculates the runner's current virtual position by lerping between the start and destination using travel progress. Computes the Euclidean distance from that virtual position to the new target. Creates a new `TravelState` with `StartWorldX`/`StartWorldZ` set to the virtual position, so the view can lerp smoothly from the runner's actual position to the new destination without snapping. Chained redirects work correctly because each redirect reads the previous `StartWorldX`/`StartWorldZ` override when computing the virtual position.

`GetTravelSpeed()` calculates Athletics-based movement speed from config values. `TickTravel()` advances runners along their path each tick and awards Athletics XP every tick (same decoupling as gathering: speed = getting there faster, XP = progression).

**Gathering:** `StartGathering(runner, gatherableIndex, gatherableConfig)` validates skill level (publishes `GatheringFailed` if not met), calculates ticks required from the skill speed formula, and puts the runner into the `Gathering` state. Which resource to gather is determined by micro rules, not by a command parameter. `TickGathering()` awards XP every tick (decoupled from item production), accumulates ticks, produces an item when the threshold is reached, and recalculates speed on level-up.

**XP decoupling:** XP is awarded per tick of gathering/traveling, not per item produced or trip completed. Speed affects economic output (items per trip). XP rate is determined by which resource you're grinding. This means a faster gatherer produces more items but doesn't level faster.

**Gathering speed formulas:**
- **PowerCurve** (default): `speedMultiplier = effectiveLevel ^ exponent`. Higher levels are proportionally more impactful. Tuning: `BaseTicks = DesiredSecondsPerItem × TickRate × MinLevel ^ Exponent`.
- **Hyperbolic**: `speedMultiplier = 1 + (effectiveLevel - 1) × perLevelFactor`. Diminishing returns — early levels feel most impactful.

**Task Sequence System:** Task sequences live in a **global library** on GameState. Runners hold a `TaskSequenceId` string reference and a `TaskSequenceCurrentStepIndex` for per-runner progress. A `TaskSequence` is a list of `TaskStep`s (TravelTo, Work, Deposit) with an `Id` and `Name`. Looping sequences wrap around; non-looping sequences complete and fire `TaskSequenceCompleted`, letting macro rules re-evaluate. `TaskSequence.CreateLoop(nodeId, hubId)` creates the standard gather loop: TravelTo(node) → Work → TravelTo(hub) → Deposit → repeat. Work steps explicitly specify their `MicroRulesetId` — no implicit fallbacks. When inventory fills during gathering, the default micro rule (IF InventoryFull → FinishTask) advances past the Work step — InventoryFull handling is not hardcoded but expressed as a visible, editable micro rule.

**Sequence Reuse (Work At):** `CommandWorkAtSuspendMacrosForOneCycle` calls `FindMatchingGatherLoop(nodeId, hubId)` before creating a new sequence. This searches `TaskSequenceLibrary` for an existing standard gather loop matching ALL of: same `TargetNodeId`, `Loop == true`, exactly 4 steps (TravelTo(node) → Work → TravelTo(hub) → Deposit), and the Work step's `MicroRulesetId == DefaultRulesets.DefaultMicroId`. If found, it reuses the existing sequence instead of creating a duplicate. If the player edits a sequence's micro ruleset, the next node-click creates a fresh sequence — the edited one stays untouched.

**Macro Rules:** `EvaluateMacroRules(runner, triggerReason)` evaluates every tick for all runners (non-Idle via TickRunner, Idle via ExecuteCurrentStep). If a macro rule fires, it calls `AssignRunner` with a new task sequence (e.g., `WorkAt` creates `TaskSequence.CreateLoop` for a different node). Same-assignment suppression prevents infinite loops. Rules with `FinishCurrentSequence = true` set a `PendingTaskSequence` that executes at the next sequence boundary instead of immediately. If there's no active sequence when a deferred rule fires, it degrades to immediate. "Work At" button sets `MacroSuspendedUntilLoop` to guarantee one cycle before macro rules resume.

**Micro Rules:** `EvaluateMicroRules(runner, node)` runs when the assignment hits a Work step. Returns a gatherable index (GatherHere), -1 (FinishTask), or -2 (NoMatch = stuck). `ReevaluateMicroDuringGathering` runs after each item gathered — may switch resource, FinishTask, or stop with NoMicroRuleMatched. "Let it break" philosophy: if no rule matches, the runner stays idle and a warning event fires.

### `Simulation/Core/EventLogService.cs`
Pure C# service that subscribes to all 15 event types and stores formatted log entries in a ring buffer (default 500 max). Each handler formats the event as a raw struct representation (e.g. `ItemGathered { ItemId=copper_ore, FreeSlots=27 }`). Optional collapsing merges consecutive entries with the same CollapseKey + RunnerId (off by default). Query methods: `GetAll()`, `GetWarnings()`, `GetActivityFeed(runnerId)`, `GetForRunner(runnerId)`, `GetByCategories()`. Categories: Warning, Automation, StateChange, Production, Lifecycle.

### `Simulation/Core/EventLogEntry.cs`
Data class for event log entries: TickNumber, EventCategory, RunnerId, Summary, RepeatCount (for collapsing), CollapseKey.

### Automation — `Simulation/Automation/`

The automation engine is **fully integrated** into `GameSimulation`'s tick loop. Two layers of rules drive all runner behavior:

**Three-layer automation (global libraries):**
All three automation components live in **global libraries** on GameState. Runners hold string ID references. Editing a template updates all runners using it.
- **Task Sequence (TaskSequenceLibrary):** A list of steps the runner follows (TravelTo → Work → TravelTo → Deposit → repeat). Created via `TaskSequence.CreateLoop(nodeId, hubId)`. Null = idle. Pure logistics — where to go, when to deposit. What the runner does at each node is emergent from micro rules. Each Work step explicitly specifies its `MicroRulesetId`. Non-looping sequences complete after the last step; the runner goes idle and macro rules re-evaluate.
- **Macro rules (MacroRulesetLibrary):** Per-runner. Conditions that swap the current task sequence for a different one. Evaluate every tick for all runners. Same-assignment suppression prevents infinite loops.
- **Micro rules (MicroRulesetLibrary):** Per-Work-step, not per-runner. What happens during the Work step (GatherHere, FinishTask). Default: `Always → GatherHere(0)`. Evaluated at Work step entry and re-evaluated after each item gathered. `GatherHere` supports item-ID resolution via `StringParam` (e.g., `"iron_ore"` resolves to gatherable index at current node) alongside positional `IntParam`.

The rule engine uses a **data-driven, first-match-wins priority rule** design. No class hierarchy or polymorphism — flat enums + parameter fields for serialization compatibility. The same Rule/Ruleset/Condition types serve both macro and micro layers. Adding a new condition/action type = add enum value + add case to switch statement.

**"Let it break" philosophy:** When rules don't cover a situation (empty ruleset, invalid index, no matching conditions), the runner stops and a `NoMicroRuleMatched` warning event fires. The UI warns the player. Null rulesets from old saves are migrated to defaults in `LoadState`.

**`ConditionType.cs`** — Enum of condition types: Always, InventoryFull, InventorySlots, InventoryContains, BankContains, SkillLevel, RunnerStateIs, AtNode, SelfHP (placeholder for combat).

**`ActionType.cs`** — Enum of action types. Split into macro and micro:
- **Macro actions:** WorkAt (create work loop at node), ReturnToHub (1-step non-looping), Idle (clear task sequence)
- **Micro actions:** GatherHere (gather resource at index), FinishTask (signal macro to advance past Work step)

**`ComparisonOperator.cs`** — Enum: GreaterThan, GreaterOrEqual, LessThan, LessOrEqual, Equal, NotEqual.

**`Condition.cs`** — `[Serializable]` data class with Type, Operator, NumericValue, StringParam, IntParam. Static factory methods for readable construction (e.g., `Condition.InventoryFull()`, `Condition.BankContains("iron", GreaterOrEqual, 50)`).

**`AutomationAction.cs`** — `[Serializable]` data class with Type, StringParam (nodeId for WorkAt), IntParam (gatherableIndex for GatherHere). Static factory methods: `WorkAt(nodeId)`, `ReturnToHub()`, `Idle()` (macro); `GatherHere(idx)`, `FinishTask()` (micro).

**`Rule.cs`** — `[Serializable]`: `List<Condition>` (AND composition), `AutomationAction`, `Enabled`, `FinishCurrentSequence` (default true — macro only, defers action until sequence boundary), `Label`.

**`Ruleset.cs`** — `[Serializable]`: `Id` (string, unique identifier for library lookups), `Name` (player-facing display name), `Category` (enum: General, Gathering, Combat, Crafting for library organization), `List<Rule>`, `DeepCopy()` generates a new Id (clone, not alias).

**`TaskSequence.cs`** — `[Serializable]`: `Id` (string, unique identifier), `List<TaskStep>`, `TargetNodeId`, `HubNodeId`, `Loop`, `Name`. `CreateLoop(nodeId, hubId, microRulesetId)` builds TravelTo(node) → Work → TravelTo(hub) → Deposit with Loop=true. `TaskStep` is `[Serializable]`: `TaskStepType` (TravelTo, Work, Deposit), `TargetNodeId`, `MicroRulesetId` (for Work steps — always explicit, UI enforces selection). Per-runner step progress (`TaskSequenceCurrentStepIndex`) lives on Runner, not on TaskSequence.

**`EvaluationContext.cs`** — Struct wrapping Runner + GameState + SimulationConfig. Created once per evaluation, not stored.

**`RuleEvaluator.cs`** — Static, pure methods. `EvaluateRuleset()` returns first matching rule index or -1. `EvaluateCondition()` switches on ConditionType. `Compare()` generic numeric comparison. "Always compute, never cache."

**`DefaultRulesets.cs`** — Factory methods:
- `CreateDefaultMicro()` — two rules: `InventoryFull → FinishTask`, `Always → GatherHere(0)`. The default micro for gathering Work steps.
- `EnsureInLibrary(state)` — ensures default micro exists in library (idempotent). No default macro — runners start with null macro (no auto-switching until player sets up rules).

**`DecisionLogEntry.cs`** — `[Serializable]`: TickNumber, GameTime, RunnerId, RunnerName, RuleIndex, RuleLabel, TriggerReason, ActionType, ActionDetail, ConditionSnapshot, WasDeferred. One entry per macro rule firing.

**`DecisionLog.cs`** — `[Serializable]`: Ring-buffer list storage with configurable max entries. `GetForRunner()` and `GetInRange()` filter methods (most recent first). The player's primary "why did my runner do that?" debugging tool. Populated when macro rules fire, with `FormatConditionSnapshot` for human-readable condition state.

**Library CRUD commands on `GameSimulation`:**
- `CommandCreate*` / `CommandDelete*` / `CommandAssign*ToRunner` / `CommandClone*` for all three library types (task sequences, macro rulesets, micro rulesets)
- `CommandCloneMacroRuleset(sourceId)` — deep-copies into a new library entry (standalone)
- `CommandCloneMacroRulesetForRunner(runnerId)` — deep-copies and assigns to runner

**Ruleset mutation commands:** All operate on both macro and micro libraries via `FindRulesetInAnyLibrary(id)` (GUID-based, searches both libraries):
- `CommandAddRuleToRuleset`, `CommandRemoveRuleFromRuleset`, `CommandMoveRuleInRuleset`
- `CommandToggleRuleEnabled`, `CommandUpdateRule`, `CommandResetRulesetToDefault`, `CommandRenameRuleset`

**Task sequence mutation commands:** Include runner step index adjustment for live editing:
- `CommandAddStepToTaskSequence` — insert before current → increment runner index
- `CommandRemoveStepFromTaskSequence` — remove before current → decrement; remove AT current → clamp; empty → runner goes idle
- `CommandMoveStepInTaskSequence` — runner index follows the moved step
- `CommandSetTaskSequenceLoop`, `CommandSetWorkStepMicroRuleset`, `CommandRenameTaskSequence`

**Query helpers:** `CountRunnersUsing*`, `GetRunnerNamesUsing*`, `CountSequencesUsingMicroRuleset`

### Items — `Simulation/Items/`

**`ItemDefinition.cs`** — Template for an item type. Has an Id, display Name, Category enum (Ore, Log, Fish, Herb, Consumable, Currency, Gear, Misc), Stackable flag, and MaxStack. Non-stackable items always have MaxStack = 1.

**`ItemStack.cs`** — A quantity of one item in an inventory or bank slot. References an ItemDefinition by string Id.

**`ItemRegistry.cs`** — Runtime lookup table (Dictionary) of ItemDefinition by Id. Populated during `StartNewGame` from `SimulationConfig.ItemDefinitions`. All systems resolve item IDs through this.

**`Inventory.cs`** — OSRS-style fixed-slot container (default 28 slots). Non-stackable items take 1 slot each. Stackable items merge onto existing stacks up to MaxStack before using new slots. Key methods: `TryAdd`, `RemoveItemsOfType`, `IsFull`, `CountItem`, `FreeSlots`, `Clear`. Note: `IsFull(null)` returns `true` — defensive behavior meaning "I can't carry this unknown item."

**`Bank.cs`** — Guild-wide bank with infinite slots. Everything stacks regardless of the item's Stackable flag. `DepositAll(inventory)` moves all items from a runner's inventory into the bank and clears the inventory. `Withdraw` moves items back into an inventory, respecting slot limits.

### Gathering — `Simulation/Gathering/`

**`GatherableConfig.cs`** — Defines a gatherable resource: what it produces (`ProducedItemId`), which skill is required (`RequiredSkill`), base ticks to gather (`BaseTicksToGather`), XP awarded per tick (`XpPerTick`), and minimum skill level to gather (`MinLevel`). Gatherables live on `WorldNode`s — a node can have multiple gatherables with different level requirements (e.g. a mine with copper at level 1 and iron at level 15).

### World — `Simulation/World/`

**`WorldMap.cs`** — The world map: a graph of `WorldNode`s connected by `WorldEdge`s.

**`WorldNode`** — A location in the world. Has an Id, Name, world-space position (WorldX, WorldZ), color (ColorR/G/B floats — stored as floats to avoid UnityEngine dependency), and a `GatherableConfig[]` array. The gatherables array is what makes a node a gathering node — empty for hubs, mob zones, etc. A node can have multiple gatherables with different skills and level requirements. The order of the array determines the gatherable index (index 0 = default). What you can do at a node is determined by its data, not any label — there is no NodeType enum.

**`WorldEdge`** — Bidirectional connection between two nodes with a travel distance.

**`WorldMap`** — Holds lists of nodes and edges. Runtime lookups (node by ID, adjacency lists) are built via `Initialize()` (must be called after construction or deserialization). Key fields:
- `HubNodeId` — the hub node's ID (set during map creation)
- `TravelDistanceScale` — multiplier for Euclidean fallback distances (does NOT affect authored edges)

Key methods:
- `Initialize()` — builds runtime lookup dictionaries. Must be called before querying.
- `AddNode` / `AddEdge` — builder helpers for constructing maps in code. `AddNode` accepts `params GatherableConfig[]` for directly attaching gatherables (used by tests).
- `GetNode(id)` — node lookup by ID.
- `GetEuclideanDistance(from, to)` — straight-line distance between two nodes.
- `GetDirectDistance(from, to)` — distance between directly connected nodes (-1 if no edge).
- `FindPath(from, to)` — Dijkstra's shortest path. Returns total distance and path as list of node IDs. Falls back to Euclidean distance × `TravelDistanceScale` when no edge path exists.
- `CreateStarterMap()` — builds the starter map topology (nodes, edges, positions, gatherables) for testing/fallback when no `WorldMapAsset` is assigned.

**Starter map nodes:** Guild Hall (hub), Copper Mine, Pine Forest, Sunlit Pond, Herb Garden, Overgrown Mine (multi-gatherable: trees + ore), Deep Mine (multi-gatherable: copper + iron Lv15), Lakeside Grove (multi-gatherable: logs + fish + herbs), Goblin Camp, Dark Cavern.

### `Simulation/Core/SaveSystem.cs`
Defines what save/load looks like without implementing it. The simulation layer says "I need someone who can save/load a GameState" but doesn't know how. This boundary exists because the simulation assembly can't reference Unity's JsonUtility.

---

## Scripts — Data Layer

### `Data/SimulationConfigAsset.cs`
`[CreateAssetMenu: Project Guild/Simulation Config]`

ScriptableObject wrapper around `SimulationConfig`. Mirrors all config fields with `[Tooltip]` and `[Range]` attributes for inspector usability. `ToConfig()` converts to the plain C# object the simulation layer uses.

Inspector sections: Travel, Skills/XP, Runner Generation, Gathering, Items, Inventory, Death.

### `Data/ItemDefinitionAsset.cs`
`[CreateAssetMenu: Project Guild/Item Definition]`

ScriptableObject for authoring an item definition. Fields: Id, Name, Category, Stackable (default false), MaxStack (default 1). `ToItemDefinition()` converts to plain C# `ItemDefinition`. Each item type (copper ore, pine log, etc.) gets its own asset file. The simulation uses string IDs at runtime — the SO reference is an authoring convenience (dropdown picker instead of typing IDs).

### `Data/GatherableConfigAsset.cs`
`[CreateAssetMenu: Project Guild/Gatherable Config]`

ScriptableObject for authoring a gatherable resource. Fields: ProducedItem (reference to `ItemDefinitionAsset`), RequiredSkill, BaseTicksToGather, XpPerTick, MinLevel. `ToGatherableConfig()` reads the item ID from the referenced SO and returns a plain C# `GatherableConfig`.

A single GatherableConfigAsset can be reused across multiple nodes (e.g. "Copper Vein" SO referenced by both Copper Mine and Deep Mine node entries).

### `Data/WorldNodeAsset.cs`
`[CreateAssetMenu: Project Guild/World Node]`

ScriptableObject for authoring a world node. Fields: Id, Name, WorldX, WorldZ, NodeColor (Unity Color), Gatherables (array of `GatherableConfigAsset` references). `ToWorldNode()` converts to a plain C# `WorldNode`, including converting each GatherableConfigAsset to a `GatherableConfig` inline. `OnValidate()` warns for empty Id/Name.

Each node in the game world gets its own WorldNodeAsset file. Gatherables are authored directly on the node — drag GatherableConfigAsset references into the node's array. This is how nodes become gathering nodes.

### `Data/WorldMapAsset.cs`
`[CreateAssetMenu: Project Guild/World Map]`

ScriptableObject for authoring the full world map. Fields:
- `HubNode` — direct reference to a `WorldNodeAsset` (the hub)
- `Nodes` — array of `WorldNodeAsset` references (all non-hub nodes)
- `Edges` — array of `WorldEdgeEntry` structs (NodeA, NodeB as `WorldNodeAsset` references + Distance float). Optional — edges define custom travel distances. Nodes without edges use Euclidean distance × `TravelDistanceScale`.
- `TravelDistanceScale` — multiplier for Euclidean fallback distances (range 0.1-2.0, default 0.5)

`ToWorldMap()` converts to a plain C# `WorldMap`: resolves SO references to string IDs, validates unique IDs, sets `HubNodeId` and `TravelDistanceScale`, and calls `Initialize()`.

`OnValidate()` validates hub assignment, checks for duplicate node IDs, and verifies edge nodes exist in the node list.

---

## Scripts — Bridge Layer

### `Bridge/SimulationRunner.cs`
The single MonoBehaviour that drives the simulation. In `Update()`, it accumulates `Time.deltaTime` and fires `Simulation.Tick()` for each 0.1 seconds owed. A `_maxTicksPerFrame` cap (default 3) prevents the "spiral of death" — if the game hitches, the simulation drops ticks rather than trying to catch up and making things worse.

Inspector fields:
- `_configAsset` — optional `SimulationConfigAsset` SO (falls back to default `SimulationConfig` if none assigned)
- `_worldMapAsset` — optional `WorldMapAsset` SO (falls back to `WorldMap.CreateStarterMap()` if none assigned)

On `Awake()`, creates the `GameSimulation` with config from the asset. On `StartNewGame()`, creates the world map from the asset (or fallback) and passes it to the simulation.

### `Bridge/SaveManager.cs`
Concrete implementation of `ISaveSystem`. Uses `JsonUtility.ToJson/FromJson` and writes to `Application.persistentDataPath/saves/`.

---

## Scripts — View Layer

### `View/GameBootstrapper.cs`
Entry point that wires up the simulation, visuals, and debug UI. Starts a new game on `Start()`, builds the visual world, and points the camera at the first runner.

**Click handling (LateUpdate):** On left-click, checks (1) not over UI, (2) `TryPickRunner()` raycasts for RunnerVisual on the "Runners" layer — if found, selects that runner and returns. (3) `TryPickBank()` raycasts the "Bank" layer — if found, toggles the bank panel and returns. (4) `TryPickNode()` raycasts the "Nodes" layer for `NodeMarker` — if found and not the hub node, shows a confirmation popup ("Send [Runner] to work at [Node]?") via `UIManager.ShowNodeClickConfirmation()`. Runner > Bank > Node priority. Requires three physics layers: "Runners", "Nodes", "Bank" — logs `Debug.LogError` if any is missing.

The debug UI (`OnGUI`) provides full inspection and editing of the automation system. It is organized into panels:

**Top-center — Runner selector:** Compact multi-column table of all runners. Shows name and abbreviated state. Click to select.

**Left panel — Runner info & commands:**
- Runner name, state, location, assignment info
- Travel progress (when traveling)
- Gathering progress (when gathering)
- Depositing progress (when depositing)
- Inventory summary (slot count, items by type)
- "Send to" buttons per node (creates assignment via `AssignRunner`)

**Bottom-center — Pawn generation & Guild Bank:**
- Random and Tutorial pawn generation buttons
- Tick count and elapsed time
- Guild bank contents

**Right panel — Skills & Live Stats:**
- All 15 skills: level, passion marker (yellow P), effective level, XP progress bar (current/needed)
- Live stats: travel speed, travel ETA/distance, Athletics XP/tick, gathering ticks/item + items/min, gathering XP/tick (with passion indicator), passion summary

**Automation panel (6 tabs):**
- **Task Sequence**: View active sequence steps, current step highlighted, pending sequence, clear button
- **Macro Rules**: View/edit/reorder/enable/delete macro rules, add rule form (condition + action + FinishCurrentSequence), copy/paste
- **Micro Rules**: View/edit/reorder/enable/delete micro rules, add form restricted to GatherHere/FinishTask actions, shows gatherables at current node for context
- **Decision Log**: Per-runner log of macro rule firings with condition snapshots
- **Warnings**: All NoMicroRuleMatched and GatheringFailed events across runners
- **Activity**: Per-runner event timeline (excludes Lifecycle noise)
- **Event Log**: Full searchable/filterable raw event log with category toggles, runner filter, optional collapsing

### `View/NodeMarker.cs`
Simple MonoBehaviour storing a `NodeId` string. Attached by `VisualSyncSystem.CreateNodeMarker()` to each node's GameObject so that raycasting can identify which world node was clicked. Used by `GameBootstrapper.TryPickNode()`.

### `View/BankMarker.cs`
Simple MonoBehaviour (no fields) attached to the bank cube by `VisualSyncSystem.CreateBankMarker()`. Used by `GameBootstrapper.TryPickBank()` to detect bank clicks via the "Bank" physics layer.

### `View/VisualSyncSystem.cs`
Bridges sim state to 3D world. Builds visual representations of world nodes (colored cylinders with floating labels) and runners (capsule primitives), updates runner positions each `LateUpdate()`. Attaches `NodeMarker` components to node GameObjects during creation for click-to-select. Creates a bank cube near the hub with `BankMarker` component and floating "Bank" label on the "Bank" physics layer.

Runner position calculation (`GetRunnerWorldPosition`):
- **Traveling:** Lerps between start and destination using `Travel.Progress`. If `StartWorldX`/`StartWorldZ` is set (redirect), uses those as the start position instead of the FromNode's position — this prevents visual snapping when a runner changes direction mid-travel.
- **Idle at a node:** Places runners at the node position with a small circular spread so multiple idle runners don't stack on each other.

Subscribes to `RunnerCreated` events to spawn visuals for runners added at runtime (e.g. pawn generation buttons).

### `View/CameraController.cs`
Orbit camera with zoom. Uses Unity's New Input System (inline action definitions). Right mouse drag to orbit, scroll wheel to zoom. Snaps instantly when switching runner targets via `SetTarget()`.

### `View/Runners/RunnerVisual.cs`
MonoBehaviour attached to each runner's 3D representation. Handles interpolated movement between positions set by VisualSyncSystem — the simulation ticks at 10/sec but the view renders at 60fps, so RunnerVisual smoothly interpolates between tick positions over one tick interval. Also creates and manages a floating name label (TextMeshPro) that billboards toward the camera.

### `View/UI/UIManager.cs`
Top-level MonoBehaviour for the real UI (UI Toolkit). Owns the `UIDocument` component, coordinates `RunnerPortraitBarController`, `RunnerDetailsPanelController`, `AutomationPanelController`, `BankPanelController`, and `ResourceBarController` as plain C# controller objects. Manages runner selection state (`SelectedRunnerId`). Subscribes to `SimulationTickCompleted` to refresh all controllers every tick (10/sec). Also coordinates camera movement on runner selection via `CameraController.SetTarget()`. Provides `OpenAutomationPanelToItem()` and `OpenAutomationPanelToItemFromRunner()` for navigation from the runner Automation tab to the editing panel. `ToggleBankPanel()` and `OpenBankPanel()` control the bank overlay. `ShowNodeClickConfirmation()` shows a centered popup for node-click Work At. The Automation toggle button is added programmatically to the root element. Initialized by `GameBootstrapper` after `StartNewGame()` and `BuildWorld()`.

### `View/UI/RunnerPortraitBarController.cs`
Plain C# class (not MonoBehaviour). Manages the portrait bar at the top of the screen. Clones `RunnerPortrait.uxml` templates per runner. Handles click-to-select (USS class `selected` toggle) and periodic state label refresh. Each portrait shows runner name and short state text. **Warning badge**: a red circle (top-right) toggled by `runner.ActiveWarning != null`. Tooltip shows the warning message. Badge `pickingMode` switches between `Position` (hoverable when visible) and `Ignore` (pass-through when hidden).

### `View/UI/RunnerDetailsPanelController.cs`
Plain C# class. Manages the bottom-right details panel showing four tabs for the selected runner: Overview (name, state, task, travel progress, inventory summary, live stats, skill XP bars, skills summary), Inventory (28-slot grid with icons), Skills (15 skill rows with XP bars), and Automation (sub-tabs for task sequence, macro rules, micro rules — read-only summary with "Edit in Library" buttons). Equipment tab remains disabled. The Automation tab is lazy-initialized on first switch. Accepts optional `VisualTreeAsset` for the automation tab template. Overview inventory items use pooled row elements (`_inventoryItemRowCache`) — rows are reused in-place, excess rows hidden, new rows created only when needed.

**Skill XP progress bars (Live Stats):** Tracks per-skill XP changes using `_lastKnownSkillXp` and `_skillXpLastChangeTime` arrays. When XP changes, the timestamp is updated. Bars are shown for skills where `Time.time - lastChangeTime < LiveStatsXpDisplayWindowSeconds`, sorted by most recent. Pool of bar elements (label + ProgressBar) is created once, shown/hidden as needed. Reset on runner switch via `ResetSkillXpTracking()`. Config-driven: `LiveStatsMaxSkillXpBars` (max simultaneous bars) and `LiveStatsXpDisplayWindowSeconds` (fade window).

### `View/UI/AutomationTabController.cs`
Plain C# class. Controller for the runner Automation tab (read-only portal). Uses **shape-keyed caching**: elements are built once when the data shape (step count, rule count, work step structure) is first seen, then updated in-place on subsequent ticks. Rebuild only happens when the shape changes (e.g., runner switches to a sequence with a different step count). Three sub-tabs:
- **Task Seq**: Assign dropdown (pick from library or "(None)"), active sequence steps with current step highlighted (gold), loop status, macro suspension indicator, pending sequence, [Clear Task] / [Resume Macros] / [Edit in Library] buttons.
- **Macro**: Assign dropdown (pick from library or "(None)"), ruleset rules as natural language sentences with enabled indicator and timing tag. "Used by N runners" label.
- **Micro**: Shows each Work step in the current sequence with its micro ruleset and rules. "Used by N sequences" label. Uses `MicroWorkSectionCache` class per Work step.

Assign dropdowns are populated from the global library every refresh. Callbacks route through `CommandAssignTaskSequenceToRunner` / `CommandAssignMacroRulesetToRunner` (or `ClearTaskSequence` for "(None)").

All [Edit in Library] buttons navigate to the Automation panel via `UIManager.OpenAutomationPanelToItemFromRunner()`.

### `View/UI/AutomationPanelController.cs`
Plain C# class. Manages the Automation overlay panel (toggle via top-left button, close with Escape). Three library tabs: Task Sequences, Macro Rulesets, Micro Rulesets. Each tab delegates to a sub-controller. Provides `OpenToItem()` and `OpenToItemFromRunner()` for navigation from the runner tab. Purely event-driven — no tick-driven `Refresh()`. Editors refresh on open, tab switch, and user interaction only.

### `View/UI/TaskSequenceEditorController.cs`
Plain C# class. Master-detail editor for the Task Sequence library. Uses **persistent elements**: editor shell from UXML (name field, loop toggle, banner, used-by label) is updated in-place via `SetValueWithoutNotify`. Steps editor uses shape-key caching (seq ID + step count) — skips rebuild when the same sequence is re-selected. List pane uses item cache (`Dictionary<string, ...>`) for CSS-only selection toggling and in-place name updates on rename. Left pane: searchable list with [+ New]. Right pane: name field, loop toggle, step list with node/micro dropdowns, [+ Add Step] (step type picker), shared template warning banner when used by >1 runner, [Assign To...] (runner picker popup) / [Clone] / [Delete], "Used by: runner names" footer.

### `View/UI/MacroRulesetEditorController.cs`
Plain C# class. Master-detail editor for the Macro Ruleset library. Uses **persistent editor shell**: banner, name field, rules header, rules container, add button, and footer are built once in `BuildEditorShell()` and updated in-place. Only the rules container rebuilds, gated by shape-key caching (ruleset ID + rule count). List pane uses item cache for CSS-only selection toggling. Left pane: searchable list. Right pane: name field, interactive rule list (via `RuleEditorController`), [+ Add Rule], [Assign To...] (runner picker popup) / [Clone] / [Reset to Default] / [Delete], shared template banner.

### `View/UI/MicroRulesetEditorController.cs`
Plain C# class. Master-detail editor for the Micro Ruleset library. Same persistent-element architecture as Macro. Shows "Used by N sequences" instead of runners. Micro-specific action types (GatherHere, FinishTask) and no timing toggle.

### `View/UI/BankPanelController.cs`
Plain C# class. OSRS-style bank overlay panel. Follows `AutomationPanelController` pattern: `IsOpen`, `Open()`, `Close()`, `Toggle()`, Escape to close. Shows all stacked items in a flex-wrap grid with category filtering (tab buttons), text search (case-insensitive), and persistent-element caching (shape-keyed by filtered item IDs — quantities update in-place, full rebuild only on shape change). Refreshes on `Open()` and on `RunnerDeposited` events while open (not every tick). Tooltips show item name, quantity, and category. Footer shows total item count. Opened via `UIManager.ToggleBankPanel()` (from bank click) or `UIManager.OpenBankPanel()` (from resource bar click).

### `View/UI/ResourceBarController.cs`
Plain C# class. Always-visible left-side panel showing guild bank totals grouped by `ItemCategory` (Rimworld-style). Refreshes every tick via `UIManager.OnSimulationTick()`. Groups items by category with collapsible headers (gold text, click to toggle). Compact rows: item name + formatted quantity (K/M suffixes for large numbers). Hidden when bank is empty. Shape-keyed caching: rebuilds DOM only when item set changes, updates quantities in-place otherwise. Collapsed state tracked in `HashSet<ItemCategory>`. Clicking any item row opens the bank panel.

### `View/UI/RuleEditorController.cs`
Static helper class. Builds interactive rule rows with inline editing. Each row has: enable toggle, condition picker (cascading dropdowns for type → parameters), action picker (macro: Idle/WorkAt/ReturnToHub with node dropdown; micro: GatherHere/FinishTask with item picker), timing toggle (macro only), move up/down buttons, delete button. All changes go through `GameSimulation` commands. Used by both Macro and Micro editor controllers.

### `View/UI/AutomationUIHelpers.cs`
Static helper class (pure C#, no Unity deps). Natural language formatting for automation components:
- `FormatCondition()` — "Bank contains Copper Ore >= 200"
- `FormatAction()` — "Work at Copper Mine"
- `FormatRule()` — "IF Bank contains Copper Ore >= 200 THEN Work at Pine Forest"
- `FormatTimingTag()` — "Immediately" or "Finish Current Sequence"
- `FormatStep()` — "Travel to Copper Mine", "Work (Default Gather)", "Deposit"
- `FormatOperator()` — >, >=, <, <=, =, !=
- `HumanizeId()` — "copper_ore" → "Copper Ore"

Resolves node IDs via GameState.Map, item IDs via optional `ItemNameResolver` delegate (falls back to `HumanizeId`).

### UI Assets — `Assets/UI/`
`MainLayout.uxml/.uss` — Root layout with flexbox: portrait bar (top), viewport spacer (center), details panel container (bottom-right). Also styles the Automation toggle button (absolute-positioned top-left). All containers use `picking-mode: ignore` so mouse clicks pass through to the 3D world.
`RunnerPortrait.uxml/.uss` — Template for one portrait, cloned per runner. Dark background, gold border on `.selected`, hover effect.
`RunnerDetailsPanel.uxml/.uss` — Bottom-right panel with tab bar (Overview, Inventory, Skills, Equipment*, Automation) and ScrollView content. Dark theme with gold section headers.
`AutomationTab.uxml/.uss` — Sub-tab bar (Task Seq / Macro / Micro) with content containers for read-only runner automation summary. Instantiated into the details panel's automation content area.
`AutomationPanel.uxml/.uss` — Full-screen overlay panel for editing automation libraries. Title bar with close button, library tab bar (Task Sequences / Macro Rulesets / Micro Rulesets), master-detail layout (list pane left, editor pane right). Includes shared template warning banner, step type picker, and editor field styles.
`RuleEditor.uss` — Styles for interactive rule editing rows: condition/action pickers, operator dropdowns, move/delete buttons, timing toggle. Loaded via AutomationPanel.uxml.
`BankPanel.uxml/.uss` — Overlay panel for OSRS-style bank view. Title bar with close button, search field, category tab row, flex-wrap item grid (66px slots, 48px icons, quantity bottom-right), footer with item count.
`PanelSettings.asset` — Scale With Screen Size, 1920x1080 reference, controls UI scaling across resolutions.

---

## Scripts — Tests

All tests are NUnit edit-mode tests. They run without Play Mode because the simulation has no Unity dependencies. Each test file creates a `SimulationConfig` in `[SetUp]` and tests use config-driven values rather than hardcoded numbers.

### `Tests/Editor/EventBusTests.cs`
Subscribe, publish, unsubscribe, multiple handlers, type isolation, clear.

### `Tests/Editor/SkillTests.cs`
Effective level with/without passion, XP gain, level-up, multi-level-up, passion XP bonus, config multiplier override.

### `Tests/Editor/RunnerTests.cs`
Default construction, skill access, effective level, factory creation (random, definition-based, biased), config range respect.

### `Tests/Editor/GameSimulationTests.cs`
New game creation, tick counting, time accumulation, travel commands, travel progress/completion, Athletics speed scaling, config-driven speed, event publishing, map-based travel. `FindMatchingGatherLoop` sequence reuse tests: matches standard loop, rejects different node/micro/non-looping, `CommandWorkAtSuspendMacrosForOneCycle` reuses existing and creates new for different nodes.

### `Tests/Editor/ItemTests.cs`
ItemRegistry register/get, ItemDefinition construction, stackable vs non-stackable defaults.

### `Tests/Editor/InventoryTests.cs`
TryAdd non-stackable, TryAdd stackable with stacking, slot limits, Remove, IsFull, CountItem, Clear.

### `Tests/Editor/BankTests.cs`
Deposit, DepositAll from inventory, Withdraw into inventory, CountItem, infinite stacking.

### `Tests/Editor/GatheringTests.cs`
Gathering validation (must be at node with gatherables), item production rate (ticks match config), XP-per-tick awards, level-up event + speed recalculation, higher skill = faster gathering, passion speed boost, inventory-full triggers deposit step, full gather-deposit loop via assignments, multiple loop accumulation in bank, GatheringStarted fires on resume.

### `Tests/Editor/WorldMapTests.cs`
Node lookup, direct distance, multi-hop pathfinding (Dijkstra), same-node path, Euclidean fallback with TravelDistanceScale. Starter map tests: hub and nodes exist, pathfinding works, shortest route selection.

### `Tests/Editor/AutomationConditionTests.cs`
Tests the automation engine in isolation (no GameSimulation). All 9 condition types evaluated against hand-built `EvaluationContext`s. Verifies Compare helper, all 6 operators, InventoryFull, InventorySlots, InventoryContains, BankContains, SkillLevel, RunnerStateIs, AtNode, Always, SelfHP (always false until Phase 5 combat).

### `Tests/Editor/AutomationRuleTests.cs`
Tests the automation engine in isolation (no GameSimulation). AND composition (multiple conditions), first-match-wins ordering, disabled rules skipped, empty conditions = always true, DeepCopy independence.

### `Tests/Editor/MacroStepTests.cs`
Task sequence step sequencing: step advance, wrap-around, TravelTo/Work/Deposit execution order, non-looping completion.

### `Tests/Editor/MacroRuleIntegrationTests.cs`
Macro rules integrated with GameSimulation: WorkAt creates task sequence, FinishCurrentSequence defers until sequence boundary (degrades to immediate when no active sequence), same-assignment suppression, ArrivedAtNode trigger, DepositCompleted trigger, SequenceCompleted trigger, pending task sequence execution, MacroSuspendedUntilLoop (Work At one-cycle guarantee), mid-gather immediate interrupt, disabled rules skipped.

### `Tests/Editor/MicroRuleTests.cs`
Micro rules integrated with GameSimulation: default micro rule gathers, GatherHere selects resource, FinishTask advances macro, invalid index = stuck ("let it break"), empty ruleset = stuck, mid-gathering re-evaluation, NoMicroRuleMatched event tests (empty ruleset, invalid index, no matching conditions, mid-gathering no-match, valid rule doesn't fire event).

### `Tests/Editor/EventLogServiceTests.cs`
EventLogService unit tests: add/retrieve entries, collapsing (same key, different key, different runner, null key, disabled), ring buffer eviction, query methods (GetWarnings, GetActivityFeed, GetByCategories, GetForRunner, Clear). Integration tests: ItemGathered logs entries, NoMicroRuleMatched appears as warning.

### `Tests/Editor/DecisionLogTests.cs`
Add/retrieve entries, ring buffer eviction (oldest removed), SetMaxEntries evicts existing, filter by runner (most recent first), filter by tick range, no matches returns empty, Clear removes all.

### `Tests/Editor/AutomationCommandTests.cs`
Ruleset mutation commands (add/remove/reorder/toggle/update/reset/rename rules), task sequence mutation commands (add/remove/move step with runner index adjustment), query helpers (count/names of runners using templates), FindRulesetInAnyLibrary, and natural language formatting (AutomationUIHelpers). Tests cover: insert before/after/at current step, remove with index clamping, empty sequence → idle, step move with index tracking, multi-runner isolation, all operator/condition/action format strings, HumanizeId.

### `Tests/Editor/RedirectTests.cs`
Uses a simple 3-node right triangle map (A, B, C) with constant speed for predictable math. Tests: basic redirect (changes destination, sets StartWorld override, virtual position correct, Euclidean TotalDistance, resets DistanceCovered, preserves FromNodeId, arrives at new destination), redirect to current destination is no-op, redirect back to origin (works, arrives, correct virtual pos and distance), chained redirects (virtual position correct, arrives at final destination, back-and-forth stress test), normal travel has no StartWorld override, redirect publishes RunnerStartedTravel event, edge cases (idle runner starts normal travel, redirect at progress zero).

### `Tests/Editor/WarningBadgeTests.cs`
`Runner.ActiveWarning` lifecycle tests. Null by default. Set on: `GatheringFailed` (NoGatherablesAtNode, NotEnoughSkill), `NoMicroRuleMatched` (empty ruleset). Cleared on: `AssignRunner`, `CommandTravel`, `StartGathering`. Uses nodes with no gatherables, high MinLevel gatherables, and empty micro rulesets to trigger warnings.

---

## Key Design Patterns

**Config-driven values:** Every tunable number lives in `SimulationConfig`. Code reads `config.BaseTravelSpeed`, never `1.0f`. Tests can inject custom configs to verify behavior at different tuning points.

**SO authoring → plain C# runtime:** ScriptableObjects are the authoring layer. Each SO type has a `To*()` method that converts to a plain C# struct/class. The simulation only sees plain data — never SOs, never UnityEngine types. This keeps the simulation testable and portable.

**String IDs everywhere (simulation layer):** Items, nodes, runners — all identified by string IDs at runtime. SOs use direct references (dropdown pickers) for authoring convenience, but `To*()` extracts the string ID at the boundary.

**Gatherables on nodes, not in global config:** Each `WorldNode` carries its own `GatherableConfig[]`. A mine can have copper + iron with different level requirements. What you can do at a node is determined by its data, not any label.

**XP decoupled from speed:** XP is awarded per tick of activity, not per item produced. Gathering speed only affects economic output (items per trip). This means leveling is about which resource you grind, while speed is about efficiency.

**Explicit RNG:** `RunnerFactory` takes a `System.Random` parameter instead of using static/shared RNG. This makes tests deterministic — same seed, same runner, every time.

**Events over polling:** The view layer subscribes to events (`RunnerArrivedAtNode`) rather than checking runner state every frame. This is both more efficient and cleaner — the view only reacts when something actually happens. The `EventLogService` also subscribes to all events for the debug event log.

**"Let it break":** When automation rules are misconfigured (empty ruleset, invalid index, no matching rule), the runner stops and a warning event fires. The UI warns the player. This applies project-wide.

**Persistent elements with shape-keyed caching (UI Toolkit):** UI elements that display dynamic data are built once and updated in-place via `SetValueWithoutNotify()` or `.text` assignment. Elements that change structurally (step rows, rule rows, list items) are gated behind a "shape key" — a string like `"{seqId}|{stepCount}"`. If the shape key matches the cached value, the rebuild is skipped entirely. If it doesn't match, the container is `Clear()`ed and rebuilt. This prevents tick-driven destruction/recreation of interactive elements (TextFields, DropdownFields) which causes cursor focus loss and garbage generation. List panes track items by ID in a `Dictionary` and toggle selection CSS without rebuilding.

**Fluent builder for definitions:** `new RunnerDefinition { Name = "Bob" }.WithSkill(SkillType.Melee, 10, passion: true)` reads like English and is hard to get wrong.

---

## Data Flow: How the World Map Gets From Inspector to Simulation

```
WorldNodeAsset (SO per node — Id, Name, position, color, gatherables)
    ↓ each node's Gatherables[] references GatherableConfigAsset SOs
GatherableConfigAsset (SO per gatherable — item, skill, ticks, XP, min level)
    ↓
WorldMapAsset (SO — HubNode, Nodes[], Edges[], TravelDistanceScale)
    ↓ ToWorldMap()
WorldMap (plain C# — nodes, edges, hub ID, scale, runtime lookups)
    ↓ passed to GameSimulation.StartNewGame(map: worldMap)
GameSimulation uses map for travel, gathering, pathfinding
```

## Data Flow: How Items Get From Inspector to Simulation

```
ItemDefinitionAsset (SO in Unity Inspector)
    ↓ ToItemDefinition()
ItemDefinition (plain C#)
    ↓ SimulationConfigAsset.ToConfig() bundles into ItemDefinition[]
SimulationConfig.ItemDefinitions (plain C# array)
    ↓ GameSimulation.StartNewGame() registers each into ItemRegistry
ItemRegistry.Get(itemId) → used by gathering, inventory, bank
```

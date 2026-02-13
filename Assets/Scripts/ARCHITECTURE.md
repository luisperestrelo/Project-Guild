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

**View** is MonoBehaviours that render the game — runner models, animations, camera, UI. The view never modifies simulation state directly — it goes through commands on GameSimulation (like `CommandTravel`, `CommandGather`).

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
    │    ├─ Idle? → do nothing
    │    ├─ Traveling? → TickTravel()
    │    │    ├─ Award Athletics XP (every tick)
    │    │    │    └─ Level up? → Publish(RunnerSkillLeveledUp)
    │    │    └─ Arrived? → set Idle, Publish(RunnerArrivedAtNode)
    │    │                   └─ HandleGatheringArrival() if mid-loop
    │    │                        ├─ At hub? → DepositAndReturn()
    │    │                        └─ At node? → ResumeGathering()
    │    └─ Gathering? → TickGathering()
    │         ├─ Award skill XP (every tick, decoupled from item production)
    │         │    └─ Level up? → Publish(RunnerSkillLeveledUp)
    │         └─ Tick accumulator full?
    │              ├─ Produce item → Publish(ItemGathered)
    │              └─ Inventory full? → Publish(InventoryFull)
    │                                   → BeginAutoReturn() (hardcoded)
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
| `GameSimulation.cs` | Simulation | The brain. Owns all state, processes ticks |
| `GameState.cs` | Simulation | The data. All saveable state lives here |
| `SimulationConfig.cs` | Simulation | The rulebook. All tuning values |
| `EventBus.cs` | Simulation | The intercom. Type-safe pub/sub |
| `SimulationEvents.cs` | Simulation | The vocabulary. Defines what events exist |
| `WorldMap.cs` | Simulation | The terrain. Nodes, edges, pathfinding |
| `Runner.cs` | Simulation | The adventurer. State, skills, inventory |
| `GatherableConfig.cs` | Simulation | The resource. What a gatherable produces and costs |
| `RuleEvaluator.cs` | Simulation | The referee. Evaluates automation rules against context (not yet integrated into tick loop) |
| `ActionExecutor.cs` | Simulation | The dispatcher. Placeholder awaiting Phase 4 integration |
| `DecisionLog.cs` | Simulation | The playback. Ring-buffer log of automation decisions (not yet populated at runtime) |
| `SimulationRunner.cs` | Bridge | The clock. Drives ticks from Unity's frame loop |
| `SimulationConfigAsset.cs` | Data | The inspector. SO wrapper for config values |
| `ItemDefinitionAsset.cs` | Data | The catalog. SO wrapper for item definitions |
| `GatherableConfigAsset.cs` | Data | The field guide. SO wrapper for gatherable configs |
| `WorldNodeAsset.cs` | Data | The pin. SO wrapper for a world node |
| `WorldMapAsset.cs` | Data | The atlas. SO wrapper for the full world map |
| `GameBootstrapper.cs` | View | The director. Wires everything up, debug UI |
| `VisualSyncSystem.cs` | View | The cameraman. Keeps 3D visuals in sync with sim |

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
- **Automation**: `DecisionLogMaxEntries` (default 100). Additional config fields (e.g. periodic check interval) will be added in Phase 4.
- **Items**: `ItemDefinitions[]` (populated from SOs at load time)
- **Inventory**: `InventorySize` (default 28)
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
The core data class for a runner. Identity (ID, name), current state (Idle/Traveling/Gathering/etc.), location (which world node), 15 skills, an `Inventory` (28-slot OSRS-style), a `TravelState` for when they're moving, a `GatheringState` for when they're gathering, and a `Ruleset` for automation rules (data only — not evaluated by the tick loop yet, awaiting Phase 4). The constructor initializes all skills to level 1 — actual values come from RunnerFactory.

**`TravelState`** — tracks from/to nodes, total distance, distance covered, and a `Progress` property (0.0-1.0). Also has optional `StartWorldX`/`StartWorldZ` float fields — when set, the view layer uses these as the travel start position instead of the FromNode's position. Used by the redirect system to prevent visual snapping when a runner changes destination mid-travel. Null means "use the FromNode position as usual."

**`GatheringState`** — tracks the node being gathered, a `GatherableIndex` (which gatherable in the node's array), a tick accumulator, the pre-calculated ticks required per item, and a `GatheringSubState` enum (`Gathering`, `TravelingToBank`, `TravelingToNode`). The sub-state and gatherable index persist across the auto-return deposit loop so the runner knows what it was doing and which resource it was working on.

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
| `InventoryFull` | RunnerId | Inventory filled, auto-return triggered |
| `RunnerDeposited` | RunnerId, ItemsDeposited | Items deposited at bank |
| `SimulationTickCompleted` | TickNumber | Every tick |

### `Simulation/Core/GameState.cs`
Root of all saveable state. Holds: list of runners, tick count, total elapsed time, world map, guild Bank, and the automation DecisionLog (present but not populated at runtime until Phase 4 integration). Serializing this one object captures the entire game world.

### `Simulation/Core/GameSimulation.cs`
The orchestrator. Owns `GameState`, `EventBus`, `SimulationConfig`, and `ItemRegistry`. Its `Tick()` method processes all runners based on their current state.

`StartNewGame()` has two overloads: one taking explicit `RunnerDefinition[]` and an optional `WorldMap` for full control, one using default placeholders for quick testing. During startup: creates or accepts the world map, and populates the `ItemRegistry` from config.

**Travel:** `CommandTravel()` is the public API for telling a runner to move. It handles two cases:

1. **Normal travel (runner is Idle):** Finds the path via `WorldMap.FindPath()`, sets the runner to `Traveling` state, publishes `RunnerStartedTravel`.

2. **Redirect (runner is already Traveling):** Calculates the runner's current virtual position by lerping between the start and destination using travel progress. Computes the Euclidean distance from that virtual position to the new target. Creates a new `TravelState` with `StartWorldX`/`StartWorldZ` set to the virtual position, so the view can lerp smoothly from the runner's actual position to the new destination without snapping. This works for any redirect — including back to the origin node. Chained redirects work correctly because each redirect reads the previous `StartWorldX`/`StartWorldZ` override when computing the virtual position.

`GetTravelSpeed()` calculates Athletics-based movement speed from config values. `TickTravel()` advances runners along their path each tick and awards Athletics XP every tick (same decoupling as gathering: speed = getting there faster, XP = progression). `StartTravelInternal()` is a shared helper used by both `CommandTravel` and the auto-return system.

**Gathering:** `CommandGather(runnerId, gatherableIndex)` validates the runner is Idle at a node with gatherables, checks minimum skill level (publishes `GatheringFailed` if not met), calculates ticks required from the skill speed formula, and puts the runner into the `Gathering` state. The `gatherableIndex` selects which gatherable in the node's array to work on (default 0). `TickGathering()` awards XP every tick (decoupled from item production), accumulates ticks, produces an item when the threshold is reached, and recalculates speed on level-up.

**XP decoupling:** XP is awarded per tick of gathering/traveling, not per item produced or trip completed. Speed affects economic output (items per trip). XP rate is determined by which resource you're grinding. This means a faster gatherer produces more items but doesn't level faster.

**Gathering speed formulas:**
- **PowerCurve** (default): `speedMultiplier = effectiveLevel ^ exponent`. Higher levels are proportionally more impactful. Tuning: `BaseTicks = DesiredSecondsPerItem × TickRate × MinLevel ^ Exponent`.
- **Hyperbolic**: `speedMultiplier = 1 + (effectiveLevel - 1) × perLevelFactor`. Diminishing returns — early levels feel most impactful.

**Auto-Return Loop (hardcoded Phase 2 behavior):** When inventory fills during gathering, `BeginAutoReturn()` fires directly (hardcoded, not driven by automation rules). The runner travels to the hub. On arrival, `HandleGatheringArrival()` detects the sub-state and calls `DepositAndReturn()` — which dumps the inventory into the guild bank and starts travel back to the gathering node. On arrival back at the node, `ResumeGathering()` resets the accumulator and resumes at the same gatherable index. (Gather rate is always recomputed fresh every tick in `TickGathering` — no special recalculation needed.) This loop repeats indefinitely. Phase 4 will replace this hardcoded loop with task-driven automation.

### Automation — `Simulation/Automation/` (Phase 3: Foundation Only)

The automation engine exists as **data types and evaluation logic only**. It is NOT integrated into `GameSimulation`'s tick loop. The rule engine can be evaluated in isolation (and is fully tested that way), but no automation rules are actively driving runner behavior at runtime. GameSimulation still uses hardcoded Phase 2 behavior for the deposit-and-return loop.

**Two-layer automation design (future phases):**
- **Macro layer (Phase 4):** Handles task assignment, navigation between nodes, task sequencing — what to do, where, in what order. Examples: "go gather copper at the mine", "when full, deposit at hub and come back", "when bank has 200 copper, switch to oak." This phase replaces the hardcoded deposit-and-return loop.
- **Micro layer (Phase 5, with combat):** Handles behavior within a task. Examples: which resource to gather at a multi-resource node, combat ability rotation, targeting priority.

The rule engine uses a **data-driven, first-match-wins priority rule** design. No class hierarchy or polymorphism — flat enums + parameter fields for serialization compatibility. Adding a new condition/action type = add enum value + add case to switch statement.

**`ConditionType.cs`** — Enum of condition types: Always, InventoryFull, InventorySlots, InventoryContains, BankContains, SkillLevel, RunnerStateIs, AtNode, SelfHP (placeholder for Phase 5 combat).

**`ActionType.cs`** — Enum of action types: Idle, TravelTo, GatherAt, ReturnToHub, DepositAndResume, FleeToHub.

**`ComparisonOperator.cs`** — Enum: GreaterThan, GreaterOrEqual, LessThan, LessOrEqual, Equal, NotEqual.

**`Condition.cs`** — `[Serializable]` data class with Type, Operator, NumericValue, StringParam, IntParam. Static factory methods for readable construction (e.g., `Condition.InventoryFull()`, `Condition.BankContains("iron", GreaterOrEqual, 50)`).

**`AutomationAction.cs`** — `[Serializable]` data class with Type, StringParam (nodeId), IntParam (gatherableIndex). Static factory methods (e.g., `AutomationAction.GatherAt("mine", 0)`).

**`Rule.cs`** — `[Serializable]`: `List<Condition>` (AND composition), `AutomationAction`, `Enabled`, `FinishCurrentTrip` (default true — FleeToHub ignores this), `Label`.

**`Ruleset.cs`** — `[Serializable]`: `List<Rule>`, `DeepCopy()` for templates/copy-paste.

**`EvaluationContext.cs`** — Struct wrapping Runner + GameState + SimulationConfig. Created once per evaluation, not stored.

**`RuleEvaluator.cs`** — Static, pure methods. `EvaluateRuleset()` returns first matching rule index or -1. `EvaluateCondition()` switches on ConditionType. `Compare()` generic numeric comparison. "Always compute, never cache."

**`DefaultRulesets.cs`** — Factory: `CreateGathererDefault()` returns a single-rule ruleset: `IF InventoryFull THEN DepositAndResume`. Intended to replicate the hardcoded BeginAutoReturn behavior once the engine is integrated in Phase 4. Currently data-only — not evaluated at runtime.

**`ActionExecutor.cs`** — Placeholder static class. Will translate automation actions into GameSimulation commands in Phase 4. Currently defines the interface but is not called by any runtime code path.

**`DecisionLogEntry.cs`** — `[Serializable]`: TickNumber, GameTime, RunnerId, RunnerName, RuleIndex, RuleLabel, TriggerReason, ActionType, ActionDetail, ConditionSnapshot, WasDeferred. One entry per rule firing.

**`DecisionLog.cs`** — `[Serializable]`: Ring-buffer list storage with configurable max entries. `GetForRunner()` and `GetInRange()` filter methods (most recent first). The player's primary "why did my runner do that?" debugging tool. Not yet populated at runtime — will be written to when automation rules fire in Phase 4.

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

The debug UI (`OnGUI`) includes an automation data panel for inspecting and editing runner rulesets and viewing the decision log, but no automation is actively running — it is purely for inspection/editing of the data structures that will drive behavior in Phase 4.

The debug UI is organized into four panels:

**Top-center — Runner selector:** Compact multi-column table of all runners. Shows name and abbreviated state. Click to select.

**Left panel — Runner info & commands:**
- Runner name, state, location
- Travel progress (when traveling)
- Gathering progress (when gathering)
- Auto-return sub-state (when in deposit loop)
- Inventory summary (slot count, items by type)
- Stop Gathering button (when gathering)
- Gather buttons per gatherable at the current node (showing index, item name, level requirement)
- Zone list for travel/redirect: when Idle, shows "Send to:" with all other nodes. When Traveling, shows "Redirect to:" with all nodes except current destination (including origin node, for turning around).

**Bottom-center — Pawn generation & Guild Bank:**
- Random and Tutorial pawn generation buttons
- Tick count and elapsed time
- Guild bank contents

**Right panel — Skills & Live Stats:**
- All 15 skills: level, passion marker (yellow P), effective level, XP progress bar (current/needed)
- Live stats: travel speed, travel ETA/distance, Athletics XP/tick, gathering ticks/item + items/min, gathering XP/tick (with passion indicator), passion summary

### `View/VisualSyncSystem.cs`
Bridges sim state to 3D world. Builds visual representations of world nodes (colored cylinders with floating labels) and runners (capsule primitives), updates runner positions each `LateUpdate()`.

Runner position calculation (`GetRunnerWorldPosition`):
- **Traveling:** Lerps between start and destination using `Travel.Progress`. If `StartWorldX`/`StartWorldZ` is set (redirect), uses those as the start position instead of the FromNode's position — this prevents visual snapping when a runner changes direction mid-travel.
- **Idle at a node:** Places runners at the node position with a small circular spread so multiple idle runners don't stack on each other.

Subscribes to `RunnerCreated` events to spawn visuals for runners added at runtime (e.g. pawn generation buttons).

### `View/CameraController.cs`
Orbit camera with zoom. Uses Unity's New Input System (inline action definitions). Right mouse drag to orbit, scroll wheel to zoom. Snaps instantly when switching runner targets via `SetTarget()`.

### `View/Runners/RunnerVisual.cs`
MonoBehaviour attached to each runner's 3D representation. Handles interpolated movement between positions set by VisualSyncSystem — the simulation ticks at 10/sec but the view renders at 60fps, so RunnerVisual smoothly interpolates between tick positions over one tick interval. Also creates and manages a floating name label (TextMeshPro) that billboards toward the camera.

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
New game creation, tick counting, time accumulation, travel commands, travel progress/completion, Athletics speed scaling, config-driven speed, event publishing, map-based travel.

### `Tests/Editor/ItemTests.cs`
ItemRegistry register/get, ItemDefinition construction, stackable vs non-stackable defaults.

### `Tests/Editor/InventoryTests.cs`
TryAdd non-stackable, TryAdd stackable with stacking, slot limits, Remove, IsFull, CountItem, Clear.

### `Tests/Editor/BankTests.cs`
Deposit, DepositAll from inventory, Withdraw into inventory, CountItem, infinite stacking.

### `Tests/Editor/GatheringTests.cs`
CommandGather validation (must be Idle, must be at node with gatherables), item production rate (ticks match config), XP-per-tick awards, level-up event + speed recalculation, higher skill = faster gathering, passion speed boost, inventory-full triggers auto-return to hub, full deposit-and-return loop, multiple loop accumulation in bank, GatheringStarted fires on resume.

### `Tests/Editor/WorldMapTests.cs`
Node lookup, direct distance, multi-hop pathfinding (Dijkstra), same-node path, Euclidean fallback with TravelDistanceScale. Starter map tests: hub and nodes exist, pathfinding works, shortest route selection.

### `Tests/Editor/AutomationConditionTests.cs`
Tests the automation engine in isolation (no GameSimulation). All 9 condition types evaluated against hand-built `EvaluationContext`s. Verifies Compare helper, all 6 operators, InventoryFull, InventorySlots, InventoryContains, BankContains, SkillLevel, RunnerStateIs, AtNode, Always, SelfHP (always false until Phase 5 combat).

### `Tests/Editor/AutomationRuleTests.cs`
Tests the automation engine in isolation (no GameSimulation). AND composition (multiple conditions), first-match-wins ordering, disabled rules skipped, empty conditions = always true, DeepCopy independence.

### `Tests/Editor/DecisionLogTests.cs`
Add/retrieve entries, ring buffer eviction (oldest removed), SetMaxEntries evicts existing, filter by runner (most recent first), filter by tick range, no matches returns empty, Clear removes all.

### `Tests/Editor/RedirectTests.cs`
Uses a simple 3-node right triangle map (A, B, C) with constant speed for predictable math. Tests: basic redirect (changes destination, sets StartWorld override, virtual position correct, Euclidean TotalDistance, resets DistanceCovered, preserves FromNodeId, arrives at new destination), redirect to current destination is no-op, redirect back to origin (works, arrives, correct virtual pos and distance), chained redirects (virtual position correct, arrives at final destination, back-and-forth stress test), normal travel has no StartWorld override, redirect publishes RunnerStartedTravel event, edge cases (idle runner starts normal travel, redirect at progress zero).

---

## Key Design Patterns

**Config-driven values:** Every tunable number lives in `SimulationConfig`. Code reads `config.BaseTravelSpeed`, never `1.0f`. Tests can inject custom configs to verify behavior at different tuning points.

**SO authoring → plain C# runtime:** ScriptableObjects are the authoring layer. Each SO type has a `To*()` method that converts to a plain C# struct/class. The simulation only sees plain data — never SOs, never UnityEngine types. This keeps the simulation testable and portable.

**String IDs everywhere (simulation layer):** Items, nodes, runners — all identified by string IDs at runtime. SOs use direct references (dropdown pickers) for authoring convenience, but `To*()` extracts the string ID at the boundary.

**Gatherables on nodes, not in global config:** Each `WorldNode` carries its own `GatherableConfig[]`. A mine can have copper + iron with different level requirements. What you can do at a node is determined by its data, not any label.

**XP decoupled from speed:** XP is awarded per tick of activity, not per item produced. Gathering speed only affects economic output (items per trip). This means leveling is about which resource you grind, while speed is about efficiency.

**Explicit RNG:** `RunnerFactory` takes a `System.Random` parameter instead of using static/shared RNG. This makes tests deterministic — same seed, same runner, every time.

**Events over polling:** The view layer subscribes to events (`RunnerArrivedAtNode`) rather than checking runner state every frame. This is both more efficient and cleaner — the view only reacts when something actually happens.

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

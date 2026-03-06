using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGuild.Simulation.Automation;

namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// Player-facing Chronicle. Subscribes to the same EventBus events as
    /// EventLogService but produces human-readable text entries.
    /// Completely separate from EventLogService (which is dev/debug only).
    /// </summary>
    public class ChronicleService
    {
        private readonly List<ChronicleEntry> _entries = new();
        private int _maxEntries;
        private readonly Func<GameState> _getState;
        private readonly Func<SimulationConfig> _getConfig;

        public ChronicleService(int maxEntries, Func<GameState> getState,
            Func<SimulationConfig> getConfig = null)
        {
            _maxEntries = maxEntries;
            _getState = getState;
            _getConfig = getConfig;
        }

        public IReadOnlyList<ChronicleEntry> Entries => _entries;

        public void SetMaxEntries(int max)
        {
            _maxEntries = max;
            Evict();
        }

        public void Add(ChronicleEntry entry)
        {
            // Collapsing: consecutive entries with same CollapseKey + RunnerId merge
            if (entry.CollapseKey != null && _entries.Count > 0)
            {
                var last = _entries[_entries.Count - 1];
                if (last.CollapseKey == entry.CollapseKey && last.RunnerId == entry.RunnerId)
                {
                    last.RepeatCount++;
                    last.TickNumber = entry.TickNumber;
                    last.GameTime = entry.GameTime;
                    return;
                }
            }

            entry.RepeatCount = 1;
            _entries.Add(entry);
            Evict();
        }

        public void Clear()
        {
            _entries.Clear();
        }

        // ─── Query Methods ────────────────────────────────────────

        /// <summary>All entries, most recent first.</summary>
        public List<ChronicleEntry> GetAll()
        {
            var result = new List<ChronicleEntry>(_entries.Count);
            for (int i = _entries.Count - 1; i >= 0; i--)
                result.Add(_entries[i]);
            return result;
        }

        /// <summary>Entries for a specific runner, most recent first.</summary>
        public List<ChronicleEntry> GetForRunner(string runnerId)
        {
            var result = new List<ChronicleEntry>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].RunnerId == runnerId)
                    result.Add(_entries[i]);
            }
            return result;
        }

        /// <summary>Entries that happened at a specific node, most recent first.</summary>
        public List<ChronicleEntry> GetForNode(string nodeId)
        {
            var result = new List<ChronicleEntry>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].NodeId == nodeId)
                    result.Add(_entries[i]);
            }
            return result;
        }

        /// <summary>Most recent N entries, most recent first.</summary>
        public List<ChronicleEntry> GetRecent(int count)
        {
            var result = new List<ChronicleEntry>(Math.Min(count, _entries.Count));
            int start = Math.Max(0, _entries.Count - count);
            for (int i = _entries.Count - 1; i >= start; i--)
                result.Add(_entries[i]);
            return result;
        }

        // ─── Subscription ─────────────────────────────────────────

        public void SubscribeAll(EventBus events)
        {
            events.Subscribe<RunnerCreated>(OnRunnerCreated);
            events.Subscribe<RunnerSkillLeveledUp>(OnLeveledUp);
            events.Subscribe<RunnerStartedTravel>(OnStartedTravel);
            events.Subscribe<RunnerArrivedAtNode>(OnArrived);
            events.Subscribe<GatheringStarted>(OnGatheringStarted);
            events.Subscribe<GatheringFailed>(OnGatheringFailed);
            events.Subscribe<ItemGathered>(OnItemGathered);
            events.Subscribe<InventoryFull>(OnInventoryFull);
            events.Subscribe<RunnerDeposited>(OnDeposited);
            events.Subscribe<TaskSequenceChanged>(OnTaskSequenceChanged);
            events.Subscribe<TaskSequenceStepAdvanced>(OnStepAdvanced);
            events.Subscribe<TaskSequenceCompleted>(OnSequenceCompleted);
            events.Subscribe<AutomationRuleFired>(OnRuleFired);
            events.Subscribe<AutomationPendingActionExecuted>(OnPendingExecuted);
            events.Subscribe<NoMicroRuleMatched>(OnNoMicroMatch);
            events.Subscribe<CombatStarted>(OnCombatStarted);
            events.Subscribe<CombatActionCompleted>(OnCombatActionCompleted);
            events.Subscribe<EnemyDied>(OnEnemyDied);
            events.Subscribe<LootDropped>(OnLootDropped);
            events.Subscribe<RunnerDied>(OnRunnerDied);
            events.Subscribe<RunnerRespawned>(OnRunnerRespawned);
            events.Subscribe<RunnerTookDamage>(OnRunnerTookDamage);
            // Note: SimulationTickCompleted intentionally NOT subscribed (Lifecycle noise)
            // Note: EnemySpawned intentionally NOT subscribed (redundant with CombatStarted)
        }

        // ─── Handlers ─────────────────────────────────────────────

        private long CurrentTick => _getState()?.TickCount ?? 0;
        private float CurrentGameTime => _getState()?.TotalTimeElapsed ?? 0f;

        private string RunnerName(string runnerId)
        {
            var state = _getState();
            if (state == null) return runnerId;
            var runner = state.Runners.Find(r => r.Id == runnerId);
            return runner?.Name ?? runnerId;
        }

        private string NodeName(string nodeId)
        {
            if (nodeId == null) return "Unknown";
            var node = _getState()?.Map?.GetNode(nodeId);
            return node?.Name ?? nodeId;
        }

        private string RunnerNodeId(string runnerId)
        {
            var state = _getState();
            var runner = state?.Runners.Find(r => r.Id == runnerId);
            return runner?.CurrentNodeId;
        }

        private string AbilityName(string abilityId)
        {
            var config = _getConfig?.Invoke();
            if (config?.AbilityDefinitions == null) return abilityId ?? "Unknown";
            foreach (var a in config.AbilityDefinitions)
            {
                if (a.Id == abilityId) return a.Name ?? abilityId;
            }
            return abilityId ?? "Unknown";
        }

        private string EnemyName(string configId)
        {
            var config = _getConfig?.Invoke();
            if (config?.EnemyDefinitions == null) return configId ?? "Unknown";
            foreach (var e in config.EnemyDefinitions)
            {
                if (e.Id == configId) return e.Name ?? configId;
            }
            return configId ?? "Unknown";
        }

        private string EnemyNameFromInstanceId(string instanceId)
        {
            var state = _getState();
            if (state?.EncounterStates == null) return instanceId ?? "Unknown";
            foreach (var encounter in state.EncounterStates.Values)
            {
                foreach (var enemy in encounter.Enemies)
                {
                    if (enemy.InstanceId == instanceId)
                        return EnemyName(enemy.ConfigId);
                }
            }
            return instanceId ?? "Unknown";
        }

        private void OnRunnerCreated(RunnerCreated e)
        {
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.Lifecycle,
                RunnerId = e.RunnerId,
                RunnerName = e.RunnerName,
                Text = $"{e.RunnerName} joined the guild",
            });
        }

        private void OnLeveledUp(RunnerSkillLeveledUp e)
        {
            string name = RunnerName(e.RunnerId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.Production,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = RunnerNodeId(e.RunnerId),
                Text = $"{name} leveled up {e.Skill} to {e.NewLevel}!",
            });
        }

        private void OnStartedTravel(RunnerStartedTravel e)
        {
            string name = RunnerName(e.RunnerId);
            string toName = NodeName(e.ToNodeId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.StateChange,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = e.FromNodeId,
                Text = $"{name} started traveling to {toName} ({e.EstimatedDurationSeconds:F1}s)",
            });
        }

        private void OnArrived(RunnerArrivedAtNode e)
        {
            string name = RunnerName(e.RunnerId);
            string nodeName = NodeName(e.NodeId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.StateChange,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = e.NodeId,
                Text = $"{name} arrived at {nodeName}",
            });
        }

        private void OnGatheringStarted(GatheringStarted e)
        {
            string name = RunnerName(e.RunnerId);
            string nodeName = NodeName(e.NodeId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.StateChange,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = e.NodeId,
                Text = $"{name} started gathering {e.ItemId} at {nodeName}",
                CollapseKey = $"GatherStart:{e.RunnerId}:{e.ItemId}",
            });
        }

        private void OnGatheringFailed(GatheringFailed e)
        {
            string name = RunnerName(e.RunnerId);
            string text;
            if (e.Reason == GatheringFailureReason.NotEnoughSkill)
                text = $"{name}: {e.Skill} level too low ({e.CurrentLevel}/{e.RequiredLevel})";
            else if (e.Reason == GatheringFailureReason.NoGatherablesAtNode)
                text = $"{name}: no gatherables at this node";
            else
                text = $"{name}: gathering failed ({e.Reason})";

            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.Warning,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = e.NodeId,
                Text = text,
            });
        }

        private void OnItemGathered(ItemGathered e)
        {
            string name = RunnerName(e.RunnerId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.Production,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = RunnerNodeId(e.RunnerId),
                Text = $"{name} gathered {e.ItemId} ({e.InventoryFreeSlots} slots free)",
                CollapseKey = $"ItemGathered:{e.RunnerId}:{e.ItemId}",
            });
        }

        private void OnInventoryFull(InventoryFull e)
        {
            string name = RunnerName(e.RunnerId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.StateChange,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = RunnerNodeId(e.RunnerId),
                Text = $"{name}'s inventory is full",
            });
        }

        private void OnDeposited(RunnerDeposited e)
        {
            string name = RunnerName(e.RunnerId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.StateChange,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = RunnerNodeId(e.RunnerId),
                Text = $"{name} deposited {e.ItemsDeposited} items",
            });
        }

        private void OnTaskSequenceChanged(TaskSequenceChanged e)
        {
            string name = RunnerName(e.RunnerId);
            string target = e.TargetNodeId != null ? NodeName(e.TargetNodeId) : "none";
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.Automation,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = RunnerNodeId(e.RunnerId),
                Text = $"{name} assigned to {target} ({e.Reason})",
            });
        }

        private void OnStepAdvanced(TaskSequenceStepAdvanced e)
        {
            string name = RunnerName(e.RunnerId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.StateChange,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = RunnerNodeId(e.RunnerId),
                Text = $"{name} advancing to step {e.StepIndex + 1} ({e.StepType})",
                CollapseKey = $"Step:{e.RunnerId}:{e.StepType}",
            });
        }

        private void OnSequenceCompleted(TaskSequenceCompleted e)
        {
            string name = RunnerName(e.RunnerId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.Automation,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = RunnerNodeId(e.RunnerId),
                Text = $"{name} completed task sequence: {e.SequenceName}",
            });
        }

        private void OnRuleFired(AutomationRuleFired e)
        {
            string name = RunnerName(e.RunnerId);
            string deferred = e.WasDeferred ? " (deferred)" : "";
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.Automation,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = RunnerNodeId(e.RunnerId),
                Text = $"{name}: rule fired \"{e.RuleLabel}\" -> {e.ActionType}{deferred}",
            });
        }

        private void OnPendingExecuted(AutomationPendingActionExecuted e)
        {
            string name = RunnerName(e.RunnerId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.Automation,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = RunnerNodeId(e.RunnerId),
                Text = $"{name}: deferred action executed ({e.ActionType})",
            });
        }

        private void OnNoMicroMatch(NoMicroRuleMatched e)
        {
            string name = RunnerName(e.RunnerId);
            string detail = e.RulesetIsEmpty ? "no micro rules configured" : $"no rule matched ({e.RuleCount} rules)";
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.Warning,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = e.NodeId,
                Text = $"{name}: {detail}",
                CollapseKey = $"NoMicroMatch:{e.RunnerId}:{e.NodeId}",
            });
        }

        // ─── Combat Handlers ─────────────────────────────────────

        private void OnCombatStarted(CombatStarted e)
        {
            string name = RunnerName(e.RunnerId);
            string nodeName = NodeName(e.NodeId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.StateChange,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = e.NodeId,
                Text = $"{name} engaged enemies at {nodeName}",
            });
        }

        private void OnCombatActionCompleted(CombatActionCompleted e)
        {
            string name = RunnerName(e.RunnerId);
            string abilityName = AbilityName(e.AbilityId);
            string targetName = EnemyNameFromInstanceId(e.TargetEnemyInstanceId);
            bool isHeal = e.PrimaryEffectType == Combat.EffectType.Heal
                || e.PrimaryEffectType == Combat.EffectType.HealSelf
                || e.PrimaryEffectType == Combat.EffectType.HealAoe;
            string text = isHeal
                ? $"{name} used {abilityName} for {e.Value:F2} healing"
                : $"{name} used {abilityName} on {targetName} for {e.Value:F2} damage";
            if (e.WasKill) text += " (killed)";

            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.Production,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = RunnerNodeId(e.RunnerId),
                Text = text,
            });
        }

        private void OnEnemyDied(EnemyDied e)
        {
            string name = RunnerName(e.KillerRunnerId);
            string enemyName = EnemyName(e.EnemyConfigId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.Production,
                RunnerId = e.KillerRunnerId,
                RunnerName = name,
                NodeId = e.NodeId,
                Text = $"{name} defeated {enemyName}",
            });
        }

        private void OnLootDropped(LootDropped e)
        {
            string name = RunnerName(e.RunnerId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.Production,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = RunnerNodeId(e.RunnerId),
                Text = $"{name} received {e.Quantity}x {e.ItemId}",
            });
        }

        private void OnRunnerDied(RunnerDied e)
        {
            string name = RunnerName(e.RunnerId);
            string nodeName = NodeName(e.NodeId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.Warning,
                RunnerId = e.RunnerId,
                RunnerName = name,
                NodeId = e.NodeId,
                Text = $"{name} was slain at {nodeName}",
            });
        }

        private void OnRunnerRespawned(RunnerRespawned e)
        {
            string name = RunnerName(e.RunnerId);
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.StateChange,
                RunnerId = e.RunnerId,
                RunnerName = name,
                Text = $"{name} has recovered at the Guild Hall",
            });
        }

        private void OnRunnerTookDamage(RunnerTookDamage e)
        {
            string runnerName = RunnerName(e.RunnerId);
            string enemyName = EnemyNameFromInstanceId(e.EnemyInstanceId);
            float maxHp = 0f;
            var state = _getState();
            var runner = state?.Runners.Find(r => r.Id == e.RunnerId);
            if (runner != null)
            {
                var config = _getConfig?.Invoke();
                if (config != null)
                    maxHp = Combat.CombatFormulas.CalculateMaxHitpoints(
                        runner.GetEffectiveLevel(SkillType.Hitpoints, config), config);
            }
            float hpPercent = maxHp > 0f ? (e.RemainingHp / maxHp) * 100f : 0f;
            Add(new ChronicleEntry
            {
                TickNumber = CurrentTick,
                GameTime = CurrentGameTime,
                Category = EventCategory.Production,
                RunnerId = e.RunnerId,
                RunnerName = runnerName,
                NodeId = RunnerNodeId(e.RunnerId),
                Text = $"{enemyName} attacked {runnerName} for {e.Damage:F2} damage ({runnerName} at {hpPercent:F2}%)",
            });
        }

        // ─── Internal ─────────────────────────────────────────────

        private void Evict()
        {
            while (_entries.Count > _maxEntries)
                _entries.RemoveAt(0);
        }
    }
}

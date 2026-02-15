using System;
using System.Collections.Generic;
using ProjectGuild.Simulation.Automation;

namespace ProjectGuild.Simulation.Core
{
    public class EventLogService
    {
        private readonly List<EventLogEntry> _entries = new();
        private int _maxEntries;
        private readonly Func<GameState> _getState;

        public EventLogService(int maxEntries, Func<GameState> getState)
        {
            _maxEntries = maxEntries;
            _getState = getState;
        }

        public IReadOnlyList<EventLogEntry> Entries => _entries;

        /// <summary>
        /// When true, consecutive entries with the same CollapseKey + RunnerId
        /// merge into one entry with an incrementing RepeatCount.
        /// Default: false (every event is a separate entry).
        /// </summary>
        public bool CollapsingEnabled { get; set; }

        public void SetMaxEntries(int max)
        {
            _maxEntries = max;
            Evict();
        }

        public void Add(EventLogEntry entry)
        {
            if (CollapsingEnabled && entry.CollapseKey != null && _entries.Count > 0)
            {
                var last = _entries[_entries.Count - 1];
                if (last.CollapseKey == entry.CollapseKey && last.RunnerId == entry.RunnerId)
                {
                    last.RepeatCount++;
                    last.TickNumber = entry.TickNumber;
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

        public List<EventLogEntry> GetAll()
        {
            var result = new List<EventLogEntry>(_entries.Count);
            for (int i = _entries.Count - 1; i >= 0; i--)
                result.Add(_entries[i]);
            return result;
        }

        public List<EventLogEntry> GetForRunner(string runnerId)
        {
            var result = new List<EventLogEntry>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].RunnerId == runnerId)
                    result.Add(_entries[i]);
            }
            return result;
        }

        public List<EventLogEntry> GetWarnings()
        {
            var result = new List<EventLogEntry>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Category == EventCategory.Warning)
                    result.Add(_entries[i]);
            }
            return result;
        }

        public List<EventLogEntry> GetActivityFeed(string runnerId)
        {
            var result = new List<EventLogEntry>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.RunnerId == runnerId && e.Category != EventCategory.Lifecycle)
                    result.Add(e);
            }
            return result;
        }

        public List<EventLogEntry> GetByCategories(params EventCategory[] categories)
        {
            var set = new HashSet<EventCategory>(categories);
            var result = new List<EventLogEntry>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (set.Contains(_entries[i].Category))
                    result.Add(_entries[i]);
            }
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
            events.Subscribe<SimulationTickCompleted>(OnTick);
        }

        // ─── Handlers ─────────────────────────────────────────────

        private long CurrentTick => _getState()?.TickCount ?? 0;

        private void OnRunnerCreated(RunnerCreated e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.Lifecycle,
                RunnerId = e.RunnerId,
                Summary = $"RunnerCreated {{ Name={e.RunnerName} }}",
            });
        }

        private void OnLeveledUp(RunnerSkillLeveledUp e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.Production,
                RunnerId = e.RunnerId,
                Summary = $"RunnerSkillLeveledUp {{ Skill={e.Skill}, NewLevel={e.NewLevel} }}",
            });
        }

        private void OnStartedTravel(RunnerStartedTravel e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.StateChange,
                RunnerId = e.RunnerId,
                Summary = $"RunnerStartedTravel {{ From={e.FromNodeId}, To={e.ToNodeId}, Duration={e.EstimatedDurationSeconds:F1}s }}",
            });
        }

        private void OnArrived(RunnerArrivedAtNode e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.StateChange,
                RunnerId = e.RunnerId,
                Summary = $"RunnerArrivedAtNode {{ NodeId={e.NodeId} }}",
            });
        }

        private void OnGatheringStarted(GatheringStarted e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.StateChange,
                RunnerId = e.RunnerId,
                Summary = $"GatheringStarted {{ NodeId={e.NodeId}, ItemId={e.ItemId}, Skill={e.Skill} }}",
            });
        }

        private void OnGatheringFailed(GatheringFailed e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.Warning,
                RunnerId = e.RunnerId,
                Summary = $"GatheringFailed {{ Reason={e.Reason}, ItemId={e.ItemId}, Skill={e.Skill}, Level={e.CurrentLevel}/{e.RequiredLevel} }}",
            });
        }

        private void OnItemGathered(ItemGathered e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.Production,
                RunnerId = e.RunnerId,
                Summary = $"ItemGathered {{ ItemId={e.ItemId}, FreeSlots={e.InventoryFreeSlots} }}",
                CollapseKey = $"ItemGathered:{e.RunnerId}:{e.ItemId}",
            });
        }

        private void OnInventoryFull(InventoryFull e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.StateChange,
                RunnerId = e.RunnerId,
                Summary = "InventoryFull",
            });
        }

        private void OnDeposited(RunnerDeposited e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.StateChange,
                RunnerId = e.RunnerId,
                Summary = $"RunnerDeposited {{ ItemsDeposited={e.ItemsDeposited} }}",
            });
        }

        private void OnTaskSequenceChanged(TaskSequenceChanged e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.Automation,
                RunnerId = e.RunnerId,
                Summary = $"TaskSequenceChanged {{ TargetNodeId={e.TargetNodeId ?? "null"}, Reason={e.Reason} }}",
            });
        }

        private void OnStepAdvanced(TaskSequenceStepAdvanced e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.StateChange,
                RunnerId = e.RunnerId,
                Summary = $"TaskSequenceStepAdvanced {{ StepType={e.StepType}, StepIndex={e.StepIndex} }}",
                CollapseKey = $"Step:{e.RunnerId}:{e.StepType}",
            });
        }

        private void OnSequenceCompleted(TaskSequenceCompleted e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.Automation,
                RunnerId = e.RunnerId,
                Summary = $"TaskSequenceCompleted {{ Name={e.SequenceName} }}",
            });
        }

        private void OnRuleFired(AutomationRuleFired e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.Automation,
                RunnerId = e.RunnerId,
                Summary = $"AutomationRuleFired {{ RuleIndex={e.RuleIndex}, Label={e.RuleLabel}, Action={e.ActionType}, Trigger={e.TriggerReason}, Deferred={e.WasDeferred} }}",
            });
        }

        private void OnPendingExecuted(AutomationPendingActionExecuted e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.Automation,
                RunnerId = e.RunnerId,
                Summary = $"AutomationPendingActionExecuted {{ ActionType={e.ActionType}, Detail={e.ActionDetail} }}",
            });
        }

        private void OnNoMicroMatch(NoMicroRuleMatched e)
        {
            Add(new EventLogEntry
            {
                TickNumber = CurrentTick,
                Category = EventCategory.Warning,
                RunnerId = e.RunnerId,
                Summary = $"NoMicroRuleMatched {{ NodeId={e.NodeId}, RulesetIsEmpty={e.RulesetIsEmpty}, RuleCount={e.RuleCount} }}",
                CollapseKey = $"NoMicroMatch:{e.RunnerId}:{e.NodeId}",
            });
        }

        private void OnTick(SimulationTickCompleted e)
        {
            Add(new EventLogEntry
            {
                TickNumber = e.TickNumber,
                Category = EventCategory.Lifecycle,
                Summary = $"SimulationTickCompleted {{ TickNumber={e.TickNumber} }}",
                CollapseKey = "Tick",
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

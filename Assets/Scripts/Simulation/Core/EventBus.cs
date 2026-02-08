using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// Lightweight publish/subscribe event system for decoupling simulation from view.
    ///
    /// Usage:
    ///   // Subscribe
    ///   eventBus.Subscribe<RunnerLeveledUp>(OnRunnerLeveledUp);
    ///
    ///   // Publish (from simulation code)
    ///   eventBus.Publish(new RunnerLeveledUp { RunnerId = id, Skill = skill, NewLevel = 5 });
    ///
    ///   // Unsubscribe
    ///   eventBus.Unsubscribe<RunnerLeveledUp>(OnRunnerLeveledUp);
    ///
    /// Events are structs (not classes) so that publishing an event doesn't allocate
    /// heap memory. Since Tick() runs 10x/sec and may publish dozens of events per tick,
    /// using classes would create garbage for the GC to collect, causing micro-stutters.
    /// Structs live on the stack and are free.
    ///
    /// Events are delivered synchronously: when Publish() is called, all handlers run
    /// immediately before Publish() returns. There is no queue or "process events later"
    /// step. This means when the simulation calls Publish(RunnerArrivedAtNode),
    /// any subscribed view code (e.g. play arrival animation) runs right then and there,
    /// mid-tick. This is simple and predictable — the downside is that a slow handler
    /// would block the tick, but our handlers should be lightweight (just trigger
    /// animations or update UI state, not do heavy work).
    /// </summary>
    public class EventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _subscribers = new();

        public void Subscribe<T>(Action<T> handler) where T : struct
        {
            var type = typeof(T);
            if (!_subscribers.TryGetValue(type, out var list))
            {
                list = new List<Delegate>();
                _subscribers[type] = list;
            }
            list.Add(handler);
        }

        public void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            var type = typeof(T);
            if (_subscribers.TryGetValue(type, out var list))
            {
                list.Remove(handler);
            }
        }

        public void Publish<T>(T evt) where T : struct
        {
            var type = typeof(T);
            if (_subscribers.TryGetValue(type, out var list))
            {
                // Iterate by index (backwards) to allow handlers to unsubscribe during iteration
                // (they'll be removed from the list but we won't skip any)
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (i < list.Count && list[i] is Action<T> handler)
                    {
                        handler(evt);
                    }
                }
            }
        }

        public void Clear()
        {
            _subscribers.Clear();
        }
    }
}

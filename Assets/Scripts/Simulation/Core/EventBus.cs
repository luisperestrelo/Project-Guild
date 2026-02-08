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
    /// Events should be structs to avoid allocations. The EventBus does not queue events —
    /// handlers are called synchronously during Publish(). This is intentional: simulation
    /// publishes events during its tick, and the view layer reacts immediately.
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

using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// Ring-buffer log of automation decisions. Stores the most recent N entries
    /// (configured via SimulationConfig.DecisionLogMaxEntries). Provides filtering
    /// by runner and tick range for the debug UI.
    /// </summary>
    [Serializable]
    public class DecisionLog
    {
        public List<DecisionLogEntry> Entries = new();

        private int _maxEntries = 100;

        public void SetMaxEntries(int max)
        {
            _maxEntries = max;
            Evict();
        }

        public void Add(DecisionLogEntry entry)
        {
            Entries.Add(entry);
            Evict();
        }

        /// <summary>
        /// Get all entries for a specific runner, most recent first.
        /// </summary>
        public List<DecisionLogEntry> GetForRunner(string runnerId)
        {
            var result = new List<DecisionLogEntry>();
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].RunnerId == runnerId)
                    result.Add(Entries[i]);
            }
            return result;
        }

        /// <summary>
        /// Get all entries within a tick range (inclusive), most recent first.
        /// </summary>
        public List<DecisionLogEntry> GetInRange(long fromTick, long toTick)
        {
            var result = new List<DecisionLogEntry>();
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                var entry = Entries[i];
                if (entry.TickNumber >= fromTick && entry.TickNumber <= toTick)
                    result.Add(entry);
            }
            return result;
        }

        public void Clear()
        {
            Entries.Clear();
        }

        private void Evict()
        {
            while (Entries.Count > _maxEntries)
                Entries.RemoveAt(0);
        }
    }
}

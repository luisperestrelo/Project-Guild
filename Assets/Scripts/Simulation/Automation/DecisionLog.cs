using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Automation
{
    /// <summary>
    /// Ring-buffer log of automation decisions. Stores the most recent N entries
    /// (configured via SimulationConfig). Provides filtering by runner, node,
    /// and layer for the Decision Log UI.
    /// GenerationCounter increments on every Add() so UI controllers can detect
    /// content changes even when the buffer is full (entry count stays constant).
    /// </summary>
    [Serializable]
    public class DecisionLog
    {
        public List<DecisionLogEntry> Entries = new();

        private int _maxEntries = 100;

        /// <summary>
        /// Monotonically increasing counter. Increments on every Add().
        /// Used by UI to detect content changes when the ring buffer is full
        /// (entry count stays constant but generation keeps advancing).
        /// </summary>
        public int GenerationCounter { get; private set; }

        public void SetMaxEntries(int max)
        {
            _maxEntries = max;
            Evict();
        }

        public void Add(DecisionLogEntry entry)
        {
            Entries.Add(entry);
            GenerationCounter++;
            Evict();
        }

        /// <summary>
        /// Get all entries, most recent first. Optionally filter by layer.
        /// </summary>
        public List<DecisionLogEntry> GetAll(DecisionLayer? layer = null)
        {
            var result = new List<DecisionLogEntry>();
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                var entry = Entries[i];
                if (layer == null || entry.Layer == layer)
                    result.Add(entry);
            }
            return result;
        }

        /// <summary>
        /// Get entries for a specific node, most recent first.
        /// Optionally filter by layer (macro/micro).
        /// </summary>
        public List<DecisionLogEntry> GetForNode(string nodeId, DecisionLayer? layer = null)
        {
            var result = new List<DecisionLogEntry>();
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                var entry = Entries[i];
                if (entry.NodeId == nodeId && (layer == null || entry.Layer == layer))
                    result.Add(entry);
            }
            return result;
        }

        /// <summary>
        /// Get all entries for a specific runner, most recent first.
        /// Optionally filter by layer (macro/micro).
        /// </summary>
        public List<DecisionLogEntry> GetForRunner(string runnerId, DecisionLayer? layer = null)
        {
            var result = new List<DecisionLogEntry>();
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                var entry = Entries[i];
                if (entry.RunnerId == runnerId && (layer == null || entry.Layer == layer))
                    result.Add(entry);
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

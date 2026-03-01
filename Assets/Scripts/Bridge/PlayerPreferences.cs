using System;
using System.IO;
using UnityEngine;

namespace ProjectGuild.Bridge
{
    /// <summary>
    /// Player UX preferences that persist across sessions.
    /// Stored separately from GameState (game save) since these are UI-layer concerns.
    /// Saved to {persistentDataPath}/preferences.json.
    /// </summary>
    [Serializable]
    public class PlayerPreferences
    {
        private static string FilePath =>
            Path.Combine(Application.persistentDataPath, "preferences.json");

        // ─── Logbook ─────────────────────────────────────
        public bool LogbookAutoNavigateOnSelection = true;
        public bool LogbookAutoNavigateOnArrival = true;
        public bool LogbookAutoExpandOnNavigation = false;

        // ─── Automation ──────────────────────────────────
        public bool SkipDeleteConfirmation = false;
        public bool SkipCancelCreationConfirmation = false;

        // ─── Default scope filters ──────────────────────
        // Stored as strings to avoid coupling to view-layer enums.
        // Valid values: "CurrentNode", "SelectedRunner", "Global"/"All"
        public string ChronicleDefaultScopeFilter = "CurrentNode";
        public string DecisionLogDefaultScopeFilter = "SelectedRunner";

        /// <summary>
        /// Load preferences from disk. Returns defaults if file doesn't exist or is corrupt.
        /// </summary>
        public static PlayerPreferences Load()
        {
            if (!File.Exists(FilePath))
                return new PlayerPreferences();

            try
            {
                string json = File.ReadAllText(FilePath);
                var prefs = JsonUtility.FromJson<PlayerPreferences>(json);
                return prefs ?? new PlayerPreferences();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayerPreferences] Failed to load preferences: {e.Message}. Using defaults.");
                return new PlayerPreferences();
            }
        }

        /// <summary>
        /// Save preferences to disk immediately.
        /// </summary>
        public void Save()
        {
            try
            {
                string json = JsonUtility.ToJson(this, prettyPrint: true);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayerPreferences] Failed to save preferences: {e.Message}");
            }
        }
    }
}

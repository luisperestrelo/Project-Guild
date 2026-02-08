using UnityEngine;
using System.IO;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.Bridge
{
    /// <summary>
    /// Bridge layer implementation of save/load. Uses Unity's JsonUtility
    /// for serialization and Application.persistentDataPath for file storage.
    ///
    /// Save files are stored as:
    ///   {persistentDataPath}/saves/{slotName}.json
    /// </summary>
    public class SaveManager : ISaveSystem
    {
        private readonly string _saveFolderPath;

        public SaveManager()
        {
            _saveFolderPath = Path.Combine(Application.persistentDataPath, "saves");
            if (!Directory.Exists(_saveFolderPath))
            {
                Directory.CreateDirectory(_saveFolderPath);
            }
        }

        public void Save(GameState state, string slotName)
        {
            string json = JsonUtility.ToJson(state, prettyPrint: true);
            string path = GetSavePath(slotName);
            File.WriteAllText(path, json);
            Debug.Log($"[SaveManager] Game saved to {path}");
        }

        public GameState Load(string slotName)
        {
            string path = GetSavePath(slotName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[SaveManager] Save file not found: {path}");
                return null;
            }

            string json = File.ReadAllText(path);
            var state = JsonUtility.FromJson<GameState>(json);
            Debug.Log($"[SaveManager] Game loaded from {path}");
            return state;
        }

        public bool SaveExists(string slotName)
        {
            return File.Exists(GetSavePath(slotName));
        }

        public void DeleteSave(string slotName)
        {
            string path = GetSavePath(slotName);
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log($"[SaveManager] Save deleted: {path}");
            }
        }

        private string GetSavePath(string slotName)
        {
            return Path.Combine(_saveFolderPath, $"{slotName}.json");
        }
    }
}

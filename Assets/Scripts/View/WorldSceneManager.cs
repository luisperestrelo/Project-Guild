using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProjectGuild.Bridge;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.View
{
    /// <summary>
    /// Manages the lifecycle of additive node scenes. Each world node can have
    /// a dedicated Unity scene that loads when runners arrive and unloads when empty.
    ///
    /// Scenes are authored at local origin — on load, the root NodeSceneRoot
    /// GameObject is moved to a world-space offset so multiple node scenes
    /// don't overlap (each gets its own Z-band: nodeIndex * SceneOffsetSpacing).
    ///
    /// Lifecycle:
    /// - Load when first runner arrives or camera targets a runner there
    /// - Pre-load when a runner starts traveling to a node
    /// - Unload after a grace period when no runners remain and camera is elsewhere
    /// - Hub exception: Guild Hall can be loaded without runners present
    /// </summary>
    public class WorldSceneManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SimulationRunner _simulationRunner;

        [Header("Scene Offset")]
        [Tooltip("Z-axis spacing between loaded node scenes. Each scene is offset by nodeIndex * this value.")]
        [SerializeField] private float _sceneOffsetSpacing = 2000f;

        [Tooltip("Seconds to wait before unloading a scene after it becomes empty and camera is elsewhere.")]
        [SerializeField] private float _unloadGracePeriod = 10f;

        private void OnEnable()
        {
            if (_simulationRunner == null)
                _simulationRunner = FindAnyObjectByType<SimulationRunner>();
        }

        /// <summary>
        /// Tracks state for each loaded (or loading) node scene.
        /// </summary>
        private class LoadedNodeScene
        {
            public string NodeId;
            public string SceneName;
            public Scene Scene;
            public NodeSceneRoot SceneRoot;
            public Vector3 Offset;
            public LoadState State;
            public float EmptySinceTime; // Time.time when last runner left (-1 = not empty)
            public readonly List<Action> PendingCallbacks = new();
        }

        public enum LoadState
        {
            Loading,
            Ready,
            Unloading,
        }

        private readonly Dictionary<string, LoadedNodeScene> _loadedScenes = new();
        private readonly Dictionary<string, int> _nodeIndexLookup = new(); // nodeId → index for offset
        private GameSimulation Sim => _simulationRunner?.Simulation;

        // ─── Public API ──────────────────────────────────────────

        /// <summary>
        /// Initialize the scene manager with the current world map.
        /// Must be called after SimulationRunner.StartNewGame/LoadGame.
        /// Builds the nodeId → index lookup for offset calculations.
        /// </summary>
        public void Initialize()
        {
            ClearAll();

            if (Sim?.CurrentGameState?.Map == null) return;

            var nodes = Sim.CurrentGameState.Map.Nodes;
            for (int i = 0; i < nodes.Count; i++)
                _nodeIndexLookup[nodes[i].Id] = i;

            // Subscribe to travel events for pre-loading
            Sim.Events.Subscribe<RunnerStartedTravel>(OnRunnerStartedTravel);
            Sim.Events.Subscribe<RunnerArrivedAtNode>(OnRunnerArrivedAtNode);
        }

        /// <summary>
        /// Tear down all loaded scenes and unsubscribe from events.
        /// </summary>
        public void ClearAll()
        {
            foreach (var kvp in _loadedScenes)
            {
                if (kvp.Value.State == LoadState.Ready && kvp.Value.Scene.isLoaded)
                {
                    SceneManager.UnloadSceneAsync(kvp.Value.Scene);
                }
            }
            _loadedScenes.Clear();
            _nodeIndexLookup.Clear();

            if (Sim?.Events != null)
            {
                Sim.Events.Unsubscribe<RunnerStartedTravel>(OnRunnerStartedTravel);
                Sim.Events.Unsubscribe<RunnerArrivedAtNode>(OnRunnerArrivedAtNode);
            }
        }

        /// <summary>
        /// Is the scene for this node loaded and ready?
        /// </summary>
        public bool IsNodeSceneReady(string nodeId)
        {
            return _loadedScenes.TryGetValue(nodeId, out var loaded) && loaded.State == LoadState.Ready;
        }

        /// <summary>
        /// Get the world-space offset for a loaded node scene.
        /// Returns Vector3.zero if the scene is not loaded.
        /// </summary>
        public Vector3 GetNodeSceneOffset(string nodeId)
        {
            return _loadedScenes.TryGetValue(nodeId, out var loaded) ? loaded.Offset : Vector3.zero;
        }

        /// <summary>
        /// Get the NodeSceneRoot for a loaded node scene, or null if not ready.
        /// </summary>
        public NodeSceneRoot GetNodeSceneRoot(string nodeId)
        {
            return _loadedScenes.TryGetValue(nodeId, out var loaded) && loaded.State == LoadState.Ready
                ? loaded.SceneRoot
                : null;
        }

        /// <summary>
        /// Ensure a node's scene is loaded. Calls onReady immediately if already loaded,
        /// or queues the callback for when loading completes.
        /// </summary>
        public void EnsureNodeSceneLoaded(string nodeId, Action onReady = null)
        {
            if (_loadedScenes.TryGetValue(nodeId, out var existing))
            {
                // Cancel pending unload
                existing.EmptySinceTime = -1f;

                if (existing.State == LoadState.Ready)
                {
                    onReady?.Invoke();
                    return;
                }
                if (existing.State == LoadState.Loading)
                {
                    if (onReady != null) existing.PendingCallbacks.Add(onReady);
                    return;
                }
            }

            // Need to load
            string sceneName = GetSceneNameForNode(nodeId);
            if (string.IsNullOrEmpty(sceneName))
            {
                // No scene for this node — nothing to load
                onReady?.Invoke();
                return;
            }

            LoadNodeScene(nodeId, sceneName, onReady);
        }

        /// <summary>
        /// Get all loaded node scene entries (for VisualSyncSystem to iterate).
        /// </summary>
        public IEnumerable<string> GetLoadedNodeIds()
        {
            foreach (var kvp in _loadedScenes)
            {
                if (kvp.Value.State == LoadState.Ready)
                    yield return kvp.Key;
            }
        }

        // ─── Load / Unload ───────────────────────────────────────

        private void LoadNodeScene(string nodeId, string sceneName, Action onReady)
        {
            Vector3 offset = CalculateOffset(nodeId);

            var entry = new LoadedNodeScene
            {
                NodeId = nodeId,
                SceneName = sceneName,
                Offset = offset,
                State = LoadState.Loading,
                EmptySinceTime = -1f,
            };
            if (onReady != null) entry.PendingCallbacks.Add(onReady);
            _loadedScenes[nodeId] = entry;

            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (op == null)
            {
                Debug.LogError($"[WorldSceneManager] Failed to start loading scene '{sceneName}' for node '{nodeId}'. " +
                    "Is the scene added to Build Settings?");
                _loadedScenes.Remove(nodeId);
                onReady?.Invoke();
                return;
            }

            op.completed += _ => OnSceneLoaded(entry);
        }

        private void OnSceneLoaded(LoadedNodeScene entry)
        {
            // Find the scene by name
            entry.Scene = SceneManager.GetSceneByName(entry.SceneName);
            if (!entry.Scene.isLoaded)
            {
                Debug.LogError($"[WorldSceneManager] Scene '{entry.SceneName}' for node '{entry.NodeId}' " +
                    "reported loaded but is not found.");
                _loadedScenes.Remove(entry.NodeId);
                return;
            }

            // Find NodeSceneRoot in the loaded scene
            foreach (var rootObj in entry.Scene.GetRootGameObjects())
            {
                entry.SceneRoot = rootObj.GetComponentInChildren<NodeSceneRoot>();
                if (entry.SceneRoot != null) break;
            }

            if (entry.SceneRoot == null)
            {
                Debug.LogWarning($"[WorldSceneManager] Scene '{entry.SceneName}' has no NodeSceneRoot component. " +
                    "Scene content may not be positioned correctly.");
            }
            else
            {
                // Move the scene root to its world-space offset
                entry.SceneRoot.transform.position = entry.Offset;
            }

            entry.State = LoadState.Ready;

            // Fire pending callbacks
            foreach (var callback in entry.PendingCallbacks)
                callback?.Invoke();
            entry.PendingCallbacks.Clear();

            Debug.Log($"[WorldSceneManager] Loaded scene '{entry.SceneName}' for node '{entry.NodeId}' at offset {entry.Offset}");
        }

        private void UnloadNodeScene(string nodeId)
        {
            if (!_loadedScenes.TryGetValue(nodeId, out var entry)) return;
            if (entry.State == LoadState.Unloading) return;

            entry.State = LoadState.Unloading;

            if (entry.Scene.isLoaded)
            {
                var op = SceneManager.UnloadSceneAsync(entry.Scene);
                if (op != null)
                    op.completed += _ => _loadedScenes.Remove(nodeId);
                else
                    _loadedScenes.Remove(nodeId);
            }
            else
            {
                _loadedScenes.Remove(nodeId);
            }

            Debug.Log($"[WorldSceneManager] Unloading scene '{entry.SceneName}' for node '{nodeId}'");
        }

        // ─── Offset Calculation ──────────────────────────────────

        private Vector3 CalculateOffset(string nodeId)
        {
            int index = _nodeIndexLookup.TryGetValue(nodeId, out var i) ? i : 0;
            return new Vector3(0f, 0f, (index + 1) * _sceneOffsetSpacing);
        }

        // ─── Helpers ─────────────────────────────────────────────

        private string GetSceneNameForNode(string nodeId)
        {
            var node = Sim?.CurrentGameState?.Map?.GetNode(nodeId);
            return node?.SceneName;
        }

        /// <summary>
        /// Returns true if any runners are currently at the given node (not traveling).
        /// </summary>
        private bool AnyRunnersAtNode(string nodeId)
        {
            if (Sim?.CurrentGameState?.Runners == null) return false;
            foreach (var runner in Sim.CurrentGameState.Runners)
            {
                if (runner.CurrentNodeId == nodeId && runner.State != RunnerState.Traveling)
                    return true;
            }
            return false;
        }

        // ─── Grace Period Unloading ──────────────────────────────

        private void Update()
        {
            if (Sim == null) return;

            // Check for scenes that should be unloaded
            // Collect keys first to avoid modifying dictionary during iteration
            var nodesToUnload = new List<string>();

            foreach (var kvp in _loadedScenes)
            {
                var entry = kvp.Value;
                if (entry.State != LoadState.Ready) continue;
                if (entry.EmptySinceTime < 0f) continue;

                // Hub exception: don't auto-unload the guild hall
                if (kvp.Key == Sim.CurrentGameState.Map?.HubNodeId) continue;

                // If runners returned, cancel unload
                if (AnyRunnersAtNode(kvp.Key))
                {
                    entry.EmptySinceTime = -1f;
                    continue;
                }

                // Grace period expired?
                if (Time.time - entry.EmptySinceTime >= _unloadGracePeriod)
                    nodesToUnload.Add(kvp.Key);
            }

            foreach (var nodeId in nodesToUnload)
                UnloadNodeScene(nodeId);
        }

        // ─── Event Handlers ──────────────────────────────────────

        /// <summary>
        /// When a runner starts traveling, pre-load the destination scene
        /// and mark the departure node for unloading if now empty.
        /// </summary>
        private void OnRunnerStartedTravel(RunnerStartedTravel evt)
        {
            // Pre-load destination
            string destNodeId = evt.ToNodeId;
            string sceneName = GetSceneNameForNode(destNodeId);
            if (!string.IsNullOrEmpty(sceneName))
                EnsureNodeSceneLoaded(destNodeId);

            // Check if departure node is now empty — start unload timer
            string fromNodeId = evt.FromNodeId;
            if (_loadedScenes.TryGetValue(fromNodeId, out var entry)
                && entry.State == LoadState.Ready
                && entry.EmptySinceTime < 0f
                && !AnyRunnersAtNode(fromNodeId))
            {
                entry.EmptySinceTime = Time.time;
            }
        }

        /// <summary>
        /// When a runner arrives at a node, ensure the scene is ready
        /// and cancel any pending unload.
        /// </summary>
        private void OnRunnerArrivedAtNode(RunnerArrivedAtNode evt)
        {
            EnsureNodeSceneLoaded(evt.NodeId);
        }

        private void OnDestroy()
        {
            if (Sim?.Events != null)
            {
                Sim.Events.Unsubscribe<RunnerStartedTravel>(OnRunnerStartedTravel);
                Sim.Events.Unsubscribe<RunnerArrivedAtNode>(OnRunnerArrivedAtNode);
            }
        }
    }
}

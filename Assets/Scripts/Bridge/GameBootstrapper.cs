using UnityEngine;
using ProjectGuild.Simulation.Core;
using ProjectGuild.View;
using ProjectGuild.View.Runners;

namespace ProjectGuild.Bridge
{
    /// <summary>
    /// Entry point that wires up the simulation, visuals, and provides a simple
    /// debug UI for commanding runners. Attach to a GameObject in the scene along
    /// with SimulationRunner and VisualSyncSystem.
    ///
    /// For Phase 1, this handles: starting a new game, building the visual world,
    /// and providing buttons to send runners to different nodes.
    /// </summary>
    public class GameBootstrapper : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _simulationRunner;
        [SerializeField] private VisualSyncSystem _visualSyncSystem;
        [SerializeField] private CameraController _cameraController;

        private void Start()
        {
            if (_simulationRunner == null)
                _simulationRunner = GetComponent<SimulationRunner>();
            if (_visualSyncSystem == null)
                _visualSyncSystem = GetComponent<VisualSyncSystem>();
            if (_cameraController == null)
                _cameraController = FindAnyObjectByType<CameraController>();

            // Start a new game
            _simulationRunner.StartNewGame();

            // Build the visual world
            _visualSyncSystem.BuildWorld();

            // Point camera at first runner
            SelectRunner(0);
        }

        // ─── Runner Selection + Camera ───────────────────────────────

        private int _selectedRunnerIndex = 0;

        private void SelectRunner(int index)
        {
            var sim = _simulationRunner.Simulation;
            if (sim == null || index < 0 || index >= sim.State.Runners.Count) return;

            _selectedRunnerIndex = index;
            var runner = sim.State.Runners[index];
            var visual = _visualSyncSystem.GetRunnerVisual(runner.Id);

            if (_cameraController != null && visual != null)
            {
                _cameraController.SetTarget(visual);
            }
        }

        // ─── Temporary Debug UI ──────────────────────────────────────
        // Simple OnGUI buttons for testing. Will be replaced with proper UI Toolkit later.

        private void OnGUI()
        {
            var sim = _simulationRunner.Simulation;
            if (sim == null || sim.State.Runners.Count == 0) return;

            GUILayout.BeginArea(new Rect(10, 10, 280, Screen.height - 20));

            GUILayout.Label("<b>Project Guild — Phase 1 Debug</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Space(5);

            // Runner selector
            GUILayout.Label("Select Runner:");
            for (int i = 0; i < sim.State.Runners.Count; i++)
            {
                var runner = sim.State.Runners[i];
                string label = $"{runner.Name} [{runner.State}]";
                if (i == _selectedRunnerIndex)
                    label = $">> {label} <<";

                if (GUILayout.Button(label))
                    SelectRunner(i);
            }

            GUILayout.Space(10);

            var selected = sim.State.Runners[_selectedRunnerIndex];

            // Runner info
            GUILayout.Label($"<b>{selected.Name}</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label($"State: {selected.State}");
            GUILayout.Label($"Location: {selected.CurrentNodeId}");
            GUILayout.Label($"Athletics: {selected.GetSkill(SkillType.Athletics).Level}");

            if (selected.State == RunnerState.Traveling && selected.Travel != null)
            {
                GUILayout.Label($"Traveling to: {selected.Travel.ToNodeId}");
                GUILayout.Label($"Progress: {selected.Travel.Progress:P0}");
            }

            GUILayout.Space(10);

            // Travel commands — show all nodes the runner isn't currently at
            GUILayout.Label("Send to:");
            foreach (var node in sim.State.Map.Nodes)
            {
                if (node.Id == selected.CurrentNodeId) continue;
                if (selected.State == RunnerState.Traveling) continue;

                if (GUILayout.Button($"{node.Name} ({node.Id})"))
                {
                    sim.CommandTravel(selected.Id, node.Id);
                }
            }

            GUILayout.Space(10);
            GUILayout.Label($"Tick: {sim.State.TickCount}");
            GUILayout.Label($"Time: {sim.State.TotalTimeElapsed:F1}s");

            GUILayout.EndArea();
        }
    }
}

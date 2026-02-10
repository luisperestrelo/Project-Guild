using System;
using UnityEngine;
using ProjectGuild.Bridge;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;
using ProjectGuild.View.Runners;

namespace ProjectGuild.View
{
    /// <summary>
    /// Entry point that wires up the simulation, visuals, and provides a simple
    /// debug UI for commanding runners. Attach to a GameObject in the scene along
    /// with SimulationRunner and VisualSyncSystem.
    ///
    /// Handles: starting a new game, building the visual world,
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
            if (sim == null || index < 0 || index >= sim.CurrentGameState.Runners.Count) return;

            _selectedRunnerIndex = index;
            var runner = sim.CurrentGameState.Runners[index];
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
            if (sim == null || sim.CurrentGameState.Runners.Count == 0) return;

            // Scale UI for high-DPI / large resolutions
            float scale = Mathf.Max(1f, Screen.height / 720f);
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            float scaledWidth = 300f;
            float scaledHeight = Screen.height / scale - 20f;
            GUILayout.BeginArea(new Rect(10, 10, scaledWidth, scaledHeight));

            GUILayout.Label("<b>Project Guild — Phase 1 Debug</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16 });
            GUILayout.Space(5);

            // Runner selector
            GUILayout.Label("Select Runner:");
            for (int i = 0; i < sim.CurrentGameState.Runners.Count; i++)
            {
                var runner = sim.CurrentGameState.Runners[i];
                string label = $"{runner.Name} [{runner.State}]";
                if (i == _selectedRunnerIndex)
                    label = $">> {label} <<";

                if (GUILayout.Button(label))
                    SelectRunner(i);
            }

            GUILayout.Space(10);

            // Clamp index in case runners were added/removed since last selection
            if (_selectedRunnerIndex >= sim.CurrentGameState.Runners.Count)
                _selectedRunnerIndex = 0;

            var selected = sim.CurrentGameState.Runners[_selectedRunnerIndex];

            // Runner info
            GUILayout.Label($"<b>{selected.Name}</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label($"State: {selected.State}");
            GUILayout.Label($"Location: {selected.CurrentNodeId}");

            if (selected.State == RunnerState.Traveling && selected.Travel != null)
            {
                GUILayout.Label($"Traveling to: {selected.Travel.ToNodeId}");
                GUILayout.Label($"Progress: {selected.Travel.Progress:P0}");
            }

            // Gathering state
            if (selected.State == RunnerState.Gathering && selected.Gathering != null)
            {
                float progress = selected.Gathering.TicksRequired > 0
                    ? selected.Gathering.TickAccumulator / selected.Gathering.TicksRequired
                    : 0f;
                GUILayout.Label($"Gathering at: {selected.Gathering.NodeId}");
                GUILayout.Label($"Progress: {progress:P0}");
            }
            else if (selected.Gathering != null && selected.Gathering.SubState != GatheringSubState.Gathering)
            {
                GUILayout.Label($"Auto-return: {selected.Gathering.SubState}");
            }

            // Inventory
            GUILayout.Space(5);
            GUILayout.Label($"Inventory: {selected.Inventory.Slots.Count}/{selected.Inventory.MaxSlots}");
            if (selected.Inventory.Slots.Count > 0)
            {
                // Summarize items by type
                var counts = new System.Collections.Generic.Dictionary<string, int>();
                foreach (var slot in selected.Inventory.Slots)
                {
                    if (!counts.ContainsKey(slot.ItemId)) counts[slot.ItemId] = 0;
                    counts[slot.ItemId] += slot.Quantity;
                }
                foreach (var kvp in counts)
                {
                    var def = sim.ItemRegistry?.Get(kvp.Key);
                    string name = def != null ? def.Name : kvp.Key;
                    GUILayout.Label($"  {name}: {kvp.Value}");
                }
            }

            GUILayout.Space(10);

            // Stop gathering button — visible when in gather loop (gathering or auto-returning)
            if (selected.Gathering != null)
            {
                if (GUILayout.Button("Stop Gathering"))
                {
                    sim.CancelGathering(selected.Id);
                }
            }

            // Commands — context-sensitive based on where the runner is
            var currentNode = sim.CurrentGameState.Map.GetNode(selected.CurrentNodeId);

            // Gather button — only at gathering nodes, only when idle
            if (selected.State == RunnerState.Idle && currentNode != null)
            {
                var gatherConfig = sim.Config.GetGatherableConfig(currentNode.Type);
                if (gatherConfig != null)
                {
                    var itemDef = sim.ItemRegistry?.Get(gatherConfig.ProducedItemId);
                    string itemName = itemDef != null ? itemDef.Name : gatherConfig.ProducedItemId;
                    if (GUILayout.Button($"Gather ({itemName})"))
                    {
                        sim.CommandGather(selected.Id);
                    }
                }
            }

            // Travel commands — show all nodes the runner isn't currently at
            if (selected.State == RunnerState.Idle)
            {
                GUILayout.Label("Send to:");
                foreach (var node in sim.CurrentGameState.Map.Nodes)
                {
                    if (node.Id == selected.CurrentNodeId) continue;

                    if (GUILayout.Button($"{node.Name} ({node.Id})"))
                    {
                        sim.CommandTravel(selected.Id, node.Id);
                    }
                }
            }

            // Pawn generation
            GUILayout.Space(10);
            GUILayout.Label("<b>Generate Pawn</b>", new GUIStyle(GUI.skin.label) { richText = true });
            if (GUILayout.Button("Random Pawn"))
            {
                var rng = new System.Random();
                var runner = RunnerFactory.Create(rng, sim.Config, "hub");
                sim.AddRunner(runner);
            }
            if (GUILayout.Button("Tutorial Reward Pawn"))
            {
                var rng = new System.Random();
                var bias = new RunnerFactory.BiasConstraints
                {
                    PickOneSkillToBoostedAndPassionate = new[]
                    {
                        SkillType.Mining, SkillType.Woodcutting,
                        SkillType.Fishing, SkillType.Foraging,
                    },
                    WeakenedNoPassionSkills = new[]
                    {
                        SkillType.Melee, SkillType.Ranged,
                        SkillType.Magic, SkillType.Defence,
                    },
                };
                var runner = RunnerFactory.CreateBiased(rng, sim.Config, bias, "hub");
                sim.AddRunner(runner);
            }

            // Bank summary
            GUILayout.Space(10);
            GUILayout.Label("<b>Guild Bank</b>", new GUIStyle(GUI.skin.label) { richText = true });
            if (sim.CurrentGameState.Bank.Stacks.Count == 0)
            {
                GUILayout.Label("  (empty)");
            }
            else
            {
                foreach (var stack in sim.CurrentGameState.Bank.Stacks)
                {
                    var def = sim.ItemRegistry?.Get(stack.ItemId);
                    string name = def != null ? def.Name : stack.ItemId;
                    GUILayout.Label($"  {name}: {stack.Quantity}");
                }
            }

            GUILayout.Space(10);
            GUILayout.Label($"Tick: {sim.CurrentGameState.TickCount}");
            GUILayout.Label($"Time: {sim.CurrentGameState.TotalTimeElapsed:F1}s");

            GUILayout.EndArea();

            // ─── Right panel: selected runner's skills (always visible) ───
            float skillsPanelWidth = 260f;
            float skillsPanelX = Screen.width / scale - skillsPanelWidth - 10f;
            GUILayout.BeginArea(new Rect(skillsPanelX, 10, skillsPanelWidth, scaledHeight));

            var boldLabel = new GUIStyle(GUI.skin.label) { richText = true };

            GUILayout.Label($"<b>{selected.Name} — Skills</b>", boldLabel);
            for (int s = 0; s < SkillTypeExtensions.SkillCount; s++)
            {
                var skill = selected.Skills[s];
                string passionMarker = skill.HasPassion ? " <color=yellow>P</color>" : "";
                float effectiveLevel = selected.GetEffectiveLevel((SkillType)s, sim.Config);
                string effectiveStr = skill.HasPassion ? $" (eff: {effectiveLevel:F1})" : "";
                float skillProgress = skill.GetLevelProgress(sim.Config);
                string bar = ProgressBar(skillProgress, 8);
                float xpToNext = skill.GetXpToNextLevel(sim.Config);
                GUILayout.Label($"{(SkillType)s}: {skill.Level}{passionMarker}{effectiveStr} {bar} {skill.Xp:F0}/{xpToNext:F0}", boldLabel);
            }

            // ─── Live Stats ───
            GUILayout.Space(10);
            GUILayout.Label("<b>Live Stats</b>", boldLabel);

            // Travel speed
            float athleticsLevel = selected.GetEffectiveLevel(SkillType.Athletics, sim.Config);
            float travelSpeed = sim.Config.BaseTravelSpeed + (athleticsLevel - 1) * sim.Config.AthleticsSpeedPerLevel;
            GUILayout.Label($"Travel speed: {travelSpeed:F2} u/s (Athletics eff: {athleticsLevel:F1})");

            // Travel progress details
            if (selected.State == RunnerState.Traveling && selected.Travel != null)
            {
                float eta = travelSpeed > 0
                    ? (selected.Travel.TotalDistance - selected.Travel.DistanceCovered) / travelSpeed
                    : 0f;
                GUILayout.Label($"Distance: {selected.Travel.DistanceCovered:F1}/{selected.Travel.TotalDistance:F1}");
                GUILayout.Label($"ETA: {eta:F1}s");
                var athSkill = selected.Skills[(int)SkillType.Athletics];
                float actualAthXp = athSkill.HasPassion
                    ? sim.Config.AthleticsXpPerTick * sim.Config.PassionXpMultiplier
                    : sim.Config.AthleticsXpPerTick;
                string athPassion = athSkill.HasPassion ? " (P)" : "";
                GUILayout.Label($"Athletics XP/tick: {actualAthXp:F2}{athPassion}");
            }

            // Gathering stats
            if (selected.State == RunnerState.Gathering && selected.Gathering != null)
            {
                var node = sim.CurrentGameState.Map.GetNode(selected.Gathering.NodeId);
                var gatherConfig = node != null ? sim.Config.GetGatherableConfig(node.Type) : null;
                if (gatherConfig != null)
                {
                    float ticksReq = selected.Gathering.TicksRequired;
                    float itemsPerMin = ticksReq > 0 ? 60f * 10f / ticksReq : 0f;
                    GUILayout.Label($"Ticks/item: {ticksReq:F1} ({itemsPerMin:F1} items/min)");
                    var gatherSkill = selected.Skills[(int)gatherConfig.RequiredSkill];
                    float actualXpPerTick = gatherSkill.HasPassion
                        ? gatherConfig.XpPerTick * sim.Config.PassionXpMultiplier
                        : gatherConfig.XpPerTick;
                    string passionNote = gatherSkill.HasPassion ? " (P)" : "";
                    GUILayout.Label($"XP/tick: {actualXpPerTick:F2}{passionNote} ({actualXpPerTick * 10f:F1} XP/sec)");
                }
            }

            // Passion summary
            int passionCount = 0;
            string passionList = "";
            for (int s = 0; s < SkillTypeExtensions.SkillCount; s++)
            {
                if (selected.Skills[s].HasPassion)
                {
                    passionCount++;
                    if (passionList.Length > 0) passionList += ", ";
                    passionList += ((SkillType)s).ToString();
                }
            }
            GUILayout.Label($"Passions ({passionCount}): {(passionList.Length > 0 ? passionList : "none")}");

            GUILayout.EndArea();

            GUI.matrix = matrix;
        }

        private static string ProgressBar(float progress, int width)
        {
            int filled = Mathf.RoundToInt(progress * width);
            return "[" + new string('=', filled) + new string('-', width - filled) + "]";
        }
    }
}

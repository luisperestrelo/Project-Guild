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

        private Vector2 _runnerScrollPos;
        private Vector2 _zoneScrollPos;

        private void OnGUI()
        {
            var sim = _simulationRunner.Simulation;
            if (sim == null || sim.CurrentGameState.Runners.Count == 0) return;

            // Scale UI for high-DPI / large resolutions
            float scale = Mathf.Max(1f, Screen.height / 720f);
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            float screenW = Screen.width / scale;
            float screenH = Screen.height / scale;
            var richLabel = new GUIStyle(GUI.skin.label) { richText = true };

            // Clamp index in case runners were added/removed since last selection
            if (_selectedRunnerIndex >= sim.CurrentGameState.Runners.Count)
                _selectedRunnerIndex = 0;

            var selected = sim.CurrentGameState.Runners[_selectedRunnerIndex];

            // ─── Top-center: Runner selector (compact table) ───
            float topH = 140f;
            float topX = 10f;
            float topW = screenW - 280f - 30f; // leave room for right skills panel
            GUILayout.BeginArea(new Rect(topX, 10, topW, topH));

            GUILayout.Label("<b>Project Guild — Debug</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 });

            _runnerScrollPos = GUILayout.BeginScrollView(_runnerScrollPos, GUILayout.Height(topH - 30f));

            int runnerColumns = Mathf.Max(1, Mathf.FloorToInt(topW / 200f));
            int runnerCol = 0;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < sim.CurrentGameState.Runners.Count; i++)
            {
                var runner = sim.CurrentGameState.Runners[i];
                string stateTag = runner.State.ToString().Substring(0, Mathf.Min(4, runner.State.ToString().Length));
                string label = i == _selectedRunnerIndex
                    ? $">> {runner.Name} [{stateTag}]"
                    : $"{runner.Name} [{stateTag}]";

                if (GUILayout.Button(label, GUILayout.Width(190f), GUILayout.Height(20f)))
                    SelectRunner(i);

                runnerCol++;
                if (runnerCol >= runnerColumns)
                {
                    runnerCol = 0;
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // ─── Left panel: Runner info + Send to (zone list) ───
            float leftW = 220f;
            float leftTop = topH + 15f;
            float leftH = screenH - leftTop - 20f;
            GUILayout.BeginArea(new Rect(10, leftTop, leftW, leftH));

            // Runner info — always visible
            GUILayout.Label($"<b>{selected.Name}</b>", richLabel);
            GUILayout.Label($"State: {selected.State}  |  At: {selected.CurrentNodeId}");

            if (selected.State == RunnerState.Traveling && selected.Travel != null)
                GUILayout.Label($"Traveling to: {selected.Travel.ToNodeId} ({selected.Travel.Progress:P0})");

            if (selected.State == RunnerState.Gathering && selected.Gathering != null)
            {
                float progress = selected.Gathering.TicksRequired > 0
                    ? selected.Gathering.TickAccumulator / selected.Gathering.TicksRequired
                    : 0f;
                GUILayout.Label($"Gathering at: {selected.Gathering.NodeId} ({progress:P0})");
            }
            else if (selected.Gathering != null && selected.Gathering.SubState != GatheringSubState.Gathering)
            {
                GUILayout.Label($"Auto-return: {selected.Gathering.SubState}");
            }

            // Inventory
            GUILayout.Label($"Inventory: {selected.Inventory.Slots.Count}/{selected.Inventory.MaxSlots}");
            if (selected.Inventory.Slots.Count > 0)
            {
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

            GUILayout.Space(5);

            // Stop gathering button
            if (selected.Gathering != null)
            {
                if (GUILayout.Button("Stop Gathering"))
                    sim.CancelGathering(selected.Id);
            }

            // Gather buttons — context-sensitive
            var currentNode = sim.CurrentGameState.Map.GetNode(selected.CurrentNodeId);
            if (selected.State == RunnerState.Idle && currentNode != null && currentNode.Gatherables.Length > 0)
            {
                for (int g = 0; g < currentNode.Gatherables.Length; g++)
                {
                    var gatherConfig = currentNode.Gatherables[g];
                    var itemDef = sim.ItemRegistry?.Get(gatherConfig.ProducedItemId);
                    string itemName = itemDef != null ? itemDef.Name : gatherConfig.ProducedItemId;
                    string levelReq = gatherConfig.MinLevel > 0 ? $" [Lv{gatherConfig.MinLevel}+]" : "";
                    int gatherIndex = g;
                    if (GUILayout.Button($"[{g}] Gather ({itemName}{levelReq})"))
                        sim.CommandGather(selected.Id, gatherIndex);
                }
            }

            // Zone list — visible when idle OR traveling (traveling = redirect)
            if (selected.State == RunnerState.Idle || selected.State == RunnerState.Traveling)
            {
                GUILayout.Space(5);
                string label = selected.State == RunnerState.Traveling ? "<b>Redirect to:</b>" : "<b>Send to:</b>";
                GUILayout.Label(label, richLabel);
                _zoneScrollPos = GUILayout.BeginScrollView(_zoneScrollPos);
                foreach (var node in sim.CurrentGameState.Map.Nodes)
                {
                    // Idle: hide the node we're standing at. Traveling: hide current destination.
                    if (selected.State == RunnerState.Idle && node.Id == selected.CurrentNodeId) continue;
                    if (selected.State == RunnerState.Traveling && selected.Travel != null
                        && node.Id == selected.Travel.ToNodeId) continue;

                    if (GUILayout.Button(node.Name, GUILayout.Height(20f)))
                        sim.CommandTravel(selected.Id, node.Id);
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();

            // ─── Bottom-center: Pawn generation + Guild Bank ───
            float bottomH = 130f;
            float bottomY = screenH - bottomH - 10f;
            float bottomCenterX = leftW + 20f;
            float bottomCenterW = screenW - leftW - 280f - 40f;
            GUILayout.BeginArea(new Rect(bottomCenterX, bottomY, bottomCenterW, bottomH));

            GUILayout.BeginHorizontal();

            // Pawn generation (left half)
            GUILayout.BeginVertical(GUILayout.Width(bottomCenterW * 0.45f));
            GUILayout.Label("<b>Generate Pawn</b>", richLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Random"))
            {
                var rng = new System.Random();
                var runner = RunnerFactory.Create(rng, sim.Config, "hub");
                sim.AddRunner(runner);
            }
            if (GUILayout.Button("Tutorial"))
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
            GUILayout.EndHorizontal();
            GUILayout.Space(5);
            GUILayout.Label($"Tick: {sim.CurrentGameState.TickCount}  |  Time: {sim.CurrentGameState.TotalTimeElapsed:F1}s");
            GUILayout.EndVertical();

            GUILayout.Space(10);

            // Guild Bank (right half)
            GUILayout.BeginVertical(GUILayout.Width(bottomCenterW * 0.45f));
            GUILayout.Label("<b>Guild Bank</b>", richLabel);
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
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // ─── Right panel: selected runner's skills (always visible) ───
            float skillsPanelWidth = 260f;
            float skillsPanelX = screenW - skillsPanelWidth - 10f;
            GUILayout.BeginArea(new Rect(skillsPanelX, 10, skillsPanelWidth, screenH - 20f));

            GUILayout.Label($"<b>{selected.Name} — Skills</b>", richLabel);
            for (int s = 0; s < SkillTypeExtensions.SkillCount; s++)
            {
                var skill = selected.Skills[s];
                string passionMarker = skill.HasPassion ? " <color=yellow>P</color>" : "";
                float effectiveLevel = selected.GetEffectiveLevel((SkillType)s, sim.Config);
                string effectiveStr = skill.HasPassion ? $" (eff: {effectiveLevel:F1})" : "";
                float skillProgress = skill.GetLevelProgress(sim.Config);
                string bar = ProgressBar(skillProgress, 8);
                float xpToNext = skill.GetXpToNextLevel(sim.Config);
                GUILayout.Label($"{(SkillType)s}: {skill.Level}{passionMarker}{effectiveStr} {bar} {skill.Xp:F0}/{xpToNext:F0}", richLabel);
            }

            // ─── Live Stats ───
            GUILayout.Space(10);
            GUILayout.Label("<b>Live Stats</b>", richLabel);

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
                var gatherConfig = (node != null && selected.Gathering.GatherableIndex < node.Gatherables.Length)
                    ? node.Gatherables[selected.Gathering.GatherableIndex] : null;
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

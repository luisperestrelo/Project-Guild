using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ProjectGuild.Bridge;
using ProjectGuild.Simulation.Automation;
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
        [SerializeField] private UI.UIManager _uiManager;

        private InputAction _clickAction;
        private bool _clickedThisFrame;
        private bool _debugUIEnabled = true;

        private void Awake()
        {
            _clickAction = new InputAction("Click", InputActionType.Button,
                binding: "<Mouse>/leftButton");
        }

        private void OnEnable() => _clickAction.Enable();
        private void OnDisable() => _clickAction.Disable();

        private void Start()
        {
            if (_simulationRunner == null)
                _simulationRunner = GetComponent<SimulationRunner>();
            if (_visualSyncSystem == null)
                _visualSyncSystem = GetComponent<VisualSyncSystem>();
            if (_cameraController == null)
                _cameraController = FindAnyObjectByType<CameraController>();
            if (_uiManager == null)
                _uiManager = FindAnyObjectByType<UI.UIManager>();

            // Start a new game
            _simulationRunner.StartNewGame();

            // Build the visual world
            _visualSyncSystem.BuildWorld();

            // Point camera at first runner
            SelectRunner(0);

            // Initialize real UI (after sim + visuals are ready)
            if (_uiManager != null)
                _uiManager.Initialize();
        }

        // ─── Runner Selection + Camera ───────────────────────────────

        private int _selectedRunnerIndex = 0;

        private void SelectRunner(int index)
        {
            var sim = _simulationRunner.Simulation;
            if (sim == null || index < 0 || index >= sim.CurrentGameState.Runners.Count) return;

            _selectedRunnerIndex = index;
            var runner = sim.CurrentGameState.Runners[index];

            // Update real UI (portrait selection, details panel, camera)
            if (_uiManager != null)
            {
                _uiManager.SelectRunner(runner.Id);
            }
            else
            {
                // Fallback if UIManager not present
                var visual = _visualSyncSystem.GetRunnerVisual(runner.Id);
                if (_cameraController != null && visual != null)
                    _cameraController.SetTarget(visual);
            }
        }

        private void Update()
        {
            // F1 toggles debug UI
            if (Keyboard.current.f1Key.wasPressedThisFrame)
                _debugUIEnabled = !_debugUIEnabled;

            // Track click for this frame — consumed in OnGUI if it hit UI,
            // otherwise processed here for world-space runner picking.
            if (_clickAction.WasPressedThisFrame())
                _clickedThisFrame = true;
        }

        private void LateUpdate()
        {
            // Process click after OnGUI has had a chance to consume it.
            // GUIUtility.hotControl > 0 means the click hit a GUI element.
            if (_clickedThisFrame)
            {
                _clickedThisFrame = false;
                // Skip if click hit IMGUI
                if (GUIUtility.hotControl > 0) return;
                // Skip if click hit UI Toolkit (uses event-driven pointer tracking, not the
                // broken ScreenToPanel + panel.Pick() approach that had coordinate mismatch)
                if (_uiManager != null && _uiManager.IsPointerOverUI()) return;

                TryPickRunner();
            }
        }

        private bool IsPointerOverUIToolkit()
        {
            if (_uiManager == null) return false;
            var uiDoc = _uiManager.GetComponent<UnityEngine.UIElements.UIDocument>();
            if (uiDoc == null) return false;
            var panel = uiDoc.rootVisualElement?.panel;
            if (panel == null) return false;

            // Pick the topmost element at the mouse position
            var mousePos = Mouse.current.position.ReadValue();
            // Convert screen position to panel coordinates
            var panelPos = UnityEngine.UIElements.RuntimePanelUtils.ScreenToPanel(
                panel, new UnityEngine.Vector2(mousePos.x, mousePos.y));
            var picked = panel.Pick(panelPos);

            // If picked element exists and isn't set to Ignore, UI is blocking
            return picked != null && picked.pickingMode != UnityEngine.UIElements.PickingMode.Ignore;
        }

        private void TryPickRunner()
        {
            var sim = _simulationRunner.Simulation;
            if (sim == null || Camera.main == null) return;

            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return;

            var visual = hit.collider.GetComponentInParent<RunnerVisual>();
            if (visual == null) return;

            // Find the runner index by ID
            for (int i = 0; i < sim.CurrentGameState.Runners.Count; i++)
            {
                if (sim.CurrentGameState.Runners[i].Id == visual.RunnerId)
                {
                    SelectRunner(i);
                    return;
                }
            }
        }

        // ─── Temporary Debug UI ──────────────────────────────────────
        // Simple OnGUI buttons for testing. Will be replaced with proper UI Toolkit later.

        private Vector2 _runnerScrollPos;
        private Vector2 _zoneScrollPos;

        // Automation panel state
        private int _autoTab; // 0=Task Seq, 1=Macro Rules, 2=Micro Rules, 3=Decision Log, 4=Warnings, 5=Activity, 6=Event Log
        private int _decisionLogFilter; // 0=All, 1=Macro, 2=Micro
        private Vector2 _rulesScrollPos;
        private Vector2 _microRulesScrollPos;
        private Vector2 _logScrollPos;
        private Vector2 _warningsScrollPos;
        private Vector2 _activityScrollPos;
        private Vector2 _eventLogScrollPos;
        private bool[] _eventCategoryFilters = { true, true, true, true, false }; // Warning, Automation, State, Production, Lifecycle(off)
        private bool _eventLogAllRunners = true;
        private string _eventLogSearch = "";
        private bool _showAddRule;
        private bool _showAddMicroRule;
        private Ruleset _clipboardRuleset;

        // Add rule form
        private string _newRuleLabel = "";
        private int _newCondType;
        private int _newCondOp;
        private string _newCondValue = "0";
        private string _newCondStringParam = "";
        private int _newCondIntParam;
        private int _newActionType;
        private int _newActionNodeIndex;
        private bool _newRuleFinishTrip = true;

        // Add micro rule form
        private string _newMicroRuleLabel = "";
        private int _newMicroCondType;
        private int _newMicroCondOp;
        private string _newMicroCondValue = "0";
        private string _newMicroCondStringParam = "";
        private int _newMicroCondIntParam;
        private int _newMicroActionType; // 0=GatherHere, 1=FinishTask
        private string _newMicroGatherIndex = "0";

        // Templates

        private void OnGUI()
        {
            if (!_debugUIEnabled) return;

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
            else if (selected.State == RunnerState.Depositing && selected.Depositing != null)
            {
                GUILayout.Label($"Depositing... ({selected.Depositing.TicksRemaining} ticks left)");
            }

            if (selected.TaskSequence != null)
            {
                var seq = selected.TaskSequence;
                int stepIdx = selected.TaskSequenceCurrentStepIndex;
                var step = seq.Steps != null && stepIdx >= 0 && stepIdx < seq.Steps.Count ? seq.Steps[stepIdx] : null;
                string stepDesc = step != null ? $"{step.Type}" : "done";
                string loopTag = seq.Loop ? " [Loop]" : "";
                string suspendTag = selected.MacroSuspendedUntilLoop ? " [Macro paused]" : "";
                GUILayout.Label($"Task: {seq.Name ?? seq.TargetNodeId ?? "?"} (step {stepIdx}/{seq.Steps?.Count ?? 0}: {stepDesc}){loopTag}{suspendTag}");
            }

            // Inventory
            GUILayout.Label($"Inventory: {selected.Inventory.Slots.Count}/{selected.Inventory.MaxSlots}");
            if (selected.Inventory.Slots.Count > 0)
            {
                var counts = new Dictionary<string, int>();
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

            // Cancel task sequence button
            if (selected.TaskSequence != null)
            {
                if (GUILayout.Button("Cancel Task"))
                    sim.ClearTaskSequence(selected.Id);
            }

            // Work At buttons — creates task sequence + one-shot shield macro rule
            GUILayout.Space(5);
            GUILayout.Label("<b>Work At:</b>", richLabel);
            _zoneScrollPos = GUILayout.BeginScrollView(_zoneScrollPos);
            string hubId = sim.CurrentGameState.Map?.HubNodeId ?? "hub";
            foreach (var node in sim.CurrentGameState.Map.Nodes)
            {
                if (node.Id == hubId) continue; // skip hub
                string nodeId = node.Id;

                // Show what's available at this node
                var resources = new List<string>();
                foreach (var g in node.Gatherables)
                {
                    var itemDef = sim.ItemRegistry?.Get(g.ProducedItemId);
                    resources.Add(itemDef != null ? itemDef.Name : g.ProducedItemId);
                }
                string detail = resources.Count > 0 ? $" ({string.Join(", ", resources)})" : "";

                if (GUILayout.Button($"{node.Name}{detail}", GUILayout.Height(20f)))
                {
                    sim.CommandWorkAtSuspendMacrosForOneCycle(selected.Id, nodeId);
                }
            }
            GUILayout.EndScrollView();

            GUILayout.EndArea();

            // ─── Center panel: Automation (Rules / Decision Log) ───
            float centerX = leftW + 20f;
            float centerW = screenW - leftW - 280f - 40f;
            float centerTop = topH + 15f;
            float bottomH = 130f;
            float centerH = screenH - centerTop - bottomH - 25f;

            if (centerW > 200f) // only draw if there's reasonable space
            {
                GUILayout.BeginArea(new Rect(centerX, centerTop, centerW, centerH));
                DrawAutomationPanel(sim, selected, richLabel, centerW, centerH);
                GUILayout.EndArea();
            }

            // ─── Bottom-center: Pawn generation + Guild Bank ───
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

        // ─── Automation Panel ────────────────────────────────────────

        private void DrawAutomationPanel(GameSimulation sim, Runner selected, GUIStyle richLabel, float panelW, float panelH)
        {
            GUILayout.Label("<color=#888888>Automation</color>", richLabel);

            // Tab bar
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_autoTab == 0, "Task Seq", GUI.skin.button, GUILayout.Height(22f)))
                _autoTab = 0;
            if (GUILayout.Toggle(_autoTab == 1, "Macro Rules", GUI.skin.button, GUILayout.Height(22f)))
                _autoTab = 1;
            if (GUILayout.Toggle(_autoTab == 2, "Micro Rules", GUI.skin.button, GUILayout.Height(22f)))
                _autoTab = 2;
            if (GUILayout.Toggle(_autoTab == 3, "Decision Log", GUI.skin.button, GUILayout.Height(22f)))
                _autoTab = 3;
            if (GUILayout.Toggle(_autoTab == 4, "Warnings", GUI.skin.button, GUILayout.Height(22f)))
                _autoTab = 4;
            if (GUILayout.Toggle(_autoTab == 5, "Activity", GUI.skin.button, GUILayout.Height(22f)))
                _autoTab = 5;
            if (GUILayout.Toggle(_autoTab == 6, "Event Log", GUI.skin.button, GUILayout.Height(22f)))
                _autoTab = 6;
            GUILayout.FlexibleSpace();

            // Copy/Paste buttons (for whichever ruleset tab is active)
            if (_autoTab == 1 || _autoTab == 2)
            {
                if (GUILayout.Button("Copy", GUILayout.Width(50f), GUILayout.Height(22f)))
                {
                    var src = _autoTab == 1 ? selected.MacroRuleset : selected.MicroRuleset;
                    _clipboardRuleset = src?.DeepCopy();
                }
                GUI.enabled = _clipboardRuleset != null;
                if (GUILayout.Button("Paste", GUILayout.Width(50f), GUILayout.Height(22f)))
                {
                    if (_autoTab == 1)
                        selected.MacroRuleset = _clipboardRuleset.DeepCopy();
                    else
                        selected.MicroRuleset = _clipboardRuleset.DeepCopy();
                }
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(3);

            switch (_autoTab)
            {
                case 0: DrawTaskSequenceTab(sim, selected, richLabel); break;
                case 1: DrawRulesTab(sim, selected, richLabel, panelW); break;
                case 2: DrawMicroRulesTab(sim, selected, richLabel); break;
                case 3: DrawDecisionLogTab(sim, selected, richLabel); break;
                case 4: DrawWarningsTab(sim, richLabel); break;
                case 5: DrawActivityTab(sim, selected, richLabel); break;
                case 6: DrawEventLogTab(sim, selected, richLabel); break;
            }
        }

        // ─── Task Sequence Tab ───────────────────────────────────────

        private Vector2 _taskSeqScrollPos;

        private void DrawTaskSequenceTab(GameSimulation sim, Runner selected, GUIStyle richLabel)
        {
            GUILayout.Label($"<b>Task Sequence for {selected.Name}</b>", richLabel);

            var seq = selected.TaskSequence;
            if (seq == null)
            {
                GUILayout.Label("  (no active task sequence — runner is idle)");
            }
            else
            {
                GUILayout.Label($"Name: <b>{seq.Name ?? "(unnamed)"}</b>  |  Loop: {(seq.Loop ? "Yes" : "No")}  |  Target: {seq.TargetNodeId ?? "?"}", richLabel);

                _taskSeqScrollPos = GUILayout.BeginScrollView(_taskSeqScrollPos);
                if (seq.Steps != null)
                {
                    for (int i = 0; i < seq.Steps.Count; i++)
                    {
                        var step = seq.Steps[i];
                        int curIdx = selected.TaskSequenceCurrentStepIndex;
                        string prefix = i == curIdx ? "<color=#88ff88>>>> </color>" : "    ";
                        string stepDesc = step.Type switch
                        {
                            TaskStepType.TravelTo => $"TravelTo({step.TargetNodeId})",
                            TaskStepType.Work => "Work",
                            TaskStepType.Deposit => "Deposit",
                            _ => step.Type.ToString(),
                        };
                        GUILayout.Label($"{prefix}<color={(i == curIdx ? "#88ff88" : "#cccccc")}>[{i}] {stepDesc}</color>", richLabel);
                    }
                }
                GUILayout.EndScrollView();
            }

            GUILayout.Space(5);

            // Macro suspension indicator
            if (selected.MacroSuspendedUntilLoop)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=orange>Macro rules paused (1 cycle)</color>", richLabel);
                if (GUILayout.Button("Resume", GUILayout.Width(60f), GUILayout.Height(18f)))
                    sim.ResumeMacroRules(selected.Id);
                GUILayout.EndHorizontal();
            }

            // Pending task sequence
            if (selected.PendingTaskSequence != null)
            {
                GUILayout.Label($"<color=yellow>Pending: {selected.PendingTaskSequence.Name ?? selected.PendingTaskSequence.TargetNodeId ?? "?"}</color>", richLabel);
            }

            GUILayout.Space(5);

            // Cancel button
            if (seq != null)
            {
                if (GUILayout.Button("Clear Task Sequence", GUILayout.Height(22f)))
                {
                    sim.ClearTaskSequence(selected.Id);
                }
            }
        }

        // ─── Rules Tab ──────────────────────────────────────────────

        private void DrawRulesTab(GameSimulation sim, Runner selected, GUIStyle richLabel, float panelW)
        {
            var ruleset = selected.MacroRuleset;
            int ruleCount = ruleset?.Rules?.Count ?? 0;

            GUILayout.Label($"<b>Rules for {selected.Name}</b> ({ruleCount} rules)", richLabel);

            _rulesScrollPos = GUILayout.BeginScrollView(_rulesScrollPos);

            if (ruleset != null && ruleset.Rules != null)
            {
                int deleteIndex = -1;
                int moveUpIndex = -1;
                int moveDownIndex = -1;

                for (int i = 0; i < ruleset.Rules.Count; i++)
                {
                    var rule = ruleset.Rules[i];
                    GUILayout.BeginHorizontal();

                    // Enable toggle
                    rule.Enabled = GUILayout.Toggle(rule.Enabled, "", GUILayout.Width(18f));

                    // Rule summary
                    string condSummary = FormatConditions(rule.Conditions);
                    string actionSummary = FormatAction(rule.Action);
                    string tripLabel = rule.FinishCurrentSequence ? "<color=#88ff88>Finish Seq</color>" : "<color=#888888>Immediately</color>";
                    string enabledColor = rule.Enabled ? "white" : "#666666";
                    string label = rule.Label.Length > 0 ? $"\"{rule.Label}\"" : $"#{i}";
                    GUILayout.Label(
                        $"<color={enabledColor}>{label}: IF {condSummary} THEN {actionSummary}</color> [{tripLabel}]",
                        richLabel);

                    GUILayout.FlexibleSpace();

                    // Reorder / delete buttons
                    GUI.enabled = i > 0;
                    if (GUILayout.Button("^", GUILayout.Width(22f), GUILayout.Height(18f)))
                        moveUpIndex = i;
                    GUI.enabled = i < ruleset.Rules.Count - 1;
                    if (GUILayout.Button("v", GUILayout.Width(22f), GUILayout.Height(18f)))
                        moveDownIndex = i;
                    GUI.enabled = true;
                    if (GUILayout.Button("x", GUILayout.Width(22f), GUILayout.Height(18f)))
                        deleteIndex = i;

                    GUILayout.EndHorizontal();
                }

                // Apply deferred operations
                if (deleteIndex >= 0)
                    ruleset.Rules.RemoveAt(deleteIndex);
                if (moveUpIndex > 0)
                    (ruleset.Rules[moveUpIndex], ruleset.Rules[moveUpIndex - 1]) =
                        (ruleset.Rules[moveUpIndex - 1], ruleset.Rules[moveUpIndex]);
                if (moveDownIndex >= 0 && moveDownIndex < ruleset.Rules.Count - 1)
                    (ruleset.Rules[moveDownIndex], ruleset.Rules[moveDownIndex + 1]) =
                        (ruleset.Rules[moveDownIndex + 1], ruleset.Rules[moveDownIndex]);
            }
            else
            {
                GUILayout.Label("  (no ruleset)");
            }

            GUILayout.EndScrollView();

            GUILayout.Space(3);

            // ─── Add Rule / Templates ───
            _showAddRule = GUILayout.Toggle(_showAddRule, _showAddRule ? "Hide Add Rule" : "Add Rule...", GUI.skin.button);
            if (_showAddRule)
                DrawAddRuleForm(sim, selected);

            GUILayout.Space(3);
            DrawTemplateControls(sim, selected, richLabel);
        }

        // ─── Micro Rules Tab ────────────────────────────────────────

        private static readonly string[] MicroActionNames = { "GatherHere", "FinishTask" };

        private void DrawMicroRulesTab(GameSimulation sim, Runner selected, GUIStyle richLabel)
        {
            var ruleset = selected.MicroRuleset;
            int ruleCount = ruleset?.Rules?.Count ?? 0;

            GUILayout.Label($"<b>Micro Rules for {selected.Name}</b> ({ruleCount} rules)", richLabel);

            // Show gatherables at current node for context
            var node = sim.CurrentGameState.Map.GetNode(selected.CurrentNodeId);
            if (node != null && node.Gatherables.Length > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#888888>Node gatherables:</color>", richLabel);
                for (int g = 0; g < node.Gatherables.Length; g++)
                {
                    GUILayout.Label($"<color=#88ddff>[{g}] {node.Gatherables[g].ProducedItemId}</color>", richLabel);
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("<color=#888888>Runner is not at a gathering node</color>", richLabel);
            }

            GUILayout.Space(2);

            _microRulesScrollPos = GUILayout.BeginScrollView(_microRulesScrollPos);

            if (ruleset != null && ruleset.Rules != null)
            {
                int deleteIndex = -1;
                int moveUpIndex = -1;
                int moveDownIndex = -1;

                for (int i = 0; i < ruleset.Rules.Count; i++)
                {
                    var rule = ruleset.Rules[i];
                    GUILayout.BeginHorizontal();

                    // Enable toggle
                    rule.Enabled = GUILayout.Toggle(rule.Enabled, "", GUILayout.Width(18f));

                    // Rule summary
                    string condSummary = FormatConditions(rule.Conditions);
                    string actionSummary = FormatMicroAction(rule.Action, node);
                    string enabledColor = rule.Enabled ? "white" : "#666666";
                    string label = rule.Label.Length > 0 ? $"\"{rule.Label}\"" : $"#{i}";
                    GUILayout.Label(
                        $"<color={enabledColor}>{label}: IF {condSummary} THEN {actionSummary}</color>",
                        richLabel);

                    GUILayout.FlexibleSpace();

                    // Reorder / delete buttons
                    GUI.enabled = i > 0;
                    if (GUILayout.Button("^", GUILayout.Width(22f), GUILayout.Height(18f)))
                        moveUpIndex = i;
                    GUI.enabled = i < ruleset.Rules.Count - 1;
                    if (GUILayout.Button("v", GUILayout.Width(22f), GUILayout.Height(18f)))
                        moveDownIndex = i;
                    GUI.enabled = true;
                    if (GUILayout.Button("x", GUILayout.Width(22f), GUILayout.Height(18f)))
                        deleteIndex = i;

                    GUILayout.EndHorizontal();
                }

                // Apply deferred operations
                if (deleteIndex >= 0)
                    ruleset.Rules.RemoveAt(deleteIndex);
                if (moveUpIndex > 0)
                    (ruleset.Rules[moveUpIndex], ruleset.Rules[moveUpIndex - 1]) =
                        (ruleset.Rules[moveUpIndex - 1], ruleset.Rules[moveUpIndex]);
                if (moveDownIndex >= 0 && moveDownIndex < ruleset.Rules.Count - 1)
                    (ruleset.Rules[moveDownIndex], ruleset.Rules[moveDownIndex + 1]) =
                        (ruleset.Rules[moveDownIndex + 1], ruleset.Rules[moveDownIndex]);
            }
            else
            {
                GUILayout.Label("  (no ruleset)");
            }

            GUILayout.EndScrollView();

            GUILayout.Space(3);

            // ─── Add Micro Rule ───
            _showAddMicroRule = GUILayout.Toggle(_showAddMicroRule, _showAddMicroRule ? "Hide Add Rule" : "Add Rule...", GUI.skin.button);
            if (_showAddMicroRule)
                DrawAddMicroRuleForm(sim, selected, node);

            GUILayout.Space(3);

            // Reset to default
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Default", GUILayout.Width(100f)))
            {
                selected.MicroRuleset = DefaultRulesets.CreateDefaultMicro();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawAddMicroRuleForm(GameSimulation sim, Runner selected, WorldNode node)
        {
            GUILayout.BeginVertical(GUI.skin.box);

            // Label
            GUILayout.BeginHorizontal();
            GUILayout.Label("Label:", GUILayout.Width(45f));
            _newMicroRuleLabel = GUILayout.TextField(_newMicroRuleLabel, GUILayout.Width(150f));
            GUILayout.EndHorizontal();

            // Condition type (reuses same condition types as macro)
            GUILayout.BeginHorizontal();
            GUILayout.Label("IF:", GUILayout.Width(25f));
            if (GUILayout.Button(ConditionTypeNames[_newMicroCondType], GUILayout.Width(120f)))
                _newMicroCondType = (_newMicroCondType + 1) % ConditionTypeNames.Length;

            var condType = (ConditionType)_newMicroCondType;

            bool needsOperator = condType == ConditionType.InventorySlots
                || condType == ConditionType.InventoryContains
                || condType == ConditionType.BankContains
                || condType == ConditionType.SkillLevel
                || condType == ConditionType.SelfHP;
            bool needsStringParam = condType == ConditionType.InventoryContains
                || condType == ConditionType.BankContains
                || condType == ConditionType.AtNode;
            bool needsIntParam = condType == ConditionType.SkillLevel
                || condType == ConditionType.RunnerStateIs;

            if (needsStringParam)
            {
                if (condType == ConditionType.AtNode)
                {
                    var nodes = sim.CurrentGameState.Map.Nodes;
                    if (nodes.Count > 0)
                    {
                        if (_newMicroCondIntParam >= nodes.Count) _newMicroCondIntParam = 0;
                        if (GUILayout.Button(nodes[_newMicroCondIntParam].Name, GUILayout.Width(130f)))
                            _newMicroCondIntParam = (_newMicroCondIntParam + 1) % nodes.Count;
                        _newMicroCondStringParam = nodes[_newMicroCondIntParam].Id;
                    }
                }
                else
                {
                    _newMicroCondStringParam = GUILayout.TextField(_newMicroCondStringParam, GUILayout.Width(80f));
                }
            }

            if (needsIntParam)
            {
                if (condType == ConditionType.SkillLevel)
                {
                    if (GUILayout.Button(SkillNames[_newMicroCondIntParam], GUILayout.Width(90f)))
                        _newMicroCondIntParam = (_newMicroCondIntParam + 1) % SkillNames.Length;
                }
                else if (condType == ConditionType.RunnerStateIs)
                {
                    string[] stateNames = Enum.GetNames(typeof(RunnerState));
                    int stateIdx = Mathf.Clamp(_newMicroCondIntParam, 0, stateNames.Length - 1);
                    if (GUILayout.Button(stateNames[stateIdx], GUILayout.Width(80f)))
                        _newMicroCondIntParam = (_newMicroCondIntParam + 1) % stateNames.Length;
                }
            }

            if (needsOperator)
            {
                if (GUILayout.Button(OperatorNames[_newMicroCondOp], GUILayout.Width(30f)))
                    _newMicroCondOp = (_newMicroCondOp + 1) % OperatorNames.Length;
                _newMicroCondValue = GUILayout.TextField(_newMicroCondValue, GUILayout.Width(40f));
            }

            GUILayout.EndHorizontal();

            // Action type — micro only: GatherHere or FinishTask
            GUILayout.BeginHorizontal();
            GUILayout.Label("THEN:", GUILayout.Width(40f));
            if (GUILayout.Button(MicroActionNames[_newMicroActionType], GUILayout.Width(100f)))
                _newMicroActionType = (_newMicroActionType + 1) % MicroActionNames.Length;

            if (_newMicroActionType == 0) // GatherHere
            {
                // Show gatherable selector if at a gathering node
                if (node != null && node.Gatherables.Length > 0)
                {
                    int.TryParse(_newMicroGatherIndex, out int gIdx);
                    gIdx = Mathf.Clamp(gIdx, 0, node.Gatherables.Length - 1);
                    string gatherName = $"[{gIdx}] {node.Gatherables[gIdx].ProducedItemId}";
                    if (GUILayout.Button(gatherName, GUILayout.Width(150f)))
                    {
                        gIdx = (gIdx + 1) % node.Gatherables.Length;
                        _newMicroGatherIndex = gIdx.ToString();
                    }
                }
                else
                {
                    GUILayout.Label("idx:", GUILayout.Width(25f));
                    _newMicroGatherIndex = GUILayout.TextField(_newMicroGatherIndex, GUILayout.Width(25f));
                }
            }

            GUILayout.EndHorizontal();

            // Create button
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Create Rule", GUILayout.Width(90f)))
            {
                CreateMicroRuleFromForm(selected);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void CreateMicroRuleFromForm(Runner selected)
        {
            if (selected.MicroRuleset == null)
                selected.MicroRuleset = new Ruleset();

            // Build condition
            var condType = (ConditionType)_newMicroCondType;
            var condition = new Condition { Type = condType };

            float.TryParse(_newMicroCondValue, out float numVal);
            condition.NumericValue = numVal;
            condition.Operator = (ComparisonOperator)_newMicroCondOp;
            condition.StringParam = _newMicroCondStringParam;
            condition.IntParam = _newMicroCondIntParam;

            // Build action
            AutomationAction action;
            if (_newMicroActionType == 0)
            {
                int.TryParse(_newMicroGatherIndex, out int gatherIdx);
                action = AutomationAction.GatherHere(gatherIdx);
            }
            else
            {
                action = AutomationAction.FinishTask();
            }

            // Build rule
            var rule = new Rule
            {
                Label = _newMicroRuleLabel.Length > 0 ? _newMicroRuleLabel : "",
                Action = action,
                FinishCurrentSequence = false, // not applicable to micro
                Enabled = true,
            };

            if (condType != ConditionType.Always)
                rule.Conditions.Add(condition);

            selected.MicroRuleset.Rules.Add(rule);

            // Reset form
            _newMicroRuleLabel = "";
            _showAddMicroRule = false;
        }

        private static string FormatMicroAction(AutomationAction action, WorldNode node)
        {
            if (action.Type == ActionType.FinishTask)
                return "<color=#ffaa44>FinishTask</color>";

            if (action.Type == ActionType.GatherHere)
            {
                int idx = action.IntParam;
                string itemName = "";
                if (node != null && idx >= 0 && idx < node.Gatherables.Length)
                    itemName = $" ({node.Gatherables[idx].ProducedItemId})";
                return $"<color=#88ddff>GatherHere[{idx}]{itemName}</color>";
            }

            return $"<color=#ff8888>{action.Type}</color>"; // unexpected action type in micro
        }

        // ─── Add Macro Rule Form ────────────────────────────────────

        private static readonly string[] ConditionTypeNames = Enum.GetNames(typeof(ConditionType));
        private static readonly string[] MacroActionNames = { "AssignSequence", "Idle" };
        private static readonly ActionType[] MacroActionTypes = { ActionType.AssignSequence, ActionType.Idle };
        private static readonly string[] OperatorNames = { ">", ">=", "<", "<=", "==", "!=" };
        private static readonly string[] SkillNames = Enum.GetNames(typeof(SkillType));

        private void DrawAddRuleForm(GameSimulation sim, Runner selected)
        {
            GUILayout.BeginVertical(GUI.skin.box);

            // Label
            GUILayout.BeginHorizontal();
            GUILayout.Label("Label:", GUILayout.Width(45f));
            _newRuleLabel = GUILayout.TextField(_newRuleLabel, GUILayout.Width(150f));
            GUILayout.EndHorizontal();

            // Condition type (click to cycle)
            GUILayout.BeginHorizontal();
            GUILayout.Label("IF:", GUILayout.Width(25f));
            if (GUILayout.Button(ConditionTypeNames[_newCondType], GUILayout.Width(120f)))
                _newCondType = (_newCondType + 1) % ConditionTypeNames.Length;

            var condType = (ConditionType)_newCondType;

            // Show relevant fields based on condition type
            bool needsOperator = condType == ConditionType.InventorySlots
                || condType == ConditionType.InventoryContains
                || condType == ConditionType.BankContains
                || condType == ConditionType.SkillLevel
                || condType == ConditionType.SelfHP;
            bool needsStringParam = condType == ConditionType.InventoryContains
                || condType == ConditionType.BankContains
                || condType == ConditionType.AtNode;
            bool needsIntParam = condType == ConditionType.SkillLevel
                || condType == ConditionType.RunnerStateIs;

            if (needsStringParam)
            {
                // Show node/item selector based on context
                if (condType == ConditionType.AtNode)
                {
                    var nodes = sim.CurrentGameState.Map.Nodes;
                    if (nodes.Count > 0)
                    {
                        if (_newActionNodeIndex >= nodes.Count) _newActionNodeIndex = 0;
                        if (GUILayout.Button(nodes[_newActionNodeIndex].Name, GUILayout.Width(130f)))
                            _newActionNodeIndex = (_newActionNodeIndex + 1) % nodes.Count;
                        _newCondStringParam = nodes[_newActionNodeIndex].Id;
                    }
                }
                else
                {
                    _newCondStringParam = GUILayout.TextField(_newCondStringParam, GUILayout.Width(80f));
                }
            }

            if (needsIntParam)
            {
                if (condType == ConditionType.SkillLevel)
                {
                    if (GUILayout.Button(SkillNames[_newCondIntParam], GUILayout.Width(90f)))
                        _newCondIntParam = (_newCondIntParam + 1) % SkillNames.Length;
                }
                else if (condType == ConditionType.RunnerStateIs)
                {
                    string[] stateNames = Enum.GetNames(typeof(RunnerState));
                    int stateIdx = Mathf.Clamp(_newCondIntParam, 0, stateNames.Length - 1);
                    if (GUILayout.Button(stateNames[stateIdx], GUILayout.Width(80f)))
                        _newCondIntParam = (_newCondIntParam + 1) % stateNames.Length;
                }
            }

            if (needsOperator)
            {
                if (GUILayout.Button(OperatorNames[_newCondOp], GUILayout.Width(30f)))
                    _newCondOp = (_newCondOp + 1) % OperatorNames.Length;
                _newCondValue = GUILayout.TextField(_newCondValue, GUILayout.Width(40f));
            }

            GUILayout.EndHorizontal();

            // Action type — macro only: WorkAt, ReturnToHub, Idle
            GUILayout.BeginHorizontal();
            GUILayout.Label("THEN:", GUILayout.Width(40f));
            if (GUILayout.Button(MacroActionNames[_newActionType], GUILayout.Width(130f)))
                _newActionType = (_newActionType + 1) % MacroActionNames.Length;

            var actionType = MacroActionTypes[_newActionType];
            bool actionNeedsSequence = actionType == ActionType.AssignSequence;

            if (actionNeedsSequence)
            {
                var seqs = sim.CurrentGameState.TaskSequenceLibrary;
                if (seqs.Count > 0)
                {
                    if (_newActionNodeIndex >= seqs.Count) _newActionNodeIndex = 0;
                    if (GUILayout.Button(seqs[_newActionNodeIndex].Name ?? seqs[_newActionNodeIndex].Id, GUILayout.Width(130f)))
                        _newActionNodeIndex = (_newActionNodeIndex + 1) % seqs.Count;
                }
            }

            GUILayout.EndHorizontal();

            // FinishCurrentSequence + Create button
            GUILayout.BeginHorizontal();
            _newRuleFinishTrip = GUILayout.Toggle(_newRuleFinishTrip, "Finish Current Sequence", GUILayout.Width(170f));

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Create Rule", GUILayout.Width(90f)))
            {
                CreateRuleFromForm(sim, selected);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void CreateRuleFromForm(GameSimulation sim, Runner selected)
        {
            if (selected.MacroRuleset == null)
                selected.MacroRuleset = new Ruleset();

            // Build condition
            var condType = (ConditionType)_newCondType;
            var condition = new Condition { Type = condType };

            float.TryParse(_newCondValue, out float numVal);
            condition.NumericValue = numVal;
            condition.Operator = (ComparisonOperator)_newCondOp;
            condition.StringParam = _newCondStringParam;
            condition.IntParam = _newCondIntParam;

            // Build action
            var actionType = MacroActionTypes[_newActionType];
            AutomationAction action;

            if (actionType == ActionType.AssignSequence)
            {
                var seqs = sim.CurrentGameState.TaskSequenceLibrary;
                string seqId = (seqs.Count > 0 && _newActionNodeIndex < seqs.Count)
                    ? seqs[_newActionNodeIndex].Id : "";
                action = AutomationAction.AssignSequence(seqId);
            }
            else
            {
                action = new AutomationAction { Type = actionType };
            }

            // Build rule
            var rule = new Rule
            {
                Label = _newRuleLabel.Length > 0 ? _newRuleLabel : "",
                Action = action,
                FinishCurrentSequence = _newRuleFinishTrip,
                Enabled = true,
            };

            // Only add condition if it's not Always with no conditions (empty = always true)
            if (condType != ConditionType.Always)
                rule.Conditions.Add(condition);

            selected.MacroRuleset.Rules.Add(rule);

            // Reset form
            _newRuleLabel = "";
            _showAddRule = false;
        }

        // ─── Template Controls ──────────────────────────────────────

        private void DrawTemplateControls(GameSimulation sim, Runner selected, GUIStyle richLabel)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear Macro Ruleset", GUILayout.Width(160f)))
            {
                selected.MacroRuleset = null;
                selected.MacroRulesetId = null;
            }
            GUILayout.EndHorizontal();
        }

        // ─── Decision Log Tab ───────────────────────────────────────

        private void DrawDecisionLogTab(GameSimulation sim, Runner selected, GUIStyle richLabel)
        {
            GUILayout.Label($"<b>Decision Log for {selected.Name}</b>", richLabel);

            // Layer filter
            GUILayout.BeginHorizontal();
            string[] filterNames = { "All", "Macro", "Micro" };
            for (int i = 0; i < filterNames.Length; i++)
            {
                if (GUILayout.Toggle(_decisionLogFilter == i, filterNames[i], GUI.skin.button, GUILayout.Height(18f)))
                    _decisionLogFilter = i;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear Log", GUILayout.Width(70f), GUILayout.Height(18f)))
                sim.CurrentGameState.DecisionLog.Clear();
            GUILayout.EndHorizontal();

            DecisionLayer? layerFilter = _decisionLogFilter switch
            {
                1 => DecisionLayer.Macro,
                2 => DecisionLayer.Micro,
                _ => null,
            };

            var entries = sim.CurrentGameState.DecisionLog.GetForRunner(selected.Id, layerFilter);

            if (entries.Count == 0)
            {
                GUILayout.Label("  (no decisions logged yet)");
                return;
            }

            _logScrollPos = GUILayout.BeginScrollView(_logScrollPos);

            foreach (var entry in entries)
            {
                string deferred = entry.WasDeferred ? " <color=yellow>[deferred]</color>" : "";
                string layerTag = entry.Layer == DecisionLayer.Macro
                    ? "<color=#88ff88>[MACRO]</color>"
                    : "<color=#88ddff>[MICRO]</color>";
                GUILayout.Label(
                    $"<color=#aaaaaa>T{entry.TickNumber}</color> {layerTag} [{entry.TriggerReason}] " +
                    $"<b>{entry.RuleLabel}</b> -> {entry.ActionDetail}{deferred}",
                    richLabel);
                GUILayout.Label(
                    $"  <color=#888888>{entry.ConditionSnapshot}</color>",
                    richLabel);
            }

            GUILayout.EndScrollView();
        }

        // ─── Warnings Tab ─────────────────────────────────────────────

        private void DrawWarningsTab(GameSimulation sim, GUIStyle richLabel)
        {
            var warnings = sim.EventLog.GetWarnings();
            GUILayout.Label($"<b>Warnings</b> ({warnings.Count} total)", richLabel);

            if (warnings.Count == 0)
            {
                GUILayout.Label("  (no warnings)");
                return;
            }

            _warningsScrollPos = GUILayout.BeginScrollView(_warningsScrollPos);
            foreach (var entry in warnings)
            {
                string repeat = entry.RepeatCount > 1 ? $" <color=#888888>x{entry.RepeatCount}</color>" : "";
                string runnerId = entry.RunnerId ?? "global";
                GUILayout.Label(
                    $"<color=#aaaaaa>T{entry.TickNumber}</color> " +
                    $"<color=#ffaa44>[{runnerId}]</color> " +
                    $"<color=#ff8888>{entry.Summary}</color>{repeat}",
                    richLabel);
            }
            GUILayout.EndScrollView();

            if (GUILayout.Button("Clear Warnings", GUILayout.Height(18f)))
            {
                // Can't selectively clear, so we just note it's a full clear
                sim.EventLog.Clear();
            }
        }

        // ─── Activity Tab ─────────────────────────────────────────────

        private void DrawActivityTab(GameSimulation sim, Runner selected, GUIStyle richLabel)
        {
            var feed = sim.EventLog.GetActivityFeed(selected.Id);
            GUILayout.Label($"<b>Activity: {selected.Name}</b> ({feed.Count} entries)", richLabel);

            if (feed.Count == 0)
            {
                GUILayout.Label("  (no activity)");
                return;
            }

            _activityScrollPos = GUILayout.BeginScrollView(_activityScrollPos);
            foreach (var entry in feed)
            {
                string repeat = entry.RepeatCount > 1 ? $" x{entry.RepeatCount}" : "";
                string color = entry.Category switch
                {
                    EventCategory.Warning => "#ff8888",
                    EventCategory.Automation => "#88ff88",
                    EventCategory.Production => "#88ddff",
                    _ => "#cccccc",
                };
                GUILayout.Label(
                    $"<color=#aaaaaa>T{entry.TickNumber}</color> " +
                    $"<color={color}>{entry.Summary}{repeat}</color>",
                    richLabel);
            }
            GUILayout.EndScrollView();
        }

        // ─── Event Log Tab ────────────────────────────────────────────

        private static readonly string[] CategoryNames = { "Warning", "Automation", "State", "Production", "Lifecycle" };
        private static readonly EventCategory[] CategoryValues =
        {
            EventCategory.Warning, EventCategory.Automation, EventCategory.StateChange,
            EventCategory.Production, EventCategory.Lifecycle,
        };

        private void DrawEventLogTab(GameSimulation sim, Runner selected, GUIStyle richLabel)
        {
            // Search bar
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(50f));
            _eventLogSearch = GUILayout.TextField(_eventLogSearch, GUILayout.Height(18f));
            GUILayout.EndHorizontal();

            // Category toggles
            GUILayout.BeginHorizontal();
            for (int i = 0; i < CategoryNames.Length; i++)
            {
                _eventCategoryFilters[i] = GUILayout.Toggle(
                    _eventCategoryFilters[i], CategoryNames[i],
                    GUI.skin.button, GUILayout.Height(18f));
            }
            GUILayout.EndHorizontal();

            // Runner filter + options
            GUILayout.BeginHorizontal();
            _eventLogAllRunners = GUILayout.Toggle(_eventLogAllRunners, "All Runners", GUI.skin.button, GUILayout.Height(18f));
            if (!_eventLogAllRunners)
                GUILayout.Label($"  Filtered: {selected.Name}", richLabel);
            GUILayout.FlexibleSpace();
            sim.EventLog.CollapsingEnabled = GUILayout.Toggle(
                sim.EventLog.CollapsingEnabled, "Collapse", GUI.skin.button, GUILayout.Height(18f));
            if (GUILayout.Button("Clear", GUILayout.Width(50f), GUILayout.Height(18f)))
                sim.EventLog.Clear();
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // Entries
            _eventLogScrollPos = GUILayout.BeginScrollView(_eventLogScrollPos);
            var entries = sim.EventLog.Entries;
            string searchLower = string.IsNullOrEmpty(_eventLogSearch) ? null : _eventLogSearch.ToLowerInvariant();

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];

                // Category filter
                int catIdx = System.Array.IndexOf(CategoryValues, entry.Category);
                if (catIdx >= 0 && !_eventCategoryFilters[catIdx]) continue;

                // Runner filter
                if (!_eventLogAllRunners && entry.RunnerId != selected.Id) continue;

                // Search filter
                if (searchLower != null && !entry.Summary.ToLowerInvariant().Contains(searchLower)) continue;

                string repeat = entry.RepeatCount > 1 ? $" <color=#888888>x{entry.RepeatCount}</color>" : "";
                string runnerId = entry.RunnerId != null ? $"[{entry.RunnerId}] " : "";
                string color = entry.Category switch
                {
                    EventCategory.Warning => "#ff8888",
                    EventCategory.Automation => "#88ff88",
                    EventCategory.Production => "#88ddff",
                    EventCategory.Lifecycle => "#666666",
                    _ => "#cccccc",
                };
                GUILayout.Label(
                    $"<color=#aaaaaa>T{entry.TickNumber}</color> {runnerId}" +
                    $"<color={color}>{entry.Summary}</color>{repeat}",
                    richLabel);
            }
            GUILayout.EndScrollView();
        }

        // ─── Formatting Helpers ─────────────────────────────────────

        private static string FormatConditions(List<Condition> conditions)
        {
            if (conditions == null || conditions.Count == 0)
                return "Always";

            var parts = new List<string>();
            foreach (var c in conditions)
                parts.Add(FormatCondition(c));
            return string.Join(" AND ", parts);
        }

        private static string FormatCondition(Condition c)
        {
            return c.Type switch
            {
                ConditionType.Always => "Always",
                ConditionType.InventoryFull => "Inv Full",
                ConditionType.InventorySlots => $"FreeSlots {FormatOp(c.Operator)} {c.NumericValue}",
                ConditionType.InventoryContains => $"Inv({c.StringParam}) {FormatOp(c.Operator)} {c.NumericValue}",
                ConditionType.BankContains => $"Bank({c.StringParam}) {FormatOp(c.Operator)} {c.NumericValue}",
                ConditionType.SkillLevel => $"{(SkillType)c.IntParam} {FormatOp(c.Operator)} {c.NumericValue}",
                ConditionType.RunnerStateIs => $"State == {(RunnerState)c.IntParam}",
                ConditionType.AtNode => $"At {c.StringParam}",
                ConditionType.SelfHP => $"HP {FormatOp(c.Operator)} {c.NumericValue}%",
                _ => c.Type.ToString(),
            };
        }

        private static string FormatOp(ComparisonOperator op) => op switch
        {
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.GreaterOrEqual => ">=",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.LessOrEqual => "<=",
            ComparisonOperator.Equal => "==",
            ComparisonOperator.NotEqual => "!=",
            _ => "?",
        };

        private static string FormatAction(AutomationAction a)
        {
            return a.Type switch
            {
                ActionType.Idle => "Idle",
                ActionType.AssignSequence => $"Assign: {a.StringParam}",
                ActionType.GatherHere => a.IntParam == -1 ? "Gather Any" : $"Gather Here[{a.IntParam}]",
                ActionType.FinishTask => "FinishTask",
                _ => a.Type.ToString(),
            };
        }

        private static string ProgressBar(float progress, int width)
        {
            int filled = Mathf.RoundToInt(progress * width);
            return "[" + new string('=', filled) + new string('-', width - filled) + "]";
        }
    }
}

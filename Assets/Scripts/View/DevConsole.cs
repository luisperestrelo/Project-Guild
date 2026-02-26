#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ProjectGuild.Bridge;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;

namespace ProjectGuild.View
{
    /// <summary>
    /// Development console for spawning runners, inspecting state, fast-forwarding
    /// ticks, and debugging automation. Tilde or backslash key to open/close.
    ///
    /// Built entirely in code (no UXML) since this is dev-only tooling.
    /// Excluded from release builds via UNITY_EDITOR || DEVELOPMENT_BUILD.
    /// </summary>
    public class DevConsole : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _simulationRunner;
        [SerializeField] private UIDocument _uiDocument;

        private UI.UIManager _uiManager;

        private VisualElement _consoleRoot;
        private ScrollView _outputScroll;
        private Label _outputLabel;
        private TextField _inputField;
        private Label _hudLabel;

        private bool _isOpen;
        private bool _hudVisible = true;
        private bool _eventLogLive;
        private int _lastSeenEventLogCount;

        // Drag state
        private VisualElement _titleBar;
        private bool _isDragging;
        private Vector2 _dragOffset;

        private readonly StringBuilder _outputBuffer = new();
        private const int MaxOutputLines = 500;
        private int _outputLineCount;

        // ─── Lifecycle ─────────────────────────────────────────────

        private void Start()
        {
            if (_simulationRunner == null)
                _simulationRunner = FindAnyObjectByType<SimulationRunner>();
            if (_uiDocument == null)
                _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null)
            {
                Debug.LogError("[DevConsole] No UIDocument found on this GameObject. Add a UIDocument component to the DevConsole GameObject.");
                return;
            }

            _uiManager = FindAnyObjectByType<UI.UIManager>();

            BuildUI();
            SetConsoleVisible(false);

            if (_simulationRunner?.Simulation != null)
            {
                _simulationRunner.Simulation.Events.Subscribe<SimulationTickCompleted>(OnTick);
            }
        }

        private void Update()
        {
            if (Keyboard.current.backquoteKey.wasPressedThisFrame
                || Keyboard.current.backslashKey.wasPressedThisFrame)
            {
                if (_isOpen)
                    CloseConsole();
                else
                    OpenConsole();
            }
        }

        private void OnDestroy()
        {
            if (_simulationRunner?.Simulation != null)
            {
                _simulationRunner.Simulation.Events.Unsubscribe<SimulationTickCompleted>(OnTick);
            }
        }

        // ─── UI Construction ───────────────────────────────────────

        private void BuildUI()
        {
            var root = _uiDocument.rootVisualElement;

            // ─ HUD (persistent tick/time counter, top-right) ─
            _hudLabel = new Label("Tick 0 | 0:00");
            _hudLabel.style.position = Position.Absolute;
            _hudLabel.style.right = 8;
            _hudLabel.style.top = 8;
            _hudLabel.style.fontSize = 12;
            _hudLabel.style.color = new Color(0.8f, 0.8f, 0.8f, 0.9f);
            _hudLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _hudLabel.style.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.7f);
            _hudLabel.style.paddingTop = 3;
            _hudLabel.style.paddingBottom = 3;
            _hudLabel.style.paddingLeft = 8;
            _hudLabel.style.paddingRight = 8;
            _hudLabel.style.borderTopLeftRadius = 3;
            _hudLabel.style.borderTopRightRadius = 3;
            _hudLabel.style.borderBottomLeftRadius = 3;
            _hudLabel.style.borderBottomRightRadius = 3;
            _hudLabel.pickingMode = PickingMode.Ignore;
            root.Add(_hudLabel);

            // ─ Console panel (left-center overlay, draggable) ─
            _consoleRoot = new VisualElement();
            _consoleRoot.style.position = Position.Absolute;
            _consoleRoot.style.left = 20;
            _consoleRoot.style.top = new Length(25, LengthUnit.Percent);
            _consoleRoot.style.width = 700;
            _consoleRoot.style.height = 450;
            _consoleRoot.style.backgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
            _consoleRoot.style.borderTopWidth = 1;
            _consoleRoot.style.borderBottomWidth = 1;
            _consoleRoot.style.borderLeftWidth = 1;
            _consoleRoot.style.borderRightWidth = 1;
            _consoleRoot.style.borderTopColor = new Color(0.4f, 0.35f, 0.2f);
            _consoleRoot.style.borderBottomColor = new Color(0.4f, 0.35f, 0.2f);
            _consoleRoot.style.borderLeftColor = new Color(0.4f, 0.35f, 0.2f);
            _consoleRoot.style.borderRightColor = new Color(0.4f, 0.35f, 0.2f);
            _consoleRoot.style.borderTopLeftRadius = 4;
            _consoleRoot.style.borderTopRightRadius = 4;
            _consoleRoot.style.borderBottomLeftRadius = 4;
            _consoleRoot.style.borderBottomRightRadius = 4;
            _consoleRoot.style.paddingTop = 8;
            _consoleRoot.style.paddingBottom = 8;
            _consoleRoot.style.paddingLeft = 10;
            _consoleRoot.style.paddingRight = 10;
            _consoleRoot.style.flexDirection = FlexDirection.Column;

            // Title bar (draggable)
            _titleBar = new VisualElement();
            _titleBar.style.flexDirection = FlexDirection.Row;
            _titleBar.style.marginBottom = 6;
            _titleBar.style.cursor = new UnityEngine.UIElements.Cursor();
            _titleBar.style.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 0.9f);
            _titleBar.style.paddingTop = 4;
            _titleBar.style.paddingBottom = 4;
            _titleBar.style.paddingLeft = 4;
            _titleBar.style.paddingRight = 4;
            _titleBar.style.borderTopLeftRadius = 3;
            _titleBar.style.borderTopRightRadius = 3;

            var titleLabel = new Label("Dev Console");
            titleLabel.style.fontSize = 14;
            titleLabel.style.color = new Color(0.86f, 0.71f, 0.24f);
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.flexGrow = 1;
            titleLabel.pickingMode = PickingMode.Ignore;
            _titleBar.Add(titleLabel);

            var dragHint = new Label("drag to move");
            dragHint.style.fontSize = 10;
            dragHint.style.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            dragHint.style.unityTextAlign = TextAnchor.MiddleRight;
            dragHint.pickingMode = PickingMode.Ignore;
            _titleBar.Add(dragHint);

            _titleBar.RegisterCallback<PointerDownEvent>(OnTitleBarPointerDown);
            _titleBar.RegisterCallback<PointerMoveEvent>(OnTitleBarPointerMove);
            _titleBar.RegisterCallback<PointerUpEvent>(OnTitleBarPointerUp);

            _consoleRoot.Add(_titleBar);

            // Output area (scrollable)
            _outputScroll = new ScrollView(ScrollViewMode.Vertical);
            _outputScroll.style.flexGrow = 1;
            _outputScroll.style.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.8f);
            _outputScroll.style.borderTopLeftRadius = 2;
            _outputScroll.style.borderTopRightRadius = 2;
            _outputScroll.style.borderBottomLeftRadius = 2;
            _outputScroll.style.borderBottomRightRadius = 2;
            _outputScroll.style.paddingTop = 4;
            _outputScroll.style.paddingBottom = 4;
            _outputScroll.style.paddingLeft = 6;
            _outputScroll.style.paddingRight = 6;
            _outputScroll.style.marginBottom = 6;

            _outputLabel = new Label();
            _outputLabel.style.fontSize = 12;
            _outputLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
            _outputLabel.style.whiteSpace = WhiteSpace.Normal;
            _outputLabel.enableRichText = true;
            _outputLabel.pickingMode = PickingMode.Ignore;
            _outputScroll.Add(_outputLabel);
            _consoleRoot.Add(_outputScroll);

            // Input field
            _inputField = new TextField();
            _inputField.style.minHeight = 32;
            _inputField.style.fontSize = 14;
            _inputField.style.marginTop = 4;
            // Style the inner text input element so text isn't clipped
            var textInput = _inputField.Q(className: "unity-text-field__input");
            if (textInput != null)
            {
                textInput.style.paddingTop = 4;
                textInput.style.paddingBottom = 4;
                textInput.style.paddingLeft = 6;
                textInput.style.paddingRight = 6;
                textInput.style.fontSize = 14;
            }
            _inputField.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
            _consoleRoot.Add(_inputField);

            root.Add(_consoleRoot);
        }

        // ─── Open / Close ──────────────────────────────────────────

        private void OpenConsole()
        {
            _isOpen = true;
            SetConsoleVisible(true);
            _consoleRoot.BringToFront();
            _inputField.schedule.Execute(() =>
            {
                _inputField.Focus();
                _inputField.value = "";
            });
        }

        private void CloseConsole()
        {
            _isOpen = false;
            SetConsoleVisible(false);
            _inputField.value = "";
        }

        private void SetConsoleVisible(bool visible)
        {
            _consoleRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ─── Input Handling ────────────────────────────────────────

        private void OnInputKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                evt.StopImmediatePropagation();
                string input = _inputField.value.Trim();
                if (!string.IsNullOrEmpty(input))
                {
                    Print($"<color=#DCB43C>> {input}</color>");
                    ProcessCommand(input);
                }
                _inputField.value = "";
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                CloseConsole();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.BackQuote || evt.keyCode == KeyCode.Backslash)
            {
                evt.StopPropagation();
                CloseConsole();
            }
        }

        // ─── Drag ─────────────────────────────────────────────────

        private void OnTitleBarPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            _isDragging = true;
            _dragOffset = new Vector2(
                evt.position.x - _consoleRoot.resolvedStyle.left,
                evt.position.y - _consoleRoot.resolvedStyle.top);
            _titleBar.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnTitleBarPointerMove(PointerMoveEvent evt)
        {
            if (!_isDragging) return;
            _consoleRoot.style.left = evt.position.x - _dragOffset.x;
            _consoleRoot.style.top = evt.position.y - _dragOffset.y;
            evt.StopPropagation();
        }

        private void OnTitleBarPointerUp(PointerUpEvent evt)
        {
            if (!_isDragging) return;
            _isDragging = false;
            _titleBar.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        // ─── Runner Targeting ──────────────────────────────────────

        private GameSimulation Sim => _simulationRunner?.Simulation;

        /// <summary>
        /// Resolve a runner by name (case-insensitive partial match).
        /// Returns null and prints error if no match or ambiguous.
        /// </summary>
        private Runner ResolveRunner(string nameQuery)
        {
            if (Sim == null) return null;
            var query = nameQuery.ToLowerInvariant();
            var matches = Sim.CurrentGameState.Runners
                .Where(r => r.Name.ToLowerInvariant().Contains(query))
                .ToList();

            if (matches.Count == 1) return matches[0];
            if (matches.Count == 0)
            {
                Print($"<color=#CC4444>No runner matching '{nameQuery}'.</color>");
                return null;
            }
            Print($"<color=#CC4444>Ambiguous — {matches.Count} matches:</color>");
            foreach (var m in matches)
                Print($"  {m.Name}");
            return null;
        }

        /// <summary>
        /// Get the currently selected runner (from UIManager portrait bar).
        /// </summary>
        private Runner GetSelectedRunner()
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return null; }
            string selectedId = _uiManager?.SelectedRunnerId;
            if (selectedId == null)
            {
                Print("<color=#CC4444>No runner selected. Select one in the UI or pass a name.</color>");
                return null;
            }
            return Sim.FindRunner(selectedId);
        }

        /// <summary>
        /// Get runner from optional name arg, falling back to selected runner.
        /// Returns the runner and the number of args consumed for the name (0 or 1+).
        /// </summary>
        private (Runner runner, int argsConsumed) ResolveRunnerArg(string[] parts, int startIndex)
        {
            // Check if the arg at startIndex looks like a runner name (not a number, not a known keyword)
            if (startIndex < parts.Length && !int.TryParse(parts[startIndex], out _))
            {
                // Could be a quoted name with spaces — join remaining args that don't parse as commands
                // For simplicity: try single token first, then try two tokens
                string candidate = parts[startIndex];
                var runner = TryResolveRunnerSilent(candidate);
                if (runner != null) return (runner, 1);

                // Try two-token name (e.g., "Aldric Stormwind")
                if (startIndex + 1 < parts.Length)
                {
                    string twoToken = parts[startIndex] + " " + parts[startIndex + 1];
                    runner = TryResolveRunnerSilent(twoToken);
                    if (runner != null) return (runner, 2);
                }
            }

            // Fall back to selected
            return (GetSelectedRunner(), 0);
        }

        private Runner TryResolveRunnerSilent(string nameQuery)
        {
            if (Sim == null) return null;
            var query = nameQuery.ToLowerInvariant();
            var matches = Sim.CurrentGameState.Runners
                .Where(r => r.Name.ToLowerInvariant().Contains(query))
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        // ─── Command Processing ────────────────────────────────────

        private void ProcessCommand(string input)
        {
            if (input.StartsWith("/"))
                input = input.Substring(1);

            // Strip commas — users naturally type "/spawn Bob, mining 5" etc.
            input = input.Replace(",", " ");
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            string cmd = parts[0].ToLowerInvariant();

            switch (cmd)
            {
                case "help": PrintHelp(); break;
                case "spawn": HandleSpawn(parts); break;
                case "eventlog": HandleEventLog(parts); break;
                case "tick": HandleTick(parts); break;
                case "advance": HandleAdvance(parts); break;
                case "time": PrintTimeInfo(); break;
                case "hud": HandleHud(parts); break;
                case "runners": HandleRunners(); break;
                case "inspect": HandleInspect(parts); break;
                case "bank": HandleBank(); break;
                case "xp": HandleXp(parts); break;
                case "level": HandleLevel(parts); break;
                case "fill": HandleFill(parts); break;
                case "empty": HandleEmpty(parts); break;
                case "assign": HandleAssign(parts); break;
                case "idle": HandleIdle(parts); break;
                case "tp": HandleTeleport(parts); break;
                case "deposit": HandleDeposit(parts); break;
                case "withdraw": HandleWithdraw(parts); break;
                case "give": HandleGive(parts); break;
                case "sequences": HandleSequences(); break;
                case "macros": HandleMacros(); break;
                case "micros": HandleMicros(); break;
                case "state": HandleState(); break;
                case "nodes": HandleNodes(); break;
                case "items": HandleItems(); break;
                case "clear": ClearOutput(); break;
                default:
                    Print($"<color=#CC4444>Unknown command: {cmd}. Type /help for commands.</color>");
                    break;
            }
        }

        // ─── Help ──────────────────────────────────────────────────

        private void PrintHelp()
        {
            Print("<color=#DCB43C>--- Spawning ---</color>");
            Print("  /spawn random                     Random runner at hub");
            Print("  /spawn tutorial                   Tutorial-biased runner");
            Print("  /spawn mining 50 P woodcutting 30 Custom stats (P = passion)");
            Print("  /spawn \"Name\" mining 50 P         Named + custom stats");
            Print("");
            Print("<color=#DCB43C>--- Inspection ---</color>");
            Print("  /inspect [name]           Full runner state dump");
            Print("  /runners                  List all runners");
            Print("  /bank                     Bank contents");
            Print("  /state                    High-level game state");
            Print("  /nodes                    List world nodes");
            Print("  /items                    List registered items");
            Print("  /sequences                Task sequence library");
            Print("  /macros                   Macro ruleset library");
            Print("  /micros                   Micro ruleset library");
            Print("");
            Print("<color=#DCB43C>--- Runner Commands (target by name, default = selected) ---</color>");
            Print("  /give [name] <item> <qty>   Add items to inventory");
            Print("  /fill [name]                Fill inventory with current gather target");
            Print("  /empty [name]               Clear inventory");
            Print("  /xp [name] <skill> <amount> Grant XP");
            Print("  /level [name] <skill> <lvl> Set skill level");
            Print("  /assign [name] <node>       Work At (creates gather loop)");
            Print("  /idle [name]                Force idle");
            Print("  /tp [name] <node>           Teleport to node");
            Print("");
            Print("<color=#DCB43C>--- Economy ---</color>");
            Print("  /deposit <item> <qty>     Add items to bank");
            Print("  /withdraw <item> <qty>    Remove items from bank");
            Print("");
            Print("<color=#DCB43C>--- Simulation ---</color>");
            Print("  /tick                     Show current tick");
            Print("  /tick <N>                 Fast-forward N ticks");
            Print("  /advance 30s|5m|2h        Advance game time");
            Print("  /time                     Show game time");
            Print("");
            Print("<color=#DCB43C>--- Event Log ---</color>");
            Print("  /eventlog                 Last 50 entries");
            Print("  /eventlog <N>             Last N entries");
            Print("  /eventlog filter <cat>    Filter (warning/auto/state/prod/life)");
            Print("  /eventlog live            Toggle live streaming");
            Print("  /eventlog max             Buffer size");
            Print("");
            Print("<color=#DCB43C>--- Misc ---</color>");
            Print("  /hud on|off               Toggle tick/time HUD");
            Print("  /clear                    Clear console");
        }

        // ─── Spawn ─────────────────────────────────────────────────

        private void HandleSpawn(string[] parts)
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            if (parts.Length < 2)
            {
                Print("<color=#CC4444>Usage: /spawn random | /spawn tutorial | /spawn [\"name\"] skill level [P] ...</color>");
                return;
            }

            string hubId = Sim.CurrentGameState.Map.HubNodeId;
            string sub = parts[1].ToLowerInvariant();

            if (sub == "random")
            {
                var runner = RunnerFactory.Create(new System.Random(), Sim.Config, hubId);
                Sim.AddRunner(runner);
                Print($"Spawned random runner: <color=#7CCD7C>{runner.Name}</color> at {hubId}");
                PrintRunnerSkillSummary(runner);
                return;
            }

            if (sub == "tutorial")
            {
                var bias = new RunnerFactory.BiasConstraints
                {
                    PickOneSkillToBoostedAndPassionate = new[]
                    {
                        SkillType.Mining, SkillType.Woodcutting, SkillType.Fishing,
                        SkillType.Foraging, SkillType.PotionMaking,
                    },
                };
                var runner = RunnerFactory.CreateBiased(new System.Random(), Sim.Config, bias, hubId);
                Sim.AddRunner(runner);
                Print($"Spawned tutorial runner: <color=#7CCD7C>{runner.Name}</color> at {hubId}");
                PrintRunnerSkillSummary(runner);
                return;
            }

            // Custom spawn: /spawn [\"name\"] skill level [P] skill level [P] ...
            HandleSpawnCustom(parts);
        }

        private void HandleSpawnCustom(string[] parts)
        {
            string hubId = Sim.CurrentGameState.Map.HubNodeId;
            int idx = 1;
            string forcedName = null;

            // Check for quoted name: /spawn "First Last" mining 5
            if (parts[idx].StartsWith("\""))
            {
                var nameParts = new List<string>();
                for (; idx < parts.Length; idx++)
                {
                    string cleaned = parts[idx].Trim('"', ',', ' ');
                    if (cleaned.Length > 0) nameParts.Add(cleaned);
                    if (parts[idx].TrimEnd(',', ' ').EndsWith("\"")) { idx++; break; }
                }
                forcedName = string.Join(" ", nameParts);
            }
            // Check for unquoted name: if first token isn't a skill name, treat it as a name
            // Supports single-word (/spawn Bob mining 5) and two-word (/spawn Bob Smith mining 5)
            else if (!TryParseSkillType(parts[idx], out _))
            {
                forcedName = parts[idx];
                idx++;
                // Check if the next token is also not a skill (two-word name)
                if (idx < parts.Length && !TryParseSkillType(parts[idx], out _)
                    && !int.TryParse(parts[idx], out _))
                {
                    forcedName += " " + parts[idx];
                    idx++;
                }
            }

            // Parse skill overrides: skill level [P] skill level [P] ...
            var skillOverrides = new Dictionary<SkillType, (int level, bool passion)>();
            while (idx < parts.Length)
            {
                if (!TryParseSkillType(parts[idx], out var skillType))
                {
                    Print($"<color=#CC4444>Unknown skill: {parts[idx]}. Valid: melee, ranged, defence, hitpoints, magic, restoration, execution, mining, woodcutting, fishing, foraging, engineering, potionmaking, cooking, athletics</color>");
                    return;
                }
                idx++;

                if (idx >= parts.Length || !int.TryParse(parts[idx], out int level))
                {
                    Print($"<color=#CC4444>Expected level number after {skillType}.</color>");
                    return;
                }
                idx++;

                bool passion = false;
                if (idx < parts.Length && parts[idx].ToUpperInvariant() == "P")
                {
                    passion = true;
                    idx++;
                }

                skillOverrides[skillType] = (level, passion);
            }

            // Create a random runner, then apply overrides
            var runner = RunnerFactory.Create(new System.Random(), Sim.Config, hubId);
            if (forcedName != null)
                runner.Name = forcedName;

            foreach (var (skill, (level, passion)) in skillOverrides)
            {
                int i = (int)skill;
                runner.Skills[i].Level = Math.Clamp(level, 1, 99);
                if (passion) runner.Skills[i].HasPassion = true;
            }

            Sim.AddRunner(runner);
            Print($"Spawned custom runner: <color=#7CCD7C>{runner.Name}</color> at {hubId}");
            PrintRunnerSkillSummary(runner);
        }

        private bool TryParseSkillType(string input, out SkillType result)
        {
            result = default;
            string lower = input.ToLowerInvariant();
            // Support shorthand and full names
            var map = new Dictionary<string, SkillType>
            {
                ["melee"] = SkillType.Melee,
                ["ranged"] = SkillType.Ranged,
                ["defence"] = SkillType.Defence,
                ["defense"] = SkillType.Defence,
                ["hitpoints"] = SkillType.Hitpoints,
                ["hp"] = SkillType.Hitpoints,
                ["magic"] = SkillType.Magic,
                ["restoration"] = SkillType.Restoration,
                ["resto"] = SkillType.Restoration,
                ["execution"] = SkillType.Execution,
                ["mining"] = SkillType.Mining,
                ["woodcutting"] = SkillType.Woodcutting,
                ["wc"] = SkillType.Woodcutting,
                ["fishing"] = SkillType.Fishing,
                ["foraging"] = SkillType.Foraging,
                ["engineering"] = SkillType.Engineering,
                ["potionmaking"] = SkillType.PotionMaking,
                ["potion"] = SkillType.PotionMaking,
                ["cooking"] = SkillType.Cooking,
                ["athletics"] = SkillType.Athletics,
                ["farming"] = SkillType.Foraging, // alias
                ["herblore"] = SkillType.PotionMaking, // alias
            };

            if (map.TryGetValue(lower, out result)) return true;

            // Try exact enum parse
            return Enum.TryParse(input, true, out result);
        }

        private void PrintRunnerSkillSummary(Runner runner)
        {
            var sb = new StringBuilder("  Skills: ");
            bool first = true;
            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                var skill = runner.Skills[i];
                if (skill.Level > 1 || skill.HasPassion)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append($"{skill.Type} {skill.Level}");
                    if (skill.HasPassion) sb.Append("<color=#DCB43C>(P)</color>");
                }
            }
            if (first) sb.Append("all Lv 1");
            Print(sb.ToString());
        }

        // ─── Inspect ───────────────────────────────────────────────

        private void HandleInspect(string[] parts)
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            Runner runner;
            if (parts.Length > 1)
            {
                string nameQuery = string.Join(" ", parts.Skip(1));
                runner = ResolveRunner(nameQuery);
            }
            else
            {
                runner = GetSelectedRunner();
            }
            if (runner == null) return;

            Print($"<color=#DCB43C>=== {runner.Name} ===</color>");
            Print($"  ID: {runner.Id}");
            Print($"  State: <color=#7CCD7C>{runner.State}</color> at {runner.CurrentNodeId}");

            // Task sequence
            var seq = Sim.GetRunnerTaskSequence(runner);
            if (seq != null)
            {
                Print($"  Task: {seq.Name} (step {runner.TaskSequenceCurrentStepIndex}/{seq.Steps.Count}, loop={seq.Loop})");
                if (runner.MacroSuspendedUntilLoop)
                    Print("  <color=#CC9900>Macro rules suspended until loop completes</color>");
            }
            else
            {
                Print("  Task: none");
            }

            // Pending
            var pending = Sim.GetRunnerPendingTaskSequence(runner);
            if (pending != null)
                Print($"  Pending: {pending.Name}");

            // Macro
            var macro = Sim.GetRunnerMacroRuleset(runner);
            Print(macro != null
                ? $"  Macro: {macro.Name} ({macro.Rules.Count} rules)"
                : "  Macro: none");

            // Warning
            if (!string.IsNullOrEmpty(runner.ActiveWarning))
                Print($"  <color=#CC4444>Warning: {runner.ActiveWarning}</color>");

            // Travel state
            if (runner.State == RunnerState.Traveling && runner.Travel != null)
                Print($"  Travel: {runner.Travel.FromNodeId} -> {runner.Travel.ToNodeId} ({runner.Travel.Progress:P0})");

            // Gathering state
            if (runner.State == RunnerState.Gathering && runner.Gathering != null)
                Print($"  Gathering: node={runner.Gathering.NodeId} index={runner.Gathering.GatherableIndex} progress={runner.Gathering.TickAccumulator:F1}/{runner.Gathering.TicksRequired:F1}");

            // Inventory
            var inv = runner.Inventory;
            Print($"  Inventory: {inv.Slots.Count}/{inv.MaxSlots} slots");
            var itemCounts = new Dictionary<string, int>();
            foreach (var slot in inv.Slots)
            {
                itemCounts.TryGetValue(slot.ItemId, out int c);
                itemCounts[slot.ItemId] = c + slot.Quantity;
            }
            foreach (var (itemId, qty) in itemCounts)
            {
                string iname = Sim.ItemRegistry.Get(itemId)?.Name ?? itemId;
                Print($"    {iname}: {qty}");
            }

            // Skills
            Print("  Skills:");
            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                var skill = runner.Skills[i];
                string passion = skill.HasPassion ? " <color=#DCB43C>(P)</color>" : "";
                float effectiveLevel = skill.GetEffectiveLevel(Sim.Config);
                string effective = effectiveLevel != skill.Level ? $" (eff: {effectiveLevel:F1})" : "";
                Print($"    {skill.Type}: Lv {skill.Level}{effective} | XP {skill.Xp:F0}{passion}");
            }
        }

        // ─── Runners ───────────────────────────────────────────────

        private void HandleRunners()
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            var runners = Sim.CurrentGameState.Runners;
            Print($"{runners.Count} runners:");
            foreach (var r in runners)
            {
                string taskInfo = r.TaskSequenceId != null
                    ? Sim.GetRunnerTaskSequence(r)?.Name ?? r.TaskSequenceId
                    : "none";
                Print($"  <color=#7CCD7C>{r.Name}</color> [{r.State}] at {r.CurrentNodeId} | Task: {taskInfo}");
            }
        }

        // ─── Bank ──────────────────────────────────────────────────

        private void HandleBank()
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            var bank = Sim.CurrentGameState.Bank;
            if (bank.Stacks.Count == 0)
            {
                Print("Bank is empty.");
                return;
            }
            Print($"Bank ({bank.Stacks.Count} item types):");
            foreach (var stack in bank.Stacks)
            {
                string name = Sim.ItemRegistry.Get(stack.ItemId)?.Name ?? stack.ItemId;
                Print($"  {name}: {stack.Quantity}");
            }
        }

        // ─── XP / Level ────────────────────────────────────────────

        private void HandleXp(string[] parts)
        {
            // /xp [name] <skill> <amount>
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }
            if (parts.Length < 3)
            {
                Print("<color=#CC4444>Usage: /xp [runner] <skill> <amount></color>");
                return;
            }

            var (runner, consumed) = ResolveRunnerArg(parts, 1);
            if (runner == null) return;

            int skillIdx = 1 + consumed;
            if (skillIdx + 1 >= parts.Length)
            {
                Print("<color=#CC4444>Usage: /xp [runner] <skill> <amount></color>");
                return;
            }

            if (!TryParseSkillType(parts[skillIdx], out var skillType))
            {
                Print($"<color=#CC4444>Unknown skill: {parts[skillIdx]}</color>");
                return;
            }
            if (!float.TryParse(parts[skillIdx + 1], out float amount))
            {
                Print($"<color=#CC4444>Invalid amount: {parts[skillIdx + 1]}</color>");
                return;
            }

            var skill = runner.Skills[(int)skillType];
            int levelBefore = skill.Level;
            bool leveledUp = skill.AddXp(amount, Sim.Config);
            string levelMsg = leveledUp ? $" <color=#7CCD7C>LEVEL UP! {levelBefore} → {skill.Level}</color>" : "";
            Print($"Granted {amount:F0} XP to {runner.Name}'s {skillType} (now {skill.Xp:F0} XP, level {skill.Level}){levelMsg}");
        }

        private void HandleLevel(string[] parts)
        {
            // /level [name] <skill> <level>
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }
            if (parts.Length < 3)
            {
                Print("<color=#CC4444>Usage: /level [runner] <skill> <level></color>");
                return;
            }

            var (runner, consumed) = ResolveRunnerArg(parts, 1);
            if (runner == null) return;

            int skillIdx = 1 + consumed;
            if (skillIdx + 1 >= parts.Length)
            {
                Print("<color=#CC4444>Usage: /level [runner] <skill> <level></color>");
                return;
            }

            if (!TryParseSkillType(parts[skillIdx], out var skillType))
            {
                Print($"<color=#CC4444>Unknown skill: {parts[skillIdx]}</color>");
                return;
            }
            if (!int.TryParse(parts[skillIdx + 1], out int level))
            {
                Print($"<color=#CC4444>Invalid level: {parts[skillIdx + 1]}</color>");
                return;
            }

            level = Math.Clamp(level, 1, 99);
            runner.Skills[(int)skillType].Level = level;
            Print($"Set {runner.Name}'s {skillType} to level {level}");
        }

        // ─── Inventory Manipulation ────────────────────────────────

        private void HandleFill(string[] parts)
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            var (runner, argsConsumed) = ResolveRunnerArg(parts, 1);
            if (runner == null) return;

            // Check if an explicit item ID was provided after the runner name
            int itemArgIndex = 1 + argsConsumed;
            string itemId = null;
            if (itemArgIndex < parts.Length)
            {
                itemId = parts[itemArgIndex].ToLowerInvariant();
                if (Sim.ItemRegistry.Get(itemId) == null)
                {
                    Print($"<color=#CC4444>Unknown item: {parts[itemArgIndex]}. Use /items to list.</color>");
                    return;
                }
            }

            // Auto-detect: current gather target > first inventory item > first registered item
            if (itemId == null && runner.State == RunnerState.Gathering && runner.Gathering != null)
            {
                var node = Sim.CurrentGameState.Map.GetNode(runner.Gathering.NodeId);
                if (node != null && runner.Gathering.GatherableIndex < node.Gatherables.Length)
                    itemId = node.Gatherables[runner.Gathering.GatherableIndex].ProducedItemId;
            }
            if (itemId == null)
            {
                var firstSlot = runner.Inventory.Slots.FirstOrDefault();
                if (firstSlot != null) itemId = firstSlot.ItemId;
            }
            if (itemId == null)
            {
                var firstItem = Sim.ItemRegistry.AllItemDefinitions.FirstOrDefault();
                itemId = firstItem?.Id;
            }
            if (itemId == null)
            {
                Print("<color=#CC4444>No items registered.</color>");
                return;
            }

            var def = Sim.ItemRegistry.Get(itemId);
            int added = 0;
            while (runner.Inventory.TryAdd(def))
                added++;

            Print($"Added {added}x {def.Name} to {runner.Name}'s inventory (now full)");
        }

        private void HandleEmpty(string[] parts)
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            var (runner, _) = ResolveRunnerArg(parts, 1);
            if (runner == null) return;

            runner.Inventory.Clear();
            Print($"Cleared {runner.Name}'s inventory");
        }

        private void HandleGive(string[] parts)
        {
            // /give [name] <item> <qty>
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }
            if (parts.Length < 3)
            {
                Print("<color=#CC4444>Usage: /give [runner] <item_id> <quantity></color>");
                return;
            }

            var (runner, consumed) = ResolveRunnerArg(parts, 1);
            if (runner == null) return;

            int itemIdx = 1 + consumed;
            if (itemIdx + 1 >= parts.Length)
            {
                Print("<color=#CC4444>Usage: /give [runner] <item_id> <quantity></color>");
                return;
            }

            string itemId = parts[itemIdx];
            var def = Sim.ItemRegistry.Get(itemId);
            if (def == null)
            {
                Print($"<color=#CC4444>Unknown item: {itemId}. Use /items to list.</color>");
                return;
            }

            if (!int.TryParse(parts[itemIdx + 1], out int qty) || qty < 1)
            {
                Print($"<color=#CC4444>Invalid quantity: {parts[itemIdx + 1]}</color>");
                return;
            }

            int added = 0;
            for (int i = 0; i < qty; i++)
            {
                if (!runner.Inventory.TryAdd(def)) break;
                added++;
            }
            Print($"Added {added}x {def.Name} to {runner.Name}" + (added < qty ? $" (inventory full, {qty - added} couldn't fit)" : ""));
        }

        // ─── Assignment / Movement ─────────────────────────────────

        private void HandleAssign(string[] parts)
        {
            // /assign [name] <node>
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }
            if (parts.Length < 2)
            {
                Print("<color=#CC4444>Usage: /assign [runner] <nodeId></color>");
                return;
            }

            var (runner, consumed) = ResolveRunnerArg(parts, 1);
            if (runner == null) return;

            int nodeIdx = 1 + consumed;
            if (nodeIdx >= parts.Length)
            {
                Print("<color=#CC4444>Usage: /assign [runner] <nodeId></color>");
                return;
            }

            string nodeId = parts[nodeIdx];
            var node = Sim.CurrentGameState.Map.GetNode(nodeId);
            if (node == null)
            {
                Print($"<color=#CC4444>Unknown node: {nodeId}. Use /nodes to list.</color>");
                return;
            }

            Sim.CommandWorkAtSuspendMacrosForOneCycle(runner.Id, nodeId);
            Print($"Assigned {runner.Name} to work at {node.Name} ({nodeId})");
        }

        private void HandleIdle(string[] parts)
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            Runner runner;
            if (parts.Length > 1)
                runner = ResolveRunner(string.Join(" ", parts.Skip(1)));
            else
                runner = GetSelectedRunner();
            if (runner == null) return;

            Sim.ClearTaskSequence(runner.Id);
            Print($"{runner.Name} set to idle");
        }

        private void HandleTeleport(string[] parts)
        {
            // /tp [name] <node>
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }
            if (parts.Length < 2)
            {
                Print("<color=#CC4444>Usage: /tp [runner] <nodeId></color>");
                return;
            }

            var (runner, consumed) = ResolveRunnerArg(parts, 1);
            if (runner == null) return;

            int nodeIdx = 1 + consumed;
            if (nodeIdx >= parts.Length)
            {
                Print("<color=#CC4444>Usage: /tp [runner] <nodeId></color>");
                return;
            }

            string nodeId = parts[nodeIdx];
            var node = Sim.CurrentGameState.Map.GetNode(nodeId);
            if (node == null)
            {
                Print($"<color=#CC4444>Unknown node: {nodeId}. Use /nodes to list.</color>");
                return;
            }

            // Clear any active sequence/state and teleport
            Sim.ClearTaskSequence(runner.Id);
            runner.CurrentNodeId = nodeId;
            runner.State = RunnerState.Idle;
            runner.Travel = null;
            runner.Gathering = null;
            runner.Depositing = null;
            runner.RedirectWorldX = null;
            runner.RedirectWorldZ = null;
            Print($"Teleported {runner.Name} to {node.Name} ({nodeId})");
        }

        // ─── Economy ───────────────────────────────────────────────

        private void HandleDeposit(string[] parts)
        {
            // /deposit <item> <qty>
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }
            if (parts.Length < 3)
            {
                Print("<color=#CC4444>Usage: /deposit <item_id> <quantity></color>");
                return;
            }

            string itemId = parts[1];
            if (!Sim.ItemRegistry.Has(itemId))
            {
                Print($"<color=#CC4444>Unknown item: {itemId}. Use /items to list.</color>");
                return;
            }
            if (!int.TryParse(parts[2], out int qty) || qty < 1)
            {
                Print($"<color=#CC4444>Invalid quantity: {parts[2]}</color>");
                return;
            }

            Sim.CurrentGameState.Bank.Deposit(itemId, qty);
            string name = Sim.ItemRegistry.Get(itemId)?.Name ?? itemId;
            Print($"Deposited {qty}x {name} to bank (total: {Sim.CurrentGameState.Bank.CountItem(itemId)})");
        }

        private void HandleWithdraw(string[] parts)
        {
            // /withdraw <item> <qty>
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }
            if (parts.Length < 3)
            {
                Print("<color=#CC4444>Usage: /withdraw <item_id> <quantity></color>");
                return;
            }

            string itemId = parts[1];
            if (!Sim.ItemRegistry.Has(itemId))
            {
                Print($"<color=#CC4444>Unknown item: {itemId}. Use /items to list.</color>");
                return;
            }
            if (!int.TryParse(parts[2], out int qty) || qty < 1)
            {
                Print($"<color=#CC4444>Invalid quantity: {parts[2]}</color>");
                return;
            }

            var bank = Sim.CurrentGameState.Bank;
            int current = bank.CountItem(itemId);
            int removed = Math.Min(qty, current);
            if (removed <= 0)
            {
                Print($"<color=#CC4444>Bank has no {itemId}.</color>");
                return;
            }
            // Directly manipulate Stacks (RemoveFromBank is private, dev console only)
            for (int i = 0; i < bank.Stacks.Count; i++)
            {
                if (bank.Stacks[i].ItemId != itemId) continue;
                bank.Stacks[i].Quantity -= removed;
                if (bank.Stacks[i].Quantity <= 0) bank.Stacks.RemoveAt(i);
                break;
            }
            string name = Sim.ItemRegistry.Get(itemId)?.Name ?? itemId;
            Print($"Withdrew {removed}x {name} from bank (remaining: {bank.CountItem(itemId)})");
        }

        // ─── Tick / Time ───────────────────────────────────────────

        private void HandleTick(string[] parts)
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            if (parts.Length < 2)
            {
                // Just show tick info
                Print($"Current tick: {Sim.CurrentGameState.TickCount}");
                return;
            }

            if (int.TryParse(parts[1], out int count) && count > 0)
            {
                long startTick = Sim.CurrentGameState.TickCount;
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < count; i++)
                    Sim.Tick();
                stopwatch.Stop();
                long endTick = Sim.CurrentGameState.TickCount;

                float seconds = (endTick - startTick) * Sim.TickDeltaTime;
                Print($"Advanced {count} ticks ({seconds:F1}s game time) in {stopwatch.ElapsedMilliseconds}ms real time. Now at tick {endTick}.");
                return;
            }

            Print($"<color=#CC4444>Usage: /tick <N> to advance N ticks, or /tick to show current.</color>");
        }

        private void HandleAdvance(string[] parts)
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            if (parts.Length < 2)
            {
                Print("<color=#CC4444>Usage: /advance 30s, /advance 5m, /advance 2h</color>");
                return;
            }

            string arg = parts[1].ToLowerInvariant().Trim();
            float totalSeconds;

            // Parse time suffix: s=seconds, m=minutes, h=hours
            if (arg.EndsWith("s") && float.TryParse(arg.TrimEnd('s'), out float secs))
                totalSeconds = secs;
            else if (arg.EndsWith("m") && float.TryParse(arg.TrimEnd('m'), out float mins))
                totalSeconds = mins * 60f;
            else if (arg.EndsWith("h") && float.TryParse(arg.TrimEnd('h'), out float hours))
                totalSeconds = hours * 3600f;
            else if (float.TryParse(arg, out float raw))
                totalSeconds = raw; // bare number = seconds
            else
            {
                Print("<color=#CC4444>Usage: /advance 30s, /advance 5m, /advance 2h (bare number = seconds)</color>");
                return;
            }

            int tickCount = Math.Max(1, (int)(totalSeconds / Sim.TickDeltaTime));
            long startTick = Sim.CurrentGameState.TickCount;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < tickCount; i++)
                Sim.Tick();
            stopwatch.Stop();
            long endTick = Sim.CurrentGameState.TickCount;

            float actualSeconds = (endTick - startTick) * Sim.TickDeltaTime;
            Print($"Advanced {actualSeconds:F1}s game time ({tickCount} ticks) in {stopwatch.ElapsedMilliseconds}ms real time. Now at tick {endTick}.");
        }

        private void PrintTimeInfo()
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            long ticks = Sim.CurrentGameState.TickCount;
            float seconds = ticks * Sim.TickDeltaTime;
            int totalSeconds = (int)seconds;
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int secs = totalSeconds % 60;
            Print($"Game time: {hours}:{minutes:D2}:{secs:D2} ({ticks} ticks)");
        }

        // ─── Library Inspection ────────────────────────────────────

        private void HandleSequences()
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            var lib = Sim.CurrentGameState.TaskSequenceLibrary;
            Print($"Task Sequence Library ({lib.Count} entries):");
            foreach (var seq in lib)
            {
                int userCount = Sim.CurrentGameState.Runners.Count(r => r.TaskSequenceId == seq.Id);
                string loopStr = seq.Loop ? "loop" : "once";
                Print($"  <color=#7CCD7C>{seq.Name}</color> [{loopStr}, {seq.Steps.Count} steps, {userCount} runners] target={seq.TargetNodeId}");
            }
        }

        private void HandleMacros()
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            var lib = Sim.CurrentGameState.MacroRulesetLibrary;
            Print($"Macro Ruleset Library ({lib.Count} entries):");
            foreach (var rs in lib)
            {
                int userCount = Sim.CurrentGameState.Runners.Count(r => r.MacroRulesetId == rs.Id);
                Print($"  <color=#7CCD7C>{rs.Name}</color> [{rs.Rules.Count} rules, {userCount} runners]");
            }
        }

        private void HandleMicros()
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            var lib = Sim.CurrentGameState.MicroRulesetLibrary;
            Print($"Micro Ruleset Library ({lib.Count} entries):");
            foreach (var rs in lib)
            {
                Print($"  <color=#7CCD7C>{rs.Name}</color> [{rs.Rules.Count} rules] ({rs.Category})");
            }
        }

        // ─── State / Nodes / Items ─────────────────────────────────

        private void HandleState()
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            var gs = Sim.CurrentGameState;
            long ticks = gs.TickCount;
            float seconds = ticks * Sim.TickDeltaTime;

            Print("<color=#DCB43C>=== Game State ===</color>");
            Print($"  Tick: {ticks} ({seconds:F0}s)");
            Print($"  Runners: {gs.Runners.Count}");
            Print($"  Nodes: {gs.Map.Nodes.Count}");
            Print($"  Task Sequences: {gs.TaskSequenceLibrary.Count}");
            Print($"  Macro Rulesets: {gs.MacroRulesetLibrary.Count}");
            Print($"  Micro Rulesets: {gs.MicroRulesetLibrary.Count}");
            Print($"  Bank items: {gs.Bank.Stacks.Count} types");
            Print($"  Event Log: {Sim.EventLog.Entries.Count} entries");
            Print($"  Macro Decision Log: {gs.MacroDecisionLog.Entries.Count} entries");
            Print($"  Micro Decision Log: {gs.MicroDecisionLog.Entries.Count} entries");
        }

        private void HandleNodes()
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            var map = Sim.CurrentGameState.Map;
            Print($"{map.Nodes.Count} nodes (hub={map.HubNodeId}):");
            foreach (var node in map.Nodes)
            {
                int runnerCount = Sim.CurrentGameState.Runners.Count(r => r.CurrentNodeId == node.Id);
                string gatherables = node.Gatherables.Length > 0
                    ? string.Join(", ", node.Gatherables.Select(g => g.ProducedItemId))
                    : "none";
                Print($"  <color=#7CCD7C>{node.Name}</color> ({node.Id}) [{runnerCount} runners] gatherables: {gatherables}");
            }
        }

        private void HandleItems()
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            var items = Sim.ItemRegistry.AllItemDefinitions.ToList();
            Print($"{items.Count} registered items:");
            foreach (var item in items)
                Print($"  <color=#7CCD7C>{item.Id}</color> — {item.Name} ({item.Category})");
        }

        // ─── Event Log ─────────────────────────────────────────────

        private void HandleEventLog(string[] parts)
        {
            if (Sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            var eventLog = Sim.EventLog;

            if (parts.Length == 1)
            {
                DumpEventLog(eventLog.GetAll(), 50);
                return;
            }

            string sub = parts[1].ToLowerInvariant();

            if (sub == "live")
            {
                _eventLogLive = !_eventLogLive;
                Print(_eventLogLive
                    ? "<color=#7CCD7C>Live event streaming ON</color>"
                    : "Live event streaming OFF");
                return;
            }

            if (sub == "max")
            {
                Print($"Event log buffer: {eventLog.Entries.Count} entries");
                return;
            }

            if (sub == "filter" && parts.Length > 2)
            {
                string catStr = parts[2].ToLowerInvariant();
                EventCategory? cat = catStr switch
                {
                    "warning" or "warn" => EventCategory.Warning,
                    "auto" or "automation" => EventCategory.Automation,
                    "state" => EventCategory.StateChange,
                    "prod" or "production" => EventCategory.Production,
                    "life" or "lifecycle" => EventCategory.Lifecycle,
                    _ => null,
                };

                if (cat == null)
                {
                    Print($"<color=#CC4444>Unknown category: {catStr}. Use: warning, auto, state, prod, life.</color>");
                    return;
                }

                var filtered = new List<EventLogEntry>();
                foreach (var entry in eventLog.GetAll())
                {
                    if (entry.Category == cat.Value)
                        filtered.Add(entry);
                }
                DumpEventLog(filtered, 50);
                return;
            }

            if (int.TryParse(sub, out int count))
            {
                DumpEventLog(eventLog.GetAll(), count);
                return;
            }

            Print($"<color=#CC4444>Unknown eventlog subcommand: {sub}</color>");
        }

        private void DumpEventLog(List<EventLogEntry> entries, int limit)
        {
            if (entries.Count == 0)
            {
                Print("(no entries)");
                return;
            }

            int shown = Math.Min(entries.Count, limit);
            Print($"Showing {shown} of {entries.Count} entries:");

            for (int i = shown - 1; i >= 0; i--)
            {
                var e = entries[i];
                string catColor = e.Category switch
                {
                    EventCategory.Warning => "#CC4444",
                    EventCategory.Automation => "#6699CC",
                    EventCategory.StateChange => "#AAAAAA",
                    EventCategory.Production => "#7CCD7C",
                    EventCategory.Lifecycle => "#CC99CC",
                    _ => "#AAAAAA",
                };
                string repeat = e.RepeatCount > 1 ? $" (x{e.RepeatCount})" : "";
                Print($"  <color={catColor}>[{e.Category}]</color> T{e.TickNumber}: {e.Summary}{repeat}");
            }
        }

        // ─── HUD ───────────────────────────────────────────────────

        private void HandleHud(string[] parts)
        {
            if (parts.Length < 2)
            {
                Print($"HUD is {(_hudVisible ? "ON" : "OFF")}. Use /hud on or /hud off.");
                return;
            }

            string sub = parts[1].ToLowerInvariant();
            if (sub == "on") { _hudVisible = true; _hudLabel.style.display = DisplayStyle.Flex; Print("HUD ON"); }
            else if (sub == "off") { _hudVisible = false; _hudLabel.style.display = DisplayStyle.None; Print("HUD OFF"); }
            else Print($"<color=#CC4444>Use /hud on or /hud off.</color>");
        }

        // ─── Output ────────────────────────────────────────────────

        private void ClearOutput()
        {
            _outputBuffer.Clear();
            _outputLineCount = 0;
            _outputLabel.text = "";
        }

        private void Print(string text)
        {
            if (_outputLineCount >= MaxOutputLines)
            {
                string content = _outputBuffer.ToString();
                int trimIndex = 0;
                int linesToRemove = 100;
                for (int i = 0; i < content.Length && linesToRemove > 0; i++)
                {
                    if (content[i] == '\n')
                    {
                        linesToRemove--;
                        trimIndex = i + 1;
                    }
                }
                _outputBuffer.Clear();
                _outputBuffer.Append(content.Substring(trimIndex));
                _outputLineCount -= 100;
            }

            _outputBuffer.AppendLine(text);
            _outputLineCount++;
            _outputLabel.text = _outputBuffer.ToString();

            _outputScroll.schedule.Execute(() =>
                _outputScroll.scrollOffset = new Vector2(0, float.MaxValue));
        }

        // ─── Tick Handler ──────────────────────────────────────────

        private void OnTick(SimulationTickCompleted e)
        {
            if (_hudVisible && _hudLabel != null)
            {
                long ticks = Sim.CurrentGameState.TickCount;
                float seconds = ticks * Sim.TickDeltaTime;
                int totalSeconds = (int)seconds;
                int minutes = totalSeconds / 60;
                int secs = totalSeconds % 60;
                _hudLabel.text = $"Tick {ticks} | {minutes}:{secs:D2}";
            }

            if (_eventLogLive && _isOpen)
            {
                var entries = Sim.EventLog.Entries;
                for (int i = _lastSeenEventLogCount; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    string cat = entry.Category.ToString().Substring(0, Math.Min(4, entry.Category.ToString().Length));
                    Print($"  <color=#6699CC>[LIVE]</color> T{entry.TickNumber} [{cat}] {entry.Summary}");
                }
            }
            _lastSeenEventLogCount = Sim.EventLog.Entries.Count;
        }
    }
}
#endif

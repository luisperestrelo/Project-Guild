#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ProjectGuild.Bridge;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View
{
    /// <summary>
    /// Development console for spawning runners, inspecting event logs, and
    /// viewing tick/time counters. Tilde key to open/close.
    ///
    /// Built entirely in code (no UXML) since this is dev-only tooling.
    /// Excluded from release builds via UNITY_EDITOR || DEVELOPMENT_BUILD.
    /// </summary>
    public class DevConsole : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _simulationRunner;
        [SerializeField] private UIDocument _uiDocument;

        private VisualElement _consoleRoot;
        private ScrollView _outputScroll;
        private Label _outputLabel;
        private TextField _inputField;
        private Label _hudLabel;

        private bool _isOpen;
        private bool _hudVisible = true;
        private bool _eventLogLive;

        private readonly StringBuilder _outputBuffer = new();
        private const int MaxOutputLines = 500;
        private int _outputLineCount;

        // ─── Lifecycle ─────────────────────────────────────────────

        private void Start()
        {
            if (_simulationRunner == null)
                _simulationRunner = FindAnyObjectByType<SimulationRunner>();
            if (_uiDocument == null)
                _uiDocument = FindAnyObjectByType<UIDocument>();

            BuildUI();
            SetConsoleVisible(false);

            if (_simulationRunner?.Simulation != null)
            {
                _simulationRunner.Simulation.Events.Subscribe<SimulationTickCompleted>(OnTick);
            }
        }

        private void Update()
        {
            if (Keyboard.current.backquoteKey.wasPressedThisFrame)
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

            // ─ HUD (persistent tick/time counter, top-left) ─
            _hudLabel = new Label("Tick 0 | 0:00");
            _hudLabel.style.position = Position.Absolute;
            _hudLabel.style.left = 8;
            _hudLabel.style.top = 8;
            _hudLabel.style.fontSize = 12;
            _hudLabel.style.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);
            _hudLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _hudLabel.pickingMode = PickingMode.Ignore;
            root.Add(_hudLabel);

            // ─ Console panel (centered overlay) ─
            _consoleRoot = new VisualElement();
            _consoleRoot.style.position = Position.Absolute;
            _consoleRoot.style.left = new Length(50, LengthUnit.Percent);
            _consoleRoot.style.top = new Length(10, LengthUnit.Percent);
            _consoleRoot.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
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

            // Title
            var title = new Label("Dev Console");
            title.style.fontSize = 14;
            title.style.color = new Color(0.86f, 0.71f, 0.24f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 6;
            title.pickingMode = PickingMode.Ignore;
            _consoleRoot.Add(title);

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
            _inputField.style.height = 28;
            _inputField.style.fontSize = 13;
            _inputField.RegisterCallback<KeyDownEvent>(OnInputKeyDown);
            _consoleRoot.Add(_inputField);

            root.Add(_consoleRoot);
        }

        // ─── Open / Close ──────────────────────────────────────────

        private void OpenConsole()
        {
            _isOpen = true;
            SetConsoleVisible(true);
            // Focus the input field after a frame (UI Toolkit needs a frame to register)
            _inputField.schedule.Execute(() =>
            {
                _inputField.Focus();
                // Clear the backtick that triggered the open
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
                string input = _inputField.value.Trim();
                if (!string.IsNullOrEmpty(input))
                {
                    Print($"<color=#DCB43C>> {input}</color>");
                    ProcessCommand(input);
                }
                _inputField.value = "";
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                CloseConsole();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.BackQuote)
            {
                // Prevent backtick from appearing in input
                evt.StopPropagation();
                evt.PreventDefault();
                CloseConsole();
            }
        }

        // ─── Command Processing ────────────────────────────────────

        private void ProcessCommand(string input)
        {
            // Strip leading / if present
            if (input.StartsWith("/"))
                input = input.Substring(1);

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            string cmd = parts[0].ToLowerInvariant();

            switch (cmd)
            {
                case "help":
                    PrintHelp();
                    break;
                case "spawn":
                    HandleSpawn(parts);
                    break;
                case "eventlog":
                    HandleEventLog(parts);
                    break;
                case "tick":
                    PrintTickInfo();
                    break;
                case "time":
                    PrintTimeInfo();
                    break;
                case "hud":
                    HandleHud(parts);
                    break;
                case "runners":
                    HandleRunners();
                    break;
                case "clear":
                    ClearOutput();
                    break;
                default:
                    Print($"<color=#CC4444>Unknown command: {cmd}. Type /help for commands.</color>");
                    break;
            }
        }

        // ─── Commands ──────────────────────────────────────────────

        private void PrintHelp()
        {
            Print("<color=#DCB43C>Commands:</color>");
            Print("  /spawn random         Spawn a random runner");
            Print("  /spawn tutorial       Spawn a tutorial-biased runner");
            Print("  /eventlog             Show last 50 event log entries");
            Print("  /eventlog <N>         Show last N entries");
            Print("  /eventlog filter <cat> Filter by category (warning/auto/state/prod/life)");
            Print("  /eventlog live        Toggle live event streaming");
            Print("  /eventlog max         Show event buffer size");
            Print("  /runners              List all runners");
            Print("  /tick                 Show current tick number");
            Print("  /time                 Show game time");
            Print("  /hud on|off           Toggle tick/time HUD");
            Print("  /clear                Clear console output");
        }

        private void HandleSpawn(string[] parts)
        {
            var sim = _simulationRunner?.Simulation;
            if (sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            string mode = parts.Length > 1 ? parts[1].ToLowerInvariant() : "random";
            string hubId = sim.CurrentGameState.Map.HubNodeId;
            Runner newRunner;

            switch (mode)
            {
                case "random":
                    newRunner = RunnerFactory.Create(new System.Random(), sim.Config, hubId);
                    sim.AddRunner(newRunner);
                    Print($"Spawned random runner: <color=#7CCD7C>{newRunner.Name}</color> at {hubId}");
                    PrintRunnerSkillSummary(newRunner, sim.Config);
                    break;

                case "tutorial":
                    var bias = new RunnerFactory.BiasConstraints
                    {
                        PickOneSkillToBoostedAndPassionate = new[]
                        {
                            SkillType.Mining, SkillType.Woodcutting, SkillType.Fishing,
                            SkillType.Farming, SkillType.Herblore,
                        },
                    };
                    newRunner = RunnerFactory.CreateBiased(new System.Random(), sim.Config, bias, hubId);
                    sim.AddRunner(newRunner);
                    Print($"Spawned tutorial runner: <color=#7CCD7C>{newRunner.Name}</color> at {hubId}");
                    PrintRunnerSkillSummary(newRunner, sim.Config);
                    break;

                default:
                    Print($"<color=#CC4444>Unknown spawn mode: {mode}. Use 'random' or 'tutorial'.</color>");
                    break;
            }
        }

        private void PrintRunnerSkillSummary(Runner runner, SimulationConfig config)
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
                    if (skill.HasPassion) sb.Append("(P)");
                }
            }
            if (first) sb.Append("all Lv 1");
            Print(sb.ToString());
        }

        private void HandleEventLog(string[] parts)
        {
            var sim = _simulationRunner?.Simulation;
            if (sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            var eventLog = sim.EventLog;

            if (parts.Length == 1)
            {
                // /eventlog — show last 50
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

            // /eventlog N
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

            // entries are already newest-first from GetAll()
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

        private void PrintTickInfo()
        {
            var sim = _simulationRunner?.Simulation;
            if (sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }
            Print($"Current tick: {sim.CurrentGameState.TickCount}");
        }

        private void PrintTimeInfo()
        {
            var sim = _simulationRunner?.Simulation;
            if (sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            long ticks = sim.CurrentGameState.TickCount;
            float seconds = ticks * sim.TickDeltaTime;
            int totalSeconds = (int)seconds;
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int secs = totalSeconds % 60;
            Print($"Game time: {hours}:{minutes:D2}:{secs:D2} ({ticks} ticks)");
        }

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

        private void HandleRunners()
        {
            var sim = _simulationRunner?.Simulation;
            if (sim == null) { Print("<color=#CC4444>No simulation running.</color>"); return; }

            var runners = sim.CurrentGameState.Runners;
            Print($"{runners.Count} runners:");
            foreach (var r in runners)
            {
                string taskInfo = r.TaskSequenceId != null
                    ? sim.GetRunnerTaskSequence(r)?.Name ?? r.TaskSequenceId
                    : "none";
                Print($"  <color=#7CCD7C>{r.Name}</color> [{r.State}] at {r.CurrentNodeId} | Task: {taskInfo}");
            }
        }

        private void ClearOutput()
        {
            _outputBuffer.Clear();
            _outputLineCount = 0;
            _outputLabel.text = "";
        }

        // ─── Output ────────────────────────────────────────────────

        private void Print(string text)
        {
            if (_outputLineCount >= MaxOutputLines)
            {
                // Trim oldest lines (remove first 100 lines)
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

            // Auto-scroll to bottom
            _outputScroll.schedule.Execute(() =>
                _outputScroll.scrollOffset = new Vector2(0, float.MaxValue));
        }

        // ─── Tick Handler ──────────────────────────────────────────

        private void OnTick(SimulationTickCompleted e)
        {
            // Update HUD
            if (_hudVisible && _hudLabel != null)
            {
                var sim = _simulationRunner.Simulation;
                long ticks = sim.CurrentGameState.TickCount;
                float seconds = ticks * sim.TickDeltaTime;
                int totalSeconds = (int)seconds;
                int minutes = totalSeconds / 60;
                int secs = totalSeconds % 60;
                _hudLabel.text = $"Tick {ticks} | {minutes}:{secs:D2}";
            }

            // Live event log streaming
            if (_eventLogLive && _isOpen)
            {
                var sim = _simulationRunner.Simulation;
                var entries = sim.EventLog.Entries;
                if (entries.Count > 0)
                {
                    var latest = entries[entries.Count - 1];
                    if (latest.TickNumber == e.TickNumber)
                    {
                        Print($"  <color=#6699CC>[LIVE]</color> T{latest.TickNumber}: {latest.Summary}");
                    }
                }
            }
        }
    }
}
#endif

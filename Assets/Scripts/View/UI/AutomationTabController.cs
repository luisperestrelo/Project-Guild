using UnityEngine.UIElements;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Controller for the runner Automation tab (read-only summary).
    /// Shows what task sequence, macro rules, and micro rules are active
    /// for the selected runner. Three sub-tabs: Task Seq, Macro, Micro.
    ///
    /// This is the "portal" — clicking [Edit in Library] will open the
    /// Automation panel (implemented in Batch C).
    ///
    /// Plain C# class, not MonoBehaviour. Refreshes via Refresh() called
    /// each tick by RunnerDetailsPanelController.
    /// </summary>
    public class AutomationTabController
    {
        private readonly UIManager _uiManager;
        private readonly VisualElement _root;

        // ─── Sub-tab switching ──────────────────────────
        private readonly Button _subTabTaskSeq;
        private readonly Button _subTabMacro;
        private readonly Button _subTabMicro;
        private readonly ScrollView _contentTaskSeq;
        private readonly ScrollView _contentMacro;
        private readonly ScrollView _contentMicro;
        private string _activeSubTab = "taskseq";

        // ─── Task Sequence sub-tab ──────────────────────
        private readonly Label _taskSeqNameLabel;
        private readonly Label _taskSeqUsageLabel;
        private readonly Label _taskSeqLoopValue;
        private readonly Label _suspensionLabel;
        private readonly VisualElement _stepsContainer;
        private readonly VisualElement _pendingSection;
        private readonly Label _pendingLabel;
        private readonly Button _btnClearTask;
        private readonly Button _btnResumeMacros;
        private readonly Button _btnEditTaskSeq;

        // ─── Macro sub-tab ──────────────────────────────
        private readonly Label _macroNameLabel;
        private readonly Label _macroUsageLabel;
        private readonly VisualElement _macroRulesContainer;
        private readonly Button _btnEditMacro;

        // ─── Micro sub-tab ──────────────────────────────
        private readonly Label _microNoTaskLabel;
        private readonly VisualElement _microStepsContainer;

        private string _currentRunnerId;

        public AutomationTabController(VisualElement root, UIManager uiManager)
        {
            _root = root;
            _uiManager = uiManager;

            // Sub-tab buttons
            _subTabTaskSeq = root.Q<Button>("sub-tab-taskseq");
            _subTabMacro = root.Q<Button>("sub-tab-macro");
            _subTabMicro = root.Q<Button>("sub-tab-micro");

            _subTabTaskSeq.clicked += () => SwitchSubTab("taskseq");
            _subTabMacro.clicked += () => SwitchSubTab("macro");
            _subTabMicro.clicked += () => SwitchSubTab("micro");

            // Sub-tab content
            _contentTaskSeq = root.Q<ScrollView>("sub-content-taskseq");
            _contentMacro = root.Q<ScrollView>("sub-content-macro");
            _contentMicro = root.Q<ScrollView>("sub-content-micro");

            // Task Sequence elements
            _taskSeqNameLabel = root.Q<Label>("taskseq-name-label");
            _taskSeqUsageLabel = root.Q<Label>("taskseq-usage-label");
            _taskSeqLoopValue = root.Q<Label>("taskseq-loop-value");
            _suspensionLabel = root.Q<Label>("taskseq-suspension-label");
            _stepsContainer = root.Q("taskseq-steps-container");
            _pendingSection = root.Q("taskseq-pending-section");
            _pendingLabel = root.Q<Label>("taskseq-pending-label");
            _btnClearTask = root.Q<Button>("btn-clear-task");
            _btnResumeMacros = root.Q<Button>("btn-resume-macros");
            _btnEditTaskSeq = root.Q<Button>("btn-edit-taskseq");

            _btnClearTask.clicked += OnClearTaskClicked;
            _btnResumeMacros.clicked += OnResumeMacrosClicked;
            _btnEditTaskSeq.clicked += OnEditTaskSeqClicked;

            // Macro elements
            _macroNameLabel = root.Q<Label>("macro-name-label");
            _macroUsageLabel = root.Q<Label>("macro-usage-label");
            _macroRulesContainer = root.Q("macro-rules-container");
            _btnEditMacro = root.Q<Button>("btn-edit-macro");
            _btnEditMacro.clicked += OnEditMacroClicked;

            // Micro elements
            _microNoTaskLabel = root.Q<Label>("micro-no-task-label");
            _microStepsContainer = root.Q("micro-steps-container");
        }

        public void ShowRunner(string runnerId)
        {
            _currentRunnerId = runnerId;
            Refresh();
        }

        private void SwitchSubTab(string tabName)
        {
            _activeSubTab = tabName;

            SetSubTabActive(_subTabTaskSeq, tabName == "taskseq");
            SetSubTabActive(_subTabMacro, tabName == "macro");
            SetSubTabActive(_subTabMicro, tabName == "micro");

            _contentTaskSeq.style.display = tabName == "taskseq" ? DisplayStyle.Flex : DisplayStyle.None;
            _contentMacro.style.display = tabName == "macro" ? DisplayStyle.Flex : DisplayStyle.None;
            _contentMicro.style.display = tabName == "micro" ? DisplayStyle.Flex : DisplayStyle.None;

            Refresh();
        }

        private static void SetSubTabActive(Button tab, bool active)
        {
            if (active)
                tab.AddToClassList("sub-tab-active");
            else
                tab.RemoveFromClassList("sub-tab-active");
        }

        public void Refresh()
        {
            var sim = _uiManager.Simulation;
            if (sim == null || _currentRunnerId == null) return;

            var runner = sim.FindRunner(_currentRunnerId);
            if (runner == null) return;

            switch (_activeSubTab)
            {
                case "taskseq":
                    RefreshTaskSequenceTab(runner, sim);
                    break;
                case "macro":
                    RefreshMacroTab(runner, sim);
                    break;
                case "micro":
                    RefreshMicroTab(runner, sim);
                    break;
            }
        }

        // ─── Task Sequence Sub-Tab ──────────────────────

        private void RefreshTaskSequenceTab(Runner runner, GameSimulation sim)
        {
            var state = sim.CurrentGameState;
            var seq = sim.GetRunnerTaskSequence(runner);

            if (seq == null)
            {
                _taskSeqNameLabel.text = "No active task";
                _taskSeqUsageLabel.text = "";
                _taskSeqLoopValue.text = "-";
                _stepsContainer.Clear();
                _suspensionLabel.style.display = DisplayStyle.None;
                _pendingSection.style.display = DisplayStyle.None;
                _btnClearTask.style.display = DisplayStyle.None;
                _btnResumeMacros.style.display = DisplayStyle.None;
                _btnEditTaskSeq.style.display = DisplayStyle.None;
                return;
            }

            // Header
            _taskSeqNameLabel.text = seq.Name ?? seq.Id ?? "Task Sequence";
            int usageCount = sim.CountRunnersUsingTaskSequence(seq.Id);
            _taskSeqUsageLabel.text = usageCount > 1
                ? $"Used by {usageCount} runners"
                : "Used by this runner only";

            _taskSeqLoopValue.text = seq.Loop ? "Yes" : "No";

            // Macro suspension indicator
            if (runner.MacroSuspendedUntilLoop)
            {
                _suspensionLabel.text = "Macros paused \u2014 completing first cycle";
                _suspensionLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                _suspensionLabel.style.display = DisplayStyle.None;
            }

            // Step list
            _stepsContainer.Clear();
            if (seq.Steps != null)
            {
                for (int i = 0; i < seq.Steps.Count; i++)
                {
                    var step = seq.Steps[i];
                    bool isCurrent = (i == runner.TaskSequenceCurrentStepIndex);

                    var row = new VisualElement();
                    row.AddToClassList("auto-step-row");
                    if (isCurrent) row.AddToClassList("auto-step-current");

                    // Current step indicator
                    var indicator = new Label(isCurrent ? "\u25b6" : "");
                    indicator.AddToClassList("auto-step-indicator");
                    indicator.pickingMode = PickingMode.Ignore;
                    row.Add(indicator);

                    // Step index
                    var indexLabel = new Label($"{i + 1}.");
                    indexLabel.AddToClassList("auto-step-index");
                    indexLabel.pickingMode = PickingMode.Ignore;
                    row.Add(indexLabel);

                    // Step description
                    string stepText = AutomationUIHelpers.FormatStep(step, state,
                        microId => sim.FindMicroRulesetInLibrary(microId)?.Name);
                    var stepLabel = new Label(stepText);
                    stepLabel.AddToClassList("auto-step-label");
                    stepLabel.pickingMode = PickingMode.Ignore;
                    row.Add(stepLabel);

                    _stepsContainer.Add(row);
                }
            }

            // Pending sequence indicator
            var pending = sim.GetRunnerPendingTaskSequence(runner);
            if (pending != null)
            {
                _pendingSection.style.display = DisplayStyle.Flex;
                _pendingLabel.text = $"Pending: {pending.Name ?? pending.TargetNodeId ?? "sequence"} (after current cycle)";
            }
            else
            {
                _pendingSection.style.display = DisplayStyle.None;
            }

            // Action buttons
            _btnClearTask.style.display = DisplayStyle.Flex;
            _btnResumeMacros.style.display = runner.MacroSuspendedUntilLoop
                ? DisplayStyle.Flex : DisplayStyle.None;
            _btnEditTaskSeq.style.display = DisplayStyle.Flex;
        }

        // ─── Macro Rules Sub-Tab ────────────────────────

        private void RefreshMacroTab(Runner runner, GameSimulation sim)
        {
            var state = sim.CurrentGameState;
            var ruleset = sim.GetRunnerMacroRuleset(runner);

            if (ruleset == null)
            {
                _macroNameLabel.text = "No macro ruleset";
                _macroUsageLabel.text = "";
                _macroRulesContainer.Clear();
                _btnEditMacro.style.display = DisplayStyle.None;
                return;
            }

            // Header
            _macroNameLabel.text = ruleset.Name ?? ruleset.Id ?? "Macro Ruleset";
            int usageCount = sim.CountRunnersUsingMacroRuleset(ruleset.Id);
            _macroUsageLabel.text = usageCount > 1
                ? $"Used by {usageCount} runners"
                : "Used by this runner only";

            // Rule list
            _macroRulesContainer.Clear();
            if (ruleset.Rules.Count == 0)
            {
                var emptyLabel = new Label("No rules (runner will not switch tasks automatically)");
                emptyLabel.AddToClassList("auto-info-value");
                _macroRulesContainer.Add(emptyLabel);
            }
            else
            {
                // Item name resolver for natural language formatting
                AutomationUIHelpers.ItemNameResolver itemResolver = CreateItemResolver(sim);

                for (int i = 0; i < ruleset.Rules.Count; i++)
                {
                    var rule = ruleset.Rules[i];
                    var row = CreateRuleRow(i, rule, state, isMacro: true, itemResolver);
                    _macroRulesContainer.Add(row);
                }
            }

            _btnEditMacro.style.display = DisplayStyle.Flex;
        }

        // ─── Micro Rules Sub-Tab ────────────────────────

        private void RefreshMicroTab(Runner runner, GameSimulation sim)
        {
            var state = sim.CurrentGameState;
            var seq = sim.GetRunnerTaskSequence(runner);

            _microStepsContainer.Clear();

            if (seq == null || seq.Steps == null)
            {
                _microNoTaskLabel.style.display = DisplayStyle.Flex;
                return;
            }

            _microNoTaskLabel.style.display = DisplayStyle.None;

            // Show each Work step with its micro ruleset
            AutomationUIHelpers.ItemNameResolver itemResolver = CreateItemResolver(sim);

            for (int i = 0; i < seq.Steps.Count; i++)
            {
                var step = seq.Steps[i];
                if (step.Type != TaskStepType.Work) continue;

                var section = new VisualElement();
                section.AddToClassList("micro-work-section");

                // Work step header (e.g., "Work at Copper Mine")
                string stepDesc = AutomationUIHelpers.FormatStep(step, state, null);
                string nodeContext = "";
                // Find the TravelTo step that precedes this Work step to show the node
                if (i > 0 && seq.Steps[i - 1].Type == TaskStepType.TravelTo)
                {
                    string nodeId = seq.Steps[i - 1].TargetNodeId;
                    nodeContext = $" at {AutomationUIHelpers.ResolveNodeName(nodeId, state)}";
                }

                var headerLabel = new Label($"Work{nodeContext}");
                headerLabel.AddToClassList("micro-work-header");
                section.Add(headerLabel);

                // Micro ruleset info
                var microRuleset = sim.FindMicroRulesetInLibrary(step.MicroRulesetId);
                if (microRuleset != null)
                {
                    int seqCount = sim.CountSequencesUsingMicroRuleset(microRuleset.Id);
                    string usageText = seqCount > 1
                        ? $"\"{microRuleset.Name}\" (Used by {seqCount} sequences)"
                        : $"\"{microRuleset.Name}\"";
                    var infoLabel = new Label(usageText);
                    infoLabel.AddToClassList("micro-work-ruleset-info");
                    section.Add(infoLabel);

                    // Rule list
                    for (int r = 0; r < microRuleset.Rules.Count; r++)
                    {
                        var rule = microRuleset.Rules[r];
                        var ruleRow = CreateRuleRow(r, rule, state, isMacro: false, itemResolver);
                        section.Add(ruleRow);
                    }

                    if (microRuleset.Rules.Count == 0)
                    {
                        var emptyLabel = new Label("No rules (runner will get stuck)");
                        emptyLabel.AddToClassList("auto-info-value");
                        section.Add(emptyLabel);
                    }
                }
                else
                {
                    var missingLabel = new Label(string.IsNullOrEmpty(step.MicroRulesetId)
                        ? "No micro ruleset assigned (runner will get stuck)"
                        : $"Missing micro ruleset: {step.MicroRulesetId}");
                    missingLabel.AddToClassList("auto-info-value");
                    section.Add(missingLabel);
                }

                // Edit in Library button
                string capturedMicroId = step.MicroRulesetId;
                var editBtn = new Button(() =>
                {
                    if (!string.IsNullOrEmpty(capturedMicroId))
                        _uiManager.OpenAutomationPanelToItemFromRunner("micro", capturedMicroId, _currentRunnerId);
                });
                editBtn.text = "Edit in Library";
                editBtn.AddToClassList("auto-action-button");
                editBtn.AddToClassList("auto-edit-button");
                editBtn.SetEnabled(!string.IsNullOrEmpty(capturedMicroId));
                section.Add(editBtn);

                _microStepsContainer.Add(section);
            }

            // If no Work steps found
            if (_microStepsContainer.childCount == 0)
            {
                var noWorkLabel = new Label("No Work steps in current task sequence");
                noWorkLabel.AddToClassList("auto-info-value");
                _microStepsContainer.Add(noWorkLabel);
            }
        }

        // ─── Shared Helpers ─────────────────────────────

        private VisualElement CreateRuleRow(int index, Rule rule, GameState state,
            bool isMacro, AutomationUIHelpers.ItemNameResolver itemResolver)
        {
            var row = new VisualElement();
            row.AddToClassList("auto-rule-row");
            if (!rule.Enabled) row.AddToClassList("auto-rule-disabled");

            // Index
            var indexLabel = new Label($"{index + 1}.");
            indexLabel.AddToClassList("auto-rule-index");
            indexLabel.pickingMode = PickingMode.Ignore;
            row.Add(indexLabel);

            // Enabled indicator
            var enabledLabel = new Label(rule.Enabled ? "\u2713" : "\u2717");
            enabledLabel.AddToClassList("auto-rule-enabled-indicator");
            enabledLabel.pickingMode = PickingMode.Ignore;
            row.Add(enabledLabel);

            // Rule text
            string ruleText = AutomationUIHelpers.FormatRule(rule, state, itemResolver);
            var textLabel = new Label(ruleText);
            textLabel.AddToClassList("auto-rule-text");
            textLabel.pickingMode = PickingMode.Ignore;
            row.Add(textLabel);

            // Timing tag (macro only)
            if (isMacro)
            {
                string timing = AutomationUIHelpers.FormatTimingTag(rule);
                var timingLabel = new Label($"[{timing}]");
                timingLabel.AddToClassList("auto-rule-timing");
                timingLabel.pickingMode = PickingMode.Ignore;
                row.Add(timingLabel);
            }

            return row;
        }

        private static AutomationUIHelpers.ItemNameResolver CreateItemResolver(GameSimulation sim)
        {
            if (sim.ItemRegistry == null) return null;
            return itemId =>
            {
                var def = sim.ItemRegistry.Get(itemId);
                return def?.Name;
            };
        }

        // ─── Button Handlers ────────────────────────────

        private void OnClearTaskClicked()
        {
            if (_currentRunnerId == null) return;
            _uiManager.Simulation?.ClearTaskSequence(_currentRunnerId);
        }

        private void OnResumeMacrosClicked()
        {
            if (_currentRunnerId == null) return;
            _uiManager.Simulation?.ResumeMacroRules(_currentRunnerId);
        }

        private void OnEditTaskSeqClicked()
        {
            if (_currentRunnerId == null) return;
            var sim = _uiManager.Simulation;
            var runner = sim?.FindRunner(_currentRunnerId);
            if (runner == null) return;
            var seq = sim.GetRunnerTaskSequence(runner);
            if (seq?.Id == null) return;

            _uiManager.OpenAutomationPanelToItemFromRunner("taskseq", seq.Id, _currentRunnerId);
        }

        private void OnEditMacroClicked()
        {
            if (_currentRunnerId == null) return;
            var sim = _uiManager.Simulation;
            var runner = sim?.FindRunner(_currentRunnerId);
            if (runner == null) return;
            var ruleset = sim.GetRunnerMacroRuleset(runner);
            if (ruleset?.Id == null) return;

            _uiManager.OpenAutomationPanelToItemFromRunner("macro", ruleset.Id, _currentRunnerId);
        }
    }
}

using System.Collections.Generic;
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
    /// This is the "portal" — clicking [Edit in Library] opens the
    /// global Automation panel.
    ///
    /// Uses shape-keyed caching: elements are built once when the data shape
    /// (step count, rule count, etc.) is first seen, then updated in-place on
    /// subsequent ticks. Rebuild only happens when the shape changes.
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

        // ─── Task Sequence sub-tab (persistent UXML elements) ──────
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
        private readonly DropdownField _taskSeqAssignDropdown;

        // ─── Task Sequence cached step rows ──────
        private string _cachedTaskSeqShapeKey;
        private readonly List<(VisualElement row, Label indicator, Label index, Label text, Label microLink)> _stepRowCache = new();

        // ─── Task Sequence assign dropdown state ──────
        private readonly List<string> _taskSeqChoiceIds = new();
        private bool _taskSeqAssignSuppressCallback;
        private string _cachedTaskSeqDropdownKey;

        // ─── Macro sub-tab (persistent UXML elements) ──────
        private readonly Label _macroNameLabel;
        private readonly Label _macroUsageLabel;
        private readonly VisualElement _macroRulesContainer;
        private readonly Button _btnEditMacro;
        private readonly DropdownField _macroAssignDropdown;

        // ─── Macro assign dropdown state ──────
        private readonly List<string> _macroChoiceIds = new();
        private bool _macroAssignSuppressCallback;
        private string _cachedMacroDropdownKey;

        // ─── Macro cached rule rows ──────
        private string _cachedMacroShapeKey;
        private readonly List<(VisualElement row, Label index, Label enabled, Label text, Label timing)> _macroRuleRowCache = new();

        // ─── Micro sub-tab (persistent UXML elements) ──────
        private readonly Label _microNoTaskLabel;
        private readonly VisualElement _microStepsContainer;

        // ─── Micro cached sections ──────
        private string _cachedMicroShapeKey;
        private readonly List<MicroWorkSectionCache> _microSectionCache = new();

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

            // Task Sequence assign dropdown
            _taskSeqAssignDropdown = root.Q<DropdownField>("taskseq-assign-dropdown");
            _taskSeqAssignDropdown.RegisterValueChangedCallback(OnTaskSeqAssignChanged);

            // Macro elements
            _macroNameLabel = root.Q<Label>("macro-name-label");
            _macroUsageLabel = root.Q<Label>("macro-usage-label");
            _macroRulesContainer = root.Q("macro-rules-container");
            _btnEditMacro = root.Q<Button>("btn-edit-macro");
            _btnEditMacro.clicked += OnEditMacroClicked;

            // Macro assign dropdown
            _macroAssignDropdown = root.Q<DropdownField>("macro-assign-dropdown");
            _macroAssignDropdown.RegisterValueChangedCallback(OnMacroAssignChanged);

            // Micro elements
            _microNoTaskLabel = root.Q<Label>("micro-no-task-label");
            _microStepsContainer = root.Q("micro-steps-container");
        }

        public void ShowRunner(string runnerId)
        {
            _currentRunnerId = runnerId;
            // Force rebuild on runner change
            _cachedTaskSeqShapeKey = null;
            _cachedMacroShapeKey = null;
            _cachedMicroShapeKey = null;
            _cachedTaskSeqDropdownKey = null;
            _cachedMacroDropdownKey = null;
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

            // Assign dropdown — always populated (this is how the player assigns one)
            PopulateTaskSeqAssignDropdown(runner, sim);

            if (seq == null)
            {
                _taskSeqNameLabel.text = "No active task";
                _taskSeqUsageLabel.text = "";
                _taskSeqLoopValue.text = "-";
                _suspensionLabel.style.display = DisplayStyle.None;
                _pendingSection.style.display = DisplayStyle.None;
                _btnClearTask.style.display = DisplayStyle.None;
                _btnResumeMacros.style.display = DisplayStyle.None;
                _btnEditTaskSeq.style.display = DisplayStyle.None;

                if (_stepRowCache.Count > 0)
                {
                    _stepsContainer.Clear();
                    _stepRowCache.Clear();
                }
                _cachedTaskSeqShapeKey = null;
                return;
            }

            // Header (always update in-place)
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

            // Step list — shape-keyed rebuild
            int stepCount = seq.Steps?.Count ?? 0;
            string shapeKey = $"{seq.Id}|{stepCount}";

            if (shapeKey != _cachedTaskSeqShapeKey)
            {
                // Shape changed — rebuild step rows
                _stepsContainer.Clear();
                _stepRowCache.Clear();
                _cachedTaskSeqShapeKey = shapeKey;

                for (int i = 0; i < stepCount; i++)
                {
                    var step = seq.Steps[i];
                    var row = new VisualElement();
                    row.AddToClassList("auto-step-row");

                    var indicator = new Label();
                    indicator.AddToClassList("auto-step-indicator");
                    indicator.pickingMode = PickingMode.Ignore;
                    row.Add(indicator);

                    var indexLabel = new Label($"{i + 1}.");
                    indexLabel.AddToClassList("auto-step-index");
                    indexLabel.pickingMode = PickingMode.Ignore;
                    row.Add(indexLabel);

                    var stepLabel = new Label();
                    stepLabel.AddToClassList("auto-step-label");
                    stepLabel.pickingMode = PickingMode.Ignore;
                    row.Add(stepLabel);

                    // For Work steps, add a clickable micro ruleset link
                    Label microLink = null;
                    if (step.Type == TaskStepType.Work)
                    {
                        var arrow = new Label("\u2192"); // →
                        arrow.AddToClassList("auto-step-arrow");
                        arrow.pickingMode = PickingMode.Ignore;
                        row.Add(arrow);

                        microLink = new Label();
                        microLink.AddToClassList("auto-step-micro-link");
                        string capturedMicroId = step.MicroRulesetId;
                        microLink.RegisterCallback<ClickEvent>(evt =>
                        {
                            if (evt.clickCount == 2 && !string.IsNullOrEmpty(capturedMicroId))
                                _uiManager.OpenAutomationPanelToItemFromRunner("micro", capturedMicroId, _currentRunnerId);
                        });
                        row.Add(microLink);
                    }

                    _stepsContainer.Add(row);
                    _stepRowCache.Add((row, indicator, indexLabel, stepLabel, microLink));
                }
            }

            // Update step row values in-place
            for (int i = 0; i < _stepRowCache.Count && i < stepCount; i++)
            {
                var step = seq.Steps[i];
                bool isCurrent = (i == runner.TaskSequenceCurrentStepIndex);
                var cached = _stepRowCache[i];

                // Current step indicator and CSS class
                cached.indicator.text = isCurrent ? "\u25b6" : "";
                if (isCurrent)
                    cached.row.AddToClassList("auto-step-current");
                else
                    cached.row.RemoveFromClassList("auto-step-current");

                // Step description
                cached.text.text = AutomationUIHelpers.FormatStep(step, state);

                // Micro ruleset link for Work steps
                if (cached.microLink != null && step.Type == TaskStepType.Work)
                {
                    var microRuleset = sim.FindMicroRulesetInLibrary(step.MicroRulesetId);
                    cached.microLink.text = microRuleset?.Name ?? step.MicroRulesetId ?? "None";
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

            // Assign dropdown — always populated
            PopulateMacroAssignDropdown(runner, sim);

            if (ruleset == null)
            {
                _macroNameLabel.text = "No macro ruleset";
                _macroUsageLabel.text = "";
                _btnEditMacro.style.display = DisplayStyle.None;

                if (_macroRuleRowCache.Count > 0)
                {
                    _macroRulesContainer.Clear();
                    _macroRuleRowCache.Clear();
                }
                _cachedMacroShapeKey = null;
                return;
            }

            // Header (always update in-place)
            _macroNameLabel.text = ruleset.Name ?? ruleset.Id ?? "Macro Ruleset";
            int usageCount = sim.CountRunnersUsingMacroRuleset(ruleset.Id);
            _macroUsageLabel.text = usageCount > 1
                ? $"Used by {usageCount} runners"
                : "Used by this runner only";

            // Rule list — shape-keyed rebuild
            int ruleCount = ruleset.Rules.Count;
            string shapeKey = $"{ruleset.Id}|{ruleCount}";

            if (shapeKey != _cachedMacroShapeKey)
            {
                _macroRulesContainer.Clear();
                _macroRuleRowCache.Clear();
                _cachedMacroShapeKey = shapeKey;

                if (ruleCount == 0)
                {
                    var emptyLabel = new Label("No rules (runner will not switch tasks automatically)");
                    emptyLabel.AddToClassList("auto-info-value");
                    _macroRulesContainer.Add(emptyLabel);
                }
                else
                {
                    for (int i = 0; i < ruleCount; i++)
                    {
                        var row = new VisualElement();
                        row.AddToClassList("auto-rule-row");

                        var indexLabel = new Label();
                        indexLabel.AddToClassList("auto-rule-index");
                        indexLabel.pickingMode = PickingMode.Ignore;
                        row.Add(indexLabel);

                        var enabledLabel = new Label();
                        enabledLabel.AddToClassList("auto-rule-enabled-indicator");
                        enabledLabel.pickingMode = PickingMode.Ignore;
                        row.Add(enabledLabel);

                        var textLabel = new Label();
                        textLabel.AddToClassList("auto-rule-text");
                        textLabel.pickingMode = PickingMode.Ignore;
                        row.Add(textLabel);

                        var timingLabel = new Label();
                        timingLabel.AddToClassList("auto-rule-timing");
                        timingLabel.pickingMode = PickingMode.Ignore;
                        row.Add(timingLabel);

                        _macroRulesContainer.Add(row);
                        _macroRuleRowCache.Add((row, indexLabel, enabledLabel, textLabel, timingLabel));
                    }
                }
            }

            // Update rule row values in-place
            if (ruleCount > 0)
            {
                AutomationUIHelpers.ItemNameResolver itemResolver = CreateItemResolver(sim);
                for (int i = 0; i < _macroRuleRowCache.Count && i < ruleCount; i++)
                {
                    var rule = ruleset.Rules[i];
                    var cached = _macroRuleRowCache[i];

                    cached.index.text = $"{i + 1}.";
                    cached.enabled.text = rule.Enabled ? "\u2713" : "\u2717";
                    cached.text.text = AutomationUIHelpers.FormatRule(rule, state, itemResolver);
                    cached.timing.text = $"[{AutomationUIHelpers.FormatTimingTag(rule)}]";

                    if (rule.Enabled)
                        cached.row.RemoveFromClassList("auto-rule-disabled");
                    else
                        cached.row.AddToClassList("auto-rule-disabled");
                }
            }

            _btnEditMacro.style.display = DisplayStyle.Flex;
        }

        // ─── Micro Rules Sub-Tab ────────────────────────

        private void RefreshMicroTab(Runner runner, GameSimulation sim)
        {
            var state = sim.CurrentGameState;
            var seq = sim.GetRunnerTaskSequence(runner);

            if (seq == null || seq.Steps == null)
            {
                _microNoTaskLabel.style.display = DisplayStyle.Flex;
                if (_microSectionCache.Count > 0)
                {
                    _microStepsContainer.Clear();
                    _microSectionCache.Clear();
                }
                _cachedMicroShapeKey = null;
                return;
            }

            _microNoTaskLabel.style.display = DisplayStyle.None;

            // Build shape key from Work step structure
            string shapeKey = BuildMicroShapeKey(seq, sim);

            if (shapeKey != _cachedMicroShapeKey)
            {
                // Shape changed — rebuild micro sections
                _microStepsContainer.Clear();
                _microSectionCache.Clear();
                _cachedMicroShapeKey = shapeKey;

                for (int i = 0; i < seq.Steps.Count; i++)
                {
                    var step = seq.Steps[i];
                    if (step.Type != TaskStepType.Work) continue;

                    var sectionCache = new MicroWorkSectionCache();

                    var section = new VisualElement();
                    section.AddToClassList("micro-work-section");
                    sectionCache.section = section;

                    // Work step header
                    sectionCache.headerLabel = new Label();
                    sectionCache.headerLabel.AddToClassList("micro-work-header");
                    section.Add(sectionCache.headerLabel);

                    // Micro ruleset info
                    sectionCache.infoLabel = new Label();
                    sectionCache.infoLabel.AddToClassList("micro-work-ruleset-info");
                    section.Add(sectionCache.infoLabel);

                    // Rule rows (built based on current micro ruleset)
                    var microRuleset = sim.FindMicroRulesetInLibrary(step.MicroRulesetId);
                    int ruleCount = microRuleset?.Rules.Count ?? 0;

                    if (ruleCount == 0)
                    {
                        var emptyLabel = new Label();
                        emptyLabel.AddToClassList("auto-info-value");
                        section.Add(emptyLabel);
                        sectionCache.emptyLabel = emptyLabel;
                    }
                    else
                    {
                        for (int r = 0; r < ruleCount; r++)
                        {
                            var row = new VisualElement();
                            row.AddToClassList("auto-rule-row");

                            var indexLabel = new Label();
                            indexLabel.AddToClassList("auto-rule-index");
                            indexLabel.pickingMode = PickingMode.Ignore;
                            row.Add(indexLabel);

                            var enabledLabel = new Label();
                            enabledLabel.AddToClassList("auto-rule-enabled-indicator");
                            enabledLabel.pickingMode = PickingMode.Ignore;
                            row.Add(enabledLabel);

                            var textLabel = new Label();
                            textLabel.AddToClassList("auto-rule-text");
                            textLabel.pickingMode = PickingMode.Ignore;
                            row.Add(textLabel);

                            section.Add(row);
                            sectionCache.ruleRows.Add((row, indexLabel, enabledLabel, textLabel));
                        }
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
                    sectionCache.editButton = editBtn;
                    sectionCache.stepIndex = i;

                    _microStepsContainer.Add(section);
                    _microSectionCache.Add(sectionCache);
                }

                // If no Work steps found
                if (_microSectionCache.Count == 0)
                {
                    var noWorkLabel = new Label("No Work steps in current task sequence");
                    noWorkLabel.AddToClassList("auto-info-value");
                    _microStepsContainer.Add(noWorkLabel);
                }
            }

            // Update micro section values in-place
            AutomationUIHelpers.ItemNameResolver itemResolver = CreateItemResolver(sim);

            foreach (var sectionCache in _microSectionCache)
            {
                int i = sectionCache.stepIndex;
                if (i >= seq.Steps.Count) continue;

                var step = seq.Steps[i];

                // Header
                string nodeContext = "";
                if (i > 0 && seq.Steps[i - 1].Type == TaskStepType.TravelTo)
                {
                    string nodeId = seq.Steps[i - 1].TargetNodeId;
                    nodeContext = $" at {AutomationUIHelpers.ResolveNodeName(nodeId, state)}";
                }
                sectionCache.headerLabel.text = $"Work{nodeContext}";

                // Micro ruleset info
                var microRuleset = sim.FindMicroRulesetInLibrary(step.MicroRulesetId);
                if (microRuleset != null)
                {
                    int seqCount = sim.CountSequencesUsingMicroRuleset(microRuleset.Id);
                    string usageText = seqCount > 1
                        ? $"\"{microRuleset.Name}\" (Used by {seqCount} sequences)"
                        : $"\"{microRuleset.Name}\"";
                    sectionCache.infoLabel.text = usageText;
                    sectionCache.infoLabel.style.display = DisplayStyle.Flex;

                    // Update rule rows
                    for (int r = 0; r < sectionCache.ruleRows.Count && r < microRuleset.Rules.Count; r++)
                    {
                        var rule = microRuleset.Rules[r];
                        var cached = sectionCache.ruleRows[r];

                        cached.index.text = $"{r + 1}.";
                        cached.enabled.text = rule.Enabled ? "\u2713" : "\u2717";
                        cached.text.text = AutomationUIHelpers.FormatRule(rule, state, itemResolver);

                        if (rule.Enabled)
                            cached.row.RemoveFromClassList("auto-rule-disabled");
                        else
                            cached.row.AddToClassList("auto-rule-disabled");
                    }

                    if (sectionCache.emptyLabel != null)
                    {
                        sectionCache.emptyLabel.text = microRuleset.Rules.Count == 0
                            ? "No rules (runner will get stuck)" : "";
                        sectionCache.emptyLabel.style.display = microRuleset.Rules.Count == 0
                            ? DisplayStyle.Flex : DisplayStyle.None;
                    }
                }
                else
                {
                    sectionCache.infoLabel.text = string.IsNullOrEmpty(step.MicroRulesetId)
                        ? "No micro ruleset assigned (runner will get stuck)"
                        : $"Missing micro ruleset: {step.MicroRulesetId}";
                    sectionCache.infoLabel.style.display = DisplayStyle.Flex;
                }
            }
        }

        private static string BuildMicroShapeKey(TaskSequence seq, GameSimulation sim)
        {
            // Shape = seq ID + work step indices + micro IDs + rule counts
            var parts = new List<string> { seq.Id ?? "" };
            for (int i = 0; i < seq.Steps.Count; i++)
            {
                var step = seq.Steps[i];
                if (step.Type != TaskStepType.Work) continue;

                string microId = step.MicroRulesetId ?? "";
                var microRuleset = sim.FindMicroRulesetInLibrary(step.MicroRulesetId);
                int ruleCount = microRuleset?.Rules.Count ?? 0;
                parts.Add($"{i}:{microId}:{ruleCount}");
            }
            return string.Join("|", parts);
        }

        // ─── Micro Section Cache ─────────────────────────

        private class MicroWorkSectionCache
        {
            public VisualElement section;
            public Label headerLabel;
            public Label infoLabel;
            public Label emptyLabel;
            public Button editButton;
            public int stepIndex;
            public readonly List<(VisualElement row, Label index, Label enabled, Label text)> ruleRows = new();
        }

        // ─── Assign Dropdown Helpers ──────────────────────

        private void PopulateTaskSeqAssignDropdown(Runner runner, GameSimulation sim)
        {
            // Shape-key: library count + runner's current assignment
            // Only rebuild choices when the library or assignment changes
            int libCount = sim.CurrentGameState.TaskSequenceLibrary?.Count ?? 0;
            string dropdownKey = $"{libCount}|{runner.TaskSequenceId}";

            if (dropdownKey != _cachedTaskSeqDropdownKey)
            {
                _cachedTaskSeqDropdownKey = dropdownKey;

                var choices = new List<string> { "(None)" };
                _taskSeqChoiceIds.Clear();
                _taskSeqChoiceIds.Add(null);

                foreach (var seq in sim.CurrentGameState.TaskSequenceLibrary)
                {
                    choices.Add(seq.Name ?? seq.Id ?? "Unnamed");
                    _taskSeqChoiceIds.Add(seq.Id);
                }
                _taskSeqAssignDropdown.choices = choices;

                // Set current value without triggering callback
                int currentIdx = runner.TaskSequenceId != null
                    ? _taskSeqChoiceIds.IndexOf(runner.TaskSequenceId)
                    : 0;
                if (currentIdx < 0) currentIdx = 0;
                _taskSeqAssignSuppressCallback = true;
                _taskSeqAssignDropdown.SetValueWithoutNotify(choices[currentIdx]);
                _taskSeqAssignSuppressCallback = false;
            }
        }

        private void PopulateMacroAssignDropdown(Runner runner, GameSimulation sim)
        {
            // Shape-key: library count + runner's current assignment
            // Only rebuild choices when the library or assignment changes
            int libCount = sim.CurrentGameState.MacroRulesetLibrary?.Count ?? 0;
            string dropdownKey = $"{libCount}|{runner.MacroRulesetId}";

            if (dropdownKey != _cachedMacroDropdownKey)
            {
                _cachedMacroDropdownKey = dropdownKey;

                var choices = new List<string> { "(None)" };
                _macroChoiceIds.Clear();
                _macroChoiceIds.Add(null);

                foreach (var ruleset in sim.CurrentGameState.MacroRulesetLibrary)
                {
                    choices.Add(ruleset.Name ?? ruleset.Id ?? "Unnamed");
                    _macroChoiceIds.Add(ruleset.Id);
                }
                _macroAssignDropdown.choices = choices;

                int currentIdx = runner.MacroRulesetId != null
                    ? _macroChoiceIds.IndexOf(runner.MacroRulesetId)
                    : 0;
                if (currentIdx < 0) currentIdx = 0;
                _macroAssignSuppressCallback = true;
                _macroAssignDropdown.SetValueWithoutNotify(choices[currentIdx]);
                _macroAssignSuppressCallback = false;
            }
        }

        private void OnTaskSeqAssignChanged(ChangeEvent<string> evt)
        {
            if (_taskSeqAssignSuppressCallback || _currentRunnerId == null) return;
            int idx = _taskSeqAssignDropdown.index;
            if (idx < 0 || idx >= _taskSeqChoiceIds.Count) return;

            string seqId = _taskSeqChoiceIds[idx];
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            if (seqId == null)
                sim.ClearTaskSequence(_currentRunnerId);
            else
                sim.CommandAssignTaskSequenceToRunner(_currentRunnerId, seqId);
        }

        private void OnMacroAssignChanged(ChangeEvent<string> evt)
        {
            if (_macroAssignSuppressCallback || _currentRunnerId == null) return;
            int idx = _macroAssignDropdown.index;
            if (idx < 0 || idx >= _macroChoiceIds.Count) return;

            string rulesetId = _macroChoiceIds[idx];
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            sim.CommandAssignMacroRulesetToRunner(_currentRunnerId, rulesetId);
        }

        // ─── Shared Helpers ─────────────────────────────

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

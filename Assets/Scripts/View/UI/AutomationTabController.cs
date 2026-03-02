using System;
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
        private readonly Label _suspensionLabel;
        private readonly VisualElement _stepsContainer;
        private readonly VisualElement _pendingSection;
        private readonly Label _pendingLabel;
        private readonly Button _btnEditTaskSeq;
        private readonly Button _btnChangeTaskSeq;
        private readonly Button _btnNewTaskSeq;
        private readonly Button _btnCopyTaskSeq;
        private readonly Button _btnClearTask;
        private readonly Button _btnResumeMacros;

        // ─── Task Sequence cached step rows ──────
        private string _cachedTaskSeqShapeKey;
        private readonly List<(VisualElement row, Label indicator, Label index, Label text, Label microLink)> _stepRowCache = new();
        private VisualElement _overrideRow;
        private Button _btnForkWithOverrides;
        private Button _btnClearAllOverrides;

        // ─── Macro sub-tab (persistent UXML elements) ──────
        private readonly Label _macroNameLabel;
        private readonly Label _macroUsageLabel;
        private readonly VisualElement _macroRulesContainer;
        private readonly Button _btnEditMacro;
        private readonly Button _btnChangeMacro;
        private readonly Button _btnNewMacro;
        private readonly Button _btnCopyMacro;
        private readonly Button _btnClearMacro;

        // ─── Macro cached rule rows ──────
        private string _cachedMacroShapeKey;
        private readonly List<(VisualElement row, Label index, Label enabled, Label text, Label timing)> _macroRuleRowCache = new();

        // ─── Micro sub-tab (persistent UXML elements) ──────
        private readonly Label _microNoTaskLabel;
        private readonly VisualElement _microStepsContainer;

        // ─── Micro cached sections ──────
        private string _cachedMicroShapeKey;
        private readonly List<MicroWorkSectionCache> _microSectionCache = new();

        // ─── Micro tab-level override row ──────
        private VisualElement _microOverrideRow;
        private Button _btnMicroForkWithOverrides;
        private Button _btnMicroClearAllOverrides;

        private string _currentRunnerId;

        // ─── Copy Setup / Copy From buttons ──────
        private readonly Button _btnCopySetup;
        private readonly Button _btnCopyFrom;

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
            _suspensionLabel = root.Q<Label>("taskseq-suspension-label");
            _stepsContainer = root.Q("taskseq-steps-container");
            _pendingSection = root.Q("taskseq-pending-section");
            _pendingLabel = root.Q<Label>("taskseq-pending-label");

            // Task Sequence action buttons
            _btnEditTaskSeq = root.Q<Button>("btn-edit-taskseq");
            _btnChangeTaskSeq = root.Q<Button>("btn-change-taskseq");
            _btnNewTaskSeq = root.Q<Button>("btn-new-taskseq");
            _btnCopyTaskSeq = root.Q<Button>("btn-copy-taskseq");
            _btnClearTask = root.Q<Button>("btn-clear-task");
            _btnResumeMacros = root.Q<Button>("btn-resume-macros");

            _btnEditTaskSeq.clicked += OnEditTaskSeqClicked;
            _btnChangeTaskSeq.clicked += OnChangeTaskSeqClicked;
            _btnNewTaskSeq.clicked += OnNewTaskSeqClicked;
            _btnCopyTaskSeq.clicked += OnCopyTaskSeqClicked;
            _btnClearTask.clicked += OnClearTaskClicked;
            _btnResumeMacros.clicked += OnResumeMacrosClicked;

            // Override action buttons (dynamically shown)
            _overrideRow = new VisualElement();
            _overrideRow.AddToClassList("auto-override-row");
            _overrideRow.style.display = DisplayStyle.None; // hidden until overrides exist

            _btnForkWithOverrides = new Button(() => ShowForkPopup(_overrideRow, _contentTaskSeq));
            _btnForkWithOverrides.text = "Save as new Task Sequence";
            _btnForkWithOverrides.AddToClassList("auto-action-button");
            _btnForkWithOverrides.AddToClassList("auto-edit-button");
            _overrideRow.Add(_btnForkWithOverrides);

            _btnClearAllOverrides = new Button(OnClearAllOverridesClicked);
            _btnClearAllOverrides.text = "Clear All Overrides";
            _btnClearAllOverrides.AddToClassList("auto-action-button");
            _btnClearAllOverrides.AddToClassList("auto-clear-button");
            _overrideRow.Add(_btnClearAllOverrides);

            _contentTaskSeq.Add(_overrideRow);

            // Macro elements
            _macroNameLabel = root.Q<Label>("macro-name-label");
            _macroUsageLabel = root.Q<Label>("macro-usage-label");
            _macroRulesContainer = root.Q("macro-rules-container");

            // Macro action buttons
            _btnEditMacro = root.Q<Button>("btn-edit-macro");
            _btnChangeMacro = root.Q<Button>("btn-change-macro");
            _btnNewMacro = root.Q<Button>("btn-new-macro");
            _btnCopyMacro = root.Q<Button>("btn-copy-macro");
            _btnClearMacro = root.Q<Button>("btn-clear-macro");

            _btnEditMacro.clicked += OnEditMacroClicked;
            _btnChangeMacro.clicked += OnChangeMacroClicked;
            _btnNewMacro.clicked += OnNewMacroClicked;
            _btnCopyMacro.clicked += OnCopyMacroClicked;
            _btnClearMacro.clicked += OnClearMacroClicked;

            // Micro elements
            _microNoTaskLabel = root.Q<Label>("micro-no-task-label");
            _microStepsContainer = root.Q("micro-steps-container");

            // Micro tab-level override row (dynamically shown)
            _microOverrideRow = new VisualElement();
            _microOverrideRow.AddToClassList("auto-override-row");
            _microOverrideRow.style.display = DisplayStyle.None;

            _btnMicroForkWithOverrides = new Button(() => ShowForkPopup(_microOverrideRow, _contentMicro));
            _btnMicroForkWithOverrides.text = "Save as new Task Sequence";
            _btnMicroForkWithOverrides.AddToClassList("auto-action-button");
            _btnMicroForkWithOverrides.AddToClassList("auto-edit-button");
            _microOverrideRow.Add(_btnMicroForkWithOverrides);

            _btnMicroClearAllOverrides = new Button(OnClearAllOverridesClicked);
            _btnMicroClearAllOverrides.text = "Clear All Overrides";
            _btnMicroClearAllOverrides.AddToClassList("auto-action-button");
            _btnMicroClearAllOverrides.AddToClassList("auto-clear-button");
            _microOverrideRow.Add(_btnMicroClearAllOverrides);

            _contentMicro.Add(_microOverrideRow);

            // Copy Setup / Copy from... buttons (below sub-tab bar)
            var copyRow = new VisualElement();
            copyRow.AddToClassList("auto-copy-row");

            _btnCopySetup = new Button(OnCopySetupClicked);
            _btnCopySetup.text = "Copy Setup To\u2026";
            _btnCopySetup.AddToClassList("auto-action-button");
            _btnCopySetup.AddToClassList("auto-copy-button");
            copyRow.Add(_btnCopySetup);

            _btnCopyFrom = new Button(OnCopyFromClicked);
            _btnCopyFrom.text = "Copy From\u2026";
            _btnCopyFrom.AddToClassList("auto-action-button");
            _btnCopyFrom.AddToClassList("auto-copy-button");
            copyRow.Add(_btnCopyFrom);

            root.Add(copyRow);
        }

        public void ShowRunner(string runnerId)
        {
            _currentRunnerId = runnerId;
            // Force rebuild on runner change
            _cachedTaskSeqShapeKey = null;
            _cachedMacroShapeKey = null;
            _cachedMicroShapeKey = null;
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

            // Copy Setup visible only when runner has something to copy
            bool hasSetup = !string.IsNullOrEmpty(runner.TaskSequenceId) || !string.IsNullOrEmpty(runner.MacroRulesetId);
            _btnCopySetup.SetEnabled(hasSetup);

            // Config warning on Macro sub-tab button (reads stored state, not computed per-tick)
            bool hasBrokenMacro = !string.IsNullOrEmpty(runner.MacroConfigWarning);
            _subTabMacro.text = hasBrokenMacro ? "Macro \u26A0" : "Macro";
            _subTabMacro.enableRichText = true;

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
            bool hasSeq = seq != null;

            if (!hasSeq)
            {
                _taskSeqNameLabel.text = "No active task";
                _taskSeqUsageLabel.text = "";
                _suspensionLabel.style.display = DisplayStyle.None;
                _pendingSection.style.display = DisplayStyle.None;

                if (_stepRowCache.Count > 0)
                {
                    _stepsContainer.Clear();
                    _stepRowCache.Clear();
                }
                _cachedTaskSeqShapeKey = null;
            }

            // Button visibility: EDIT, COPY, CLEAR only when something is assigned
            _btnEditTaskSeq.style.display = hasSeq ? DisplayStyle.Flex : DisplayStyle.None;
            _btnCopyTaskSeq.style.display = hasSeq ? DisplayStyle.Flex : DisplayStyle.None;
            _btnClearTask.style.display = hasSeq ? DisplayStyle.Flex : DisplayStyle.None;
            _btnResumeMacros.style.display = hasSeq && runner.MacroSuspendedUntilLoop
                ? DisplayStyle.Flex : DisplayStyle.None;
            // CHANGE and + NEW always visible
            _btnChangeTaskSeq.style.display = DisplayStyle.Flex;
            _btnNewTaskSeq.style.display = DisplayStyle.Flex;

            // Override row: only when sequence assigned AND overrides exist
            bool hasOverrides = hasSeq && sim.RunnerHasMicroOverrides(runner);
            _overrideRow.style.display = hasOverrides ? DisplayStyle.Flex : DisplayStyle.None;

            if (!hasSeq) return;

            // Header (always update in-place) — loop status inline with name
            string seqName = seq.Name ?? seq.Id ?? "Task Sequence";
            string loopTag = seq.Loop ? "Looping" : "Once";
            _taskSeqNameLabel.text = $"{seqName} <color=#8CB48C>({loopTag})</color>";
            _taskSeqNameLabel.enableRichText = true;
            int usageCount = sim.CountRunnersUsingTaskSequence(seq.Id);
            _taskSeqUsageLabel.text = usageCount > 1
                ? $"Used by {usageCount} runners"
                : "Used by this runner only";

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
                        microLink.enableRichText = true;
                        int capturedStepIndex = i;
                        microLink.RegisterCallback<ClickEvent>(evt =>
                        {
                            if (evt.clickCount == 2)
                            {
                                // Double-click: open in library editor
                                string effectiveMicroId = sim.GetRunnerMicroOverrideForStep(
                                    sim.FindRunner(_currentRunnerId), capturedStepIndex)
                                    ?? seq.Steps[capturedStepIndex].MicroRulesetId;
                                if (!string.IsNullOrEmpty(effectiveMicroId))
                                    _uiManager.OpenAutomationPanelToItemFromRunner("micro", effectiveMicroId, _currentRunnerId);
                            }
                            else if (evt.clickCount == 1)
                            {
                                // Single-click: open micro swap picker
                                ShowMicroSwapPicker(microLink, capturedStepIndex);
                            }
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

                // Micro ruleset link for Work steps (shows override indicator)
                if (cached.microLink != null && step.Type == TaskStepType.Work)
                {
                    string overrideId = sim.GetRunnerMicroOverrideForStep(runner, i);
                    if (overrideId != null)
                    {
                        var overrideMicro = sim.FindMicroRulesetInLibrary(overrideId);
                        var originalMicro = sim.FindMicroRulesetInLibrary(step.MicroRulesetId);
                        string overrideName = overrideMicro?.Name ?? "Unknown";
                        string originalName = originalMicro?.Name ?? step.MicroRulesetId ?? "None";
                        cached.microLink.text = $"{overrideName} <color=#888>(was: {originalName})</color>";
                        cached.microLink.AddToClassList("micro-override-active");
                    }
                    else
                    {
                        var microRuleset = sim.FindMicroRulesetInLibrary(step.MicroRulesetId);
                        cached.microLink.text = microRuleset?.Name ?? step.MicroRulesetId ?? "None";
                        cached.microLink.RemoveFromClassList("micro-override-active");
                    }
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

        }

        // ─── Macro Rules Sub-Tab ────────────────────────

        private void RefreshMacroTab(Runner runner, GameSimulation sim)
        {
            var state = sim.CurrentGameState;
            var ruleset = sim.GetRunnerMacroRuleset(runner);
            bool hasRuleset = ruleset != null;

            if (!hasRuleset)
            {
                _macroNameLabel.text = "No macro ruleset";
                _macroUsageLabel.text = "";

                if (_macroRuleRowCache.Count > 0)
                {
                    _macroRulesContainer.Clear();
                    _macroRuleRowCache.Clear();
                }
                _cachedMacroShapeKey = null;
            }

            // Button visibility: EDIT, COPY, CLEAR only when something is assigned
            _btnEditMacro.style.display = hasRuleset ? DisplayStyle.Flex : DisplayStyle.None;
            _btnCopyMacro.style.display = hasRuleset ? DisplayStyle.Flex : DisplayStyle.None;
            _btnClearMacro.style.display = hasRuleset ? DisplayStyle.Flex : DisplayStyle.None;
            // CHANGE and + NEW always visible
            _btnChangeMacro.style.display = DisplayStyle.Flex;
            _btnNewMacro.style.display = DisplayStyle.Flex;

            if (!hasRuleset) return;

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
                        textLabel.enableRichText = true;
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
                _microOverrideRow.style.display = DisplayStyle.None;
                return;
            }

            _microNoTaskLabel.style.display = DisplayStyle.None;

            // Build shape key from Work step structure (includes override state)
            string shapeKey = BuildMicroShapeKey(seq, sim, runner);

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
                    sectionCache.stepIndex = i;

                    var section = new VisualElement();
                    section.AddToClassList("micro-work-section");
                    sectionCache.section = section;

                    // Work step header (gold bold)
                    sectionCache.headerLabel = new Label();
                    sectionCache.headerLabel.AddToClassList("micro-work-header");
                    section.Add(sectionCache.headerLabel);

                    // Micro ruleset name (bold white)
                    sectionCache.nameLabel = new Label();
                    sectionCache.nameLabel.AddToClassList("auto-name-label");
                    section.Add(sectionCache.nameLabel);

                    // Usage label (grey)
                    sectionCache.usageLabel = new Label();
                    sectionCache.usageLabel.AddToClassList("auto-usage-label");
                    section.Add(sectionCache.usageLabel);

                    // Override label (gold italic, hidden by default)
                    sectionCache.overrideLabel = new Label();
                    sectionCache.overrideLabel.AddToClassList("micro-work-override-label");
                    sectionCache.overrideLabel.style.display = DisplayStyle.None;
                    section.Add(sectionCache.overrideLabel);

                    // Rule rows (built based on EFFECTIVE micro — override or step's)
                    string overrideId = sim.GetRunnerMicroOverrideForStep(runner, i);
                    string effectiveMicroId = overrideId ?? step.MicroRulesetId;
                    var microRuleset = sim.FindMicroRulesetInLibrary(effectiveMicroId);
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
                            textLabel.enableRichText = true;
                            row.Add(textLabel);

                            section.Add(row);
                            sectionCache.ruleRows.Add((row, indexLabel, enabledLabel, textLabel));
                        }
                    }

                    // Actions row with 4 buttons
                    var actionsRow = new VisualElement();
                    actionsRow.AddToClassList("auto-actions-row");
                    sectionCache.actionsRow = actionsRow;

                    int capturedStepIndex = i;

                    // Swap button
                    var swapBtn = new Button(() => ShowMicroSwapPicker(sectionCache.actionsRow, capturedStepIndex, _contentMicro));
                    swapBtn.text = "Swap";
                    swapBtn.AddToClassList("auto-action-button");
                    swapBtn.AddToClassList("auto-change-button");
                    actionsRow.Add(swapBtn);
                    sectionCache.swapButton = swapBtn;

                    // Edit button
                    var editBtn = new Button(() =>
                    {
                        string eid = ResolveEffectiveMicroId(sim, runner, capturedStepIndex);
                        if (!string.IsNullOrEmpty(eid))
                            _uiManager.OpenAutomationPanelToItemFromRunner("micro", eid, _currentRunnerId);
                    });
                    editBtn.text = "Edit";
                    editBtn.AddToClassList("auto-action-button");
                    editBtn.AddToClassList("auto-edit-button");
                    actionsRow.Add(editBtn);
                    sectionCache.editButton = editBtn;

                    // Duplicate & Override button
                    var dupBtn = new Button(() => OnDuplicateAndOverrideClicked(capturedStepIndex));
                    dupBtn.text = "Duplicate & Override";
                    dupBtn.AddToClassList("auto-action-button");
                    dupBtn.AddToClassList("auto-duplicate-button");
                    actionsRow.Add(dupBtn);
                    sectionCache.duplicateOverrideButton = dupBtn;

                    // Clear Override button (hidden when no override)
                    var clearBtn = new Button(() =>
                    {
                        sim.CommandClearMicroOverride(_currentRunnerId, capturedStepIndex);
                        _cachedMicroShapeKey = null;
                        Refresh();
                    });
                    clearBtn.text = "Clear Override";
                    clearBtn.AddToClassList("auto-action-button");
                    clearBtn.AddToClassList("auto-clear-button");
                    actionsRow.Add(clearBtn);
                    sectionCache.clearOverrideButton = clearBtn;

                    section.Add(actionsRow);

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
            bool anyOverrides = false;

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

                // Micro ruleset info (resolve effective micro — override or step's)
                string overrideId = sim.GetRunnerMicroOverrideForStep(runner, i);
                string effectiveMicroId = overrideId ?? step.MicroRulesetId;
                var microRuleset = sim.FindMicroRulesetInLibrary(effectiveMicroId);
                bool hasOverride = overrideId != null;
                if (hasOverride) anyOverrides = true;

                if (microRuleset != null)
                {
                    // Name label (bold white)
                    sectionCache.nameLabel.text = microRuleset.Name ?? microRuleset.Id ?? "Micro Ruleset";
                    sectionCache.nameLabel.style.display = DisplayStyle.Flex;

                    // Usage label (grey)
                    int seqCount = sim.CountSequencesUsingMicroRuleset(microRuleset.Id);
                    sectionCache.usageLabel.text = seqCount > 1
                        ? $"Used by {seqCount} sequences"
                        : "Used by 1 sequence";
                    sectionCache.usageLabel.style.display = DisplayStyle.Flex;

                    // Override label
                    if (hasOverride)
                    {
                        var originalMicro = sim.FindMicroRulesetInLibrary(step.MicroRulesetId);
                        string originalName = originalMicro?.Name ?? step.MicroRulesetId ?? "None";
                        sectionCache.overrideLabel.text = $"(overriding: {originalName})";
                        sectionCache.overrideLabel.style.display = DisplayStyle.Flex;
                        sectionCache.section.AddToClassList("micro-override-active");
                    }
                    else
                    {
                        sectionCache.overrideLabel.style.display = DisplayStyle.None;
                        sectionCache.section.RemoveFromClassList("micro-override-active");
                    }

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

                    // Button state
                    sectionCache.editButton.SetEnabled(true);
                    sectionCache.duplicateOverrideButton.SetEnabled(true);
                }
                else
                {
                    sectionCache.nameLabel.text = string.IsNullOrEmpty(effectiveMicroId)
                        ? "No micro ruleset assigned"
                        : $"Missing: {effectiveMicroId}";
                    sectionCache.nameLabel.style.display = DisplayStyle.Flex;
                    sectionCache.usageLabel.text = string.IsNullOrEmpty(effectiveMicroId)
                        ? "Runner will get stuck" : "";
                    sectionCache.usageLabel.style.display = DisplayStyle.Flex;
                    sectionCache.overrideLabel.style.display = DisplayStyle.None;
                    sectionCache.section.RemoveFromClassList("micro-override-active");
                    sectionCache.editButton.SetEnabled(false);
                    sectionCache.duplicateOverrideButton.SetEnabled(false);
                }

                // Clear Override button: only visible when override exists for this step
                sectionCache.clearOverrideButton.style.display = hasOverride ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Tab-level override row: visible when ANY overrides exist
            _microOverrideRow.style.display = anyOverrides ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static string BuildMicroShapeKey(TaskSequence seq, GameSimulation sim, Runner runner)
        {
            // Shape = seq ID + work step indices + effective micro IDs + rule counts
            var parts = new List<string> { seq.Id ?? "" };
            for (int i = 0; i < seq.Steps.Count; i++)
            {
                var step = seq.Steps[i];
                if (step.Type != TaskStepType.Work) continue;

                // Use effective micro (override or step's) for shape key
                string overrideId = sim.GetRunnerMicroOverrideForStep(runner, i);
                string effectiveId = overrideId ?? step.MicroRulesetId ?? "";
                var microRuleset = sim.FindMicroRulesetInLibrary(effectiveId);
                int ruleCount = microRuleset?.Rules.Count ?? 0;
                string overrideFlag = overrideId != null ? "O" : "";
                parts.Add($"{i}:{effectiveId}:{ruleCount}:{overrideFlag}");
            }
            return string.Join("|", parts);
        }

        // ─── Micro Section Cache ─────────────────────────

        private class MicroWorkSectionCache
        {
            public VisualElement section;
            public Label headerLabel;
            public Label nameLabel;
            public Label usageLabel;
            public Label overrideLabel;
            public Label emptyLabel;
            public VisualElement actionsRow;
            public Button swapButton;
            public Button editButton;
            public Button duplicateOverrideButton;
            public Button clearOverrideButton;
            public int stepIndex;
            public readonly List<(VisualElement row, Label index, Label enabled, Label text)> ruleRows = new();
        }

        // ─── Change Handlers ──────────────────────────────

        private void OnChangeTaskSeqClicked()
        {
            if (_currentRunnerId == null) return;
            _uiManager.OpenAutomationPanelForChangeAssignment("taskseq", _currentRunnerId);
        }

        private void OnChangeMacroClicked()
        {
            if (_currentRunnerId == null) return;
            _uiManager.OpenAutomationPanelForChangeAssignment("macro", _currentRunnerId);
        }

        // ─── Copy Handlers ──────────────────────────────

        private void OnCopyTaskSeqClicked()
        {
            if (_currentRunnerId == null) return;
            var sim = _uiManager.Simulation;
            var runner = sim?.FindRunner(_currentRunnerId);
            if (runner == null || string.IsNullOrEmpty(runner.TaskSequenceId)) return;

            string cloneId = sim.CommandCloneTaskSequence(runner.TaskSequenceId);
            if (cloneId == null) return;

            _uiManager.OpenAutomationPanelToItemForNewAssignment("taskseq", cloneId, _currentRunnerId);
        }

        private void OnCopyMacroClicked()
        {
            if (_currentRunnerId == null) return;
            var sim = _uiManager.Simulation;
            var runner = sim?.FindRunner(_currentRunnerId);
            if (runner == null || string.IsNullOrEmpty(runner.MacroRulesetId)) return;

            string cloneId = sim.CommandCloneMacroRuleset(runner.MacroRulesetId);
            if (cloneId == null) return;

            _uiManager.OpenAutomationPanelToItemForNewAssignment("macro", cloneId, _currentRunnerId);
        }

        // ─── Clear Handlers ──────────────────────────────

        private void OnClearMacroClicked()
        {
            if (_currentRunnerId == null) return;
            _uiManager.Simulation?.CommandAssignMacroRulesetToRunner(_currentRunnerId, null);
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

        private void OnNewTaskSeqClicked()
        {
            if (_currentRunnerId == null) return;
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            string id = sim.CommandCreateTaskSequence();
            _uiManager.OpenAutomationPanelToItemForNewAssignment("taskseq", id, _currentRunnerId);
        }

        private void OnNewMacroClicked()
        {
            if (_currentRunnerId == null) return;
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            string id = sim.CommandCreateMacroRuleset();
            _uiManager.OpenAutomationPanelToItemForNewAssignment("macro", id, _currentRunnerId);
        }

        // ─── Copy Setup / Copy From ─────────────────────

        private void OnCopySetupClicked()
        {
            if (_currentRunnerId == null) return;
            var sim = _uiManager.Simulation;
            if (sim == null) return;
            var sourceRunner = sim.FindRunner(_currentRunnerId);
            if (sourceRunner == null) return;

            // Need something to copy
            if (string.IsNullOrEmpty(sourceRunner.TaskSequenceId) && string.IsNullOrEmpty(sourceRunner.MacroRulesetId))
                return;

            ShowRunnerPicker(_btnCopySetup, $"Copy {sourceRunner.Name}'s setup to:", runner =>
            {
                if (!string.IsNullOrEmpty(sourceRunner.TaskSequenceId))
                    sim.CommandAssignTaskSequenceToRunner(runner.Id, sourceRunner.TaskSequenceId);
                if (!string.IsNullOrEmpty(sourceRunner.MacroRulesetId))
                    sim.CommandAssignMacroRulesetToRunner(runner.Id, sourceRunner.MacroRulesetId);
            }, excludeRunnerId: _currentRunnerId);
        }

        private void OnCopyFromClicked()
        {
            if (_currentRunnerId == null) return;
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            ShowRunnerPicker(_btnCopyFrom, "Copy setup from:", runner =>
            {
                if (!string.IsNullOrEmpty(runner.TaskSequenceId))
                    sim.CommandAssignTaskSequenceToRunner(_currentRunnerId, runner.TaskSequenceId);
                if (!string.IsNullOrEmpty(runner.MacroRulesetId))
                    sim.CommandAssignMacroRulesetToRunner(_currentRunnerId, runner.MacroRulesetId);
            }, excludeRunnerId: _currentRunnerId);
        }

        private void ShowRunnerPicker(Button anchor, string headerText,
            Action<Simulation.Core.Runner> onPick, string excludeRunnerId = null)
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            // Toggle: remove if already open
            var copyRow = anchor.parent;
            var existing = copyRow?.parent?.Q("copy-runner-popup");
            if (existing != null) { existing.RemoveFromHierarchy(); return; }

            var popup = new VisualElement();
            popup.name = "copy-runner-popup";
            popup.AddToClassList("assign-popup");

            var header = new Label(headerText);
            header.AddToClassList("assign-popup-header");
            popup.Add(header);

            foreach (var runner in sim.CurrentGameState.Runners)
            {
                if (runner.Id == excludeRunnerId) continue;

                var capturedRunner = runner;
                var btn = new Button(() =>
                {
                    onPick(capturedRunner);
                    popup.RemoveFromHierarchy();
                    Refresh();
                });
                btn.text = runner.Name;
                btn.AddToClassList("assign-popup-runner");
                popup.Add(btn);
            }

            var cancelBtn = new Button(() => popup.RemoveFromHierarchy());
            cancelBtn.text = "Cancel";
            cancelBtn.AddToClassList("assign-popup-cancel");
            popup.Add(cancelBtn);

            // Insert after the copy row in the tab root (not inside the row)
            var tabRoot = copyRow.parent;
            int rowIdx = tabRoot.IndexOf(copyRow);
            tabRoot.Insert(rowIdx + 1, popup);
        }

        // ─── Micro Override Handlers ─────────────────────

        /// <summary>
        /// Shows a fork popup to save overrides as a new task sequence.
        /// Used by both Task Seq and Micro tab override rows.
        /// </summary>
        private void ShowForkPopup(VisualElement anchorRow, VisualElement searchArea)
        {
            if (_currentRunnerId == null) return;
            var sim = _uiManager.Simulation;
            if (sim == null) return;
            var runner = sim.FindRunner(_currentRunnerId);
            if (runner == null) return;
            var seq = sim.GetRunnerTaskSequence(runner);
            if (seq == null) return;

            // Toggle: remove if already open
            var existing = searchArea.Q("fork-name-popup");
            if (existing != null) { existing.RemoveFromHierarchy(); return; }

            string defaultName = $"{seq.Name} ({runner.Name}'s)";

            var popup = new VisualElement();
            popup.name = "fork-name-popup";
            popup.AddToClassList("assign-popup");

            var header = new Label("Save as new Task Sequence:");
            header.AddToClassList("assign-popup-header");
            popup.Add(header);

            var nameField = new TextField();
            nameField.value = defaultName;
            nameField.AddToClassList("fork-name-field");
            popup.Add(nameField);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;

            var confirmBtn = new Button(() =>
            {
                string name = string.IsNullOrWhiteSpace(nameField.value) ? defaultName : nameField.value;
                sim.CommandForkTaskSequenceWithOverrides(_currentRunnerId, name);
                popup.RemoveFromHierarchy();
                _cachedTaskSeqShapeKey = null;
                _cachedMicroShapeKey = null;
                Refresh();
            });
            confirmBtn.text = "Create";
            confirmBtn.AddToClassList("assign-popup-runner");
            confirmBtn.AddToClassList("swap-picker-new-btn");
            btnRow.Add(confirmBtn);

            var cancelBtn = new Button(() => popup.RemoveFromHierarchy());
            cancelBtn.text = "Cancel";
            cancelBtn.AddToClassList("assign-popup-cancel");
            btnRow.Add(cancelBtn);

            popup.Add(btnRow);

            // Insert after the anchor row
            if (anchorRow?.parent != null)
            {
                int idx = anchorRow.parent.IndexOf(anchorRow);
                anchorRow.parent.Insert(idx + 1, popup);
            }

            // Auto-focus and select all text
            nameField.schedule.Execute(() =>
            {
                nameField.Focus();
                nameField.SelectAll();
            });
        }

        private void OnClearAllOverridesClicked()
        {
            if (_currentRunnerId == null) return;
            _uiManager.Simulation?.CommandClearAllMicroOverrides(_currentRunnerId);
            _cachedTaskSeqShapeKey = null;
            _cachedMicroShapeKey = null;
            Refresh();
        }

        private void OnDuplicateAndOverrideClicked(int stepIndex)
        {
            if (_currentRunnerId == null) return;
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            string effectiveId = ResolveEffectiveMicroId(sim, sim.FindRunner(_currentRunnerId), stepIndex);
            if (string.IsNullOrEmpty(effectiveId)) return;

            string cloneId = sim.CommandCloneMicroRulesetAsOverride(_currentRunnerId, stepIndex, effectiveId);
            if (cloneId == null) return;

            _cachedMicroShapeKey = null;
            _cachedTaskSeqShapeKey = null;
            Refresh();
            _uiManager.OpenAutomationPanelToItemFromRunner("micro", cloneId, _currentRunnerId);
        }

        private string ResolveEffectiveMicroId(GameSimulation sim, Runner runner, int stepIndex)
        {
            if (runner == null) return null;
            var seq = sim.GetRunnerTaskSequence(runner);
            if (seq == null || stepIndex >= seq.Steps.Count) return null;
            string overrideId = sim.GetRunnerMicroOverrideForStep(runner, stepIndex);
            return overrideId ?? seq.Steps[stepIndex].MicroRulesetId;
        }

        /// <summary>
        /// Shows a popup to pick a micro ruleset to override on a specific Work step.
        /// searchArea controls where the popup is searched/inserted (Task Seq or Micro tab).
        /// </summary>
        private void ShowMicroSwapPicker(VisualElement anchor, int stepIndex, VisualElement searchArea = null)
        {
            var sim = _uiManager.Simulation;
            if (sim == null || _currentRunnerId == null) return;

            searchArea ??= _contentTaskSeq;

            // Toggle: remove if already open
            var existingPopup = searchArea.Q("micro-swap-popup");
            if (existingPopup != null) { existingPopup.RemoveFromHierarchy(); return; }

            var runner = sim.FindRunner(_currentRunnerId);
            if (runner == null) return;

            var popup = new VisualElement();
            popup.name = "micro-swap-popup";
            popup.AddToClassList("assign-popup");

            var header = new Label("Swap micro ruleset:");
            header.AddToClassList("assign-popup-header");
            popup.Add(header);

            // Current state
            string currentOverrideId = sim.GetRunnerMicroOverrideForStep(runner, stepIndex);
            var seq = sim.GetRunnerTaskSequence(runner);
            string stepMicroId = seq?.Steps[stepIndex]?.MicroRulesetId;
            string effectiveId = currentOverrideId ?? stepMicroId;

            foreach (var micro in sim.CurrentGameState.MicroRulesetLibrary)
            {
                var capturedId = micro.Id;

                var row = new VisualElement();
                row.AddToClassList("swap-picker-row");

                // Select button (name)
                var selectBtn = new Button(() =>
                {
                    if (capturedId == stepMicroId)
                    {
                        // Picking the step's original micro — clear any override instead of creating a redundant one
                        sim.CommandClearMicroOverride(_currentRunnerId, stepIndex);
                    }
                    else
                    {
                        sim.CommandSetMicroOverride(_currentRunnerId, stepIndex, capturedId);
                    }
                    popup.RemoveFromHierarchy();
                    _cachedMicroShapeKey = null;
                    Refresh();
                });
                selectBtn.text = micro.Name ?? micro.Id;
                selectBtn.AddToClassList("assign-popup-runner");
                selectBtn.style.flexGrow = 1;

                if (capturedId == effectiveId)
                    selectBtn.AddToClassList("assign-popup-current");

                row.Add(selectBtn);

                // View button (▸)
                var viewBtn = new Button(() =>
                {
                    popup.RemoveFromHierarchy();
                    _uiManager.OpenAutomationPanelToItemFromRunner("micro", capturedId, _currentRunnerId);
                });
                viewBtn.text = "\u25B8";
                viewBtn.AddToClassList("swap-picker-view-btn");
                row.Add(viewBtn);

                popup.Add(row);
            }

            // + New button — creates and opens editor, does NOT set as override
            var newBtn = new Button(() =>
            {
                string newId = sim.CommandCreateMicroRuleset();
                popup.RemoveFromHierarchy();
                _uiManager.OpenAutomationPanelToItemFromRunner("micro", newId, _currentRunnerId);
            });
            newBtn.text = "+ New Micro Ruleset";
            newBtn.AddToClassList("assign-popup-runner");
            newBtn.AddToClassList("swap-picker-new-btn");
            popup.Add(newBtn);

            // Clear override option (only when an override exists)
            if (currentOverrideId != null)
            {
                var clearBtn = new Button(() =>
                {
                    sim.CommandClearMicroOverride(_currentRunnerId, stepIndex);
                    popup.RemoveFromHierarchy();
                    _cachedMicroShapeKey = null;
                    Refresh();
                });
                clearBtn.text = "Clear Override";
                clearBtn.AddToClassList("assign-popup-runner");
                clearBtn.AddToClassList("swap-picker-clear-btn");
                popup.Add(clearBtn);
            }

            var cancelBtn = new Button(() => popup.RemoveFromHierarchy());
            cancelBtn.text = "Cancel";
            cancelBtn.AddToClassList("assign-popup-cancel");
            popup.Add(cancelBtn);

            // Insert after anchor's parent row (works for both Task Seq step rows and Micro tab action rows)
            var anchorRow = anchor.parent;
            if (anchorRow?.parent != null)
            {
                int rowIdx = anchorRow.parent.IndexOf(anchorRow);
                anchorRow.parent.Insert(rowIdx + 1, popup);
            }
            else
            {
                searchArea.Add(popup);
            }
        }
    }
}

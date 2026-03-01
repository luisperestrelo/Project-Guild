using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Master-detail editor for the Task Sequence library.
    /// Left pane: searchable list of all task sequences.
    /// Right pane: full editor for the selected sequence (name, loop, steps, used-by).
    ///
    /// Uses persistent elements: editor shell from UXML (name, loop, banner, used-by)
    /// is updated in-place via SetValueWithoutNotify. Steps editor rebuilds only on
    /// structural change (add/remove step). List uses item cache for CSS-only selection
    /// toggling and in-place name updates.
    /// </summary>
    public class TaskSequenceEditorController
    {
        private readonly UIManager _uiManager;
        private readonly VisualElement _root;

        // List pane
        private readonly Button _btnNew;
        private readonly TextField _searchField;
        private readonly ScrollView _listScroll;

        // List item cache — avoids full Clear+Rebuild on selection change
        private readonly Dictionary<string, (VisualElement item, Label nameLabel, Label infoLabel)> _listItemCache = new();

        // Editor pane (persistent UXML elements)
        private readonly Label _emptyLabel;
        private readonly VisualElement _editorContent;
        private readonly VisualElement _sharedBanner;
        private readonly Label _sharedBannerText;
        private readonly Button _btnCloneBanner;
        private readonly TextField _nameField;
        private readonly Toggle _autoNameToggle;
        private readonly Toggle _loopToggle;
        private readonly VisualElement _stepsEditor;
        private readonly Button _btnAddStep;
        private readonly Label _usedByLabel;
        private readonly Button _btnAssignTo;
        private readonly Button _btnClone;
        private readonly Button _btnDelete;

        private string _selectedId;
        public string SelectedId => _selectedId;
        private string _searchFilter = "";
        private string _cachedStepsShapeKey;
        private bool _focusNameFieldOnNextRefresh;

        /// <summary>
        /// Callback invoked when the player selects "+ New Micro Ruleset..." in a Work step's
        /// micro dropdown. Params: (newMicroRulesetId, wireAction). AutomationPanelController
        /// handles the navigation stack push. wireAction takes the ID to wire (may differ
        /// from the created ID if the user selects an existing item instead).
        /// </summary>
        public Action<string, Action<string>> OnRequestNavigateToNewMicroRuleset { get; set; }

        public TaskSequenceEditorController(VisualElement root, UIManager uiManager)
        {
            _uiManager = uiManager;
            _root = root;

            // List pane
            _btnNew = root.Q<Button>("btn-new-taskseq");
            _searchField = root.Q<TextField>("taskseq-search-field");
            _listScroll = root.Q<ScrollView>("taskseq-list-scroll");

            _btnNew.clicked += OnNewClicked;
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _searchFilter = evt.newValue?.ToLowerInvariant() ?? "";
                RebuildList();
            });

            // Editor pane
            _emptyLabel = root.Q<Label>("taskseq-editor-empty");
            _editorContent = root.Q("taskseq-editor-content");
            _sharedBanner = root.Q("taskseq-shared-banner");
            _sharedBannerText = root.Q<Label>("taskseq-shared-text");
            _btnCloneBanner = root.Q<Button>("btn-taskseq-clone-banner");
            _nameField = root.Q<TextField>("taskseq-name-field");

            // Auto-generate toggle — inserted into the name field row
            _autoNameToggle = new Toggle("Auto-generate name");
            _autoNameToggle.AddToClassList("auto-name-toggle");
            _autoNameToggle.tooltip = "Auto-generate name from steps";
            var nameRow = _nameField.parent;
            nameRow.Add(_autoNameToggle);

            _loopToggle = root.Q<Toggle>("taskseq-loop-toggle");
            _stepsEditor = root.Q("taskseq-steps-editor");
            _btnAddStep = root.Q<Button>("btn-add-step");
            _usedByLabel = root.Q<Label>("taskseq-used-by-label");
            _btnAssignTo = root.Q<Button>("btn-assign-to-taskseq");
            _btnClone = root.Q<Button>("btn-clone-taskseq");
            _btnDelete = root.Q<Button>("btn-delete-taskseq");

            // Editor events
            _btnAssignTo.clicked += OnAssignToClicked;
            _nameField.RegisterValueChangedCallback(evt =>
            {
                if (_selectedId == null) return;
                var sim = _uiManager.Simulation;
                if (sim == null) return;

                // Typing a name disables auto-generate
                var seq = sim.FindTaskSequenceInLibrary(_selectedId);
                if (seq != null && seq.AutoGenerateName)
                {
                    seq.AutoGenerateName = false;
                    _autoNameToggle.SetValueWithoutNotify(false);
                }

                sim.CommandRenameTaskSequence(_selectedId, evt.newValue);
                // Update list item name in-place — no full list rebuild
                UpdateListItemName(_selectedId, evt.newValue);
            });
            _autoNameToggle.RegisterValueChangedCallback(evt =>
            {
                if (_selectedId == null) return;
                var sim = _uiManager.Simulation;
                if (sim == null) return;

                var seq = sim.FindTaskSequenceInLibrary(_selectedId);
                if (seq == null) return;
                seq.AutoGenerateName = evt.newValue;

                if (evt.newValue)
                {
                    // Re-derive name immediately
                    TryAutoNameFromSteps(_selectedId);
                }
            });
            _loopToggle.RegisterValueChangedCallback(evt =>
            {
                if (_selectedId == null) return;
                _uiManager.Simulation?.CommandSetTaskSequenceLoop(_selectedId, evt.newValue);
            });
            _btnAddStep.clicked += OnAddStepClicked;
            _btnClone.clicked += OnDuplicateClicked;
            _btnDelete.clicked += () => DeleteItemWithConfirmation(_selectedId);
            _btnCloneBanner.clicked += OnCloneBannerClicked;
        }

        public void SelectNewItem(string id)
        {
            _focusNameFieldOnNextRefresh = true;
            SelectItem(id);
        }

        public void SelectItem(string id)
        {
            var oldId = _selectedId;
            _selectedId = id;

            // Toggle selection CSS without rebuilding the list
            if (oldId != null && _listItemCache.TryGetValue(oldId, out var oldItem))
                oldItem.item.RemoveFromClassList("list-item-selected");
            if (id != null && _listItemCache.TryGetValue(id, out var newItem))
                newItem.item.AddToClassList("list-item-selected");

            RefreshEditor();
        }

        public void RefreshList()
        {
            // Full list rebuild — only called on search filter change, CRUD, or panel open
            RebuildList();
        }

        // ─── List Pane ──────────────────────────────

        private void RebuildList()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            _listScroll.Clear();
            _listItemCache.Clear();

            foreach (var seq in sim.CurrentGameState.TaskSequenceLibrary)
            {
                string name = seq.Name ?? seq.Id ?? "Unnamed";
                if (!string.IsNullOrEmpty(_searchFilter) &&
                    !name.ToLowerInvariant().Contains(_searchFilter))
                    continue;

                var item = new VisualElement();
                item.AddToClassList("list-item");
                if (seq.Id == _selectedId) item.AddToClassList("list-item-selected");

                var textContainer = new VisualElement();
                textContainer.AddToClassList("list-item-text");
                textContainer.pickingMode = PickingMode.Ignore;

                var nameLabel = new Label(name);
                nameLabel.AddToClassList("list-item-name");
                nameLabel.pickingMode = PickingMode.Ignore;
                textContainer.Add(nameLabel);

                int usageCount = sim.CountRunnersUsingTaskSequence(seq.Id);
                string infoText = seq.Loop ? "Loop" : "Once";
                if (usageCount > 0) infoText += $" | {usageCount} runner{(usageCount != 1 ? "s" : "")}";
                var infoLabel = new Label(infoText);
                infoLabel.AddToClassList("list-item-info");
                infoLabel.pickingMode = PickingMode.Ignore;
                textContainer.Add(infoLabel);

                item.Add(textContainer);

                // Hover-reveal action icons
                var actions = new VisualElement();
                actions.AddToClassList("list-item-actions");

                string capturedId = seq.Id;

                var dupeBtn = new Button(() => DuplicateItem(capturedId));
                dupeBtn.text = "\u29C9"; // ⧉
                dupeBtn.AddToClassList("list-item-icon-btn");
                dupeBtn.tooltip = "Duplicate";
                actions.Add(dupeBtn);

                var delBtn = new Button(() => DeleteItemWithConfirmation(capturedId));
                delBtn.text = "\u2715"; // ✕
                delBtn.AddToClassList("list-item-icon-btn");
                delBtn.AddToClassList("list-item-icon-delete");
                delBtn.tooltip = "Delete";
                actions.Add(delBtn);

                item.Add(actions);

                item.RegisterCallback<ClickEvent>(evt =>
                {
                    // Don't select when clicking action buttons
                    if (evt.target is Button) return;
                    SelectItem(capturedId);
                });

                _listScroll.Add(item);
                _listItemCache[seq.Id] = (item, nameLabel, infoLabel);
            }
        }

        /// <summary>
        /// Update a list item's name label in-place (e.g., during rename).
        /// Avoids full list rebuild on every keystroke.
        /// </summary>
        private void UpdateListItemName(string id, string newName)
        {
            if (_listItemCache.TryGetValue(id, out var cached))
                cached.nameLabel.text = newName;
        }

        // ─── Editor Pane ──────────────────────────────

        public void RefreshEditor()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var seq = sim.FindTaskSequenceInLibrary(_selectedId);
            if (seq == null)
            {
                _emptyLabel.style.display = DisplayStyle.Flex;
                _editorContent.style.display = DisplayStyle.None;
                return;
            }

            _emptyLabel.style.display = DisplayStyle.None;
            _editorContent.style.display = DisplayStyle.Flex;

            // Update fields without triggering change callbacks
            _nameField.SetValueWithoutNotify(seq.Name ?? "");
            _autoNameToggle.SetValueWithoutNotify(seq.AutoGenerateName);
            _loopToggle.SetValueWithoutNotify(seq.Loop);

            // Auto-focus and select name field text on new item creation
            if (_focusNameFieldOnNextRefresh)
            {
                _focusNameFieldOnNextRefresh = false;
                _nameField.schedule.Execute(() =>
                {
                    _nameField.Focus();
                    _nameField.SelectAll();
                });
            }

            // Shared template banner
            int usageCount = sim.CountRunnersUsingTaskSequence(seq.Id);
            if (usageCount > 1)
            {
                _sharedBanner.style.display = DisplayStyle.Flex;
                _sharedBannerText.text = $"Changes affect all {usageCount} runners using this template.";
            }
            else
            {
                _sharedBanner.style.display = DisplayStyle.None;
            }

            // Steps
            RebuildStepsEditor(seq, sim);

            // Used by
            var names = sim.GetRunnerNamesUsingTaskSequence(seq.Id);
            _usedByLabel.text = names.Count > 0
                ? $"Used by: {string.Join(", ", names)}"
                : "Not assigned to any runner";
        }

        private void RebuildStepsEditor(TaskSequence seq, GameSimulation sim)
        {
            int stepCount = seq.Steps?.Count ?? 0;
            // Include library/node counts so dropdowns rebuild when upstream data changes
            int microLibCount = sim.CurrentGameState.MicroRulesetLibrary?.Count ?? 0;
            int nodeCount = sim.CurrentGameState.Map?.Nodes?.Count ?? 0;
            string shapeKey = $"{seq.Id}|{stepCount}|m{microLibCount}|n{nodeCount}";
            if (shapeKey == _cachedStepsShapeKey) return;

            _stepsEditor.Clear();
            _cachedStepsShapeKey = shapeKey;
            if (seq.Steps == null) return;

            var state = sim.CurrentGameState;

            for (int i = 0; i < seq.Steps.Count; i++)
            {
                var step = seq.Steps[i];
                int stepIndex = i; // capture for closures

                var row = new VisualElement();
                row.AddToClassList("editor-step-row");

                // Index
                var indexLabel = new Label($"{i + 1}.");
                indexLabel.AddToClassList("editor-step-index");
                indexLabel.pickingMode = PickingMode.Ignore;
                row.Add(indexLabel);

                // Step type label
                var typeLabel = new Label(step.Type.ToString());
                typeLabel.AddToClassList("editor-step-type");
                typeLabel.pickingMode = PickingMode.Ignore;
                row.Add(typeLabel);

                // Parameter dropdown based on step type
                switch (step.Type)
                {
                    case TaskStepType.TravelTo:
                        var nodeDropdown = CreateNodeDropdown(step.TargetNodeId, state, newNodeId =>
                        {
                            sim.CommandSetStepTargetNode(seq.Id, stepIndex, newNodeId);
                            TryAutoNameFromSteps(seq.Id);
                        });
                        nodeDropdown.AddToClassList("editor-step-dropdown");
                        row.Add(nodeDropdown);
                        break;

                    case TaskStepType.Work:
                        var microDropdown = CreateMicroDropdown(step.MicroRulesetId, sim, newMicroId =>
                        {
                            sim.CommandSetWorkStepMicroRuleset(seq.Id, stepIndex, newMicroId);
                            // No RefreshEditor — dropdown already shows the new value.
                        });
                        microDropdown.AddToClassList("editor-step-dropdown");
                        row.Add(microDropdown);
                        break;

                    case TaskStepType.Deposit:
                        // No parameters
                        var spacer = new VisualElement();
                        spacer.style.flexGrow = 1;
                        row.Add(spacer);
                        break;
                }

                // Delete button
                var deleteBtn = new Button(() =>
                {
                    sim.CommandRemoveStepFromTaskSequence(seq.Id, stepIndex);
                    TryAutoNameFromSteps(seq.Id);
                    RefreshEditor();
                    RebuildList(); // step count may affect list info
                });
                deleteBtn.text = "\u00d7";
                deleteBtn.AddToClassList("editor-step-delete");
                row.Add(deleteBtn);

                _stepsEditor.Add(row);
            }
        }

        private DropdownField CreateNodeDropdown(string currentNodeId,
            GameState state, System.Action<string> onChange)
        {
            var choices = new List<string>();
            var displayNames = new List<string>();

            if (state?.Map?.Nodes != null)
            {
                foreach (var node in state.Map.Nodes)
                {
                    choices.Add(node.Id);
                    displayNames.Add(node.Name ?? node.Id);
                }
            }

            var dropdown = new DropdownField(displayNames, 0);
            // Set current value
            int currentIndex = choices.IndexOf(currentNodeId);
            if (currentIndex >= 0)
                dropdown.index = currentIndex;

            dropdown.RegisterValueChangedCallback(evt =>
            {
                int idx = dropdown.index;
                if (idx >= 0 && idx < choices.Count)
                    onChange(choices[idx]);
            });

            return dropdown;
        }

        private DropdownField CreateMicroDropdown(string currentMicroId,
            GameSimulation sim, System.Action<string> onChange)
        {
            var choices = new List<string>();
            var displayNames = new List<string>();

            foreach (var micro in sim.CurrentGameState.MicroRulesetLibrary)
            {
                choices.Add(micro.Id);
                displayNames.Add(micro.Name ?? micro.Id);
            }

            // "+ New Micro Ruleset..." option (only when navigation callback is available)
            int newMicroIndex = -1;
            if (OnRequestNavigateToNewMicroRuleset != null)
            {
                newMicroIndex = choices.Count;
                choices.Add("__new__");
                displayNames.Add("+ New Micro Ruleset...");
            }

            var dropdown = new DropdownField(displayNames, 0);
            int currentIndex = choices.IndexOf(currentMicroId);
            if (currentIndex >= 0)
                dropdown.index = currentIndex;

            int savedCurrentIndex = currentIndex >= 0 ? currentIndex : 0;

            dropdown.RegisterValueChangedCallback(evt =>
            {
                int idx = dropdown.index;
                if (idx >= 0 && idx < choices.Count)
                {
                    if (idx == newMicroIndex && OnRequestNavigateToNewMicroRuleset != null)
                    {
                        // Create a new micro ruleset with default rules and request navigation
                        string newId = sim.CommandCreateMicroRuleset();

                        // The wire action connects the selected micro to the Work step.
                        // The id parameter may differ from newId if the user selected
                        // an existing micro ruleset instead.
                        Action<string> wireAction = (id) => onChange(id);

                        // Reset dropdown to previous value (wire happens on Done, not now)
                        if (savedCurrentIndex >= 0 && savedCurrentIndex < displayNames.Count)
                            dropdown.SetValueWithoutNotify(displayNames[savedCurrentIndex]);

                        OnRequestNavigateToNewMicroRuleset(newId, wireAction);
                    }
                    else
                    {
                        onChange(choices[idx]);
                    }
                }
            });

            return dropdown;
        }

        // ─── Button Handlers ────────────────────────────

        private void OnNewClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            string id = sim.CommandCreateTaskSequence();
            RebuildList();
            SelectNewItem(id);
        }

        private void OnAddStepClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null || _selectedId == null) return;

            // Show step type picker (inline buttons)
            var picker = new VisualElement();
            picker.AddToClassList("step-type-picker");

            void AddTypeButton(string label, TaskStepType type)
            {
                var btn = new Button(() =>
                {
                    TaskStep newStep;
                    switch (type)
                    {
                        case TaskStepType.TravelTo:
                            string defaultNode = sim.CurrentGameState.Map?.Nodes?.Count > 0
                                ? sim.CurrentGameState.Map.Nodes[0].Id : "hub";
                            newStep = new TaskStep(TaskStepType.TravelTo, defaultNode);
                            break;
                        case TaskStepType.Work:
                            newStep = new TaskStep(TaskStepType.Work,
                                microRulesetId: DefaultRulesets.DefaultMicroId);
                            break;
                        default:
                            newStep = new TaskStep(type);
                            break;
                    }
                    sim.CommandAddStepToTaskSequence(_selectedId, newStep);
                    TryAutoNameFromSteps(_selectedId);
                    picker.RemoveFromHierarchy();
                    RefreshEditor();
                    RebuildList();
                });
                btn.text = label;
                btn.AddToClassList("step-type-picker-button");
                picker.Add(btn);
            }

            AddTypeButton("TravelTo", TaskStepType.TravelTo);
            AddTypeButton("Work", TaskStepType.Work);
            AddTypeButton("Deposit", TaskStepType.Deposit);

            // Cancel button
            var cancelBtn = new Button(() => picker.RemoveFromHierarchy());
            cancelBtn.text = "Cancel";
            cancelBtn.AddToClassList("step-type-picker-button");
            picker.Add(cancelBtn);

            // Insert after the add button
            int addBtnIndex = _btnAddStep.parent.IndexOf(_btnAddStep);
            _btnAddStep.parent.Insert(addBtnIndex + 1, picker);
        }

        private void OnAssignToClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null || _selectedId == null) return;

            // Remove any existing popup first
            var existingPopup = _btnAssignTo.parent?.Q("assign-popup");
            if (existingPopup != null) { existingPopup.RemoveFromHierarchy(); return; }

            var popup = new VisualElement();
            popup.name = "assign-popup";
            popup.AddToClassList("assign-popup");

            var header = new Label("Assign to runner:");
            header.AddToClassList("assign-popup-header");
            popup.Add(header);

            foreach (var runner in sim.CurrentGameState.Runners)
            {
                string capturedRunnerId = runner.Id;
                bool alreadyAssigned = runner.TaskSequenceId == _selectedId;
                var btn = new Button(() =>
                {
                    sim.CommandAssignTaskSequenceToRunner(capturedRunnerId, _selectedId);
                    popup.RemoveFromHierarchy();
                    RefreshEditor();
                    RebuildList();
                });
                btn.text = alreadyAssigned ? $"{runner.Name} (current)" : runner.Name;
                btn.SetEnabled(!alreadyAssigned);
                btn.AddToClassList("assign-popup-runner");
                popup.Add(btn);
            }

            var cancelBtn = new Button(() => popup.RemoveFromHierarchy());
            cancelBtn.text = "Cancel";
            cancelBtn.AddToClassList("assign-popup-cancel");
            popup.Add(cancelBtn);

            int idx = _btnAssignTo.parent.IndexOf(_btnAssignTo);
            _btnAssignTo.parent.Insert(idx + 1, popup);
        }

        private void DuplicateItem(string sourceId)
        {
            var sim = _uiManager.Simulation;
            if (sim == null || string.IsNullOrEmpty(sourceId)) return;

            string newId = sim.CommandCloneTaskSequence(sourceId);
            if (newId == null) return;

            _selectedId = newId;
            RebuildList();
            RefreshEditor();
        }

        private void OnDuplicateClicked() => DuplicateItem(_selectedId);

        private void DeleteItem(string id)
        {
            var sim = _uiManager.Simulation;
            if (sim == null || string.IsNullOrEmpty(id)) return;

            sim.CommandDeleteTaskSequence(id);
            if (_selectedId == id) _selectedId = null;
            RebuildList();
            RefreshEditor();
        }

        private void DeleteItemWithConfirmation(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            var prefs = _uiManager.Preferences;
            if (prefs != null && prefs.SkipDeleteConfirmation)
            {
                DeleteItem(id);
                return;
            }

            var sim = _uiManager.Simulation;
            var seq = sim?.FindTaskSequenceInLibrary(id);
            if (seq == null) return;

            var runnerNames = sim.GetRunnerNamesUsingTaskSequence(id);
            var macroRefs = sim.GetMacroRulesReferencingTaskSequence(id);

            var warningParts = new List<string>();
            warningParts.Add($"Delete \"{seq.Name}\"?");

            if (runnerNames.Count > 0)
            {
                string list = string.Join("\n", runnerNames.ConvertAll(n => $"  - {n}"));
                warningParts.Add($"The following runners will lose their current task:\n{list}");
            }

            if (macroRefs.Count > 0)
            {
                string list = string.Join("\n", macroRefs.ConvertAll(r => $"  - {r.rulesetName} \u2192 {r.ruleLabel}"));
                warningParts.Add($"The following macro rules reference this sequence:\n{list}");
            }

            string warning = string.Join("\n\n", warningParts);

            var panelRoot = _root.panel.visualTree.Q("automation-panel-root") ?? _root;
            UIDialogs.ShowDeleteConfirmation(panelRoot, warning, prefs, () => DeleteItem(id));
        }

        private void OnCloneBannerClicked()
        {
            DuplicateItem(_selectedId);
        }

        // ─── Auto-naming ──────────────────────────────

        /// <summary>
        /// Generate a descriptive name from a task sequence's steps.
        /// Returns null if no meaningful name can be derived (empty steps).
        /// Uses the micro ruleset's Category to pick the verb (Gather/Fight/Craft/Work).
        /// Multiple Work steps append "& more". Travel chains truncate at 3 nodes.
        /// </summary>
        public static string GenerateNameFromSteps(TaskSequence seq, GameState state)
        {
            if (seq?.Steps == null || seq.Steps.Count == 0 || state?.Map == null)
                return null;

            // Collect all Work steps with their preceding TravelTo node names
            var workEntries = new List<(string nodeName, string verb)>();
            for (int i = 0; i < seq.Steps.Count; i++)
            {
                if (seq.Steps[i].Type != TaskStepType.Work) continue;

                // Determine node name from preceding TravelTo
                string nodeName = null;
                if (i > 0 && seq.Steps[i - 1].Type == TaskStepType.TravelTo)
                {
                    var node = state.Map.GetNode(seq.Steps[i - 1].TargetNodeId);
                    nodeName = node?.Name ?? seq.Steps[i - 1].TargetNodeId;
                }

                // Determine verb from micro ruleset category
                string verb = "Work";
                var microId = seq.Steps[i].MicroRulesetId;
                if (!string.IsNullOrEmpty(microId))
                {
                    var micro = state.MicroRulesetLibrary.Find(r => r.Id == microId);
                    if (micro != null)
                    {
                        verb = micro.Category switch
                        {
                            RulesetCategory.Gathering => "Gather",
                            RulesetCategory.Combat => "Fight",
                            RulesetCategory.Crafting => "Craft",
                            _ => "Work",
                        };
                    }
                }

                workEntries.Add((nodeName, verb));
            }

            if (workEntries.Count > 0)
            {
                var (nodeName, verb) = workEntries[0];
                string name = nodeName != null ? $"{verb} at {nodeName}" : verb;
                if (workEntries.Count > 1)
                    name += " & more";
                return name;
            }

            // No Work steps — describe from TravelTo targets (truncate at 3)
            const int maxTravelNodes = 3;
            var travelNodes = new List<string>();
            foreach (var step in seq.Steps)
            {
                if (step.Type == TaskStepType.TravelTo && !string.IsNullOrEmpty(step.TargetNodeId))
                {
                    var node = state.Map.GetNode(step.TargetNodeId);
                    travelNodes.Add(node?.Name ?? step.TargetNodeId);
                }
            }
            if (travelNodes.Count > 0)
            {
                if (travelNodes.Count > maxTravelNodes)
                {
                    var truncated = travelNodes.GetRange(0, maxTravelNodes);
                    return $"Travel: {string.Join(" \u2192 ", truncated)} \u2192 \u2026";
                }
                return $"Travel: {string.Join(" \u2192 ", travelNodes)}";
            }

            return null;
        }

        /// <summary>
        /// Update the sequence name from its steps, but only if AutoGenerateName is true.
        /// Called after steps are added, removed, or targets changed.
        /// </summary>
        private void TryAutoNameFromSteps(string seqId)
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var seq = sim.FindTaskSequenceInLibrary(seqId);
            if (seq == null || !seq.AutoGenerateName) return;

            string autoName = GenerateNameFromSteps(seq, sim.CurrentGameState);
            if (autoName != null && autoName != seq.Name)
            {
                sim.CommandRenameTaskSequence(seqId, autoName);
                _nameField.SetValueWithoutNotify(autoName);
                UpdateListItemName(seqId, autoName);
            }
        }

    }
}

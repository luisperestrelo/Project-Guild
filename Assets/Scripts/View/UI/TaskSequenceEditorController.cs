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
    /// All mutations go through GameSimulation commands.
    /// </summary>
    public class TaskSequenceEditorController
    {
        private readonly UIManager _uiManager;
        private readonly VisualElement _root;

        // List pane
        private readonly Button _btnNew;
        private readonly TextField _searchField;
        private readonly ScrollView _listScroll;

        // Editor pane
        private readonly Label _emptyLabel;
        private readonly VisualElement _editorContent;
        private readonly VisualElement _sharedBanner;
        private readonly Label _sharedBannerText;
        private readonly Button _btnCloneBanner;
        private readonly TextField _nameField;
        private readonly Toggle _loopToggle;
        private readonly VisualElement _stepsEditor;
        private readonly Button _btnAddStep;
        private readonly Label _usedByLabel;
        private readonly Button _btnClone;
        private readonly Button _btnDelete;

        private string _selectedId;
        private string _searchFilter = "";

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
                RefreshList();
            });

            // Editor pane
            _emptyLabel = root.Q<Label>("taskseq-editor-empty");
            _editorContent = root.Q("taskseq-editor-content");
            _sharedBanner = root.Q("taskseq-shared-banner");
            _sharedBannerText = root.Q<Label>("taskseq-shared-text");
            _btnCloneBanner = root.Q<Button>("btn-taskseq-clone-banner");
            _nameField = root.Q<TextField>("taskseq-name-field");
            _loopToggle = root.Q<Toggle>("taskseq-loop-toggle");
            _stepsEditor = root.Q("taskseq-steps-editor");
            _btnAddStep = root.Q<Button>("btn-add-step");
            _usedByLabel = root.Q<Label>("taskseq-used-by-label");
            _btnClone = root.Q<Button>("btn-clone-taskseq");
            _btnDelete = root.Q<Button>("btn-delete-taskseq");

            // Editor events
            _nameField.RegisterValueChangedCallback(evt =>
            {
                if (_selectedId == null) return;
                _uiManager.Simulation?.CommandRenameTaskSequence(_selectedId, evt.newValue);
                RefreshList(); // update name in list
            });
            _loopToggle.RegisterValueChangedCallback(evt =>
            {
                if (_selectedId == null) return;
                _uiManager.Simulation?.CommandSetTaskSequenceLoop(_selectedId, evt.newValue);
            });
            _btnAddStep.clicked += OnAddStepClicked;
            _btnClone.clicked += OnCloneClicked;
            _btnDelete.clicked += OnDeleteClicked;
            _btnCloneBanner.clicked += OnCloneBannerClicked;
        }

        public void SelectItem(string id)
        {
            _selectedId = id;
            RefreshList();
            RefreshEditor();
        }

        public void RefreshList()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            _listScroll.Clear();

            foreach (var seq in sim.CurrentGameState.TaskSequenceLibrary)
            {
                string name = seq.Name ?? seq.Id ?? "Unnamed";
                if (!string.IsNullOrEmpty(_searchFilter) &&
                    !name.ToLowerInvariant().Contains(_searchFilter))
                    continue;

                var item = new VisualElement();
                item.AddToClassList("list-item");
                if (seq.Id == _selectedId) item.AddToClassList("list-item-selected");

                var nameLabel = new Label(name);
                nameLabel.AddToClassList("list-item-name");
                nameLabel.pickingMode = PickingMode.Ignore;
                item.Add(nameLabel);

                int usageCount = sim.CountRunnersUsingTaskSequence(seq.Id);
                string infoText = seq.Loop ? "Loop" : "Once";
                if (usageCount > 0) infoText += $" | {usageCount} runner{(usageCount != 1 ? "s" : "")}";
                var infoLabel = new Label(infoText);
                infoLabel.AddToClassList("list-item-info");
                infoLabel.pickingMode = PickingMode.Ignore;
                item.Add(infoLabel);

                string capturedId = seq.Id;
                item.RegisterCallback<ClickEvent>(evt =>
                {
                    _selectedId = capturedId;
                    RefreshList();
                    RefreshEditor();
                });

                _listScroll.Add(item);
            }
        }

        private void RefreshEditor()
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
            _loopToggle.SetValueWithoutNotify(seq.Loop);

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
            _stepsEditor.Clear();
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
                            RefreshEditor();
                        });
                        nodeDropdown.AddToClassList("editor-step-dropdown");
                        row.Add(nodeDropdown);
                        break;

                    case TaskStepType.Work:
                        var microDropdown = CreateMicroDropdown(step.MicroRulesetId, sim, newMicroId =>
                        {
                            sim.CommandSetWorkStepMicroRuleset(seq.Id, stepIndex, newMicroId);
                            RefreshEditor();
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
                    RefreshEditor();
                    RefreshList();
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

            var dropdown = new DropdownField(displayNames, 0);
            int currentIndex = choices.IndexOf(currentMicroId);
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

        // ─── Button Handlers ────────────────────────────

        private void OnNewClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var seq = new TaskSequence
            {
                Name = "New Sequence",
                Loop = true,
                Steps = new List<TaskStep>(),
            };
            string id = sim.CommandCreateTaskSequence(seq);
            _selectedId = id;
            RefreshList();
            RefreshEditor();
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
                    picker.RemoveFromHierarchy();
                    RefreshEditor();
                    RefreshList();
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

        private void OnCloneClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null || _selectedId == null) return;

            var source = sim.FindTaskSequenceInLibrary(_selectedId);
            if (source == null) return;

            // Deep copy
            var clone = new TaskSequence
            {
                Name = (source.Name ?? "Sequence") + " (copy)",
                TargetNodeId = source.TargetNodeId,
                Loop = source.Loop,
                Steps = new List<TaskStep>(),
            };
            if (source.Steps != null)
            {
                foreach (var step in source.Steps)
                    clone.Steps.Add(new TaskStep(step.Type, step.TargetNodeId, step.MicroRulesetId));
            }

            string newId = sim.CommandCreateTaskSequence(clone);
            _selectedId = newId;
            RefreshList();
            RefreshEditor();
        }

        private void OnDeleteClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null || _selectedId == null) return;

            sim.CommandDeleteTaskSequence(_selectedId);
            _selectedId = null;
            RefreshList();
            RefreshEditor();
        }

        private void OnCloneBannerClicked()
        {
            // Clone and select the copy (used from the shared template banner)
            OnCloneClicked();
        }
    }
}

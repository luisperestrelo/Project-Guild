using System.Collections.Generic;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Master-detail editor for the Micro Ruleset library.
    /// Left pane: searchable list of all micro rulesets.
    /// Right pane: editor for the selected ruleset (name, rules, used-by).
    ///
    /// Uses persistent elements: the editor shell (name field, banner, footer)
    /// is built once and updated in-place. Only the rules container rebuilds,
    /// and only when the rule count or ruleset ID changes. The list pane tracks
    /// items by ID and toggles selection CSS without full rebuild.
    /// </summary>
    public class MicroRulesetEditorController
    {
        private readonly UIManager _uiManager;
        private readonly VisualElement _root;

        // List pane (persistent UXML elements)
        private readonly Button _btnNew;
        private readonly TextField _searchField;
        private readonly ScrollView _listScroll;

        // List item cache — avoids full Clear+Rebuild on selection change
        private readonly Dictionary<string, (VisualElement item, Label nameLabel, Label infoLabel)> _listItemCache = new();

        // Editor pane (persistent UXML elements)
        private readonly Label _emptyLabel;
        private readonly VisualElement _editorContent;

        // Editor shell (built once, updated in-place)
        private VisualElement _banner;
        private Label _bannerText;
        private Button _cloneBannerBtn;
        private TextField _nameField;
        private Label _rulesHeader;
        private VisualElement _rulesContainer;
        private Button _addRuleBtn;
        private VisualElement _footer;
        private Label _usedByLabel;
        private Button _cloneBtn;
        private Button _resetBtn;
        private Button _deleteBtn;
        private bool _editorShellBuilt;

        private string _selectedId;
        public string SelectedId => _selectedId;
        private string _searchFilter = "";
        private string _cachedRulesShapeKey;
        private bool _focusNameFieldOnNextRefresh;

        public MicroRulesetEditorController(VisualElement root, UIManager uiManager)
        {
            _uiManager = uiManager;
            _root = root;

            _btnNew = root.Q<Button>("btn-new-micro");
            _searchField = root.Q<TextField>("micro-search-field");
            _listScroll = root.Q<ScrollView>("micro-list-scroll");
            _emptyLabel = root.Q<Label>("micro-editor-empty");
            _editorContent = root.Q("micro-editor-content");

            _btnNew.clicked += OnNewClicked;
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _searchFilter = evt.newValue?.ToLowerInvariant() ?? "";
                RebuildList();
            });
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

            foreach (var ruleset in sim.CurrentGameState.MicroRulesetLibrary)
            {
                string name = ruleset.Name ?? ruleset.Id ?? "Unnamed";
                if (!string.IsNullOrEmpty(_searchFilter) &&
                    !name.ToLowerInvariant().Contains(_searchFilter))
                    continue;

                var item = new VisualElement();
                item.AddToClassList("list-item");
                if (ruleset.Id == _selectedId) item.AddToClassList("list-item-selected");

                var nameLabel = new Label(name);
                nameLabel.AddToClassList("list-item-name");
                nameLabel.pickingMode = PickingMode.Ignore;
                item.Add(nameLabel);

                int seqCount = sim.CountSequencesUsingMicroRuleset(ruleset.Id);
                string infoText = $"{ruleset.Rules.Count} rule{(ruleset.Rules.Count != 1 ? "s" : "")}";
                if (seqCount > 0) infoText += $" | {seqCount} sequence{(seqCount != 1 ? "s" : "")}";
                var infoLabel = new Label(infoText);
                infoLabel.AddToClassList("list-item-info");
                infoLabel.pickingMode = PickingMode.Ignore;
                item.Add(infoLabel);

                string capturedId = ruleset.Id;
                item.RegisterCallback<ClickEvent>(evt => SelectItem(capturedId));

                _listScroll.Add(item);
                _listItemCache[ruleset.Id] = (item, nameLabel, infoLabel);
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

        private void BuildEditorShell()
        {
            if (_editorShellBuilt) return;
            _editorShellBuilt = true;

            // Shared template banner
            _banner = new VisualElement();
            _banner.AddToClassList("shared-banner");
            _bannerText = new Label();
            _bannerText.AddToClassList("shared-banner-text");
            _banner.Add(_bannerText);
            _cloneBannerBtn = new Button();
            _cloneBannerBtn.text = "Clone to create a personal copy";
            _cloneBannerBtn.AddToClassList("shared-banner-button");
            _cloneBannerBtn.clicked += OnCloneBannerClicked;
            _banner.Add(_cloneBannerBtn);
            _editorContent.Add(_banner);

            // Name field
            var nameRow = new VisualElement();
            nameRow.AddToClassList("editor-field-row");
            var nameLabelElem = new Label("Name:");
            nameLabelElem.AddToClassList("editor-field-label");
            nameRow.Add(nameLabelElem);
            _nameField = new TextField();
            _nameField.AddToClassList("editor-text-field");
            _nameField.RegisterValueChangedCallback(evt =>
            {
                if (_selectedId == null) return;
                _uiManager.Simulation?.CommandRenameRuleset(_selectedId, evt.newValue);
                UpdateListItemName(_selectedId, evt.newValue);
            });
            nameRow.Add(_nameField);
            _editorContent.Add(nameRow);

            // Rules header
            _rulesHeader = new Label("Rules");
            _rulesHeader.AddToClassList("editor-section-label");
            _editorContent.Add(_rulesHeader);

            // Rules container (only this part rebuilds on structure change)
            _rulesContainer = new VisualElement();
            _editorContent.Add(_rulesContainer);

            // Add Rule button
            _addRuleBtn = new Button(OnAddRuleClicked);
            _addRuleBtn.text = "+ Add Rule";
            _addRuleBtn.AddToClassList("editor-add-button");
            _editorContent.Add(_addRuleBtn);

            // Footer
            _footer = new VisualElement();
            _footer.AddToClassList("editor-footer");

            _usedByLabel = new Label();
            _usedByLabel.AddToClassList("editor-used-by");
            _footer.Add(_usedByLabel);

            var footerButtons = new VisualElement();
            footerButtons.AddToClassList("editor-footer-buttons");

            _cloneBtn = new Button(OnCloneClicked);
            _cloneBtn.text = "Clone";
            _cloneBtn.AddToClassList("editor-footer-button");
            footerButtons.Add(_cloneBtn);

            _resetBtn = new Button(OnResetClicked);
            _resetBtn.text = "Reset to Default";
            _resetBtn.AddToClassList("editor-footer-button");
            footerButtons.Add(_resetBtn);

            _deleteBtn = new Button(OnDeleteClicked);
            _deleteBtn.text = "Delete";
            _deleteBtn.AddToClassList("editor-footer-button");
            _deleteBtn.AddToClassList("editor-delete-button");
            footerButtons.Add(_deleteBtn);

            _footer.Add(footerButtons);
            _editorContent.Add(_footer);
        }

        public void RefreshEditor()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var ruleset = sim.FindMicroRulesetInLibrary(_selectedId);
            if (ruleset == null)
            {
                _emptyLabel.style.display = DisplayStyle.Flex;
                _editorContent.style.display = DisplayStyle.None;
                return;
            }

            _emptyLabel.style.display = DisplayStyle.None;
            _editorContent.style.display = DisplayStyle.Flex;

            // Ensure the editor shell is built
            BuildEditorShell();

            // Shared template banner (update in-place)
            int seqCount = sim.CountSequencesUsingMicroRuleset(ruleset.Id);
            if (seqCount > 1)
            {
                _banner.style.display = DisplayStyle.Flex;
                _bannerText.text = $"Changes affect all {seqCount} sequences using this template.";
            }
            else
            {
                _banner.style.display = DisplayStyle.None;
            }

            // Name field (update without triggering callback)
            _nameField.SetValueWithoutNotify(ruleset.Name ?? "");

            if (_focusNameFieldOnNextRefresh)
            {
                _focusNameFieldOnNextRefresh = false;
                _nameField.schedule.Execute(() =>
                {
                    _nameField.Focus();
                    _nameField.SelectAll();
                });
            }

            // Rules container — only rebuild on structural change
            RefreshRulesContainer(ruleset, sim);

            // Footer (update in-place)
            var names = sim.GetRunnerNamesUsingMicroRuleset(ruleset.Id);
            _usedByLabel.text = names.Count > 0
                ? $"Used by: {string.Join(", ", names)}"
                : "Not assigned to any runner";
        }

        private void RefreshRulesContainer(Ruleset ruleset, GameSimulation sim)
        {
            int ruleCount = ruleset.Rules.Count;
            // Include item registry count so item pickers rebuild when new items are added
            int itemCount = sim.ItemRegistry?.Count ?? 0;
            string shapeKey = $"{ruleset.Id}|{ruleCount}|i{itemCount}";

            if (shapeKey == _cachedRulesShapeKey) return;

            _rulesContainer.Clear();
            _cachedRulesShapeKey = shapeKey;

            if (ruleCount == 0)
            {
                var emptyRules = new Label("No rules (runner will get stuck during Work step)");
                emptyRules.AddToClassList("auto-info-value");
                _rulesContainer.Add(emptyRules);
            }
            else
            {
                for (int i = 0; i < ruleCount; i++)
                {
                    var ruleRow = RuleEditorController.BuildRuleRow(
                        i, ruleset.Rules[i], ruleset.Id,
                        isMacro: false, sim, () =>
                        {
                            // Scoped rebuild: only rules container + footer, not the whole editor
                            _cachedRulesShapeKey = null; // force rebuild
                            RefreshEditor();
                        });
                    _rulesContainer.Add(ruleRow);
                }
            }
        }

        // ─── Button Handlers ────────────────────────────

        private void OnNewClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            string id = sim.CommandCreateMicroRuleset();
            _cachedRulesShapeKey = null;
            RebuildList();
            SelectNewItem(id);
        }

        private void OnAddRuleClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null || _selectedId == null) return;

            var ruleset = sim.FindMicroRulesetInLibrary(_selectedId);
            if (ruleset == null) return;

            sim.CommandAddRuleToRuleset(_selectedId, new Rule
            {
                Label = $"Rule {ruleset.Rules.Count + 1}",
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });
            _cachedRulesShapeKey = null;
            RefreshEditor();
            RebuildList(); // update rule count in list
        }

        private void OnCloneBannerClicked()
        {
            OnCloneClicked();
        }

        private void OnCloneClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null || _selectedId == null) return;

            string newId = sim.CommandCloneMicroRuleset(_selectedId);
            if (newId != null)
            {
                _selectedId = newId;
                _cachedRulesShapeKey = null;
                RebuildList();
                RefreshEditor();
            }
        }

        private void OnResetClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null || _selectedId == null) return;

            sim.CommandResetRulesetToDefault(_selectedId);
            _cachedRulesShapeKey = null;
            RefreshEditor();
        }

        private void OnDeleteClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null || _selectedId == null) return;

            sim.CommandDeleteMicroRuleset(_selectedId);
            _selectedId = null;
            _cachedRulesShapeKey = null;
            RebuildList();
            RefreshEditor();
        }
    }
}

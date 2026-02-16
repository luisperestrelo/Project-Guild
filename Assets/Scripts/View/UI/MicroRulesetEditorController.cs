using System.Collections.Generic;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Master-detail editor for the Micro Ruleset library.
    /// Left pane: searchable list of all micro rulesets.
    /// Right pane: editor for the selected ruleset (name, category, rules, used-by).
    ///
    /// Rule editing (add/remove/reorder/edit conditions+actions) is implemented in Batch D.
    /// This controller provides the full master-detail shell with CRUD and read-only rule display.
    /// </summary>
    public class MicroRulesetEditorController
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

        private string _selectedId;
        private string _searchFilter = "";

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
                RefreshList();
            });
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
                item.RegisterCallback<ClickEvent>(evt =>
                {
                    _selectedId = capturedId;
                    RefreshList();
                    RefreshEditor();
                });

                _listScroll.Add(item);
            }

            RefreshEditor();
        }

        private void RefreshEditor()
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
            _editorContent.Clear();

            var state = sim.CurrentGameState;

            // Shared template banner
            int seqCount = sim.CountSequencesUsingMicroRuleset(ruleset.Id);
            if (seqCount > 1)
            {
                var banner = new VisualElement();
                banner.AddToClassList("shared-banner");
                var bannerText = new Label($"Changes affect all {seqCount} sequences using this template.");
                bannerText.AddToClassList("shared-banner-text");
                banner.Add(bannerText);

                var cloneBtn = new Button(() =>
                {
                    string newId = sim.CommandCloneMicroRuleset(ruleset.Id);
                    if (newId != null)
                    {
                        _selectedId = newId;
                        RefreshList();
                    }
                });
                cloneBtn.text = "Clone to create a personal copy";
                cloneBtn.AddToClassList("shared-banner-button");
                banner.Add(cloneBtn);

                _editorContent.Add(banner);
            }

            // Name field
            var nameRow = new VisualElement();
            nameRow.AddToClassList("editor-field-row");
            var nameLabelElem = new Label("Name:");
            nameLabelElem.AddToClassList("editor-field-label");
            nameRow.Add(nameLabelElem);
            var nameField = new TextField();
            nameField.AddToClassList("editor-text-field");
            nameField.SetValueWithoutNotify(ruleset.Name ?? "");
            nameField.RegisterValueChangedCallback(evt =>
                sim.CommandRenameRuleset(ruleset.Id, evt.newValue));
            nameRow.Add(nameField);
            _editorContent.Add(nameRow);

            // Rules header
            var rulesHeader = new Label("Rules");
            rulesHeader.AddToClassList("editor-section-label");
            _editorContent.Add(rulesHeader);

            // Rules list (interactive editing)
            if (ruleset.Rules.Count == 0)
            {
                var emptyRules = new Label("No rules (runner will get stuck during Work step)");
                emptyRules.AddToClassList("auto-info-value");
                _editorContent.Add(emptyRules);
            }
            else
            {
                for (int i = 0; i < ruleset.Rules.Count; i++)
                {
                    var ruleRow = RuleEditorController.BuildRuleRow(
                        i, ruleset.Rules[i], ruleset.Id,
                        isMacro: false, sim, () => RefreshEditor());
                    _editorContent.Add(ruleRow);
                }
            }

            // Add Rule button
            var addRuleBtn = new Button(() =>
            {
                sim.CommandAddRuleToRuleset(ruleset.Id, new Rule
                {
                    Label = $"Rule {ruleset.Rules.Count + 1}",
                    Conditions = { Condition.Always() },
                    Action = AutomationAction.GatherHere(0),
                    Enabled = true,
                });
                RefreshEditor();
            });
            addRuleBtn.text = "+ Add Rule";
            addRuleBtn.AddToClassList("editor-add-button");
            _editorContent.Add(addRuleBtn);

            // Footer
            var footer = new VisualElement();
            footer.AddToClassList("editor-footer");

            var names = sim.GetRunnerNamesUsingMicroRuleset(ruleset.Id);
            var usedByLabel = new Label(names.Count > 0
                ? $"Used by runners: {string.Join(", ", names)}"
                : "Not used by any active runner");
            usedByLabel.AddToClassList("editor-used-by");
            footer.Add(usedByLabel);

            var footerButtons = new VisualElement();
            footerButtons.AddToClassList("editor-footer-buttons");

            var cloneFooterBtn = new Button(() =>
            {
                string newId = sim.CommandCloneMicroRuleset(ruleset.Id);
                if (newId != null)
                {
                    _selectedId = newId;
                    RefreshList();
                }
            });
            cloneFooterBtn.text = "Clone";
            cloneFooterBtn.AddToClassList("editor-footer-button");
            footerButtons.Add(cloneFooterBtn);

            var resetBtn = new Button(() =>
            {
                sim.CommandResetRulesetToDefault(ruleset.Id);
                RefreshEditor();
            });
            resetBtn.text = "Reset to Default";
            resetBtn.AddToClassList("editor-footer-button");
            footerButtons.Add(resetBtn);

            var deleteBtn = new Button(() =>
            {
                sim.CommandDeleteMicroRuleset(ruleset.Id);
                _selectedId = null;
                RefreshList();
            });
            deleteBtn.text = "Delete";
            deleteBtn.AddToClassList("editor-footer-button");
            deleteBtn.AddToClassList("editor-delete-button");
            footerButtons.Add(deleteBtn);

            footer.Add(footerButtons);
            _editorContent.Add(footer);
        }

        private void OnNewClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var ruleset = DefaultRulesets.CreateDefaultMicro();
            ruleset.Id = null; // let CommandCreate generate new ID
            ruleset.Name = "New Micro Ruleset";
            string id = sim.CommandCreateMicroRuleset(ruleset);
            _selectedId = id;
            RefreshList();
        }
    }
}

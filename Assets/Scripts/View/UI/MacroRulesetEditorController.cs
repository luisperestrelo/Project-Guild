using System.Collections.Generic;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Master-detail editor for the Macro Ruleset library.
    /// Left pane: searchable list of all macro rulesets.
    /// Right pane: editor for the selected ruleset (name, category, rules, used-by).
    ///
    /// Rule editing (add/remove/reorder/edit conditions+actions) is implemented in Batch D.
    /// This controller provides the full master-detail shell with CRUD and read-only rule display.
    /// </summary>
    public class MacroRulesetEditorController
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

        public MacroRulesetEditorController(VisualElement root, UIManager uiManager)
        {
            _uiManager = uiManager;
            _root = root;

            _btnNew = root.Q<Button>("btn-new-macro");
            _searchField = root.Q<TextField>("macro-search-field");
            _listScroll = root.Q<ScrollView>("macro-list-scroll");
            _emptyLabel = root.Q<Label>("macro-editor-empty");
            _editorContent = root.Q("macro-editor-content");

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

            foreach (var ruleset in sim.CurrentGameState.MacroRulesetLibrary)
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

                int usageCount = sim.CountRunnersUsingMacroRuleset(ruleset.Id);
                string infoText = $"{ruleset.Rules.Count} rule{(ruleset.Rules.Count != 1 ? "s" : "")}";
                if (usageCount > 0) infoText += $" | {usageCount} runner{(usageCount != 1 ? "s" : "")}";
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

            // Also refresh editor (usage counts may have changed)
            RefreshEditor();
        }

        private void RefreshEditor()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var ruleset = sim.FindMacroRulesetInLibrary(_selectedId);
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
            int usageCount = sim.CountRunnersUsingMacroRuleset(ruleset.Id);
            if (usageCount > 1)
            {
                var banner = new VisualElement();
                banner.AddToClassList("shared-banner");
                var bannerText = new Label($"Changes affect all {usageCount} runners using this template.");
                bannerText.AddToClassList("shared-banner-text");
                banner.Add(bannerText);

                var cloneBtn = new Button(() =>
                {
                    var clone = ruleset.DeepCopy();
                    clone.Name = (ruleset.Name ?? "Macro") + " (copy)";
                    string newId = sim.CommandCreateMacroRuleset(clone);
                    _selectedId = newId;
                    RefreshList();
                });
                cloneBtn.text = "Clone to create a personal copy";
                cloneBtn.AddToClassList("shared-banner-button");
                banner.Add(cloneBtn);

                _editorContent.Add(banner);
            }

            // Name field
            var nameRow = new VisualElement();
            nameRow.AddToClassList("editor-field-row");
            var nameLabel = new Label("Name:");
            nameLabel.AddToClassList("editor-field-label");
            nameRow.Add(nameLabel);
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
                var emptyRules = new Label("No rules (add rules to automate task switching)");
                emptyRules.AddToClassList("auto-info-value");
                _editorContent.Add(emptyRules);
            }
            else
            {
                for (int i = 0; i < ruleset.Rules.Count; i++)
                {
                    var ruleRow = RuleEditorController.BuildRuleRow(
                        i, ruleset.Rules[i], ruleset.Id,
                        isMacro: true, sim, () => RefreshEditor());
                    _editorContent.Add(ruleRow);
                }
            }

            // Add Rule button (placeholder text, full editing in Batch D)
            var addRuleBtn = new Button(() =>
            {
                sim.CommandAddRuleToRuleset(ruleset.Id, new Rule
                {
                    Label = $"Rule {ruleset.Rules.Count + 1}",
                    Conditions = { Condition.Always() },
                    Action = AutomationAction.Idle(),
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

            var names = sim.GetRunnerNamesUsingMacroRuleset(ruleset.Id);
            var usedByLabel = new Label(names.Count > 0
                ? $"Used by: {string.Join(", ", names)}"
                : "Not assigned to any runner");
            usedByLabel.AddToClassList("editor-used-by");
            footer.Add(usedByLabel);

            var footerButtons = new VisualElement();
            footerButtons.AddToClassList("editor-footer-buttons");

            var cloneFooterBtn = new Button(() =>
            {
                var clone = ruleset.DeepCopy();
                clone.Name = (ruleset.Name ?? "Macro") + " (copy)";
                string newId = sim.CommandCreateMacroRuleset(clone);
                _selectedId = newId;
                RefreshList();
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
                sim.CommandDeleteMacroRuleset(ruleset.Id);
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

            var ruleset = new Ruleset
            {
                Name = "New Macro Ruleset",
                Category = RulesetCategory.General,
            };
            string id = sim.CommandCreateMacroRuleset(ruleset);
            _selectedId = id;
            RefreshList();
        }
    }
}

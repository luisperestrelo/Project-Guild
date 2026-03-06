using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Master-detail editor for the Combat Style library.
    /// Left pane: searchable list of all combat styles.
    /// Right pane: editor for the selected style (name, targeting rules, ability rules, used-by).
    ///
    /// Follows the same persistent-element pattern as MicroRulesetEditorController:
    /// shell built once, rules containers rebuild only on structural change (shape-keyed).
    /// </summary>
    public class CombatStyleEditorController
    {
        private readonly UIManager _uiManager;
        private readonly VisualElement _root;

        // List pane
        private readonly Button _btnNew;
        private readonly TextField _searchField;
        private readonly ScrollView _listScroll;

        // List item cache
        private readonly Dictionary<string, (VisualElement item, Label nameLabel, Label infoLabel)> _listItemCache = new();

        // Editor pane
        private readonly Label _emptyLabel;
        private readonly VisualElement _editorContent;

        // Editor shell (built once)
        private VisualElement _banner;
        private Label _bannerText;
        private Button _cloneBannerBtn;
        private TextField _nameField;
        private NameFieldPlaceholder _namePlaceholder;

        // Targeting rules section
        private Label _targetingHeader;
        private VisualElement _targetingContainer;
        private Button _addTargetingBtn;

        // Ability rules section
        private Label _abilityHeader;
        private VisualElement _abilityContainer;
        private Button _addAbilityBtn;

        // Footer
        private VisualElement _footer;
        private Label _usedByLabel;
        private Button _cloneBtn;
        private Button _deleteBtn;
        private bool _editorShellBuilt;

        private string _selectedId;
        public string SelectedId => _selectedId;
        private string _searchFilter = "";
        private string _cachedShapeKey;
        private bool _focusNameFieldOnNextRefresh;

        public CombatStyleEditorController(VisualElement root, UIManager uiManager)
        {
            _uiManager = uiManager;
            _root = root;

            _btnNew = root.Q<Button>("btn-new-combat");
            _searchField = root.Q<TextField>("combat-search-field");
            _listScroll = root.Q<ScrollView>("combat-list-scroll");
            _emptyLabel = root.Q<Label>("combat-editor-empty");
            _editorContent = root.Q("combat-editor-content");

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

            if (oldId != null && _listItemCache.TryGetValue(oldId, out var oldItem))
                oldItem.item.RemoveFromClassList("list-item-selected");
            if (id != null && _listItemCache.TryGetValue(id, out var newItem))
                newItem.item.AddToClassList("list-item-selected");

            _cachedShapeKey = null; // Force rules rebuild on selection change
            RefreshEditor();
        }

        // ─── List ────────────────────────────────────────────

        public void RefreshList()
        {
            RebuildList();
        }

        private void RebuildList()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            _listScroll.Clear();
            _listItemCache.Clear();

            foreach (var style in sim.CurrentGameState.CombatStyleLibrary)
            {
                string name = style.Name ?? style.Id ?? "Unnamed";
                if (!string.IsNullOrEmpty(_searchFilter) &&
                    !name.ToLowerInvariant().Contains(_searchFilter))
                    continue;

                var item = new VisualElement();
                item.AddToClassList("list-item");
                if (style.Id == _selectedId) item.AddToClassList("list-item-selected");

                var textContainer = new VisualElement();
                textContainer.AddToClassList("list-item-text");
                textContainer.pickingMode = PickingMode.Ignore;

                var nameLabel = new Label(name);
                nameLabel.AddToClassList("list-item-name");
                nameLabel.pickingMode = PickingMode.Ignore;
                textContainer.Add(nameLabel);

                int tCount = style.TargetingRules.Count;
                int aCount = style.AbilityRules.Count;
                string infoText = $"{tCount} targeting, {aCount} ability";
                var infoLabel = new Label(infoText);
                infoLabel.AddToClassList("list-item-info");
                infoLabel.pickingMode = PickingMode.Ignore;
                textContainer.Add(infoLabel);

                item.Add(textContainer);

                // Hover-reveal actions
                var actions = new VisualElement();
                actions.AddToClassList("list-item-actions");

                string capturedId = style.Id;

                var dupeBtn = new Button(() => DuplicateItem(capturedId));
                dupeBtn.text = "\u2750";
                dupeBtn.AddToClassList("list-item-icon-btn");
                dupeBtn.tooltip = "Duplicate";
                actions.Add(dupeBtn);

                var delBtn = new Button(() => DeleteItemWithConfirmation(capturedId));
                delBtn.text = "\u2715";
                delBtn.AddToClassList("list-item-icon-btn");
                delBtn.AddToClassList("list-item-icon-delete");
                delBtn.tooltip = "Delete";
                actions.Add(delBtn);

                item.Add(actions);

                item.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.target is Button) return;
                    SelectItem(capturedId);
                });

                _listScroll.Add(item);
                _listItemCache[style.Id] = (item, nameLabel, infoLabel);
            }
        }

        private void UpdateListItemName(string id, string newName)
        {
            if (_listItemCache.TryGetValue(id, out var cached))
                cached.nameLabel.text = newName;
        }

        // ─── Editor ──────────────────────────────────────────

        public void RefreshEditor()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var style = sim.FindCombatStyleInLibrary(_selectedId);
            if (style == null)
            {
                _emptyLabel.style.display = DisplayStyle.Flex;
                _editorContent.style.display = DisplayStyle.None;
                return;
            }

            _emptyLabel.style.display = DisplayStyle.None;
            _editorContent.style.display = DisplayStyle.Flex;

            BuildEditorShell();

            // Banner: show which runners use this style
            var runnerNames = GetRunnerNamesUsingStyle(sim, style.Id);
            bool hasUsers = runnerNames.Count > 0;
            if (hasUsers)
            {
                _banner.style.display = DisplayStyle.Flex;
                var text = $"Active on: {string.Join(", ", runnerNames)}";
                if (runnerNames.Count > 1)
                    text += "\nEdits here affect all of them.";
                _bannerText.text = text;
                _cloneBannerBtn.style.display = runnerNames.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else
            {
                _banner.style.display = DisplayStyle.None;
            }

            // Name
            _namePlaceholder.UpdateDisplay(style.Name);

            if (_focusNameFieldOnNextRefresh)
            {
                _focusNameFieldOnNextRefresh = false;
                _nameField.schedule.Execute(() => _nameField.Focus());
            }

            // Rules (shape-keyed rebuild)
            RefreshRulesContainers(style, sim);

            // Footer
            _usedByLabel.text = runnerNames.Count > 0
                ? $"Used by: {string.Join(", ", runnerNames)}"
                : "Not assigned to any runner";
        }

        private void BuildEditorShell()
        {
            if (_editorShellBuilt) return;
            _editorShellBuilt = true;

            // Banner
            _banner = new VisualElement();
            _banner.AddToClassList("shared-banner");
            _bannerText = new Label();
            _bannerText.AddToClassList("shared-banner-text");
            _banner.Add(_bannerText);
            _cloneBannerBtn = new Button(OnCloneBannerClicked);
            _cloneBannerBtn.text = "Duplicate";
            _cloneBannerBtn.AddToClassList("shared-banner-button");
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
                if (string.IsNullOrEmpty(evt.newValue)) return;
                _uiManager.Simulation?.CommandRenameCombatStyle(_selectedId, evt.newValue);
                UpdateListItemName(_selectedId, evt.newValue);
            });
            nameRow.Add(_nameField);
            _namePlaceholder = new NameFieldPlaceholder(_nameField, () =>
            {
                var sim = _uiManager.Simulation;
                if (sim == null || _selectedId == null) return null;
                return sim.FindCombatStyleInLibrary(_selectedId)?.Name;
            });
            _editorContent.Add(nameRow);

            // ─── Targeting Rules Section ─────────────────
            _targetingHeader = new Label("Targeting Rules");
            _targetingHeader.AddToClassList("editor-section-label");
            _editorContent.Add(_targetingHeader);

            var targetingHint = new Label("Checked top to bottom. First match picks the target.");
            targetingHint.AddToClassList("rules-hint-label");
            _editorContent.Add(targetingHint);

            _targetingContainer = new VisualElement();
            _editorContent.Add(_targetingContainer);

            _addTargetingBtn = new Button(OnAddTargetingRuleClicked);
            _addTargetingBtn.text = "+ Add Targeting Rule";
            _addTargetingBtn.AddToClassList("editor-add-button");
            _editorContent.Add(_addTargetingBtn);

            // ─── Ability Rules Section ───────────────────
            _abilityHeader = new Label("Ability Rules");
            _abilityHeader.AddToClassList("editor-section-label");
            _editorContent.Add(_abilityHeader);

            var abilityHint = new Label("Checked top to bottom. First match picks the ability. Unavailable abilities are skipped.");
            abilityHint.AddToClassList("rules-hint-label");
            _editorContent.Add(abilityHint);

            _abilityContainer = new VisualElement();
            _editorContent.Add(_abilityContainer);

            _addAbilityBtn = new Button(OnAddAbilityRuleClicked);
            _addAbilityBtn.text = "+ Add Ability Rule";
            _addAbilityBtn.AddToClassList("editor-add-button");
            _editorContent.Add(_addAbilityBtn);

            // ─── Footer ─────────────────────────────────
            _footer = new VisualElement();
            _footer.AddToClassList("editor-footer");

            _usedByLabel = new Label();
            _usedByLabel.AddToClassList("editor-used-by");
            _footer.Add(_usedByLabel);

            var footerButtons = new VisualElement();
            footerButtons.AddToClassList("editor-footer-buttons");

            _cloneBtn = new Button(() => DuplicateItem(_selectedId));
            _cloneBtn.text = "Duplicate";
            _cloneBtn.AddToClassList("editor-footer-button");
            footerButtons.Add(_cloneBtn);

            _deleteBtn = new Button(() => DeleteItemWithConfirmation(_selectedId));
            _deleteBtn.text = "Delete";
            _deleteBtn.AddToClassList("editor-footer-button");
            _deleteBtn.AddToClassList("editor-delete-button");
            footerButtons.Add(_deleteBtn);

            _footer.Add(footerButtons);
            _editorContent.Add(_footer);
        }

        // ─── Rules Containers ────────────────────────────────

        private void RefreshRulesContainers(CombatStyle style, GameSimulation sim)
        {
            int tCount = style.TargetingRules.Count;
            int aCount = style.AbilityRules.Count;
            int abilityDefCount = sim.Config.AbilityDefinitions?.Length ?? 0;
            string shapeKey = $"{style.Id}|t{tCount}|a{aCount}|d{abilityDefCount}";

            if (shapeKey == _cachedShapeKey) return;
            _cachedShapeKey = shapeKey;

            // Rebuild targeting rules
            _targetingContainer.Clear();
            if (tCount == 0)
            {
                var emptyLabel = new Label("No targeting rules (runner will not pick a target)");
                emptyLabel.AddToClassList("auto-info-value");
                _targetingContainer.Add(emptyLabel);
            }
            else
            {
                for (int i = 0; i < tCount; i++)
                    _targetingContainer.Add(BuildTargetingRuleRow(i, style, sim));
            }

            // Rebuild ability rules
            _abilityContainer.Clear();
            if (aCount == 0)
            {
                var emptyLabel = new Label("No ability rules (runner will idle in combat)");
                emptyLabel.AddToClassList("auto-info-value");
                _abilityContainer.Add(emptyLabel);
            }
            else
            {
                for (int i = 0; i < aCount; i++)
                    _abilityContainer.Add(BuildAbilityRuleRow(i, style, sim));
            }
        }

        // ─── Targeting Rule Row ──────────────────────────────

        private VisualElement BuildTargetingRuleRow(int index, CombatStyle style, GameSimulation sim)
        {
            var rule = style.TargetingRules[index];
            int capturedIndex = index;

            var row = new VisualElement();
            row.AddToClassList("rule-editor-row");
            row.AddToClassList("combat-targeting-rule");

            // Header row: index, enabled, selection dropdown, move/delete
            var header = new VisualElement();
            header.AddToClassList("rule-editor-header");

            var indexLabel = new Label($"{index + 1}.");
            indexLabel.AddToClassList("rule-editor-index");
            header.Add(indexLabel);

            var enabledBtn = new Button(() =>
            {
                var s = _uiManager.Simulation?.FindCombatStyleInLibrary(_selectedId);
                if (s == null || capturedIndex >= s.TargetingRules.Count) return;
                s.TargetingRules[capturedIndex].Enabled = !s.TargetingRules[capturedIndex].Enabled;
                ForceRebuild();
            });
            enabledBtn.text = rule.Enabled ? "\u2713" : "\u2717";
            enabledBtn.AddToClassList("rule-editor-toggle");
            header.Add(enabledBtn);

            var selectionLabel = new Label("Target:");
            selectionLabel.AddToClassList("rule-editor-field-label");
            header.Add(selectionLabel);

            var selectionDropdown = CreateEnumDropdown(rule.Selection, newVal =>
            {
                var s = _uiManager.Simulation?.FindCombatStyleInLibrary(_selectedId);
                if (s == null || capturedIndex >= s.TargetingRules.Count) return;
                s.TargetingRules[capturedIndex].Selection = newVal;
            });
            selectionDropdown.AddToClassList("rule-editor-dropdown");
            header.Add(selectionDropdown);

            // Move/delete buttons
            AddMoveDeleteButtons(header, index, style.TargetingRules.Count, isTargeting: true);

            row.Add(header);

            // Conditions section
            row.Add(BuildConditionsSection(rule.Conditions, capturedIndex, isTargeting: true));

            if (!rule.Enabled)
                row.AddToClassList("rule-editor-disabled");

            return row;
        }

        // ─── Ability Rule Row ────────────────────────────────

        private VisualElement BuildAbilityRuleRow(int index, CombatStyle style, GameSimulation sim)
        {
            var rule = style.AbilityRules[index];
            int capturedIndex = index;

            var row = new VisualElement();
            row.AddToClassList("rule-editor-row");
            row.AddToClassList("combat-ability-rule");

            // Header row
            var header = new VisualElement();
            header.AddToClassList("rule-editor-header");

            var indexLabel = new Label($"{index + 1}.");
            indexLabel.AddToClassList("rule-editor-index");
            header.Add(indexLabel);

            var enabledBtn = new Button(() =>
            {
                var s = _uiManager.Simulation?.FindCombatStyleInLibrary(_selectedId);
                if (s == null || capturedIndex >= s.AbilityRules.Count) return;
                s.AbilityRules[capturedIndex].Enabled = !s.AbilityRules[capturedIndex].Enabled;
                ForceRebuild();
            });
            enabledBtn.text = rule.Enabled ? "\u2713" : "\u2717";
            enabledBtn.AddToClassList("rule-editor-toggle");
            header.Add(enabledBtn);

            var abilityLabel = new Label("Ability:");
            abilityLabel.AddToClassList("rule-editor-field-label");
            header.Add(abilityLabel);

            // Ability dropdown populated from config
            var abilityDropdown = BuildAbilityDropdown(rule.AbilityId, sim, newVal =>
            {
                var s = _uiManager.Simulation?.FindCombatStyleInLibrary(_selectedId);
                if (s == null || capturedIndex >= s.AbilityRules.Count) return;
                s.AbilityRules[capturedIndex].AbilityId = newVal;
            });
            abilityDropdown.AddToClassList("rule-editor-dropdown");
            header.Add(abilityDropdown);

            // CanInterrupt toggle
            var interruptLabel = new Label("Interrupt:");
            interruptLabel.AddToClassList("rule-editor-field-label");
            interruptLabel.tooltip = "When enabled, this rule can fire mid-action (interrupting the current ability)";
            header.Add(interruptLabel);

            var interruptBtn = new Button(() =>
            {
                var s = _uiManager.Simulation?.FindCombatStyleInLibrary(_selectedId);
                if (s == null || capturedIndex >= s.AbilityRules.Count) return;
                s.AbilityRules[capturedIndex].CanInterrupt = !s.AbilityRules[capturedIndex].CanInterrupt;
                ForceRebuild();
            });
            interruptBtn.text = rule.CanInterrupt ? "\u2713" : "\u2717";
            interruptBtn.AddToClassList("rule-editor-toggle");
            interruptBtn.tooltip = "When enabled, this rule can fire mid-action";
            header.Add(interruptBtn);

            // Move/delete buttons
            AddMoveDeleteButtons(header, index, style.AbilityRules.Count, isTargeting: false);

            row.Add(header);

            // Conditions section
            row.Add(BuildConditionsSection(rule.Conditions, capturedIndex, isTargeting: false));

            if (!rule.Enabled)
                row.AddToClassList("rule-editor-disabled");

            return row;
        }

        // ─── Ability Dropdown ────────────────────────────────

        private DropdownField BuildAbilityDropdown(string currentAbilityId, GameSimulation sim,
            Action<string> onChange)
        {
            var choices = new List<string>();
            var idMap = new Dictionary<string, string>(); // display name -> id
            int selectedIndex = 0;

            var abilities = sim.Config.AbilityDefinitions;
            if (abilities != null)
            {
                for (int i = 0; i < abilities.Length; i++)
                {
                    var ability = abilities[i];
                    string display = ability.Name ?? ability.Id;
                    // Prevent duplicate display names
                    if (choices.Contains(display))
                        display = $"{display} ({ability.Id})";
                    choices.Add(display);
                    idMap[display] = ability.Id;
                    if (ability.Id == currentAbilityId)
                        selectedIndex = i;
                }
            }

            if (choices.Count == 0)
            {
                choices.Add("(no abilities configured)");
                idMap["(no abilities configured)"] = null;
            }

            var dropdown = new DropdownField(choices, selectedIndex);
            dropdown.RegisterValueChangedCallback(evt =>
            {
                if (idMap.TryGetValue(evt.newValue, out string id))
                    onChange?.Invoke(id);
            });
            return dropdown;
        }

        // ─── Conditions Section ──────────────────────────────

        private VisualElement BuildConditionsSection(List<CombatCondition> conditions,
            int ruleIndex, bool isTargeting)
        {
            var section = new VisualElement();
            section.AddToClassList("rule-editor-conditions");

            if (conditions.Count == 0)
            {
                var alwaysLabel = new Label("Always");
                alwaysLabel.AddToClassList("rule-editor-condition-always");
                section.Add(alwaysLabel);
            }
            else
            {
                for (int i = 0; i < conditions.Count; i++)
                {
                    section.Add(BuildConditionRow(conditions[i], i, ruleIndex, isTargeting));
                }
            }

            var addCondBtn = new Button(() =>
            {
                var s = _uiManager.Simulation?.FindCombatStyleInLibrary(_selectedId);
                if (s == null) return;
                var condList = isTargeting
                    ? (ruleIndex < s.TargetingRules.Count ? s.TargetingRules[ruleIndex].Conditions : null)
                    : (ruleIndex < s.AbilityRules.Count ? s.AbilityRules[ruleIndex].Conditions : null);
                if (condList == null) return;
                condList.Add(CombatCondition.Always());
                ForceRebuild();
            });
            addCondBtn.text = "+ Condition";
            addCondBtn.AddToClassList("rule-editor-add-condition-btn");
            section.Add(addCondBtn);

            return section;
        }

        private VisualElement BuildConditionRow(CombatCondition condition, int condIndex,
            int ruleIndex, bool isTargeting)
        {
            var row = new VisualElement();
            row.AddToClassList("rule-editor-condition-row");

            int capturedCondIndex = condIndex;
            int capturedRuleIndex = ruleIndex;

            // Condition type dropdown
            var typeDropdown = CreateEnumDropdown(condition.Type, newVal =>
            {
                var cond = GetCondition(capturedRuleIndex, capturedCondIndex, isTargeting);
                if (cond == null) return;
                cond.Type = newVal;
                ForceRebuild();
            });
            typeDropdown.AddToClassList("rule-editor-condition-type");
            row.Add(typeDropdown);

            // Show operator + value for numeric conditions
            bool needsOperator = condition.Type != CombatConditionType.Always
                && condition.Type != CombatConditionType.AbilityOffCooldown
                && condition.Type != CombatConditionType.EnemyIsCasting
                && condition.Type != CombatConditionType.AnyUntauntedEnemy
                && condition.Type != CombatConditionType.EnemyTargetingSelf;

            if (needsOperator)
            {
                var opButton = new Button(() =>
                {
                    var cond = GetCondition(capturedRuleIndex, capturedCondIndex, isTargeting);
                    if (cond == null) return;
                    cond.Operator = CycleOperator(cond.Operator);
                    ForceRebuild();
                });
                opButton.text = FormatOperator(condition.Operator);
                opButton.AddToClassList("rule-editor-operator-btn");
                opButton.tooltip = "Click to cycle comparison operator";
                row.Add(opButton);

                var valueField = new FloatField();
                valueField.value = condition.NumericValue;
                valueField.AddToClassList("rule-editor-value-field");
                valueField.RegisterValueChangedCallback(evt =>
                {
                    var cond = GetCondition(capturedRuleIndex, capturedCondIndex, isTargeting);
                    if (cond != null)
                        cond.NumericValue = evt.newValue;
                });
                row.Add(valueField);

                // Show % hint for percent-based conditions
                if (condition.Type == CombatConditionType.SelfHpPercent ||
                    condition.Type == CombatConditionType.SelfManaPercent ||
                    condition.Type == CombatConditionType.TargetHpPercent ||
                    condition.Type == CombatConditionType.LowestAllyHpPercent ||
                    condition.Type == CombatConditionType.AlliesBelowHpPercent)
                {
                    var pctLabel = new Label("%");
                    pctLabel.AddToClassList("rule-editor-unit-label");
                    row.Add(pctLabel);
                }
            }

            // AbilityOffCooldown: show ability picker
            if (condition.Type == CombatConditionType.AbilityOffCooldown)
            {
                var sim = _uiManager.Simulation;
                if (sim != null)
                {
                    var abilityDropdown = BuildAbilityDropdown(condition.StringParam, sim, newVal =>
                    {
                        var cond = GetCondition(capturedRuleIndex, capturedCondIndex, isTargeting);
                        if (cond != null)
                            cond.StringParam = newVal;
                    });
                    abilityDropdown.AddToClassList("rule-editor-condition-ability");
                    row.Add(abilityDropdown);
                }
            }

            // AlliesBelowHpPercent: show "min count" field after the % value
            if (condition.Type == CombatConditionType.AlliesBelowHpPercent)
            {
                int currentCount = 1;
                if (!string.IsNullOrEmpty(condition.StringParam))
                    int.TryParse(condition.StringParam, out currentCount);

                var countLabel = new Label("min allies:");
                countLabel.AddToClassList("rule-editor-unit-label");
                row.Add(countLabel);

                var countField = new IntegerField();
                countField.value = currentCount;
                countField.AddToClassList("rule-editor-value-field");
                countField.RegisterValueChangedCallback(evt =>
                {
                    var cond = GetCondition(capturedRuleIndex, capturedCondIndex, isTargeting);
                    if (cond != null)
                        cond.StringParam = evt.newValue.ToString();
                });
                row.Add(countField);
            }

            // Delete condition button
            var delBtn = new Button(() =>
            {
                var s = _uiManager.Simulation?.FindCombatStyleInLibrary(_selectedId);
                if (s == null) return;
                var condList = isTargeting
                    ? (capturedRuleIndex < s.TargetingRules.Count ? s.TargetingRules[capturedRuleIndex].Conditions : null)
                    : (capturedRuleIndex < s.AbilityRules.Count ? s.AbilityRules[capturedRuleIndex].Conditions : null);
                if (condList == null || capturedCondIndex >= condList.Count) return;
                condList.RemoveAt(capturedCondIndex);
                ForceRebuild();
            });
            delBtn.text = "\u2715";
            delBtn.AddToClassList("rule-editor-delete-condition-btn");
            delBtn.tooltip = "Remove condition";
            row.Add(delBtn);

            return row;
        }

        private CombatCondition GetCondition(int ruleIndex, int condIndex, bool isTargeting)
        {
            var s = _uiManager.Simulation?.FindCombatStyleInLibrary(_selectedId);
            if (s == null) return null;
            if (isTargeting)
            {
                if (ruleIndex >= s.TargetingRules.Count) return null;
                var conds = s.TargetingRules[ruleIndex].Conditions;
                return condIndex < conds.Count ? conds[condIndex] : null;
            }
            else
            {
                if (ruleIndex >= s.AbilityRules.Count) return null;
                var conds = s.AbilityRules[ruleIndex].Conditions;
                return condIndex < conds.Count ? conds[condIndex] : null;
            }
        }

        // ─── Move / Delete Buttons ───────────────────────────

        private void AddMoveDeleteButtons(VisualElement parent, int index, int count, bool isTargeting)
        {
            int capturedIndex = index;

            var moveUpBtn = new Button(() =>
            {
                if (isTargeting)
                    _uiManager.Simulation?.CommandMoveTargetingRule(_selectedId, capturedIndex, capturedIndex - 1);
                else
                    _uiManager.Simulation?.CommandMoveAbilityRule(_selectedId, capturedIndex, capturedIndex - 1);
                ForceRebuild();
            });
            moveUpBtn.text = "\u25B2";
            moveUpBtn.AddToClassList("rule-editor-move-btn");
            moveUpBtn.SetEnabled(index > 0);
            parent.Add(moveUpBtn);

            var moveDownBtn = new Button(() =>
            {
                if (isTargeting)
                    _uiManager.Simulation?.CommandMoveTargetingRule(_selectedId, capturedIndex, capturedIndex + 1);
                else
                    _uiManager.Simulation?.CommandMoveAbilityRule(_selectedId, capturedIndex, capturedIndex + 1);
                ForceRebuild();
            });
            moveDownBtn.text = "\u25BC";
            moveDownBtn.AddToClassList("rule-editor-move-btn");
            moveDownBtn.SetEnabled(index < count - 1);
            parent.Add(moveDownBtn);

            var delBtn = new Button(() =>
            {
                if (isTargeting)
                    _uiManager.Simulation?.CommandRemoveTargetingRule(_selectedId, capturedIndex);
                else
                    _uiManager.Simulation?.CommandRemoveAbilityRule(_selectedId, capturedIndex);
                ForceRebuild();
            });
            delBtn.text = "\u2715";
            delBtn.AddToClassList("rule-editor-delete-btn");
            parent.Add(delBtn);
        }

        // ─── Enum Dropdown Helper ────────────────────────────

        private static DropdownField CreateEnumDropdown<T>(T currentValue, Action<T> onChange) where T : Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            var names = new List<string>();
            int selectedIndex = 0;

            for (int i = 0; i < values.Length; i++)
            {
                names.Add(FormatEnumName(values[i].ToString()));
                if (values[i].Equals(currentValue))
                    selectedIndex = i;
            }

            var dropdown = new DropdownField(names, selectedIndex);
            dropdown.RegisterValueChangedCallback(evt =>
            {
                int idx = names.IndexOf(evt.newValue);
                if (idx >= 0 && idx < values.Length)
                    onChange?.Invoke(values[idx]);
            });
            return dropdown;
        }

        private static string FormatEnumName(string name)
        {
            // "NearestEnemy" -> "Nearest Enemy", "SelfHpPercent" -> "Self Hp Percent"
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                    sb.Append(' ');
                sb.Append(name[i]);
            }
            return sb.ToString();
        }

        // ─── Operator Helpers ────────────────────────────────

        private static string FormatOperator(Simulation.Automation.ComparisonOperator op)
        {
            return op switch
            {
                Simulation.Automation.ComparisonOperator.GreaterOrEqual => ">=",
                Simulation.Automation.ComparisonOperator.GreaterThan => ">",
                Simulation.Automation.ComparisonOperator.LessOrEqual => "<=",
                Simulation.Automation.ComparisonOperator.LessThan => "<",
                Simulation.Automation.ComparisonOperator.Equal => "==",
                Simulation.Automation.ComparisonOperator.NotEqual => "!=",
                _ => ">=",
            };
        }

        private static Simulation.Automation.ComparisonOperator CycleOperator(
            Simulation.Automation.ComparisonOperator current)
        {
            return current switch
            {
                Simulation.Automation.ComparisonOperator.GreaterOrEqual => Simulation.Automation.ComparisonOperator.GreaterThan,
                Simulation.Automation.ComparisonOperator.GreaterThan => Simulation.Automation.ComparisonOperator.LessOrEqual,
                Simulation.Automation.ComparisonOperator.LessOrEqual => Simulation.Automation.ComparisonOperator.LessThan,
                Simulation.Automation.ComparisonOperator.LessThan => Simulation.Automation.ComparisonOperator.Equal,
                Simulation.Automation.ComparisonOperator.Equal => Simulation.Automation.ComparisonOperator.NotEqual,
                Simulation.Automation.ComparisonOperator.NotEqual => Simulation.Automation.ComparisonOperator.GreaterOrEqual,
                _ => Simulation.Automation.ComparisonOperator.GreaterOrEqual,
            };
        }

        // ─── Button Handlers ─────────────────────────────────

        private void OnNewClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            string id = sim.CommandCreateCombatStyle();
            RebuildList();
            SelectNewItem(id);
        }

        private void OnCloneBannerClicked()
        {
            if (_selectedId == null) return;
            DuplicateItem(_selectedId);
        }

        private void DuplicateItem(string sourceId)
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            string cloneId = sim.CommandCloneCombatStyle(sourceId);
            if (cloneId == null) return;

            RebuildList();
            SelectNewItem(cloneId);
        }

        private void DeleteItemWithConfirmation(string id)
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var style = sim.FindCombatStyleInLibrary(id);
            if (style == null) return;

            UIDialogs.ShowDeleteConfirmation(
                _editorContent,
                $"Delete \"{style.Name ?? "Combat Style"}\"?",
                _uiManager.Preferences,
                () =>
                {
                    sim.CommandDeleteCombatStyle(id);
                    if (_selectedId == id) _selectedId = null;
                    RebuildList();
                    RefreshEditor();
                });
        }

        private void OnAddTargetingRuleClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null || _selectedId == null) return;

            var rule = new TargetingRule
            {
                Selection = TargetSelection.NearestEnemy,
                Enabled = true,
            };
            sim.CommandAddTargetingRule(_selectedId, rule);
            ForceRebuild();
        }

        private void OnAddAbilityRuleClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim == null || _selectedId == null) return;

            // Default to first available ability
            string defaultAbilityId = null;
            var abilities = sim.Config.AbilityDefinitions;
            if (abilities != null && abilities.Length > 0)
                defaultAbilityId = abilities[0].Id;

            var rule = new AbilityRule
            {
                AbilityId = defaultAbilityId,
                Enabled = true,
            };
            sim.CommandAddAbilityRule(_selectedId, rule);
            ForceRebuild();
        }

        // ─── Helpers ─────────────────────────────────────────

        private void ForceRebuild()
        {
            _cachedShapeKey = null;
            RefreshEditor();
            RebuildList(); // Update info labels
        }

        private static List<string> GetRunnerNamesUsingStyle(GameSimulation sim, string styleId)
        {
            var names = new List<string>();
            foreach (var runner in sim.CurrentGameState.Runners)
            {
                if (runner.CombatStyleId == styleId)
                    names.Add(runner.Name ?? runner.Id);
            }
            return names;
        }
    }
}

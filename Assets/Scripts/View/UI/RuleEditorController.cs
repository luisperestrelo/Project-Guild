using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Reusable component that renders a single automation rule as an editable row.
    /// Two-line layout: conditions (IF) on top, action (THEN) on bottom.
    /// Supports move up/down, toggle enabled, and delete.
    ///
    /// Used by both MacroRulesetEditorController and MicroRulesetEditorController.
    /// All changes go through GameSimulation commands.
    ///
    /// IMPORTANT: Value-only changes (number fields, dropdown selections) persist via
    /// CommandUpdateRule but do NOT trigger a UI rebuild. Only structural changes
    /// (add/remove condition, add/remove/move rule, condition type change) call
    /// onStructuralChange which rebuilds the rules container.
    /// </summary>
    public class RuleEditorController
    {
        /// <summary>Build an editable rule row and add it to the parent container.</summary>
        public static VisualElement BuildRuleRow(
            int ruleIndex,
            Rule rule,
            string rulesetId,
            bool isMacro,
            GameSimulation sim,
            Action onStructuralChange,
            Action<string, Action<string>> onCreateNewSequence = null)
        {
            var state = sim.CurrentGameState;
            var row = new VisualElement();
            row.AddToClassList("rule-editor-row");
            if (!rule.Enabled) row.AddToClassList("rule-editor-disabled");

            // Persist a value change without rebuilding the UI.
            // The element already shows the new value — just save to sim.
            void PersistRule()
            {
                sim.CommandUpdateRule(rulesetId, ruleIndex, rule);
            }

            // ─── Top line: checkbox, index, IF, conditions, move/delete ───
            var topLine = new VisualElement();
            topLine.AddToClassList("rule-editor-top-line");

            // Enable toggle
            var enableBtn = new Button(() =>
            {
                sim.CommandToggleRuleEnabled(rulesetId, ruleIndex);
                onStructuralChange();
            });
            enableBtn.text = rule.Enabled ? "\u2713" : "\u2717";
            enableBtn.AddToClassList("rule-editor-toggle");
            topLine.Add(enableBtn);

            // Index label
            var indexLabel = new Label($"{ruleIndex + 1}.");
            indexLabel.AddToClassList("rule-editor-index");
            indexLabel.pickingMode = PickingMode.Ignore;
            topLine.Add(indexLabel);

            // Conditions sub-card (blue tint)
            var conditionsCard = new VisualElement();
            conditionsCard.AddToClassList("rule-editor-conditions-card");

            var ifLabel = new Label("IF");
            ifLabel.AddToClassList("rule-editor-keyword");
            ifLabel.pickingMode = PickingMode.Ignore;
            conditionsCard.Add(ifLabel);

            var conditionsContent = new VisualElement();
            conditionsContent.AddToClassList("rule-editor-conditions-content");

            for (int ci = 0; ci < rule.Conditions.Count; ci++)
            {
                int condIndex = ci;

                if (ci > 0)
                {
                    var andLabel = new Label("AND");
                    andLabel.AddToClassList("rule-editor-and-keyword");
                    andLabel.pickingMode = PickingMode.Ignore;
                    conditionsContent.Add(andLabel);
                }

                var condRow = BuildConditionEditor(
                    rule.Conditions[ci], state, sim,
                    // Value-only update — persist, no rebuild
                    (updatedCond) =>
                    {
                        rule.Conditions[condIndex] = updatedCond;
                        PersistRule();
                    },
                    // Condition type changed — structural, needs param rebuild.
                    // We handle this locally in BuildConditionEditor via RebuildParams,
                    // but the parent also needs to rebuild to re-sync condition indices.
                    (updatedCond) =>
                    {
                        rule.Conditions[condIndex] = updatedCond;
                        PersistRule();
                        onStructuralChange();
                    },
                    // Delete condition
                    () =>
                    {
                        if (rule.Conditions.Count > 1)
                        {
                            rule.Conditions.RemoveAt(condIndex);
                            PersistRule();
                            onStructuralChange();
                        }
                    });
                conditionsContent.Add(condRow);
            }

            // "+" button to add condition
            var addCondBtn = new Button(() =>
            {
                rule.Conditions.Add(Condition.Always());
                PersistRule();
                onStructuralChange();
            });
            addCondBtn.text = "+";
            addCondBtn.AddToClassList("rule-editor-add-cond");
            addCondBtn.tooltip = "Add condition";
            conditionsContent.Add(addCondBtn);

            conditionsCard.Add(conditionsContent);
            topLine.Add(conditionsCard);

            // Move / Delete buttons (anchored right)
            var buttonsContainer = new VisualElement();
            buttonsContainer.AddToClassList("rule-editor-buttons");

            var moveUpBtn = new Button(() =>
            {
                if (ruleIndex > 0)
                {
                    sim.CommandMoveRuleInRuleset(rulesetId, ruleIndex, ruleIndex - 1);
                    onStructuralChange();
                }
            });
            moveUpBtn.text = "\u25b2";
            moveUpBtn.AddToClassList("rule-editor-move-btn");
            buttonsContainer.Add(moveUpBtn);

            var moveDownBtn = new Button(() =>
            {
                var (ruleset, _) = sim.FindRulesetInAnyLibrary(rulesetId);
                if (ruleset != null && ruleIndex < ruleset.Rules.Count - 1)
                {
                    sim.CommandMoveRuleInRuleset(rulesetId, ruleIndex, ruleIndex + 1);
                    onStructuralChange();
                }
            });
            moveDownBtn.text = "\u25bc";
            moveDownBtn.AddToClassList("rule-editor-move-btn");
            buttonsContainer.Add(moveDownBtn);

            var deleteBtn = new Button(() =>
            {
                sim.CommandRemoveRuleFromRuleset(rulesetId, ruleIndex);
                onStructuralChange();
            });
            deleteBtn.text = "\u00d7";
            deleteBtn.AddToClassList("rule-editor-delete-btn");
            buttonsContainer.Add(deleteBtn);

            topLine.Add(buttonsContainer);
            row.Add(topLine);

            // ─── Bottom line: THEN, action, timing ───
            var actionCard = new VisualElement();
            actionCard.AddToClassList("rule-editor-action-card");

            var thenLabel = new Label("THEN");
            thenLabel.AddToClassList("rule-editor-keyword");
            thenLabel.pickingMode = PickingMode.Ignore;
            actionCard.Add(thenLabel);

            var actionEditor = BuildActionEditor(rule.Action, isMacro, state, sim, (updatedAction) =>
            {
                rule.Action = updatedAction;
                PersistRule();
                // Action type changes are structural (params change), but dropdown
                // selections within an action type are value-only. The action editor
                // handles this internally — we always rebuild here since action type
                // changes swap the entire parameter UI.
                onStructuralChange();
            }, onCreateNewSequence);
            actionCard.Add(actionEditor);

            // Timing toggle (macro only) — two radio-style buttons
            if (isMacro)
            {
                var timingContainer = new VisualElement();
                timingContainer.AddToClassList("rule-editor-timing");

                var interruptBtn = new Button();
                interruptBtn.text = "Immediately";
                interruptBtn.AddToClassList("rule-editor-timing-btn");
                interruptBtn.AddToClassList("rule-editor-timing-btn-left");

                var afterBtn = new Button();
                afterBtn.text = "After current task";
                afterBtn.AddToClassList("rule-editor-timing-btn");
                afterBtn.AddToClassList("rule-editor-timing-btn-right");

                void UpdateTimingSelection(bool finishCurrent)
                {
                    rule.FinishCurrentSequence = finishCurrent;
                    PersistRule();
                    interruptBtn.EnableInClassList("rule-editor-timing-active", !finishCurrent);
                    afterBtn.EnableInClassList("rule-editor-timing-active", finishCurrent);
                }

                interruptBtn.clicked += () => UpdateTimingSelection(false);
                afterBtn.clicked += () => UpdateTimingSelection(true);

                // Set initial state
                interruptBtn.EnableInClassList("rule-editor-timing-active", !rule.FinishCurrentSequence);
                afterBtn.EnableInClassList("rule-editor-timing-active", rule.FinishCurrentSequence);

                timingContainer.Add(interruptBtn);
                timingContainer.Add(afterBtn);
                actionCard.Add(timingContainer);
            }

            row.Add(actionCard);

            return row;
        }

        // ─── Condition Editor ────────────────────────────

        private static VisualElement BuildConditionEditor(
            Condition condition, GameState state, GameSimulation sim,
            Action<Condition> onValueUpdate, Action<Condition> onTypeChange, Action onDelete)
        {
            var container = new VisualElement();
            container.AddToClassList("condition-editor");

            // Condition type dropdown
            var typeChoices = new List<string>
            {
                "Always", "Inventory Full", "Free Slots",
                "Inventory Contains", "Bank Contains",
                "Skill Level", "At Node", "Runner State"
            };
            var typeValues = new List<ConditionType>
            {
                ConditionType.Always, ConditionType.InventoryFull, ConditionType.InventorySlots,
                ConditionType.InventoryContains, ConditionType.BankContains,
                ConditionType.SkillLevel, ConditionType.AtNode, ConditionType.RunnerStateIs
            };

            int currentTypeIndex = typeValues.IndexOf(condition.Type);
            if (currentTypeIndex < 0) currentTypeIndex = 0;

            var typeDropdown = new DropdownField(typeChoices, currentTypeIndex);
            typeDropdown.AddToClassList("condition-type-dropdown");

            // Parameter container (changes based on type)
            var paramContainer = new VisualElement();
            paramContainer.AddToClassList("condition-params");

            void RebuildParams(ConditionType condType, Condition cond)
            {
                paramContainer.Clear();

                switch (condType)
                {
                    case ConditionType.Always:
                    case ConditionType.InventoryFull:
                        // No parameters
                        break;

                    case ConditionType.InventorySlots:
                        AddOperatorAndNumber(paramContainer, cond, onValueUpdate);
                        break;

                    case ConditionType.InventoryContains:
                    case ConditionType.BankContains:
                        AddItemPicker(paramContainer, cond, sim, onValueUpdate);
                        AddOperatorAndNumber(paramContainer, cond, onValueUpdate);
                        break;

                    case ConditionType.SkillLevel:
                        AddSkillPicker(paramContainer, cond, onValueUpdate);
                        AddOperatorAndNumber(paramContainer, cond, onValueUpdate);
                        break;

                    case ConditionType.AtNode:
                        AddNodePicker(paramContainer, cond, state, onValueUpdate);
                        break;

                    case ConditionType.RunnerStateIs:
                        AddStatePicker(paramContainer, cond, onValueUpdate);
                        break;
                }
            }

            RebuildParams(condition.Type, condition);

            typeDropdown.RegisterValueChangedCallback(evt =>
            {
                int idx = typeDropdown.index;
                if (idx >= 0 && idx < typeValues.Count)
                {
                    var newCond = new Condition { Type = typeValues[idx] };
                    // Carry over defaults for the new type
                    if (newCond.Type == ConditionType.InventoryContains ||
                        newCond.Type == ConditionType.BankContains)
                    {
                        newCond.Operator = ComparisonOperator.GreaterOrEqual;
                        newCond.NumericValue = 1;
                    }
                    // Type change is structural — params need to rebuild
                    onTypeChange(newCond);
                }
            });

            // Delete condition button
            var deleteCondBtn = new Button(() => onDelete());
            deleteCondBtn.text = "\u2212"; // minus sign, visually distinct from rule ×
            deleteCondBtn.AddToClassList("condition-delete-btn");

            container.Add(typeDropdown);
            container.Add(paramContainer);
            container.Add(deleteCondBtn);

            return container;
        }

        private static readonly string[] OpLabels = { ">", "\u2265", "<", "\u2264", "=", "\u2260" };
        private static readonly ComparisonOperator[] OpValues =
        {
            ComparisonOperator.GreaterThan, ComparisonOperator.GreaterOrEqual,
            ComparisonOperator.LessThan, ComparisonOperator.LessOrEqual,
            ComparisonOperator.Equal, ComparisonOperator.NotEqual
        };

        private static string GetOpLabel(ComparisonOperator op)
        {
            int idx = System.Array.IndexOf(OpValues, op);
            return idx >= 0 ? OpLabels[idx] : "?";
        }

        private static void AddOperatorAndNumber(VisualElement parent,
            Condition condition, Action<Condition> onValueUpdate)
        {
            var opBtn = new Button();
            opBtn.text = GetOpLabel(condition.Operator);
            opBtn.AddToClassList("condition-op-button");
            // Wrap button in a container so the absolute popup anchors to it
            var opWrapper = new VisualElement();
            opWrapper.AddToClassList("condition-op-wrapper");

            opBtn.clicked += () =>
            {
                var existingPopup = opWrapper.Q("op-popup");
                if (existingPopup != null) { existingPopup.RemoveFromHierarchy(); return; }

                var popup = new VisualElement();
                popup.name = "op-popup";
                popup.AddToClassList("condition-op-popup");

                for (int i = 0; i < OpLabels.Length; i++)
                {
                    int capturedIndex = i;
                    var optionBtn = new Button(() =>
                    {
                        condition.Operator = OpValues[capturedIndex];
                        opBtn.text = OpLabels[capturedIndex];
                        onValueUpdate(condition);
                        popup.RemoveFromHierarchy();
                    });
                    optionBtn.text = OpLabels[capturedIndex];
                    optionBtn.AddToClassList("condition-op-option");
                    if (OpValues[capturedIndex] == condition.Operator)
                        optionBtn.AddToClassList("condition-op-option-active");
                    popup.Add(optionBtn);
                }

                opWrapper.Add(popup);
            };

            opWrapper.Add(opBtn);

            var numField = new IntegerField();
            numField.AddToClassList("condition-num-field");
            numField.SetValueWithoutNotify((int)condition.NumericValue);
            numField.RegisterValueChangedCallback(evt =>
            {
                condition.NumericValue = evt.newValue;
                onValueUpdate(condition); // value-only — no rebuild
            });

            parent.Add(opWrapper);
            parent.Add(numField);
        }

        private static void AddItemPicker(VisualElement parent,
            Condition condition, GameSimulation sim, Action<Condition> onValueUpdate)
        {
            var choices = new List<string>();
            var ids = new List<string>();

            if (sim.ItemRegistry != null)
            {
                foreach (var item in sim.ItemRegistry.AllItemDefinitions)
                {
                    ids.Add(item.Id);
                    choices.Add(item.Name ?? item.Id);
                }
            }

            if (choices.Count == 0)
            {
                choices.Add("(no items)");
                ids.Add("");
            }

            int currentIndex = ids.IndexOf(condition.StringParam);
            if (currentIndex < 0) currentIndex = 0;

            var dropdown = new DropdownField(choices, currentIndex);
            dropdown.AddToClassList("condition-item-dropdown");
            dropdown.RegisterValueChangedCallback(evt =>
            {
                int idx = dropdown.index;
                if (idx >= 0 && idx < ids.Count)
                {
                    condition.StringParam = ids[idx];
                    onValueUpdate(condition); // value-only
                }
            });
            parent.Add(dropdown);
        }

        private static void AddSkillPicker(VisualElement parent,
            Condition condition, Action<Condition> onValueUpdate)
        {
            var choices = new List<string>();
            var values = new List<int>();

            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                choices.Add(AutomationUIHelpers.FormatSkillName((SkillType)i));
                values.Add(i);
            }

            int currentIndex = values.IndexOf(condition.IntParam);
            if (currentIndex < 0) currentIndex = 0;

            var dropdown = new DropdownField(choices, currentIndex);
            dropdown.AddToClassList("condition-skill-dropdown");
            dropdown.RegisterValueChangedCallback(evt =>
            {
                int idx = dropdown.index;
                if (idx >= 0 && idx < values.Count)
                {
                    condition.IntParam = values[idx];
                    onValueUpdate(condition); // value-only
                }
            });
            parent.Add(dropdown);
        }

        private static void AddNodePicker(VisualElement parent,
            Condition condition, GameState state, Action<Condition> onValueUpdate)
        {
            var choices = new List<string>();
            var ids = new List<string>();

            if (state?.Map?.Nodes != null)
            {
                foreach (var node in state.Map.Nodes)
                {
                    ids.Add(node.Id);
                    choices.Add(node.Name ?? node.Id);
                }
            }

            if (choices.Count == 0)
            {
                choices.Add("(no nodes)");
                ids.Add("");
            }

            int currentIndex = ids.IndexOf(condition.StringParam);
            if (currentIndex < 0) currentIndex = 0;

            var dropdown = new DropdownField(choices, currentIndex);
            dropdown.AddToClassList("condition-node-dropdown");
            dropdown.RegisterValueChangedCallback(evt =>
            {
                int idx = dropdown.index;
                if (idx >= 0 && idx < ids.Count)
                {
                    condition.StringParam = ids[idx];
                    onValueUpdate(condition); // value-only
                }
            });
            parent.Add(dropdown);
        }

        private static void AddStatePicker(VisualElement parent,
            Condition condition, Action<Condition> onValueUpdate)
        {
            var choices = new List<string> { "Idle", "Traveling", "Gathering", "Depositing" };
            var values = new List<int>
            {
                (int)RunnerState.Idle, (int)RunnerState.Traveling,
                (int)RunnerState.Gathering, (int)RunnerState.Depositing
            };

            int currentIndex = values.IndexOf(condition.IntParam);
            if (currentIndex < 0) currentIndex = 0;

            var dropdown = new DropdownField(choices, currentIndex);
            dropdown.AddToClassList("condition-state-dropdown");
            dropdown.RegisterValueChangedCallback(evt =>
            {
                int idx = dropdown.index;
                if (idx >= 0 && idx < values.Count)
                {
                    condition.IntParam = values[idx];
                    onValueUpdate(condition); // value-only
                }
            });
            parent.Add(dropdown);
        }

        // ─── Action Editor ──────────────────────────────

        private static VisualElement BuildActionEditor(
            AutomationAction action, bool isMacro, GameState state,
            GameSimulation sim, Action<AutomationAction> onUpdate,
            Action<string, Action<string>> onCreateNewSequence = null)
        {
            var container = new VisualElement();
            container.AddToClassList("action-editor");

            if (isMacro)
            {
                // Flat dropdown: "Go Idle" + all library sequences by name + optional "+ New Sequence..."
                var choices = new List<string> { "Go Idle" };
                var seqIds = new List<string> { null }; // null = Idle

                if (state?.TaskSequenceLibrary != null)
                {
                    foreach (var seq in state.TaskSequenceLibrary)
                    {
                        choices.Add(seq.Name ?? seq.Id);
                        seqIds.Add(seq.Id);
                    }
                }

                // "+ New Sequence..." option (only when navigation callback is available)
                int newSequenceIndex = -1;
                if (onCreateNewSequence != null)
                {
                    newSequenceIndex = choices.Count;
                    choices.Add("+ New Sequence...");
                    seqIds.Add("__new__");
                }

                int currentIndex = 0; // default to Go Idle
                if (action.Type == ActionType.AssignSequence && !string.IsNullOrEmpty(action.StringParam))
                    currentIndex = seqIds.IndexOf(action.StringParam);
                if (currentIndex < 0) currentIndex = 0;

                var dropdown = new DropdownField(choices, currentIndex);
                dropdown.AddToClassList("action-type-dropdown");

                dropdown.RegisterValueChangedCallback(evt =>
                {
                    int idx = dropdown.index;
                    if (idx >= 0 && idx < seqIds.Count)
                    {
                        if (idx == newSequenceIndex && onCreateNewSequence != null)
                        {
                            string newId = sim.CommandCreateTaskSequence();
                            Action<string> wireAction = (id) => onUpdate(AutomationAction.AssignSequence(id));
                            dropdown.SetValueWithoutNotify(choices[currentIndex]);
                            onCreateNewSequence(newId, wireAction);
                        }
                        else if (seqIds[idx] == null)
                        {
                            onUpdate(AutomationAction.Idle());
                        }
                        else
                        {
                            onUpdate(AutomationAction.AssignSequence(seqIds[idx]));
                        }
                    }
                });

                container.Add(dropdown);
            }
            else
            {
                // Micro actions: Gather [Resource], Finish Task
                var typeChoices = new List<string> { "Gather Here", "Finish Task" };
                var typeValues = new List<ActionType> { ActionType.GatherHere, ActionType.FinishTask };

                int currentIndex = typeValues.IndexOf(action.Type);
                if (currentIndex < 0) currentIndex = 0;

                var typeDropdown = new DropdownField(typeChoices, currentIndex);
                typeDropdown.AddToClassList("action-type-dropdown");

                var paramContainer = new VisualElement();
                paramContainer.AddToClassList("action-params");

                void RebuildMicroParams(ActionType actionType)
                {
                    paramContainer.Clear();
                    if (actionType == ActionType.GatherHere)
                    {
                        if (!string.IsNullOrEmpty(action.StringParam))
                        {
                            var itemChoices = new List<string>();
                            var itemIds = new List<string>();
                            if (sim.ItemRegistry != null)
                            {
                                foreach (var item in sim.ItemRegistry.AllItemDefinitions)
                                {
                                    itemIds.Add(item.Id);
                                    itemChoices.Add(item.Name ?? item.Id);
                                }
                            }
                            itemChoices.Add("(positional: first)");
                            itemIds.Add("");

                            int itemIndex = itemIds.IndexOf(action.StringParam);
                            if (itemIndex < 0)
                                itemIndex = itemChoices.Count - 1;

                            var itemDropdown = new DropdownField(itemChoices, itemIndex);
                            itemDropdown.AddToClassList("action-item-dropdown");
                            itemDropdown.RegisterValueChangedCallback(evt =>
                            {
                                int idx = itemDropdown.index;
                                if (idx >= 0 && idx < itemIds.Count)
                                {
                                    if (string.IsNullOrEmpty(itemIds[idx]))
                                        onUpdate(AutomationAction.GatherHere(0));
                                    else
                                        onUpdate(new AutomationAction
                                        {
                                            Type = ActionType.GatherHere,
                                            StringParam = itemIds[idx],
                                        });
                                }
                            });
                            paramContainer.Add(itemDropdown);
                        }
                        else
                        {
                            var indexField = new IntegerField();
                            indexField.AddToClassList("action-index-field");
                            indexField.SetValueWithoutNotify(action.IntParam);
                            indexField.RegisterValueChangedCallback(evt =>
                            {
                                onUpdate(AutomationAction.GatherHere(evt.newValue));
                            });
                            paramContainer.Add(indexField);

                            var switchBtn = new Button(() =>
                            {
                                string defaultItem = "";
                                if (sim.ItemRegistry != null)
                                {
                                    foreach (var item in sim.ItemRegistry.AllItemDefinitions)
                                    {
                                        defaultItem = item.Id;
                                        break;
                                    }
                                }
                                if (!string.IsNullOrEmpty(defaultItem))
                                {
                                    onUpdate(new AutomationAction
                                    {
                                        Type = ActionType.GatherHere,
                                        StringParam = defaultItem,
                                    });
                                }
                            });
                            switchBtn.text = "Pick Item";
                            switchBtn.AddToClassList("action-switch-btn");
                            paramContainer.Add(switchBtn);
                        }
                    }
                }

                RebuildMicroParams(action.Type);

                typeDropdown.RegisterValueChangedCallback(evt =>
                {
                    int idx = typeDropdown.index;
                    if (idx >= 0 && idx < typeValues.Count)
                    {
                        switch (typeValues[idx])
                        {
                            case ActionType.GatherHere:
                                onUpdate(AutomationAction.GatherHere(0));
                                break;
                            case ActionType.FinishTask:
                                onUpdate(AutomationAction.FinishTask());
                                break;
                        }
                    }
                });

                container.Add(typeDropdown);
                container.Add(paramContainer);
            }

            return container;
        }
    }
}

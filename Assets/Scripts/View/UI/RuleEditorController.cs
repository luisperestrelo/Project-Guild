using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Reusable component that renders a single automation rule as an editable row.
    /// Provides inline editing via dropdowns for condition type, parameters, action type,
    /// and timing. Supports move up/down, toggle enabled, and delete.
    ///
    /// Used by both MacroRulesetEditorController and MicroRulesetEditorController.
    /// All changes go through GameSimulation commands.
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
            Action onChanged,
            Action<string, Action<string>> onCreateNewSequence = null)
        {
            var state = sim.CurrentGameState;
            var row = new VisualElement();
            row.AddToClassList("rule-editor-row");
            if (!rule.Enabled) row.AddToClassList("rule-editor-disabled");

            // ─── Enable toggle ───
            var enableBtn = new Button(() =>
            {
                sim.CommandToggleRuleEnabled(rulesetId, ruleIndex);
                onChanged();
            });
            enableBtn.text = rule.Enabled ? "\u2713" : "\u2717";
            enableBtn.AddToClassList("rule-editor-toggle");
            row.Add(enableBtn);

            // ─── Index label ───
            var indexLabel = new Label($"{ruleIndex + 1}.");
            indexLabel.AddToClassList("rule-editor-index");
            indexLabel.pickingMode = PickingMode.Ignore;
            row.Add(indexLabel);

            // ─── "IF" label ───
            var ifLabel = new Label("IF");
            ifLabel.AddToClassList("rule-editor-keyword");
            ifLabel.pickingMode = PickingMode.Ignore;
            row.Add(ifLabel);

            // ─── Condition(s) ───
            var conditionsContainer = new VisualElement();
            conditionsContainer.AddToClassList("rule-editor-conditions");

            for (int ci = 0; ci < rule.Conditions.Count; ci++)
            {
                if (ci > 0)
                {
                    var andLabel = new Label("AND");
                    andLabel.AddToClassList("rule-editor-keyword");
                    andLabel.pickingMode = PickingMode.Ignore;
                    conditionsContainer.Add(andLabel);
                }

                int condIndex = ci;
                var condRow = BuildConditionEditor(rule.Conditions[ci], state, sim, (updatedCond) =>
                {
                    rule.Conditions[condIndex] = updatedCond;
                    sim.CommandUpdateRule(rulesetId, ruleIndex, rule);
                    onChanged();
                }, () =>
                {
                    // Delete condition
                    if (rule.Conditions.Count > 1)
                    {
                        rule.Conditions.RemoveAt(condIndex);
                        sim.CommandUpdateRule(rulesetId, ruleIndex, rule);
                        onChanged();
                    }
                });
                conditionsContainer.Add(condRow);
            }

            // + AND button
            var addCondBtn = new Button(() =>
            {
                rule.Conditions.Add(Condition.Always());
                sim.CommandUpdateRule(rulesetId, ruleIndex, rule);
                onChanged();
            });
            addCondBtn.text = "+ AND";
            addCondBtn.AddToClassList("rule-editor-add-cond");
            conditionsContainer.Add(addCondBtn);

            row.Add(conditionsContainer);

            // ─── "THEN" label ───
            var thenLabel = new Label("THEN");
            thenLabel.AddToClassList("rule-editor-keyword");
            thenLabel.pickingMode = PickingMode.Ignore;
            row.Add(thenLabel);

            // ─── Action picker ───
            var actionEditor = BuildActionEditor(rule.Action, isMacro, state, sim, (updatedAction) =>
            {
                rule.Action = updatedAction;
                sim.CommandUpdateRule(rulesetId, ruleIndex, rule);
                onChanged();
            }, onCreateNewSequence);
            row.Add(actionEditor);

            // ─── Timing toggle (macro only) ───
            if (isMacro)
            {
                var timingChoices = new List<string> { "Immediately", "Finish Current Seq" };
                var timingDropdown = new DropdownField(timingChoices,
                    rule.FinishCurrentSequence ? 1 : 0);
                timingDropdown.AddToClassList("rule-editor-timing");
                timingDropdown.RegisterValueChangedCallback(evt =>
                {
                    rule.FinishCurrentSequence = timingDropdown.index == 1;
                    sim.CommandUpdateRule(rulesetId, ruleIndex, rule);
                    onChanged();
                });
                row.Add(timingDropdown);
            }

            // ─── Move / Delete buttons ───
            var buttonsContainer = new VisualElement();
            buttonsContainer.AddToClassList("rule-editor-buttons");

            var moveUpBtn = new Button(() =>
            {
                if (ruleIndex > 0)
                {
                    sim.CommandMoveRuleInRuleset(rulesetId, ruleIndex, ruleIndex - 1);
                    onChanged();
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
                    onChanged();
                }
            });
            moveDownBtn.text = "\u25bc";
            moveDownBtn.AddToClassList("rule-editor-move-btn");
            buttonsContainer.Add(moveDownBtn);

            var deleteBtn = new Button(() =>
            {
                sim.CommandRemoveRuleFromRuleset(rulesetId, ruleIndex);
                onChanged();
            });
            deleteBtn.text = "\u00d7";
            deleteBtn.AddToClassList("rule-editor-delete-btn");
            buttonsContainer.Add(deleteBtn);

            row.Add(buttonsContainer);

            return row;
        }

        // ─── Condition Editor ────────────────────────────

        private static VisualElement BuildConditionEditor(
            Condition condition, GameState state, GameSimulation sim,
            Action<Condition> onUpdate, Action onDelete)
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

            void RebuildParams(ConditionType condType)
            {
                paramContainer.Clear();

                switch (condType)
                {
                    case ConditionType.Always:
                    case ConditionType.InventoryFull:
                        // No parameters
                        break;

                    case ConditionType.InventorySlots:
                        AddOperatorAndNumber(paramContainer, condition, onUpdate);
                        break;

                    case ConditionType.InventoryContains:
                    case ConditionType.BankContains:
                        AddItemPicker(paramContainer, condition, sim, onUpdate);
                        AddOperatorAndNumber(paramContainer, condition, onUpdate);
                        break;

                    case ConditionType.SkillLevel:
                        AddSkillPicker(paramContainer, condition, onUpdate);
                        AddOperatorAndNumber(paramContainer, condition, onUpdate);
                        break;

                    case ConditionType.AtNode:
                        AddNodePicker(paramContainer, condition, state, onUpdate);
                        break;

                    case ConditionType.RunnerStateIs:
                        AddStatePicker(paramContainer, condition, onUpdate);
                        break;
                }
            }

            RebuildParams(condition.Type);

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
                    onUpdate(newCond);
                }
            });

            // Delete condition button (only if there are multiple conditions)
            var deleteCondBtn = new Button(() => onDelete());
            deleteCondBtn.text = "\u00d7";
            deleteCondBtn.AddToClassList("condition-delete-btn");

            container.Add(typeDropdown);
            container.Add(paramContainer);
            container.Add(deleteCondBtn);

            return container;
        }

        private static void AddOperatorAndNumber(VisualElement parent,
            Condition condition, Action<Condition> onUpdate)
        {
            var opChoices = new List<string> { ">", ">=", "<", "<=", "=", "!=" };
            var opValues = new List<ComparisonOperator>
            {
                ComparisonOperator.GreaterThan, ComparisonOperator.GreaterOrEqual,
                ComparisonOperator.LessThan, ComparisonOperator.LessOrEqual,
                ComparisonOperator.Equal, ComparisonOperator.NotEqual
            };

            int currentOp = opValues.IndexOf(condition.Operator);
            if (currentOp < 0) currentOp = 1; // default to >=

            var opDropdown = new DropdownField(opChoices, currentOp);
            opDropdown.AddToClassList("condition-op-dropdown");
            opDropdown.RegisterValueChangedCallback(evt =>
            {
                int idx = opDropdown.index;
                if (idx >= 0 && idx < opValues.Count)
                {
                    condition.Operator = opValues[idx];
                    onUpdate(condition);
                }
            });

            var numField = new IntegerField();
            numField.AddToClassList("condition-num-field");
            numField.SetValueWithoutNotify((int)condition.NumericValue);
            numField.RegisterValueChangedCallback(evt =>
            {
                condition.NumericValue = evt.newValue;
                onUpdate(condition);
            });

            parent.Add(opDropdown);
            parent.Add(numField);
        }

        private static void AddItemPicker(VisualElement parent,
            Condition condition, GameSimulation sim, Action<Condition> onUpdate)
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
                    onUpdate(condition);
                }
            });
            parent.Add(dropdown);
        }

        private static void AddSkillPicker(VisualElement parent,
            Condition condition, Action<Condition> onUpdate)
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
                    onUpdate(condition);
                }
            });
            parent.Add(dropdown);
        }

        private static void AddNodePicker(VisualElement parent,
            Condition condition, GameState state, Action<Condition> onUpdate)
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
                    onUpdate(condition);
                }
            });
            parent.Add(dropdown);
        }

        private static void AddStatePicker(VisualElement parent,
            Condition condition, Action<Condition> onUpdate)
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
                    onUpdate(condition);
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
                            // Create a new sequence and request navigation
                            string newId = sim.CommandCreateTaskSequence();

                            // The wire action will be executed on "Done" —
                            // it connects the selected sequence to the macro rule's action.
                            // The id parameter may differ from newId if the user selected
                            // an existing sequence instead.
                            Action<string> wireAction = (id) => onUpdate(AutomationAction.AssignSequence(id));

                            // Reset dropdown to previous value (the wire happens on Done, not now)
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
                        // Item picker or positional index
                        if (!string.IsNullOrEmpty(action.StringParam))
                        {
                            // Item-based resolution
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
                            {
                                itemIndex = itemChoices.Count - 1; // fall back to positional
                            }

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
                            // Positional index
                            var indexField = new IntegerField();
                            indexField.AddToClassList("action-index-field");
                            indexField.SetValueWithoutNotify(action.IntParam);
                            indexField.RegisterValueChangedCallback(evt =>
                            {
                                onUpdate(AutomationAction.GatherHere(evt.newValue));
                            });
                            paramContainer.Add(indexField);

                            // Switch to item-based button
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

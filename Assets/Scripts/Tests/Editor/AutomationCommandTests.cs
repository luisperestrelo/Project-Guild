using System.Collections.Generic;
using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;
using ProjectGuild.View.UI;

namespace ProjectGuild.Tests
{
    /// <summary>
    /// Tests for automation mutation commands (Batch A):
    /// - Ruleset mutations (add/remove/move/toggle/update/reset/rename)
    /// - Task sequence mutations (add/remove/move step, loop, micro, rename)
    /// - Runner step index adjustment during live sequence editing
    /// - Query helpers (count/names of runners using templates)
    /// - Natural language formatting (AutomationUIHelpers)
    /// </summary>
    [TestFixture]
    public class AutomationCommandTests
    {
        private GameSimulation _sim;
        private Runner _runner;
        private Runner _runner2;
        private SimulationConfig _config;

        private static readonly Simulation.Gathering.GatherableConfig CopperGatherable =
            new Simulation.Gathering.GatherableConfig("copper_ore", SkillType.Mining, 40f, 0.5f);

        private static readonly Simulation.Gathering.GatherableConfig TinGatherable =
            new Simulation.Gathering.GatherableConfig("tin_ore", SkillType.Mining, 40f, 0.5f);

        private void Setup(string startNodeId = "mine")
        {
            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
                    new ItemDefinition("tin_ore", "Tin Ore", ItemCategory.Ore),
                    new ItemDefinition("pine_log", "Pine Log", ItemCategory.Log),
                },
            };
            _sim = new GameSimulation(_config, tickRate: 10f);

            var defs = new[]
            {
                new RunnerFactory.RunnerDefinition { Name = "Alpha" }
                    .WithSkill(SkillType.Mining, 1),
                new RunnerFactory.RunnerDefinition { Name = "Beta" }
                    .WithSkill(SkillType.Mining, 1),
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Guild Hall");
            map.AddNode("mine", "Copper Mine", 0f, 0f, CopperGatherable, TinGatherable);
            map.AddNode("forest", "Pine Forest", 10f, 15f);
            map.AddEdge("hub", "mine", 8f);
            map.AddEdge("hub", "forest", 7f);
            map.Initialize();

            _sim.StartNewGame(defs, map, startNodeId);
            _runner = _sim.CurrentGameState.Runners[0];
            _runner2 = _sim.CurrentGameState.Runners[1];
        }

        private Ruleset CreateTestMacroRuleset(string id = null, string name = "Test Macro")
        {
            var ruleset = new Ruleset
            {
                Id = id,
                Name = name,
                Category = RulesetCategory.General,
            };
            ruleset.Rules.Add(new Rule
            {
                Label = "Rule A",
                Conditions = { Condition.Always() },
                Action = AutomationAction.AssignSequence("seq-mine"),
                Enabled = true,
            });
            ruleset.Rules.Add(new Rule
            {
                Label = "Rule B",
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 100) },
                Action = AutomationAction.AssignSequence("seq-forest"),
                Enabled = true,
            });
            return ruleset;
        }

        private Ruleset CreateTestMicroRuleset(string id = null, string name = "Test Micro")
        {
            var ruleset = new Ruleset
            {
                Id = id,
                Name = name,
                Category = RulesetCategory.Gathering,
            };
            ruleset.Rules.Add(new Rule
            {
                Label = "Deposit when full",
                Conditions = { Condition.InventoryFull() },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
            });
            ruleset.Rules.Add(new Rule
            {
                Label = "Gather resource",
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });
            return ruleset;
        }

        // ─── Ruleset: Add Rule ───────────────────────────────────────

        [Test]
        public void AddRuleToRuleset_Append_AddsAtEnd()
        {
            Setup();
            var macro = CreateTestMacroRuleset();
            _sim.CommandCreateMacroRuleset(macro);

            var newRule = new Rule
            {
                Label = "Rule C",
                Conditions = { Condition.Always() },
                Action = AutomationAction.Idle(),
                Enabled = true,
            };

            _sim.CommandAddRuleToRuleset(macro.Id, newRule);

            Assert.AreEqual(3, macro.Rules.Count);
            Assert.AreEqual("Rule C", macro.Rules[2].Label);
        }

        [Test]
        public void AddRuleToRuleset_AtIndex_InsertsCorrectly()
        {
            Setup();
            var macro = CreateTestMacroRuleset();
            _sim.CommandCreateMacroRuleset(macro);

            var newRule = new Rule
            {
                Label = "Inserted",
                Conditions = { Condition.Always() },
                Action = AutomationAction.Idle(),
                Enabled = true,
            };

            _sim.CommandAddRuleToRuleset(macro.Id, newRule, 1);

            Assert.AreEqual(3, macro.Rules.Count);
            Assert.AreEqual("Rule A", macro.Rules[0].Label);
            Assert.AreEqual("Inserted", macro.Rules[1].Label);
            Assert.AreEqual("Rule B", macro.Rules[2].Label);
        }

        [Test]
        public void AddRuleToRuleset_WorksForMicroRuleset()
        {
            Setup();
            var micro = CreateTestMicroRuleset();
            _sim.CommandCreateMicroRuleset(micro);

            var newRule = new Rule
            {
                Label = "Gather tin",
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(1),
                Enabled = true,
            };

            _sim.CommandAddRuleToRuleset(micro.Id, newRule);

            Assert.AreEqual(3, micro.Rules.Count);
            Assert.AreEqual("Gather tin", micro.Rules[2].Label);
        }

        // ─── Ruleset: Remove Rule ────────────────────────────────────

        [Test]
        public void RemoveRuleFromRuleset_RemovesCorrectRule()
        {
            Setup();
            var macro = CreateTestMacroRuleset();
            _sim.CommandCreateMacroRuleset(macro);

            _sim.CommandRemoveRuleFromRuleset(macro.Id, 0);

            Assert.AreEqual(1, macro.Rules.Count);
            Assert.AreEqual("Rule B", macro.Rules[0].Label);
        }

        [Test]
        public void RemoveRuleFromRuleset_InvalidIndex_NoOp()
        {
            Setup();
            var macro = CreateTestMacroRuleset();
            _sim.CommandCreateMacroRuleset(macro);

            _sim.CommandRemoveRuleFromRuleset(macro.Id, -1);
            _sim.CommandRemoveRuleFromRuleset(macro.Id, 99);

            Assert.AreEqual(2, macro.Rules.Count, "Invalid indices should not change anything");
        }

        // ─── Ruleset: Move Rule ──────────────────────────────────────

        [Test]
        public void MoveRuleInRuleset_SwapsOrder()
        {
            Setup();
            var macro = CreateTestMacroRuleset();
            _sim.CommandCreateMacroRuleset(macro);

            _sim.CommandMoveRuleInRuleset(macro.Id, 0, 1);

            Assert.AreEqual("Rule B", macro.Rules[0].Label);
            Assert.AreEqual("Rule A", macro.Rules[1].Label);
        }

        [Test]
        public void MoveRuleInRuleset_SameIndex_NoOp()
        {
            Setup();
            var macro = CreateTestMacroRuleset();
            _sim.CommandCreateMacroRuleset(macro);

            _sim.CommandMoveRuleInRuleset(macro.Id, 0, 0);

            Assert.AreEqual("Rule A", macro.Rules[0].Label);
            Assert.AreEqual("Rule B", macro.Rules[1].Label);
        }

        // ─── Ruleset: Toggle Enabled ─────────────────────────────────

        [Test]
        public void ToggleRuleEnabled_FlipsEnabledFlag()
        {
            Setup();
            var macro = CreateTestMacroRuleset();
            _sim.CommandCreateMacroRuleset(macro);

            Assert.IsTrue(macro.Rules[0].Enabled);

            _sim.CommandToggleRuleEnabled(macro.Id, 0);
            Assert.IsFalse(macro.Rules[0].Enabled);

            _sim.CommandToggleRuleEnabled(macro.Id, 0);
            Assert.IsTrue(macro.Rules[0].Enabled);
        }

        // ─── Ruleset: Update Rule ────────────────────────────────────

        [Test]
        public void UpdateRule_ReplacesRuleAtIndex()
        {
            Setup();
            var macro = CreateTestMacroRuleset();
            _sim.CommandCreateMacroRuleset(macro);

            var updated = new Rule
            {
                Label = "Updated Rule",
                Conditions = { Condition.InventoryFull() },
                Action = AutomationAction.AssignSequence("seq-return"),
                Enabled = false,
            };

            _sim.CommandUpdateRule(macro.Id, 0, updated);

            Assert.AreEqual("Updated Rule", macro.Rules[0].Label);
            Assert.AreEqual(ActionType.AssignSequence, macro.Rules[0].Action.Type);
            Assert.AreEqual("seq-return", macro.Rules[0].Action.StringParam);
            Assert.IsFalse(macro.Rules[0].Enabled);
            Assert.AreEqual("Rule B", macro.Rules[1].Label, "Other rules should be unchanged");
        }

        // ─── Ruleset: Reset to Default ───────────────────────────────

        [Test]
        public void ResetRulesetToDefault_MacroRuleset_ClearsRules()
        {
            Setup();
            var macro = CreateTestMacroRuleset();
            _sim.CommandCreateMacroRuleset(macro);
            Assert.AreEqual(2, macro.Rules.Count);

            _sim.CommandResetRulesetToDefault(macro.Id);

            // Default macro has 0 rules
            Assert.AreEqual(0, macro.Rules.Count);
        }

        [Test]
        public void ResetRulesetToDefault_MicroRuleset_RestoresDefaults()
        {
            Setup();
            var micro = new Ruleset
            {
                Name = "Custom Micro",
                Category = RulesetCategory.Gathering,
            };
            // Start with only 1 rule
            micro.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });
            _sim.CommandCreateMicroRuleset(micro);
            Assert.AreEqual(1, micro.Rules.Count);

            _sim.CommandResetRulesetToDefault(micro.Id);

            // Default micro has 2 rules (InventoryFull→FinishTask, Always→GatherHere)
            Assert.AreEqual(2, micro.Rules.Count);
            Assert.AreEqual(ActionType.FinishTask, micro.Rules[0].Action.Type);
            Assert.AreEqual(ActionType.GatherHere, micro.Rules[1].Action.Type);
        }

        // ─── Ruleset: Rename ─────────────────────────────────────────

        [Test]
        public void RenameRuleset_UpdatesName()
        {
            Setup();
            var macro = CreateTestMacroRuleset();
            _sim.CommandCreateMacroRuleset(macro);

            _sim.CommandRenameRuleset(macro.Id, "Renamed Macro");

            Assert.AreEqual("Renamed Macro", macro.Name);
        }

        [Test]
        public void RenameRuleset_TrimsWhitespace()
        {
            Setup();
            var macro = CreateTestMacroRuleset();
            _sim.CommandCreateMacroRuleset(macro);

            _sim.CommandRenameRuleset(macro.Id, "  Trimmed  ");

            Assert.AreEqual("Trimmed", macro.Name);
        }

        // ─── FindRulesetInAnyLibrary ─────────────────────────────────

        [Test]
        public void FindRulesetInAnyLibrary_FindsMacro()
        {
            Setup();
            var macro = CreateTestMacroRuleset();
            _sim.CommandCreateMacroRuleset(macro);

            var (found, isMacro) = _sim.FindRulesetInAnyLibrary(macro.Id);

            Assert.AreSame(macro, found);
            Assert.IsTrue(isMacro);
        }

        [Test]
        public void FindRulesetInAnyLibrary_FindsMicro()
        {
            Setup();
            var micro = CreateTestMicroRuleset();
            _sim.CommandCreateMicroRuleset(micro);

            var (found, isMacro) = _sim.FindRulesetInAnyLibrary(micro.Id);

            Assert.AreSame(micro, found);
            Assert.IsFalse(isMacro);
        }

        [Test]
        public void FindRulesetInAnyLibrary_NullId_ReturnsNull()
        {
            Setup();
            var (found, _) = _sim.FindRulesetInAnyLibrary(null);
            Assert.IsNull(found);
        }

        // ─── Task Sequence: Add Step ─────────────────────────────────

        [Test]
        public void AddStepToTaskSequence_Append()
        {
            Setup("mine");
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.CommandCreateTaskSequence(seq);
            int originalCount = seq.Steps.Count;

            var newStep = new TaskStep(TaskStepType.Work, microRulesetId: DefaultRulesets.DefaultMicroId);
            _sim.CommandAddStepToTaskSequence(seq.Id, newStep);

            Assert.AreEqual(originalCount + 1, seq.Steps.Count);
            Assert.AreEqual(TaskStepType.Work, seq.Steps[seq.Steps.Count - 1].Type);
        }

        [Test]
        public void AddStepToTaskSequence_InsertBeforeCurrent_AdjustsRunnerIndex()
        {
            Setup("mine");
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, seq);

            // Advance runner to step 1 (Work)
            // Runner starts at mine, TravelTo mine is instant, so it advances
            // Actually, runner is already at mine, so TravelTo is skip → Work step
            // Let me check: AssignRunner sets index=0, calls AdvanceMacroStep.
            // Step 0 is TravelTo mine, runner is already at mine → skip → advance to 1 (Work).
            // So runner should be gathering now at step index 1.
            Assert.AreEqual(1, _runner.TaskSequenceCurrentStepIndex,
                "Runner should have advanced to Work step (index 1)");

            // Insert a step before current (at index 0)
            var newStep = new TaskStep(TaskStepType.TravelTo, "forest");
            _sim.CommandAddStepToTaskSequence(seq.Id, newStep, 0);

            Assert.AreEqual(2, _runner.TaskSequenceCurrentStepIndex,
                "Runner index should increment when step inserted before current");
        }

        [Test]
        public void AddStepToTaskSequence_InsertAfterCurrent_NoIndexChange()
        {
            Setup("mine");
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, seq);

            int indexBefore = _runner.TaskSequenceCurrentStepIndex;

            // Insert after current
            var newStep = new TaskStep(TaskStepType.Deposit);
            _sim.CommandAddStepToTaskSequence(seq.Id, newStep, 3);

            Assert.AreEqual(indexBefore, _runner.TaskSequenceCurrentStepIndex,
                "Runner index should not change when step inserted after current");
        }

        // ─── Task Sequence: Remove Step ──────────────────────────────

        [Test]
        public void RemoveStepFromTaskSequence_RemoveBeforeCurrent_DecrementsIndex()
        {
            Setup("mine");
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, seq);

            // Runner is at step 1 (Work). Remove step 0 (TravelTo mine).
            Assert.AreEqual(1, _runner.TaskSequenceCurrentStepIndex);

            _sim.CommandRemoveStepFromTaskSequence(seq.Id, 0);

            Assert.AreEqual(0, _runner.TaskSequenceCurrentStepIndex,
                "Runner index should decrement when step removed before current");
        }

        [Test]
        public void RemoveStepFromTaskSequence_RemoveAtCurrent_KeepsIndex()
        {
            Setup("mine");
            var seq = new TaskSequence
            {
                Name = "Test",
                Loop = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "mine"),
                    new TaskStep(TaskStepType.TravelTo, "forest"),
                    new TaskStep(TaskStepType.TravelTo, "hub"),
                },
            };
            _sim.CommandCreateTaskSequence(seq);

            // Manually set runner to use this sequence at step 1
            _runner.TaskSequenceId = seq.Id;
            _runner.TaskSequenceCurrentStepIndex = 1;

            _sim.CommandRemoveStepFromTaskSequence(seq.Id, 1);

            // Next step (TravelTo hub) slides into position 1; index stays at 1
            Assert.AreEqual(1, _runner.TaskSequenceCurrentStepIndex,
                "Index should stay at same position (next step slides in)");
            Assert.AreEqual("hub", seq.Steps[1].TargetNodeId,
                "The hub step should now be at index 1");
        }

        [Test]
        public void RemoveStepFromTaskSequence_RemoveLastStepAtCurrent_ClampsIndex()
        {
            Setup("mine");
            var seq = new TaskSequence
            {
                Name = "Test",
                Loop = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "mine"),
                    new TaskStep(TaskStepType.TravelTo, "hub"),
                },
            };
            _sim.CommandCreateTaskSequence(seq);

            _runner.TaskSequenceId = seq.Id;
            _runner.TaskSequenceCurrentStepIndex = 1;

            _sim.CommandRemoveStepFromTaskSequence(seq.Id, 1);

            Assert.AreEqual(0, _runner.TaskSequenceCurrentStepIndex,
                "Index should clamp to last valid index when removed step was last");
        }

        [Test]
        public void RemoveStepFromTaskSequence_SequenceBecomesEmpty_RunnerGoesIdle()
        {
            Setup("mine");
            var seq = new TaskSequence
            {
                Name = "Single Step",
                Loop = false,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.Deposit),
                },
            };
            _sim.CommandCreateTaskSequence(seq);

            _runner.TaskSequenceId = seq.Id;
            _runner.TaskSequenceCurrentStepIndex = 0;

            _sim.CommandRemoveStepFromTaskSequence(seq.Id, 0);

            Assert.IsNull(_runner.TaskSequenceId, "Runner should have no sequence when it becomes empty");
            Assert.AreEqual(RunnerState.Idle, _runner.State);
            Assert.AreEqual(0, _runner.TaskSequenceCurrentStepIndex);
        }

        [Test]
        public void RemoveStepFromTaskSequence_RemoveAfterCurrent_NoIndexChange()
        {
            Setup("mine");
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.CommandCreateTaskSequence(seq);

            _runner.TaskSequenceId = seq.Id;
            _runner.TaskSequenceCurrentStepIndex = 0;

            int originalIndex = _runner.TaskSequenceCurrentStepIndex;

            // Remove step 2 (TravelTo hub), which is after current step 0
            _sim.CommandRemoveStepFromTaskSequence(seq.Id, 2);

            Assert.AreEqual(originalIndex, _runner.TaskSequenceCurrentStepIndex,
                "Index should not change when step removed after current");
        }

        // ─── Task Sequence: Move Step ────────────────────────────────

        [Test]
        public void MoveStepInTaskSequence_RunnerOnMovedStep_IndexFollows()
        {
            Setup("mine");
            var seq = new TaskSequence
            {
                Name = "Test",
                Loop = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "mine"),
                    new TaskStep(TaskStepType.Work, microRulesetId: DefaultRulesets.DefaultMicroId),
                    new TaskStep(TaskStepType.TravelTo, "hub"),
                    new TaskStep(TaskStepType.Deposit),
                },
            };
            _sim.CommandCreateTaskSequence(seq);

            _runner.TaskSequenceId = seq.Id;
            _runner.TaskSequenceCurrentStepIndex = 1; // on Work step

            // Move Work step from index 1 to index 3
            _sim.CommandMoveStepInTaskSequence(seq.Id, 1, 3);

            Assert.AreEqual(3, _runner.TaskSequenceCurrentStepIndex,
                "Runner index should follow the moved step");
        }

        [Test]
        public void MoveStepInTaskSequence_StepMovedFromBeforeToAfterCurrent_CurrentShiftsDown()
        {
            Setup("mine");
            var seq = new TaskSequence
            {
                Name = "Test",
                Loop = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "mine"),
                    new TaskStep(TaskStepType.Work, microRulesetId: DefaultRulesets.DefaultMicroId),
                    new TaskStep(TaskStepType.TravelTo, "hub"),
                    new TaskStep(TaskStepType.Deposit),
                },
            };
            _sim.CommandCreateTaskSequence(seq);

            _runner.TaskSequenceId = seq.Id;
            _runner.TaskSequenceCurrentStepIndex = 2; // on TravelTo hub

            // Move step 0 (TravelTo mine) to index 3 (Deposit)
            _sim.CommandMoveStepInTaskSequence(seq.Id, 0, 3);

            Assert.AreEqual(1, _runner.TaskSequenceCurrentStepIndex,
                "Current index should shift down when step moved from before to after");
        }

        [Test]
        public void MoveStepInTaskSequence_StepMovedFromAfterToBeforeCurrent_CurrentShiftsUp()
        {
            Setup("mine");
            var seq = new TaskSequence
            {
                Name = "Test",
                Loop = true,
                Steps = new List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "mine"),
                    new TaskStep(TaskStepType.Work, microRulesetId: DefaultRulesets.DefaultMicroId),
                    new TaskStep(TaskStepType.TravelTo, "hub"),
                    new TaskStep(TaskStepType.Deposit),
                },
            };
            _sim.CommandCreateTaskSequence(seq);

            _runner.TaskSequenceId = seq.Id;
            _runner.TaskSequenceCurrentStepIndex = 1; // on Work

            // Move step 3 (Deposit) to index 0
            _sim.CommandMoveStepInTaskSequence(seq.Id, 3, 0);

            Assert.AreEqual(2, _runner.TaskSequenceCurrentStepIndex,
                "Current index should shift up when step moved from after to before");
        }

        // ─── Task Sequence: Set Loop ─────────────────────────────────

        [Test]
        public void SetTaskSequenceLoop_UpdatesLoopFlag()
        {
            Setup();
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.CommandCreateTaskSequence(seq);

            Assert.IsTrue(seq.Loop);

            _sim.CommandSetTaskSequenceLoop(seq.Id, false);
            Assert.IsFalse(seq.Loop);

            _sim.CommandSetTaskSequenceLoop(seq.Id, true);
            Assert.IsTrue(seq.Loop);
        }

        // ─── Task Sequence: Set Work Step Micro Ruleset ──────────────

        [Test]
        public void SetWorkStepMicroRuleset_UpdatesMicroRulesetId()
        {
            Setup();
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.CommandCreateTaskSequence(seq);

            // Step 1 is Work
            Assert.AreEqual(DefaultRulesets.DefaultMicroId, seq.Steps[1].MicroRulesetId);

            _sim.CommandSetWorkStepMicroRuleset(seq.Id, 1, "custom-micro");

            Assert.AreEqual("custom-micro", seq.Steps[1].MicroRulesetId);
        }

        [Test]
        public void SetWorkStepMicroRuleset_NonWorkStep_NoOp()
        {
            Setup();
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.CommandCreateTaskSequence(seq);

            // Step 0 is TravelTo, not Work
            _sim.CommandSetWorkStepMicroRuleset(seq.Id, 0, "custom-micro");

            Assert.IsNull(seq.Steps[0].MicroRulesetId,
                "Setting micro on a non-Work step should be a no-op");
        }

        // ─── Task Sequence: Rename ───────────────────────────────────

        [Test]
        public void RenameTaskSequence_UpdatesName()
        {
            Setup();
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.CommandCreateTaskSequence(seq);

            _sim.CommandRenameTaskSequence(seq.Id, "My Custom Loop");

            Assert.AreEqual("My Custom Loop", seq.Name);
        }

        // ─── Query Helpers ───────────────────────────────────────────

        [Test]
        public void CountRunnersUsingTaskSequence_ReturnsCorrectCount()
        {
            Setup("mine");
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, seq);
            _sim.AssignRunner(_runner2.Id, seq);

            Assert.AreEqual(2, _sim.CountRunnersUsingTaskSequence(seq.Id));
        }

        [Test]
        public void CountRunnersUsingTaskSequence_ZeroWhenNoneAssigned()
        {
            Setup();
            Assert.AreEqual(0, _sim.CountRunnersUsingTaskSequence("nonexistent"));
        }

        [Test]
        public void CountRunnersUsingMacroRuleset_ReturnsCorrectCount()
        {
            Setup();
            var macro = CreateTestMacroRuleset();
            _sim.CommandCreateMacroRuleset(macro);
            _sim.CommandAssignMacroRulesetToRunner(_runner.Id, macro.Id);
            _sim.CommandAssignMacroRulesetToRunner(_runner2.Id, macro.Id);

            Assert.AreEqual(2, _sim.CountRunnersUsingMacroRuleset(macro.Id));
        }

        [Test]
        public void CountRunnersUsingMicroRuleset_SearchesWorkSteps()
        {
            Setup("mine");
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, seq);

            Assert.AreEqual(1, _sim.CountRunnersUsingMicroRuleset(DefaultRulesets.DefaultMicroId));
        }

        [Test]
        public void GetRunnerNamesUsingTaskSequence_ReturnsNames()
        {
            Setup("mine");
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, seq);
            _sim.AssignRunner(_runner2.Id, seq);

            var names = _sim.GetRunnerNamesUsingTaskSequence(seq.Id);

            Assert.AreEqual(2, names.Count);
            Assert.Contains("Alpha", names);
            Assert.Contains("Beta", names);
        }

        [Test]
        public void GetRunnerNamesUsingMacroRuleset_ReturnsNames()
        {
            Setup();
            var macro = CreateTestMacroRuleset();
            _sim.CommandCreateMacroRuleset(macro);
            _sim.CommandAssignMacroRulesetToRunner(_runner.Id, macro.Id);
            _sim.CommandAssignMacroRulesetToRunner(_runner2.Id, macro.Id);

            var names = _sim.GetRunnerNamesUsingMacroRuleset(macro.Id);

            Assert.AreEqual(2, names.Count);
            Assert.Contains("Alpha", names);
            Assert.Contains("Beta", names);
        }

        [Test]
        public void GetRunnerNamesUsingMicroRuleset_ReturnsNames()
        {
            Setup("mine");
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, seq);

            var names = _sim.GetRunnerNamesUsingMicroRuleset(DefaultRulesets.DefaultMicroId);

            Assert.AreEqual(1, names.Count);
            Assert.Contains("Alpha", names);
        }

        [Test]
        public void CountSequencesUsingMicroRuleset_ReturnsCorrectCount()
        {
            Setup();
            var seq1 = TaskSequence.CreateLoop("mine", "hub");
            var seq2 = TaskSequence.CreateLoop("forest", "hub");
            _sim.CommandCreateTaskSequence(seq1);
            _sim.CommandCreateTaskSequence(seq2);

            Assert.AreEqual(2, _sim.CountSequencesUsingMicroRuleset(DefaultRulesets.DefaultMicroId));
        }

        // ─── Multi-Runner Step Index Adjustment ──────────────────────

        [Test]
        public void AddStep_MultipleRunners_OnlyAffectsRunnersOnSequence()
        {
            Setup("mine");
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.CommandCreateTaskSequence(seq);

            // Only runner 1 uses this sequence
            _runner.TaskSequenceId = seq.Id;
            _runner.TaskSequenceCurrentStepIndex = 2;

            // Runner 2 has a different sequence
            _runner2.TaskSequenceId = "something-else";
            _runner2.TaskSequenceCurrentStepIndex = 0;

            _sim.CommandAddStepToTaskSequence(seq.Id, new TaskStep(TaskStepType.Deposit), 0);

            Assert.AreEqual(3, _runner.TaskSequenceCurrentStepIndex,
                "Runner 1 (on the sequence) should have index adjusted");
            Assert.AreEqual(0, _runner2.TaskSequenceCurrentStepIndex,
                "Runner 2 (different sequence) should not be affected");
        }

        // ─── Natural Language Formatting ─────────────────────────────

        [Test]
        public void FormatCondition_Always()
        {
            var condition = Condition.Always();
            var result = AutomationUIHelpers.FormatCondition(condition, null);
            Assert.AreEqual("Always", result);
        }

        [Test]
        public void FormatCondition_InventoryFull()
        {
            var condition = Condition.InventoryFull();
            var result = AutomationUIHelpers.FormatCondition(condition, null);
            Assert.AreEqual("Inventory Full", result);
        }

        [Test]
        public void FormatCondition_BankContains_WithItemResolver()
        {
            var condition = Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 200);
            string result = AutomationUIHelpers.FormatCondition(
                condition, null, id => id == "copper_ore" ? "Copper Ore" : null);
            Assert.AreEqual("Bank contains Copper Ore >= 200", result);
        }

        [Test]
        public void FormatCondition_BankContains_FallbackToHumanize()
        {
            var condition = Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 200);
            string result = AutomationUIHelpers.FormatCondition(condition, null);
            Assert.AreEqual("Bank contains Copper Ore >= 200", result);
        }

        [Test]
        public void FormatCondition_SkillLevel()
        {
            var condition = Condition.SkillLevel(SkillType.Mining, ComparisonOperator.GreaterOrEqual, 15);
            var result = AutomationUIHelpers.FormatCondition(condition, null);
            Assert.AreEqual("Mining Level >= 15", result);
        }

        [Test]
        public void FormatCondition_InventorySlots()
        {
            var condition = Condition.InventorySlots(ComparisonOperator.LessThan, 5);
            var result = AutomationUIHelpers.FormatCondition(condition, null);
            Assert.AreEqual("Free Slots < 5", result);
        }

        [Test]
        public void FormatCondition_AtNode_WithGameState()
        {
            Setup();
            var condition = Condition.AtNode("mine");
            var result = AutomationUIHelpers.FormatCondition(condition, _sim.CurrentGameState);
            Assert.AreEqual("At Copper Mine", result);
        }

        [Test]
        public void FormatAction_Idle()
        {
            var result = AutomationUIHelpers.FormatAction(AutomationAction.Idle(), null);
            Assert.AreEqual("Go Idle", result);
        }

        [Test]
        public void FormatAction_GatherHere_Positional()
        {
            var result = AutomationUIHelpers.FormatAction(AutomationAction.GatherHere(0), null);
            Assert.AreEqual("Gather Here (0)", result);
        }

        [Test]
        public void FormatAction_GatherHere_ItemId()
        {
            var action = new AutomationAction
            {
                Type = ActionType.GatherHere,
                StringParam = "copper_ore",
            };
            var result = AutomationUIHelpers.FormatAction(action, null,
                id => id == "copper_ore" ? "Copper Ore" : null);
            Assert.AreEqual("Gather Copper Ore", result);
        }

        [Test]
        public void FormatAction_FinishTask()
        {
            var result = AutomationUIHelpers.FormatAction(AutomationAction.FinishTask(), null);
            Assert.AreEqual("Finish Task", result);
        }

        [Test]
        public void FormatRule_FullSentence()
        {
            Setup();
            // Register a library sequence so FormatAction can resolve the name
            var seq = TaskSequence.CreateLoop("forest", "hub");
            seq.Id = "seq-forest-gather";
            seq.Name = "Gather at Pine Forest";
            _sim.CurrentGameState.TaskSequenceLibrary.Add(seq);

            var rule = new Rule
            {
                Conditions = { Condition.BankContains("copper_ore", ComparisonOperator.GreaterOrEqual, 200) },
                Action = AutomationAction.AssignSequence("seq-forest-gather"),
                Enabled = true,
                FinishCurrentSequence = true,
            };

            var result = AutomationUIHelpers.FormatRule(rule, _sim.CurrentGameState);

            Assert.AreEqual("IF Bank contains Copper Ore >= 200 THEN Use Gather at Pine Forest", result);
        }

        [Test]
        public void FormatRule_MultipleConditions_JoinedWithAND()
        {
            var rule = new Rule
            {
                Conditions =
                {
                    Condition.InventoryFull(),
                    Condition.SkillLevel(SkillType.Mining, ComparisonOperator.GreaterOrEqual, 10),
                },
                Action = AutomationAction.FinishTask(),
            };

            var result = AutomationUIHelpers.FormatRule(rule, null);

            Assert.AreEqual("IF Inventory Full AND Mining Level >= 10 THEN Finish Task", result);
        }

        [Test]
        public void FormatTimingTag_FinishCurrentSequence()
        {
            var rule = new Rule { FinishCurrentSequence = true };
            Assert.AreEqual("Finish Current Sequence", AutomationUIHelpers.FormatTimingTag(rule));
        }

        [Test]
        public void FormatTimingTag_Immediately()
        {
            var rule = new Rule { FinishCurrentSequence = false };
            Assert.AreEqual("Immediately", AutomationUIHelpers.FormatTimingTag(rule));
        }

        [Test]
        public void FormatStep_TravelTo()
        {
            Setup();
            var step = new TaskStep(TaskStepType.TravelTo, "mine");
            var result = AutomationUIHelpers.FormatStep(step, _sim.CurrentGameState);
            Assert.AreEqual("Travel to Copper Mine", result);
        }

        [Test]
        public void FormatStep_Work_WithMicroName()
        {
            var step = new TaskStep(TaskStepType.Work, microRulesetId: "my-micro");
            var result = AutomationUIHelpers.FormatStep(step, null,
                id => id == "my-micro" ? "Default Gather" : null);
            Assert.AreEqual("Work (Default Gather)", result);
        }

        [Test]
        public void FormatStep_Deposit()
        {
            var step = new TaskStep(TaskStepType.Deposit);
            var result = AutomationUIHelpers.FormatStep(step, null);
            Assert.AreEqual("Deposit", result);
        }

        [Test]
        public void HumanizeId_SnakeCase()
        {
            Assert.AreEqual("Copper Ore", AutomationUIHelpers.HumanizeId("copper_ore"));
        }

        [Test]
        public void HumanizeId_KebabCase()
        {
            Assert.AreEqual("Pine Forest", AutomationUIHelpers.HumanizeId("pine-forest"));
        }

        [Test]
        public void HumanizeId_Empty()
        {
            Assert.AreEqual("", AutomationUIHelpers.HumanizeId(""));
            Assert.AreEqual("", AutomationUIHelpers.HumanizeId(null));
        }

        [Test]
        public void FormatOperator_AllSymbols()
        {
            Assert.AreEqual(">", AutomationUIHelpers.FormatOperator(ComparisonOperator.GreaterThan));
            Assert.AreEqual(">=", AutomationUIHelpers.FormatOperator(ComparisonOperator.GreaterOrEqual));
            Assert.AreEqual("<", AutomationUIHelpers.FormatOperator(ComparisonOperator.LessThan));
            Assert.AreEqual("<=", AutomationUIHelpers.FormatOperator(ComparisonOperator.LessOrEqual));
            Assert.AreEqual("=", AutomationUIHelpers.FormatOperator(ComparisonOperator.Equal));
            Assert.AreEqual("!=", AutomationUIHelpers.FormatOperator(ComparisonOperator.NotEqual));
        }
    }
}

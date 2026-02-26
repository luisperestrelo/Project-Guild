using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    /// <summary>
    /// Tests for the global automation library system: shared templates,
    /// CRUD commands, library lookups, template deletion, cloning,
    /// and micro-ruleset-per-Work-step behavior.
    /// </summary>
    [TestFixture]
    public class AutomationLibraryTests
    {
        private GameSimulation _sim;
        private Runner _runner;
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
            map.AddNode("hub", "Hub");
            map.AddNode("mine", "Mine", 0f, 0f, CopperGatherable, TinGatherable);
            map.AddEdge("hub", "mine", 8f);
            map.Initialize();

            _sim.StartNewGame(defs, map, startNodeId);
            _runner = _sim.CurrentGameState.Runners[0];
        }

        // ─── Default library setup ───────────────────────────────

        [Test]
        public void StartNewGame_CreatesDefaultMicroRulesetInLibrary()
        {
            Setup();

            Assert.IsNotNull(_sim.FindMicroRulesetInLibrary(DefaultRulesets.DefaultMicroId),
                "Default micro ruleset should be in library after StartNewGame");
        }

        [Test]
        public void StartNewGame_RunnersHaveNullMacroRulesetId()
        {
            Setup();

            foreach (var runner in _sim.CurrentGameState.Runners)
            {
                Assert.IsNull(runner.MacroRulesetId,
                    $"Runner {runner.Name} should have null macro ruleset (no auto-switching by default)");
            }
        }

        [Test]
        public void StartNewGame_LibraryListsArePopulated()
        {
            Setup();

            Assert.AreEqual(0, _sim.CurrentGameState.MacroRulesetLibrary.Count,
                "Should have 0 macro rulesets (no default macro)");
            Assert.AreEqual(1, _sim.CurrentGameState.MicroRulesetLibrary.Count,
                "Should have exactly 1 micro ruleset (default)");
        }

        // ─── Shared template editing ─────────────────────────────

        [Test]
        public void SharedMacroRuleset_EditAffectsBothRunners()
        {
            Setup("mine");

            var runner1 = _sim.CurrentGameState.Runners[0];
            var runner2 = _sim.CurrentGameState.Runners[1];

            // Create a shared macro ruleset
            var sharedMacro = new Ruleset
            {
                Id = "shared-macro",
                Name = "Shared Macro",
                Category = RulesetCategory.General,
            };
            _sim.CommandCreateMacroRuleset(sharedMacro);

            // Assign to both runners
            _sim.CommandAssignMacroRulesetToRunner(runner1.Id, "shared-macro");
            _sim.CommandAssignMacroRulesetToRunner(runner2.Id, "shared-macro");

            Assert.AreEqual("shared-macro", runner1.MacroRulesetId);
            Assert.AreEqual("shared-macro", runner2.MacroRulesetId);

            // Edit the shared ruleset — add a rule
            sharedMacro.Rules.Add(new Rule
            {
                Label = "Test rule",
                Conditions = { Condition.Always() },
                Action = AutomationAction.Idle(),
                Enabled = true,
            });

            // Both runners should see the updated rules through the library
            var r1Macro = _sim.GetRunnerMacroRuleset(runner1);
            var r2Macro = _sim.GetRunnerMacroRuleset(runner2);

            Assert.AreSame(sharedMacro, r1Macro, "Runner 1 should reference same object");
            Assert.AreSame(sharedMacro, r2Macro, "Runner 2 should reference same object");
            Assert.AreEqual(1, r1Macro.Rules.Count, "Shared edit should be visible to runner 1");
            Assert.AreEqual(1, r2Macro.Rules.Count, "Shared edit should be visible to runner 2");
        }

        // ─── Template deletion ───────────────────────────────────

        [Test]
        public void DeleteTaskSequence_RunnersGoIdle()
        {
            Setup("mine");

            var runner1 = _sim.CurrentGameState.Runners[0];
            var runner2 = _sim.CurrentGameState.Runners[1];

            // Assign same task sequence to both
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(runner1.Id, seq);
            _sim.AssignRunner(runner2.Id, seq);

            Assert.AreEqual(seq.Id, runner1.TaskSequenceId);
            Assert.AreEqual(seq.Id, runner2.TaskSequenceId);

            // Delete the task sequence
            _sim.CommandDeleteTaskSequence(seq.Id);

            Assert.IsNull(runner1.TaskSequenceId, "Runner 1 should have null TaskSequenceId after deletion");
            Assert.IsNull(runner2.TaskSequenceId, "Runner 2 should have null TaskSequenceId after deletion");
            Assert.AreEqual(RunnerState.Idle, runner1.State, "Runner 1 should be idle after deletion");
            Assert.AreEqual(RunnerState.Idle, runner2.State, "Runner 2 should be idle after deletion");
        }

        [Test]
        public void DeleteMacroRuleset_RunnerMacroRulesetIdCleared()
        {
            Setup();

            var macro = new Ruleset
            {
                Id = "deletable-macro",
                Name = "Deletable",
                Category = RulesetCategory.General,
            };
            _sim.CommandCreateMacroRuleset(macro);
            _sim.CommandAssignMacroRulesetToRunner(_runner.Id, "deletable-macro");

            Assert.AreEqual("deletable-macro", _runner.MacroRulesetId);

            _sim.CommandDeleteMacroRuleset("deletable-macro");

            Assert.IsNull(_runner.MacroRulesetId, "MacroRulesetId should be null after deletion");
            Assert.IsNull(_sim.GetRunnerMacroRuleset(_runner), "GetRunnerMacroRuleset should return null");
        }

        [Test]
        public void DeleteMicroRuleset_WorkStepRefsCleared()
        {
            Setup();

            // Create a micro ruleset
            var micro = new Ruleset
            {
                Id = "deletable-micro",
                Name = "Deletable Micro",
                Category = RulesetCategory.Gathering,
            };
            micro.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });
            _sim.CommandCreateMicroRuleset(micro);

            // Create a task sequence that uses it
            var seq = TaskSequence.CreateLoop("mine", "hub", "deletable-micro");
            _sim.CommandCreateTaskSequence(seq);

            // Verify the Work step has the micro ruleset
            Assert.AreEqual("deletable-micro", seq.Steps[1].MicroRulesetId);

            // Delete the micro ruleset
            _sim.CommandDeleteMicroRuleset("deletable-micro");

            Assert.IsNull(seq.Steps[1].MicroRulesetId,
                "Work step MicroRulesetId should be cleared after deletion");
        }

        // ─── CRUD commands ───────────────────────────────────────

        [Test]
        public void CommandCreateTaskSequence_RegistersInLibrary()
        {
            Setup();

            var seq = new TaskSequence
            {
                Name = "Test Sequence",
                TargetNodeId = "mine",
                Loop = true,
                Steps = new System.Collections.Generic.List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "mine"),
                    new TaskStep(TaskStepType.Work, microRulesetId: DefaultRulesets.DefaultMicroId),
                },
            };

            string id = _sim.CommandCreateTaskSequence(seq);

            Assert.IsNotNull(id, "Should return an ID");
            Assert.AreEqual(id, seq.Id, "ID should be set on the sequence");
            Assert.AreSame(seq, _sim.FindTaskSequenceInLibrary(id),
                "Should be findable in library");
        }

        [Test]
        public void CommandCreateMacroRuleset_RegistersInLibrary()
        {
            Setup();

            var macro = new Ruleset { Name = "Test Macro", Category = RulesetCategory.General };
            string id = _sim.CommandCreateMacroRuleset(macro);

            Assert.IsNotNull(id);
            Assert.AreSame(macro, _sim.FindMacroRulesetInLibrary(id));
        }

        [Test]
        public void CommandCreateMicroRuleset_RegistersInLibrary()
        {
            Setup();

            var micro = new Ruleset { Name = "Test Micro", Category = RulesetCategory.Gathering };
            string id = _sim.CommandCreateMicroRuleset(micro);

            Assert.IsNotNull(id);
            Assert.AreSame(micro, _sim.FindMicroRulesetInLibrary(id));
        }

        // ─── Clone ───────────────────────────────────────────────

        [Test]
        public void CloneMacroRuleset_CreatesIndependentCopy()
        {
            Setup();

            // Create and assign a macro ruleset with a rule
            var original = new Ruleset
            {
                Id = "original-macro",
                Name = "Original",
                Category = RulesetCategory.General,
            };
            original.Rules.Add(new Rule
            {
                Label = "Rule 1",
                Conditions = { Condition.Always() },
                Action = AutomationAction.Idle(),
                Enabled = true,
            });
            _sim.CommandCreateMacroRuleset(original);
            _sim.CommandAssignMacroRulesetToRunner(_runner.Id, "original-macro");

            // Clone
            string cloneId = _sim.CommandCloneMacroRulesetForRunner(_runner.Id);

            Assert.IsNotNull(cloneId);
            Assert.AreNotEqual("original-macro", cloneId, "Clone should have a different ID");
            Assert.AreEqual(cloneId, _runner.MacroRulesetId, "Runner should be assigned the clone");

            // Verify independence
            var clone = _sim.FindMacroRulesetInLibrary(cloneId);
            Assert.AreEqual(1, clone.Rules.Count, "Clone should have same rules");
            Assert.AreEqual("Original (copy)", clone.Name, "Clone should have '(copy)' suffix");

            // Modify original — clone should not change
            original.Rules.Add(new Rule
            {
                Label = "Rule 2",
                Conditions = { Condition.Always() },
                Action = AutomationAction.AssignSequence("seq-mine"),
                Enabled = true,
            });
            Assert.AreEqual(2, original.Rules.Count);
            Assert.AreEqual(1, clone.Rules.Count, "Clone should be independent of original");
        }

        [Test]
        public void CloneMicroRuleset_CreatesIndependentCopy()
        {
            Setup();

            // Use the default micro as source
            string cloneId = _sim.CommandCloneMicroRuleset(DefaultRulesets.DefaultMicroId);

            Assert.IsNotNull(cloneId);
            Assert.AreNotEqual(DefaultRulesets.DefaultMicroId, cloneId);

            var clone = _sim.FindMicroRulesetInLibrary(cloneId);
            var original = _sim.FindMicroRulesetInLibrary(DefaultRulesets.DefaultMicroId);

            Assert.AreEqual(original.Rules.Count, clone.Rules.Count, "Clone should have same number of rules");
            Assert.AreEqual("Default Gather (copy)", clone.Name);
            Assert.AreNotSame(original, clone, "Clone should be a different object");
        }

        // ─── AssignRunner registers in library ───────────────────

        [Test]
        public void AssignRunner_AutoRegistersSequenceInLibrary()
        {
            Setup("mine");

            int initialCount = _sim.CurrentGameState.TaskSequenceLibrary.Count;
            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, seq);

            Assert.AreEqual(initialCount + 1, _sim.CurrentGameState.TaskSequenceLibrary.Count,
                "AssignRunner should auto-register the sequence in the library");
            Assert.AreSame(seq, _sim.FindTaskSequenceInLibrary(seq.Id));
        }

        [Test]
        public void AssignRunner_SameSequenceTwice_NoDuplicateInLibrary()
        {
            Setup("mine");

            var seq = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, seq);
            int countAfterFirst = _sim.CurrentGameState.TaskSequenceLibrary.Count;

            // Assign the same sequence to a second runner
            var runner2 = _sim.CurrentGameState.Runners[1];
            _sim.AssignRunner(runner2.Id, seq);

            Assert.AreEqual(countAfterFirst, _sim.CurrentGameState.TaskSequenceLibrary.Count,
                "Should not add duplicate to library");
        }

        // ─── Micro per-Work-step ─────────────────────────────────

        [Test]
        public void CreateLoop_SetsDefaultMicroOnWorkStep()
        {
            var seq = TaskSequence.CreateLoop("mine", "hub");

            // Step 1 is Work
            Assert.AreEqual(TaskStepType.Work, seq.Steps[1].Type);
            Assert.AreEqual(DefaultRulesets.DefaultMicroId, seq.Steps[1].MicroRulesetId,
                "CreateLoop should set DefaultMicroId on the Work step");
        }

        [Test]
        public void CreateLoop_WithCustomMicro_SetsOnWorkStep()
        {
            var seq = TaskSequence.CreateLoop("mine", "hub", "custom-micro");

            Assert.AreEqual("custom-micro", seq.Steps[1].MicroRulesetId,
                "CreateLoop should set custom micro ID on the Work step");
        }

        [Test]
        public void MicroPerWorkStep_TwoSequencesSameMicro_SharedEdit()
        {
            Setup("mine");

            // Create a shared micro ruleset
            var sharedMicro = new Ruleset
            {
                Id = "shared-gather",
                Name = "Shared Gather",
                Category = RulesetCategory.Gathering,
            };
            sharedMicro.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });
            _sim.CommandCreateMicroRuleset(sharedMicro);

            // Create two task sequences that use the same micro
            var seq1 = TaskSequence.CreateLoop("mine", "hub", "shared-gather");
            var seq2 = TaskSequence.CreateLoop("mine", "hub", "shared-gather");

            // Both Work steps reference the same ID
            Assert.AreEqual("shared-gather", seq1.Steps[1].MicroRulesetId);
            Assert.AreEqual("shared-gather", seq2.Steps[1].MicroRulesetId);

            // Edit the shared micro — add a condition
            sharedMicro.Rules.Insert(0, new Rule
            {
                Label = "Stop early",
                Conditions = { Condition.InventoryContains("copper_ore", ComparisonOperator.GreaterOrEqual, 5) },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
            });

            // Both sequences should see the change through library lookup
            var resolvedForSeq1 = _sim.FindMicroRulesetInLibrary(seq1.Steps[1].MicroRulesetId);
            var resolvedForSeq2 = _sim.FindMicroRulesetInLibrary(seq2.Steps[1].MicroRulesetId);

            Assert.AreSame(sharedMicro, resolvedForSeq1);
            Assert.AreSame(sharedMicro, resolvedForSeq2);
            Assert.AreEqual(2, resolvedForSeq1.Rules.Count, "Shared edit should be visible");
        }

        // ─── GatherHere item-ID resolution ───────────────────────

        [Test]
        public void GatherHere_StringParam_ResolvesToCorrectIndex()
        {
            Setup("mine");

            // Create a micro ruleset with item-ID-based GatherHere
            var micro = new Ruleset
            {
                Id = "gather-tin",
                Name = "Gather Tin",
                Category = RulesetCategory.Gathering,
            };
            micro.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = new AutomationAction
                {
                    Type = ActionType.GatherHere,
                    StringParam = "tin_ore", // item ID, not index
                },
                Enabled = true,
            });
            _sim.CommandCreateMicroRuleset(micro);

            // Create a task sequence using this micro
            var seq = TaskSequence.CreateLoop("mine", "hub", "gather-tin");
            _sim.AssignRunner(_runner.Id, seq);

            // Runner should be gathering tin (index 1 at the mine)
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(1, _runner.Gathering.GatherableIndex,
                "GatherHere with StringParam='tin_ore' should resolve to index 1 at the mine");
        }

        [Test]
        public void GatherHere_StringParam_ItemNotAtNode_RunnerStaysIdle()
        {
            Setup("mine");

            // Create a micro ruleset asking for an item that doesn't exist at the mine
            var micro = new Ruleset
            {
                Id = "gather-gold",
                Name = "Gather Gold",
                Category = RulesetCategory.Gathering,
            };
            micro.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = new AutomationAction
                {
                    Type = ActionType.GatherHere,
                    StringParam = "gold_ore", // not available at mine
                },
                Enabled = true,
            });
            _sim.CommandCreateMicroRuleset(micro);

            var seq = TaskSequence.CreateLoop("mine", "hub", "gather-gold");
            _sim.AssignRunner(_runner.Id, seq);

            // Runner should be stuck — item not available (let it break)
            Assert.AreEqual(RunnerState.Idle, _runner.State,
                "GatherHere with unavailable item should leave runner stuck");
        }

        // ─── EnsureInLibrary idempotency ─────────────────────────

        [Test]
        public void EnsureInLibrary_Idempotent_NoDuplicates()
        {
            Setup();

            int macroCount = _sim.CurrentGameState.MacroRulesetLibrary.Count;
            int microCount = _sim.CurrentGameState.MicroRulesetLibrary.Count;

            // Call again — should not add duplicates
            DefaultRulesets.EnsureInLibrary(_sim.CurrentGameState);

            Assert.AreEqual(macroCount, _sim.CurrentGameState.MacroRulesetLibrary.Count,
                "EnsureInLibrary should not create duplicates");
            Assert.AreEqual(microCount, _sim.CurrentGameState.MicroRulesetLibrary.Count,
                "EnsureInLibrary should not create duplicates");
        }

    }
}

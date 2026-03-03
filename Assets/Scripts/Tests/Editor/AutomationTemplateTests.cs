using System.Collections.Generic;
using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    /// <summary>
    /// Tests for the automation template system: step templates, rule templates,
    /// built-in templates, CRUD commands, apply (batch-insert), and reorder.
    /// </summary>
    [TestFixture]
    public class AutomationTemplateTests
    {
        private GameSimulation _sim;
        private Runner _runner;
        private SimulationConfig _config;

        private static readonly Simulation.Gathering.GatherableConfig CopperGatherable =
            new Simulation.Gathering.GatherableConfig("copper_ore", SkillType.Mining, 40f, 0.5f);

        private static readonly Simulation.Gathering.GatherableConfig TinGatherable =
            new Simulation.Gathering.GatherableConfig("tin_ore", SkillType.Mining, 40f, 0.5f);

        private void Setup()
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
            };

            var map = new WorldMap();
            map.HubNodeId = "hub";
            map.AddNode("hub", "Hub");
            map.AddNode("copper_mine", "Copper Mine", 0f, 0f, null, CopperGatherable, TinGatherable);
            map.AddEdge("hub", "copper_mine", 8f);
            map.Initialize();

            _sim.StartNewGame(defs, map, "hub");
            _runner = _sim.CurrentGameState.Runners[0];
        }

        // ─── Built-in templates exist after StartNewGame ──────────

        [Test]
        public void StartNewGame_CreatesBuiltInStepTemplates()
        {
            Setup();

            Assert.IsNotNull(_sim.FindStepTemplate(DefaultRulesets.GatherLoopTemplateId),
                "Gather Loop step template should exist");
            Assert.IsNotNull(_sim.FindStepTemplate(DefaultRulesets.TravelAndWorkTemplateId),
                "Travel & Work step template should exist");
            Assert.IsNotNull(_sim.FindStepTemplate(DefaultRulesets.ReturnAndDepositTemplateId),
                "Return & Deposit step template should exist");

            Assert.AreEqual(3, _sim.CurrentGameState.StepTemplateLibrary.Count,
                "Should have exactly 3 built-in step templates");
        }

        [Test]
        public void StartNewGame_CreatesBuiltInMicroRuleTemplate()
        {
            Setup();

            Assert.IsNotNull(_sim.FindRuleTemplate(DefaultRulesets.BasicGatherTemplateId, isMacro: false),
                "Basic Gather micro rule template should exist");

            Assert.AreEqual(1, _sim.CurrentGameState.MicroRuleTemplateLibrary.Count,
                "Should have exactly 1 built-in micro rule template");
        }

        [Test]
        public void StartNewGame_NoBuiltInMacroRuleTemplates()
        {
            Setup();

            Assert.AreEqual(0, _sim.CurrentGameState.MacroRuleTemplateLibrary.Count,
                "Should have no macro rule templates (intentionally blank-slate)");
        }

        [Test]
        public void StartNewGame_CreatesDefaultGatherSequence()
        {
            Setup();

            var seq = _sim.FindTaskSequenceInLibrary(DefaultRulesets.DefaultGatherSequenceId);
            Assert.IsNotNull(seq, "Default gather sequence should exist");
            Assert.AreEqual("Gather at Copper Mine", seq.Name);
            Assert.AreEqual(4, seq.Steps.Count);
            Assert.IsTrue(seq.Loop);
        }

        // ─── EnsureTemplatesInLibrary idempotency ─────────────────

        [Test]
        public void EnsureTemplatesInLibrary_Idempotent_NoDuplicates()
        {
            Setup();

            int stepCount = _sim.CurrentGameState.StepTemplateLibrary.Count;
            int microRuleCount = _sim.CurrentGameState.MicroRuleTemplateLibrary.Count;

            // Call again — should not add duplicates
            DefaultRulesets.EnsureTemplatesInLibrary(_sim.CurrentGameState);

            Assert.AreEqual(stepCount, _sim.CurrentGameState.StepTemplateLibrary.Count,
                "EnsureTemplatesInLibrary should not create duplicate step templates");
            Assert.AreEqual(microRuleCount, _sim.CurrentGameState.MicroRuleTemplateLibrary.Count,
                "EnsureTemplatesInLibrary should not create duplicate rule templates");
        }

        // ─── CommandApplyStepTemplate ─────────────────────────────

        [Test]
        public void CommandApplyStepTemplate_AppendsStepsToSequence()
        {
            Setup();

            string seqId = _sim.CommandCreateTaskSequence();
            var seq = _sim.FindTaskSequenceInLibrary(seqId);
            Assert.AreEqual(0, seq.Steps.Count, "New sequence starts empty");

            _sim.CommandApplyStepTemplate(seqId, DefaultRulesets.GatherLoopTemplateId);

            Assert.AreEqual(4, seq.Steps.Count, "Gather Loop should add 4 steps");
            Assert.AreEqual(TaskStepType.TravelTo, seq.Steps[0].Type);
            Assert.AreEqual(TaskStepType.Work, seq.Steps[1].Type);
            Assert.AreEqual(TaskStepType.TravelTo, seq.Steps[2].Type);
            Assert.AreEqual(TaskStepType.Deposit, seq.Steps[3].Type);
        }

        [Test]
        public void CommandApplyStepTemplate_IsComposable()
        {
            Setup();

            string seqId = _sim.CommandCreateTaskSequence();

            // Apply "Travel & Work" first
            _sim.CommandApplyStepTemplate(seqId, DefaultRulesets.TravelAndWorkTemplateId);
            var seq = _sim.FindTaskSequenceInLibrary(seqId);
            Assert.AreEqual(2, seq.Steps.Count);

            // Apply "Return & Deposit" after — should append
            _sim.CommandApplyStepTemplate(seqId, DefaultRulesets.ReturnAndDepositTemplateId);
            Assert.AreEqual(4, seq.Steps.Count, "Steps should be composable (2 + 2 = 4)");
            Assert.AreEqual(TaskStepType.TravelTo, seq.Steps[0].Type);
            Assert.AreEqual(TaskStepType.Work, seq.Steps[1].Type);
            Assert.AreEqual(TaskStepType.TravelTo, seq.Steps[2].Type);
            Assert.AreEqual(TaskStepType.Deposit, seq.Steps[3].Type);
        }

        [Test]
        public void CommandApplyStepTemplate_ResolvesNullTargetNodeId()
        {
            Setup();

            string seqId = _sim.CommandCreateTaskSequence();
            _sim.CommandApplyStepTemplate(seqId, DefaultRulesets.GatherLoopTemplateId);

            var seq = _sim.FindTaskSequenceInLibrary(seqId);
            // First TravelTo should have null resolved to first map node (hub)
            Assert.IsNotNull(seq.Steps[0].TargetNodeId,
                "Null TargetNodeId should be resolved on apply");
            Assert.AreEqual("hub", seq.Steps[0].TargetNodeId,
                "Null TargetNodeId should resolve to first map node");
        }

        [Test]
        public void CommandApplyStepTemplate_DeepCopiesSteps()
        {
            Setup();

            string seqId = _sim.CommandCreateTaskSequence();
            _sim.CommandApplyStepTemplate(seqId, DefaultRulesets.GatherLoopTemplateId);

            var seq = _sim.FindTaskSequenceInLibrary(seqId);
            var template = _sim.FindStepTemplate(DefaultRulesets.GatherLoopTemplateId);

            // Mutate applied step — template should not change
            seq.Steps[2].TargetNodeId = "other_node";
            Assert.AreEqual("hub", template.Steps[2].TargetNodeId,
                "Mutating applied steps should not affect the template");
        }

        // ─── CommandApplyRuleTemplate ─────────────────────────────

        [Test]
        public void CommandApplyRuleTemplate_AppendsRulesToRuleset()
        {
            Setup();

            string rulesetId = _sim.CommandCreateMicroRuleset();
            var ruleset = _sim.FindMicroRulesetInLibrary(rulesetId);
            int initialRuleCount = ruleset.Rules.Count; // default micro has 2 rules

            _sim.CommandApplyRuleTemplate(rulesetId, DefaultRulesets.BasicGatherTemplateId, isMacro: false);

            Assert.AreEqual(initialRuleCount + 2, ruleset.Rules.Count,
                "Basic Gather template should append 2 rules");
        }

        [Test]
        public void CommandApplyRuleTemplate_DeepCopiesRules()
        {
            Setup();

            string rulesetId = _sim.CommandCreateMicroRuleset();
            var ruleset = _sim.FindMicroRulesetInLibrary(rulesetId);
            int initialCount = ruleset.Rules.Count;

            _sim.CommandApplyRuleTemplate(rulesetId, DefaultRulesets.BasicGatherTemplateId, isMacro: false);

            var template = _sim.FindRuleTemplate(DefaultRulesets.BasicGatherTemplateId, isMacro: false);

            // Mutate an applied rule — template should not change
            ruleset.Rules[initialCount].Label = "Mutated Label";
            Assert.AreEqual("Deposit when full", template.Rules[0].Label,
                "Mutating applied rules should not affect the template");
        }

        // ─── CommandDeleteStepTemplate ────────────────────────────

        [Test]
        public void CommandDeleteStepTemplate_RefusesBuiltIn()
        {
            Setup();

            bool result = _sim.CommandDeleteStepTemplate(DefaultRulesets.GatherLoopTemplateId);

            Assert.IsFalse(result, "Should refuse to delete built-in template");
            Assert.IsNotNull(_sim.FindStepTemplate(DefaultRulesets.GatherLoopTemplateId),
                "Built-in template should still exist after delete attempt");
        }

        [Test]
        public void CommandDeleteStepTemplate_RemovesCustom()
        {
            Setup();

            var steps = new List<TaskStep>
            {
                new TaskStep(TaskStepType.TravelTo, "hub"),
                new TaskStep(TaskStepType.Deposit),
            };
            string id = _sim.CommandCreateStepTemplate("Custom Template", steps);
            Assert.IsNotNull(_sim.FindStepTemplate(id));

            bool result = _sim.CommandDeleteStepTemplate(id);

            Assert.IsTrue(result, "Should succeed deleting custom template");
            Assert.IsNull(_sim.FindStepTemplate(id), "Custom template should be removed");
        }

        // ─── CommandDeleteRuleTemplate ────────────────────────────

        [Test]
        public void CommandDeleteRuleTemplate_RefusesBuiltIn()
        {
            Setup();

            bool result = _sim.CommandDeleteRuleTemplate(DefaultRulesets.BasicGatherTemplateId, isMacro: false);

            Assert.IsFalse(result, "Should refuse to delete built-in rule template");
            Assert.IsNotNull(_sim.FindRuleTemplate(DefaultRulesets.BasicGatherTemplateId, isMacro: false));
        }

        // ─── CommandReorderTemplate ───────────────────────────────

        [Test]
        public void CommandReorderTemplate_ChangesListPosition()
        {
            Setup();

            // Verify initial order: Gather Loop at index 0
            Assert.AreEqual(DefaultRulesets.GatherLoopTemplateId,
                _sim.CurrentGameState.StepTemplateLibrary[0].Id);

            // Move Gather Loop to index 2 (last)
            _sim.CommandReorderTemplate(DefaultRulesets.GatherLoopTemplateId, 2, TemplateKind.Step);

            Assert.AreEqual(DefaultRulesets.GatherLoopTemplateId,
                _sim.CurrentGameState.StepTemplateLibrary[2].Id,
                "Gather Loop should be at index 2 after reorder");
            Assert.AreEqual(DefaultRulesets.TravelAndWorkTemplateId,
                _sim.CurrentGameState.StepTemplateLibrary[0].Id,
                "Travel & Work should now be at index 0");
        }

        // ─── CommandCreateStepTemplate ────────────────────────────

        [Test]
        public void CommandCreateStepTemplate_FromExistingSteps()
        {
            Setup();

            var steps = new List<TaskStep>
            {
                new TaskStep(TaskStepType.TravelTo, "copper_mine"),
                new TaskStep(TaskStepType.Work, microRulesetId: DefaultRulesets.DefaultMicroId),
            };

            string id = _sim.CommandCreateStepTemplate("Custom Gather", steps);

            var template = _sim.FindStepTemplate(id);
            Assert.IsNotNull(template);
            Assert.AreEqual("Custom Gather", template.Name);
            Assert.IsFalse(template.IsBuiltIn);
            Assert.AreEqual(2, template.Steps.Count);

            // Verify deep-copy: mutating source steps should not affect template
            steps[0].TargetNodeId = "other_node";
            Assert.AreEqual("copper_mine", template.Steps[0].TargetNodeId,
                "Template steps should be independent of source");
        }

        // ─── CommandCreateRuleTemplate ────────────────────────────

        [Test]
        public void CommandCreateRuleTemplate_CreatesCustomTemplate()
        {
            Setup();

            var rules = new List<Rule>
            {
                new Rule
                {
                    Label = "Custom Rule",
                    Conditions = { Condition.Always() },
                    Action = AutomationAction.Idle(),
                    Enabled = true,
                },
            };

            string id = _sim.CommandCreateRuleTemplate("Custom Macro", rules, isMacro: true);

            var template = _sim.FindRuleTemplate(id, isMacro: true);
            Assert.IsNotNull(template);
            Assert.AreEqual("Custom Macro", template.Name);
            Assert.IsFalse(template.IsBuiltIn);
            Assert.AreEqual(1, template.Rules.Count);

            Assert.AreEqual(1, _sim.CurrentGameState.MacroRuleTemplateLibrary.Count,
                "Should be in macro library");
        }

        // ─── CommandRenameTemplate ────────────────────────────────

        [Test]
        public void CommandRenameTemplate_UpdatesName()
        {
            Setup();

            var steps = new List<TaskStep> { new TaskStep(TaskStepType.Deposit) };
            string id = _sim.CommandCreateStepTemplate("Old Name", steps);

            _sim.CommandRenameTemplate(id, "New Name", TemplateKind.Step);

            Assert.AreEqual("New Name", _sim.FindStepTemplate(id).Name);
        }

        // ─── Save/Load persistence ───────────────────────────────

        [Test]
        public void LoadState_PreservesCustomTemplates()
        {
            Setup();

            // Create a custom template
            var steps = new List<TaskStep> { new TaskStep(TaskStepType.Deposit) };
            string customId = _sim.CommandCreateStepTemplate("My Custom", steps);

            // Simulate save/load by reusing the same GameState
            var state = _sim.CurrentGameState;
            var sim2 = new GameSimulation(_config, tickRate: 10f);
            sim2.LoadState(state);

            // Custom template should still exist
            Assert.IsNotNull(sim2.FindStepTemplate(customId),
                "Custom templates should persist through LoadState");
            // Built-in templates should also exist (EnsureTemplatesInLibrary runs in LoadState)
            Assert.IsNotNull(sim2.FindStepTemplate(DefaultRulesets.GatherLoopTemplateId));
        }
    }
}

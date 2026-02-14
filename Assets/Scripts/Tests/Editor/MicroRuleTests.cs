using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class MicroRuleTests
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
                new RunnerFactory.RunnerDefinition { Name = "Tester" }
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

        private int TickUntil(System.Func<bool> condition, int safetyLimit = 10000)
        {
            int ticks = 0;
            while (!condition() && ticks < safetyLimit)
            {
                _sim.Tick();
                ticks++;
            }
            return ticks;
        }

        // ─── Default micro rule ─────────────────────────────────

        [Test]
        public void DefaultMicro_GathersIndex0()
        {
            Setup("mine");

            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(0, _runner.Gathering.GatherableIndex,
                "Default micro rule (Always → GatherHere(0)) should select index 0");
        }

        // ─── Custom micro rule selects specific resource ──────

        [Test]
        public void CustomMicro_GathersSpecificIndex()
        {
            Setup("mine");

            // Micro rule: always gather index 1 (tin)
            _runner.MicroRuleset = new Ruleset();
            _runner.MicroRuleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(1),
                Enabled = true,
            });

            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(1, _runner.Gathering.GatherableIndex,
                "Custom micro rule should select index 1 (tin)");
        }

        // ─── Multi-resource switching ───────────────────────────

        [Test]
        public void MultiResource_SwitchesBasedOnInventory()
        {
            Setup("mine");

            // Rules: gather copper until 3, then gather tin until 3, then FinishTask
            _runner.MicroRuleset = new Ruleset();
            _runner.MicroRuleset.Rules.Add(new Rule
            {
                Label = "Copper until 3",
                Conditions = { Condition.InventoryContains("copper_ore", ComparisonOperator.LessThan, 3) },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });
            _runner.MicroRuleset.Rules.Add(new Rule
            {
                Label = "Tin until 3",
                Conditions = { Condition.InventoryContains("tin_ore", ComparisonOperator.LessThan, 3) },
                Action = AutomationAction.GatherHere(1),
                Enabled = true,
            });
            _runner.MicroRuleset.Rules.Add(new Rule
            {
                Label = "Done",
                Conditions = { Condition.Always() },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
            });

            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Should start gathering copper (index 0)
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(0, _runner.Gathering.GatherableIndex);

            // Tick until we have 3 copper — should switch to tin
            TickUntil(() => _runner.Inventory.CountItem("copper_ore") >= 3);
            Assert.AreEqual(3, _runner.Inventory.CountItem("copper_ore"));

            // Runner should now be gathering tin (index 1) — or about to after re-eval
            // Give it a tick to process the switch
            if (_runner.State == RunnerState.Gathering)
            {
                Assert.AreEqual(1, _runner.Gathering.GatherableIndex,
                    "After 3 copper, micro rules should switch to tin (index 1)");
            }

            // Tick until we have 3 tin
            TickUntil(() => _runner.Inventory.CountItem("tin_ore") >= 3);
            Assert.AreEqual(3, _runner.Inventory.CountItem("tin_ore"));

            // FinishTask should have fired — runner advances past Gather step
            // With a looping gather assignment, next step is TravelTo(hub)
            // The runner should be traveling to hub or depositing
            Assert.AreNotEqual(RunnerState.Gathering, _runner.State,
                "After 3 copper + 3 tin, FinishTask should stop gathering and advance macro");
        }

        // ─── FinishTask action ──────────────────────────────────

        [Test]
        public void FinishTask_StopsGatheringAndAdvancesMacro()
        {
            Setup("mine");

            // Micro rule: always FinishTask (never actually gathers)
            _runner.MicroRuleset = new Ruleset();
            _runner.MicroRuleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
            });

            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // ExecuteGatherStep evaluates micro → FinishTask → advance macro
            // Next step after Gather is TravelTo(hub), so runner should be traveling
            Assert.AreEqual(RunnerState.Traveling, _runner.State,
                "FinishTask should skip gathering and advance to next macro step (TravelTo hub)");
            Assert.AreEqual("hub", _runner.Travel.ToNodeId);
        }

        [Test]
        public void FinishTask_MidGathering_StopsAndAdvancesMacro()
        {
            Setup("mine");

            // Start with normal micro (gather copper), then switch to FinishTask after 2 items
            _runner.MicroRuleset = new Ruleset();
            _runner.MicroRuleset.Rules.Add(new Rule
            {
                Label = "Copper until 2",
                Conditions = { Condition.InventoryContains("copper_ore", ComparisonOperator.LessThan, 2) },
                Action = AutomationAction.GatherHere(0),
                Enabled = true,
            });
            _runner.MicroRuleset.Rules.Add(new Rule
            {
                Label = "Done after 2",
                Conditions = { Condition.Always() },
                Action = AutomationAction.FinishTask(),
                Enabled = true,
            });

            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.AreEqual(RunnerState.Gathering, _runner.State);

            // Tick until FinishTask fires (after 2 copper)
            TickUntil(() => _runner.State != RunnerState.Gathering);

            Assert.AreEqual(2, _runner.Inventory.CountItem("copper_ore"));
            // Should be traveling to hub (next macro step after Gather)
            Assert.AreEqual(RunnerState.Traveling, _runner.State);
            Assert.AreEqual("hub", _runner.Travel.ToNodeId);
        }

        // ─── Empty/null micro ruleset: "let it break" vs data fallback ──

        [Test]
        public void EmptyMicroRuleset_RunnerStaysIdle()
        {
            Setup("mine");

            // Empty micro ruleset — player deleted all rules. Runner should be stuck.
            _runner.MicroRuleset = new Ruleset();

            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Runner should NOT be gathering — no micro rule matched, stuck at Work step
            Assert.AreEqual(RunnerState.Idle, _runner.State,
                "Empty micro ruleset should leave runner stuck (let it break)");
            Assert.IsNull(_runner.Gathering);
        }

        [Test]
        public void NullMicroRuleset_RunnerStaysIdle()
        {
            Setup("mine");

            // Null = broken data (old save). Runner should get stuck, same as empty.
            // In production, LoadState migrates nulls to defaults before gameplay.
            _runner.MicroRuleset = null;

            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.AreEqual(RunnerState.Idle, _runner.State,
                "Null micro ruleset should leave runner stuck (let it break)");
            Assert.IsNull(_runner.Gathering);
        }

        // ─── Micro rule with out-of-bounds index ────────────────

        [Test]
        public void MicroRule_InvalidIndex_RunnerStaysIdle()
        {
            Setup("mine");

            // Micro rule points to index 99 which doesn't exist — broken rule
            _runner.MicroRuleset = new Ruleset();
            _runner.MicroRuleset.Rules.Add(new Rule
            {
                Conditions = { Condition.Always() },
                Action = AutomationAction.GatherHere(99),
                Enabled = true,
            });

            var assignment = Assignment.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Runner should NOT be gathering — rule is broken, stuck at Work step
            Assert.AreEqual(RunnerState.Idle, _runner.State,
                "Invalid micro rule index should leave runner stuck (let it break)");
            Assert.IsNull(_runner.Gathering);
        }
    }
}

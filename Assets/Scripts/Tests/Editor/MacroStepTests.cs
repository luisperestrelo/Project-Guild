using NUnit.Framework;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Tests
{
    [TestFixture]
    public class MacroStepTests
    {
        private GameSimulation _sim;
        private Runner _runner;
        private SimulationConfig _config;

        private static readonly Simulation.Gathering.GatherableConfig CopperGatherable =
            new Simulation.Gathering.GatherableConfig("copper_ore", SkillType.Mining, 40f, 0.5f);

        private void Setup(string startNodeId = "hub")
        {
            _config = new SimulationConfig
            {
                ItemDefinitions = new[]
                {
                    new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore),
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
            map.AddNode("mine", "Mine", 0f, 0f, CopperGatherable);
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

        // ─── Assignment.AdvanceStep unit tests ──────────────────

        [Test]
        public void AdvanceStep_LoopingAssignment_WrapsToZero()
        {
            var assignment = Assignment.CreateGatherLoop("mine", "hub");
            // 4 steps: TravelTo(mine), Gather, TravelTo(hub), Deposit
            Assert.AreEqual(0, assignment.CurrentStepIndex);

            Assert.IsTrue(assignment.AdvanceStep()); // → 1
            Assert.AreEqual(1, assignment.CurrentStepIndex);

            Assert.IsTrue(assignment.AdvanceStep()); // → 2
            Assert.IsTrue(assignment.AdvanceStep()); // → 3

            Assert.IsTrue(assignment.AdvanceStep()); // → wraps to 0
            Assert.AreEqual(0, assignment.CurrentStepIndex);
        }

        [Test]
        public void AdvanceStep_NonLoopingAssignment_ReturnsFalse()
        {
            var assignment = new Assignment
            {
                Type = AssignmentType.Gather,
                Steps = new System.Collections.Generic.List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "mine"),
                    new TaskStep(TaskStepType.Gather),
                },
                CurrentStepIndex = 0,
                Loop = false,
            };

            Assert.IsTrue(assignment.AdvanceStep());  // → 1
            Assert.IsFalse(assignment.AdvanceStep()); // past end
            Assert.IsNull(assignment.CurrentStep);
        }

        [Test]
        public void AdvanceStep_EmptySteps_ReturnsFalse()
        {
            var assignment = Assignment.CreateIdle();
            Assert.IsFalse(assignment.AdvanceStep());
        }

        [Test]
        public void CreateGatherLoop_HasCorrectSteps()
        {
            var assignment = Assignment.CreateGatherLoop("mine", "hub", 1);

            Assert.AreEqual(AssignmentType.Gather, assignment.Type);
            Assert.AreEqual(4, assignment.Steps.Count);
            Assert.IsTrue(assignment.Loop);
            Assert.AreEqual("mine", assignment.TargetNodeId);
            Assert.AreEqual(1, assignment.GatherableIndex);

            Assert.AreEqual(TaskStepType.TravelTo, assignment.Steps[0].Type);
            Assert.AreEqual("mine", assignment.Steps[0].TargetNodeId);

            Assert.AreEqual(TaskStepType.Gather, assignment.Steps[1].Type);

            Assert.AreEqual(TaskStepType.TravelTo, assignment.Steps[2].Type);
            Assert.AreEqual("hub", assignment.Steps[2].TargetNodeId);

            Assert.AreEqual(TaskStepType.Deposit, assignment.Steps[3].Type);
        }

        // ─── AssignRunner API ───────────────────────────────────

        [Test]
        public void AssignRunner_PublishesAssignmentChangedEvent()
        {
            Setup("hub");

            AssignmentChanged? received = null;
            _sim.Events.Subscribe<AssignmentChanged>(e => received = e);

            var assignment = Assignment.CreateGatherLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment, "test");

            Assert.IsNotNull(received);
            Assert.AreEqual(_runner.Id, received.Value.RunnerId);
            Assert.AreEqual(AssignmentType.Gather, received.Value.NewType);
            Assert.AreEqual("mine", received.Value.TargetNodeId);
            Assert.AreEqual("test", received.Value.Reason);
        }

        [Test]
        public void AssignRunner_StartsFirstStep_TravelToMine()
        {
            Setup("hub");

            var assignment = Assignment.CreateGatherLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // First step is TravelTo(mine), runner is at hub → should start traveling
            Assert.AreEqual(RunnerState.Traveling, _runner.State);
            Assert.AreEqual("mine", _runner.Travel.ToNodeId);
        }

        [Test]
        public void AssignRunner_AtTarget_SkipsTravelAndGathers()
        {
            Setup("mine");

            var assignment = Assignment.CreateGatherLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // First step is TravelTo(mine) — already there → skip → Gather step → starts gathering
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.IsNotNull(_runner.Gathering);
            Assert.AreEqual("mine", _runner.Gathering.NodeId);
        }

        [Test]
        public void AssignRunner_NullAssignment_SetsIdle()
        {
            Setup("mine");
            _sim.CommandGather(_runner.Id);

            _sim.AssignRunner(_runner.Id, null);

            Assert.AreEqual(RunnerState.Idle, _runner.State);
            Assert.IsNull(_runner.Assignment);
        }

        [Test]
        public void AssignRunner_IdleAssignment_DoesNothing()
        {
            Setup("hub");

            var assignment = Assignment.CreateIdle();
            _sim.AssignRunner(_runner.Id, assignment);

            Assert.AreEqual(RunnerState.Idle, _runner.State);
            // Tick a few times — nothing should happen
            for (int i = 0; i < 10; i++)
                _sim.Tick();
            Assert.AreEqual(RunnerState.Idle, _runner.State);
        }

        // ─── Macro step execution ──────────────────────────────

        [Test]
        public void MacroStep_GatherThenDeposit_FullLoop()
        {
            Setup("mine");

            var assignment = Assignment.CreateGatherLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Should start gathering
            Assert.AreEqual(RunnerState.Gathering, _runner.State);

            // Tick until the runner deposits at the hub
            RunnerDeposited? deposited = null;
            _sim.Events.Subscribe<RunnerDeposited>(e => deposited = e);

            TickUntil(() => deposited != null);

            Assert.IsNotNull(deposited);
            Assert.AreEqual(_config.InventorySize, deposited.Value.ItemsDeposited);

            // Bank should have the items
            Assert.AreEqual(_config.InventorySize, _sim.CurrentGameState.Bank.CountItem("copper_ore"));
        }

        [Test]
        public void MacroStep_FullLoop_ResumesGatheringAtMine()
        {
            Setup("mine");

            var assignment = Assignment.CreateGatherLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Tick until the runner is back at the mine gathering
            TickUntil(() =>
                _runner.CurrentNodeId == "mine"
                && _runner.State == RunnerState.Gathering
                && _sim.CurrentGameState.Bank.CountItem("copper_ore") > 0);

            Assert.AreEqual("mine", _runner.CurrentNodeId);
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.AreEqual(_config.InventorySize, _sim.CurrentGameState.Bank.CountItem("copper_ore"));
        }

        // ─── Deposit step (timed) ───────────────────────────────

        [Test]
        public void Deposit_EntersDepositingState()
        {
            Setup("hub");

            var assignment = Assignment.CreateGatherLoop("mine", "hub");
            assignment.CurrentStepIndex = 3; // Deposit step

            _sim.AssignRunner(_runner.Id, assignment);

            Assert.AreEqual(RunnerState.Depositing, _runner.State);
            Assert.IsNotNull(_runner.Depositing);
            Assert.AreEqual(_config.DepositDurationTicks, _runner.Depositing.TicksRemaining);
        }

        [Test]
        public void Deposit_TakesConfiguredTicks()
        {
            Setup("hub");

            // Give the runner some items to deposit
            var itemDef = new ItemDefinition("copper_ore", "Copper Ore", ItemCategory.Ore);
            for (int i = 0; i < 10; i++)
                _runner.Inventory.TryAdd(itemDef);

            var assignment = Assignment.CreateGatherLoop("mine", "hub");
            assignment.CurrentStepIndex = 3;

            RunnerDeposited? deposited = null;
            _sim.Events.Subscribe<RunnerDeposited>(e => deposited = e);

            _sim.AssignRunner(_runner.Id, assignment);

            // Tick one less than the duration — should still be depositing
            for (int i = 0; i < _config.DepositDurationTicks - 1; i++)
                _sim.Tick();

            Assert.AreEqual(RunnerState.Depositing, _runner.State,
                "Should still be depositing before duration elapses");
            Assert.IsNull(deposited, "Should not have deposited yet");

            // One more tick — deposit completes
            _sim.Tick();

            Assert.IsNotNull(deposited, "Deposit should complete after configured ticks");
            Assert.AreEqual(10, deposited.Value.ItemsDeposited);
            Assert.AreEqual(10, _sim.CurrentGameState.Bank.CountItem("copper_ore"));
        }

        [Test]
        public void Deposit_EmptyInventory_StillTakesTime_NoEvent()
        {
            Setup("hub");

            var assignment = Assignment.CreateGatherLoop("mine", "hub");
            assignment.CurrentStepIndex = 3;

            RunnerDeposited? deposited = null;
            _sim.Events.Subscribe<RunnerDeposited>(e => deposited = e);

            _sim.AssignRunner(_runner.Id, assignment);

            // Tick through the deposit duration
            for (int i = 0; i < _config.DepositDurationTicks; i++)
                _sim.Tick();

            // No RunnerDeposited event for empty inventory, but step still advances
            Assert.IsNull(deposited, "Should not publish RunnerDeposited for empty inventory");
            Assert.AreEqual(RunnerState.Traveling, _runner.State,
                "Should advance to next step (TravelTo mine) after deposit completes");
            Assert.AreEqual("mine", _runner.Travel.ToNodeId);
        }

        [Test]
        public void Deposit_AdvancesToNextStep_AfterCompletion()
        {
            Setup("hub");

            var assignment = Assignment.CreateGatherLoop("mine", "hub");
            assignment.CurrentStepIndex = 3;

            _sim.AssignRunner(_runner.Id, assignment);

            // Tick through deposit
            for (int i = 0; i < _config.DepositDurationTicks; i++)
                _sim.Tick();

            // Should now be traveling to mine (step 0 after loop wrap)
            Assert.AreEqual(RunnerState.Traveling, _runner.State);
            Assert.AreEqual("mine", _runner.Travel.ToNodeId);
        }

        // ─── Step events ────────────────────────────────────────

        [Test]
        public void MacroStep_AssignmentStepAdvanced_EventFiredOnStepCompletion()
        {
            Setup("mine");

            int advancedCount = 0;
            _sim.Events.Subscribe<AssignmentStepAdvanced>(e => advancedCount++);

            var assignment = Assignment.CreateGatherLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Runner is at mine — TravelTo(mine) is skipped (already there),
            // which advances to Gather step and fires the event.
            Assert.Greater(advancedCount, 0);
        }
    }
}

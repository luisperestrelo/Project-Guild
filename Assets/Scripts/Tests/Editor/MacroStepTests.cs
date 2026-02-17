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

        // ─── Step advancement (through simulation) ──────────────────

        [Test]
        public void StepAdvancement_LoopingSequence_WrapsToZero()
        {
            Setup("mine");

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Runner is at mine. TravelTo(mine) skips → step advances through Work → Gathering.
            // We just need to verify the looping behavior through a full cycle.
            // Tick until back at mine gathering again (full loop: Work → TravelTo(hub) → Deposit → TravelTo(mine) → Work)
            TickUntil(() =>
                _runner.CurrentNodeId == "mine"
                && _runner.State == RunnerState.Gathering
                && _sim.CurrentGameState.Bank.CountItem("copper_ore") > 0);

            // Step index should be back on Work (1) after looping
            Assert.AreEqual(1, _runner.TaskSequenceCurrentStepIndex,
                "After a full loop, runner should be back on the Work step");
        }

        [Test]
        public void StepAdvancement_NonLoopingSequence_RunnerGoesIdle()
        {
            Setup("hub");

            // Non-looping sequence: just travel to hub (already there) → completed
            var assignment = new TaskSequence
            {
                Id = "test-non-loop",
                Steps = new System.Collections.Generic.List<TaskStep>
                {
                    new TaskStep(TaskStepType.TravelTo, "hub"),
                },
                Loop = false,
            };

            TaskSequenceCompleted? completed = null;
            _sim.Events.Subscribe<TaskSequenceCompleted>(e => completed = e);

            _sim.AssignRunner(_runner.Id, assignment);

            // Already at hub → TravelTo skips → sequence ends → runner goes idle
            Assert.IsNotNull(completed, "Non-looping sequence should complete");
            Assert.AreEqual(RunnerState.Idle, _runner.State);
            Assert.IsNull(_runner.TaskSequence);
        }

        [Test]
        public void CreateLoop_HasCorrectSteps()
        {
            var assignment = TaskSequence.CreateLoop("mine", "hub");

            Assert.AreEqual(4, assignment.Steps.Count);
            Assert.IsTrue(assignment.Loop);
            Assert.AreEqual("mine", assignment.TargetNodeId);

            Assert.AreEqual(TaskStepType.TravelTo, assignment.Steps[0].Type);
            Assert.AreEqual("mine", assignment.Steps[0].TargetNodeId);

            Assert.AreEqual(TaskStepType.Work, assignment.Steps[1].Type);

            Assert.AreEqual(TaskStepType.TravelTo, assignment.Steps[2].Type);
            Assert.AreEqual("hub", assignment.Steps[2].TargetNodeId);

            Assert.AreEqual(TaskStepType.Deposit, assignment.Steps[3].Type);
        }

        // ─── AssignRunner API ───────────────────────────────────

        [Test]
        public void AssignRunner_PublishesTaskSequenceChangedEvent()
        {
            Setup("hub");

            TaskSequenceChanged? received = null;
            _sim.Events.Subscribe<TaskSequenceChanged>(e => received = e);

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment, "test");

            Assert.IsNotNull(received);
            Assert.AreEqual(_runner.Id, received.Value.RunnerId);
            Assert.AreEqual("mine", received.Value.TargetNodeId);
            Assert.AreEqual("test", received.Value.Reason);
        }

        [Test]
        public void AssignRunner_StartsFirstStep_TravelToMine()
        {
            Setup("hub");

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // First step is TravelTo(mine), runner is at hub → should start traveling
            Assert.AreEqual(RunnerState.Traveling, _runner.State);
            Assert.AreEqual("mine", _runner.Travel.ToNodeId);
        }

        [Test]
        public void AssignRunner_AtTarget_SkipsTravelAndGathers()
        {
            Setup("mine");

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // First step is TravelTo(mine) — already there → skip → Work step → starts gathering
            Assert.AreEqual(RunnerState.Gathering, _runner.State);
            Assert.IsNotNull(_runner.Gathering);
            Assert.AreEqual("mine", _runner.Gathering.NodeId);
        }

        [Test]
        public void AssignRunner_NullSequence_SetsIdle()
        {
            Setup("mine");
            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            _sim.AssignRunner(_runner.Id, null);

            Assert.AreEqual(RunnerState.Idle, _runner.State);
            Assert.IsNull(_runner.TaskSequence);
        }

        [Test]
        public void AssignRunner_NullSequence_DoesNothing()
        {
            Setup("hub");

            _sim.AssignRunner(_runner.Id, null);

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

            var assignment = TaskSequence.CreateLoop("mine", "hub");
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

            var assignment = TaskSequence.CreateLoop("mine", "hub");
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

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);
            // Manually move to Deposit step (index 3) — AssignRunner resets to 0
            _runner.TaskSequenceCurrentStepIndex = 3;
            _runner.State = RunnerState.Idle;
            _sim.Tick(); // ExecuteCurrentStep sees Deposit step

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

            var assignment = TaskSequence.CreateLoop("mine", "hub");

            RunnerDeposited? deposited = null;
            _sim.Events.Subscribe<RunnerDeposited>(e => deposited = e);

            _sim.AssignRunner(_runner.Id, assignment);
            _runner.TaskSequenceCurrentStepIndex = 3;
            _runner.State = RunnerState.Idle;
            _sim.Tick(); // enters Depositing

            // Tick one less than the remaining duration — should still be depositing
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

            var assignment = TaskSequence.CreateLoop("mine", "hub");

            RunnerDeposited? deposited = null;
            _sim.Events.Subscribe<RunnerDeposited>(e => deposited = e);

            _sim.AssignRunner(_runner.Id, assignment);
            _runner.TaskSequenceCurrentStepIndex = 3;
            _runner.State = RunnerState.Idle;
            _sim.Tick(); // enters Depositing

            // Tick through the remaining deposit duration
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

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);
            _runner.TaskSequenceCurrentStepIndex = 3;
            _runner.State = RunnerState.Idle;
            _sim.Tick(); // enters Depositing

            // Tick through deposit
            for (int i = 0; i < _config.DepositDurationTicks; i++)
                _sim.Tick();

            // Should now be traveling to mine (step 0 after loop wrap)
            Assert.AreEqual(RunnerState.Traveling, _runner.State);
            Assert.AreEqual("mine", _runner.Travel.ToNodeId);
        }

        // ─── Step events ────────────────────────────────────────

        [Test]
        public void MacroStep_TaskSequenceStepAdvanced_EventFiredOnStepCompletion()
        {
            Setup("mine");

            int advancedCount = 0;
            _sim.Events.Subscribe<TaskSequenceStepAdvanced>(e => advancedCount++);

            var assignment = TaskSequence.CreateLoop("mine", "hub");
            _sim.AssignRunner(_runner.Id, assignment);

            // Runner is at mine — TravelTo(mine) is skipped (already there),
            // which advances to Work step and fires the event.
            Assert.Greater(advancedCount, 0);
        }
    }
}

using System;
using System.Collections.Generic;
using ProjectGuild.Simulation.Automation;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Items;
using ProjectGuild.Simulation.Tutorial;
using ProjectGuild.Simulation.World;

namespace ProjectGuild.Simulation.Core
{
    /// <summary>
    /// The root of all simulation state. Everything that needs to be saved/loaded
    /// lives under this object. This is the single source of truth for the game world.
    /// </summary>
    [Serializable]
    public class GameState
    {
        public List<Runner> Runners = new();
        public long TickCount;
        public float TotalTimeElapsed;

        public WorldMap Map;
        public Bank Bank = new();
        public DecisionLog MacroDecisionLog = new();
        public DecisionLog MicroDecisionLog = new();
        public DecisionLog CombatDecisionLog = new();
        public LogbookState Logbook = new();
        public TutorialState Tutorial = new();

        // ─── Global Automation Libraries ────────────────────────────
        // Named templates with IDs. Runners hold string refs into these.
        // Editing a library entry immediately affects all runners/sequences referencing it.
        public List<TaskSequence> TaskSequenceLibrary = new();
        public List<Ruleset> MacroRulesetLibrary = new();
        public List<Ruleset> MicroRulesetLibrary = new();

        // ─── Automation Template Libraries ─────────────────────────
        // Reusable step/rule snippets for batch-insertion into sequences and rulesets.
        public List<StepTemplate> StepTemplateLibrary = new();
        public List<RuleTemplate> MacroRuleTemplateLibrary = new();
        public List<RuleTemplate> MicroRuleTemplateLibrary = new();

        // ─── Combat ────────────────────────────────────────────────
        /// <summary>
        /// Global library of combat styles. Runners hold string refs into this.
        /// Editing a library entry affects all runners using it.
        /// </summary>
        public List<CombatStyle> CombatStyleLibrary = new();

        /// <summary>
        /// Per-node encounter state for active combat encounters.
        /// Key = nodeId. Created when a runner starts fighting, removed when last runner leaves/dies.
        /// </summary>
        public Dictionary<string, EncounterState> EncounterStates = new();

        // Economy state (TODO)
    }
}

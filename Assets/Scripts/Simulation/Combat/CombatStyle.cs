using System;
using System.Collections.Generic;

namespace ProjectGuild.Simulation.Combat
{
    /// <summary>
    /// A combat style defines how a runner fights: targeting priority and ability selection.
    /// Parallel to Ruleset: lives in a global library, runners hold ID references.
    /// Full rule evaluation logic comes in Batch C; this is the data structure.
    /// </summary>
    [Serializable]
    public class CombatStyle
    {
        /// <summary>
        /// Unique identifier for library lookups. Null when not in a library.
        /// </summary>
        public string Id;

        /// <summary>
        /// Player-facing display name (e.g. "Fire Mage", "Tank", "Healer").
        /// </summary>
        public string Name;

        /// <summary>
        /// Targeting rules evaluated top-to-bottom, first match wins.
        /// Determines which enemy the runner attacks.
        /// </summary>
        public List<TargetingRule> TargetingRules = new();

        /// <summary>
        /// Ability rules evaluated top-to-bottom, first match wins.
        /// Determines which ability to use on the selected target.
        /// </summary>
        public List<AbilityRule> AbilityRules = new();

        public CombatStyle() { }

        public CombatStyle DeepCopy()
        {
            var copy = new CombatStyle
            {
                Id = Guid.NewGuid().ToString(),
                Name = Name,
            };
            foreach (var rule in TargetingRules)
                copy.TargetingRules.Add(rule.DeepCopy());
            foreach (var rule in AbilityRules)
                copy.AbilityRules.Add(rule.DeepCopy());
            return copy;
        }
    }

    /// <summary>
    /// How the runner selects a target.
    /// </summary>
    public enum TargetSelection
    {
        NearestEnemy,
        LowestHpEnemy,
        HighestHpEnemy,
        NearestAlly,
        LowestHpAlly,
    }

    /// <summary>
    /// A targeting rule: IF conditions THEN select target using this method.
    /// </summary>
    [Serializable]
    public class TargetingRule
    {
        public List<CombatCondition> Conditions = new();
        public TargetSelection Selection = TargetSelection.NearestEnemy;
        public bool Enabled = true;
        public string Label = "";

        public TargetingRule DeepCopy()
        {
            var copy = new TargetingRule
            {
                Selection = Selection,
                Enabled = Enabled,
                Label = Label,
            };
            foreach (var cond in Conditions)
                copy.Conditions.Add(cond.DeepCopy());
            return copy;
        }
    }

    /// <summary>
    /// An ability rule: IF conditions THEN use this ability.
    /// </summary>
    [Serializable]
    public class AbilityRule
    {
        public List<CombatCondition> Conditions = new();
        public string AbilityId;
        public bool Enabled = true;
        public bool CanInterrupt;
        public string Label = "";

        public AbilityRule DeepCopy()
        {
            var copy = new AbilityRule
            {
                AbilityId = AbilityId,
                Enabled = Enabled,
                CanInterrupt = CanInterrupt,
                Label = Label,
            };
            foreach (var cond in Conditions)
                copy.Conditions.Add(cond.DeepCopy());
            return copy;
        }
    }
}

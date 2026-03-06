using UnityEditor;
using UnityEngine;
using ProjectGuild.Data;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View
{
    /// <summary>
    /// Editor menu item that generates ability and enemy ScriptableObject assets.
    /// Safe to re-run: skips existing SOs by checking Id.
    /// </summary>
    public static class CombatContentGenerator
    {
        private const string AbilityRoot = "Assets/ScriptableObjects/Combat/Abilities";
        private const string EnemyRoot = "Assets/ScriptableObjects/Combat/Enemies";

        [MenuItem("Tools/Project Guild/Generate Combat Content")]
        public static void Generate()
        {
            int created = 0;
            created += GenerateAbilities();
            created += GenerateEnemies();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (created > 0)
            {
                Debug.Log($"[CombatContentGenerator] Created {created} new asset(s). " +
                    "Wire AbilityDefinitions array on SimulationConfigAsset in Inspector " +
                    "(drag all ability SOs into the array).");
            }
            else
            {
                Debug.Log("[CombatContentGenerator] All assets already exist. Nothing created.");
            }
        }

        private static int GenerateAbilities()
        {
            int created = 0;

            // Melee abilities
            created += CreateAbility("Melee", "basic_attack", "Basic Attack", SkillType.Melee,
                actionTime: 10, cooldown: 0, mana: 0, unlockLevel: 0,
                "A basic melee attack.",
                new AbilityEffectData { Type = EffectType.Damage, BaseValue = 10, ScalingStat = SkillType.Melee, ScalingFactor = 1.0f });

            created += CreateAbility("Melee", "taunt", "Taunt", SkillType.Melee,
                actionTime: 5, cooldown: 30, mana: 0, unlockLevel: 3,
                "Forces a single enemy to attack you.",
                new AbilityEffectData { Type = EffectType.Taunt, BaseValue = 1, ScalingStat = SkillType.Melee, ScalingFactor = 1.0f });

            created += CreateAbility("Melee", "mass_taunt", "Mass Taunt", SkillType.Melee,
                actionTime: 10, cooldown: 100, mana: 0, unlockLevel: 8,
                "Forces all nearby enemies to attack you.",
                new AbilityEffectData { Type = EffectType.TauntAoe, BaseValue = 99, ScalingStat = SkillType.Melee, ScalingFactor = 1.0f });

            created += CreateAbility("Melee", "bloodthirst", "Bloodthirst", SkillType.Melee,
                actionTime: 10, cooldown: 0, mana: 0, unlockLevel: 10,
                "A weaker strike that heals you on a killing blow.",
                new AbilityEffectData { Type = EffectType.Damage, BaseValue = 7, ScalingStat = SkillType.Melee, ScalingFactor = 0.7f },
                new AbilityEffectData { Type = EffectType.HealSelf, BaseValue = 10, ScalingStat = SkillType.Melee, ScalingFactor = 1.0f,
                    HasCondition = true, ConditionType = AbilityEffectConditionType.IsKillingBlow, ConditionThreshold = 0 });

            // Magic abilities
            created += CreateAbility("Magic", "fireball", "Fireball", SkillType.Magic,
                actionTime: 20, cooldown: 0, mana: 0, unlockLevel: 1,
                "A ball of fire that damages a single target.",
                new AbilityEffectData { Type = EffectType.Damage, BaseValue = 10, ScalingStat = SkillType.Magic, ScalingFactor = 1.0f });

            created += CreateAbility("Magic", "fire_nova", "Fire Nova", SkillType.Magic,
                actionTime: 35, cooldown: 50, mana: 0, unlockLevel: 5,
                "An explosion of fire that damages all nearby enemies.",
                new AbilityEffectData { Type = EffectType.DamageAoe, BaseValue = 8, ScalingStat = SkillType.Magic, ScalingFactor = 0.6f });

            created += CreateAbility("Magic", "culling_frost", "Culling Frost", SkillType.Magic,
                actionTime: 25, cooldown: 0, mana: 0, unlockLevel: 8,
                "Ice damage that deals massive bonus damage to low-health targets (below 35% HP).",
                new AbilityEffectData { Type = EffectType.Damage, BaseValue = 4, ScalingStat = SkillType.Magic, ScalingFactor = 0.4f },
                new AbilityEffectData { Type = EffectType.Damage, BaseValue = 10, ScalingStat = SkillType.Magic, ScalingFactor = 2.5f,
                    HasCondition = true, ConditionType = AbilityEffectConditionType.TargetHpBelowPercent, ConditionThreshold = 35 });

            // Restoration abilities (only these cost mana)
            created += CreateAbility("Restoration", "heal", "Heal", SkillType.Restoration,
                actionTime: 18, cooldown: 0, mana: 15, unlockLevel: 1,
                "Heals an allied runner.",
                new AbilityEffectData { Type = EffectType.Heal, BaseValue = 10, ScalingStat = SkillType.Restoration, ScalingFactor = 1.0f });

            created += CreateAbility("Restoration", "circle_of_mending", "Circle of Mending", SkillType.Restoration,
                actionTime: 30, cooldown: 40, mana: 30, unlockLevel: 8,
                "Heals all nearby allies.",
                new AbilityEffectData { Type = EffectType.HealAoe, BaseValue = 8, ScalingStat = SkillType.Restoration, ScalingFactor = 0.8f });

            created += CreateAbility("Restoration", "greater_heal", "Greater Heal", SkillType.Restoration,
                actionTime: 25, cooldown: 0, mana: 25, unlockLevel: 15,
                "A powerful single-target heal.",
                new AbilityEffectData { Type = EffectType.Heal, BaseValue = 25, ScalingStat = SkillType.Restoration, ScalingFactor = 2.5f });

            return created;
        }

        private static int GenerateEnemies()
        {
            int created = 0;

            created += CreateEnemy("goblin_grunt", "Goblin Grunt", level: 3,
                hp: 80, damage: 8, defence: 2, atkSpeed: 15,
                EnemyAiBehavior.Aggressive);

            created += CreateEnemy("goblin_shaman", "Goblin Shaman", level: 5,
                hp: 60, damage: 12, defence: 1, atkSpeed: 20,
                EnemyAiBehavior.Opportunistic);

            return created;
        }

        private static int CreateAbility(string subfolder, string id, string displayName,
            SkillType skillType, int actionTime, int cooldown, float mana, int unlockLevel,
            string description, params AbilityEffectData[] effects)
        {
            string folderPath = $"{AbilityRoot}/{subfolder}";
            EnsureFolder(folderPath);

            // Check if already exists by scanning folder
            string[] guids = AssetDatabase.FindAssets("t:AbilityConfigAsset", new[] { folderPath });
            foreach (string guid in guids)
            {
                var existing = AssetDatabase.LoadAssetAtPath<AbilityConfigAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (existing != null && existing.Id == id)
                    return 0; // Already exists
            }

            var asset = ScriptableObject.CreateInstance<AbilityConfigAsset>();
            asset.Id = id;
            asset.Name = displayName;
            asset.SkillType = skillType;
            asset.ActionTimeTicks = actionTime;
            asset.CooldownTicks = cooldown;
            asset.ManaCost = mana;
            asset.UnlockLevel = unlockLevel;
            asset.Description = description;
            asset.Effects = effects;

            string assetName = displayName.Replace(" ", "");
            string path = $"{folderPath}/{assetName}.asset";
            AssetDatabase.CreateAsset(asset, path);
            Debug.Log($"[CombatContentGenerator] Created ability: {path}");
            return 1;
        }

        private static int CreateEnemy(string id, string displayName, int level,
            float hp, float damage, float defence, int atkSpeed,
            EnemyAiBehavior behavior)
        {
            EnsureFolder(EnemyRoot);

            string[] guids = AssetDatabase.FindAssets("t:EnemyConfigAsset", new[] { EnemyRoot });
            foreach (string guid in guids)
            {
                var existing = AssetDatabase.LoadAssetAtPath<EnemyConfigAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (existing != null && existing.Id == id)
                    return 0;
            }

            var asset = ScriptableObject.CreateInstance<EnemyConfigAsset>();
            asset.Id = id;
            asset.Name = displayName;
            asset.Level = level;
            asset.MaxHitpoints = hp;
            asset.BaseDamage = damage;
            asset.BaseDefence = defence;
            asset.AttackSpeedTicks = atkSpeed;
            asset.AiBehavior = behavior;
            asset.LootTable = new LootTableEntryData[0]; // Loot wired manually in Inspector

            string assetName = displayName.Replace(" ", "");
            string path = $"{EnemyRoot}/{assetName}.asset";
            AssetDatabase.CreateAsset(asset, path);
            Debug.Log($"[CombatContentGenerator] Created enemy: {path}");
            return 1;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}

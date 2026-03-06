using UnityEngine;
using UnityEditor;
using ProjectGuild.Data;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View
{
    [CustomEditor(typeof(AbilityConfigAsset))]
    public class AbilityConfigAssetEditor : Editor
    {
        private static readonly int[] PreviewLevels = { 1, 15, 50, 99 };

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var asset = (AbilityConfigAsset)target;
            if (asset.Effects == null || asset.Effects.Length == 0) return;

            // Find a SimulationConfigAsset for tuning values
            var configAsset = FindSimulationConfig();
            SimulationConfig config = configAsset != null ? configAsset.ToConfig() : CreateFallbackConfig();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Computed Values", EditorStyles.boldLabel);

            foreach (var effectData in asset.Effects)
            {
                if (effectData == null) continue;

                var effect = new AbilityEffect(
                    effectData.Type, effectData.BaseValue,
                    effectData.ScalingStat, effectData.ScalingFactor);

                string effectLabel = effectData.Type.ToString();
                bool isDamage = effectData.Type == EffectType.Damage
                    || effectData.Type == EffectType.DamageAoe;
                bool isHeal = effectData.Type == EffectType.Heal
                    || effectData.Type == EffectType.HealSelf
                    || effectData.Type == EffectType.HealAoe;

                if (!isDamage && !isHeal) continue;

                string unit = isDamage ? "dmg" : "heal";
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(effectLabel);

                foreach (int level in PreviewLevels)
                {
                    float value;
                    if (isDamage)
                        value = CombatFormulas.CalculateDamage(effect, level, 0f, config);
                    else
                        value = CombatFormulas.CalculateHeal(effect, level, config);

                    EditorGUILayout.LabelField($"Lv{level}: {value:F1} {unit}", GUILayout.MinWidth(90));
                }

                EditorGUILayout.EndHorizontal();

                // Show formula
                EditorGUILayout.HelpBox(
                    $"Formula: {effectData.BaseValue} * {effectData.ScalingFactor} * (1 + level * {config.CombatDamageScalingPerLevel})",
                    MessageType.None);

                // Conditional effect preview
                if (effectData.HasCondition)
                {
                    string condText = effectData.ConditionType switch
                    {
                        AbilityEffectConditionType.TargetHpBelowPercent =>
                            $"Conditional: applies when target below {effectData.ConditionThreshold}% HP",
                        AbilityEffectConditionType.TargetHpAbovePercent =>
                            $"Conditional: applies when target above {effectData.ConditionThreshold}% HP",
                        AbilityEffectConditionType.IsKillingBlow =>
                            "Conditional: applies on killing blow",
                        _ => $"Conditional: {effectData.ConditionType}",
                    };
                    EditorGUILayout.LabelField(condText, EditorStyles.miniLabel);
                }
            }
        }

        private static SimulationConfigAsset FindSimulationConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:SimulationConfigAsset");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<SimulationConfigAsset>(path);
            }
            return null;
        }

        private static SimulationConfig CreateFallbackConfig()
        {
            return new SimulationConfig
            {
                CombatDamageScalingPerLevel = 0.02f,
                MaxDefenceReductionPercent = 75f,
            };
        }
    }
}

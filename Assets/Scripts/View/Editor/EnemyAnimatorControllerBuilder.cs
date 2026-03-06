using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ProjectGuild.View
{
    /// <summary>
    /// Builds a simple Animator Controller for enemies (goblins).
    /// States: Idle, Attack, HitReact, Death.
    /// Uses Synty Goblin Locomotion idle + Sword Combat attack/death clips.
    /// </summary>
    public static class EnemyAnimatorControllerBuilder
    {
        private const string OutputFolder = "Assets/Art/AnimatorControllers";

        private const string GoblinIdlePath =
            "Assets/Art/Synty/AnimationGoblinLocomotion/Animations/Polygon/Neutral/Idles/A_POLY_GBL_Idle_Standing_Neut.fbx";
        private const string AttackPath =
            "Assets/Art/Synty/AnimationSwordCombat/Animations/Polygon/Attack/LightCombo01/A_Attack_LightCombo01A_Sword.fbx";
        private const string HitReactPath =
            "Assets/Art/Synty/AnimationSwordCombat/Animations/Polygon/Hit/HitReact/A_Hit_F_React_Sword.fbx";
        private const string DeathPath =
            "Assets/Art/Synty/AnimationSwordCombat/Animations/Polygon/Death/A_Death_F_01_Sword.fbx";

        [MenuItem("Tools/Project Guild/Build Enemy Animator Controller")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art"))
                    AssetDatabase.CreateFolder("Assets", "Art");
                AssetDatabase.CreateFolder("Assets/Art", "AnimatorControllers");
            }

            var idleClip = LoadClipFromFbx(GoblinIdlePath);
            var attackClip = LoadClipFromFbx(AttackPath);
            var hitClip = LoadClipFromFbx(HitReactPath);
            var deathClip = LoadClipFromFbx(DeathPath);

            // Fallback idle if goblin idle not found
            if (idleClip == null)
            {
                idleClip = LoadClipFromFbx(
                    "Assets/Art/Synty/AnimationBaseLocomotion/Animations/Polygon/Masculine/Idle/A_Idle_Standing_Masc.fbx");
            }

            if (idleClip == null)
            {
                Debug.LogError("[EnemyAnimatorControllerBuilder] Could not find any idle animation clip.");
                return;
            }

            string path = $"{OutputFolder}/AC_Enemy_Goblin.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("InCombat", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);

            var sm = controller.layers[0].stateMachine;

            var idleState = sm.AddState("Idle", new Vector3(300, 0, 0));
            idleState.motion = idleClip;
            sm.defaultState = idleState;

            if (attackClip != null)
            {
                var attackState = sm.AddState("Attack", new Vector3(600, 0, 0));
                attackState.motion = attackClip;

                var toAttack = idleState.AddTransition(attackState);
                toAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
                toAttack.duration = 0.1f;
                toAttack.hasExitTime = false;

                var fromAttack = attackState.AddTransition(idleState);
                fromAttack.hasExitTime = true;
                fromAttack.exitTime = 0.9f;
                fromAttack.duration = 0.1f;
            }

            if (hitClip != null)
            {
                var hitState = sm.AddState("HitReact", new Vector3(600, 100, 0));
                hitState.motion = hitClip;

                var toHit = idleState.AddTransition(hitState);
                toHit.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
                toHit.duration = 0.05f;
                toHit.hasExitTime = false;

                var fromHit = hitState.AddTransition(idleState);
                fromHit.hasExitTime = true;
                fromHit.exitTime = 0.8f;
                fromHit.duration = 0.1f;
            }

            if (deathClip != null)
            {
                var deathState = sm.AddState("Death", new Vector3(300, 200, 0));
                deathState.motion = deathClip;

                var toDeath = sm.AddAnyStateTransition(deathState);
                toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Die");
                toDeath.duration = 0.1f;
                toDeath.hasExitTime = false;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[EnemyAnimatorControllerBuilder] Built AC_Enemy_Goblin controller.");
        }

        private static AnimationClip LoadClipFromFbx(string fbxPath)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (var asset in assets)
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
            }
            return null;
        }
    }
}

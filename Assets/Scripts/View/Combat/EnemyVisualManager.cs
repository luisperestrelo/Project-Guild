using System.Collections.Generic;
using UnityEngine;
using ProjectGuild.Bridge;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.Combat
{
    /// <summary>
    /// Manages enemy visual GameObjects in the 3D world. Subscribes to sim events
    /// to spawn/despawn enemy prefabs, update HP bars, and play animations.
    /// Parallel to VisualSyncSystem but for enemies (which are simpler: no travel, no scenes).
    /// </summary>
    public class EnemyVisualManager : MonoBehaviour
    {
        [Header("Enemy Prefabs")]
        [Tooltip("Prefab for Goblin Grunt enemies (PolygonDungeon goblin models).")]
        [SerializeField] private GameObject _goblinGruntPrefab;
        [Tooltip("Prefab for Goblin Shaman enemies.")]
        [SerializeField] private GameObject _goblinShamanPrefab;
        [Tooltip("Fallback prefab for enemies without a specific model.")]
        [SerializeField] private GameObject _fallbackEnemyPrefab;

        [Header("VFX Prefabs")]
        [Tooltip("Blood splat on melee hit.")]
        [SerializeField] private GameObject _bloodSplatVfx;
        [Tooltip("Fireball projectile.")]
        [SerializeField] private GameObject _fireballVfx;
        [Tooltip("Fire Nova explosion.")]
        [SerializeField] private GameObject _fireNovaVfx;
        [Tooltip("Fire impact explosion (spawns when fireball arrives).")]
        [SerializeField] private GameObject _fireImpactVfx;
        [Tooltip("Ice/frost effect for Culling Frost.")]
        [SerializeField] private GameObject _frostVfx;
        [Tooltip("Heal sparkle effect.")]
        [SerializeField] private GameObject _healVfx;
        [Tooltip("AoE heal circle.")]
        [SerializeField] private GameObject _healCircleVfx;

        [Header("References")]
        [SerializeField] private SimulationRunner _simulationRunner;
        [SerializeField] private WorldSceneManager _worldSceneManager;

        [Header("Settings")]
        [Tooltip("Time in seconds before a dead enemy's visual is hidden (after death anim plays).")]
        [SerializeField] private float _deathHideDelay = 2f;
        [Tooltip("Speed at which enemies walk toward their target runner.")]
        [SerializeField] private float _enemyWalkSpeed = 3f;
        [Tooltip("Speed of projectile VFX (fireball, etc.) in m/s.")]
        [SerializeField] private float _projectileSpeed = 6f;

        private float EnemyWalkSpeed => _enemyWalkSpeed;

        private readonly Dictionary<string, EnemyVisual> _enemyVisuals = new();
        private readonly Dictionary<string, string> _enemyNodeMap = new(); // instanceId → nodeId
        private readonly Dictionary<string, float> _deathTimers = new();
        private GameSimulation Sim => _simulationRunner?.Simulation;

        private void OnEnable()
        {
            if (_simulationRunner == null)
                _simulationRunner = FindAnyObjectByType<SimulationRunner>();
            if (_worldSceneManager == null)
                _worldSceneManager = FindAnyObjectByType<WorldSceneManager>();
        }

        public void Initialize()
        {
            if (Sim == null) return;

            Sim.Events.Subscribe<EnemySpawned>(OnEnemySpawned);
            Sim.Events.Subscribe<EnemyDied>(OnEnemyDied);
            Sim.Events.Subscribe<EncounterEnded>(OnEncounterEnded);
            Sim.Events.Subscribe<CombatActionCompleted>(OnCombatActionCompleted);
            Sim.Events.Subscribe<RunnerTookDamage>(OnRunnerTookDamage);

            // Spawn visuals for any enemies already alive (e.g. loaded game)
            foreach (var kvp in Sim.CurrentGameState.EncounterStates)
            {
                var encounter = kvp.Value;
                if (!encounter.IsActive) continue;
                foreach (var enemy in encounter.GetAliveEnemies())
                {
                    SpawnEnemyVisual(enemy, encounter.NodeId);
                }
            }
        }

        public EnemyVisual GetEnemyVisual(string instanceId)
        {
            return _enemyVisuals.TryGetValue(instanceId, out var v) ? v : null;
        }

        private void Update()
        {
            if (Sim == null) return;

            // Update HP bars for all alive enemies
            foreach (var kvp in Sim.CurrentGameState.EncounterStates)
            {
                var encounter = kvp.Value;
                if (!encounter.IsActive) continue;

                foreach (var enemy in encounter.Enemies)
                {
                    if (!_enemyVisuals.TryGetValue(enemy.InstanceId, out var visual)) continue;
                    if (visual == null) continue;

                    if (enemy.IsAlive)
                    {
                        var config = Sim.Config.GetEnemyConfig(enemy.ConfigId);
                        if (config != null)
                            visual.SetHpPercent(enemy.CurrentHp / config.MaxHitpoints);

                        // Walk toward target runner (or nearest)
                        MoveTowardTarget(visual, enemy, encounter.NodeId);
                    }
                }
            }

            // Process death timers (hide dead enemy visuals after delay)
            var expired = new List<string>();
            var keys = new List<string>(_deathTimers.Keys);
            foreach (var key in keys)
            {
                float remaining = _deathTimers[key] - Time.deltaTime;
                _deathTimers[key] = remaining;
                if (remaining <= 0f)
                {
                    expired.Add(key);
                    if (_enemyVisuals.TryGetValue(key, out var visual) && visual != null)
                        visual.gameObject.SetActive(false);
                }
            }
            foreach (var id in expired)
                _deathTimers.Remove(id);
        }

        private void OnEnemySpawned(EnemySpawned evt)
        {
            // Check if we already have a visual (respawn case)
            if (_enemyVisuals.TryGetValue(evt.EnemyInstanceId, out var existing) && existing != null)
            {
                existing.Respawn();
                _deathTimers.Remove(evt.EnemyInstanceId);
                return;
            }

            var encounter = Sim.CurrentGameState.EncounterStates.GetValueOrDefault(evt.NodeId);
            var enemy = encounter?.FindEnemy(evt.EnemyInstanceId);
            if (enemy != null)
                SpawnEnemyVisual(enemy, evt.NodeId);
        }

        private void OnEnemyDied(EnemyDied evt)
        {
            if (!_enemyVisuals.TryGetValue(evt.EnemyInstanceId, out var visual)) return;
            if (visual == null) return;

            visual.PlayDeath();
            _deathTimers[evt.EnemyInstanceId] = _deathHideDelay;
        }

        private void OnEncounterEnded(EncounterEnded evt)
        {
            // Destroy all enemy visuals for this node
            var toRemove = new List<string>();
            foreach (var kvp in _enemyNodeMap)
            {
                if (kvp.Value == evt.NodeId)
                    toRemove.Add(kvp.Key);
            }
            foreach (var id in toRemove)
            {
                _deathTimers.Remove(id);
                _enemyNodeMap.Remove(id);
                if (_enemyVisuals.TryGetValue(id, out var visual) && visual != null)
                    Destroy(visual.gameObject);
                _enemyVisuals.Remove(id);
            }
        }

        private void OnCombatActionCompleted(CombatActionCompleted evt)
        {
            // Play VFX based on ability effect type
            var runner = Sim.FindRunner(evt.RunnerId);
            if (runner == null) return;

            Vector3 casterPos = GetRunnerPosition(evt.RunnerId);
            Vector3 targetPos = casterPos;

            // Get target position
            if (!string.IsNullOrEmpty(evt.TargetEnemyInstanceId))
            {
                if (_enemyVisuals.TryGetValue(evt.TargetEnemyInstanceId, out var tv) && tv != null)
                {
                    targetPos = tv.transform.position;
                    // Enemy hit react
                    if (evt.PrimaryEffectType == EffectType.Damage || evt.PrimaryEffectType == EffectType.DamageAoe)
                        tv.PlayHitReact();
                }
            }

            SpawnAbilityVfx(evt.AbilityId, evt.PrimaryEffectType, casterPos, targetPos);
        }

        private void OnRunnerTookDamage(RunnerTookDamage evt)
        {
            // Enemy attacked a runner: play blood splat on runner
            if (_bloodSplatVfx != null)
            {
                Vector3 pos = GetRunnerPosition(evt.RunnerId);
                SpawnVfx(_bloodSplatVfx, pos + Vector3.up);
            }
        }

        private void SpawnEnemyVisual(EnemyInstance enemy, string nodeId)
        {
            var config = Sim.Config.GetEnemyConfig(enemy.ConfigId);
            if (config == null) return;

            // Pick prefab based on config ID
            GameObject prefab = PickPrefabForEnemy(enemy.ConfigId);
            if (prefab == null)
            {
                Debug.LogWarning($"[EnemyVisualManager] No prefab for enemy config '{enemy.ConfigId}'.");
                return;
            }

            var obj = Instantiate(prefab);

            // Position in the node scene
            Vector3 pos = GetEnemySpawnPosition(nodeId, enemy.SpawnEntryIndex, enemy.InstanceId);
            obj.transform.position = pos;

            // Add collider for click detection
            if (obj.GetComponentInChildren<Collider>() == null)
            {
                var col = obj.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, 0.7f, 0f);
                col.radius = 0.3f;
                col.height = 1.4f;
            }

            var visual = obj.AddComponent<EnemyVisual>();
            string displayName = config.Name ?? enemy.ConfigId;
            visual.Initialize(enemy.InstanceId, enemy.ConfigId, displayName, pos);

            _enemyVisuals[enemy.InstanceId] = visual;
            _enemyNodeMap[enemy.InstanceId] = nodeId;
        }

        private GameObject PickPrefabForEnemy(string configId)
        {
            // Match config IDs to prefabs
            if (configId.Contains("shaman")) return _goblinShamanPrefab != null ? _goblinShamanPrefab : _fallbackEnemyPrefab;
            if (configId.Contains("goblin")) return _goblinGruntPrefab != null ? _goblinGruntPrefab : _fallbackEnemyPrefab;
            return _fallbackEnemyPrefab;
        }

        private Vector3 GetEnemySpawnPosition(string nodeId, int spawnEntryIndex, string instanceId)
        {
            // Try to get position from node scene
            if (_worldSceneManager != null && _worldSceneManager.IsNodeSceneReady(nodeId))
            {
                var sceneRoot = _worldSceneManager.GetNodeSceneRoot(nodeId);
                if (sceneRoot != null && sceneRoot.EnemySpawnPoints != null
                    && sceneRoot.EnemySpawnPoints.Length > 0)
                {
                    // Spread enemies across spawn points using instance hash
                    int hash = instanceId.GetHashCode() & 0x7FFFFFFF;
                    int idx = hash % sceneRoot.EnemySpawnPoints.Length;
                    if (sceneRoot.EnemySpawnPoints[idx] != null)
                        return sceneRoot.EnemySpawnPoints[idx].position;
                }

                // Fallback: offset from scene root
                return sceneRoot != null
                    ? sceneRoot.transform.position + GetSpreadOffset(spawnEntryIndex)
                    : Vector3.zero;
            }

            return Vector3.zero;
        }

        private static Vector3 GetSpreadOffset(int index)
        {
            float angle = index * 1.2f;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 3f;
        }

        /// <summary>
        /// Move enemy toward its taunted runner or nearest fighter. Stops at melee range.
        /// </summary>
        private void MoveTowardTarget(EnemyVisual visual, EnemyInstance enemy, string nodeId)
        {
            Vector3 targetPos = visual.transform.position;
            bool found = false;

            // Priority: taunted runner
            if (!string.IsNullOrEmpty(enemy.TauntedByRunnerId))
            {
                Vector3 pos = GetRunnerPosition(enemy.TauntedByRunnerId);
                if (pos.sqrMagnitude > 0.01f)
                {
                    targetPos = pos;
                    found = true;
                }
            }

            // Fallback: nearest fighting runner at this node
            if (!found)
            {
                float bestDist = float.MaxValue;
                foreach (var runner in Sim.CurrentGameState.Runners)
                {
                    if (runner.State != RunnerState.Fighting || runner.CurrentNodeId != nodeId) continue;
                    Vector3 rPos = GetRunnerPosition(runner.Id);
                    float d = (rPos - visual.transform.position).sqrMagnitude;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        targetPos = rPos;
                        found = true;
                    }
                }
            }

            if (!found) return;

            visual.FaceTarget(targetPos);

            // Walk toward target, stop at melee range (~1.5m)
            const float meleeRange = 1.5f;
            Vector3 toTarget = targetPos - visual.transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;

            if (dist > meleeRange)
            {
                float step = EnemyWalkSpeed * Time.deltaTime;
                Vector3 move = toTarget.normalized * Mathf.Min(step, dist - meleeRange);
                visual.transform.position += move;
            }
        }

        private Vector3 GetRunnerPosition(string runnerId)
        {
            // Find the runner visual's position
            var vss = FindAnyObjectByType<VisualSyncSystem>();
            if (vss != null)
            {
                var rv = vss.GetRunnerVisual(runnerId);
                if (rv != null) return rv.transform.position;
            }
            return Vector3.zero;
        }

        private void SpawnAbilityVfx(string abilityId, EffectType effectType, Vector3 casterPos, Vector3 targetPos)
        {
            GameObject vfxPrefab = null;
            Vector3 spawnPos = targetPos + Vector3.up * 0.5f;

            if (abilityId != null && abilityId.Contains("fireball"))
            {
                // Fireball travels from caster to target
                if (_fireballVfx != null)
                    SpawnProjectile(_fireballVfx, casterPos + Vector3.up, targetPos + Vector3.up, _fireImpactVfx);
                return;
            }
            else if (abilityId != null && abilityId.Contains("fire_nova"))
            {
                vfxPrefab = _fireNovaVfx;
                spawnPos = casterPos;
            }
            else if (abilityId != null && abilityId.Contains("culling_frost"))
            {
                vfxPrefab = _frostVfx;
                spawnPos = targetPos + Vector3.up;
            }
            else if (abilityId != null && abilityId.Contains("circle_of_mending"))
            {
                vfxPrefab = _healCircleVfx;
                spawnPos = casterPos;
            }
            else if (effectType == EffectType.Heal || effectType == EffectType.HealSelf || effectType == EffectType.HealAoe)
            {
                vfxPrefab = _healVfx;
                spawnPos = targetPos + Vector3.up;
            }
            else if (effectType == EffectType.Damage && _bloodSplatVfx != null)
            {
                vfxPrefab = _bloodSplatVfx;
                spawnPos = targetPos + Vector3.up;
            }

            if (vfxPrefab != null)
                SpawnVfx(vfxPrefab, spawnPos);
        }

        private void SpawnVfx(GameObject prefab, Vector3 position)
        {
            var obj = Instantiate(prefab, position, Quaternion.identity);
            // Auto-destroy after particle systems finish
            var ps = obj.GetComponentInChildren<ParticleSystem>();
            float duration = ps != null ? ps.main.duration + ps.main.startLifetime.constantMax + 0.5f : 3f;
            Destroy(obj, duration);
        }

        private void SpawnProjectile(GameObject prefab, Vector3 origin, Vector3 target, GameObject impactPrefab)
        {
            var obj = Instantiate(prefab, origin, Quaternion.identity);
            var proj = obj.AddComponent<ProjectileVfx>();
            proj.Launch(target, _projectileSpeed, impactPrefab);
        }

        public void Cleanup()
        {
            foreach (var kvp in _enemyVisuals)
                if (kvp.Value != null)
                    Destroy(kvp.Value.gameObject);
            _enemyVisuals.Clear();
            _enemyNodeMap.Clear();
            _deathTimers.Clear();
        }

        private void OnDestroy()
        {
            if (Sim?.Events != null)
            {
                Sim.Events.Unsubscribe<EnemySpawned>(OnEnemySpawned);
                Sim.Events.Unsubscribe<EnemyDied>(OnEnemyDied);
                Sim.Events.Unsubscribe<EncounterEnded>(OnEncounterEnded);
                Sim.Events.Unsubscribe<CombatActionCompleted>(OnCombatActionCompleted);
                Sim.Events.Unsubscribe<RunnerTookDamage>(OnRunnerTookDamage);
            }
            Cleanup();
        }
    }
}

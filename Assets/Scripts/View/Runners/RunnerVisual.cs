using TMPro;
using UnityEngine;
using ProjectGuild.Bridge;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.Runners
{
    /// <summary>
    /// Visual representation of a runner in the 3D world. Managed by VisualSyncSystem —
    /// don't create these manually. The sync system spawns one per runner and updates
    /// its position each frame by interpolating between the simulation's tick positions.
    ///
    /// Four movement modes (caller decides which to use):
    /// - SetTargetPosition: tick interpolation (0.1s) for sim-driven travel
    /// - WalkToPosition: visible walk at WalkSpeed for in-node movement (straight line)
    /// - WalkAlongPath: visible walk through NavMesh waypoints (avoids obstacles)
    /// - SnapToPosition: instant teleport for scene transitions and initial placement
    ///
    /// Supports Synty character prefabs with Animator. When an Animator is present,
    /// drives a "Speed" float parameter (0=idle, 1=moving) for locomotion blending.
    /// Root motion is disabled — movement is fully code-driven.
    /// </summary>
    public class RunnerVisual : MonoBehaviour
    {
        /// <summary>
        /// The simulation runner ID this visual represents.
        /// </summary>
        public string RunnerId { get; private set; }
        public string RunnerName { get; private set; }

        // Interpolation state (current segment)
        private Vector3 _previousPosition;
        private Vector3 _targetPosition;
        private float _interpolationT;
        private float _interpolationSpeed = 10f; // 1/duration — default completes in 0.1s (one tick)

        // Multi-waypoint path (NavMesh walks)
        private Vector3[] _pathWaypoints;
        private int _pathIndex; // index of waypoint we're currently walking TOWARD

        // Walk speed for within-node movement (gathering spot changes, repositioning, etc.)
        // Settable so VisualSyncSystem can apply athletics-based in-node speed per runner.
        private float _walkSpeed = 8f;
        public float WalkSpeed { get => _walkSpeed; set => _walkSpeed = value; }

        // Animation
        private Animator _animator;
        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int AttackTrigger = Animator.StringToHash("Attack");
        private static readonly int CastTrigger = Animator.StringToHash("Cast");
        private static readonly int HitTrigger = Animator.StringToHash("Hit");
        private static readonly int DieTrigger = Animator.StringToHash("Die");
        private static readonly int InCombatParam = Animator.StringToHash("InCombat");
        private static readonly int IsDeadParam = Animator.StringToHash("IsDead");

        // Ground snap: Y offset from ground surface to transform.position.
        // Capsule pivot is at center (1m), Synty character pivot is at feet (0m).
        private float _groundYOffset;

        // Name label
        private TextMeshPro _nameLabel;
        private const float NameLabelHeightPrefab = 2.5f;
        private const float NameLabelHeightCapsule = 2.2f;

        // Nameplate HP/Mana bars
        private const float BarGroupOffsetFromLabel = -0.2f;
        private const float HpBarWidth = 1.2f;
        private const float HpBarHeight = 0.08f;
        private const float ManaBarHeight = 0.06f;
        private const float ManaBarGap = 0.02f;

        private GameObject _barGroup;
        private SpriteRenderer _hpFillRenderer;
        private Transform _hpFillTransform;
        private GameObject _manaBarGroup;
        private SpriteRenderer _manaFillRenderer;
        private Transform _manaFillTransform;

        // Sim data set by VisualSyncSystem each frame
        public Runner SimRunner { get; set; }
        public SimulationConfig SimConfig { get; set; }
        public long CurrentTick { get; set; }
        public PlayerPreferences DisplayPrefs { get; set; }

        public void Initialize(string runnerId, string runnerName, Vector3 startPosition)
        {
            RunnerId = runnerId;
            RunnerName = runnerName;
            transform.position = startPosition;
            _previousPosition = startPosition;
            _targetPosition = startPosition;
            _interpolationT = 1f;
            _pathWaypoints = null;
            _lastFramePosition = startPosition;
            gameObject.name = $"Runner_{runnerName}";

            // Detect and configure Animator (Synty character prefabs)
            _animator = GetComponentInChildren<Animator>();
            if (_animator != null)
            {
                _animator.applyRootMotion = false;
                _groundYOffset = 0f; // Synty characters have pivot at feet
            }
            else
            {
                _groundYOffset = 1f; // Capsule pivot is at center, half-height = 1m
            }

            // Create a floating name label above the character
            bool hasPrefabModel = _animator != null;
            float labelHeight = hasPrefabModel ? NameLabelHeightPrefab : NameLabelHeightCapsule;
            var labelObj = new GameObject("NameLabel");
            labelObj.transform.SetParent(transform);
            labelObj.transform.localPosition = new Vector3(0f, labelHeight, 0f);
            _nameLabel = labelObj.AddComponent<TextMeshPro>();
            _nameLabel.text = runnerName;
            _nameLabel.fontSize = 4f;
            _nameLabel.alignment = TextAlignmentOptions.Center;
            _nameLabel.color = Color.white;
            _nameLabel.rectTransform.sizeDelta = new Vector2(6f, 2f);

            // Create HP/Mana bar group below the name label
            float barGroupY = labelHeight + BarGroupOffsetFromLabel;
            CreateNameplateBars(barGroupY);
        }

        /// <summary>
        /// Tick interpolation for sim-driven travel. Completes in one tick interval (0.1s).
        /// Called every frame by VisualSyncSystem — only resets when target actually changes.
        /// </summary>
        public void SetTargetPosition(Vector3 newTarget)
        {
            if ((_targetPosition - newTarget).sqrMagnitude < 0.0001f) return;

            _pathWaypoints = null;
            _previousPosition = transform.position;
            _targetPosition = newTarget;
            _interpolationT = 0f;
            _interpolationSpeed = 10f; // 0.1s = one tick interval
        }

        /// <summary>
        /// Visible walk to a position at WalkSpeed (straight line).
        /// Used when no NavMesh path is available.
        /// </summary>
        public void WalkToPosition(Vector3 target)
        {
            if ((target - FinalDestination).sqrMagnitude < 0.0001f) return;

            _pathWaypoints = null;
            _previousPosition = transform.position;
            _targetPosition = target;
            _interpolationT = 0f;

            float distance = (_targetPosition - _previousPosition).magnitude;
            _interpolationSpeed = distance > 0.01f ? WalkSpeed / distance : 10f;
        }

        /// <summary>
        /// Walk along a multi-waypoint path at WalkSpeed (NavMesh-computed).
        /// Walks through each waypoint in sequence, avoiding obstacles.
        /// </summary>
        public void WalkAlongPath(Vector3[] waypoints)
        {
            if (waypoints == null || waypoints.Length == 0) return;

            // Skip if already walking to the same final destination
            if ((waypoints[^1] - FinalDestination).sqrMagnitude < 0.0001f) return;

            _pathWaypoints = waypoints;
            _pathIndex = 0;
            _previousPosition = transform.position;
            _targetPosition = waypoints[0];
            _interpolationT = 0f;

            float distance = (_targetPosition - _previousPosition).magnitude;
            _interpolationSpeed = distance > 0.01f ? WalkSpeed / distance : 10f;
        }

        /// <summary>
        /// Snap immediately to a position (no interpolation). Used for scene transitions,
        /// initial placement, and cross-scene jumps.
        /// </summary>
        public void SnapToPosition(Vector3 position)
        {
            _pathWaypoints = null;
            transform.position = position;
            _previousPosition = position;
            _targetPosition = position;
            _interpolationT = 1f;
        }

        /// <summary>
        /// The final destination of the current movement (last waypoint if on a path,
        /// or the single target position).
        /// </summary>
        private Vector3 FinalDestination =>
            _pathWaypoints != null && _pathWaypoints.Length > 0
                ? _pathWaypoints[^1]
                : _targetPosition;

        // Combat visual state
        private bool _isDead;
        private bool _isHidden;

        // Track position for velocity-based animation
        private Vector3 _lastFramePosition;

        // ─── Combat Animation API ──────────────────────────────

        public void PlayMeleeAttack()
        {
            if (_animator != null && !_isDead)
                _animator.SetTrigger(AttackTrigger);
        }

        public void PlayCastSpell()
        {
            if (_animator != null && !_isDead)
                _animator.SetTrigger(CastTrigger);
        }

        public void PlayHitReact()
        {
            if (_animator != null && !_isDead)
                _animator.SetTrigger(HitTrigger);
        }

        public void SetCombatState(bool inCombat)
        {
            if (_animator != null)
                _animator.SetBool(InCombatParam, inCombat);
        }

        public void SetDead(bool dead)
        {
            _isDead = dead;
            if (_animator != null)
            {
                if (dead)
                    _animator.SetTrigger(DieTrigger);
                _animator.SetBool(IsDeadParam, dead);
            }
        }

        public void SetHidden(bool hidden)
        {
            _isHidden = hidden;
            // Hide all renderers but keep the GO active for position tracking
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = !hidden;
            if (_nameLabel != null)
                _nameLabel.gameObject.SetActive(!hidden);
            if (_barGroup != null)
                _barGroup.SetActive(!hidden);
        }

        public void FaceTarget(Vector3 targetPos)
        {
            Vector3 dir = targetPos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(dir);
        }

        private void Update()
        {
            if (_interpolationT < 1f)
            {
                _interpolationT += Time.deltaTime * _interpolationSpeed;
                if (_interpolationT > 1f) _interpolationT = 1f;

                Vector3 lerpedPos = Vector3.Lerp(_previousPosition, _targetPosition, _interpolationT);
                transform.position = lerpedPos;

                // Face movement direction
                Vector3 direction = _targetPosition - _previousPosition;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(direction);
                }
            }

            // Advance to next waypoint when current segment is complete
            if (_interpolationT >= 1f && _pathWaypoints != null)
            {
                _pathIndex++;
                if (_pathIndex < _pathWaypoints.Length)
                {
                    _previousPosition = _targetPosition;
                    _targetPosition = _pathWaypoints[_pathIndex];
                    _interpolationT = 0f;

                    float distance = (_targetPosition - _previousPosition).magnitude;
                    _interpolationSpeed = distance > 0.01f ? WalkSpeed / distance : 10f;
                }
                else
                {
                    // Path complete
                    _pathWaypoints = null;
                }
            }

            // Snap to ground every frame (not just during interpolation)
            transform.position = SnapToGround(transform.position, _groundYOffset);

            // Drive animation from actual velocity (avoids stop-go flicker between ticks)
            if (_animator != null)
            {
                Vector3 delta = transform.position - _lastFramePosition;
                delta.y = 0f;
                float frameSpeed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.001f);
                _animator.SetFloat(SpeedParam, frameSpeed > 0.1f ? 1f : 0f);
            }
            _lastFramePosition = transform.position;

            // Billboard name label and bars — Y-axis only so they stay upright from any angle
            if (Camera.main != null)
            {
                var camForward = Camera.main.transform.forward;
                camForward.y = 0f;
                if (camForward.sqrMagnitude > 0.001f)
                {
                    var rot = Quaternion.LookRotation(camForward);
                    if (_nameLabel != null)
                        _nameLabel.transform.rotation = rot;
                    if (_barGroup != null)
                        _barGroup.transform.rotation = rot;
                }
            }

            // Update nameplate bar visuals
            UpdateNameplateBars();
        }

        // ─── Nameplate Bars ──────────────────────────────

        private void CreateNameplateBars(float groupY)
        {
            _barGroup = new GameObject("BarGroup");
            _barGroup.transform.SetParent(transform);
            _barGroup.transform.localPosition = new Vector3(0f, groupY, 0f);

            // HP background
            var hpBg = CreateBarSprite("HpBackground", _barGroup.transform,
                Vector3.zero, HpBarWidth, HpBarHeight,
                new Color(0f, 0f, 0f, 0.5f), sortOrder: 0);

            // HP fill (anchored left)
            var hpFillObj = CreateBarSprite("HpFill", _barGroup.transform,
                Vector3.zero, HpBarWidth, HpBarHeight,
                new Color(0.31f, 0.78f, 0.31f), sortOrder: 1);
            _hpFillRenderer = hpFillObj.GetComponent<SpriteRenderer>();
            _hpFillTransform = hpFillObj.transform;

            // Mana background (below HP)
            float manaY = -(HpBarHeight + ManaBarGap);
            var manaBg = CreateBarSprite("ManaBackground", _barGroup.transform,
                new Vector3(0f, manaY, 0f), HpBarWidth, ManaBarHeight,
                new Color(0f, 0f, 0f, 0.5f), sortOrder: 0);

            // Mana fill
            var manaFillObj = CreateBarSprite("ManaFill", _barGroup.transform,
                new Vector3(0f, manaY, 0f), HpBarWidth, ManaBarHeight,
                new Color(0.27f, 0.51f, 0.86f), sortOrder: 1);
            _manaFillRenderer = manaFillObj.GetComponent<SpriteRenderer>();
            _manaFillTransform = manaFillObj.transform;

            _manaBarGroup = manaBg.transform.parent.gameObject;
            // Group mana bg + fill under one parent for easy show/hide
            var manaGroupObj = new GameObject("ManaGroup");
            manaGroupObj.transform.SetParent(_barGroup.transform);
            manaGroupObj.transform.localPosition = Vector3.zero;
            manaBg.transform.SetParent(manaGroupObj.transform, true);
            manaFillObj.transform.SetParent(manaGroupObj.transform, true);
            _manaBarGroup = manaGroupObj;
        }

        private static GameObject CreateBarSprite(string name, Transform parent,
            Vector3 localPos, float width, float height, Color color, int sortOrder)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent);
            obj.transform.localPosition = localPos;
            obj.transform.localScale = new Vector3(width, height, 1f);

            var sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite = CreateWhiteSprite();
            sr.color = color;
            sr.sortingOrder = sortOrder;

            return obj;
        }

        private static Sprite _cachedWhiteSprite;

        private static Sprite CreateWhiteSprite()
        {
            if (_cachedWhiteSprite != null) return _cachedWhiteSprite;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _cachedWhiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return _cachedWhiteSprite;
        }

        private void UpdateNameplateBars()
        {
            if (_barGroup == null || SimRunner == null || SimConfig == null) return;

            var prefs = DisplayPrefs;
            bool showNameplates = prefs == null || prefs.ShowRunnerNameplates;

            // Name label visibility
            if (_nameLabel != null)
                _nameLabel.gameObject.SetActive(showNameplates);

            bool showBars = showNameplates && (prefs == null || prefs.ShowNameplateHealthBars);
            _barGroup.SetActive(showBars);

            if (!showBars) return;

            // HP fill: 0 when dead, 100% when uninitialized, actual value otherwise
            float hpPercent;
            if (SimRunner.State == RunnerState.Dead)
            {
                hpPercent = 0f;
            }
            else if (SimRunner.CurrentHitpoints < 0f)
            {
                hpPercent = 1f;
            }
            else
            {
                float maxHp = CombatFormulas.CalculateMaxHitpoints(
                    SimRunner.GetEffectiveLevel(SkillType.Hitpoints, SimConfig), SimConfig);
                hpPercent = maxHp > 0f ? Mathf.Clamp01(SimRunner.CurrentHitpoints / maxHp) : 0f;
            }

            // Scale X from left: adjust localScale.x and shift position so left edge stays anchored
            float hpWidth = hpPercent * HpBarWidth;
            _hpFillTransform.localScale = new Vector3(hpWidth, HpBarHeight, 1f);
            _hpFillTransform.localPosition = new Vector3((hpWidth - HpBarWidth) * 0.5f, 0f, 0f);

            // HP color
            if (hpPercent > 0.5f)
                _hpFillRenderer.color = new Color(0.31f, 0.78f, 0.31f); // green
            else if (hpPercent > 0.25f)
                _hpFillRenderer.color = new Color(0.86f, 0.71f, 0.20f); // yellow
            else
                _hpFillRenderer.color = new Color(0.78f, 0.24f, 0.24f); // red

            // Mana visibility
            string manaPref = prefs != null ? prefs.NameplateManaDisplay : "WhenUsed";
            float maxMana = CombatFormulas.CalculateMaxMana(
                SimRunner.GetEffectiveLevel(SkillType.Restoration, SimConfig), SimConfig);

            bool showMana = manaPref != "Never"
                && SimRunner.CurrentMana >= 0f
                && (manaPref == "Always"
                    || (SimRunner.LastManaSpentTick >= 0 && CurrentTick - SimRunner.LastManaSpentTick < 100)
                    || SimRunner.CurrentMana < maxMana);

            _manaBarGroup.SetActive(showMana);

            if (showMana)
            {
                float manaPercent = maxMana > 0f ? Mathf.Clamp01(SimRunner.CurrentMana / maxMana) : 0f;
                float manaWidth = manaPercent * HpBarWidth;
                float manaY = -(HpBarHeight + ManaBarGap);
                _manaFillTransform.localScale = new Vector3(manaWidth, ManaBarHeight, 1f);
                _manaFillTransform.localPosition = new Vector3((manaWidth - HpBarWidth) * 0.5f, manaY, 0f);
            }
        }

        /// <summary>
        /// Raycast down from above the position to stick the runner to the ground surface.
        /// Works with terrain, mesh colliders, any physics surface.
        /// Falls back to the original position if nothing is hit (flat plane, no collider).
        /// </summary>
        private static int _groundLayerMask = -1;

        /// <summary>
        /// Raycast down to find the ground surface, then place the runner at
        /// groundY + yOffset. The yOffset preserves the hovering offset that
        /// VisualSyncSystem bakes into target positions (RunnerYOffset = 1m
        /// for capsules so the pivot sits above ground, 0 for character models).
        /// </summary>
        private static Vector3 SnapToGround(Vector3 pos, float yOffset)
        {
            // Cache layer mask excluding the Runners layer
            if (_groundLayerMask == -1)
            {
                int runnersLayer = LayerMask.NameToLayer("Runners");
                _groundLayerMask = runnersLayer >= 0 ? ~(1 << runnersLayer) : ~0;
            }

            Vector3 origin = new Vector3(pos.x, pos.y + 20f, pos.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 40f, _groundLayerMask))
            {
                return new Vector3(pos.x, hit.point.y + yOffset, pos.z);
            }
            return pos;
        }
    }
}

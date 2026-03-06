using TMPro;
using UnityEngine;

namespace ProjectGuild.View.Combat
{
    /// <summary>
    /// Visual representation of a single enemy instance in the 3D world.
    /// Managed by EnemyVisualManager. Parallel to RunnerVisual but simpler:
    /// enemies don't travel, they stay at spawn points and attack in place.
    /// </summary>
    public class EnemyVisual : MonoBehaviour
    {
        public string EnemyInstanceId { get; private set; }
        public string EnemyConfigId { get; private set; }

        private Animator _animator;
        private TextMeshPro _nameLabel;

        // HP bar
        private GameObject _barGroup;
        private SpriteRenderer _hpFillRenderer;
        private Transform _hpFillTransform;
        private const float HpBarWidth = 1.0f;
        private const float HpBarHeight = 0.06f;
        private const float NameLabelHeight = 2.2f;

        // Animation hashes
        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int AttackTrigger = Animator.StringToHash("Attack");
        private static readonly int HitTrigger = Animator.StringToHash("Hit");
        private static readonly int DieTrigger = Animator.StringToHash("Die");
        private static readonly int InCombatParam = Animator.StringToHash("InCombat");

        // State
        private float _currentHpPercent = 1f;
        private bool _isDead;

        public void Initialize(string instanceId, string configId, string displayName, Vector3 position)
        {
            EnemyInstanceId = instanceId;
            EnemyConfigId = configId;
            transform.position = position;
            gameObject.name = $"Enemy_{displayName}_{instanceId[..Mathf.Min(6, instanceId.Length)]}";

            _animator = GetComponentInChildren<Animator>();
            if (_animator != null)
                _animator.applyRootMotion = false;

            // Name label
            var labelObj = new GameObject("NameLabel");
            labelObj.transform.SetParent(transform);
            labelObj.transform.localPosition = new Vector3(0f, NameLabelHeight, 0f);
            _nameLabel = labelObj.AddComponent<TextMeshPro>();
            _nameLabel.text = displayName;
            _nameLabel.fontSize = 3f;
            _nameLabel.alignment = TextAlignmentOptions.Center;
            _nameLabel.color = new Color(1f, 0.4f, 0.4f); // Red tint for enemies
            _nameLabel.rectTransform.sizeDelta = new Vector2(6f, 2f);

            CreateHpBar();
        }

        public void SetHpPercent(float percent)
        {
            _currentHpPercent = Mathf.Clamp01(percent);
        }

        public void PlayAttack()
        {
            if (_animator != null && !_isDead)
                _animator.SetTrigger(AttackTrigger);
        }

        public void PlayHitReact()
        {
            if (_animator != null && !_isDead)
                _animator.SetTrigger(HitTrigger);
        }

        public void PlayDeath()
        {
            _isDead = true;
            if (_animator != null)
                _animator.SetTrigger(DieTrigger);
        }

        public void Respawn()
        {
            _isDead = false;
            _currentHpPercent = 1f;
            gameObject.SetActive(true);
            // Reset animator to idle
            if (_animator != null)
            {
                _animator.Rebind();
                _animator.Update(0f);
            }
        }

        public void FaceTarget(Vector3 targetPosition)
        {
            Vector3 dir = targetPosition - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(dir);
        }

        private void Update()
        {
            // Billboard name + bars
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

            UpdateHpBar();
        }

        private void CreateHpBar()
        {
            float barY = NameLabelHeight - 0.2f;
            _barGroup = new GameObject("BarGroup");
            _barGroup.transform.SetParent(transform);
            _barGroup.transform.localPosition = new Vector3(0f, barY, 0f);

            // Background
            CreateBarSprite("HpBg", _barGroup.transform, Vector3.zero,
                HpBarWidth, HpBarHeight, new Color(0f, 0f, 0f, 0.5f), 0);

            // Fill
            var fillObj = CreateBarSprite("HpFill", _barGroup.transform, Vector3.zero,
                HpBarWidth, HpBarHeight, new Color(0.78f, 0.24f, 0.24f), 1);
            _hpFillRenderer = fillObj.GetComponent<SpriteRenderer>();
            _hpFillTransform = fillObj.transform;
        }

        private void UpdateHpBar()
        {
            if (_hpFillTransform == null) return;

            float w = _currentHpPercent * HpBarWidth;
            _hpFillTransform.localScale = new Vector3(w, HpBarHeight, 1f);
            _hpFillTransform.localPosition = new Vector3((w - HpBarWidth) * 0.5f, 0f, 0f);
        }

        private static Sprite _cachedWhiteSprite;

        private static GameObject CreateBarSprite(string name, Transform parent,
            Vector3 localPos, float width, float height, Color color, int sortOrder)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent);
            obj.transform.localPosition = localPos;
            obj.transform.localScale = new Vector3(width, height, 1f);
            var sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite = GetWhiteSprite();
            sr.color = color;
            sr.sortingOrder = sortOrder;
            return obj;
        }

        private static Sprite GetWhiteSprite()
        {
            if (_cachedWhiteSprite != null) return _cachedWhiteSprite;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _cachedWhiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return _cachedWhiteSprite;
        }
    }
}

using UnityEngine;

namespace ProjectGuild.View.Combat
{
    /// <summary>
    /// Moves a VFX GameObject from origin to target at a fixed speed,
    /// then optionally spawns an impact prefab and destroys itself.
    /// </summary>
    public class ProjectileVfx : MonoBehaviour
    {
        private Vector3 _target;
        private float _speed;
        private GameObject _impactPrefab;
        private bool _initialized;

        public void Launch(Vector3 target, float speed, GameObject impactPrefab = null)
        {
            _target = target;
            _speed = speed;
            _impactPrefab = impactPrefab;
            _initialized = true;

            // Face the target
            Vector3 dir = _target - transform.position;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(dir);
        }

        private void Update()
        {
            if (!_initialized) return;

            float step = _speed * Time.deltaTime;
            Vector3 toTarget = _target - transform.position;

            if (toTarget.magnitude <= step)
            {
                // Arrived
                transform.position = _target;
                OnArrived();
                return;
            }

            transform.position += toTarget.normalized * step;

            // Keep facing target
            if (toTarget.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(toTarget);
        }

        private void OnArrived()
        {
            if (_impactPrefab != null)
            {
                var impact = Instantiate(_impactPrefab, _target, Quaternion.identity);
                var ps = impact.GetComponentInChildren<ParticleSystem>();
                float dur = ps != null ? ps.main.duration + ps.main.startLifetime.constantMax + 0.5f : 3f;
                Destroy(impact, dur);
            }

            Destroy(gameObject);
        }
    }
}

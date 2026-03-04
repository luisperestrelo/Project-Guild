using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace ProjectGuild.View
{
    /// <summary>
    /// Controller for the dedicated LoadingScene (scene index 0).
    /// Shows a solid black screen on the very first rendered frame, loads SampleScene
    /// additively, waits for GameBootstrapper.OnWorldReady, fades out, then unloads itself.
    ///
    /// When playing directly from SampleScene in the Editor (no LoadingScene loaded),
    /// OnWorldReady fires with no listeners — the world appears immediately.
    /// </summary>
    public class LoadingSceneController : MonoBehaviour
    {
        [Header("Fade Timing")]
        [Tooltip("Duration of the fade-out when revealing the world.")]
        [SerializeField] private float _fadeOutDuration = 0.5f;

        [Header("UI")]
        [SerializeField] private PanelSettings _panelSettings;

        private UIDocument _uiDocument;
        private VisualElement _overlay;

        private void Awake()
        {
            _uiDocument = gameObject.GetComponent<UIDocument>();
            if (_uiDocument == null)
                _uiDocument = gameObject.AddComponent<UIDocument>();

            if (_panelSettings != null)
                _uiDocument.panelSettings = _panelSettings;

            _uiDocument.sortingOrder = 200;
        }

        private void OnEnable()
        {
            if (_overlay == null)
                BuildOverlay();
        }

        private void Start()
        {
            GameBootstrapper.OnWorldReady += HandleWorldReady;
            SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Additive);
        }

        private void OnDestroy()
        {
            GameBootstrapper.OnWorldReady -= HandleWorldReady;
        }

        private void BuildOverlay()
        {
            var root = _uiDocument.rootVisualElement;
            if (root == null) return;

            _overlay = new VisualElement();
            _overlay.name = "loading-scene-overlay";
            _overlay.pickingMode = PickingMode.Ignore;
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.top = 0;
            _overlay.style.right = 0;
            _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new StyleColor(Color.black);
            _overlay.style.opacity = 1f;
            _overlay.style.display = DisplayStyle.Flex;

            root.Add(_overlay);
        }

        private void HandleWorldReady()
        {
            GameBootstrapper.OnWorldReady -= HandleWorldReady;
            StartCoroutine(FadeOutAndUnload());
        }

        private IEnumerator FadeOutAndUnload()
        {
            if (_overlay != null)
            {
                float elapsed = 0f;
                while (elapsed < _fadeOutDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / _fadeOutDuration);
                    _overlay.style.opacity = 1f - t;
                    yield return null;
                }
                _overlay.style.opacity = 0f;
            }

            SceneManager.UnloadSceneAsync(gameObject.scene);
        }
    }
}

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectGuild.View
{
    /// <summary>
    /// Fade-to-black overlay for scene transitions. Uses a dedicated UIDocument
    /// with high sort order to cover both the 3D viewport and all UI panels.
    ///
    /// Timing is very snappy (0.08s out + 0.02s hold + 0.10s in = 0.20s total)
    /// for an "alt-tabbing between bots" feel — a quick blink, not cinematic.
    ///
    /// If called mid-fade, the current fade is interrupted: the new mid-fade action
    /// executes immediately and fade-in restarts. No stacking, no waiting.
    /// </summary>
    public class SceneTransitionOverlay : MonoBehaviour
    {
        [Header("Fade Timing")]
        [Tooltip("Duration of the fade-out (screen going black).")]
        [SerializeField] private float _fadeOutDuration = 0.08f;

        [Tooltip("Duration of the black hold before executing the mid-fade action.")]
        [SerializeField] private float _holdDuration = 0.02f;

        [Tooltip("Duration of the fade-in (screen clearing).")]
        [SerializeField] private float _fadeInDuration = 0.10f;

        [Header("UI")]
        [SerializeField] private PanelSettings _panelSettings;

        private UIDocument _uiDocument;
        private VisualElement _overlay;
        private Coroutine _activeFade;

        /// <summary>
        /// True while a fade transition is in progress.
        /// </summary>
        public bool IsTransitioning => _activeFade != null;

        private void Awake()
        {
            _uiDocument = gameObject.GetComponent<UIDocument>();
            if (_uiDocument == null)
                _uiDocument = gameObject.AddComponent<UIDocument>();

            if (_panelSettings != null)
                _uiDocument.panelSettings = _panelSettings;

            _uiDocument.sortingOrder = 100;

            BuildOverlay();
        }

        private void BuildOverlay()
        {
            // Create a minimal visual tree — just one full-screen black element
            var root = _uiDocument.rootVisualElement;

            _overlay = new VisualElement();
            _overlay.name = "scene-transition-overlay";
            _overlay.pickingMode = PickingMode.Ignore;
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.top = 0;
            _overlay.style.right = 0;
            _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new StyleColor(Color.black);
            _overlay.style.opacity = 0f;
            _overlay.style.display = DisplayStyle.None;

            root.Add(_overlay);
        }

        /// <summary>
        /// Immediately show the overlay at full black. Interrupts any active fade.
        /// Used by ReloadWorld() to cover the screen during teardown/rebuild.
        /// </summary>
        public void Show()
        {
            if (_activeFade != null)
            {
                StopCoroutine(_activeFade);
                _activeFade = null;
            }

            _overlay.style.display = DisplayStyle.Flex;
            _overlay.style.opacity = 1f;
        }

        /// <summary>
        /// Fade in from full black. Interrupts any active fade and starts a fresh fade-in.
        /// Used by ReloadWorld() after the world is rebuilt.
        /// </summary>
        public void FadeIn()
        {
            if (_activeFade != null)
            {
                StopCoroutine(_activeFade);
                _activeFade = null;
            }

            _overlay.style.display = DisplayStyle.Flex;
            _overlay.style.opacity = 1f;
            _activeFade = StartCoroutine(FadeInCoroutine());
        }

        /// <summary>
        /// Play a fade-to-black transition. The onMidFade callback fires at peak black
        /// (after fade-out + hold), then fades back in.
        ///
        /// If called during an active fade: interrupts immediately, runs the new
        /// onMidFade action, and restarts fade-in. No stacking.
        /// </summary>
        public void PlayFadeTransition(Action onMidFade)
        {
            if (_activeFade != null)
            {
                StopCoroutine(_activeFade);
                _activeFade = null;

                // Already at some level of blackness — execute immediately and fade in
                onMidFade?.Invoke();
                _activeFade = StartCoroutine(FadeInCoroutine());
                return;
            }

            _activeFade = StartCoroutine(FadeTransitionCoroutine(onMidFade));
        }

        private IEnumerator FadeTransitionCoroutine(Action onMidFade)
        {
            _overlay.style.display = DisplayStyle.Flex;

            // Fade out (transparent → black)
            float elapsed = 0f;
            while (elapsed < _fadeOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _fadeOutDuration);
                _overlay.style.opacity = t;
                yield return null;
            }
            _overlay.style.opacity = 1f;

            // Hold at black
            if (_holdDuration > 0f)
                yield return new WaitForSecondsRealtime(_holdDuration);

            // Execute the mid-fade action (camera snap, etc.)
            onMidFade?.Invoke();

            // Fade in (black → transparent)
            yield return FadeInInternal();
        }

        private IEnumerator FadeInCoroutine()
        {
            yield return FadeInInternal();
        }

        private IEnumerator FadeInInternal()
        {
            float elapsed = 0f;
            while (elapsed < _fadeInDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _fadeInDuration);
                _overlay.style.opacity = 1f - t;
                yield return null;
            }
            _overlay.style.opacity = 0f;
            _overlay.style.display = DisplayStyle.None;
            _activeFade = null;
        }
    }
}

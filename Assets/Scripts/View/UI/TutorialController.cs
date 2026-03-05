using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Tutorial;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// View-layer controller for the tutorial overlay. Subscribes to
    /// TutorialMilestoneCompleted events and shows contextual messages.
    /// Manages element highlighting (gold pulse) and the "Skip Tutorial" button.
    ///
    /// Not a MonoBehaviour, not ITickRefreshable. Purely event-driven.
    /// </summary>
    public class TutorialController
    {
        private readonly VisualElement _root;
        private readonly VisualElement _messagePanel;
        private readonly Label _messageText;
        private readonly Button _dismissButton;
        private readonly Button _skipButton;
        private readonly UIManager _uiManager;

        // Highlight state: inline styles so they work on any element regardless of USS tree.
        // Tracked separately so map close only removes map highlights, not the World button.
        private VisualElement _highlightedWorldButton;
        private readonly List<VisualElement> _highlightedMapNodes = new();
        private IVisualElementScheduledItem _pulseSchedule;
        private bool _pulseDim;

        private static readonly Color GoldBright = new(0.86f, 0.71f, 0.24f, 0.9f);
        private static readonly Color GoldDim = new(0.86f, 0.71f, 0.24f, 0.25f);

        public TutorialController(VisualElement root, UIManager uiManager)
        {
            _root = root;
            _uiManager = uiManager;

            // Instantiate() wraps in a TemplateContainer. Set flexGrow so it fills.
            if (root.childCount > 0)
                root[0].style.flexGrow = 1;

            _messagePanel = root.Q("tutorial-message-panel");
            _messageText = root.Q<Label>("tutorial-message-text");
            _dismissButton = root.Q<Button>("btn-tutorial-dismiss");
            _skipButton = root.Q<Button>("btn-tutorial-skip");

            if (_dismissButton == null || _skipButton == null)
            {
                Debug.LogWarning("[TutorialController] Failed to find UI elements. " +
                    "Is the correct UXML asset assigned to Tutorial Overlay Asset?");
                return;
            }

            _dismissButton.clicked += OnDismissClicked;
            _skipButton.clicked += OnSkipClicked;

            // Start hidden
            _messagePanel.style.display = DisplayStyle.None;

            // Subscribe to tutorial events
            var sim = _uiManager.Simulation;
            if (sim != null)
            {
                sim.Events.Subscribe<TutorialMilestoneCompleted>(OnMilestoneCompleted);
                sim.Events.Subscribe<TutorialPhaseCompleted>(OnPhaseCompleted);
            }

            // Start pulse animation (inline style toggle every 600ms)
            _pulseSchedule = _root.schedule.Execute(TogglePulse).Every(600);

            // Check if intro message should show (1.5s delay for new game)
            CheckForPendingIntro();
        }

        /// <summary>
        /// Called by UIManager when the strategic map is opened.
        /// Highlights Copper Mine if the player hasn't sent a runner yet.
        /// </summary>
        public void OnStrategicMapOpened()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var tutorial = sim.CurrentGameState.Tutorial;
            if (!tutorial.IsActive) return;

            if (!tutorial.IsMilestoneCompleted(TutorialMilestones.Gathering_SentRunnerToNode))
                HighlightMapNode("copper_mine");
        }

        /// <summary>
        /// Called by UIManager when the strategic map is closed.
        /// Only removes map node highlights. World button highlight persists.
        /// </summary>
        public void OnStrategicMapClosed()
        {
            ClearMapNodeHighlights();
        }

        /// <summary>
        /// Clean up event subscriptions.
        /// </summary>
        public void Dispose()
        {
            var sim = _uiManager.Simulation;
            if (sim != null)
            {
                sim.Events.Unsubscribe<TutorialMilestoneCompleted>(OnMilestoneCompleted);
                sim.Events.Unsubscribe<TutorialPhaseCompleted>(OnPhaseCompleted);
            }

            _pulseSchedule?.Pause();
            ClearAllHighlights();
        }

        // ─── Event Handlers ───────────────────────────────────────

        private void OnMilestoneCompleted(TutorialMilestoneCompleted e)
        {
            switch (e.MilestoneId)
            {
                case TutorialMilestones.Gathering_Intro:
                    // Message shown via CheckForPendingIntro with delay
                    break;

                case TutorialMilestones.Gathering_SentRunnerToNode:
                    ShowMessage("Good. They'll head out and start gathering once they arrive.");
                    ClearAllHighlights();
                    break;

                case TutorialMilestones.Gathering_IdleNudgeShown:
                    ShowMessage("Your other runners could be put to work too. There's more than just copper out there: try the Pine Forest or Herb Garden.");
                    break;

                case TutorialMilestones.Gathering_CopperDeposited:
                    ShowMessage("Good. You have enough copper to start crafting. Head to the Guild Hall to forge some equipment.");
                    break;
            }
        }

        private void OnPhaseCompleted(TutorialPhaseCompleted e)
        {
            // Future phases will show transition messages here
        }

        // ─── Intro Check ──────────────────────────────────────────

        private void CheckForPendingIntro()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var tutorial = sim.CurrentGameState.Tutorial;
            if (!tutorial.IsActive) return;
            if (tutorial.CurrentPhase != TutorialPhase.Gathering) return;

            // Intro already completed means we're in a new game. Show with delay.
            if (tutorial.IsMilestoneCompleted(TutorialMilestones.Gathering_Intro)
                && !tutorial.IsMilestoneCompleted(TutorialMilestones.Gathering_SentRunnerToNode))
            {
                _root.schedule.Execute(() =>
                {
                    ShowMessage("You'll need to prepare for the battles ahead. Gather some copper ore to craft equipment.\n\nOpen the World map (M) and send a runner to the Copper Mine.");
                    HighlightWorldButton();
                }).ExecuteLater(1500);
            }
        }

        // ─── Message Display ──────────────────────────────────────

        private void ShowMessage(string text)
        {
            _messageText.text = text;
            _messagePanel.style.display = DisplayStyle.Flex;
        }

        private void HideMessage()
        {
            _messagePanel.style.display = DisplayStyle.None;
        }

        private void OnDismissClicked()
        {
            HideMessage();
        }

        private void OnSkipClicked()
        {
            var sim = _uiManager.Simulation;
            if (sim != null)
                sim.Tutorial.SkipTutorial();

            HideMessage();
            ClearAllHighlights();
            _uiManager.InvalidateStrategicMapNodes();
        }

        // ─── Highlight System ──────────────────────────────────────
        // Tracking (what to highlight) is separate from presentation (how to highlight).
        // To change the visual effect (e.g. arrows, glow, particles), replace the
        // three effect methods: ApplyHighlightEffect, RemoveHighlightEffect, UpdateHighlightPulse.

        private void HighlightWorldButton()
        {
            var worldButton = _uiManager.GetWorldButtonElement();
            if (worldButton == null) return;
            _highlightedWorldButton = worldButton;
            ApplyHighlightEffect(worldButton);
        }

        private void HighlightMapNode(string nodeId)
        {
            var nodeElement = _uiManager.GetStrategicMapNodeElement(nodeId);
            if (nodeElement == null) return;
            if (_highlightedMapNodes.Contains(nodeElement)) return;
            _highlightedMapNodes.Add(nodeElement);
            ApplyHighlightEffect(nodeElement);
        }

        private void ClearMapNodeHighlights()
        {
            for (int i = 0; i < _highlightedMapNodes.Count; i++)
                RemoveHighlightEffect(_highlightedMapNodes[i]);
            _highlightedMapNodes.Clear();
        }

        private void ClearAllHighlights()
        {
            ClearMapNodeHighlights();
            if (_highlightedWorldButton != null)
            {
                RemoveHighlightEffect(_highlightedWorldButton);
                _highlightedWorldButton = null;
            }
        }

        private void TogglePulse()
        {
            bool hasAny = _highlightedWorldButton != null || _highlightedMapNodes.Count > 0;
            if (!hasAny) return;

            _pulseDim = !_pulseDim;

            if (_highlightedWorldButton != null)
                UpdateHighlightPulse(_highlightedWorldButton, _pulseDim);

            for (int i = 0; i < _highlightedMapNodes.Count; i++)
                UpdateHighlightPulse(_highlightedMapNodes[i], _pulseDim);
        }

        // ─── Highlight Effect (gold border pulse) ─────────────────
        // Swap these three methods to change the visual treatment.
        // Uses inline styles so it works on any element regardless of USS tree.

        private static void ApplyHighlightEffect(VisualElement element)
        {
            element.style.borderTopWidth = 2;
            element.style.borderBottomWidth = 2;
            element.style.borderLeftWidth = 2;
            element.style.borderRightWidth = 2;
            SetBorderColor(element, GoldBright);
        }

        private static void RemoveHighlightEffect(VisualElement element)
        {
            element.style.borderTopWidth = StyleKeyword.Null;
            element.style.borderBottomWidth = StyleKeyword.Null;
            element.style.borderLeftWidth = StyleKeyword.Null;
            element.style.borderRightWidth = StyleKeyword.Null;
            element.style.borderTopColor = StyleKeyword.Null;
            element.style.borderBottomColor = StyleKeyword.Null;
            element.style.borderLeftColor = StyleKeyword.Null;
            element.style.borderRightColor = StyleKeyword.Null;
        }

        private static void UpdateHighlightPulse(VisualElement element, bool dim)
        {
            SetBorderColor(element, dim ? GoldDim : GoldBright);
        }

        private static void SetBorderColor(VisualElement element, Color color)
        {
            var sc = new StyleColor(color);
            element.style.borderTopColor = sc;
            element.style.borderBottomColor = sc;
            element.style.borderLeftColor = sc;
            element.style.borderRightColor = sc;
        }
    }
}

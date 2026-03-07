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
        private readonly List<VisualElement> _highlightedGeneral = new();
        private IVisualElementScheduledItem _pulseSchedule;
        private bool _pulseDim;

        // Step tracker
        private readonly VisualElement _trackerPanel;
        private readonly List<Label> _stepLabels = new();
        private string _lastMessage;

        // Culling Frost hand-holding state
        private int _cfStep; // 0=not active, 1=click mage portrait, 2=click automation tab, 3=done
        private string _mageRunnerId;

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

            // Build step tracker (left side, always visible during tutorial)
            _trackerPanel = BuildStepTracker();
            root.Q("tutorial-overlay-root")?.Add(_trackerPanel);

            // Check if intro message should show (1.5s delay for new game)
            CheckForPendingIntro();

            // Initial tracker state
            RefreshTracker();
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
            RefreshTracker();

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

                // ─── Crafting Phase ─────────────────────────────────
                case TutorialMilestones.Crafting_Intro:
                    ShowMessage("Time to put those materials to use. Open the Crafting panel at the Guild Hall to forge equipment for your runners.\n\nCraft a weapon or piece of armor, then equip it on a runner.");
                    break;

                case TutorialMilestones.Crafting_FirstItemCrafted:
                    ShowMessage("Your first piece of equipment! Now equip it on one of your runners. Open a runner's details and look for the Equipment tab.");
                    break;

                case TutorialMilestones.Crafting_ItemEquipped:
                    ShowMessage("Well done. Your runner is now stronger. Time to put that equipment to the test.");
                    break;

                // ─── Combat Phase ───────────────────────────────────
                case TutorialMilestones.Combat_Intro:
                    ShowMessage("Goblins have set up camp nearby. Open the World map (M) and send your runners to the Goblin Camp.\n\nEquip everyone first. They'll need every advantage they can get.");
                    HighlightWorldButton();
                    break;

                case TutorialMilestones.Combat_SentToGoblins:
                    ShowMessage("Your runners are heading to the Goblin Camp. Watch them fight. They'll use their abilities automatically based on their combat style.");
                    ClearAllHighlights();
                    break;

                case TutorialMilestones.Combat_FirstKill:
                    ShowMessage("First blood! Your runners are fighting well. Keep watching. They'll continue farming experience and loot.");
                    break;

                // ─── Automation Phase (triggered by Culling Frost unlock) ─
                case TutorialMilestones.Automation_Intro:
                    StartCullingFrostGuide();
                    break;

                // ─── Tutorial Complete ───────────────────────────────
                case TutorialMilestones.NewPawnAwarded:
                    ShowMessage("A new runner has arrived at the Guild Hall! They seem to have a knack for gathering.\n\nPerhaps you could send them to gather resources while your fighters keep farming the goblins.");
                    break;
            }
        }

        private void OnPhaseCompleted(TutorialPhaseCompleted e)
        {
            // Phase transitions are handled by the intro milestones above
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
            _lastMessage = text;
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
            _trackerPanel.style.display = DisplayStyle.None;
            _uiManager.InvalidateStrategicMapNodes();
        }

        // ─── Step Tracker ─────────────────────────────────────────

        private static readonly (string label, string milestone)[] TrackerSteps =
        {
            ("Gather copper ore", TutorialMilestones.Gathering_CopperDeposited),
            ("Craft equipment", TutorialMilestones.Crafting_FirstItemCrafted),
            ("Equip a runner", TutorialMilestones.Crafting_ItemEquipped),
            ("Fight goblins", TutorialMilestones.Combat_FirstKill),
            ("Learn Culling Frost", TutorialMilestones.Automation_Intro),
            ("Set up combat rules", TutorialMilestones.Automation_Complete),
            ("Earn a new runner", TutorialMilestones.NewPawnAwarded),
        };

        private VisualElement BuildStepTracker()
        {
            var panel = new VisualElement();
            panel.name = "tutorial-step-tracker";
            panel.pickingMode = PickingMode.Ignore;
            panel.style.position = Position.Absolute;
            panel.style.left = 12;
            panel.style.top = 80;
            panel.style.backgroundColor = new Color(0.06f, 0.06f, 0.1f, 0.85f);
            panel.style.borderTopLeftRadius = 6;
            panel.style.borderTopRightRadius = 6;
            panel.style.borderBottomLeftRadius = 6;
            panel.style.borderBottomRightRadius = 6;
            panel.style.borderTopWidth = 1;
            panel.style.borderBottomWidth = 1;
            panel.style.borderLeftWidth = 1;
            panel.style.borderRightWidth = 1;
            panel.style.borderTopColor = new Color(0.3f, 0.3f, 0.4f, 0.5f);
            panel.style.borderBottomColor = new Color(0.3f, 0.3f, 0.4f, 0.5f);
            panel.style.borderLeftColor = new Color(0.3f, 0.3f, 0.4f, 0.5f);
            panel.style.borderRightColor = new Color(0.3f, 0.3f, 0.4f, 0.5f);
            panel.style.paddingTop = 10;
            panel.style.paddingBottom = 10;
            panel.style.paddingLeft = 14;
            panel.style.paddingRight = 14;
            panel.style.minWidth = 180;

            var header = new Label("Tutorial");
            header.style.fontSize = 14;
            header.style.color = new Color(0.86f, 0.71f, 0.24f);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 8;
            header.pickingMode = PickingMode.Ignore;
            panel.Add(header);

            _stepLabels.Clear();
            for (int i = 0; i < TrackerSteps.Length; i++)
            {
                var stepLabel = new Label();
                stepLabel.style.fontSize = 11;
                stepLabel.style.marginBottom = 3;
                stepLabel.style.paddingLeft = 4;
                stepLabel.pickingMode = PickingMode.Ignore;
                panel.Add(stepLabel);
                _stepLabels.Add(stepLabel);
            }

            // "Show last message" button
            var showMsgBtn = new Button(() =>
            {
                if (_lastMessage != null)
                    ShowMessage(_lastMessage);
            });
            showMsgBtn.text = "Show Last Hint";
            showMsgBtn.style.marginTop = 10;
            showMsgBtn.style.fontSize = 10;
            showMsgBtn.style.height = 22;
            showMsgBtn.style.backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.8f);
            showMsgBtn.style.color = new Color(0.7f, 0.7f, 0.8f);
            showMsgBtn.style.borderTopWidth = 1;
            showMsgBtn.style.borderBottomWidth = 1;
            showMsgBtn.style.borderLeftWidth = 1;
            showMsgBtn.style.borderRightWidth = 1;
            showMsgBtn.style.borderTopColor = new Color(0.4f, 0.4f, 0.5f, 0.4f);
            showMsgBtn.style.borderBottomColor = new Color(0.4f, 0.4f, 0.5f, 0.4f);
            showMsgBtn.style.borderLeftColor = new Color(0.4f, 0.4f, 0.5f, 0.4f);
            showMsgBtn.style.borderRightColor = new Color(0.4f, 0.4f, 0.5f, 0.4f);
            showMsgBtn.style.borderTopLeftRadius = 3;
            showMsgBtn.style.borderTopRightRadius = 3;
            showMsgBtn.style.borderBottomLeftRadius = 3;
            showMsgBtn.style.borderBottomRightRadius = 3;
            panel.Add(showMsgBtn);

            return panel;
        }

        private void RefreshTracker()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var tutorial = sim.CurrentGameState.Tutorial;
            if (!tutorial.IsActive)
            {
                _trackerPanel.style.display = DisplayStyle.None;
                return;
            }

            _trackerPanel.style.display = DisplayStyle.Flex;

            bool foundCurrent = false;
            for (int i = 0; i < TrackerSteps.Length; i++)
            {
                var (label, milestone) = TrackerSteps[i];
                bool completed = tutorial.IsMilestoneCompleted(milestone);

                if (completed)
                {
                    _stepLabels[i].text = $"<color=#66aa66>  {label}</color>";
                    _stepLabels[i].enableRichText = true;
                    _stepLabels[i].style.display = DisplayStyle.Flex;
                }
                else if (!foundCurrent)
                {
                    foundCurrent = true;
                    _stepLabels[i].text = $">> {label}";
                    _stepLabels[i].enableRichText = false;
                    _stepLabels[i].style.color = new Color(0.9f, 0.85f, 0.6f);
                    _stepLabels[i].style.unityFontStyleAndWeight = FontStyle.Bold;
                    _stepLabels[i].style.display = DisplayStyle.Flex;
                }
                else
                {
                    // Hidden: show next one dimmed, hide the rest
                    if (i == TrackerSteps.Length - 1 || foundCurrent)
                    {
                        _stepLabels[i].text = $"   ???";
                        _stepLabels[i].enableRichText = false;
                        _stepLabels[i].style.color = new Color(0.35f, 0.35f, 0.4f);
                        _stepLabels[i].style.unityFontStyleAndWeight = FontStyle.Normal;
                        _stepLabels[i].style.display = DisplayStyle.Flex;
                    }
                }
            }
        }

        // ─── Culling Frost Hand-Holding ─────────────────────────────

        private void StartCullingFrostGuide()
        {
            // Find the mage runner (first runner with Magic >= 8)
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            _mageRunnerId = null;
            foreach (var r in sim.CurrentGameState.Runners)
            {
                if (r.GetSkill(SkillType.Magic).Level >= 8)
                {
                    _mageRunnerId = r.Id;
                    break;
                }
            }

            if (_mageRunnerId == null)
            {
                // Fallback: just show the generic message
                ShowMessage("Your mage just learned Culling Frost! Open their Combat Style and add a rule: \"If enemy HP < 35%, use Culling Frost\" above the Fireball rule.");
                return;
            }

            var mage = sim.FindRunner(_mageRunnerId);
            string mageName = mage?.Name ?? "your mage";

            // Step 1: Highlight the mage portrait, tell player to click it
            _cfStep = 1;
            ShowMessage($"{mageName} just learned Culling Frost! It deals massive damage to enemies below 35% HP.\n\nClick {mageName}'s portrait to select them.");
            HighlightElement(_uiManager.GetPortraitElement(_mageRunnerId));

            // Subscribe to runner selection to advance steps
            _uiManager.OnRunnerSelected += OnRunnerSelectedForCF;
        }

        private void OnRunnerSelectedForCF(string runnerId)
        {
            if (_cfStep == 1 && runnerId == _mageRunnerId)
            {
                // Step 2: Runner selected, now highlight the Automation tab
                _cfStep = 2;
                ClearGeneralHighlights();

                var sim = _uiManager.Simulation;
                var mage = sim?.FindRunner(_mageRunnerId);
                string mageName = mage?.Name ?? "your mage";

                ShowMessage($"Good. Now click the Automation tab to see {mageName}'s combat rules.");
                var automationTab = _uiManager.GetAutomationTabButton();
                HighlightElement(automationTab);

                // Also listen for tab switches to auto-advance
                if (automationTab != null)
                {
                    automationTab.RegisterCallback<ClickEvent>(OnAutomationTabClickedForCF);
                }
            }
        }

        private void OnAutomationTabClickedForCF(ClickEvent evt)
        {
            if (_cfStep != 2) return;

            // Step 3: They're in the automation tab
            _cfStep = 3;
            ClearGeneralHighlights();

            // Unsubscribe
            var automationTab = _uiManager.GetAutomationTabButton();
            automationTab?.UnregisterCallback<ClickEvent>(OnAutomationTabClickedForCF);

            var sim = _uiManager.Simulation;
            _uiManager.OnRunnerSelected -= OnRunnerSelectedForCF;

            // Show final instruction with the Combat sub-tab visible
            _root.schedule.Execute(() =>
            {
                ShowMessage("Now open the Combat Style editor. Add a new ability rule:\n\n" +
                    "Condition: Target HP % <= 35\n" +
                    "Ability: Culling Frost\n\n" +
                    "Drag it above the Fireball rule so it fires first when enemies are low.");
            }).ExecuteLater(300);
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

        private void HighlightElement(VisualElement element)
        {
            if (element == null) return;
            if (_highlightedGeneral.Contains(element)) return;
            _highlightedGeneral.Add(element);
            ApplyHighlightEffect(element);
        }

        private void ClearGeneralHighlights()
        {
            for (int i = 0; i < _highlightedGeneral.Count; i++)
                RemoveHighlightEffect(_highlightedGeneral[i]);
            _highlightedGeneral.Clear();
        }

        private void ClearAllHighlights()
        {
            ClearMapNodeHighlights();
            ClearGeneralHighlights();
            if (_highlightedWorldButton != null)
            {
                RemoveHighlightEffect(_highlightedWorldButton);
                _highlightedWorldButton = null;
            }
        }

        private void TogglePulse()
        {
            bool hasAny = _highlightedWorldButton != null || _highlightedMapNodes.Count > 0
                || _highlightedGeneral.Count > 0;
            if (!hasAny) return;

            _pulseDim = !_pulseDim;

            if (_highlightedWorldButton != null)
                UpdateHighlightPulse(_highlightedWorldButton, _pulseDim);

            for (int i = 0; i < _highlightedMapNodes.Count; i++)
                UpdateHighlightPulse(_highlightedMapNodes[i], _pulseDim);

            for (int i = 0; i < _highlightedGeneral.Count; i++)
                UpdateHighlightPulse(_highlightedGeneral[i], _pulseDim);
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

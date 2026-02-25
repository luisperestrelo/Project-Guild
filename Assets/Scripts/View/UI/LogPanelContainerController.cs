using UnityEngine.UIElements;

namespace ProjectGuild.View.UI
{
    public enum LogTab
    {
        Chronicle,
        Decisions,
    }

    /// <summary>
    /// Orchestrates the tabbed log panel (bottom-left). Owns tab switching,
    /// collapse/expand, and the unread badge. Delegates content rendering
    /// to ChroniclePanelController and DecisionLogPanelController.
    /// Plain C# class (not MonoBehaviour).
    /// </summary>
    public class LogPanelContainerController : ITickRefreshable
    {
        private readonly VisualElement _root;
        private readonly UIManager _uiManager;

        // ─── Expanded state ─────────────────────────────
        private readonly VisualElement _expandedPanel;
        private readonly Button _tabChronicleBtn;
        private readonly Button _tabDecisionsBtn;
        private readonly Button _collapseBtn;
        private readonly VisualElement _chronicleContent;
        private readonly VisualElement _decisionContent;

        // ─── Collapsed state ────────────────────────────
        private readonly VisualElement _collapsedTab;
        private readonly Label _collapsedBadge;

        // ─── Sub-controllers ────────────────────────────
        private readonly ChroniclePanelController _chronicleController;
        private readonly DecisionLogPanelController _decisionController;

        // ─── State ──────────────────────────────────────
        private LogTab _activeTab = LogTab.Chronicle;
        private bool _isExpanded = true;

        // Tracking for unread counts when collapsed or on other tab
        private int _lastChronicleEntryCount;
        private int _lastDecisionEntryCount;

        public bool IsExpanded => _isExpanded;

        public LogPanelContainerController(
            VisualElement root,
            UIManager uiManager,
            VisualTreeAsset chroniclePanelAsset,
            VisualTreeAsset decisionLogPanelAsset)
        {
            _root = root;
            _uiManager = uiManager;

            _expandedPanel = root.Q("log-panel-expanded");
            _collapsedTab = root.Q("log-panel-collapsed");
            _collapsedBadge = root.Q<Label>("collapsed-badge");

            // Tab buttons
            _tabChronicleBtn = root.Q<Button>("tab-chronicle");
            _tabDecisionsBtn = root.Q<Button>("tab-decisions");
            _collapseBtn = root.Q<Button>("collapse-button");

            _tabChronicleBtn.clicked += () => SwitchTab(LogTab.Chronicle);
            _tabDecisionsBtn.clicked += () => SwitchTab(LogTab.Decisions);
            _collapseBtn.clicked += ToggleCollapse;

            // Collapsed tab — click to expand
            _collapsedTab.RegisterCallback<ClickEvent>(_ => Expand());

            // Content slots
            _chronicleContent = root.Q("chronicle-content");
            _decisionContent = root.Q("decision-content");

            // Instantiate sub-panels
            if (chroniclePanelAsset != null)
            {
                var chronicleInstance = chroniclePanelAsset.Instantiate();
                chronicleInstance.style.flexGrow = 1;
                _chronicleContent.Add(chronicleInstance);
                _chronicleController = new ChroniclePanelController(chronicleInstance, uiManager);
            }

            if (decisionLogPanelAsset != null)
            {
                var decisionInstance = decisionLogPanelAsset.Instantiate();
                decisionInstance.style.flexGrow = 1;
                _decisionContent.Add(decisionInstance);
                _decisionController = new DecisionLogPanelController(decisionInstance, uiManager);
            }

            // Initialize entry count tracking
            _lastChronicleEntryCount = _chronicleController?.GetTotalEntryCount() ?? 0;
            _lastDecisionEntryCount = _decisionController?.GetTotalEntryCount() ?? 0;

            // Show Chronicle tab by default
            SwitchTab(LogTab.Chronicle);

            uiManager.RegisterTickRefreshable(this);
        }

        // ─── Tab Switching ───────────────────────────────

        private void SwitchTab(LogTab tab)
        {
            _activeTab = tab;

            // Update tab button styles
            SetTabActive(_tabChronicleBtn, tab == LogTab.Chronicle);
            SetTabActive(_tabDecisionsBtn, tab == LogTab.Decisions);

            // Show/hide content
            _chronicleContent.style.display = tab == LogTab.Chronicle ? DisplayStyle.Flex : DisplayStyle.None;
            _decisionContent.style.display = tab == LogTab.Decisions ? DisplayStyle.Flex : DisplayStyle.None;

            // Reset unread on the newly active tab
            if (tab == LogTab.Chronicle)
                _chronicleController?.ResetNewEntries();
            else
                _decisionController?.ResetNewEntries();
        }

        private static void SetTabActive(Button btn, bool active)
        {
            if (active)
                btn.AddToClassList("log-tab-active");
            else
                btn.RemoveFromClassList("log-tab-active");
        }

        // ─── Collapse / Expand ───────────────────────────

        public void ToggleCollapse()
        {
            if (_isExpanded) Collapse();
            else Expand();
        }

        public void Collapse()
        {
            _isExpanded = false;
            _expandedPanel.style.display = DisplayStyle.None;
            _collapsedTab.style.display = DisplayStyle.Flex;
            UpdateBadge();
        }

        public void Expand()
        {
            _isExpanded = true;
            _expandedPanel.style.display = DisplayStyle.Flex;
            _collapsedTab.style.display = DisplayStyle.None;

            // Reset unread on the active tab since we're showing it
            if (_activeTab == LogTab.Chronicle)
                _chronicleController?.ResetNewEntries();
            else
                _decisionController?.ResetNewEntries();

            UpdateBadge();
        }

        private void UpdateBadge()
        {
            int totalUnread = (_chronicleController?.NewEntriesSinceLastView ?? 0)
                            + (_decisionController?.NewEntriesSinceLastView ?? 0);

            if (totalUnread > 0)
            {
                _collapsedBadge.text = totalUnread > 99 ? "99+" : totalUnread.ToString();
                _collapsedBadge.style.display = DisplayStyle.Flex;
            }
            else
            {
                _collapsedBadge.style.display = DisplayStyle.None;
            }
        }

        // ─── Refresh (called every tick by UIManager) ────

        public void Refresh()
        {
            // Track new entries for unread badges on both sub-controllers
            TrackNewEntries();

            if (!_isExpanded)
            {
                UpdateBadge();
                return;
            }

            // Only refresh the active tab's controller
            if (_activeTab == LogTab.Chronicle)
                _chronicleController?.Refresh();
            else
                _decisionController?.Refresh();
        }

        private void TrackNewEntries()
        {
            // Chronicle
            int chronicleCount = _chronicleController?.GetTotalEntryCount() ?? 0;
            if (chronicleCount > _lastChronicleEntryCount && _lastChronicleEntryCount >= 0)
            {
                int newCount = chronicleCount - _lastChronicleEntryCount;
                // Notify if collapsed or on other tab
                if (!_isExpanded || _activeTab != LogTab.Chronicle)
                    _chronicleController?.NotifyNewEntries(newCount);
            }
            _lastChronicleEntryCount = chronicleCount;

            // Decision log
            int decisionCount = _decisionController?.GetTotalEntryCount() ?? 0;
            if (decisionCount > _lastDecisionEntryCount && _lastDecisionEntryCount >= 0)
            {
                int newCount = decisionCount - _lastDecisionEntryCount;
                if (!_isExpanded || _activeTab != LogTab.Decisions)
                    _decisionController?.NotifyNewEntries(newCount);
            }
            _lastDecisionEntryCount = decisionCount;
        }
    }
}

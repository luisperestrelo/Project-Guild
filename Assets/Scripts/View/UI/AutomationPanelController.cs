using UnityEngine.UIElements;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Controller for the Automation overlay panel. Manages open/close state
    /// and tab switching between Task Sequences, Macro Rulesets, and Micro Rulesets.
    ///
    /// Each library tab has its own editor controller. This class coordinates them.
    /// </summary>
    public class AutomationPanelController
    {
        private readonly UIManager _uiManager;
        private readonly VisualElement _root;
        private readonly VisualElement _panelRoot;

        // Tab buttons
        private readonly Button _tabTaskSeq;
        private readonly Button _tabMacro;
        private readonly Button _tabMicro;

        // Tab content containers
        private readonly VisualElement _contentTaskSeq;
        private readonly VisualElement _contentMacro;
        private readonly VisualElement _contentMicro;

        private string _activeTab = "taskseq";

        // Sub-controllers for each library type
        private TaskSequenceEditorController _taskSeqEditor;
        private MacroRulesetEditorController _macroEditor;
        private MicroRulesetEditorController _microEditor;

        public bool IsOpen { get; private set; }

        // Pending assignment context — set when opened from runner's "+ New" button
        private string _pendingAssignRunnerId;
        private string _pendingAssignTabType;
        private string _pendingAssignItemId;

        public AutomationPanelController(VisualElement root, UIManager uiManager)
        {
            _root = root;
            _panelRoot = root.Q("automation-panel-root");
            _uiManager = uiManager;

            // Make panel root focusable so it can receive KeyDownEvents
            _panelRoot.focusable = true;

            // Close button
            var btnClose = root.Q<Button>("btn-close-panel");
            btnClose.clicked += Close;

            // Tab buttons
            _tabTaskSeq = root.Q<Button>("panel-tab-taskseq");
            _tabMacro = root.Q<Button>("panel-tab-macro");
            _tabMicro = root.Q<Button>("panel-tab-micro");

            _tabTaskSeq.clicked += () => SwitchTab("taskseq");
            _tabMacro.clicked += () => SwitchTab("macro");
            _tabMicro.clicked += () => SwitchTab("micro");

            // Content containers
            _contentTaskSeq = root.Q("panel-content-taskseq");
            _contentMacro = root.Q("panel-content-macro");
            _contentMicro = root.Q("panel-content-micro");

            // Initialize sub-controllers
            _taskSeqEditor = new TaskSequenceEditorController(_contentTaskSeq, uiManager);
            _macroEditor = new MacroRulesetEditorController(_contentMacro, uiManager);
            _microEditor = new MicroRulesetEditorController(_contentMicro, uiManager);

            // Start hidden
            _root.style.display = DisplayStyle.None;

            // Escape to close (registered on inner panel root, not TemplateContainer)
            _panelRoot.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == UnityEngine.KeyCode.Escape)
                {
                    Close();
                    evt.StopPropagation();
                }
            });
        }

        public void Open()
        {
            IsOpen = true;
            _root.style.display = DisplayStyle.Flex;
            _panelRoot.Focus();
            RefreshActiveTab();
        }

        public void Close()
        {
            IsOpen = false;
            _root.style.display = DisplayStyle.None;
            ClearPendingAssignment();
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>
        /// Open the panel and navigate to a specific item.
        /// Used by "Edit in Library" buttons in the runner Automation tab.
        /// </summary>
        public void OpenToItem(string tabType, string itemId)
        {
            Open();
            SwitchTab(tabType);

            switch (tabType)
            {
                case "taskseq":
                    _taskSeqEditor.SelectItem(itemId);
                    break;
                case "macro":
                    _macroEditor.SelectItem(itemId);
                    break;
                case "micro":
                    _microEditor.SelectItem(itemId);
                    break;
            }
        }

        /// <summary>
        /// Open the panel to a specific item from a runner context.
        /// The editor's shared banner handles the "affects N runners" warning.
        /// </summary>
        public void OpenToItemFromRunner(string tabType, string itemId, string runnerId)
        {
            ClearPendingAssignment();
            OpenToItem(tabType, itemId);
        }

        /// <summary>
        /// Open the panel for a newly created item that needs to be assigned to a runner.
        /// Shows a prominent "Assign to [Runner Name]" bar at the top.
        /// </summary>
        public void OpenToItemForNewAssignment(string tabType, string itemId, string runnerId)
        {
            _pendingAssignRunnerId = runnerId;
            _pendingAssignTabType = tabType;
            _pendingAssignItemId = itemId;
            OpenToItem(tabType, itemId);
            ShowPendingAssignBar();
        }

        private void ShowPendingAssignBar()
        {
            var sim = _uiManager.Simulation;
            var runner = sim?.FindRunner(_pendingAssignRunnerId);
            if (runner == null) return;

            // Add assign button to the editor's footer buttons area
            VisualElement footerButtons = null;
            if (_pendingAssignTabType == "taskseq")
                footerButtons = _contentTaskSeq.Q(className: "editor-footer-buttons");
            else if (_pendingAssignTabType == "macro")
                footerButtons = _contentMacro.Q(className: "editor-footer-buttons");

            if (footerButtons != null)
            {
                var assignBtn = new Button(() => CompletePendingAssignment());
                assignBtn.name = "btn-pending-assign";
                assignBtn.text = $"Assign to {runner.Name}";
                assignBtn.AddToClassList("editor-footer-button");
                assignBtn.style.backgroundColor = new UnityEngine.Color(0.2f, 0.4f, 0.2f, 0.9f);
                assignBtn.style.color = new UnityEngine.Color(0.85f, 1f, 0.85f);
                assignBtn.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
                footerButtons.Insert(0, assignBtn);
            }
        }

        private void CompletePendingAssignment()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            if (_pendingAssignTabType == "taskseq")
                sim.CommandAssignTaskSequenceToRunner(_pendingAssignRunnerId, _pendingAssignItemId);
            else if (_pendingAssignTabType == "macro")
                sim.CommandAssignMacroRulesetToRunner(_pendingAssignRunnerId, _pendingAssignItemId);

            ClearPendingAssignment();
            Close();
        }

        private void ClearPendingAssignment()
        {
            _pendingAssignRunnerId = null;
            _pendingAssignTabType = null;
            _pendingAssignItemId = null;
            // Remove the footer button if it exists
            var footerBtn = _panelRoot.Q("btn-pending-assign");
            footerBtn?.RemoveFromHierarchy();
        }

        private void SwitchTab(string tabName)
        {
            _activeTab = tabName;

            SetTabActive(_tabTaskSeq, tabName == "taskseq");
            SetTabActive(_tabMacro, tabName == "macro");
            SetTabActive(_tabMicro, tabName == "micro");

            _contentTaskSeq.style.display = tabName == "taskseq" ? DisplayStyle.Flex : DisplayStyle.None;
            _contentMacro.style.display = tabName == "macro" ? DisplayStyle.Flex : DisplayStyle.None;
            _contentMicro.style.display = tabName == "micro" ? DisplayStyle.Flex : DisplayStyle.None;

            RefreshActiveTab();
        }

        private void RefreshActiveTab()
        {
            switch (_activeTab)
            {
                case "taskseq":
                    _taskSeqEditor.RefreshList();
                    _taskSeqEditor.RefreshEditor();
                    break;
                case "macro":
                    _macroEditor.RefreshList();
                    _macroEditor.RefreshEditor();
                    break;
                case "micro":
                    _microEditor.RefreshList();
                    _microEditor.RefreshEditor();
                    break;
            }
        }

        private static void SetTabActive(Button tab, bool active)
        {
            if (active)
                tab.AddToClassList("panel-tab-active");
            else
                tab.RemoveFromClassList("panel-tab-active");
        }

        // No tick-driven Refresh(). The panel is purely event-driven:
        // refreshes on open, tab switch, and user interaction only.
    }
}

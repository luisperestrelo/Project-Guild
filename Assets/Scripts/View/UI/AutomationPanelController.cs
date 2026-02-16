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

        public AutomationPanelController(VisualElement root, UIManager uiManager)
        {
            _root = root;
            _uiManager = uiManager;

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

            // Register Escape key to close
            _root.RegisterCallback<KeyDownEvent>(evt =>
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
            _root.Focus();
            RefreshActiveTab();
        }

        public void Close()
        {
            IsOpen = false;
            _root.style.display = DisplayStyle.None;
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
        /// Open the panel to a specific item, but prompt if shared.
        /// If the item is used by multiple runners, shows a prompt:
        /// "Edit for all N runners, or create a copy for [runner name]?"
        /// </summary>
        public void OpenToItemFromRunner(string tabType, string itemId, string runnerId)
        {
            var sim = _uiManager.Simulation;
            if (sim == null)
            {
                OpenToItem(tabType, itemId);
                return;
            }

            int usageCount = 0;
            switch (tabType)
            {
                case "taskseq":
                    usageCount = sim.CountRunnersUsingTaskSequence(itemId);
                    break;
                case "macro":
                    usageCount = sim.CountRunnersUsingMacroRuleset(itemId);
                    break;
                case "micro":
                    usageCount = sim.CountSequencesUsingMicroRuleset(itemId);
                    break;
            }

            // If shared (multiple users), the shared banner in the editor handles the warning.
            // Just open directly — the editor will show the "Changes affect N runners" banner.
            OpenToItem(tabType, itemId);
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
                    break;
                case "macro":
                    _macroEditor.RefreshList();
                    break;
                case "micro":
                    _microEditor.RefreshList();
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

        /// <summary>
        /// Refresh is intentionally a no-op on tick. The panel editors rebuild themselves
        /// in response to user actions (button clicks, dropdown changes), not on a timer.
        /// Tick-driven rebuilds would destroy interactive elements (dropdowns, text fields)
        /// 10 times per second, making the UI unusable.
        /// </summary>
        public void Refresh()
        {
            // No-op. Editors self-refresh on user interaction.
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Automation;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Controller for the Automation overlay panel. Manages open/close state
    /// and tab switching between Task Sequences, Macro Rulesets, and Micro Rulesets.
    ///
    /// Each library tab has its own editor controller. This class coordinates them.
    ///
    /// Navigation stack: when the player clicks "+ New Sequence..." in a macro rule
    /// dropdown (or "+ New Micro Ruleset..." in a Work step), we push a navigation
    /// entry, switch to the target tab, and show a breadcrumb header with Done/Cancel.
    /// Done wires the new item; Cancel deletes it and restores the previous state.
    /// </summary>
    public class AutomationPanelController
    {
        private readonly UIManager _uiManager;
        private readonly VisualElement _root;
        private readonly VisualElement _panelRoot;

        // Tab bar + buttons
        private readonly VisualElement _tabBar;
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

        // Pending assignment context — set when opened from a runner's automation tab.
        // Only the runner ID matters; the active tab determines what type to assign.
        private string _pendingAssignRunnerId;

        // ─── Navigation Stack ─────────────────────────────────
        private struct NavigationEntry
        {
            public string TabName;
            public string SelectedItemId;
            public string PendingItemId;
            public string PendingItemTab; // "taskseq" or "micro"
            public string PendingItemOriginalName; // name at creation, for unmodified check
            public Action<string> WireAction; // takes the ID to wire (may differ from PendingItemId)
        }

        private readonly Stack<NavigationEntry> _navigationStack = new();

        // Nav header elements (built lazily)
        private VisualElement _navHeader;
        private Label _navBreadcrumbLabel;
        private Button _navCancelButton;

        // Nav footer Done button (replaces normal footer buttons during nav stack)
        private Button _navDoneButton;

        public AutomationPanelController(VisualElement root, UIManager uiManager)
        {
            _root = root;
            _panelRoot = root.Q("automation-panel-root");
            _uiManager = uiManager;

            // Make panel root focusable so it can receive KeyDownEvents
            _panelRoot.focusable = true;

            // Close button
            var btnClose = root.Q<Button>("btn-close-panel");
            btnClose.clicked += OnCloseButtonClicked;

            // Tab bar + buttons
            _tabBar = root.Q("panel-tab-bar");
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

            // Wire navigation callbacks from editors
            _macroEditor.OnRequestNavigateToNewSequence = (newId, wireAction) =>
                PushNavigationAndSwitchTo("taskseq", newId, wireAction);
            _taskSeqEditor.OnRequestNavigateToNewMicroRuleset = (newId, wireAction) =>
                PushNavigationAndSwitchTo("micro", newId, wireAction);

            // Start hidden
            _root.style.display = DisplayStyle.None;

            // Escape handling
            _panelRoot.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    if (_navigationStack.Count > 0)
                        CancelNavigation();
                    else
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
            // Clear nav stack without wiring or deleting — panel is just closing
            _navigationStack.Clear();
            UpdateNavHeaderVisibility();
        }

        private void OnCloseButtonClicked()
        {
            if (_navigationStack.Count > 0)
                CancelNavigation();
            else
                Close();
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
            SelectItemOnTab(tabType, itemId);
        }

        /// <summary>
        /// Open the panel to a specific item from a runner context.
        /// Shows "Assign to [Runner]" so the player can reassign from any entry point.
        /// </summary>
        public void OpenToItemFromRunner(string tabType, string itemId, string runnerId)
        {
            _pendingAssignRunnerId = runnerId;
            OpenToItem(tabType, itemId);
            ShowAssignButtonOnActiveTab();
        }

        /// <summary>
        /// Open the panel for a newly created item that needs to be assigned to a runner.
        /// Shows a prominent "Assign to [Runner Name]" button in the footer.
        /// Auto-focuses the name field for immediate renaming.
        /// </summary>
        public void OpenToItemForNewAssignment(string tabType, string itemId, string runnerId)
        {
            _pendingAssignRunnerId = runnerId;
            Open();
            SwitchTab(tabType);
            SelectNewItemOnTab(tabType, itemId);
            ShowAssignButtonOnActiveTab();
        }

        /// <summary>
        /// Open the panel to the list view for browsing/picking, with "Assign to [Runner]" context.
        /// Used by CHANGE button — player wants to pick a different existing item.
        /// </summary>
        public void OpenForChangeAssignment(string tabType, string runnerId)
        {
            _pendingAssignRunnerId = runnerId;
            Open();
            SwitchTab(tabType);
            ShowAssignButtonOnActiveTab();
        }

        // ─── Item Selection Helpers ──────────────────────────────

        private void SelectItemOnTab(string tabType, string itemId)
        {
            switch (tabType)
            {
                case "taskseq": _taskSeqEditor.SelectItem(itemId); break;
                case "macro": _macroEditor.SelectItem(itemId); break;
                case "micro": _microEditor.SelectItem(itemId); break;
            }
        }

        private void SelectNewItemOnTab(string tabType, string itemId)
        {
            switch (tabType)
            {
                case "taskseq": _taskSeqEditor.SelectNewItem(itemId); break;
                case "macro": _macroEditor.SelectNewItem(itemId); break;
                case "micro": _microEditor.SelectNewItem(itemId); break;
            }
        }

        // ─── Navigation Stack ─────────────────────────────────

        /// <summary>
        /// Push the current state onto the nav stack and switch to a target tab/item.
        /// Called when "+ New Sequence..." or "+ New Micro Ruleset..." is selected.
        /// </summary>
        public void PushNavigationAndSwitchTo(string targetTab, string targetItemId, Action<string> wireAction)
        {
            // Save current state
            string currentSelectedId = null;
            if (_activeTab == "taskseq") currentSelectedId = _taskSeqEditor.SelectedId;
            else if (_activeTab == "macro") currentSelectedId = _macroEditor.SelectedId;
            else if (_activeTab == "micro") currentSelectedId = _microEditor.SelectedId;

            // Look up the name of the newly created item for the unmodified check later
            string originalName = null;
            var sim = _uiManager.Simulation;
            if (sim != null)
            {
                if (targetTab == "taskseq")
                    originalName = sim.FindTaskSequenceInLibrary(targetItemId)?.Name;
                else if (targetTab == "micro")
                    originalName = sim.FindMicroRulesetInLibrary(targetItemId)?.Name;
            }

            var entry = new NavigationEntry
            {
                TabName = _activeTab,
                SelectedItemId = currentSelectedId,
                PendingItemId = targetItemId,
                PendingItemTab = targetTab,
                PendingItemOriginalName = originalName,
                WireAction = wireAction,
            };
            _navigationStack.Push(entry);

            // Switch to target tab and select the new item (with auto-focus)
            SwitchTab(targetTab);
            SelectNewItemOnTab(targetTab, targetItemId);

            UpdateNavHeaderVisibility();
        }

        /// <summary>
        /// Done: wire the new item and pop back to the previous state.
        /// </summary>
        private void PopNavigation()
        {
            if (_navigationStack.Count == 0) return;

            var entry = _navigationStack.Pop();

            // Wire whatever is currently selected (not necessarily the pending item)
            string selectedId = GetSelectedIdForTab(entry.PendingItemTab);
            entry.WireAction?.Invoke(selectedId);

            // If the user selected an existing item instead of the new one,
            // clean up the unused pending item — but only if it's still in its
            // blank default state. If they edited it (renamed, added steps/rules),
            // keep it in the library.
            var sim = _uiManager.Simulation;
            if (sim != null && !string.IsNullOrEmpty(entry.PendingItemId) && selectedId != entry.PendingItemId)
            {
                if (IsPendingItemUnmodified(sim, entry))
                {
                    switch (entry.PendingItemTab)
                    {
                        case "taskseq":
                            sim.CommandDeleteTaskSequence(entry.PendingItemId);
                            break;
                        case "micro":
                            sim.CommandDeleteMicroRuleset(entry.PendingItemId);
                            break;
                    }
                }
            }

            // Restore previous tab and selection
            SwitchTab(entry.TabName);
            switch (entry.TabName)
            {
                case "taskseq":
                    _taskSeqEditor.RefreshList();
                    _taskSeqEditor.SelectItem(entry.SelectedItemId);
                    break;
                case "macro":
                    _macroEditor.RefreshList();
                    _macroEditor.SelectItem(entry.SelectedItemId);
                    break;
                case "micro":
                    _microEditor.RefreshList();
                    _microEditor.SelectItem(entry.SelectedItemId);
                    break;
            }

            UpdateNavHeaderVisibility();
        }

        private string GetSelectedIdForTab(string tabName)
        {
            return tabName switch
            {
                "taskseq" => _taskSeqEditor.SelectedId,
                "macro" => _macroEditor.SelectedId,
                "micro" => _microEditor.SelectedId,
                _ => null,
            };
        }

        /// <summary>
        /// Check if the pending item is still in its blank default state (unedited).
        /// If the user changed the name, added steps/rules, etc., returns false.
        /// </summary>
        private static bool IsPendingItemUnmodified(Simulation.Core.GameSimulation sim, NavigationEntry entry)
        {
            switch (entry.PendingItemTab)
            {
                case "taskseq":
                    var seq = sim.FindTaskSequenceInLibrary(entry.PendingItemId);
                    if (seq == null) return true; // already gone
                    return seq.Name == entry.PendingItemOriginalName
                        && seq.Loop == true
                        && (seq.Steps == null || seq.Steps.Count == 0);

                case "micro":
                    var micro = sim.FindMicroRulesetInLibrary(entry.PendingItemId);
                    if (micro == null) return true;
                    // Default micro starts with 2 rules from CreateDefaultMicro().
                    // Consider it unmodified if the name hasn't changed and rule count
                    // matches the default (player hasn't added/removed rules).
                    var defaultRuleCount = DefaultRulesets.CreateDefaultMicro().Rules.Count;
                    return micro.Name == entry.PendingItemOriginalName
                        && (micro.Rules == null || micro.Rules.Count == defaultRuleCount);

                default:
                    return true;
            }
        }

        /// <summary>
        /// Cancel: show confirmation, then delete the temporary item and pop back.
        /// </summary>
        private void CancelNavigation()
        {
            if (_navigationStack.Count == 0) return;

            // Don't stack multiple confirmation dialogs (e.g. repeated Escape presses)
            if (_panelRoot.Q(className: "delete-confirm-overlay") != null) return;

            var prefs = _uiManager.Preferences;
            if (prefs != null && prefs.SkipCancelCreationConfirmation)
            {
                ExecuteCancelNavigation();
                return;
            }

            UIDialogs.ShowCancelCreationConfirmation(_panelRoot, prefs, () => ExecuteCancelNavigation());
        }

        private void ExecuteCancelNavigation()
        {
            if (_navigationStack.Count == 0) return;

            var entry = _navigationStack.Pop();

            // Delete the temporary item
            var sim = _uiManager.Simulation;
            if (sim != null && !string.IsNullOrEmpty(entry.PendingItemId))
            {
                switch (entry.PendingItemTab)
                {
                    case "taskseq":
                        sim.CommandDeleteTaskSequence(entry.PendingItemId);
                        break;
                    case "micro":
                        sim.CommandDeleteMicroRuleset(entry.PendingItemId);
                        break;
                }
            }

            // Restore previous tab and selection
            SwitchTab(entry.TabName);
            switch (entry.TabName)
            {
                case "taskseq":
                    _taskSeqEditor.RefreshList();
                    _taskSeqEditor.SelectItem(entry.SelectedItemId);
                    break;
                case "macro":
                    _macroEditor.RefreshList();
                    _macroEditor.SelectItem(entry.SelectedItemId);
                    break;
                case "micro":
                    _microEditor.RefreshList();
                    _microEditor.SelectItem(entry.SelectedItemId);
                    break;
            }

            UpdateNavHeaderVisibility();
        }

        private void EnsureNavHeaderBuilt()
        {
            if (_navHeader != null) return;

            _navHeader = new VisualElement();
            _navHeader.AddToClassList("nav-header");

            _navBreadcrumbLabel = new Label();
            _navBreadcrumbLabel.AddToClassList("nav-breadcrumb");
            _navHeader.Add(_navBreadcrumbLabel);

            _navCancelButton = new Button(() => CancelNavigation());
            _navCancelButton.text = "Cancel";
            _navCancelButton.AddToClassList("nav-cancel-button");
            _navHeader.Add(_navCancelButton);

            // Insert after tab bar in the panel root
            int tabBarIndex = _panelRoot.IndexOf(_tabBar);
            _panelRoot.Insert(tabBarIndex + 1, _navHeader);

            _navHeader.style.display = DisplayStyle.None;
        }

        private void UpdateNavHeaderVisibility()
        {
            bool inNavStack = _navigationStack.Count > 0;

            if (inNavStack)
            {
                EnsureNavHeaderBuilt();
                _navHeader.style.display = DisplayStyle.Flex;
                _tabBar.style.display = DisplayStyle.None;
                _navBreadcrumbLabel.text = BuildBreadcrumb();
            }
            else
            {
                if (_navHeader != null)
                    _navHeader.style.display = DisplayStyle.None;
                _tabBar.style.display = DisplayStyle.Flex;
            }

            // Toggle footer: hide normal buttons and show Done during nav stack,
            // or restore normal buttons when stack is empty.
            UpdateFooterForNavStack(inNavStack);
        }

        /// <summary>
        /// When in nav stack: hide the active tab's footer buttons (Assign To, Duplicate, Delete, etc.)
        /// and show a single Done button in the footer. When not in nav stack: restore normal buttons.
        /// </summary>
        private void UpdateFooterForNavStack(bool inNavStack)
        {
            // Remove any existing nav Done button first
            _navDoneButton?.RemoveFromHierarchy();

            // Get the active tab's content root
            var activeContent = GetActiveTabContent();
            if (activeContent == null) return;

            var footerButtons = activeContent.Q(className: "editor-footer-buttons");
            var footer = activeContent.Q(className: "editor-footer");

            if (inNavStack)
            {
                // Hide normal footer buttons
                if (footerButtons != null)
                    footerButtons.style.display = DisplayStyle.None;

                // Also hide the Assign To button injected by AutomationPanelController
                RemoveAssignButton();

                // Add Done button to footer
                if (footer != null)
                {
                    if (_navDoneButton == null)
                    {
                        _navDoneButton = new Button(() => PopNavigation());
                        _navDoneButton.text = "Done";
                        _navDoneButton.AddToClassList("editor-footer-button");
                        _navDoneButton.AddToClassList("nav-done-button");
                    }
                    footer.Add(_navDoneButton);
                }
            }
            else
            {
                // Restore normal footer buttons on all tabs
                RestoreFooterButtons(_contentTaskSeq);
                RestoreFooterButtons(_contentMacro);
                RestoreFooterButtons(_contentMicro);
            }
        }

        private static void RestoreFooterButtons(VisualElement tabContent)
        {
            var footerButtons = tabContent?.Q(className: "editor-footer-buttons");
            if (footerButtons != null)
                footerButtons.style.display = DisplayStyle.Flex;
        }

        private VisualElement GetActiveTabContent()
        {
            return _activeTab switch
            {
                "taskseq" => _contentTaskSeq,
                "macro" => _contentMacro,
                "micro" => _contentMicro,
                _ => null,
            };
        }

        private string BuildBreadcrumb()
        {
            var parts = new List<string>();

            // Walk the stack bottom-up (oldest first)
            var entries = _navigationStack.ToArray();
            // Stack.ToArray gives top-first, so reverse
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                parts.Add(GetTabDisplayName(entries[i].TabName));
            }

            // Add current (active) tab
            parts.Add(GetTabDisplayName(_activeTab));

            return string.Join(" \u25B8 ", parts);
        }

        private static string GetTabDisplayName(string tabName)
        {
            return tabName switch
            {
                "taskseq" => "Task Sequences",
                "macro" => "Macro Rulesets",
                "micro" => "Micro Rulesets",
                _ => tabName,
            };
        }

        // ─── Assign To Button ─────────────────────────────────

        private void ShowAssignButtonOnActiveTab()
        {
            // Remove any existing button first
            RemoveAssignButton();

            if (string.IsNullOrEmpty(_pendingAssignRunnerId)) return;
            // No Assign To during nav stack (Done replaces it) or on Micro tab
            if (_navigationStack.Count > 0) return;
            if (_activeTab == "micro") return;

            var sim = _uiManager.Simulation;
            var runner = sim?.FindRunner(_pendingAssignRunnerId);
            if (runner == null) return;

            VisualElement footerButtons = null;
            if (_activeTab == "taskseq")
                footerButtons = _contentTaskSeq.Q(className: "editor-footer-buttons");
            else if (_activeTab == "macro")
                footerButtons = _contentMacro.Q(className: "editor-footer-buttons");

            if (footerButtons != null)
            {
                var assignBtn = new Button(() => CompletePendingAssignment());
                assignBtn.name = "btn-pending-assign";
                assignBtn.text = $"Assign to {runner.Name}";
                assignBtn.AddToClassList("editor-footer-button");
                assignBtn.style.backgroundColor = new Color(0.2f, 0.4f, 0.2f, 0.9f);
                assignBtn.style.color = new Color(0.85f, 1f, 0.85f);
                assignBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                footerButtons.Insert(0, assignBtn);
            }
        }

        private void RemoveAssignButton()
        {
            // Could be on any tab's footer — search all
            _contentTaskSeq.Q("btn-pending-assign")?.RemoveFromHierarchy();
            _contentMacro.Q("btn-pending-assign")?.RemoveFromHierarchy();
        }

        private void CompletePendingAssignment()
        {
            var sim = _uiManager.Simulation;
            if (sim == null || string.IsNullOrEmpty(_pendingAssignRunnerId)) return;

            // Use whatever is currently selected on the active tab
            string itemId = null;
            if (_activeTab == "taskseq")
                itemId = _taskSeqEditor.SelectedId;
            else if (_activeTab == "macro")
                itemId = _macroEditor.SelectedId;

            if (string.IsNullOrEmpty(itemId)) return;

            if (_activeTab == "taskseq")
                sim.CommandAssignTaskSequenceToRunner(_pendingAssignRunnerId, itemId);
            else if (_activeTab == "macro")
                sim.CommandAssignMacroRulesetToRunner(_pendingAssignRunnerId, itemId);

            ClearPendingAssignment();
            Close();
        }

        private void ClearPendingAssignment()
        {
            _pendingAssignRunnerId = null;
            RemoveAssignButton();
        }

        // ─── Tab Switching ────────────────────────────────────

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

            // Move Assign To button to the new tab if runner context exists
            if (!string.IsNullOrEmpty(_pendingAssignRunnerId))
                ShowAssignButtonOnActiveTab();
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

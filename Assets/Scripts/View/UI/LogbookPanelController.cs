using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Controls the Logbook panel — a player scratchpad for jotting down observations.
    /// Positioned bottom-center. Folders auto-created per visited node.
    /// Plain C# class (not MonoBehaviour).
    /// </summary>
    public class LogbookPanelController : ITickRefreshable
    {
        private readonly UIManager _uiManager;
        private readonly VisualElement _root;

        // ─── Expanded state ─────────────────────────────
        private readonly VisualElement _expandedPanel;
        private readonly DropdownField _folderDropdown;
        private readonly VisualElement _pageTabs;
        private readonly Button _addPageButton;
        private readonly Button _lockButton;
        private readonly Button _searchButton;
        private readonly Button _collapseButton;
        private readonly TextField _logbookText;

        // ─── Search ─────────────────────────────────────
        private readonly VisualElement _searchBar;
        private readonly TextField _searchField;
        private readonly VisualElement _searchResults;

        // ─── Collapsed state ────────────────────────────
        private readonly VisualElement _collapsedPanel;

        // ─── State ──────────────────────────────────────
        private bool _isExpanded = true;
        private bool _isLocked;
        private bool _isManualLock; // true = user clicked lock button; false = auto-locked from typing
        private bool _isSearchVisible;
        private string _currentFolderId;
        private string _currentPageId;

        // Folder dropdown choice→ID mapping
        private readonly List<string> _folderChoiceIds = new();

        // Page tab cache: pageId → button
        private readonly Dictionary<string, Button> _pageTabCache = new();

        // ─── Refresh staleness tracking ─────────────────
        private string _lastSelectedRunnerId;
        private string _lastRunnerNodeId;
        private int _lastFolderCount;

        public bool IsExpanded => _isExpanded;

        public LogbookPanelController(VisualElement root, UIManager uiManager)
        {
            _root = root;
            _uiManager = uiManager;

            // Expanded state
            _expandedPanel = root.Q("logbook-expanded");
            _folderDropdown = root.Q<DropdownField>("folder-dropdown");
            _pageTabs = root.Q("page-tabs");
            _addPageButton = root.Q<Button>("add-page-button");
            _lockButton = root.Q<Button>("lock-button");
            _searchButton = root.Q<Button>("search-button");
            _collapseButton = root.Q<Button>("collapse-button");
            _logbookText = root.Q<TextField>("logbook-text");

            // Search
            _searchBar = root.Q("search-bar");
            _searchField = root.Q<TextField>("search-field");
            _searchResults = root.Q("search-results");

            // Collapsed state
            _collapsedPanel = root.Q("logbook-collapsed");

            // Wire events
            _folderDropdown.RegisterValueChangedCallback(OnFolderDropdownChanged);
            _addPageButton.clicked += OnAddPage;
            _lockButton.clicked += OnToggleLock;
            _searchButton.clicked += OnToggleSearch;
            _collapseButton.clicked += ToggleCollapse;
            _collapsedPanel.RegisterCallback<ClickEvent>(_ => Expand());

            // Text editing — write back to LogbookState immediately
            _logbookText.RegisterValueChangedCallback(OnTextChanged);

            // Auto-lock when user focuses the text field, auto-unlock when focus leaves
            _logbookText.RegisterCallback<FocusInEvent>(_ => AutoLockOnEdit());
            _logbookText.RegisterCallback<FocusOutEvent>(_ => AutoUnlockOnBlur());

            // Ctrl+F keyboard shortcut on the whole panel
            _expandedPanel.RegisterCallback<KeyDownEvent>(OnKeyDown);

            // Search field
            _searchField.RegisterValueChangedCallback(OnSearchChanged);

            // Context menus on page tabs and folder dropdown
            RegisterFolderContextMenu();

            // Initialize display
            PopulateFolderDropdown();
            UpdateLockDisplay();

            uiManager.RegisterTickRefreshable(this);
        }

        // ─── Tick Refresh ─────────────────────────────────

        public void Refresh()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var logbook = sim.CurrentGameState?.Logbook;
            if (logbook == null) return;

            // Ensure folders exist for ALL runners' current nodes (not just selected).
            // Cheap: only creates when missing, skips existing.
            var runners = sim.CurrentGameState.Runners;
            for (int i = 0; i < runners.Count; i++)
            {
                var r = runners[i];
                if (r.CurrentNodeId == null) continue;
                if (logbook.Folders.Exists(f => f.NodeId == r.CurrentNodeId)) continue;

                var node = sim.CurrentGameState.Map.GetNode(r.CurrentNodeId);
                if (node != null)
                    EnsureNodeFolder(r.CurrentNodeId, node.Name);
            }

            // Repopulate dropdown if folder count changed
            int folderCount = logbook.Folders.Count;
            if (folderCount != _lastFolderCount)
            {
                _lastFolderCount = folderCount;
                PopulateFolderDropdown();
            }

            // Auto-navigate to selected runner's node (if unlocked)
            string selectedRunnerId = _uiManager.SelectedRunnerId;
            if (selectedRunnerId != null && !_isLocked)
            {
                var runner = sim.CurrentGameState.Runners.Find(r => r.Id == selectedRunnerId);
                string nodeId = runner?.CurrentNodeId;

                bool runnerChanged = selectedRunnerId != _lastSelectedRunnerId;
                bool nodeChanged = nodeId != _lastRunnerNodeId;

                if ((runnerChanged || nodeChanged) && nodeId != null)
                {
                    var node = sim.CurrentGameState.Map.GetNode(nodeId);
                    if (node != null)
                    {
                        var folder = EnsureNodeFolder(nodeId, node.Name);
                        if (folder != null)
                            NavigateToFolder(folder.Id);
                    }
                }

                _lastSelectedRunnerId = selectedRunnerId;
                _lastRunnerNodeId = nodeId;
            }
        }

        // ─── Logbook State accessor ─────────────────────

        private LogbookState GetLogbook()
        {
            return _uiManager.Simulation?.CurrentGameState?.Logbook;
        }

        private LogbookFolder GetCurrentFolder()
        {
            var logbook = GetLogbook();
            if (logbook == null || _currentFolderId == null) return null;
            return logbook.Folders.Find(f => f.Id == _currentFolderId);
        }

        private LogbookPage GetCurrentPage()
        {
            var folder = GetCurrentFolder();
            if (folder == null || _currentPageId == null) return null;
            return folder.Pages.Find(p => p.Id == _currentPageId);
        }

        // ─── Folder Management ──────────────────────────

        /// <summary>
        /// Ensures a folder exists for the given node. Creates one with a default page if not found.
        /// Returns the folder.
        /// </summary>
        public LogbookFolder EnsureNodeFolder(string nodeId, string nodeName)
        {
            var logbook = GetLogbook();
            if (logbook == null) return null;

            var existing = logbook.Folders.Find(f => f.NodeId == nodeId);
            if (existing != null) return existing;

            var folder = new LogbookFolder(nodeName, nodeId);
            logbook.Folders.Add(folder);
            PopulateFolderDropdown();
            return folder;
        }

        private LogbookFolder CreateCustomFolder(string name)
        {
            var logbook = GetLogbook();
            if (logbook == null) return null;

            var folder = new LogbookFolder(name);
            logbook.Folders.Add(folder);
            PopulateFolderDropdown();
            return folder;
        }

        private void DeleteFolder(string folderId)
        {
            var logbook = GetLogbook();
            if (logbook == null) return;

            var folder = logbook.Folders.Find(f => f.Id == folderId);
            if (folder == null || folder.NodeId != null) return; // Can't delete node folders

            logbook.Folders.Remove(folder);

            // If we just deleted the current folder, navigate somewhere else
            if (_currentFolderId == folderId)
            {
                _currentFolderId = null;
                _currentPageId = null;
                if (logbook.Folders.Count > 0)
                    NavigateToFolder(logbook.Folders[0].Id);
            }

            PopulateFolderDropdown();
        }

        private void RenameFolder(string folderId, string newName)
        {
            var logbook = GetLogbook();
            var folder = logbook?.Folders.Find(f => f.Id == folderId);
            if (folder == null || folder.NodeId != null) return; // Can't rename node folders

            folder.Name = newName;
            PopulateFolderDropdown();
        }

        private void PopulateFolderDropdown()
        {
            var logbook = GetLogbook();
            var choices = new List<string>();
            _folderChoiceIds.Clear();

            if (logbook != null)
            {
                // Node folders first, then custom folders
                foreach (var folder in logbook.Folders)
                {
                    if (folder.NodeId != null)
                    {
                        choices.Add(folder.Name);
                        _folderChoiceIds.Add(folder.Id);
                    }
                }
                foreach (var folder in logbook.Folders)
                {
                    if (folder.NodeId == null)
                    {
                        choices.Add(folder.Name);
                        _folderChoiceIds.Add(folder.Id);
                    }
                }
            }

            // Add "New Folder..." option
            choices.Add("+ New Folder...");
            _folderChoiceIds.Add("__new_folder__");

            _folderDropdown.choices = choices;

            // Restore current selection
            if (_currentFolderId != null)
            {
                int idx = _folderChoiceIds.IndexOf(_currentFolderId);
                if (idx >= 0)
                    _folderDropdown.SetValueWithoutNotify(choices[idx]);
            }
            else if (choices.Count > 1) // at least one real folder + "New Folder..."
            {
                _folderDropdown.SetValueWithoutNotify(choices[0]);
            }
        }

        private void OnFolderDropdownChanged(ChangeEvent<string> evt)
        {
            int idx = _folderDropdown.choices.IndexOf(evt.newValue);
            if (idx < 0 || idx >= _folderChoiceIds.Count) return;

            string folderId = _folderChoiceIds[idx];

            if (folderId == "__new_folder__")
            {
                // Create a new custom folder
                var folder = CreateCustomFolder("New Folder");
                if (folder != null)
                    NavigateToFolder(folder.Id);
                return;
            }

            NavigateToFolder(folderId);
        }

        private void NavigateToFolder(string folderId)
        {
            _currentFolderId = folderId;
            var folder = GetCurrentFolder();
            if (folder == null) return;

            // Select in dropdown
            int idx = _folderChoiceIds.IndexOf(folderId);
            if (idx >= 0 && idx < _folderDropdown.choices.Count)
                _folderDropdown.SetValueWithoutNotify(_folderDropdown.choices[idx]);

            // Build page tabs
            RebuildPageTabs();

            // Select first page if current page is not in this folder
            if (folder.Pages.Count > 0)
            {
                var currentInFolder = _currentPageId != null && folder.Pages.Exists(p => p.Id == _currentPageId);
                if (!currentInFolder)
                    SelectPage(folder.Pages[0].Id);
            }
        }

        // ─── Page Management ────────────────────────────

        private void RebuildPageTabs()
        {
            _pageTabs.Clear();
            _pageTabCache.Clear();

            var folder = GetCurrentFolder();
            if (folder == null) return;

            foreach (var page in folder.Pages)
            {
                var tab = new Button();
                tab.text = page.Name;
                tab.AddToClassList("page-tab");

                string pageId = page.Id;
                tab.clicked += () => SelectPage(pageId);

                // Right-click context menu on each tab
                _uiManager.RegisterContextMenu(tab, () =>
                {
                    var actions = new List<(string, Action)>
                    {
                        ("Rename Page", () => StartRenamePage(pageId)),
                    };
                    // Can't delete last page
                    var currentFolder = GetCurrentFolder();
                    if (currentFolder != null && currentFolder.Pages.Count > 1)
                        actions.Add(("Delete Page", () => DeletePage(pageId)));
                    return actions;
                });

                _pageTabs.Add(tab);
                _pageTabCache[page.Id] = tab;
            }

            UpdateActivePageTab();
        }

        private void SelectPage(string pageId)
        {
            _currentPageId = pageId;
            UpdateActivePageTab();
            LoadPageContent();
        }

        private void UpdateActivePageTab()
        {
            foreach (var (id, tab) in _pageTabCache)
            {
                if (id == _currentPageId)
                    tab.AddToClassList("page-tab-active");
                else
                    tab.RemoveFromClassList("page-tab-active");
            }
        }

        private void LoadPageContent()
        {
            var page = GetCurrentPage();
            _logbookText.SetValueWithoutNotify(page?.Content ?? "");
        }

        private void OnAddPage()
        {
            var folder = GetCurrentFolder();
            if (folder == null) return;

            var page = new LogbookPage("New Page");
            folder.Pages.Add(page);
            RebuildPageTabs();
            SelectPage(page.Id);
        }

        private void DeletePage(string pageId)
        {
            var folder = GetCurrentFolder();
            if (folder == null || folder.Pages.Count <= 1) return;

            var page = folder.Pages.Find(p => p.Id == pageId);
            if (page == null) return;

            folder.Pages.Remove(page);

            // If deleted page was selected, switch to first page
            if (_currentPageId == pageId)
            {
                _currentPageId = null;
                if (folder.Pages.Count > 0)
                    SelectPage(folder.Pages[0].Id);
            }

            RebuildPageTabs();
        }

        private void StartRenamePage(string pageId)
        {
            var folder = GetCurrentFolder();
            var page = folder?.Pages.Find(p => p.Id == pageId);
            if (page == null) return;

            if (!_pageTabCache.TryGetValue(pageId, out var tab)) return;

            // Replace tab button text with a TextField for inline rename
            var renameField = new TextField();
            renameField.SetValueWithoutNotify(page.Name);
            renameField.AddToClassList("page-tab");
            renameField.style.maxWidth = 120;

            void CommitRename()
            {
                string newName = renameField.value?.Trim();
                if (!string.IsNullOrEmpty(newName))
                    page.Name = newName;
                RebuildPageTabs();
                UpdateActivePageTab();
            }

            renameField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    evt.StopPropagation();

                    CommitRename();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopPropagation();
                    RebuildPageTabs();
                    UpdateActivePageTab();
                }
            });

            renameField.RegisterCallback<FocusOutEvent>(_ => CommitRename());

            // Swap the tab with the rename field
            int tabIndex = _pageTabs.IndexOf(tab);
            if (tabIndex >= 0)
            {
                _pageTabs.Insert(tabIndex, renameField);
                tab.RemoveFromHierarchy();
                renameField.schedule.Execute(() => renameField.Focus());
            }
        }

        // ─── Folder Context Menu ────────────────────────

        private void RegisterFolderContextMenu()
        {
            // Right-click on the dropdown triggers context menu for the current folder
            _folderDropdown.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1) return; // right-click only
                evt.StopPropagation();

                var folder = GetCurrentFolder();
                if (folder == null) return;

                // Node folders can't be renamed or deleted
                if (folder.NodeId != null) return;

                string folderId = folder.Id;
                var actions = new List<(string, Action)>
                {
                    ("Rename Folder", () => StartRenameFolder(folderId)),
                    ("Delete Folder", () => DeleteFolder(folderId)),
                };
                _uiManager.ShowContextMenu(evt.position.x, evt.position.y, actions);
            });
        }

        private void StartRenameFolder(string folderId)
        {
            var logbook = GetLogbook();
            var folder = logbook?.Folders.Find(f => f.Id == folderId);
            if (folder == null || folder.NodeId != null) return;

            // For simplicity, use a prompt-style approach:
            // Replace the folder dropdown temporarily with a TextField
            var renameField = new TextField();
            renameField.SetValueWithoutNotify(folder.Name);
            renameField.style.maxWidth = 140;
            renameField.style.minWidth = 80;
            renameField.style.fontSize = 11;

            var parent = _folderDropdown.parent;
            int dropdownIndex = parent.IndexOf(_folderDropdown);

            void CommitRename()
            {
                string newName = renameField.value?.Trim();
                if (!string.IsNullOrEmpty(newName))
                    folder.Name = newName;

                // Restore dropdown
                parent.Insert(dropdownIndex, _folderDropdown);
                renameField.RemoveFromHierarchy();
                PopulateFolderDropdown();
            }

            renameField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    evt.StopPropagation();

                    CommitRename();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopPropagation();
                    parent.Insert(dropdownIndex, _folderDropdown);
                    renameField.RemoveFromHierarchy();
                }
            });

            renameField.RegisterCallback<FocusOutEvent>(_ => CommitRename());

            _folderDropdown.RemoveFromHierarchy();
            parent.Insert(dropdownIndex, renameField);
            renameField.schedule.Execute(() => renameField.Focus());
        }

        // ─── Text Editing ───────────────────────────────

        private void OnTextChanged(ChangeEvent<string> evt)
        {
            var page = GetCurrentPage();
            if (page != null)
                page.Content = evt.newValue;
        }

        private void AutoLockOnEdit()
        {
            if (!_isLocked)
            {
                _isLocked = true;
                _isManualLock = false;
                UpdateLockDisplay();
            }
        }

        private void AutoUnlockOnBlur()
        {
            // Only auto-unlock if this was an auto-lock (from typing).
            // Manual lock (from clicking the button) stays until manually toggled.
            if (_isLocked && !_isManualLock)
            {
                _isLocked = false;
                UpdateLockDisplay();
            }
        }

        // ─── Lock ───────────────────────────────────────

        private void OnToggleLock()
        {
            _isLocked = !_isLocked;
            _isManualLock = _isLocked; // clicking the button is always a manual action
            UpdateLockDisplay();
        }

        private void UpdateLockDisplay()
        {
            if (_isLocked)
            {
                _lockButton.text = "Locked";
                _lockButton.AddToClassList("lock-active");
            }
            else
            {
                _lockButton.text = "Unlocked";
                _lockButton.RemoveFromClassList("lock-active");
            }
        }

        // ─── Collapse / Expand ──────────────────────────

        public void ToggleCollapse()
        {
            if (_isExpanded) Collapse();
            else Expand();
        }

        public void Collapse()
        {
            _isExpanded = false;
            _expandedPanel.style.display = DisplayStyle.None;
            _collapsedPanel.style.display = DisplayStyle.Flex;
        }

        public void Expand()
        {
            _isExpanded = true;
            _expandedPanel.style.display = DisplayStyle.Flex;
            _collapsedPanel.style.display = DisplayStyle.None;
        }

        // ─── Auto-navigation on runner selection ────────

        /// <summary>
        /// Called by UIManager when the selected runner changes.
        /// If not locked, navigates to the folder matching the runner's current node.
        /// </summary>
        public void OnRunnerSelected(string runnerId)
        {
            if (_isLocked) return;

            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var runner = sim.CurrentGameState.Runners.Find(r => r.Id == runnerId);
            if (runner?.CurrentNodeId == null) return;

            // Update tracking so Refresh() doesn't double-navigate this tick
            _lastSelectedRunnerId = runnerId;
            _lastRunnerNodeId = runner.CurrentNodeId;

            var node = sim.CurrentGameState.Map.GetNode(runner.CurrentNodeId);
            if (node == null) return;

            var folder = EnsureNodeFolder(runner.CurrentNodeId, node.Name);
            if (folder != null)
                NavigateToFolder(folder.Id);
        }

        // ─── Search (Ctrl+F) ────────────────────────────

        private void OnToggleSearch()
        {
            _isSearchVisible = !_isSearchVisible;
            _searchBar.style.display = _isSearchVisible ? DisplayStyle.Flex : DisplayStyle.None;

            if (_isSearchVisible)
            {
                _searchField.SetValueWithoutNotify("");
                _searchResults.Clear();
                _searchField.schedule.Execute(() => _searchField.Focus());
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.ctrlKey && evt.keyCode == KeyCode.F)
            {
                evt.StopPropagation();
                if (!_isSearchVisible)
                    OnToggleSearch();
                else
                    _searchField.Focus();
            }
            else if (evt.keyCode == KeyCode.Escape && _isSearchVisible)
            {
                evt.StopPropagation();
                OnToggleSearch();
            }
        }

        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            _searchResults.Clear();
            string query = evt.newValue?.Trim();
            if (string.IsNullOrEmpty(query))
                return;

            var logbook = GetLogbook();
            if (logbook == null) return;

            int resultCount = 0;
            const int maxResults = 10;

            foreach (var folder in logbook.Folders)
            {
                foreach (var page in folder.Pages)
                {
                    if (resultCount >= maxResults) break;

                    int matchIndex = page.Content != null
                        ? page.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase)
                        : -1;

                    // Also search page name
                    bool nameMatch = page.Name != null
                        && page.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (matchIndex < 0 && !nameMatch) continue;

                    // Build snippet
                    string snippet;
                    if (matchIndex >= 0)
                    {
                        int start = Math.Max(0, matchIndex - 20);
                        int end = Math.Min(page.Content.Length, matchIndex + query.Length + 20);
                        snippet = (start > 0 ? "..." : "") + page.Content.Substring(start, end - start) + (end < page.Content.Length ? "..." : "");
                    }
                    else
                    {
                        snippet = "(title match)";
                    }

                    var resultLabel = new Label($"{folder.Name} > {page.Name}: {snippet}");
                    resultLabel.AddToClassList("search-result-row");

                    string targetFolderId = folder.Id;
                    string targetPageId = page.Id;
                    resultLabel.RegisterCallback<ClickEvent>(_ =>
                    {
                        NavigateToFolder(targetFolderId);
                        SelectPage(targetPageId);
                    });

                    _searchResults.Add(resultLabel);
                    resultCount++;
                }
                if (resultCount >= maxResults) break;
            }
        }

        // ─── Copy to Logbook (public API for other controllers) ──

        /// <summary>
        /// Appends the given text to the current page's content.
        /// If no folder/page exists, creates a "General" custom folder first.
        /// </summary>
        public void AppendToCurrentPage(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var logbook = GetLogbook();
            if (logbook == null) return;

            // Ensure we have at least one folder and page
            if (_currentFolderId == null || GetCurrentFolder() == null)
            {
                if (logbook.Folders.Count == 0)
                {
                    var folder = CreateCustomFolder("General");
                    if (folder != null)
                        NavigateToFolder(folder.Id);
                }
                else
                {
                    NavigateToFolder(logbook.Folders[0].Id);
                }
            }

            var page = GetCurrentPage();
            if (page == null) return;

            // Append with newline separator
            if (!string.IsNullOrEmpty(page.Content) && !page.Content.EndsWith("\n"))
                page.Content += "\n";
            page.Content += text + "\n";

            // Refresh text field if this page is currently displayed
            LoadPageContent();
        }

    }
}

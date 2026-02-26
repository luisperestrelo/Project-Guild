using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectGuild.Bridge;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Controller for the Options overlay panel. Manages UX preferences
    /// (logbook, default filters) and save/load/new game operations.
    /// Follows BankPanelController pattern (Open/Close/Toggle/IsOpen).
    /// </summary>
    public class OptionsPanelController
    {
        private readonly VisualElement _root;
        private readonly VisualElement _panelRoot;
        private readonly UIManager _uiManager;

        // ─── Preference controls ─────────────────────────
        private readonly Toggle _toggleAutoNavSelection;
        private readonly Toggle _toggleAutoNavArrival;
        private readonly Toggle _toggleAutoExpand;
        private readonly DropdownField _chronicleScopeDropdown;
        private readonly DropdownField _decisionLogScopeDropdown;

        // ─── Save/Load controls ──────────────────────────
        private readonly Label _saveStatusLabel;
        private readonly Button _loadButton;

        // ─── Confirmation dialog ─────────────────────────
        private readonly VisualElement _confirmOverlay;
        private readonly Label _confirmMessage;
        private readonly Button _confirmYesButton;
        private readonly Button _confirmNoButton;
        private Action _pendingConfirmAction;

        // ─── Scope filter choices ────────────────────────
        private static readonly List<string> ChronicleFilterChoices = new()
            { "Current Node", "Selected Runner", "Global" };
        private static readonly List<string> ChronicleFilterValues = new()
            { "CurrentNode", "SelectedRunner", "Global" };

        private static readonly List<string> DecisionLogFilterChoices = new()
            { "Current Node", "Selected Runner", "All" };
        private static readonly List<string> DecisionLogFilterValues = new()
            { "CurrentNode", "SelectedRunner", "All" };

        public bool IsOpen { get; private set; }

        public OptionsPanelController(VisualElement root, UIManager uiManager)
        {
            _root = root;
            _panelRoot = root.Q("options-panel-root");
            _uiManager = uiManager;

            // Make panel root focusable so it can receive KeyDownEvents
            _panelRoot.focusable = true;

            // Close button
            root.Q<Button>("btn-close-options").clicked += Close;

            // Toggles
            _toggleAutoNavSelection = root.Q<Toggle>("toggle-logbook-auto-nav-selection");
            _toggleAutoNavArrival = root.Q<Toggle>("toggle-logbook-auto-nav-arrival");
            _toggleAutoExpand = root.Q<Toggle>("toggle-logbook-auto-expand");

            // Dropdowns
            _chronicleScopeDropdown = root.Q<DropdownField>("dropdown-chronicle-scope");
            _chronicleScopeDropdown.choices = ChronicleFilterChoices;

            _decisionLogScopeDropdown = root.Q<DropdownField>("dropdown-decision-log-scope");
            _decisionLogScopeDropdown.choices = DecisionLogFilterChoices;

            // Save/Load
            _saveStatusLabel = root.Q<Label>("save-status-label");
            _loadButton = root.Q<Button>("btn-load-game");
            root.Q<Button>("btn-save-game").clicked += OnSaveClicked;
            _loadButton.clicked += OnLoadClicked;
            root.Q<Button>("btn-new-game").clicked += OnNewGameClicked;

            // Confirmation dialog
            _confirmOverlay = root.Q("options-confirm-overlay");
            _confirmMessage = root.Q<Label>("confirm-message");
            _confirmYesButton = root.Q<Button>("btn-confirm-yes");
            _confirmNoButton = root.Q<Button>("btn-confirm-no");
            _confirmYesButton.clicked += OnConfirmYes;
            _confirmNoButton.clicked += OnConfirmNo;

            // Load current preferences into controls
            LoadPreferencesIntoControls();

            // Register change callbacks (after setting initial values to avoid spurious saves)
            _toggleAutoNavSelection.RegisterValueChangedCallback(OnToggleChanged);
            _toggleAutoNavArrival.RegisterValueChangedCallback(OnToggleChanged);
            _toggleAutoExpand.RegisterValueChangedCallback(OnToggleChanged);
            _chronicleScopeDropdown.RegisterValueChangedCallback(OnDropdownChanged);
            _decisionLogScopeDropdown.RegisterValueChangedCallback(OnDropdownChanged);

            // Escape to close (registered on inner panel root, not TemplateContainer)
            _panelRoot.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    if (_confirmOverlay.style.display == DisplayStyle.Flex)
                        DismissConfirmation();
                    else
                        Close();
                    evt.StopPropagation();
                }
            });

            // Start hidden
            _root.style.display = DisplayStyle.None;
        }

        // ─── Open / Close / Toggle ──────────────────────

        public void Open()
        {
            IsOpen = true;
            _root.style.display = DisplayStyle.Flex;
            _panelRoot.Focus();
            LoadPreferencesIntoControls();
            RefreshSaveStatus();
        }

        public void Close()
        {
            IsOpen = false;
            _root.style.display = DisplayStyle.None;
            DismissConfirmation();
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        // ─── Preferences ────────────────────────────────

        private void LoadPreferencesIntoControls()
        {
            var prefs = _uiManager.Preferences;

            _toggleAutoNavSelection.SetValueWithoutNotify(prefs.LogbookAutoNavigateOnSelection);
            _toggleAutoNavArrival.SetValueWithoutNotify(prefs.LogbookAutoNavigateOnArrival);
            _toggleAutoExpand.SetValueWithoutNotify(prefs.LogbookAutoExpandOnNavigation);

            // Chronicle scope
            int chronicleIdx = ChronicleFilterValues.IndexOf(prefs.ChronicleDefaultScopeFilter);
            if (chronicleIdx < 0) chronicleIdx = 0;
            _chronicleScopeDropdown.SetValueWithoutNotify(ChronicleFilterChoices[chronicleIdx]);

            // Decision log scope
            int decisionIdx = DecisionLogFilterValues.IndexOf(prefs.DecisionLogDefaultScopeFilter);
            if (decisionIdx < 0) decisionIdx = 1; // default to "Selected Runner"
            _decisionLogScopeDropdown.SetValueWithoutNotify(DecisionLogFilterChoices[decisionIdx]);
        }

        private void SavePreferencesFromControls()
        {
            var prefs = _uiManager.Preferences;

            prefs.LogbookAutoNavigateOnSelection = _toggleAutoNavSelection.value;
            prefs.LogbookAutoNavigateOnArrival = _toggleAutoNavArrival.value;
            prefs.LogbookAutoExpandOnNavigation = _toggleAutoExpand.value;

            // Chronicle scope
            int chronicleIdx = ChronicleFilterChoices.IndexOf(_chronicleScopeDropdown.value);
            if (chronicleIdx >= 0 && chronicleIdx < ChronicleFilterValues.Count)
                prefs.ChronicleDefaultScopeFilter = ChronicleFilterValues[chronicleIdx];

            // Decision log scope
            int decisionIdx = DecisionLogFilterChoices.IndexOf(_decisionLogScopeDropdown.value);
            if (decisionIdx >= 0 && decisionIdx < DecisionLogFilterValues.Count)
                prefs.DecisionLogDefaultScopeFilter = DecisionLogFilterValues[decisionIdx];

            prefs.Save();
        }

        private void OnToggleChanged(ChangeEvent<bool> evt) => SavePreferencesFromControls();
        private void OnDropdownChanged(ChangeEvent<string> evt) => SavePreferencesFromControls();

        // ─── Save / Load ─────────────────────────────────

        private void RefreshSaveStatus()
        {
            var saveManager = _uiManager.SaveManager;
            if (saveManager == null)
            {
                _saveStatusLabel.text = "Save system unavailable";
                _loadButton.SetEnabled(false);
                return;
            }

            if (saveManager.SaveExists("save1"))
            {
                // Show file modification time
                string path = System.IO.Path.Combine(
                    Application.persistentDataPath, "saves", "save1.json");
                try
                {
                    var lastWrite = System.IO.File.GetLastWriteTime(path);
                    _saveStatusLabel.text = $"Last saved: {lastWrite:yyyy-MM-dd HH:mm:ss}";
                }
                catch
                {
                    _saveStatusLabel.text = "Save file exists";
                }
                _loadButton.SetEnabled(true);
            }
            else
            {
                _saveStatusLabel.text = "No save file found";
                _loadButton.SetEnabled(false);
            }
        }

        private void OnSaveClicked()
        {
            _uiManager.RequestSaveGame();
            RefreshSaveStatus();
        }

        private void OnLoadClicked()
        {
            ShowConfirmation(
                "Load saved game?\nCurrent progress will be lost.",
                () => _uiManager.RequestLoadGame());
        }

        private void OnNewGameClicked()
        {
            ShowConfirmation(
                "Start a new game?\nCurrent progress will be lost.",
                () => _uiManager.RequestNewGame());
        }

        // ─── Confirmation Dialog ─────────────────────────

        private void ShowConfirmation(string message, Action onConfirm)
        {
            _pendingConfirmAction = onConfirm;
            _confirmMessage.text = message;
            _confirmOverlay.style.display = DisplayStyle.Flex;
        }

        private void DismissConfirmation()
        {
            _pendingConfirmAction = null;
            _confirmOverlay.style.display = DisplayStyle.None;
        }

        private void OnConfirmYes()
        {
            var action = _pendingConfirmAction;
            DismissConfirmation();
            Close();
            action?.Invoke();
        }

        private void OnConfirmNo()
        {
            DismissConfirmation();
        }
    }
}

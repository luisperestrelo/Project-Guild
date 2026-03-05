using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ProjectGuild.Bridge;
using ProjectGuild.View;

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
        private readonly Toggle _toggleSkipDeleteConfirm;
        private readonly Toggle _toggleSkipCancelCreationConfirm;
        private readonly Toggle _toggleMapCenterOnRunner;
        private readonly Toggle _toggleMapCloseOnAssignment;
        private readonly Toggle _toggleTutorialEnabled;

        // ─── Save/Load controls ──────────────────────────
        private readonly Label _saveStatusLabel;
        private readonly Button _loadButton;

        // ─── Confirmation dialog ─────────────────────────
        private readonly VisualElement _confirmOverlay;
        private readonly Label _confirmMessage;
        private readonly Button _confirmYesButton;
        private readonly Button _confirmNoButton;
        private Action _pendingConfirmAction;

        // ─── Hotkey rebinding ────────────────────────────
        private readonly Dictionary<string, Button> _hotkeyButtons = new();
        private string _rebindingHotkeyId;
        private IVisualElementScheduledItem _rebindTimeoutSchedule;

        private static readonly (string id, string label, Func<PlayerPreferences, string> getter,
            Action<PlayerPreferences, string> setter)[] HotkeyDefs =
        {
            ("map", "Map", p => p.HotkeyMap, (p, v) => p.HotkeyMap = v),
            ("automation", "Automation", p => p.HotkeyAutomation, (p, v) => p.HotkeyAutomation = v),
            ("options", "Options", p => p.HotkeyOptions, (p, v) => p.HotkeyOptions = v),
            ("guildhall", "Guild Hall", p => p.HotkeyGuildHall, (p, v) => p.HotkeyGuildHall = v),
        };

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

            _toggleSkipDeleteConfirm = root.Q<Toggle>("toggle-skip-delete-confirm");
            _toggleSkipCancelCreationConfirm = root.Q<Toggle>("toggle-skip-cancel-creation-confirm");
            _toggleMapCenterOnRunner = root.Q<Toggle>("toggle-map-center-on-runner");
            _toggleMapCloseOnAssignment = root.Q<Toggle>("toggle-map-close-on-assignment");
            _toggleTutorialEnabled = root.Q<Toggle>("toggle-tutorial-enabled");

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
            _toggleSkipDeleteConfirm.RegisterValueChangedCallback(OnToggleChanged);
            _toggleSkipCancelCreationConfirm.RegisterValueChangedCallback(OnToggleChanged);
            _toggleMapCenterOnRunner.RegisterValueChangedCallback(OnToggleChanged);
            _toggleMapCloseOnAssignment.RegisterValueChangedCallback(OnToggleChanged);
            _toggleTutorialEnabled.RegisterValueChangedCallback(OnToggleChanged);
            _chronicleScopeDropdown.RegisterValueChangedCallback(OnDropdownChanged);
            _decisionLogScopeDropdown.RegisterValueChangedCallback(OnDropdownChanged);

            // Build hotkey rebinding section
            BuildHotkeySection(root);

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
            if (_rebindingHotkeyId != null)
                CancelRebind();
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
            _toggleSkipDeleteConfirm.SetValueWithoutNotify(prefs.SkipDeleteConfirmation);
            _toggleSkipCancelCreationConfirm.SetValueWithoutNotify(prefs.SkipCancelCreationConfirmation);
            _toggleMapCenterOnRunner.SetValueWithoutNotify(prefs.MapCenterOnRunner);
            _toggleMapCloseOnAssignment.SetValueWithoutNotify(prefs.MapCloseOnAssignment);
            _toggleTutorialEnabled.SetValueWithoutNotify(prefs.TutorialEnabledForNewGames);

            // Chronicle scope
            int chronicleIdx = ChronicleFilterValues.IndexOf(prefs.ChronicleDefaultScopeFilter);
            if (chronicleIdx < 0) chronicleIdx = 0;
            _chronicleScopeDropdown.SetValueWithoutNotify(ChronicleFilterChoices[chronicleIdx]);

            // Decision log scope
            int decisionIdx = DecisionLogFilterValues.IndexOf(prefs.DecisionLogDefaultScopeFilter);
            if (decisionIdx < 0) decisionIdx = 1; // default to "Selected Runner"
            _decisionLogScopeDropdown.SetValueWithoutNotify(DecisionLogFilterChoices[decisionIdx]);

            // Hotkey buttons
            foreach (var def in HotkeyDefs)
            {
                if (_hotkeyButtons.TryGetValue(def.id, out var btn))
                    btn.text = def.getter(prefs).ToUpper();
            }
        }

        private void SavePreferencesFromControls()
        {
            var prefs = _uiManager.Preferences;

            prefs.LogbookAutoNavigateOnSelection = _toggleAutoNavSelection.value;
            prefs.LogbookAutoNavigateOnArrival = _toggleAutoNavArrival.value;
            prefs.LogbookAutoExpandOnNavigation = _toggleAutoExpand.value;
            prefs.SkipDeleteConfirmation = _toggleSkipDeleteConfirm.value;
            prefs.SkipCancelCreationConfirmation = _toggleSkipCancelCreationConfirm.value;
            prefs.MapCenterOnRunner = _toggleMapCenterOnRunner.value;
            prefs.MapCloseOnAssignment = _toggleMapCloseOnAssignment.value;
            prefs.TutorialEnabledForNewGames = _toggleTutorialEnabled.value;

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

        // ─── Hotkey Rebinding ────────────────────────────

        private void BuildHotkeySection(VisualElement root)
        {
            var hotkeyContainer = root.Q("hotkey-section");
            if (hotkeyContainer == null) return;

            var prefs = _uiManager.Preferences;

            foreach (var def in HotkeyDefs)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingTop = 4;
                row.style.paddingBottom = 4;

                var label = new Label(def.label);
                label.style.color = new StyleColor(new Color(0.78f, 0.78f, 0.84f));
                label.style.fontSize = 12;
                label.style.minWidth = 100;
                row.Add(label);

                string currentKey = def.getter(prefs);
                var btn = new Button();
                btn.text = currentKey.ToUpper();
                btn.style.minWidth = 60;
                btn.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.16f));
                btn.style.color = new StyleColor(new Color(0.86f, 0.86f, 0.94f));
                btn.style.borderTopWidth = btn.style.borderBottomWidth =
                    btn.style.borderLeftWidth = btn.style.borderRightWidth = 1;
                btn.style.borderTopColor = btn.style.borderBottomColor =
                    btn.style.borderLeftColor = btn.style.borderRightColor =
                        new StyleColor(new Color(0.3f, 0.3f, 0.4f));
                btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                    btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 3;
                btn.style.paddingLeft = btn.style.paddingRight = 8;
                btn.style.paddingTop = btn.style.paddingBottom = 3;
                btn.style.fontSize = 12;
                btn.style.unityFontStyleAndWeight = FontStyle.Bold;
                btn.style.unityTextAlign = TextAnchor.MiddleCenter;

                string hotkeyId = def.id;
                btn.clicked += () => StartRebind(hotkeyId);
                _hotkeyButtons[def.id] = btn;
                row.Add(btn);

                hotkeyContainer.Add(row);
            }
        }

        private void StartRebind(string hotkeyId)
        {
            if (_rebindingHotkeyId != null)
                CancelRebind();

            _rebindingHotkeyId = hotkeyId;
            if (_hotkeyButtons.TryGetValue(hotkeyId, out var btn))
            {
                btn.text = "...";
                btn.style.borderTopColor = btn.style.borderBottomColor =
                    btn.style.borderLeftColor = btn.style.borderRightColor =
                        new StyleColor(new Color(0.86f, 0.7f, 0.23f));
            }

            // Listen for any keypress on the panel root
            _panelRoot.RegisterCallback<KeyDownEvent>(OnRebindKeyDown);

            // 5-second timeout
            _rebindTimeoutSchedule = _panelRoot.schedule.Execute(() => CancelRebind()).StartingIn(5000);
        }

        private void OnRebindKeyDown(KeyDownEvent evt)
        {
            if (_rebindingHotkeyId == null) return;

            evt.StopPropagation();

            if (evt.keyCode == KeyCode.Escape)
            {
                CancelRebind();
                return;
            }

            // Convert KeyCode to input system key name
            string keyName = KeyCodeToInputSystemName(evt.keyCode);
            if (keyName == null) return;

            // Apply the binding
            var prefs = _uiManager.Preferences;
            foreach (var def in HotkeyDefs)
            {
                if (def.id == _rebindingHotkeyId)
                {
                    def.setter(prefs, keyName);
                    break;
                }
            }
            prefs.Save();

            // Update button text
            if (_hotkeyButtons.TryGetValue(_rebindingHotkeyId, out var btn))
            {
                btn.text = keyName.ToUpper();
                btn.style.borderTopColor = btn.style.borderBottomColor =
                    btn.style.borderLeftColor = btn.style.borderRightColor =
                        new StyleColor(new Color(0.3f, 0.3f, 0.4f));
            }

            FinishRebind();

            // Rebuild CameraController hotkey InputActions
            var cam = UnityEngine.Object.FindAnyObjectByType<CameraController>();
            cam?.RebuildHotkeyActions();
        }

        private void CancelRebind()
        {
            if (_rebindingHotkeyId == null) return;

            // Restore button text to current value
            var prefs = _uiManager.Preferences;
            foreach (var def in HotkeyDefs)
            {
                if (def.id == _rebindingHotkeyId)
                {
                    if (_hotkeyButtons.TryGetValue(_rebindingHotkeyId, out var btn))
                    {
                        btn.text = def.getter(prefs).ToUpper();
                        btn.style.borderTopColor = btn.style.borderBottomColor =
                            btn.style.borderLeftColor = btn.style.borderRightColor =
                                new StyleColor(new Color(0.3f, 0.3f, 0.4f));
                    }
                    break;
                }
            }

            FinishRebind();
        }

        private void FinishRebind()
        {
            _rebindingHotkeyId = null;
            _panelRoot.UnregisterCallback<KeyDownEvent>(OnRebindKeyDown);
            _rebindTimeoutSchedule?.Pause();
            _rebindTimeoutSchedule = null;
        }

        private static string KeyCodeToInputSystemName(KeyCode keyCode)
        {
            // Map common KeyCodes to Input System key names
            if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
                return ((char)('a' + (keyCode - KeyCode.A))).ToString();
            if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
                return ((char)('0' + (keyCode - KeyCode.Alpha0))).ToString();
            if (keyCode >= KeyCode.F1 && keyCode <= KeyCode.F12)
                return $"f{1 + (keyCode - KeyCode.F1)}";

            return keyCode switch
            {
                KeyCode.Space => "space",
                KeyCode.Tab => "tab",
                KeyCode.BackQuote => "backquote",
                KeyCode.Minus => "minus",
                KeyCode.Equals => "equals",
                KeyCode.LeftBracket => "leftBracket",
                KeyCode.RightBracket => "rightBracket",
                KeyCode.Backslash => "backslash",
                KeyCode.Semicolon => "semicolon",
                KeyCode.Quote => "quote",
                KeyCode.Comma => "comma",
                KeyCode.Period => "period",
                KeyCode.Slash => "slash",
                _ => null
            };
        }

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

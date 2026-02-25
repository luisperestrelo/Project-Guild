using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Gathering;
using ProjectGuild.Simulation.Automation;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Manages the bottom-right runner details panel.
    /// Handles tab switching (Overview, Inventory, Skills) and refreshes
    /// displayed data for the selected runner every tick.
    ///
    /// Plain C# class (not MonoBehaviour). Skill rows and inventory slots
    /// are built once at construction, then refreshed with current data.
    /// </summary>
    public class RunnerDetailsPanelController : ITickRefreshable
    {
        private readonly UIManager _uiManager;
        private readonly VisualElement _root;

        // ─── Tab switching ──────────────────────────────
        private readonly Button _tabOverview;
        private readonly Button _tabInventory;
        private readonly Button _tabSkills;
        private readonly Button _tabAutomation;
        private readonly ScrollView _contentOverview;
        private readonly ScrollView _contentInventory;
        private readonly ScrollView _contentSkills;
        private readonly VisualElement _contentAutomation;
        private AutomationTabController _automationTabController;
        private readonly VisualTreeAsset _automationTabAsset;
        private string _activeTab = "overview";

        // ─── Overview elements ──────────────────────────
        private readonly Label _nameLabel;
        private readonly Label _stateLabel;
        private readonly Label _taskInfoLabel;
        private readonly VisualElement _travelProgressContainer;
        private readonly Label _progressLabel;
        private readonly ProgressBar _travelProgressBar;
        private readonly Label _skillsSummaryLabel;
        private readonly Label _warningLabel;

        // ─── Live stats elements ─────────────────────────
        private readonly VisualElement _liveStatsContainer;
        private VisualElement _statRowInventory;
        private Label _statValueInventory;
        private VisualElement _statRowTravelSpeed;
        private Label _statValueTravelSpeed;
        private VisualElement _statRowEta;
        private Label _statValueEta;
        private VisualElement _statRowTravelXp;
        private Label _statValueTravelXp;
        private Label _statLabelTravelXp;
        private VisualElement _statRowGatherSpeed;
        private Label _statValueGatherSpeed;
        private VisualElement _statRowGatherXp;
        private Label _statLabelGatherXp;
        private Label _statValueGatherXp;
        private string _tooltipTravelSpeed = "";
        private string _tooltipEta = "";
        private string _tooltipTravelXp = "";
        private string _tooltipGatherSpeed = "";
        private string _tooltipGatherXp = "";

        // ─── Skill XP bars (live stats) ───────────────────
        // Per-runner tracking so switching runners preserves XP bar visibility
        private readonly Dictionary<string, float[]> _perRunnerSkillXp = new();
        private readonly Dictionary<string, float[]> _perRunnerSkillXpChangeTime = new();
        private readonly List<(VisualElement row, Label label, ProgressBar bar)> _xpBarPool = new();

        // ─── Skills tab elements ────────────────────────
        private readonly VisualElement _skillsList;
        private readonly Label[] _skillLevelLabels;
        private readonly Label[] _skillPassionLabels;
        private readonly ProgressBar[] _skillProgressBars;
        private readonly string[] _skillRowTooltips;
        private readonly string[] _passionTooltips;

        // ─── Inventory tab elements ─────────────────────
        private readonly Label _inventoryTabSummary;
        private readonly VisualElement _inventoryGrid;
        private readonly List<VisualElement> _inventorySlots = new();
        private readonly List<VisualElement> _inventorySlotIcons = new();
        private readonly List<Label> _inventorySlotLabels = new();
        private readonly List<Label> _inventorySlotQuantities = new();
        private string[] _slotTooltips = new string[0];

        // ─── Rename state ─────────────────────────────────
        private TextField _renameField;
        private bool _isRenaming;

        private string _currentRunnerId;

        public RunnerDetailsPanelController(VisualElement root, UIManager uiManager,
            VisualTreeAsset automationTabAsset = null)
        {
            _root = root;
            _uiManager = uiManager;
            _automationTabAsset = automationTabAsset;

            // ─── Tab buttons ────────────────────────────
            _tabOverview = root.Q<Button>("tab-overview");
            _tabInventory = root.Q<Button>("tab-inventory");
            _tabSkills = root.Q<Button>("tab-skills");
            _tabAutomation = root.Q<Button>("tab-automation");

            _tabOverview.clicked += () => SwitchTab("overview");
            _tabInventory.clicked += () => SwitchTab("inventory");
            _tabSkills.clicked += () => SwitchTab("skills");
            _tabAutomation.clicked += () => SwitchTab("automation");

            // Enable the Automation tab (remove disabled class)
            _tabAutomation.RemoveFromClassList("tab-disabled");

            // ─── Tab content panels ─────────────────────
            _contentOverview = root.Q<ScrollView>("tab-content-overview");
            _contentInventory = root.Q<ScrollView>("tab-content-inventory");
            _contentSkills = root.Q<ScrollView>("tab-content-skills");
            _contentAutomation = root.Q("tab-content-automation");

            // ─── Overview elements ──────────────────────
            _nameLabel = root.Q<Label>("runner-name-label");
            _nameLabel.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount == 2)
                    BeginRename();
            });
            _stateLabel = root.Q<Label>("runner-state-label");
            _taskInfoLabel = root.Q<Label>("task-info-label");
            _travelProgressContainer = root.Q("travel-progress-container");
            _progressLabel = root.Q<Label>("travel-progress-label");
            _travelProgressBar = root.Q<ProgressBar>("travel-progress-bar");
            _warningLabel = root.Q<Label>("overview-warning-label");
            _liveStatsContainer = root.Q("live-stats-container");
            BuildLiveStatRows();
            _skillsSummaryLabel = root.Q<Label>("skills-summary-label");

            // ─── Skills tab ─────────────────────────────
            _skillsList = root.Q("skills-list");
            _skillLevelLabels = new Label[SkillTypeExtensions.SkillCount];
            _skillPassionLabels = new Label[SkillTypeExtensions.SkillCount];
            _skillProgressBars = new ProgressBar[SkillTypeExtensions.SkillCount];
            _skillRowTooltips = new string[SkillTypeExtensions.SkillCount];
            _passionTooltips = new string[SkillTypeExtensions.SkillCount];
            BuildSkillRows();

            // ─── Inventory tab ──────────────────────────
            _inventoryTabSummary = root.Q<Label>("inventory-tab-summary");
            _inventoryGrid = root.Q("inventory-grid");
            BuildInventoryGrid();
            _slotTooltips = new string[_inventorySlots.Count];

            uiManager.RegisterTickRefreshable(this);
        }

        private void SwitchTab(string tabName)
        {
            _activeTab = tabName;

            // Toggle tab-active / remove from all buttons
            SetTabActive(_tabOverview, tabName == "overview");
            SetTabActive(_tabInventory, tabName == "inventory");
            SetTabActive(_tabSkills, tabName == "skills");
            SetTabActive(_tabAutomation, tabName == "automation");

            // Show/hide content panels
            _contentOverview.style.display = tabName == "overview" ? DisplayStyle.Flex : DisplayStyle.None;
            _contentInventory.style.display = tabName == "inventory" ? DisplayStyle.Flex : DisplayStyle.None;
            _contentSkills.style.display = tabName == "skills" ? DisplayStyle.Flex : DisplayStyle.None;
            _contentAutomation.style.display = tabName == "automation" ? DisplayStyle.Flex : DisplayStyle.None;

            // Lazy-init automation tab controller on first switch
            if (tabName == "automation" && _automationTabController == null)
                InitializeAutomationTab();

            Refresh();
        }

        private static void SetTabActive(Button tab, bool active)
        {
            if (active)
                tab.AddToClassList("tab-active");
            else
                tab.RemoveFromClassList("tab-active");
        }

        private void InitializeAutomationTab()
        {
            if (_automationTabAsset == null || _contentAutomation == null) return;

            var instance = _automationTabAsset.Instantiate();
            // TemplateContainer needs flex-grow to fill the tab content area,
            // otherwise it stays zero-height and children are invisible.
            instance.style.flexGrow = 1;
            _contentAutomation.Add(instance);
            _automationTabController = new AutomationTabController(instance, _uiManager);

            if (_currentRunnerId != null)
                _automationTabController.ShowRunner(_currentRunnerId);
        }

        public void ShowRunner(string runnerId)
        {
            if (_isRenaming) CancelRename();
            _currentRunnerId = runnerId;
            _automationTabController?.ShowRunner(runnerId);
            Refresh();
        }

        // ─── Rename ──────────────────────────────────────

        private void BeginRename()
        {
            if (_isRenaming || _currentRunnerId == null) return;
            _isRenaming = true;

            // Create a text field in place of the name label
            _renameField = new TextField();
            _renameField.AddToClassList("runner-name-rename");
            _renameField.value = _nameLabel.text;
            _nameLabel.style.display = DisplayStyle.None;

            // Insert the text field where the name label is
            _nameLabel.parent.Insert(_nameLabel.parent.IndexOf(_nameLabel) + 1, _renameField);

            // Focus and select all text
            _renameField.schedule.Execute(() =>
            {
                _renameField.Q("unity-text-input").Focus();
                _renameField.SelectAll();
            });

            // Confirm on Enter
            _renameField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    CommitRename();
                else if (evt.keyCode == KeyCode.Escape)
                    CancelRename();
            });

            // Confirm on focus loss
            _renameField.RegisterCallback<FocusOutEvent>(evt => CommitRename());
        }

        private void CommitRename()
        {
            if (!_isRenaming) return;
            _isRenaming = false;

            string newName = _renameField?.value?.Trim();
            if (!string.IsNullOrEmpty(newName) && _currentRunnerId != null)
                _uiManager.Simulation?.CommandRenameRunner(_currentRunnerId, newName);

            CleanupRenameField();
        }

        private void CancelRename()
        {
            if (!_isRenaming) return;
            _isRenaming = false;
            CleanupRenameField();
        }

        private void CleanupRenameField()
        {
            _renameField?.RemoveFromHierarchy();
            _renameField = null;
            _nameLabel.style.display = DisplayStyle.Flex;
        }

        public void Refresh()
        {
            var sim = _uiManager.Simulation;
            if (sim == null || _currentRunnerId == null) return;

            var runner = sim.FindRunner(_currentRunnerId);
            if (runner == null) return;

            var config = sim.Config;

            // Always refresh overview header (visible name/state at top)
            RefreshOverviewHeader(runner, sim);

            // Only refresh the active tab's data
            switch (_activeTab)
            {
                case "overview":
                    RefreshOverview(runner, sim, config);
                    break;
                case "inventory":
                    RefreshInventoryTab(runner, sim);
                    break;
                case "skills":
                    RefreshSkillsTab(runner, config);
                    break;
                case "automation":
                    _automationTabController?.Refresh();
                    break;
            }
        }

        // ─── Overview ───────────────────────────────────

        private void RefreshOverviewHeader(Runner runner, GameSimulation sim)
        {
            if (!_isRenaming)
                _nameLabel.text = runner.Name;
            var node = sim.CurrentGameState.Map.GetNode(runner.CurrentNodeId);
            string nodeName = node?.Name ?? runner.CurrentNodeId ?? "Unknown";
            _stateLabel.text = $"{runner.State} at {nodeName}";
        }

        private void RefreshOverview(Runner runner, GameSimulation sim, SimulationConfig config)
        {
            // Warning banner
            bool hasWarning = runner.ActiveWarning != null;
            _warningLabel.style.display = hasWarning ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasWarning)
                _warningLabel.text = runner.ActiveWarning;

            // Task info
            var taskSeq = sim.GetRunnerTaskSequence(runner);
            if (taskSeq != null)
            {
                string seqName = taskSeq.Name ?? taskSeq.TargetNodeId ?? "Task";
                int stepCount = taskSeq.Steps?.Count ?? 0;
                int currentStepIdx = runner.TaskSequenceCurrentStepIndex;
                string stepDesc = "...";
                if (taskSeq.Steps != null && currentStepIdx >= 0 && currentStepIdx < stepCount)
                    stepDesc = FormatStepDescription(taskSeq.Steps[currentStepIdx], sim);
                _taskInfoLabel.text = $"{seqName}\n{stepDesc} ({currentStepIdx + 1}/{stepCount})";
            }
            else
            {
                _taskInfoLabel.text = "No active task";
            }

            // Travel / Deposit progress (mutually exclusive states, shared bar)
            bool isTraveling = runner.State == RunnerState.Traveling && runner.Travel != null;
            bool isDepositing = runner.State == RunnerState.Depositing && runner.Depositing != null;
            _travelProgressContainer.style.display = (isTraveling || isDepositing) ? DisplayStyle.Flex : DisplayStyle.None;
            if (isTraveling)
            {
                _progressLabel.text = "Travel:";
                _travelProgressBar.value = runner.Travel.Progress * 100f;
                _travelProgressBar.title = $"{runner.Travel.Progress:P0}";
                _travelProgressBar.RemoveFromClassList("depositing");
            }
            else if (isDepositing)
            {
                _progressLabel.text = "Depositing:";
                int total = config.DepositDurationTicks;
                float progress = total > 0 ? 1f - (float)runner.Depositing.TicksRemaining / total : 1f;
                _travelProgressBar.value = progress * 100f;
                _travelProgressBar.title = $"{progress:P0}";
                _travelProgressBar.AddToClassList("depositing");
            }

            // Live stats (contextual based on runner state)
            RefreshLiveStats(runner, sim, config);

            // Skills summary
            _skillsSummaryLabel.text = FormatSkillsSummary(runner);
        }

        // ─── Live Stats ───────────────────────────────────

        private void BuildLiveStatRows()
        {
            (_statRowInventory, _, _statValueInventory) = CreateStatRow("Inventory", "");
            _liveStatsContainer.Add(_statRowInventory);

            (_statRowTravelSpeed, _, _statValueTravelSpeed) = CreateStatRow("Travel Speed", "");
            _liveStatsContainer.Add(_statRowTravelSpeed);
            _uiManager.RegisterTooltip(_statRowTravelSpeed, () => _tooltipTravelSpeed);

            (_statRowEta, _, _statValueEta) = CreateStatRow("ETA", "");
            _liveStatsContainer.Add(_statRowEta);
            _uiManager.RegisterTooltip(_statRowEta, () => _tooltipEta);

            (_statRowTravelXp, _statLabelTravelXp, _statValueTravelXp) = CreateStatRow("Athletics XP/hr", "");
            _liveStatsContainer.Add(_statRowTravelXp);
            _uiManager.RegisterTooltip(_statRowTravelXp, () => _tooltipTravelXp);

            (_statRowGatherSpeed, _, _statValueGatherSpeed) = CreateStatRow("Gather Time", "");
            _liveStatsContainer.Add(_statRowGatherSpeed);
            _uiManager.RegisterTooltip(_statRowGatherSpeed, () => _tooltipGatherSpeed);

            (_statRowGatherXp, _statLabelGatherXp, _statValueGatherXp) = CreateStatRow("", "");
            _liveStatsContainer.Add(_statRowGatherXp);
            _uiManager.RegisterTooltip(_statRowGatherXp, () => _tooltipGatherXp);

            // Pre-create XP bar pool (max bars determined at runtime from config,
            // but we create a reasonable default here — pool grows if needed)
            BuildXpBarPool(4);
        }

        private void RefreshLiveStats(Runner runner, GameSimulation sim, SimulationConfig config)
        {
            // --- Inventory slots (always shown) ---
            int usedSlots = runner.Inventory.Slots.Count;
            int maxSlots = runner.Inventory.MaxSlots;
            _statValueInventory.text = $"{usedSlots} / {maxSlots}";
            _statRowInventory.style.display = DisplayStyle.Flex;

            float tickRate = 1f / sim.TickDeltaTime;
            var athSkill = runner.Skills[(int)SkillType.Athletics];

            // --- Travel Speed (always shown) ---
            float athleticsLevel = runner.GetEffectiveLevel(SkillType.Athletics, config);
            float travelSpeed = config.BaseTravelSpeed + (athleticsLevel - 1) * config.AthleticsSpeedPerLevel;
            _statValueTravelSpeed.text = $"{travelSpeed:F1} m/s";
            _statRowTravelSpeed.style.display = DisplayStyle.Flex;

            if (athSkill.HasPassion)
            {
                _tooltipTravelSpeed = $"Athletics Lv {athSkill.Level} <color=#DCB43C>(P)</color>\n" +
                    $"Effective level: {athleticsLevel:F1}";
            }
            else
            {
                _tooltipTravelSpeed = $"Athletics Lv {athSkill.Level}";
            }

            // --- Traveling-specific stats ---
            bool isTraveling = runner.State == RunnerState.Traveling && runner.Travel != null;
            _statRowEta.style.display = isTraveling ? DisplayStyle.Flex : DisplayStyle.None;
            _statRowTravelXp.style.display = isTraveling ? DisplayStyle.Flex : DisplayStyle.None;

            if (isTraveling)
            {
                float remaining = runner.Travel.TotalDistance - runner.Travel.DistanceCovered;
                float eta = travelSpeed > 0 ? remaining / travelSpeed : 0f;
                _statValueEta.text = $"{eta:F1}s";
                _tooltipEta = $"{runner.Travel.DistanceCovered:F0}m / {runner.Travel.TotalDistance:F0}m";

                float athXpPerTick = athSkill.HasPassion
                    ? config.AthleticsXpPerTick * config.PassionXpMultiplier
                    : config.AthleticsXpPerTick;
                float athXpPerHour = athXpPerTick * tickRate * 3600f;
                _statValueTravelXp.text = $"{athXpPerHour:N0}";
                _tooltipTravelXp = athSkill.HasPassion
                    ? $"Passion: +{(config.PassionXpMultiplier - 1f) * 100f:F0}% XP"
                    : "No passion bonus";
            }

            // --- Gathering-specific stats ---
            bool isGathering = runner.State == RunnerState.Gathering && runner.Gathering != null;
            GatherableConfig gatherConfig = null;
            if (isGathering)
            {
                var node = sim.CurrentGameState.Map.GetNode(runner.Gathering.NodeId);
                if (node != null && runner.Gathering.GatherableIndex < node.Gatherables.Length)
                    gatherConfig = node.Gatherables[runner.Gathering.GatherableIndex];
            }

            bool showGather = isGathering && gatherConfig != null;
            _statRowGatherSpeed.style.display = showGather ? DisplayStyle.Flex : DisplayStyle.None;
            _statRowGatherXp.style.display = showGather ? DisplayStyle.Flex : DisplayStyle.None;

            if (showGather)
            {
                float ticksReq = runner.Gathering.TicksRequired;
                float timePerItem = ticksReq / tickRate;
                _statValueGatherSpeed.text = $"{timePerItem:F1}s";

                float gatherEffLevel = runner.GetEffectiveLevel(gatherConfig.RequiredSkill, config);
                var gatherSkill = runner.Skills[(int)gatherConfig.RequiredSkill];
                string skillName = gatherConfig.RequiredSkill.ToString();

                _tooltipGatherSpeed = gatherSkill.HasPassion
                    ? $"{skillName} Lv {gatherSkill.Level} <color=#DCB43C>(P)</color>\nEffective level: {gatherEffLevel:F1}"
                    : $"{skillName} Lv {gatherSkill.Level}";

                float gatherXpPerTick = gatherSkill.HasPassion
                    ? gatherConfig.XpPerTick * config.PassionXpMultiplier
                    : gatherConfig.XpPerTick;
                float gatherXpPerHour = gatherXpPerTick * tickRate * 3600f;
                _statLabelGatherXp.text = $"{skillName} XP/hr";
                _statValueGatherXp.text = $"{gatherXpPerHour:N0}";
                _tooltipGatherXp = gatherSkill.HasPassion
                    ? $"Passion: +{(config.PassionXpMultiplier - 1f) * 100f:F0}% XP"
                    : "No passion bonus";
            }

            // Skill XP progress bars
            RefreshSkillXpBars(runner);
        }

        // ─── Skill XP Bars ──────────────────────────────

        private void BuildXpBarPool(int count)
        {
            for (int i = _xpBarPool.Count; i < count; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("live-stat-xp-row");
                row.style.display = DisplayStyle.None;

                var label = new Label();
                label.AddToClassList("live-stat-xp-label");
                label.pickingMode = PickingMode.Ignore;
                row.Add(label);

                var bar = new ProgressBar();
                bar.AddToClassList("live-stat-xp-bar");
                row.Add(bar);

                _liveStatsContainer.Add(row);
                _xpBarPool.Add((row, label, bar));
            }
        }

        private void RefreshSkillXpBars(Runner runner)
        {
            int skillCount = SkillTypeExtensions.SkillCount;
            float now = Time.time;
            string runnerId = runner.Id;

            // Get or create per-runner tracking arrays
            if (!_perRunnerSkillXp.TryGetValue(runnerId, out var lastKnownXp))
            {
                lastKnownXp = new float[skillCount];
                var changeTimes = new float[skillCount];
                for (int i = 0; i < skillCount; i++)
                {
                    lastKnownXp[i] = runner.Skills[i].Level * 10000f + runner.Skills[i].Xp;
                    changeTimes[i] = 0f;
                }
                _perRunnerSkillXp[runnerId] = lastKnownXp;
                _perRunnerSkillXpChangeTime[runnerId] = changeTimes;
            }
            var lastChangeTime = _perRunnerSkillXpChangeTime[runnerId];

            // Detect XP changes
            for (int i = 0; i < skillCount; i++)
            {
                float currentXpSignature = runner.Skills[i].Level * 10000f + runner.Skills[i].Xp;
                if (currentXpSignature != lastKnownXp[i])
                {
                    lastKnownXp[i] = currentXpSignature;
                    lastChangeTime[i] = now;
                }
            }

            // Collect skills with recent XP changes, sorted by most recent
            float window = _uiManager.LiveStatsXpDisplayWindowSeconds;
            int maxBars = _uiManager.LiveStatsMaxSkillXpBars;

            // Ensure pool is large enough
            if (_xpBarPool.Count < maxBars)
                BuildXpBarPool(maxBars);

            // Gather eligible skills as (skillIndex, lastChangeTime)
            var eligible = new List<(int skillIndex, float lastChange)>();
            for (int i = 0; i < skillCount; i++)
            {
                if (lastChangeTime[i] > 0f && (now - lastChangeTime[i]) < window)
                    eligible.Add((i, lastChangeTime[i]));
            }
            eligible.Sort((a, b) => b.lastChange.CompareTo(a.lastChange));

            int barCount = System.Math.Min(eligible.Count, maxBars);
            for (int i = 0; i < barCount; i++)
            {
                int si = eligible[i].skillIndex;
                var skill = runner.Skills[si];
                string skillName = ((SkillType)si).ToString();
                string passionMark = skill.HasPassion ? " <color=#DCB43C>P</color>" : "";
                float progress = skill.GetLevelProgress(_uiManager.Simulation.Config) * 100f;

                var (row, label, bar) = _xpBarPool[i];
                label.text = $"{skillName} Lv {skill.Level}{passionMark}";
                bar.value = progress;
                bar.title = $"{progress:F0}%";
                row.style.display = DisplayStyle.Flex;
            }

            // Hide unused bars
            for (int i = barCount; i < _xpBarPool.Count; i++)
                _xpBarPool[i].row.style.display = DisplayStyle.None;
        }

        private static (VisualElement row, Label label, Label value) CreateStatRow(string labelText, string valueText)
        {
            var row = new VisualElement();
            row.AddToClassList("live-stat-row");

            var labelElem = new Label(labelText);
            labelElem.AddToClassList("live-stat-label");
            labelElem.pickingMode = PickingMode.Ignore;
            row.Add(labelElem);

            var valueElem = new Label(valueText);
            valueElem.AddToClassList("live-stat-value");
            valueElem.pickingMode = PickingMode.Ignore;
            row.Add(valueElem);

            return (row, labelElem, valueElem);
        }

        // ─── Inventory Tab ──────────────────────────────

        private void RefreshInventoryTab(Runner runner, GameSimulation sim)
        {
            int usedSlots = runner.Inventory.Slots.Count;
            int maxSlots = runner.Inventory.MaxSlots;
            _inventoryTabSummary.text = $"{usedSlots} / {maxSlots} slots";

            // Update slot visuals
            for (int i = 0; i < _inventorySlots.Count; i++)
            {
                if (i < runner.Inventory.Slots.Count)
                {
                    var slot = runner.Inventory.Slots[i];
                    var itemDef = sim.ItemRegistry?.Get(slot.ItemId);
                    string name = itemDef?.Name ?? slot.ItemId;

                    _inventorySlots[i].AddToClassList("inventory-slot-filled");
                    _slotTooltips[i] = slot.Quantity > 1 ? $"{name} x{slot.Quantity}" : name;

                    // Show icon if available, fall back to text label
                    var iconSprite = _uiManager.GetItemIcon(slot.ItemId);
                    if (iconSprite != null)
                    {
                        _inventorySlotIcons[i].style.backgroundImage = new StyleBackground(iconSprite);
                        _inventorySlotIcons[i].style.display = DisplayStyle.Flex;
                        _inventorySlotLabels[i].style.display = DisplayStyle.None;
                    }
                    else
                    {
                        _inventorySlotIcons[i].style.backgroundImage = StyleKeyword.None;
                        _inventorySlotIcons[i].style.display = DisplayStyle.None;
                        _inventorySlotLabels[i].style.display = DisplayStyle.Flex;
                        _inventorySlotLabels[i].text = name;
                    }

                    _inventorySlotQuantities[i].text = slot.Quantity > 1 ? $"x{slot.Quantity}" : "";
                    _inventorySlotQuantities[i].style.display = DisplayStyle.Flex;
                }
                else
                {
                    _inventorySlots[i].RemoveFromClassList("inventory-slot-filled");
                    _slotTooltips[i] = "";
                    _inventorySlotIcons[i].style.backgroundImage = StyleKeyword.None;
                    _inventorySlotIcons[i].style.display = DisplayStyle.None;
                    _inventorySlotLabels[i].style.display = DisplayStyle.Flex;
                    _inventorySlotLabels[i].text = "";
                    _inventorySlotQuantities[i].text = "";
                }
            }
        }

        // ─── Skills Tab ─────────────────────────────────

        private void RefreshSkillsTab(Runner runner, SimulationConfig config)
        {
            float passionEffMult = config.PassionEffectivenessMultiplier;
            float passionXpMult = config.PassionXpMultiplier;

            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                var skill = runner.Skills[i];
                var skillType = (SkillType)i;
                string skillName = skillType.ToString();
                string desc = skillType.GetDescription();
                float progress = skill.GetLevelProgress(config) * 100f;
                _skillProgressBars[i].value = progress;

                string xpLine = $"XP: {skill.Xp:F0} / {skill.GetXpToNextLevel(config):F0} ({progress:F0}%)";

                if (skill.HasPassion)
                {
                    float effectiveLevel = skill.GetEffectiveLevel(config);
                    _skillLevelLabels[i].text = $"<color=#7CCD7C>{effectiveLevel:F1}</color>";
                    _skillPassionLabels[i].text = "P";

                    _skillRowTooltips[i] = $"<b>{skillName}</b> — {desc}\n" +
                        $"Base Lv {skill.Level}, Effective Lv {effectiveLevel:F1}\n{xpLine}";
                    _passionTooltips[i] = $"Passion increases XP gains by {(passionXpMult - 1f) * 100f:F0}% " +
                        $"and effective level by {(passionEffMult - 1f) * 100f:F0}%.";
                }
                else
                {
                    _skillLevelLabels[i].text = skill.Level.ToString();
                    _skillPassionLabels[i].text = "";

                    _skillRowTooltips[i] = $"<b>{skillName}</b> — {desc}\nLv {skill.Level}\n{xpLine}";
                    _passionTooltips[i] = "";
                }
            }
        }


        // ─── Build helpers ──────────────────────────────

        private void BuildSkillRows()
        {
            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                var skillType = (SkillType)i;

                var row = new VisualElement();
                row.AddToClassList("skill-row");

                var nameLabel = new Label(skillType.ToString());
                nameLabel.AddToClassList("skill-name");
                nameLabel.pickingMode = PickingMode.Ignore;
                row.Add(nameLabel);

                var levelLabel = new Label("1");
                levelLabel.AddToClassList("skill-level");
                levelLabel.enableRichText = true;
                levelLabel.pickingMode = PickingMode.Ignore;
                row.Add(levelLabel);
                _skillLevelLabels[i] = levelLabel;

                var passionLabel = new Label("");
                passionLabel.AddToClassList("skill-passion");
                row.Add(passionLabel);
                _skillPassionLabels[i] = passionLabel;

                var progressBar = new ProgressBar();
                progressBar.AddToClassList("skill-xp-bar");
                progressBar.lowValue = 0f;
                progressBar.highValue = 100f;
                row.Add(progressBar);
                _skillProgressBars[i] = progressBar;

                _skillsList.Add(row);

                // Tooltip on skill row (updated during refresh)
                int idx = i;
                _skillRowTooltips[i] = "";
                _uiManager.RegisterTooltip(row, () => _skillRowTooltips[idx]);

                // Tooltip on passion P (updated during refresh)
                _passionTooltips[i] = "";
                _uiManager.RegisterTooltip(passionLabel, () => _passionTooltips[idx]);
            }
        }

        private void BuildInventoryGrid()
        {
            // Build max slots (read from config default — 28 OSRS-style)
            int maxSlots = _uiManager.Simulation?.Config?.InventorySize ?? 28;

            for (int i = 0; i < maxSlots; i++)
            {
                var slot = new VisualElement();
                slot.AddToClassList("inventory-slot");

                var icon = new VisualElement();
                icon.AddToClassList("inventory-slot-icon");
                icon.pickingMode = PickingMode.Ignore;
                slot.Add(icon);

                var label = new Label("");
                label.AddToClassList("inventory-slot-label");
                label.pickingMode = PickingMode.Ignore;
                slot.Add(label);

                var quantity = new Label("");
                quantity.AddToClassList("inventory-slot-quantity");
                quantity.pickingMode = PickingMode.Ignore;
                slot.Add(quantity);

                _inventoryGrid.Add(slot);
                _inventorySlots.Add(slot);
                _inventorySlotIcons.Add(icon);
                _inventorySlotLabels.Add(label);
                _inventorySlotQuantities.Add(quantity);

                // Tooltip reads from the slot's current tooltip field (set during refresh)
                int slotIndex = i;
                _uiManager.RegisterTooltip(slot, () => _slotTooltips[slotIndex]);
            }
        }

        // ─── Utility ────────────────────────────────────

        private static string FormatStepDescription(Simulation.Automation.TaskStep step, GameSimulation sim)
        {
            switch (step.Type)
            {
                case Simulation.Automation.TaskStepType.TravelTo:
                    var node = sim.CurrentGameState.Map.GetNode(step.TargetNodeId);
                    string nodeName = node?.Name ?? step.TargetNodeId ?? "?";
                    return $"Traveling to {nodeName}";
                case Simulation.Automation.TaskStepType.Work:
                    return "Working";
                case Simulation.Automation.TaskStepType.Deposit:
                    return "Depositing";
                default:
                    return step.Type.ToString();
            }
        }

        private string FormatSkillsSummary(Runner runner)
        {
            var config = _uiManager.Simulation?.Config;

            // Show top 5 skills by effective level, with passion indicator
            var skills = new List<(string name, int level, float effectiveLevel, bool passion)>();
            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                var skill = runner.Skills[i];
                float eff = config != null ? skill.GetEffectiveLevel(config) : skill.Level;
                skills.Add((((SkillType)i).ToString(), skill.Level, eff, skill.HasPassion));
            }

            skills.Sort((a, b) => b.effectiveLevel.CompareTo(a.effectiveLevel));

            // If everything is level 1, just say so
            if (skills[0].level <= 1) return "All skills level 1";

            int count = System.Math.Min(5, skills.Count);
            var top = skills.GetRange(0, count);
            return string.Join(", ", top.Select(s =>
                s.passion
                    ? $"{s.name} <color=#7CCD7C>{s.effectiveLevel:F1}</color> <color=#DCB43C>P</color>"
                    : $"{s.name} {s.level}"));
        }
    }
}

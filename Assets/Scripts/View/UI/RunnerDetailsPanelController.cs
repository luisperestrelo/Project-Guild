using System.Collections.Generic;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Core;

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
    public class RunnerDetailsPanelController
    {
        private readonly UIManager _uiManager;
        private readonly VisualElement _root;

        // ─── Tab switching ──────────────────────────────
        private readonly Button _tabOverview;
        private readonly Button _tabInventory;
        private readonly Button _tabSkills;
        private readonly ScrollView _contentOverview;
        private readonly ScrollView _contentInventory;
        private readonly ScrollView _contentSkills;
        private string _activeTab = "overview";

        // ─── Overview elements ──────────────────────────
        private readonly Label _nameLabel;
        private readonly Label _stateLabel;
        private readonly Label _taskInfoLabel;
        private readonly VisualElement _travelProgressContainer;
        private readonly ProgressBar _travelProgressBar;
        private readonly Label _inventorySummaryLabel;
        private readonly VisualElement _inventoryItemsContainer;
        private readonly Label _skillsSummaryLabel;

        // ─── Skills tab elements ────────────────────────
        private readonly VisualElement _skillsList;
        private readonly Label[] _skillLevelLabels;
        private readonly Label[] _skillPassionLabels;
        private readonly ProgressBar[] _skillProgressBars;

        // ─── Inventory tab elements ─────────────────────
        private readonly Label _inventoryTabSummary;
        private readonly VisualElement _inventoryGrid;
        private readonly List<VisualElement> _inventorySlots = new();
        private readonly List<Label> _inventorySlotLabels = new();
        private readonly List<Label> _inventorySlotQuantities = new();

        private string _currentRunnerId;

        public RunnerDetailsPanelController(VisualElement root, UIManager uiManager)
        {
            _root = root;
            _uiManager = uiManager;

            // ─── Tab buttons ────────────────────────────
            _tabOverview = root.Q<Button>("tab-overview");
            _tabInventory = root.Q<Button>("tab-inventory");
            _tabSkills = root.Q<Button>("tab-skills");

            _tabOverview.clicked += () => SwitchTab("overview");
            _tabInventory.clicked += () => SwitchTab("inventory");
            _tabSkills.clicked += () => SwitchTab("skills");

            // ─── Tab content panels ─────────────────────
            _contentOverview = root.Q<ScrollView>("tab-content-overview");
            _contentInventory = root.Q<ScrollView>("tab-content-inventory");
            _contentSkills = root.Q<ScrollView>("tab-content-skills");

            // ─── Overview elements ──────────────────────
            _nameLabel = root.Q<Label>("runner-name-label");
            _stateLabel = root.Q<Label>("runner-state-label");
            _taskInfoLabel = root.Q<Label>("task-info-label");
            _travelProgressContainer = root.Q("travel-progress-container");
            _travelProgressBar = root.Q<ProgressBar>("travel-progress-bar");
            _inventorySummaryLabel = root.Q<Label>("inventory-summary-label");
            _inventoryItemsContainer = root.Q("inventory-items-container");
            _skillsSummaryLabel = root.Q<Label>("skills-summary-label");

            // ─── Skills tab ─────────────────────────────
            _skillsList = root.Q("skills-list");
            _skillLevelLabels = new Label[SkillTypeExtensions.SkillCount];
            _skillPassionLabels = new Label[SkillTypeExtensions.SkillCount];
            _skillProgressBars = new ProgressBar[SkillTypeExtensions.SkillCount];
            BuildSkillRows();

            // ─── Inventory tab ──────────────────────────
            _inventoryTabSummary = root.Q<Label>("inventory-tab-summary");
            _inventoryGrid = root.Q("inventory-grid");
            BuildInventoryGrid();
        }

        private void SwitchTab(string tabName)
        {
            _activeTab = tabName;

            // Toggle tab-active / remove from all buttons
            SetTabActive(_tabOverview, tabName == "overview");
            SetTabActive(_tabInventory, tabName == "inventory");
            SetTabActive(_tabSkills, tabName == "skills");

            // Show/hide content panels
            _contentOverview.style.display = tabName == "overview" ? DisplayStyle.Flex : DisplayStyle.None;
            _contentInventory.style.display = tabName == "inventory" ? DisplayStyle.Flex : DisplayStyle.None;
            _contentSkills.style.display = tabName == "skills" ? DisplayStyle.Flex : DisplayStyle.None;

            Refresh();
        }

        private static void SetTabActive(Button tab, bool active)
        {
            if (active)
                tab.AddToClassList("tab-active");
            else
                tab.RemoveFromClassList("tab-active");
        }

        public void ShowRunner(string runnerId)
        {
            _currentRunnerId = runnerId;
            Refresh();
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
            }
        }

        // ─── Overview ───────────────────────────────────

        private void RefreshOverviewHeader(Runner runner, GameSimulation sim)
        {
            _nameLabel.text = runner.Name;
            var node = sim.CurrentGameState.Map.GetNode(runner.CurrentNodeId);
            string nodeName = node?.Name ?? runner.CurrentNodeId ?? "Unknown";
            _stateLabel.text = $"{runner.State} at {nodeName}";
        }

        private void RefreshOverview(Runner runner, GameSimulation sim, SimulationConfig config)
        {
            // Task info
            var taskSeq = sim.GetRunnerTaskSequence(runner);
            if (taskSeq != null)
            {
                string seqName = taskSeq.Name ?? taskSeq.TargetNodeId ?? "Task";
                int stepCount = taskSeq.Steps?.Count ?? 0;
                int currentStep = runner.TaskSequenceCurrentStepIndex + 1;
                _taskInfoLabel.text = $"{seqName} (step {currentStep}/{stepCount})";
            }
            else
            {
                _taskInfoLabel.text = "No active task";
            }

            // Travel progress
            bool isTraveling = runner.State == RunnerState.Traveling && runner.Travel != null;
            _travelProgressContainer.style.display = isTraveling ? DisplayStyle.Flex : DisplayStyle.None;
            if (isTraveling)
            {
                _travelProgressBar.value = runner.Travel.Progress * 100f;
                _travelProgressBar.title = $"{runner.Travel.Progress:P0}";
            }

            // Inventory summary
            int usedSlots = runner.Inventory.Slots.Count;
            int maxSlots = runner.Inventory.MaxSlots;
            _inventorySummaryLabel.text = $"{usedSlots} / {maxSlots} slots";

            // Inventory items (brief list)
            _inventoryItemsContainer.Clear();
            var counts = AggregateInventory(runner, sim);
            foreach (var kvp in counts)
            {
                var row = new VisualElement();
                row.AddToClassList("inventory-item-row");
                row.pickingMode = PickingMode.Ignore;

                var nameLabel = new Label(kvp.Key);
                nameLabel.AddToClassList("inventory-item-name");
                nameLabel.pickingMode = PickingMode.Ignore;
                row.Add(nameLabel);

                var countLabel = new Label($"x{kvp.Value}");
                countLabel.AddToClassList("inventory-item-count");
                countLabel.pickingMode = PickingMode.Ignore;
                row.Add(countLabel);

                _inventoryItemsContainer.Add(row);
            }

            // Skills summary (top 3 non-level-1 skills, or "All skills level 1")
            _skillsSummaryLabel.text = FormatSkillsSummary(runner);
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
                    _inventorySlotLabels[i].text = name;
                    _inventorySlotQuantities[i].text = slot.Quantity > 1 ? $"x{slot.Quantity}" : "";
                    _inventorySlotQuantities[i].style.display = DisplayStyle.Flex;
                }
                else
                {
                    _inventorySlots[i].RemoveFromClassList("inventory-slot-filled");
                    _inventorySlotLabels[i].text = "";
                    _inventorySlotQuantities[i].text = "";
                }
            }
        }

        // ─── Skills Tab ─────────────────────────────────

        private void RefreshSkillsTab(Runner runner, SimulationConfig config)
        {
            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                var skill = runner.Skills[i];
                _skillLevelLabels[i].text = skill.Level.ToString();
                _skillPassionLabels[i].text = skill.HasPassion ? "P" : "";
                _skillProgressBars[i].value = skill.GetLevelProgress(config) * 100f;
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
                row.pickingMode = PickingMode.Ignore;

                var nameLabel = new Label(skillType.ToString());
                nameLabel.AddToClassList("skill-name");
                row.Add(nameLabel);

                var levelLabel = new Label("1");
                levelLabel.AddToClassList("skill-level");
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
                _inventorySlotLabels.Add(label);
                _inventorySlotQuantities.Add(quantity);
            }
        }

        // ─── Utility ────────────────────────────────────

        private static Dictionary<string, int> AggregateInventory(Runner runner, GameSimulation sim)
        {
            var result = new Dictionary<string, int>();
            foreach (var slot in runner.Inventory.Slots)
            {
                var itemDef = sim.ItemRegistry?.Get(slot.ItemId);
                string name = itemDef?.Name ?? slot.ItemId;
                if (!result.ContainsKey(name))
                    result[name] = 0;
                result[name] += slot.Quantity;
            }
            return result;
        }

        private static string FormatSkillsSummary(Runner runner)
        {
            var notable = new List<string>();
            for (int i = 0; i < SkillTypeExtensions.SkillCount; i++)
            {
                var skill = runner.Skills[i];
                if (skill.Level > 1)
                    notable.Add($"{(SkillType)i} {skill.Level}");
            }

            if (notable.Count == 0) return "All skills level 1";
            if (notable.Count <= 3) return string.Join(", ", notable);
            return string.Join(", ", notable.GetRange(0, 3)) + $" (+{notable.Count - 3} more)";
        }
    }
}

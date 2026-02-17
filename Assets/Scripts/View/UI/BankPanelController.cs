using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Controller for the Guild Bank overlay panel. OSRS-style bank view:
    /// large grid of stacked items with search and category filtering.
    /// Follows AutomationPanelController pattern (Open/Close/Toggle/IsOpen).
    /// Refreshes on Open() and on RunnerDeposited events while open.
    /// </summary>
    public class BankPanelController
    {
        private readonly VisualElement _root;
        private readonly UIManager _uiManager;
        private readonly TextField _searchField;
        private readonly VisualElement _categoryTabs;
        private readonly VisualElement _itemGrid;
        private readonly Label _totalLabel;

        private string _searchFilter = "";
        private ItemCategory? _categoryFilter;

        // Persistent-element cache keyed by item ID
        private readonly Dictionary<string, (VisualElement slot, VisualElement icon, Label label, Label quantity)> _slotCache = new();
        private string _lastShapeKey = "";

        // Category tab buttons
        private readonly List<(Button button, ItemCategory? category)> _categoryButtons = new();

        public bool IsOpen { get; private set; }

        public BankPanelController(VisualElement root, UIManager uiManager)
        {
            _root = root;
            _uiManager = uiManager;

            // Close button
            root.Q<Button>("btn-close-bank").clicked += Close;

            // Search field
            _searchField = root.Q<TextField>("bank-search-field");
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _searchFilter = evt.newValue ?? "";
                RebuildGrid();
            });

            _categoryTabs = root.Q("bank-category-tabs");
            _itemGrid = root.Q("bank-item-grid");
            _totalLabel = root.Q<Label>("bank-total-label");

            // Escape to close
            _root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    Close();
                    evt.StopPropagation();
                }
            });

            // Subscribe to deposit events for live refresh
            uiManager.Simulation?.Events.Subscribe<RunnerDeposited>(OnDeposited);

            // Start hidden
            _root.style.display = DisplayStyle.None;
        }

        public void Open()
        {
            IsOpen = true;
            _root.style.display = DisplayStyle.Flex;
            _root.Focus();
            BuildCategoryTabs();
            _lastShapeKey = ""; // force rebuild
            RebuildGrid();
        }

        public void Close()
        {
            IsOpen = false;
            _root.style.display = DisplayStyle.None;
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        private void OnDeposited(RunnerDeposited evt)
        {
            if (IsOpen)
                RebuildGrid();
        }

        // ─── Category Tabs ──────────────────────────────

        private void BuildCategoryTabs()
        {
            _categoryTabs.Clear();
            _categoryButtons.Clear();

            var sim = _uiManager.Simulation;
            if (sim == null) return;

            // Determine which categories have items in the bank
            var categoriesWithItems = new HashSet<ItemCategory>();
            foreach (var stack in sim.CurrentGameState.Bank.Stacks)
            {
                var def = sim.ItemRegistry?.Get(stack.ItemId);
                if (def != null)
                    categoriesWithItems.Add(def.Category);
            }

            // "All" tab
            var allBtn = new Button(() => SetCategoryFilter(null));
            allBtn.text = "All";
            allBtn.AddToClassList("bank-category-tab");
            if (_categoryFilter == null)
                allBtn.AddToClassList("bank-category-tab-active");
            _categoryTabs.Add(allBtn);
            _categoryButtons.Add((allBtn, null));

            // One tab per category that has items
            foreach (ItemCategory cat in System.Enum.GetValues(typeof(ItemCategory)))
            {
                if (!categoriesWithItems.Contains(cat)) continue;

                var catBtn = new Button(() => SetCategoryFilter(cat));
                catBtn.text = cat.ToString();
                catBtn.AddToClassList("bank-category-tab");
                if (_categoryFilter.HasValue && _categoryFilter.Value == cat)
                    catBtn.AddToClassList("bank-category-tab-active");
                _categoryTabs.Add(catBtn);
                _categoryButtons.Add((catBtn, cat));
            }
        }

        private void SetCategoryFilter(ItemCategory? category)
        {
            _categoryFilter = category;

            // Update active tab styling
            foreach (var (button, cat) in _categoryButtons)
            {
                if (cat == _categoryFilter)
                    button.AddToClassList("bank-category-tab-active");
                else
                    button.RemoveFromClassList("bank-category-tab-active");
            }

            _lastShapeKey = ""; // force rebuild
            RebuildGrid();
        }

        // ─── Item Grid ──────────────────────────────────

        private void RebuildGrid()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            // Collect and filter bank stacks
            var filteredStacks = new List<(ItemStack stack, ItemDefinition def)>();
            foreach (var stack in sim.CurrentGameState.Bank.Stacks)
            {
                var def = sim.ItemRegistry?.Get(stack.ItemId);
                if (def == null) continue;

                // Category filter
                if (_categoryFilter.HasValue && def.Category != _categoryFilter.Value)
                    continue;

                // Search filter
                if (!string.IsNullOrEmpty(_searchFilter) &&
                    !def.Name.Contains(_searchFilter, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                filteredStacks.Add((stack, def));
            }

            // Sort by category then name
            filteredStacks.Sort((a, b) =>
            {
                int catCmp = a.def.Category.CompareTo(b.def.Category);
                return catCmp != 0 ? catCmp : string.Compare(a.def.Name, b.def.Name, System.StringComparison.Ordinal);
            });

            // Build shape key
            string shapeKey = string.Join("|", filteredStacks.Select(s => s.stack.ItemId));

            if (shapeKey == _lastShapeKey)
            {
                // Shape unchanged — update quantities in-place
                foreach (var (stack, def) in filteredStacks)
                {
                    if (_slotCache.TryGetValue(stack.ItemId, out var cached))
                        cached.quantity.text = stack.Quantity.ToString();
                }
            }
            else
            {
                // Shape changed — rebuild grid
                _itemGrid.Clear();
                _slotCache.Clear();
                _lastShapeKey = shapeKey;

                foreach (var (stack, def) in filteredStacks)
                {
                    var slot = new VisualElement();
                    slot.AddToClassList("bank-slot");

                    // Icon or text fallback
                    var iconSprite = _uiManager.GetItemIcon(stack.ItemId);
                    VisualElement iconElem;
                    Label labelElem;

                    if (iconSprite != null)
                    {
                        iconElem = new VisualElement();
                        iconElem.AddToClassList("bank-slot-icon");
                        iconElem.style.backgroundImage = new StyleBackground(iconSprite);
                        iconElem.pickingMode = PickingMode.Ignore;
                        slot.Add(iconElem);

                        labelElem = null; // no text label needed with icon
                    }
                    else
                    {
                        iconElem = null;
                        labelElem = new Label(def.Name);
                        labelElem.AddToClassList("bank-slot-label");
                        labelElem.pickingMode = PickingMode.Ignore;
                        slot.Add(labelElem);
                    }

                    var quantityLabel = new Label(stack.Quantity.ToString());
                    quantityLabel.AddToClassList("bank-slot-quantity");
                    quantityLabel.pickingMode = PickingMode.Ignore;
                    slot.Add(quantityLabel);

                    // Tooltip
                    string itemName = def.Name;
                    string itemCategory = def.Category.ToString();
                    string itemId = stack.ItemId;
                    _uiManager.RegisterTooltip(slot, () =>
                    {
                        // Re-read quantity from bank for fresh data
                        var currentSim = _uiManager.Simulation;
                        int qty = currentSim?.CurrentGameState.Bank.CountItem(itemId) ?? 0;
                        return $"{itemName} x{qty}\n{itemCategory}";
                    });

                    _itemGrid.Add(slot);
                    _slotCache[stack.ItemId] = (slot, iconElem, labelElem, quantityLabel);
                }
            }

            // Update footer
            int totalItems = sim.CurrentGameState.Bank.Stacks.Sum(s => s.Quantity);
            int totalTypes = sim.CurrentGameState.Bank.Stacks.Count;
            if (filteredStacks.Count < totalTypes)
                _totalLabel.text = $"Showing {filteredStacks.Count} of {totalTypes} item types ({totalItems:N0} total)";
            else
                _totalLabel.text = $"{totalTypes} item types ({totalItems:N0} total)";
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Items;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Always-visible left-side panel showing guild bank totals grouped by ItemCategory.
    /// Rimworld-style: collapsible category headers, compact item rows with quantity.
    /// Refreshes every tick — updates quantities in-place, rebuilds only when item set changes.
    /// </summary>
    public class ResourceBarController : ITickRefreshable
    {
        private readonly VisualElement _root;
        private readonly UIManager _uiManager;

        // Collapsed categories
        private readonly HashSet<ItemCategory> _collapsedCategories = new();

        // Shape-keyed caching: rebuild DOM only when item set changes
        private string _lastShapeKey = "";

        // Per-item quantity labels for in-place updates
        private readonly Dictionary<string, Label> _quantityLabels = new();

        // Category header elements for collapse toggle
        private readonly Dictionary<ItemCategory, (VisualElement header, VisualElement itemsContainer)> _categoryElements = new();

        public ResourceBarController(VisualElement root, UIManager uiManager)
        {
            _root = root;
            _uiManager = uiManager;

            uiManager.RegisterTickRefreshable(this);
        }

        public void Refresh()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var bank = sim.CurrentGameState.Bank;
            if (bank.Stacks.Count == 0)
            {
                _root.style.display = DisplayStyle.None;
                return;
            }

            _root.style.display = DisplayStyle.Flex;

            // Group items by category
            var grouped = new SortedDictionary<ItemCategory, List<(ItemStack stack, ItemDefinition def)>>();
            foreach (var stack in bank.Stacks)
            {
                var def = sim.ItemRegistry?.Get(stack.ItemId);
                if (def == null) continue;

                if (!grouped.TryGetValue(def.Category, out var list))
                {
                    list = new List<(ItemStack, ItemDefinition)>();
                    grouped[def.Category] = list;
                }
                list.Add((stack, def));
            }

            // Sort items within each category by name
            foreach (var list in grouped.Values)
                list.Sort((a, b) => string.Compare(a.def.Name, b.def.Name, System.StringComparison.Ordinal));

            // Build shape key: categories + item IDs
            string shapeKey = string.Join("|",
                grouped.SelectMany(g => g.Value.Select(v => $"{g.Key}:{v.stack.ItemId}")));

            if (shapeKey == _lastShapeKey)
            {
                // Shape unchanged — update quantities in-place
                foreach (var (_, items) in grouped)
                {
                    foreach (var (stack, _) in items)
                    {
                        if (_quantityLabels.TryGetValue(stack.ItemId, out var qtyLabel))
                            qtyLabel.text = FormatQuantity(stack.Quantity);
                    }
                }
                return;
            }

            // Shape changed — full rebuild
            _root.Clear();
            _quantityLabels.Clear();
            _categoryElements.Clear();
            _lastShapeKey = shapeKey;

            foreach (var (category, items) in grouped)
            {
                // Category header
                var header = new VisualElement();
                header.AddToClassList("resource-category-header");

                var arrow = new Label(_collapsedCategories.Contains(category) ? "+" : "-");
                arrow.AddToClassList("resource-category-arrow");
                arrow.pickingMode = PickingMode.Ignore;
                header.Add(arrow);

                var catLabel = new Label(category.ToString());
                catLabel.AddToClassList("resource-category-name");
                catLabel.pickingMode = PickingMode.Ignore;
                header.Add(catLabel);

                _root.Add(header);

                // Items container
                var itemsContainer = new VisualElement();
                itemsContainer.AddToClassList("resource-items-container");

                if (_collapsedCategories.Contains(category))
                    itemsContainer.style.display = DisplayStyle.None;

                // Click header to toggle collapse
                ItemCategory capturedCat = category;
                VisualElement capturedArrow = arrow;
                header.RegisterCallback<ClickEvent>(_ =>
                {
                    if (_collapsedCategories.Contains(capturedCat))
                    {
                        _collapsedCategories.Remove(capturedCat);
                        itemsContainer.style.display = DisplayStyle.Flex;
                        ((Label)capturedArrow).text = "-";
                    }
                    else
                    {
                        _collapsedCategories.Add(capturedCat);
                        itemsContainer.style.display = DisplayStyle.None;
                        ((Label)capturedArrow).text = "+";
                    }
                });

                foreach (var (stack, def) in items)
                {
                    var row = new VisualElement();
                    row.AddToClassList("resource-item-row");

                    var nameLabel = new Label(def.Name);
                    nameLabel.AddToClassList("resource-item-name");
                    nameLabel.pickingMode = PickingMode.Ignore;
                    row.Add(nameLabel);

                    var qtyLabel = new Label(FormatQuantity(stack.Quantity));
                    qtyLabel.AddToClassList("resource-item-quantity");
                    qtyLabel.pickingMode = PickingMode.Ignore;
                    row.Add(qtyLabel);

                    _quantityLabels[stack.ItemId] = qtyLabel;

                    // Click item to open bank panel
                    row.RegisterCallback<ClickEvent>(_ => _uiManager.OpenBankPanel());

                    // Tooltip
                    string itemName = def.Name;
                    string itemId = stack.ItemId;
                    string catName = category.ToString();
                    _uiManager.RegisterTooltip(row, () =>
                    {
                        int qty = _uiManager.Simulation?.CurrentGameState.Bank.CountItem(itemId) ?? 0;
                        return $"{itemName} x{qty}\n{catName}";
                    });

                    itemsContainer.Add(row);
                }

                _root.Add(itemsContainer);
                _categoryElements[category] = (header, itemsContainer);
            }
        }

        private static string FormatQuantity(int qty)
        {
            if (qty >= 1_000_000) return $"{qty / 1_000_000f:F1}M";
            if (qty >= 10_000) return $"{qty / 1_000f:F1}K";
            return qty.ToString("N0");
        }
    }
}

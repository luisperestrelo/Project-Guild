using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Core;
using ProjectGuild.Simulation.Crafting;
using ProjectGuild.Simulation.Items;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Throwaway crafting UI for demo/tutorial. Built entirely in code, no UXML.
    /// Shows recipes for Engineering and Alchemy, lets player craft items
    /// and equip them on runners.
    /// </summary>
    public class CraftingPanelController
    {
        private readonly VisualElement _root;
        private readonly UIManager _uiManager;
        private readonly VisualElement _panelRoot;
        private readonly VisualElement _recipeList;
        private readonly Label _stationLabel;
        private readonly VisualElement _equipSection;
        private readonly VisualElement _equipList;
        private CraftingStation _currentStation = CraftingStation.Engineering;

        public bool IsOpen { get; private set; }

        public CraftingPanelController(VisualElement parentRoot, UIManager uiManager)
        {
            _uiManager = uiManager;

            // Build the entire panel in code
            _root = new VisualElement();
            _root.name = "crafting-panel-overlay";
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.top = 0;
            _root.style.right = 0;
            _root.style.bottom = 0;
            _root.pickingMode = PickingMode.Ignore;

            _panelRoot = new VisualElement();
            _panelRoot.name = "crafting-panel-root";
            _panelRoot.style.position = Position.Absolute;
            _panelRoot.style.left = new Length(50, LengthUnit.Percent);
            _panelRoot.style.top = new Length(50, LengthUnit.Percent);
            _panelRoot.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            _panelRoot.style.width = 500;
            _panelRoot.style.maxHeight = 600;
            _panelRoot.style.backgroundColor = new Color(0.12f, 0.12f, 0.15f, 0.95f);
            _panelRoot.style.borderTopLeftRadius = 8;
            _panelRoot.style.borderTopRightRadius = 8;
            _panelRoot.style.borderBottomLeftRadius = 8;
            _panelRoot.style.borderBottomRightRadius = 8;
            _panelRoot.style.borderTopWidth = 2;
            _panelRoot.style.borderBottomWidth = 2;
            _panelRoot.style.borderLeftWidth = 2;
            _panelRoot.style.borderRightWidth = 2;
            _panelRoot.style.borderTopColor = new Color(0.4f, 0.35f, 0.2f);
            _panelRoot.style.borderBottomColor = new Color(0.4f, 0.35f, 0.2f);
            _panelRoot.style.borderLeftColor = new Color(0.4f, 0.35f, 0.2f);
            _panelRoot.style.borderRightColor = new Color(0.4f, 0.35f, 0.2f);
            _panelRoot.style.paddingTop = 12;
            _panelRoot.style.paddingBottom = 12;
            _panelRoot.style.paddingLeft = 16;
            _panelRoot.style.paddingRight = 16;
            _panelRoot.focusable = true;
            _root.Add(_panelRoot);

            // Title bar
            var titleBar = new VisualElement();
            titleBar.style.flexDirection = FlexDirection.Row;
            titleBar.style.justifyContent = Justify.SpaceBetween;
            titleBar.style.marginBottom = 8;

            _stationLabel = new Label("Engineering Station");
            _stationLabel.style.fontSize = 18;
            _stationLabel.style.color = new Color(0.9f, 0.8f, 0.4f);
            _stationLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleBar.Add(_stationLabel);

            var closeBtn = new Button(() => Close()) { text = "X" };
            closeBtn.style.width = 28;
            closeBtn.style.height = 28;
            closeBtn.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
            closeBtn.style.color = Color.white;
            titleBar.Add(closeBtn);
            _panelRoot.Add(titleBar);

            // Station tabs
            var tabRow = new VisualElement();
            tabRow.style.flexDirection = FlexDirection.Row;
            tabRow.style.marginBottom = 8;

            var engBtn = new Button(() => SwitchStation(CraftingStation.Engineering)) { text = "Engineering" };
            engBtn.style.flexGrow = 1;
            engBtn.style.marginRight = 4;
            tabRow.Add(engBtn);

            var alchBtn = new Button(() => SwitchStation(CraftingStation.Alchemy)) { text = "Alchemy" };
            alchBtn.style.flexGrow = 1;
            tabRow.Add(alchBtn);

            _panelRoot.Add(tabRow);

            // Recipe list
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1;
            scrollView.style.maxHeight = 300;

            _recipeList = new VisualElement();
            scrollView.Add(_recipeList);
            _panelRoot.Add(scrollView);

            // Equipment section
            var equipHeader = new Label("Equipment in Bank (click to equip on selected runner)");
            equipHeader.style.fontSize = 12;
            equipHeader.style.color = new Color(0.7f, 0.7f, 0.7f);
            equipHeader.style.marginTop = 12;
            equipHeader.style.marginBottom = 4;
            _panelRoot.Add(equipHeader);

            var equipScroll = new ScrollView(ScrollViewMode.Vertical);
            equipScroll.style.maxHeight = 150;

            _equipList = new VisualElement();
            equipScroll.Add(_equipList);
            _panelRoot.Add(equipScroll);

            // Escape to close
            _panelRoot.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    Close();
                    evt.StopPropagation();
                }
            });

            parentRoot.Add(_root);
            _root.style.display = DisplayStyle.None;

            // Subscribe to crafting events
            uiManager.Simulation?.Events.Subscribe<CraftingCompleted>(e => { if (IsOpen) Refresh(); });
        }

        public void Open()
        {
            CraftingRecipeRegistry.Initialize();
            IsOpen = true;
            _root.style.display = DisplayStyle.Flex;
            _panelRoot.Focus();
            Refresh();
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

        private void SwitchStation(CraftingStation station)
        {
            _currentStation = station;
            _stationLabel.text = station == CraftingStation.Engineering
                ? "Engineering Station"
                : "Alchemy Lab (using consumables not yet available)";
            Refresh();
        }

        private void Refresh()
        {
            _recipeList.Clear();
            _equipList.Clear();

            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var bank = sim.CurrentGameState.Bank;

            // Build recipe rows
            foreach (var recipe in CraftingRecipeRegistry.GetAllForStation(_currentStation))
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 4;
                row.style.paddingTop = 6;
                row.style.paddingBottom = 6;
                row.style.paddingLeft = 8;
                row.style.paddingRight = 8;
                row.style.backgroundColor = new Color(0.18f, 0.18f, 0.22f);
                row.style.borderTopLeftRadius = 4;
                row.style.borderTopRightRadius = 4;
                row.style.borderBottomLeftRadius = 4;
                row.style.borderBottomRightRadius = 4;

                // Recipe info
                var infoCol = new VisualElement();
                infoCol.style.flexGrow = 1;

                var nameLabel = new Label(recipe.Name);
                nameLabel.style.fontSize = 14;
                nameLabel.style.color = Color.white;
                nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                infoCol.Add(nameLabel);

                // Ingredients
                var ingText = "";
                bool canCraft = true;
                foreach (var ing in recipe.Ingredients)
                {
                    int have = bank.CountItem(ing.ItemId);
                    string itemName = sim.ItemRegistry?.Get(ing.ItemId)?.Name ?? ing.ItemId;
                    bool enough = have >= ing.Quantity;
                    if (!enough) canCraft = false;
                    string color = enough ? "#88cc88" : "#cc4444";
                    ingText += $"<color={color}>{itemName}: {have}/{ing.Quantity}</color>  ";
                }

                var ingLabel = new Label(ingText);
                ingLabel.enableRichText = true;
                ingLabel.style.fontSize = 11;
                ingLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                infoCol.Add(ingLabel);

                // Stats preview
                if (recipe.EquipmentStats != null && recipe.EquipmentStats.Count > 0)
                {
                    var statsText = "";
                    foreach (var kvp in recipe.EquipmentStats)
                        statsText += $"+{kvp.Value} {kvp.Key}  ";
                    var statsLabel = new Label(statsText);
                    statsLabel.style.fontSize = 10;
                    statsLabel.style.color = new Color(0.5f, 0.7f, 1f);
                    infoCol.Add(statsLabel);
                }

                row.Add(infoCol);

                // Craft button — instant for demo, no runner needed
                string capturedRecipeId = recipe.Id;
                var craftBtn = new Button(() =>
                {
                    sim.InstantCraft(capturedRecipeId);
                    Refresh();
                }) { text = "Craft" };
                craftBtn.style.width = 60;
                craftBtn.style.height = 28;
                craftBtn.SetEnabled(canCraft);
                if (canCraft)
                {
                    craftBtn.style.backgroundColor = new Color(0.2f, 0.5f, 0.2f);
                    craftBtn.style.color = Color.white;
                }
                row.Add(craftBtn);

                _recipeList.Add(row);
            }

            // Build equipment list (gear items in bank)
            foreach (var stack in bank.Stacks)
            {
                var itemDef = sim.ItemRegistry?.Get(stack.ItemId);
                if (itemDef == null || itemDef.Category != ItemCategory.Gear) continue;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2;
                row.style.paddingTop = 4;
                row.style.paddingBottom = 4;
                row.style.paddingLeft = 8;
                row.style.backgroundColor = new Color(0.15f, 0.15f, 0.2f);

                var label = new Label($"{itemDef.Name} x{stack.Quantity}");
                label.style.flexGrow = 1;
                label.style.color = new Color(0.8f, 0.8f, 0.6f);
                label.style.fontSize = 12;
                row.Add(label);

                string capturedItemId = stack.ItemId;
                var equipBtn = new Button(() =>
                {
                    var selectedRunner = _uiManager.GetSelectedRunner();
                    if (selectedRunner != null)
                    {
                        sim.CommandEquipFromBank(selectedRunner.Id, capturedItemId);
                        Refresh();
                    }
                }) { text = "Equip" };
                equipBtn.style.width = 50;
                equipBtn.style.height = 22;
                equipBtn.style.backgroundColor = new Color(0.3f, 0.3f, 0.6f);
                equipBtn.style.color = Color.white;
                equipBtn.style.fontSize = 10;
                row.Add(equipBtn);

                _equipList.Add(row);
            }
        }

    }
}

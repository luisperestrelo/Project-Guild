using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ProjectGuild.Simulation.Combat;
using ProjectGuild.Simulation.Core;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Controller for the Abilities panel. Shows all abilities grouped by skill type,
    /// with icons, computed stats, and lock states. Supports a "pick mode" for the
    /// Combat Style editor where clicking an ability selects it for a rule.
    /// </summary>
    public class AbilitiesPanelController
    {
        private readonly VisualElement _root;
        private readonly VisualElement _panelRoot;
        private readonly UIManager _uiManager;

        private readonly Label _runnerNameLabel;
        private readonly VisualElement _tabBar;
        private readonly TextField _searchField;
        private readonly VisualElement _grid;
        private readonly Label _footerLabel;

        private string _searchFilter = "";
        private SkillType? _activeTab; // null = All
        private string _cachedShapeKey = "";

        // Pick mode: when set, clicking an ability invokes the callback and closes the panel
        private Action<string> _pickCallback;
        private bool IsPickMode => _pickCallback != null;

        public bool IsOpen { get; private set; }

        // Skill types shown as tabs (combat skills only)
        private static readonly SkillType[] TabSkills = {
            SkillType.Melee, SkillType.Magic, SkillType.Restoration
        };

        // Rich text color constants (matching AutomationTabController)
        private const string DmgColor = "#E05555";
        private const string HealColor = "#55CC55";
        private const string ManaColor = "#6CA0DC";
        private const string TimeColor = "#CCAA55";
        private const string CooldownColor = "#CC8844";

        public AbilitiesPanelController(VisualElement root, UIManager uiManager)
        {
            _root = root;
            _panelRoot = root.Q("abilities-panel");
            _uiManager = uiManager;

            _runnerNameLabel = root.Q<Label>("abilities-runner-name");
            _tabBar = root.Q("abilities-tab-bar");
            _searchField = root.Q<TextField>("abilities-search-field");
            _grid = root.Q("abilities-grid");
            _footerLabel = root.Q<Label>("abilities-footer-label");

            _panelRoot.focusable = true;

            root.Q<Button>("btn-close-abilities").clicked += Close;

            _searchField.RegisterValueChangedCallback(evt =>
            {
                _searchFilter = evt.newValue ?? "";
                _cachedShapeKey = "";
                RebuildGrid();
            });

            _panelRoot.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    Close();
                    evt.StopPropagation();
                }
            });

            _root.style.display = DisplayStyle.None;
        }

        public void Open(string runnerId = null)
        {
            IsOpen = true;
            _pickCallback = null;
            _root.style.display = DisplayStyle.Flex;
            _panelRoot.Focus();
            _cachedShapeKey = "";
            _activeTab = GetBestTabForRunner(runnerId);
            BuildTabs();
            RebuildGrid();
            UpdateRunnerName(runnerId);
        }

        /// <summary>
        /// Open in pick mode. Clicking an ability invokes the callback with the ability ID and closes.
        /// </summary>
        public void OpenPickMode(Action<string> onPick, string runnerId = null)
        {
            IsOpen = true;
            _pickCallback = onPick;
            _root.style.display = DisplayStyle.Flex;
            _panelRoot.Focus();
            _cachedShapeKey = "";
            _activeTab = GetBestTabForRunner(runnerId);
            BuildTabs();
            RebuildGrid();
            UpdateRunnerName(runnerId);
            _footerLabel.text = "Click an ability to select it.";
        }

        public void Close()
        {
            IsOpen = false;
            _pickCallback = null;
            _root.style.display = DisplayStyle.None;
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open(_uiManager.SelectedRunnerId);
        }

        /// <summary>
        /// Pick the tab matching the runner's highest effective combat skill (includes passion).
        /// </summary>
        private SkillType? GetBestTabForRunner(string runnerId)
        {
            var sim = _uiManager.Simulation;
            if (sim == null || string.IsNullOrEmpty(runnerId)) return null;
            var runner = sim.CurrentGameState.Runners.Find(r => r.Id == runnerId);
            if (runner == null) return null;

            SkillType? best = null;
            float bestLevel = -1f;
            foreach (var skill in TabSkills)
            {
                float effective = runner.GetEffectiveLevel(skill, sim.Config);
                if (effective > bestLevel)
                {
                    bestLevel = effective;
                    best = skill;
                }
            }
            return best;
        }

        private void UpdateRunnerName(string runnerId)
        {
            var sim = _uiManager.Simulation;
            if (sim == null || string.IsNullOrEmpty(runnerId))
            {
                _runnerNameLabel.text = "";
                return;
            }
            var runner = sim.CurrentGameState.Runners.Find(r => r.Id == runnerId);
            _runnerNameLabel.text = runner != null ? runner.Name : "";
        }

        // ---- Tabs ----

        private void BuildTabs()
        {
            _tabBar.Clear();

            // "All" tab
            var allBtn = new Button(() => SetActiveTab(null));
            allBtn.text = "All";
            allBtn.AddToClassList("abilities-tab");
            if (_activeTab == null)
                allBtn.AddToClassList("abilities-tab-active");
            _tabBar.Add(allBtn);

            foreach (var skill in TabSkills)
            {
                var captured = skill;
                var btn = new Button(() => SetActiveTab(captured));
                btn.text = skill.ToString();
                btn.AddToClassList("abilities-tab");
                btn.AddToClassList($"abilities-tab-{skill.ToString().ToLower()}");
                if (_activeTab == skill)
                    btn.AddToClassList("abilities-tab-active");
                _tabBar.Add(btn);
            }
        }

        private void SetActiveTab(SkillType? tab)
        {
            _activeTab = tab;
            _cachedShapeKey = "";
            BuildTabs();
            RebuildGrid();
        }

        // ---- Grid ----

        private void RebuildGrid()
        {
            var sim = _uiManager.Simulation;
            if (sim == null) return;

            var abilities = sim.Config.AbilityDefinitions;
            if (abilities == null || abilities.Length == 0)
            {
                _grid.Clear();
                _footerLabel.text = "No abilities defined.";
                return;
            }

            // Get runner for level checks
            Runner runner = null;
            string runnerId = _uiManager.SelectedRunnerId;
            if (!string.IsNullOrEmpty(runnerId))
                runner = sim.CurrentGameState.Runners.Find(r => r.Id == runnerId);

            // Filter and sort
            var filtered = new List<AbilityConfig>();
            foreach (var ability in abilities)
            {
                if (_activeTab.HasValue && ability.SkillType != _activeTab.Value)
                    continue;
                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    string name = (ability.Name ?? ability.Id).ToLowerInvariant();
                    if (!name.Contains(_searchFilter.ToLowerInvariant()))
                        continue;
                }
                filtered.Add(ability);
            }

            // Sort by skill type, then unlock level
            filtered.Sort((a, b) =>
            {
                int cmp = a.SkillType.CompareTo(b.SkillType);
                return cmp != 0 ? cmp : a.UnlockLevel.CompareTo(b.UnlockLevel);
            });

            // Shape key for caching
            string levelSig = "";
            if (runner != null)
                foreach (var skill in runner.Skills)
                    levelSig += $"{skill.Level},";
            string shapeKey = $"{_activeTab}|{_searchFilter}|{filtered.Count}|{levelSig}|{IsPickMode}";
            if (shapeKey == _cachedShapeKey) return;
            _cachedShapeKey = shapeKey;

            _grid.Clear();

            // Group by skill type when showing "All"
            SkillType? lastSkill = null;
            int totalCount = 0;
            int unlockedCount = 0;

            foreach (var ability in filtered)
            {
                // Insert skill header when skill changes (only in All tab)
                if (_activeTab == null && ability.SkillType != lastSkill)
                {
                    if (lastSkill != null)
                    {
                        // Spacer between groups
                        var spacer = new VisualElement();
                        spacer.style.width = Length.Percent(100);
                        spacer.style.height = 8;
                        _grid.Add(spacer);
                    }
                    lastSkill = ability.SkillType;
                }

                float inherentLevel = runner?.GetEffectiveLevel(ability.SkillType, sim.Config) ?? 0;
                bool unlocked = ability.UnlockLevel <= 0 || inherentLevel >= ability.UnlockLevel;
                totalCount++;
                if (unlocked) unlockedCount++;

                var card = BuildAbilityCard(ability, runner, sim, unlocked);
                _grid.Add(card);
            }

            if (!IsPickMode)
                _footerLabel.text = $"{unlockedCount}/{totalCount} abilities unlocked";
        }

        private VisualElement BuildAbilityCard(AbilityConfig ability, Runner runner,
            GameSimulation sim, bool unlocked)
        {
            var card = new VisualElement();
            card.AddToClassList("ability-card");
            card.AddToClassList($"ability-card-{ability.SkillType.ToString().ToLower()}");

            if (!unlocked)
                card.AddToClassList("ability-card-locked");

            if (IsPickMode)
                card.AddToClassList("ability-card-pick-mode");

            // Icon
            var icon = new VisualElement();
            icon.AddToClassList("ability-card-icon");
            var sprite = _uiManager.GetAbilityIcon(ability.Id);
            if (sprite != null)
                icon.style.backgroundImage = new StyleBackground(sprite);
            else
                icon.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.3f));
            card.Add(icon);

            // Name
            var nameLabel = new Label(ability.Name ?? ability.Id);
            nameLabel.AddToClassList("ability-card-name");
            card.Add(nameLabel);

            // Info line
            var infoLabel = new Label();
            infoLabel.AddToClassList("ability-card-info");

            string actionTime = $"{ability.ActionTimeTicks / 10f:0.#}s";
            string mana = ability.ManaCost > 0 ? $" | {ability.ManaCost:0} mana" : "";
            string cd = ability.CooldownTicks > 0
                ? $" | {ability.CooldownTicks / 10f:0.#}s CD" : "";
            infoLabel.text = $"{actionTime}{mana}{cd}";
            card.Add(infoLabel);

            // Unlock info for locked abilities
            if (!unlocked)
            {
                var unlockLabel = new Label($"Requires {ability.SkillType} {ability.UnlockLevel}");
                unlockLabel.AddToClassList("ability-card-unlock");
                card.Add(unlockLabel);
            }

            // Tooltip with full computed description (hold ALT for math breakdown)
            var capturedAbility = ability;
            int runnerSkillLevel = runner?.GetSkill(ability.SkillType).Level ?? 0;
            _uiManager.RegisterTooltip(card, () =>
            {
                bool showMath = Keyboard.current != null && Keyboard.current.altKey.isPressed;
                return BuildAbilityTooltip(capturedAbility, runner, sim, runnerSkillLevel, unlocked, showMath);
            });

            // Click handler
            if (IsPickMode)
            {
                var capturedId = ability.Id;
                card.RegisterCallback<ClickEvent>(evt =>
                {
                    _pickCallback?.Invoke(capturedId);
                    Close();
                });
            }

            return card;
        }

        // ---- Tooltip (reuses the same rich-text format as AutomationTabController) ----

        private static string BuildAbilityTooltip(AbilityConfig ability, Runner runner,
            GameSimulation sim, int runnerSkillLevel, bool unlocked, bool showMath = false)
        {
            string name = ability.Name ?? ability.Id;
            float level = runner?.GetEffectiveLevel(ability.SkillType, sim.Config) ?? 1f;
            var parts = new List<string>();

            // Title + thematic description
            string desc = ability.Description;
            if (!string.IsNullOrEmpty(desc))
                parts.Add($"<b>{name}</b>\n<color=#AAAACC>{desc}</color>");
            else
                parts.Add($"<b>{name}</b>");

            // Effect descriptions
            foreach (var effect in ability.Effects)
            {
                bool isDamage = effect.Type == EffectType.Damage || effect.Type == EffectType.DamageAoe;
                bool isHeal = effect.Type == EffectType.Heal || effect.Type == EffectType.HealAoe
                    || effect.Type == EffectType.HealSelf;
                bool isTaunt = effect.Type == EffectType.Taunt || effect.Type == EffectType.TauntAoe
                    || effect.Type == EffectType.TauntAll;

                string condPrefix = "";
                if (effect.Condition != null)
                {
                    condPrefix = effect.Condition.Type switch
                    {
                        AbilityEffectConditionType.TargetHpBelowPercent =>
                            $"Against targets below <b><color={DmgColor}>{effect.Condition.Threshold:F0}%</color></b>: ",
                        AbilityEffectConditionType.TargetHpAbovePercent =>
                            $"Against targets above <b><color={DmgColor}>{effect.Condition.Threshold:F0}%</color></b>: ",
                        AbilityEffectConditionType.IsKillingBlow => "On killing blow: ",
                        _ => "",
                    };
                }

                if (isDamage)
                {
                    float dmg = CombatFormulas.CalculateDamage(effect, level, 0f, sim.Config);
                    string aoe = effect.Type == EffectType.DamageAoe
                        ? $" to up to <b>{(effect.MaxTargets < 0 ? "all" : effect.MaxTargets.ToString())}</b> enemies" : "";
                    parts.Add($"{condPrefix}Deals <b><color={DmgColor}>{dmg:F1}</color></b> damage{aoe}.");
                    if (showMath)
                        parts.Add($"<color=#888888>  {effect.BaseValue} base x {effect.ScalingFactor}x x (1 + {level:F1} x {sim.Config.CombatDamageScalingPerLevel})</color>");
                }
                else if (isHeal)
                {
                    float heal = CombatFormulas.CalculateHeal(effect, level, sim.Config);
                    string target = effect.Type == EffectType.HealSelf ? " to self"
                        : effect.Type == EffectType.HealAoe
                            ? $" to up to <b>{(effect.MaxTargets < 0 ? "all" : effect.MaxTargets.ToString())}</b> allies"
                            : "";
                    parts.Add($"{condPrefix}Heals <b><color={HealColor}>{heal:F1}</color></b>{target}.");
                    if (showMath)
                        parts.Add($"<color=#888888>  {effect.BaseValue} base x {effect.ScalingFactor}x x (1 + {level:F1} x {sim.Config.CombatDamageScalingPerLevel})</color>");
                }
                else if (isTaunt)
                {
                    string scope = effect.Type switch
                    {
                        EffectType.Taunt => "Forces target to attack you.",
                        EffectType.TauntAoe => $"Forces up to <b>{(effect.MaxTargets < 0 ? "all" : effect.MaxTargets.ToString())}</b> enemies to attack you.",
                        EffectType.TauntAll => "Forces all enemies to attack you.",
                        _ => "Taunts.",
                    };
                    parts.Add(scope);
                }
            }

            // Action time, cooldown, mana
            string actionTime = $"<b><color={TimeColor}>{ability.ActionTimeTicks / 10f:0.#}s</color></b>";
            string cdStr = ability.CooldownTicks > 0
                ? $"  <b><color={CooldownColor}>{ability.CooldownTicks / 10f:0.#}s</color></b> cooldown" : "";
            string manaStr = ability.ManaCost > 0
                ? $"  <b><color={ManaColor}>{ability.ManaCost}</color></b> mana" : "";
            parts.Add($"{actionTime} action{cdStr}{manaStr}");
            if (showMath)
                parts.Add($"<color=#888888>  {ability.ActionTimeTicks} ticks / 10 tps | CD: {ability.CooldownTicks} ticks</color>");

            // Effective level info (math mode)
            if (showMath && runner != null)
                parts.Add($"<color=#888888>  {ability.SkillType} effective level: {level:F1} (base {runner.GetSkill(ability.SkillType).Level})</color>");

            // Unlock
            if (!unlocked)
                parts.Add($"<color=#CC4444>Requires {ability.SkillType} {ability.UnlockLevel} (runner: {ability.SkillType} {runnerSkillLevel})</color>");
            else if (ability.UnlockLevel > 0)
                parts.Add($"Requires {ability.SkillType} {ability.UnlockLevel}");

            if (!showMath)
                parts.Add("<color=#555555>Hold ALT for math</color>");

            return string.Join("\n", parts);
        }
    }
}

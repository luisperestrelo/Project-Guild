using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Builds and owns the template shortcut row shown in task sequence and ruleset editors.
    /// Shows "Templates:" label + favorited template buttons + "More..." for all + gear manage.
    /// Only favorited templates appear as quick-access buttons. "More..." opens a popup
    /// below the row with ALL templates for one-click apply.
    /// Always shows the gear button, even with 0 templates (so "Save Current as Template" is reachable).
    /// Shared across all three editors (DRY).
    ///
    /// Instance class — created once per editor, owns its visual elements, and supports
    /// in-place button refresh via <see cref="RefreshIfNeeded"/> (tick-driven) and
    /// <see cref="ForceRefresh"/> (event-driven, e.g. after favorite toggle).
    /// </summary>
    public class TemplateRowBuilder<T>
    {
        public const int MaxFavorites = 20;

        private const string InfoTooltipText =
            "Favorited templates (\u2605) appear here. Max 20 (\u2605).\n"
            + "\"More...\" shows all templates.\n"
            + "Use the gear to manage favorites, reorder, rename,\n"
            + "delete, or save the current selection as a template.";

        private readonly List<T> _templates;
        private readonly Func<T, string> _getId;
        private readonly Func<T, string> _getName;
        private readonly Func<T, bool> _getIsFavorite;
        private readonly Action<string> _onApply;
        private readonly Action<VisualElement, Func<string>> _registerTooltip;

        private readonly VisualElement _wrapper;
        private readonly VisualElement _row;
        private readonly VisualElement _buttonsContainer;

        private string _cachedShapeKey;

        /// <summary>The root element to insert into the hierarchy.</summary>
        public VisualElement Root => _wrapper;

        /// <summary>
        /// Create a template row builder.
        /// </summary>
        /// <param name="templates">Live reference to the template library list</param>
        /// <param name="getId">Accessor for template Id</param>
        /// <param name="getName">Accessor for template Name</param>
        /// <param name="getIsFavorite">Accessor for template IsFavorite</param>
        /// <param name="onApply">Called when a template button is clicked (template id)</param>
        /// <param name="onManage">Called when the gear manage button is clicked</param>
        /// <param name="registerTooltip">Tooltip registration callback: (element, getText)</param>
        public TemplateRowBuilder(
            List<T> templates,
            Func<T, string> getId,
            Func<T, string> getName,
            Func<T, bool> getIsFavorite,
            Action<string> onApply,
            Action onManage,
            Action<VisualElement, Func<string>> registerTooltip = null)
        {
            _templates = templates;
            _getId = getId;
            _getName = getName;
            _getIsFavorite = getIsFavorite;
            _onApply = onApply;
            _registerTooltip = registerTooltip;

            // Build structural skeleton (stable — never rebuilt)
            _wrapper = new VisualElement();
            _wrapper.AddToClassList("template-wrapper");

            _row = new VisualElement();
            _row.AddToClassList("template-row");

            var label = new Label("Templates:");
            label.AddToClassList("template-row-label");
            _row.Add(label);

            _buttonsContainer = new VisualElement();
            _buttonsContainer.AddToClassList("template-row-buttons");
            _row.Add(_buttonsContainer);

            // Info icon
            var infoLabel = new Label("\u24d8"); // ⓘ
            infoLabel.AddToClassList("template-info-icon");
            _registerTooltip?.Invoke(infoLabel, () => InfoTooltipText);
            _row.Add(infoLabel);

            // Manage button (gear) — always visible
            var manageBtn = new Button(onManage);
            manageBtn.text = "\u2699"; // ⚙
            manageBtn.AddToClassList("template-manage-button");
            _registerTooltip?.Invoke(manageBtn, () => "Manage templates");
            _row.Add(manageBtn);

            _wrapper.Add(_row);

            // Initial button population
            RebuildButtons();
        }

        /// <summary>
        /// Rebuild buttons only if the template state has changed (count or favorite count).
        /// Call this from tick refresh.
        /// </summary>
        public void RefreshIfNeeded()
        {
            string key = BuildShapeKey();
            if (key == _cachedShapeKey) return;
            _cachedShapeKey = key;
            RebuildButtons();
        }

        /// <summary>
        /// Force rebuild buttons regardless of cache.
        /// Call this from event callbacks (favorite toggle, reorder, delete, save).
        /// </summary>
        public void ForceRefresh()
        {
            _cachedShapeKey = null;
            RebuildButtons();
        }

        private string BuildShapeKey()
        {
            if (_templates == null) return "null";
            int favCount = 0;
            // Include template names/order in key so reorder triggers rebuild
            int hash = _templates.Count;
            foreach (var t in _templates)
            {
                if (_getIsFavorite(t)) favCount++;
                hash = hash * 31 + (_getId(t)?.GetHashCode() ?? 0);
            }
            return $"t{_templates.Count}f{favCount}h{hash}";
        }

        private void RebuildButtons()
        {
            _buttonsContainer.Clear();

            // Also remove any lingering overflow popup from wrapper
            var existingOverflow = _wrapper.Q("template-overflow-popup");
            existingOverflow?.RemoveFromHierarchy();

            bool hasTemplates = _templates != null && _templates.Count > 0;
            bool hasFavorites = false;

            if (hasTemplates)
            {
                foreach (var template in _templates)
                {
                    if (!_getIsFavorite(template)) continue;
                    hasFavorites = true;

                    string capturedId = _getId(template);
                    string name = _getName(template);

                    var btn = new Button(() => _onApply(capturedId));
                    btn.text = name;
                    btn.AddToClassList("template-button");
                    _registerTooltip?.Invoke(btn, () => $"Insert \"{name}\" steps");
                    _buttonsContainer.Add(btn);
                }
            }

            if (!hasFavorites && hasTemplates)
            {
                var hint = new Label("(none favorited)");
                hint.AddToClassList("template-row-hint");
                _buttonsContainer.Add(hint);
            }
            else if (!hasTemplates)
            {
                var hint = new Label("(none yet)");
                hint.AddToClassList("template-row-hint");
                _buttonsContainer.Add(hint);
            }

            // "More..." button — shows ALL templates in a popup below the row
            if (hasTemplates)
            {
                var moreBtn = new Button();
                moreBtn.text = "More\u2026";
                moreBtn.AddToClassList("template-button");
                moreBtn.AddToClassList("template-more-button");
                moreBtn.clicked += () =>
                {
                    var existing = _wrapper.Q("template-overflow-popup");
                    if (existing != null) { existing.RemoveFromHierarchy(); return; }

                    var popup = new VisualElement();
                    popup.name = "template-overflow-popup";
                    popup.AddToClassList("template-overflow-popup");

                    foreach (var template in _templates)
                    {
                        string capturedId = _getId(template);
                        string name = _getName(template);
                        bool isFav = _getIsFavorite(template);

                        var overflowBtn = new Button(() =>
                        {
                            _onApply(capturedId);
                            popup.RemoveFromHierarchy();
                        });
                        overflowBtn.text = isFav ? $"\u2605 {name}" : name;
                        overflowBtn.AddToClassList("template-overflow-item");
                        popup.Add(overflowBtn);
                    }

                    var cancelBtn = new Button(() => popup.RemoveFromHierarchy());
                    cancelBtn.text = "Cancel";
                    cancelBtn.AddToClassList("assign-popup-cancel");
                    popup.Add(cancelBtn);

                    _wrapper.Add(popup);
                };
                _buttonsContainer.Add(moreBtn);
            }
        }

        /// <summary>
        /// Generate a snapshot name with an incrementing suffix: "Name (snapshot 1)", "Name (snapshot 2)", etc.
        /// </summary>
        public static string NextSnapshotName<TItem>(string baseName, List<TItem> items, Func<TItem, string> getName)
        {
            int count = 0;
            string prefix = baseName + " (snapshot";
            foreach (var item in items)
                if (getName(item).StartsWith(prefix)) count++;
            return $"{baseName} (snapshot {count + 1})";
        }
    }
}

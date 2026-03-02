using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Builds the manage popup for automation templates.
    /// Shows all templates with favorite toggle, reorder arrows, inline rename
    /// for custom templates, lock icon for built-in, delete for custom,
    /// and "Save Current as Template" button.
    /// </summary>
    public static class TemplateManagePopup
    {
        /// <summary>
        /// Build a manage popup for a list of templates.
        /// </summary>
        /// <param name="onTemplatesChanged">Called after any mutation (favorite, reorder, delete, save)
        /// so the parent can refresh the quick-access row immediately.</param>
        /// <param name="registerTooltip">Tooltip registration callback for the custom tooltip system.</param>
        public static VisualElement Build<T>(
            List<T> templates,
            Func<T, string> getId,
            Func<T, string> getName,
            Func<T, bool> getIsBuiltIn,
            Func<T, bool> getIsFavorite,
            Func<string, bool> onToggleFavorite,
            Action<string, int> onReorder,
            Action<string, string> onRename,
            Func<string, bool> onDelete,
            Action onSaveCurrentAsTemplate,
            Action onClose,
            Action onTemplatesChanged = null,
            Action<VisualElement, Func<string>> registerTooltip = null)
        {
            var popup = new VisualElement();
            popup.name = "template-manage-popup";
            popup.AddToClassList("template-manage-popup");

            var header = new Label("Manage Templates");
            header.AddToClassList("assign-popup-header");
            popup.Add(header);

            var listContainer = new VisualElement();
            listContainer.AddToClassList("template-manage-list");

            void RebuildList()
            {
                listContainer.Clear();

                // Count current favorites to enforce max
                int favCount = 0;
                foreach (var t in templates)
                    if (getIsFavorite(t)) favCount++;
                bool atMax = favCount >= TemplateRowBuilder<T>.MaxFavorites;

                for (int i = 0; i < templates.Count; i++)
                {
                    var template = templates[i];
                    int capturedIndex = i;
                    string capturedId = getId(template);
                    bool isBuiltIn = getIsBuiltIn(template);
                    bool isFavorite = getIsFavorite(template);

                    var row = new VisualElement();
                    row.AddToClassList("template-manage-row");

                    // Favorite toggle (star)
                    bool starBlocked = !isFavorite && atMax;
                    var starBtn = new Button(() =>
                    {
                        if (starBlocked) return;
                        onToggleFavorite(capturedId);
                        RebuildList();
                        onTemplatesChanged?.Invoke();
                    });
                    starBtn.text = isFavorite ? "\u2605" : "\u2606"; // ★ or ☆
                    starBtn.AddToClassList("template-favorite-btn");
                    if (isFavorite) starBtn.AddToClassList("template-favorite-active");
                    if (starBlocked)
                    {
                        starBtn.AddToClassList("template-favorite-disabled");
                        registerTooltip?.Invoke(starBtn, () => $"Max {TemplateRowBuilder<T>.MaxFavorites} favorites reached");
                    }
                    row.Add(starBtn);

                    // Move arrows
                    var moveContainer = new VisualElement();
                    moveContainer.AddToClassList("template-manage-arrows");

                    var upBtn = new Button(() =>
                    {
                        if (capturedIndex > 0)
                        {
                            onReorder(capturedId, capturedIndex - 1);
                            RebuildList();
                            onTemplatesChanged?.Invoke();
                        }
                    });
                    upBtn.text = "\u25b2";
                    upBtn.AddToClassList("editor-step-move-btn");
                    upBtn.SetEnabled(capturedIndex > 0);
                    moveContainer.Add(upBtn);

                    var downBtn = new Button(() =>
                    {
                        if (capturedIndex < templates.Count - 1)
                        {
                            onReorder(capturedId, capturedIndex + 1);
                            RebuildList();
                            onTemplatesChanged?.Invoke();
                        }
                    });
                    downBtn.text = "\u25bc";
                    downBtn.AddToClassList("editor-step-move-btn");
                    downBtn.SetEnabled(capturedIndex < templates.Count - 1);
                    moveContainer.Add(downBtn);

                    row.Add(moveContainer);

                    if (isBuiltIn)
                    {
                        // Built-in: lock icon with tooltip + name label (no editing)
                        var lockIcon = new Label("\ud83d\udd12");
                        lockIcon.AddToClassList("template-manage-lock");
                        registerTooltip?.Invoke(lockIcon, () => "Built-in template (cannot be deleted)");
                        row.Add(lockIcon);

                        var nameLabel = new Label(getName(template));
                        nameLabel.AddToClassList("template-manage-name");
                        row.Add(nameLabel);
                    }
                    else
                    {
                        // Custom: inline text field for rename + delete button (no tooltip, icon is self-explanatory)
                        var nameField = new TextField();
                        nameField.AddToClassList("template-manage-name-field");
                        nameField.SetValueWithoutNotify(getName(template));
                        nameField.RegisterValueChangedCallback(evt =>
                        {
                            onRename(capturedId, evt.newValue);
                            onTemplatesChanged?.Invoke();
                        });
                        row.Add(nameField);

                        var deleteBtn = new Button(() =>
                        {
                            bool deleted = onDelete(capturedId);
                            if (deleted)
                            {
                                RebuildList();
                                onTemplatesChanged?.Invoke();
                            }
                        });
                        deleteBtn.text = "\u00d7";
                        deleteBtn.AddToClassList("editor-step-delete");
                        row.Add(deleteBtn);
                    }

                    listContainer.Add(row);
                }
            }

            RebuildList();
            popup.Add(listContainer);

            // "Save Current as Template" button
            if (onSaveCurrentAsTemplate != null)
            {
                var saveBtn = new Button(() =>
                {
                    onSaveCurrentAsTemplate();
                    RebuildList();
                    onTemplatesChanged?.Invoke();
                });
                saveBtn.text = "Save Current as Template";
                saveBtn.AddToClassList("editor-add-button");
                saveBtn.style.marginTop = 6;
                popup.Add(saveBtn);
            }

            // Close button
            var closeBtn = new Button(() =>
            {
                popup.RemoveFromHierarchy();
                onClose?.Invoke();
            });
            closeBtn.text = "Close";
            closeBtn.AddToClassList("assign-popup-cancel");
            popup.Add(closeBtn);

            return popup;
        }
    }
}

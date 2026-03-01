using System;
using UnityEngine.UIElements;
using ProjectGuild.Bridge;

namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Shared UI dialog utilities (confirmation popups, etc.).
    /// Used by library editors to avoid duplicating dialog code.
    /// </summary>
    public static class UIDialogs
    {
        /// <summary>
        /// Shows a modal delete confirmation overlay on the given parent element.
        /// Includes a "Don't ask again" toggle that persists to PlayerPreferences.
        /// </summary>
        public static void ShowDeleteConfirmation(
            VisualElement parent, string message,
            PlayerPreferences prefs, Action onConfirm)
        {
            var overlay = new VisualElement();
            overlay.AddToClassList("delete-confirm-overlay");

            var box = new VisualElement();
            box.AddToClassList("delete-confirm-box");

            var text = new Label(message);
            text.AddToClassList("delete-confirm-text");
            box.Add(text);

            var buttons = new VisualElement();
            buttons.AddToClassList("delete-confirm-buttons");

            var cancelBtn = new Button(() => overlay.RemoveFromHierarchy());
            cancelBtn.text = "Cancel";
            cancelBtn.AddToClassList("delete-confirm-cancel");
            buttons.Add(cancelBtn);

            var yesBtn = new Button(() =>
            {
                overlay.RemoveFromHierarchy();
                onConfirm?.Invoke();
            });
            yesBtn.text = "Delete";
            yesBtn.AddToClassList("delete-confirm-yes");
            buttons.Add(yesBtn);

            box.Add(buttons);

            var toggle = new Toggle("Don't ask again");
            toggle.AddToClassList("delete-confirm-toggle");
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (prefs != null)
                {
                    prefs.SkipDeleteConfirmation = evt.newValue;
                    prefs.Save();
                }
            });
            box.Add(toggle);

            overlay.Add(box);
            parent.Add(overlay);
        }

        /// <summary>
        /// Shows a modal cancel-creation confirmation overlay.
        /// Used when the player cancels creating a new item during navigation stack flow.
        /// </summary>
        public static void ShowCancelCreationConfirmation(
            VisualElement parent, PlayerPreferences prefs, Action onConfirm)
        {
            var overlay = new VisualElement();
            overlay.AddToClassList("delete-confirm-overlay");

            var box = new VisualElement();
            box.AddToClassList("delete-confirm-box");

            var text = new Label("Discard this item? Changes will be lost.");
            text.AddToClassList("delete-confirm-text");
            box.Add(text);

            var buttons = new VisualElement();
            buttons.AddToClassList("delete-confirm-buttons");

            var cancelBtn = new Button(() => overlay.RemoveFromHierarchy());
            cancelBtn.text = "Keep Editing";
            cancelBtn.AddToClassList("delete-confirm-cancel");
            buttons.Add(cancelBtn);

            var yesBtn = new Button(() =>
            {
                overlay.RemoveFromHierarchy();
                onConfirm?.Invoke();
            });
            yesBtn.text = "Discard";
            yesBtn.AddToClassList("delete-confirm-yes");
            buttons.Add(yesBtn);

            box.Add(buttons);

            var toggle = new Toggle("Don't ask again");
            toggle.AddToClassList("delete-confirm-toggle");
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (prefs != null)
                {
                    prefs.SkipCancelCreationConfirmation = evt.newValue;
                    prefs.Save();
                }
            });
            box.Add(toggle);

            overlay.Add(box);
            parent.Add(overlay);
        }
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarblesECS.Presentation
{
    internal sealed class MorningPrepView
    {
        private static readonly Color Paper = new Color(.91f, .86f, .73f);
        private static readonly Color Ink = new Color(.14f, .11f, .085f);
        private static readonly Color LightText = new Color(.96f, .93f, .84f);
        private readonly RectTransform root;
        private readonly RectTransform prompt;
        private readonly TextMeshProUGUI promptText;
        private readonly RectTransform reader;
        private readonly TextMeshProUGUI noteText;
        private readonly UnityEngine.UI.Image fade;
        private readonly TMP_FontAsset font;

        internal MorningPrepView(Transform parent, TMP_FontAsset font)
        {
            this.font = font;
            root = Rect("Morning Prep Overlay", parent);
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;

            prompt = Rect("Morning Prep Prompt", root);
            prompt.anchorMin = prompt.anchorMax = new Vector2(.5f, 0);
            prompt.sizeDelta = new Vector2(840, 68);
            prompt.anchoredPosition = new Vector2(0, 80);
            Image(prompt, new Color(.075f, .065f, .055f, .93f));
            promptText = Text("Prompt Text", prompt, LightText, 29);
            Stretch(promptText.rectTransform, new Vector2(18, 6));

            reader = Rect("Note Reader", root);
            Stretch(reader, Vector2.zero);
            Image(reader, new Color(0, 0, 0, .68f));
            var card = Rect("Note Card", reader);
            card.anchorMin = card.anchorMax = new Vector2(.5f, .5f);
            card.sizeDelta = new Vector2(650, 230);
            Image(card, Paper);
            noteText = Text("Note Text", card, Ink, 46);
            noteText.enableWordWrapping = true;
            noteText.enableAutoSizing = true;
            noteText.fontSizeMin = 22;
            noteText.fontSizeMax = 46;
            Stretch(noteText.rectTransform, new Vector2(37, 25));
            var close = Text("Close Hint", card, LightText, 25);
            var hint = close.rectTransform;
            hint.anchorMin = new Vector2(0, 0);
            hint.anchorMax = new Vector2(1, 0);
            hint.sizeDelta = new Vector2(0, 45);
            hint.anchoredPosition = new Vector2(0, -47);
            close.text = "E / Esc  Close";

            var fadeRect = Rect("Wake Up Fade", root);
            Stretch(fadeRect, Vector2.zero);
            fade = Image(fadeRect, Color.black);
            ShowPrompt(null);
            ShowNote(null);
        }

        internal float FadeAlpha => fade.color.a;
        internal void Fade(float alpha)
        {
            fade.color = new Color(0, 0, 0, alpha);
            fade.gameObject.SetActive(alpha > 0);
        }

        internal void ShowPrompt(string value)
        {
            prompt.gameObject.SetActive(!string.IsNullOrEmpty(value));
            promptText.text = value ?? string.Empty;
        }

        internal void ShowNote(string value)
        {
            reader.gameObject.SetActive(value != null);
            noteText.text = value ?? string.Empty;
        }

        internal Transform PlaceNote(Transform anchor, string content)
        {
            var note = Rect("Morning Prep Note", anchor);
            var canvas = note.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            note.sizeDelta = new Vector2(170, 110);
            note.localPosition = new Vector3(0, 0, -.08f);
            note.localRotation = Quaternion.identity;
            note.localScale = Vector3.one * .014f;

            var sheet = Rect("Paper", note);
            Stretch(sheet, Vector2.zero);
            Image(sheet, LightText);
            var stripe = Rect("Paper Rule", sheet);
            stripe.anchorMin = new Vector2(0, .85f);
            stripe.anchorMax = new Vector2(1, .89f);
            stripe.offsetMin = new Vector2(9, 0);
            stripe.offsetMax = new Vector2(-9, 0);
            Image(stripe, new Color(.44f, .16f, .13f));
            var label = Text("Printed Text", sheet, Ink, 16);
            label.enableAutoSizing = true;
            label.fontSizeMin = 9;
            label.fontSizeMax = 16;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(label.rectTransform, new Vector2(8, 18));
            label.text = content;
            return note;
        }

        internal void Dispose() => Object.Destroy(root.gameObject);

        private TextMeshProUGUI Text(string name, Transform parent, Color color, float size)
        {
            var result = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            result.font = font;
            result.fontSize = size;
            result.color = color;
            result.alignment = TextAlignmentOptions.Center;
            result.raycastTarget = false;
            result.richText = false;
            return result;
        }

        private static UnityEngine.UI.Image Image(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect, Vector2 inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = inset;
            rect.offsetMax = -inset;
        }
    }
}

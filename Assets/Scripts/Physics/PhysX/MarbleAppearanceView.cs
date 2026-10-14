using TMPro;
using UnityEngine;

namespace MarblesECS.PhysX
{
    /// <summary>Reusable numbered/coloured ball presentation, restored when a pooled body is reused.</summary>
    internal sealed class MarbleAppearanceView : MonoBehaviour
    {
        private Renderer surface;
        private MaterialPropertyBlock original, tinted;
        private TextMeshPro label;
        internal void Apply(string text, Color color)
        {
            if (surface == null)
            {
                surface = GetComponent<Renderer>();
                original = new MaterialPropertyBlock(); tinted = new MaterialPropertyBlock();
                if (surface != null) surface.GetPropertyBlock(original);
            }
            if (surface != null)
            { surface.GetPropertyBlock(tinted); tinted.SetColor("_BaseColor", color); tinted.SetColor("_Color", color); surface.SetPropertyBlock(tinted); }
            if (label == null)
            {
                var go = new GameObject("BallLabel"); go.transform.SetParent(transform, false);
                label = go.AddComponent<TextMeshPro>(); label.fontSize = 6; label.alignment = TextAlignmentOptions.Center;
                label.enableWordWrapping = false; label.color = Color.white; label.rectTransform.sizeDelta = Vector2.one;
            }
            label.text = text; label.gameObject.SetActive(text.Length > 0); PositionLabel();
        }
        internal void ResetAppearance()
        { if (surface != null && original != null) surface.SetPropertyBlock(original); if (label != null) label.gameObject.SetActive(false); }
        private void LateUpdate() { if (label != null && label.gameObject.activeSelf) PositionLabel(); }
        private void PositionLabel()
        {
            var camera = Camera.main;
            if (camera == null) { label.transform.localPosition = Vector3.up * .55f; return; }
            var toward = (camera.transform.position - transform.position).normalized;
            label.transform.position = transform.position + toward * transform.lossyScale.x * .56f;
            label.transform.rotation = Quaternion.LookRotation(-toward, camera.transform.up);
        }
    }
    public sealed partial class MarblePhysicsBridge : IMarbleAppearanceSink
    {
        public bool SetAppearance(MarbleKey key, string label, Color color)
        {
            if (!bodies.TryGetValue(key, out var body) || body == null) return false;
            var view = body.GetComponent<MarbleAppearanceView>();
            if (view == null) view = body.gameObject.AddComponent<MarbleAppearanceView>();
            view.Apply(label, color); return true;
        }
    }
}

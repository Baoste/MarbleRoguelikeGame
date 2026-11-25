using UnityEngine;

namespace MarblesECS
{
    internal readonly struct FloatingTextAnimation
    {
        internal readonly Vector3 Offset;
        internal readonly float Height;
        internal readonly Color32 Color;

        internal FloatingTextAnimation(in FloatingTextData text, double now)
        {
            var style = text.Style;
            float age = (float)(now - text.SpawnTime);
            float time = age / style.Lifetime;
            float pop = style.Scale.Evaluate(time);
            float scaleRange = pop < 0 ? 1 - style.InitialScale : style.PeakScale - 1;
            Height = style.Height * (1 + pop * scaleRange);
            var color = style.Color;
            color.a = (byte)Mathf.RoundToInt(color.a * style.Alpha.Evaluate(time));
            Color = color;
            Offset = style.Offset + new Vector3(text.HorizontalSpeed * age, style.RiseSpeed * age, 0);
        }
    }
}

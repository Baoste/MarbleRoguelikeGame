using System;
using UnityEngine;

namespace MarblesECS.PhysX
{
    /// <summary>Normalized layout coordinates on the unit-scale trapezoidal playing surface.</summary>
    internal readonly struct BoardLayoutProjection
    {
        internal readonly float Length, TopWidth, BottomWidth;
        internal float MaxWidth => Mathf.Max(TopWidth, BottomWidth);

        internal BoardLayoutProjection(float length, float topWidth, float bottomWidth)
        {
            if (!MarbleRules.IsFinite(length) || !MarbleRules.IsFinite(topWidth) ||
                !MarbleRules.IsFinite(bottomWidth) || length <= 0 || topWidth <= 0 || bottomWidth <= 0)
                throw new ArgumentException("The board surface must have positive, finite dimensions.");
            Length = length;
            TopWidth = topWidth;
            BottomWidth = bottomWidth;
        }

        internal float WidthAt(float y) => Mathf.Lerp(TopWidth, BottomWidth, y);
        internal Vector2 ToPlane(float x, float y) => new Vector2((x - .5f) * WidthAt(y), y * Length);

        internal Vector2 ToNormalized(Vector2 planePoint)
        {
            float y = planePoint.y / Length;
            return new Vector2(.5f + planePoint.x / WidthAt(y), y);
        }

        internal Vector3 ToSurfaceLocal(float x, float y, float height)
        {
            var position = ToPlane(x, y);
            // The camera looks uphill along local +X, so local -Z points screen-right.
            return new Vector3(Length * .5f - position.y, height, -position.x);
        }

        internal void Validate(BoardLayoutDefinition layout)
        {
            const float tolerance = .0001f;
            float slope = (BottomWidth - TopWidth) / (2 * Length);
            float sideMargin = Mathf.Sqrt(1 + slope * slope);
            for (int i = 0; i < layout.Pins.Count; i++)
            {
                var pin = layout.Pins[i];
                var p = ToPlane(pin.x, pin.y);
                if (p.y - pin.radius < -tolerance || p.y + pin.radius > Length + tolerance ||
                    Mathf.Abs(p.x) + pin.radius * sideMargin > WidthAt(pin.y) * .5f + tolerance)
                    throw new ArgumentException($"{layout.Source} pins[{i}] extends outside the playing surface.");
            }
            for (int i = 0; i < layout.Zones.Count; i++)
            {
                var zone = layout.Zones[i];
                var p = ToPlane(zone.x, zone.y);
                float first = p.y - zone.depth * .5f;
                float last = p.y + zone.depth * .5f;
                float available = Mathf.Min(WidthAt(first / Length), WidthAt(last / Length));
                if (first < -tolerance || last > Length + tolerance ||
                    Mathf.Abs(p.x) + zone.width * .5f > available * .5f + tolerance)
                    throw new ArgumentException($"{layout.Source} zones[{i}] extends outside the playing surface.");
            }
        }
    }
}

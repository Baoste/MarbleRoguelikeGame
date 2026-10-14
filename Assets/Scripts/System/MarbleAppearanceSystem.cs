using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MarblesECS
{
    internal struct MarbleAppearance : IComponentData { public FixedString64Bytes Label; public Color Color; }
    public interface IMarbleAppearanceSink { bool SetAppearance(MarbleKey key, string label, Color color); }
    internal static class MarbleAppearanceSystem
    {
        internal static void Set(SimulationContext c, Entity ball, string label, float r, float g, float b)
        {
            if (label == null || System.Text.Encoding.UTF8.GetByteCount(label) > 60 ||
                !MarbleRules.IsFinite(r) || !MarbleRules.IsFinite(g) || !MarbleRules.IsFinite(b) ||
                r < 0 || r > 1 || g < 0 || g > 1 || b < 0 || b > 1) throw new ArgumentOutOfRangeException();
            var value = new MarbleAppearance { Label = new FixedString64Bytes(label), Color = new Color(r, g, b, 1) };
            if (c.Manager.HasComponent<MarbleAppearance>(ball)) c.Manager.SetComponentData(ball, value);
            else c.Manager.AddComponentData(ball, value);
            Sync(c, ball);
        }
        internal static void Sync(SimulationContext c, Entity ball)
        {
            if (!c.Manager.HasComponent<MarbleAppearance>(ball) || !(c.Physics is IMarbleAppearanceSink sink)) return;
            var value = c.Manager.GetComponentData<MarbleAppearance>(ball); var shot = c.Manager.GetComponentData<ShotOrigin>(ball);
            sink.SetAppearance(new MarbleKey(shot.RoundId, shot.SpawnSequence), value.Label.ToString(), value.Color);
        }
        internal static void Copy(SimulationContext c, Entity parent, Entity child)
        {
            if (!c.Manager.HasComponent<MarbleAppearance>(parent)) return;
            c.Manager.AddComponentData(child, c.Manager.GetComponentData<MarbleAppearance>(parent)); Sync(c, child);
        }
    }
}

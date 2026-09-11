using Unity.Entities;

namespace MarblesECS
{
    internal static class MarbleModifierSystem
    {
        internal struct Totals
        {
            internal double ScoreAdd, ScoreMul, RushChanceAdd;
        }

        internal static Totals Aggregate(SimulationContext context, Entity entity)
        {
            var result = new Totals { ScoreMul = 1 };
            var modifiers = context.Manager.GetBuffer<MarbleModifier>(entity);
            for (int i = modifiers.Length - 1; i >= 0; i--)
            {
                var entry = modifiers[i];
                if (entry.ExpireTick != 0 && entry.ExpireTick <= context.Round.Tick)
                {
                    modifiers.RemoveAt(i);
                    continue;
                }
                switch (entry.Attribute)
                {
                    case MarbleModifierAttribute.ScoreAdd: result.ScoreAdd += entry.Value * entry.Stacks; break;
                    case MarbleModifierAttribute.RushChanceAdd: result.RushChanceAdd += entry.Value * entry.Stacks; break;
                    case MarbleModifierAttribute.ScoreMul:
                        for (int stack = 0; stack < entry.Stacks; stack++)
                            result.ScoreMul = MarbleRules.CombineDeviceMultiplier(result.ScoreMul, entry.Value);
                        break;
                }
            }
            return result;
        }
    }
}

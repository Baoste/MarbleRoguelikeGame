using System;

namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        public bool TryGetMarble(MarbleKey key, out MarbleStateSnapshot snapshot)
        {
            ThrowIfDisposed();
            snapshot = default;
            if (!context.TryGetFlying(key, out var entity)) return false;
            var score = context.Manager.GetComponentData<MarbleScore>(entity);
            var rush = context.Manager.GetComponentData<MarbleRush>(entity);
            snapshot = new MarbleStateSnapshot
            {
                Key = key, StableId = context.Manager.GetComponentData<StableIdentity>(entity).StableId,
                DefinitionId = context.Manager.GetComponentData<DefinitionRef>(entity).DefinitionId,
                BloodInvestment = context.Manager.GetComponentData<ShotOrigin>(entity).BloodInvestment,
                BaseScore = score.BaseScore, ScoreBonus = score.ScoreBonus, ScoreMultiplier = score.ScoreMultiplier,
                BaseRushChance = rush.BaseRushChance, RushChanceBonus = rush.RushChanceBonus,
                Restitution = context.Manager.GetComponentData<MarblePhysicsProperties>(entity).Restitution
            };
            return true;
        }

        /// <summary>Permanent, validated gameplay effect; invoke at a gameplay sync point, never in a PhysX callback.</summary>
        public bool ApplyMarbleEffect(MarbleKey key, MarbleModifierAttribute attribute, double value)
        {
            ThrowIfDisposed();
            if (insidePhysicsStep) throw new InvalidOperationException("Apply effects outside the PhysX step.");
            ValidateAttribute(attribute, value);
            if (!context.TryGetFlying(key, out var entity)) return false;
            if (attribute == MarbleModifierAttribute.RushChanceAdd)
            {
                var rush = context.Manager.GetComponentData<MarbleRush>(entity);
                if (!MarbleRules.IsFinite(rush.RushChanceBonus + value)) return false;
                rush.RushChanceBonus += value;
                context.Manager.SetComponentData(entity, rush);
            }
            else
            {
                var score = context.Manager.GetComponentData<MarbleScore>(entity);
                if (attribute == MarbleModifierAttribute.ScoreAdd)
                {
                    if (!MarbleRules.IsFinite(score.ScoreBonus + value)) return false;
                    score.ScoreBonus += value;
                }
                else score.ScoreMultiplier = MarbleRules.CombineDeviceMultiplier(score.ScoreMultiplier, value);
                context.Manager.SetComponentData(entity, score);
            }
            return true;
        }

        /// <summary>Replace one stack group and refresh expiry; duration zero lasts for the marble's lifetime.</summary>
        public bool ApplyMarbleModifier(MarbleKey key, uint stackKey, MarbleModifierAttribute attribute,
            double value, long durationTicks, ushort stacks = 1)
        {
            ThrowIfDisposed();
            if (insidePhysicsStep) throw new InvalidOperationException("Apply effects outside the PhysX step.");
            ValidateAttribute(attribute, value);
            if (stackKey == 0 || durationTicks < 0 || stacks == 0) throw new ArgumentOutOfRangeException();
            if (!MarbleRules.IsFinite(value * stacks)) throw new ArgumentOutOfRangeException(nameof(value));
            if (!context.TryGetFlying(key, out var entity)) return false;
            long expires = durationTicks == 0 ? 0 : checked(context.Round.Tick + durationTicks);
            var buffer = context.Manager.GetBuffer<MarbleModifier>(entity);
            var entry = new MarbleModifier { StackKey = stackKey, Attribute = attribute,
                Value = value, Stacks = stacks, ExpireTick = expires };
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].StackKey == stackKey) { buffer[i] = entry; return true; }
            buffer.Add(entry);
            return true;
        }

        private static void ValidateAttribute(MarbleModifierAttribute attribute, double value)
        {
            if (!MarbleRules.IsFinite(value) || (attribute != MarbleModifierAttribute.ScoreAdd &&
                attribute != MarbleModifierAttribute.ScoreMul && attribute != MarbleModifierAttribute.RushChanceAdd))
                throw new ArgumentOutOfRangeException();
            if (attribute == MarbleModifierAttribute.ScoreMul) MarbleRules.CheckMultiplier(value);
        }
    }
}

using System;
using System.Collections.Generic;
using Unity.Entities;

namespace MarblesECS
{
    internal static class AttributeRuntime
    {
        internal const float RadiusToWorld = 0.16f;
        internal static bool Enabled(SimulationContext c) => c.Content != null;
        internal static double Get(SimulationContext c, Entity entity, GameAttribute id) =>
            c.Manager.GetBuffer<AttributeValue>(entity)[(int)id].Current;
        internal static double Global(SimulationContext c, GameAttribute id) => Get(c, c.PlayerEntity, id);
        internal static double Device(SimulationContext c, Entity device, GameAttribute id)
        {
            var effects = new List<AttributeEffect>();
            foreach (var entity in new[] { c.PlayerEntity, device })
            {
                var source = c.Manager.GetBuffer<AttributeModifierData>(entity);
                for (int i = 0; i < source.Length; i++)
                    if (source[i].ExpireTick == 0 || source[i].ExpireTick > c.Round.Tick) effects.Add(source[i].Effect);
            }
            return AttributeMath.Evaluate(c.Content.Definition(id), c.Manager.GetBuffer<AttributeValue>(device)[(int)id].Base, effects);
        }

        internal static void Initialize(SimulationContext c, Entity entity, double[] values = null)
        {
            var buffer = c.Manager.HasBuffer<AttributeValue>(entity) ? c.Manager.GetBuffer<AttributeValue>(entity) : c.Manager.AddBuffer<AttributeValue>(entity);
            buffer.Clear();
            for (int i = 0; i < (int)GameAttribute.Count; i++)
            {
                double value = values == null ? c.Content.Definition((GameAttribute)i).DefaultValue : values[i];
                buffer.Add(new AttributeValue { Base = value, Current = value });
            }
            if (!c.Manager.HasBuffer<AttributeModifierData>(entity)) c.Manager.AddBuffer<AttributeModifierData>(entity);
        }

        internal static void Recompute(SimulationContext c, Entity entity)
        {
            var effects = c.Manager.GetBuffer<AttributeModifierData>(entity);
            if (effects.Length == 0)
            {
                var plain = c.Manager.GetBuffer<AttributeValue>(entity);
                for (int i = 0; i < plain.Length; i++)
                {
                    var value = plain[i]; value.Current = c.Content.Definition((GameAttribute)i).Clamp(value.Base); plain[i] = value;
                }
                return;
            }
            var list = new List<AttributeEffect>(effects.Length);
            for (int i = effects.Length - 1; i >= 0; i--)
                if (effects[i].ExpireTick != 0 && effects[i].ExpireTick <= c.Round.Tick) effects.RemoveAt(i);
            for (int i = 0; i < effects.Length; i++) list.Add(effects[i].Effect);
            var values = c.Manager.GetBuffer<AttributeValue>(entity);
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                value.Current = AttributeMath.Evaluate(c.Content.Definition((GameAttribute)i), value.Base, list);
                values[i] = value;
            }
        }

        internal static void SetBase(SimulationContext c, Entity entity, GameAttribute id, double value)
        {
            var buffer = c.Manager.GetBuffer<AttributeValue>(entity);
            var entry = buffer[(int)id];
            entry.Base = c.Content.Definition(id).Clamp(value);
            buffer[(int)id] = entry;
            Recompute(c, entity);
        }

        internal sealed class Spawn
        {
            internal double[] Values;
            internal BallFeatures Features;
            internal ShotQuote Quote;
            internal int Count;
        }

        internal static Spawn Prepare(SimulationContext c, float flow)
        {
            float investment = MarbleRules.Investment(c.Tuning, flow);
            var values = new double[(int)GameAttribute.Count];
            var bases = c.Manager.GetBuffer<AttributeValue>(c.PlayerEntity);
            for (int i = 0; i < values.Length; i++) values[i] = bases[i].Base;
            // Flow chooses the invested amount; all attribute bases come exclusively from GameContent.
            values[(int)GameAttribute.BLOOD_COST] *= investment;
            var features = new BallFeatures { RushScoreMultiplier = 1 };
            double volume = 1;
            var effects = new List<AttributeEffect>();
            var globalEffects = c.Manager.GetBuffer<AttributeModifierData>(c.PlayerEntity);
            for (int i = 0; i < globalEffects.Length; i++)
                if (globalEffects[i].ExpireTick == 0 || globalEffects[i].ExpireTick > c.Round.Tick) effects.Add(globalEffects[i].Effect);
            var doses = c.Manager.GetBuffer<ActiveDrugData>(c.PlayerEntity);
            for (int i = 0; i < doses.Length; i++)
            {
                if (doses[i].EndsAt <= c.Round.Time) continue;
                var drug = CampaignStateUtility.Drug(c, doses[i].DefinitionId);
                effects.AddRange(drug.Effects);
                volume *= drug.VolumeMultiplier;
                switch (drug.Kind)
                {
                    case DrugKind.LegacyBounce:
                        effects.Add(new AttributeEffect { Attribute = GameAttribute.BALL_BOUNCE,
                            Operation = AttributeOperation.Multiply, Value = drug.RestitutionMultiplier });
                        break;
                    case DrugKind.Adrenaline: features.RushChanceAdd += .2; break;
                    case DrugKind.RushFruit: features.RushScoreMultiplier *= 1.15; break;
                    case DrugKind.ThinBlood: features.PierceChance = 1 - (1 - features.PierceChance) * .7f; break;
                    case DrugKind.Replicator: features.Replicates = true; break;
                    case DrugKind.Magnetic: features.Magnetic = true; features.MagneticRadius = 10 * c.Content.CentimetersToUnits; break;
                    case DrugKind.Direction: features.ExitDown = true; break;
                }
            }
            values[(int)GameAttribute.BALL_RADIUS] *= Math.Pow(volume, 1d / 3d);
            for (int i = 0; i < values.Length; i++) values[i] = AttributeMath.Evaluate(c.Content.Definition((GameAttribute)i), values[i], effects);
            return new Spawn
            {
                Values = values, Features = features, Count = (int)values[(int)GameAttribute.BALL_SPAWN_COUNT],
                Quote = new ShotQuote(investment, (float)values[(int)GameAttribute.BLOOD_COST],
                    (float)values[(int)GameAttribute.BALL_RADIUS] * RadiusToWorld,
                    (float)values[(int)GameAttribute.BALL_MASS], values[(int)GameAttribute.BALL_BASE_VALUE], 1)
            };
        }

        internal static void Tick(SimulationContext c)
        {
            if (!Enabled(c)) return;
            var drugs = c.Manager.GetBuffer<ActiveDrugData>(c.PlayerEntity);
            for (int i = drugs.Length - 1; i >= 0; i--) if (drugs[i].EndsAt <= c.Round.Time) drugs.RemoveAt(i);
            Recompute(c, c.PlayerEntity);
            var round = c.Round;
            round.RushRemaining = Math.Max(0, round.RushRemaining - c.StepSeconds * Global(c, GameAttribute.RUSH_DECAY));
            if (round.RushRemaining == 0) round.RushMeter = Math.Max(0, round.RushMeter - c.StepSeconds * Global(c, GameAttribute.RUSH_DECAY));
            c.Round = round;
            using (var entities = c.MarbleQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
                foreach (var entity in entities)
                {
                    Recompute(c, entity);
                    var lifeFeatures = c.Manager.GetComponentData<BallFeatures>(entity);
                    c.Manager.SetComponentData(entity, new Lifetime { ExpireTick = lifeFeatures.BirthTick +
                        (long)Math.Ceiling(Get(c, entity, GameAttribute.BALL_LIFETIME) / c.StepSeconds) });
                    if (c.Physics is IMarblePhysicsCommands commands)
                    {
                        var shot = c.Manager.GetComponentData<ShotOrigin>(entity);
                        commands.SetProperties(new MarbleKey(shot.RoundId, shot.SpawnSequence), Properties(c, entity));
                    }
                }
        }

        internal static MarblePhysicalSettings Properties(SimulationContext c, Entity entity)
        {
            var f = c.Manager.GetComponentData<BallFeatures>(entity);
            return new MarblePhysicalSettings
            {
                Radius = (float)Get(c, entity, GameAttribute.BALL_RADIUS) * RadiusToWorld,
                Mass = (float)Get(c, entity, GameAttribute.BALL_MASS), Bounce = (float)Get(c, entity, GameAttribute.BALL_BOUNCE),
                Friction = (float)Get(c, entity, GameAttribute.BALL_FRICTION), Gravity = (float)Get(c, entity, GameAttribute.BALL_GRAVITY_MOD),
                MaxSpeed = (float)Get(c, entity, GameAttribute.BALL_MAX_SPEED), MagnetResponse = (float)Get(c, entity, GameAttribute.BALL_MAGNET_RESPONSE),
                PierceCount = Math.Max(0, (int)Get(c, entity, GameAttribute.BALL_PIERCE_COUNT) - f.PiercesUsed), PierceChance = f.PierceChance,
                Magnetic = f.Magnetic, MagneticRadius = f.MagneticRadius
            };
        }
    }
}

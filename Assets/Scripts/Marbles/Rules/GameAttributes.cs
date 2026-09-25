using System;
using System.Collections.Generic;

namespace MarblesECS
{
    public enum GameAttribute : byte
    {
        BLOOD_COST, BLOOD_DIVIDEND_RATE, BALL_SPAWN_COUNT, BALL_BASE_VALUE,
        BALL_GAMBLE_VALUE_MOD, BALL_RUSH_GAIN, BALL_RADIUS, BALL_MASS, BALL_BOUNCE,
        BALL_FRICTION, BALL_GRAVITY_MOD, BALL_LAUNCH_SPEED, BALL_MAX_SPEED,
        BALL_MAGNET_RESPONSE, BALL_LIFETIME, BALL_SPLIT_COUNT, BALL_CLONE_VALUE_RETENTION,
        BALL_PIERCE_COUNT, MACHINE_MULT, GLOBAL_MULT, SCORE_BY_SPEED, SCORE_OVERKILL_RATE,
        RUSH_DURATION, RUSH_MULT, RUSH_DECAY, GAMBLE_LOSS_CHANCE, GAMBLE_RETURN_CHANCE,
        GAMBLE_WIN_CHANCE, GAMBLE_POT_VALUE, DEVICE_TRIGGER_RATE, DEVICE_COOLDOWN,
        SHOP_PRICE_MOD, COIN_GAIN_RATE, GAMBLE_QUADRUPLE_CHANCE, Count
    }

    public enum AttributeOperation : byte { Add, Multiply, Set }

    [Serializable]
    public sealed class AttributeDefinition
    {
        public string Id, Name, Unit, Operators, StackRule, AppliesTo;
        public double DefaultValue, MinValue, MaxValue;
        public int StackOrder;
        public AttributeDefinition Copy() => (AttributeDefinition)MemberwiseClone();
        public bool Allows(AttributeOperation operation) => ("," + Operators + ",").Contains("," +
            (operation == AttributeOperation.Add ? "ADD" : operation == AttributeOperation.Multiply ? "MUL" : "SET") + ",");
        public double Clamp(double value)
        {
            if (double.IsNaN(value)) throw new ArgumentException("NaN attribute: " + Id);
            value = Math.Max(MinValue, Math.Min(MaxValue, value));
            return Unit == "count" ? Math.Floor(value) : value;
        }
        public void Validate()
        {
            if (!Enum.TryParse(Id, out GameAttribute id) || id == GameAttribute.Count ||
                !MarbleRules.IsFinite(DefaultValue) || !MarbleRules.IsFinite(MinValue) ||
                !MarbleRules.IsFinite(MaxValue) || MinValue > DefaultValue || DefaultValue > MaxValue ||
                string.IsNullOrEmpty(Operators) || string.IsNullOrEmpty(StackRule))
                throw new ArgumentException("Invalid attribute definition: " + Id);
        }
    }

    [Serializable]
    public struct AttributeEffect
    {
        public GameAttribute Attribute;
        public AttributeOperation Operation;
        public double Value;
        public int Priority;
    }

    public static class AttributeMath
    {
        // SET chooses the highest priority, then the last authored effect at that priority.
        // Clamp once, after all operations. An explicit zero multiplier annihilates the product.
        public static double Evaluate(AttributeDefinition definition, double basis, IEnumerable<AttributeEffect> effects)
        {
            double add = 0, multiply = 1;
            bool zero = false;
            int priority = int.MinValue;
            var id = (GameAttribute)Enum.Parse(typeof(GameAttribute), definition.Id);
            foreach (var effect in effects)
            {
                if (effect.Attribute != id) continue;
                if (!definition.Allows(effect.Operation) || !MarbleRules.IsFinite(effect.Value))
                    throw new ArgumentException("Invalid operation for " + definition.Id);
                switch (effect.Operation)
                {
                    case AttributeOperation.Set:
                        if (effect.Priority >= priority) { basis = effect.Value; priority = effect.Priority; }
                        break;
                    case AttributeOperation.Add: add += effect.Value; break;
                    case AttributeOperation.Multiply:
                        if (effect.Value < 0) throw new ArgumentOutOfRangeException(nameof(effect.Value));
                        zero |= effect.Value == 0; multiply *= effect.Value; break;
                }
            }
            double value = definition.StackRule.StartsWith("MUL", StringComparison.Ordinal)
                ? (zero ? 0 : basis * multiply) + add : zero ? 0 : (basis + add) * multiply;
            return definition.Clamp(value);
        }

        public static int GamblingOutcome(double draw, double loss, double refund, double win, double quadruple)
        {
            double total = loss + refund + win + quadruple;
            if (!MarbleRules.IsFinite(total) || total <= 0 || loss < 0 || refund < 0 || win < 0 || quadruple < 0)
                throw new ArgumentException("Gambling probabilities must have a positive sum.");
            double pick = draw * total;
            return pick < loss ? 0 : pick < loss + refund ? 1 : pick < loss + refund + win ? 2 : 4;
        }
    }
}

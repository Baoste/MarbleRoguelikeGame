using System;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace MarblesECS
{
    /// <summary>Generic game operations exposed to device callbacks. Valid for that callback only.</summary>
    [Preserve]
    public sealed class DeviceContactContext
    {
        private SimulationContext c;
        private readonly Entity ball, deviceEntity;
        private readonly MarbleContact contact;
        private readonly DevicePose pose;
        private readonly DeviceDefinition definition;
        private readonly DeviceHoldInfo hold;
        private readonly DeviceImpactInfo impact;
        private bool triggered;
        private bool usageGranted;
        internal DeviceContactContext(SimulationContext c, Entity ball, Entity device, MarbleContact contact, DevicePose pose,
            HeldMotion? completedHold = null)
        {
            this.c = c; this.ball = ball; deviceEntity = device; this.contact = contact; this.pose = pose;
            definition = CampaignStateUtility.Device(c, c.Manager.GetComponentData<OwnedDeviceData>(device).DefinitionId);
            Motion = new DeviceMotionApi(this, ball, device, Device.InstanceId, pose);
            Vectors = new DeviceVectorApi(this);
            if (completedHold.HasValue) hold = new DeviceHoldInfo(this, completedHold.Value, c.Round.Time);
            else if (contact.HasImpact) impact = new DeviceImpactInfo(this, contact);
        }
        internal void Close() { c = null; }
        private SimulationContext C => c ?? throw new InvalidOperationException("The Lua device callback has ended.");
        internal SimulationContext Context => C;
        internal MarbleContact Contact { get { _ = C; return contact; } }
        public DeviceMotionApi Motion { get; }
        public DeviceHoldInfo Hold { get { _ = C; return hold; } }
        public DeviceImpactInfo Impact { get { _ = C; return impact; } }
        public DeviceVectorApi Vectors { get; }
        private OwnedDeviceData Device => C.Manager.GetComponentData<OwnedDeviceData>(deviceEntity);
        private BallFeatures Features => C.Manager.GetComponentData<BallFeatures>(ball);
        public double Value => Get("BALL_BASE_VALUE");
        public double Parameter => definition.Value;
        public double Strength => definition.Strength;
        public double ScoreMultiplier => definition.ScoreMultiplier;
        public double RushChanceAdd => definition.RushChanceAdd;
        public float Speed => Motion.Speed;
        public long LinkKey => contact.LinkKey;
        public int PairId => Device.PairId;
        public double Time => C.Round.Time;
        public int RoundId => C.Round.RoundId;
        public string BallId
        {
            get
            {
                var shot = C.Manager.GetComponentData<ShotOrigin>(ball);
                return new MarbleKey(shot.RoundId, shot.SpawnSequence).ToString();
            }
        }
        public float DeltaTime => C.StepSeconds;
        public float Radius => (float)Get("BALL_RADIUS") * AttributeRuntime.RadiusToWorld;
        public float Range { get { _ = C; return definition.Range; } }
        public int RemainingDurability => DeviceDurabilitySystem.Remaining(C, Device);
        /// <summary>Accept one use for this callback. Trigger/Consume/TrySettle share the same use.</summary>
        public bool TryUse()
        {
            _ = C;
            if (hold != null) return true; // Completion of a previously accepted effect is not another use.
            return usageGranted || (usageGranted = DeviceDurabilitySystem.TryUse(C, deviceEntity));
        }
        public double TransportLockedUntil { get => Features.PortalLockedUntil; set { var f = Features; f.PortalLockedUntil = value; C.Manager.SetComponentData(ball, f); } }
        public bool ExitDown { get => Features.ExitDown; set { var f = Features; f.ExitDown = value; C.Manager.SetComponentData(ball, f); } }
        public double StoredValue { get => Device.StoredValue; set { var d = Device; d.StoredValue = value; C.Manager.SetComponentData(deviceEntity, d); } }
        public double RushOnSettlement { get => Features.CloverChance; set { var f = Features; f.CloverChance = (float)value; C.Manager.SetComponentData(ball, f); } }
        public bool RefundOnDespawn { get => Features.Revive; set { var f = Features; f.Revive = value; C.Manager.SetComponentData(ball, f); } }
        public double Get(string attribute) => AttributeRuntime.Get(C, ball, Parse(attribute));
        public double Global(string attribute) => AttributeRuntime.Global(C, Parse(attribute));
        public void Set(string attribute, double value) => AttributeRuntime.SetBase(C, ball, Parse(attribute), value);
        private static GameAttribute Parse(string name) => (GameAttribute)Enum.Parse(typeof(GameAttribute), name, false);
        /// <summary>Per-ball visit tracking without a device-wide cooldown.</summary>
        public bool Visit(long visitKey, int maximum) => BallInteractionSystem.MarkVisit(C, ball,
            visitKey == 0 ? contact.TargetId : visitKey, maximum);
        /// <summary>Rate, cooldown, per-ball visit limit and generic trigger modifiers.</summary>
        public bool Trigger(long visitKey = 0)
        {
            if (triggered || hold != null || (!usageGranted && RemainingDurability == 0)) return false;
            AttributeRuntime.Recompute(C, deviceEntity);
            double rate = Get("DEVICE_TRIGGER_RATE") * AttributeRuntime.Get(C, deviceEntity, GameAttribute.DEVICE_TRIGGER_RATE);
            var d = Device;
            if (rate <= 0 || d.NextTriggerAt > Time) return false;
            if (!BallInteractionSystem.MarkVisit(C, ball, visitKey != 0 ? visitKey : contact.TargetId, definition.MaxTriggersPerBall)) return false;
            if (!TryUse()) return false;
            d = Device; // TryUse updates the same component; preserve its durability counters.
            d.NextTriggerAt = Time + Math.Max(C.StepSeconds, AttributeRuntime.Device(C, deviceEntity, GameAttribute.DEVICE_COOLDOWN) / rate);
            C.Manager.SetComponentData(deviceEntity, d); triggered = true;
            double multiplier = AttributeRuntime.Get(C, deviceEntity, GameAttribute.MACHINE_MULT);
            if (multiplier != 1) Set("MACHINE_MULT", Get("MACHINE_MULT") * multiplier);
            if (Features.Replicates) BallInteractionSystem.Replicate(C, ball, contact);
            return true;
        }
        public void Consume(bool refund)
        { if (TryUse()) BallInteractionSystem.Consume(C, ball, refund); }
        /// <summary>Finalize this ball for an explicit score amount, without implicit multipliers or Rush rolls.</summary>
        public bool TrySettle(double score)
        {
            _ = C;
            if (!MarbleRules.IsFinite(score) || score < 0 || C.Manager.GetComponentData<SettlementState>(ball).IsSettled ||
                C.Manager.GetComponentData<DespawnState>(ball).PendingDespawn || !TryUse()) return false;
            return ContentSettlementSystem.TrySettleAmount(C, ball, contact, score);
        }
        public void SetStatus(string text) => C.DeviceScripts.SetStatus(Device.InstanceId, RoundId, text);
        public void AddCoins(double value)
        {
            var state = C.Campaign;
            state.Coins = MarbleRules.AddScore(state.Coins, MarbleRules.RoundScore(value, CampaignStateUtility.MaxCoins), CampaignStateUtility.MaxCoins);
            C.Campaign = state;
        }
        // Compatibility aliases for previously authored packages. New Lua uses Motion directly.
        public bool ScaleVelocity(float scale) => Motion.TryScaleSpeed(scale);
        public bool Exit(string direction, float speed, float angle = 0) => ExitAt(Motion.Source, direction, speed, angle);
        private bool ExitAt(DeviceEndpoint target, string direction, float speed, float angle)
        {
            if (target == null) return false;
            var heading = direction == "down" ? target.Down : target.Forward;
            return Motion.TryLaunch(target, target.Rotate(heading, angle), speed);
        }
        public bool ExitPartner(string direction, float speed) => ExitAt(Motion.FindPartner(), direction, speed, 0);
        public void Children(int count, bool replace) => ContentSpawnSystem.Children(C, ball, count, replace, BallInteractionSystem.Down(pose), pose.Up);
        public bool Roll(double probability)
        { var round = C.Round; bool result = MarbleRules.Roll(ref round.RandomState, probability); C.Round = round; return result; }
        /// <summary>Uniform [minimum, maximum) using the round's RNG; equal bounds return without consuming RNG.</summary>
        public double RandomRange(double minimum, double maximum)
        {
            var context = C;
            if (!MarbleRules.IsFinite(minimum) || !MarbleRules.IsFinite(maximum) || minimum > maximum ||
                !MarbleRules.IsFinite(maximum - minimum)) throw new ArgumentOutOfRangeException(nameof(maximum));
            if (minimum == maximum) return minimum;
            var round = context.Round;
            double result = minimum + (maximum - minimum) * MarbleRules.NextRandom(ref round.RandomState);
            context.Round = round;
            return result;
        }
        public void BreakPinsOnCross() => ((IMarblePhysicsCommands)C.Physics).BreakPinsOnCross(Device.InstanceId);
        public void RemoveDevice()
        { C.Manager.DestroyEntity(deviceEntity); var state = C.Campaign; state.LayoutRevision++; C.Campaign = state; }
        public void MultiplyScore(double factor)
        { var s = C.Manager.GetComponentData<MarbleScore>(ball); s.ScoreMultiplier = MarbleRules.CombineDeviceMultiplier(s.ScoreMultiplier, factor); C.Manager.SetComponentData(ball, s); }
        public void AddRushChance(double amount)
        { var r = C.Manager.GetComponentData<MarbleRush>(ball); r.RushChanceBonus = Math.Min(1, r.RushChanceBonus + amount); C.Manager.SetComponentData(ball, r); }
    }
}

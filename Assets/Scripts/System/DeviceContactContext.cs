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
        /// <summary>Device contributions only: one base plus accumulated factor-minus-one bonuses.</summary>
        public double DeviceScoreMultiplier => Math.Max(0, Math.Min(1000,
            1 + C.Manager.GetComponentData<MarbleScore>(ball).DeviceMultiplierBonus));
        /// <summary>Successful effects from any other placed device instance during this ball's lifetime.</summary>
        public bool HasTriggeredOtherDevice
        {
            get
            {
                var f = Features;
                return f.TriggeredMultipleDevices || (f.FirstTriggeredDeviceId != 0 && f.FirstTriggeredDeviceId != InstanceId);
            }
        }
        /// <summary>Records Lua-only effects; independent of visit limits, cooldown and durability.</summary>
        public void MarkTriggered() => BallInteractionSystem.RecordDeviceEffect(C, ball, InstanceId);
        internal bool RecordEffect(bool success) { if (success) MarkTriggered(); return success; }
        /// <summary>Current wallet balance as a Lua number; read only and exact through the wallet cap.</summary>
        public double Coins => C.Campaign.Coins;
        public double Blood => C.Player.Blood;
        public double BloodCapacity => C.Tuning.BloodCapacity;
        public double RestoreBlood(double amount)
        {
            double restored = PlayerBloodSystem.Restore(C, amount);
            if (restored > 0) MarkTriggered();
            return restored;
        }
        public double Parameter => definition.Value;
        public double Strength => definition.Strength;
        public double ScoreMultiplier => definition.ScoreMultiplier;
        public double RushChanceAdd => definition.RushChanceAdd;
        public float Speed => Motion.Speed;
        public long LinkKey => contact.LinkKey;
        public int PairId => Device.PairId;
        public int InstanceId => Device.InstanceId;
        public int SpawnSourceId => Features.SpawnSourceId;
        public DeviceSpawnBatch CreateSpawnBatch() => new DeviceSpawnBatch(() => C, InstanceId);
        public DeviceCopy CreateCopy() => new DeviceCopy(() => C, ball, InstanceId);
        public bool HasCopiedFrom(int instanceId)
        {
            var sources = C.Manager.GetBuffer<DeviceCopySource>(ball);
            foreach (var source in sources) if (source.InstanceId == instanceId) return true;
            return false;
        }
        public DeviceSplitBatch CreateSplitBatch(int count) => new DeviceSplitBatch(() => C, ball, InstanceId, count);
        public bool HasSplitFrom(int instanceId)
        {
            var sources = C.Manager.GetBuffer<DeviceSplitSource>(ball);
            foreach (var source in sources) if (source.InstanceId == instanceId) return true;
            return false;
        }
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
        public int HeldCount => HeldMotionSystem.Count(C, Device.InstanceId);
        public bool CanUseOnce(string key) => DeviceDurabilitySystem.CanUseOnce(C, deviceEntity, key);
        public bool TryUseOnce(string key) => DeviceDurabilitySystem.TryUseOnce(C, deviceEntity, key);
        public bool TryKeepActive(float seconds) => DeviceDurabilitySystem.KeepActive(C, deviceEntity, seconds);
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
        public void Set(string attribute, double value)
        {
            var id = Parse(attribute); double before = AttributeRuntime.Get(C, ball, id);
            AttributeRuntime.SetBase(C, ball, id, value);
            if (AttributeRuntime.Get(C, ball, id) != before) MarkTriggered();
        }
        /// <summary>Adds to the stored base attribute, then evaluates current modifiers once.</summary>
        public void AddBase(string attribute, double amount)
        {
            var context = C;
            var id = Parse(attribute);
            double value = context.Manager.GetBuffer<AttributeValue>(ball)[(int)id].Base + amount;
            if (!MarbleRules.IsFinite(amount) || !MarbleRules.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(amount));
            double before = AttributeRuntime.Get(context, ball, id);
            AttributeRuntime.SetBase(context, ball, id, value);
            if (AttributeRuntime.Get(context, ball, id) != before) MarkTriggered();
        }
        private static GameAttribute Parse(string name) => (GameAttribute)Enum.Parse(typeof(GameAttribute), name, false);
        /// <summary>Replace this attribute's current value and basis, removing its existing per-ball modifiers.</summary>
        public void SetCurrent(string attribute, double value)
        {
            var context = C; var id = Parse(attribute);
            if (!MarbleRules.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            double replacement = context.Content.Definition(id).Clamp(value);
            double before = AttributeRuntime.Get(context, ball, id);
            var effects = context.Manager.GetBuffer<AttributeModifierData>(ball);
            bool removed = false;
            for (int i = effects.Length - 1; i >= 0; i--)
                if (effects[i].Effect.Attribute == id) { effects.RemoveAt(i); removed = true; }
            AttributeRuntime.SetBase(context, ball, id, replacement);
            if (removed || replacement != before) MarkTriggered();
        }
        /// <summary>Per-ball visit tracking without a device-wide cooldown.</summary>
        public bool Visit(long visitKey, int maximum) => BallInteractionSystem.MarkVisit(C, ball,
            visitKey == 0 ? contact.TargetId : visitKey, maximum);
        /// <summary>Rate, cooldown, per-ball visit limit and generic trigger modifiers.</summary>
        public bool Trigger(long visitKey = 0) => PrepareTrigger(visitKey, true);
        /// <summary>Trigger checks/modifiers without spending durability; the script charges the later effect explicitly.</summary>
        public bool TryPrepareTrigger(long visitKey = 0) => PrepareTrigger(visitKey, false);
        private bool PrepareTrigger(long visitKey, bool spendUse)
        {
            if (triggered || hold != null || (!usageGranted && RemainingDurability == 0)) return false;
            AttributeRuntime.Recompute(C, deviceEntity);
            double rate = Get("DEVICE_TRIGGER_RATE") * AttributeRuntime.Get(C, deviceEntity, GameAttribute.DEVICE_TRIGGER_RATE);
            var d = Device;
            if (rate <= 0 || d.NextTriggerAt > Time) return false;
            if (!BallInteractionSystem.MarkVisit(C, ball, visitKey != 0 ? visitKey : contact.TargetId, definition.MaxTriggersPerBall)) return false;
            if (spendUse && !TryUse()) return false;
            d = Device; // TryUse updates the same component; preserve its durability counters.
            d.NextTriggerAt = Time + Math.Max(C.StepSeconds, AttributeRuntime.Device(C, deviceEntity, GameAttribute.DEVICE_COOLDOWN) / rate);
            C.Manager.SetComponentData(deviceEntity, d); triggered = true;
            double multiplier = AttributeRuntime.Get(C, deviceEntity, GameAttribute.MACHINE_MULT);
            if (multiplier != 1) AddScoreMultiplier(multiplier);
            if (Features.Replicates) BallInteractionSystem.Replicate(C, ball, contact);
            return true;
        }
        public void Consume(bool refund)
        { if (TryUse()) BallInteractionSystem.Consume(C, ball, refund); }
        /// <summary>Consumes only; composing a charge or settlement batch does not implicitly spend durability.</summary>
        public bool TryConsume(bool refund)
        {
            if (C.Manager.GetComponentData<SettlementState>(ball).IsSettled || C.Manager.GetComponentData<DespawnState>(ball).PendingDespawn) return false;
            BallInteractionSystem.Consume(C, ball, refund); return true;
        }
        /// <summary>Finalize this ball for an explicit score amount, without implicit multipliers or Rush rolls.</summary>
        public bool TrySettle(double score)
        {
            _ = C;
            if (!MarbleRules.IsFinite(score) || score < 0 || C.Manager.GetComponentData<SettlementState>(ball).IsSettled ||
                C.Manager.GetComponentData<DespawnState>(ball).PendingDespawn || !TryUse()) return false;
            return ContentSettlementSystem.TrySettleAmount(C, ball, contact, score);
        }
        /// <summary>Exact settlement only. The script registers the owning effect's durability separately.</summary>
        public bool TrySettleAmount(double score)
        {
            if (!MarbleRules.IsFinite(score) || score < 0 || C.Manager.GetComponentData<SettlementState>(ball).IsSettled ||
                C.Manager.GetComponentData<DespawnState>(ball).PendingDespawn) return false;
            return ContentSettlementSystem.TrySettleAmount(C, ball, contact, score);
        }
        public void SetStatus(string text) => C.DeviceScripts.SetStatus(Device.InstanceId, RoundId, text);
        public void AddCoins(double value)
        {
            var state = C.Campaign;
            state.Coins = MarbleRules.AddScore(state.Coins, MarbleRules.RoundScore(value, CampaignStateUtility.MaxCoins), CampaignStateUtility.MaxCoins);
            C.Campaign = state;
            if (value >= 1) MarkTriggered();
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
        /// <summary>Adds (amount - 1) to the device bonus; ordinary settlement applies 1 + total bonus.</summary>
        public void AddScoreMultiplier(double amount)
        {
            var context = C;
            var score = context.Manager.GetComponentData<MarbleScore>(ball);
            score.AddDeviceMultiplier(amount);
            context.Manager.SetComponentData(ball, score);
            if (amount != 1) MarkTriggered();
        }
        /// <summary>Legacy package name; now uses the same additive rule as AddScoreMultiplier.</summary>
        public void MultiplyScore(double factor) => AddScoreMultiplier(factor);
        public void AddRushChance(double amount)
        { var r = C.Manager.GetComponentData<MarbleRush>(ball); r.RushChanceBonus = Math.Min(1, r.RushChanceBonus + amount); C.Manager.SetComponentData(ball, r); }
    }
}

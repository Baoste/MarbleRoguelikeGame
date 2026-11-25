using System.Linq;
using MarblesECS.PhysX;
using UnityEngine;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static void FloatingTextFeedbackValidation()
        {
            var config = FloatingTextConfig.Load();
            if (!config.Enabled) return;
            long gross = 0;
            uint random = 0;
            foreach (bool visible in new[] { false, true })
            using (var f = new Fixture())
            {
                var events = visible ? new FloatingTextEventBuffer(config, 32) : null;
                f.C.FloatingText = events;
                var mult = f.Add(f.C.Balance.Devices.Single(device => device.Id == 113));
                var sandbag = f.Add(f.C.Balance.Devices.Single(device => device.Id == 9008));
                var key = f.Launch();
                var hit = f.Hit(key, sandbag);
                f.Step(hit);
                Check(events == null || events.First < 0, "a sensor entry that Sandbag rejects emits no numeric feedback");

                hit.HasImpact = true;
                hit.Normal = Vector3.up;
                hit.IncomingVelocity = Vector3.down;
                hit.Position = Vector3.right;
                double before = f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE);
                f.Step(hit);
                double added = f.Simulation.GetMarbleAttribute(key, GameAttribute.BALL_BASE_VALUE) - before;
                Check(added > 0, "shipped Sandbag actually applies the value change");
                if (visible) CheckText(events, FloatingTextType.Additive, FloatingTextSource.Device, sandbag.InstanceId, added, hit.Position);

                var pinObject = new GameObject("Validation Pin");
                try
                {
                    var pin = pinObject.AddComponent<MarblePin>();
                    pin.TargetId = 99991;
                    pin.ScoreMultiplier = 2;
                    var pinHit = pin.CreateContact(key, Vector3.up);
                    f.Step(pinHit, pinHit);
                    if (visible) CheckText(events, FloatingTextType.Multiplier, FloatingTextSource.Pin, pin.TargetId, 2, pinHit.Position);
                    pin.TargetId++;
                    pin.ScoreMultiplier = 1;
                    f.Step(pin.CreateContact(key, Vector3.up));
                    Check(events == null || events.First < 0, "neutral pins are quiet by default");
                }
                finally { Object.DestroyImmediate(pinObject); }

                f.Step(f.Hit(key, mult));
                if (visible) CheckText(events, FloatingTextType.Multiplier, FloatingTextSource.Device, mult.InstanceId, 1.85, Vector3.zero);
                f.Step(f.Hit(key, mult));
                Check(events == null || events.First < 0, "a repeated per-ball Lua trigger does not publish again");

                var round = f.C.Round;
                round.Score = f.C.Tuning.MaxRoundScore - 3;
                f.C.Round = round;
                var terminal = new MarbleContact { Key = key, TargetId = 41, Kind = MarbleContactKind.Score, Multiplier = 1, Position = Vector3.left };
                f.Step(terminal, terminal);
                Check(f.Simulation.TryDequeueScore(out var score) && score.Score > 3 && score.RoundScore == f.C.Tuning.MaxRoundScore &&
                    !f.Simulation.TryDequeueScore(out _), "terminal contact commits once and preserves the score event contract");
                if (visible)
                {
                    CheckText(events, FloatingTextType.Score, FloatingTextSource.Zone, terminal.TargetId, 3, terminal.Position, exact: 3);
                    Check(score.Score == gross && f.C.Round.RandomState == random, "enabling floating text preserves gameplay score and RNG");
                    uint revision = events.Revision;
                    f.Simulation.StartSession(f.Physics);
                    Check(events.Revision != revision && events.First < 0, "reset clears queued text and invalidates the presentation batch");
                }
                gross = score.Score;
                random = f.C.Round.RandomState;
            }

            using (var f = new Fixture())
            {
                var events = new FloatingTextEventBuffer(config, 16);
                f.C.FloatingText = events;
                var device = f.Add(f.C.Balance.Devices.Single(d => d.Id == 113));
                var key = f.Launch();
                CampaignStateUtility.FindDevice(f.C, device.InstanceId, out var entity);
                using var script = new DeviceLuaScript(@"
function Effect(api)
    api:EmitFloatingText('critical', 10)
    api:EmitFloatingText('unknown', 1)
end", null, "Floating text validation");
                var api = new DeviceContactContext(f.C, f.Ball(key), entity, f.Hit(key, device), f.Physics.Poses[device.InstanceId]);
                try
                {
                    script.Invoke("Effect", api);
                    CheckText(events, FloatingTextType.Critical, FloatingTextSource.Device, device.InstanceId, 10, Vector3.zero);
                    Check(api.TrySettleAmount(8), "Lua can commit an explicit score");
                    CheckText(events, FloatingTextType.Score, FloatingTextSource.Device, device.InstanceId, 8, Vector3.zero, exact: 8);
                }
                finally { api.Close(); }

                var deviceApi = new DeviceEventContext(f.C, entity);
                try
                {
                    script.Invoke("Effect", deviceApi);
                    CheckText(events, FloatingTextType.Critical, FloatingTextSource.Device, device.InstanceId, 10, f.Physics.Poses[device.InstanceId].Position);
                    Check(deviceApi.Board.AddScoreMultiplier(1.5, 2) > 0, "Lua can register a timed board multiplier");
                    CheckText(events, FloatingTextType.Multiplier, FloatingTextSource.Device, device.InstanceId, 1.5, f.Physics.Poses[device.InstanceId].Position);
                }
                finally { deviceApi.Close(); }

                var anchor = new MarbleContact { Key = f.Launch(), TargetId = 23, Kind = MarbleContactKind.RandomScore,
                    Position = Vector3.forward, Multiplier = 1 };
                f.Step(anchor);
                Check(events.First < 0, "a pending wheel deposit is not final score feedback");
                Check(GamblingWheelSystem.Draw(f.C), "wheel resolves a pending deposit");
                long change = f.C.Round.WheelLastScoreChange;
                CheckText(events, FloatingTextType.Score, FloatingTextSource.Zone, anchor.TargetId, change, anchor.Position, exact: change);
            }
        }

        private static void CheckText(FloatingTextEventBuffer events, FloatingTextType type, FloatingTextSource source,
            int id, double value, Vector3 position, long? exact = null)
        {
            int first = events.First;
            Check(first >= 0 && events.Next(first) < 0, "one presentation event: " + type);
            var text = events.Get(first).Event;
            Check(text.Type == type && text.Source == source && text.SourceId == id && text.Position == position &&
                text.ExactValue == exact, "presentation event retains type, source, position and numeric representation: " + type);
            Near(value, text.Value, "presentation value: " + type);
            events.ClearPending();
        }
    }
}

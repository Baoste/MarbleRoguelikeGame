using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MRes;

namespace MarblesECS.Editor
{
    public static partial class GameContentValidation
    {
        private static void StarLampBoardApi()
        {
            using (var f = new Fixture())
            {
                var a = AddStarLamp(f); var b = AddStarLamp(f); var key = f.Launch();
                var api = CashApi(f, key, a); var board = api.Board;
                int first = board.AddScoreMultiplier(3, 1), second = board.AddScoreMultiplier(1.5, 2);
                Check(first > 0 && second > first, "timed effects have distinct handles");
                Near(3.5, LampMultiplier(f, key), "independent temporary factors add their offsets from one");
                Near(f.C.Round.Time + 1, board.GetScoreMultiplierEndTime(first), "handle returns exact simulation deadline");
                Near(0, board.GetScoreMultiplierEndTime(int.MaxValue), "unknown handle is inactive");
                var endpoint = api.Motion.Source;
                var copy = api.CreateCopy(); Check(copy.TrySpawn(endpoint.Position + endpoint.Down, endpoint.Down * 6), "spawn copied ball while board bonus active");
                var children = f.C.Marbles.Keys.Where(k => !k.Equals(key)).ToArray(); Check(children.Length == 1, "one copy created");
                Near(3.5, LampMultiplier(f, children[0]), "copy receives one shared aura without double inheritance");
                Near(1, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(children[0])).ScoreMultiplier, "copy stores only permanent multiplier");
                var splitApi = CashApi(f, children[0], b); var split = splitApi.CreateSplitBatch(3);
                for (int i = -1; i <= 1; i++)
                {
                    var direction = endpoint.Rotate(endpoint.Down, i * 45);
                    Check(split.TryAdd(endpoint.Position + direction * 2, direction * 6), "prepare split during active aura");
                }
                Check(split.TryCommit(), "split copy while board effect is active"); splitApi.Close(); f.Step();
                children = f.C.Marbles.Keys.Where(k => !k.Equals(key)).ToArray(); Near(3, children.Length, "copy replaced by three split descendants");
                foreach (var child in children)
                { Near(3.5, LampMultiplier(f, child), "split descendant receives exactly one board contribution"); Near(1, f.C.Manager.GetComponentData<MarbleScore>(f.Ball(child)).ScoreMultiplier, "temporary multiplier not copied into split score"); }
                ExpectArgument(() => board.AddScoreMultiplier(-1, 1)); ExpectArgument(() => board.AddScoreMultiplier(1001, 1));
                ExpectArgument(() => board.AddScoreMultiplier(2, double.NaN)); ExpectArgument(() => board.AddScoreMultiplier(2, 0));
                ExpectArgument(() => board.AddScoreMultiplier(2, 61));
                Near(20, api.RemainingDurability, "board effect registration does not implicitly spend durability");
                double end = board.GetScoreMultiplierEndTime(first); api.Close();
                ExpectExpired(() => board.AddScoreMultiplier(2, 1)); ExpectExpired(() => board.GetScoreMultiplierEndTime(first));
                LampAdvance(f, end); Near(1.5, LampMultiplier(f, key), "effects expire independently");
                Near(1.5, LampMultiplier(f, children[0]), "copy also loses expired contribution");
                LampAdvance(f, end + 1); Near(1, LampMultiplier(f, key), "all temporary contributions removed");
                api = CashApi(f, key, b); api.SetDeviceScoreMultiplier(999); api.Board.AddScoreMultiplier(4, .2);
                Near(1000, LampMultiplier(f, key), "combined factor respects cap without erasing stored bonus");
                end = f.C.Round.Time + .2; api.Close(); LampAdvance(f, end); Near(999, LampMultiplier(f, key), "cap does not permanently consume existing bonus");
                CampaignStateUtility.FindDevice(f.C, a.InstanceId, out var entity);
                var tick = new DeviceEventContext(f.C, entity); int id = tick.Board.AddScoreMultiplier(2, 5); var retained = tick.Board;
                Check(id > 0, "device-only callback can register board effect"); tick.Close(); ExpectExpired(() => retained.GetScoreMultiplierEndTime(id));
                MorePlace(f.C, a.InstanceId, false); Near(999, LampMultiplier(f, key), "retrieval cancels source-owned effect");
                MorePlace(f.C, a.InstanceId, true); Near(999, LampMultiplier(f, key), "replacement cannot revive cancelled effect");
            }
        }
        private static void StarLampDispatchApi()
        {
            using (var f = new Fixture())
            {
                var a = AddStarLamp(f); var b = AddStarLamp(f); var inventory = AddStarLamp(f); MorePlace(f.C, inventory.InstanceId, false);
                int other = MoreCompanion(f.C, true); var key = f.Launch();
                var scripts = (Dictionary<int, MResLuaScript>)typeof(DeviceScriptSystem).GetField("scripts", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f.C.DeviceScripts);
                foreach (var owned in new[] { a, b })
                {
                    var package = DeviceResourceCatalog.Current.Get(9027).Package;
                    var probe = (MResLuaScript)Activator.CreateInstance(typeof(MResLuaScript), BindingFlags.NonPublic | BindingFlags.Instance, null,
                        new object[] { @"local saved
function Receive(api, value)
 saved = api
 api:SetRoundValue('received', value)
 api:SetRoundValue('order', api:CountPlacedDevicesWithRoundValue(api.DefinitionId, 'received', value))
end
function Fail(api, value) if api.InstanceId == " + a.InstanceId + @" then error('EXPECTED_DEVICE_EVENT_FAILURE') end; Receive(api, value) end
function Verify() assert(not pcall(function() return saved.Time end)) end", package.Metadata, "DeviceEventProbe", null }, null);
                    f.C.DeviceScripts.Remove(owned.InstanceId); scripts.Add(owned.InstanceId, probe);
                }
                var api = CashApi(f, key, a);
                Near(2, api.CountPlacedDevicesOfDefinition(9027), "definition count includes own placed instances only");
                Near(1, api.CountPlacedDevicesOfDefinition(9008), "definition count is not hardcoded to lamps");
                Near(2, api.ForEachPlacedDevice(9027, "Receive", 4.25), "dispatch includes caller and same-definition placed peer");
                CampaignStateUtility.FindDevice(f.C, a.InstanceId, out var ae); CampaignStateUtility.FindDevice(f.C, b.InstanceId, out var be);
                Near(1, DeviceRoundState.Get(f.C, ae, "order"), "dispatch order is stable instance order");
                Near(2, DeviceRoundState.Get(f.C, be, "order"), "later callback sees earlier writes");
                Near(4.25, DeviceRoundState.Get(f.C, be, "received"), "numeric payload arrives unchanged");
                scripts[a.InstanceId].Invoke("Verify"); scripts[b.InstanceId].Invoke("Verify");
                Near(1, api.ForEachPlacedDevice(9027, "Fail", 9), "recipient exception does not prevent other recipients");
                Near(9, DeviceRoundState.Get(f.C, be, "received"), "remaining recipient completes after failure");
                Near(0, api.ForEachPlacedDevice(9027, "Missing", 1), "missing callbacks skipped");
                Near(0, api.ForEachPlacedDevice(uint.MaxValue, "Receive", 1), "unknown definition has no recipients");
                var tick = new DeviceEventContext(f.C, be);
                Near(2, tick.ForEachPlacedDevice(9027, "Receive", 8), "event context can dispatch too");
                ExpectArgument(() => api.CountPlacedDevicesOfDefinition(0)); ExpectArgument(() => tick.CountPlacedDevicesOfDefinition(0));
                ExpectArgument(() => api.ForEachPlacedDevice(0, "Receive", 1)); ExpectArgument(() => api.ForEachPlacedDevice(9027, "", 1));
                ExpectArgument(() => api.ForEachPlacedDevice(9027, "Receive", double.NaN));
                var exhausted = f.C.Manager.GetComponentData<OwnedDeviceData>(be); exhausted.DurabilityRound = f.C.Round.RoundId; exhausted.UsesThisRound = 20; f.C.Manager.SetComponentData(be, exhausted);
                Near(2, api.CountPlacedDevicesOfDefinition(9027), "count still includes exhausted device");
                Near(1, api.ForEachPlacedDevice(9027, "Receive", 5), "exhausted non-continuing recipient cannot run gameplay callback");
                api.Close(); tick.Close(); ExpectExpired(() => api.ForEachPlacedDevice(9027, "Receive", 1));
                ExpectExpired(() => tick.CountPlacedDevicesOfDefinition(9027));
            }
        }
    }
}

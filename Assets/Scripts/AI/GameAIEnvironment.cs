using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MarblesECS.PhysX;
using UnityEngine;

namespace MarblesECS.AI
{
    /// <summary>One episode in the real scene. Commands are selected from a freshly validated legal list.</summary>
    public sealed class GameAIEnvironment
    {
        private readonly MarbleGameController controller;
        private readonly DevicePlacementUI placement;
        private readonly GameAIConfig config;
        private int actionsTaken, ticks;
        private bool truncated;
        private long accountedScore;
        private GameAIAction[] legal = Array.Empty<GameAIAction>();
        private readonly string contentHash;
        private readonly uint[] catalog;

        public GameAIEnvironment(MarbleGameController controller, DevicePlacementUI placement, GameAIConfig config)
        {
            this.controller = controller; this.placement = placement; this.config = config;
            config.Validate();
            if (controller == null || !controller.IsReady || placement == null || placement.BoardRoot == null)
                throw new InvalidOperationException("Scene requires a ready campaign controller and DevicePlacementUI.");
            if (!controller.AutoMoveLauncher) throw new InvalidOperationException("Protocol v1 requires automatic launcher movement.");
            placement.CancelPlacement(); placement.enabled = false;
            placement.SyncPlacedObjects();
            accountedScore = controller.Session.ConfirmedScore;
            catalog = controller.Balance.Devices.Select(x => x.Id).Concat(controller.Balance.Drugs.Select(x => x.Id))
                .Append(controller.Balance.Marble.MarbleDefinitionId).Distinct().OrderBy(x => x).ToArray();
            var hashBalance = controller.Balance.Copy(); hashBalance.Marble.RandomSeed = 1;
            var source = new StringBuilder(JsonUtility.ToJson(hashBalance));
            foreach (var resource in DeviceResourceCatalog.Current.Entries.OrderBy(x => x.File, StringComparer.Ordinal))
                source.Append(resource.File).Append(Hash(resource.Bytes));
            string manifest = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "manifest.json");
            if (File.Exists(manifest)) source.Append(File.ReadAllText(manifest));
            source.Append(controller.FixedStep.ToString("R", CultureInfo.InvariantCulture));
            source.Append(JsonUtility.ToJson(new GeometryFingerprint { entities = Entities().ToArray() }));
            contentHash = Hash(Encoding.UTF8.GetBytes(source.ToString()));
        }

        [Serializable] private sealed class GeometryFingerprint { public GameAIEntity[] entities; }

        public GameAIResponse Observe(float reward = 0)
        {
            var session = controller.Session;
            var snapshot = controller.Snapshot;
            bool terminal = session.Phase == RoundPhase.Won || session.Phase == RoundPhase.Lost;
            legal = terminal || truncated ? Array.Empty<GameAIAction>() : LegalActions().GroupBy(x => x.key).Select(x => x.First()).ToArray();
            var p = controller.LauncherLocalPosition;
            var globals = new float[26];
            globals[(int)session.Phase] = 1;
            globals[6] = snapshot.Blood / 100; globals[7] = session.Coins / 100f;
            globals[8] = session.ConfirmedScore / 1000f; globals[9] = session.TargetScore / 1000f;
            globals[10] = session.PendingScore / 1000f; globals[11] = session.StageIndex / 10f;
            globals[12] = session.StageCount / 10f; globals[13] = (float)snapshot.RushSecondsRemaining / 10;
            globals[14] = (float)snapshot.RushMeter / 100; globals[15] = (float)snapshot.RushMaximum / 100;
            globals[16] = (float)snapshot.TimeSeconds / 60; globals[17] = snapshot.Flow;
            globals[18] = snapshot.ActiveMarbles / 256f; globals[19] = p.x / 10;
            globals[20] = p.y / 10; globals[21] = p.z / 10;
            globals[22] = (float)snapshot.ShotsPerSecond / 20; globals[23] = controller.AIFireCooldown;
            globals[24] = controller.AILauncherPhase;
            globals[25] = controller.LauncherMoveSpeed / 10;
            var observation = new GameAIObservation { phase = session.Phase.ToString(), globals = globals,
                counters = new[] { session.TotalScore, session.ConfirmedScore, session.PendingScore, session.RoundScore,
                    session.Coins, session.TargetScore, snapshot.Tick, snapshot.RoundId, session.LayoutRevision },
                entities = Entities().ToArray(), actions = legal };
            observation.digest = Hash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(observation)));
            return new GameAIResponse { observation = observation, reward = reward, terminated = terminal,
                truncated = truncated, reason = truncated ? "budget" : terminal ? session.Phase.ToString() : "",
                finalScore = session.ConfirmedScore, steps = actionsTaken, ticks = ticks,
                contentHash = contentHash, catalogIds = catalog, unityVersion = Application.unityVersion };
        }

        public GameAIResponse Step(string actionKey)
        {
            var action = legal.FirstOrDefault(x => x.key == actionKey);
            if (action == null) throw new ArgumentException("Action is absent from the current legal list: " + actionKey);
            bool ok = true;
            // Each real-time action specifies the input for its own interval; no pending shot leaks across it.
            controller.AISimulation.ClearFireInput();
            switch (action.kind)
            {
                case "wait": break;
                case "fire": controller.SetFlow(action.value); controller.AISimulation.SetFireHeld(true); break;
                case "buy": ok = controller.BuyOffer(action.index); break;
                case "refresh": ok = controller.RefreshShop(); break;
                case "sell": ok = controller.SellDevice(action.id); break;
                case "build": ok = controller.EnterBuild(); break;
                case "place": ok = placement.PlaceAt(action.id, action.x, action.z); break;
                case "rotate": ok = controller.RotateDevice(action.id, action.value); break;
                case "remove": ok = controller.RemoveDevicePlacement(action.id); break;
                case "offer": ok = controller.TryOfferDevice(action.id, action.target); break;
                case "begin": placement.SyncPlacedObjects(); ok = controller.BeginRound(); break;
                case "cashout": ok = controller.CashOut(); break;
                case "drug": ok = controller.UseDrug(action.definitionId); break;
                case "activate": ok = controller.TryActivateDevice(action.id); break;
                default: throw new ArgumentException("Unknown action.");
            }
            if (!ok) throw new InvalidOperationException("A listed legal action was rejected: " + actionKey);
            actionsTaken++;
            placement.SyncPlacedObjects();
            var startingPhase = controller.Session.Phase;
            for (int i = 0; i < config.decisionTicks && ticks < config.maxTicks; i++)
            {
                var phase = controller.Session.Phase;
                if (phase != RoundPhase.Playing && phase != RoundPhase.Draining) break;
                controller.AdvanceAutomationTick(); ticks++;
                if (controller.Session.Phase != startingPhase) break;
            }
            controller.AISimulation.ClearFireInput();
            bool terminal = controller.Session.Phase == RoundPhase.Won || controller.Session.Phase == RoundPhase.Lost;
            truncated = !terminal && (actionsTaken >= config.maxActions || ticks >= config.maxTicks);
            long now = controller.Session.ConfirmedScore;
            // ConfirmedScore already avoids counting a round twice when it is committed to TotalScore.
            float reward = (now - accountedScore) / config.rewardScale;
            accountedScore = now;
            return Observe(reward);
        }

        private IEnumerable<GameAIAction> LegalActions()
        {
            var session = controller.Session;
            var devices = controller.GetOwnedDevices();
            if (session.Phase == RoundPhase.Playing || session.Phase == RoundPhase.Draining)
            {
                yield return Action("wait");
                if (session.Phase == RoundPhase.Playing)
                {
                    foreach (float flow in new[] { 0f, .5f, 1f })
                        if (controller.Snapshot.Blood >= controller.QuoteShot(flow).Cost)
                            yield return Action("fire", value: flow);
                    if (session.CanCashOut) yield return Action("cashout");
                    foreach (var drug in controller.GetDrugInventory())
                        if (drug.Count > 0 && CanUseDrug(drug)) yield return Action("drug", definition: drug.DefinitionId);
                }
                foreach (var device in devices)
                    if (controller.AICanActivate(device)) yield return Action("activate", device.InstanceId, definition: device.DefinitionId);
                yield break;
            }
            if (session.Phase == RoundPhase.Shop)
            {
                yield return Action("build");
                foreach (var offer in controller.GetShopOffers())
                    if (controller.AICanBuy(offer)) yield return Action("buy", index: offer.Index, definition: offer.DefinitionId,
                        value: offer.Price / 100f);
                if (session.Coins >= controller.ShopRefreshPrice) yield return Action("refresh");
            }
            if (session.Phase == RoundPhase.Shop || session.Phase == RoundPhase.Build)
                foreach (var device in devices) yield return Action("sell", device.InstanceId, definition: device.DefinitionId);
            if (session.Phase != RoundPhase.Build) yield break;
            yield return Action("begin");
            foreach (var device in devices)
            {
                if (device.Placed) yield return Action("remove", device.InstanceId, definition: device.DefinitionId);
                yield return Action("rotate", device.InstanceId, value: 45, definition: device.DefinitionId);
                yield return Action("rotate", device.InstanceId, value: -45, definition: device.DefinitionId);
                for (int row = 0; row < config.gridRows; row++)
                    for (int column = 0; column < config.gridColumns; column++)
                    {
                        var point = placement.PlanePointToBoard(
                            (column / (float)(config.gridColumns - 1) - .5f) * placement.PlacementPlaneSize.x,
                            (row / (float)(config.gridRows - 1) - .5f) * placement.PlacementPlaneSize.y);
                        if (placement.SnapToGrid && controller.GridSize > 0)
                        { point.x = Mathf.Round(point.x / controller.GridSize) * controller.GridSize;
                          point.z = Mathf.Round(point.z / controller.GridSize) * controller.GridSize; }
                        if (device.Placed && Mathf.Approximately(device.X, point.x) && Mathf.Approximately(device.Z, point.z)) continue;
                        if (placement.CanPlaceAt(device.InstanceId, point.x, point.z))
                            yield return Action("place", device.InstanceId, x: point.x, z: point.z, definition: device.DefinitionId);
                    }
                foreach (var target in devices)
                    if (controller.CanOfferDevice(device.InstanceId, target.InstanceId))
                        yield return Action("offer", device.InstanceId, target: target.InstanceId, definition: device.DefinitionId);
            }
        }

        private bool CanUseDrug(DrugInventorySnapshot drug)
        {
            var definition = CampaignStateUtility.Drug(controller.AISimulation.Context, drug.DefinitionId);
            if (definition.Kind != DrugKind.LegacyBounce) return true;
            int total = 0;
            foreach (var item in controller.GetDrugInventory())
                if (CampaignStateUtility.Drug(controller.AISimulation.Context, item.DefinitionId).Kind == DrugKind.LegacyBounce)
                    total += item.ActiveDoses;
            return total < controller.Balance.Marble.MaxActiveBounceDrugs;
        }

        private GameAIAction Action(string kind, int id = 0, int index = 0, float x = 0, float z = 0,
            float value = 0, uint definition = 0, int target = 0)
        {
            string[] kinds = { "wait", "fire", "buy", "refresh", "sell", "build", "place", "rotate", "remove", "offer", "begin", "cashout", "drug", "activate" };
            var features = new float[20]; features[Array.IndexOf(kinds, kind)] = 1;
            features[14] = x / 10; features[15] = z / 10; features[16] = value / (kind == "rotate" ? 360 : 1);
            var device = controller.GetOwnedDevices().FirstOrDefault(d => d.InstanceId == id);
            features[17] = device.X / 10; features[18] = device.Z / 10; features[19] = device.RemainingDurability / 100f;
            if (target != 0)
            {
                var targetDevice = controller.GetOwnedDevices().First(d => d.InstanceId == target);
                features[17] = targetDevice.X / 10; features[18] = targetDevice.Z / 10;
                features[19] = targetDevice.RemainingDurability / 100f;
            }
            return new GameAIAction { kind = kind, id = id, index = index, x = x, z = z, value = value,
                definitionId = definition, target = target, features = features,
                key = string.Join(":", kind, id.ToString(), index.ToString(), x.ToString("R", CultureInfo.InvariantCulture),
                    z.ToString("R", CultureInfo.InvariantCulture), value.ToString("R", CultureInfo.InvariantCulture), definition.ToString(), target.ToString()) };
        }

        private IEnumerable<GameAIEntity> Entities()
        {
            foreach (var device in controller.GetOwnedDevices())
                yield return new GameAIEntity { kind = 0, instanceId = device.InstanceId, definitionId = device.DefinitionId,
                    status = controller.GetDeviceStatus(device.InstanceId), values = new[] {
                        device.X / 10, 0, device.Z / 10, device.Angle / 360, device.Radius, device.Range / 10,
                        device.Strength / 10, device.Placed ? 1f : 0, device.RemainingDurability / 100f,
                        device.Durability / 100f, device.SellPrice / 100f, (float)device.ScoreMultiplier / 10,
                        (float)device.RushChanceAdd, (float)device.Rarity / 4, device.CompletingEffect ? 1f : 0, 0 } };
            foreach (var ball in controller.AIBalls()) yield return ball;
            if (controller.Session.Phase == RoundPhase.Shop)
                foreach (var offer in controller.GetShopOffers())
                    yield return new GameAIEntity { kind = 2, definitionId = offer.DefinitionId, instanceId = offer.Index,
                        values = new[] { offer.Price / 100f, offer.Sold ? 1f : 0, (float)offer.Kind, (float)offer.Rarity / 4,
                            offer.Durability / 100f, offer.SellPrice / 100f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 } };
            foreach (var drug in controller.GetDrugInventory())
                yield return new GameAIEntity { kind = 3, definitionId = drug.DefinitionId, values = new[] {
                    drug.Count / 10f, drug.ActiveDoses / 10f, (float)drug.SecondsRemaining / 10,
                    drug.DurationSeconds / 10, drug.RestitutionMultiplier, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 } };
            // Geometry is observed, not an alternative physics model. Placement still uses real colliders.
            foreach (var collider in controller.PhysicsRoot.GetComponentsInChildren<Collider>())
            {
                if (collider.GetComponentInParent<MResDeviceBody>() != null || collider.GetComponentInParent<MarbleBody>() != null) continue;
                var p = controller.PhysicsRoot.InverseTransformPoint(collider.bounds.center);
                var size = collider.bounds.size;
                var zone = collider.GetComponentInParent<MarbleZone>();
                yield return new GameAIEntity { kind = 4, values = new[] { p.x / 10, p.y / 10, p.z / 10,
                    size.x / 10, size.y / 10, size.z / 10, collider.enabled ? 1f : 0, collider.isTrigger ? 1f : 0,
                    zone != null ? (float)zone.Kind : -1, zone != null ? zone.Multiplier / 10 : 0,
                    zone != null ? zone.BaseRushChance : 0, 0, 0, 0, 0, 0 } };
            }
        }

        public static string Hash(byte[] bytes)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}

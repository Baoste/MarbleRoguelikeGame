using System;
using System.Collections.Generic;
using MarblesECS.AI;
using UnityEngine;

namespace MarblesECS.PhysX
{
    public sealed partial class MarbleGameController
    {
        internal MarbleSimulation AISimulation => simulation;
        internal float AIFireCooldown => (float)Math.Max(0, simulation.Context.Launcher.NextFireAt - simulation.Context.Round.Time);
        internal float AILauncherPhase => LauncherMoveHalfWidth > 0 ?
            (float)(simulation.Context.Manager.GetComponentData<LauncherMovementState>(simulation.Context.LauncherEntity).Travel /
                (LauncherMoveHalfWidth * 4d)) : 0;

        internal void AdvanceAutomationTick()
        {
            if (!AutomationControlled || !IsReady || RequiresRestart) throw new InvalidOperationException("AI controller is not ready.");
            if (!IsSimulationRunning) return;
            try
            {
                MRes.MResManualSimulation.Step(localScene, step);
                AdvanceGameTick(true);
                // The outcome has already been sampled by the normal settlement system.
                if (Session.Phase == RoundPhase.Draining && Session.GamblingResolved)
                    simulation.CompleteRandomScoreReveal();
                FlushScores();
            }
            catch { simulation.AbortPhysicsStep(); RequiresRestart = true; throw; }
        }

        internal void SampleAutomationFuture(uint seed)
        {
            if (!AutomationControlled || Snapshot.ActiveMarbles != 0 ||
                (Session.Phase != RoundPhase.Build && Session.Phase != RoundPhase.Shop))
                throw new InvalidOperationException("Search reseeding requires an empty Shop/Build boundary.");
            var campaign = simulation.Context.Campaign;
            campaign.RandomState = seed == 0 ? 1u : seed;
            simulation.Context.Campaign = campaign;
            var round = simulation.Context.Round;
            round.RandomState = campaign.RandomState;
            simulation.Context.Round = round;
            // Also decorrelate Unity's random stream for resources that use it.
            UnityEngine.Random.InitState(unchecked((int)seed));
        }

        internal IEnumerable<GameAIEntity> AIBalls()
        {
            var keys = new List<MarbleKey>(simulation.Context.Marbles.Keys);
            keys.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            foreach (var key in keys)
            {
                if (!bridge.TryGetState(key, out var motion) || !simulation.TryGetMarble(key, out var ball)) continue;
                var p = PhysicsRoot.InverseTransformPoint(motion.Position);
                var v = PhysicsRoot.InverseTransformVector(motion.LinearVelocity);
                yield return new GameAIEntity { kind = 1, definitionId = ball.DefinitionId,
                    values = new[] { p.x / 10, p.y / 10, p.z / 10, v.x / 10, v.y / 10, v.z / 10,
                        (float)ball.BaseScore / 1000, (float)ball.ScoreMultiplier / 10, ball.BloodInvestment / 10,
                        ball.Restitution, (float)ball.RushContribution / 100, 0, 0, 0, 0, 0 } };
            }
        }

        internal bool AICanBuy(ShopOfferSnapshot offer)
        {
            if (offer.Sold || offer.Price > Session.Coins) return false;
            var c = simulation.Context;
            if (offer.Kind == ShopItemKind.Device)
                return c.DeviceQuery.CalculateEntityCount() + CampaignStateUtility.Device(c, offer.DefinitionId).PurchaseQuantity <= Balance.Campaign.MaxOwnedDevices;
            int total = 0;
            foreach (var drug in GetDrugInventory()) total += drug.Count;
            return total < Balance.Campaign.MaxDrugInventory;
        }

        internal bool AICanActivate(OwnedDeviceSnapshot device) => device.Placed &&
            (!device.Exhausted || device.CompletingEffect) && DeviceScript(device).HasFunction("OnActivate");
    }
}

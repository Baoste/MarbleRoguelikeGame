namespace MarblesECS
{
    internal static class ContactResolutionSystem
    {
        internal static void Execute(SimulationContext context)
        {
            context.Contacts.Sort((a, b) =>
            {
                int order = a.Key.Sequence.CompareTo(b.Key.Sequence);
                if (order == 0) order = b.Priority.CompareTo(a.Priority);
                return order != 0 ? order : a.TargetId.CompareTo(b.TargetId);
            });
            // All non-terminal effects finish before any terminal settles.
            foreach (var contact in context.Contacts)
            {
                if (!IsDevice(contact.Kind) || !context.TryGetFlying(contact.Key, out var entity)) continue;
                var visits = context.Manager.GetBuffer<VisitedDeviceData>(entity);
                bool visited = false;
                for (int i = 0; i < visits.Length; i++)
                    if (visits[i].DeviceId == contact.TargetId) { visited = true; break; }
                if (visited) continue;
                var score = context.Manager.GetComponentData<MarbleScore>(entity);
                score.ScoreMultiplier = MarbleRules.CombineDeviceMultiplier(score.ScoreMultiplier, contact.Multiplier);
                context.Manager.SetComponentData(entity, score);
                var rush = context.Manager.GetComponentData<MarbleRush>(entity);
                rush.RushChanceBonus = System.Math.Min(1, rush.RushChanceBonus + contact.RushChanceAdd);
                context.Manager.SetComponentData(entity, rush);
                visits.Add(new VisitedDeviceData { DeviceId = contact.TargetId });
            }
            foreach (var contact in context.Contacts)
            {
                if (IsDevice(contact.Kind) || !context.TryGetFlying(contact.Key, out var entity)) continue;
                SettlementSystem.Settle(context, entity, contact);
            }
        }

        private static bool IsDevice(MarbleContactKind kind) =>
            kind == MarbleContactKind.Multiplier || kind == MarbleContactKind.RushPin;
    }
}

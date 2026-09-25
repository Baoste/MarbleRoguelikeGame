namespace MarblesECS
{
    public sealed partial class MarbleSimulation
    {
        public void EnqueueContact(MarbleContact contact)
        {
            ThrowIfDisposed();
            if (!started || contact.Key.RoundId != context.Round.RoundId || !context.IsRunning) return;
            if (contact.TargetId <= 0) return;
            if (contact.Kind != MarbleContactKind.Multiplier && contact.Kind != MarbleContactKind.Score &&
                contact.Kind != MarbleContactKind.Drain && contact.Kind != MarbleContactKind.RushPin &&
                contact.Kind != MarbleContactKind.RandomScore && contact.Kind != MarbleContactKind.Device &&
                contact.Kind != MarbleContactKind.Pin && contact.Kind != MarbleContactKind.Pierced) return;
            if (!MarbleRules.IsFinite(contact.Multiplier) || contact.Multiplier < 0 || contact.Multiplier > 1000) return;
            if (!MarbleRules.IsFinite(contact.RushChanceAdd) || contact.RushChanceAdd < 0 || contact.RushChanceAdd > 1) return;
            if (contact.FlatScore < 0 || !SimulationContext.IsFinite(contact.Position)) return;
            context.Contacts.Add(contact);
        }

    }
}

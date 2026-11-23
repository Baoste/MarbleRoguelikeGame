namespace MarblesECS
{
    public enum MorningPrepInteraction : byte { ReadNote }

    public sealed class MorningPrepDefinition
    {
        public string ObjectiveId { get; }
        public MorningPrepInteraction Interaction { get; }
        public string AnchorId { get; }
        public string Text { get; }

        internal MorningPrepDefinition(string id, MorningPrepInteraction interaction, string anchorId, string text)
        {
            ObjectiveId = id;
            Interaction = interaction;
            AnchorId = anchorId;
            Text = text;
        }
    }
}

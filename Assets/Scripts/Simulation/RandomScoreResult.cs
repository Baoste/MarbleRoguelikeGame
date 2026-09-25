namespace MarblesECS
{
    public readonly struct RandomScoreResult
    {
        public readonly long PendingScore;
        public readonly bool Won;
        public readonly long Payout;
        public readonly int Multiplier;

        public RandomScoreResult(long pendingScore, bool won, long payout, int multiplier = 0)
        {
            PendingScore = pendingScore;
            Won = won;
            Payout = payout;
            Multiplier = multiplier;
        }
    }
}

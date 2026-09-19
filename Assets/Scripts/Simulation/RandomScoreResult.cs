namespace MarblesECS
{
    public readonly struct RandomScoreResult
    {
        public readonly long PendingScore;
        public readonly bool Won;
        public readonly long Payout;

        public RandomScoreResult(long pendingScore, bool won, long payout)
        {
            PendingScore = pendingScore;
            Won = won;
            Payout = payout;
        }
    }
}

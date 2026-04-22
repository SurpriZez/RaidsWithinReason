using Verse;

namespace RaidsWithinReason
{
    public enum RaidGoalType
    {
        Loot,
        Capture,
        Destroy,
        Revenge,
        ReleasePrisoner,
        Retaliation,
    }

    public class RaidGoalDef : Def
    {
        public RaidGoalType goalType;
        public string targetDescription = string.Empty;
        public float successGoodwillDelta;
        public float failureGoodwillDelta;
        public bool retreatOnSuccess;
    }
}

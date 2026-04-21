using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RaidsWithinReason
{
    // Tracks when each faction last sent a negotiator so we can enforce the cooldown.
    // Auto-discovered by RimWorld as a GameComponent.
    public class NegotiatorCooldownComponent : GameComponent
    {
        private Dictionary<Faction, int> lastNegotiatorTick = new Dictionary<Faction, int>();

        public NegotiatorCooldownComponent(Game game) : base() { }

        public bool IsOnCooldown(Faction faction)
        {
            if (!lastNegotiatorTick.TryGetValue(faction, out int tick)) return false;
            int cooldownTicks = RWR_Mod.Settings.negotiatorCooldownDays * GenDate.TicksPerDay;
            return Find.TickManager.TicksGame - tick < cooldownTicks;
        }

        public void RecordNegotiatorSent(Faction faction)
        {
            lastNegotiatorTick[faction] = Find.TickManager.TicksGame;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref lastNegotiatorTick, "lastNegotiatorTick",
                LookMode.Reference, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                lastNegotiatorTick ??= new Dictionary<Faction, int>();
                foreach (Faction stale in lastNegotiatorTick.Keys.Where(k => k == null).ToList())
                    lastNegotiatorTick.Remove(stale);
            }
        }
    }
}

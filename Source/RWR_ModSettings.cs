using UnityEngine;
using Verse;

namespace RaidsWithinReason
{
    public class RWR_Settings : ModSettings
    {
        public bool  enableGoalLetters      = true;
        public bool  enableRetreatOnSuccess = true;
        public float chaoticRaidChance      = 0f;
        public float negotiationChance      = 0.45f;
        public int   negotiatorCooldownDays = 10;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableGoalLetters,      "enableGoalLetters",      true);
            Scribe_Values.Look(ref enableRetreatOnSuccess, "enableRetreatOnSuccess", true);
            Scribe_Values.Look(ref chaoticRaidChance,      "chaoticRaidChance",      0f);
            Scribe_Values.Look(ref negotiationChance,      "negotiationChance",      0.45f);
            Scribe_Values.Look(ref negotiatorCooldownDays, "negotiatorCooldownDays", 10);
        }
    }

    public class RWR_Mod : Mod
    {
        public static RWR_Settings Settings { get; private set; }

        public RWR_Mod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RWR_Settings>();
        }

        public override string SettingsCategory() => "Raids Within Reason";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled(
                "Show pre-raid goal announcement letter",
                ref Settings.enableGoalLetters);

            listing.CheckboxLabeled(
                "Raiders retreat after achieving their objective",
                ref Settings.enableRetreatOnSuccess);

            listing.Gap(6f);

            listing.Label($"Chance a raid ignores goals and attacks randomly (0 = all raids have purpose): {Settings.chaoticRaidChance:P0}");
            Settings.chaoticRaidChance = listing.Slider(Settings.chaoticRaidChance, 0f, 1f);
            listing.Gap(4f);

            listing.Label($"Chance faction negotiates before raiding: {Settings.negotiationChance:P0}");
            Settings.negotiationChance = listing.Slider(Settings.negotiationChance, 0f, 1f);
            listing.Gap(4f);

            listing.Label($"Min days between negotiators from same faction: {Settings.negotiatorCooldownDays}");
            Settings.negotiatorCooldownDays = Mathf.RoundToInt(
                listing.Slider(Settings.negotiatorCooldownDays, 0f, 60f));

            listing.End();
        }
    }
}

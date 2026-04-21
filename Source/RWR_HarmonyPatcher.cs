using System.Reflection;
using HarmonyLib;
using Verse;

namespace RaidsWithinReason
{
    [StaticConstructorOnStartup]
    public static class RWR_HarmonyPatcher
    {
        static RWR_HarmonyPatcher()
        {
            new Harmony("AuthorName.RaidsWithinReason")
                .PatchAll(Assembly.GetExecutingAssembly());

            Log.Message("[RaidsWithinReason] Initialized");
        }
    }
}

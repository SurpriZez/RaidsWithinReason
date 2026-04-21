using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidsWithinReason
{
    internal static class Patch_AlertsReadout_Constructor
    {
        // RimWorld automatically discovers all subclasses of Verse.Alert via reflection
        // and adds them to the AlertsReadout during its constructor.
        // Manually injecting it via Harmony was causing it to show up twice!
        // [HarmonyPostfix]
        // public static void Postfix(AlertsReadout __instance) { ... }
    }
}

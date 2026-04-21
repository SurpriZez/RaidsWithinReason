using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RaidsWithinReason
{
    [HarmonyPatch(typeof(AlertsReadout), MethodType.Constructor)]
    public static class Patch_AlertsReadout_Constructor
    {
        [HarmonyPostfix]
        public static void Postfix(AlertsReadout __instance)
        {
            // Find the list of alerts by type rather than name to be more version-independent.
            var field = typeof(AlertsReadout).GetFields(System.Reflection.BindingFlags.Instance | 
                                                       System.Reflection.BindingFlags.NonPublic | 
                                                       System.Reflection.BindingFlags.Public)
                                            .FirstOrDefault(f => f.FieldType == typeof(List<Alert>));

            if (field != null)
            {
                var list = (List<Alert>)field.GetValue(__instance);
                if (list != null)
                {
                    list.Add(new Alert_NegotiatorPresent());
                }
            }
            else
            {
                Log.Warning("[RWR] Could not find any List<Alert> field in AlertsReadout. Negotiator alert will not be visible.");
            }
        }
    }
}

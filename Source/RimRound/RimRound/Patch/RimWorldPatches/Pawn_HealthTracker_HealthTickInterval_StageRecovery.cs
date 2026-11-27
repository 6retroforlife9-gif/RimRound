using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.HealthTickInterval))]
    internal static class Pawn_HealthTracker_HealthTickInterval_StageRecovery
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var targets = new List<int>();
            FieldInfo healingStat = AccessTools.Field(typeof(StatDefOf), nameof(StatDefOf.InjuryHealingFactor));
            MethodInfo heal = AccessTools.Method(typeof(Hediff), nameof(Hediff.Heal));
            MethodInfo injuryHeal = AccessTools.Method(typeof(Hediff_Injury), nameof(Hediff_Injury.Heal));
            bool usesHealingStat = false;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].LoadsField(healingStat)) usesHealingStat = true;
                if (!code[i].Calls(heal) && !code[i].Calls(injuryHeal)) continue;
                if (usesHealingStat) targets.Add(i);
                usesHealingStat = false;
            }
            // The two native routes are natural healing and tended healing.
            // Leave Anomaly regeneration and external fixed healing untouched.
            if (targets.Count != 2)
            {
                Log.Error("[RimRound] Expected two native injury recovery calls; found " + targets.Count + ". Stage recovery was not applied.");
                return code;
            }
            MethodInfo replacement = AccessTools.Method(typeof(Pawn_HealthTracker_HealthTickInterval_StageRecovery), nameof(HealWithStageRecovery));
            foreach (int index in targets)
            {
                code[index].opcode = OpCodes.Call;
                code[index].operand = replacement;
            }
            return code;
        }

        private static void HealWithStageRecovery(Hediff injury, float amount)
        {
            if (amount > 0f) amount *= StageHealthPerks.InjuryRecoveryMultiplier(injury);
            injury.Heal(amount);
        }
    }
}

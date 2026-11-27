using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.Ideology
{
    public static class AbundanceRoles
    {
        public const string Meme = "RR_EmbodiedAbundance";
        public const string Feeder = "RR_Role_Feeder";
        public const string Feedee = "RR_Role_Feedee";
        public const string Provisioner = "RR_Role_Provisioner";
        public static readonly string[] Roles = { Feeder, Feedee, Provisioner };
        static readonly FieldInfo CachedPreceptIcon = AccessTools.Field(typeof(Precept), "icon");
        public static bool Enabled(Ideo ideo) => ModsConfig.IdeologyActive && ideo?.memes?.Any(m => m.defName == Meme) == true;
        public static bool IsCustom(PreceptDef def) => def != null && Roles.Contains(def.defName);
        public static bool Has(Pawn pawn, string role) => pawn?.Dead == false && Enabled(pawn.Ideo) &&
            pawn.Ideo.RolesListForReading.Any(r => r.def.defName == role && r.Active && r.IsAssigned(pawn));
        public static float FeedingSpeed(Pawn pawn) => Has(pawn, Feeder) ? 1.25f : 1f;
        public static bool GainsFromMeals(Pawn pawn) => Has(pawn, Feedee) && BodyModelEligibility.IsEligible(pawn);

        public static void Reconcile(Ideo ideo)
        {
            if (!ModsConfig.IdeologyActive || ideo == null) return;
            // Do not replace filters, roles or the building entry underneath an
            // active Lord. The periodic reconciliation retries after cleanup.
            if (RbaBuildings.HasActiveRitual(ideo)) return;
            // Existing feast instances saved the inherited festival artwork.
            // Replace only that stock override; preserve any custom icon choice.
            foreach (var feast in ideo.PreceptsListForReading.OfType<Precept_Ritual>().Where(r => r.def.defName == CommunalFeast.RitualDefName &&
                r.iconPathOverride == "UI/Icons/Rituals/RitualFestival"))
            {
                feast.iconPathOverride = "UI/RimRoundIdeology/RitualFeast";
                CachedPreceptIcon?.SetValue(feast, null);
            }
            foreach(var ritual in ideo.PreceptsListForReading.OfType<Precept_Ritual>().Where(AbundanceRituals.IsFoodRitual))
                if(!(ritual.obligationTargetFilter is RitualTarget_AbundanceFood) &&
                    !Find.Maps.Any(m=>Find.IdeoManager.GetActiveRituals(m).Any(r=>r.Ritual==ritual)))
                {
                    ritual.obligationTargetFilter=DefDatabase<RitualObligationTargetFilterDef>.GetNamed("RR_AbundanceFoodTarget").GetInstance();
                    ritual.obligationTargetFilter.parent=ritual;
                }
            bool old = ideo.memes.Any(m => m.defName == "RR_FoodSupremacy");
            if (old)
            {
                // Active legacy Lords retain their precepts until native cleanup.
                if (Find.Maps != null && Find.Maps.Any(m => Find.IdeoManager.GetActiveRituals(m).Any(r => r.Ritual?.ideo == ideo))) return;
                ideo.memes.RemoveAll(m => m.defName == "RR_FoodSupremacy");
                if (!Enabled(ideo)) ideo.memes.Add(DefDatabase<MemeDef>.GetNamed(Meme));
            }
            if (Enabled(ideo))
            {
                foreach (string name in Roles) AddMissing(ideo, name);
                if (BodyRoleExpectations.IdealFor(ideo) == null) AddMissing(ideo, "RR_LeadershipBodyIdeal_Ample");
                var paste = DefDatabase<PreceptDef>.GetNamed("NutrientPasteEating_DontMind");
                if (!ideo.HasPrecept(paste))
                {
                    foreach (Precept p in ideo.PreceptsListForReading.Where(p => p.def.issue == paste.issue).ToArray()) ideo.RemovePrecept(p);
                    AddMissing(ideo, paste.defName);
                }
            }
            else
            {
                foreach (Precept_Role role in ideo.RolesListForReading.Where(r => IsCustom(r.def)).ToArray())
                {
                    foreach (Pawn p in role.ChosenPawns().ToArray()) role.Unassign(p, true);
                    ideo.RemovePrecept(role);
                }
            }
            RbaBuildings.Reconcile(ideo);
            if (old)
            {
                ideo.RecachePrecepts();
                BodyRoleExpectations.Instance?.Reconcile();
                Messages.Message("RR_AbundanceMigrated".Translate(ideo.name), MessageTypeDefOf.NeutralEvent, false);
            }
        }
        private static void AddMissing(Ideo ideo, string name)
        {
            var def = DefDatabase<PreceptDef>.GetNamed(name);
            if (!ideo.HasPrecept(def)) ideo.AddPrecept(PreceptMaker.MakePrecept(def), true);
        }
    }

    public sealed class GameComponent_AbundanceRoles : GameComponent
    {
        private int lastTick = -1;
        public GameComponent_AbundanceRoles(Game game) { }
        public override void GameComponentUpdate()
        {
            if (!ModsConfig.IdeologyActive || Scribe.mode != LoadSaveMode.Inactive ||
                Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting || Find.IdeoManager == null) return;
            int now = Find.TickManager.TicksGame;
            if (lastTick >= 0 && now >= lastTick && now - lastTick < 250) return;
            lastTick = now;
            foreach (Ideo ideo in Find.IdeoManager.IdeosListForReading.ToArray()) AbundanceRoles.Reconcile(ideo);
        }
    }

    [HarmonyPatch(typeof(IdeoFoundation), nameof(IdeoFoundation.EnsurePreceptsCompatibleWithMemes))]
    internal static class AbundanceReformRitualInitialization
    {
        private static void Postfix(IdeoFoundation __instance, IdeoGenerationParms parms)
        {
            if (RbaBuildings.HasActiveRitual(__instance.ideo)) return;
            // Native reform fills newly required ritual patterns without Init.
            // Give only our new ceremony its native identity before the player
            // edits it or an active ritual stores a reference to it. Existing
            // named/date-configured rituals keep their identity and all state.
            foreach (var ritual in __instance.ideo.PreceptsListForReading.OfType<Precept_Ritual>()
                .Where(r => r.def.defName == AbundanceRituals.Ceremony && r.Id < 0))
                ritual.Init(__instance.ideo, parms.forFaction);
            RbaBuildings.Reconcile(__instance.ideo);
        }
    }

    // Preserve the native check and every subsequent eligibility/conflict rule.
    // Only the count supplied to its existing limit is changed for this meme.
    [HarmonyPatch(typeof(IdeoFoundation), nameof(IdeoFoundation.CanAdd))]
    internal static class AbundanceSpecialistAllowance
    {
        private static int Count(int nativeCount, IdeoFoundation foundation, PreceptDef incoming)
        {
            if (!AbundanceRoles.Enabled(foundation.ideo)) return nativeCount;
            if (AbundanceRoles.IsCustom(incoming)) return 0;
            return foundation.ideo.PreceptsListForReading.Count(p => p is Precept_RoleMulti && p.def.visible && !AbundanceRoles.IsCustom(p.def));
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int patched = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                var method = instruction.operand as MethodInfo;
                if (instruction.opcode == OpCodes.Call && (method?.DeclaringType == typeof(Enumerable) || method?.DeclaringType == typeof(GenCollection)) &&
                    method.Name == "Count" && method.IsGenericMethod && method.GetGenericArguments()[0] == typeof(Precept))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AbundanceSpecialistAllowance), nameof(Count)));
                    patched++;
                }
            }
            if (patched != 1) throw new InvalidOperationException("Embodied Abundance specialist limit: expected one native precept count, found " + patched);
        }
    }

    public sealed class RoleRequirement_AbundanceAdult : RoleRequirement
    {
        public override bool Met(Pawn p, Precept_Role role) => role?.def?.defName == AbundanceRoles.Feeder
            ? PawnHelperEligibility.IsAdultHumanlike(p) && PawnHelperEligibility.AllowsDirectedHelp(p)
            : BodyModelEligibility.HasAdultBiology(p);
        public override string GetLabel(Precept_Role role) => "RR_AbundanceAdult".Translate();
    }

    [HarmonyPatch(typeof(RitualBehaviorWorker), nameof(RitualBehaviorWorker.PostCleanup))]
    internal static class AbundanceRoleCeremonyVisualCleanup
    {
        private static void Postfix(LordJob_Ritual ritual)
        {
            if (ritual?.Ritual?.def.defName != "RoleChange" || !AbundanceRoles.Enabled(ritual.Ritual.ideo)) return;
            // Native stencil visuals fade after the Lord is removed. Preserve
            // the fade without saving a reference to that retired ceremony.
            var drawers = ritual.lord?.lordManager?.stencilDrawers;
            if (drawers == null) return;
            foreach (var drawer in drawers)
                if (drawer.sourceLord == ritual.lord) drawer.sourceLord = null;
        }
    }

    [HarmonyPatch(typeof(Precept_RoleMulti), nameof(Precept_RoleMulti.Unassign))]
    internal static class AbundanceFeedeeUnassigned
    {
        private static void Postfix(Precept_RoleMulti __instance, Pawn p)
        {
            if (__instance.def.defName == AbundanceRoles.Feedee)
                p?.TryGetComp<RimRound.Comps.FullnessAndDietStats_ThingComp>()?.ClearMealGainEligibility();
        }
    }

    [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.Notify_RemovedByReforming))]
    internal static class AbundanceFeedeeRemovedByReforming
    {
        private static void Prefix(Precept_Role __instance)
        {
            if (__instance.def.defName != AbundanceRoles.Feedee) return;
            foreach (Pawn pawn in __instance.ChosenPawns())
                pawn?.TryGetComp<RimRound.Comps.FullnessAndDietStats_ThingComp>()?.ClearMealGainEligibility();
        }
    }

    [HarmonyPatch(typeof(Precept), nameof(Precept.TryGetLostByReformingWarning))]
    internal static class AbundanceRoleReformWarning
    {
        private static void Postfix(Precept __instance, ref string warning, ref bool __result)
        {
            if (!(__instance is Precept_Role role) || !AbundanceRoles.IsCustom(role.def)) return;
            var holders = role.ChosenPawns().Where(p => p != null).Select(p => p.LabelShortCap.ToString()).ToArray();
            if (holders.Length == 0) return;
            warning = "RR_AbundanceRoleLostByReforming".Translate(role.LabelCap, string.Join(", ", holders));
            __result = true;
        }
    }
}

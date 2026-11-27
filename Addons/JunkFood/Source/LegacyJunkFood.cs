using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Oinky.JunkFood
{
    public class LegacyJunkFoodRecipeExtension : DefModExtension { }

    public class Recipe_JunkFoodLegacy : RecipeWorker
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null) => false;
        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill) { }
    }

    public class Gene_JunkFoodLegacy : Gene
    {
        public override bool Active => false;
    }

    public class Need_JunkFoodLegacy : Need_Chemical
    {
        public Need_JunkFoodLegacy(Pawn pawn) : base(pawn) { }
        // Old Hediff_Addiction saves can inspect this identity before migration.
        // Keep their native type contract without category notifications in the
        // constructor, when Scribe has not yet resolved hediff pawn references.
        public override void SetInitialLevel() { curLevelInt = .5f; }
        public override bool ShowOnNeedList => false;
        public override void NeedInterval() { }
    }

    public class ThoughtWorker_JunkFoodLegacy : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn) => ThoughtState.Inactive;
    }

    public static class LegacyJunkFoodMigration
    {
        public const string ChemicalName = "junkfoodChem";
        public const string NeedName = "chemical_Junkfood";
        private static readonly HashSet<string> GeneNames = new HashSet<string>
        { "ChemicalDependency_junkfoodChem", "AddictionResistant_junkfoodChem", "AddictionImmune_junkfoodChem" };
        private static readonly HashSet<string> HediffNames = new HashSet<string>
        { "junkfoodHigh", "junkfoodTolerance", "junkfoodAddiction" };
        private static readonly HashSet<string> ThoughtNames = new HashSet<string>
        { "junkfoodWithdrawal", "GeneticChemicalDependency_junkfoodChem" };
        private static readonly MethodInfo RemoveNeed = AccessTools.Method(typeof(Pawn_NeedsTracker), "RemoveNeed");
        public static bool IsLegacyGene(GeneDef def) => def != null && GeneNames.Contains(def.defName);

        public static void Migrate(Pawn pawn)
        {
            if (pawn == null) return;
            if (pawn.genes != null)
            {
                foreach (Gene gene in pawn.genes.GenesListForReading.ToList())
                {
                    if (!IsLegacyGene(gene.def) || gene is Gene_JunkFoodLegacy) continue;
                    // Retain the same saved gene identity and endo/xeno placement,
                    // replacing only an obsolete active runtime class. Genepacks,
                    // xenotypes and templates resolve these same neutral defs.
                    bool xenogene = pawn.genes.Xenogenes.Contains(gene);
                    GeneDef def = gene.def;
                    pawn.genes.RemoveGene(gene);
                    pawn.genes.AddGene(def, xenogene);
                }
            }
            if (pawn.health?.hediffSet != null)
                foreach (Hediff hediff in pawn.health.hediffSet.hediffs.ToList())
                    if (HediffNames.Contains(hediff.def.defName) ||
                        (hediff is Hediff_ChemicalDependency dependency && dependency.chemical?.defName == ChemicalName))
                        pawn.health.RemoveHediff(hediff);
            if (pawn.needs != null)
                foreach (Need need in pawn.needs.AllNeeds.ToList())
                    if (need.def?.defName == NeedName) RemoveNeed.Invoke(pawn.needs, new object[] { need.def });
            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
            if (memories != null)
                foreach (Thought_Memory memory in memories.Memories.ToList())
                    if (ThoughtNames.Contains(memory.def.defName)) memories.RemoveMemory(memory);
            JunkFoodGameComponent.DirtyThoughts(pawn);
        }

        public static void SweepLoadedPawns()
        {
            var pawns = new HashSet<Pawn>();
            if (Find.WorldPawns != null)
                foreach (Pawn pawn in Find.WorldPawns.AllPawnsAliveOrDead) pawns.Add(pawn);
            if (Find.Maps != null)
                foreach (Map map in Find.Maps)
                    foreach (Pawn pawn in map.mapPawns.AllPawns) pawns.Add(pawn);
            foreach (Pawn pawn in pawns) Migrate(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExposeData))]
    internal static class JunkLegacyPawnLoad
    {
        private static void Postfix(Pawn __instance)
        {
            // Also covers held/cryptosleep pawns not in a map's pawn list.
            if (Scribe.mode == LoadSaveMode.PostLoadInit) LegacyJunkFoodMigration.Migrate(__instance);
        }
    }

    [HarmonyPatch(typeof(Pawn_NeedsTracker), "ShouldHaveNeed")]
    internal static class JunkLegacyNeedDisabled
    {
        private static void Postfix(NeedDef nd, ref bool __result)
        { if (nd?.defName == LegacyJunkFoodMigration.NeedName) __result = false; }
    }

    [HarmonyPatch(typeof(Gene_ChemicalDependency), nameof(Gene_ChemicalDependency.Active), MethodType.Getter)]
    internal static class JunkLegacyDependencyDisabled
    {
        private static void Postfix(Gene_ChemicalDependency __instance, ref bool __result)
        { if (LegacyJunkFoodMigration.IsLegacyGene(__instance.def)) __result = false; }
    }

    [HarmonyPatch(typeof(Hediff_ChemicalDependency), nameof(Hediff_ChemicalDependency.ShouldRemove), MethodType.Getter)]
    internal static class JunkLegacyDependencyHediffDisabled
    {
        private static void Postfix(Hediff_ChemicalDependency __instance, ref bool __result)
        { if (__instance.chemical?.defName == LegacyJunkFoodMigration.ChemicalName) __result = true; }
    }
}

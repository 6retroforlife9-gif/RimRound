using AlienRace;
using AlienRace.ExtendedGraphics;
using RimRound.Comps;
using RimRound.Presentation;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimRound.Utilities
{
    public static class BodyTypeUtility
    {
        public static bool PawnIsOverWeightThreshold(Pawn pawn, BodyTypeDef bodyType)
        {
            if (!BodyModelEligibility.IsEligible(pawn) || pawn.WeightHediff() == null) return false;
            float maxValueForBodyType = 0;
            if (RacialBodyTypeInfoUtility.defaultFemaleSet.ContainsKey(bodyType))
                maxValueForBodyType = RacialBodyTypeInfoUtility.defaultFemaleSet[bodyType].maxSeverity;
            else
                Log.Error("Only use default female bodytypedefs in PawnIsOverWeightThreshold!");

            if (Utilities.HediffUtility.WeightHediff(pawn).Severity > maxValueForBodyType * RacialBodyTypeInfoUtility.GetBodyTypeWeightRequirementMultiplier(pawn))
            {
                return true;
            }

            return false;
        }


        public static string GetProperBodyGraphicPathFromPawn(Pawn pawn)
        {
            if (ModsConfig.BiotechActive && pawn.story.bodyType == BodyTypeDefOf.Baby)
            {
                return BodyTypeDefOf.Baby.bodyNakedGraphicPath;
            }
            else if (ModsConfig.BiotechActive && pawn.story.bodyType == BodyTypeDefOf.Child)
            {
                return BodyTypeDefOf.Child.bodyNakedGraphicPath;
            }


            string basePath = "Things/Pawn/Humanlike/Bodies/";

            if (pawn.def is AlienRace.ThingDef_AlienRace alienRace &&
                alienRace.alienRace.graphicPaths.body.path is String alienBodyPath &&
                alienBodyPath != basePath)
            {
                if (!IsRRBody(pawn.story.bodyType))
                {
                    if (alienBodyPath.Last() != '/') // Anty race ꒰ ง ˘ω˘ ꒱ว
                        alienBodyPath = alienBodyPath.Substring(0, alienBodyPath.LastIndexOf('/') + 1);


                    return alienBodyPath + "Naked_" + pawn.story.bodyType.defName;
                }
            }

            string convertedString = ConvertBodyPathStringsIfNecessary(basePath +"Naked_" + pawn.story.bodyType.defName);

            if (pawn.def is AlienRace.ThingDef_AlienRace alienRace2 && RacialBodyTypeInfoUtility.specialRacialTextureSuffixes.ContainsKey(alienRace2.defName))
            {
                if (RacialBodyTypeInfoUtility.validTextureSuffixes.ContainsKey(RacialBodyTypeInfoUtility.specialRacialTextureSuffixes[alienRace2.defName]))
                {
                    Regex pattern = RacialBodyTypeInfoUtility.validTextureSuffixes[RacialBodyTypeInfoUtility.specialRacialTextureSuffixes[alienRace2.defName]];

                    if (pattern.IsMatch(convertedString))
                    {
                        int lastSlash2 = convertedString.LastIndexOf('/');

                        string basePath2 = convertedString.Substring(0, lastSlash2 + 1);
                        string bodyTypeName2 = convertedString.Substring(lastSlash2 + 1);

                        convertedString = basePath2 + RacialBodyTypeInfoUtility.specialRacialTextureSuffixes[alienRace2.defName] + "/" + bodyTypeName2;

                    }
                }
            }

            return convertedString;
        }

        public static string ConvertBodyPathStringsIfNecessary(string originalBodyPath)
        {
            int lastSlash = originalBodyPath.LastIndexOf('/');

            string basePath = originalBodyPath.Substring(0, lastSlash + 1);
            string bodyTypeName = originalBodyPath.Substring(lastSlash + 1);

            if (!IsRRBody(bodyTypeName))
            {
                return originalBodyPath;
            }

            if (bodyTypeName.Contains("Naked_"))
                bodyTypeName = bodyTypeName.Substring(bodyTypeName.IndexOf("Naked_") + 6);

            if (bodyTypeName == "F_060_LardyAlt")
                bodyTypeName = "F_060_Lardy";


            bodyTypeName = RacialBodyTypeInfoUtility.GetEquivalentBodyTypeDef(DefDatabase<BodyTypeDef>.GetNamed(bodyTypeName)).ToString();
            bodyTypeName = ConvertBodyTypeDefDefnameAccordingToSettings(bodyTypeName);



            return basePath + "Naked_" + bodyTypeName;
        }


        public static bool HasCustomBody(Pawn p)
        {
            return PawnPresentationOwner.IsCustom(p) && PawnPresentationOwner.For(p).UsesCustomBody;
        }

        public static bool IsRRBody(BodyTypeDef bodyTypeDef)
        {
            return bodyTypeDef != null && IsRRBody(bodyTypeDef.defName);
        }

        public static bool IsRRBody(string bodyTypeDefString)
        {
            if (string.IsNullOrEmpty(bodyTypeDefString)) return false;
            string pattern = @"[fmFM]{1}_+[0-9]{3,}?a*_+[A-Za-z]+";

            Regex regex = new Regex(pattern, RegexOptions.Compiled);

            if (regex.IsMatch(bodyTypeDefString))
                return true;

            return false;
        }


        public static string ConvertBodyTypeDefDefnameAccordingToSettings(string bodytypeCleaned)
        {
            if (GlobalSettings.onlyUseStandardBodyType && Regex.IsMatch(bodytypeCleaned, "[0-9]{3}a"))
            {
                bodytypeCleaned = Regex.Replace(bodytypeCleaned, "a_", "_");
            }

            if (GlobalSettings.useLegacyMaleSprites && Regex.IsMatch(bodytypeCleaned, @"^M_")) 
            {
                bodytypeCleaned += "Old";
            }
            else if (GlobalSettings.useAltMaleSprites && Regex.IsMatch(bodytypeCleaned, @"^M_")) // Else is intentional and necessary here
            {
                bodytypeCleaned += "New";
            }

            if (bodytypeCleaned == Defs.BodyTypeDefOf.F_060_Lardy.defName && !GlobalSettings.useOldLardySprite)
            {
                bodytypeCleaned += "Alt";
            }

            if (Regex.IsMatch(bodytypeCleaned, "Gelatinous"))
            {
                bodytypeCleaned = Regex.Replace(bodytypeCleaned, "[0-9]{3}", "100");
            }

            return bodytypeCleaned;
        }

        public static void InvalidateCorpseCache() { }

        public static Corpse GetCorpseContainingPawn(Pawn pawn)
        {
            return pawn?.Corpse;
        }

        public static BodyTypeDef GetBodyTypeBasedOnWeightSeverity(Pawn pawn, bool personallyExempt = false, bool categoricallyExempt = false)
        {
            BodyTypeDef result = null;

            bool dessicated = GetCorpseContainingPawn(pawn) is Corpse c && c.IsDessicated();

            if (pawn.Dead && dessicated)
                return RimWorld.BodyTypeDefOf.Thin;

            if (personallyExempt || categoricallyExempt)
            {
                return pawn.TryGetComp<FullnessAndDietStats_ThingComp>().DefaultBodyType ??
                    RimWorld.BodyTypeDefOf.Thin;
            }


            Dictionary<BodyTypeDef, BodyTypeInfo> bodyTypeDictionary = RacialBodyTypeInfoUtility.GetRacialDictionary(pawn);


            if (bodyTypeDictionary is null)
                return pawn.story.bodyType;

            float weightSeverity = Utilities.HediffUtility.GetHediffOfDefFrom(Defs.HediffDefOf.RimRound_Weight, pawn)?.Severity ?? -1;
            if (weightSeverity == -1)
                return pawn.story.bodyType;

            float weightRequirementMultiplier = RacialBodyTypeInfoUtility.GetBodyTypeWeightRequirementMultiplier(pawn);

            //Edge cases
            if (weightSeverity == 0)
                result = bodyTypeDictionary.First().Key;

            if (weightSeverity >= bodyTypeDictionary.Last().Value.maxSeverity * weightRequirementMultiplier)
                result = bodyTypeDictionary.Last().Key;


            foreach (var x in bodyTypeDictionary)
            {
                if (weightSeverity <= x.Value.maxSeverity * weightRequirementMultiplier)
                {
                    result = x.Key;
                    break;
                }
            }

            if (result is null)
                result = bodyTypeDictionary.First().Key;

            if (!BodyTypeUtility.IsRRBody(result))
                return result;

            int chosenBodyTypeNumber;
            if (!int.TryParse(Regex.Match(result.defName, "[FM]_[0-9]{3}").Value.Substring(2), out chosenBodyTypeNumber))
            {
                Log.Error("Failed to get body number from defName.");
                chosenBodyTypeNumber = 0;
            }

            if (chosenBodyTypeNumber < 100 || GlobalSettings.maxVisualSizeGelLevel.threshold == 20)
                return result;


            int maxSizeNumber = RacialBodyTypeInfoUtility.gelatinousLevelToCode[GlobalSettings.maxVisualSizeGelLevel.threshold];

            if (maxSizeNumber < chosenBodyTypeNumber)
            {
                string resultString = result.defName;

                if (Regex.IsMatch(result.defName, "F_[0-9]{3}"))
                    resultString = Regex.Replace(result.defName, "F_[0-9]{3}", $"F_{maxSizeNumber}");
                else if (Regex.IsMatch(result.defName, "M_[0-9]{3}"))
                    resultString = Regex.Replace(result.defName, "M_[0-9]{3}", $"M_{maxSizeNumber}");

                result = DefDatabase<BodyTypeDef>.GetNamed(resultString);
            }


            return result;
        }


        /// <summary>
        /// Returns true if the pawn did update, false otherwise.
        /// </summary>
        /// <param name="pawn"></param>
        /// <param name="personallyExempt"></param>
        /// <param name="categoricallyExempt"></param>
        /// <param name="forceUpdate"></param>
        /// <param name="BodyCheck"></param>
        public static bool UpdatePawnSprite(Pawn pawn, bool personallyExempt = false, bool categoricallyExempt = false, bool forceUpdate = false, bool BodyCheck = true)
        {
            if (!(pawn?.RaceProps?.Humanlike is bool isHumanlike && isHumanlike))
                return false;

            var comp = pawn.TryGetComp<PawnBodyType_ThingComp>();

            if (comp is null)
                return false;

            var previous = comp.PreparedPresentation;
            var biology = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            string previousStage = biology?.BodyPresentationStageKey;
            if (BodyCheck)
            {
                comp.ticksSinceLastBodyChange = 0;
                LegacyBodyStagePolicies.InitializeForPawn(pawn);
                bool nativeCompatibility = personallyExempt || categoricallyExempt ||
                    (pawn.Dead && pawn.Corpse?.IsDessicated() == true);
                LegacyBodyStagePolicies.AdvanceForPawn(pawn, nativeCompatibility);
            }
            PawnPresentationOwner.Prepare(pawn, false);
            bool changed = !ReferenceEquals(previous, comp.PreparedPresentation);
            if (changed || forceUpdate) RedrawPawn(pawn);
            if (BodyCheck && previousStage != biology?.BodyPresentationStageKey)
                NotifyWeightChanged(pawn);
            return changed;
        }

        private static void NotifyWeightChanged(Pawn pawn)
        {
            var pbtComp = pawn.TryGetComp<PawnBodyType_ThingComp>();
            pbtComp.NotifyWeightStageListeners();
            
        }

        internal static void RedrawPawn(Pawn pawn)
        {
            if (pawn != null) PawnPresentationOwner.Invalidate(pawn);
        }

        public static void UpdatePawnDrawSize(Pawn pawn, bool personallyExempt = false, bool categoricallyExempt = false)
        {

            if (pawn.def is ThingDef_AlienRace alienProps)
            {

                float drawSize;

                BodyTypeInfo? bodyTypeInfo = RacialBodyTypeInfoUtility.GetRacialBodyTypeInfo(pawn);
                if (bodyTypeInfo is null)
                    drawSize = 1;
                else
                    drawSize = bodyTypeInfo.AsNonNullable().meshSize;

                var alienComp = pawn.TryGetComp<AlienPartGenerator.AlienComp>();
                if (alienComp is null)
                {
                    Debug.LogWarning("AlienComp was null in update pawn drawsize!");
                }

                alienComp.customDrawSize =  new Vector2(drawSize, drawSize);

            }
        }


        /// <summary>
        /// Checks a pawn against settings in the RR tab for if they should use RimRound custom bodies.
        /// </summary>
        /// <param name="p">The pawn to check for eligibility.</param>
        /// <returns>true if they should be exempt, false otherwise.</returns>
        public static ExemptionReason CheckExemptions(Pawn p)
        {
            if (GlobalSettings.bodyChangeFemale is false && p.gender is Gender.Female)
                return new ExemptionReason("RR_ExemptionReason_FemaleDisabled".Translate());
            else if (GlobalSettings.bodyChangeMale is false && p.gender is Gender.Male)
                return new ExemptionReason("RR_ExemptionReason_MaleDisabled".Translate());
            else if (GlobalSettings.bodyChangeFriendlyNPC is false && p.Faction != Faction.OfPlayer && p.Faction.AllyOrNeutralTo(Faction.OfPlayer))
                return new ExemptionReason("RR_ExemptionReason_FriendlyDisabled".Translate());
            else if (GlobalSettings.bodyChangeHostileNPC is false && p.Faction.HostileTo(Faction.OfPlayer) && !p.IsPrisoner)
                return new ExemptionReason("RR_ExemptionReason_HostileDisabled".Translate());
            else if (GlobalSettings.bodyChangePrisoners is false && p.IsPrisoner)
                return new ExemptionReason("RR_ExemptionReason_PrisonerDisabled".Translate());
            else if (GlobalSettings.bodyChangeSlaves is false && p.IsSlaveOfColony)
                return new ExemptionReason("RR_ExemptionReason_SlaveDisabled".Translate());
            else if (GlobalSettings.minimumAgeForCustomBody.threshold > (p?.ageTracker?.AgeBiologicalYears ?? 0) || GlobalSettings.maximumAgeForCustomBody.threshold < (p?.ageTracker?.AgeBiologicalYears ?? 0))
                return new ExemptionReason("RR_ExemptionReason_AgeDisabled".Translate());
            else
                return false;

        }


        internal static void AssignPersonalCategoricalExemptions(PawnBodyType_ThingComp comp)
        {
            if (comp is null)
                return;

            if (!comp.parent?.AsPawn().RaceProps.Humanlike ?? false)
                return;

            comp.CategoricallyExempt = CheckExemptions(comp.parent.AsPawn());
        }

        // Snapshot only authoritative owners in this Game. Refresh never adopts
        // an unbound comp or one retained from a previous game.
        internal static List<Pawn> CurrentGamePresentationPawns(Game game)
        {
            var pawns = new List<Pawn>();
            if (!UnityData.IsInMainThread || game == null || !ReferenceEquals(game, Current.Game)) return pawns;

            foreach (var map in game.Maps)
                if (map?.mapPawns != null) pawns.AddRange(map.mapPawns.AllPawns);
            if (game.World?.worldPawns != null) pawns.AddRange(game.World.worldPawns.AllPawnsAliveOrDead);
            if (game.InitData?.startingAndOptionalPawns != null) pawns.AddRange(game.InitData.startingAndOptionalPawns);

            return pawns.Distinct().Where(pawn => pawn != null && !pawn.Destroyed && !pawn.Discarded &&
                ReferenceEquals(pawn.TryGetComp<PawnBodyType_ThingComp>()?.PresentationGame, game)).ToList();
        }

        internal static void UpdateAllPawnSprites()
        {
            foreach (var pawn in CurrentGamePresentationPawns(Current.Game))
            {
                var comp = pawn.TryGetComp<PawnBodyType_ThingComp>();
                AssignPersonalCategoricalExemptions(comp);
                UpdatePawnSprite(pawn, comp.PersonallyExempt, comp.CategoricallyExempt, true, false);
            }
        }


        internal static void AssignBodyTypeCategoricalExemptions(bool updatePawnSprite = false)
        {
            foreach (var pawn in CurrentGamePresentationPawns(Current.Game))
            {
                var comp = pawn.TryGetComp<PawnBodyType_ThingComp>();
                AssignPersonalCategoricalExemptions(comp);
                if (updatePawnSprite)
                    UpdatePawnSprite(pawn, comp.PersonallyExempt, comp.CategoricallyExempt, true, false);
            }
        }

    }
}

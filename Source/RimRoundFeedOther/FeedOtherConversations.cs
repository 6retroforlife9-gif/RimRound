using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimRound.FeedOther
{
    /// <summary>
    /// One editable library of context-filtered social lines. The actual text is
    /// stored in 1.6/Defs/RimRound_FeedOtherConversations.xml, not in this DLL.
    /// </summary>
    public class FeedOtherConversationDef : Def
    {
        public List<FeedOtherConversationEntry> entries = new List<FeedOtherConversationEntry>();
    }

    public class FeedOtherConversationEntry
    {
        public string activity = "Any";
        public string phase = "Any";
        public string relationship = "Any";
        public string speech = "Any";
        public string tone = "Any";
        // Optional post-meal context filters. Existing XML entries omit these
        // fields and therefore continue to match as "Any".
        public string firstWeightGoal = "Any";
        public string secondWeightGoal = "Any";
        public string firstSize = "Any";
        public string secondSize = "Any";
        public string fullness = "Any";
        public string fullnessMood = "Any";
        // Either, Feeder, FedPawn or MutualNarration. For SharedMeal,
        // Feeder/FedPawn simply mean the first/second pawn supplied by the job.
        public string speakerRole = "Either";
        public List<string> lines = new List<string>();
    }

    public class FeedOtherConversationState : IExposable
    {
        public int nextConversationTick;
        public int conversationCount;
        public bool openingPlayed;
        public bool finishingPlayed;
        public bool firstPawnSpeaksNext = true;
        public List<string> recentLineIds = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref nextConversationTick, "nextConversationTick", 0);
            Scribe_Values.Look(ref conversationCount, "conversationCount", 0);
            Scribe_Values.Look(ref openingPlayed, "openingPlayed", false);
            Scribe_Values.Look(ref finishingPlayed, "finishingPlayed", false);
            Scribe_Values.Look(ref firstPawnSpeaksNext, "firstPawnSpeaksNext", true);
            Scribe_Collections.Look(ref recentLineIds, "recentLineIds", LookMode.Value);
            if (recentLineIds == null)
            {
                recentLineIds = new List<string>();
            }
        }
    }

    /// <summary>
    /// Save-safe social-log entry that stores the final rendered sentence rather
    /// than a temporary runtime RulePackDef. This means dialogue edits never put
    /// invalid def references into an existing save.
    /// </summary>
    public class PlayLogEntry_FeedOtherConversation : LogEntry
    {
        private Pawn initiator;
        private Pawn recipient;
        private string renderedText;

        public PlayLogEntry_FeedOtherConversation()
            : base(null)
        {
        }

        public PlayLogEntry_FeedOtherConversation(Pawn initiator, Pawn recipient, string renderedText)
            : base(FeedOtherDefOf.RR_FeedOtherConversationLog)
        {
            this.initiator = initiator;
            this.recipient = recipient;
            this.renderedText = renderedText;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref initiator, "initiator");
            Scribe_References.Look(ref recipient, "recipient");
            Scribe_Values.Look(ref renderedText, "renderedText", string.Empty);
        }

        public override bool Concerns(Thing thing)
        {
            return thing != null && (thing == initiator || thing == recipient);
        }

        public override IEnumerable<Thing> GetConcerns()
        {
            if (initiator != null)
            {
                yield return initiator;
            }

            if (recipient != null && recipient != initiator)
            {
                yield return recipient;
            }
        }

        protected override string ToGameStringFromPOV_Worker(Thing pov, bool forceLog)
        {
            return renderedText ?? string.Empty;
        }

        public override string GetTipString()
        {
            return renderedText ?? string.Empty;
        }
    }

    public class InteractionWorker_FeedOtherConversation : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            // The Def supplies a standard speech symbol, but normal random social
            // interaction selection must never pick it outside Feed Other jobs.
            return 0f;
        }
    }

    public static class FeedOtherConversationUtility
    {
        public const string SharedMealActivity = "SharedMeal";
        public const string FeedingActivity = "Feeding";
        public const string FeedingInPlaceActivity = "FeedingInPlace";
        public const string PostSharedMealActivity = "PostSharedMeal";
        public const string PostFeedingActivity = "PostFeeding";

        private const string StartingPhase = "Starting";
        private const string OngoingPhase = "Ongoing";
        private const string FinishingPhase = "Finishing";

        private const string BothSpeaking = "BothSpeaking";
        private const string OneSpeaking = "OneSpeaking";
        private const string NeitherSpeaking = "NeitherSpeaking";

        private const string PositiveTone = "Positive";
        private const string NeutralTone = "Neutral";
        private const string ReluctantTone = "Reluctant";

        private const string EitherSpeakerRole = "Either";
        private const string FeederSpeakerRole = "Feeder";
        private const string FedPawnSpeakerRole = "FedPawn";
        private const string MutualNarrationSpeakerRole = "MutualNarration";

        private const int OpeningDelayMinimumTicks = 180;
        private const int OpeningDelayMaximumTicks = 360;
        private const int ConversationDelayMinimumTicks = 900;
        private const int ConversationDelayMaximumTicks = 1500;
        private const int RecentLineMemory = 3;

        private static FeedOtherConversationDef Library =>
            DefDatabase<FeedOtherConversationDef>.GetNamedSilentFail("RR_FeedOtherConversationLibrary");

        public static void TickConversation(
            FeedOtherConversationState state,
            Pawn first,
            Pawn second,
            string activity)
        {
            if (!FeedOtherMod.Settings.feedingDialogueEnabled ||
                !CanRun(state, first, second, activity))
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (state.nextConversationTick <= 0)
            {
                state.nextConversationTick = now + Rand.RangeInclusive(
                    OpeningDelayMinimum(activity),
                    OpeningDelayMaximum(activity));
                return;
            }

            if (now < state.nextConversationTick || state.conversationCount >= MaximumLines(activity))
            {
                return;
            }

            string phase = state.openingPlayed ? OngoingPhase : StartingPhase;
            if (TryConversation(state, first, second, activity, phase))
            {
                if (phase == StartingPhase)
                {
                    state.openingPlayed = true;
                }

                state.conversationCount++;
            }

            state.nextConversationTick = now + Rand.RangeInclusive(
                ConversationDelayMinimum(activity),
                ConversationDelayMaximum(activity));
        }

        public static void TryFinishingConversation(
            FeedOtherConversationState state,
            Pawn first,
            Pawn second,
            string activity)
        {
            if (!FeedOtherMod.Settings.feedingDialogueEnabled ||
                !CanRun(state, first, second, activity) || state.finishingPlayed)
            {
                return;
            }

            // Preserve room for the closing interaction even when a long session
            // has already used its normal line allowance.
            if (TryConversation(state, first, second, activity, FinishingPhase))
            {
                state.finishingPlayed = true;
                state.conversationCount++;
            }
        }

        private static bool TryConversation(
            FeedOtherConversationState state,
            Pawn first,
            Pawn second,
            string activity,
            string phase)
        {
            FeedOtherConversationDef library = Library;
            InteractionDef interaction = FeedOtherDefOf.RR_FeedOtherConversation;
            LogEntryDef logDef = FeedOtherDefOf.RR_FeedOtherConversationLog;
            if (library?.entries == null || library.entries.Count == 0 ||
                interaction == null || logDef == null)
            {
                return false;
            }

            bool firstCanSpeak = CanSpeak(first);
            bool secondCanSpeak = CanSpeak(second);
            string speech = firstCanSpeak && secondCanSpeak
                ? BothSpeaking
                : firstCanSpeak || secondCanSpeak
                    ? OneSpeaking
                    : NeitherSpeaking;
            string relationship = Relationship(first, second);
            string tone = Tone(first, second, activity);
            string firstWeightGoal = WeightGoal(first);
            string secondWeightGoal = WeightGoal(second);
            string firstSize = SizeContext(first);
            string secondSize = SizeContext(second);
            string fullness = FullnessContext(first, second, activity);
            string fullnessMood = FullnessMoodContext(first, second, activity);
            string preferredRole = state.firstPawnSpeaksNext
                ? FeederSpeakerRole
                : FedPawnSpeakerRole;

            List<LineCandidate> candidates = BuildCandidates(
                library,
                activity,
                phase,
                relationship,
                speech,
                tone,
                firstWeightGoal,
                secondWeightGoal,
                firstSize,
                secondSize,
                fullness,
                fullnessMood,
                preferredRole,
                firstCanSpeak,
                secondCanSpeak,
                state.recentLineIds);
            if (candidates.Count == 0)
            {
                // A malformed or heavily customised XML file should degrade to
                // its broadest valid lines rather than stopping conversations.
                candidates = BuildCandidates(
                    library,
                    activity,
                    phase,
                    "Any",
                    speech,
                    tone,
                    firstWeightGoal,
                    secondWeightGoal,
                    firstSize,
                    secondSize,
                    fullness,
                    fullnessMood,
                    preferredRole,
                    firstCanSpeak,
                    secondCanSpeak,
                    state.recentLineIds);
            }

            if (candidates.Count == 0)
            {
                // If a very small custom pool has used every remembered line,
                // allow an older line again rather than silently ending chatter.
                candidates = BuildCandidates(
                    library,
                    activity,
                    phase,
                    relationship,
                    speech,
                    tone,
                    firstWeightGoal,
                    secondWeightGoal,
                    firstSize,
                    secondSize,
                    fullness,
                    fullnessMood,
                    preferredRole,
                    firstCanSpeak,
                    secondCanSpeak,
                    null);
            }

            if (candidates.Count == 0 && !relationship.Equals("Any", StringComparison.OrdinalIgnoreCase))
            {
                candidates = BuildCandidates(
                    library,
                    activity,
                    phase,
                    "Any",
                    speech,
                    tone,
                    firstWeightGoal,
                    secondWeightGoal,
                    firstSize,
                    secondSize,
                    fullness,
                    fullnessMood,
                    preferredRole,
                    firstCanSpeak,
                    secondCanSpeak,
                    null);
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            LineCandidate chosen = ChooseCandidate(candidates);
            Pawn speaker;
            Pawn listener;
            ResolveSpeaker(
                chosen.speakerRole,
                first,
                second,
                firstCanSpeak,
                secondCanSpeak,
                state.firstPawnSpeaksNext,
                out speaker,
                out listener);

            string rendered = RenderLine(chosen.line, speaker, listener, first, second);
            Find.PlayLog?.Add(new PlayLogEntry_FeedOtherConversation(
                speaker,
                listener,
                rendered));

            if (speaker?.Spawned == true)
            {
                if (speech == NeitherSpeaking)
                {
                    MoteMaker.MakeThoughtBubble(speaker, "…", false);
                }
                else
                {
                    UnityEngine.Texture2D symbol = interaction.GetSymbol(speaker.Faction, speaker.Ideo);
                    if (symbol != null)
                    {
                        MoteMaker.MakeSpeechBubble(speaker, symbol);
                    }
                }
            }

            if (firstCanSpeak && secondCanSpeak && speech != NeitherSpeaking)
            {
                // Prefer the other pawn next time. A role-locked line may still
                // override this preference, but an impossible perspective can
                // no longer be assigned to the wrong pawn.
                state.firstPawnSpeaksNext = speaker != first;
            }

            RememberLine(state, chosen.id);
            return true;
        }

        private static List<LineCandidate> BuildCandidates(
            FeedOtherConversationDef library,
            string activity,
            string phase,
            string relationship,
            string speech,
            string tone,
            string firstWeightGoal,
            string secondWeightGoal,
            string firstSize,
            string secondSize,
            string fullness,
            string fullnessMood,
            string preferredRole,
            bool firstCanSpeak,
            bool secondCanSpeak,
            List<string> recent)
        {
            List<LineCandidate> candidates = new List<LineCandidate>();
            for (int entryIndex = 0; entryIndex < library.entries.Count; entryIndex++)
            {
                FeedOtherConversationEntry entry = library.entries[entryIndex];
                string configuredRole = NormalizeSpeakerRole(entry?.speakerRole);
                if (entry?.lines == null ||
                    !Matches(entry.activity, activity) ||
                    !Matches(entry.phase, phase) ||
                    !Matches(entry.relationship, relationship) ||
                    !Matches(entry.speech, speech) ||
                    !Matches(entry.tone, tone) ||
                    !Matches(entry.firstWeightGoal, firstWeightGoal) ||
                    !Matches(entry.secondWeightGoal, secondWeightGoal) ||
                    !Matches(entry.firstSize, firstSize) ||
                    !Matches(entry.secondSize, secondSize) ||
                    !Matches(entry.fullness, fullness) ||
                    !Matches(entry.fullnessMood, fullnessMood) ||
                    !RoleCanRun(configuredRole, firstCanSpeak, secondCanSpeak, speech))
                {
                    continue;
                }

                float weight = 1f;
                weight += Exact(entry.activity, activity) ? 2f : 0f;
                weight += Exact(entry.phase, phase) ? 2f : 0f;
                weight += Exact(entry.relationship, relationship) ? 1.5f : 0f;
                weight += Exact(entry.speech, speech) ? 1.5f : 0f;
                weight += Exact(entry.tone, tone) ? 2f : 0f;
                weight += Exact(entry.firstWeightGoal, firstWeightGoal) ? 2.5f : 0f;
                weight += Exact(entry.secondWeightGoal, secondWeightGoal) ? 2.5f : 0f;
                weight += Exact(entry.firstSize, firstSize) ? 1.5f : 0f;
                weight += Exact(entry.secondSize, secondSize) ? 1.5f : 0f;
                weight += Exact(entry.fullness, fullness) ? 2f : 0f;
                weight += Exact(entry.fullnessMood, fullnessMood) ? 2f : 0f;
                weight += configuredRole.Equals(preferredRole, StringComparison.OrdinalIgnoreCase) ? 1.5f : 0f;
                weight += configuredRole == EitherSpeakerRole ? 0.25f : 0f;

                for (int lineIndex = 0; lineIndex < entry.lines.Count; lineIndex++)
                {
                    string line = entry.lines[lineIndex];
                    if (line.NullOrEmpty())
                    {
                        continue;
                    }

                    string id = entryIndex + ":" + lineIndex;
                    if (recent != null && recent.Contains(id))
                    {
                        continue;
                    }

                    candidates.Add(new LineCandidate(id, line, weight, configuredRole));
                }
            }

            return candidates;
        }

        private static LineCandidate ChooseCandidate(List<LineCandidate> candidates)
        {
            float total = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                total += candidates[i].weight;
            }

            float pick = Rand.Value * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                pick -= candidates[i].weight;
                if (pick <= 0f)
                {
                    return candidates[i];
                }
            }

            return candidates[candidates.Count - 1];
        }

        private static string RenderLine(
            string line,
            Pawn speaker,
            Pawn listener,
            Pawn first,
            Pawn second)
        {
            if (line.NullOrEmpty())
            {
                return string.Empty;
            }

            string speakerName = PawnName(speaker, "Someone");
            string listenerName = PawnName(listener, "someone");
            string firstName = PawnName(first, "Someone");
            string secondName = PawnName(second, "someone");
            string speakerPossessive = PawnPossessive(speaker);
            string listenerPossessive = PawnPossessive(listener);
            string firstPossessive = PawnPossessive(first);
            string secondPossessive = PawnPossessive(second);
            string speakerSize = SizeDescription(speaker);
            string listenerSize = SizeDescription(listener);
            string firstSize = SizeDescription(first);
            string secondSize = SizeDescription(second);

            return line
                // Traditional interaction tokens follow the actual speaker.
                .Replace("[INITIATOR_nameDef]", speakerName)
                .Replace("[RECIPIENT_nameDef]", listenerName)
                .Replace("[INITIATOR_possessive]", speakerPossessive)
                .Replace("[RECIPIENT_possessive]", listenerPossessive)
                // Explicit activity-role tokens never swap when the speaker does.
                .Replace("[FEEDER_nameDef]", firstName)
                .Replace("[FEDPAWN_nameDef]", secondName)
                .Replace("[FED_PAWN_nameDef]", secondName)
                .Replace("[FEEDER_possessive]", firstPossessive)
                .Replace("[FEDPAWN_possessive]", secondPossessive)
                .Replace("[FED_PAWN_possessive]", secondPossessive)
                .Replace("[INITIATOR_sizeDesc]", speakerSize)
                .Replace("[RECIPIENT_sizeDesc]", listenerSize)
                .Replace("[FEEDER_sizeDesc]", firstSize)
                .Replace("[FEDPAWN_sizeDesc]", secondSize)
                .Replace("[FED_PAWN_sizeDesc]", secondSize);
        }

        private static string PawnName(Pawn pawn, string fallback)
        {
            return pawn?.NameShortColored.Resolve() ?? fallback;
        }

        private static string PawnPossessive(Pawn pawn)
        {
            return pawn != null ? GenText.Possessive(pawn) : "their";
        }

        private static void ResolveSpeaker(
            string configuredRole,
            Pawn first,
            Pawn second,
            bool firstCanSpeak,
            bool secondCanSpeak,
            bool preferFirst,
            out Pawn speaker,
            out Pawn listener)
        {
            string role = NormalizeSpeakerRole(configuredRole);
            if (role == FeederSpeakerRole && firstCanSpeak)
            {
                speaker = first;
                listener = second;
                return;
            }

            if (role == FedPawnSpeakerRole && secondCanSpeak)
            {
                speaker = second;
                listener = first;
                return;
            }

            if (firstCanSpeak && secondCanSpeak)
            {
                speaker = preferFirst ? first : second;
                listener = speaker == first ? second : first;
                return;
            }

            if (firstCanSpeak)
            {
                speaker = first;
                listener = second;
                return;
            }

            if (secondCanSpeak)
            {
                speaker = second;
                listener = first;
                return;
            }

            // Fully non-verbal lines use stable activity roles for log text.
            speaker = first;
            listener = second;
        }

        private static bool RoleCanRun(
            string configuredRole,
            bool firstCanSpeak,
            bool secondCanSpeak,
            string speech)
        {
            string role = NormalizeSpeakerRole(configuredRole);
            if (speech == NeitherSpeaking)
            {
                return role == MutualNarrationSpeakerRole || role == EitherSpeakerRole;
            }

            if (role == FeederSpeakerRole)
            {
                return firstCanSpeak;
            }

            if (role == FedPawnSpeakerRole)
            {
                return secondCanSpeak;
            }

            return firstCanSpeak || secondCanSpeak;
        }

        private static string NormalizeSpeakerRole(string configuredRole)
        {
            if (configuredRole.NullOrEmpty())
            {
                return EitherSpeakerRole;
            }

            if (configuredRole.Equals(FeederSpeakerRole, StringComparison.OrdinalIgnoreCase))
            {
                return FeederSpeakerRole;
            }

            if (configuredRole.Equals(FedPawnSpeakerRole, StringComparison.OrdinalIgnoreCase) ||
                configuredRole.Equals("Recipient", StringComparison.OrdinalIgnoreCase))
            {
                return FedPawnSpeakerRole;
            }

            if (configuredRole.Equals(MutualNarrationSpeakerRole, StringComparison.OrdinalIgnoreCase) ||
                configuredRole.Equals("Mutual", StringComparison.OrdinalIgnoreCase))
            {
                return MutualNarrationSpeakerRole;
            }

            return EitherSpeakerRole;
        }

        private static void RememberLine(FeedOtherConversationState state, string id)
        {
            if (state.recentLineIds == null)
            {
                state.recentLineIds = new List<string>();
            }

            state.recentLineIds.Add(id);
            while (state.recentLineIds.Count > RecentLineMemory)
            {
                state.recentLineIds.RemoveAt(0);
            }
        }

        private static bool CanRun(
            FeedOtherConversationState state,
            Pawn first,
            Pawn second,
            string activity)
        {
            return state != null && first != null && second != null &&
                first.Spawned && second.Spawned && first.Map == second.Map &&
                !activity.NullOrEmpty();
        }

        private static bool CanSpeak(Pawn pawn)
        {
            return pawn?.health?.capacities != null &&
                pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking);
        }

        private static string Relationship(Pawn first, Pawn second)
        {
            if (LovePartnerRelationUtility.LovePartnerRelationExists(first, second))
            {
                return "Romantic";
            }

            if ((first.relations?.FamilyByBlood?.Contains(second) ?? false) ||
                (second.relations?.FamilyByBlood?.Contains(first) ?? false))
            {
                return "Family";
            }

            int firstOpinion = first.relations?.OpinionOf(second) ?? 0;
            int secondOpinion = second.relations?.OpinionOf(first) ?? 0;
            if (firstOpinion < 0 || secondOpinion < 0)
            {
                return "Strained";
            }

            if (firstOpinion >= 40 || secondOpinion >= 40)
            {
                return "Friend";
            }

            return "General";
        }

        private static string Tone(Pawn first, Pawn second, string activity)
        {
            WeightOpinion firstOpinion = NormalizeWeightOpinion(
                first.TryGetComp<ThingComp_PawnAttitude>()?.weightOpinion ?? WeightOpinion.None);
            WeightOpinion secondOpinion = NormalizeWeightOpinion(
                second.TryGetComp<ThingComp_PawnAttitude>()?.weightOpinion ?? WeightOpinion.None);

            // In one-way feeding, the fed pawn's attitude determines whether the
            // activity feels enthusiastic, ordinary or reluctant. This prevents
            // the feeder being assigned dialogue that describes personally
            // tolerating or learning to accept being fed.
            if (IsOneWayFeedingActivity(activity))
            {
                return OpinionTone(secondOpinion);
            }

            if (firstOpinion <= WeightOpinion.NeutralMinus ||
                secondOpinion <= WeightOpinion.NeutralMinus)
            {
                return ReluctantTone;
            }

            if (firstOpinion >= WeightOpinion.NeutralPlus &&
                secondOpinion >= WeightOpinion.NeutralPlus)
            {
                return PositiveTone;
            }

            return NeutralTone;
        }

        private static string OpinionTone(WeightOpinion opinion)
        {
            if (opinion <= WeightOpinion.NeutralMinus)
            {
                return ReluctantTone;
            }

            if (opinion >= WeightOpinion.NeutralPlus)
            {
                return PositiveTone;
            }

            return NeutralTone;
        }

        private static WeightOpinion NormalizeWeightOpinion(WeightOpinion opinion)
        {
            return opinion == WeightOpinion.None ? WeightOpinion.Neutral : opinion;
        }

        private static string WeightGoal(Pawn pawn)
        {
            WeightOpinion opinion = NormalizeWeightOpinion(
                pawn?.TryGetComp<ThingComp_PawnAttitude>()?.weightOpinion ?? WeightOpinion.None);
            int sizeStage = SafeThoughtIndex(pawn);

            if (opinion >= WeightOpinion.NeutralPlus)
            {
                return sizeStage >= 8 ? "CelebrateSize" : "Gain";
            }

            if (opinion <= WeightOpinion.NeutralMinus)
            {
                return sizeStage >= 4 ? "Lose" : "StayLean";
            }

            return "Neutral";
        }

        private static string SizeContext(Pawn pawn)
        {
            int stage = SafeThoughtIndex(pawn);
            if (stage <= 2)
            {
                return "Thin";
            }

            if (stage <= 7)
            {
                return "Soft";
            }

            if (stage <= 11)
            {
                return "Large";
            }

            return "Extreme";
        }

        private static string SizeDescription(Pawn pawn)
        {
            switch (SizeContext(pawn))
            {
                case "Thin":
                    return "slim build";
                case "Soft":
                    return "soft figure";
                case "Large":
                    return "heavy figure";
                case "Extreme":
                    return "immense size";
                default:
                    return "current figure";
            }
        }

        private static int SafeThoughtIndex(Pawn pawn)
        {
            try
            {
                return pawn == null ? 0 : WeightOpinionUtility.GetThoughtIndex(pawn);
            }
            catch
            {
                return 0;
            }
        }

        private static string FullnessContext(Pawn first, Pawn second, string activity)
        {
            // A one-way feeder has not eaten. Its post-feeding dialogue must use
            // only the fed pawn's fullness, otherwise the feeder can be assigned
            // lines about personally digesting or being unable to stand.
            float fullness = IsOneWayFeedingActivity(activity)
                ? FeedOtherUtility.FullnessFractionOfHardLimit(second)
                : Math.Max(
                    FeedOtherUtility.FullnessFractionOfHardLimit(first),
                    FeedOtherUtility.FullnessFractionOfHardLimit(second));
            if (fullness >= 0.85f)
            {
                return "Stuffed";
            }

            if (fullness >= FeedOtherUtility.VeryFullFractionOfHardLimit)
            {
                return "VeryFull";
            }

            if (fullness >= 0.50f)
            {
                return "Full";
            }

            return "Comfortable";
        }

        private static string FullnessMoodContext(Pawn first, Pawn second, string activity)
        {
            if (IsOneWayFeedingActivity(activity))
            {
                if (HasMemory(second, FeedOtherDefOf.RR_FeedOtherVeryFullDiscomfort))
                {
                    return "Negative";
                }

                if (HasMemory(second, FeedOtherDefOf.RR_FeedOtherVeryFullMood))
                {
                    return "Positive";
                }

                return "Neutral";
            }

            if (HasMemory(first, FeedOtherDefOf.RR_FeedOtherVeryFullDiscomfort) ||
                HasMemory(second, FeedOtherDefOf.RR_FeedOtherVeryFullDiscomfort))
            {
                return "Negative";
            }

            if (HasMemory(first, FeedOtherDefOf.RR_FeedOtherVeryFullMood) ||
                HasMemory(second, FeedOtherDefOf.RR_FeedOtherVeryFullMood))
            {
                return "Positive";
            }

            return "Neutral";
        }

        private static bool HasMemory(Pawn pawn, ThoughtDef thought)
        {
            return thought != null &&
                pawn?.needs?.mood?.thoughts?.memories?.GetFirstMemoryOfDef(thought) != null;
        }

        private static bool IsOneWayFeedingActivity(string activity)
        {
            return activity == FeedingActivity ||
                activity == FeedingInPlaceActivity ||
                activity == PostFeedingActivity;
        }

        private static bool IsPostMealActivity(string activity)
        {
            return activity == PostSharedMealActivity || activity == PostFeedingActivity;
        }

        private static int OpeningDelayMinimum(string activity)
        {
            return IsPostMealActivity(activity) ? 120 : OpeningDelayMinimumTicks;
        }

        private static int OpeningDelayMaximum(string activity)
        {
            return IsPostMealActivity(activity) ? 240 : OpeningDelayMaximumTicks;
        }

        private static int ConversationDelayMinimum(string activity)
        {
            return IsPostMealActivity(activity) ? 600 : ConversationDelayMinimumTicks;
        }

        private static int ConversationDelayMaximum(string activity)
        {
            return IsPostMealActivity(activity) ? 900 : ConversationDelayMaximumTicks;
        }

        private static int MaximumLines(string activity)
        {
            if (IsPostMealActivity(activity))
            {
                // Two normal lines plus the closing line gives a maximum of three.
                return 2;
            }

            return activity == SharedMealActivity ? 5 : 4;
        }

        private static bool Matches(string configured, string actual)
        {
            return configured.NullOrEmpty() ||
                configured.Equals("Any", StringComparison.OrdinalIgnoreCase) ||
                configured.Equals(actual, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Exact(string configured, string actual)
        {
            return !configured.NullOrEmpty() &&
                !configured.Equals("Any", StringComparison.OrdinalIgnoreCase) &&
                configured.Equals(actual, StringComparison.OrdinalIgnoreCase);
        }

        private struct LineCandidate
        {
            public readonly string id;
            public readonly string line;
            public readonly float weight;
            public readonly string speakerRole;

            public LineCandidate(string id, string line, float weight, string speakerRole)
            {
                this.id = id;
                this.line = line;
                this.weight = weight;
                this.speakerRole = speakerRole;
            }
        }
    }
}

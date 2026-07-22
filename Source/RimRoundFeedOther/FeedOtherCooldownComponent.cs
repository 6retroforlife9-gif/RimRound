using System.Collections.Generic;
using Verse;

namespace RimRound.FeedOther
{
    /// <summary>
    /// Saved recreation cooldowns for Feed Other sessions. Manual right-click
    /// orders ignore them by default, or share them when configured to do so.
    /// </summary>
    public class FeedOtherCooldownComponent : GameComponent
    {
        public static int ParticipantCooldownTicks => FeedOtherMod.Settings.ParticipantCooldownTicks;
        public static int OneWayFeederCooldownTicks => FeedOtherMod.Settings.OneWayFeederCooldownTicks;
        public static int PairCooldownTicks => FeedOtherMod.Settings.PairCooldownTicks;
        public static int PrisonerMealDeliveryCooldownTicks => FeedOtherMod.Settings.PrisonerMealDeliveryCooldownTicks;
        private const int PrisonerFattenFullnessLatchMarker = int.MaxValue;
        private const float PrisonerFattenLatchEpsilon = 0.0001f;

        private Dictionary<int, int> participantCooldownUntil = new Dictionary<int, int>();
        private Dictionary<string, int> pairCooldownUntil = new Dictionary<string, int>();
        private Dictionary<int, int> prisonerFattenCooldownUntil = new Dictionary<int, int>();
        private Dictionary<int, int> prisonerMealDeliveryCooldownUntil = new Dictionary<int, int>();

        public FeedOtherCooldownComponent(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(
                ref participantCooldownUntil,
                "rrFeedOtherParticipantCooldownUntil",
                LookMode.Value,
                LookMode.Value);
            Scribe_Collections.Look(
                ref pairCooldownUntil,
                "rrFeedOtherPairCooldownUntil",
                LookMode.Value,
                LookMode.Value);
            Scribe_Collections.Look(
                ref prisonerFattenCooldownUntil,
                "rrPrisonerFattenCooldownUntil",
                LookMode.Value,
                LookMode.Value);
            Scribe_Collections.Look(
                ref prisonerMealDeliveryCooldownUntil,
                "rrPrisonerMealDeliveryCooldownUntil",
                LookMode.Value,
                LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureCollections();
                RemoveExpiredEntries();
            }
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            NotRegalBedUtility.RepairLoadedBeds();
        }


        public override void GameComponentTick()
        {
            base.GameComponentTick();
            int now = Find.TickManager?.TicksGame ?? 0;
            if (FeedOtherMod.Settings.prisonerFeedingOverhaulEnabled &&
                FeedOtherMod.Settings.prisonerFattenEnabled &&
                now > 0 && now % 60 == 0)
            {
                PrisonerFatteningFoodPatch.EnforceAllFattenBedLocks();
            }
        }

        public static bool IsPrisonerMealDeliveryOnCooldown(Pawn prisoner)
        {
            FeedOtherCooldownComponent component = Instance;
            if (component == null || prisoner == null)
            {
                return false;
            }

            return component.IsActive(
                component.prisonerMealDeliveryCooldownUntil,
                prisoner.thingIDNumber);
        }

        public static void NotifyPrisonerMealDelivered(Pawn prisoner)
        {
            FeedOtherCooldownComponent component = Instance;
            if (component == null || prisoner == null)
            {
                return;
            }

            component.EnsureCollections();
            int now = Find.TickManager?.TicksGame ?? 0;
            component.prisonerMealDeliveryCooldownUntil[prisoner.thingIDNumber] =
                now + PrisonerMealDeliveryCooldownTicks;
        }

        public static bool IsParticipantOnCooldown(Pawn pawn)
        {
            FeedOtherCooldownComponent component = Instance;
            if (component == null || pawn == null)
            {
                return false;
            }

            return component.IsActive(component.participantCooldownUntil, pawn.thingIDNumber);
        }


        public static bool IsPrisonerFattenOnCooldown(Pawn prisoner)
        {
            FeedOtherCooldownComponent component = Instance;
            if (component == null || prisoner == null)
            {
                return false;
            }

            component.EnsureCollections();
            int marker;
            if (!component.prisonerFattenCooldownUntil.TryGetValue(
                    prisoner.thingIDNumber,
                    out marker))
            {
                return false;
            }

            // Discard obsolete finite time-based cooldowns left by v1.0.54 or
            // v1.0.55. v1.0.57 uses only the fullness hysteresis marker.
            if (marker != PrisonerFattenFullnessLatchMarker)
            {
                component.prisonerFattenCooldownUntil.Remove(prisoner.thingIDNumber);
                return false;
            }

            RimRound.Comps.FullnessAndDietStats_ThingComp fullness =
                prisoner.TryGetComp<RimRound.Comps.FullnessAndDietStats_ThingComp>();
            if (fullness == null || fullness.Disabled || fullness.HardLimit <= 0f)
            {
                return true;
            }

            float resumeTarget = fullness.HardLimit *
                PrisonerFatteningFoodPatch.FattenResumeFraction;
            if (fullness.CurrentFullness + PrisonerFattenLatchEpsilon < resumeTarget)
            {
                component.prisonerFattenCooldownUntil.Remove(prisoner.thingIDNumber);
                return false;
            }

            return true;
        }

        public static void NotifyPrisonerFattenSessionCompleted(Pawn prisoner)
        {
            FeedOtherCooldownComponent component = Instance;
            if (component == null || prisoner == null)
            {
                return;
            }

            component.EnsureCollections();
            component.prisonerFattenCooldownUntil[prisoner.thingIDNumber] =
                PrisonerFattenFullnessLatchMarker;
        }

        public static void ClearPrisonerFattenCooldown(Pawn prisoner)
        {
            FeedOtherCooldownComponent component = Instance;
            if (component == null || prisoner == null)
            {
                return;
            }

            component.EnsureCollections();
            component.prisonerFattenCooldownUntil.Remove(prisoner.thingIDNumber);
        }

        public static bool IsPairOnCooldown(Pawn first, Pawn second)
        {
            FeedOtherCooldownComponent component = Instance;
            if (component == null || first == null || second == null)
            {
                return false;
            }

            return component.IsActive(component.pairCooldownUntil, PairKey(first, second));
        }

        public static void NotifyAutonomousSessionStarted(Pawn first, Pawn second, bool oneWayFeeding)
        {
            FeedOtherCooldownComponent component = Instance;
            if (component == null || first == null || second == null)
            {
                return;
            }

            component.EnsureCollections();
            int now = Find.TickManager?.TicksGame ?? 0;
            int recipientOrParticipantUntil = now + ParticipantCooldownTicks;
            int feederUntil = now + (oneWayFeeding
                ? OneWayFeederCooldownTicks
                : ParticipantCooldownTicks);

            component.participantCooldownUntil[first.thingIDNumber] = feederUntil;
            component.participantCooldownUntil[second.thingIDNumber] = recipientOrParticipantUntil;
            component.pairCooldownUntil[PairKey(first, second)] = now + PairCooldownTicks;
        }

        private static FeedOtherCooldownComponent Instance
        {
            get
            {
                return Verse.Current.Game?.GetComponent<FeedOtherCooldownComponent>();
            }
        }

        private static string PairKey(Pawn first, Pawn second)
        {
            int firstId = first.thingIDNumber;
            int secondId = second.thingIDNumber;
            return firstId < secondId
                ? firstId.ToString() + ":" + secondId.ToString()
                : secondId.ToString() + ":" + firstId.ToString();
        }

        private bool IsActive<TKey>(Dictionary<TKey, int> cooldowns, TKey key)
        {
            EnsureCollections();
            int until;
            if (!cooldowns.TryGetValue(key, out until))
            {
                return false;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            if (until > now)
            {
                return true;
            }

            cooldowns.Remove(key);
            return false;
        }

        private void EnsureCollections()
        {
            if (participantCooldownUntil == null)
            {
                participantCooldownUntil = new Dictionary<int, int>();
            }

            if (pairCooldownUntil == null)
            {
                pairCooldownUntil = new Dictionary<string, int>();
            }

            if (prisonerFattenCooldownUntil == null)
            {
                prisonerFattenCooldownUntil = new Dictionary<int, int>();
            }

            if (prisonerMealDeliveryCooldownUntil == null)
            {
                prisonerMealDeliveryCooldownUntil = new Dictionary<int, int>();
            }
        }

        private void RemoveExpiredEntries()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            RemoveExpiredEntries(participantCooldownUntil, now);
            RemoveExpiredEntries(pairCooldownUntil, now);
            RemoveExpiredEntries(prisonerFattenCooldownUntil, now);
            RemoveExpiredEntries(prisonerMealDeliveryCooldownUntil, now);
        }

        private static void RemoveExpiredEntries<TKey>(Dictionary<TKey, int> cooldowns, int now)
        {
            if (cooldowns == null || cooldowns.Count == 0)
            {
                return;
            }

            List<TKey> expired = new List<TKey>();
            foreach (KeyValuePair<TKey, int> entry in cooldowns)
            {
                if (entry.Value <= now)
                {
                    expired.Add(entry.Key);
                }
            }

            foreach (TKey key in expired)
            {
                cooldowns.Remove(key);
            }
        }
    }
}

using System;

namespace RimRound.Simulation
{
    public enum BodyDiscussionOutcome { Unavailable, NoChange, Receptive, Breakthrough, Irritated }

    public struct BodyDiscussionResult
    {
        public BodyDiscussionOutcome Outcome;
        public float Lower, Upper, Conviction, Irritation, Influence;
        public bool Completed => Outcome != BodyDiscussionOutcome.Unavailable;
    }

    // Pure calculation: callers commit the returned range through the canonical
    // preference owner. Neither this class nor its saved clocks owns body mass.
    public static class BodySocialProgression
    {
        public const int DiscussionTicks = 450;
        public const int CooldownTicks = 10000;
        public const int OutcomesPerDay = 2;
        public const int DayTicks = 60000;
        public const int MaximumInterval = 2500;

        public static bool CaptureTarget(float lower, float upper, bool larger, out float targetLower, out float targetUpper)
        {
            targetLower = targetUpper = 0f;
            if (!BodyPreferenceState.ValidRange(lower, upper)) return false;
            float factor = larger ? 2f : .5f;
            targetLower = Clamp(lower * factor, BodyPreferenceState.MinimumRatio, BodyPreferenceState.MaximumRatio);
            targetUpper = Clamp(upper * factor, BodyPreferenceState.MinimumRatio, BodyPreferenceState.MaximumRatio);
            return true;
        }

        public static BodyDiscussionResult Discuss(float lower, float upper, float conviction,
            float targetLower, float targetUpper, int social, float speech, float hearing, float consciousness,
            float opinion, float irritation, float supportiveBonus, float sample)
        {
            var result = new BodyDiscussionResult { Lower = lower, Upper = upper, Conviction = conviction, Irritation = irritation };
            if (!BodyPreferenceState.ValidRange(lower, upper) || !BodyPreferenceState.ValidRange(targetLower, targetUpper) ||
                !Unit(conviction) || !Unit(irritation) || !Unit(sample) || !Finite(speech) || !Finite(hearing) ||
                !Finite(consciousness) || !Finite(opinion) || !Finite(supportiveBonus) ||
                speech <= 0f || hearing <= 0f || consciousness <= 0f) return result;
            float capacity = Math.Min(Clamp(speech, 0f, 1f), Math.Min(Clamp(hearing, 0f, 1f), Clamp(consciousness, 0f, 1f)));
            float hostility = Math.Max(0f, -Clamp(opinion, -100f, 100f)) / 100f;
            float refusal = Math.Min(.85f, hostility * .5f + irritation * .6f);
            if (sample < refusal)
            {
                result.Outcome = BodyDiscussionOutcome.Irritated;
                result.Irritation = Clamp(irritation + .15f, 0f, 1f);
                return result;
            }
            result.Irritation = Clamp(irritation + .025f, 0f, 1f);
            if (AtTarget(lower, upper, targetLower, targetUpper))
            {
                result.Outcome = BodyDiscussionOutcome.NoChange;
                return result;
            }
            result.Influence = Clamp(.06f * (.30f + .0875f * Math.Max(0, Math.Min(20, social))) * capacity *
                (1f + Clamp(opinion, -100f, 100f) / 200f) * (1.5f - conviction) *
                (1f + Clamp(supportiveBonus, 0f, 1f)) * (1f - irritation * .5f), 0f, .30f);
            result.Conviction = Math.Max(0f, conviction - result.Influence);
            if (result.Conviction <= 0f)
            {
                result.Lower = targetLower;
                result.Upper = targetUpper;
                result.Conviction = .5f;
                result.Outcome = BodyDiscussionOutcome.Breakthrough;
            }
            else
            {
                result.Lower = LogLerp(lower, targetLower, result.Influence);
                result.Upper = LogLerp(upper, targetUpper, result.Influence);
                result.Outcome = BodyDiscussionOutcome.Receptive;
            }
            return result;
        }

        public static bool AtTarget(float lower, float upper, float a, float b)
            => Math.Abs(Math.Log(lower / a)) < .0001d && Math.Abs(Math.Log(upper / b)) < .0001d;
        public static float DecayIrritation(float irritation, int ticks, float listenerBonus)
        {
            if (!Unit(irritation) || ticks <= 0 || ticks > MaximumInterval || !Finite(listenerBonus)) return irritation;
            return Math.Max(0f, irritation - .25f * ticks / DayTicks * (1f + Clamp(listenerBonus, 0f, .2f)));
        }
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool Unit(float value) => Finite(value) && value >= 0f && value <= 1f;
        public static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        private static float LogLerp(float from, float to, float fraction) =>
            Clamp((float)Math.Exp(Math.Log(from) + (Math.Log(to) - Math.Log(from)) * fraction), BodyPreferenceState.MinimumRatio, BodyPreferenceState.MaximumRatio);
    }

    // Each participant owns its own saved clock. A recipient clock is shared by
    // every possible speaker; switching orders or toggling intent never resets it.
    public sealed class BodyDiscussionClock
    {
        public int Schema = 1;
        public int NextSpeakerTick, NextRecipientTick;
        public int OutcomeDay = -1, OutcomesToday;
        public string LastEventId = string.Empty;
        public int LastOutcomeTick = -1;
        public int LastOutcome;
        public float Irritation;
        public bool IsUsable => Schema == 1 && NextSpeakerTick >= 0 && NextRecipientTick >= 0 && OutcomeDay >= -1 &&
            OutcomesToday >= 0 && OutcomesToday <= BodySocialProgression.OutcomesPerDay && LastOutcomeTick >= -1 &&
            LastOutcome >= (int)BodyDiscussionOutcome.Unavailable && LastOutcome <= (int)BodyDiscussionOutcome.Irritated &&
            BodySocialProgression.Unit(Irritation);
        public bool CanParticipate(int tick, int day) => IsUsable && tick >= 0 && day >= 0 &&
            tick >= NextSpeakerTick && tick >= NextRecipientTick && tick > LastOutcomeTick &&
            (day > OutcomeDay || (day == OutcomeDay && OutcomesToday < BodySocialProgression.OutcomesPerDay));
        public bool CanRecord(int tick, int day, string eventId) => !string.IsNullOrEmpty(eventId) &&
            eventId != LastEventId && CanParticipate(tick, day);
        public bool Record(int tick, int day, string eventId, BodyDiscussionOutcome outcome, bool speaker)
        {
            if (!CanRecord(tick, day, eventId) || outcome <= BodyDiscussionOutcome.Unavailable || outcome > BodyDiscussionOutcome.Irritated ||
                tick > int.MaxValue - BodySocialProgression.CooldownTicks) return false;
            OutcomesToday = day == OutcomeDay ? OutcomesToday + 1 : 1;
            OutcomeDay = day;
            if (speaker) NextSpeakerTick = tick + BodySocialProgression.CooldownTicks;
            else NextRecipientTick = tick + BodySocialProgression.CooldownTicks;
            LastEventId = eventId; LastOutcomeTick = tick; LastOutcome = (int)outcome;
            return true;
        }
    }

    // Only actual active intervals count. Useful intake is an explicit committed
    // event, not an assignment, predicted intake, discarded food or UI read.
    public sealed class BodyPreferenceExperience
    {
        public int Schema = 1;
        public int WindowTicks, PleasantTicks, AdverseTicks;
        public double UsefulNutrition, ExpenditureNutrition;
        public bool IsUsable => Schema == 1 && WindowTicks >= 0 && WindowTicks < BodySocialProgression.DayTicks &&
            PleasantTicks >= 0 && PleasantTicks <= WindowTicks && AdverseTicks >= 0 && AdverseTicks <= WindowTicks &&
            Nonnegative(UsefulNutrition) && Nonnegative(ExpenditureNutrition);
        public void RecordIntake(float useful)
        {
            if (IsUsable && BodySocialProgression.Finite(useful) && useful > 0f && Nonnegative(UsefulNutrition + useful))
                UsefulNutrition += useful;
        }
        public void RecordResourceExpenditure(float nutrition)
        {
            if (IsUsable && BodySocialProgression.Finite(nutrition) && nutrition > 0f &&
                Nonnegative(ExpenditureNutrition + nutrition)) ExpenditureNutrition += nutrition;
        }
        public bool Observe(int ticks, float expenditure, float experiencedRatio, bool pleasant, bool adverse,
            float lower, float upper, out float nextLower, out float nextUpper)
        {
            nextLower = lower; nextUpper = upper;
            if (!IsUsable || ticks <= 0 || ticks > BodySocialProgression.MaximumInterval || !BodySocialProgression.Finite(expenditure) ||
                expenditure < 0f || !BodySocialProgression.Finite(experiencedRatio) || experiencedRatio <= 0f ||
                !BodyPreferenceState.ValidRange(lower, upper)) return false;
            int remaining = ticks;
            bool completed = false;
            while (remaining > 0)
            {
                int step = Math.Min(remaining, BodySocialProgression.DayTicks - WindowTicks);
                WindowTicks += step;
                if (adverse) AdverseTicks += step;
                else if (pleasant) PleasantTicks += step;
                ExpenditureNutrition += expenditure * ((double)step / ticks);
                remaining -= step;
                if (WindowTicks < BodySocialProgression.DayTicks) continue;
                double delta = 0d;
                if (AdverseTicks > 0) delta = -.02d * AdverseTicks / BodySocialProgression.DayTicks;
                else if (PleasantTicks >= BodySocialProgression.DayTicks * .5 && UsefulNutrition > ExpenditureNutrition && ExpenditureNutrition > 0d)
                {
                    double center = .5d * (Math.Log(nextLower) + Math.Log(nextUpper));
                    double distance = Math.Log(experiencedRatio) - center;
                    double rate = .01d * PleasantTicks / BodySocialProgression.DayTicks *
                        Math.Min(1d, (UsefulNutrition - ExpenditureNutrition) / ExpenditureNutrition);
                    delta = Math.Max(-rate, Math.Min(rate, distance));
                }
                nextLower = BodySocialProgression.Clamp((float)(nextLower * Math.Exp(delta)), BodyPreferenceState.MinimumRatio, BodyPreferenceState.MaximumRatio);
                nextUpper = BodySocialProgression.Clamp((float)(nextUpper * Math.Exp(delta)), BodyPreferenceState.MinimumRatio, BodyPreferenceState.MaximumRatio);
                WindowTicks = PleasantTicks = AdverseTicks = 0;
                UsefulNutrition = ExpenditureNutrition = 0d;
                completed = true;
            }
            return completed;
        }
        private static bool Nonnegative(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
    }
}

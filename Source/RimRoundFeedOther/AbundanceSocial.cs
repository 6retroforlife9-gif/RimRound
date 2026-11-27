using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.Comps;
using RimRound.Ideology;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    public static class AbundanceSocial
    {
        public static bool CanShare(Pawn first,Pawn second)=>first!=null && second!=null && first.Awake() && second.Awake() && CanPlanShare(first,second);
        public static bool CanPlanShare(Pawn first,Pawn second)=>first!=second && SocialFeedingPolicy.ControlledAdult(first) &&
            SocialFeedingPolicy.ControlledAdult(second) && (first.Awake() || RitualAttendance.CanWakeForFeast(first)) &&
            (second.Awake() || RitualAttendance.CanWakeForFeast(second)) && !first.HostileTo(second) &&
            (first.relations?.OpinionOf(second) ?? -1)>=0 && (second.relations?.OpinionOf(first) ?? -1)>=0;
        public static bool BellyFriendly(Pawn first,Pawn second)=>CanShare(first,second) &&
            (SocialFeedingPolicy.LikesLargerBodies(first) || AbundanceRoles.Has(first,AbundanceRoles.Feeder)) &&
            second.TryGetComp<ThingComp_PawnAttitude>()?.PreferredLowerRatio>=2f;
        public static void Converse(Pawn server,Pawn eater,AbundanceMeal meal,LordJob_AbundanceMeals ritual)
        {
            if(meal==null || ritual==null)return;
            Pawn other=server==eater ? ritual.lord.ownedPawns.FirstOrDefault(p=>p!=server && CanShare(server,p) &&
                server.Position.InHorDistOf(p.Position,3f) && AbundanceBeds.SameRoom(server.Position,p.Position,server.Map)) : eater;
            if(other==null || !CanShare(server,other))return;
            var state=meal.Conversation as FeedOtherConversationState;
            if(state==null)meal.Conversation=state=new FeedOtherConversationState();
            FeedOtherConversationUtility.TickConversation(state,server,other,server==eater ? FeedOtherConversationUtility.SharedMealActivity : FeedOtherConversationUtility.FeedingInPlaceActivity);
        }
        public static Job TryGiveJob(Pawn pawn)
        {
            var ritual=AbundanceRituals.Session(pawn);
            if(ritual==null || !ritual.FoodReady || Find.TickManager.TicksGame<ritual.NextSocialTick || !AbundanceRituals.CapableServer(pawn))return null;
            var recipient=ritual.lord.ownedPawns.Where(p=>CanShare(pawn,p) &&
                !ritual.Meals.Any(m=>(m.Eater==p || m.Server==p) && !m.Finished) && AbundanceBeds.SameRoom(p.Position,pawn.Position,pawn.Map))
                .OrderBy(p=>p==ritual.HostPawn ? 0 : 1).ThenBy(p=>p.Position.DistanceToSquared(pawn.Position)).FirstOrDefault();
            if(recipient==null)return null;
            var cells=recipient==ritual.HostPawn ? AbundanceBeds.ApproachCells(ritual.Bed) : GenAdj.AdjacentCells.Select(c=>recipient.Position+c);
            var cell=cells.Where(c=>c.InBounds(pawn.Map) && c.Standable(pawn.Map) && c.InHorDistOf(recipient.Position,2.5f) &&
                pawn.CanReserveAndReach(c,PathEndMode.OnCell,Danger.Some) && pawn.CanReserve(recipient))
                .OrderBy(c=>pawn.Position.DistanceToSquared(c)).DefaultIfEmpty(IntVec3.Invalid).First();
            if(!cell.IsValid)return null;
            ritual.NextSocialTick=Find.TickManager.TicksGame+900;
            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RR_AbundanceSocial"),recipient,cell);
        }
    }
    public sealed class JobDriver_AbundanceSocial : JobDriver
    {
        Pawn Recipient=>job.targetA.Pawn;
        public override bool TryMakePreToilReservations(bool errorOnFailed)=>pawn.Reserve(Recipient,job,1,-1,null,errorOnFailed) && pawn.Reserve(job.targetB,job,1,-1,null,errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(()=>!AbundanceSocial.CanShare(pawn,Recipient) || AbundanceRituals.Session(pawn)==null ||
                AbundanceRituals.Session(pawn)!=AbundanceRituals.Session(Recipient));
            yield return Toils_Goto.GotoCell(TargetIndex.B,PathEndMode.OnCell);
            var visit=ToilMaker.MakeToil("CelebrateAbundanceTogether");
            visit.defaultCompleteMode=ToilCompleteMode.Delay;visit.defaultDuration=240;
            visit.initAction=()=>pawn.rotationTracker.FaceCell(Recipient.Position);
            visit.AddFailCondition(()=>!pawn.Position.InHorDistOf(Recipient.Position,2.5f));
            yield return visit;
            var interact=ToilMaker.MakeToil("AbundanceAffection");
            interact.initAction=()=>
            {
                string name="RR_AbundanceHug";
                if(AbundanceSocial.BellyFriendly(pawn,Recipient))name=Rand.Bool ? "RR_AbundanceBellyRub" : "RR_AbundanceBellyGrab";
                var def=DefDatabase<InteractionDef>.GetNamed(name);
                if(pawn.interactions.TryInteractWith(Recipient,def))AbundanceRituals.Session(pawn).SocialInteractions++;
            };
            interact.defaultCompleteMode=ToilCompleteMode.Instant;yield return interact;
        }
    }
    public sealed class InteractionWorker_AbundanceAffection : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator,Pawn recipient)=>0f;
        public override void Interacted(Pawn initiator,Pawn recipient,List<RulePackDef> extraSentencePacks,
            out string letterText,out string letterLabel,out LetterDef letterDef,out LookTargets lookTargets)
        {
            letterText=letterLabel=null;letterDef=null;lookTargets=null;
            if(!AbundanceSocial.CanShare(initiator,recipient))return;
            var thought=DefDatabase<ThoughtDef>.GetNamed("RR_AbundanceCompany");
            initiator.needs.mood?.thoughts.memories.TryGainMemory(thought,recipient);
            recipient.needs.mood?.thoughts.memories.TryGainMemory(thought,initiator);
        }
    }
}

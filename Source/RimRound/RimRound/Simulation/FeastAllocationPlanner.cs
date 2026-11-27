using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RimRound.Simulation
{
    /// <summary>
    /// A private numeric stock snapshot. Clone must copy every mutable resource;
    /// TryTake consumes one whole physical serving only in that snapshot. It may
    /// reject the next FIFO batch for a diner even when an earlier batch allowed
    /// that diner. Answers must be deterministic for the same snapshot. Neither
    /// operation may mutate native gameplay or caller stock.
    /// </summary>
    public interface IFeastAllocationStock
    {
        IFeastAllocationStock Clone();
        bool TryTake(int dinerIndex, int offerId);
    }

    /// <summary>
    /// Optional read-only policy for the NEXT serving in this stock snapshot.
    /// Query before TryTake; static offer permission remains an upper bound.
    /// Clones must retain this capability and own their sequence/resource state.
    /// </summary>
    public interface IFeastAllocationPortionPolicy
    {
        bool AllowsPartial(int dinerIndex, int offerId);
    }

    /// <summary>
    /// Optional canonical identity for all stock state affecting future answers.
    /// Immutable inputs may be implicit within one Plan call. Null/empty means
    /// unavailable and never establishes equivalence. No gameplay mutation.
    /// </summary>
    public interface IFeastAllocationStateKey
    {
        string GetAllocationStateKey();
    }

    public sealed class FeastAllocationOffer
    {
        public int OfferId { get; }
        public double Nutrition { get; }
        // Already scaled for this eater; never apply their multiplier again.
        public double PhysicalFullness { get; }
        public bool AllowsPartial { get; }
        public int Preference { get; }
        public FeastAllocationOffer(int offerId,double nutrition,double physicalFullness,bool allowsPartial)
            : this(offerId,nutrition,physicalFullness,allowsPartial,0) { }
        public FeastAllocationOffer(int offerId, double nutrition, double physicalFullness, bool allowsPartial, int preference)
        { OfferId=offerId; Nutrition=nutrition; PhysicalFullness=physicalFullness; AllowsPartial=allowsPartial; Preference=preference; }
    }

    public sealed class FeastAllocationDiner
    {
        public double NutritionBudget { get; }
        public double PhysicalFullnessBudget { get; }
        public bool LimitFullness { get; }
        public bool RequireIntake { get; }
        public double CompletionTolerance { get; }
        public IReadOnlyList<FeastAllocationOffer> Offers { get; }
        public FeastAllocationDiner(double nutritionBudget, double physicalFullnessBudget,
            bool limitFullness, bool requireIntake, IEnumerable<FeastAllocationOffer> offers, double completionTolerance=0)
        {
            NutritionBudget=nutritionBudget; PhysicalFullnessBudget=physicalFullnessBudget;
            LimitFullness=limitFullness; RequireIntake=requireIntake;
            CompletionTolerance=completionTolerance;
            Offers=offers==null ? null : Array.AsReadOnly(offers.ToArray());
        }
    }

    public sealed class FeastAllocationOptions
    {
        public int MaxNodes { get; }
        public int MaxTotalPortions { get; }
        public int MaxSearchDepth { get; }
        public FeastAllocationOptions(int maxNodes=100000, int maxTotalPortions=512, int maxSearchDepth=512)
        { MaxNodes=maxNodes; MaxTotalPortions=maxTotalPortions; MaxSearchDepth=maxSearchDepth; }
    }
    public enum FeastAllocationFailure { None, InvalidInput, InvalidStock, Shortage, PlanningLimit, RequiredDinerCannotEat, NoActualEaters }
    public enum FeastAllocationCompletion { TargetReached, WholePortionLimit, AttendWithoutEating }

    public sealed class FeastAllocationPart
    {
        public int DinerIndex { get; }
        public int OfferId { get; }
        public double UsefulFraction { get; }
        public double UsefulNutrition { get; }
        public double PhysicalFullness { get; }
        public double WastedNutrition { get; }
        public double WastedPhysicalFullness { get; }
        internal FeastAllocationPart(int diner, FeastAllocationOffer offer, double fraction)
        {
            DinerIndex=diner; OfferId=offer.OfferId; UsefulFraction=fraction;
            UsefulNutrition=offer.Nutrition*fraction; PhysicalFullness=offer.PhysicalFullness*fraction;
            WastedNutrition=offer.Nutrition-UsefulNutrition; WastedPhysicalFullness=offer.PhysicalFullness-PhysicalFullness;
        }
    }
    public sealed class FeastAllocationDinerResult
    {
        public IReadOnlyList<FeastAllocationPart> Parts { get; }
        public double UsefulNutrition { get; }
        public double PhysicalFullness { get; }
        public FeastAllocationCompletion Completion { get; }
        public int? WitnessOfferId { get; }
        internal FeastAllocationDinerResult(IEnumerable<FeastAllocationPart> parts, FeastAllocationCompletion completion, int? witness)
        {
            Parts=Array.AsReadOnly(parts.ToArray()); Completion=completion; WitnessOfferId=witness;
            UsefulNutrition=Parts.Sum(p=>p.UsefulNutrition); PhysicalFullness=Parts.Sum(p=>p.PhysicalFullness);
        }
    }
    public sealed class FeastAllocationResult
    {
        readonly IFeastAllocationStock finalStock;
        public bool Success => Failure==FeastAllocationFailure.None;
        public FeastAllocationFailure Failure { get; }
        public int FailureDinerIndex { get; }
        // Includes stock probes, so a large offer matrix cannot evade the bound.
        public int VisitedNodes { get; }
        public IReadOnlyList<FeastAllocationDinerResult> Diners { get; }
        // Replay in this order: grouping by diner may change FIFO batch identity.
        public IReadOnlyList<FeastAllocationPart> Steps { get; }
        internal FeastAllocationResult(FeastAllocationFailure failure, int diner, int nodes,
            IEnumerable<FeastAllocationDinerResult> diners=null, IEnumerable<FeastAllocationPart> steps=null, IFeastAllocationStock stock=null)
        {
            Failure=failure; FailureDinerIndex=diner; VisitedNodes=nodes; finalStock=stock;
            Diners=Array.AsReadOnly((diners??Enumerable.Empty<FeastAllocationDinerResult>()).ToArray());
            Steps=Array.AsReadOnly((steps??Enumerable.Empty<FeastAllocationPart>()).ToArray());
        }
        public IFeastAllocationStock CloneFinalStock() => finalStock==null ? null : FeastAllocationPlanner.PrivateClone(finalStock);
    }

    /// <summary>
    /// Deterministic bounded search over real whole-item claims. No native types,
    /// food effects or mutations belong here. Caller supplies eligible dishes and
    /// must revalidate the result at collection and ingestion.
    /// </summary>
    public static class FeastAllocationPlanner
    {
        const int MaxDiners=128, MaxOffers=4096, SafeDepth=512, SafePortions=512;
        // Relative only: genuinely tiny positive budgets are not treated as zero.
        const double Roundoff=1e-10;
        sealed class InvalidStockException : Exception { }
        internal static IFeastAllocationStock PrivateClone(IFeastAllocationStock source)
        {
            var clone=source.Clone();
            if(clone==null || ReferenceEquals(source,clone) ||
                source is IFeastAllocationPortionPolicy && !(clone is IFeastAllocationPortionPolicy))throw new InvalidStockException();
            return clone;
        }
        public static FeastAllocationResult Plan(IReadOnlyList<FeastAllocationDiner> diners,
            IFeastAllocationStock stock, FeastAllocationOptions options=null)
        {
            options=options??new FeastAllocationOptions();
            if(!Valid(diners,stock,options))return new FeastAllocationResult(FeastAllocationFailure.InvalidInput,-1,0);
            var search=new Search(diners.ToArray(),options);
            try
            {
                State answer;
                if(search.Visit(new State(diners,PrivateClone(stock)),0,out answer))return search.Result(answer);
                return new FeastAllocationResult(search.Limited ? FeastAllocationFailure.PlanningLimit :
                    search.RequiredFailure>=0 ? FeastAllocationFailure.RequiredDinerCannotEat :
                    search.NoEaters ? FeastAllocationFailure.NoActualEaters : FeastAllocationFailure.Shortage,
                    search.RequiredFailure>=0 ? search.RequiredFailure : search.FailureDiner,search.Work);
            }
            catch(InvalidStockException)
            { return new FeastAllocationResult(FeastAllocationFailure.InvalidStock,-1,search.Work); }
            // Exceptions from adapter TryTake are deliberately not swallowed.
        }
        static bool Valid(IReadOnlyList<FeastAllocationDiner> diners, IFeastAllocationStock stock, FeastAllocationOptions o)
        {
            if(diners==null || diners.Count==0 || diners.Count>MaxDiners || stock==null || o.MaxNodes<=0 ||
                o.MaxTotalPortions<=0 || o.MaxTotalPortions>SafePortions || o.MaxSearchDepth<=0 || o.MaxSearchDepth>SafeDepth)return false;
            int offers=0;
            foreach(var d in diners)
            {
                if(d==null || !Finite(d.NutritionBudget) || d.NutritionBudget<0 || !Finite(d.PhysicalFullnessBudget) ||
                    d.PhysicalFullnessBudget<0 || !Finite(d.CompletionTolerance) || d.CompletionTolerance<0 || d.Offers==null)return false;
                var ids=new HashSet<int>();
                foreach(var offer in d.Offers)
                {
                    if(++offers>MaxOffers || offer==null || offer.OfferId<0 || !ids.Add(offer.OfferId) ||
                        !Finite(offer.Nutrition) || offer.Nutrition<=0 || !Finite(offer.PhysicalFullness) || offer.PhysicalFullness<0)return false;
                }
            }
            return true;
        }
        sealed class State
        {
            public IFeastAllocationStock Stock;
            public double[] N,V,UsedN,UsedV;
            public List<FeastAllocationPart> Steps;
            public int?[] Witness;
            public State(IReadOnlyList<FeastAllocationDiner> diners, IFeastAllocationStock stock)
            { Stock=stock;N=diners.Select(d=>d.NutritionBudget).ToArray();V=diners.Select(d=>d.PhysicalFullnessBudget).ToArray();UsedN=new double[diners.Count];UsedV=new double[diners.Count];Steps=new List<FeastAllocationPart>(); }
            public State With(FeastAllocationPart part,IFeastAllocationStock stock)
            {
                var next=new State { Stock=stock,N=(double[])N.Clone(),V=(double[])V.Clone(),UsedN=(double[])UsedN.Clone(),UsedV=(double[])UsedV.Clone(),Steps=new List<FeastAllocationPart>(Steps) };
                next.N[part.DinerIndex]=Subtract(next.N[part.DinerIndex],part.UsefulNutrition);
                next.V[part.DinerIndex]=Subtract(next.V[part.DinerIndex],part.PhysicalFullness);
                next.UsedN[part.DinerIndex]+=part.UsefulNutrition;
                next.UsedV[part.DinerIndex]+=part.PhysicalFullness;
                next.Steps.Add(part);return next;
            }
            State() { }
        }
        sealed class Candidate
        {
            public int Diner;
            public FeastAllocationOffer Offer;
            public double Fraction;
            public int Choices;
        }
        sealed class Search
        {
            const int MaximumMemoCharacters=2*1024*1024;
            readonly FeastAllocationDiner[] diners;
            readonly FeastAllocationOffer[][] offers;
            readonly FeastAllocationOptions options;
            public int Work, RequiredFailure=-1, FailureDiner=-1;
            public bool Limited,NoEaters;
            bool exhausted;
            long limitEvents;
            int memoCharacters;
            readonly HashSet<string> failedStates=new HashSet<string>(StringComparer.Ordinal);
            public Search(FeastAllocationDiner[] diners,FeastAllocationOptions options)
            { this.diners=diners;this.options=options;offers=diners.Select(d=>d.Offers.OrderBy(o=>o.OfferId).ToArray()).ToArray(); }
            bool Spend()
            { if(Work>=options.MaxNodes){Limit();exhausted=true;return false;}Work++;return true; }
            void Limit(){Limited=true;limitEvents++;}
            string MemoKey(State s,int depth)
            {
                var keyed=s.Stock as IFeastAllocationStateKey;
                if(keyed==null)return null;
                string stock=keyed.GetAllocationStateKey();
                if(string.IsNullOrEmpty(stock) || stock.Length>MaximumMemoCharacters)return null;
                var key=new StringBuilder();key.Append(stock.Length).Append(':').Append(stock).Append('|').Append(depth).Append('|').Append(s.Steps.Count);
                for(int i=0;i<diners.Length;i++)
                {
                    key.Append('|').Append(Bits(s.N[i])).Append(Bits(s.V[i])).Append(Bits(s.UsedN[i])).Append(Bits(s.UsedV[i]));
                    key.Append(s.UsedN[i]>0?'1':'0');
                }
                return key.Length>MaximumMemoCharacters ? null : key.ToString();
            }
            static string Bits(double value)=>unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString("X16",CultureInfo.InvariantCulture);
            bool Probe(IFeastAllocationStock stock,int diner,int offer,out IFeastAllocationStock taken)
            {
                taken=null;if(!Spend())return false;
                var clone=PrivateClone(stock);
                if(!clone.TryTake(diner,offer))return false;
                taken=clone;return true;
            }
            bool Done(State s,int i) => s.N[i]<=diners[i].CompletionTolerance ||
                diners[i].LimitFullness && s.V[i]<=diners[i].CompletionTolerance;
            bool PartialAllowed(State s,int diner,FeastAllocationOffer offer,out bool allowed)
            {
                allowed=offer.AllowsPartial;
                var policy=s.Stock as IFeastAllocationPortionPolicy;
                if(!allowed || policy==null)return true;
                if(!Spend())return false;
                allowed=policy.AllowsPartial(diner,offer.OfferId);return true;
            }
            public bool Visit(State s,int depth,out State answer)
            {
                answer=null;
                if(depth>options.MaxSearchDepth){Limit();return false;}
                if(!Spend())return false;
                string key=MemoKey(s,depth);
                if(key!=null && failedStates.Contains(key))return false;
                long before=limitEvents;
                bool found=Explore(s,depth,out answer);
                // A cutoff is not an exhausted state. Keep only proven failures
                // and bound retained UTF-16 characters independently of nodes.
                if(!found && key!=null && !exhausted && limitEvents==before && key.Length<=MaximumMemoCharacters-memoCharacters && failedStates.Add(key))
                    memoCharacters+=key.Length;
                return found;
            }
            bool Explore(State s,int depth,out State answer)
            {
                answer=null;
                var candidates=new List<Candidate>();
                for(int i=0;i<diners.Length;i++)
                {
                    if(Done(s,i))continue;
                    int start=candidates.Count;
                    foreach(var offer in offers[i])
                    {
                        double fraction;
                        bool fits=Fits(s,i,offer,offer.AllowsPartial,out fraction);
                        // Static numeric candidates are cheap. Probe real stock
                        // only when trying a branch, not every matrix cell at
                        // every recursion level. Retain partial underflows so
                        // available unrepresentable food remains PlanningLimit.
                        if(fits || offer.AllowsPartial)candidates.Add(new Candidate{Diner=i,Offer=offer,Fraction=fraction});
                    }
                    int count=candidates.Count-start;
                    for(int c=start;c<candidates.Count;c++)candidates[c].Choices=count;
                }
                bool executable=false,numericBlocked=false;
                // Try constrained diners first, but backtrack diner order as well
                // as dish choice. Another eater can unlock a later FIFO batch.
                foreach(var candidate in candidates.OrderBy(c=>c.Choices).ThenBy(c=>c.Diner)
                    .ThenByDescending(c=>c.Offer.Preference).ThenBy(c=>c.Fraction<1d?1:0).ThenBy(c=>c.Offer.OfferId))
                {
                    int i=candidate.Diner;var offer=candidate.Offer;
                    bool allowPartial;double fraction=candidate.Fraction;
                    if(fraction>=1d)allowPartial=false; // A whole fit needs no fractional permission query.
                    else if(!PartialAllowed(s,i,offer,out allowPartial))return false;
                    bool fits=Fits(s,i,offer,allowPartial,out fraction);
                    if(!fits && !allowPartial)continue;
                    IFeastAllocationStock taken;
                    if(!Probe(s.Stock,i,offer.OfferId,out taken))
                    { if(exhausted)return false;continue; }
                    if(!fits){Limit();numericBlocked=true;continue;}
                    executable=true;
                    if(s.Steps.Count>=options.MaxTotalPortions){Limit();continue;}
                    var part=new FeastAllocationPart(i,offer,fraction);
                    var next=s.With(part,taken);
                    if(!Finite(next.UsedN[i]) || !Finite(next.UsedV[i]) ||
                        next.N[i]==s.N[i] && (!diners[i].LimitFullness || next.V[i]==s.V[i]))
                    { Limit();continue; } // No representable progress; never spin on tiny items.
                    if(Visit(next,depth+1,out answer))return true;
                    if(exhausted)return false;
                }
                // A failed deeper allocation does not authorize stopping early
                // while any executable fitting serving exists in THIS ledger.
                if(!executable && !numericBlocked && ValidateFinal(s)){answer=s;return true;}
                return false;
            }
            bool Fits(State s,int i,FeastAllocationOffer offer,bool allowPartial,out double fraction)
            {
                fraction=0;
                if(!allowPartial)
                {
                    if(!Within(offer.Nutrition,s.N[i]) || diners[i].LimitFullness && !Within(offer.PhysicalFullness,s.V[i]))return false;
                    fraction=1;return true;
                }
                fraction=Math.Min(1d,s.N[i]/offer.Nutrition);
                if(diners[i].LimitFullness && offer.PhysicalFullness>0)fraction=Math.Min(fraction,s.V[i]/offer.PhysicalFullness);
                return fraction>0 && Finite(fraction) && offer.Nutrition*fraction>0;
            }
            bool ValidateFinal(State s)
            {
                // This only runs after no safe fitting offer can be taken from
                // the final joint stock. Witness probes never publish their debit.
                s.Witness=new int?[diners.Length];
                for(int i=0;i<diners.Length;i++)
                {
                    if(Done(s,i))continue;
                    foreach(var offer in offers[i])
                    {
                        bool partial;
                        if(!PartialAllowed(s,i,offer,out partial))return false;
                        if(partial)continue;
                        double unusedFraction;
                        if(Fits(s,i,offer,false,out unusedFraction))continue;
                        IFeastAllocationStock ignored;
                        if(Probe(s.Stock,i,offer.OfferId,out ignored)){s.Witness[i]=offer.OfferId;break;}
                        if(exhausted)return false;
                    }
                    if(!s.Witness[i].HasValue){FailureDiner=i;return false;}
                }
                for(int i=0;i<diners.Length;i++)
                    if(diners[i].RequireIntake && !s.Steps.Any(p=>p.DinerIndex==i))
                    { RequiredFailure=i;return false; }
                if(s.Steps.Count==0){NoEaters=true;return false;}
                return true;
            }
            public FeastAllocationResult Result(State s)
            {
                var results=Enumerable.Range(0,diners.Length).Select(i=>
                {
                    var parts=s.Steps.Where(p=>p.DinerIndex==i).ToArray();
                    return new FeastAllocationDinerResult(parts,parts.Length==0 ? FeastAllocationCompletion.AttendWithoutEating :
                        Done(s,i) ? FeastAllocationCompletion.TargetReached : FeastAllocationCompletion.WholePortionLimit,s.Witness[i]);
                }).ToArray();
                return new FeastAllocationResult(FeastAllocationFailure.None,-1,Work,results,s.Steps,s.Stock);
            }
        }
        static double Subtract(double budget,double used)
        {
            double left=budget-used;
            return left<=0 || left<=Math.Max(Math.Abs(budget),Math.Abs(used))*Roundoff ? 0 : left;
        }
        static bool Within(double needed,double available) => needed<=available ||
            needed-available<=Math.Max(Math.Abs(needed),Math.Abs(available))*Roundoff;
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

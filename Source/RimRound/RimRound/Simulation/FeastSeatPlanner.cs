using System;
using System.Collections.Generic;

namespace RimRound.Simulation
{
    /// <summary>
    /// Complete deterministic bipartite seat assignment. Rows are diners, values
    /// are eligible seat IDs in caller preference order. Native reservations,
    /// reachability and distance are evaluated by the caller, never here.
    /// </summary>
    public static class FeastSeatPlanner
    {
        public const int MaximumDiners=128;
        public const int MaximumSeats=512;
        public const int MaximumEdges=8192;

        // False never exposes a partial assignment. Duplicate IDs within a row
        // are invalid; shared seat IDs across different diners are expected.
        public static bool TryAssign(IReadOnlyList<IReadOnlyList<int>> orderedCandidates,
            int seatCount,out int[] assigned)
        {
            assigned=null;
            if(orderedCandidates==null || seatCount<0 || seatCount>MaximumSeats ||
                orderedCandidates.Count>MaximumDiners)return false;
            int diners=orderedCandidates.Count,edges=0;
            var rows=new int[diners][];
            for(int diner=0;diner<diners;diner++)
            {
                var row=orderedCandidates[diner];
                if(row==null || row.Count>MaximumEdges-edges)return false;
                edges+=row.Count;rows[diner]=new int[row.Count];
                var seen=new bool[seatCount];
                for(int i=0;i<row.Count;i++)
                {
                    int seat=row[i];
                    if(seat<0 || seat>=seatCount || seen[seat])return false;
                    seen[seat]=true;rows[diner][i]=seat;
                }
            }
            if(diners>seatCount)return false;
            var owner=new int[seatCount];var result=new int[diners];
            for(int i=0;i<seatCount;i++)owner[i]=-1;
            for(int i=0;i<diners;i++)result[i]=-1;
            for(int diner=0;diner<diners;diner++)
                if(!Place(diner,rows,owner,result,new bool[seatCount],0))return false;
            assigned=result;return true;
        }

        static bool Place(int diner,int[][] rows,int[] owner,int[] result,bool[] visited,int depth)
        {
            // An augmenting path cannot contain more diners than the graph.
            // The explicit cardinality/depth limits keep Mono stack use bounded.
            if(depth>=MaximumDiners)return false;
            foreach(int seat in rows[diner])
                if(!visited[seat] && owner[seat]<0)
                {
                    visited[seat]=true;owner[seat]=diner;result[diner]=seat;return true;
                }
            // Retain existing nearest choices when a free seat is available to
            // this diner. Otherwise reroute occupants to find a complete match.
            foreach(int seat in rows[diner])
            {
                if(visited[seat])continue;
                visited[seat]=true;
                int previous=owner[seat];
                if(previous>=0 && Place(previous,rows,owner,result,visited,depth+1))
                { owner[seat]=diner;result[diner]=seat;return true; }
            }
            return false;
        }
    }
}

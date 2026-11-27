using System;
using System.Collections.Generic;

namespace RimRound.Simulation
{
    /// <summary>Conserved allocation: each physical pool has one capacity, however many diets accept it.</summary>
    public static class FoodReserveAllocation
    {
        public const double ProtectedDays = 3d;
        public const double ResumeDays = 3.5d;
        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static double WholePortions(double nutrition, double portion) => Finite(nutrition) && Finite(portion) && nutrition >= 0d && portion > 0d
            ? Math.Min(nutrition, Math.Floor(nutrition / portion) * portion) : 0d;

        public static bool TryDebit(double[] amounts, bool[] compatible, bool[] matches, double cost, bool knownSource)
        {
            if (amounts == null || compatible == null || matches == null || compatible.Length != amounts.Length || matches.Length != amounts.Length ||
                !Finite(cost) || cost < 0d) return false;
            for (int i = 0; i < amounts.Length; i++) if (!Finite(amounts[i]) || amounts[i] < 0d) return false;
            if (knownSource)
            {
                double available = 0d;
                for (int i = 0; i < amounts.Length; i++) if (matches[i] && compatible[i]) available += amounts[i];
                if (available < cost && cost - available > Math.Max(1d, cost) * 1e-12d) return false;
                double remaining = cost;
                for (int i = 0; i < amounts.Length && remaining > 0d; i++) if (matches[i] && compatible[i])
                { double used = Math.Min(amounts[i], remaining); amounts[i] -= used; remaining -= used; }
                return true;
            }
            bool any = false;
            // Unknown origin: protect every compatible scarce pool against this
            // debit; choosing the most abundant pool could mask the actual loss.
            for (int i = 0; i < amounts.Length; i++) if (compatible[i] && amounts[i] > 0d)
            { amounts[i] = Math.Max(0d, amounts[i] - cost); any = true; }
            return any;
        }

        public static bool Allows(double days, double projectedDays, ref bool paused)
        {
            if (!Finite(days) || !Finite(projectedDays) || days < ProtectedDays)
            { paused = true; return false; }
            if (paused && days < ResumeDays) return false;
            if (paused) paused = false;
            return projectedDays >= ProtectedDays;
        }

        public static double Days(double[] supply, double[] demand, bool[][] accepts, double[] dailySupply = null, double[] productionLimits = null, double[] expiryDays = null)
        {
            if (supply == null || demand == null || accepts == null || accepts.Length != supply.Length)
                return double.NaN;
            dailySupply = dailySupply ?? new double[supply.Length];
            productionLimits = productionLimits ?? Unlimited(supply.Length);
            expiryDays = expiryDays ?? Unlimited(supply.Length);
            if (dailySupply.Length != supply.Length || productionLimits.Length != supply.Length || expiryDays.Length != supply.Length) return double.NaN;
            double total = 0d, needed = 0d, produced = 0d;
            for (int i = 0; i < supply.Length; i++)
            {
                if (!Finite(supply[i]) || supply[i] < 0d || accepts[i] == null || accepts[i].Length != demand.Length ||
                    !Finite(dailySupply[i]) || dailySupply[i] < 0d || !Finite(productionLimits[i]) || productionLimits[i] < 0d || !Finite(expiryDays[i]) || expiryDays[i] < 0d)
                    return double.NaN;
                total += supply[i];
                produced += dailySupply[i];
            }
            foreach (double value in demand)
            { if (!Finite(value) || value < 0d) return double.NaN; needed += value; }
            if (!Finite(total) || !Finite(needed) || !Finite(produced)) return double.NaN;
            if (needed <= 0d) return 0d; // no trustworthy protected population
            // Every renewable milk unit must have its production cost in the
            // mother's demand. A self-sustaining/unbounded graph is not a
            // trustworthy reserve estimate and must never clear the guard.
            if (needed <= produced) return double.NaN;
            Compress(ref supply, ref accepts, ref dailySupply, ref productionLimits, ref expiryDays, demand.Length);
            double low = 0d, high = total / (needed - produced);
            if (!Finite(high)) return double.NaN;
            // This solver is used for estimates, never stock mutations. Forty
            // iterations retain precision at a three-day transaction boundary.
            for (int i = 0; i < 40; i++)
            {
                double middle = (low + high) / 2d;
                if (CoversGraph(Project(supply, dailySupply, productionLimits, expiryDays, middle), demand, accepts, middle)) low = middle; else high = middle;
            }
            return high;
        }

        public static bool Covers(double[] supply, double[] demand, bool[][] accepts, double days, double[] dailySupply = null, double[] productionLimits = null, double[] expiryDays = null)
        {
            if (!Finite(days) || days < 0d) return false;
            if (supply == null || demand == null || accepts == null || accepts.Length != supply.Length) return false;
            dailySupply = dailySupply ?? new double[supply.Length];
            productionLimits = productionLimits ?? Unlimited(supply.Length);
            expiryDays = expiryDays ?? Unlimited(supply.Length);
            if (dailySupply.Length != supply.Length || productionLimits.Length != supply.Length || expiryDays.Length != supply.Length) return false;
            for (int i = 0; i < supply.Length; i++) if (!Finite(supply[i]) || supply[i] < 0d || accepts[i] == null || accepts[i].Length != demand.Length ||
                !Finite(dailySupply[i]) || dailySupply[i] < 0d || !Finite(productionLimits[i]) || productionLimits[i] < 0d || !Finite(expiryDays[i]) || expiryDays[i] < 0d ||
                !Finite(supply[i] + Math.Min(dailySupply[i] * days, productionLimits[i]))) return false;
            foreach (double value in demand) if (!Finite(value) || value < 0d) return false;
            Compress(ref supply, ref accepts, ref dailySupply, ref productionLimits, ref expiryDays, demand.Length);
            return CoversGraph(Project(supply, dailySupply, productionLimits, expiryDays, days), demand, accepts, days);
        }
        private static double[] Unlimited(int count)
        { var limits = new double[count]; for (int i = 0; i < count; i++) limits[i] = double.MaxValue; return limits; }
        private static double[] Project(double[] supply, double[] dailySupply, double[] limits, double[] expiryDays, double days)
        {
            // Nursing milk disappears with its hediff; surplus produced earlier
            // cannot be banked as imaginary bottles beyond that lifetime. For a
            // longer horizon omit that route entirely (conservative with fallback).
            var projected = new double[supply.Length];
            for (int i = 0; i < supply.Length; i++) projected[i] = days > expiryDays[i] ? 0d : supply[i] + Math.Min(dailySupply[i] * days, limits[i]);
            return projected;
        }
        private static void Compress(ref double[] supply, ref bool[][] accepts, ref double[] dailySupply, ref double[] limits, ref double[] expiryDays, int diets)
        {
            var lookup = new Dictionary<string, int>();
            var amounts = new List<double>(); var rates = new List<double>(); var caps = new List<double>(); var expires = new List<double>(); var masks = new List<bool[]>();
            for (int i = 0; i < supply.Length; i++)
            {
                if (supply[i] <= 0d && dailySupply[i] <= 0d) continue;
                var signature = new char[diets];
                for (int j = 0; j < diets; j++) signature[j] = accepts[i][j] ? '1' : '0';
                // Different mothers have independent expiry/capacity. Merging
                // them would allow a short-lived producer to borrow another's lifetime.
                string key = new string(signature) + (dailySupply[i] > 0d || expiryDays[i] < double.MaxValue ? ":producer:" + i : ""); int pool;
                if (!lookup.TryGetValue(key, out pool)) { pool = amounts.Count; lookup.Add(key, pool); amounts.Add(0d); rates.Add(0d); caps.Add(limits[i]); expires.Add(expiryDays[i]); masks.Add(accepts[i]); }
                amounts[pool] += supply[i];
                rates[pool] += dailySupply[i];
            }
            supply = amounts.ToArray(); accepts = masks.ToArray(); dailySupply = rates.ToArray(); limits = caps.ToArray(); expiryDays = expires.ToArray();
        }
        private static bool CoversGraph(double[] supply, double[] demand, bool[][] accepts, double days)
        {
            int end = supply.Length + demand.Length + 1;
            var graph = new List<Edge>[end + 1];
            for (int i = 0; i < graph.Length; i++) graph[i] = new List<Edge>();
            double needed = 0d;
            for (int i = 0; i < supply.Length; i++)
            {
                if (!Finite(supply[i]) || supply[i] < 0d) return false;
                Add(graph, 0, i + 1, supply[i]);
                for (int j = 0; j < demand.Length; j++)
                    if (accepts[i][j]) Add(graph, i + 1, supply.Length + j + 1, supply[i]);
            }
            for (int j = 0; j < demand.Length; j++)
            {
                double amount = demand[j] * days;
                if (!Finite(amount)) return false;
                needed += amount;
                if (!Finite(needed)) return false;
                Add(graph, supply.Length + j + 1, end, amount);
            }
            double flow = 0d;
            var level = new int[graph.Length];
            while (Levels(graph, level, end))
            {
                var next = new int[graph.Length];
                double pushed;
                while ((pushed = Push(graph, level, next, 0, end, double.MaxValue)) > 0d) flow += pushed;
            }
            // Only machine-rounding tolerance; this is not a nutrition allowance.
            return needed - flow <= Math.Max(1d, needed) * 1e-12d;
        }

        private sealed class Edge { public int To, Reverse; public double Capacity; }
        private static void Add(List<Edge>[] graph, int from, int to, double capacity)
        {
            graph[from].Add(new Edge { To = to, Reverse = graph[to].Count, Capacity = capacity });
            graph[to].Add(new Edge { To = from, Reverse = graph[from].Count - 1 });
        }
        private static bool Levels(List<Edge>[] graph, int[] level, int end)
        {
            for (int i = 0; i < level.Length; i++) level[i] = -1;
            var queue = new Queue<int>(); queue.Enqueue(0); level[0] = 0;
            while (queue.Count > 0)
            {
                int from = queue.Dequeue();
                foreach (Edge edge in graph[from]) if (edge.Capacity > 0d && level[edge.To] < 0)
                { level[edge.To] = level[from] + 1; queue.Enqueue(edge.To); }
            }
            return level[end] >= 0;
        }
        private static double Push(List<Edge>[] graph, int[] level, int[] next, int from, int end, double amount)
        {
            if (from == end) return amount;
            for (; next[from] < graph[from].Count; next[from]++)
            {
                Edge edge = graph[from][next[from]];
                if (edge.Capacity <= 0d || level[edge.To] != level[from] + 1) continue;
                double sent = Push(graph, level, next, edge.To, end, Math.Min(amount, edge.Capacity));
                if (sent <= 0d) continue;
                edge.Capacity -= sent; graph[edge.To][edge.Reverse].Capacity += sent; return sent;
            }
            return 0d;
        }
    }
}

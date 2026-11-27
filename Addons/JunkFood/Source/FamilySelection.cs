using System;
using System.Collections.Generic;
using System.Linq;

namespace Oinky.JunkFood
{
    public static class FamilySelection
    {
        // Each family has one vote regardless of the number of branded variants.
        public static T Pick<T>(IEnumerable<T> candidates, Func<T, string> family, Func<int, int> next)
            where T : class
        {
            var groups = candidates.GroupBy(family).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
            if (groups.Count == 0) return null;
            List<T> members = groups[next(groups.Count)].ToList();
            return members[next(members.Count)];
        }

        public static List<T> DistinctFamilies<T>(IEnumerable<T> candidates, int count,
            Func<T, string> family, Func<int, int> next) where T : class
        {
            List<T> pool = candidates.ToList();
            var result = new List<T>();
            while (result.Count < count && pool.Count > 0)
            {
                T choice = Pick(pool, family, next);
                result.Add(choice);
                string chosenFamily = family(choice);
                pool.RemoveAll(item => family(item) == chosenFamily);
            }
            return result;
        }
    }
}

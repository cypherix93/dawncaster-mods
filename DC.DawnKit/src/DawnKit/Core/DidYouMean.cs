using System;
using System.Collections.Generic;

namespace DawnKit.Core.Lifecycle
{
    /// <summary>
    /// Nearest-match suggestions for validation errors (SPEC.md §4.2, P14): a
    /// candidate matches when its Levenshtein distance to the input is ≤ 2 or one
    /// is a (case-insensitive) prefix of the other. Mirrors the Python rule in
    /// tools/validate_pack.py so the offline validator and the engine give the
    /// same hints. Used for codeLine commands (660-command talent union) and for
    /// enum-member typos on the data path.
    /// </summary>
    internal static class DidYouMean
    {
        private const int MaxDistance = 2;
        private const int MaxSuggestions = 3;
        private const int MinPrefixLength = 3;

        /// <summary>" — did you mean 'x', 'y'?" for the closest candidates, or "" when nothing is close.</summary>
        internal static string Suggest(string input, IEnumerable<string> candidates)
        {
            if (string.IsNullOrEmpty(input) || candidates == null)
            {
                return "";
            }
            var scored = new List<KeyValuePair<int, string>>();
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }
                int score = Score(input, candidate);
                if (score >= 0)
                {
                    scored.Add(new KeyValuePair<int, string>(score, candidate));
                }
            }
            if (scored.Count == 0)
            {
                return "";
            }
            scored.Sort((a, b) => a.Key != b.Key
                ? a.Key.CompareTo(b.Key)
                : string.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase));
            int take = Math.Min(MaxSuggestions, scored.Count);
            var names = new string[take];
            for (int i = 0; i < take; i++)
            {
                names[i] = "'" + scored[i].Value + "'";
            }
            return " — did you mean " + string.Join(", ", names) + "?";
        }

        /// <summary>Match score (lower = closer), or -1 for no match.</summary>
        private static int Score(string input, string candidate)
        {
            if (string.Equals(input, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return 0; // case-only mismatch — the closest possible hint
            }
            string a = input.ToLowerInvariant();
            string b = candidate.ToLowerInvariant();
            if (a.Length >= MinPrefixLength && b.StartsWith(a, StringComparison.Ordinal))
            {
                return 1;
            }
            if (b.Length >= MinPrefixLength && a.StartsWith(b, StringComparison.Ordinal))
            {
                return 1;
            }
            int distance = BoundedLevenshtein(a, b, MaxDistance);
            return distance >= 0 ? 1 + distance : -1;
        }

        /// <summary>Levenshtein distance if ≤ max, else -1 (with a cheap length cutoff).</summary>
        internal static int BoundedLevenshtein(string a, string b, int max)
        {
            if (Math.Abs(a.Length - b.Length) > max)
            {
                return -1;
            }
            int[] prev = new int[b.Length + 1];
            int[] curr = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++)
            {
                prev[j] = j;
            }
            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                int rowMin = curr[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                    if (curr[j] < rowMin)
                    {
                        rowMin = curr[j];
                    }
                }
                if (rowMin > max)
                {
                    return -1; // every path already exceeds the bound
                }
                int[] tmp = prev;
                prev = curr;
                curr = tmp;
            }
            return prev[b.Length] <= max ? prev[b.Length] : -1;
        }
    }
}

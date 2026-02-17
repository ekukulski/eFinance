using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace eFinance.Services
{
    public enum MatchType { Exact, StartsWith, Contains }

    public sealed record CategoryRule(
        int Id,
        string Pattern,
        int CategoryId,
        MatchType MatchType,
        int Priority,
        bool IsEnabled
    );

    public sealed record MatchResult(int CategoryId, int RuleId, string Pattern);

    public static class RuleMatcher
    {
        public static MatchResult? Match(string description, IReadOnlyList<CategoryRule> rules)
        {
            var desc = Normalize(description);

            foreach (var rule in OrderRules(rules))
            {
                var pat = Normalize(rule.Pattern);

                if (rule.MatchType == MatchType.Exact && desc == pat)
                    return new MatchResult(rule.CategoryId, rule.Id, rule.Pattern);

                if (rule.MatchType == MatchType.StartsWith && desc.StartsWith(pat, StringComparison.Ordinal))
                    return new MatchResult(rule.CategoryId, rule.Id, rule.Pattern);

                if (rule.MatchType == MatchType.Contains && desc.Contains(pat, StringComparison.Ordinal))
                    return new MatchResult(rule.CategoryId, rule.Id, rule.Pattern);
            }

            return null;
        }

        private static IEnumerable<CategoryRule> OrderRules(IEnumerable<CategoryRule> rules)
            => rules
               .Where(r => r.IsEnabled)
               .OrderByDescending(r => r.Priority)
               // break ties: Exact > StartsWith > Contains
               .ThenBy(r => r.MatchType switch
               {
                   MatchType.Exact => 0,
                   MatchType.StartsWith => 1,
                   _ => 2
               })
               // longer patterns first helps avoid “AMZN” beating “AMZN MKTP US*…”
               .ThenByDescending(r => r.Pattern.Length);

        private static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim().ToUpperInvariant();
            s = Regex.Replace(s, @"\s+", " ");
            return s;
        }
    }
}

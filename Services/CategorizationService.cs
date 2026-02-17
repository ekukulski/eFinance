using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using eFinance.Data;

namespace eFinance.Services
{
    public sealed class CategorizationService
    {
        private readonly SqliteDatabase _database;

        // Cache rules in memory (massive speed improvement during import)
        private List<CategoryRule>? _cachedRules;
        private DateTime _lastLoadUtc = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        public CategorizationService(SqliteDatabase database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        /// <summary>
        /// Returns the best category match for a transaction description.
        /// Includes rule id + pattern for auditing.
        /// </summary>
        public async Task<MatchResult?> CategorizeAsync(
            string? description,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(description))
                return null;

            var rules = await GetRulesAsync(ct);

            return RuleMatcher.Match(description, rules);
        }

        // ------------------------------------------------------------
        // Load + Cache rules
        // ------------------------------------------------------------
        private async Task<IReadOnlyList<CategoryRule>> GetRulesAsync(CancellationToken ct)
        {
            // If cache still valid, use it
            if (_cachedRules is not null &&
                DateTime.UtcNow - _lastLoadUtc < CacheDuration)
            {
                return _cachedRules;
            }

            var rulesFromDb = await _database.GetCategoryRulesAsync(enabledOnly: true);

            var rules = rulesFromDb.Select(r => new CategoryRule(
                r.Id,
                r.DescriptionPattern,
                r.CategoryId,
                ParseMatchType(r.MatchType),
                r.Priority,
                r.IsEnabled == 1
            )).ToList();

            _cachedRules = rules;
            _lastLoadUtc = DateTime.UtcNow;

            return rules;
        }

        private static MatchType ParseMatchType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return MatchType.Contains;

            if (Enum.TryParse<MatchType>(value.Trim(), ignoreCase: true, out var mt))
                return mt;

            return MatchType.Contains;
        }

        /// <summary>
        /// Force reload rules immediately (call after editing rules in UI).
        /// </summary>
        public void InvalidateCache()
        {
            _cachedRules = null;
            _lastLoadUtc = DateTime.MinValue;
        }
    }
}

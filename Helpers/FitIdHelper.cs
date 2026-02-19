using System.Text.RegularExpressions;

namespace eFinance.Importing
{
    public static class FitIdHelper
    {
        // Conservative: remove long digit runs (>=5), punctuation, normalize whitespace, uppercase.
        // Also removes a trailing 2-letter US state token ONLY if it’s at the very end (e.g. " IL").
        public static string NormalizeDescription(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var s = raw.Trim();

            // Normalize unicode apostrophes, etc.
            s = s.Replace('’', '\'').Replace('“', '"').Replace('”', '"');

            // Remove long numeric sequences (order IDs, phone numbers, reference numbers)
            // Keep small numbers (1-4 digits) since they’re often meaningful store identifiers.
            s = Regex.Replace(s, @"\b\d{5,}\b", "", RegexOptions.CultureInvariant);

            // Remove punctuation/symbols (keep letters/digits/spaces)
            s = Regex.Replace(s, @"[^\p{L}\p{Nd}\s]", " ", RegexOptions.CultureInvariant);

            // Collapse whitespace
            s = Regex.Replace(s, @"\s+", " ", RegexOptions.CultureInvariant).Trim();

            // Uppercase for stable matching
            s = s.ToUpperInvariant();

            // Remove trailing US state code token at end (e.g., " IL", " CA")
            // Only if it's exactly 2 letters as the final token.
            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var last = parts[^1];
                if (last.Length == 2 && last.All(char.IsLetter))
                {
                    s = string.Join(' ', parts.Take(parts.Length - 1));
                }
            }

            return s;
        }

        // New stable FitId (V2)
        public static string BuildFitIdV2(
            string accountKey,
            DateTime postedDate,
            decimal amount,
            string? rawDescription)
        {
            // Amount normalized to cents to avoid locale/formatting drift.
            var cents = decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

            var normDesc = NormalizeDescription(rawDescription);

            // Include accountKey so identical txn in different accounts doesn't collide.
            // yyyyMMdd so stable across cultures.
            return $"V2|{accountKey}|{postedDate:yyyyMMdd}|{cents}|{normDesc}";
        }

        // Legacy FitId builder hook (keep your existing logic in one place)
        // Drop your current legacy FitId creation here so importer can check BOTH.
        public static string BuildFitIdLegacy(
            string accountKey,
            DateTime postedDate,
            decimal amount,
            string? rawDescription)
        {
            // ---- IMPORTANT ----
            // Replace the body of this method with your CURRENT/OLD FitId algorithm
            // (the one already used in your DB rows).
            //
            // Example placeholder (DO NOT keep if it differs from your current logic):
            var s = (rawDescription ?? string.Empty).Trim().ToUpperInvariant();
            var cents = decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
            return $"{accountKey}|{postedDate:yyyy-MM-dd}|{cents}|{s}";
        }
    }
}

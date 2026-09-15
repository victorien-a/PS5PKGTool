using System.Globalization;
using System.Text;
using PS5PKGTool.Core.Models;

namespace PS5PKGTool.Core.Services;

/// <summary>
/// Pure query grammar for matching a <see cref="Ps5GameInfo"/> against a library search string.
/// This is the portable counterpart of the WinForms search box's matching logic; it contains no
/// UI concerns.
/// </summary>
public static class Ps5LibraryQuery
{
    /// <summary>
    /// Matches a game against the search box. Supports space-separated AND tokens, quoted phrases,
    /// field prefixes (<c>title:</c>, <c>id:</c>, <c>content:</c>, <c>category:</c>, <c>region:</c>,
    /// <c>size:</c>, <c>version:</c>, <c>fw:</c>, <c>feature:</c>, <c>drm:</c>, <c>path:</c>),
    /// OR groups with <c>|</c>, numeric comparisons (<c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c>,
    /// <c>&lt;=</c>, <c>=</c>) and <c>-</c> negation.
    /// </summary>
    public static bool Matches(Ps5GameInfo game, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        foreach (string token in TokenizeQuery(query))
        {
            bool negate = token.Length > 1 && token[0] == '-';
            string body = negate ? token[1..] : token;
            if (body.Length == 0) continue;
            int colon = body.IndexOf(':');
            bool match = colon > 0
                ? MatchField(game, body[..colon].ToLowerInvariant(), body[(colon + 1)..])
                : MatchFreeText(game, body);
            if (negate) match = !match;
            if (!match) return false;
        }
        return true;
    }

    /// <summary>Filters a game sequence, keeping only the games that <see cref="Matches"/> the query.</summary>
    public static IEnumerable<Ps5GameInfo> Filter(IEnumerable<Ps5GameInfo> games, string query) =>
        games.Where(game => Matches(game, query));

    private static IEnumerable<string> TokenizeQuery(string query)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        foreach (char c in query)
        {
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (!quoted && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    private static bool MatchFreeText(Ps5GameInfo game, string value) =>
        MatchAny(value, part =>
            ContainsText(game.Title, part) ||
            ContainsText(game.TitleId, part) ||
            ContainsText(game.ContentId, part) ||
            ContainsText(game.RootPath, part) ||
            ContainsText(game.SourceDescription, part));

    private static bool MatchField(Ps5GameInfo game, string field, string value) => field switch
    {
        "title" => MatchAny(value, part => ContainsText(game.Title, part)),
        "id" or "titleid" or "title-id" => MatchAny(value, part => ContainsText(game.TitleId, part)),
        "content" or "contentid" or "content-id" => MatchAny(value, part => ContainsText(game.ContentId, part)),
        "category" => MatchAny(value, part => ContainsText(CategoryOf(game), part)),
        "region" => MatchAny(value, part => ContainsText(RegionOf(game), part)),
        "source" or "format" => MatchAny(value, part =>
            ContainsText(game.SourceDescription, part) || ContainsText(SourceFilterValue(game), part)),
        "drm" => MatchAny(value, part => ContainsText(game.DrmType, part)),
        "path" or "location" => MatchAny(value, part => ContainsText(game.RootPath, part)),
        "feature" or "features" => MatchAny(value, part => game.DeclaredFeatures.Any(feature => ContainsText(feature, part))),
        "size" => MatchSize(value, game.SourceSize),
        "version" => MatchVersion(value, game.DisplayVersion),
        "fw" or "firmware" => MatchVersion(value, game.RequiredSystemSoftware),
        _ => MatchFreeText(game, value)
    };

    private static bool MatchAny(string value, Func<string, bool> predicate)
    {
        foreach (string part in value.Split('|', StringSplitOptions.RemoveEmptyEntries))
            if (predicate(part)) return true;
        return false;
    }

    private static bool ContainsText(string? source, string value) =>
        !string.IsNullOrEmpty(source) && source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static bool MatchSize(string value, long size)
    {
        if (TryParseComparison(value, out string op, out string rest) && TryParseSize(rest, out long target))
            return Satisfies(size, target, op);
        return TryParseSize(value, out long minimum) && size >= minimum;
    }

    private static bool MatchVersion(string value, string candidate)
    {
        if (TryParseComparison(value, out string op, out string rest))
            return CompareVersions(candidate, rest) is int comparison && Satisfies(comparison, op);
        return ContainsText(candidate, value);
    }

    private static bool TryParseComparison(string value, out string op, out string rest)
    {
        foreach (string candidate in new[] { ">=", "<=", ">", "<", "=" })
        {
            if (value.StartsWith(candidate, StringComparison.Ordinal))
            {
                op = candidate;
                rest = value[candidate.Length..].Trim();
                return true;
            }
        }
        op = "=";
        rest = value;
        return false;
    }

    private static bool Satisfies(long value, long target, string op) => op switch
    {
        ">" => value > target,
        "<" => value < target,
        ">=" => value >= target,
        "<=" => value <= target,
        _ => value == target
    };

    private static bool Satisfies(int comparison, string op) => op switch
    {
        ">" => comparison > 0,
        "<" => comparison < 0,
        ">=" => comparison >= 0,
        "<=" => comparison <= 0,
        _ => comparison == 0
    };

    private static int? CompareVersions(string? left, string? right)
    {
        long[]? a = ParseVersion(left);
        long[]? b = ParseVersion(right);
        if (a is null || b is null) return null;
        int length = Math.Max(a.Length, b.Length);
        for (int i = 0; i < length; i++)
        {
            long x = i < a.Length ? a[i] : 0;
            long y = i < b.Length ? b[i] : 0;
            if (x != y) return x < y ? -1 : 1;
        }
        return 0;
    }

    private static long[]? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = new List<long>();
        foreach (string piece in value.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            int digits = 0;
            while (digits < piece.Length && char.IsDigit(piece[digits])) digits++;
            if (digits == 0) break;
            parts.Add(long.Parse(piece[..digits], CultureInfo.InvariantCulture));
        }
        return parts.Count > 0 ? parts.ToArray() : null;
    }

    private static bool TryParseSize(string value, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string text = value.Trim().ToUpperInvariant().Replace(" ", string.Empty);
        int digits = 0;
        while (digits < text.Length && (char.IsDigit(text[digits]) || text[digits] == '.')) digits++;
        if (digits == 0 ||
            !double.TryParse(text[..digits], NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            return false;
        double multiplier = text[digits..] switch
        {
            "" or "B" => 1d,
            "K" or "KB" => 1024d,
            "M" or "MB" => 1024d * 1024d,
            "G" or "GB" => 1024d * 1024d * 1024d,
            "T" or "TB" => 1024d * 1024d * 1024d * 1024d,
            _ => 0d
        };
        if (multiplier == 0d) return false;
        bytes = (long)(number * multiplier);
        return true;
    }

    private static string SourceFilterValue(Ps5GameInfo game) => game.SourceKind switch
    {
        Ps5SourceKind.LooseDump => "Dump Files",
        Ps5SourceKind.SonyPackage => "PKG",
        Ps5SourceKind.Ffpfsc => "FFPFSC",
        Ps5SourceKind.FilesystemImage => "exFAT",
        Ps5SourceKind.Ffpkg => "FFPKG",
        _ => "Other"
    };

    private static string RegionOf(Ps5GameInfo game)
    {
        string id = !string.IsNullOrWhiteSpace(game.ContentId) ? game.ContentId : game.TitleId;
        if (id.Length < 1) return "Unknown";
        return char.ToUpperInvariant(id[0]) switch
        {
            'U' => "Americas",
            'E' => "Europe",
            'J' => "Japan",
            'K' => "Korea",
            'A' => "Asia",
            'H' => "Hong Kong",
            _ => "Other"
        };
    }

    private static string CategoryOf(Ps5GameInfo game)
    {
        string raw = game.ApplicationCategory;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            string token = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? raw;
            if (int.TryParse(token, out int number))
            {
                // Only 1 selects additional content; publisher-specific encodings such as 0x01000000
                // are base applications, matching the engine's ProsperoParam normalization.
                return number switch
                {
                    1 => "DLC",
                    2 => "Patch",
                    3 => "App",
                    _ => "Game"
                };
            }
            return raw;
        }
        return game.Package?.ContentType switch
        {
            0x20 => "Game",
            0x21 => "Patch",
            0x22 => "Add-on",
            _ => game.SourceKind == Ps5SourceKind.LooseDump ? "Game" : "Unknown"
        };
    }
}

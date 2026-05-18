using System.Globalization;
using System.Text.RegularExpressions;
using SpaceSweeper.Core.Scanning;

namespace SpaceSweeper.Core.Analysis;

public sealed class StorageNodeFilter
{
    public static readonly StorageNodeFilter Empty = new([], [], DateTimeOffset.UtcNow);

    private readonly IReadOnlyList<FilterCriterion> _includes;
    private readonly IReadOnlyList<FilterCriterion> _excludes;
    private readonly DateTimeOffset _now;

    private StorageNodeFilter(
        IReadOnlyList<FilterCriterion> includes,
        IReadOnlyList<FilterCriterion> excludes,
        DateTimeOffset now)
    {
        _includes = includes;
        _excludes = excludes;
        _now = now;
    }

    public bool HasCriteria => _includes.Count > 0 || _excludes.Count > 0;

    public static StorageNodeFilter Compile(string? query, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Empty;
        }

        query = query.Length > 512 ? query[..512] : query;
        var includes = new List<FilterCriterion>();
        var excludes = new List<FilterCriterion>();

        foreach (var rawToken in query.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(16))
        {
            var token = rawToken;
            var exclude = token.StartsWith('|');
            if (exclude)
            {
                token = token[1..].Trim();
            }

            if (token.Length == 0)
            {
                continue;
            }

            var criterion = ParseCriterion(token);
            if (exclude)
            {
                excludes.Add(criterion);
            }
            else
            {
                includes.Add(criterion);
            }
        }

        return includes.Count == 0 && excludes.Count == 0
            ? Empty
            : new StorageNodeFilter(includes, excludes, now ?? DateTimeOffset.UtcNow);
    }

    public bool Matches(StorageNode node, StorageNodeTag? tag)
    {
        return !IsExcluded(node, tag) && IsIncluded(node, tag);
    }

    public bool IsExcluded(StorageNode node, StorageNodeTag? tag)
    {
        return _excludes.Any(criterion => criterion.Matches(node, tag, _now));
    }

    public bool IsIncluded(StorageNode node, StorageNodeTag? tag)
    {
        return _includes.Count == 0 || _includes.All(criterion => criterion.Matches(node, tag, _now));
    }

    public bool MatchesSubtree(StorageNode node, Func<StorageNode, StorageNodeTag?> tagResolver)
    {
        return new StorageNodeVisibility(this, tagResolver).IsSubtreeVisible(node);
    }

    private static FilterCriterion ParseCriterion(string token)
    {
        if (token.StartsWith(':'))
        {
            return FilterCriterion.ForTag(token[1..]);
        }

        if ((token[0] == '>' || token[0] == '<') && token.Length > 1)
        {
            var comparison = token[0];
            var value = token[1..].Trim();
            if (TryParseAge(value, out var age))
            {
                return FilterCriterion.ForAge(comparison, age);
            }

            if (TryParseSize(value, out var size))
            {
                return FilterCriterion.ForSize(comparison, size);
            }
        }

        return FilterCriterion.ForName(token);
    }

    private static bool TryParseSize(string value, out long bytes)
    {
        bytes = 0;
        var match = Regex.Match(value, @"^(?<number>\d+(?:\.\d+)?)\s*(?<unit>b|kb|k|mb|m|gb|g|tb|t)?$", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups["number"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        var multiplier = match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "k" or "kb" => 1024d,
            "m" or "mb" => 1024d * 1024,
            "g" or "gb" => 1024d * 1024 * 1024,
            "t" or "tb" => 1024d * 1024 * 1024 * 1024,
            _ => 1d
        };

        bytes = (long)Math.Max(0, number * multiplier);
        return true;
    }

    private static bool TryParseAge(string value, out TimeSpan age)
    {
        age = TimeSpan.Zero;
        var match = Regex.Match(value, @"^(?<number>\d+(?:\.\d+)?)\s*(?<unit>d|day|days|mo|month|months|y|year|years)$", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups["number"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        age = match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "y" or "year" or "years" => TimeSpan.FromDays(number * 365),
            "mo" or "month" or "months" => TimeSpan.FromDays(number * 30),
            _ => TimeSpan.FromDays(number)
        };
        return true;
    }

    private sealed class FilterCriterion
    {
        private readonly Func<StorageNode, StorageNodeTag?, DateTimeOffset, bool> _predicate;

        private FilterCriterion(Func<StorageNode, StorageNodeTag?, DateTimeOffset, bool> predicate)
        {
            _predicate = predicate;
        }

        public static FilterCriterion ForName(string token)
        {
            var pattern = token.Contains('*') || token.Contains('?') ? token : $"*{token}*";
            var regex = new Regex(
                "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));

            return new FilterCriterion((node, _, _) =>
            {
                try
                {
                    return regex.IsMatch(node.Name);
                }
                catch (RegexMatchTimeoutException)
                {
                    return false;
                }
            });
        }

        public static FilterCriterion ForSize(char comparison, long size)
        {
            return comparison == '>'
                ? new FilterCriterion((node, _, _) => node.Length >= size)
                : new FilterCriterion((node, _, _) => node.Length <= size);
        }

        public static FilterCriterion ForAge(char comparison, TimeSpan age)
        {
            return comparison == '>'
                ? new FilterCriterion((node, _, now) => node.LastWriteTime is not null && now - node.LastWriteTime.Value >= age)
                : new FilterCriterion((node, _, now) => node.LastWriteTime is not null && now - node.LastWriteTime.Value <= age);
        }

        public static FilterCriterion ForTag(string value)
        {
            if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
            {
                return new FilterCriterion((_, tag, _) => tag is not null);
            }

            return Enum.TryParse<StorageNodeTag>(value, ignoreCase: true, out var expected)
                ? new FilterCriterion((_, tag, _) => tag == expected)
                : new FilterCriterion((_, _, _) => false);
        }

        public bool Matches(StorageNode node, StorageNodeTag? tag, DateTimeOffset now)
        {
            return _predicate(node, tag, now);
        }
    }
}

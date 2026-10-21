namespace Orchard.Services;

/// <summary>Ranks launchable items against what the user typed into Spotlight or Launchpad.</summary>
public static class AppSearch
{
    /// <returns>0 for no match; higher is better.</returns>
    public static int Score(string query, string name)
    {
        query = query.Trim();
        if (query.Length == 0) return 1;
        var q = query.ToLowerInvariant();
        var n = name.ToLowerInvariant();

        if (n == q) return 1000;
        if (n.StartsWith(q, StringComparison.Ordinal)) return 900 - n.Length;

        var words = n.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w.StartsWith(q, StringComparison.Ordinal))) return 700 - n.Length;

        var acronym = string.Concat(words.Select(w => w[0]));
        if (acronym.StartsWith(q, StringComparison.Ordinal)) return 600 - n.Length;

        if (n.Contains(q, StringComparison.Ordinal)) return 500 - n.Length;

        // Subsequence: every typed character appears in order ("chrm" → "Google Chrome").
        int j = 0, gaps = 0;
        for (int i = 0; i < n.Length && j < q.Length; i++)
        {
            if (n[i] == q[j]) j++;
            else if (j > 0) gaps++;
        }
        return j == q.Length && q.Length >= 2 ? Math.Max(10, 200 - gaps * 5 - n.Length) : 0;
    }

    public static IEnumerable<T> Rank<T>(IEnumerable<T> items, string query, Func<T, string> name, int take = 8) =>
        items.Select(i => (Item: i, Score: Score(query, name(i))))
             .Where(x => x.Score > 0)
             .OrderByDescending(x => x.Score)
             .ThenBy(x => name(x.Item), StringComparer.OrdinalIgnoreCase)
             .Take(take)
             .Select(x => x.Item);
}

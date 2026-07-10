namespace Playdex.Api.Games;

public static class GameConnections
{
    public static (long First, long Second) Normalize(long a, long b)
    {
        if (a == b) throw new ArgumentException("Cannot connect a game to itself.");
        return a < b ? (a, b) : (b, a);
    }

    public static HashSet<long> ResolveConnectedIds(long gameId, List<(long GameId, long ConnectedGameId)> edges)
    {
        var adj = new Dictionary<long, List<long>>();
        foreach (var (g, c) in edges)
        {
            if (!adj.ContainsKey(g)) adj[g] = [];
            if (!adj.ContainsKey(c)) adj[c] = [];
            adj[g].Add(c);
            adj[c].Add(g);
        }

        var visited = new HashSet<long>();
        var queue = new Queue<long>();
        queue.Enqueue(gameId);
        visited.Add(gameId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adj.TryGetValue(current, out var neighbors)) continue;
            foreach (var n in neighbors)
            {
                if (visited.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return visited;
    }

    public static Dictionary<long, double> MergeGameTotals(
        Dictionary<long, double> gameTotals,
        List<(long GameId, long ConnectedGameId)> edges)
    {
        var parent = new Dictionary<long, long>();
        long Find(long x)
        {
            if (!parent.ContainsKey(x)) parent[x] = x;
            if (parent[x] != x) parent[x] = Find(parent[x]);
            return parent[x];
        }
        void Union(long a, long b) => parent[Find(a)] = Find(b);

        foreach (var (g, c) in edges)
        {
            Union(g, c);
        }

        var merged = new Dictionary<long, (double Total, long RepId)>();
        foreach (var (gameId, total) in gameTotals)
        {
            var root = Find(gameId);
            if (!merged.ContainsKey(root) || total > merged[root].Total)
            {
                merged[root] = (total, gameId);
            }
            else
            {
                var existing = merged[root];
                merged[root] = (existing.Total + total, existing.RepId);
            }
        }

        return merged.ToDictionary(kv => kv.Value.RepId, kv => kv.Value.Total);
    }
}

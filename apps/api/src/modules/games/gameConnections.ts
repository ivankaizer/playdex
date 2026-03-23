export type GameConnectionEdge = {
  game_id: number;
  connected_game_id: number;
};

export type GameTotalsRow = {
  game_id: number;
  title: string;
  total_seconds: number;
};

export const normalizeGameConnection = (
  gameId: number,
  connectedGameId: number
) => {
  if (gameId === connectedGameId) {
    return null;
  }
  return gameId < connectedGameId
    ? [gameId, connectedGameId]
    : [connectedGameId, gameId];
};

export const resolveConnectedGameIds = (
  gameId: number,
  edges: GameConnectionEdge[]
) => {
  const graph = new Map<number, Set<number>>();
  const addEdge = (from: number, to: number) => {
    const existing = graph.get(from);
    if (existing) {
      existing.add(to);
      return;
    }
    graph.set(from, new Set([to]));
  };

  edges.forEach((edge) => {
    addEdge(edge.game_id, edge.connected_game_id);
    addEdge(edge.connected_game_id, edge.game_id);
  });

  const visited = new Set<number>();
  const stack = [gameId];
  while (stack.length > 0) {
    const current = stack.pop();
    if (!current || visited.has(current)) {
      continue;
    }
    visited.add(current);
    const neighbors = graph.get(current);
    if (!neighbors) {
      continue;
    }
    neighbors.forEach((neighbor) => {
      if (!visited.has(neighbor)) {
        stack.push(neighbor);
      }
    });
  }

  return visited.size > 0 ? Array.from(visited) : [gameId];
};

export const mergeGameTotalsByConnections = (
  rows: GameTotalsRow[],
  edges: GameConnectionEdge[]
) => {
  if (rows.length === 0) {
    return [] as GameTotalsRow[];
  }

  const parent = new Map<number, number>();
  const find = (id: number): number => {
    const existing = parent.get(id);
    if (existing === undefined) {
      parent.set(id, id);
      return id;
    }
    if (existing === id) {
      return id;
    }
    const root = find(existing);
    parent.set(id, root);
    return root;
  };
  const union = (left: number, right: number) => {
    const leftRoot = find(left);
    const rightRoot = find(right);
    if (leftRoot === rightRoot) {
      return;
    }
    const minRoot = Math.min(leftRoot, rightRoot);
    const maxRoot = Math.max(leftRoot, rightRoot);
    parent.set(maxRoot, minRoot);
  };

  edges.forEach((edge) => union(edge.game_id, edge.connected_game_id));

  const grouped = new Map<
    number,
    { totalSeconds: number; entries: GameTotalsRow[] }
  >();

  rows.forEach((row) => {
    const root = find(row.game_id);
    const existing = grouped.get(root);
    if (existing) {
      existing.totalSeconds += row.total_seconds;
      existing.entries.push(row);
      return;
    }
    grouped.set(root, {
      totalSeconds: row.total_seconds,
      entries: [row],
    });
  });

  return Array.from(grouped.values())
    .map((group) => {
      const primary = group.entries.reduce((best, current) =>
        current.total_seconds > best.total_seconds ? current : best
      );
      return {
        game_id: primary.game_id,
        title: primary.title,
        total_seconds: group.totalSeconds,
      };
    })
    .sort((a, b) => b.total_seconds - a.total_seconds);
};

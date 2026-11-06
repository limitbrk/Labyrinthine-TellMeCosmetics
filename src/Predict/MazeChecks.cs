using System.Collections.Generic;
using System.Linq;

namespace TellMeCosmetics.Predict;

using Placed = MazeStructures.Placed;
using Structure = MazeStructures.Structure;

// Port of the GenerateNewMaze validation steps after structure placement (GameAssembly RVA 0x81c280 .. 0x897660):
// RemoveInvalidTransitions, FillCustomUnwalkableStructuresList, EnsureExitIsReachable (exact A*), RemoveUnreachableAreas,
// RemoveUnreachableLandmarks, RequiredLandmarksExist, InsertSafezones.
public static class MazeChecks
{
    private const int Walkable = 0, Wall = 1, Landmark = 2, Safezone = 3, LandmarkBorder = 4;

    public sealed class Safezones
    {
        public int Cells, MinSpawned, MinTried, DistStart, DistLandmark, DistOthers;
        public bool MoveForward;
        public int GridSize, GridSpacing;   // SafezoneGrid.Size is mutated by InsertSafezones
        public float ReduceMapSize = -1;    // Contract.ShouldReduceMapSize multiplier, -1 when false
        public bool[] SubCanPlace;          // possibleSubmazes[i].canPlaceSafezones
    }

    public sealed class World
    {
        public MazeSubmazes.State S;
        public List<Placed> Spawned = new(), Puzzles = new();
    }

    // ---------- RemoveInvalidTransitions / FillCustomUnwalkableStructuresList ----------

    public static void RemoveInvalidTransitions(World w)
    {
        var s = w.S;
        if (s.Transitions == null || s.Transitions.Count == 0) return;
        var remove = s.Transitions.Keys.Where(k => !IsStraight(s, k.x, k.y, true) && !IsStraight(s, k.x, k.y, false)).ToList();
        foreach (var p in remove) { s.Transitions.Remove(p); SetTile(s, p.x, p.y, Wall); }
    }

    public static void FillCustomUnwalkable(World w)
    {
        w.S.CustomUnwalkable.Clear();
        foreach (var p in w.Spawned)
            if (p.S.NonWalkable != null)
                foreach (var o in p.S.NonWalkable) w.S.CustomUnwalkable.Add((p.X + o.x, p.Y + o.y));
    }

    private static bool InMaze(MazeSubmazes.State s, int x, int y) => x >= 0 && y >= 0 && x < s.MazeX && y < s.MazeY;
    private static bool InMap(MazeSubmazes.State s, int x, int y) => (uint)x < (uint)s.Map.GetLength(0) && (uint)y < (uint)s.Map.GetLength(1);
    private static void SetTile(MazeSubmazes.State s, int x, int y, int v) { if (InMap(s, x, y)) s.Map[x, y] = v; }

    private static bool IsStraight(MazeSubmazes.State s, int x, int y, bool dirY)
    {
        bool Open(int a, int b) => InMaze(s, a, b) && s.Map[a, b] != Wall;
        if (!Open(x, y)) return false;
        bool up = Open(x, y + 1), down = Open(x, y - 1), r = Open(x + 1, y), l = Open(x - 1, y);
        return dirY ? up && down && !r && !l : !up && !down && l && r;
    }

    // ---------- exact Pathfinding.FindPath ----------

    // HashSet<Node> iteration order: slot order, freed slots reused LIFO (same in .NET Framework and corefx).
    private sealed class SlotSet
    {
        private readonly List<(int x, int y)?> slots = new();
        private readonly Stack<int> free = new();
        private readonly Dictionary<(int, int), int> index = new();
        public int Count => index.Count;
        public bool Contains((int, int) p) => index.ContainsKey(p);
        public void Add((int x, int y) p)
        {
            if (index.ContainsKey(p)) return;
            int i;
            if (free.Count > 0) { i = free.Pop(); slots[i] = p; }
            else { i = slots.Count; slots.Add(p); }
            index[p] = i;
        }
        public void Remove((int, int) p)
        {
            if (!index.Remove(p, out int i)) return;
            slots[i] = null;
            free.Push(i);
        }
        public IEnumerable<(int x, int y)> Items() { foreach (var p in slots) if (p.HasValue) yield return p.Value; }
    }

    private static bool InPf(MazeSubmazes.State s, int x, int y) => x >= 0 && y >= 0 && x < s.PfRows && y < s.PfCols;

    private static bool CollidesWithStartOrEnd(MazeSubmazes.State s, int x, int y)
    {
        foreach (var (nx, ny) in new[] { (x - 1, y), (x, y - 1), (x, y + 1), (x + 1, y) })
        {
            if (!InPf(s, nx, ny)) continue;
            int v = s.Map[nx, ny];
            if (v == MazeSubmazes.StartTile && (s.Start.x - x, s.Start.y - y) != s.StartDir) return true;
            if (v == MazeSubmazes.EndTile && (s.End.x - x, s.End.y - y) != s.EndDir) return true;
        }
        return false;
    }

    private static bool TryTransition(MazeSubmazes.State s, int x, int y, out MazeSubmazes.Trans t)
    {
        t = default;
        return s.Transitions != null && s.Transitions.TryGetValue((x, y), out t);
    }

    private static bool SameSubmazeAround(MazeSubmazes.State s, int x, int y, int r)
    {
        for (int i = x - r; i <= x + r; i++)
            for (int j = y - r; j <= y + r; j++)
                if (InMaze(s, i, j) && s.Types[i, j] != s.Types[x, y]) return false;
        return true;
    }

    private static bool NodeOk(MazeSubmazes.State s, int x, int y, int curX, bool ignoreWalls, bool useTypes)
    {
        if (!InPf(s, x, y) || CollidesWithStartOrEnd(s, x, y)) return false;
        if (s.CustomUnwalkable.Contains((x, y))) return false;
        if (!ignoreWalls) return s.Map[x, y] != Wall;
        for (int i = x - 1; i <= x + 1; i++)
            for (int j = y - 1; j <= y + 1; j++)
                if (InPf(s, i, j) && s.Map[i, j] == Landmark) return false;
        if (!useTypes) return true;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (!(System.Math.Abs(dx) == 1 && System.Math.Abs(dy) == 1)
                    && TryTransition(s, x + dx, y + dy, out var t) && t.Vertical != (dx == 0)) return false;
        if (!SameSubmazeAround(s, x, y, 1) && (!TryTransition(s, x, y, out var t2) || (x == curX) != t2.Vertical)) return false;
        return true;
    }

    private static (int x, int y) ProcessPosition(MazeSubmazes.State s, (int x, int y) p)
    {
        if (p == s.Start) p = (p.x - s.StartDir.x, p.y - s.StartDir.y);
        else if (p == s.End) p = (p.x - s.EndDir.x, p.y - s.EndDir.y);
        return (p.x < 0 ? 0 : System.Math.Min(p.x, s.PfRows - 1), p.y < 0 ? 0 : System.Math.Min(p.y, s.PfCols - 1));
    }

    private static int Manhattan((int x, int y) a, (int x, int y) b) => System.Math.Abs(a.x - b.x) + System.Math.Abs(a.y - b.y);

    public static bool FindPath(MazeSubmazes.State s, (int x, int y) from, (int x, int y) to, List<(int x, int y)> path, bool ignoreWalls, bool useTypes)
    {
        from = ProcessPosition(s, from);
        to = ProcessPosition(s, to);
        var g = new Dictionary<(int, int), int>();
        var f = new Dictionary<(int, int), int>();
        var came = new Dictionary<(int, int), (int x, int y)>();
        int G((int, int) p) => g.TryGetValue(p, out int v) ? v : int.MaxValue;

        var open = new SlotSet();
        var closed = new SlotSet();
        open.Add(from);
        g[from] = 0;
        f[from] = Manhattan(from, to);
        while (open.Count > 0)
        {
            (int x, int y) cur = default;
            bool first = true;
            foreach (var p in open.Items())
                if (first || f[p] < f[cur]) { cur = p; first = false; }
            if (cur == to)
            {
                if (path != null)
                {
                    path.Clear();
                    for (var n = cur; ; n = came[n]) { path.Add(n); if (!came.ContainsKey(n)) break; }
                }
                return true;
            }
            open.Remove(cur);
            closed.Add(cur);
            foreach (var n in new[] { (cur.x, cur.y + 1), (cur.x, cur.y - 1), (cur.x + 1, cur.y), (cur.x - 1, cur.y) })
            {
                if (!NodeOk(s, n.Item1, n.Item2, cur.x, ignoreWalls, useTypes) || closed.Contains(n)) continue;
                int t = G(cur) + 1;
                if (t >= G(n)) continue;
                came[n] = cur;
                g[n] = t;
                f[n] = t + Manhattan(n, to);
                if (!open.Contains(n)) open.Add(n);
            }
        }
        if (path != null) { path.Clear(); path.AddRange(closed.Items()); }
        return false;
    }

    private static float Dist((int x, int y) a, (int x, int y) b)
    {
        float dx = a.x - (float)b.x, dy = a.y - (float)b.y;
        return (float)System.Math.Sqrt(dy * dy + dx * dx);
    }

    public static bool EnsureExitIsReachable(World w, bool useTypes)
    {
        var s = w.S;
        var path = new List<(int x, int y)>();
        if (FindPath(s, s.Start, s.End, path, false, false)) return true;
        var cands = path.OrderBy(p => Dist(p, s.End)).ToList();
        int n = System.Math.Min(cands.Count, 10);
        for (int i = 0; i < n; i++)
        {
            if (!FindPath(s, cands[i], s.End, path, true, useTypes)) continue;
            foreach (var p in path) SetTile(s, p.x, p.y, Walkable);
            return true;
        }
        return false;
    }

    // ---------- RemoveUnreachableAreas / landmarks ----------

    public static void RemoveUnreachableAreas(World w)
    {
        var s = w.S;
        var last = s.Start;
        for (int x = 0; x < s.PfRows; x++)
            for (int y = 0; y < s.PfCols; y++)
            {
                int v = s.Map[x, y];
                if (v != Walkable && v != Landmark && v != LandmarkBorder) continue;
                if (MazeSubmazes.CanReachPoint(s, (x, y), last)) last = (x, y);
                else s.Map[x, y] = Wall;
            }
    }

    // Returns true when at least one landmark was removed.
    public static bool RemoveUnreachableLandmarks(World w)
    {
        var s = w.S;
        if (w.Spawned.Count == 0) return false;
        var keep = new List<Placed>();
        var keepPuzzles = new List<Placed>();
        bool changed = false;
        FillCustomUnwalkable(w);
        foreach (var p in w.Spawned)
        {
            if (!IsLandmarkValid(s, p) || !CanReachLandmark(s, p))
            {
                changed = true;
                for (int i = 0; i < p.S.DimX; i++)
                    for (int j = 0; j < p.S.DimY; j++) SetTile(s, p.X + i, p.Y + j, Wall);
                continue;
            }
            keep.Add(p);
            if (w.Puzzles.Contains(p)) keepPuzzles.Add(p);
        }
        w.Spawned = keep;
        w.Puzzles = keepPuzzles;
        return changed;
    }

    private static bool IsLandmarkValid(MazeSubmazes.State s, Placed p)
    {
        for (int i = 0; i < p.S.DimX; i++)
            for (int j = 0; j < p.S.DimY; j++)
            {
                if (p.S.BorderTile(i, j) == Wall) continue;
                int v = s.Map[p.X + i, p.Y + j];
                if (v != Landmark && v != LandmarkBorder) return false;
            }
        return true;
    }

    private static bool CanReachLandmark(MazeSubmazes.State s, Placed p)
    {
        for (int i = 0; i < p.S.DimX; i++)
            for (int j = 0; j < p.S.DimY; j++)
                if (s.Map[p.X + i, p.Y + j] == Landmark) return MazeSubmazes.CanReachStartOrEnd(s, (p.X + i, p.Y + j));
        return false;
    }

    public static bool RequiredLandmarksExist(World w, Structure[] required, int minRequired)
    {
        int found = 0;
        if (required != null)
            foreach (var r in required)
                if (r != null && w.Spawned.Any(p => p.S.PrefabId == r.PrefabId)) found++;
        return minRequired <= found;
    }

    // ---------- InsertSafezones ----------

    private static bool IsDeadEnd(MazeSubmazes.State s, int x, int y, bool requireFull)
    {
        int mask = 0;
        var ring = new[] { (x - 1, y + 1), (x - 1, y), (x - 1, y - 1), (x, y - 1), (x + 1, y - 1), (x + 1, y), (x + 1, y + 1), (x, y + 1) };
        for (int b = 0; b < 8; b++)
            if (InMaze(s, ring[b].Item1, ring[b].Item2) && s.Map[ring[b].Item1, ring[b].Item2] == Wall) mask |= 1 << b;
        int run = 0, need = (requireFull ? 1 : 0) + 6;
        for (int i = 0; i < 16; i++)
        {
            run = (mask & (1 << (i < 8 ? i : i - 8))) != 0 ? run + 1 : 0;
            if (run >= need) return true;
        }
        return false;
    }

    private static bool IsTileInRange(MazeSubmazes.State s, int x, int y, int r, int tile)
    {
        for (int i = x - r; i <= x + r; i++)
            for (int j = y - r; j <= y + r; j++)
                if (InMap(s, i, j) && s.Map[i, j] == tile) return true;
        return false;
    }

    private static bool TooClose((int x, int y) p, IEnumerable<(int x, int y)> pts, int d) =>
        pts.Any(q => System.Math.Abs(p.x - q.x) < d && System.Math.Abs(p.y - q.y) < d);

    private static (int x, int y) SafezoneDir(MazeSubmazes.State s, (int x, int y) p)
    {
        foreach (var (dx, dy) in new[] { (-1, 0), (0, -1), (0, 1), (1, 0) })
        {
            int x = p.x + dx, y = p.y + dy;
            if (InMaze(s, x, y) && (s.Map[x, y] == Walkable || s.Map[x, y] == LandmarkBorder)) return (dx, dy);
        }
        return (0, 0);
    }

    public static int InsertSafezones(World w, Safezones z)
    {
        var s = w.S;
        int computed = (System.Math.Min(s.MazeX, s.MazeY) - (z.Cells + 1) * z.GridSpacing) / z.Cells;
        z.GridSize = System.Math.Max(computed, z.GridSize > 1 ? 2 : 1);
        int step = z.GridSpacing + z.GridSize;
        int cellsX = (s.MazeX - z.GridSpacing) / step, cellsY = (s.MazeY - z.GridSpacing) / step;
        int offX = ((s.MazeX - z.GridSpacing) % step) / 2 + z.GridSpacing, offY = ((s.MazeY - z.GridSpacing) % step) / 2 + z.GridSpacing;

        int area = s.MazeX * s.MazeY;
        if (z.ReduceMapSize >= 0) area = (int)((float)area / ((z.ReduceMapSize + 1f) * 0.5f));
        int tries = System.Math.Max(z.MinTried, area / (int)((float)(area / 800 + 12) * (float)(area / 800 + 12)));

        var gridCells = new List<(int x, int y)>();
        for (int gy = 0; gy < cellsY; gy++)
            for (int gx = 0; gx < cellsX; gx++) gridCells.Add((gx, gy));

        var rnd = new NetRandom(unchecked(s.SecondSeed + 0xad));
        var cands = new List<(int x, int y)>();
        int placed = 0;
        while (tries > 0)
        {
            if (gridCells.Count == 0) break;
            int i = rnd.Next(0, gridCells.Count);
            var cell = gridCells[i];
            gridCells.RemoveAt(i);
            cands.Clear();
            int minX = step * cell.x + offX, minY = step * cell.y + offY;
            for (int y = minY; y < minY + z.GridSize; y++)
                for (int x = minX; x < minX + z.GridSize; x++)
                {
                    if (s.Map[x, y] != Walkable || !IsDeadEnd(s, x, y, true)) continue;
                    int t = s.Types[x, y];
                    if (t > 0 && z.SubCanPlace != null && !z.SubCanPlace[t]) continue;
                    if (IsTileInRange(s, x, y, z.DistStart, MazeSubmazes.StartTile) || IsTileInRange(s, x, y, z.DistStart, MazeSubmazes.EndTile)
                        || IsTileInRange(s, x, y, z.DistLandmark, Landmark) || TooClose((x, y), cands, z.DistOthers)
                        || (s.Transitions != null && TooClose((x, y), s.Transitions.Keys, z.DistOthers))) continue;
                    cands.Add((x, y));
                }
            while (cands.Count > 0)
            {
                int j = rnd.Next(0, cands.Count);
                var p = cands[j];
                var dir = SafezoneDir(s, p);
                if (p != s.Start && p != s.End && !IsTileInRange(s, p.x, p.y, 3, Safezone) && MazeSubmazes.CanReachStartOrEnd(s, p))
                {
                    if (!z.MoveForward) SetTile(s, p.x, p.y, Safezone);
                    else { SetTile(s, p.x, p.y, Wall); SetTile(s, p.x + dir.x, p.y + dir.y, Safezone); }
                    placed++;
                    tries--;
                    break;
                }
                cands.RemoveAt(j);
            }
        }
        return placed;
    }
}

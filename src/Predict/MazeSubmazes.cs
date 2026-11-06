using System.Collections.Generic;
using System.Linq;

namespace TellMeCosmetics.Predict;

// Port of MazeGenerator.SetupSubmazes and its callees (GameAssembly RVA 0x81cea0 .. 0x8973d0).
public static class MazeSubmazes
{
    public const int Walkable = 0, Wall = 1, Landmark = 2, StartTile = 100, EndTile = 101;

    public sealed class Submaze { public int RadiusMin, RadiusMax, SpawnTries; public float Chance; }
    public struct Trans { public int From, To; public bool Vertical, Flip; }

    // Mutable state shared by every generation step, mirroring MazeGenerator/MazeDataGenerator fields.
    public sealed class State
    {
        public int MazeX, MazeY;            // MazeGenerator.mazeX/mazeY, bounds of map/types
        public int SecondSeed;              // MazeGenerator.secondSeed, seeds per-step System.Random instances
        public int PfRows, PfCols;          // Pathfinding.sizeRows/sizeCols
        public int[,] Map;                  // MazeMap.map
        public int[,] Types;                // MazeGenerator.mazeTypeMap
        public Dictionary<(int x, int y), Trans> Transitions; // null after a failed SetupSubmazes, like the game
        public Submaze[] Subs;
        public (int x, int y) Start, End, StartDir, EndDir;
        public HashSet<(int x, int y)> CustomUnwalkable = new();
        public List<(int x, int y)> StructureCenters = new(); // spawnedStructures, used by AreSubmazesValid
    }

    private static bool InMaze(State s, int x, int y) => x >= 0 && y >= 0 && x < s.MazeX && y < s.MazeY;
    private static int Tile(State s, int x, int y) => s.Map[x, y];

    public static bool SetupSubmazes(State s, NetRandom rnd)
    {
        s.Types = new int[s.MazeX, s.MazeY];
        s.Transitions?.Clear();
        if (s.Subs == null || s.Subs.Length == 0) return false;

        var backup = (int[,])s.Map.Clone();
        bool placed = false;
        for (int i = 1; i < s.Subs.Length; i++)
        {
            var sub = s.Subs[i];
            int tries = sub.SpawnTries < 2 ? 1 : sub.SpawnTries;
            for (int t = 0; t < tries; t++)
                if (rnd.NextDouble() <= sub.Chance && TryToPlaceSubmaze(s, i, rnd)) placed = true;
        }
        if (!placed) return false;

        ProcessSubmazes(s);
        if (CanReachPoint(s, s.Start, s.End) && AreSubmazesValid(s)) return true;

        System.Array.Copy(backup, s.Map, backup.Length);
        s.Types = new int[s.MazeX, s.MazeY];
        s.Transitions = null;
        return false;
    }

    private static float Dist(int ax, int ay, int bx, int by)
    {
        float dx = ax - (float)bx, dy = ay - (float)by;
        return (float)System.Math.Sqrt(dy * dy + dx * dx);
    }

    private static bool TryToPlaceSubmaze(State s, int i, NetRandom rnd)
    {
        if (s.Subs[i].RadiusMax <= 0) return false;
        for (int t = 0; t < 100; t++)
        {
            int x = rnd.Next(3, s.MazeX - 4), y = rnd.Next(3, s.MazeY - 4);
            if (!CanPlaceSubmazeAt(s, x, y, i, out int r)) continue;
            for (int px = x - r; px <= x + r; px++)
                for (int py = y - r; py <= y + r; py++)
                    if (InMaze(s, px, py) && Dist(x, y, px, py) <= r) s.Types[px, py] = i;
            return true;
        }
        return false;
    }

    private static bool CanPlaceSubmazeAt(State s, int x, int y, int i, out int radius)
    {
        radius = 0;
        var sub = s.Subs[i];
        int cap = (int)System.Math.Ceiling((double)((float)System.Math.Max(s.MazeX, s.MazeY) * 0.25f));
        if (sub.RadiusMax <= cap) cap = sub.RadiusMax;
        for (int r = sub.RadiusMin; r <= cap; r++)
        {
            int big = r + 2;
            for (int px = x - big; px <= x + big; px++)
                for (int py = y - big; py <= y + big; py++)
                    if (Dist(x, y, px, py) <= big && !CanInsertSubmazeAt(s, px, py)) return radius > 0;
            radius = r;
        }
        return radius > 0;
    }

    private static bool CanInsertSubmazeAt(State s, int x, int y)
    {
        if (!InMaze(s, x, y)) return true;
        if (Tile(s, x, y) == Landmark) return false;
        return s.Types[x, y] == 0;
    }

    private static int GetMostCommonType(State s, int x, int y, int[] buf)
    {
        if (System.Math.Abs(x - s.Start.x) < 3 && System.Math.Abs(y - s.Start.y) < 3) return 0;
        if (System.Math.Abs(x - s.End.x) < 3 && System.Math.Abs(y - s.End.y) < 3) return 0;
        System.Array.Clear(buf, 0, buf.Length);
        buf[s.Types[x, y]]++;
        foreach (var (nx, ny) in new[] { (x + 1, y), (x + 1, y + 1), (x + 1, y - 1), (x - 1, y), (x - 1, y + 1), (x - 1, y - 1), (x, y + 1), (x, y - 1) })
            if (InMaze(s, nx, ny) && Tile(s, nx, ny) != Wall) buf[s.Types[nx, ny]]++;
        int best = 0, idx = 0;
        for (int i = 0; i < s.Subs.Length; i++)
            if (buf[i] >= best) { best = buf[i]; idx = i; }
        return idx;
    }

    private static bool Open(State s, int x, int y, bool walkableOnly)
    {
        if (!InMaze(s, x, y)) return false;
        int v = Tile(s, x, y);
        return walkableOnly ? v == Walkable : v != Wall;
    }

    private static bool IsStraightPiece(State s, int x, int y, bool directionY, bool walkableOnly)
    {
        if (!Open(s, x, y, walkableOnly)) return false;
        bool up = Open(s, x, y + 1, walkableOnly), down = Open(s, x, y - 1, walkableOnly);
        bool r = Open(s, x + 1, y, walkableOnly), l = Open(s, x - 1, y, walkableOnly);
        return directionY ? up && down && !r && !l : !up && !down && l && r;
    }

    private static bool IsPositionValid(State s, int x, int y) => InMaze(s, x, y) && Tile(s, x, y) != Wall;

    private static void ProcessSubmazes(State s)
    {
        s.Transitions = new Dictionary<(int, int), Trans>();
        var buf = new int[10];
        for (int k = 0; k < 5; k++)
            for (int x = 0; x < s.MazeX; x++)
                for (int y = 0; y < s.MazeY; y++)
                    s.Types[x, y] = GetMostCommonType(s, x, y, buf);

        var copy = (int[,])s.Types.Clone();
        for (int k = 0; k < 20; k++)
        {
            for (int x = 1; x < s.MazeX - 1; x++)
                for (int y = 1; y < s.MazeY - 1; y++)
                {
                    if (Tile(s, x, y) != Walkable) continue;
                    if (Tile(s, x - 1, y) == Walkable && Tile(s, x + 1, y) == Walkable
                        && s.Types[x - 1, y] != s.Types[x + 1, y] && !IsStraightPiece(s, x, y, false, true))
                    {
                        int t = GetMostCommonType(s, x, y, buf);
                        copy[x, y] = t; copy[x + 1, y] = t; copy[x - 1, y] = t;
                    }
                    if (Tile(s, x, y - 1) == Walkable && Tile(s, x, y + 1) == Walkable
                        && s.Types[x, y - 1] != s.Types[x, y + 1] && !IsStraightPiece(s, x, y, true, true))
                    {
                        int t = GetMostCommonType(s, x, y, buf);
                        copy[x, y] = t; copy[x, y + 1] = t; copy[x, y - 1] = t;
                    }
                }
            System.Array.Copy(copy, s.Types, copy.Length);
        }

        for (int x = 1; x < s.MazeX - 1; x++)
            for (int y = 1; y < s.MazeY - 1; y++)
            {
                if (Tile(s, x, y) == Wall) continue;
                int open = (Tile(s, x + 1, y) != Wall ? 1 : 0) + (Tile(s, x - 1, y) != Wall ? 1 : 0)
                    + (Tile(s, x, y + 1) != Wall ? 1 : 0) + (Tile(s, x, y - 1) != Wall ? 1 : 0);
                if (open <= 2) continue;
                int t = GetMostCommonType(s, x, y, buf);
                foreach (var (nx, ny, dirY) in new[] { (x + 1, y, false), (x - 1, y, false), (x, y + 1, true), (x, y - 1, true) })
                    if (s.Types[nx, ny] != t && !IsStraightPiece(s, nx, ny, dirY, false) && Tile(s, nx, ny) == Walkable)
                        s.Map[nx, ny] = Wall;
            }

        for (int x = 1; x < s.MazeX - 1; x++)
            for (int y = 1; y < s.MazeY - 1; y++)
            {
                int up = s.Types[x - 1, y], down = s.Types[x + 1, y], right = s.Types[x, y + 1], left = s.Types[x, y - 1];
                if (up != down && IsStraightPiece(s, x, y, false, false)
                    && IsPositionValid(s, x - 1, y) && IsPositionValid(s, x, y) && IsPositionValid(s, x + 1, y))
                    s.Transitions[(x, y)] = new Trans { From = up, To = down, Vertical = false, Flip = down < up };
                if (right != left && IsStraightPiece(s, x, y, true, false)
                    && IsPositionValid(s, x, y - 1) && IsPositionValid(s, x, y) && IsPositionValid(s, x, y + 1))
                    s.Transitions[(x, y)] = new Trans { From = left, To = right, Vertical = true, Flip = right < left };
            }

        foreach (var key in s.Transitions.Keys.ToList())
        {
            if (!s.Transitions.TryGetValue(key, out var tr)) continue;
            if (!tr.Vertical) { s.Transitions.Remove((key.x - 1, key.y)); s.Transitions.Remove((key.x + 1, key.y)); }
            else { s.Transitions.Remove((key.x, key.y - 1)); s.Transitions.Remove((key.x, key.y + 1)); }
        }
    }

    private static bool AreSubmazesValid(State s)
    {
        var pos = new (int x, int y)[4];
        var nt = new int[4];
        for (int x = 0; x < s.MazeX; x++)
            for (int y = 0; y < s.MazeY; y++)
            {
                int t = s.Types[x, y];
                if (t == 0 || Tile(s, x, y) == Wall) continue;
                pos[0] = (x - 1, y); pos[1] = (x + 1, y); pos[2] = (x, y + 1); pos[3] = (x, y - 1);
                for (int i = 0; i < 4; i++)
                    nt[i] = InMaze(s, pos[i].x, pos[i].y) && Tile(s, pos[i].x, pos[i].y) != Wall ? s.Types[pos[i].x, pos[i].y] : -1;
                for (int i = 0; i < 4; i++)
                    if (nt[i] != -1 && nt[i] != t && !s.Transitions.ContainsKey(pos[i]) && !s.Transitions.ContainsKey((x, y)))
                        return false;
            }
        foreach (var c in s.StructureCenters)
            if (!CanReachStartOrEnd(s, c)) return false;
        return true;
    }

    public static bool CanReachStartOrEnd(State s, (int x, int y) p)
    {
        float ds = Dist(p.x, p.y, s.Start.x, s.Start.y), de = Dist(p.x, p.y, s.End.x, s.End.y);
        return CanReachPoint(s, p, ds < de ? s.Start : s.End);
    }

    // Pathfinding.FindPath without ignoreWalls: A* has no iteration cap, so plain reachability gives the same answer.
    public static bool CanReachPoint(State s, (int x, int y) from, (int x, int y) to)
    {
        from = ProcessPosition(s, from);
        to = ProcessPosition(s, to);
        if (from == to) return true;
        var seen = new HashSet<(int, int)> { from };
        var queue = new Queue<(int x, int y)>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var n in new[] { (x, y + 1), (x, y - 1), (x + 1, y), (x - 1, y) })
            {
                if (!NodeOk(s, n.Item1, n.Item2) || !seen.Add(n)) continue;
                if (n == to) return true;
                queue.Enqueue(n);
            }
        }
        return false;
    }

    private static (int x, int y) ProcessPosition(State s, (int x, int y) p)
    {
        if (p == s.Start) p = (p.x - s.StartDir.x, p.y - s.StartDir.y);
        else if (p == s.End) p = (p.x - s.EndDir.x, p.y - s.EndDir.y);
        int x = p.x < 0 ? 0 : System.Math.Min(p.x, s.PfRows - 1);
        int y = p.y < 0 ? 0 : System.Math.Min(p.y, s.PfCols - 1);
        return (x, y);
    }

    private static bool InPf(State s, int x, int y) => x >= 0 && y >= 0 && x < s.PfRows && y < s.PfCols;

    private static bool NodeOk(State s, int x, int y)
    {
        if (!InPf(s, x, y) || CollidesWithStartOrEnd(s, x, y)) return false;
        if (s.CustomUnwalkable.Contains((x, y))) return false;
        return s.Map[x, y] != Wall;
    }

    private static bool CollidesWithStartOrEnd(State s, int x, int y)
    {
        foreach (var (nx, ny) in new[] { (x - 1, y), (x, y - 1), (x, y + 1), (x + 1, y) })
        {
            if (!InPf(s, nx, ny)) continue;
            int v = s.Map[nx, ny];
            if (v == StartTile && (s.Start.x - x, s.Start.y - y) != s.StartDir) return true;
            if (v == EndTile && (s.End.x - x, s.End.y - y) != s.EndDir) return true;
        }
        return false;
    }
}

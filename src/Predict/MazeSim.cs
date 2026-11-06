using System.Collections.Generic;

namespace TellMeCosmetics.Predict;

// Port of MazeDataGenerator.DataFromDimensions and its callees (GameAssembly RVA 0x892da0 .. 0x8957b0).
// UnityRandom / NetRandom are C# copies of the game's RNGs (GameRandom.cs), so sequences match bit for bit.
public static class MazeSim
{
    public sealed class Sub { public float Chance; public int RadiusMin, RadiusMax; public float RadiusMaxMapPercent; }

    public sealed class Input
    {
        public int Seed, SecondSeed, MazeType, Rows, Cols, MapX, MapY, MinStartToEnd, MinEdge;
        public float Threshold;
        public Sub Sub; // mutated like the game's SubGenerationSettings
        public Result Prev; // previous attempt: the game keeps its end point when no new one is found
    }

    public sealed class Result
    {
        public int[,] Map;
        public (int x, int y) Start, StartDir, End, EndDir;
        public int StartEuler, EndEuler;
    }

    private const int MazeD = 3, MazeN = 13; // MazeType.Maze_D uses Kruskal, Maze_N also allows walls at edges
    private const int Walkable = 0, Wall = 1;

    private sealed class Cell
    {
        public int SetID;
        public List<Cell> Cells;
        public Cell(int id) { SetID = id; Cells = new List<Cell> { this }; }
    }

    private sealed class PieceConfig { public bool MoveOnX, StartFromZero; public int StartPos; public List<int> Allowed; }

    public static Result DataFromDimensions(Input inp)
    {
        var res = new Result();
        if (inp.Prev != null) { res.End = inp.Prev.End; res.EndDir = inp.Prev.EndDir; res.EndEuler = inp.Prev.EndEuler; }
        var ur = new UnityRandom(inp.Seed); // UnityEngine.Random.InitState(inp.Seed)
        if (inp.MazeType == MazeD)
        {
            res.Map = Kruskal(new NetRandom(inp.Seed), inp.Cols, inp.Rows);
        }
        else
        {
            res.Map = new int[inp.MapX, inp.MapY];
            GenerateMaze(res.Map, inp.Threshold, ur);
            InsertSubGenerations(res.Map, inp);
        }

        var startCfg = PieceCfg(inp, true, ur);
        res.StartEuler = 0;
        var dir = (0, 0);
        EdgePoint(res.Map, inp, startCfg, ref res.StartEuler, out res.Start, ref dir, false, default, 5);
        res.StartDir = dir;

        for (int i = 0; i < 20; i++)
        {
            var endCfg = PieceCfg(inp, false, ur);
            int e = 0;
            var d = (0, 0);
            if (EdgePoint(res.Map, inp, endCfg, ref e, out var p, ref d, true, res.Start, inp.MinStartToEnd))
            {
                res.End = p;
                res.EndDir = d;
                res.EndEuler = e == 0 ? 180 : e != 180 ? -e : 0;
                break;
            }
        }
        return res;
    }

    private static void GenerateMaze(int[,] m, float threshold, UnityRandom ur)
    {
        int rMax = m.GetLength(0) - 1, cMax = m.GetLength(1) - 1;
        for (int i = 0; i <= rMax; i++)
            for (int j = 0; j <= cMax; j++)
            {
                if (i == 0 || j == 0 || i == rMax || j == cMax) { m[i, j] = 1; continue; }
                if (((i | j) & 1) != 0 || !(ur.Value > threshold)) continue;
                m[i, j] = 1;
                int a, b;
                if (ur.Value >= 0.5f) { a = ur.Value >= 0.5f ? 1 : -1; b = 0; }
                else { a = 0; b = ur.Value >= 0.5f ? 1 : -1; }
                m[i + a, j + b] = 1;
            }
    }

    private static void InsertSubGenerations(int[,] m, Input inp)
    {
        var s = inp.Sub;
        if (s == null) return;
        if (0f < s.RadiusMaxMapPercent)
        {
            int v = (int)((float)(inp.Cols + inp.Rows) * 0.5f * s.RadiusMaxMapPercent);
            if (s.RadiusMax > v) s.RadiusMax = v;
        }
        if (s.RadiusMax < s.RadiusMin) { s.Chance = 0; s.RadiusMin = s.RadiusMax; }

        var rnd = new NetRandom(unchecked(inp.SecondSeed + 0x69d));
        if (!(rnd.NextDouble() <= s.Chance)) return;

        int lim = System.Math.Min(inp.Rows, inp.Cols) / 2 - 1;
        int r = rnd.Next(s.RadiusMin, s.RadiusMax);
        r = r < 0 ? 0 : r > lim ? lim : r;
        int size = r * 2 + 2;
        var sub = Kruskal(rnd, size, size);
        int cx = rnd.Next(r + 1, inp.Rows - r - 1);
        int cy = rnd.Next(r + 1, inp.Cols - r - 1);
        for (int k = 0; -r + k < r; k++)
            for (int j = -r; j < r; j++)
            {
                int x = cx - r + k, y = cy + j;
                if (y >= 0 && x >= 0 && y < inp.Cols && x < inp.Rows) Set(m, x, y, sub[r + 1 + j, k + 1]);
            }
    }

    // MazeMap.set_Item silently ignores out-of-range writes.
    private static void Set(int[,] m, int x, int y, int v)
    {
        if ((uint)x < (uint)m.GetLength(0) && (uint)y < (uint)m.GetLength(1)) m[x, y] = v;
    }

    private static int[,] Kruskal(NetRandom rnd, int w, int h)
    {
        var maze = new int[h, w];
        var cells = new Cell[h, w];
        var walls = new List<(int x, int y)>(w * h / 2);
        int id = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                maze[y, x] = 1;
                if ((x & y & 1) == 0) continue;
                maze[y, x] = 0;
                cells[y, x] = new Cell(++id);
                if (x + 1 < w) walls.Add((x + 1, y));
                if (y + 1 < h) walls.Add((x, y + 1));
            }

        Cell At(int x, int y) => x >= 0 && x < w && y >= 0 && y < h ? cells[y, x] : null;
        while (walls.Count > 0)
        {
            int i = rnd.Next(walls.Count);
            var (wx, wy) = walls[i];
            walls.RemoveAt(i);
            Join(At(wx - 1, wy), At(wx + 1, wy), wx, wy);
            Join(At(wx, wy - 1), At(wx, wy + 1), wx, wy);
        }
        return maze;

        void Join(Cell a, Cell b, int wx, int wy)
        {
            if (a == null || b == null || a.SetID == b.SetID) return;
            maze[wy, wx] = 0;
            var to = b.Cells;
            foreach (var c in a.Cells.ToArray()) { to.Add(c); c.SetID = b.SetID; c.Cells = to; }
        }
    }

    private static PieceConfig PieceCfg(Input inp, bool isStart, UnityRandom ur)
    {
        var cfg = new PieceConfig { Allowed = new List<int> { Walkable } };
        if (inp.MazeType == MazeD)
        {
            cfg.MoveOnX = inp.Rows < inp.Cols;
            cfg.StartFromZero = isStart;
            cfg.StartPos = isStart ? 0 : inp.Rows < inp.Cols ? inp.Cols : inp.Rows;
            return cfg;
        }
        if (inp.MazeType == MazeN) cfg.Allowed.Add(Wall);
        cfg.MoveOnX = ur.Value > 0.5f;
        cfg.StartFromZero = ur.Value > 0.5f;
        int pos = cfg.MoveOnX ? inp.Cols : inp.Rows;
        float v = ur.Value;
        cfg.StartPos = v < 0.33f ? 0 : v < 0.66f ? pos / 2 : pos;
        return cfg;
    }

    private static bool EdgePoint(int[,] m, Input inp, PieceConfig cfg, ref int euler, out (int x, int y) point, ref (int x, int y) dir,
        bool hasExcl, (int x, int y) excl, int minDist)
    {
        dir = (0, 0);
        int maxDim = System.Math.Max(inp.Rows, inp.Cols), edge = inp.MinEdge;
        var xs = new int[2];
        var ys = new int[2];
        int Clamp(int a, int size) => a < edge ? edge : System.Math.Min(a, size - edge);
        bool In(int x, int y) => x >= 0 && y >= 0 && x < inp.Rows && y < inp.Cols;
        int Tile(int x, int y) => (uint)x < (uint)m.GetLength(0) && (uint)y < (uint)m.GetLength(1) ? m[x, y] : 0;

        bool CheckTile(int x, int y)
        {
            if (!In(x, y) || !cfg.Allowed.Contains(Tile(x, y))) return false;
            if (hasExcl && System.Math.Abs(excl.x - x) <= minDist) return minDist < System.Math.Abs(excl.y - y);
            return true;
        }

        bool DirValid((int x, int y) d, int px, int py)
        {
            for (int i = -5; i <= 5; i++)
            {
                int x = px + d.x + d.y * i, y = py + d.y + d.x * i;
                if (In(x, y) && cfg.Allowed.Contains(Tile(x, y))) return false;
            }
            return true;
        }

        int line = maxDim;
        for (int pass = 0; pass <= 1; pass++)
        {
            line--;
            int lineIdx = cfg.StartFromZero ? pass : line;
            for (int k = 0; k < maxDim; k++)
            {
                int a = cfg.StartPos - k, b = cfg.StartPos + k;
                if (!cfg.MoveOnX) { ys[0] = Clamp(a, inp.Cols); ys[1] = Clamp(b, inp.Cols); xs[0] = xs[1] = lineIdx; }
                else { xs[0] = Clamp(a, inp.Rows); xs[1] = Clamp(b, inp.Rows); ys[0] = ys[1] = lineIdx; }
                for (int t = 0; t < 2; t++)
                {
                    int px = xs[t], py = ys[t];
                    if (!CheckTile(px, py)) continue;
                    int s = cfg.StartFromZero ? -1 : 1;
                    if (!cfg.MoveOnX) { px += s; euler = s * -90; dir = (s, 0); }
                    else { py += s; euler = (s + 1) * 90; dir = (0, s); }
                    if (In(px, py) && DirValid(dir, px, py)) { point = (px, py); return true; }
                }
            }
        }
        euler = 0;
        point = (0, 0);
        return false;
    }
}

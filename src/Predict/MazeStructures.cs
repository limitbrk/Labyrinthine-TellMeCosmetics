using System.Collections.Generic;

namespace TellMeCosmetics.Predict;

// Port of MazeGenerator.InsertStructuresWithFewTries and its callees (GameAssembly RVA 0x818410 .. 0x895f50).
public static class MazeStructures
{
    private const int Walkable = 0, Wall = 1, Landmark = 2, LandmarkBorder = 4;

    public sealed class Structure
    {
        public int DimX, DimY, SubmazeMask, PrefabId;
        public int[] Border;               // null/empty → every border tile is LandmarkBorder
        public (int x, int y)[] NonWalkable;
        public string[] Objectives;        // supportedObjectives names, empty = supports every objective
        public bool SupportsObjectives;    // RndObjectiveManager.ContainsObjective result, fixed per contract
        public bool SupportsSubmaze(int type) { int bit = 1 << (type & 31); return (SubmazeMask & bit) == bit; }
        public int BorderTile(int i, int j)
        {
            if (Border == null || Border.Length == 0) return LandmarkBorder;
            int idx = i + j * DimX;
            return idx >= 0 && idx < Border.Length ? Border[idx] : LandmarkBorder;
        }
    }

    public sealed class Placed { public Structure S; public int X, Y; }

    // Generator fields InsertStructures reads and mutates; they persist across attempts like on MazeGenerator.
    public sealed class Settings { public int StructureAttempts, EdgeDist, OtherDist; public bool CanChangeDist; }

    public static int InsertWithFewTries(MazeSubmazes.State s, Settings cfg, List<Placed> spawned, Structure[] arr, int count,
        Il2CppSystem.Random rnd, List<Placed> outList, bool addRandom, bool checkSize)
    {
        for (int t = 0; t <= 2; t++)
        {
            var tmp = new List<Placed>();
            int n = Insert(s, cfg, spawned, arr, count, rnd, tmp, addRandom, checkSize);
            if (n > 0) { outList.AddRange(tmp); return n; }
        }
        return 0;
    }

    private static int[] RandomIndexArrayDistinct(MazeSubmazes.State s, Settings cfg, Structure[] arr, int count, Il2CppSystem.Random rnd, bool addRandom, bool checkSize)
    {
        var result = new int[count];
        var avail = new List<int>(arr.Length);
        int limit = (int)((float)(System.Math.Min(s.MazeX, s.MazeY) + cfg.EdgeDist * -2) * 0.4f);
        for (int i = 0; i < arr.Length; i++)
            if (!checkSize || (arr[i] != null && arr[i].DimX <= limit && arr[i].DimY <= limit)) avail.Add(i);
        if (avail.Count == 0) return new int[0];

        int k = 0;
        while (k < count)
        {
            if (avail.Count == 0)
            {
                if (!addRandom) { System.Array.Resize(ref result, k); return result; }
                result[k++] = rnd.Next(0, arr.Length);
                continue;
            }
            int r = rnd.Next(0, avail.Count);
            int idx = avail[r];
            // The game's list extension swaps with the last element before removing it, which changes the order.
            avail[r] = avail[^1];
            avail.RemoveAt(avail.Count - 1);
            if (arr[idx] != null && arr[idx].SupportsObjectives) result[k++] = idx;
        }
        return result;
    }

    private static int Insert(MazeSubmazes.State s, Settings cfg, List<Placed> spawned, Structure[] arr, int count,
        Il2CppSystem.Random rnd, List<Placed> outList, bool addRandom, bool checkSize)
    {
        if (arr == null || arr.Length == 0) return 0;
        var backup = (int[,])s.Map.Clone();
        var idx = RandomIndexArrayDistinct(s, cfg, arr, count, rnd, addRandom, checkSize);
        if (idx.Length == 0) return 1;

        int placed = 0, sizeX = s.Map.GetLength(0), sizeY = s.Map.GetLength(1);
        foreach (int i in idx)
        {
            var st = arr[i];
            int attempts = cfg.StructureAttempts;
            while (true)
            {
                if (attempts < 0)
                {
                    if (!cfg.CanChangeDist) break;
                    attempts = cfg.StructureAttempts;
                    cfg.CanChangeDist = false;
                    cfg.OtherDist /= 2;
                }
                int min = cfg.EdgeDist + 1, maxX = sizeX - cfg.EdgeDist - st.DimX, maxY = sizeY - cfg.EdgeDist - st.DimY;
                if (!(min < maxX && min < maxY)) { attempts--; continue; }
                int x = rnd.Next(min, maxX), y = rnd.Next(min, maxY);
                if (Overlaps(s, cfg, x, y, st)) { attempts--; continue; }

                System.Array.Copy(s.Map, backup, s.Map.Length);
                for (int a = 0; a < st.DimX; a++)
                    for (int b = 0; b < st.DimY; b++)
                    {
                        int px = x + a, py = y + b;
                        if (a < 1 || a >= st.DimX - 1 || b < 1 || b >= st.DimY - 1)
                        {
                            int bt = st.BorderTile(a, b);
                            if (bt == Walkable) { Set(s, px, py, LandmarkBorder); ConnectNeighbour(s, x, y, st, px, py, 1); }
                            else Set(s, px, py, bt);
                        }
                        else Set(s, px, py, Landmark);
                    }

                var closest = ClosestLandmark(s, spawned, x, y);
                if (MazeSubmazes.CanReachPoint(s, closest, (x + 1, y + 1)))
                {
                    outList.Add(new Placed { S = st, X = x, Y = y });
                    placed++;
                    break;
                }
                for (int a = x - 10; a <= st.DimX + 10 + x; a++)
                    for (int b = y - 10; b <= y + 10 + st.DimY; b++)
                        if (a >= 0 && b >= 0 && a < s.MazeX && b < s.MazeY) s.Map[a, b] = backup[a, b];
                attempts--;
            }
        }
        return placed;
    }

    // MazeMap.set_Item silently ignores out-of-range writes.
    private static void Set(MazeSubmazes.State s, int x, int y, int v)
    {
        if ((uint)x < (uint)s.Map.GetLength(0) && (uint)y < (uint)s.Map.GetLength(1)) s.Map[x, y] = v;
    }

    private static bool InMaze(MazeSubmazes.State s, int x, int y) => x >= 0 && y >= 0 && x < s.MazeX && y < s.MazeY;

    private static bool Overlaps(MazeSubmazes.State s, Settings cfg, int x, int y, Structure st)
    {
        if (!(st.DimX + x < s.MazeX) || !(y + st.DimY < s.MazeY)) return true;
        int d = cfg.OtherDist;
        for (int i = x - d; i < d + x + st.DimX; i++)
            for (int j = y - d; j < d + y + st.DimY; j++)
            {
                bool inner = x - 1 <= i && i < x + st.DimX + 1 && y - 1 <= j && j < y + 1 + st.DimY;
                if (!InMaze(s, i, j)) { if (inner) return true; continue; }
                if (inner && !st.SupportsSubmaze(s.Types[i, j])) return true;
                if ((uint)s.Map[i, j] > 1) return true;
            }
        return false;
    }

    private static (int x, int y) ClosestLandmark(MazeSubmazes.State s, List<Placed> spawned, int x, int y)
    {
        var best = s.Start;
        float fx = s.Start.x - x, fy = s.Start.y - y;
        float bestD = fx * fx + fy * fy;
        foreach (var p in spawned)
        {
            float dx = p.X - x, dy = p.Y - y;
            float d = dx * dx + dy * dy;
            if (d < bestD) { best = (p.X + 1, p.Y + 1); bestD = d; }
        }
        return best;
    }

    private static bool InRect(int x, int y, int rx, int ry, Structure st) => x >= rx && x < rx + st.DimX && y >= ry && y < ry + st.DimY;

    private static void ConnectNeighbour(MazeSubmazes.State s, int rx, int ry, Structure st, int px, int py, int depth)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = px + dx, ny = py + dy;
                if (!InMaze(s, nx, ny) || InRect(nx, ny, rx, ry, st)) continue;
                if (depth > 0) ConnectNeighbour(s, rx, ry, st, nx, ny, depth - 1);
                int v = s.Map[nx, ny];
                if (v != Walkable && v != LandmarkBorder) continue;
                if (CheckPos(s, rx, ry, st, px, py, nx, py)) return;
                if (CheckPos(s, rx, ry, st, px, py, px, py + (ny - py))) return;
            }
    }

    private static bool CheckPos(MazeSubmazes.State s, int rx, int ry, Structure st, int ox, int oy, int x, int y)
    {
        if (x == ox && y == oy) return false;
        if (s.Transitions != null)
            for (int i = x - 2; i <= x + 2; i++)
                for (int j = y - 2; j <= y + 2; j++)
                    if (InMaze(s, i, j) && s.Types[i, j] != s.Types[x, y]) return false;
        if (InRect(x, y, rx, ry, st)) return false;
        s.Map[x, y] = Walkable;
        return true;
    }
}

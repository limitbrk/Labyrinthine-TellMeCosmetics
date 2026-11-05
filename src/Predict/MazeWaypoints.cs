using System.Collections.Generic;

namespace TellMeCosmetics.Predict;

using Placed = MazeStructures.Placed;

// Port of AIWaypointsGenerator.GenerateWaypoints with skipNavMeshChecks (GameAssembly RVA 0x641e30 .. 0x6432a0).
// GenerateNewMaze fails the attempt when a generator yields fewer than 80% of its desired waypoint count.
public static class MazeWaypoints
{
    public struct Vec3
    {
        public float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        // Vector3.Distance as compiled: (dy*dy + dx*dx) + dz*dz, sqrt in double.
        public static float Distance(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)System.Math.Sqrt(dy * dy + dx * dx + dz * dz);
        }
    }

    public sealed class Generator
    {
        public int CheckRange, PlaceMinDistSafezone, ValidTile, SizeMin, SizeMax, CountMin, CountMax;
        public float FromStart, FromSafezone, FromPuzzle, Mult;
    }

    public sealed class Context
    {
        public MazeSubmazes.State S;
        public List<Placed> Puzzles;
        public Vec3[] PatrolPoints;   // FindObjectsOfType<AIPatrolPoint>() positions at generation time
        public float GlobalDist;      // BehaviorDesigner global float read by GenerateWaypoints
        public int Tile;              // MazeGenerator.TILE_SIZE
    }

    public static int Desired(Generator g, int mazeX, int mazeY)
    {
        float f = ((float)mazeX * g.Mult + (float)mazeY * g.Mult) * 0.5f;
        float v = ((f - (float)g.SizeMin) / (float)(g.SizeMax - g.SizeMin)) * ((float)g.CountMax - (float)g.CountMin) + (float)g.CountMin;
        return (int)System.Math.Ceiling((double)v);
    }

    private static Vec3 GridToWorld(Context c, int x, int y) => new((float)c.Tile * (float)x, 0f, (float)c.Tile * (float)y);
    private static (int x, int y) WorldToGrid(Context c, float x, float z) => ((int)(x / (float)c.Tile), (int)(z / (float)c.Tile));
    private static bool InMaze(Context c, int x, int y) => x >= 0 && y >= 0 && x < c.S.MazeX && y < c.S.MazeY;

    public static List<Vec3> Generate(Generator g, Context c)
    {
        var wps = new List<Vec3>(32);
        int maxDistTiles = (int)(c.GlobalDist / (float)c.Tile);
        int desired = Desired(g, c.S.MazeX, c.S.MazeY);
        int target = (int)System.Math.Ceiling((double)((float)desired * 0.6f));
        float sp = (float)(c.S.MazeY * c.S.MazeX / desired);
        int spacingCap = (int)(sp < 0 ? float.NaN : System.MathF.Sqrt(sp));
        int spacing = (maxDistTiles <= spacingCap ? maxDistTiles : spacingCap) + 1;
        while (wps.Count < target && 1 < spacing)
        {
            spacing--;
            for (int x = 0; x < c.S.MazeX; x += spacing)
                for (int y = 0; y < c.S.MazeY; y += spacing)
                    CheckSquare(g, c, x, y, wps, spacing);
        }
        AddExtraWaypoints(g, c, wps, 3, spacing);

        float minKeep = (float)Desired(g, c.S.MazeX, c.S.MazeY) * 0.8f;
        float maxSqr = c.GlobalDist * c.GlobalDist;
        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < wps.Count; i++)
            {
                if (wps.Count <= (int)minKeep) return wps;
                if (AnyWaypointInRange(g, c, wps[i], wps, maxSqr)) continue;
                // The game's list extension swaps with the last element before removing it.
                wps[i] = wps[^1];
                wps.RemoveAt(wps.Count - 1);
                i--;
                changed = true;
            }
        } while (changed);
        return wps;
    }

    // Spiral over rings 0..3 around (X,Y), dx then dy ascending, only cells on the ring.
    private static bool CheckSquare(Generator g, Context c, int X, int Y, List<Vec3> wps, int spacing)
    {
        for (int r = 0; r <= 3; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    if ((r <= System.Math.Abs(dx) || r <= System.Math.Abs(dy)) && TryToAddWaypoint(g, c, X + dx, Y + dy, wps, spacing))
                        return true;
        return false;
    }

    private static bool TryToAddWaypoint(Generator g, Context c, int x, int y, List<Vec3> wps, int spacing)
    {
        var s = c.S;
        if (x < 0 || y < 0 || x >= s.MazeX || y >= s.MazeY) return false;
        if (s.Map[x, y] != g.ValidTile) return false;
        var pos = GridToWorld(c, x, y);

        int r = g.PlaceMinDistSafezone;
        for (int i = x - r; i <= r + x; i++)
            for (int j = y - r; j <= y + r; j++)
                if (InMaze(c, i, j)
                    && ((float)x - (float)i) * ((float)x - (float)i) + ((float)y - (float)j) * ((float)y - (float)j) <= (float)(r * r)
                    && s.Map[i, j] == 3) return false;

        float minDist = (float)(c.Tile * spacing) * 0.9f;
        foreach (var p in c.PatrolPoints) if (Vec3.Distance(p, pos) < minDist) return false;
        foreach (var w in wps) if (Vec3.Distance(w, pos) < minDist) return false;
        // waypointBlockers is filled later by PopulateMaze, so it is empty during generation.
        if (!MazeSubmazes.CanReachStartOrEnd(s, (x, y))) return false;
        wps.Add(pos);
        return true;
    }

    private static void AddExtraWaypoints(Generator g, Context c, List<Vec3> wps, int connections, int spacing)
    {
        int n = wps.Count;
        var queue = new Queue<Vec3>();
        for (int i = 0; i < n; i++)
        {
            GetFurthestWaypoints(wps[i], wps, queue, connections);
            foreach (var q in queue.ToArray())
            {
                var w = wps[i];
                float mz = (q.Z - w.Z) * 0.5f + w.Z;
                float mx = (q.X - w.X) * 0.5f + w.X;
                var gp = WorldToGrid(c, mx, mz);
                CheckSquare(g, c, gp.x, gp.y, wps, spacing);
            }
        }
    }

    private static void GetFurthestWaypoints(Vec3 pos, List<Vec3> wps, Queue<Vec3> outQ, int k)
    {
        outQ.Clear();
        float best = 0f;
        for (int i = 0; i < wps.Count; i++)
        {
            if (!(best < Vec3.Distance(pos, wps[i]))) continue;
            outQ.Enqueue(wps[i]);
            if (k < outQ.Count) outQ.Dequeue();
            best = (float)i; // the game stores the index, not the distance
        }
    }

    private static bool AnyWaypointInRange(Generator g, Context c, Vec3 pos, List<Vec3> wps, float maxSqr)
    {
        for (int i = wps.Count - 1; i >= 0; i--)
        {
            var w = wps[i];
            float a = w.Y - pos.Y, b = w.X - pos.X, d = w.Z - pos.Z;
            float e = pos.Y - w.Y, f = pos.X - w.X, h = pos.Z - w.Z;
            if (1e-10f <= a * a + b * b + d * d && e * e + f * f + h * h <= maxSqr)
                return !ConnectionIntersectsAnySafezone(g, c, pos, w, g.CheckRange);
        }
        return false;
    }

    private static float GetCheckRange(Generator g, Context c, int x, int y, out float threshold)
    {
        threshold = 0f;
        int t = c.S.Map[x, y];
        if (t == MazeSubmazes.StartTile) return g.FromStart;
        if (t == 3) { threshold = 1.5f; return g.FromSafezone; }
        if (t != 2) return -1f;
        threshold = 0.5f;
        foreach (var p in c.Puzzles)
            if (x >= p.X && y >= p.Y && x <= p.X + p.S.DimX - 1 && y <= p.Y + p.S.DimY - 1) return g.FromPuzzle;
        return -1f;
    }

    private static bool ConnectionIntersectsAnySafezone(Generator g, Context c, Vec3 a, Vec3 b, int range)
    {
        if (range <= 0) return false;
        var ga = WorldToGrid(c, a.X, a.Z);
        var gb = WorldToGrid(c, b.X, b.Z);
        int maxX = System.Math.Max(ga.x, gb.x), minY = System.Math.Min(ga.y, gb.y), maxY = System.Math.Max(ga.y, gb.y);
        int minX = gb.x <= ga.x ? gb.x : ga.x;
        float threshold = 0f, sum = 0f;
        var corners = new[] { a, b };
        for (int x = minX - range; x <= maxX + range; x++)
            for (int y = minY - range; y <= maxY + range; y++)
            {
                if (!InMaze(c, x, y)) continue;
                float w = GetCheckRange(g, c, x, y, out threshold);
                if (!(0f < w)) continue;
                var cell = GridToWorld(c, x, y);
                foreach (var corner in corners)
                {
                    float d = Vec3.Distance(corner, cell);
                    if (d < (float)c.Tile * w) sum += 1f - d / ((float)c.Tile * w);
                    if (threshold < sum) return true;
                }
                if (threshold < sum) return true;
            }
        return false;
    }
}

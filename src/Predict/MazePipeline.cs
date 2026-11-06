using MelonLoader;
using MelonLoader.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TellMeCosmetics.Predict;

using Structure = MazeStructures.Structure;
using Placed = MazeStructures.Placed;

// Mirrors MazeGenerator.Awake → GenerateMap → CreateMaze → GenerateNewMaze to find how many maze attempts fail,
// which decides the seeds RndCustomizationItemsManager sees. Scene data is captured per maze type and cached to disk.
public static class MazePipeline
{
    public sealed class Submaze { public int RadiusMin, RadiusMax, SpawnTries; public float Chance; public bool CanPlaceSafezones; }
    public sealed class ObjectiveSet { public int MinMapSize; public string[] Objectives; }
    public sealed class LandmarkObjective { public Structure[] Landmarks; public int Min; }

    public sealed class Config
    {
        public int MazeType;
        public int SubmazeTries, StructureAttempts, EdgeDist, MinRequired, MinDistStartEnd, MinDistEdge;
        public bool CanChangeDist, RequireAtLeastOneLandmark;
        public float SubChance, SubPct;
        public int SubRMin, SubRMax;
        public Submaze[] Subs;
        public int SafeCells, SafeMinSpawned, SafeMinTried, SafeDistStart, SafeDistLandmark, SafeDistOthers, GridSize, GridSpacing;
        public bool MoveSafezonesForward;
        public Structure[] Required, Puzzles, Landmarks;
        public ObjectiveSet[] Sets;
        public Dictionary<string, LandmarkObjective> LandmarkObjectives = new();
        public int Tile;                              // MazeGenerator.TILE_SIZE
        public float MaxWaypointsDistance;            // BehaviorDesigner global "max_waypoints_distance"
        public MazeWaypoints.Generator[] Waypoints;   // MazeGenerator.aiWaypointsGenerators
    }

    public sealed class ContractInput { public int Seed, SecondSeed, MazeType, MapX, MapY, MapSize, LandmarkCount, PuzzleCount; }

    public sealed class Outcome { public int Seed, SecondSeed, FailedAttempts; }

    // ---------- objective set (RndObjectiveManager.SelectRandomObjectiveSet) ----------

    public static int SelectObjectiveSet(Config cfg, ContractInput c)
    {
        if (cfg.Sets == null || cfg.Sets.Length == 0) return -1;
        var seeder = new NetRandom(unchecked(c.SecondSeed + 0x1529));
        var rnd = new NetRandom(seeder.Next(100000000, 999999999));
        var left = Enumerable.Range(0, cfg.Sets.Length).ToList();
        while (left.Count > 0)
        {
            int i = rnd.Next(left.Count);
            var set = cfg.Sets[left[i]];
            if (set != null && set.MinMapSize <= c.MapSize) return left[i];
            left.RemoveAt(i);
        }
        return rnd.Next(cfg.Sets.Length);
    }

    // C# float→int casts on .NET 9+ saturate; il2cpp's cvttss2si returns int.MinValue for NaN/overflow.
    private static int CastInt(float f) => float.IsNaN(f) || f >= 2147483648f || f < -2147483648f ? int.MinValue : (int)f;

    // ---------- full GenerateMap ----------

    private static readonly object simGate = new(); // Run marks Structure.SupportsObjectives on the shared config, so one simulation at a time
    public static Outcome Simulate(Config cfg, ContractInput c)
    {
        lock (simGate) return Run(cfg, c);
    }

    private static Outcome Run(Config cfg, ContractInput c)
    {
        int seed = c.Seed, second = c.SecondSeed, mazeX = c.MapX, mazeY = c.MapY;
        int landmarkCount = Math.Min(c.LandmarkCount, cfg.Landmarks?.Length ?? 0), puzzleCount = c.PuzzleCount;
        var settings = new MazeStructures.Settings
        {
            StructureAttempts = cfg.StructureAttempts, EdgeDist = cfg.EdgeDist, CanChangeDist = cfg.CanChangeDist,
            OtherDist = CastInt(MathF.Sqrt((float)(mazeY * mazeX) / ((float)(landmarkCount + puzzleCount) * 2.5f))),
        };

        // Objective set decides which structures may spawn and which landmarks are required.
        int setIdx = SelectObjectiveSet(cfg, c);
        var active = setIdx >= 0 ? new HashSet<string>(cfg.Sets[setIdx].Objectives) : new HashSet<string>();
        foreach (var st in AllStructures(cfg)) st.SupportsObjectives = st.Objectives == null || st.Objectives.Length == 0 || st.Objectives.Any(active.Contains);

        var required = (cfg.Required ?? new Structure[0]).ToList();
        int minRequired = cfg.MinRequired < 1 ? required.Count : cfg.MinRequired;
        var extra = new List<Structure>();
        int extraMin = 0;
        if (setIdx >= 0)
            foreach (var name in cfg.Sets[setIdx].Objectives)
                if (cfg.LandmarkObjectives.TryGetValue(name, out var lo)) { extra.AddRange(lo.Landmarks); extraMin += lo.Min; }
        foreach (var st in extra.Where(s => s != null)) st.SupportsObjectives = st.Objectives == null || st.Objectives.Length == 0 || st.Objectives.Any(active.Contains);
        if (extra.Count > 0) { required.AddRange(extra); minRequired += extraMin; }
        var requiredArr = required.ToArray();

        var sub = new MazeSim.Sub { Chance = cfg.SubChance, RadiusMin = cfg.SubRMin, RadiusMax = cfg.SubRMax, RadiusMaxMapPercent = cfg.SubPct };
        var safe = new MazeChecks.Safezones
        {
            Cells = cfg.SafeCells, MinSpawned = cfg.SafeMinSpawned, MinTried = cfg.SafeMinTried, DistStart = cfg.SafeDistStart,
            DistLandmark = cfg.SafeDistLandmark, DistOthers = cfg.SafeDistOthers, MoveForward = cfg.MoveSafezonesForward,
            GridSize = cfg.GridSize, GridSpacing = cfg.GridSpacing, ReduceMapSize = ReduceMapSize(c.MazeType),
            SubCanPlace = cfg.Subs?.Select(x => x.CanPlaceSafezones).ToArray(),
        };
        var subs = cfg.Subs?.Select(x => new MazeSubmazes.Submaze { RadiusMin = x.RadiusMin, RadiusMax = x.RadiusMax, SpawnTries = x.SpawnTries, Chance = x.Chance }).ToArray();

        // State the game keeps on MazeGenerator/MazeDataGenerator between attempts.
        var customUnwalkable = new HashSet<(int x, int y)>();
        MazeSim.Result prev = null;
        int[,] types = null;
        Dictionary<(int x, int y), MazeSubmazes.Trans> transitions = null;
        int failed = 0, initialSeed = seed;

        for (int batch = 0; batch < 6; batch++)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var data = MazeSim.DataFromDimensions(new MazeSim.Input
                {
                    Seed = seed, SecondSeed = second, MazeType = c.MazeType, Rows = mazeX, Cols = mazeY, MapX = mazeX, MapY = mazeY,
                    MinStartToEnd = cfg.MinDistStartEnd, MinEdge = cfg.MinDistEdge, Threshold = 0.1f, Sub = sub, Prev = prev,
                });
                prev = data;
                var s = new MazeSubmazes.State
                {
                    MazeX = mazeX, MazeY = mazeY, SecondSeed = second, PfRows = mazeX, PfCols = mazeY, Map = data.Map,
                    Types = types ?? new int[mazeX, mazeY], Transitions = transitions, Subs = subs,
                    Start = data.Start, End = data.End, StartDir = data.StartDir, EndDir = data.EndDir, CustomUnwalkable = customUnwalkable,
                };
                bool ok = GenerateNewMaze(cfg, s, settings, safe, requiredArr, minRequired, landmarkCount, puzzleCount);
                types = s.Types;
                transitions = s.Transitions;
                if (ok) return new Outcome { Seed = seed, SecondSeed = second, FailedAttempts = failed };
                failed++;
                seed = unchecked(seed + 0x4a3);
                second = unchecked(second + 0x18c1);
            }
            seed = initialSeed;
            mazeX = Math.Min(mazeX + 5, 100);
            mazeY = Math.Min(mazeY + 5, 100);
            types = null; // shortcut: the game keeps the old-size mazeTypeMap until SetupSubmazes reallocates it, which it always does first
        }
        return null;
    }

    private static IEnumerable<Structure> AllStructures(Config cfg) =>
        new[] { cfg.Required, cfg.Puzzles, cfg.Landmarks }.Where(a => a != null).SelectMany(a => a).Where(s => s != null);

    // Contract.ShouldReduceMapSize: Maze_K/O/Q → 0.5, Maze_R → 0.8.
    private static float ReduceMapSize(int mazeType) => mazeType switch { 10 or 14 or 17 => 0.5f, 18 => 0.8f, _ => -1f };

    private static bool GenerateNewMaze(Config cfg, MazeSubmazes.State s, MazeStructures.Settings settings, MazeChecks.Safezones safe,
        Structure[] required, int minRequired, int landmarkCount, int puzzleCount)
    {
        if ((uint)s.Start.x < (uint)s.Map.GetLength(0) && (uint)s.Start.y < (uint)s.Map.GetLength(1)) s.Map[s.Start.x, s.Start.y] = MazeSubmazes.StartTile;
        if ((uint)s.End.x < (uint)s.Map.GetLength(0) && (uint)s.End.y < (uint)s.Map.GetLength(1)) s.Map[s.End.x, s.End.y] = MazeSubmazes.EndTile;

        var rnd = new NetRandom(s.SecondSeed);
        for (int i = 0; i < cfg.SubmazeTries; i++)
            if (MazeSubmazes.SetupSubmazes(s, rnd)) break;

        var w = new MazeChecks.World { S = s };
        if (required.Length != 0 && MazeStructures.InsertWithFewTries(s, settings, w.Spawned, required, required.Length, rnd, w.Spawned, false, false) < minRequired)
            return false;
        MazeStructures.InsertWithFewTries(s, settings, w.Spawned, cfg.Puzzles, puzzleCount, rnd, w.Puzzles, false, false);
        w.Spawned.AddRange(w.Puzzles);
        int landmarks = MazeStructures.InsertWithFewTries(s, settings, w.Spawned, cfg.Landmarks, landmarkCount, rnd, w.Spawned, true, true);
        if (cfg.RequireAtLeastOneLandmark && landmarkCount > 0 && landmarks == 0) return false;

        bool useTypes = s.Types != null;
        MazeChecks.RemoveInvalidTransitions(w);
        MazeChecks.FillCustomUnwalkable(w);
        if (!MazeChecks.EnsureExitIsReachable(w, useTypes)) return false;
        MazeChecks.RemoveUnreachableAreas(w);
        if (MazeChecks.RemoveUnreachableLandmarks(w))
        {
            while (MazeChecks.RemoveUnreachableLandmarks(w)) { }
            if (w.Spawned.Count == 0) return false;
        }
        MazeChecks.RemoveUnreachableAreas(w);
        MazeChecks.FillCustomUnwalkable(w);
        if (!MazeChecks.EnsureExitIsReachable(w, useTypes)) return false;
        if (!MazeChecks.RequiredLandmarksExist(w, required, minRequired)) return false;
        if (MazeChecks.InsertSafezones(w, safe) < cfg.SafeMinSpawned) return false;

        int walls = 0;
        for (int x = 0; x < s.MazeX; x++)
            for (int y = 0; y < s.MazeY; y++)
                if (s.Map[x, y] == 1) walls++;
        if (!(0.35f <= 1f - (float)walls / (float)(s.MazeY * s.MazeX))) return false;

        // AIWaypointsGenerator.GenerateWaypoints with skipNavMeshChecks; no AIPatrolPoint exists yet during generation.
        var ctx = new MazeWaypoints.Context { S = s, Puzzles = w.Puzzles, PatrolPoints = new MazeWaypoints.Vec3[0], GlobalDist = cfg.MaxWaypointsDistance, Tile = cfg.Tile };
        foreach (var gen in cfg.Waypoints ?? new MazeWaypoints.Generator[0])
            if (gen != null && (float)MazeWaypoints.Generate(gen, ctx).Count < (float)MazeWaypoints.Desired(gen, s.MazeX, s.MazeY) * 0.8f) return false;
        // shortcut: the final per-object check on MazeGenerator (field 0x68) is assumed to pass; no observed attempt failed there.
        return true;
    }

    // ---------- cache ----------

    private static readonly string cachePath = Path.Combine(MelonEnvironment.UserDataDirectory, "TellMeCosmetics.maze.bin");
    private static Dictionary<int, Config> configs;
    private static readonly object gate = new(); // ScenePreload stores from a background thread
    private const int CacheVersion = 3;

    public static Config Get(int mazeType)
    {
        lock (gate)
        {
            configs ??= Load();
            return configs.TryGetValue(mazeType, out var c) ? c : null;
        }
    }

    // replace=false keeps a config already cached for this game build (preload must not overwrite an in-game capture).
    public static void Store(Config cfg, bool replace = true)
    {
        lock (gate)
        {
            configs ??= Load();
            if (!replace && configs.ContainsKey(cfg.MazeType)) return;
            configs[cfg.MazeType] = cfg;
            try
            {
                using var s = File.Create(cachePath);
                Save(s, ScenePreload.Stamp, configs.Values);
            }
            catch (Exception ex) { MelonLogger.Warning($"Maze cache not saved: {ex.Message}"); return; }
            configs = null; // reload from disk so every prediction also exercises the cache round trip
        }
    }

    // A cache written by another game build reads as empty, so ScenePreload rebuilds it.
    private static Dictionary<int, Config> Load()
    {
        var result = new Dictionary<int, Config>();
        try
        {
            if (!File.Exists(cachePath)) return result;
            using var s = File.OpenRead(cachePath);
            return Parse(s, ScenePreload.Stamp);
        }
        catch (Exception ex) { MelonLogger.Warning($"Maze cache unreadable: {ex.Message}"); result.Clear(); }
        return result;
    }

    // Cache file format, shared with the baseline embedded in the mod. Another version or stamp parses as empty.
    internal static void Save(Stream s, string stamp, IEnumerable<Config> cs)
    {
        using var w = new BinaryWriter(s, System.Text.Encoding.UTF8, true);
        w.Write(CacheVersion);
        w.Write(stamp);
        w.Write(cs.Count());
        foreach (var c in cs) Write(w, c);
    }

    internal static Dictionary<int, Config> Parse(Stream s, string stamp)
    {
        var result = new Dictionary<int, Config>();
        using var r = new BinaryReader(s, System.Text.Encoding.UTF8, true);
        if (r.ReadInt32() != CacheVersion || r.ReadString() != stamp) return result;
        for (int n = r.ReadInt32(); n > 0; n--) { var c = Read(r); result[c.MazeType] = c; }
        return result;
    }

    // Persisted fields as "name=value" lines, PrefabIds renumbered by first use (instance IDs in game, a counter in ScenePreload).
    internal static string[] Dump(Config c)
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) Write(w, c);
        ms.Position = 0;
        var copy = Read(new BinaryReader(ms));
        var ids = new Dictionary<int, int>();
        foreach (var s in new[] { copy.Required, copy.Puzzles, copy.Landmarks }.Concat(copy.LandmarkObjectives.Values.Select(l => l.Landmarks))
            .Where(a => a != null).SelectMany(a => a).Where(s => s != null))
            s.PrefabId = s.PrefabId == 0 ? 0 : ids.TryGetValue(s.PrefabId, out var n) ? n : ids[s.PrefabId] = ids.Count + 1;
        return typeof(Config).GetFields().Select(f => $"{f.Name}={Text(f.GetValue(copy))}").ToArray();
    }

    private static string Text(object o) => o switch
    {
        null => "null",
        string s => s,
        float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        System.Collections.IDictionary d => "{" + string.Join(", ", d.Keys.Cast<object>().Select(k => $"{k}: {Text(d[k])}")) + "}",
        System.Collections.IEnumerable e => "[" + string.Join(", ", e.Cast<object>().Select(Text)) + "]",
        _ when o.GetType().IsPrimitive || o.GetType().IsEnum => Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture),
        _ => "{" + string.Join(" ", o.GetType().GetFields().Where(f => f.Name != nameof(Structure.SupportsObjectives)).Select(f => $"{f.Name}={Text(f.GetValue(o))}")) + "}",
    };

    private static void Write(BinaryWriter w, Config c)
    {
        foreach (int v in new[] { c.MazeType, c.SubmazeTries, c.StructureAttempts, c.EdgeDist, c.MinRequired, c.MinDistStartEnd, c.MinDistEdge,
            c.SubRMin, c.SubRMax, c.SafeCells, c.SafeMinSpawned, c.SafeMinTried, c.SafeDistStart, c.SafeDistLandmark, c.SafeDistOthers, c.GridSize, c.GridSpacing })
            w.Write(v);
        w.Write(c.CanChangeDist); w.Write(c.RequireAtLeastOneLandmark); w.Write(c.MoveSafezonesForward);
        w.Write(c.SubChance); w.Write(c.SubPct);
        WriteArr(w, c.Subs, x => { w.Write(x.RadiusMin); w.Write(x.RadiusMax); w.Write(x.SpawnTries); w.Write(x.Chance); w.Write(x.CanPlaceSafezones); });
        WriteStructs(w, c.Required); WriteStructs(w, c.Puzzles); WriteStructs(w, c.Landmarks);
        WriteArr(w, c.Sets, x => { w.Write(x.MinMapSize); WriteArr(w, x.Objectives, w.Write); });
        w.Write(c.LandmarkObjectives.Count);
        foreach (var kv in c.LandmarkObjectives) { w.Write(kv.Key); w.Write(kv.Value.Min); WriteStructs(w, kv.Value.Landmarks); }
        w.Write(c.Tile); w.Write(c.MaxWaypointsDistance);
        WriteArr(w, c.Waypoints, g =>
        {
            foreach (int v in new[] { g.CheckRange, g.PlaceMinDistSafezone, g.ValidTile, g.SizeMin, g.SizeMax, g.CountMin, g.CountMax }) w.Write(v);
            w.Write(g.FromStart); w.Write(g.FromSafezone); w.Write(g.FromPuzzle); w.Write(g.Mult);
        });
    }

    internal static Config Read(BinaryReader r)
    {
        var c = new Config
        {
            MazeType = r.ReadInt32(), SubmazeTries = r.ReadInt32(), StructureAttempts = r.ReadInt32(), EdgeDist = r.ReadInt32(), MinRequired = r.ReadInt32(),
            MinDistStartEnd = r.ReadInt32(), MinDistEdge = r.ReadInt32(), SubRMin = r.ReadInt32(), SubRMax = r.ReadInt32(), SafeCells = r.ReadInt32(),
            SafeMinSpawned = r.ReadInt32(), SafeMinTried = r.ReadInt32(), SafeDistStart = r.ReadInt32(), SafeDistLandmark = r.ReadInt32(),
            SafeDistOthers = r.ReadInt32(), GridSize = r.ReadInt32(), GridSpacing = r.ReadInt32(),
            CanChangeDist = r.ReadBoolean(), RequireAtLeastOneLandmark = r.ReadBoolean(), MoveSafezonesForward = r.ReadBoolean(),
            SubChance = r.ReadSingle(), SubPct = r.ReadSingle(),
        };
        c.Subs = ReadArr(r, () => new Submaze { RadiusMin = r.ReadInt32(), RadiusMax = r.ReadInt32(), SpawnTries = r.ReadInt32(), Chance = r.ReadSingle(), CanPlaceSafezones = r.ReadBoolean() });
        c.Required = ReadStructs(r); c.Puzzles = ReadStructs(r); c.Landmarks = ReadStructs(r);
        c.Sets = ReadArr(r, () => new ObjectiveSet { MinMapSize = r.ReadInt32(), Objectives = ReadArr(r, r.ReadString) });
        for (int n = r.ReadInt32(); n > 0; n--) c.LandmarkObjectives[r.ReadString()] = new LandmarkObjective { Min = r.ReadInt32(), Landmarks = ReadStructs(r) };
        c.Tile = r.ReadInt32(); c.MaxWaypointsDistance = r.ReadSingle();
        c.Waypoints = ReadArr(r, () => new MazeWaypoints.Generator
        {
            CheckRange = r.ReadInt32(), PlaceMinDistSafezone = r.ReadInt32(), ValidTile = r.ReadInt32(), SizeMin = r.ReadInt32(), SizeMax = r.ReadInt32(),
            CountMin = r.ReadInt32(), CountMax = r.ReadInt32(), FromStart = r.ReadSingle(), FromSafezone = r.ReadSingle(), FromPuzzle = r.ReadSingle(), Mult = r.ReadSingle(),
        });
        return c;
    }

    // Arrays keep null entries (the game tolerates them), written as a presence flag; a null array is length -1.
    private static void WriteArr<T>(BinaryWriter w, T[] arr, Action<T> item)
    {
        w.Write(arr?.Length ?? -1);
        if (arr == null) return;
        foreach (var x in arr) { w.Write(x != null); if (x != null) item(x); }
    }

    private static T[] ReadArr<T>(BinaryReader r, Func<T> item)
    {
        int n = r.ReadInt32();
        if (n < 0) return null;
        var arr = new T[n];
        for (int i = 0; i < n; i++) if (r.ReadBoolean()) arr[i] = item();
        return arr;
    }

    private static void WriteStructs(BinaryWriter w, Structure[] arr) => WriteArr(w, arr, s =>
    {
        w.Write(s.DimX); w.Write(s.DimY); w.Write(s.SubmazeMask); w.Write(s.PrefabId);
        WriteArr(w, s.Border?.Cast<object>().ToArray(), o => w.Write((int)o));
        WriteArr(w, s.NonWalkable?.Cast<object>().ToArray(), o => { var p = ((int x, int y))o; w.Write(p.x); w.Write(p.y); });
        WriteArr(w, s.Objectives, w.Write);
    });

    private static Structure[] ReadStructs(BinaryReader r) => ReadArr(r, () => new Structure
    {
        DimX = r.ReadInt32(), DimY = r.ReadInt32(), SubmazeMask = r.ReadInt32(), PrefabId = r.ReadInt32(),
        Border = ReadArr(r, () => (object)r.ReadInt32())?.Select(o => (int)o).ToArray(),
        NonWalkable = ReadArr(r, () => (object)(r.ReadInt32(), r.ReadInt32()))?.Select(o => ((int x, int y))o).ToArray(),
        Objectives = ReadArr(r, r.ReadString),
    });
}

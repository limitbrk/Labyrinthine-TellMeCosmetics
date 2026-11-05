using Il2Cpp;
using Il2CppCharacterCustomization;
using Il2CppRandomGeneration.Contracts;
using Il2CppValkoGames.Labyrinthine.Misc.TimeInfo;
using MelonLoader;
using MelonLoader.Utils;
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using UnityEngine;

namespace TellMeCosmetics.Predict;

// Mirrors RndCustomizationItemsManager.SpawnCustomizationItems + RndCustomizationSpawner.Spawn (GameAssembly RVA 0x52a250 / 0x52a930).
// Pool data only exists in the maze scene, so it is captured there and cached to disk for the lobby.
public static class ContractPredictor
{
    public record Entry(int Id, float W); // Id -1 = prefab without CustomizationPickup (still takes weight)
    public record Seasonal(int Type, float Chance, Entry[] Prefabs);
    public record Pool(float[] Chance, Entry[] Normal, Entry[] Hardcore, Seasonal[] Seasonal);

    private const int Hardcore = 2; // ContractType.Hardcore
    // Each maze's spawners add their own possiblePrefabs to the global set, so the pool is cached per maze type.
    private static string CachePath(int mazeType) => Path.Combine(MelonEnvironment.UserDataDirectory, "TellMeCosmetics", $"pool.{mazeType}.txt");
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Pool> pools = new(); // ScenePreload stores from a background thread

    internal static Pool GetPool(int mazeType) => pools.GetOrAdd(mazeType, LoadCache);

    // Preload result; a pool already cached for this game build (an in-game capture) is kept.
    internal static void StoreIfMissing(int mazeType, Pool pool)
    {
        if (GetPool(mazeType) != null) return;
        pools[mazeType] = pool;
        SaveCache(mazeType, pool);
    }

    private static Pool LoadCache(int mazeType)
    {
        try
        {
            var path = CachePath(mazeType);
            if (!File.Exists(path)) return null;
            return Parse(File.ReadAllLines(path), ScenePreload.Stamp);
        }
        catch (Exception ex) { MelonLogger.Warning($"Pool cache unreadable: {ex.Message}"); return null; }
    }

    // Line 0 is the game build stamp; a pool from another build parses as missing so ScenePreload rebuilds it.
    internal static Pool Parse(string[] l, string stamp)
    {
        if (l.Length < 4 || l[0] != "v " + stamp) return null;
        l = l[1..];
        return new Pool(Split(l[0]).Select(F).ToArray(), Entries(Split(l[1])), Entries(Split(l[2])),
            l.Skip(3).Select(Split).Select(t => new Seasonal(int.Parse(t[0]), F(t[1]), Entries(t[2..]))).ToArray());
    }

    internal static string[] FileLines(Pool pool, string stamp) => Lines(pool).Prepend("v " + stamp).ToArray();

    // Cache format: line 0 "v <build stamp>", line 1 chances, line 2 normal pool, line 3 hardcore pool, then "type chance pool" per seasonal config. Entries are "id:weight".
    private static string[] Split(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    private static float F(string v) => float.Parse(v, CultureInfo.InvariantCulture);
    private static string S(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static Entry[] Entries(string[] t) => t.Select(x => x.Split(':')).Select(x => new Entry(int.Parse(x[0]), F(x[1]))).ToArray();
    private static string Line(Entry[] e) => string.Join(" ", e.Select(x => $"{x.Id}:{S(x.W)}"));

    internal static string[] Lines(Pool pool) => new[] { string.Join(" ", pool.Chance.Select(S)), Line(pool.Normal), Line(pool.Hardcore) }
        .Concat(pool.Seasonal.Select(c => $"{c.Type} {S(c.Chance)} {Line(c.Prefabs)}")).ToArray();

    private static void SaveCache(int mazeType, Pool pool)
    {
        var lines = FileLines(pool, ScenePreload.Stamp);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath(mazeType)));
            File.WriteAllLines(CachePath(mazeType), lines);
        }
        catch (Exception ex) { MelonLogger.Warning($"Pool cache not saved: {ex.Message}"); }
    }

    private static Entry[] ToEntries(System.Collections.Generic.IEnumerable<WeightedPrefab> list) =>
        list?.Select(p => new Entry(p.Value != null && p.Value.TryGetComponent(out CustomizationPickup c) ? c.ItemID : -1, p.weight)).ToArray()
        ?? Array.Empty<Entry>();

    // Same order as Spawn(): global set first, then the spawner's own list.
    private static Entry[] SpawnerPool(RndCustomizationSpawner s, bool hardcore) =>
        ToEntries(hardcore ? s.globalPrefabsHardcore?.Value : s.globalPrefabs?.Value)
            .Concat(ToEntries(hardcore ? s.possiblePrefabsHardcore : s.possiblePrefabs)).ToArray();

    // Call in the maze scene, right before the game rolls the cosmetic.
    public static Pool Capture(RndCustomizationItemsManager mgr, int mazeType)
    {
        var spawners = UnityEngine.Object.FindObjectsOfType<RndCustomizationSpawner>();
        if (mgr == null || spawners.Length == 0) return null;

        var normal = SpawnerPool(spawners[0], false);
        // Spawn() adds the spawner's own possiblePrefabs; if spawners differ the lobby prediction depends on which spawner gets picked.
        if (spawners.Any(s => !SpawnerPool(s, false).SequenceEqual(normal)))
            MelonLogger.Warning("RndCustomizationSpawners have different pools; lobby prediction may be wrong for this maze.");

        var pool = pools[mazeType] = new Pool(
            mgr.chanceForItem.ToArray(),
            normal,
            SpawnerPool(spawners[0], true),
            mgr.seasonalItems.ToArray().Where(c => c != null)
                .Select(c => new Seasonal((int)c.seasonalEventType, c.chance, ToEntries(c.prefabs))).ToArray());
        SaveCache(mazeType, pool);
        return pool;
    }

    // WeightedRandom.PickRandom: first entry where acc + w >= NextDouble() * Σw, else the last one.
    private static Entry Pick(Il2CppSystem.Random rnd, Entry[] list)
    {
        if (list.Length == 0) return null;
        double total = 0, acc = 0;
        foreach (var e in list) total += e.W;
        double r = rnd.NextDouble() * total;
        foreach (var e in list)
        {
            if (e.W + acc >= r) return e;
            acc += e.W;
        }
        return list[^1];
    }

    public static MazePipeline.ContractInput ToInput(Contract c) => new()
    {
        Seed = c.Seed, SecondSeed = c.SecondSeed, MazeType = (int)c.MazeType, MapX = c.MapSizeVec.x, MapY = c.MapSizeVec.y,
        MapSize = (int)c.MapSize, LandmarkCount = c.LandmarkCount, PuzzleCount = c.PuzzleCount,
    };

    private static readonly System.Collections.Generic.Dictionary<(int, int, int, int, int), MazePipeline.Outcome> outcomes = new();
    public static void ClearOutcomes() => outcomes.Clear();

    // Seeds MazeGenerator holds after generation: each failed maze attempt shifts them (MazeGenerator.CreateMaze).
    public static (int seed, int second, bool simulated) MazeSeeds(Contract contract)
    {
        var input = ToInput(contract);
        var cfg = MazePipeline.Get(input.MazeType);
        if (cfg == null) return (input.Seed, input.SecondSeed, false);
        var key = (input.Seed, input.SecondSeed, input.MazeType, input.MapX, input.MapY);
        if (!outcomes.TryGetValue(key, out var o))
        {
            try { o = MazePipeline.Simulate(cfg, input); }
            catch (Exception ex) { MelonLogger.Warning($"Maze simulation failed: {ex}"); o = null; }
            outcomes[key] = o;
        }
        return o == null ? (input.Seed, input.SecondSeed, false) : (o.Seed, o.SecondSeed, true);
    }

    /// <summary>
    /// Cosmetic ItemID the contract's maze will spawn, or null when the spawn-chance roll fails.
    /// simulated=false means the maze type was never captured (no pool, or seeds assume the maze builds on its first attempt).
    /// </summary>
    public static int? Predict(Contract contract, out float chance, out bool simulated)
    {
        chance = 0;
        simulated = false;
        var pool = GetPool((int)contract.MazeType);
        if (pool == null) return null;
        var (seed, second, sim) = MazeSeeds(contract);
        simulated = sim;
        int type = (int)contract.ContractType;

        // SpawnCustomizationItems: chanceForItem[type] > Random(second).NextDouble()
        if (type >= pool.Chance.Length) return null;
        chance = pool.Chance[type];
        if (!(chance > new Il2CppSystem.Random(second).NextDouble())) return null;

        Entry[] list = null;
        if (type != Hardcore && SeasonalEventsTimeHandler.TryGetActiveSeasonalEvent(out var evt, out _, out _) && evt != null)
        {
            var cfg = pool.Seasonal.FirstOrDefault(c => c.Prefabs.Length > 0 && c.Type == (int)evt.EventType);
            if (cfg != null && cfg.Chance > new Il2CppSystem.Random(second + 0x12b5).NextDouble()) list = cfg.Prefabs;
        }
        list ??= type == Hardcore ? pool.Hardcore : pool.Normal;

        var excluded = contract.m_ExcludedCustomizationItemIDs;
        bool ok(Entry e) => e != null && e.Id >= 0 && !(excluded?.Contains((ushort)e.Id) ?? false);

        var rnd = new Il2CppSystem.Random(unchecked(second + 0x23e3 + seed + 0x692) / 2);
        for (int i = 0; i < 50; i++)
        {
            var e = Pick(rnd, list);
            if (ok(e)) return e.Id;
        }
        // After 50 misses: first allowed entry in list order, else one more pick taken as-is (even if excluded).
        var last = list.FirstOrDefault(ok) ?? Pick(rnd, list);
        return last != null && last.Id >= 0 ? last.Id : null;
    }
}

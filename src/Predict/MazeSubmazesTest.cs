using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using System.Linq;

namespace TellMeCosmetics.Predict;

// Debug harness for the SetupSubmazes port, logs MATCH/MISMATCH per call; patched only when the DebugMazeTests preference is on.
[HarmonyPatch(typeof(MazeGenerator), nameof(MazeGenerator.SetupSubmazes))]
class MazeSubmazesTest
{
    static bool Prepare() => TellMeCosmetics.Config.ConfigManager.Instance.DebugMazeTests.Value;
    internal static NetRandom CloneRandom(Il2CppSystem.Random r) => new(r._seedArray.ToArray(), r._inext, r._inextp);

    internal static bool SameRandom(Il2CppSystem.Random a, NetRandom b) =>
        a._inext == b._inext && a._inextp == b._inextp && a._seedArray.SequenceEqual(b._seedArray);

    internal static int[,] ReadMap(MazeMap m)
    {
        var r = new int[m.SizeX, m.SizeY];
        for (int x = 0; x < m.SizeX; x++)
            for (int y = 0; y < m.SizeY; y++)
                r[x, y] = (int)m[x, y];
        return r;
    }

    // Il2CppInterop has no int[,] wrapper; il2cpp arrays keep row-major data after a 0x20-byte header.
    internal static int ReadInt2D(System.IntPtr arr, int cols, int x, int y) =>
        System.Runtime.InteropServices.Marshal.ReadInt32(arr, 0x20 + (x * cols + y) * 4);

    static void Prefix(MazeGenerator __instance, Il2CppSystem.Random random, out (MazeSubmazes.State s, NetRandom r) __state)
    {
        var g = __instance;
        var dg = g.dataGenerator;
        var s = new MazeSubmazes.State
        {
            MazeX = g.mazeX, MazeY = g.mazeY,
            PfRows = dg.sizeRows, PfCols = dg.sizeCols,
            Map = ReadMap(g.mazeMap),
            Subs = g.possibleSubmazes?.Select(x => new MazeSubmazes.Submaze { RadiusMin = x.submazeRadiusMin, RadiusMax = x.submazeRadiusMax, SpawnTries = x.spawnTries, Chance = x.chanceToSpawn }).ToArray(),
            Start = (dg.StartPoint.x, dg.StartPoint.y), End = (dg.EndPoint.x, dg.EndPoint.y),
            StartDir = (dg.StartPointDir.x, dg.StartPointDir.y), EndDir = (dg.EndPointDir.x, dg.EndPointDir.y),
        };
        if (dg.CustomUnwalkablePositions != null)
            foreach (var p in dg.CustomUnwalkablePositions) s.CustomUnwalkable.Add((p.x, p.y));
        if (g.spawnedStructures != null && g.spawnedStructures.Count > 0)
            MelonLogger.Warning($"🧪Submaze: spawnedStructures not empty ({g.spawnedStructures.Count}), centers not modelled");
        __state = (s, CloneRandom(random));
    }

    static void Postfix(MazeGenerator __instance, Il2CppSystem.Random random, bool __result, (MazeSubmazes.State s, NetRandom r) __state)
    {
        var (s, r) = __state;
        bool sim = MazeSubmazes.SetupSubmazes(s, r);
        var g = __instance;
        var map = ReadMap(g.mazeMap);
        int mapDiff = 0, typeDiff = 0;
        for (int x = 0; x < s.MazeX; x++)
            for (int y = 0; y < s.MazeY; y++)
            {
                if (map[x, y] != s.Map[x, y]) mapDiff++;
                if (ReadInt2D(g.mazeTypeMap.Pointer, s.MazeY, x, y) != s.Types[x, y]) typeDiff++;
            }
        int gt = g.transitions?.Count ?? -1, st = s.Transitions?.Count ?? -1, transDiff = 0;
        if (g.transitions != null && s.Transitions != null)
            foreach (var kv in g.transitions)
                if (!s.Transitions.TryGetValue((kv.Key.x, kv.Key.y), out var t) || t.From != kv.Value.from || t.To != kv.Value.to || t.Vertical != kv.Value.vertical || t.Flip != kv.Value.flip) transDiff++;
        bool rndOk = SameRandom(random, r);
        bool ok = sim == __result && mapDiff == 0 && typeDiff == 0 && gt == st && transDiff == 0 && rndOk;
        MelonLogger.Msg($"🧪Submaze {(ok ? "MATCH" : "MISMATCH")} result={__result}"
            + (ok ? "" : $" sim={sim} mapDiff={mapDiff} typeDiff={typeDiff} trans game={gt} sim={st} transDiff={transDiff} rndOk={rndOk}"));
    }
}

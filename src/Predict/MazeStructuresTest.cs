using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using System.Linq;

namespace TellMeCosmetics.Predict;

// Debug harness for the InsertStructures port, logs MATCH/MISMATCH per call; patched only when the DebugMazeTests preference is on.
[HarmonyPatch(typeof(MazeGenerator), nameof(MazeGenerator.InsertStructuresWithFewTries))]
class MazeStructuresTest
{
    static bool Prepare() => TellMeCosmetics.Config.ConfigManager.Instance.DebugMazeTests.Value;
    internal class Snapshot
    {
        public MazeSubmazes.State S;
        public MazeStructures.Settings Cfg;
        public List<MazeStructures.Placed> Spawned;
        public MazeStructures.Structure[] Arr;
        public NetRandom R;
        public int OutBefore;
    }

    internal static MazeStructures.Structure ToStructure(MazeStructure m) => m == null ? null : new MazeStructures.Structure
    {
        DimX = m.Dimensions.x, DimY = m.Dimensions.y, SubmazeMask = (int)m.supportedSubmazes,
        PrefabId = m.Prefab != null ? m.Prefab.GetInstanceID() : 0,
        Border = m.Border?.Select(t => (int)t).ToArray(), SupportsObjectives = m.SupportsObjectives(),
        NonWalkable = m.nonWalkableOverrides?.Select(v => (v.x, v.y)).ToArray(),
        Objectives = m.supportedObjectives?.Where(o => o != null).Select(o => o.name).ToArray() ?? new string[0],
    };

    internal static MazeSubmazes.State ReadState(MazeGenerator g)
    {
        var dg = g.dataGenerator;
        var s = new MazeSubmazes.State
        {
            MazeX = g.mazeX, MazeY = g.mazeY, PfRows = dg.sizeRows, PfCols = dg.sizeCols,
            Map = MazeSubmazesTest.ReadMap(g.mazeMap),
            Start = (dg.StartPoint.x, dg.StartPoint.y), End = (dg.EndPoint.x, dg.EndPoint.y),
            StartDir = (dg.StartPointDir.x, dg.StartPointDir.y), EndDir = (dg.EndPointDir.x, dg.EndPointDir.y),
            Types = new int[g.mazeX, g.mazeY],
            // Only null-ness matters here (CheckPos), contents were verified by the submaze test.
            Transitions = g.transitions == null ? null : new Dictionary<(int x, int y), MazeSubmazes.Trans>(),
        };
        for (int x = 0; x < g.mazeX; x++)
            for (int y = 0; y < g.mazeY; y++)
                s.Types[x, y] = MazeSubmazesTest.ReadInt2D(g.mazeTypeMap.Pointer, g.mazeY, x, y);
        if (dg.CustomUnwalkablePositions != null)
            foreach (var p in dg.CustomUnwalkablePositions) s.CustomUnwalkable.Add((p.x, p.y));
        return s;
    }

    static void Prefix(MazeGenerator __instance, Il2CppReferenceArray<MazeStructure> mazeStructures, Il2CppSystem.Random random,
        Il2CppSystem.Collections.Generic.List<MazeStructure> outSpawnedList, out Snapshot __state)
    {
        var g = __instance;
        __state = new Snapshot
        {
            S = ReadState(g),
            Cfg = new MazeStructures.Settings { StructureAttempts = g.structureAttempts, EdgeDist = g.landmarkDistanceFromEdge, OtherDist = g.landmarkDistanceFromOther, CanChangeDist = g.canChangeLandmarkDistance },
            Spawned = g.spawnedStructures == null ? new() : g.spawnedStructures.ToArray().Select(m => new MazeStructures.Placed { S = ToStructure(m), X = m.StartPosition.x, Y = m.StartPosition.y }).ToList(),
            Arr = mazeStructures?.Select(ToStructure).ToArray(),
            R = MazeSubmazesTest.CloneRandom(random),
            OutBefore = outSpawnedList?.Count ?? 0,
        };
    }

    static void Postfix(MazeGenerator __instance, int count, bool addRandomWhenOutOfUnique, bool checkStructureSize,
        Il2CppSystem.Random random, Il2CppSystem.Collections.Generic.List<MazeStructure> outSpawnedList, int __result, Snapshot __state)
    {
        var st = __state;
        var outList = new List<MazeStructures.Placed>();
        int sim = MazeStructures.InsertWithFewTries(st.S, st.Cfg, st.Spawned, st.Arr, count, st.R, outList, addRandomWhenOutOfUnique, checkStructureSize);
        var g = __instance;

        var map = MazeSubmazesTest.ReadMap(g.mazeMap);
        int mapDiff = 0;
        for (int x = 0; x < map.GetLength(0); x++)
            for (int y = 0; y < map.GetLength(1); y++)
                if (map[x, y] != st.S.Map[x, y]) mapDiff++;
        var gamePos = outSpawnedList == null ? new List<(int, int)>() : outSpawnedList.ToArray().Skip(st.OutBefore).Select(m => (m.StartPosition.x, m.StartPosition.y)).ToList();
        var simPos = outList.Select(p => (p.X, p.Y)).ToList();
        bool posOk = gamePos.SequenceEqual(simPos);
        bool cfgOk = st.Cfg.OtherDist == g.landmarkDistanceFromOther && st.Cfg.CanChangeDist == g.canChangeLandmarkDistance;
        bool rndOk = MazeSubmazesTest.SameRandom(random, st.R);
        bool ok = sim == __result && mapDiff == 0 && posOk && cfgOk && rndOk;
        MelonLogger.Msg($"🧪Structures {(ok ? "MATCH" : "MISMATCH")} count={count} placed={__result}"
            + (ok ? "" : $" sim={sim} mapDiff={mapDiff} pos game=[{string.Join(" ", gamePos)}] sim=[{string.Join(" ", simPos)}] cfgOk={cfgOk} rndOk={rndOk}"));
    }
}

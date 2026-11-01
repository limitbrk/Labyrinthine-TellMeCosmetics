using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using System.Linq;

namespace TellMeCosmetics.Predict;

// Debug harness for the MazeChecks port, logs MATCH/MISMATCH per call; patched only when the DebugMazeTests preference is on.
static class MazeChecksTest
{
    internal static MazeStructures.Structure ToChecks(MazeStructure m) => MazeStructuresTest.ToStructure(m);

    internal static MazeChecks.World ReadWorld(MazeGenerator g)
    {
        var s = MazeStructuresTest.ReadState(g);
        s.SecondSeed = g.secondSeed;
        if (g.transitions != null)
            foreach (var kv in g.transitions)
                s.Transitions[(kv.Key.x, kv.Key.y)] = new MazeSubmazes.Trans { From = kv.Value.from, To = kv.Value.to, Vertical = kv.Value.vertical, Flip = kv.Value.flip };
        var w = new MazeChecks.World { S = s };
        var byPtr = new Dictionary<System.IntPtr, MazeStructures.Placed>();
        if (g.spawnedStructures != null)
            foreach (var m in g.spawnedStructures.ToArray())
            {
                var p = new MazeStructures.Placed { S = ToChecks(m), X = m.StartPosition.x, Y = m.StartPosition.y };
                w.Spawned.Add(p);
                byPtr[m.Pointer] = p;
            }
        if (g.spawnedPuzzles != null)
            foreach (var m in g.spawnedPuzzles.ToArray())
                w.Puzzles.Add(byPtr.TryGetValue(m.Pointer, out var p) ? p : new MazeStructures.Placed { S = ToChecks(m), X = m.StartPosition.x, Y = m.StartPosition.y });
        return w;
    }

    internal static void Report(string name, MazeGenerator g, MazeChecks.World w, bool ok, string detail, bool compareMap = true)
    {
        var map = MazeSubmazesTest.ReadMap(g.mazeMap);
        int diff = 0;
        for (int x = 0; compareMap && x < map.GetLength(0); x++)
            for (int y = 0; y < map.GetLength(1); y++)
                if (map[x, y] != w.S.Map[x, y]) diff++;
        ok &= diff == 0;
        MelonLogger.Msg($"🧪{name} {(ok ? "MATCH" : "MISMATCH")} {detail}" + (diff == 0 ? "" : $" mapDiff={diff}"));
    }
}

[HarmonyPatch(typeof(MazeGenerator), nameof(MazeGenerator.RemoveInvalidTransitions))]
class ChkInvalidTransitions
{
    static bool Prepare() => TellMeCosmetics.Config.ConfigManager.Instance.DebugMazeTests.Value;
    static void Prefix(MazeGenerator __instance, out MazeChecks.World __state) => __state = MazeChecksTest.ReadWorld(__instance);
    static void Postfix(MazeGenerator __instance, MazeChecks.World __state)
    {
        MazeChecks.RemoveInvalidTransitions(__state);
        int game = __instance.transitions?.Count ?? -1, sim = __state.S.Transitions?.Count ?? -1;
        MazeChecksTest.Report("InvalidTransitions", __instance, __state, game == sim, $"trans game={game} sim={sim}");
    }
}

// EnsureExitIsReachable(int[,]) cannot be hooked: the interop trampoline crashes on the int[,] argument.
// GenerateNewMaze calls it right after a direct FillCustomUnwalkableStructuresList, so snapshot there and resolve at the
// next step: RemoveUnreachableAreas/RequiredLandmarksExist mean it returned true, the next DataFromDimensions means false.
[HarmonyPatch(typeof(MazeGenerator), nameof(MazeGenerator.FillCustomUnwalkableStructuresList))]
class ChkExitReachable
{
    static bool Prepare() => TellMeCosmetics.Config.ConfigManager.Instance.DebugMazeTests.Value;
    static MazeChecks.World pending;
    internal static int insideLandmarks; // FillCustomUnwalkable is also called inside RemoveUnreachableLandmarks

    static void Postfix(MazeGenerator __instance)
    {
        if (insideLandmarks == 0) pending = MazeChecksTest.ReadWorld(__instance);
    }

    internal static void Resolve(bool gameResult)
    {
        if (pending == null) return;
        var w = pending;
        pending = null;
        bool sim = MazeChecks.EnsureExitIsReachable(w, MazeGenerator.instance.mazeTypeMap != null);
        MazeChecksTest.Report("ExitReachable", MazeGenerator.instance, w, sim == gameResult, $"result={gameResult} sim={sim}", compareMap: gameResult);
    }
}

[HarmonyPatch(typeof(MazeDataGenerator), nameof(MazeDataGenerator.RemoveUnreachableAreas))]
class ChkUnreachableAreas
{
    static bool Prepare() => TellMeCosmetics.Config.ConfigManager.Instance.DebugMazeTests.Value;
    static void Prefix(out MazeChecks.World __state)
    {
        ChkExitReachable.Resolve(true);
        __state = MazeChecksTest.ReadWorld(MazeGenerator.instance);
    }
    static void Postfix(MazeChecks.World __state)
    {
        MazeChecks.RemoveUnreachableAreas(__state);
        MazeChecksTest.Report("UnreachableAreas", MazeGenerator.instance, __state, true, "");
    }
}

[HarmonyPatch(typeof(MazeGenerator), nameof(MazeGenerator.RemoveUnreachableLandmarks))]
class ChkUnreachableLandmarks
{
    static bool Prepare() => TellMeCosmetics.Config.ConfigManager.Instance.DebugMazeTests.Value;
    static void Prefix(MazeGenerator __instance, out MazeChecks.World __state)
    {
        ChkExitReachable.insideLandmarks++;
        __state = MazeChecksTest.ReadWorld(__instance);
    }
    static void Postfix(MazeGenerator __instance, bool __result, MazeChecks.World __state)
    {
        ChkExitReachable.insideLandmarks--;
        bool sim = MazeChecks.RemoveUnreachableLandmarks(__state);
        var game = __instance.spawnedStructures?.ToArray().Select(m => (m.StartPosition.x, m.StartPosition.y)).ToList() ?? new();
        var simPos = __state.Spawned.Select(p => (p.X, p.Y)).ToList();
        int gp = __instance.spawnedPuzzles?.Count ?? 0;
        bool ok = sim == __result && game.SequenceEqual(simPos) && gp == __state.Puzzles.Count;
        MazeChecksTest.Report("UnreachableLandmarks", __instance, __state, ok, $"removed={__result} sim={sim} kept game={game.Count} sim={simPos.Count} puzzles game={gp} sim={__state.Puzzles.Count}");
    }
}

[HarmonyPatch(typeof(MazeGenerator), nameof(MazeGenerator.RequiredLandmarksExist))]
class ChkRequiredLandmarks
{
    static bool Prepare() => TellMeCosmetics.Config.ConfigManager.Instance.DebugMazeTests.Value;
    static void Postfix(MazeGenerator __instance, bool __result)
    {
        ChkExitReachable.Resolve(true);
        var w = MazeChecksTest.ReadWorld(__instance);
        var req = __instance.requiredLandmarks?.Select(m => m == null ? null : MazeChecksTest.ToChecks(m)).ToArray();
        bool sim = MazeChecks.RequiredLandmarksExist(w, req, __instance.minRequiredLandmarksSpawned);
        MazeChecksTest.Report("RequiredLandmarks", __instance, w, sim == __result, $"result={__result} sim={sim}");
    }
}

[HarmonyPatch(typeof(MazeGenerator), nameof(MazeGenerator.InsertSafezones))]
class ChkSafezones
{
    static bool Prepare() => TellMeCosmetics.Config.ConfigManager.Instance.DebugMazeTests.Value;
    static void Prefix(MazeGenerator __instance, out (MazeChecks.World w, MazeChecks.Safezones z) __state)
    {
        var g = __instance;
        float m = 0;
        bool reduce = g.contract != null && g.contract.ShouldReduceMapSize(g.contract.MazeType, out m);
        __state = (MazeChecksTest.ReadWorld(g), new MazeChecks.Safezones
        {
            Cells = g.safezoneCells, MinSpawned = g.minSafezonesSpawned, MinTried = g.minSafezonesTriedToSpawn,
            DistStart = g.safezoneMinDistanceToStart, DistLandmark = g.safezoneMinDistanceToLandmark, DistOthers = g.safezoneMinDistanceToOthers,
            MoveForward = g.moveSafezonesForward, GridSize = g.SafezoneGrid.Size, GridSpacing = g.SafezoneGrid.Spacing,
            ReduceMapSize = reduce ? m : -1, SubCanPlace = g.possibleSubmazes?.Select(x => x.canPlaceSafezones).ToArray(),
        });
    }
    static void Postfix(MazeGenerator __instance, int __result, (MazeChecks.World w, MazeChecks.Safezones z) __state)
    {
        int sim = MazeChecks.InsertSafezones(__state.w, __state.z);
        bool ok = sim == __result && __state.z.GridSize == __instance.SafezoneGrid.Size;
        MazeChecksTest.Report("Safezones", __instance, __state.w, ok, $"placed={__result} sim={sim} min={__instance.minSafezonesSpawned} grid game={__instance.SafezoneGrid.Size} sim={__state.z.GridSize}");
    }
}

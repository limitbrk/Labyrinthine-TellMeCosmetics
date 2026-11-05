using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace TellMeCosmetics.Predict;

// Debug harness for the MazeSim port, logs MATCH/MISMATCH per maze attempt; patched only when the DebugMazeTests preference is on.
[HarmonyPatch(typeof(MazeDataGenerator), nameof(MazeDataGenerator.DataFromDimensions))]
class MazeSimTest
{
    static bool Prepare() => TellMeCosmetics.Config.ConfigManager.Instance.DebugMazeTests.Value;
    static void Prefix(MazeDataGenerator __instance, MazeDataGenerator.SubGenerationSettings settings, out MazeSim.Input __state)
    {
        __state = null;
        ChkExitReachable.Resolve(false); // a new attempt means the previous EnsureExitIsReachable failed
        var mg = MazeGenerator.instance;
        var contract = GameValues.instance?.RandomMazeContract;
        if (mg == null || contract == null) return;
        var map = __instance.mazeMap;
        __state = new MazeSim.Input
        {
            Seed = mg.seed, SecondSeed = mg.secondSeed, MazeType = (int)contract.MazeType,
            Rows = __instance.sizeRows, Cols = __instance.sizeCols,
            MapX = map?.SizeX ?? __instance.sizeRows, MapY = map?.SizeY ?? __instance.sizeCols,
            MinStartToEnd = __instance.minDistanceFromStartToEndPiece, MinEdge = __instance.minDistanceFromEdgeToStartEndPiece,
            Threshold = __instance.placementThreshold,
            Sub = settings == null ? null : new MazeSim.Sub { Chance = settings.chance, RadiusMin = settings.radiusMin, RadiusMax = settings.radiusMax, RadiusMaxMapPercent = settings.radiusMaxMapPercent },
        };
    }

    static void Postfix(MazeDataGenerator __instance, MazeMap __result, MazeSim.Input __state)
    {
        if (__state == null || __result == null) return;
        var saved = UnityEngine.Random.state;
        MazeSim.Result sim;
        try { sim = MazeSim.DataFromDimensions(__state); }
        finally { UnityEngine.Random.state = saved; }

        int diff = 0, firstX = -1, firstY = -1;
        bool sizeOk = sim.Map.GetLength(0) == __result.SizeX && sim.Map.GetLength(1) == __result.SizeY;
        if (sizeOk)
            for (int x = 0; x < __result.SizeX; x++)
                for (int y = 0; y < __result.SizeY; y++)
                    if ((int)__result[x, y] != sim.Map[x, y] && diff++ == 0) { firstX = x; firstY = y; }

        var gs = __instance.StartPoint; var ge = __instance.EndPoint;
        bool ptsOk = gs.x == sim.Start.x && gs.y == sim.Start.y && ge.x == sim.End.x && ge.y == sim.End.y
            && __instance.StartPointEulerY == sim.StartEuler && __instance.EndPointEulerY == sim.EndEuler;
        bool ok = sizeOk && diff == 0 && ptsOk;
        MelonLogger.Msg($"🧪MazeSim {(ok ? "MATCH" : "MISMATCH")} type={__state.MazeType} seed={__state.Seed}/{__state.SecondSeed} size={__result.SizeX}x{__result.SizeY}"
            + (ok ? "" : $" sizeOk={sizeOk} cellDiff={diff} first=({firstX},{firstY})"
                + $" start game={gs.x},{gs.y}/{__instance.StartPointEulerY} sim={sim.Start.x},{sim.Start.y}/{sim.StartEuler}"
                + $" end game={ge.x},{ge.y}/{__instance.EndPointEulerY} sim={sim.End.x},{sim.End.y}/{sim.EndEuler}"));
    }
}

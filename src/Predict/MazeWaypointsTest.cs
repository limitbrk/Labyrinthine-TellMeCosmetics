using HarmonyLib;
using Il2Cpp;
using Il2CppBehaviorDesigner.Runtime;
using Il2CppInterop.Runtime;
using Il2CppValkoGames.Labyrinthine.Cases;
using MelonLoader;
using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace TellMeCosmetics.Predict;

// Debug harness for the GenerateWaypoints port, logs MATCH/MISMATCH per call plus where the patrol points come from;
// patched only when the DebugMazeTests preference is on.
[HarmonyPatch(typeof(AIWaypointsGenerator), nameof(AIWaypointsGenerator.GenerateWaypoints))]
class MazeWaypointsTest
{
    static bool Prepare() => TellMeCosmetics.Config.ConfigManager.Instance.DebugMazeTests.Value;
    // GameAssembly RVA of the string literal GenerateWaypoints passes to GlobalVariables.GetVariable; filled once the method ran.
    const int GlobalVarNameSlot = 0x3aef1d8;

    internal static string GlobalVarName()
    {
        var mod = System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>()
            .FirstOrDefault(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
        if (mod == null) return null;
        var ptr = Marshal.ReadIntPtr(mod.BaseAddress + GlobalVarNameSlot);
        return ptr == IntPtr.Zero || ((long)ptr & 1) != 0 ? null : IL2CPP.Il2CppStringToManaged(ptr);
    }

    // BehaviorDesigner global float GenerateWaypoints reads (name confirmed in-game via GlobalVarName()).
    internal static float MaxWaypointsDistance(string name = "max_waypoints_distance")
    {
        var variable = name == null ? null : GlobalVariables.Instance?.GetVariable(name);
        return variable == null ? float.NaN : variable.GetValue().Unbox<float>();
    }

    internal static MazeWaypoints.Generator ToGenerator(AIWaypointsGenerator g) => new()
    {
        CheckRange = g.waypointDistanceCheckRange, PlaceMinDistSafezone = g.wapointPlacementMinDistanceFromSafezone, ValidTile = (int)g.validTileType,
        SizeMin = g.waypointsMapSizeMin, SizeMax = g.waypointsMapSizeMax, CountMin = g.waypointsCountForMinSize, CountMax = g.waypointsCountForMaxSize,
        FromStart = g.waypointDistanceFromStart, FromSafezone = g.waypointDistanceFromSafezone, FromPuzzle = g.waypointDistanceFromPuzzle, Mult = g.waypointsMapSizeMultiplier,
    };

    static void Prefix(AIWaypointsGenerator __instance, AIWaypointsGenerator.Data data, out MazeWaypoints.Context __state)
    {
        __state = null;
        var mg = MazeGenerator.instance;
        // Calls with NavMesh checks come from PopulateMaze after generation and never decide whether an attempt fails.
        if (mg == null || data == null || !data.skipNavMeshChecks) return;
        var w = MazeChecksTest.ReadWorld(mg);
        __state = new MazeWaypoints.Context
        {
            S = w.S, Puzzles = w.Puzzles, Tile = MazeGenerator.TILE_SIZE,
            PatrolPoints = data.existingPatrolPoints?.Where(p => p != null).Select(p => { var v = p.transform.position; return new MazeWaypoints.Vec3(v.x, v.y, v.z); }).ToArray() ?? new MazeWaypoints.Vec3[0],
        };
        // Where do the patrol points come from? Clones of structures from this or earlier attempts, or the scene itself.
        int inSpawned = 0, inOtherStructure = 0, scene = 0;
        var spawned = mg.spawnedStructures?.ToArray().Select(m => m.Pointer).ToHashSet() ?? new();
        foreach (var p in data.existingPatrolPoints ?? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppValkoGames.Labyrinthine.Monsters.AIPatrolPoint>(0))
        {
            var ms = p?.GetComponentInParent<MazeStructure>(true);
            if (ms == null) scene++;
            else if (spawned.Contains(ms.Pointer)) inSpawned++;
            else inOtherStructure++;
        }
        MelonLogger.Msg($"🧪Patrol points={__state.PatrolPoints.Length} inSpawnedStructures={inSpawned} inOtherStructures={inOtherStructure} scene={scene} skipNavMesh={data.skipNavMeshChecks}"
            + (__state.PatrolPoints.Length > 0 ? $" first=({__state.PatrolPoints[0].X:R},{__state.PatrolPoints[0].Y:R},{__state.PatrolPoints[0].Z:R})" : ""));
    }

    static void Postfix(AIWaypointsGenerator __instance, AIWaypointsGenerator.Data data, Il2CppSystem.Collections.Generic.List<UnityEngine.Vector3> __result, MazeWaypoints.Context __state)
    {
        if (__state == null || __result == null) return;
        string name = GlobalVarName();
        float dist = MaxWaypointsDistance(name);
        __state.GlobalDist = dist;

        var gen = ToGenerator(__instance);
        var sim = MazeWaypoints.Generate(gen, __state);
        var game = __result.ToArray().Select(v => (v.x, v.y, v.z)).ToList();
        bool posOk = game.SequenceEqual(sim.Select(v => (v.X, v.Y, v.Z)));
        int desired = MazeWaypoints.Desired(gen, data.mazeX, data.mazeY);
        MelonLogger.Msg($"🧪Waypoints {(posOk ? "MATCH" : "MISMATCH")} count game={game.Count} sim={sim.Count} desired={desired} pass={(float)game.Count >= desired * 0.8f}"
            + $" var='{name}'={dist:R} tile={__state.Tile}" + (posOk ? "" : $" firstDiff={FirstDiff(game, sim)}"));
    }

    static string FirstDiff(System.Collections.Generic.List<(float, float, float)> game, System.Collections.Generic.List<MazeWaypoints.Vec3> sim)
    {
        for (int i = 0; i < Math.Max(game.Count, sim.Count); i++)
        {
            string g = i < game.Count ? game[i].ToString() : "-", s = i < sim.Count ? (sim[i].X, sim[i].Y, sim[i].Z).ToString() : "-";
            if (g != s) return $"#{i} game={g} sim={s}";
        }
        return "none";
    }
}

using HarmonyLib;
using Il2Cpp;
using Il2CppRandomGeneration;
using Il2CppValkoGames.Labyrinthine.Cases.Objectives;
using MelonLoader;
using System;
using System.Linq;

namespace TellMeCosmetics.Predict;

// Captures the scene's maze generation settings before GenerateMap mutates any of them, so the lobby can simulate this maze type.
[HarmonyPatch(typeof(MazeGenerator), nameof(MazeGenerator.GenerateMap))]
class MazeCapture
{
    static void Prefix(MazeGenerator __instance)
    {
        try
        {
            var g = __instance;
            var contract = g.contract;
            var mgr = RndObjectiveManager.Instance;
            if (contract == null || mgr == null) return;

            var sg = g.subGenerationSettings;
            var cfg = new MazePipeline.Config
            {
                MazeType = (int)contract.MazeType,
                SubmazeTries = g.submazeTries, StructureAttempts = g.structureAttempts, EdgeDist = g.landmarkDistanceFromEdge,
                MinRequired = g.minRequiredLandmarksSpawned, MinDistStartEnd = g.minDistanceFromStartToEndPiece, MinDistEdge = g.minDistanceFromEdgeToStartEndPiece,
                CanChangeDist = g.canChangeLandmarkDistance, RequireAtLeastOneLandmark = g.requireAtLeastOneLandmark,
                SubChance = sg?.chance ?? 0, SubRMin = sg?.radiusMin ?? 0, SubRMax = sg?.radiusMax ?? 0, SubPct = sg?.radiusMaxMapPercent ?? 0,
                Subs = g.possibleSubmazes?.Select(x => x == null ? null : new MazePipeline.Submaze
                {
                    RadiusMin = x.submazeRadiusMin, RadiusMax = x.submazeRadiusMax, SpawnTries = x.spawnTries, Chance = x.chanceToSpawn, CanPlaceSafezones = x.canPlaceSafezones,
                }).ToArray(),
                SafeCells = g.safezoneCells, SafeMinSpawned = g.minSafezonesSpawned, SafeMinTried = g.minSafezonesTriedToSpawn,
                SafeDistStart = g.safezoneMinDistanceToStart, SafeDistLandmark = g.safezoneMinDistanceToLandmark, SafeDistOthers = g.safezoneMinDistanceToOthers,
                GridSize = g.SafezoneGrid.Size, GridSpacing = g.SafezoneGrid.Spacing, MoveSafezonesForward = g.moveSafezonesForward,
                Required = g.requiredLandmarks?.Select(MazeStructuresTest.ToStructure).ToArray(),
                Puzzles = g.puzzleStructures?.Select(MazeStructuresTest.ToStructure).ToArray(),
                Landmarks = g.landmarkStructures?.Select(MazeStructuresTest.ToStructure).ToArray(),
                Sets = mgr.objectiveSets?.Select(s => s == null ? null : new MazePipeline.ObjectiveSet
                {
                    MinMapSize = (int)s.minRequiredMapSize,
                    Objectives = s.objectives?.Where(o => o?.objective != null).Select(o => o.objective.name).ToArray() ?? new string[0],
                }).ToArray(),
                Tile = MazeGenerator.TILE_SIZE, MaxWaypointsDistance = MazeWaypointsTest.MaxWaypointsDistance(),
                Waypoints = g.aiWaypointsGenerators?.Select(x => x == null ? null : MazeWaypointsTest.ToGenerator(x)).ToArray(),
            };
            if (mgr.objectiveSets != null)
                foreach (var set in mgr.objectiveSets)
                    foreach (var oc in set?.objectives ?? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<RndObjectiveManager.CaseObjectiveConfig>(0))
                    {
                        var lo = oc?.objective?.TryCast<CaseObjectiveWithLandmarkSO>();
                        if (lo == null || cfg.LandmarkObjectives.ContainsKey(lo.name)) continue;
                        cfg.LandmarkObjectives[lo.name] = new MazePipeline.LandmarkObjective
                        {
                            Min = lo.MinLandmarksToSpawn,
                            Landmarks = lo.GetRequiredLandmarks(contract.MazeType)?.ToArray().Select(MazeStructuresTest.ToStructure).ToArray() ?? new MazeStructures.Structure[0],
                        };
                    }
            MazePipeline.Store(cfg);
            ContractPredictor.ClearOutcomes();

            if (!TellMeCosmetics.Config.ConfigManager.Instance.DebugMazeTests.Value) return;
            ScenePreload.Check(cfg.MazeType, cfg, null);
            int predictedSet = MazePipeline.SelectObjectiveSet(cfg, ContractPredictor.ToInput(contract));
            int actualSet = mgr.objectiveSets == null ? -1 : Array.FindIndex(mgr.objectiveSets.ToArray(), s => s != null && mgr.activeObjectiveSet != null && s.Pointer == mgr.activeObjectiveSet.Pointer);
            MelonLogger.Msg($"🧪ObjectiveSet {(predictedSet == actualSet ? "MATCH" : "MISMATCH")} predicted={predictedSet} actual={actualSet} | captured tile={cfg.Tile} maxWaypointsDistance={cfg.MaxWaypointsDistance:R} waypointGenerators={cfg.Waypoints?.Length ?? 0}");
        }
        catch (Exception ex) { MelonLogger.Warning($"Maze capture failed: {ex}"); }
    }
}

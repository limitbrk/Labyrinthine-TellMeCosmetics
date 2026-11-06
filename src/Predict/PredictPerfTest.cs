using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TellMeCosmetics.Predict;

// Debug harness: times seed → maze-seed simulation (the cost behind each lobby cosmetic prediction); runs only with DebugPerfTest.
static class PredictPerfTest
{
    private const int Runs = 100;
    private static readonly HashSet<int> benched = new();

    // Once per maze type: replays the first lobby contract seen with Runs fixed random seeds, one per frame so the lobby stays responsive.
    public static void BenchOnce(MazePipeline.Config cfg, MazePipeline.ContractInput template)
    {
        if (benched.Add(template.MazeType)) MelonCoroutines.Start(Bench(cfg, template));
    }

    private static IEnumerator Bench(MazePipeline.Config cfg, MazePipeline.ContractInput t)
    {
        var rnd = new System.Random(12345); // fixed so runs are comparable across builds
        var ms = new List<double>();
        int failed = 0, unbuilt = 0;
        for (int i = 0; i < Runs; i++)
        {
            var input = new MazePipeline.ContractInput
            {
                Seed = rnd.Next(), SecondSeed = rnd.Next(), MazeType = t.MazeType, MapX = t.MapX, MapY = t.MapY,
                MapSize = t.MapSize, LandmarkCount = t.LandmarkCount, PuzzleCount = t.PuzzleCount,
            };
            var sw = Stopwatch.StartNew();
            MazePipeline.Outcome o;
            try { o = MazePipeline.Simulate(cfg, input); }
            catch (System.Exception ex) { MelonLogger.Warning($"⏱️Perf type={t.MazeType} seed={input.Seed}/{input.SecondSeed} threw: {ex.Message}"); yield break; }
            ms.Add(sw.Elapsed.TotalMilliseconds);
            if (o == null) unbuilt++; else failed += o.FailedAttempts;
            yield return null;
        }
        ms.Sort();
        MelonLogger.Msg($"⏱️Perf type={t.MazeType} map={t.MapX}x{t.MapY} runs={Runs} avg={ms.Average():F2}ms p50={ms[Runs / 2]:F2}ms p95={ms[Runs * 95 / 100]:F2}ms max={ms[^1]:F2}ms"
            + $" failedAttempts avg={failed / (double)(Runs - unbuilt):F2} unbuilt={unbuilt}");
    }
}

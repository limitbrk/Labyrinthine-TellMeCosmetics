using MelonLoader;

namespace TellMeCosmetics.Predict;

// Debug harness: NetRandom/UnityRandom against Il2CppSystem.Random/UnityEngine.Random; main thread, runs only with DebugMazeTests.
static class GameRandomTest
{
    public static void Run()
    {
        int bad = 0;
        var seeds = new[] { 0, 1, -1, 12345, int.MaxValue, int.MinValue, 0x4a3, -987654321 };
        var saved = UnityEngine.Random.state;
        try
        {
            foreach (int seed in seeds)
            {
                var g = new Il2CppSystem.Random(seed);
                var p = new NetRandom(seed);
                for (int i = 0; i < 500; i++)
                {
                    bool ok = (i % 5) switch
                    {
                        0 => g.Next() == p.Next(),
                        1 => g.NextDouble() == p.NextDouble(),
                        2 => g.Next(i + 1) == p.Next(i + 1),
                        3 => g.Next(-i, 3 * i) == p.Next(-i, 3 * i),
                        _ => g.Next(int.MinValue + i, int.MaxValue - i) == p.Next(int.MinValue + i, int.MaxValue - i), // large-range path
                    };
                    if (!ok && bad++ == 0) MelonLogger.Msg($"🧪GameRandom NetRandom first diff seed={seed} call={i}");
                }
                UnityEngine.Random.InitState(seed);
                var u = new UnityRandom(seed);
                for (int i = 0; i < 500; i++)
                    if (UnityEngine.Random.value != u.Value && bad++ == 0) MelonLogger.Msg($"🧪GameRandom UnityRandom first diff seed={seed} call={i}");
            }
        }
        finally { UnityEngine.Random.state = saved; }
        MelonLogger.Msg($"🧪GameRandom {(bad == 0 ? "MATCH" : $"MISMATCH diffs={bad}")}");
    }
}

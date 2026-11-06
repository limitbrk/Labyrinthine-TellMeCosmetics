using MelonLoader;

namespace TellMeCosmetics.Config
{
    public class GameplayConfig
    {
        public MelonPreferences_Entry<bool> RevealAll { get; set; }
        public MelonPreferences_Entry<bool> DebugMazeTests { get; set; }
        public MelonPreferences_Entry<bool> DebugPerfTest { get; set; }

        public GameplayConfig(MelonPreferences_Category category)
        {
            // Load the configuration from MelonPreferences
            RevealAll = category.CreateEntry<bool>("RevealAllItems", false);
            DebugMazeTests = category.CreateEntry<bool>("DebugMazeTests", false,
                description: "Compare every maze generation step against the game and log MATCH/MISMATCH (slower map loading)");
            DebugPerfTest = category.CreateEntry<bool>("DebugPerfTest", false,
                description: "Time 100 maze simulations per maze type in the lobby and log ⏱️Perf (the lobby stutters while it runs)");
        }
    }
}
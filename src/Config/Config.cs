using MelonLoader;
using MelonLoader.Utils;
using System.IO;

namespace TellMeCosmetics.Config;
public static class ConfigManager
{
    private static readonly string configPath = Path.Combine(MelonEnvironment.UserDataDirectory, "TellMeCosmetics.cfg");

    private static GameplayConfig instance;

    // Lazy so Harmony Prepare() can read it even when patches are applied before OnInitializeMelon.
    public static GameplayConfig Instance => instance ??= Load();

    private static GameplayConfig Load()
    {
        var gameplay_cfg = MelonPreferences.CreateCategory("Gameplay");
        gameplay_cfg.SetFilePath(configPath);

        return new GameplayConfig(gameplay_cfg);
    }
}
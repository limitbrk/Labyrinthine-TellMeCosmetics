using MelonLoader;
using MelonLoader.Utils;
using System.IO;

namespace TellMeCosmetics.Config;
public static class ConfigManager
{
    private static readonly string configPath = Path.Combine(MelonEnvironment.UserDataDirectory, "TellMeCosmetics.cfg");

    public static GameplayConfig Load()
    {
        var gameplay_cfg = MelonPreferences.CreateCategory("Gameplay");
        gameplay_cfg.SetFilePath(configPath);

        return new GameplayConfig(gameplay_cfg);
    }
}
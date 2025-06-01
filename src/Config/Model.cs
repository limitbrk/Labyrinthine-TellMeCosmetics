using MelonLoader;

namespace TellMeCosmetics.Config
{
    public class GameplayConfig
    {
        public MelonPreferences_Entry<bool> RevealAll { get; set; }

        public GameplayConfig(MelonPreferences_Category category)
        {
            // Load the configuration from MelonPreferences
            RevealAll = category.CreateEntry<bool>("RevealAllItems", false);
        }
    }
}
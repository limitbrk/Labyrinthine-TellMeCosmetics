using Il2CppTMPro;
using System.Linq;

namespace TellMeCosmetics.UI;

public static class UIUtils
{
    public static TMP_FontAsset GetFontByName(string name) 
    {
        return UnityEngine.Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
            .FirstOrDefault(f => f.name.Equals(name, System.StringComparison.OrdinalIgnoreCase));
    }

}
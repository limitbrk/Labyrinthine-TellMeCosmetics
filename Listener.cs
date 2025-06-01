using HarmonyLib;
using Il2CppCharacterCustomization;

namespace TellMeCosmetics;

[HarmonyPatch(typeof(CustomizationPickup), nameof(CustomizationPickup.Start))]
class Customization_SpawnItemListener
{
    static void Postfix(CustomizationPickup __instance)
    {
        CustomizationPickupFinderMod.Instance?.Show(__instance);
    }
}
[HarmonyPatch(typeof(CustomizationPickup), nameof(CustomizationPickup.Pickup))]
class Customization_PickUpListener
{
    static void Postfix(CustomizationPickup __instance){
        CustomizationPickupFinderMod.Instance?.Pickup(__instance.ItemID);
    }
}

[HarmonyPatch(typeof(CustomizationSave), nameof(CustomizationSave.Load))]
class Customization_SaveLoadListener
{
    static void Postfix(CustomizationSave __result)
    {
        // CustomizationSave IntPtr always change
        CustomizationPickupFinderMod.Instance?.UpdateSave(__result);
    }
}

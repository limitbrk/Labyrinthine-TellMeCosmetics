using HarmonyLib;
using Il2CppCharacterCustomization;
using Il2CppRandomGeneration.Contracts;

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

[HarmonyPatch(typeof(RndCustomizationItemsManager), nameof(RndCustomizationItemsManager.SpawnCustomizationItems))]
class Customization_RndSpawnListener
{
    static void Prefix(RndCustomizationItemsManager __instance)
    {
        CustomizationPickupFinderMod.Instance?.CapturePool(__instance);
    }
}

[HarmonyPatch(typeof(ContractUI), nameof(ContractUI.UpdateUI))]
class Contract_UpdateUIListener
{
    static void Postfix(ContractUI __instance)
    {
        CustomizationPickupFinderMod.Instance?.ShowPrediction(__instance);
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

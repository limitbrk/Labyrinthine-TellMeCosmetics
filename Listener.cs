using HarmonyLib;

using Il2CppCharacterCustomization;
using Il2CppValkoGames.Labyrinthine.Cases;
using UnityEngine;

namespace TellMeCosmetics;

[HarmonyPatch(typeof(RndSpawnerBase), nameof(RndSpawnerBase.SpawnItem))]
class CustomizationItemSpawnListener
{
    static void Postfix(ref GameObject spawned, bool __result){
        if (__result && spawned != null)
        {
            var customizationPickup = spawned.GetComponentInChildren<CustomizationPickup>();
            if (customizationPickup == null)
            {
                // Non-Cosmetics = skip ... [You may get idea for all item tracking]
                return;
            }
            CustomizationPickupFinderMod.Instance?.Show(customizationPickup);
        }
    }
}
[HarmonyPatch(typeof(CustomizationPickup), nameof(CustomizationPickup.Pickup))]
class CustomizationPickupPickupListener
{
    static void Postfix(CustomizationPickup __instance){
        CustomizationPickupFinderMod.Instance?.Pickup(__instance.ItemID);
    }
}

[HarmonyPatch(typeof(CustomizationSave), nameof(CustomizationSave.Load))]
class CustomizationSaveLoadListener
{
    static void Postfix(CustomizationSave __result)
    {
        // CustomizationSave IntPtr always change
        CustomizationPickupFinderMod.Instance?.UpdateSave(__result);
    }
}

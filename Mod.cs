using HarmonyLib;
using MelonLoader;
using System;
using System.Threading;
using UnityEngine;

using Il2CppCharacterCustomization;

using TellMeCosmetics;
using TellMeCosmetics.UI;

[assembly: MelonInfo(typeof(CustomizationPickupFinderMod), "Tell Me Cosmetics", "0.0.4", "LimitBRK")]
[assembly: MelonGame("Valko Game Studios", "Labyrinthine")]

namespace TellMeCosmetics;
public class CustomizationPickupFinderMod : MelonMod
{

    internal static CustomizationPickupFinderMod Instance { get; private set; }
    private ItemAlertUI alertui;

    public override void OnInitializeMelon()
    {
        Instance = this; // Store a reference to the instance
        LoggerInstance.Msg($"TellMeCosmetics Mod Loaded!!!");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        LoggerInstance.Msg($"Entering {buildIndex}/{sceneName}");

        // Init UI
        if (buildIndex >= 1 && sceneName == "MainMenu") {
            if (this.alertui == null || this.alertui.isDestroy()){
                try {
                    RectTransform ui = GameObject.Find("Global/Global Canvas/Player_UI/Tab Menu").GetComponent<RectTransform>();
                    ItemsCollectionSO itemsCollection = Resources.FindObjectsOfTypeAll<ItemsCollectionSO>()[0];
                    if(itemsCollection != null){
                        LoggerInstance.Msg("Found itemsCollectionSO Class");
                    }
                    if(ui != null) {
                        LoggerInstance.Msg($"Initialization UI...");
                        this.alertui = new ItemAlertUI(ui, itemsCollection);
                        LoggerInstance.Msg($"Created UI Successfully");
                    }
                } 
                catch (NullReferenceException e)
                {
                    LoggerInstance.Warning(e);
                    // SKIP if null
                }
            }
        }

        // Clear UI
        if (buildIndex >= 3 && sceneName == "LoadingScreen"){
            LoggerInstance.Msg($"Clearing UI when enter {sceneName}");
            Thread t = new(() =>
            {
                this.alertui?.Clear();
            });
            t.Start();
        }
    }

    internal void Show(CustomizationPickup item) {
        LoggerInstance.Msg($"Spawned Cosmetic ItemID {item.ItemID} {item.name}!");
        this.alertui?.Show(item);
    }
    
}

[HarmonyPatch(typeof(RndCustomizationSpawner), nameof(RndCustomizationSpawner.Spawn))]
class CustomizationItemSpawnListener
{
    static void Postfix(){
        CustomizationPickup item = GameObject.FindObjectOfType<CustomizationPickup>(); 
        CustomizationPickupFinderMod.Instance?.Show(item);
    }
}

using HarmonyLib;
using MelonLoader;
using System;
using System.Threading;
using UnityEngine;

using Il2CppCharacterCustomization;

using TellMeCosmetics;
using TellMeCosmetics.UI;
using Unity;

[assembly: MelonInfo(typeof(CustomizationPickupFinderMod), "Tell Me Cosmetics", "0.1.1", "LimitBRK")]
[assembly: MelonGame("Valko Game Studios", "Labyrinthine")]

namespace TellMeCosmetics;
public class CustomizationPickupFinderMod : MelonMod
{
    internal static CustomizationPickupFinderMod Instance { get; private set; }
    private CustomizationSave SaveInstance;
    private ItemsCollectionSO itemsCollection;
    private ItemAlertUI alertui;

    // Settings
    private MelonPreferences_Category category_mod;
    private MelonPreferences_Entry<bool> entry_revealall;

    public override void OnInitializeMelon()
    {
        category_mod = MelonPreferences.CreateCategory("Gameplay");
        category_mod.SetFilePath("Mods/TellMeCosmetics_config.cfg");
        entry_revealall = category_mod.CreateEntry<bool>("RevealAllItems", false);

        Instance = this; // Store a reference to the instance
        LoggerInstance.Msg($"TellMeCosmetics Mod Loaded!!!");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        LoggerInstance.Msg($"SCENE {buildIndex}/{sceneName} Loaded!");
        if (this.itemsCollection == null){
            ItemsCollectionSO[] ic = Resources.FindObjectsOfTypeAll<ItemsCollectionSO>();
            if(ic.Length > 0){
                this.itemsCollection = ic[0];
                LoggerInstance.Msg("Init itemsCollectionSO Class");
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

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        LoggerInstance.Msg($"SCENE {buildIndex}/{sceneName} Initialized!");
        // Init UI 
        // TODO: Why MainMenu loaded twice? So we go twice as them want
        if (buildIndex >= 1 && sceneName == "MainMenu") {
            if (this.alertui == null || this.alertui.IsDestroy()){
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
    }

    internal void Show(CustomizationPickup item) {
        // Intended to not hide itemname in log 
        LoggerInstance.Msg($"Spawned Cosmetic ItemID {item.ItemID} {item.name}!");
        if (!this.entry_revealall.Value && this.SaveInstance != null) {
            this.alertui?.Show(item, reveal: this.SaveInstance.IsItemUnlocked(item.ItemID));
        } else {
            this.alertui?.Show(item);
        }
    }

    internal void Pickup(ushort itemID) {
        this.alertui?.Pickup(itemID);
    }

    
    internal void UpdateSave(CustomizationSave save) {
        if(save != null) this.SaveInstance = save;
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

[HarmonyPatch(typeof(CustomizationSave), nameof(CustomizationSave.Load))]
class CustomizationSaveLoadListener
{
    static void Postfix(CustomizationSave __result){
        // CustomizationSave IntPtr always change
        CustomizationPickupFinderMod.Instance?.UpdateSave(__result);
    }
}

[HarmonyPatch(typeof(CustomizationPickup), nameof(CustomizationPickup.Pickup))]
class CustomizationPickupPickupListener
{
    static void Postfix(CustomizationPickup __instance){
        CustomizationPickupFinderMod.Instance?.Pickup(__instance.ItemID);
    }
}
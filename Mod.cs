﻿using HarmonyLib;
using MelonLoader;
using System;
using System.Threading;
using UnityEngine;

using Il2CppCharacterCustomization;

using TellMeCosmetics;
using TellMeCosmetics.UI;
using Il2CppValkoGames.Labyrinthine.Saves;

[assembly: MelonInfo(typeof(CustomizationPickupFinderMod), "Tell Me Cosmetics", "0.0.4", "LimitBRK")]
[assembly: MelonGame("Valko Game Studios", "Labyrinthine")]

namespace TellMeCosmetics;
public class CustomizationPickupFinderMod : MelonMod
{

    internal static CustomizationPickupFinderMod Instance { get; private set; }
    private ItemAlertUI alertui;
    private bool cosmetics_ready;

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
            if (this.alertui == null){
                try {
                    RectTransform ui = GameObject.Find("Global/Global Canvas/Player_UI/Tab Menu").GetComponent<RectTransform>();
                    // still issue with = Method not found: 'UnityEngine.Object[] UnityEngine.Resources.FindObjectsOfTypeAll(System.Type) InstanceID 76846'.
                    // ItemsCollectionSO itemsCollection = AssetBundle.FindObjectOfTypeAll<Il2CppCharacterCustomization.ItemsCollectionSO>();
                    ItemsCollectionSO itemsCollection = Resources.FindObjectsOfTypeAll<ItemsCollectionSO>()[0];
                    if(itemsCollection != null){
                        LoggerInstance.Msg("Found itemsCollectionSO");
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
        if (this.alertui != null && buildIndex >= 3 && sceneName == "LoadingScreen"){
            LoggerInstance.Msg($"Clearing UI when enter {sceneName}");
            Thread t = new Thread(() =>
            {
                this.cosmetics_ready = false;
                this.alertui.Clear();
            });
            t.Start();
        }

        // Clear UI
        if (this.alertui != null && buildIndex >= 4 && sceneName.StartsWith("Rand_Maze")){
            this.cosmetics_ready = true;
        }
    }

    internal void Show(ushort itemID) {
        LoggerInstance.Msg($"Spawned Cosmetic ItemID {itemID}!!! {cosmetics_ready}");
        if(this.alertui != null && this.cosmetics_ready){
            this.alertui.Show(itemID);
        }
    }
    
}

[HarmonyPatch(typeof(RndCustomizationSpawner), nameof(RndCustomizationSpawner.Spawn))]
class CustomizationItemSpawnListener
{
    static void Postfix(ref GameObject ___spawned){
        CustomizationPickup item = ___spawned.GetComponent<CustomizationPickup>();
        CustomizationPickupFinderMod.Instance?.Show(item.ItemID);
    }
}

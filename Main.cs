using Il2CppCharacterCustomization;
using MelonLoader;
using System;
using System.Collections;
using System.Linq;
using TellMeCosmetics;
using TellMeCosmetics.Config;
using TellMeCosmetics.UI;
using UnityEngine;

[assembly: VerifyLoaderVersion(0, 6, 6, true)]
[assembly: MelonInfo(typeof(CustomizationPickupFinderMod), ModInfo.Name, ModInfo.Version, ModInfo.Version)]
[assembly: MelonGame("Valko Game Studios", "Labyrinthine")]

namespace TellMeCosmetics;

public class CustomizationPickupFinderMod : MelonMod
{
    internal static CustomizationPickupFinderMod Instance { get; private set; }
    private CustomizationSave SaveInstance;
    private ItemsCollectionSO itemsCollection;
    private ItemAlertUI alertui;

    // Settings
    private GameplayConfig config;

    public override void OnInitializeMelon()
    {
        config = ConfigManager.Load();
        Instance = this; // Store a reference to the instance
        LoggerInstance.Msg($"TellMeCosmetics Mod Loaded!!!");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        LoggerInstance.Msg($"SCENE {buildIndex}/{sceneName} Loaded!");
        MelonCoroutines.Start(InitItemsCollectionSO());

        // Clear UI
        if (buildIndex >= 3 && sceneName == "LoadingScreen")
        {
            MelonCoroutines.Start(ClearAlertUIAfterDelay());
        }
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        LoggerInstance.Msg($"SCENE {buildIndex}/{sceneName} Initialized!");
        // Init UI 
        if (buildIndex >= 1 && sceneName == "MainMenu")
        {
            if (this.alertui == null || this.alertui.IsDestroy())
            {
                MelonCoroutines.Start(InitUI());
            }
        }
    }
    private IEnumerator InitItemsCollectionSO()
    {

        if (this.itemsCollection == null)
        {
            yield return null;

            ItemsCollectionSO[] ic = Resources.FindObjectsOfTypeAll<ItemsCollectionSO>();
            if (ic.Length > 0)
            {
                this.itemsCollection = ic[0];
                LoggerInstance.Msg("Init itemsCollectionSO Class");
            }
        }
    }
    private IEnumerator InitUI()
    {
        yield return new WaitForSeconds(1.0f); // Give Unity time to fully load the UI

        try
        {
            var tabMenu = GameObject.Find("Global/Global Canvas/Player_UI/Tab Menu");
            if (tabMenu == null)
            {
                LoggerInstance.Warning("UI Tab Menu not found!");
                yield break;
            }

            RectTransform ui = tabMenu.GetComponent<RectTransform>();
            if (ui == null)
            {
                LoggerInstance.Warning("UI RectTransform missing!");
                yield break;
            }

            itemsCollection = Resources.FindObjectsOfTypeAll<ItemsCollectionSO>().FirstOrDefault();
            if (itemsCollection == null)
            {
                LoggerInstance.Warning("ItemsCollectionSO not found!");
                yield break;
            }

            LoggerInstance.Msg("Initializing UI components...");
            this.alertui = new ItemAlertUI(ui, itemsCollection);
            LoggerInstance.Msg("UI initialized successfully.");
        }
        catch (Exception ex)
        {
            LoggerInstance.Error($"[InitUI] Exception: {ex}");
        }
    }
    private IEnumerator ClearAlertUIAfterDelay()
    {
        yield return new WaitForEndOfFrame(); // Let Unity stabilize
        alertui?.Clear();
        LoggerInstance.Msg("UI Cleared");
    }

    internal void Show(CustomizationPickup item)
    {
        // Intended to not hide itemname in log 
        LoggerInstance.Msg($"📦Spawned Cosmetic ItemID {item.ItemID} {item.name}!");
        if (!this.config.RevealAll.Value && this.SaveInstance != null)
        {
            this.alertui?.Show(item, reveal: this.SaveInstance.IsItemUnlocked(item.ItemID));
        }
        else
        {
            this.alertui?.Show(item);
        }
    }

    internal void Pickup(ushort itemID)
    {
        this.alertui?.Pickup(itemID);
    }

    internal void UpdateSave(CustomizationSave save)
    {
        if (save != null) this.SaveInstance = save;
    }
}

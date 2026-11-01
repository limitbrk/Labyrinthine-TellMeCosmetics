using Il2CppCharacterCustomization;
using Il2CppRandomGeneration.Contracts;
using MelonLoader;
using System;
using System.Collections;
using System.Linq;
using TellMeCosmetics;
using TellMeCosmetics.Config;
using TellMeCosmetics.Predict;
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
        config = ConfigManager.Instance;
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
        if (this.itemsCollection != null) yield break;

        for (int attempts = 0; attempts < 3; attempts++)
        {
            ItemsCollectionSO[] ic = Resources.FindObjectsOfTypeAll<ItemsCollectionSO>();
            if (ic.Length > 0)
            {
                this.itemsCollection = ic[0];
                LoggerInstance.Msg("Init itemsCollectionSO Class");
                yield break;
            }
            yield return new WaitForSeconds(0.1f);
        }
        LoggerInstance.Warning("Failed to initialize ItemsCollectionSO after 3 attempts");
    }
    private IEnumerator InitUI()
    {
        yield return new WaitForSeconds(0.2f); // Reduced wait time
        
        for (int attempts = 0; attempts < 3; attempts++)
        {
            if (TryInitializeUI())
            {
                LoggerInstance.Msg("UI initialized successfully.");
                yield break;
            }
            yield return new WaitForSeconds(0.1f);
            LoggerInstance.Warning($"UI initialization attempt {attempts + 1}/3 failed, retrying...");
        }
        LoggerInstance.Error("Failed to initialize UI after 3 attempts");
    }

    private bool TryInitializeUI()
    {
        try
        {
            var tabMenu = GameObject.Find("Global/Global Canvas/Player_UI/Tab Menu");
            if (tabMenu == null)
            {
                LoggerInstance.Warning("UI Tab Menu not found!");
                return false;
            }

            RectTransform ui = tabMenu.GetComponent<RectTransform>();
            if (ui == null)
            {
                LoggerInstance.Warning("UI RectTransform missing!");
                return false;
            }

            itemsCollection = Resources.FindObjectsOfTypeAll<ItemsCollectionSO>().FirstOrDefault();
            if (itemsCollection == null)
            {
                LoggerInstance.Warning("ItemsCollectionSO not found!");
                return false;
            }

            LoggerInstance.Msg("Initializing UI components...");
            this.alertui = new ItemAlertUI(ui, itemsCollection);
            return true;
        }
        catch (Exception ex)
        {
            LoggerInstance.Error($"[InitUI] Exception: {ex}");
            return false;
        }
    }
    private IEnumerator ClearAlertUIAfterDelay()
    {
        yield return new WaitForEndOfFrame(); // Let Unity stabilize
        alertui?.Clear();
        LoggerInstance.Msg("UI Cleared");
    }

    private System.Collections.Generic.Queue<CustomizationPickup> pendingItems = new();
    private bool isProcessingItems = false;

    internal void Show(CustomizationPickup item)
    {
        if (item.gameObject.transform.localPosition == Vector3.zero) return; // Invalid position, likely not spawned in world
        
        // Queue the item instead of showing immediately
        pendingItems.Enqueue(item);
        
        // Start processing if not already running
        if (!isProcessingItems)
        {
            MelonCoroutines.Start(ProcessPendingItems());
        }
    }

    private IEnumerator ProcessPendingItems()
    {
        isProcessingItems = true;
        
        while (pendingItems.Count > 0)
        {
            var item = pendingItems.Dequeue();

            // Process item
            if (item.gameObject.transform.localPosition == Vector3.zero) continue; // Invalid position, likely not spawned in world
            LoggerInstance.Msg($"📦Spawned Cosmetic ItemID {item.ItemID} {item.name}!");
            if (!this.config.RevealAll.Value && this.SaveInstance != null)
            {
                this.alertui?.Show(item, reveal: this.SaveInstance.IsItemUnlocked(item.ItemID));
            }
            else
            {
                this.alertui?.Show(item);
            }
            
            // Add a small delay between processing items
            yield return new WaitForSeconds(0.05f);
        }
        
        isProcessingItems = false;
    }

    internal void Pickup(ushort itemID)
    {
        this.alertui?.Pickup(itemID);
    }

    internal void CapturePool(RndCustomizationItemsManager mgr)
    {
        ContractPredictor.Capture(mgr);
        var contract = Il2Cpp.GameValues.instance?.RandomMazeContract;
        var maze = Il2Cpp.MazeGenerator.instance;
        // The end-to-end check simulates the whole maze again, so it only runs with DebugMazeTests.
        if (contract == null || maze == null || !this.config.DebugMazeTests.Value) return;
        // Failed maze attempts shift the seeds; the maze simulation should land on the same ones.
        var (seed, second, simulated) = ContractPredictor.MazeSeeds(contract);
        bool seedsOk = seed == maze.seed && second == maze.secondSeed;
        LoggerInstance.Msg($"🔮Lobby prediction for this contract: {ContractPredictor.Predict(contract, out _, out _)?.ToString() ?? "none"}"
            + $" | 🎯Pipeline {(!simulated ? "NOT SIMULATED" : seedsOk ? "MATCH" : "MISMATCH")} predicted {seed}/{second} actual {maze.seed}/{maze.secondSeed} (contract {contract.Seed}/{contract.SecondSeed})");
    }

    internal void ShowPrediction(ContractUI ui)
    {
        var contract = ui.Contract;
        if (contract == null || ui.infoText == null) return;

        string line;
        bool simulated = false;
        if (!ContractPredictor.HasPool) line = "Cosmetic: ? (play one map to learn the pool)";
        else if (ContractPredictor.Predict(contract, out _, out simulated) is not int id) line = "Cosmetic: none";
        else if (!this.config.RevealAll.Value && this.SaveInstance != null && !this.SaveInstance.IsItemUnlocked((ushort)id)) line = "Cosmetic: Undiscovered Item";
        else if (this.itemsCollection != null && id < this.itemsCollection.collection.Length) line = "Cosmetic: " + ItemAlertUI.Describe(this.itemsCollection.collection[id], (ushort)id);
        else line = $"Cosmetic: #{id}";

        // "?" = this maze type was never captured, so the prediction assumes the maze builds on its first attempt.
        ui.infoText.text += line + (ContractPredictor.HasPool && !simulated ? " ?" : ""); // UpdateUI rebuilds the text each call and already ends with a newline
    }

    internal void UpdateSave(CustomizationSave save)
    {
        if (save != null) this.SaveInstance = save;
    }
}

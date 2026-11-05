using Il2CppCharacterCustomization;
using Il2CppRandomGeneration.Contracts;
using Il2CppValkoGames.Labyrinthine.UI.Customization;
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
    private CustomizationUISlot menuSlot; // any slot (prefab included) carries the menu's rarity splash sprites/colours

    // Settings
    private GameplayConfig config;

    public override void OnInitializeMelon()
    {
        config = ConfigManager.Instance;
        Instance = this; // Store a reference to the instance
        LoggerInstance.Msg($"TellMeCosmetics Mod Loaded!!!");
        // Read every maze scene from disk in the background so the lobby can predict mazes never entered.
        ScenePreload.Start(config.DebugMazeTests.Value);
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
        var contract = Il2Cpp.GameValues.instance?.RandomMazeContract;
        var pool = contract == null ? null : ContractPredictor.Capture(mgr, (int)contract.MazeType);
        if (pool != null && this.config.DebugMazeTests.Value) ScenePreload.Check((int)contract.MazeType, null, pool);
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

        var view = new ContractItemUI.View();
        bool simulated = false;
        int? predicted = ContractPredictor.Predict(contract, out _, out simulated);
        // "?" until this maze type's pool and generation are learned: unsimulated seeds assume a first-attempt build and are often wrong.
        // Normal cases rarely drop a cosmetic, so they show nothing instead; only Rare/Hardcore get the "?".
        if (!simulated) { if (contract.ContractType != ContractType.Normal) view.AltIcon = "?"; }
        else if (predicted is not int id) { } // no cosmetic: show nothing
        else
        {
            var item = this.itemsCollection != null && id < this.itemsCollection.collection.Length ? this.itemsCollection.collection[id] : null;
            bool isNew = this.SaveInstance != null && !this.SaveInstance.IsItemUnlocked((ushort)id);
            bool locked = isNew && !this.config.RevealAll.Value;
            bool music = item != null && item.BodyPart == BodyPart.Music;
            view.Icon = music ? null : item?.Icon;
            view.AltIcon = music ? "♫" : "?";
            view.Silhouette = locked;
            view.New = isNew;
            // Locked items get no splash, so the colour cannot reveal their rarity.
            // Same splash as the customization menu (CustomizationUISlot.SetItem): backgroundSprites[rarity] tinted backgroundColors[rarity].
            if (this.menuSlot == null) this.menuSlot = Resources.FindObjectsOfTypeAll<CustomizationUISlot>().FirstOrDefault();
            var slot = this.menuSlot;
            int rarity = item == null || locked ? -1 : (int)item.ItemRarity;
            if (slot != null && rarity >= 0 && rarity < slot.backgroundColors.Length && rarity < slot.backgroundSprites.Length)
            {
                // Only special rarities have their own sprite; the rest use the slot image's own splash
                // (defaultBackgroundSprite is copied from it in Init, so it is still null on the prefab).
                view.Frame = slot.backgroundSprites[rarity] ?? slot.itemBackgroundImage?.sprite;
                view.FrameColor = slot.backgroundColors[rarity];
            }
        }
        ContractItemUI.Show(ui, view);
    }

    internal void UpdateSave(CustomizationSave save)
    {
        if (save != null) this.SaveInstance = save;
    }
}

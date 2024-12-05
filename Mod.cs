using MelonLoader;
using UnityEngine;
using System.Threading;

using Il2CppCharacterCustomization;

using TellMeCosmetics;
using TellMeCosmetics.UI;
using System;

[assembly: MelonInfo(typeof(CustomizationPickupFinderMod), "Tell Me Cosmetics", "0.0.1", "LimitBRK")]
[assembly: MelonGame("Valko Game Studios", "Labyrinthine")]

namespace TellMeCosmetics;
public class CustomizationPickupFinderMod : MelonMod
{
    private ItemAlertUI alertui;

    public override void OnInitializeMelon()
    {
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
                this.alertui.Clear();
            });
            t.Start();
        }

        // Change UI
        if (this.alertui != null && buildIndex >= 4 && sceneName.Contains("Maze"))
        {
            LoggerInstance.Msg($"Start Search in {sceneName}");
            Thread t = new Thread(() =>
            {
                int tried = 0;
                while (tried++ < 5)
                {
                    CustomizationPickup customItem = GameObject.FindObjectOfType<CustomizationPickup>();
                    if (customItem != null)
                    {
                        LoggerInstance.Msg($"Found Cosmetic ItemID: ({customItem.ItemID}){customItem.name}");
                        this.alertui.Show(customItem);
                        break;

                        // This section try to get Item name/Item image -- Blocked By ObsecureShort that make I cannot access
                        // RndInventoryManager inventory = GameObject.Find("Global/Rnd Inventory Manager").GetComponent<RndInventoryManager>();
                        // List<StoreCustomizationItemSO> items = inventory.GetItemsByIDs<StoreCustomizationItemSO>();
                        
                    }
                    LoggerInstance.Msg($"Not Found. Retry: {tried}");
                    Thread.Sleep(5000);
                }
            });
            t.Start();
        }
    }
}

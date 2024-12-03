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
    public static CustomizationPickup CustomizationPickup { get; set; }

    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg($"TellMeCosmetics Mod Loaded!!!");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        if (this.alertui == null) {
            try {
                RectTransform ui = GameObject.Find("Global/Global Canvas/Player_UI").GetComponent<RectTransform>();
                if(ui != null) {
                    LoggerInstance.Msg($"Initialization UI...");
                    this.alertui = new ItemAlertUI(ui);
                    LoggerInstance.Msg($"Created UI Successfully");
                }
            } 
            catch (NullReferenceException)
            {
                // SKIP if null
            }
        }

        // Source of map name = Il2CppRandomGeneration.Contracts.MazeType
        if (this.alertui != null && buildIndex >= 4 && sceneName.Contains("Maze"))
        {
            LoggerInstance.Msg($"Start Search in {sceneName}");
            CustomizationPickup = null;
            Thread t = new Thread(() =>
            {
                int tried = 0;
                while (tried++ < 6)
                {
                    CustomizationPickup = GameObject.FindObjectOfType<CustomizationPickup>();
                    if (CustomizationPickup != null)
                    {
                        LoggerInstance.Msg($"Found Cosmetic ItemID: ({CustomizationPickup.ItemID}){CustomizationPickup.name}");
                        this.alertui.ShowUIwithCooldown(CustomizationPickup.name);
                        break;

                        // This section try to get Item name/Item image
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

using System;
using Il2CppCharacterCustomization;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace TellMeCosmetics.UI;
public class ItemAlertUI
{
    private readonly CustomizationItem[] itemsCollection;
    private readonly GameObject ui_object;
    private readonly TMP_FontAsset fontref;

    // UI Instance 
    private readonly TextMeshProUGUI myText;
    private readonly GameObject iconObject;
    private readonly Image iconImage;

    public ItemAlertUI(RectTransform parentUI, ItemsCollectionSO itemsCollectionSO) 
    {
        this.fontref = parentUI.Find("Ping Text")?.GetComponent<TextMeshProUGUI>()?.font;
        
        // TODO: more readable UI
        this.itemsCollection = itemsCollectionSO.collection;
        MelonLogger.Msg($"Loaded {this.itemsCollection.Length} cosmetic assets!");

        // Create a new panel
        this.ui_object = new GameObject("ItemAlert UI");
        this.ui_object.SetActive(false);
        this.ui_object.transform.SetParent(parentUI, false);

        // Set the RectTransform properties
        RectTransform rectTransform = this.ui_object.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0, 0);
        rectTransform.anchorMax = new Vector2(0, 0);
        rectTransform.pivot = new Vector2(0, 0);
        rectTransform.anchoredPosition = new Vector2(0, 0);
        rectTransform.sizeDelta = new Vector2(0, 50);

        HorizontalLayoutGroup layout = this.ui_object.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.spacing = 10;

        // Add Image component for icon display
        this.iconObject = new("Item Icon");
        this.iconObject.transform.SetParent(this.ui_object.transform, false);
        this.iconObject.SetActive(false);
        this.iconImage = this.iconObject.AddComponent<Image>();
        this.iconImage.preserveAspect = true;
        this.iconImage.sprite = null;
        RectTransform imageRect = this.iconObject.GetComponent<RectTransform>();
        imageRect.sizeDelta = new Vector2(64, 64);
        
        // Add Text component for displaying item name
        GameObject textObject = new("Item Name");
        textObject.transform.SetParent(this.ui_object.transform, false);
        this.myText = textObject.AddComponent<TextMeshProUGUI>();
        this.myText.font = fontref;
        this.myText.fontSize = 36;
        this.myText.alignment = TextAlignmentOptions.MidlineLeft;
        this.myText.overflowMode = TextOverflowModes.Overflow;
    }

    public void Show(CustomizationPickup item, bool reveal = true) 
    {
        // Check if components are initialized
        if (this.ui_object == null || this.myText == null || this.iconImage == null) return;

        if (!reveal) {
            this.SetItemUI(null, $"? Undiscovered Item");
        } else if (this.itemsCollection != null && itemsCollection.Length >= 0){
            if (item.ItemID >= itemsCollection.Length){
                throw new Exception("Out of itemID index to search");
            }
            // DEV Still use array index as itemId
            CustomizationItem itemData = this.itemsCollection[item.ItemID];
            string basedItemDataname = ""
                + $"#{item.ItemID} "
                + $"{(!string.IsNullOrEmpty(itemData.Name) ? itemData.Name : itemData.icon.name).Replace("_"," ")} "
                + $"({itemData.ItemRarity})";
            switch (itemData.BodyPart){
                case BodyPart.Music:
                    this.SetItemUI(null, $"♫ {basedItemDataname}");
                    break;
                default:
                    this.SetItemUI(itemData.Icon, basedItemDataname);
                    break;
            }
        } else {
            this.SetItemUI(null, $"#{item.ItemID} {FilterClone(item.name)}");
        }
    }

    private void SetItemUI(Sprite itemSprite, string itemText){
        this.iconImage.sprite = itemSprite; 
        this.iconObject.SetActive(itemSprite != null);
        this.myText.text = itemText;
        this.ui_object.SetActive(true);
    }

    public void Clear() 
    {
        if(this.ui_object == null || this.myText == null) return; 
        this.ui_object.SetActive(false);
        this.iconImage.sprite = null;  // Clear the icon
        this.myText.text = string.Empty;
    }

    public bool isDestroy() 
    {
        return this.ui_object == null;
    }

    private static string FilterClone(string name)
    {
        const string cloneSuffix = "(Clone)";
        if (name.EndsWith(cloneSuffix))
        {
            return name[..^cloneSuffix.Length].Replace("_"," ");
        }
        return name;
    }
}
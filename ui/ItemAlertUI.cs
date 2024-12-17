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
    private readonly TMP_FontAsset fontref;

    // UI Instances
    private readonly GameObject ui_object;
    private readonly TextMeshProUGUI myText;
    private readonly GameObject iconObject;
    private readonly Image iconImage;
    private readonly GameObject iconAltObject;
    private readonly TextMeshProUGUI iconAltImage;

    // Value 
    private ushort itemID;

    public ItemAlertUI(RectTransform parentUI, ItemsCollectionSO itemsCollectionSO) 
    {
        this.fontref = parentUI.Find("Ping Text")?.GetComponent<TextMeshProUGUI>()?.font;
        
        // TODO: more readable UI
        this.itemsCollection = itemsCollectionSO?.collection;
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

        this.iconAltObject = new("Item Icon Alt");
        this.iconAltObject.transform.SetParent(this.ui_object.transform, false);
        this.iconAltObject.SetActive(false);
        this.iconAltImage = this.iconAltObject.AddComponent<TextMeshProUGUI>();
        this.iconAltImage.font = fontref;
        this.iconAltImage.fontSize = 60;
        this.iconAltImage.richText = false;
        this.iconAltImage.fontWeight = FontWeight.Black;
        this.iconAltImage.alignment = TextAlignmentOptions.Midline;
        RectTransform imageAltRect = this.iconAltObject.GetComponent<RectTransform>();
        imageAltRect.sizeDelta = new Vector2(64, 64);
        
        // Add Text component for displaying item name
        GameObject textObject = new("Item Name");
        textObject.transform.SetParent(this.ui_object.transform, false);
        this.myText = textObject.AddComponent<TextMeshProUGUI>();
        this.myText.font = fontref;
        this.myText.fontSize = 36;
        this.myText.richText = false;
        this.myText.alignment = TextAlignmentOptions.MidlineLeft;
        this.myText.overflowMode = TextOverflowModes.Overflow;
    }

    private void SetItemUI(string itemText, Sprite icon = null, char altIcon = '?'){
        this.myText.text = itemText;
        this.myText.fontStyle = FontStyles.Normal;

        this.iconImage.sprite = icon; 
        this.iconObject.SetActive(icon != null);

        this.iconAltImage.text = (icon == null) ? altIcon.ToString() : string.Empty; 
        this.iconAltObject.SetActive(icon == null);

        this.ui_object.SetActive(true);
    }
    
    public void Show(CustomizationPickup item, bool reveal = true) 
    {
        this.itemID = item.ItemID;
        // Check if components are initialized
        if (this.ui_object == null || this.myText == null || this.iconImage == null) return;

        if (!reveal) {
            this.SetItemUI("Undiscovered Item");
        } else if (this.itemsCollection != null && itemsCollection.Length >= 0){
            if (this.itemID >= itemsCollection.Length){
                throw new Exception("Out of itemID index to search");
            }
            // DEV Still use array index as itemId
            CustomizationItem itemData = this.itemsCollection[this.itemID];
            string basedItemDataname = ""
                + $"#{this.itemID} "
                + $"{(!string.IsNullOrEmpty(itemData.Name) ? itemData.Name : itemData.icon.name).Replace("_"," ")} "
                + $"({itemData.ItemRarity})";
            switch (itemData.BodyPart){
                case BodyPart.Music:
                    this.SetItemUI(basedItemDataname, altIcon: '♫');
                    break;
                default:
                    this.SetItemUI(basedItemDataname, itemData.Icon);
                    break;
            }
        } else {
            this.SetItemUI($"#{this.itemID} {FilterClone(item.name)}");
        }
    }

    public void Pickup(ushort itemID){
        if (this.itemID == itemID) {
            MelonLogger.Msg("Picked item up");
            this.myText.fontStyle = FontStyles.Strikethrough;
        }
    }

    public void Clear() 
    {
        if(this.ui_object == null || this.myText == null) return; 
        this.ui_object.SetActive(false);
        
        this.myText.text = string.Empty;
        this.myText.fontStyle = FontStyles.Normal;

        this.iconImage.sprite = null;  // Clear the icon
        this.iconObject.SetActive(false);

        this.iconAltImage.text = string.Empty; 
        this.iconAltObject.SetActive(false);
    }

    public bool IsDestroy() 
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
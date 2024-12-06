using System;
using System.Linq;
using Il2CppCharacterCustomization;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace TellMeCosmetics.UI;
public class ItemAlertUI
{
    private readonly CustomizationItem[] itemsCollection;
    private readonly GameObject ui_object;
    private readonly Text myText;
    private readonly Image iconImage;

    public ItemAlertUI(RectTransform parentUI, ItemsCollectionSO itemsCollectionSO) 
    {
        // TODO: more readable UI
        this.itemsCollection = itemsCollectionSO.collection;
        MelonLogger.Msg($"Loaded {this.itemsCollection.Length} cosmetics!");

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

        HorizontalLayoutGroup layout = this.ui_object.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.LowerLeft;
        layout.spacing = 5;
        // layout.padding = new RectOffset(5, 5, 5, 5);

        // Add Image component for icon display
        GameObject imageObject = new("Icon");
        imageObject.transform.SetParent(this.ui_object.transform, false);
        this.iconImage = imageObject.AddComponent<Image>();
        this.iconImage.preserveAspect = true;
        this.iconImage.sprite = null;
        RectTransform imageRect = imageObject.GetComponent<RectTransform>();
        imageRect.sizeDelta = new Vector2(100, 100);
        
        // Add Text component for displaying item name
        GameObject textObject = new("Text");
        textObject.transform.SetParent(this.ui_object.transform, false);
        this.myText = textObject.AddComponent<Text>();
        this.myText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        this.myText.fontSize = 24;
        // myText.color = new Color(1f, 1f, 1f, 0.75f);
        this.myText.alignment = TextAnchor.MiddleLeft;
        this.myText.horizontalOverflow = HorizontalWrapMode.Overflow;
    }

    public void Show(CustomizationPickup item) 
    {
        // Check if components are initialized
        if (this.ui_object == null || this.myText == null || this.iconImage == null) return;

        if(this.itemsCollection != null && itemsCollection.Length >= 0){
            if (item.ItemID >= itemsCollection.Length){
                throw new Exception("Out of itemID index to search");
            }
            // DEV Still use array index as itemId
            CustomizationItem itemData = this.itemsCollection[item.ItemID];
            this.iconImage.sprite = itemData.icon;
            this.myText.text = $"#{item.ItemID} {(!string.IsNullOrEmpty(itemData.Name) ? itemData.Name : itemData.icon.name).Replace("_"," ")} ({itemData.ItemRarity})";
        } else {
            this.iconImage.sprite = null;
            this.myText.text = $"#{item.ItemID} {FilterClone(item.name)}";
        }
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
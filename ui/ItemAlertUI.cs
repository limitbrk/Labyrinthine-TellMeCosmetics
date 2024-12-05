using Il2CppCharacterCustomization;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace TellMeCosmetics.UI;
public class ItemAlertUI
{
    CustomizationItem[] itemsCollection;
    GameObject ui_object;
    Text myText;
    Image iconImage;

    public ItemAlertUI(RectTransform parentUI, ItemsCollectionSO itemsCollectionSO) 
    {
        this.itemsCollection = itemsCollectionSO.collection;
        MelonLogger.Msg($"Loaded {this.itemsCollection.Length} cosmetics!");
        // Create a new panel
        this.ui_object = new GameObject("ItemAlert UI");
        this.ui_object.SetActive(false);
        this.ui_object.transform.SetParent(parentUI);

        // Add Image component for icon display
        this.iconImage = this.ui_object.AddComponent<Image>();
        this.iconImage.preserveAspect = true;
        
        // TODO: Still Bug this step
        // Add Text component for displaying item name
        this.myText = this.ui_object.AddComponent<Text>();
        this.myText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        this.myText.fontSize = 24;
        // myText.color = new Color(1f, 1f, 1f, 0.75f);
        this.myText.alignment = TextAnchor.MiddleLeft;
        this.myText.horizontalOverflow = HorizontalWrapMode.Overflow;

        // Layout the UI elements in a horizontal layout group
        HorizontalLayoutGroup layoutGroup = this.ui_object.AddComponent<HorizontalLayoutGroup>();
        layoutGroup.spacing = 10;  // Space between icon and text
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childForceExpandWidth = false;

        // Set the RectTransform properties
        RectTransform rectTransform = this.ui_object.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0, 0);
        rectTransform.anchorMax = new Vector2(0, 0);
        rectTransform.pivot = new Vector2(0, 0);
        rectTransform.anchoredPosition = new Vector2(10, 10);
    }

    public void Show(CustomizationPickup cosmetic) 
    {
        // Check if components are initialized
        if (this.ui_object == null || this.myText == null || this.iconImage == null)
        {
            return;
        }

        if(this.itemsCollection != null){
            // DEV Still use array index as itemId
            this.iconImage.sprite = this.itemsCollection[cosmetic.itemID].icon;
            this.myText.text = $"[{cosmetic.itemID}] {itemsCollection[cosmetic.itemID].icon.name}";
        } else {
            string filteredName = cosmetic.name.Replace("(Clone)","");
            this.iconImage.sprite = null; // Use a default sprite or handle accordingly
            this.myText.text = $"[{cosmetic.itemID}] {filteredName}";
        }
        this.ui_object.SetActive(true);
    }

    public void Clear() 
    {
        if(this.ui_object == null && this.myText == null){
            return;
        }
        this.ui_object.SetActive(false);
        this.iconImage.sprite = null;  // Clear the icon
        this.myText.text = string.Empty;
    }
}
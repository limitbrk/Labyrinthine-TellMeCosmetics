using Il2CppCharacterCustomization;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace TellMeCosmetics.UI;
public class ItemAlertUI
{
    ItemsCollectionSO itemsCollectionSO;
    GameObject ui_object;
    Text myText;

    public ItemAlertUI(RectTransform parentUI, ItemsCollectionSO itemsCollectionSO) 
    {
        this.itemsCollectionSO = itemsCollectionSO;
        // Create a new panel
        this.ui_object = new GameObject("ItemAlert UI");
        this.ui_object.SetActive(false);
        this.ui_object.transform.SetParent(parentUI);

        this.myText = this.ui_object.AddComponent<Text>();
        this.myText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        this.myText.fontSize = 24;
        // myText.color = new Color(1f, 1f, 1f, 0.75f);
        this.myText.alignment = TextAnchor.MiddleLeft;
        this.myText.horizontalOverflow = HorizontalWrapMode.Overflow;

        RectTransform rectTransform = this.ui_object.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0, 0);
        rectTransform.anchorMax = new Vector2(0, 0);
        rectTransform.pivot = new Vector2(0, 0);
        rectTransform.anchoredPosition = new Vector2(10, 10);
    }

    public void Show(CustomizationPickup cosmetic) 
    {
        // Create a new panel
        if(this.ui_object == null && this.myText == null){
            return;
        }
        if(this.itemsCollectionSO != null){
            MelonLogger.Msg("Searching ITEM in collection");
            itemsCollectionSO.TryGetItem(cosmetic.itemID, out CustomizationItem customItem);
            MelonLogger.Msg("Found ITEM in collection");
            this.myText.text = $"{cosmetic.itemID}: {customItem.Name}";
        } else {
            string filteredName = cosmetic.name.Replace("(Clone)","");
            this.myText.text = $"{cosmetic.itemID}: {filteredName}";
        }
        this.ui_object.SetActive(true);
    }

    public void Clear() 
    {
        if(this.ui_object == null && this.myText == null){
            return;
        }
        this.ui_object.SetActive(false);
        this.myText.text = null;
    }
}
using System.Collections;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace TellMeCosmetics.UI;
public class ItemAlertUI
{
    GameObject ui_object;
    public ItemAlertUI(RectTransform tf) 
    {
        // Create a new panel
        this.ui_object = new GameObject("ItemAlert UI");
        this.ui_object.SetActive(false);
        this.ui_object.transform.SetParent(tf);

        Text myText;
        myText = this.ui_object.AddComponent<Text>();
        myText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        myText.fontSize = 24;
        // myText.color = new Color(1f, 1f, 1f, 0.75f);
        myText.alignment = TextAnchor.MiddleLeft;
        myText.horizontalOverflow = HorizontalWrapMode.Overflow;

        RectTransform rectTransform = this.ui_object.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0, 0);
        rectTransform.anchorMax = new Vector2(0, 0);
        rectTransform.pivot = new Vector2(0, 0);
        rectTransform.anchoredPosition = new Vector2(10, 10);
    }

    public void ShowUIwithCooldown(string itemname) 
    {
        // Create a new panel
        Text myText = this.ui_object.GetComponent<Text>();
        if(myText == null){

        }
        myText.text = itemname.Replace("(Clone)","");
        this.ui_object.SetActive(true);
        MelonCoroutines.Start(SelfDestruct(this.ui_object));
    }

    private static IEnumerator SelfDestruct(GameObject ui)
    {
        yield return new WaitForSeconds(30f);
        ui.SetActive(false);
    }
}
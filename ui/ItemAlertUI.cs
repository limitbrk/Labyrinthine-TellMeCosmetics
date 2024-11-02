using System.Collections;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace TellMeCosmetics.UI;
public class ItemAlertUI
{
    public static void CreateUI(RectTransform tf, string itemname) 
    {
        // Create a new panel
        GameObject ui = new GameObject("ItemAlert UI");
        ui.transform.SetParent(tf);

        Text myText;
        myText = ui.AddComponent<Text>();
        myText.text = itemname.Replace("(Clone)","");
        myText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        myText.fontSize = 24;
        myText.color = new Color(1f, 1f, 1f, 0.75f);
        myText.alignment = TextAnchor.MiddleLeft;
        myText.horizontalOverflow = HorizontalWrapMode.Overflow;

        RectTransform rectTransform = ui.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0, 0);
        rectTransform.anchorMax = new Vector2(0, 0);
        rectTransform.pivot = new Vector2(0, 0);
        rectTransform.anchoredPosition = new Vector2(10, 10);

        MelonCoroutines.Start(SelfDestruct(ui));
    }

    private static IEnumerator SelfDestruct(GameObject ui)
    {
        yield return new WaitForSeconds(30f);
        GameObject.Destroy(ui);
    }
}
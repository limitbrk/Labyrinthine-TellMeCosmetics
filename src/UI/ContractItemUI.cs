using Il2CppRandomGeneration.Contracts;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TellMeCosmetics.UI;

// Predicted cosmetic drawn on a contract card, below its photo, as a world-space canvas parented to the card.
// Paper and icon are HDRP/Lit quads so they follow the scene lighting like the contract; text stays UI (dark ink never glows).
public static class ContractItemUI
{
    private const string Name = "TMC Contract Cosmetic";
    // Polaroid layout: paper border (thicker at the bottom), black square with the item, NEW badge on the top-right corner.
    private const float Width = 100f, IconHeight = 100f, LabelHeight = 14f, Border = 6f; // canvas units
    private static readonly Vector2 BadgePos = new(31f, (IconHeight + LabelHeight) / 2f - 5f), BadgeSize = new(46f, 18f); // canvas units, from centre

    public sealed class View
    {
        public Sprite Icon;       // null → AltIcon text is shown instead
        public Sprite Frame;      // rarity ink splash behind the item, as in the customization menu
        public Color FrameColor = Color.white; // rarity colour the splash sprite is tinted with
        public string AltIcon;    // "?" or "♫"
        public bool Silhouette;   // item not unlocked yet
        public bool New;          // not unlocked yet → NEW badge
    }

    public static void Show(ContractUI ui, View view)
    {
        var root = ui.transform.Find(Name)?.gameObject ?? Create(ui);
        bool any = view.Icon != null || !string.IsNullOrEmpty(view.AltIcon);
        root.SetActive(any);
        if (!any)
        {
            // No card: put any seal we moved back where the game placed it.
            foreach (var stamp in ui.stamps ?? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject>(0))
                if (stamp != null) stamp.transform.position = SealHome(stamp.transform).position;
            return;
        }
        Place(ui, root.transform);

        var mat = root.transform.Find("Paper").GetComponent<MeshRenderer>().sharedMaterial;
        mat.mainTexture = Compose(mat.mainTexture?.TryCast<RenderTexture>(), view);

        var alt = root.transform.Find("Alt").GetComponent<TextMeshProUGUI>();
        alt.text = view.Icon == null ? view.AltIcon ?? "" : "";

        root.transform.Find("Badge").gameObject.SetActive(view.New);
    }

    private static GameObject Create(ContractUI ui)
    {
        var root = new GameObject(Name);
        root.transform.SetParent(ui.transform, false);
        root.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        root.AddComponent<CanvasGroup>().interactable = false;
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(Width, IconHeight + LabelHeight);

        // Paper note hiding the busy contract print; local +z points into the contract, so it sits just behind the canvas plane.
        Quad(root, "Paper", Vector2.zero, new Vector2(PaperW, PaperH), 0.05f, Lit());

        var alt = Text(ui, Child(root, "Alt", new Vector2(0, LabelHeight / 2), new Vector2(Width, IconHeight)), 80);
        alt.fontStyle = FontStyles.Bold;
        alt.color = new Color(0.85f, 0.85f, 0.8f); // sits on the black square
        alt.color = new Color(0.85f, 0.85f, 0.8f); // sits on the black square

        // Badge text only; its red tag is painted into the lit paper texture by Compose.
        var badge = Text(ui, Child(root, "Badge", BadgePos, BadgeSize), 14);
        badge.text = "NEW";
        badge.fontStyle = FontStyles.Bold;
        badge.color = Color.white;
        return root;
    }

    private static GameObject Child(GameObject root, string name, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return go;
    }

    private static void Quad(GameObject root, string name, Vector2 pos, Vector2 size, float z, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>()); // must not block clicks on the contract
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = new Vector3(pos.x, pos.y, z);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static Material Lit()
    {
        var m = new Material(Shader.Find("HDRP/Lit"));
        m.SetFloat("_Smoothness", 0f); // matte like paper
        return m;
    }

    private const float PaperW = Width + 2 * Border, PaperH = IconHeight + LabelHeight + 2 * Border; // canvas units
    private const int PxPerUnit = 2;

    // The game's HDRP/Lit build has no alpha-test variant, so the icon is blended onto the paper texture instead
    // and the result is shown on one lit quad. shortcut: the RenderTexture is not released when the lobby unloads, a few KB per card visit.
    private static RenderTexture Compose(RenderTexture rt, View view)
    {
        int w = (int)PaperW * PxPerUnit, h = (int)PaperH * PxPerUnit;
        rt ??= new RenderTexture(w, h, 0);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, w, h, 0); // top-left origin, y down
        GL.Clear(true, true, Color.clear);
        Graphics.DrawTexture(new Rect(0, 0, w, h), PaperTexture());
        var photo = new Rect(Border * PxPerUnit, Border * PxPerUnit, Width * PxPerUnit, IconHeight * PxPerUnit);
        // DrawTexture colour: 0.5 means unchanged. Texture2D.blackTexture is fully transparent, so tint white instead.
        // Black square like the customization menu; Texture2D.blackTexture is fully transparent, so tint white instead.
        Graphics.DrawTexture(photo, Texture2D.whiteTexture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, new Color(0f, 0f, 0f, 0.5f));

        var plain = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        // Stretched over the whole square like the menu's Image (no preserveAspect).
        if (!view.Silhouette && view.Frame != null)
        {
            var tex = view.Frame.texture;
            var r = view.Frame.textureRect;
            Graphics.DrawTexture(photo, tex, new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height), 0, 0, 0, 0, view.FrameColor * 0.5f);
        }
        // GUI/Text Shader multiplies the tint as-is (no 0.5 convention), so pure white there.
        DrawSprite(view.Icon, photo, 0.8f, view.Silhouette ? Color.white : plain, view.Silhouette ? SilhouetteMaterial() : null);
        if (view.New)
        {
            var c = new Vector2(PaperW / 2f + BadgePos.x, PaperH / 2f - BadgePos.y) * PxPerUnit; // canvas (y up) → texture (y down)
            var s = BadgeSize * PxPerUnit;
            Graphics.DrawTexture(new Rect(c - s / 2f, s), Texture2D.whiteTexture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, new Color(0.45f, 0.06f, 0.05f, 0.5f));
        }
        GL.PopMatrix();
        RenderTexture.active = prev;
        return rt;
    }

    // Sprite centred in the area at `fill` of its size, aspect kept; sprites live in atlases so only their textureRect is drawn.
    private static void DrawSprite(Sprite sprite, Rect area, float fill, Color tint, Material mat = null)
    {
        if (sprite == null) return;
        var tex = sprite.texture;
        var r = sprite.textureRect;
        float slot = Mathf.Min(area.width, area.height) * fill;
        float iw = r.width >= r.height ? slot : slot * r.width / r.height, ih = r.width >= r.height ? slot * r.height / r.width : slot;
        var src = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);
        Graphics.DrawTexture(new Rect(area.center.x - iw / 2f, area.center.y - ih / 2f, iw, ih), tex, src, 0, 0, 0, 0, tint, mat);
    }

    private static Material silhouette;

    // Locked items: the icon's alpha filled with plain white. DrawTexture's tint only multiplies, so it cannot whiten dark pixels.
    private static Material SilhouetteMaterial()
    {
        if (silhouette != null) return silhouette;
        silhouette = new Material(Shader.Find("GUI/Text Shader")) { color = Color.white };
        silhouette.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return silhouette;
    }

    private static TextMeshProUGUI Text(ContractUI ui, GameObject go, float size)
    {
        var t = go.AddComponent<TextMeshProUGUI>();
        t.font = ui.infoText.font;
        t.color = new Color(0.12f, 0.1f, 0.08f); // dark ink; the contract's own grey ink fades on the cream note
        t.fontMaterial.SetColor("_FaceColor", Color.white); // the shared font material's grey face colour would tint the ink
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = true;
        t.raycastTarget = false;
        return t;
    }

    // Right under the polaroid, between the paper and the top of the wax seal so the seal overlaps it: width ~55% of the photo, oriented like the card's text (whose forward points into the paper).
    private static void Place(ContractUI ui, Transform root)
    {
        var text = ui.infoText.transform;
        Vector3 right = text.right, up = text.up, forward = text.forward;
        var photo = ui.photoMeshFilters != null && ui.photoMeshFilters.Length > 0 ? ui.photoMeshFilters[0]?.GetComponent<Renderer>() : null;

        Vector3 photoBottom;
        float photoWidth;
        if (photo != null)
        {
            var b = photo.bounds;
            photoWidth = Extent(b.size, right);
            photoBottom = b.center - up * (Extent(b.size, up) / 2f);
        }
        else
        {
            // shortcut: no photo found, park it under the info text; tune if a card ever lacks photoMeshFilters.
            photoWidth = ui.infoText.rectTransform.rect.width * text.lossyScale.x;
            photoBottom = text.position - up * photoWidth;
        }

        float scale = photoWidth * 0.55f / Width;
        float height = (IconHeight + LabelHeight) * scale;
        var pos = photoBottom - up * (height / 2f + photoWidth * 0.01f);
        var seal = Seal(ui, text.position, forward);
        if (seal == null)
        {
            root.rotation = text.rotation;
            root.position = pos - forward * photoWidth * 0.002f;
        }
        else
        {
            // The contract sheet is tilted ~8° against the text, so a card in the text plane cuts into it. The sheet's normal is its
            // local right axis (seen in-game on every contract; seals are tilted randomly so theirs is unusable), and the seal's
            // centre is about the sheet surface.
            var t = seal.transform;
            var home = SealHome(t);
            var c = home.position + (seal.bounds.center - t.position); // seal centre at its original spot
            var n = ui.transform.right;
            if (Vector3.Dot(n, forward) < 0f) n = -n;
            var cardUp = Vector3.ProjectOnPlane(up, n).normalized;
            root.rotation = Quaternion.LookRotation(n, cardUp);
            // Card bottom edge on the seal's centre so the seal covers the card's lower edge; never moved up into the photo.
            float shift = Vector3.Dot(pos - c, cardUp) - height / 2f;
            if (shift > 0f) pos -= cardUp * shift;
            // Onto the sheet plane through the seal centre, lifted enough that the bent sheet never cuts a corner.
            root.position = pos - n * (Vector3.Dot(pos - c, n) + photoWidth * 0.06f); // at 0.02 the sheet still cut the top-left corner
            // The seal keeps the game's spot on the sheet and only rises off it: it is tilted ~13°, so it is lifted until its
            // lowest point clears the card; otherwise half of it sinks behind the card.
            float sealHalf = Extent(seal.bounds.size, n) / 2f;
            t.position += c - n * (Vector3.Dot(c - root.position, n) + sealHalf + photoWidth * 0.002f) - seal.bounds.center;
        }
        var parent = root.parent.lossyScale;
        root.localScale = new Vector3(scale / parent.x, scale / parent.y, scale / parent.z);
    }

    private static Texture2D paperTexture;

    // Procedural old-paper texture: cream base, Perlin fibres, fine grain, darker edges. Built once, kept across scenes.
    private static Texture2D PaperTexture()
    {
        if (paperTexture != null) return paperTexture;
        const int w = 108, h = 128, edge = 7;
        var tex = new Texture2D(w, h) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var rnd = new System.Random(1234);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fibre = (Mathf.PerlinNoise(x * 0.05f, y * 0.35f) - 0.5f) * 0.06f + (Mathf.PerlinNoise(x * 0.2f, y * 0.2f) - 0.5f) * 0.04f;
                float grain = ((float)rnd.NextDouble() - 0.5f) * 0.03f;
                int d = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                float aged = d < edge ? (edge - d) / (float)edge * 0.12f : 0f;
                float v = 1f + fibre + grain - aged;
                tex.SetPixel(x, y, new Color(0.94f * v, 0.91f * v, 0.82f * v, 1f));
            }
        tex.Apply();
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset; // scene loads unload unreferenced assets
        return paperTexture = tex;
    }

    // Empty marker beside a seal at the spot the game placed it, made the first time we see it, so moving the seal can be undone.
    // Lives in the scene, so it goes away with the lobby.
    private static Transform SealHome(Transform seal)
    {
        string name = "TMC Seal Home " + seal.name;
        var home = seal.parent.Find(name);
        if (home != null) return home;
        home = new GameObject(name).transform;
        home.SetParent(seal.parent, false);
        home.position = seal.position;
        return home;
    }

    // Active wax seal (ContractUI.stamps, e.g. "rare_stamp" with the wax_seal material, tallest above the text plane):
    // its renderer, or null when the contract has no seal.
    private static Renderer Seal(ContractUI ui, Vector3 plane, Vector3 forward)
    {
        float top = float.MinValue;
        Renderer seal = null;
        foreach (var stamp in ui.stamps ?? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject>(0))
            if (stamp != null && stamp.activeInHierarchy)
                foreach (var r in stamp.GetComponentsInChildren<Renderer>())
                {
                    var b = r.bounds;
                    float h = Vector3.Dot(b.center - plane, -forward) + Extent(b.size, forward) / 2f;
                    if (h <= top) continue; // the tallest renderer is the seal itself
                    top = h;
                    seal = r;
                }
        return seal;
    }

    private static float Extent(Vector3 size, Vector3 axis) =>
        Mathf.Abs(size.x * axis.x) + Mathf.Abs(size.y * axis.y) + Mathf.Abs(size.z * axis.z);
}

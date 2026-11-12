using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// A document the vampire can read (lore, the serum report). Hook Read() to an Interactable's onInteract.
/// The page covers the screen and freezes the game until F, E, Space or a click closes it.
/// </summary>
public class ReadableNote : MonoBehaviour
{
    public string title = "Report";
    [TextArea(4, 14)] public string body = "";

    static CanvasGroup page;
    static TMP_Text titleText, bodyText;
    bool open;

    public void Read()
    {
        if (open) return;
        Build();
        titleText.text = title;
        bodyText.text = body;
        page.alpha = 1f;
        open = true;
        GameState.Paused = true;   // stops the vampire and the police while he reads
        StartCoroutine(WaitForClose());
    }

    IEnumerator WaitForClose()
    {
        yield return null;   // the F that opened the page must not close it
        while (true)
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if ((kb != null && (kb.fKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                || (mouse != null && mouse.leftButton.wasPressedThisFrame)) break;
            yield return null;
        }
        page.alpha = 0f;
        yield return null;   // let that key press pass before the game runs again (no bite or re-read on the same press)
        GameState.Paused = false;
        open = false;
    }

    static void Build()
    {
        if (page != null) return;
        var root = new GameObject("NoteUI", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        page = root.AddComponent<CanvasGroup>();
        page.alpha = 0f;
        page.blocksRaycasts = page.interactable = false;

        var dim = Child(root.transform, "Dim");
        Stretch(dim, Vector2.zero, Vector2.one);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        var paper = Child(root.transform, "Paper");
        paper.sizeDelta = new Vector2(820f, 900f);
        paper.gameObject.AddComponent<Image>().color = new Color(0.86f, 0.81f, 0.70f);

        titleText = Text(paper, "Title", 46f, FontStyles.Bold, TextAlignmentOptions.Top);
        Stretch(titleText.rectTransform, new Vector2(0.08f, 0.86f), new Vector2(0.92f, 0.96f));
        bodyText = Text(paper, "Body", 28f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        Stretch(bodyText.rectTransform, new Vector2(0.08f, 0.1f), new Vector2(0.92f, 0.84f));
        var hint = Text(paper, "Hint", 22f, FontStyles.Italic, TextAlignmentOptions.Bottom);
        Stretch(hint.rectTransform, new Vector2(0.08f, 0.02f), new Vector2(0.92f, 0.08f));
        hint.text = "F  Close";
    }

    static RectTransform Child(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static TMP_Text Text(Transform parent, string name, float size, FontStyles style, TextAlignmentOptions align)
    {
        var t = Child(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = new Color(0.12f, 0.08f, 0.06f);
        t.raycastTarget = false;
        return t;
    }
}

using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A short message in the lower third of the screen: "Checkpoint", "Locked in", "New ability: Throw".
/// Call LevelToast.Show(title, subtitle) from anywhere; a new message replaces the current one.
/// </summary>
public class LevelToast : MonoBehaviour
{
    static LevelToast instance;

    CanvasGroup group;
    TMP_Text title, subtitle;
    Coroutine playing;

    public static void Show(string titleText, string subtitleText = "", float hold = 2.2f)
    {
        if (instance == null) instance = Build();
        instance.title.text = titleText;
        instance.subtitle.text = subtitleText;
        if (instance.playing != null) instance.StopCoroutine(instance.playing);
        instance.playing = instance.StartCoroutine(instance.Play(hold));
    }

    IEnumerator Play(float hold)
    {
        for (float t = group.alpha; t < 1f; t += Time.unscaledDeltaTime * 3f) { group.alpha = t; yield return null; }
        group.alpha = 1f;
        yield return new WaitForSecondsRealtime(hold);
        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime * 1.5f) { group.alpha = t; yield return null; }
        group.alpha = 0f;
        playing = null;
    }

    static LevelToast Build()
    {
        var root = new GameObject("LevelToast", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var toast = root.AddComponent<LevelToast>();
        toast.group = root.AddComponent<CanvasGroup>();
        toast.group.alpha = 0f;
        toast.group.blocksRaycasts = toast.group.interactable = false;
        toast.title = Text(root.transform, "Title", 44f, new Color(0.86f, 0.2f, 0.18f), 0.25f, 0.31f);
        toast.title.characterSpacing = 6f;
        toast.subtitle = Text(root.transform, "Subtitle", 26f, new Color(0.93f, 0.87f, 0.75f), 0.2f, 0.25f);
        return toast;
    }

    static TMP_Text Text(Transform parent, string name, float size, Color color, float yMin, float yMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, yMin); rt.anchorMax = new Vector2(1f, yMax);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }
}

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Special ability "Blood Leap" (key 1, 20 mana): the same targeting as Shadow Bite (a ring shows the reach, pick a human
/// with the mouse, left-click), but the vampire lands next to the human and drinks him dry instead of turning him.
/// A fast way to feed from a distance. Humans only; added to the vampire automatically like the other abilities.
/// </summary>
[DisallowMultipleComponent]
public class BloodLeapAbility : ThrallAbility
{
    protected override void Awake()
    {
        hotkey = Key.Digit1;
        range = 8f;
        cooldown = 10f;
        accent = new Color(1f, 0.22f, 0.2f);
        shadowColor = new Color(0.45f, 0.03f, 0.05f);
        base.Awake();
    }

    protected override string Title => "BLOOD LEAP";
    protected override string AimSubtitle => "Pick a human inside the ring to drink";
    protected override int Slot => 0;              // bottom left of the ability triangle
    protected override float ManaCost => 20f;
    protected override bool ShowsThrallTimer => false;
    protected override Color PoolGlow => new Color(0.85f, 0.08f, 0.10f);
    protected override Color DustColor => new Color(0.35f, 0.02f, 0.04f, 0.85f);
    protected override bool TargetsOfficers => false;
    protected override bool AtCapacity => false;
    protected override string CountLabel => "";
    protected override Sprite MakeIcon() => MakeLeapIcon(128);

    protected override IEnumerator BiteOnArrival(Transform target)
    {
        HumanAI human = target != null ? target.GetComponent<HumanAI>() : null;
        if (human == null || !human.CanBeBitten) yield break;   // he died or was taken meanwhile: the leap still lands
        yield return Controller.BiteHuman(human);
    }

    /// <summary>
    /// The icon: a drop of blood flying to the right like a comet (round head, tail streaming back), with speed lines
    /// behind it and two fangs in its head — a lunge that ends in a bite.
    /// </summary>
    static Sprite MakeLeapIcon(int size)
    {
        Texture2D tex = NewTexture(size, size);
        Color32[] px = new Color32[size * size];
        Vector2[] leftFang = { new Vector2(0.60f, 0.56f), new Vector2(0.67f, 0.56f), new Vector2(0.635f, 0.43f) };
        Vector2[] rightFang = { new Vector2(0.70f, 0.56f), new Vector2(0.77f, 0.56f), new Vector2(0.735f, 0.43f) };
        float[] rows = { 0.30f, 0.50f, 0.70f };
        float[] starts = { 0.10f, 0.02f, 0.12f };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float drop = 0f, fang = 0f;
                for (int sy = 0; sy < 3; sy++)
                    for (int sx = 0; sx < 3; sx++)
                    {
                        float u = (x + (sx + 0.5f) / 3f) / size, v = (y + (sy + 0.5f) / 3f) / size;
                        // Drop space: its round end sits at the right, its tip streams left.
                        float du = 0.5f + (v - 0.5f) / 0.62f, dv = 1f - (u - 0.06f) / 0.86f;
                        if (du >= 0f && du <= 1f && dv >= 0f && dv <= 1f && AbilityHUD.InDrop(du, dv)) drop += 1f / 9f;
                        Vector2 p = new Vector2(u, v);
                        if (InTriangle(p, leftFang) || InTriangle(p, rightFang)) fang += 1f / 9f;
                    }

                float uc = (x + 0.5f) / size, vc = (y + 0.5f) / size;
                // Three speed lines trailing behind the drop.
                float lines = 0f;
                for (int i = 0; i < rows.Length; i++)
                {
                    float thick = 1f - Mathf.Clamp01(Mathf.Abs(vc - rows[i]) / 0.022f);
                    float along = Mathf.Clamp01((uc - starts[i]) / 0.25f) * (1f - Mathf.Clamp01((uc - 0.30f) / 0.08f));
                    lines = Mathf.Max(lines, thick * along * 0.85f);
                }

                Color blood = Color.Lerp(new Color(0.45f, 0.0f, 0.03f), new Color(0.95f, 0.10f, 0.12f), Mathf.Clamp01(vc * 1.2f));
                float shine = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(uc, vc), new Vector2(0.78f, 0.66f)) / 0.07f);
                blood = Color.Lerp(blood, new Color(1f, 0.75f, 0.75f), shine * 0.8f);
                Color c = Color.Lerp(new Color(1f, 0.55f, 0.55f), blood, drop);
                c = Color.Lerp(c, Color.white, fang * drop);
                float a = Mathf.Max(drop, lines * (1f - drop));
                px[y * size + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(Mathf.Clamp01(a) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}

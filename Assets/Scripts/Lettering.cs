using TMPro;
using UnityEngine;

// The game's two lettering voices. Scream, Big Shoulders Display Black, shouts the events: the title,
// CLOSE ONE, TOO LATE. Protocol, Courier Prime Bold, types like a noir report: the score, "please",
// "tap to try again", the TAP hint and the CONTROLS menu. Both are drawn in black ink: a thick outline and
// a hard shadow down and to the right, no glow. One asset in Resources ("Lettering") holds the fonts; it is
// built by Tools > Lettering > Build Font Assets.
[CreateAssetMenu(menuName = "Wait a second/Lettering", fileName = "Lettering")]
public sealed class Lettering : ScriptableObject
{
    public enum Voice { Scream, ScreamHeavy, Protocol }

    [SerializeField] private TMP_FontAsset scream;
    [SerializeField, Tooltip("Scream ink with the longer shadow of TOO LATE.")]
    private Material screamHeavy;
    [SerializeField] private TMP_FontAsset protocol;
    [SerializeField, Tooltip("The protocol font as a plain font for the IMGUI menu, which cannot draw TextMeshPro.")]
    private Font protocolMenuFont;

    private static Lettering loaded;
    private static bool missingReported;

    // The protocol font for IMGUI; null keeps the default font.
    public static Font MenuFont => Load() != null ? loaded.protocolMenuFont : null;

    // A white caption in one voice that never catches taps or wraps.
    public static TextMeshProUGUI CreateText(Transform parent, string objectName, Voice voice, float size)
    {
        var text = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(parent, false);
        var assets = Load();
        if (assets != null)
        {
            text.font = voice == Voice.Protocol ? assets.protocol : assets.scream;
            if (voice == Voice.ScreamHeavy && assets.screamHeavy != null) text.fontSharedMaterial = assets.screamHeavy;
        }
        text.fontSize = size;
        text.color = Color.white;
        text.raycastTarget = false;
        text.richText = true;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    private static Lettering Load()
    {
        if (loaded == null) loaded = Resources.Load<Lettering>("Lettering");
        if (loaded == null && !missingReported)
        {
            missingReported = true;
            Debug.LogError("Lettering asset is missing from Resources; run Tools > Lettering > Build Font Assets.");
        }
        return loaded;
    }
}

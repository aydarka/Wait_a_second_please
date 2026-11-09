using TMPro;
using UnityEngine;

// "TAP" typed beside the yellow ring of the counter-shot, for a newcomer only: it shows until the player has
// fired twice by tapping. Missed windows do not count, and the count is kept on the phone across restarts.
// It stands to the right of the ring at its height, or to the left when the right has no room, and stays put
// while the ring closes. With several counter-shots waiting it marks the earliest, the one a tap fires.
[DisallowMultipleComponent]
public sealed class TapHint : MonoBehaviour
{
    private const string ShotsKey = "Lettering.TapHintShots";
    private const int HintShots = 2;
    private const string Word = "TAP";
    private const float Size = 64f;
    private const float GapEm = 0.15f;
    private const float SecondsPerChar = 0.04f;
    private const float CaretPeriod = 0.3f;
    private const float MissedHatchSeconds = 0.3f;

    private RectTransform area;
    private TMP_Text text;
    private LetterMotion motion;
    private bool showing;

    private static bool Needed => PlayerPrefs.GetInt(ShotsKey, 0) < HintShots;

    public static void ResetCount()
    {
        PlayerPrefs.DeleteKey(ShotsKey);
        PlayerPrefs.Save();
    }

    // The hint lives inside the safe area; its caption is made here.
    public static TapHint Create(RectTransform safeArea)
    {
        var text = Lettering.CreateText(safeArea, "TAP Hint", Lettering.Voice.Protocol, Size);
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        // The caret slot is part of the line, so its size never changes while it is typed.
        Vector2 preferred = text.GetPreferredValues(Word + "_");
        rect.sizeDelta = preferred;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        var hint = text.gameObject.AddComponent<TapHint>();
        hint.area = safeArea;
        hint.text = text;
        hint.motion = text.gameObject.AddComponent<LetterMotion>();
        return hint;
    }

    // Gone at once, for the death screen.
    public void Hide()
    {
        showing = false;
        motion.Cut();
    }

    private void OnEnable()
    {
        CounterShot.TapWindowOpened += WindowOpened;
        CounterShot.TapWindowClosed += WindowClosed;
    }

    private void OnDisable()
    {
        CounterShot.TapWindowOpened -= WindowOpened;
        CounterShot.TapWindowClosed -= WindowClosed;
    }

    private void WindowOpened(float seconds)
    {
        if (!showing && Needed) Show();
    }

    private void WindowClosed(bool fired)
    {
        if (fired)
        {
            PlayerPrefs.SetInt(ShotsKey, PlayerPrefs.GetInt(ShotsKey, 0) + 1);
            PlayerPrefs.Save();
        }
        if (!showing) return;
        if (CounterShot.AwaitingTap && Needed)
        {
            // Another shot still waits: the hint moves on to its ring, typed afresh after a shot.
            if (fired) Show();
            return;
        }
        showing = false;
        if (fired) motion.Cut();
        else motion.HatchOut(MissedHatchSeconds);
    }

    private void Show()
    {
        showing = true;
        Place();
        motion.Type(Word, 0f, SecondsPerChar, CaretPeriod);
    }

    private void LateUpdate()
    {
        if (showing) Place();
    }

    private void Place()
    {
        if (!CounterShot.TryGetTapRing(out Vector2 centre, out float radiusPixels)) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, centre, null, out Vector2 local)) return;
        var rect = text.rectTransform;
        float pixelsPerUnit = Mathf.Max(0.0001f, area.lossyScale.x);
        float reach = radiusPixels / pixelsPerUnit + GapEm * text.fontSize;
        Rect bounds = area.rect;
        Vector2 size = rect.sizeDelta;
        bool right = local.x + reach + size.x <= bounds.xMax;
        float x = right ? local.x + reach + size.x * 0.5f : local.x - reach - size.x * 0.5f;
        float y = Mathf.Clamp(local.y, bounds.yMin + size.y * 0.5f, bounds.yMax - size.y * 0.5f);
        // Anchored at the area's centre, so the local point is the anchored position.
        Vector2 offset = bounds.center;
        motion.RestPosition = new Vector2(x, y) - offset;
    }
}

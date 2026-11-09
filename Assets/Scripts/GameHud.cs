using TMPro;
using UnityEngine;

// The run's lettering on the HUD canvas. GameSession says what happened; this says it on screen:
// - the title at the start: "WAIT A SECOND," shouted, "please" typed under it on the right;
// - the score in the top-left corner;
// - CLOSE ONE stamped in yellow after a close dodge, in the upper band, clear of the horizon where enemies stand;
// - TOO LATE slammed in with the red of the death screen, then "tap to try again" typed under it;
// - the TAP hint for the counter-shot (TapHint).
// Everything stays inside the screen's safe area and moves in real time. Sizes are CanvasScaler units
// (1920 x 1080 reference); places are shares of the screen, as in the lettering moodboard.
[DisallowMultipleComponent]
public sealed class GameHud : MonoBehaviour
{
    private static readonly Color Yellow = new Color(1f, 0.86f, 0.1f);

    private const string TitleShout = "WAIT A SECOND,";
    private const string TitleWhisper = "please";
    private const string CloseWord = "CLOSE ONE";
    private const string DeathWord = "TOO LATE";
    private const string RestartWords = "tap to try again";

    private const float TitleSize = 127f;
    private const float PleaseSize = 59f;
    private const float ScoreSize = 46f;
    private const float CloseSize = 160f;
    private const float DeathSize = 183f;
    private const float PromptSize = 48f;

    // Shares of the screen height from the top edge, portrait / landscape.
    private const float TitleTopPortrait = 0.21f;
    private const float TitleTopLandscape = 0.23f;
    private const float CloseTopPortrait = 0.19f;
    private const float CloseTopLandscape = 0.17f;
    // "please" ends this share of the screen width before the right edge, portrait / landscape.
    private const float PleaseRightPortrait = 0.06f;
    private const float PleaseRightLandscape = 0.22f;
    private static readonly Vector2 ScoreOffset = new Vector2(48f, -24f);
    private const float CloseTilt = 4f;
    private const float PromptGap = 16f;
    // The death screen's lettering takes the half of the screen the red hole leaves free.
    private const float DeathUpperBand = 0.77f;
    private const float DeathLowerBand = 0.17f;
    private const float HoleLowLimit = 0.45f;

    private const float TitlePopDelay = 0.15f;
    private const float PleaseTypeDelay = 0.6f;
    private const float PleasePerChar = 0.07f;
    private const float TitleLeavesAt = 2.4f;
    private const float HatchSeconds = 0.3f;
    private const float CloseHold = 0.45f;
    private const float PromptPerChar = 0.028f;
    private const float CaretPeriod = 0.9f;

    private RectTransform area;
    private SafeAreaFrame frame;
    private TMP_Text score;
    private LetterMotion title;
    private LetterMotion please;
    private LetterMotion close;
    private LetterMotion death;
    private LetterMotion prompt;
    private TapHint tapHint;
    private int layoutVersion = -1;
    private Vector2 layoutSize;
    private float layoutScale;
    private bool deathInUpperBand;
    private int shownMetres = -1;
    private int shownDodged = -1;

    public static GameHud Create(Transform canvas)
    {
        var host = new GameObject("Lettering", typeof(RectTransform), typeof(SafeAreaFrame));
        host.transform.SetParent(canvas, false);
        var hud = host.AddComponent<GameHud>();
        hud.Build();
        return hud;
    }

    public void SetScore(float metres, int dodged)
    {
        int shown = Mathf.RoundToInt(metres);
        if (shown == shownMetres && dodged == shownDodged) return;
        shownMetres = shown;
        shownDodged = dodged;
        score.text = shown + " m · " + dodged + " dodged";
    }

    public void CloseOne()
    {
        // A title still on screen gives way, or CLOSE ONE would land on it.
        title.HatchOut(HatchSeconds);
        please.HatchOut(HatchSeconds);
        close.Stamp(CloseWord);
        close.HatchOut(HatchSeconds, CloseHold + 0.08f);
    }

    // The run is over; the hole closes around this viewport point.
    public void Death(Vector2 holeCentre)
    {
        title.Cut();
        please.Cut();
        close.Cut();
        tapHint.Hide();
        deathInUpperBand = holeCentre.y < HoleLowLimit;
        Layout();
        death.Slam(DeathWord);
    }

    public void ShowRestartPrompt() => prompt.Type(RestartWords, 0f, PromptPerChar, CaretPeriod);

    // The red leaves after the restart tap; the lettering goes with it.
    public void Leave(float seconds)
    {
        death.HatchOut(seconds);
        prompt.HatchOut(seconds);
    }

    private void Build()
    {
        area = (RectTransform)transform;
        frame = GetComponent<SafeAreaFrame>();

        score = Lettering.CreateText(area, "Score", Lettering.Voice.Protocol, ScoreSize);
        var scoreRect = score.rectTransform;
        scoreRect.anchorMin = scoreRect.anchorMax = scoreRect.pivot = new Vector2(0f, 1f);
        scoreRect.anchoredPosition = ScoreOffset;
        scoreRect.sizeDelta = new Vector2(1000f, ScoreSize * 1.5f);
        score.alignment = TextAlignmentOptions.TopLeft;

        title = Caption("Title", Lettering.Voice.Scream, TitleSize, TitleShout, new Vector2(0.5f, 1f), TextAlignmentOptions.Top);
        please = Caption("Please", Lettering.Voice.Protocol, PleaseSize, TitleWhisper + "_", new Vector2(1f, 1f), TextAlignmentOptions.TopRight);
        close = Caption("Close One", Lettering.Voice.Scream, CloseSize, CloseWord, new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center);
        close.GetComponent<TMP_Text>().color = Yellow;
        close.Tilt = CloseTilt;
        death = Caption("Too Late", Lettering.Voice.ScreamHeavy, DeathSize, DeathWord, new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center);
        prompt = Caption("Restart Prompt", Lettering.Voice.Protocol, PromptSize, RestartWords + "_", new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center);
        tapHint = TapHint.Create(area);

        Layout();
        title.Pop(TitleShout, TitlePopDelay);
        please.Type(TitleWhisper, PleaseTypeDelay, PleasePerChar, CaretPeriod);
        title.HatchOut(HatchSeconds, TitleLeavesAt);
        please.HatchOut(HatchSeconds, TitleLeavesAt);
    }

    // A hidden caption sized to the given line, anchored at the frame's centre.
    private LetterMotion Caption(string objectName, Lettering.Voice voice, float size, string line, Vector2 pivot,
        TextAlignmentOptions alignment)
    {
        var text = Lettering.CreateText(area, objectName, voice, size);
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = pivot;
        rect.sizeDelta = text.GetPreferredValues(line);
        text.alignment = alignment;
        return text.gameObject.AddComponent<LetterMotion>();
    }

    private void LateUpdate()
    {
        // The canvas scaler settles after the first frame, and the frame refits on rotation.
        if (frame.Version != layoutVersion || area.rect.size != layoutSize || area.lossyScale.x != layoutScale) Layout();
        GiveWayToRing();
    }

    private void Layout()
    {
        layoutVersion = frame.Version;
        layoutSize = area.rect.size;
        layoutScale = area.lossyScale.x;
        bool portrait = Screen.height >= Screen.width;

        float titleTop = AreaY(1f - (portrait ? TitleTopPortrait : TitleTopLandscape));
        title.RestPosition = new Vector2(0f, titleTop);
        float pleaseTop = titleTop - TitleSize * 0.95f - PleaseSize * 0.25f;
        please.RestPosition = new Vector2(AreaX(1f - (portrait ? PleaseRightPortrait : PleaseRightLandscape)), pleaseTop);

        float closeTop = AreaY(1f - (portrait ? CloseTopPortrait : CloseTopLandscape));
        close.RestPosition = new Vector2(0f, closeTop - Height(close) * 0.5f);

        // TOO LATE and its prompt as one block centred on the free band, kept inside the frame.
        float deathHeight = Height(death);
        float promptHeight = Height(prompt);
        float block = deathHeight + PromptGap + promptHeight;
        float half = area.rect.height * 0.5f;
        float centre = Mathf.Clamp(AreaY(deathInUpperBand ? DeathUpperBand : DeathLowerBand), -half + block * 0.5f, half - block * 0.5f);
        death.RestPosition = new Vector2(0f, centre + block * 0.5f - deathHeight * 0.5f);
        prompt.RestPosition = new Vector2(0f, centre - block * 0.5f + promptHeight * 0.5f);
    }

    // CLOSE ONE never covers the counter-shot ring: if the ring comes up under it, it leaves at once.
    private void GiveWayToRing()
    {
        if (!close.Visible || close.Leaving) return;
        if (!CounterShot.TryGetTapRing(out Vector2 centre, out float radius)) return;
        var corners = new Vector3[4];
        ((RectTransform)close.transform).GetWorldCorners(corners);
        Vector2 min = Vector2.Min(Vector2.Min(corners[0], corners[1]), Vector2.Min(corners[2], corners[3]));
        Vector2 max = Vector2.Max(Vector2.Max(corners[0], corners[1]), Vector2.Max(corners[2], corners[3]));
        Vector2 nearest = Vector2.Max(min, Vector2.Min(max, centre));
        if ((nearest - centre).sqrMagnitude <= radius * radius) close.HatchOut(HatchSeconds);
    }

    private static float Height(LetterMotion caption) => ((RectTransform)caption.transform).sizeDelta.y;

    // A share of the screen width or height as a position relative to the frame's centre.
    private float AreaX(float share) => ScreenToArea(new Vector2(share * Screen.width, 0f)).x;
    private float AreaY(float share) => ScreenToArea(new Vector2(0f, share * Screen.height)).y;

    private Vector2 ScreenToArea(Vector2 screenPoint)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screenPoint, null, out Vector2 local);
        return local - area.rect.center;
    }
}

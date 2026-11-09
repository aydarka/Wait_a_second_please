using TMPro;
using UnityEngine;

// How one caption comes and goes, always in real time so slow motion never slows it. It comes in with a
// pop, a stamp or a slam, or is typed letter by letter with a blinking caret; it leaves by hatching like
// the street fog or is cut at once. It never fades: on the lettering shader the text alpha is the share
// of each hatch stripe still standing (see Lettering.shader).
[RequireComponent(typeof(TMP_Text))]
public sealed class LetterMotion : MonoBehaviour
{
    private enum Entrance { None, Pop, Stamp, Slam }

    private const float PopScale = 1.6f;
    private const float PopSeconds = 0.12f;
    private const float StampScale = 1.5f;
    private const float StampExtraTilt = 6f;
    private const float StampSeconds = 0.08f;
    private const float SlamScale = 2.6f;
    private const float SlamSeconds = 0.35f;
    private const float JoltSeconds = 0.14f;
    private const int HatchSteps = 5;
    private const string Caret = "_";
    private const string Hidden = "<alpha=#00>";

    // Jolt after a slam: three hard steps, in shares of the caption's size.
    private static readonly Vector2[] Jolt = { new Vector2(-0.02f, -0.015f), new Vector2(0.015f, 0.01f), Vector2.zero };

    private TMP_Text text;
    private Entrance entrance;
    private float tilt;
    private float shownAt;
    private string typed;
    private float typePerChar;
    private float caretPeriod;
    private int typedShown = -1;
    private bool caretShown;
    private float leaveAt = float.PositiveInfinity;
    private float leaveSeconds;
    private bool gone = true;
    private Vector2 restPosition;

    public bool Visible => !gone && Time.unscaledTime >= shownAt && Time.unscaledTime < leaveAt + leaveSeconds;
    public bool Leaving => !gone && Time.unscaledTime >= leaveAt;

    // Resting place of the caption; motion offsets are added on top.
    public Vector2 RestPosition
    {
        get => restPosition;
        set { restPosition = value; Text.rectTransform.anchoredPosition = value; }
    }

    // Resting tilt in degrees, counter-clockwise.
    public float Tilt
    {
        get => tilt;
        set { tilt = value; Text.rectTransform.localRotation = Quaternion.Euler(0f, 0f, value); }
    }

    private TMP_Text Text => text != null ? text : text = GetComponent<TMP_Text>();

    private void Awake()
    {
        restPosition = Text.rectTransform.anchoredPosition;
        Text.alpha = 0f;
    }

    public void Pop(string content, float delay = 0f) => Begin(content, Entrance.Pop, delay);
    public void Stamp(string content, float delay = 0f) => Begin(content, Entrance.Stamp, delay);
    public void Slam(string content, float delay = 0f) => Begin(content, Entrance.Slam, delay);
    public void Show(string content, float delay = 0f) => Begin(content, Entrance.None, delay);

    // Types the content letter by letter; the caret appears with the first letter and blinks until the caption
    // leaves. The hidden letters and the caret keep their places, so the line never shifts while it is typed.
    public void Type(string content, float delay, float secondsPerChar, float caretBlinkPeriod)
    {
        Begin(null, Entrance.None, delay);
        typed = content;
        typePerChar = Mathf.Max(0.001f, secondsPerChar);
        caretPeriod = Mathf.Max(0.02f, caretBlinkPeriod);
        typedShown = -1;
        RenderTyped(0, false);
    }

    // Leaves by hatching: the stripes thin to nothing in hard steps.
    public void HatchOut(float seconds, float delay = 0f)
    {
        if (gone) return;
        float at = Time.unscaledTime + delay;
        if (at >= leaveAt) return;
        leaveAt = at;
        leaveSeconds = Mathf.Max(0.01f, seconds);
    }

    public void Cut()
    {
        gone = true;
        Text.alpha = 0f;
    }

    private void Begin(string content, Entrance kind, float delay)
    {
        gone = false;
        entrance = kind;
        shownAt = Time.unscaledTime + delay;
        leaveAt = float.PositiveInfinity;
        typed = null;
        if (content != null) Text.text = content;
        Text.alpha = 0f;
        Apply(Time.unscaledTime);
    }

    private void Update()
    {
        if (!gone) Apply(Time.unscaledTime);
    }

    private float EntranceSeconds => entrance switch
    {
        Entrance.Pop => PopSeconds,
        Entrance.Stamp => StampSeconds,
        Entrance.Slam => SlamSeconds + JoltSeconds,
        _ => 0f,
    };

    private void Apply(float now)
    {
        var rect = Text.rectTransform;
        float age = now - shownAt;
        if (age < 0f)
        {
            Text.alpha = 0f;
            return;
        }
        // A caption standing still needs nothing each frame until it leaves.
        bool settled = typed == null && age > EntranceSeconds + 0.05f && now < leaveAt;
        if (settled && Text.alpha >= 1f && rect.localScale.x == 1f && rect.anchoredPosition == restPosition) return;

        if (typed != null)
        {
            int count = Mathf.Min(typed.Length, Mathf.FloorToInt(age / typePerChar) + 1);
            bool caret = Mathf.Repeat(age - typePerChar, caretPeriod) < caretPeriod * 0.5f;
            RenderTyped(count, caret);
        }

        float scale = 1f;
        float extraTilt = 0f;
        Vector2 offset = Vector2.zero;
        switch (entrance)
        {
            case Entrance.Pop:
                scale = Mathf.Lerp(PopScale, 1f, EaseOut(age / PopSeconds));
                break;
            case Entrance.Stamp:
                float stamp = EaseOut(age / StampSeconds);
                scale = Mathf.Lerp(StampScale, 1f, stamp);
                extraTilt = Mathf.Lerp(StampExtraTilt, 0f, stamp);
                break;
            case Entrance.Slam:
                float slam = Mathf.Clamp01(age / SlamSeconds);
                scale = Mathf.Lerp(SlamScale, 1f, slam * slam * slam);
                float jolt = age - SlamSeconds;
                if (jolt >= 0f && jolt < JoltSeconds)
                {
                    int step = Mathf.Min(Jolt.Length - 1, Mathf.FloorToInt(jolt / JoltSeconds * Jolt.Length));
                    Vector2 size = new Vector2(Text.preferredWidth, Text.preferredHeight);
                    offset = Vector2.Scale(Jolt[step], size);
                }
                break;
        }
        rect.localScale = new Vector3(scale, scale, 1f);
        rect.localRotation = Quaternion.Euler(0f, 0f, tilt + extraTilt);
        rect.anchoredPosition = restPosition + offset;

        float standing = 1f;
        if (now >= leaveAt)
        {
            float leaving = (now - leaveAt) / leaveSeconds;
            standing = leaving >= 1f ? 0f : 1f - Mathf.Floor(leaving * HatchSteps) / HatchSteps;
            if (leaving >= 1f) gone = true;
        }
        Text.alpha = standing;
    }

    private void RenderTyped(int count, bool caret)
    {
        if (count == typedShown && caret == caretShown) return;
        typedShown = count;
        caretShown = caret;
        string shown = typed.Substring(0, count);
        string rest = typed.Substring(count);
        // The caret slot always follows the typed letters; before the first letter there is no caret yet.
        Text.text = count > 0 && caret
            ? shown + Caret + Hidden + rest
            : shown + Hidden + Caret + rest;
    }

    private static float EaseOut(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - (1f - t) * (1f - t);
    }
}

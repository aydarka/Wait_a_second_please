using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Run state: counts distance and dodged shots, ends the run on a hit and restarts the scene.
[DisallowMultipleComponent]
public sealed class GameSession : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private string title = "WAIT A SECOND, PLEASE";
    [SerializeField] private string deathMessage = "TOO LATE";
    [SerializeField, Min(0f), Tooltip("Real-time seconds the title stays on screen.")]
    private float titleDuration = 3f;
    [SerializeField, Min(0f), Tooltip("Misses closer than this, in metres, are a close one: the message flashes and the player fires back.")]
    private float closeDistance = 0.6f;
    [SerializeField, Min(0f), Tooltip("Real-time seconds before a tap restarts the run.")]
    private float restartDelay = 1.2f;

    [Header("Hit screen")]
    [SerializeField, Min(0.1f), Tooltip("Tip radius of the closed hole around the shooter, in multiples of his height on screen.")]
    private float shooterHoleScale = 1.4f;
    [SerializeField, Min(0f), Tooltip("Smallest tip radius of the closed hole, in screen heights.")]
    private float minShooterHole = 0.1f;
    [SerializeField, Min(0.01f), Tooltip("Real-time seconds the red closes in from the screen edges.")]
    private float hitScreenInDuration = 0.35f;
    [SerializeField, Tooltip("How closed the red is while it comes in: 0 is past the screen edges, 1 is the hole around the shooter.")]
    private AnimationCurve hitScreenIn = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2.5f), new Keyframe(1f, 1f, 0f, 0f));
    [SerializeField, Min(0.01f), Tooltip("Real-time seconds the red takes to leave past the edges after the restart tap.")]
    private float hitScreenOutDuration = 0.3f;
    [SerializeField, Tooltip("How closed the red is while it leaves: starts at 1 around the shooter, ends at 0 past the edges.")]
    private AnimationCurve hitScreenOut = new AnimationCurve(new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 0f, -2.5f, 0f));
    [SerializeField] private Color red = new Color(0.85f, 0.04f, 0.04f);
    [SerializeField] private Color yellow = new Color(1f, 0.86f, 0.1f);

    // Feet to hat top of a gunman, metres.
    private const float ShooterHeight = 1.95f;

    private static GameSession instance;

    private float startZ;
    private float startTime;
    private float endTime;
    private float nearMissTime = float.NegativeInfinity;
    private int dodged;
    private bool isOver;
    private Text titleText;
    private Text scoreText;
    private Text messageText;
    private JaggedHole hitScreen;
    private float openHoleRadius;
    private float closedHoleRadius;
    private float leaveTime = float.PositiveInfinity;

    // Shooter is the enemy whose bullet hit; the hit screen closes on him.
    public static void ReportPlayerHit(Vector3 point, Transform shooter)
    {
        if (instance != null && !instance.isOver) instance.EndRun(shooter);
    }

    // True for a close one: the message flashes and the player fires back at the shooter.
    public static bool ReportDodge(float missDistance)
    {
        if (instance == null || instance.isOver) return false;
        instance.dodged++;
        if (missDistance > instance.closeDistance) return false;
        instance.nearMissTime = Time.unscaledTime;
        return true;
    }

    private void Awake()
    {
        if (player == null && Camera.main != null) player = Camera.main.transform;
        BuildHud();
    }

    private void OnEnable()
    {
        if (instance != null && instance != this)
            Debug.LogWarning("Only one GameSession should be active.", this);
        instance = this;
        startZ = player != null ? player.position.z : 0f;
        startTime = Time.unscaledTime;
    }

    private void OnDisable()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        float distance = player != null ? Mathf.Max(0f, player.position.z - startZ) : 0f;
        scoreText.text = $"{distance:0} m   ·   {dodged} dodged";

        float titleAge = now - startTime;
        titleText.color = WithAlpha(Color.white, isOver ? 0f : 1f - Mathf.InverseLerp(titleDuration * 0.6f, titleDuration, titleAge));

        if (!isOver)
        {
            float nearMissAge = now - nearMissTime;
            messageText.text = "CLOSE ONE";
            messageText.color = WithAlpha(yellow, 1f - Mathf.Clamp01(nearMissAge / 0.8f));
            return;
        }

        float age = now - endTime;
        bool leaving = now >= leaveTime;
        var pointer = Pointer.current;
        bool tapped = pointer != null && pointer.press.wasPressedThisFrame && !ControlsPanel.Covers(pointer.position.ReadValue());
        // Only the player's tap restarts the run; a tap on the controls menu belongs to the menu.
        if (!leaving && age >= restartDelay && tapped)
        {
            leaveTime = now;
            leaving = true;
        }

        // Pistol Whip-style hit: the red comes in from the screen edges and closes on the shooter,
        // and on restart it leaves the same way.
        float closed = leaving
            ? hitScreenOut.Evaluate(Mathf.Clamp01((now - leaveTime) / hitScreenOutDuration))
            : hitScreenIn.Evaluate(Mathf.Clamp01(age / hitScreenInDuration));
        // Radii far past the edges change nothing on screen, so the curve runs on a log scale.
        hitScreen.HoleRadius = Mathf.Exp(Mathf.LerpUnclamped(Mathf.Log(openHoleRadius), Mathf.Log(closedHoleRadius), closed));
        messageText.text = age >= restartDelay ? $"{deathMessage}\n<size=40>tap to try again</size>" : deathMessage;
        messageText.color = leaving ? Color.clear : Color.white;

        if (leaving && now - leaveTime >= hitScreenOutDuration)
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void EndRun(Transform shooter)
    {
        isOver = true;
        endTime = Time.unscaledTime;
        // The hole closes on the shooter: centred between his feet and hat, sized to his height on screen.
        Vector2 centre = new Vector2(0.5f, 0.5f);
        closedHoleRadius = Mathf.Max(0.01f, minShooterHole);
        var view = Camera.main;
        if (view != null && shooter != null)
        {
            // He steps back to where he fired from, so the hole opens there and not behind his cover.
            var gunman = shooter.GetComponent<EnemyGunman>();
            Vector3 stand = gunman != null ? gunman.ShootingPosition : shooter.position;
            Vector3 feet = view.WorldToViewportPoint(stand);
            Vector3 hat = view.WorldToViewportPoint(stand + Vector3.up * ShooterHeight);
            if (feet.z > 0f && hat.z > 0f)
            {
                centre = Vector2.Lerp(feet, hat, 0.5f);
                centre = new Vector2(Mathf.Clamp(centre.x, 0.05f, 0.95f), Mathf.Clamp(centre.y, 0.05f, 0.95f));
                closedHoleRadius = Mathf.Max(closedHoleRadius, Mathf.Abs(hat.y - feet.y) * shooterHoleScale);
            }
            ShotEffects.Spotlight(shooter);
        }
        hitScreen.HoleCentre = centre;
        openHoleRadius = Mathf.Max(hitScreen.ClearRadius, closedHoleRadius);
        hitScreen.HoleRadius = openHoleRadius;
        hitScreen.enabled = true;
        // The message takes the half of the screen the hole leaves free.
        if (centre.y < 0.45f) Stretch(messageText.rectTransform, new Vector2(0.05f, 0.66f), new Vector2(0.95f, 0.88f));
        else Stretch(messageText.rectTransform, new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.3f));
        // Time stops: the player no longer runs on, and the time controller cannot answer the flinch.
        foreach (var controller in FindObjectsByType<TimeScaleController>(FindObjectsSortMode.None))
            controller.enabled = false;
        foreach (var dodge in FindObjectsByType<PlayerTiltDodge>(FindObjectsSortMode.None))
            dodge.enabled = false;
        Time.timeScale = 0f;
    }

    private void OnDestroy()
    {
        // Scene reloads must not inherit the stopped time of a death if no controller takes over.
        if (isOver) Time.timeScale = 1f;
    }

    private void BuildHud()
    {
        var canvasObject = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        hitScreen = new GameObject("Hit Screen", typeof(RectTransform), typeof(JaggedHole)).GetComponent<JaggedHole>();
        hitScreen.transform.SetParent(canvasObject.transform, false);
        Stretch(hitScreen.rectTransform, Vector2.zero, Vector2.one);
        hitScreen.color = red;
        hitScreen.raycastTarget = false;
        hitScreen.enabled = false;

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText = CreateText(canvasObject.transform, font, "Title", title, 96, FontStyle.Bold,
            new Vector2(0.05f, 0.55f), new Vector2(0.95f, 0.8f), TextAnchor.MiddleCenter);
        scoreText = CreateText(canvasObject.transform, font, "Score", string.Empty, 44, FontStyle.Bold,
            new Vector2(0.03f, 0.88f), new Vector2(0.97f, 0.98f), TextAnchor.UpperLeft);
        messageText = CreateText(canvasObject.transform, font, "Message", string.Empty, 120, FontStyle.Bold,
            new Vector2(0.05f, 0.3f), new Vector2(0.95f, 0.7f), TextAnchor.MiddleCenter);
        messageText.color = WithAlpha(yellow, 0f);
    }

    private static Text CreateText(Transform parent, Font font, string objectName, string content, int size,
        FontStyle style, Vector2 anchorMin, Vector2 anchorMax, TextAnchor alignment)
    {
        var text = new GameObject(objectName, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(parent, false);
        Stretch(text.rectTransform, anchorMin, anchorMax);
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.supportRichText = true;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(3f, -3f);
        return text;
    }

    private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }
}

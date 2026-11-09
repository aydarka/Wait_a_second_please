using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Run state: counts distance and dodged shots, ends the run on a hit and restarts the scene.
// The lettering (title, score, CLOSE ONE, TOO LATE) is GameHud's; this only tells it what happened.
[DisallowMultipleComponent]
public sealed class GameSession : MonoBehaviour
{
    [SerializeField] private Transform player;
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

    // Feet to hat top of a gunman, metres.
    private const float ShooterHeight = 1.95f;

    private static GameSession instance;

    private float startZ;
    private float endTime;
    private int dodged;
    private bool isOver;
    private bool restartPromptShown;
    private GameHud hud;
    private JaggedHole hitScreen;
    private float openHoleRadius;
    private float closedHoleRadius;
    private float leaveTime = float.PositiveInfinity;

    // Shooter is the enemy whose bullet hit; the hit screen closes on him.
    public static void ReportPlayerHit(Vector3 point, Transform shooter)
    {
        if (instance != null && !instance.isOver) instance.EndRun(shooter);
    }

    // Counts a dodged shot. A close one (the bullet decides) stamps CLOSE ONE and returns true,
    // so the player fires back at the shooter.
    public static bool ReportDodge(bool close)
    {
        if (instance == null || instance.isOver) return false;
        instance.dodged++;
        if (!close) return false;
        instance.hud.CloseOne();
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
    }

    private void OnDisable()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        float distance = player != null ? Mathf.Max(0f, player.position.z - startZ) : 0f;
        hud.SetScore(distance, dodged);
        if (!isOver) return;

        float age = now - endTime;
        bool leaving = now >= leaveTime;
        var pointer = Pointer.current;
        bool tapped = pointer != null && pointer.press.wasPressedThisFrame && !ControlsPanel.Covers(pointer.position.ReadValue());
        // Only the player's tap restarts the run; a tap on the controls menu belongs to the menu.
        if (!leaving && age >= restartDelay && tapped)
        {
            leaveTime = now;
            leaving = true;
            hud.Leave(hitScreenOutDuration);
        }
        if (!restartPromptShown && !leaving && age >= restartDelay)
        {
            restartPromptShown = true;
            hud.ShowRestartPrompt();
        }

        // Pistol Whip-style hit: the red comes in from the screen edges and closes on the shooter,
        // and on restart it leaves the same way.
        float closed = leaving
            ? hitScreenOut.Evaluate(Mathf.Clamp01((now - leaveTime) / hitScreenOutDuration))
            : hitScreenIn.Evaluate(Mathf.Clamp01(age / hitScreenInDuration));
        // Radii far past the edges change nothing on screen, so the curve runs on a log scale.
        hitScreen.HoleRadius = Mathf.Exp(Mathf.LerpUnclamped(Mathf.Log(openHoleRadius), Mathf.Log(closedHoleRadius), closed));

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
        // TOO LATE takes the half of the screen the hole leaves free.
        hud.Death(centre);
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

        // Lettering draws over the red.
        hud = GameHud.Create(canvasObject.transform);
    }

    private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

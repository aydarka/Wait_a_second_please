using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Two ways to run time, switched in ControlOptions.TimeControl:
// Tilt: time barely moves while the device is still, follows how fast it is tilted, and runs normally while the screen
// is held or while game code holds normal time (the player's counter-shot in flight, as in Superhot).
// Danger: time runs normally until a shot threatens the player and slows down the closer a bullet passing near the eyes
// gets, staying slow while it flies past the ear; a sharp tilt wins back part of the slow-down, the tap leaves it alone,
// and the slowest point grows less slow as the run goes on.
// In both modes game code can hold slow motion (HoldSlowMotion), and SlowMotionEntered/Exited mark the edges for sounds.
[DisallowMultipleComponent]
public sealed class TimeScaleController : MonoBehaviour
{
    [Header("Tilt time control")]
    [SerializeField, Min(0f)] private float slowTimeScale = 0.03f;
    [SerializeField, Min(0f)] private float normalTimeScale = 1f;
    [SerializeField, Min(0f), Tooltip("Tilt speed in degrees per second that is ignored as hand tremor.")]
    private float tiltDeadZone = 6f;
    [SerializeField, Min(0.01f), Tooltip("Tilt speed in degrees per second that reaches normal time.")]
    private float fullSpeedTiltRate = 120f;
    [SerializeField, Min(0.01f), Tooltip("Shapes the tilt response: above 1 keeps slow tilts slower.")]
    private float tiltResponseExponent = 1.5f;
    [SerializeField, Min(0f), Tooltip("Real-time duration of a full transition from slow to normal speed.")]
    private float transitionDuration = 0.08f;
    [SerializeField, Min(0f), Tooltip("Real-time duration of a full transition from normal back to slow speed.")]
    private float slowDownDuration = 0.35f;

    [Header("Danger time control")]
    [SerializeField, Tooltip("Time scale at the most dangerous moment (a bullet about to hit), by metres run. It rises so later shots leave less time to react; 1000 m is about three minutes of running.")]
    private AnimationCurve peakTimeScaleByDistance = new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(1000f, 0.3f));
    [SerializeField, Range(0f, 1f), Tooltip("Danger of an enemy at the moment it fires: only a light slow-down that grows while it aims. The bullet carries on from there, and its approach does the rest.")]
    private float aimSlowDown = 0.15f;
    [SerializeField, Min(0f), Tooltip("A bullet starts slowing time beyond Aim Slow Down once it is this close to the eyes, in metres.")]
    private float slowDownStartDistance = 25f;
    [SerializeField, Min(0f), Tooltip("A bullet this close to the eyes, in metres, gives the full slow-down.")]
    private float deepestSlowDownDistance = 1f;
    [SerializeField, Min(0.01f), Tooltip("Shapes the approach: above 1 keeps most of the slow-down for the last metres, so the bullet rushes in and then nearly stops in front of the face.")]
    private float approachExponent = 2f;
    [SerializeField, Min(0f), Tooltip("A bullet that will pass the eyes closer than this, in metres, keeps the full slow-down even once it is dodged, so it flies slowly past the ear.")]
    private float nearPassDistance = 1f;
    [SerializeField, Min(0f), Tooltip("A bullet that will pass the eyes farther than this, in metres, does not slow time; between the two the slow-down fades.")]
    private float farPassDistance = 2f;
    [SerializeField, Min(0f), Tooltip("Real-time seconds the slow-down stays after a bullet has passed the eyes, so it is seen and heard flying on behind the player.")]
    private float passHoldSeconds = 0.35f;
    [SerializeField, Min(0f), Tooltip("Real-time seconds for a full drop from normal time to the slowest point.")]
    private float dangerSlowDownDuration = 0.12f;
    [SerializeField, Min(0f), Tooltip("Real-time seconds for time to return to normal once the danger is gone.")]
    private float dangerReleaseDuration = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("Share of the way from the danger slow-down back to normal time that the sharpest tilt wins back (Superhot: moving fast lets the bullet come sooner). Without danger the tilt changes nothing.")]
    private float maxTiltLift = 0.5f;
    [SerializeField, Min(0f), Tooltip("Tilt speed in degrees per second that is ignored as hand tremor while in danger.")]
    private float dangerTiltDeadZone = 5f;
    [SerializeField, Min(0.01f), Tooltip("Tilt speed in degrees per second that wins back the full Max Tilt Lift.")]
    private float dangerFullSpeedTiltRate = 60f;
    [SerializeField, Min(0.01f), Tooltip("Shapes the tilt lift: above 1 keeps slow tilts closer to the danger slow-down.")]
    private float dangerTiltResponseExponent = 1.5f;

    // Time counts as slow motion below the first scale and as normal again above the second (the gap stops flicker).
    private const float SlowMotionEnterScale = 0.5f;
    private const float SlowMotionExitScale = 0.8f;

    private static readonly HashSet<object> normalTimeHolders = new HashSet<object>();
    private static readonly HashSet<object> slowMotionHolders = new HashSet<object>();
    private static readonly Predicate<object> IsDestroyed = holder => holder is UnityEngine.Object unityObject && unityObject == null;
    private static float flowSlowScale;
    private static float flowNormalScale = 1f;

    private readonly HashSet<EnemyGunman> aimingEnemies = new HashSet<EnemyGunman>();
    private readonly Predicate<EnemyGunman> stoppedAiming = enemy => enemy == null || !enemy.isActiveAndEnabled || !enemy.IsAiming;
    private Dictionary<Projectile, float> bulletDanger = new Dictionary<Projectile, float>();
    private Dictionary<Projectile, float> previousBulletDanger = new Dictionary<Projectile, float>();
    private float previousTimeScale;
    private float flow;
    private float danger;
    private float passedDanger;
    private float passedDangerUntil;
    private bool slowMotionHeld;
    private float slowHoldFlow;
    private float slowHoldDanger;
    // The parts of the last frame that do not come from the tilt: the tap or normal-time holds (Tilt) and the measured
    // danger (Danger). A slow hold catches these, so the tilt keeps working on top of it.
    private float lastHeldFlow;
    private float lastMeasuredDanger;
    private bool pressOnPanel;
    private ControlOptions.TimeControlMode activeMode;
    private EnemySpawner spawner;
    private float startZ;

    // 0 at the slowest time scale of the active mode, 1 at the normal one; drives slow-motion visuals.
    // Derived from the actual time scale so scripted slow motion (the death shot) reads as slow too.
    public static float Flow => Mathf.InverseLerp(flowSlowScale, flowNormalScale, Time.timeScale);

    // Hooks for the slow-motion sounds (whoosh in, whoosh out): raised when time drops into slow motion and when it
    // climbs back to normal speed. Not raised when the run ends, which has its own death screen.
    public static event Action SlowMotionEntered;
    public static event Action SlowMotionExited;
    public static bool InSlowMotion { get; private set; }

    // Keeps time as slow as it is at the moment of the first hold, in both modes, until every requester releases it.
    // A slow hold wins over normal-time holds, the tap and the tilt. For the counter-shot: the dodged bullet passes and
    // the player draws and fires while time stays slow; time picks up again only after the release.
    // Holding twice is harmless; a destroyed Unity object releases itself; a scene reload clears every hold.
    public static void HoldSlowMotion(object requester)
    {
        if (requester != null) slowMotionHolders.Add(requester);
    }

    // Lets time return to its usual speed (smoothly) once no one else holds slow motion. Safe without a hold.
    public static void ReleaseSlowMotion(object requester)
    {
        if (requester != null) slowMotionHolders.Remove(requester);
    }

    // Tilt time control only: runs time at normal speed, like holding the screen, until the same requester releases it.
    // Holding twice is harmless; a destroyed Unity object releases itself; a scene reload clears every hold.
    // Danger time control ignores holds: without danger time is normal anyway, and danger always wins.
    public static void HoldNormalTime(object requester)
    {
        if (requester != null) normalTimeHolders.Add(requester);
    }

    // Lets time settle back to the tilt-driven speed (over slowDownDuration) once no one else holds it. Safe without a hold.
    public static void ReleaseNormalTime(object requester)
    {
        if (requester != null) normalTimeHolders.Remove(requester);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        normalTimeHolders.Clear();
        slowMotionHolders.Clear();
        SlowMotionEntered = null;
        SlowMotionExited = null;
        InSlowMotion = false;
    }

    private void OnEnable()
    {
        previousTimeScale = Time.timeScale;
        activeMode = ControlOptions.TimeControl;
        flow = 0f;
        danger = 0f;
        Time.timeScale = activeMode == ControlOptions.TimeControlMode.Danger
            ? Mathf.Max(0f, normalTimeScale)
            : Mathf.Max(0f, slowTimeScale);
        // A new controller means a new run: holds left over from the previous scene no longer apply.
        normalTimeHolders.Clear();
        slowMotionHolders.Clear();
        slowMotionHeld = false;
        aimingEnemies.Clear();
        bulletDanger.Clear();
        previousBulletDanger.Clear();
        passedDanger = 0f;
        passedDangerUntil = 0f;
        InSlowMotion = Time.timeScale < SlowMotionEnterScale;
        EnemyGunman.AimStarted += OnAimStarted;
        spawner = FindAnyObjectByType<EnemySpawner>();
        startZ = Camera.main != null ? Camera.main.transform.position.z : 0f;
        TiltInput.Recalibrate();
    }

    private void Update()
    {
        if (normalTimeHolders.Count > 0) normalTimeHolders.RemoveWhere(IsDestroyed);
        if (slowMotionHolders.Count > 0) slowMotionHolders.RemoveWhere(IsDestroyed);

        var mode = ControlOptions.TimeControl;
        if (mode != activeMode)
        {
            // Carry the current speed over so switching modes mid-run does not jerk time.
            activeMode = mode;
            flow = Mathf.InverseLerp(Mathf.Max(0f, slowTimeScale), NormalScale, Time.timeScale);
            danger = DangerAt(Time.timeScale);
        }

        // A slow hold keeps the slow-down that was there when the first requester asked for it; the tilt still works.
        bool held = slowMotionHolders.Count > 0;
        if (held && !slowMotionHeld)
        {
            slowHoldFlow = lastHeldFlow;
            slowHoldDanger = lastMeasuredDanger;
        }
        slowMotionHeld = held;

        if (mode == ControlOptions.TimeControlMode.Danger) UpdateDanger();
        else UpdateTilt();
        AnnounceSlowMotion();
    }

    private void AnnounceSlowMotion()
    {
        if (!InSlowMotion && Time.timeScale < SlowMotionEnterScale)
        {
            InSlowMotion = true;
            SlowMotionEntered?.Invoke();
        }
        else if (InSlowMotion && Time.timeScale > SlowMotionExitScale)
        {
            InSlowMotion = false;
            SlowMotionExited?.Invoke();
        }
    }

    private float NormalScale => Mathf.Max(Mathf.Max(0f, slowTimeScale), normalTimeScale);

    private void UpdateTilt()
    {
        float slowScale = Mathf.Max(0f, slowTimeScale);
        float normalScale = NormalScale;
        var pointer = Pointer.current;
        bool isHeld = false;
        if (Application.isFocused && pointer != null)
        {
            // A press on the controls menu is a menu tap, not a request for normal time.
            if (pointer.press.wasPressedThisFrame) pressOnPanel = ControlsPanel.Covers(pointer.position.ReadValue());
            isHeld = pointer.press.isPressed && !pressOnPanel;
        }
        // With the vertical dodge off, tilting the top edge does nothing, so it must not run time either.
        float tiltSpeed = ControlOptions.VerticalDodge ? TiltInput.AngularSpeed : TiltInput.RollSpeed;
        isHeld |= normalTimeHolders.Count > 0;

        float tiltFlow = Mathf.Pow(
            Mathf.InverseLerp(tiltDeadZone, Mathf.Max(tiltDeadZone + 0.01f, fullSpeedTiltRate), tiltSpeed),
            Mathf.Max(0.01f, tiltResponseExponent));
        float heldFlow = isHeld ? 1f : 0f;
        lastHeldFlow = heldFlow;
        // A slow hold stops the tap and normal-time holds from running time faster than when it began; the tilt still does.
        if (slowMotionHeld) heldFlow = Mathf.Min(heldFlow, slowHoldFlow);
        float targetFlow = Mathf.Max(heldFlow, tiltFlow);

        // Speed up almost instantly so a sharp tilt is felt, but let time settle back gradually.
        float duration = targetFlow > flow ? transitionDuration : slowDownDuration;
        flow = duration > 0f
            ? Mathf.MoveTowards(flow, targetFlow, Time.unscaledDeltaTime / duration)
            : targetFlow;
        Time.timeScale = Mathf.Lerp(slowScale, normalScale, flow);
        flowSlowScale = slowScale;
        flowNormalScale = normalScale;
    }

    private void UpdateDanger()
    {
        // A sharp tilt wins back part of the way to normal time. In log space lerping the danger slow-down towards normal
        // time by the lift is the same as scaling the danger, so both sources share one value and one smoothing.
        float tiltSpeed = ControlOptions.VerticalDodge ? TiltInput.AngularSpeed : TiltInput.RollSpeed;
        float lift = maxTiltLift * Mathf.Pow(
            Mathf.InverseLerp(dangerTiltDeadZone, Mathf.Max(dangerTiltDeadZone + 0.01f, dangerFullSpeedTiltRate), tiltSpeed),
            Mathf.Max(0.01f, dangerTiltResponseExponent));
        float measured = MeasureDanger();
        lastMeasuredDanger = measured;
        // A slow hold keeps at least the danger it caught; the tilt still wins part of it back, as everywhere in danger.
        if (slowMotionHeld) measured = Mathf.Max(measured, slowHoldDanger);
        float targetDanger = measured * (1f - lift);
        // Slow down fast when a threat appears, and let time pick up again a little more gently once it has passed.
        float duration = targetDanger > danger ? dangerSlowDownDuration : dangerReleaseDuration;
        danger = duration > 0f
            ? Mathf.MoveTowards(danger, targetDanger, Time.unscaledDeltaTime / duration)
            : targetDanger;

        float normalScale = NormalScale;
        float peakScale = PeakScale(normalScale);
        // Interpolating in log space makes equal steps of danger feel like equal steps of slow-down.
        Time.timeScale = Mathf.Exp(Mathf.Lerp(Mathf.Log(normalScale), Mathf.Log(peakScale), danger));
        flowSlowScale = peakScale;
        flowNormalScale = normalScale;
    }

    // The highest danger among enemies taking aim, enemy bullets still on their way to the player, and bullets that
    // have just flown past the eyes.
    private float MeasureDanger()
    {
        float highest = 0f;
        aimingEnemies.RemoveWhere(stoppedAiming);
        foreach (var enemy in aimingEnemies)
            highest = Mathf.Max(highest, aimSlowDown * Mathf.Clamp01(enemy.AimProgress));

        var swap = previousBulletDanger;
        previousBulletDanger = bulletDanger;
        bulletDanger = swap;
        bulletDanger.Clear();
        var view = Camera.main;
        if (view != null)
        {
            foreach (var bullet in Projectile.Approaching)
            {
                if (bullet == null || !bullet.PredictMeeting(view, out _, out float missDistance, out _))
                    continue;
                // Only how close the bullet will pass counts, not whether it would hit: a dodged bullet still flies slowly
                // past the ear. One that will pass far away does not slow time.
                float threat = 1f - Mathf.InverseLerp(nearPassDistance, Mathf.Max(nearPassDistance + 0.01f, farPassDistance), missDistance);
                // The slow-down follows the bullet's approach, not the moment it was fired, and deepens most in the last metres.
                float distance = Vector3.Distance(bullet.transform.position, view.transform.position);
                float closeness = 1f - Mathf.InverseLerp(deepestSlowDownDistance,
                    Mathf.Max(deepestSlowDownDistance + 0.01f, slowDownStartDistance), distance);
                float bulletLevel = threat * Mathf.Lerp(aimSlowDown, 1f, Mathf.Pow(closeness, Mathf.Max(0.01f, approachExponent)));
                bulletDanger[bullet] = bulletLevel;
                highest = Mathf.Max(highest, bulletLevel);
            }
        }

        // A bullet that dropped out since last frame has passed the eyes: keep its slow-down a moment longer.
        float now = Time.unscaledTime;
        foreach (var entry in previousBulletDanger)
        {
            if (bulletDanger.ContainsKey(entry.Key)) continue;
            passedDanger = now < passedDangerUntil ? Mathf.Max(passedDanger, entry.Value) : entry.Value;
            passedDangerUntil = now + passHoldSeconds;
        }
        if (now < passedDangerUntil) highest = Mathf.Max(highest, passedDanger);
        return highest;
    }

    private float PeakScale(float normalScale)
    {
        float run = spawner != null ? spawner.DistanceRun
            : Camera.main != null ? Mathf.Max(0f, Camera.main.transform.position.z - startZ) : 0f;
        return Mathf.Clamp(peakTimeScaleByDistance.Evaluate(run), 0.01f, normalScale);
    }

    private float DangerAt(float timeScale)
    {
        float normalScale = NormalScale;
        float peakScale = PeakScale(normalScale);
        if (peakScale >= normalScale) return 0f;
        return Mathf.Clamp01(Mathf.InverseLerp(Mathf.Log(normalScale), Mathf.Log(peakScale), Mathf.Log(Mathf.Max(0.0001f, timeScale))));
    }

    private void OnAimStarted(EnemyGunman enemy, float warningSeconds)
    {
        if (enemy != null) aimingEnemies.Add(enemy);
    }

    private void OnDisable()
    {
        EnemyGunman.AimStarted -= OnAimStarted;
        aimingEnemies.Clear();
        // The run ending (death screen) is not a return to normal speed, so no exit sound is announced.
        InSlowMotion = false;
        Time.timeScale = previousTimeScale;
    }

    private void OnValidate()
    {
        slowTimeScale = Mathf.Max(0f, slowTimeScale);
        normalTimeScale = Mathf.Max(slowTimeScale, normalTimeScale);
        tiltDeadZone = Mathf.Max(0f, tiltDeadZone);
        fullSpeedTiltRate = Mathf.Max(tiltDeadZone + 0.01f, fullSpeedTiltRate);
        transitionDuration = Mathf.Max(0f, transitionDuration);
        slowDownDuration = Mathf.Max(0f, slowDownDuration);
        slowDownStartDistance = Mathf.Max(0f, slowDownStartDistance);
        deepestSlowDownDistance = Mathf.Clamp(deepestSlowDownDistance, 0f, slowDownStartDistance);
        nearPassDistance = Mathf.Max(0f, nearPassDistance);
        farPassDistance = Mathf.Max(nearPassDistance + 0.01f, farPassDistance);
        passHoldSeconds = Mathf.Max(0f, passHoldSeconds);
        dangerSlowDownDuration = Mathf.Max(0f, dangerSlowDownDuration);
        dangerReleaseDuration = Mathf.Max(0f, dangerReleaseDuration);
        dangerTiltDeadZone = Mathf.Max(0f, dangerTiltDeadZone);
        dangerFullSpeedTiltRate = Mathf.Max(dangerTiltDeadZone + 0.01f, dangerFullSpeedTiltRate);
    }
}

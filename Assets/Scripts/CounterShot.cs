using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// The player's answer to a close dodge, played as one slow-motion beat: the enemy bullet slips past the ear and
// the pistol comes up from below the frame, aimed at the enemy who fired, while time is held slow. Once it is up
// the world darkens, the enemy and the pistol stand out white and a yellow ring closes on the enemy's chest: a tap
// anywhere fires, and only after the shot does time pick up. If the ring closes first the chance is gone, the
// pistol drops away and the enemy goes back into cover.
// The shot flies in game time with the same air rings as enemy shots, always lands, and the enemy dies the
// moment it arrives. As in Superhot the flight runs at normal speed in tilt time control, but only while no
// enemy bullet is still coming at the player: another shot in the air keeps time in the player's hands.
[DisallowMultipleComponent]
public sealed class CounterShot : MonoBehaviour
{
    [SerializeField, Min(0.01f), Tooltip("Flight speed, metres per game second.")]
    private float speed = 50f;
    [SerializeField, Tooltip("Launch point on screen (0 to 1 across and up) when there is no pistol to fire from.")]
    private Vector2 hipOnScreen = new Vector2(0.78f, 0.12f);
    [SerializeField, Min(0.05f), Tooltip("Launch point distance in front of the eyes without a pistol, metres.")]
    private float hipDepth = 0.6f;
    [SerializeField, Min(0.1f), Tooltip("Real-time seconds the player has to tap once the pistol is up; the yellow ring on the enemy closes over this time.")]
    private float tapWindow = 1.25f;
    [SerializeField, Min(0f), Tooltip("Muzzle flash radius, metres.")]
    private float flashRadius = 0.16f;
    [SerializeField, Min(0f), Tooltip("Real-time seconds the muzzle flash stays on screen.")]
    private float flashSeconds = 0.18f;
    [SerializeField, Min(0f), Tooltip("Real-time seconds the slow motion lasts after the shot leaves, so the flash and the kick read before time picks up.")]
    private float slowAfterShot = 0.1f;
    [SerializeField, Min(0.01f), Tooltip("Bullet size at the muzzle relative to the model; it shrinks back to the model size at the enemy.")]
    private float launchScale = 2.5f;
    [SerializeField] private AirRingTrail rings = new AirRingTrail();

    // Hooks for sound and effects: the shot leaves the muzzle (at that point); it reaches the enemy, who dies.
    public static event Action<Vector3> Fired;
    public static event Action<EnemyGunman> Landed;
    // Hooks for the tap prompt: the chance to shoot back opens (for this many real seconds), and it closes,
    // fired or missed.
    public static event Action<float> TapWindowOpened;
    public static event Action<bool> TapWindowClosed;

    // Shots waiting for the tap, oldest first: one tap fires one of them.
    private static readonly List<CounterShot> waiting = new List<CounterShot>();
    private static int tapUsedFrame = -1;

    private EnemyGunman target;
    private PlayerGun gun;
    private Renderer[] parts;
    private bool windowOpen;
    private float windowOpenedAt;
    private bool launched;
    private bool slowHeld;
    private float launchedAt;
    private Vector3 launchPosition;
    private Vector3 initialScale;
    private float flown;

    // True while the player may tap to shoot back.
    public static bool AwaitingTap => waiting.Count > 0;
    // Where the yellow ring of the shot the next tap fires sits on screen, in pixels, and the radius it starts
    // at, in pixels, so the tap prompt can stand beside it. False while no shot waits for a tap.
    public static bool TryGetTapRing(out Vector2 screenCentre, out float startRadiusPixels)
    {
        screenCentre = Vector2.zero;
        startRadiusPixels = 0f;
        var view = Camera.main;
        if (waiting.Count == 0 || view == null || waiting[0].target == null) return false;
        ShotEffects.TargetRingStart(view, waiting[0].target.TargetPoint, out Vector3 centre, out float radius);
        Vector3 screen = view.WorldToScreenPoint(centre);
        if (screen.z <= 0f) return false;
        Vector3 edge = view.WorldToScreenPoint(centre + view.transform.right * radius);
        screenCentre = screen;
        startRadiusPixels = Vector2.Distance(screen, edge);
        return true;
    }

    // Answers the enemy; one already dead or gone gets no answer.
    public static void Fire(EnemyGunman enemy)
    {
        if (enemy == null || enemy.IsDead || !enemy.isActiveAndEnabled) return;
        var prefab = Resources.Load<CounterShot>("PlayerBullet");
        if (prefab == null)
        {
            Debug.LogError("CounterShot needs the PlayerBullet prefab in a Resources folder.");
            return;
        }
        // He stays out of cover from now on, while the pistol comes up.
        enemy.HoldExposed();
        Instantiate(prefab).Prepare(enemy);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Fired = null;
        Landed = null;
        TapWindowOpened = null;
        TapWindowClosed = null;
        waiting.Clear();
        tapUsedFrame = -1;
    }

    // The dodge has just counted: time is held slow while the bullet slips past and the pistol comes up.
    // The shot waits unseen until the player taps.
    private void Prepare(EnemyGunman enemy)
    {
        target = enemy;
        initialScale = transform.localScale;
        parts = GetComponentsInChildren<Renderer>();
        SetVisible(false);
        rings.Restart();
        HoldSlowMotion(true);
        gun = PlayerGun.Draw(this, enemy.TargetPoint);
        ShotEffects.BeginFocus(this, enemy, gun != null ? gun.Parts : null);
    }

    private void Update()
    {
        if (target == null || target.IsDead || !target.isActiveAndEnabled)
        {
            Destroy(gameObject);
            return;
        }
        // The enemy may still be stepping out of cover, so the aim follows his chest every frame.
        if (gun != null) gun.Aim(target.TargetPoint);
        if (!launched)
        {
            WaitForTap();
            return;
        }
        // The shot is out: a moment later the slow motion lets go.
        if (slowHeld && Time.unscaledTime - launchedAt >= slowAfterShot) HoldSlowMotion(false);
        if (!slowHeld) HoldTimeWhileSafe();

        Vector3 path = target.TargetPoint - launchPosition;
        float length = path.magnitude;
        flown += speed * Time.deltaTime;
        if (flown >= length)
        {
            Landed?.Invoke(target);
            target.Die(path);
            Destroy(gameObject);
            return;
        }
        Place(launchPosition + path / length * flown, path, flown / length);
        rings.Emit(launchPosition, path / length * speed, flown, launchScale, 1f, length);
    }

    // The chance opens once the pistol is up; the ring closes over the window in real time, as the moment is
    // held slow. The oldest waiting shot takes the tap.
    private void WaitForTap()
    {
        if (!windowOpen)
        {
            if (gun != null && !gun.IsUp) return;
            windowOpen = true;
            windowOpenedAt = Time.unscaledTime;
            waiting.Add(this);
            TapWindowOpened?.Invoke(tapWindow);
        }
        float closing = (Time.unscaledTime - windowOpenedAt) / tapWindow;
        ShotEffects.TargetRing(this, closing);
        var view = Camera.main;
        if (view != null && waiting.Count > 0 && waiting[0] == this && TappedThisFrame())
        {
            tapUsedFrame = Time.frameCount;
            CloseWindow(true);
            Launch(view);
        }
        else if (closing >= 1f)
        {
            // Too late: the pistol drops away and the enemy goes back into cover, free to shoot again.
            CloseWindow(false);
            target.ReleaseExposed();
            Destroy(gameObject);
        }
    }

    // A tap anywhere but on the controls menu, not one another shot already took this frame.
    private static bool TappedThisFrame()
    {
        if (tapUsedFrame == Time.frameCount) return false;
        var pointer = Pointer.current;
        return pointer != null && pointer.press.wasPressedThisFrame && !ControlsPanel.Covers(pointer.position.ReadValue());
    }

    private void CloseWindow(bool fired)
    {
        if (!windowOpen) return;
        windowOpen = false;
        waiting.Remove(this);
        ShotEffects.EndFocus(this);
        TapWindowClosed?.Invoke(fired);
    }

    private void Launch(Camera view)
    {
        launched = true;
        launchedAt = Time.unscaledTime;
        launchPosition = gun != null
            ? gun.MuzzlePosition
            : view.ViewportToWorldPoint(new Vector3(hipOnScreen.x, hipOnScreen.y, hipDepth));
        flown = 0f;
        ShotEffects.PlayerFlash(view, launchPosition, flashRadius, flashSeconds);
        if (gun != null) gun.Kick();
        SetVisible(true);
        Place(launchPosition, target.TargetPoint - launchPosition, 0f);
        Fired?.Invoke(launchPosition);
    }

    // Landing, a missed chance, losing the target or a scene reload all end the shot here.
    private void OnDestroy()
    {
        CloseWindow(false);
        ShotEffects.EndFocus(this);
        HoldSlowMotion(false);
        TimeScaleController.ReleaseNormalTime(this);
        PlayerGun.Holster(this);
    }

    // Slow motion from the dodge until just after the shot, in both time controls.
    private void HoldSlowMotion(bool hold)
    {
        slowHeld = hold;
        if (hold) TimeScaleController.HoldSlowMotion(this);
        else TimeScaleController.ReleaseSlowMotion(this);
    }

    // Normal time while the shot flies, unless an enemy bullet is still on its way to the player. Once that
    // bullet has passed, normal time comes back for the rest of the flight; a bullet fired meanwhile lets
    // time settle back to slow.
    private void HoldTimeWhileSafe()
    {
        if (Projectile.AnyApproaching) TimeScaleController.ReleaseNormalTime(this);
        else TimeScaleController.HoldNormalTime(this);
    }

    private void Place(Vector3 position, Vector3 direction, float progress)
    {
        if (direction.sqrMagnitude > 0f) transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
        else transform.position = position;
        transform.localScale = initialScale * Mathf.Lerp(launchScale, 1f, progress);
    }

    private void SetVisible(bool visible)
    {
        foreach (var part in parts) part.enabled = visible;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

// Bullets move after the camera has run forward for the frame, so the eyes they see are from the same moment.
[DefaultExecutionOrder(100)]
public class Projectile : MonoBehaviour
{
    [SerializeField, Min(0.01f), Tooltip("Speed along the world Z axis in metres per second.")]
    private float speed = 20f;
    [SerializeField, Min(1f)] private float finalScaleMultiplier = 5f;
    [SerializeField, Min(0f), Tooltip("Distance from the player's eye line that counts as a hit, for a bullet still on screen when it reaches the eyes.")]
    private float hitRadius = 0.35f;
    [SerializeField, Min(0f), Tooltip("A dodged bullet that passed this close to the eyes or closer is a close one (the message and the counter-shot), metres.")]
    private float closeDistance = 0.6f;
    [SerializeField, Tooltip("Half-angles of the frame a bullet has to stay inside to hit, horizontal and vertical, degrees; the screen itself where it is narrower. One frame for landscape and portrait keeps dodging equally hard in both.")]
    private Vector2 dodgeFrame = new Vector2(25f, 30f);
    [SerializeField, Min(0f), Tooltip("Distance behind the player after which a missed bullet returns to the pool.")]
    private float flyByDistance = 6f;
    [SerializeField, Tooltip("Colliders that stop the bullet: it ends in sparks where its path meets one. Nothing set uses the StreetCover layer.")]
    private LayerMask blockers;

    [SerializeField] private AirRingTrail rings = new AirRingTrail();

    // Bullets in flight that have not yet reached the player.
    private static readonly HashSet<Projectile> approaching = new HashSet<Projectile>();

    private Vector3 initialScale;
    private float flightDuration;
    private float elapsedFlightTime;
    private bool hasInitialScale;
    private Vector3 destination;
    private Vector3 launchPosition;
    private Vector3 velocity;
    private float meetingDistance;
    private PrefabPool owner;
    private bool isFlying;
    private bool passedPlayer;
    private Renderer body;
    // The player's eyes at this bullet's last move: a bullet and the eyes it is judged against are from one moment.
    private Vector3 eyesSeen;
    // Where along its path the bullet strikes cover, metres from the muzzle; infinity when the way is clear.
    private float coverDistance = float.PositiveInfinity;
    private Vector3 coverNormal;

    private const string DefaultBlockerLayer = "StreetCover";

    public float ZSpeed => Mathf.Max(0.01f, speed);
    // A bullet in the frame at the eyes hits when it passes this close to them, metres.
    public float HitRadius => hitRadius;

    // A dodged bullet that passes this close to the eyes or closer counts as a close one, metres.
    public float CloseDistance => closeDistance;

    // Hooks for sound and effects. PassedPlayer: a bullet went past the eyes without hitting, close or not
    // (the fly-by by the ear). HitPlayer: the bullet that ends the run, as it reaches the eyes.
    public static event Action<Projectile, bool> PassedPlayer;
    public static event Action<Projectile> HitPlayer;
    // HitCover: a bullet struck cover on its way (at that point) and ended there in sparks.
    public static event Action<Projectile, Vector3> HitCover;

    // True while any enemy bullet is still on its way to the player, i.e. there is still something to dodge.
    public static bool AnyApproaching => approaching.Count > 0;
    // The enemy bullets still on their way to the player. Read only; do not keep it across frames.
    public static IReadOnlyCollection<Projectile> Approaching => approaching;

    // How this bullet meets the player if he stays where he is now: game seconds until it reaches the plane of
    // his eyes (he runs forward meanwhile), how far from his eyes it passes there, and whether it is still in
    // the frame there. It hits exactly when inFrameAtEyes and missDistance <= HitRadius: the same rule and the
    // same sums as the real crossing. False once the bullet is no longer coming at the player.
    public bool PredictMeeting(Camera view, out float secondsToEyes, out float missDistance, out bool inFrameAtEyes)
    {
        if (isFlying && !passedPlayer && view != null &&
            Meet(view, out secondsToEyes, out _, out missDistance, out inFrameAtEyes) && secondsToEyes >= 0f)
            return true;
        secondsToEyes = float.PositiveInfinity;
        missDistance = float.PositiveInfinity;
        inFrameAtEyes = false;
        return false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        approaching.Clear();
        PassedPlayer = null;
        HitPlayer = null;
        HitCover = null;
    }

    public void Launch(Vector3 targetPosition, float duration, PrefabPool pool)
    {
        // Launch can run before Awake when the pool creates an inactive instance.
        if (!hasInitialScale)
        {
            initialScale = transform.localScale;
            hasInitialScale = true;
        }
        transform.localScale = initialScale;
        destination = targetPosition;
        launchPosition = transform.position;
        Vector3 flightDirection = destination - launchPosition;
        if (flightDirection.sqrMagnitude > 0f)
            transform.rotation = Quaternion.LookRotation(flightDirection);
        flightDuration = Mathf.Max(0f, duration);
        velocity = flightDuration > 0f ? flightDirection / flightDuration : flightDirection.normalized * ZSpeed;
        meetingDistance = flightDirection.magnitude;
        rings.Restart();
        elapsedFlightTime = 0f;
        owner = pool;
        isFlying = true;
        passedPlayer = false;
        var view = Camera.main;
        eyesSeen = view != null ? view.transform.position : launchPosition;
        // The path is a straight line fixed at launch, so one look along it finds the cover it will strike.
        // Until it strikes, the bullet is a threat like any other.
        coverDistance = float.PositiveInfinity;
        if (blockers.value == 0) blockers = LayerMask.GetMask(DefaultBlockerLayer);
        if (blockers.value != 0 &&
            Physics.Linecast(launchPosition, destination, out RaycastHit hit, blockers, QueryTriggerInteraction.Ignore))
        {
            coverDistance = hit.distance;
            coverNormal = hit.normal;
        }
        approaching.Add(this);
    }

    private void Update()
    {
        if (!isFlying) return;
        elapsedFlightTime += Time.deltaTime;
        if (elapsedFlightTime < flightDuration)
        {
            float progress = Mathf.Clamp01(elapsedFlightTime / flightDuration);
            // Interpolate all axes over the predicted meeting time, keeping Z speed constant.
            transform.position = Vector3.Lerp(launchPosition, destination, progress);
            transform.localScale = initialScale * Mathf.Lerp(1f, Mathf.Max(1f, finalScaleMultiplier), progress);
        }
        else
        {
            // A dodged bullet keeps flying past the player instead of vanishing at the meeting point.
            transform.position = destination + velocity * (elapsedFlightTime - flightDuration);
            transform.localScale = initialScale * Mathf.Max(1f, finalScaleMultiplier);
        }
        float flown = Vector3.Distance(launchPosition, transform.position);
        rings.Emit(launchPosition, velocity, Mathf.Min(flown, coverDistance),
            1f, Mathf.Max(1f, finalScaleMultiplier), meetingDistance);
        if (flown >= coverDistance)
        {
            StrikeCover();
            return;
        }

        var player = Camera.main;
        if (player != null)
        {
            eyesSeen = player.transform.position;
            if (!passedPlayer && transform.position.z <= eyesSeen.z)
            {
                passedPlayer = true;
                approaching.Remove(this);
                CheckHit(player);
                if (!isFlying) return;
            }
            if (passedPlayer && transform.position.z < eyesSeen.z - flyByDistance)
            {
                Finish();
                return;
            }
        }
        else if (elapsedFlightTime >= flightDuration)
        {
            Finish();
        }
    }

    // The player's rule: a bullet that left the screen was dodged. Only a bullet still in the frame when it
    // reaches the eyes can hit, and then only within the hit radius. A dodge is close when the bullet passed
    // within closeDistance of the eyes, wherever it left the frame: a shot from the street side runs near the
    // frame edge and leaves it early even when it nearly grazes the face. The frame is the same in landscape
    // and portrait (dodgeFrame), so a wide landscape screen does not make dodging harder.
    private void CheckHit(Camera view)
    {
        if (!Meet(view, out _, out Vector3 crossing, out float missDistance, out bool inFrame))
        {
            // The player outran a bullet slower than him; it never met his eyes.
            PassedPlayer?.Invoke(this, false);
            GameSession.ReportDodge(false);
            return;
        }
        if (missDistance <= hitRadius && inFrame)
        {
            HitPlayer?.Invoke(this);
            // The killing bullet and its rings vanish: the death screen shows who fired it instead.
            ShotEffects.ClearRings(rings.Owner);
            var shooter = FindShooter();
            GameSession.ReportPlayerHit(crossing, shooter != null ? shooter.transform : null);
            Finish();
        }
        else if (Passed(missDistance <= closeDistance))
        {
            // A close one: the player fires back at whoever sent this bullet.
            CounterShot.Fire(FindShooter());
        }
    }

    // The bullet ends on the cover in its way: sparks, no hit, no dodge. Leaving the approaching set lets the
    // time control hold its slow-down a moment longer, so the sparks are seen before time picks up.
    private void StrikeCover()
    {
        Vector3 point = launchPosition + velocity.normalized * coverDistance;
        transform.position = point;
        ShotEffects.CoverSparks(point, coverNormal, velocity);
        HitCover?.Invoke(this, point);
        Finish();
    }

    // A dodge: the fly-by hook, then the count; true for a close one the player answers.
    private bool Passed(bool close)
    {
        PassedPlayer?.Invoke(this, close);
        return GameSession.ReportDodge(close);
    }

    // Where this bullet crosses the plane of the eyes, seen from the running player: on the line through where
    // the bullet and the eyes both were at its last move, at its speed relative to him. Exact at any frame rate,
    // and the same sums for the real crossing and for a prediction. Gives the game seconds until the crossing
    // (negative once past it), the crossing point, the miss there and whether the bullet is in the frame there.
    private bool Meet(Camera view, out float seconds, out Vector3 crossing, out float missDistance, out bool inFrame)
    {
        Vector3 approach = velocity - Vector3.forward * CameraSpeed(view);
        seconds = float.PositiveInfinity;
        crossing = transform.position;
        missDistance = float.PositiveInfinity;
        inFrame = false;
        if (approach.z >= 0f) return false;
        seconds = (transform.position.z - eyesSeen.z) / -approach.z;
        crossing = transform.position + approach * seconds;
        missDistance = new Vector2(crossing.x - eyesSeen.x, crossing.y - eyesSeen.y).magnitude;
        inFrame = InFrameAtEyes(crossing - eyesSeen, approach, view);
        return true;
    }

    // Whether the bullet was still in the frame at the camera's near plane on its way in, i.e. right up to the
    // eyes; crossing is relative to the eyes. It counts as in the frame while any of its drawn size is inside.
    private bool InFrameAtEyes(Vector3 crossing, Vector3 approach, Camera view)
    {
        Quaternion eyes = view.transform.rotation;
        float closing = -Vector3.Dot(approach, eyes * Vector3.forward);
        if (closing <= 0f) return false;
        float depth = view.nearClipPlane;
        Vector3 local = Quaternion.Inverse(eyes) * (crossing - approach / closing * depth);
        float tanVertical = Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanHorizontal = Mathf.Min(tanVertical * view.aspect, Mathf.Tan(dodgeFrame.x * Mathf.Deg2Rad));
        tanVertical = Mathf.Min(tanVertical, Mathf.Tan(dodgeFrame.y * Mathf.Deg2Rad));
        float radius = FullSizeRadius();
        return Mathf.Abs(local.x) - radius < depth * tanHorizontal && Mathf.Abs(local.y) - radius < depth * tanVertical;
    }

    // Half the bullet's drawn size across the view once fully grown, as it is when it reaches the eyes.
    private float FullSizeRadius()
    {
        if (body == null) body = GetComponentInChildren<Renderer>();
        if (body == null) return 0f;
        float growth = hasInitialScale && transform.localScale.x > 0f
            ? Mathf.Max(1f, finalScaleMultiplier) * initialScale.x / transform.localScale.x
            : 1f;
        return Mathf.Max(body.bounds.extents.x, body.bounds.extents.y) * growth;
    }

    private static float CameraSpeed(Camera view) =>
        view.TryGetComponent<CameraMovement>(out var movement) ? movement.Speed : 0f;

    // The bullet's pool lives on the enemy that fired it; the gun nearest the launch point is the fallback.
    private EnemyGunman FindShooter()
    {
        var shooter = owner != null ? owner.GetComponentInParent<EnemyGunman>() : null;
        if (shooter == null)
        {
            float nearest = float.PositiveInfinity;
            foreach (var gunman in FindObjectsByType<EnemyGunman>(FindObjectsSortMode.None))
            {
                if (gunman.Muzzle == null) continue;
                float distance = (gunman.Muzzle.position - launchPosition).sqrMagnitude;
                if (distance < nearest)
                {
                    nearest = distance;
                    shooter = gunman;
                }
            }
        }
        return shooter;
    }

    private void Finish()
    {
        isFlying = false;
        approaching.Remove(this);
        if (owner != null) owner.Return(gameObject);
        else gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (hasInitialScale)
            transform.localScale = initialScale;
        approaching.Remove(this);
        isFlying = false;
        passedPlayer = false;
        owner = null;
        elapsedFlightTime = 0f;
        flightDuration = 0f;
    }
}

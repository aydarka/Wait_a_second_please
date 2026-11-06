using System.Collections.Generic;
using UnityEngine;

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

    public float ZSpeed => Mathf.Max(0.01f, speed);

    // True while any enemy bullet is still on its way to the player, i.e. there is still something to dodge.
    public static bool AnyApproaching => approaching.Count > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        approaching.Clear();
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
        approaching.Add(this);
    }

    private void Update()
    {
        if (!isFlying) return;
        Vector3 previousPosition = transform.position;
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
        rings.Emit(launchPosition, velocity, Vector3.Distance(launchPosition, transform.position),
            1f, Mathf.Max(1f, finalScaleMultiplier), meetingDistance);

        var player = Camera.main;
        if (player != null)
        {
            Vector3 playerPosition = player.transform.position;
            if (!passedPlayer && transform.position.z <= playerPosition.z)
            {
                passedPlayer = true;
                approaching.Remove(this);
                CheckHit(previousPosition, transform.position, player);
                if (!isFlying) return;
            }
            if (passedPlayer && transform.position.z < playerPosition.z - flyByDistance)
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
    private void CheckHit(Vector3 from, Vector3 to, Camera view)
    {
        // Measure the miss distance where the bullet crossed the player's Z plane.
        Vector3 playerPosition = view.transform.position;
        float span = from.z - to.z;
        float t = span > 0f ? Mathf.Clamp01((from.z - playerPosition.z) / span) : 1f;
        Vector3 crossing = Vector3.Lerp(from, to, t);
        Vector2 miss = new Vector2(crossing.x - playerPosition.x, crossing.y - playerPosition.y);
        float missDistance = miss.magnitude;
        if (missDistance <= hitRadius && InFrameAtEyes(crossing, view))
        {
            // The killing bullet and its rings vanish: the death screen shows who fired it instead.
            ShotEffects.ClearRings(rings.Owner);
            var shooter = FindShooter();
            GameSession.ReportPlayerHit(crossing, shooter != null ? shooter.transform : null);
            Finish();
        }
        else if (GameSession.ReportDodge(missDistance <= closeDistance))
        {
            // A close one: the player fires back at whoever sent this bullet.
            CounterShot.Fire(FindShooter());
        }
    }

    // Whether the bullet was still in the frame at the camera's near plane on its way in, i.e. right up to the
    // eyes. Its position there is traced back from the crossing against the camera's own run forward, so the
    // answer does not depend on frame rate.
    private bool InFrameAtEyes(Vector3 crossing, Camera view)
    {
        Transform eyes = view.transform;
        float cameraSpeed = view.TryGetComponent<CameraMovement>(out var movement) ? movement.Speed : 0f;
        Vector3 approach = velocity - Vector3.forward * cameraSpeed;
        float closing = -Vector3.Dot(approach, eyes.forward);
        if (closing <= 0f) return false;
        float depth = view.nearClipPlane;
        Vector3 local = eyes.InverseTransformPoint(crossing - approach / closing * depth);
        float tanVertical = Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanHorizontal = Mathf.Min(tanVertical * view.aspect, Mathf.Tan(dodgeFrame.x * Mathf.Deg2Rad));
        tanVertical = Mathf.Min(tanVertical, Mathf.Tan(dodgeFrame.y * Mathf.Deg2Rad));
        // The bullet counts as in the frame while any of its drawn size is inside it.
        var body = GetComponentInChildren<Renderer>();
        float radius = body != null ? Mathf.Max(body.bounds.extents.x, body.bounds.extents.y) : 0f;
        return Mathf.Abs(local.x) - radius < depth * tanHorizontal && Mathf.Abs(local.y) - radius < depth * tanVertical;
    }

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

using UnityEngine;

public class Projectile : MonoBehaviour
{
    [SerializeField, Min(0.01f), Tooltip("Speed along the world Z axis in metres per second.")]
    private float speed = 20f;
    [SerializeField, Min(1f)] private float finalScaleMultiplier = 5f;
    [SerializeField, Min(0f), Tooltip("Distance from the player's eye line that counts as a hit.")]
    private float hitRadius = 0.35f;
    [SerializeField, Min(0f), Tooltip("Distance behind the player after which a missed bullet returns to the pool.")]
    private float flyByDistance = 6f;
    [SerializeField, Min(0f), Tooltip("How far in front of the eyes the killing bullet hangs, metres.")]
    private float freezeDistance = 0.7f;

    [Header("Air rings")]
    [SerializeField, Min(0.05f), Tooltip("Flight distance between rings, metres.")]
    private float ringSpacing = 0.35f;
    [SerializeField, Tooltip("Smallest and largest ring radius at launch size, metres; rings grow with the bullet.")]
    private Vector2 ringRadius = new Vector2(0.03f, 0.065f);
    [SerializeField, Min(0.1f), Tooltip("Flight distance of one small-to-large wave of ring sizes, metres.")]
    private float ringWaveLength = 1.7f;
    [SerializeField, Min(0f), Tooltip("Ring line width at launch size, metres; the shader keeps a minimum in pixels.")]
    private float ringLineWidth = 0.004f;
    [SerializeField, Min(0.01f), Tooltip("Game seconds a ring hangs before it has crumbled away.")]
    private float ringLifetime = 0.7f;
    [SerializeField, Min(0f), Tooltip("Flight distance over which a new ring opens from bullet size, metres.")]
    private float ringOpenDistance = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("Ring strength is how slow time runs at its birth; weaker rings are not born.")]
    private float minRingStrength = 0.1f;

    private Vector3 initialScale;
    private float flightDuration;
    private float elapsedFlightTime;
    private bool hasInitialScale;
    private Vector3 destination;
    private Vector3 launchPosition;
    private Vector3 velocity;
    private float meetingDistance;
    private float nextRingDistance;
    private PrefabPool owner;
    private bool isFlying;
    private bool passedPlayer;
    private bool frozen;

    public float ZSpeed => Mathf.Max(0.01f, speed);

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
        nextRingDistance = ringSpacing;
        elapsedFlightTime = 0f;
        owner = pool;
        isFlying = true;
        passedPlayer = false;
        frozen = false;
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
        SpawnRings();

        var player = Camera.main;
        if (player != null)
        {
            Vector3 playerPosition = player.transform.position;
            if (!passedPlayer && transform.position.z <= playerPosition.z)
            {
                passedPlayer = true;
                CheckHit(previousPosition, transform.position, playerPosition);
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

    private void LateUpdate()
    {
        if (frozen) HoldBeforeEyes();
    }

    // Rings stay where they were born and only exist while time is slowed: the trail records the player's time.
    private void SpawnRings()
    {
        float pathSpeed = velocity.magnitude;
        if (pathSpeed <= 0f) return;
        Vector3 direction = velocity / pathSpeed;
        float flown = Vector3.Distance(launchPosition, transform.position);
        float strength = 1f - TimeScaleController.Flow;
        float maxScale = Mathf.Max(1f, finalScaleMultiplier);
        while (nextRingDistance <= flown)
        {
            float distance = nextRingDistance;
            nextRingDistance += ringSpacing;
            if (strength < minRingStrength) continue;

            float growth = Mathf.Lerp(1f, maxScale, meetingDistance > 0f ? Mathf.Clamp01(distance / meetingDistance) : 1f);
            // Sizes follow a wave along the path, like the rings around Neo's bullets, not the distance to the gun.
            float wave = 0.5f + 0.5f * Mathf.Sin(distance * 2f * Mathf.PI / ringWaveLength);
            float radius = Mathf.Lerp(ringRadius.x, ringRadius.y, wave) * growth * Random.Range(0.9f, 1.1f);
            float birthTime = Time.time - (flown - distance) / pathSpeed;
            ShotEffects.AddRing(launchPosition + direction * distance, direction, radius, ringLineWidth * growth,
                strength, birthTime, ringLifetime, ringOpenDistance / pathSpeed);
        }
    }

    private void CheckHit(Vector3 from, Vector3 to, Vector3 playerPosition)
    {
        // Measure the miss distance where the bullet crossed the player's Z plane.
        float span = from.z - to.z;
        float t = span > 0f ? Mathf.Clamp01((from.z - playerPosition.z) / span) : 1f;
        Vector3 crossing = Vector3.Lerp(from, to, t);
        Vector2 miss = new Vector2(crossing.x - playerPosition.x, crossing.y - playerPosition.y);
        float missDistance = miss.magnitude;
        if (missDistance <= hitRadius)
        {
            GameSession.ReportPlayerHit(crossing);
            isFlying = false;
            frozen = true;
            HoldBeforeEyes();
        }
        else
        {
            GameSession.ReportDodge(missDistance);
        }
    }

    // The killing bullet hangs in front of the eyes, framed by the hole in the death screen.
    private void HoldBeforeEyes()
    {
        var player = Camera.main;
        if (player == null) return;
        Transform eyes = player.transform;
        transform.SetPositionAndRotation(eyes.position + eyes.forward * freezeDistance, Quaternion.LookRotation(-eyes.forward));
    }

    private void Finish()
    {
        isFlying = false;
        if (owner != null) owner.Return(gameObject);
        else gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (hasInitialScale)
            transform.localScale = initialScale;
        isFlying = false;
        passedPlayer = false;
        frozen = false;
        owner = null;
        elapsedFlightTime = 0f;
        flightDuration = 0f;
    }
}

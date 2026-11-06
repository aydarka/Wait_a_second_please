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

    [SerializeField] private AirRingTrail rings = new AirRingTrail();

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
            // The killing bullet and its rings vanish: the death screen shows who fired it instead.
            ShotEffects.ClearRings(rings.Owner);
            var shooter = FindShooter();
            GameSession.ReportPlayerHit(crossing, shooter != null ? shooter.transform : null);
            Finish();
        }
        else if (GameSession.ReportDodge(missDistance))
        {
            // A close one: the player fires back at whoever sent this bullet.
            CounterShot.Fire(FindShooter());
        }
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
        if (owner != null) owner.Return(gameObject);
        else gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (hasInitialScale)
            transform.localScale = initialScale;
        isFlying = false;
        passedPlayer = false;
        owner = null;
        elapsedFlightTime = 0f;
        flightDuration = 0f;
    }
}

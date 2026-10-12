using UnityEngine;

public class Projectile : MonoBehaviour
{
    [SerializeField, Min(0.01f), Tooltip("Speed along the world Z axis in metres per second.")]
    private float speed = 20f;
    [SerializeField, Min(1f)] private float finalScaleMultiplier = 5f;
    private Vector3 initialScale;
    private float flightDuration;
    private float elapsedFlightTime;
    private bool hasInitialScale;
    private Vector3 destination;
    private Vector3 launchPosition;
    private PrefabPool owner;
    private bool isFlying;

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
        elapsedFlightTime = 0f;
        owner = pool;
        isFlying = true;
    }

    private void Update()
    {
        if (!isFlying) return;
        elapsedFlightTime += Time.deltaTime;
        float progress = flightDuration > 0f
            ? Mathf.Clamp01(elapsedFlightTime / flightDuration)
            : 1f;
        // Interpolate all axes over the predicted meeting time, keeping Z speed constant.
        transform.position = Vector3.Lerp(launchPosition, destination, progress);
        transform.localScale = initialScale * Mathf.Lerp(1f, Mathf.Max(1f, finalScaleMultiplier), progress);
        if (progress < 1f) return;
        isFlying = false;
        if (owner != null) owner.Return(gameObject);
        else gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (hasInitialScale)
            transform.localScale = initialScale;
        isFlying = false;
        owner = null;
        elapsedFlightTime = 0f;
        flightDuration = 0f;
    }
}

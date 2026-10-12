using UnityEngine;

public class Projectile : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float speed = 1f;
    [SerializeField, Min(1f)] private float finalScaleMultiplier = 5f;
    private Vector3 initialScale;
    private float flightDistance;
    private bool hasInitialScale;
    private Vector3 destination;
    private PrefabPool owner;
    private bool isFlying;

    public void Launch(Vector3 targetPosition, PrefabPool pool)
    {
        // Launch can run before Awake when the pool creates an inactive instance.
        if (!hasInitialScale)
        {
            initialScale = transform.localScale;
            hasInitialScale = true;
        }
        transform.localScale = initialScale;
        destination = targetPosition;
        flightDistance = Vector3.Distance(transform.position, destination);
        owner = pool;
        isFlying = true;
    }

    private void Update()
    {
        if (!isFlying) return;
        transform.position = Vector3.MoveTowards(transform.position, destination,
            Mathf.Max(0.01f, speed) * Time.deltaTime);
        float progress = flightDistance > 0f
            ? Mathf.Clamp01(1f - Vector3.Distance(transform.position, destination) / flightDistance)
            : 1f;
        transform.localScale = initialScale * Mathf.Lerp(1f, Mathf.Max(1f, finalScaleMultiplier), progress);
        if (transform.position != destination) return;
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
    }
}

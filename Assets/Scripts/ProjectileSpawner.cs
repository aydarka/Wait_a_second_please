using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectileSpawner : MonoBehaviour
{
    [SerializeField] private Transform shootPoint;
    [SerializeField] private PrefabPool pool;
    [SerializeField, Min(0f)] private float initialDelay = 3f;
    [SerializeField, Min(0.01f)] private float shotInterval = 1f;
    private float timeUntilShot;
    private bool configurationValid;

    private void Awake()
    {
        configurationValid = shootPoint != null && pool != null &&
            pool.Prefab != null && pool.Prefab.TryGetComponent<Projectile>(out _);
        if (!configurationValid)
            Debug.LogError("ProjectileSpawner requires a ShootPoint and a pool with a Projectile prefab.", this);
    }

    private void OnEnable()
    {
        timeUntilShot = Mathf.Max(0f, initialDelay);
    }

    private void Update()
    {
        if (!configurationValid) return;
        timeUntilShot -= Time.deltaTime;
        if (timeUntilShot > 0f) return;
        // Preserve the interval without producing catch-up bursts after a long frame.
        timeUntilShot = Mathf.Max(0f, timeUntilShot + Mathf.Max(0.01f, shotInterval));
        var targetCamera = Camera.main;
        if (targetCamera == null) return;
        Vector3 destination = targetCamera.transform.position;
        var bullet = pool.Rent(shootPoint.position, shootPoint.rotation);
        if (bullet == null) return;
        bullet.GetComponent<Projectile>().Launch(destination, pool);
        bullet.SetActive(true);
    }
}

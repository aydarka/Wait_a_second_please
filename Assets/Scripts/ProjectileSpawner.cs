using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectileSpawner : MonoBehaviour
{
    [SerializeField] private Transform shootPoint;
    [SerializeField] private PrefabPool pool;
    [SerializeField, Min(0f)] private float initialDelay = 3f;
    [SerializeField, Min(0.01f)] private float shotInterval = 1f;
    [SerializeField, Min(0)] private int maxShots = 3;
    private float timeUntilShot;
    private int shotsFired;
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
        shotsFired = 0;
        timeUntilShot = Mathf.Max(0f, initialDelay);
    }

    private void LateUpdate()
    {
        if (!configurationValid || shotsFired >= maxShots) return;
        timeUntilShot -= Time.deltaTime;
        if (timeUntilShot > 0f) return;
        // Preserve the interval without producing catch-up bursts after a long frame.
        timeUntilShot = Mathf.Max(0f, timeUntilShot + Mathf.Max(0.01f, shotInterval));
        var targetCamera = Camera.main;
        if (targetCamera == null) return;
        Vector3 destination = targetCamera.transform.position;
        float zDistance = shootPoint.position.z - destination.z;
        // The player has already passed this firing point; there is no forward meeting.
        if (zDistance <= 0f) return;

        float cameraSpeed = targetCamera.TryGetComponent<CameraMovement>(out var movement)
            ? movement.Speed : 0f;
        var bullet = pool.Rent(shootPoint.position, shootPoint.rotation);
        if (bullet == null) return;
        var projectile = bullet.GetComponent<Projectile>();
        float duration = zDistance / (projectile.ZSpeed + cameraSpeed);
        destination.z += cameraSpeed * duration;
        projectile.Launch(destination, duration, pool);
        bullet.SetActive(true);
        shotsFired++;
    }
}

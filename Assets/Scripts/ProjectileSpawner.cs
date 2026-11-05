using UnityEngine;

// The enemy's gun: launches a pooled bullet from the muzzle to where the player will be.
// When to fire is decided by EnemyGunman.
[DisallowMultipleComponent]
public sealed class ProjectileSpawner : MonoBehaviour
{
    [SerializeField] private Transform shootPoint;
    [SerializeField] private PrefabPool pool;
    private bool configurationValid;

    public Transform ShootPoint => shootPoint;

    private void Awake()
    {
        configurationValid = shootPoint != null && pool != null &&
            pool.Prefab != null && pool.Prefab.TryGetComponent<Projectile>(out _);
        if (!configurationValid)
            Debug.LogError("ProjectileSpawner requires a ShootPoint and a pool with a Projectile prefab.", this);
    }

    // Offset shifts the aim point across the player's view in metres, for spread within a burst.
    public bool Fire(Vector2 offset)
    {
        if (!configurationValid) return false;
        var targetCamera = Camera.main;
        if (targetCamera == null) return false;
        Vector3 destination = targetCamera.transform.position;
        float zDistance = shootPoint.position.z - destination.z;
        // The player has already passed this firing point; there is no forward meeting.
        if (zDistance <= 0f) return false;

        float cameraSpeed = targetCamera.TryGetComponent<CameraMovement>(out var movement)
            ? movement.Speed : 0f;
        var bullet = pool.Rent(shootPoint.position, shootPoint.rotation);
        if (bullet == null) return false;
        var projectile = bullet.GetComponent<Projectile>();
        float duration = zDistance / (projectile.ZSpeed + cameraSpeed);
        destination.z += cameraSpeed * duration;
        destination.x += offset.x;
        destination.y += offset.y;
        projectile.Launch(destination, duration, pool);
        bullet.SetActive(true);
        return true;
    }
}

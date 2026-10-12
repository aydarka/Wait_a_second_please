using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemySpawner : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private PrefabPool pool;
    [SerializeField, Min(0.01f)] private float spawnDistance = 30f;
    [SerializeField, Min(0f)] private float minRadius = 2f;
    [SerializeField, Min(0f)] private float maxRadius = 6f;
    [SerializeField, Range(-180f, 180f), Tooltip("Degrees from world +Y: negative left, positive right.")]
    private float minAngle = -120f;
    [SerializeField, Range(-180f, 180f)] private float maxAngle = 120f;
    [SerializeField, Min(0f)] private float initialDelay = 10f;
    [SerializeField, Min(0.01f)] private float minSpawnInterval = 3f;
    [SerializeField, Min(0.01f)] private float maxSpawnInterval = 5f;

    private float timeUntilSpawn;
    private bool configurationValid;

    private void Awake()
    {
        configurationValid = player != null && pool != null && pool.Prefab != null &&
            pool.Prefab.TryGetComponent<ReturnToPoolBehindPlayer>(out _);
        if (!configurationValid)
            Debug.LogError("EnemySpawner requires a player and a pool with a ReturnToPoolBehindPlayer prefab.", this);
    }

    private void OnEnable()
    {
        timeUntilSpawn = Mathf.Max(0f, initialDelay);
    }

    private void Update()
    {
        if (!configurationValid || player == null || pool == null) return;
        timeUntilSpawn -= Time.deltaTime;
        if (timeUntilSpawn > 0f) return;

        float minimumInterval = Mathf.Max(0.01f, minSpawnInterval);
        timeUntilSpawn = Random.Range(minimumInterval, Mathf.Max(minimumInterval, maxSpawnInterval));

        float innerRadius = Mathf.Max(0f, minRadius);
        float outerRadius = Mathf.Max(innerRadius, maxRadius);
        // Uniform sampling by area within the selected XY annular sector.
        float radius = Mathf.Sqrt(Random.Range(innerRadius * innerRadius, outerRadius * outerRadius));
        float minimumAngle = Mathf.Clamp(minAngle, -180f, 180f);
        float maximumAngle = Mathf.Clamp(maxAngle, minimumAngle, 180f);
        float angle = Random.Range(minimumAngle, maximumAngle) * Mathf.Deg2Rad;
        Vector3 position = player.position + new Vector3(
            Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius, Mathf.Max(0.01f, spawnDistance));

        var enemy = pool.Rent(position, pool.Prefab.transform.rotation);
        if (enemy == null) return;
        enemy.GetComponent<ReturnToPoolBehindPlayer>().Initialize(player, pool);
        enemy.SetActive(true);
    }

    private void OnValidate()
    {
        spawnDistance = Mathf.Max(0.01f, spawnDistance);
        minRadius = Mathf.Max(0f, minRadius);
        maxRadius = Mathf.Max(minRadius, maxRadius);
        minAngle = Mathf.Clamp(minAngle, -180f, 180f);
        maxAngle = Mathf.Clamp(maxAngle, minAngle, 180f);
        initialDelay = Mathf.Max(0f, initialDelay);
        minSpawnInterval = Mathf.Max(0.01f, minSpawnInterval);
        maxSpawnInterval = Mathf.Max(minSpawnInterval, maxSpawnInterval);
    }
}

using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FloorSpawner : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private PrefabPool pool;
    [SerializeField, Min(1)] private int minRowWidth = 3;
    [SerializeField, Min(1)] private int maxRowWidth = 5;
    [SerializeField, Min(0f)] private float distanceAhead = 55f;
    [SerializeField] private float floorY = -0.25f;

    private sealed class Row
    {
        public float FrontZ;
        public readonly List<GameObject> Blocks = new List<GameObject>();
    }

    private readonly Queue<Row> activeRows = new Queue<Row>();
    private readonly Stack<Row> availableRows = new Stack<Row>();
    private float blockWidth;
    private float blockDepth;
    private float centerX;
    private float nextRowZ;
    private bool configurationValid;

    private void Awake()
    {
        if (player != null && pool != null && pool.Prefab != null &&
            pool.Prefab.TryGetComponent<MeshFilter>(out var meshFilter) && meshFilter.sharedMesh != null)
        {
            Vector3 size = Vector3.Scale(meshFilter.sharedMesh.bounds.size, pool.Prefab.transform.localScale);
            blockWidth = Mathf.Abs(size.x);
            blockDepth = Mathf.Abs(size.z);
            configurationValid = blockWidth > 0f && blockDepth > 0f;
        }

        if (!configurationValid)
            Debug.LogError("FloorSpawner requires a player and a pool with a non-zero sized Cube mesh prefab.", this);
    }

    private void OnEnable()
    {
        if (!configurationValid) return;
        centerX = player.position.x;
        nextRowZ = player.position.z;
        MaintainFloor();
    }

    private void LateUpdate()
    {
        if (configurationValid && player != null && pool != null)
            MaintainFloor();
    }

    private void MaintainFloor()
    {
        float playerZ = player.position.z;
        while (activeRows.Count > 0 && activeRows.Peek().FrontZ < playerZ)
            ReturnRow(activeRows.Dequeue());

        // Skip unseen rows if the player jumps forward instead of allocating a long trail.
        float firstVisibleCenter = playerZ - blockDepth * 0.5f;
        if (nextRowZ < firstVisibleCenter)
            nextRowZ += Mathf.Ceil((firstVisibleCenter - nextRowZ) / blockDepth) * blockDepth;

        float frontLimit = playerZ + Mathf.Max(0f, distanceAhead);
        while (nextRowZ - blockDepth * 0.5f < frontLimit)
        {
            if (!SpawnRow(nextRowZ)) break;
            nextRowZ += blockDepth;
        }
    }

    private bool SpawnRow(float z)
    {
        int minimum = Mathf.Max(1, minRowWidth);
        int maximum = Mathf.Max(minimum, maxRowWidth);
        int width = Random.Range(minimum, maximum + 1);
        Row row = availableRows.Count > 0 ? availableRows.Pop() : new Row();
        row.FrontZ = z + blockDepth * 0.5f;
        float leftX = centerX - (width - 1) * blockWidth * 0.5f;

        for (int column = 0; column < width; column++)
        {
            var block = pool.Rent(new Vector3(leftX + column * blockWidth, floorY, z), Quaternion.identity);
            if (block == null)
            {
                ReturnRow(row);
                return false;
            }
            block.SetActive(true);
            row.Blocks.Add(block);
        }
        activeRows.Enqueue(row);
        return true;
    }

    private void ReturnRow(Row row)
    {
        if (pool != null)
            foreach (var block in row.Blocks)
                pool.Return(block);
        row.Blocks.Clear();
        availableRows.Push(row);
    }

    private void OnDisable()
    {
        while (activeRows.Count > 0)
            ReturnRow(activeRows.Dequeue());
    }

    private void OnValidate()
    {
        minRowWidth = Mathf.Max(1, minRowWidth);
        maxRowWidth = Mathf.Max(minRowWidth, maxRowWidth);
        distanceAhead = Mathf.Max(0f, distanceAhead);
    }
}

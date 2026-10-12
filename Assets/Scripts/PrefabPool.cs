using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PrefabPool : MonoBehaviour
{
    [SerializeField] private GameObject prefab;
    private readonly Stack<GameObject> available = new Stack<GameObject>();
    private readonly HashSet<GameObject> instances = new HashSet<GameObject>();
    private readonly HashSet<GameObject> rented = new HashSet<GameObject>();
    private Transform storage;

    public GameObject Prefab => prefab;

    // Returns an inactive object for initialization before OnEnable.
    public GameObject Rent(Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
        {
            Debug.LogError("PrefabPool requires a prefab.", this);
            return null;
        }
        EnsureStorage();
        GameObject instance = null;
        while (available.Count > 0 && instance == null)
            instance = available.Pop();
        if (instance == null)
        {
            instance = Instantiate(prefab, storage);
            instance.SetActive(false);
            instances.Add(instance);
        }
        instance.transform.SetParent(null, false);
        instance.transform.SetPositionAndRotation(position, rotation);
        rented.Add(instance);
        return instance;
    }

    public void Return(GameObject instance)
    {
        if (instance == null || !rented.Remove(instance)) return;
        instance.SetActive(false);
        EnsureStorage();
        instance.transform.SetParent(storage, false);
        available.Push(instance);
    }

    private void EnsureStorage()
    {
        if (storage != null) return;
        var root = new GameObject($"{name} Pool Storage");
        root.SetActive(false);
        root.transform.SetParent(transform, false);
        storage = root.transform;
    }

    private void OnDestroy()
    {
        foreach (var instance in instances)
            if (instance != null) Destroy(instance);
        available.Clear();
        rented.Clear();
        instances.Clear();
    }
}

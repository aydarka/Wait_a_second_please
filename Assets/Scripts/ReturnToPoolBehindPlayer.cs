using UnityEngine;

[DisallowMultipleComponent]
public sealed class ReturnToPoolBehindPlayer : MonoBehaviour
{
    private Transform player;
    private PrefabPool owner;

    public void Initialize(Transform playerTransform, PrefabPool pool)
    {
        player = playerTransform;
        owner = pool;
    }

    private void LateUpdate()
    {
        if (player != null && owner != null && transform.position.z < player.position.z)
            owner.Return(gameObject);
    }

    private void OnDisable()
    {
        player = null;
        owner = null;
    }
}

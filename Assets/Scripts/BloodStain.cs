using UnityEngine;

// A red splash left on a wall or on the road where an enemy died. It stays while the player can still
// see it and removes itself once the player has run well past it.
[DisallowMultipleComponent]
public sealed class BloodStain : MonoBehaviour
{
    private const float KeepBehindPlayer = 15f;

    private Mesh ownMesh;

    public static BloodStain Create(Mesh mesh, Material material, Vector3 position, Quaternion rotation, float size)
    {
        var stain = new GameObject("Blood Stain", typeof(MeshFilter), typeof(MeshRenderer), typeof(BloodStain));
        stain.transform.SetPositionAndRotation(position, rotation);
        stain.transform.localScale = Vector3.one * size;
        stain.GetComponent<MeshFilter>().sharedMesh = mesh;
        var stainRenderer = stain.GetComponent<MeshRenderer>();
        stainRenderer.sharedMaterial = material;
        stainRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        stainRenderer.receiveShadows = false;
        var component = stain.GetComponent<BloodStain>();
        component.ownMesh = mesh;
        return component;
    }

    private void Update()
    {
        var view = Camera.main;
        if (view != null && transform.position.z < view.transform.position.z - KeepBehindPlayer)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (ownMesh != null) Destroy(ownMesh);
    }
}

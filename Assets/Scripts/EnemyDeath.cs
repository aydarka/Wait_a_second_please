using System;
using System.Collections.Generic;
using UnityEngine;

// The agreed enemy death, all in game time so it hangs in slow motion: the shot knocks the body back,
// a flat red splash bursts behind the silhouette and the hat flies off spinning; then the body sinks into
// the dark and a red stain stays on the wall behind it, or on the road when no wall is near.
// EnemyGunman owns the pose; this class adds the fall, the hat, the blood and the fade timing.
[Serializable]
public sealed class EnemyDeath
{
    [SerializeField] private Transform hat;
    [SerializeField, Tooltip("Flat red without a rim, such as the enemy shader with red colours and no rim width.")]
    private Material bloodMaterial;

    [Header("Fall, game seconds")]
    [SerializeField, Tooltip("How far the body tips back, degrees from upright.")] private float fallAngle = 80f;
    [SerializeField, Min(0.01f)] private float fallDuration = 0.55f;
    [SerializeField, Tooltip("Extra backward bend of the chest, degrees.")] private float backArch = 25f;
    [SerializeField, Min(0f), Tooltip("Time before the body starts sinking into the dark.")] private float fadeDelay = 0.9f;
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.6f;

    [Header("Blood")]
    [SerializeField, Min(0f), Tooltip("Width of the splash in the air, metres.")] private float splashSize = 1.4f;
    [SerializeField, Min(0.01f)] private float splashGrow = 0.12f;
    [SerializeField, Min(0f), Tooltip("How far behind the chest the splash bursts, metres.")] private float splashBehind = 0.6f;
    [SerializeField, Min(0f), Tooltip("How far the splash flies on backwards, staying behind the falling body, metres.")]
    private float splashDrift = 1.1f;
    [SerializeField, Min(0.01f)] private float splashDriftTime = 0.3f;
    [SerializeField, Min(0f), Tooltip("Width of the stain left behind, metres.")] private float stainSize = 1.6f;
    [SerializeField, Min(0f), Tooltip("Farthest wall behind the enemy that gets the stain, metres.")] private float stainReach = 8f;

    [Header("Hat")]
    [SerializeField, Tooltip("Launch speed of the hat: backwards and upwards, metres per second.")]
    private Vector2 hatLaunch = new Vector2(1.6f, 3f);
    [SerializeField, Tooltip("Spin of the flying hat, degrees per second.")] private float hatSpin = 540f;

    private const float CutAboveFloor = 4.5f;

    private Vector3 hatLocalPosition;
    private Quaternion hatLocalRotation;
    private float age = -1f;
    private Vector3 back;
    private float floor;
    private Vector3 hatStart;
    private Quaternion hatStartRotation;
    private Vector3 hatVelocity;
    private Vector3 hatSpinAxis;
    private bool hatLanded;
    private Vector3 hatRest;
    private Quaternion hatRestRotation;
    private GameObject splash;
    private Mesh splashMesh;
    private Vector3 splashCentre;
    private BloodStain stain;

    public bool IsValid => hat != null && bloodMaterial != null;

    public bool IsActive => age >= 0f;

    public void Capture()
    {
        hatLocalPosition = hat.localPosition;
        hatLocalRotation = hat.localRotation;
    }

    // Puts everything back for a pooled enemy that comes alive again.
    public void ResetState()
    {
        age = -1f;
        if (hat != null) hat.SetLocalPositionAndRotation(hatLocalPosition, hatLocalRotation);
        if (splash != null) splash.SetActive(false);
        // A stain that never got shown belongs to a death that was cut short.
        if (stain != null && !stain.gameObject.activeSelf) UnityEngine.Object.Destroy(stain.gameObject);
        stain = null;
    }

    public void Begin(Transform root, Vector3 shotDirection, Vector3 chest, float floorHeight, LayerMask walls)
    {
        age = 0f;
        floor = floorHeight;
        back = new Vector3(shotDirection.x, 0f, shotDirection.z);
        back = back.sqrMagnitude > 0.0001f ? back.normalized : -root.forward;

        hatStart = hat.position;
        hatStartRotation = hat.rotation;
        hatVelocity = back * hatLaunch.x + Vector3.up * hatLaunch.y + root.right * UnityEngine.Random.Range(-0.6f, 0.6f);
        hatSpinAxis = (root.right + UnityEngine.Random.insideUnitSphere * 0.5f).normalized;
        hatLanded = false;

        if (splash == null)
        {
            splash = new GameObject("Blood Splash", typeof(MeshFilter), typeof(MeshRenderer));
            var splashRenderer = splash.GetComponent<MeshRenderer>();
            splashRenderer.sharedMaterial = bloodMaterial;
            splashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            splashRenderer.receiveShadows = false;
        }
        if (splashMesh != null) UnityEngine.Object.Destroy(splashMesh);
        splashMesh = BuildSplat(UnityEngine.Random.Range(int.MinValue, int.MaxValue), 8);
        splash.GetComponent<MeshFilter>().sharedMesh = splashMesh;
        splashCentre = chest + back * splashBehind;
        splash.transform.position = splashCentre;
        splash.transform.localScale = Vector3.zero;
        splash.SetActive(true);

        // The stain lands where the blood would: on the first wall behind, or on the road behind the body.
        Mesh stainMesh = BuildSplat(UnityEngine.Random.Range(int.MinValue, int.MaxValue), 10);
        Vector3 stainPosition;
        Quaternion stainRotation;
        if (walls.value != 0 && Physics.Raycast(chest, back, out RaycastHit hit, stainReach, walls, QueryTriggerInteraction.Ignore))
        {
            stainPosition = hit.point + hit.normal * 0.01f;
            stainRotation = Quaternion.LookRotation(-hit.normal, Vector3.up) *
                Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), Vector3.forward);
        }
        else
        {
            stainPosition = new Vector3(root.position.x, floor + 0.01f, root.position.z) + back * 0.9f;
            stainRotation = Quaternion.LookRotation(Vector3.down, back);
        }
        stain = BloodStain.Create(stainMesh, bloodMaterial, stainPosition, stainRotation, stainSize);
        stain.gameObject.SetActive(false);
    }

    public void Advance(float deltaTime)
    {
        if (age >= 0f) age += Mathf.Max(0f, deltaTime);
    }

    // The root turns about the feet so the body tips back along the shot.
    public Quaternion Tilt()
    {
        float angle = fallAngle * FallWeight * Mathf.Deg2Rad;
        return Quaternion.FromToRotation(Vector3.up, Vector3.RotateTowards(Vector3.up, back, angle, 0f));
    }

    private float FallWeight
    {
        get
        {
            float t = Mathf.Clamp01(age / fallDuration);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }
    }

    // Runs after the gunman has posed the body for this frame.
    public void AfterPose(Transform root, Transform chestBone, Camera view)
    {
        if (age < 0f) return;
        chestBone.rotation = Quaternion.AngleAxis(-backArch * FallWeight, root.right) * chestBone.rotation;

        if (!hatLanded)
        {
            Vector3 position = hatStart + hatVelocity * age + 0.5f * Physics.gravity * (age * age);
            Quaternion rotation = Quaternion.AngleAxis(hatSpin * age, hatSpinAxis) * hatStartRotation;
            if (position.y <= floor + 0.05f && age > 0.1f)
            {
                hatLanded = true;
                hatRest = new Vector3(position.x, floor + 0.05f, position.z);
                hatRestRotation = rotation;
            }
            hat.SetPositionAndRotation(position, rotation);
        }
        if (hatLanded) hat.SetPositionAndRotation(hatRest, hatRestRotation);

        float grown = Mathf.Clamp01(age / splashGrow);
        grown = 1f - (1f - grown) * (1f - grown);
        float shrink = 1f - Mathf.SmoothStep(0f, 1f, FadeProgress);
        splash.transform.localScale = Vector3.one * (splashSize * grown * shrink);
        // The blood flies on faster than the body falls, so the body never passes in front of it.
        float drift = Mathf.Clamp01(age / splashDriftTime);
        Vector3 splashPosition = splashCentre + back * (splashDrift * (1f - (1f - drift) * (1f - drift)));
        splash.transform.position = splashPosition;
        if (view != null)
            splash.transform.rotation = Quaternion.LookRotation(splashPosition - view.transform.position, Vector3.up);
        if (shrink <= 0f) splash.SetActive(false);

        if (stain != null && !stain.gameObject.activeSelf && age >= splashGrow) stain.gameObject.SetActive(true);
    }

    private float FadeProgress => Mathf.Clamp01((age - fadeDelay) / fadeDuration);

    // While the body sinks into the dark, everything below this height is cut away.
    public bool Cutting(out float height)
    {
        height = floor - 0.05f + CutAboveFloor * Mathf.SmoothStep(0f, 1f, FadeProgress);
        return age >= fadeDelay;
    }

    // A comic splat in the XY plane, about one unit across: a spiky blob and droplets, visible from both sides.
    private static Mesh BuildSplat(int seed, int droplets)
    {
        var random = new System.Random(seed);
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        AddBlob(vertices, triangles, Vector2.zero, 0.3f, 18, true, random);
        for (int i = 0; i < droplets; i++)
        {
            float angle = (float)(random.NextDouble() * Math.PI * 2.0);
            float distance = 0.36f + (float)random.NextDouble() * 0.16f;
            float size = 0.02f + (float)random.NextDouble() * 0.05f;
            AddBlob(vertices, triangles, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance, size, 7, false, random);
        }
        var mesh = new Mesh { name = "Blood Splat" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddBlob(List<Vector3> vertices, List<int> triangles, Vector2 centre, float radius, int points,
        bool spiky, System.Random random)
    {
        int first = vertices.Count;
        vertices.Add(centre);
        for (int i = 0; i < points; i++)
        {
            float angle = i / (float)points * Mathf.PI * 2f;
            float reach = radius * (0.75f + 0.5f * (float)random.NextDouble());
            if (spiky && i % 3 == 0) reach *= 1.5f + 0.4f * (float)random.NextDouble();
            vertices.Add(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach);
        }
        for (int i = 0; i < points; i++)
        {
            int a = first + 1 + i;
            int b = first + 1 + (i + 1) % points;
            // Both windings, so the splat shows from either side without a two-sided shader.
            triangles.Add(first); triangles.Add(a); triangles.Add(b);
            triangles.Add(first); triangles.Add(b); triangles.Add(a);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Shot visuals that are not part of a bullet: the Matrix-style rings a bullet leaves hanging in the air,
// the red aim glint on a levelling gun and the yellow muzzle flash.
// Created on first use, one per scene. Rings age in game time inside WaitASecond/ShotRings,
// so once a ring is placed the CPU never touches it again.
[DisallowMultipleComponent]
public sealed class ShotEffects : MonoBehaviour
{
    private const int RingCapacity = 256;
    private const float FlashDuration = 0.06f;
    private const float ReferenceScreenHeight = 1080f;
    // Tells sit this far towards the camera so the gun never cuts into them, metres.
    private const float TowardCamera = 0.15f;
    private const float BoundsSize = 400f;

    private static readonly int ShotTimeId = Shader.PropertyToID("_ShotTime");
    private static readonly Vector2[] Corners =
        { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };

    private static ShotEffects instance;

    private struct Flash
    {
        public Transform Muzzle;
        public float Born;
        public int Seed;
    }

    private Mesh ringMesh;
    private readonly List<Vector3> ringPositions = new List<Vector3>(new Vector3[RingCapacity * 4]);
    private readonly List<Vector4> ringShapes = new List<Vector4>(new Vector4[RingCapacity * 4]);
    private readonly List<Vector4> ringLives = new List<Vector4>(new Vector4[RingCapacity * 4]);
    private readonly List<Vector4> ringGrowth = new List<Vector4>(new Vector4[RingCapacity * 4]);
    private int nextRing;
    private int ringSeed;
    private bool ringsDirty;

    private Mesh inkMesh;
    private readonly List<Vector3> inkVertices = new List<Vector3>();
    private readonly List<Color> inkColors = new List<Color>();
    private readonly List<int> inkTriangles = new List<int>();
    private readonly List<Vector2> starPoints = new List<Vector2>();
    private readonly List<EnemyGunman> glints = new List<EnemyGunman>();
    private readonly List<Flash> flashes = new List<Flash>();
    private Color red = new Color(0.89f, 0.09f, 0.04f);
    private Color yellow = new Color(0.95f, 0.76f, 0.11f);
    private Color black = Color.black;

    // Leaves a ring across the flight path; birthTime is in Time.time (game) seconds.
    public static void AddRing(Vector3 centre, Vector3 direction, float radius, float lineWidth,
        float strength, float birthTime, float lifetime, float openTime)
    {
        var effects = Current;
        if (effects != null) effects.PutRing(centre, direction, radius, lineWidth, strength, birthTime, lifetime, openTime);
    }

    private static ShotEffects Current
    {
        get
        {
            if (instance == null && Application.isPlaying)
                instance = new GameObject("Shot Effects").AddComponent<ShotEffects>();
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Subscribe()
    {
        instance = null;
        EnemyGunman.AimStarted -= OnAimStarted;
        EnemyGunman.AimStarted += OnAimStarted;
        EnemyGunman.ShotFired -= OnShotFired;
        EnemyGunman.ShotFired += OnShotFired;
    }

    private static void OnAimStarted(EnemyGunman enemy, float warningSeconds)
    {
        var effects = Current;
        if (effects != null && enemy != null && !effects.glints.Contains(enemy)) effects.glints.Add(enemy);
    }

    private static void OnShotFired(EnemyGunman enemy)
    {
        var effects = Current;
        if (effects == null || enemy == null || enemy.Muzzle == null) return;
        effects.flashes.Add(new Flash { Muzzle = enemy.Muzzle, Born = Time.time, Seed = Random.Range(0, int.MaxValue) });
    }

    private void Awake()
    {
        var ringMaterial = Resources.Load<Material>("ShotRings");
        var inkMaterial = Resources.Load<Material>("ShotInk");
        if (ringMaterial == null || inkMaterial == null)
        {
            Debug.LogError("ShotEffects requires the ShotRings and ShotInk materials in a Resources folder.", this);
            enabled = false;
            return;
        }
        if (QualitySettings.activeColorSpace == ColorSpace.Linear)
        {
            // Vertex colours reach the shader untouched, so they are converted like material colours are.
            red = red.linear;
            yellow = yellow.linear;
        }

        ringMesh = CreateLayer("Rings", ringMaterial);
        var triangles = new int[RingCapacity * 6];
        for (int i = 0; i < RingCapacity; i++)
        {
            for (int corner = 0; corner < 4; corner++)
                ringLives[i * 4 + corner] = new Vector4(-1e6f, 1f, 0f, 0f);
            int v = i * 4;
            triangles[i * 6] = v;
            triangles[i * 6 + 1] = v + 1;
            triangles[i * 6 + 2] = v + 2;
            triangles[i * 6 + 3] = v;
            triangles[i * 6 + 4] = v + 2;
            triangles[i * 6 + 5] = v + 3;
        }
        UploadRings();
        ringMesh.SetTriangles(triangles, 0);
        inkMesh = CreateLayer("Tells", inkMaterial);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (ringMesh != null) Destroy(ringMesh);
        if (inkMesh != null) Destroy(inkMesh);
    }

    private Mesh CreateLayer(string layerName, Material material)
    {
        var layer = new GameObject(layerName, typeof(MeshFilter), typeof(MeshRenderer));
        layer.transform.SetParent(transform, false);
        var mesh = new Mesh { name = layerName };
        mesh.MarkDynamic();
        layer.GetComponent<MeshFilter>().sharedMesh = mesh;
        var meshRenderer = layer.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        return mesh;
    }

    private void PutRing(Vector3 centre, Vector3 direction, float radius, float lineWidth,
        float strength, float birthTime, float lifetime, float openTime)
    {
        if (ringMesh == null || direction.sqrMagnitude <= 0f) return;
        Vector3 axis = direction.normalized;
        Vector3 side = Vector3.Cross(Vector3.up, axis);
        if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(Vector3.right, axis);
        side.Normalize();
        Vector3 up = Vector3.Cross(axis, side);
        // The quad leaves room for the outline, which can be wider than the line on a distant ring.
        float halfSize = radius * 1.4f + lineWidth;
        ringSeed = (ringSeed + 1) % 10000;

        int first = nextRing * 4;
        nextRing = (nextRing + 1) % RingCapacity;
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 c = Corners[corner];
            ringPositions[first + corner] = centre + (side * c.x + up * c.y) * halfSize;
            ringShapes[first + corner] = new Vector4(c.x, c.y, halfSize, radius);
            ringLives[first + corner] = new Vector4(birthTime, lifetime, strength, ringSeed);
            ringGrowth[first + corner] = new Vector4(openTime, lineWidth, 0f, 0f);
        }
        ringsDirty = true;
    }

    private void UploadRings()
    {
        ringMesh.SetVertices(ringPositions);
        ringMesh.SetUVs(0, ringShapes);
        ringMesh.SetUVs(1, ringLives);
        ringMesh.SetUVs(2, ringGrowth);
    }

    private void LateUpdate()
    {
        Shader.SetGlobalFloat(ShotTimeId, Time.time);
        var view = Camera.main;
        Vector3 focus = view != null ? view.transform.position : Vector3.zero;
        if (ringsDirty)
        {
            UploadRings();
            ringsDirty = false;
        }
        // Rings are stored in world space; the bounds just follow the camera so they are never culled.
        ringMesh.bounds = new Bounds(focus, Vector3.one * BoundsSize);
        BuildTells(view);
        inkMesh.bounds = new Bounds(focus, Vector3.one * BoundsSize);
    }

    private void BuildTells(Camera view)
    {
        for (int i = glints.Count - 1; i >= 0; i--)
        {
            var enemy = glints[i];
            if (enemy == null || !enemy.isActiveAndEnabled || !enemy.IsAiming || enemy.Muzzle == null)
                glints.RemoveAt(i);
        }
        for (int i = flashes.Count - 1; i >= 0; i--)
        {
            if (flashes[i].Muzzle == null || Time.time - flashes[i].Born > FlashDuration)
                flashes.RemoveAt(i);
        }

        inkVertices.Clear();
        inkColors.Clear();
        inkTriangles.Clear();
        if (view != null)
        {
            float pixelAtOneMetre = 2f * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad) / ReferenceScreenHeight;
            foreach (var enemy in glints) AddGlint(view.transform, enemy.Muzzle.position, enemy.AimProgress, pixelAtOneMetre);
            foreach (var flash in flashes) AddFlash(view.transform, flash.Muzzle.position, flash.Seed, pixelAtOneMetre);
        }
        inkMesh.Clear();
        inkMesh.SetVertices(inkVertices);
        inkMesh.SetColors(inkColors);
        inkMesh.SetTriangles(inkTriangles, 0);
    }

    // Sizes are in metres but never drop below a size in pixels, so a distant tell stays readable.
    private static Vector3 TowardViewer(Transform view, Vector3 point, float pixelAtOneMetre, out float pixel)
    {
        Vector3 centre = point + (view.position - point).normalized * TowardCamera;
        pixel = pixelAtOneMetre * Mathf.Max(0.01f, Vector3.Dot(centre - view.position, view.forward));
        return centre;
    }

    // Red dot on the muzzle and a red ring closing in on it; the ring collapses as the bullet leaves.
    private void AddGlint(Transform view, Vector3 muzzle, float progress, float pixelAtOneMetre)
    {
        Vector3 centre = TowardViewer(view, muzzle, pixelAtOneMetre, out float pixel);
        float dot = Mathf.Max(0.05f, 4f * pixel);
        float ring = Mathf.Lerp(Mathf.Max(0.6f, 34f * pixel), dot * 1.5f, Mathf.Clamp01(progress));
        float halfWidth = 1.25f * pixel;
        AddDisc(centre, view.right, view.up, dot, red);
        AddAnnulus(centre, view.right, view.up, ring - halfWidth, ring + halfWidth, red);
    }

    // A flat jagged star with a black edge; every shot gets its own rays.
    private void AddFlash(Transform view, Vector3 muzzle, int seed, float pixelAtOneMetre)
    {
        Vector3 centre = TowardViewer(view, muzzle, pixelAtOneMetre, out float pixel);
        float outer = Mathf.Max(0.35f, 26f * pixel);
        var random = new System.Random(seed);
        const int points = 16;
        float step = 2f * Mathf.PI / points;
        starPoints.Clear();
        for (int i = 0; i < points; i++)
        {
            float angle = i * step + ((float)random.NextDouble() - 0.5f) * step * 0.6f;
            float radius = outer * (i % 2 == 0 ? 0.65f + 0.55f * (float)random.NextDouble()
                : 0.32f + 0.12f * (float)random.NextDouble());
            starPoints.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
        AddStar(centre, view.right, view.up, 2.2f * pixel, black);
        AddStar(centre, view.right, view.up, 0f, yellow);
    }

    private void AddStar(Vector3 centre, Vector3 right, Vector3 up, float grow, Color color)
    {
        int hub = inkVertices.Count;
        AddVertex(centre, color);
        foreach (var point in starPoints)
        {
            Vector2 p = point + point.normalized * grow;
            AddVertex(centre + right * p.x + up * p.y, color);
        }
        for (int i = 0; i < starPoints.Count; i++)
            AddTriangle(hub, hub + 1 + i, hub + 1 + (i + 1) % starPoints.Count);
    }

    private void AddDisc(Vector3 centre, Vector3 right, Vector3 up, float radius, Color color)
    {
        const int segments = 20;
        int hub = inkVertices.Count;
        AddVertex(centre, color);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * 2f * Mathf.PI / segments;
            AddVertex(centre + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius, color);
        }
        for (int i = 0; i < segments; i++)
            AddTriangle(hub, hub + 1 + i, hub + 1 + (i + 1) % segments);
    }

    private void AddAnnulus(Vector3 centre, Vector3 right, Vector3 up, float inner, float outer, Color color)
    {
        const int segments = 40;
        int first = inkVertices.Count;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * 2f * Mathf.PI / segments;
            Vector3 direction = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            AddVertex(centre + direction * Mathf.Max(0f, inner), color);
            AddVertex(centre + direction * outer, color);
        }
        for (int i = 0; i < segments; i++)
        {
            int a = first + i * 2, b = first + (i + 1) % segments * 2;
            AddTriangle(a, a + 1, b + 1);
            AddTriangle(a, b + 1, b);
        }
    }

    private void AddVertex(Vector3 position, Color color)
    {
        inkVertices.Add(position);
        inkColors.Add(color);
    }

    private void AddTriangle(int a, int b, int c)
    {
        inkTriangles.Add(a);
        inkTriangles.Add(b);
        inkTriangles.Add(c);
    }
}

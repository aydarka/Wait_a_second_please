using UnityEngine;
using UnityEngine.UI;

// Full-screen colour with a jagged star-shaped hole in the middle, like Pistol Whip's hit screen.
// The hole radius is a share of the rect height; the rays are fixed per instance, so the hole
// keeps its shape while it closes.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class JaggedHole : MaskableGraphic
{
    [SerializeField, Min(3)] private int rays = 14;
    [SerializeField, Range(0.05f, 1f), Tooltip("Depth of the notches between rays, relative to the ray tips.")]
    private float notchDepth = 0.5f;
    [SerializeField, Min(0f), Tooltip("Radius of the ray tips, in rect heights.")]
    private float holeRadius = 0.5f;
    [SerializeField, Tooltip("Hole centre within the rect: 0 is the left or bottom edge, 1 the right or top.")]
    private Vector2 holeCentre = new Vector2(0.5f, 0.5f);
    [SerializeField] private int seed = 9;

    public Vector2 HoleCentre
    {
        get => holeCentre;
        set
        {
            if (holeCentre == value) return;
            holeCentre = value;
            SetVerticesDirty();
        }
    }

    public float HoleRadius
    {
        get => holeRadius;
        set
        {
            value = Mathf.Max(0f, value);
            if (Mathf.Approximately(holeRadius, value)) return;
            holeRadius = value;
            SetVerticesDirty();
        }
    }

    // Tip radius at which even the deepest notch lies past every corner, so no colour shows at all.
    public float ClearRadius
    {
        get
        {
            Rect rect = rectTransform.rect;
            float aspect = rect.height > 0f ? rect.width / rect.height : 1f;
            float farthest = 0f;
            for (int corner = 0; corner < 4; corner++)
            {
                var offset = new Vector2(((corner & 1) - holeCentre.x) * aspect, (corner >> 1) - holeCentre.y);
                farthest = Mathf.Max(farthest, offset.magnitude);
            }
            return farthest / (notchDepth * 0.8f) * 1.05f;
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 centre = rect.min + Vector2.Scale(rect.size, holeCentre);
        float tip = holeRadius * rect.height;
        // The outer edge lies past the corners from any centre and past the longest ray,
        // so the strip between it and the star covers the rect.
        float outer = Mathf.Max(rect.size.magnitude, tip * 1.3f);
        int points = Mathf.Max(3, rays) * 2;
        float step = 2f * Mathf.PI / points;
        var random = new System.Random(seed);
        for (int i = 0; i < points; i++)
        {
            // Jitter stays under half a step so neighbouring points never cross.
            float angle = i * step + ((float)random.NextDouble() - 0.5f) * step * 0.6f;
            float radius = tip * (i % 2 == 0 ? 0.7f + 0.5f * (float)random.NextDouble()
                : notchDepth * (0.8f + 0.4f * (float)random.NextDouble()));
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            vh.AddVert(centre + direction * radius, color, Vector2.zero);
            vh.AddVert(centre + direction * outer, color, Vector2.zero);
        }
        for (int i = 0; i < points; i++)
        {
            int a = i * 2, b = (i + 1) % points * 2;
            vh.AddTriangle(a, a + 1, b + 1);
            vh.AddTriangle(a, b + 1, b);
        }
    }
}

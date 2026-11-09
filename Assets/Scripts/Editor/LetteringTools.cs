using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

// Builds the lettering from the TTFs in Assets/Fonts:
// - static SDF atlases with Latin letters, digits and the punctuation the HUD uses;
// - their materials on the hatching lettering shader, inked black: outline and hard shadow, sizes in em;
// - the Lettering asset in Resources that the game loads.
// Running it again re-inks the materials and keeps the atlases; delete an atlas asset to rebuild it.
public static class LetteringTools
{
    private const string FontFolder = "Assets/Fonts";
    private const string LetteringPath = "Assets/Resources/Lettering.asset";
    private const string ShaderName = "WaitASecond/Lettering";
    private const string ScreamFont = "BigShouldersDisplay-Black";
    private const string ProtocolFont = "CourierPrime-Bold";
    private const int SamplingSize = 72;
    // A quarter em around every glyph: room in the distance field for the outline and the shadow.
    private const int Padding = 18;
    private const string Extras = "·–—‘’“”…";

    // Ink in em of the letter: the visible outline outside it and the shadow's offset down and to the right.
    private const float ScreamOutline = 0.075f;
    private const float ScreamShadow = 0.06f;
    private const float HeavyShadow = 0.09f;
    private const float ProtocolOutline = 0.1f;
    private const float ProtocolShadow = 0.05f;

    [MenuItem("Tools/Lettering/Build Font Assets")]
    public static void Build()
    {
        var scream = BuildFont(ScreamFont);
        var protocol = BuildFont(ProtocolFont);
        if (scream == null || protocol == null) return;

        Ink(scream.material, ScreamOutline, ScreamShadow);
        Ink(protocol.material, ProtocolOutline, ProtocolShadow);
        var heavy = Preset(scream, "Heavy Shadow");
        Ink(heavy, ScreamOutline, HeavyShadow);

        var lettering = AssetDatabase.LoadAssetAtPath<Lettering>(LetteringPath);
        if (lettering == null)
        {
            lettering = ScriptableObject.CreateInstance<Lettering>();
            AssetDatabase.CreateAsset(lettering, LetteringPath);
        }
        var serialized = new SerializedObject(lettering);
        serialized.FindProperty("scream").objectReferenceValue = scream;
        serialized.FindProperty("screamHeavy").objectReferenceValue = heavy;
        serialized.FindProperty("protocol").objectReferenceValue = protocol;
        serialized.FindProperty("protocolMenuFont").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Font>(FontFolder + "/" + ProtocolFont + ".ttf");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log("Lettering built: " + AssetDatabase.GetAssetPath(scream) + ", " + AssetDatabase.GetAssetPath(protocol));
    }

    [MenuItem("Tools/Lettering/Reset TAP Hint")]
    public static void ResetTapHint()
    {
        TapHint.ResetCount();
        Debug.Log("TAP hint will show again for the next two counter-shots.");
    }

    private static TMP_FontAsset BuildFont(string fontName)
    {
        string assetPath = FontFolder + "/" + fontName + " SDF.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (existing != null) return existing;

        var font = AssetDatabase.LoadAssetAtPath<Font>(FontFolder + "/" + fontName + ".ttf");
        if (font == null)
        {
            Debug.LogError("Lettering: " + fontName + ".ttf is missing from " + FontFolder + ".");
            return null;
        }
        string characters = SupportedCharacters(font);

        // The smallest atlas that holds every character.
        foreach (var size in new[] { new Vector2Int(1024, 1024), new Vector2Int(2048, 1024), new Vector2Int(2048, 2048) })
        {
            var asset = TMP_FontAsset.CreateFontAsset(font, SamplingSize, Padding, GlyphRenderMode.SDF16, size.x, size.y,
                AtlasPopulationMode.Dynamic, false);
            if (!asset.TryAddCharacters(characters, out string missing))
            {
                Debug.Log("Lettering: " + fontName + " does not fit " + size.x + "x" + size.y + " (" + missing.Length + " left).");
                Object.DestroyImmediate(asset.atlasTexture);
                Object.DestroyImmediate(asset.material);
                Object.DestroyImmediate(asset);
                continue;
            }
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            asset.name = fontName + " SDF";
            AssetDatabase.CreateAsset(asset, assetPath);
            asset.atlasTexture.name = fontName + " SDF Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
            var material = asset.material;
            material.name = fontName + " SDF Material";
            material.shader = Shader.Find(ShaderName);
            material.SetFloat("_HatchSamplingSize", SamplingSize);
            AssetDatabase.AddObjectToAsset(material, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }
        Debug.LogError("Lettering: " + fontName + " does not fit even a 2048x2048 atlas.");
        return null;
    }

    // The wanted characters the font actually has.
    private static string SupportedCharacters(Font font)
    {
        FontEngine.InitializeFontEngine();
        FontEngine.LoadFontFace(font, SamplingSize);
        var wanted = new StringBuilder();
        for (char c = ' '; c <= '~'; c++) wanted.Append(c);
        wanted.Append(Extras);
        var supported = new StringBuilder();
        foreach (char c in wanted.ToString())
            if (FontEngine.TryGetGlyphIndex(c, out uint glyph) && glyph != 0) supported.Append(c);
        return supported.ToString();
    }

    // A second material on the same atlas, kept next to the font asset as TMP expects of presets.
    private static Material Preset(TMP_FontAsset asset, string suffix)
    {
        string path = FontFolder + "/" + asset.name + " - " + suffix + ".mat";
        var preset = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (preset != null) return preset;
        preset = new Material(asset.material) { name = asset.name + " - " + suffix };
        AssetDatabase.CreateAsset(preset, path);
        return preset;
    }

    // Black ink: the face keeps its size and the outline sits fully outside it; the shadow is the outlined
    // letter moved down and to the right, hard-edged. TMP's scale ratios are off (RATIOS_OFF), so the values
    // below are exactly what the shader gets. The distance field falls from 0.5 at the letter's edge to 0 over
    // _GradientScale atlas texels, so an outline of w reaches w * 2 * _GradientScale texels out, while a shadow
    // offset of o moves it o * _GradientScale texels. An em is SamplingSize texels.
    private static void Ink(Material material, float outlineEm, float shadowEm)
    {
        material.shader = Shader.Find(ShaderName);
        material.SetFloat("_HatchSamplingSize", SamplingSize);
        material.EnableKeyword("OUTLINE_ON");
        material.EnableKeyword("UNDERLAY_ON");
        material.EnableKeyword(ShaderUtilities.Keyword_Ratios);
        material.SetColor("_FaceColor", Color.white);
        material.SetColor("_OutlineColor", Color.black);
        material.SetColor("_UnderlayColor", Color.black);
        material.SetFloat("_OutlineSoftness", 0f);
        material.SetFloat("_UnderlaySoftness", 0f);
        float gradient = material.GetFloat("_GradientScale");
        float width = outlineEm * SamplingSize / (2f * gradient);
        material.SetFloat("_OutlineWidth", width);
        material.SetFloat("_FaceDilate", width);
        material.SetFloat("_UnderlayDilate", width);
        float offset = shadowEm * SamplingSize / gradient;
        material.SetFloat("_UnderlayOffsetX", offset);
        material.SetFloat("_UnderlayOffsetY", -offset);
        ShaderUtilities.UpdateShaderRatios(material);
        EditorUtility.SetDirty(material);
    }
}

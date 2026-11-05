using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// High-contrast black-and-white grading that deepens while time is slowed.
// World materials stay neutral so the yellow and red accents are the only colour on screen.
[DisallowMultipleComponent]
public sealed class NoirPostEffects : MonoBehaviour
{
    [SerializeField, Tooltip("Finds the first Volume in the scene when empty.")] private Volume volume;
    [SerializeField] private float contrast = 45f;
    [SerializeField, Range(0f, 1f)] private float filmGrain = 0.45f;
    [SerializeField, Range(0f, 1f)] private float normalVignette = 0.25f;
    [SerializeField, Range(0f, 1f)] private float slowVignette = 0.45f;
    [SerializeField, Range(-1f, 0f)] private float slowLensDistortion = -0.15f;

    // No chromatic aberration: its blue and orange fringes break the black-white-yellow-red palette.
    private Vignette vignette;
    private LensDistortion lensDistortion;

    private void Awake()
    {
        if (volume == null) volume = FindAnyObjectByType<Volume>();
        if (volume == null)
        {
            Debug.LogError("NoirPostEffects requires a Volume in the scene.", this);
            enabled = false;
            return;
        }

        // volume.profile clones the shared asset, so play mode never edits the saved profile.
        VolumeProfile profile = volume.profile;
        var adjustments = GetOrAdd<ColorAdjustments>(profile);
        adjustments.contrast.Override(contrast);
        var grain = GetOrAdd<FilmGrain>(profile);
        grain.type.Override(FilmGrainLookup.Medium3);
        grain.intensity.Override(filmGrain);
        grain.response.Override(0.8f);
        vignette = GetOrAdd<Vignette>(profile);
        vignette.color.Override(Color.black);
        lensDistortion = GetOrAdd<LensDistortion>(profile);
        Apply(TimeScaleController.Flow);
    }

    private void LateUpdate()
    {
        Apply(TimeScaleController.Flow);
    }

    private void Apply(float flow)
    {
        float slow = 1f - Mathf.Clamp01(flow);
        vignette.intensity.Override(Mathf.Lerp(normalVignette, slowVignette, slow));
        lensDistortion.intensity.Override(slowLensDistortion * slow);
    }

    private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component))
            component = profile.Add<T>();
        component.active = true;
        return component;
    }
}

using UnityEditor;
using UnityEngine;

// Import settings for every clip under Assets/Audio, applied once when a clip is first imported (later manual
// changes in the inspector are kept). Tuned for budget Android phones as the lower bound:
// short sounds are ADPCM decompressed on load (almost no CPU when played), loops and music are Vorbis kept
// compressed in memory (small, and several layers stay in sync, unlike streaming). Sound effects are mono, as
// 3D panning needs; files under a Music folder keep their channels.
public sealed class SoundImportRules : AssetPostprocessor
{
    private const string Root = "Assets/Audio/";

    private void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(Root) || !assetImporter.importSettingsMissing) return;
        var importer = (AudioImporter)assetImporter;
        bool music = assetPath.Contains("/Music/") || assetPath.EndsWith("music_loop.wav");
        bool loop = music || assetPath.Contains("_loop.");

        var settings = importer.defaultSampleSettings;
        settings.preloadAudioData = true;
        if (loop)
        {
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            settings.quality = music ? 0.5f : 0.6f;
        }
        else
        {
            settings.compressionFormat = AudioCompressionFormat.ADPCM;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
        }
        importer.defaultSampleSettings = settings;
        importer.forceToMono = !assetPath.Contains("/Music/");
        importer.loadInBackground = false;
    }
}

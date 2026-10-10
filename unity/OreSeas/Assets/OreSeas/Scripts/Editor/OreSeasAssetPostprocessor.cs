using UnityEditor;
using UnityEngine;

namespace OreSeas.EditorTools
{
    /// <summary>Pixel-art import rules: atlases are point-filtered, uncompressed, no mips (sliced at runtime by Atlas.cs).</summary>
    public sealed class OreSeasAssetPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/OreSeas/")) return;
            var ti = (TextureImporter)assetImporter;
            bool atlas = assetPath.Contains("/Resources/Atlases/");
            ti.textureType = atlas ? TextureImporterType.Default : TextureImporterType.Sprite;
            if (!atlas) { ti.spritePixelsPerUnit = 1; ti.spriteImportMode = SpriteImportMode.Single; }
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 2048;
            ti.sRGBTexture = true;
            ti.isReadable = false;
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').Contains("/OreSeas/")) return;
            var ai = (AudioImporter)assetImporter;
            bool music = assetPath.Contains("/music_");
            var s = ai.defaultSampleSettings;
            s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
            s.quality = 0.6f;
            ai.defaultSampleSettings = s;
            ai.forceToMono = !music;
            ai.loadInBackground = music;
        }
    }
}

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ShaftAttack.EditorTools
{
    /// <summary>
    /// Import settings for everything in ShaftAttack/Textures. The rock textures tile across the whole
    /// world through triplanar mapping, so they need Repeat wrapping, full mipmaps, trilinear filtering,
    /// and some anisotropy - walls are often seen at a glancing angle, where plain filtering smears them.
    /// </summary>
    public class RockTextureImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("Assets/ShaftAttack/Textures/")) return;

            TextureImporter ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 8;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
#endif

using UnityEditor;

/// <summary>
/// Import settings for the combat VFX textures (Assets/Resources/VFX/Tex): their channels are data (R edge band, G dissolve
/// noise, A shape), so they are read linear, not as colour; uncompressed so thin lines and the noise survive; mipmapped
/// for the zoomed-out camera. Ones named "*_tile" repeat along their length (strokes on lines and trails).
/// </summary>
public class VfxTextureImport : AssetPostprocessor
{
    public const string Folder = "Assets/Resources/VFX/Tex/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var t = (TextureImporter)assetImporter;
        t.textureType = TextureImporterType.Default;
        t.sRGBTexture = false;
        t.alphaSource = TextureImporterAlphaSource.FromInput;
        t.alphaIsTransparency = false;
        t.mipmapEnabled = true;
        t.textureCompression = TextureImporterCompression.Uncompressed;
        t.wrapMode = assetPath.Contains("_tile") ? UnityEngine.TextureWrapMode.Repeat : UnityEngine.TextureWrapMode.Clamp;
        t.filterMode = UnityEngine.FilterMode.Bilinear;
        t.maxTextureSize = 512;
    }
}

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Keeps hand-painted runtime sprites compact and consistently imported.</summary>
public sealed class YekVajabJaArtAssetPostprocessor : AssetPostprocessor
{
    [MenuItem("Tools/Yek Vajab Ja/Configure Art Sprite Imports")]
    private static void ConfigureExistingArtSprites()
    {
        string[] roots = { "Assets/Resources/Cars", "Assets/Resources/Props" };
        string[] assets = AssetDatabase.FindAssets("t:Texture2D", roots);
        foreach (string guid in assets)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;
            ApplySpriteSettings(importer, path);
            importer.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Configured {assets.Length} Yek Vajab Ja art sprites.");
    }

    private void OnPreprocessTexture()
    {
        string normalizedPath = assetPath.Replace('\\', '/');
        bool carSprite = normalizedPath.StartsWith("Assets/Resources/Cars/");
        bool propSprite = normalizedPath.StartsWith("Assets/Resources/Props/");
        if (!carSprite && !propSprite) return;

        ApplySpriteSettings((TextureImporter)assetImporter, normalizedPath);
    }

    private static void ApplySpriteSettings(TextureImporter importer, string path)
    {
        bool carSprite = path.StartsWith("Assets/Resources/Cars/");
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = carSprite ? 512 : 256;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.compressionQuality = 85;
    }
}
#endif

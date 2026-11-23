using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates a selectable, editable board of the game's car and roadside sprites.</summary>
internal static class YVArtEditPreviewBuilder
{
    private const string RootName = "ART EDIT PREVIEW - edit sprites below; hidden during Play";
    private static readonly string[] CarNames = { "Pride", "SamandTaxi", "NissanPickup", "Peugeot206", "Peugeot405" };
    private static readonly string[] PropNames = { "Tree", "YellowFlowers", "Stones", "Hydrant", "Planter", "RedFlowers", "Tile", "Drain" };
    private const float ReferenceSize = 64f;

    [MenuItem("Tools/Yek Vajab Ja/Create Art Edit Preview")]
    private static void CreatePreview()
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Create art edit preview");
        root.AddComponent<ArtEditPreviewGuard>();
        CreateScaleReference(root.transform);
        GameObject cars = CreateGroup(root.transform, "Cars - drag, rotate, or scale each sprite");
        GameObject props = CreateGroup(root.transform, "Roadside props - drag, rotate, or scale each sprite");

        for (int i = 0; i < CarNames.Length; i++)
        {
            Sprite sprite = Resources.Load<Sprite>("Cars/" + CarNames[i]);
            if (sprite == null) continue;
            GameObject item = CreateSprite(cars.transform, CarNames[i], sprite, new Vector3((i - 2) * 88f, 76f, 0f), 64f);
            item.name = CarNames[i] + " - drag/scale this object";
        }

        for (int i = 0; i < PropNames.Length; i++)
        {
            Sprite sprite = Resources.Load<Sprite>("Props/" + PropNames[i]);
            if (sprite == null) continue;
            int row = i / 4;
            int col = i % 4;
            Vector3 position = new Vector3((col - 1.5f) * 62f, -15f - row * 62f, 0f);
            GameObject item = CreateSprite(props.transform, PropNames[i], sprite, position, 36f);
            item.name = PropNames[i] + " - drag/scale this object";
        }

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>();
        if (renderers.Length > 0)
        {
            Bounds boardBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) boardBounds.Encapsulate(renderers[i].bounds);
            boardBounds.Encapsulate(new Bounds(new Vector3(280f, 76f, 0f), new Vector3(ReferenceSize, ReferenceSize, 0f)));
            boardBounds.Expand(24f);
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                float aspect = Mathf.Max(sceneView.camera != null ? sceneView.camera.aspect : 1f, 0.1f);
                float viewHeight = Mathf.Max(boardBounds.size.y, boardBounds.size.x / aspect);
                sceneView.LookAt(boardBounds.center, Quaternion.identity, Mathf.Max(18f, viewHeight * 0.56f));
            }
        }
        Debug.Log("Art Edit Preview created. Expand the hierarchy and select individual sprites to edit. The preview disables itself during Play.");
    }

    [MenuItem("Tools/Yek Vajab Ja/Add 64x64 Scale Reference")]
    private static void AddScaleReferenceToCurrentPreview()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.LogWarning("Create the Art Edit Preview first, then add its scale reference.");
            return;
        }

        Transform oldReference = root.transform.Find("SCALE REFERENCE - 64x64 units");
        if (oldReference != null && oldReference.childCount == 0)
        {
            SpriteRenderer existingSprite = oldReference.GetComponent<SpriteRenderer>();
            if (existingSprite != null && existingSprite.sprite != null &&
                existingSprite.sprite.rect.width == ReferenceSize && existingSprite.sprite.rect.height == ReferenceSize) return;
        }
        if (oldReference != null) Undo.DestroyObjectImmediate(oldReference.gameObject);
        CreateScaleReference(root.transform);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Added a filled 64x64 unit square Sprite as the scale reference.");
    }

    internal static void EnsureScaleReference()
    {
        AddScaleReferenceToCurrentPreview();
    }

    private static void CreateScaleReference(Transform parent)
    {
        GameObject reference = new GameObject("SQUARE SPRITE REFERENCE - 64x64 units");
        Undo.RegisterCreatedObjectUndo(reference, "Add scale reference");
        reference.transform.SetParent(parent, false);
        reference.transform.localPosition = new Vector3(280f, 76f, 0f);
        SpriteRenderer spriteRenderer = reference.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = GetScaleReferencePixel();
        spriteRenderer.color = new Color(0.2f, 0.9f, 1f, 0.35f);
        spriteRenderer.sortingOrder = 20;
    }

    private static Sprite GetScaleReferencePixel()
    {
        const string texturePath = "Assets/Editor/ScaleReferencePixel.png";
        Texture2D currentTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (!File.Exists(texturePath) || currentTexture == null || currentTexture.width != 64 || currentTexture.height != 64)
        {
            Texture2D texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[64 * 64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
        }

        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer != null && (importer.textureType != TextureImporterType.Sprite || importer.spritePixelsPerUnit != 1f))
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 1f;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
    }

    private static GameObject CreateGroup(Transform parent, string name)
    {
        GameObject group = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(group, "Create art preview group");
        group.transform.SetParent(parent, false);
        return group;
    }

    private static GameObject CreateSprite(Transform parent, string name, Sprite sprite, Vector3 position, float longestEdge)
    {
        GameObject item = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(item, "Create art preview sprite");
        item.transform.SetParent(parent, false);
        item.transform.localPosition = position;
        float fit = longestEdge / Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y, 0.01f);
        item.transform.localScale = Vector3.one * fit;
        SpriteRenderer renderer = item.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 1;
        return item;
    }
}

[InitializeOnLoad]
internal static class YVScaleReferenceInstaller
{
    static YVScaleReferenceInstaller()
    {
        EditorApplication.delayCall += InstallInOpenPreview;
    }

    private static void InstallInOpenPreview()
    {
        GameObject root = GameObject.Find("ART EDIT PREVIEW - edit sprites below; hidden during Play");
        if (root == null) return;
        YVArtEditPreviewBuilder.EnsureScaleReference();
        Transform reference = root.transform.Find("SQUARE SPRITE REFERENCE - 64x64 units");
        if (reference != null)
        {
            Selection.activeGameObject = reference.gameObject;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
        }
        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.IsValid() && scene.isDirty) EditorSceneManager.SaveScene(scene);
    }
}

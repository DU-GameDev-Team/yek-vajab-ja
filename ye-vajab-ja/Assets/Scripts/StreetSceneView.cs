using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>Builds the street and its seed-driven, lightweight roadside scenery.</summary>
[DisallowMultipleComponent]
public sealed class StreetSceneView : MonoBehaviour
{
    private const float LogicalWidth = 360f;
    private const float LogicalHeight = 640f;
    private const float DesignAspect = LogicalWidth / LogicalHeight;
    private const float CameraZoom = 1.12f;
    // Match the hand-adjusted Art Edit Preview sprite bounds in Game.unity (width, height in world units).
    // These differ per vehicle; normalizing every car to 64 made runtime cars larger than the approved references.
    private const int PropSlotCount = 10;
    private const int PropTypeCount = 8;
    private static readonly string[] PropResourceNames = { "Tree", "YellowFlowers", "Stones", "Hydrant", "Planter", "RedFlowers", "Tile", "Drain" };
    private static readonly string[] ParkedCarResourceNames = { "SamandTaxi", "Peugeot206", "Peugeot405", "Pride" };

    [Tooltip("Optional player-car sprite. Empty uses Resources/Cars/NissanPickup.")]
    [SerializeField] private Sprite playerCarSprite;
    [Tooltip("Optional parked-car overrides in order: Samand taxi, Peugeot 206, white Peugeot 405, turquoise Pride. Empty entries use their Resources/Cars sprites.")]
    [SerializeField] private Sprite[] parkedCarSprites = new Sprite[4];
    [Tooltip("Optional sprites by type: tree, yellow flowers, rocks, hydrant, grass planter, red flowers, pavement tile, drain. Empty entries use matching sprites in Resources/Props.")]
    [SerializeField] private Sprite[] replacementPropSprites = new Sprite[PropTypeCount];

    private Texture2D _pixelTexture;
    private Sprite _pixelSprite;
    private int _lastWidth;
    private int _lastHeight;
    private SpriteRenderer _road;
    private SpriteRenderer _leftParkingLane;
    private SpriteRenderer _rightParkingLane;
    private SpriteRenderer _leftSidewalk;
    private SpriteRenderer _rightSidewalk;
    private SpriteRenderer _leftCurb;
    private SpriteRenderer _rightCurb;
    private readonly List<SpriteRenderer> _curbMarkers = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> _laneDashes = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> _sidewalkSeams = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> _roadSpeckles = new List<SpriteRenderer>();
    private readonly List<GameObject> _layoutProps = new List<GameObject>();
    private GameObject _upperParkedCar;
    private GameObject _lowerParkedCar;
    private GameObject _playerCar;
    private int _environment;
    private int _lastFamiliarCarIndex = -1;
    private int _lastAlternateCarIndex = -1;

    private struct PropChoice
    {
        public int type;
        public int side;
        public int row;
        public int xOffset;
        public int yOffset;
        public int scaleStep;
        public int palette;
        public int flip;
    }

    private void Awake()
    {
        ConfigureCamera();
        BuildStreet();
    }

    private void LateUpdate()
    {
        if (_lastWidth != Screen.width || _lastHeight != Screen.height)
            ConfigureCamera();
    }

    private void OnDestroy()
    {
        if (_pixelSprite != null) Destroy(_pixelSprite);
        if (_pixelTexture != null) Destroy(_pixelTexture);
    }

    private void ConfigureCamera()
    {
        Camera gameCamera = GetComponent<Camera>();
        if (gameCamera == null) return;

        float screenAspect = Screen.height > 0 ? (float)Screen.width / Screen.height : DesignAspect;
        gameCamera.orthographic = true;
        gameCamera.orthographicSize = (screenAspect < DesignAspect
            ? LogicalWidth / (2f * Mathf.Max(screenAspect, 0.01f))
            : LogicalHeight * 0.5f) / CameraZoom;
        gameCamera.rect = new Rect(0f, 0f, 1f, 1f);
        gameCamera.transform.position = new Vector3(LogicalWidth * 0.5f, LogicalHeight * 0.5f, -10f);
        gameCamera.backgroundColor = Hex("202724");
        _lastWidth = Screen.width;
        _lastHeight = Screen.height;
    }

    private void BuildStreet()
    {
        _pixelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "Yek Vajab Ja Solid Pixel",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        _pixelTexture.SetPixel(0, 0, Color.white);
        _pixelTexture.Apply(false, true);
        _pixelSprite = Sprite.Create(_pixelTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        _pixelSprite.name = "Yek Vajab Ja Solid Sprite";
        _pixelSprite.hideFlags = HideFlags.DontSave;

        _road = CreateRect("Road", new Vector2(180f, 320f), 360f, 640f, Hex("343b38"), 0);
        _leftParkingLane = CreateRect("Left parking lane tint", new Vector2(96f, 320f), 52f, 640f, Hex("303734"), 1);
        _rightParkingLane = CreateRect("Right parking lane tint", new Vector2(264f, 320f), 52f, 640f, Hex("303734"), 1);
        _leftSidewalk = CreateRect("Left sidewalk", new Vector2(30f, 320f), 60f, 640f, Hex("d8ccb0"), 2);
        _rightSidewalk = CreateRect("Right sidewalk", new Vector2(330f, 320f), 60f, 640f, Hex("d8ccb0"), 2);
        BuildSurfaceDetails();
        _leftCurb = CreateRect("Left curb base", new Vector2(62f, 320f), 8f, 640f, Hex("eee4cf"), 3);
        _rightCurb = CreateRect("Right curb base", new Vector2(298f, 320f), 8f, 640f, Hex("eee4cf"), 3);

        for (int y = 8; y < 640; y += 48)
        {
            _curbMarkers.Add(CreateRect("Left curb amber marker", new Vector2(62f, y + 12f), 8f, 24f, Hex("d89a47"), 4));
            _curbMarkers.Add(CreateRect("Right curb amber marker", new Vector2(298f, y + 12f), 8f, 24f, Hex("d89a47"), 4));
        }
        for (int y = 18; y < 640; y += 74)
        {
            _laneDashes.Add(CreateRect("Left lane dash", new Vector2(130f, y + 20f), 2f, 40f, Hex("b9beb4"), 2));
            _laneDashes.Add(CreateRect("Right lane dash", new Vector2(230f, y + 20f), 2f, 40f, Hex("b9beb4"), 2));
        }

        SetEnvironment(0, 0);

        const float centerC = 290f;
        const float freeGap = 120f;
        const float halfCenterDistance = (freeGap + 60f) * 0.5f;
        float upperCarUnityY = LogicalHeight - (centerC - halfCenterDistance);
        float lowerCarUnityY = LogicalHeight - (centerC + halfCenterDistance);
        _upperParkedCar = CreateCar("Parked car upper", new Vector2(262f, upperCarUnityY), Hex("718491"), 10, "SamandTaxi");
        _lowerParkedCar = CreateCar("Parked car lower", new Vector2(262f, lowerCarUnityY), Hex("687d89"), 10, "Peugeot405");
        _playerCar = CreateCar("Player car", new Vector2(198f, LogicalHeight - centerC), Hex("efc34d"), 20, "NissanPickup");
        CreateRect("Parking gap top marker", new Vector2(285f, LogicalHeight - (centerC - freeGap * 0.5f)), 18f, 2f, Hex("f1dfb9"), 5);
        CreateRect("Parking gap bottom marker", new Vector2(285f, LogicalHeight - (centerC + freeGap * 0.5f)), 18f, 2f, Hex("f1dfb9"), 5);
    }

    public void SetEnvironment(int variant) => SetEnvironment(variant, 0);

    public void SetEnvironment(int variant, int seed)
    {
        int theme = ((variant % 3) + 3) % 3;
        bool sunset = theme == 1;
        bool night = theme == 2;
        _environment = theme;
        Camera gameCamera = GetComponent<Camera>();
        if (gameCamera != null)
            gameCamera.backgroundColor = night ? Hex("111827") : (sunset ? Hex("3b2c3b") : Hex("202724"));

        SetColor(_road, night ? "202a3a" : (sunset ? "63474c" : "59635e"));
        SetColor(_leftParkingLane, night ? "252f3f" : (sunset ? "574148" : "4b5550"));
        SetColor(_rightParkingLane, night ? "252f3f" : (sunset ? "574148" : "4b5550"));
        SetColor(_leftSidewalk, night ? "465166" : (sunset ? "c98568" : "d8ccb0"));
        SetColor(_rightSidewalk, night ? "465166" : (sunset ? "c98568" : "d8ccb0"));
        SetColor(_leftCurb, night ? "aeb9ca" : (sunset ? "f3bb8c" : "eee4cf"));
        SetColor(_rightCurb, night ? "aeb9ca" : (sunset ? "f3bb8c" : "eee4cf"));
        SetColors(_curbMarkers, night ? "f0ad48" : (sunset ? "df6548" : "d89a47"));
        SetColors(_laneDashes, night ? "9eacc0" : (sunset ? "f5d2b4" : "b9beb4"));
        Color seamColor = night ? Hex("758197") : (sunset ? Hex("a96558") : Hex("bcae90"));
        seamColor.a = 0.44f;
        SetColors(_sidewalkSeams, seamColor);
        Color speckleColor = night ? Hex("a9b2c0") : (sunset ? Hex("e2b7a0") : Hex("c5c8b9"));
        speckleColor.a = 0.12f;
        SetColors(_roadSpeckles, speckleColor);
        ApplyVehicleSprites(seed);
        BuildLayoutProps(seed);
    }

    private void BuildSurfaceDetails()
    {
        for (int y = 20; y < 640; y += 36)
        {
            _sidewalkSeams.Add(CreateRect("Sidewalk tile seam", new Vector2(30f, y), 54f, 0.7f, new Color(0.74f, 0.68f, 0.57f, 0.44f), 3));
            _sidewalkSeams.Add(CreateRect("Sidewalk tile seam", new Vector2(330f, y + 18f), 54f, 0.7f, new Color(0.74f, 0.68f, 0.57f, 0.44f), 3));
        }
        for (int y = 24; y < 640; y += 72)
        {
            _sidewalkSeams.Add(CreateRect("Sidewalk tile joint", new Vector2(17f, y), 0.7f, 24f, new Color(0.74f, 0.68f, 0.57f, 0.35f), 3));
            _sidewalkSeams.Add(CreateRect("Sidewalk tile joint", new Vector2(343f, y + 36f), 0.7f, 24f, new Color(0.74f, 0.68f, 0.57f, 0.35f), 3));
        }

        System.Random random = new System.Random(0x4A11CE);
        for (int i = 0; i < 46; i++)
        {
            float x = random.Next(74, 286);
            float y = random.Next(0, 640);
            float width = random.Next(1, 4);
            float height = random.Next(1, 3);
            _roadSpeckles.Add(CreateRect("Soft asphalt grain", new Vector2(x, y), width, height, new Color(0.78f, 0.80f, 0.73f, 0.12f), 2));
        }
    }

    private void ApplyVehicleSprites(int seed)
    {
        if (_playerCar == null || _upperParkedCar == null || _lowerParkedCar == null) return;

        System.Random random = new System.Random(seed ^ 0x2C91D7);
        int familiarCarIndex = random.Next(0, 2);
        int alternateCarIndex = random.Next(2, 4);
        if (familiarCarIndex == _lastFamiliarCarIndex && alternateCarIndex == _lastAlternateCarIndex)
            alternateCarIndex = 2 + ((alternateCarIndex - 2 + 1) % 2);
        _lastFamiliarCarIndex = familiarCarIndex;
        _lastAlternateCarIndex = alternateCarIndex;

        string familiarCar = ParkedCarResourceNames[familiarCarIndex];
        string alternateCar = ParkedCarResourceNames[alternateCarIndex];
        if (random.Next(0, 2) == 0)
        {
            string swap = familiarCar;
            familiarCar = alternateCar;
            alternateCar = swap;
        }

        SetCarSprite(_playerCar, "NissanPickup");
        SetCarSprite(_upperParkedCar, familiarCar);
        SetCarSprite(_lowerParkedCar, alternateCar);
    }

    private void SetCarSprite(GameObject car, string resourceName)
    {
        if (car == null) return;
        Transform body = car.transform.Find("Body");
        Sprite sprite = LoadCarSprite(resourceName);
        if (body == null || sprite == null) return;

        SpriteRenderer renderer = body.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = body.gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = Color.white;
        body.localPosition = Vector3.zero;
        Vector2 previewSize = car.name == "Player car"
            ? GetPlayerCarSize(resourceName)
            : GetPreviewCarSize(resourceName);
        body.localScale = new Vector3(
            previewSize.x / Mathf.Max(sprite.bounds.size.x, 0.01f),
            previewSize.y / Mathf.Max(sprite.bounds.size.y, 0.01f),
            1f);
        renderer.sortingOrder = car.name == "Player car" ? 21 : 11;

        for (int i = car.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = car.transform.GetChild(i);
            if (child != body) Destroy(child.gameObject);
        }
    }

    private Sprite LoadCarSprite(string resourceName)
    {
        if (resourceName == "NissanPickup" && playerCarSprite != null) return playerCarSprite;
        int parkedIndex = Array.IndexOf(ParkedCarResourceNames, resourceName);
        if (parkedIndex >= 0 && parkedCarSprites != null && parkedIndex < parkedCarSprites.Length && parkedCarSprites[parkedIndex] != null)
            return parkedCarSprites[parkedIndex];
        return Resources.Load<Sprite>("Cars/" + resourceName);
    }

    public static bool HasSameDecorationLayout(int seedA, int seedB)
    {
        PropChoice[] a = MakeLayout(seedA);
        PropChoice[] b = MakeLayout(seedB);
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].type != b[i].type || a[i].side != b[i].side || a[i].row != b[i].row ||
                a[i].xOffset != b[i].xOffset || a[i].yOffset != b[i].yOffset ||
                a[i].scaleStep != b[i].scaleStep || a[i].palette != b[i].palette || a[i].flip != b[i].flip)
                return false;
        }
        return true;
    }

    private void BuildLayoutProps(int seed)
    {
        foreach (GameObject prop in _layoutProps)
            if (prop != null) Destroy(prop);
        _layoutProps.Clear();

        PropChoice[] choices = MakeLayout(seed);
        for (int i = 0; i < choices.Length; i++)
        {
            PropChoice choice = choices[i];
            // Keep the complete 36-unit sprite inside the zoomed camera view.
            float x = choice.side == 0 ? 42f : 318f;
            float y = 64f + choice.row * 122f + choice.yOffset;
            x += choice.side == 0 ? choice.xOffset : -choice.xOffset;
            // Match the art edit preview: every roadside sprite uses a 36-unit longest edge.
            const float size = 1f;
            GameObject root = new GameObject("Roadside prop " + (i + 1));
            root.transform.position = new Vector3(x, y, 0f);
            _layoutProps.Add(root);

            Sprite propSprite = GetPropSprite(choice.type);
            if (propSprite != null)
            {
                SpriteRenderer replacement = root.AddComponent<SpriteRenderer>();
                replacement.sprite = propSprite;
                replacement.sortingOrder = 4;
                Vector2 bounds = replacement.sprite.bounds.size;
                // Normalize by the visible sprite's long edge so every asset shares one world-space footprint.
                const float normalizedPropFootprint = 36f;
                float fit = normalizedPropFootprint / Mathf.Max(bounds.x, bounds.y, 0.01f) * size;
                root.transform.localScale = new Vector3(fit, fit, 1f);
                replacement.flipX = choice.flip != 0;
                replacement.color = EnvironmentSpriteTint();
            }
            else
            {
                BuildProceduralProp(root.transform, choice, size);
            }
        }
    }

    private Sprite GetPropSprite(int type)
    {
        if (replacementPropSprites != null && type < replacementPropSprites.Length && replacementPropSprites[type] != null)
            return replacementPropSprites[type];
        return type >= 0 && type < PropResourceNames.Length
            ? Resources.Load<Sprite>("Props/" + PropResourceNames[type])
            : null;
    }

    private Color EnvironmentSpriteTint()
    {
        if (_environment == 2) return new Color(0.78f, 0.82f, 0.92f, 1f);
        if (_environment == 1) return new Color(1f, 0.90f, 0.82f, 1f);
        return Color.white;
    }

    private static PropChoice[] MakeLayout(int seed)
    {
        System.Random random = new System.Random(seed ^ 0x51A7C3);
        PropChoice[] choices = new PropChoice[PropSlotCount];
        int[] typePool = new int[PropTypeCount];
        for (int i = 0; i < typePool.Length; i++) typePool[i] = i;
        for (int i = typePool.Length - 1; i > 0; i--)
        {
            int swap = random.Next(i + 1);
            int value = typePool[i];
            typePool[i] = typePool[swap];
            typePool[swap] = value;
        }

        // Use 3-5 distinct prop types per layout and avoid the same type in adjacent rows.
        int selectedTypeCount = random.Next(3, 6);
        int[] rowTypes = new int[PropSlotCount / 2];
        int lastType = -1;
        for (int row = 0; row < rowTypes.Length; row++)
        {
            int candidate;
            do
            {
                candidate = typePool[random.Next(selectedTypeCount)];
            }
            while (candidate == lastType);
            rowTypes[row] = candidate;
            lastType = candidate;
        }

        for (int row = 0; row < rowTypes.Length; row++)
        {
            int palette = random.Next(0, 3);
            int scaleStep = random.Next(0, 4);
            int xOffset = random.Next(-4, 5);
            int yOffset = random.Next(-8, 9);
            for (int side = 0; side < 2; side++)
            {
                choices[row * 2 + side] = new PropChoice
                {
                    type = rowTypes[row],
                    side = side,
                    row = row,
                    xOffset = xOffset,
                    yOffset = yOffset,
                    scaleStep = scaleStep,
                    palette = palette,
                    flip = side
                };
            }
        }
        return choices;
    }

    private void BuildProceduralProp(Transform root, PropChoice choice, float scale)
    {
        Color main = PaletteColor(choice.type, choice.palette);
        Color shade = Color.Lerp(main, Color.black, 0.24f);
        int order = 4;
        switch (choice.type)
        {
            case 0: // leafy tree, kept small on the sidewalk
                CreateRect("Tree shadow", new Vector2(2f, -5f), 17f * scale, 6f * scale, new Color(0f, 0f, 0f, 0.16f), order, root);
                CreateRect("Tree trunk", new Vector2(0f, -2f), 5f * scale, 13f * scale, Hex("79573e"), order + 1, root);
                CreateRect("Tree canopy", new Vector2(0f, 6f), 21f * scale, 18f * scale, main, order + 2, root);
                CreateRect("Tree canopy highlight", new Vector2(-4f, 9f), 7f * scale, 6f * scale, Color.Lerp(main, Color.white, 0.2f), order + 3, root);
                break;
            case 1: // yellow flower bed
                CreateRect("Flower shrub base", Vector2.zero, 18f * scale, 10f * scale, shade, order, root);
                CreateRect("Flower shrub crown", new Vector2(0f, 4f), 19f * scale, 11f * scale, main, order + 1, root);
                CreateRect("Yellow flower", new Vector2(-5f, 8f), 4f * scale, 4f * scale, Hex("ffd84d"), order + 2, root);
                CreateRect("Yellow flower", new Vector2(5f, 7f), 4f * scale, 4f * scale, Hex("ffe66b"), order + 2, root);
                break;
            case 2: // stones
                CreateRect("Stone one", new Vector2(-5f, 0f), 9f * scale, 7f * scale, main, order, root);
                CreateRect("Stone two", new Vector2(3f, 2f), 8f * scale, 9f * scale, shade, order + 1, root);
                CreateRect("Stone glint", new Vector2(2f, 5f), 3f * scale, 2f * scale, Color.Lerp(main, Color.white, 0.45f), order + 2, root);
                break;
            case 3: // small fire hydrant
                CreateRect("Hydrant foot", new Vector2(0f, -4f), 12f * scale, 4f * scale, shade, order, root);
                CreateRect("Hydrant body", new Vector2(0f, 1f), 8f * scale, 12f * scale, main, order + 1, root);
                CreateRect("Hydrant cap", new Vector2(0f, 8f), 12f * scale, 4f * scale, Color.Lerp(main, Color.white, 0.15f), order + 2, root);
                CreateRect("Hydrant nozzle", new Vector2(-6f, 2f), 5f * scale, 4f * scale, main, order + 1, root);
                break;
            case 4: // planter and grass/flowers
                CreateRect("Planter", new Vector2(0f, -2f), 16f * scale, 7f * scale, Hex("b67a54"), order, root);
                CreateRect("Planter greenery", new Vector2(0f, 4f), 14f * scale, 9f * scale, main, order + 1, root);
                break;
            case 5: // red flower bed
                CreateRect("Flower bed greenery", Vector2.zero, 18f * scale, 9f * scale, shade, order, root);
                CreateRect("Flower stem", new Vector2(-4f, 4f), 2f * scale, 9f * scale, main, order + 1, root);
                CreateRect("Flower stem", new Vector2(4f, 4f), 2f * scale, 9f * scale, main, order + 1, root);
                CreateRect("Red flower", new Vector2(-4f, 9f), 5f * scale, 5f * scale, Hex("d94f52"), order + 2, root);
                CreateRect("Red flower", new Vector2(4f, 9f), 5f * scale, 5f * scale, Hex("ed6570"), order + 2, root);
                break;
            default: // sidewalk crack / paving mark, not a road hazard
                CreateRect("Pavement seam", Vector2.zero, 16f * scale, 3f * scale, main, order, root);
                CreateRect("Pavement branch", new Vector2(-4f, -3f), 3f * scale, 6f * scale, shade, order + 1, root);
                break;
        }
        if (choice.flip != 0) root.localScale = new Vector3(-1f, 1f, 1f);
    }

    private Color PaletteColor(int type, int palette)
    {
        if (_environment == 2)
        {
            if (type == 0 || type == 1 || type == 4 || type == 5) return Hex(palette == 0 ? "789879" : (palette == 1 ? "8c9c77" : "607e7d"));
            if (type == 3) return Hex(palette == 1 ? "df765f" : "dc695c");
            return Hex(palette == 0 ? "8e9bad" : "a0a8b4");
        }
        if (_environment == 1)
        {
            if (type == 0 || type == 1 || type == 4 || type == 5) return Hex(palette == 0 ? "a56850" : (palette == 1 ? "b67855" : "8f7051"));
            if (type == 3) return Hex(palette == 1 ? "e0734e" : "cf594a");
            return Hex(palette == 0 ? "a9957b" : "98816e");
        }
        if (type == 0 || type == 1 || type == 4 || type == 5) return Hex(palette == 0 ? "60865d" : (palette == 1 ? "72965f" : "537b70"));
        if (type == 3) return Hex(palette == 1 ? "e35d4e" : "cf4d47");
        return Hex(palette == 0 ? "98988b" : "898f91");
    }

    private static void SetColor(SpriteRenderer renderer, string htmlColor)
    {
        if (renderer != null) renderer.color = Hex(htmlColor);
    }

    private static void SetColors(List<SpriteRenderer> renderers, string htmlColor)
    {
        SetColors(renderers, Hex(htmlColor));
    }

    private static void SetColors(List<SpriteRenderer> renderers, Color color)
    {
        foreach (SpriteRenderer renderer in renderers) renderer.color = color;
    }

    private GameObject CreateCar(string carName, Vector2 position, Color bodyColor, int order, string spriteName)
    {
        GameObject root = new GameObject(carName);
        root.transform.position = position;
        GameObject body = new GameObject("Body");
        body.transform.SetParent(root.transform, false);
        SpriteRenderer renderer = body.AddComponent<SpriteRenderer>();
        Sprite sprite = LoadCarSprite(spriteName);
        if (sprite != null)
        {
            renderer.sprite = sprite;
            renderer.color = Color.white;
            Vector2 previewSize = carName == "Player car"
                ? GetPlayerCarSize(spriteName)
                : GetPreviewCarSize(spriteName);
            body.transform.localScale = new Vector3(
                previewSize.x / Mathf.Max(sprite.bounds.size.x, 0.01f),
                previewSize.y / Mathf.Max(sprite.bounds.size.y, 0.01f),
                1f);
            renderer.sortingOrder = order + 1;
            return root;
        }

        CreateRect("Soft shadow", new Vector2(3f, -4f), 38f, 62f, new Color(0.08f, 0.10f, 0.09f, 0.30f), order, root.transform);
        renderer.sprite = _pixelSprite;
        renderer.color = bodyColor;
        renderer.sortingOrder = order + 1;
        body.transform.localScale = new Vector3(34f, 60f, 1f);
        CreateRect("Roof", new Vector2(0f, 1f), 25f, 35f, Color.Lerp(bodyColor, Hex("d4d9d4"), 0.22f), order + 2, root.transform);
        CreateRect("Front glass", new Vector2(0f, 12f), 21f, 11f, Hex("b7c9c8"), order + 3, root.transform);
        CreateRect("Rear glass", new Vector2(0f, -10f), 21f, 10f, Hex("9aafb1"), order + 3, root.transform);
        CreateRect("Center roof strip", new Vector2(0f, 0f), 2f, 31f, Color.Lerp(bodyColor, Color.black, 0.12f), order + 4, root.transform);
        CreateRect("Left front wheel", new Vector2(-18f, 18f), 4f, 12f, Hex("222725"), order + 5, root.transform);
        CreateRect("Right front wheel", new Vector2(18f, 18f), 4f, 12f, Hex("222725"), order + 5, root.transform);
        CreateRect("Left rear wheel", new Vector2(-18f, -18f), 4f, 12f, Hex("222725"), order + 5, root.transform);
        CreateRect("Right rear wheel", new Vector2(18f, -18f), 4f, 12f, Hex("222725"), order + 5, root.transform);
        CreateRect("Front lights", new Vector2(-10f, 28f), 5f, 2f, Hex("fff0be"), order + 6, root.transform);
        CreateRect("Front lights", new Vector2(10f, 28f), 5f, 2f, Hex("fff0be"), order + 6, root.transform);
        CreateRect("Rear lights", new Vector2(-10f, -28f), 5f, 2f, Hex("c97867"), order + 6, root.transform);
        CreateRect("Rear lights", new Vector2(10f, -28f), 5f, 2f, Hex("c97867"), order + 6, root.transform);
        return root;
    }

    private static Vector2 GetPreviewCarSize(string spriteName)
    {
        // Measured from each sprite's bounds and its saved transform in ART EDIT PREVIEW in Game.unity.
        switch (spriteName)
        {
            case "Pride": return new Vector2(37.1512f, 55.7943f);
            case "SamandTaxi": return new Vector2(36.3221f, 61.2173f);
            case "NissanPickup": return new Vector2(37.3678f, 62.4633f);
            case "Peugeot206": return new Vector2(38.7857f, 56.8973f);
            case "Peugeot405": return new Vector2(37.6668f, 63.3044f);
            default: return new Vector2(34f, 60f);
        }
    }

    private static Vector2 GetPlayerCarSize(string spriteName)
    {
        return GetPreviewCarSize(spriteName);
    }

    private SpriteRenderer CreateRect(string objectName, Vector2 position, float width, float height, Color color, int order, Transform parent = null)
    {
        GameObject item = new GameObject(objectName);
        if (parent != null) item.transform.SetParent(parent, false);
        item.transform.localPosition = new Vector3(position.x, position.y, 0f);
        item.transform.localScale = new Vector3(width, height, 1f);
        SpriteRenderer renderer = item.AddComponent<SpriteRenderer>();
        renderer.sprite = _pixelSprite;
        renderer.color = color;
        renderer.sortingOrder = order;
        return renderer;
    }

    private static Color Hex(string value)
    {
        ColorUtility.TryParseHtmlString("#" + value, out Color color);
        return color;
    }
}

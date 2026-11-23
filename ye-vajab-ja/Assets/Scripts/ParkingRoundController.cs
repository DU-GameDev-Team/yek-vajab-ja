using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Deterministic three-round parking run, scoring and browser-facing flow.</summary>
[DisallowMultipleComponent]
public sealed class ParkingRoundController : MonoBehaviour
{
    private const int CurrentRulesVersion = 7;
    private static readonly double[] LegacyGaps = { 120d, 108d, 100d };
    private static readonly double[] LegacyAmplitudes = { 70d, 74d, 78d };
    private static readonly double[] LegacyDurationsMs = { 3200d, 3000d, 2800d };
    private static readonly double[] Version2Gaps = { 116d, 104d, 92d };
    private static readonly double[] Version2Amplitudes = { 72d, 78d, 84d };
    private static readonly double[] Version2DurationsMs = { 3000d, 2700d, 2400d };
    private static readonly double[] Version3Gaps = { 112d, 100d, 88d };
    private static readonly double[] Version3Amplitudes = { 76d, 82d, 88d };
    private static readonly double[] Version3DurationsMs = { 2900d, 2600d, 2300d };
    private static readonly double[] Version4Gaps = { 112d, 100d, 88d };
    private static readonly double[] Version4Amplitudes = { 76d, 82d, 88d };
    private static readonly double[] Version4DurationsMs = { 2900d, 2450d, 1950d };
    private static readonly double[] Gaps = { 112d, 98d, 84d };
    private static readonly double[] Amplitudes = { 76d, 82d, 88d };
    private static readonly double[] DurationsMs = { 2900d, 2250d, 1600d };
    private static readonly double[] Version5Gaps = { 112d, 100d, 88d };
    private static readonly double[] Version5Amplitudes = { 76d, 82d, 88d };
    private static readonly double[] Version5DurationsMs = { 2900d, 2250d, 1600d };
    private static readonly double[] Version6Gaps = { 112d, 98d, 84d };
    private static readonly double[] Version6Amplitudes = { 76d, 82d, 88d };
    private static readonly double[] Version6DurationsMs = { 2900d, 2250d, 1600d };

    // Gameplay collision is measured from the player's sprite length; older rule versions retain their lengths.
    private const double CarLength = 62.4633d;
    private const double Version6CarLength = 56.8973d;
    private const double LegacyCarLength = 60d;
    private double ActiveCarLength => _rulesVersion >= 7 ? CarLength :
        _rulesVersion >= 6 ? Version6CarLength : LegacyCarLength;

    private const double CenterBase = 290d;
    private const double ParkSlideMs = 450d;
    private const double FeedbackMs = 5000d;
    private const float LogicalHeight = 640f;

    private float PassLaneX => _parkOnRight ? 198f : 162f;
    private float ParkLaneX => _parkOnRight ? 262f : 98f;
    private float GapMarkerX => _parkOnRight ? 285f : 75f;

    [Serializable]
    public sealed class BrowserState
    {
        public string phase;
        public int round;
        public int total;
        public int currentScore;
        public int[] scores;
        public bool hasOpponent;
        public int opponentScore;
        public bool parkOnRight;
        public bool audioReady;
        public string sharePayload;
    }

    [Serializable]
    public sealed class SharePayload
    {
        public int v;
        public int seed;
        public long[] tapsMs;
        public int total;
    }

    [Serializable]
    public sealed class ChallengeData
    {
        public int v;
        public int seed;
        public int total;
    }


    private Transform _playerCar;
    private Transform _preview;
    private SpriteRenderer _previewRenderer;
    private Transform _upperParkedCar;
    private Transform _lowerParkedCar;
    private Transform _gapTopMarker;
    private Transform _gapBottomMarker;

    private readonly int[] _scores = new int[3];
    private readonly long[] _tapTimesMs = new long[3];
    private int _seed;
    private int _rulesVersion = CurrentRulesVersion;
    private int _roundIndex;
    private double _centerC;
    private double _phase;
    private double _activeElapsedMs;
    private double _previousClockMs;
    private double _slideStartClockMs;
    private double _feedbackEndClockMs;
    private double _pauseStartedClockMs;
    private int _lastScreenWidth;
    private int _lastScreenHeight;
    private Vector3 _slideStartPosition;
    private Vector3 _slideEndPosition;
    private bool _inputLocked;
    private bool _sliding;
    private bool _showFeedback;
    private bool _showFinalResult;
    private bool _paused;
    private bool _hasStarted;
    private bool _hasOpponentScore;
    private bool _parkOnRight;
    private int _opponentScore;
    private int _currentScore;
    private bool _hasCurrentResult;
    private StreetSceneView _streetScene;
    private AudioSource _resultAudioSource;
    private AudioSource _musicAudioSource;
    private AudioClip _collisionClip;
    private AudioClip _tireScreechClip;
    private AudioClip _highScoreClip;
    private AudioClip _finalFanfareClip;
    private AudioClip _backgroundMusicClip;
    private bool _backgroundMusicStarted;
    private bool _musicReady;

    private double NowMs => Time.realtimeSinceStartupAsDouble * 1000d;

    private void Start()
    {
        GameObject player = GameObject.Find("Player car");
        GameObject upper = GameObject.Find("Parked car upper");
        GameObject lower = GameObject.Find("Parked car lower");
        GameObject topMarker = GameObject.Find("Parking gap top marker");
        GameObject bottomMarker = GameObject.Find("Parking gap bottom marker");
        if (player == null || upper == null || lower == null || topMarker == null || bottomMarker == null)
        {
            Debug.LogError("ParkingRoundController could not find all street objects created by StreetSceneView.");
            enabled = false;
            return;
        }

        _playerCar = player.transform;
        _streetScene = GetComponent<StreetSceneView>();
        _upperParkedCar = upper.transform;
        _lowerParkedCar = lower.transform;
        _gapTopMarker = topMarker.transform;
        _gapBottomMarker = bottomMarker.transform;
        InitializeResultAudio();
#if UNITY_EDITOR
        // Keep the editor scene convenient for direct iteration; WebGL waits for the tutorial button.
        _hasStarted = true;
#endif
        SpriteRenderer body = _playerCar.Find("Body")?.GetComponent<SpriteRenderer>();
        if (body == null)
        {
            Debug.LogError("ParkingRoundController could not find the Player car body sprite.");
            enabled = false;
            return;
        }

        _preview = new GameObject("Parking preview shadow").transform;
        _previewRenderer = _preview.gameObject.AddComponent<SpriteRenderer>();
        _previewRenderer.sprite = body.sprite;
        _previewRenderer.color = new Color(0.10f, 0.12f, 0.10f, 0.30f);
        _previewRenderer.sortingOrder = 8;
        _preview.localScale = body.transform.localScale;

        _lastScreenWidth = Screen.width;
        _lastScreenHeight = Screen.height;
        BeginGame(CreateRandomSeed());
    }

    private void Update()
    {
        double now = NowMs;
        UpdateMusicReadiness();
        if (!_hasStarted)
        {
            _previousClockMs = now;
            return;
        }
        if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            EnterPause(now);
        }
        if (_paused)
        {
            _previousClockMs = now;
            return;
        }

        if (!_inputLocked)
            _activeElapsedMs += Math.Max(0d, now - _previousClockMs);
        _previousClockMs = now;

        if (_sliding)
        {
            float progress = Mathf.Clamp01((float)((now - _slideStartClockMs) / ParkSlideMs));
            _playerCar.position = Vector3.Lerp(_slideStartPosition, _slideEndPosition, progress);
            if (progress >= 1f)
                FinishSlide(now);
            return;
        }

        if (_showFeedback && now >= _feedbackEndClockMs)
        {
            CompleteFeedback();
        }

        if (_inputLocked)
            return;

        double roundedTimeMs = Math.Round(_activeElapsedMs, MidpointRounding.AwayFromZero);
        float logicalY = (float)CalculateGddY(roundedTimeMs);
        _playerCar.position = new Vector3(PassLaneX, GddYToUnityY(logicalY), 0f);
        UpdatePreview(logicalY);

        if (WasParkPressed())
        {
            StartBackgroundMusic();
            LockPark(roundedTimeMs, now);
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            EnterPause(NowMs);
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            EnterPause(NowMs);
    }

    private void EnterPause(double now)
    {
        if (!_hasStarted || _paused) return;
        _paused = true;
        _pauseStartedClockMs = now;
        _previousClockMs = now;
        NotifyWebUi();
    }

    private void ResumeFromPause()
    {
        double now = NowMs;
        double pausedDuration = Math.Max(0d, now - _pauseStartedClockMs);
        if (_sliding) _slideStartClockMs += pausedDuration;
        if (_showFeedback) _feedbackEndClockMs += pausedDuration;
        _previousClockMs = now;
        _paused = false;
        NotifyWebUi();
    }

    private bool WasParkPressed()
    {
        bool pointerPressed = false;
        if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
        {
            Camera gameCamera = Camera.main;
            Vector2 pointerPosition = Pointer.current.position.ReadValue();
            float guiY = Screen.height - pointerPosition.y;
            Vector3 worldPosition = gameCamera != null ? gameCamera.ScreenToWorldPoint(pointerPosition) : Vector3.zero;
            bool insideLogicalStreet = worldPosition.x >= 0f && worldPosition.x <= 360f && worldPosition.y >= 0f && worldPosition.y <= 640f;
            // HUD and browser controls must stay outside the parking input.
            pointerPressed = gameCamera != null && gameCamera.pixelRect.Contains(pointerPosition) && insideLogicalStreet && guiY >= 112f;
        }

        Keyboard keyboard = Keyboard.current;
        bool keyPressed = keyboard != null &&
            (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
        return pointerPressed || keyPressed;
    }

    public void WebPark()
    {
        if (!_hasStarted || _paused || _inputLocked || _sliding || _showFeedback || _showFinalResult)
            return;

        double now = NowMs;
        double clickTimeMs = _activeElapsedMs + Math.Max(0d, now - _previousClockMs);
        _activeElapsedMs = clickTimeMs;
        _previousClockMs = now;
        double roundedTimeMs = Math.Round(clickTimeMs, MidpointRounding.AwayFromZero);
        StartBackgroundMusic();
        LockPark(roundedTimeMs, now);
    }

    private void BeginGame(int seed) => BeginGame(seed, CurrentRulesVersion);

    private void BeginGame(int seed, int rulesVersion)
    {
        _seed = Mathf.Clamp(seed, 0, 511);
        _rulesVersion = rulesVersion >= 1 && rulesVersion <= CurrentRulesVersion ? rulesVersion : CurrentRulesVersion;
        // Seed bit 2 is round one's phase bit; it also selects the parking side.
        _parkOnRight = (_seed & 4) != 0;
        // Derive the ambience from the challenge seed so both players see the same street.
        _streetScene?.SetEnvironment(EnvironmentForSeed(_seed), _seed);
        Array.Clear(_scores, 0, _scores.Length);
        Array.Clear(_tapTimesMs, 0, _tapTimesMs.Length);
        _showFinalResult = false;
        StartRound(0);
    }

    private void StartRound(int index)
    {
        _roundIndex = index;
        int digit = (_seed >> (index * 3)) & 7;
        _centerC = CenterBase + 18d * (digit % 4);
        _phase = digit >= 4 ? 0.5d : 0d;
        _activeElapsedMs = 0d;
        _previousClockMs = NowMs;
        _inputLocked = false;
        _sliding = false;
        _showFeedback = false;
        _hasCurrentResult = false;
        _currentScore = 0;

        ConfigureParkingLayout();
        float startingY = GddYToUnityY(CalculateGddY(0d));
        _playerCar.position = new Vector3(PassLaneX, startingY, 0f);
        _preview.gameObject.SetActive(true);
        UpdatePreview((float)CalculateGddY(0d));
        NotifyWebUi();
    }

    private void ConfigureParkingLayout()
    {
        double gap = GapForRound(_roundIndex);
        double halfCenterDistance = (gap + ActiveCarLength) * 0.5d;
        double upperGddY = _centerC - halfCenterDistance;
        double lowerGddY = _centerC + halfCenterDistance;
        _upperParkedCar.position = new Vector3(ParkLaneX, GddYToUnityY(upperGddY), 0f);
        _lowerParkedCar.position = new Vector3(ParkLaneX, GddYToUnityY(lowerGddY), 0f);
        _gapTopMarker.position = new Vector3(GapMarkerX, GddYToUnityY(_centerC - gap * 0.5d), 0f);
        _gapBottomMarker.position = new Vector3(GapMarkerX, GddYToUnityY(_centerC + gap * 0.5d), 0f);
    }

    private void LockPark(double roundedTapMs, double now)
    {
        _inputLocked = true;
        double gddY = CalculateGddY(roundedTapMs);
        double error = Math.Abs(gddY - _centerC);
        double margin = (GapForRound(_roundIndex) - ActiveCarLength) * 0.5d;
        _currentScore = CalculateScore(error, margin);
        _tapTimesMs[_roundIndex] = (long)roundedTapMs;
        _scores[_roundIndex] = _currentScore;
        _hasCurrentResult = true;
        _preview.gameObject.SetActive(false);

        if (_currentScore == 0)
        {
            // Collision remains in the passing lane so cars never overlap.
            PlayResultSound(_collisionClip, 0.82f);
            BeginFeedback(now);
            return;
        }

        _slideStartPosition = new Vector3(PassLaneX, GddYToUnityY(gddY), 0f);
        _slideEndPosition = new Vector3(ParkLaneX, GddYToUnityY(gddY), 0f);
        _playerCar.position = _slideStartPosition;
        PlayResultSound(_tireScreechClip, 0.78f);
        if (_currentScore > 80)
            PlayResultSound(_highScoreClip, 0.78f);
        _slideStartClockMs = now;
        _sliding = true;
        NotifyWebUi();
    }

    private void FinishSlide(double now)
    {
        _sliding = false;
        _playerCar.position = _slideEndPosition;
        BeginFeedback(now);
    }

    private void InitializeResultAudio()
    {
        _resultAudioSource = GetComponent<AudioSource>();
        if (_resultAudioSource == null) _resultAudioSource = gameObject.AddComponent<AudioSource>();
        _resultAudioSource.playOnAwake = false;
        _resultAudioSource.loop = false;
        _resultAudioSource.spatialBlend = 0f;
        _collisionClip = Resources.Load<AudioClip>("Audio/CarWindowSmash");
        _tireScreechClip = Resources.Load<AudioClip>("Audio/CarBrakingSkid");
        _highScoreClip = Resources.Load<AudioClip>("Audio/SuccessHighScore");
        _finalFanfareClip = Resources.Load<AudioClip>("Audio/SuccessFanfare");

        _musicAudioSource = gameObject.AddComponent<AudioSource>();
        _musicAudioSource.playOnAwake = false;
        _musicAudioSource.loop = true;
        _musicAudioSource.spatialBlend = 0f;
        _musicAudioSource.volume = 0.16f;
        _backgroundMusicClip = Resources.Load<AudioClip>("Audio/TheBazaarLoop");
        if (_backgroundMusicClip != null && _backgroundMusicClip.loadState == AudioDataLoadState.Unloaded)
            _backgroundMusicClip.LoadAudioData();
        _musicReady = _backgroundMusicClip == null;
    }

    private void UpdateMusicReadiness()
    {
        if (_musicReady || _backgroundMusicClip == null)
            return;

        AudioDataLoadState loadState = _backgroundMusicClip.loadState;
        if (loadState != AudioDataLoadState.Loaded && loadState != AudioDataLoadState.Failed)
            return;

        _musicReady = true;
        if (loadState == AudioDataLoadState.Failed)
            Debug.LogWarning("Background music could not be prepared; the game can still start.");
        NotifyWebUi();
    }

    private void StartBackgroundMusic()
    {
        if (_musicAudioSource == null || _backgroundMusicClip == null)
            return;

        if (_backgroundMusicStarted && _musicAudioSource.isPlaying)
            return;

        _musicAudioSource.clip = _backgroundMusicClip;
        _musicAudioSource.Play();
        _backgroundMusicStarted = true;
    }

    private void PlayResultSound(AudioClip clip, float volume)
    {
        if (_resultAudioSource != null && clip != null)
            _resultAudioSource.PlayOneShot(clip, volume);
    }

    private void BeginFeedback(double now)
    {
        _showFeedback = true;
        _feedbackEndClockMs = now + FeedbackMs;
        NotifyWebUi();
    }

    private void CompleteFeedback()
    {
        if (!_showFeedback || _paused)
            return;

        _showFeedback = false;
        if (_roundIndex < 2)
        {
            StartRound(_roundIndex + 1);
            return;
        }

        _showFinalResult = true;
        // Give the final result cue a clean start instead of overlapping the last parking SFX.
        if (_resultAudioSource != null) _resultAudioSource.Stop();
        NotifyWebUi();
        PlayResultSound(_finalFanfareClip, 0.82f);
    }

    public void WebSkipFeedback()
    {
        CompleteFeedback();
    }

    private void UpdatePreview(float logicalY)
    {
        if (_preview == null || _inputLocked) return;
        _preview.position = new Vector3(ParkLaneX, GddYToUnityY(logicalY), 0f);
    }

    private double CalculateGddY(double activeMilliseconds)
    {
        double duration = _rulesVersion == 1 ? LegacyDurationsMs[_roundIndex] :
            _rulesVersion == 2 ? Version2DurationsMs[_roundIndex] :
            _rulesVersion == 3 ? Version3DurationsMs[_roundIndex] :
            _rulesVersion == 4 ? Version4DurationsMs[_roundIndex] :
            _rulesVersion == 5 ? Version5DurationsMs[_roundIndex] :
            _rulesVersion == 6 ? Version6DurationsMs[_roundIndex] : DurationsMs[_roundIndex];
        double amplitude = _rulesVersion == 1 ? LegacyAmplitudes[_roundIndex] :
            _rulesVersion == 2 ? Version2Amplitudes[_roundIndex] :
            _rulesVersion == 3 ? Version3Amplitudes[_roundIndex] :
            _rulesVersion == 4 ? Version4Amplitudes[_roundIndex] :
            _rulesVersion == 5 ? Version5Amplitudes[_roundIndex] :
            _rulesVersion == 6 ? Version6Amplitudes[_roundIndex] : Amplitudes[_roundIndex];
        double u = ((activeMilliseconds / duration) + _phase) % 1d;
        return _centerC + amplitude * (1d - 4d * Math.Abs(u - 0.5d));
    }

    private double GapForRound(int roundIndex) => _rulesVersion == 1 ? LegacyGaps[roundIndex] :
        _rulesVersion == 2 ? Version2Gaps[roundIndex] :
        _rulesVersion == 3 ? Version3Gaps[roundIndex] :
        _rulesVersion == 4 ? Version4Gaps[roundIndex] :
        _rulesVersion == 5 ? Version5Gaps[roundIndex] :
        _rulesVersion == 6 ? Version6Gaps[roundIndex] : Gaps[roundIndex];

    private static int CalculateScore(double error, double margin)
    {
        if (error > margin) return 0;
        double raw = 100d * (1d - error / margin);
        // GDD's reference runtime is JavaScript Math.round, which rounds positive .5 upward.
        return Math.Max(1, (int)Math.Floor(raw + 0.5d));
    }

    private static float GddYToUnityY(double gddY) => LogicalHeight - (float)gddY;

    private static int CreateRandomSeed() => UnityEngine.Random.Range(0, 512);

    private int CreateDifferentSeed()
    {
        int oldEnvironment = EnvironmentForSeed(_seed);
        int nextSeed;
        int[] separatedCenters = new int[4];
        do
        {
            nextSeed = 0;
            for (int round = 0; round < 3; round++)
            {
                int oldDigit = (_seed >> (round * 3)) & 7;
                int oldCenterIndex = oldDigit % 4;
                int candidateCount = 0;
                for (int candidate = 0; candidate < 4; candidate++)
                {
                    if (Math.Abs(candidate - oldCenterIndex) >= 2)
                        separatedCenters[candidateCount++] = candidate;
                }

                int centerIndex = separatedCenters[UnityEngine.Random.Range(0, candidateCount)];
                int phaseBit = UnityEngine.Random.Range(0, 2);
                int newDigit = centerIndex + phaseBit * 4;
                nextSeed |= newDigit << (round * 3);
            }

            bool nextParkOnRight = (nextSeed & 4) != 0;
            int nextEnvironment = EnvironmentForSeed(nextSeed);
            // Keep side/time and sidewalk props random, but require both to visibly change each layout.
            bool sideOrTimeChanged = nextParkOnRight != _parkOnRight || nextEnvironment != oldEnvironment;
            bool streetPropsChanged = !StreetSceneView.HasSameDecorationLayout(_seed, nextSeed);
            if (sideOrTimeChanged && streetPropsChanged)
                break;
        } while (true);

        return nextSeed;
    }

    // Round two and three phase bits provide an independent, deterministic three-way ambience.
    private static int EnvironmentForSeed(int seed)
    {
        int roundTwoPhase = (seed >> 5) & 1;
        int roundThreePhase = (seed >> 8) & 1;
        return (roundTwoPhase + 2 * roundThreePhase) % 3;
    }

    private int TotalScore => _scores[0] + _scores[1] + _scores[2];

    private string CurrentPhase
    {
        get
        {
            if (_paused) return "paused";
            if (_showFinalResult) return "final";
            if (_showFeedback) return "feedback";
            if (_sliding) return "sliding";
            return "playing";
        }
    }

    private void NotifyWebUi()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var state = new BrowserState
        {
            phase = CurrentPhase,
            round = _roundIndex + 1,
            total = TotalScore,
            currentScore = _currentScore,
            scores = (int[])_scores.Clone(),
            hasOpponent = _hasOpponentScore,
            opponentScore = _opponentScore,
            parkOnRight = _parkOnRight,
            audioReady = _musicReady,
            sharePayload = _showFinalResult
                ? JsonUtility.ToJson(new SharePayload { v = _rulesVersion, seed = _seed, tapsMs = (long[])_tapTimesMs.Clone(), total = TotalScore })
                : ""
        };
        YekVajabJaBrowserBridge.UpdateUi(JsonUtility.ToJson(state));
#endif
    }

    public void WebReplaySameLayout()
    {
        if (!_showFinalResult) return;
        BeginGame(_seed, _rulesVersion);
        NotifyWebUi();
    }

    public void WebNewLayout()
    {
        _hasOpponentScore = false;
        _opponentScore = 0;
        BeginGame(CreateDifferentSeed());
        NotifyWebUi();
    }

    public void WebContinue()
    {
        if (_paused) ResumeFromPause();
    }

    public void WebStartGame()
    {
        if (_hasStarted || !_musicReady) return;
        _hasStarted = true;
        _previousClockMs = NowMs;
        StartBackgroundMusic();
        NotifyWebUi();
    }

    public void OpenChallenge(string json)
    {
        ChallengeData challenge = JsonUtility.FromJson<ChallengeData>(json);
        if (challenge == null || challenge.v < 1 || challenge.v > CurrentRulesVersion || challenge.seed < 0 || challenge.seed > 511 || challenge.total < 0 || challenge.total > 300)
        {
            Debug.LogWarning("The challenge payload was not valid.");
            return;
        }

        _hasOpponentScore = true;
        _opponentScore = challenge.total;
        BeginGame(challenge.seed, challenge.v);
        NotifyWebUi();
    }

#if UNITY_EDITOR
    private void OnGUI()
    {
        int scale = Mathf.Clamp(Screen.height / 22, 18, 36);
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = scale,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = _hasCurrentResult && _currentScore == 0
            ? new Color(1f, 0.72f, 0.28f)
            : new Color(0.92f, 0.94f, 0.87f);

        if (_paused)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 110f, Screen.height * 0.5f - 55f, 220f, 110f), GUIContent.none);
            GUI.Label(new Rect(Screen.width * 0.5f - 100f, Screen.height * 0.5f - 48f, 200f, 32f), "PAUSED", style);
            if (GUI.Button(new Rect(Screen.width * 0.5f - 82f, Screen.height * 0.5f - 5f, 164f, 46f), "CONTINUE"))
                ResumeFromPause();
            return;
        }

        GUI.Label(new Rect(Screen.width * 0.5f - 85f, 12f, 170f, 48f), $"{_roundIndex + 1} / 3      {TotalScore}", style);
        if (_hasOpponentScore) GUI.Label(new Rect(Screen.width * 0.5f - 100f, 50f, 200f, 34f), "OPPONENT: " + _opponentScore, style);
        if (_hasCurrentResult && _showFeedback)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 78f, 66f, 156f, 90f), GUIContent.none);
            GUI.Label(new Rect(Screen.width * 0.5f - 74f, 72f, 148f, 78f), _currentScore.ToString(), style);
        }

        if (_showFinalResult)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 135f, Screen.height * 0.5f - 105f, 270f, 210f), GUIContent.none);
            GUI.Label(new Rect(Screen.width * 0.5f - 120f, Screen.height * 0.5f - 95f, 240f, 60f), TotalScore + " / 300", style);
            if (GUI.Button(new Rect(Screen.width * 0.5f - 112f, Screen.height * 0.5f - 24f, 224f, 48f), "REPLAY SAME LAYOUT"))
                WebReplaySameLayout();
            if (GUI.Button(new Rect(Screen.width * 0.5f - 112f, Screen.height * 0.5f + 34f, 224f, 48f), "NEW LAYOUT"))
                WebNewLayout();
        }
    }
#endif
}







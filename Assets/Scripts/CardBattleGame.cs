using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

internal static class CardBattleBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (UnityEngine.Object.FindObjectOfType<CardBattleGame>() != null)
        {
            return;
        }

        var host = new GameObject("Card Battle Runtime");
        host.AddComponent<CardBattleGame>();
    }
}

public sealed class CardBattleGame : MonoBehaviour
{
    private const int MaxPlayerHp = 50;
    private const int MaxEnemyHp = 60;
    private const int BaseEnergy = 3;
    private const int AudioSampleRate = 44100;
    private const float LeverPlayerFrame = 1f;
    private const float LeverEnemyFrame = 120f;
    private const float LeverReturnFrame = 236f;
    private const float LeverSwitchDuration = 0.42f;

    private readonly List<string> logs = new List<string>();
    private readonly List<CardView> cardViews = new List<CardView>();
    private readonly List<RectTransform> attackTrails = new List<RectTransform>();
    private readonly List<RectTransform> impactSlashes = new List<RectTransform>();
    private readonly List<RectTransform> impactRipples = new List<RectTransform>();
    private readonly List<RectTransform> impactSparks = new List<RectTransform>();
    private readonly Dictionary<string, AudioClip> soundClips = new Dictionary<string, AudioClip>();
    private readonly List<AudioSource> audioSources = new List<AudioSource>();
    private readonly HashSet<string> usedOnce = new HashSet<string>();
    private readonly HashSet<string> discardedCards = new HashSet<string>();

    private int audioCursor;
    private Font font;
    private RectTransform table;
    private RectTransform handRoot;
    private RectTransform scrollSheet;
    private RectTransform scrollLeftRoll;
    private RectTransform scrollRightRoll;
    private RectTransform fieldDivider;
    private RectTransform combatFlash;
    private RectTransform attackLine;
    private RectTransform chargeGlow;
    private RectTransform impactFlash;
    private RectTransform impactBurst;
    private RectTransform impactRing;
    private Text floatingText;
    private Text logText;
    private Text statusBar;
    private Text energyText;
    private Text playerHpText;
    private Text playerBlockText;
    private Text playerCardTitleText;
    private Text playerCardHpText;
    private Text playerCardBlockText;
    private Text enemyCardTitleText;
    private Text enemyHpText;
    private Text enemyBlockText;
    private Image playerHpFill;
    private Image enemyHpFill;
    private Text intentIconText;
    private Text intentNameText;
    private Text intentDescText;
    private Text debuffText;
    private Text turnText;
    private Text enemyActionText;
    private Text resultTitleText;
    private Text resultBodyText;
    private Button endTurnButton;
    private RawImage endTurnLeverImage;
    private RenderTexture endTurnLeverTexture;
    private GameObject endTurnLeverRig;
    private GameObject endTurnLeverModel;
    private Transform endTurnLeverModelPivot;
    private Camera endTurnLeverCamera;
    private Animation endTurnLeverAnimation;
    private AnimationClip endTurnLeverClip;
    private GameObject resultOverlay;
    private RectTransform enemyActiveCard;
    private RectTransform enemyAttackCard;
    private RectTransform activeHeroCard;
    private CardView draggingCard;
    private CardView hoveredCard;
    private Vector2 dragOffset;
    private Coroutine tableShakeRoutine;

    private int round;
    private int playerHp;
    private int playerBlock;
    private int energy;
    private int enemyHp;
    private int enemyBlock;
    private int intentIndex;
    private int weakTurns;
    private int vulnerableTurns;
    private bool playerTurn;
    private bool gameOver;
    private bool inputLocked;
    private bool isDraggingCard;
    private bool scrollOpen;
    private bool initialized;
    private string bootstrapError;

    private CardData[] cards;
    private IntentData[] intents;

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            Initialize();
        }
    }

    private void Initialize()
    {
        try
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            Application.runInBackground = true;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
            cardViews.Clear();
            BuildAudio();
            ClearRuntimeVisuals();
            BuildData();
            BuildScene();
            ResetGame();
        }
        catch (Exception exception)
        {
            initialized = false;
            bootstrapError = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private void ClearRuntimeVisuals()
    {
        var oldCanvas = GameObject.Find("Card Battle Canvas");
        if (oldCanvas != null)
        {
            Destroy(oldCanvas);
        }

        var oldLeverRig = GameObject.Find("End Turn Lever Render Rig");
        if (oldLeverRig != null)
        {
            Destroy(oldLeverRig);
        }

        if (endTurnLeverTexture != null)
        {
            endTurnLeverTexture.Release();
            Destroy(endTurnLeverTexture);
            endTurnLeverTexture = null;
        }
    }

    private void BuildData()
    {
        cards = new[]
        {
            new CardData("slash", "迅斩", "水 · 攻击", 1, "造成 6 点伤害。", new Color32(82, 190, 230, 255), false, () => DamageEnemy(6, "迅斩")),
            new CardData("guard", "架盾", "岩 · 防御", 1, "获得 5 点护甲。", new Color32(218, 172, 82, 255), false, () => GainBlock(5)),
            new CardData("heavy", "裂焰", "火 · 攻击", 2, "造成 11 点伤害。", new Color32(236, 92, 72, 255), false, () => DamageEnemy(11, "裂焰")),
            new CardData("heal", "回春", "风 · 支援", 2, "回复 5 点生命。", new Color32(112, 204, 138, 255), false, () => Heal(5)),
            new CardData("charge", "凝能", "秘 · 资源", 0, "获得 1 点费用。本回合限 1 次。", new Color32(240, 194, 86, 255), true, () => GainEnergy(1))
        };

        intents = new[]
        {
            new IntentData("突刺", "攻", "下回合造成 8 点伤害，并施加 1 回合虚弱。", new Color32(236, 92, 72, 255), () =>
            {
                DamagePlayer(8, "敌方攻击区发动攻击");
                ApplyWeak(1);
            }),
            new IntentData("蓄甲", "防", "下回合获得 7 点护甲。", new Color32(82, 190, 230, 255), () =>
            {
                enemyBlock += 7;
                enemyActionText.text = "敌方获得 7 点护甲";
                Log("敌人进入防御姿态，获得 7 点护甲。");
                StartCoroutine(SupportFlash("护甲 +7", enemyAttackCard));
            }),
            new IntentData("破阵", "破", "下回合造成 14 点伤害，并施加 1 回合易伤。", new Color32(240, 194, 86, 255), () =>
            {
                DamagePlayer(14, "敌方攻击区发动强攻");
                ApplyVulnerable(1);
            })
        };
    }

    private void BuildAudio()
    {
        audioSources.Clear();
        var existingSources = GetComponents<AudioSource>();
        foreach (var source in existingSources)
        {
            if (source != null)
            {
                audioSources.Add(source);
            }
        }

        while (audioSources.Count < 8)
        {
            audioSources.Add(gameObject.AddComponent<AudioSource>());
        }

        foreach (var source in audioSources)
        {
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 0.72f;
            source.pitch = 1f;
        }

        audioCursor = 0;
        soundClips.Clear();

        soundClips["draw"] = LoadOrCreateClip("CardBattle/Audio/draw_card", "Card_Draw", 0.16f, DrawSample);
    }

    private AudioClip LoadOrCreateClip(string resourcePath, string fallbackName, float duration, Func<float, float, float> sampleAtTime)
    {
        var clip = Resources.Load<AudioClip>(resourcePath);
        return clip != null ? clip : CreateSfxClip(fallbackName, duration, sampleAtTime);
    }

    private AudioClip CreateSfxClip(string name, float duration, Func<float, float, float> sampleAtTime)
    {
        var samples = Mathf.Max(1, Mathf.CeilToInt(duration * AudioSampleRate));
        var data = new float[samples];
        for (var i = 0; i < samples; i++)
        {
            var t = i / (float)AudioSampleRate;
            data[i] = Mathf.Clamp(sampleAtTime(t, duration), -0.95f, 0.95f);
        }

        var clip = AudioClip.Create(name, samples, 1, AudioSampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void PlaySound(string key, float volume, float pitch)
    {
        if (audioSources.Count == 0 || !soundClips.TryGetValue(key, out var clip) || clip == null)
        {
            return;
        }

        var source = audioSources[audioCursor % audioSources.Count];
        audioCursor = (audioCursor + 1) % audioSources.Count;
        source.pitch = pitch;
        source.PlayOneShot(clip, volume);
    }

    private static float Chirp(float startFrequency, float endFrequency, float t, float duration)
    {
        var k = (endFrequency - startFrequency) / Mathf.Max(0.001f, duration);
        return Mathf.Sin(Mathf.PI * 2f * (startFrequency * t + 0.5f * k * t * t));
    }

    private static float Envelope(float t, float duration, float attack, float release)
    {
        var fadeIn = Mathf.Clamp01(t / Mathf.Max(0.001f, attack));
        var fadeOut = Mathf.Clamp01((duration - t) / Mathf.Max(0.001f, release));
        return Mathf.Min(fadeIn, fadeOut);
    }

    private static float Noise(float t, float seed)
    {
        return Mathf.Repeat(Mathf.Sin((t * AudioSampleRate + seed) * 12.9898f) * 43758.5453f, 1f) * 2f - 1f;
    }

    private static float DrawSample(float t, float duration)
    {
        var env = Envelope(t, duration, 0.004f, 0.14f);
        return (Chirp(740f, 310f, t, duration) * 0.26f + Noise(t, 71f) * 0.12f) * env;
    }

    private void BuildScene()
    {
        var canvasObject = new GameObject("Card Battle Canvas", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        scaler.matchWidthOrHeight = 0.5f;

        var root = canvasObject.GetComponent<RectTransform>();
        Stretch(root);

        var backdrop = CreateImage(root, "Backdrop", new Color32(252, 252, 250, 255));
        Stretch(backdrop);

        table = CreatePanel(root, "Battle Arena", 50, 0, 1500, 900, new Color32(250, 250, 247, 255));
        table.anchorMin = new Vector2(0.5f, 0.5f);
        table.anchorMax = new Vector2(0.5f, 0.5f);
        table.pivot = new Vector2(0.5f, 0.5f);
        table.anchoredPosition = Vector2.zero;
        AddOutline(table.gameObject, new Color32(18, 18, 18, 255), new Vector2(2, -2));

        CreateTableDivider();
        BuildSketchZones();
        BuildBattleLog();
        BuildResultOverlay(root);
        BuildAttackEffects();
        BringTextToFront(table);
    }

    private void BuildSketchZones()
    {
        BuildReferenceSketchLayout();
    }

    private void BuildReferenceSketchLayout()
    {
        var infoPanel = Zone("选择查看敌方其他卡位的状态栏", 60, 32, 245, 122);
        Label(infoPanel, "点击敌方卡位时显示状态", 18, -70, 208, 28, 16, TextAnchor.MiddleCenter, Muted());

        var actionPile = CreateStackZone(108, 205, 165, 245, 10, 10);
        Label(actionPile, "行动牌", 0, -18, 165, 28, 20, TextAnchor.MiddleCenter, ColorText());

        var powerPile = CreateStackZone(108, 520, 165, 230, 10, 10);
        Label(powerPile, "能力牌", 0, -18, 165, 28, 20, TextAnchor.MiddleCenter, ColorText());

        var costArea = Zone(string.Empty, -12, 836, 360, 122);
        energyText = Label(costArea, "费用 3", 26, -22, 128, 30, 24, TextAnchor.MiddleLeft, ColorText());
        Button(costArea, "重开", 174, -22, 72, 34, () => ResetGame(), true);

        var stateRail = Zone(string.Empty, 510, 34, 42, 218);
        DrawRailSegments(stateRail, 1);

        enemyActiveCard = CreateHeroCard(table, "敌方出战角色", 590, 36, 0, false);

        var topEffects = Zone(string.Empty, 1122, 14, 350, 166);
        enemyAttackCard = topEffects;
        var intentBox = CreatePanel(topEffects, "Intent Icon", 26, -32, 42, 42, new Color32(248, 248, 248, 255));
        AddOutline(intentBox.gameObject, ColorText(), new Vector2(1, -1));
        intentIconText = Label(intentBox, "攻", 0, 0, 42, 42, 22, TextAnchor.MiddleCenter, ColorText());
        intentNameText = Label(topEffects, "突刺", 86, -30, 210, 24, 18, TextAnchor.MiddleLeft, ColorText());
        intentDescText = Label(topEffects, "下回合造成 8 点伤害", 26, -78, 294, 52, 14, TextAnchor.UpperLeft, Muted());
        enemyActionText = Label(topEffects, "等待我方结束回合", 26, -126, 294, 22, 13, TextAnchor.MiddleCenter, Muted());

        fieldDivider = CreateImage(table, "Field Divider", new Color32(18, 18, 18, 255));
        SetTopLeft(fieldDivider, 0, 312, 1500, 3);
        fieldDivider.SetAsFirstSibling();

        var enemyHandA = CreatePanel(table, "Enemy Hand Counter A", 1060, 258, 44, 54, new Color32(252, 252, 250, 255));
        AddOutline(enemyHandA.gameObject, ColorText(), new Vector2(1, -1));

        Zone(string.Empty, 1226, 248, 224, 44);
        BuildEndTurnLever();

        var handRail = Zone(string.Empty, 1004, 476, 42, 196);
        DrawRailSegments(handRail, 2);

        var playerEffects = Zone(string.Empty, 1124, 470, 350, 288);
        debuffText = Label(playerEffects, "暂无负面状态", 28, -34, 290, 58, 18, TextAnchor.UpperLeft, ColorText());
        logText = Label(playerEffects, string.Empty, 28, -106, 292, 146, 13, TextAnchor.UpperLeft, Muted());

        BuildPlayerCharacter();
        BuildHand();

        var statusPanel = Zone(string.Empty, 458, 808, 456, 44);
        turnText = Label(statusPanel, "我方行动", 14, -8, 96, 28, 16, TextAnchor.MiddleCenter, ColorText());
        statusBar = Label(statusPanel, string.Empty, 110, -6, 336, 30, 17, TextAnchor.MiddleCenter, ColorText());
    }

    private void BuildPlayerCharacter()
    {
        var leftBench = CreateBenchCard(table, 430, 590, 0f);
        leftBench.SetAsFirstSibling();

        activeHeroCard = CreateHeroCard(table, "我方角色", 612, 530, 0, true);

        var rightBench = CreateBenchCard(table, 798, 590, 0f);
        rightBench.SetAsFirstSibling();
    }

    private void BuildEndTurnLever()
    {
        var lever = CreateEmpty(table, "End Turn Lever");
        SetTopLeft(lever, 1110, 248, 112, 150);

        var viewObject = new GameObject("Lever Model View", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        viewObject.transform.SetParent(lever, false);
        var view = viewObject.GetComponent<RectTransform>();
        SetTopLeft(view, 0, 0, 112, 150);

        endTurnLeverTexture = new RenderTexture(192, 256, 16, RenderTextureFormat.ARGB32)
        {
            name = "End Turn Lever Render Texture",
            antiAliasing = 4
        };

        endTurnLeverImage = viewObject.GetComponent<RawImage>();
        endTurnLeverImage.texture = endTurnLeverTexture;
        endTurnLeverImage.color = Color.white;

        BuildEndTurnLeverRig();

        endTurnButton = viewObject.AddComponent<Button>();
        endTurnButton.targetGraphic = endTurnLeverImage;
        var colors = endTurnButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.disabledColor = Color.white;
        colors.colorMultiplier = 1f;
        endTurnButton.colors = colors;
        endTurnButton.onClick.AddListener(() => EndTurn());
    }

    private void BuildEndTurnLeverRig()
    {
        if (endTurnLeverTexture == null)
        {
            return;
        }

        var prefab = Resources.Load<GameObject>("CardBattle/Models/on_off_lever");
        if (prefab == null)
        {
            Debug.LogError("Cannot load CardBattle/Models/on_off_lever. The lever model must be under Assets/Resources/CardBattle/Models.");
            return;
        }

        endTurnLeverRig = new GameObject("End Turn Lever Render Rig");
        endTurnLeverRig.transform.position = new Vector3(200f, -200f, 200f);

        endTurnLeverModelPivot = new GameObject("Lever Model Pivot").transform;
        endTurnLeverModelPivot.SetParent(endTurnLeverRig.transform, false);

        endTurnLeverModel = Instantiate(prefab, endTurnLeverModelPivot);
        endTurnLeverModel.name = "On Off Lever Model";
        endTurnLeverModel.transform.localPosition = Vector3.zero;
        endTurnLeverModel.transform.localRotation = Quaternion.identity;
        endTurnLeverModel.transform.localScale = Vector3.one;

        ApplyEndTurnLeverMaterials(endTurnLeverModel);
        FitEndTurnLeverModel(endTurnLeverModel.transform);
        SetupEndTurnLeverAnimation();

        var keyLightObject = new GameObject("Lever Key Light");
        keyLightObject.transform.SetParent(endTurnLeverRig.transform, false);
        keyLightObject.transform.localEulerAngles = new Vector3(0f, 0f, 0f);
        var keyLight = keyLightObject.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.intensity = 1.6f;
        keyLight.color = new Color(1f, 0.96f, 0.9f, 1f);

        var fillLightObject = new GameObject("Lever Fill Light");
        fillLightObject.transform.SetParent(endTurnLeverRig.transform, false);
        fillLightObject.transform.localEulerAngles = new Vector3(0f, 180f, 0f);
        var fillLight = fillLightObject.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.intensity = 0.35f;
        fillLight.color = new Color(0.6f, 0.74f, 1f, 1f);

        var cameraObject = new GameObject("Lever Camera");
        cameraObject.transform.SetParent(endTurnLeverRig.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 3.6f, 0f);
        cameraObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        endTurnLeverCamera = cameraObject.AddComponent<Camera>();
        endTurnLeverCamera.clearFlags = CameraClearFlags.SolidColor;
        endTurnLeverCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        endTurnLeverCamera.orthographic = true;
        endTurnLeverCamera.orthographicSize = 1.18f;
        endTurnLeverCamera.nearClipPlane = 0.01f;
        endTurnLeverCamera.farClipPlane = 20f;
        endTurnLeverCamera.targetTexture = endTurnLeverTexture;
        endTurnLeverCamera.Render();
    }

    private void FitEndTurnLeverModel(Transform model)
    {
        model.localEulerAngles = new Vector3(0f, 90f, 0f);

        var bounds = GetRenderBounds(model.gameObject);
        if (bounds.size.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        CenterLeverModel(model, bounds);
        bounds = GetRenderBounds(model.gameObject);

        var maxDimension = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        var scale = 1.68f / Mathf.Max(0.001f, maxDimension);
        model.localScale *= scale;

        bounds = GetRenderBounds(model.gameObject);
        CenterLeverModel(model, bounds);
    }

    private void SetupEndTurnLeverAnimation()
    {
        if (endTurnLeverModel == null)
        {
            return;
        }

        endTurnLeverClip = FindEndTurnLeverClip();
        if (endTurnLeverClip == null)
        {
            Debug.LogWarning("No animation clip found in CardBattle/Models/on_off_lever.");
            return;
        }

        endTurnLeverClip.legacy = true;
        endTurnLeverAnimation = endTurnLeverModel.GetComponent<Animation>();
        if (endTurnLeverAnimation == null)
        {
            endTurnLeverAnimation = endTurnLeverModel.AddComponent<Animation>();
        }

        endTurnLeverAnimation.playAutomatically = false;
        endTurnLeverAnimation.AddClip(endTurnLeverClip, endTurnLeverClip.name);
        endTurnLeverAnimation.clip = endTurnLeverClip;
        SampleEndTurnLeverAnimation(false);
    }

    private AnimationClip FindEndTurnLeverClip()
    {
        var clips = Resources.LoadAll<AnimationClip>("CardBattle/Models/on_off_lever");
        AnimationClip fallback = null;
        foreach (var clip in clips)
        {
            if (clip == null || clip.name.IndexOf("__preview__", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            if (fallback == null)
            {
                fallback = clip;
            }

            var name = clip.name.ToLowerInvariant();
            if (name.Contains("take") || name.Contains("lever") || name.Contains("action"))
            {
                return clip;
            }
        }

        return fallback;
    }

    private void CenterLeverModel(Transform model, Bounds bounds)
    {
        if (model.parent == null)
        {
            return;
        }

        var localCenter = model.parent.InverseTransformPoint(bounds.center);
        model.localPosition -= localCenter;
    }

    private Bounds GetRenderBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        var hasBounds = false;
        var bounds = new Bounds(root.transform.position, Vector3.zero);
        foreach (var renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return bounds;
    }

    private void ApplyEndTurnLeverMaterials(GameObject root)
    {
        var dark = CreateLeverMaterial("Lever Dark Metal", new Color32(16, 16, 16, 255), 0.78f, 0.58f);
        var rubber = CreateLeverMaterial("Lever Rubber", new Color32(3, 3, 3, 255), 0.2f, 0.42f);
        var steel = CreateLeverMaterial("Lever Polished Steel", new Color32(210, 210, 206, 255), 0.92f, 0.72f);

        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
        {
            var name = renderer.name.ToLowerInvariant();
            if (name.Contains("stem") || name.Contains("pin") || name.Contains("hub") || name.Contains("cap"))
            {
                renderer.material = steel;
            }
            else if (name.Contains("grip") || name.Contains("rubber"))
            {
                renderer.material = rubber;
            }
            else
            {
                renderer.material = dark;
            }
        }
    }

    private Material CreateLeverMaterial(string name, Color color, float metallic, float smoothness)
    {
        var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
        var material = new Material(shader)
        {
            name = name,
            color = color
        };

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", metallic);
        }
        if (material.HasProperty("_Glossiness"))
        {
            material.SetFloat("_Glossiness", smoothness);
        }

        return material;
    }

    private RectTransform CreateBenchCard(RectTransform parent, float x, float y, float rotation)
    {
        var card = CreatePanel(parent, "Sketch Bench Character", x, y, 124, 154, new Color32(252, 252, 250, 255));
        card.localEulerAngles = new Vector3(0, 0, rotation);
        AddOutline(card.gameObject, ColorText(), new Vector2(2, -2));
        return card;
    }

    private RectTransform CreateHeroCard(RectTransform parent, string title, float x, float y, float rotation, bool active)
    {
        var card = CreatePanel(parent, title, x, y, 150, 218, new Color32(252, 252, 250, 255));
        card.localEulerAngles = new Vector3(0, 0, rotation);
        AddOutline(card.gameObject, ColorText(), new Vector2(2, -2));
        DrawHeroArt(card, 18, -18, 114, 104);
        var titleLabel = Label(card, title, 12, -132, 126, 24, 18, TextAnchor.MiddleCenter, ColorText());
        var hp = Label(card, "生命 50", 16, -160, 56, 20, 12, TextAnchor.MiddleCenter, Red());
        var block = Label(card, "护甲 0", 82, -160, 56, 20, 12, TextAnchor.MiddleCenter, Blue());
        var hpFill = CreateStatBar(card, 18, -190, 114, 8, new Color32(236, 92, 72, 255));
        if (active)
        {
            playerCardTitleText = titleLabel;
            playerCardHpText = hp;
            playerCardBlockText = block;
            playerHpText = hp;
            playerBlockText = block;
            if (playerHpFill == null)
            {
                playerHpFill = hpFill;
            }
        }
        else if (title.IndexOf("敌方", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            enemyCardTitleText = titleLabel;
            enemyHpText = hp;
            enemyBlockText = block;
            enemyHpFill = hpFill;
        }
        card.gameObject.AddComponent<RectMask2D>();
        return card;
    }

    private void BuildHand()
    {
        scrollSheet = CreatePanel(table, "Bamboo Scroll Sheet", 390, 618, 660, 176, new Color32(252, 252, 250, 255));
        AddOutline(scrollSheet.gameObject, ColorText(), new Vector2(2, -2));
        scrollLeftRoll = CreatePanel(table, "Bamboo Scroll Left Roll", 366, 600, 32, 204, new Color32(252, 252, 250, 255));
        AddOutline(scrollLeftRoll.gameObject, ColorText(), new Vector2(2, -2));
        scrollRightRoll = CreatePanel(table, "Bamboo Scroll Right Roll", 1042, 600, 32, 204, new Color32(252, 252, 250, 255));
        AddOutline(scrollRightRoll.gameObject, ColorText(), new Vector2(2, -2));
        SetBambooScrollVisual(false, 0f);

        handRoot = CreateEmpty(table, "Hand");
        SetTopLeft(handRoot, 392, 630, 646, 154);

        var offsets = new[] { -232f, -116f, 0f, 116f, 232f };
        for (var i = 0; i < cards.Length; i++)
        {
            var card = cards[i];
            var button = CreateCardButton(handRoot, card, offsets[i], 0f);
            cardViews.Add(new CardView(card, button.Button, button.Note, button.Root, button.Root.anchoredPosition, button.Title, button.Cost, button.Type, button.Body));
        }
    }

    private void SetBambooScrollVisual(bool open, float progress)
    {
        progress = Mathf.Clamp01(progress);
        var visible = open || progress > 0.001f;
        if (scrollSheet != null)
        {
            scrollSheet.gameObject.SetActive(visible);
            var width = Mathf.Lerp(44f, 660f, progress);
            SetTopLeft(scrollSheet, 390 + (660f - width) * 0.5f, 618, width, 176);
        }
        if (scrollLeftRoll != null)
        {
            scrollLeftRoll.gameObject.SetActive(visible);
            SetTopLeft(scrollLeftRoll, Mathf.Lerp(704f, 366f, progress), 600, 32, 204);
        }
        if (scrollRightRoll != null)
        {
            scrollRightRoll.gameObject.SetActive(visible);
            SetTopLeft(scrollRightRoll, Mathf.Lerp(704f, 1042f, progress), 600, 32, 204);
        }
    }

    private void ToggleBambooScroll()
    {
        if (inputLocked || gameOver || !playerTurn)
        {
            return;
        }

        StartCoroutine(scrollOpen ? CloseBambooScroll() : OpenBambooScroll());
    }

    private IEnumerator OpenBambooScroll()
    {
        inputLocked = true;
        RaiseHandLayer();
        for (var i = 0; i < cardViews.Count; i++)
        {
            var view = cardViews[i];
            if (discardedCards.Contains(view.Card.Id))
            {
                continue;
            }

            view.Root.gameObject.SetActive(true);
            view.Root.anchoredPosition = BambooScrollPositionInHand();
            view.Root.localScale = Vector3.one * 0.18f;
            view.Root.localEulerAngles = Vector3.zero;
            EnsureCanvasGroup(view.Root).alpha = 0f;
        }

        for (var t = 0f; t < 0.28f; t += Time.unscaledDeltaTime)
        {
            var p = EaseOut(t / 0.28f);
            SetBambooScrollVisual(true, p);
            for (var i = 0; i < cardViews.Count; i++)
            {
                var view = cardViews[i];
                if (discardedCards.Contains(view.Card.Id))
                {
                    continue;
                }

                var group = EnsureCanvasGroup(view.Root);
                view.Root.anchoredPosition = Vector2.Lerp(BambooScrollPositionInHand(), view.HomePosition, p);
                view.Root.localScale = Vector3.Lerp(Vector3.one * 0.18f, Vector3.one, p);
                view.Root.localEulerAngles = Vector3.zero;
                group.alpha = p;
            }
            yield return null;
        }

        SetBambooScrollVisual(true, 1f);
        for (var i = 0; i < cardViews.Count; i++)
        {
            var view = cardViews[i];
            if (discardedCards.Contains(view.Card.Id))
            {
                continue;
            }

            view.Root.anchoredPosition = view.HomePosition;
            view.Root.localScale = Vector3.one;
            view.Root.localEulerAngles = Vector3.zero;
            EnsureCanvasGroup(view.Root).alpha = 1f;
        }

        scrollOpen = true;
        inputLocked = false;
        Render();
    }

    private IEnumerator CloseBambooScroll()
    {
        inputLocked = true;
        for (var t = 0f; t < 0.22f; t += Time.unscaledDeltaTime)
        {
            var p = EaseOut(t / 0.22f);
            var close = 1f - p;
            SetBambooScrollVisual(true, close);
            for (var i = 0; i < cardViews.Count; i++)
            {
                var view = cardViews[i];
                if (!view.Root.gameObject.activeSelf)
                {
                    continue;
                }

                var group = EnsureCanvasGroup(view.Root);
                view.Root.anchoredPosition = Vector2.Lerp(view.HomePosition, BambooScrollPositionInHand(), p);
                view.Root.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 0.18f, p);
                group.alpha = close;
            }
            yield return null;
        }

        CloseBambooScrollImmediate();
        inputLocked = false;
        Render();
    }

    private void CloseBambooScrollImmediate()
    {
        scrollOpen = false;
        hoveredCard = null;
        SetBambooScrollVisual(false, 0f);
        if (endTurnButton != null)
        {
            endTurnButton.interactable = playerTurn && !gameOver && !inputLocked;
        }
        foreach (var view in cardViews)
        {
            view.Root.anchoredPosition = BambooScrollPositionInHand();
            view.Root.localScale = Vector3.one;
            view.Root.localEulerAngles = Vector3.zero;
            EnsureCanvasGroup(view.Root).alpha = 0f;
            if (!isDraggingCard || draggingCard != view)
            {
                view.Root.gameObject.SetActive(false);
            }
        }
    }

    private BuiltCard CreateCardButton(RectTransform parent, CardData card, float xOffset, float rotation)
    {
        var root = CreatePanel(parent, card.Name, 323 + xOffset - 46, 0, 92, 132, new Color32(252, 252, 250, 255));
        root.localEulerAngles = new Vector3(0, 0, rotation);
        AddOutline(root.gameObject, ColorText(), new Vector2(2, -2));
        root.gameObject.AddComponent<RectMask2D>();

        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = root.GetComponent<Image>();
        root.gameObject.AddComponent<CanvasGroup>();

        var titleText = Label(root, card.Name, 7, -6, 54, 18, 12, TextAnchor.MiddleLeft, ColorText());
        var cost = CreatePanel(root, "Cost", 70, -6, 16, 16, new Color32(252, 252, 250, 255));
        AddOutline(cost.gameObject, ColorText(), new Vector2(1, -1));
        var costText = Label(cost, card.Cost.ToString(), 0, 0, 16, 16, 11, TextAnchor.MiddleCenter, ColorText());
        var separator = CreateImage(root, "Card Separator", ColorText());
        SetTopLeft(separator, 7, 30, 78, 1);
        var typeText = Label(root, card.Type, 7, -34, 78, 15, 8, TextAnchor.MiddleLeft, Muted());
        typeText.horizontalOverflow = HorizontalWrapMode.Wrap;
        var textLine = CreateImage(root, "Card Text Separator", ColorText());
        SetTopLeft(textLine, 7, 76, 78, 1);
        var bodyText = Label(root, card.Text, 7, -84, 78, 38, 8, TextAnchor.UpperLeft, ColorText());
        var note = Label(root, string.Empty, 7, -118, 78, 12, 8, TextAnchor.MiddleLeft, Red());
        var dissolve = CreateImage(root, "Card Dissolve", new Color32(255, 220, 130, 0));
        SetTopLeft(dissolve, 0, 0, 92, 132);
        var dissolveImage = dissolve.GetComponent<Image>();
        dissolveImage.sprite = CreateDissolveSprite(card.Tint);
        dissolveImage.type = Image.Type.Simple;
        dissolveImage.preserveAspect = false;
        dissolveImage.raycastTarget = false;
        dissolve.gameObject.SetActive(false);
        ApplyCardTextQuality(titleText, 12, true);
        ApplyCardTextQuality(costText, 11, true);
        ApplyCardTextQuality(typeText, 8, false);
        ApplyCardTextQuality(bodyText, 8, false);
        ApplyCardTextQuality(note, 8, false);
        return new BuiltCard(button, note, root, titleText, costText, typeText, bodyText);
    }

    private void BuildBattleLog()
    {
        if (logText != null)
        {
            return;
        }

        var logPanel = Zone(string.Empty, 1164, 692, 322, 184);
        logText = Label(logPanel, string.Empty, 24, -48, 274, 104, 12, TextAnchor.UpperLeft, ColorText());
    }

    private void BuildResultOverlay(RectTransform root)
    {
        resultOverlay = CreateImage(root, "Result Overlay", new Color32(0, 0, 0, 170)).gameObject;
        Stretch(resultOverlay.GetComponent<RectTransform>());
        resultOverlay.SetActive(false);

        var box = CreatePanel(resultOverlay.GetComponent<RectTransform>(), "Result Box", 590, -310, 420, 230, new Color32(252, 252, 250, 255));
        AddOutline(box.gameObject, ColorText(), new Vector2(2, -2));
        box.anchorMin = new Vector2(0.5f, 0.5f);
        box.anchorMax = new Vector2(0.5f, 0.5f);
        box.pivot = new Vector2(0.5f, 0.5f);
        box.anchoredPosition = Vector2.zero;
        resultTitleText = Label(box, "胜利", 0, -30, 420, 48, 34, TextAnchor.MiddleCenter, ColorText());
        resultBodyText = Label(box, "你击败了敌人。", 36, -86, 348, 56, 16, TextAnchor.MiddleCenter, Muted());
        Button(box, "再来一局", 142, -160, 136, 42, () => ResetGame());
    }

    private void BuildAttackEffects()
    {
        combatFlash = CreateImage(table, "Combat Flash", new Color32(0, 0, 0, 0));
        Stretch(combatFlash);
        var combatFlashImage = combatFlash.GetComponent<Image>();
        combatFlashImage.raycastTarget = false;
        combatFlash.gameObject.SetActive(false);

        attackLine = CreateImage(table, "Attack Line", new Color32(255, 220, 120, 0));
        attackLine.anchorMin = new Vector2(0.5f, 0.5f);
        attackLine.anchorMax = new Vector2(0.5f, 0.5f);
        attackLine.pivot = new Vector2(0.5f, 0.5f);
        attackLine.sizeDelta = new Vector2(520, 28);
        var attackLineImage = attackLine.GetComponent<Image>();
        attackLineImage.sprite = CreateAttackTrailSprite();
        attackLineImage.type = Image.Type.Simple;
        attackLineImage.preserveAspect = false;
        attackLineImage.raycastTarget = false;
        attackLine.gameObject.SetActive(false);

        attackTrails.Clear();
        for (var i = 0; i < 7; i++)
        {
            var trail = CreateImage(table, "Attack Trail", Color.white);
            trail.anchorMin = new Vector2(0.5f, 0.5f);
            trail.anchorMax = new Vector2(0.5f, 0.5f);
            trail.pivot = new Vector2(0.5f, 0.5f);
            trail.sizeDelta = new Vector2(420, 26);
            var trailImage = trail.GetComponent<Image>();
            trailImage.sprite = CreateAttackTrailSprite();
            trailImage.type = Image.Type.Simple;
            trailImage.preserveAspect = false;
            trailImage.raycastTarget = false;
            trail.gameObject.SetActive(false);
            attackTrails.Add(trail);
        }

        chargeGlow = CreateImage(table, "Attack Charge Glow", Color.white);
        chargeGlow.anchorMin = new Vector2(0.5f, 0.5f);
        chargeGlow.anchorMax = new Vector2(0.5f, 0.5f);
        chargeGlow.pivot = new Vector2(0.5f, 0.5f);
        chargeGlow.sizeDelta = new Vector2(190, 190);
        var chargeGlowImage = chargeGlow.GetComponent<Image>();
        chargeGlowImage.sprite = CreateChargeGlowSprite();
        chargeGlowImage.raycastTarget = false;
        chargeGlow.gameObject.SetActive(false);

        impactFlash = CreateImage(table, "Impact Flash", Color.white);
        impactFlash.anchorMin = new Vector2(0.5f, 0.5f);
        impactFlash.anchorMax = new Vector2(0.5f, 0.5f);
        impactFlash.pivot = new Vector2(0.5f, 0.5f);
        impactFlash.sizeDelta = new Vector2(150, 150);
        var flashImage = impactFlash.GetComponent<Image>();
        flashImage.sprite = CreateImpactFlashSprite();
        flashImage.raycastTarget = false;
        impactFlash.gameObject.SetActive(false);

        impactBurst = CreateImage(table, "Impact Burst", Color.white);
        impactBurst.anchorMin = new Vector2(0.5f, 0.5f);
        impactBurst.anchorMax = new Vector2(0.5f, 0.5f);
        impactBurst.pivot = new Vector2(0.5f, 0.5f);
        impactBurst.sizeDelta = new Vector2(120, 120);
        var burstImage = impactBurst.GetComponent<Image>();
        burstImage.sprite = CreateImpactSprite("slash", false);
        burstImage.raycastTarget = false;
        impactBurst.gameObject.SetActive(false);

        impactRing = CreateImage(table, "Impact Ring", Color.white);
        impactRing.anchorMin = new Vector2(0.5f, 0.5f);
        impactRing.anchorMax = new Vector2(0.5f, 0.5f);
        impactRing.pivot = new Vector2(0.5f, 0.5f);
        impactRing.sizeDelta = new Vector2(130, 130);
        var ringImage = impactRing.GetComponent<Image>();
        ringImage.sprite = CreateImpactSprite("slash", true);
        ringImage.raycastTarget = false;
        impactRing.gameObject.SetActive(false);

        impactSlashes.Clear();
        for (var i = 0; i < 4; i++)
        {
            var slash = CreateImage(table, "Impact Slash", Color.white);
            slash.anchorMin = new Vector2(0.5f, 0.5f);
            slash.anchorMax = new Vector2(0.5f, 0.5f);
            slash.pivot = new Vector2(0.5f, 0.5f);
            slash.sizeDelta = new Vector2(220f, 28f);
            var slashImage = slash.GetComponent<Image>();
            slashImage.sprite = CreateAttackTrailSprite();
            slashImage.type = Image.Type.Simple;
            slashImage.preserveAspect = false;
            slashImage.raycastTarget = false;
            slash.gameObject.SetActive(false);
            impactSlashes.Add(slash);
        }

        impactRipples.Clear();
        for (var i = 0; i < 3; i++)
        {
            var ripple = CreateImage(table, "Impact Ripple", Color.white);
            ripple.anchorMin = new Vector2(0.5f, 0.5f);
            ripple.anchorMax = new Vector2(0.5f, 0.5f);
            ripple.pivot = new Vector2(0.5f, 0.5f);
            ripple.sizeDelta = new Vector2(180, 180);
            var rippleImage = ripple.GetComponent<Image>();
            rippleImage.sprite = CreateRippleSprite();
            rippleImage.raycastTarget = false;
            ripple.gameObject.SetActive(false);
            impactRipples.Add(ripple);
        }

        impactSparks.Clear();
        for (var i = 0; i < 24; i++)
        {
            var spark = CreateImage(table, "Impact Spark", Color.white);
            spark.anchorMin = new Vector2(0.5f, 0.5f);
            spark.anchorMax = new Vector2(0.5f, 0.5f);
            spark.pivot = new Vector2(0.5f, 0.5f);
            spark.sizeDelta = new Vector2(34f, 10f);
            var sparkImage = spark.GetComponent<Image>();
            sparkImage.sprite = CreateSparkSprite();
            sparkImage.type = Image.Type.Simple;
            sparkImage.preserveAspect = false;
            sparkImage.raycastTarget = false;
            spark.gameObject.SetActive(false);
            impactSparks.Add(spark);
        }

        floatingText = Label(table, string.Empty, 650, -370, 220, 72, 52, TextAnchor.MiddleCenter, new Color32(255, 226, 145, 255));
        floatingText.gameObject.SetActive(false);
        var shadow = floatingText.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color32(0, 0, 0, 160);
        shadow.effectDistance = new Vector2(0, -4);
    }

    private void CreateTableDivider()
    {
    }

    private RectTransform CreateStackZone(float x, float y, float width, float height, float offsetX, float offsetY)
    {
        var backB = CreatePanel(table, "Sketch Stack Back B", x + offsetX * 1.7f, y + offsetY * 1.7f, width, height, new Color32(252, 252, 250, 255));
        AddOutline(backB.gameObject, ColorText(), new Vector2(2, -2));
        var backA = CreatePanel(table, "Sketch Stack Back A", x + offsetX, y + offsetY, width, height, new Color32(252, 252, 250, 255));
        AddOutline(backA.gameObject, ColorText(), new Vector2(2, -2));
        return Zone(string.Empty, x, y, width, height);
    }

    private void DrawRailSegments(RectTransform parent, int segments)
    {
        if (segments <= 0)
        {
            return;
        }

        for (var i = 1; i <= segments; i++)
        {
            var y = parent.sizeDelta.y * i / (segments + 1);
            var line = CreateImage(parent, "Sketch Rail Segment", ColorText());
            SetTopLeft(line, 0, y, parent.sizeDelta.x, 1);
        }
    }

    private void DrawHeroArt(RectTransform parent, float x, float y, float width, float height)
    {
        var art = CreatePanel(parent, "Hero Art", x, y, width, height, new Color32(252, 252, 250, 255));
        AddOutline(art.gameObject, ColorText(), new Vector2(2, -2));
        var head = CreatePanel(art, "Sketch Head", width * 0.5f - 9f, -20, 18, 18, new Color32(252, 252, 250, 255));
        AddOutline(head.gameObject, ColorText(), new Vector2(1, -1));
        var body = CreateImage(art, "Sketch Body", ColorText());
        SetTopLeft(body, width * 0.5f - 1f, 46, 2, 36);
        var arm = CreateImage(art, "Sketch Arm", ColorText());
        SetTopLeft(arm, width * 0.5f - 26f, 58, 52, 2);
        arm.localEulerAngles = new Vector3(0, 0, -14f);
        var legA = CreateImage(art, "Sketch Leg A", ColorText());
        SetTopLeft(legA, width * 0.5f - 14f, 80, 2, 24);
        legA.localEulerAngles = new Vector3(0, 0, 18f);
        var legB = CreateImage(art, "Sketch Leg B", ColorText());
        SetTopLeft(legB, width * 0.5f + 12f, 80, 2, 24);
        legB.localEulerAngles = new Vector3(0, 0, -18f);
    }

    private RectTransform Zone(string title, float x, float y, float width, float height)
    {
        var zone = CreatePanel(table, title.Length == 0 ? "Zone" : title, x, y, width, height, new Color32(252, 252, 250, 245));
        AddOutline(zone.gameObject, ColorText(), new Vector2(2, -2));
        zone.gameObject.AddComponent<RectMask2D>();
        if (!string.IsNullOrEmpty(title))
        {
            Label(zone, title, 16, -14, width - 32, 30, 22, TextAnchor.MiddleCenter, ColorText());
        }
        return zone;
    }

    private Image CreateStatBar(RectTransform parent, float x, float y, float width, float height, Color32 fillColor)
    {
        var frame = CreatePanel(parent, "Stat Bar", x, y, width, height, new Color32(248, 248, 248, 255));
        AddOutline(frame.gameObject, ColorText(), new Vector2(1, -1));

        var fill = CreateImage(frame, "Stat Bar Fill", fillColor);
        Stretch(fill);
        var image = fill.GetComponent<Image>();
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = (int)Image.OriginHorizontal.Left;
        image.fillAmount = 1f;
        image.raycastTarget = false;
        return image;
    }

    private Text Label(RectTransform parent, string text, float x, float y, float width, float height, int size, TextAnchor anchor, Color color)
    {
        var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(parent, false);
        var rect = textObject.GetComponent<RectTransform>();
        SetTopLeft(rect, x, TopDistance(y), width, height);
        var label = textObject.GetComponent<Text>();
        label.font = font;
        label.text = text;
        label.fontSize = size;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = Mathf.Max(8, size - 5);
        label.resizeTextMaxSize = size;
        label.alignment = anchor;
        label.color = color;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        rect.SetAsLastSibling();
        return label;
    }

    private void BringTextToFront(Transform root)
    {
        for (var i = 0; i < root.childCount; i++)
        {
            BringTextToFront(root.GetChild(i));
        }

        var labels = root.GetComponentsInChildren<Text>(false);
        foreach (var label in labels)
        {
            if (label != null && label.transform.parent == root)
            {
                label.transform.SetAsLastSibling();
            }
        }
    }

    private Button Button(RectTransform parent, string text, float x, float y, float width, float height, Action onClick, bool secondary = false)
    {
        var rect = CreatePanel(parent, text, x, y, width, height, secondary ? new Color32(244, 244, 242, 255) : new Color32(35, 35, 35, 255));
        AddOutline(rect.gameObject, ColorText(), new Vector2(2, -2));
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        button.onClick.AddListener(() => onClick());
        Label(rect, text, 0, 0, width, height, 16, TextAnchor.MiddleCenter, secondary ? ColorText() : new Color32(250, 250, 250, 255));
        return button;
    }

    private RectTransform CreatePanel(RectTransform parent, string name, float x, float y, float width, float height, Color color)
    {
        var rect = CreateImage(parent, name, color);
        SetTopLeft(rect, x, TopDistance(y), width, height);
        return rect;
    }

    private RectTransform CreateImage(RectTransform parent, string name, Color color)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(parent, false);
        var image = obj.GetComponent<Image>();
        image.color = color;
        return obj.GetComponent<RectTransform>();
    }

    private void ApplyTexture(RectTransform rect, Color32 low, Color32 high, Color32 accent)
    {
        var image = rect.GetComponent<Image>();
        if (image == null)
        {
            return;
        }

        image.sprite = CreatePanelSprite(low, high, accent, ResolveTextureSkin(rect.name));
        image.color = Color.white;
        image.type = Image.Type.Simple;
    }

    private void ApplyPortraitTexture(RectTransform rect, bool enemy)
    {
        var image = rect.GetComponent<Image>();
        if (image == null)
        {
            return;
        }

        image.sprite = CreatePortraitSprite(enemy);
        image.color = Color.white;
        image.type = Image.Type.Simple;
    }

    private string ResolveTextureSkin(string name)
    {
        if (ContainsName(name, "Backdrop"))
        {
            return "backdrop";
        }
        if (ContainsName(name, "Battle Arena") || ContainsName(name, "Arena Glow"))
        {
            return "arena";
        }
        if (ContainsName(name, "Deck Card"))
        {
            return "cardback";
        }
        if (ContainsName(name, "Cost") || ContainsName(name, "Intent Icon") || ContainsName(name, "Icon"))
        {
            return "sigil";
        }
        if (ContainsName(name, "END TURN") || ContainsName(name, "结束回合") || ContainsName(name, "重开") || ContainsName(name, "再来一局"))
        {
            return "button";
        }
        if (ContainsName(name, "Stat Bar"))
        {
            return "bar";
        }
        if (ContainsName(name, "斩击") || ContainsName(name, "守势") || ContainsName(name, "重击") || ContainsName(name, "急救") || ContainsName(name, "蓄能"))
        {
            return "card";
        }

        return "panel";
    }

    private static bool ContainsName(string source, string value)
    {
        return source != null && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private Sprite CreatePanelSprite(Color32 low, Color32 high, Color32 accent, string skin)
    {
        const int size = 96;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var u = x / (float)(size - 1);
                var v = y / (float)(size - 1);
                var edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                var centerX = Mathf.Abs(u - 0.5f);
                var centerY = Mathf.Abs(v - 0.5f);
                var noise = Mathf.Repeat(Mathf.Sin(x * 12.9898f + y * 78.233f + skin.Length * 15.13f) * 43758.5453f, 1f);
                var color = Color.Lerp(low, high, Mathf.Clamp01(v * 0.88f + noise * 0.08f));
                var vignette = Mathf.Clamp01((centerX + centerY) * 0.82f);
                color = Color.Lerp(color, Color.black, vignette * 0.30f);

                if (skin == "backdrop")
                {
                    var mist = Mathf.Sin((u + v) * 18f) * 0.5f + Mathf.Sin((u - v) * 31f) * 0.5f;
                    color = Color.Lerp(color, new Color32(5, 7, 8, 255), 0.38f + Mathf.Abs(mist) * 0.10f);
                    color = Color.Lerp(color, accent, Mathf.Clamp01(1f - Mathf.Abs(v - 0.55f) * 2.8f) * 0.05f);
                }
                else if (skin == "arena")
                {
                    var crack = Mathf.Abs(Mathf.Sin((u * 5.5f + v * 2.0f) * Mathf.PI));
                    var stoneJoint = Mathf.Min(Mathf.Abs(Mathf.Repeat(u * 6f, 1f) - 0.5f), Mathf.Abs(Mathf.Repeat(v * 4f, 1f) - 0.5f));
                    color = Color.Lerp(color, new Color32(20, 18, 17, 255), stoneJoint < 0.018f ? 0.38f : 0f);
                    color = Color.Lerp(color, new Color32(6, 6, 6, 255), crack < 0.035f && noise > 0.58f ? 0.45f : 0f);
                    color = Color.Lerp(color, accent, Mathf.Clamp01(1f - Mathf.Sqrt(centerX * centerX + centerY * centerY) * 2.2f) * 0.10f);
                }
                else if (skin == "cardback")
                {
                    color = Color.Lerp(new Color32(14, 16, 18, 255), new Color32(50, 36, 24, 255), v);
                    var diamond = Mathf.Abs(Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f) - 0.24f);
                    var cross = Mathf.Min(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f));
                    color = Color.Lerp(color, accent, diamond < 0.018f ? 0.58f : 0f);
                    color = Color.Lerp(color, accent, cross < 0.012f && Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f) < 0.35f ? 0.35f : 0f);
                    color = Color.Lerp(color, Color.black, edge < 6f ? 0.25f : 0f);
                }
                else if (skin == "slot")
                {
                    color = Color.Lerp(color, Color.black, 0.36f);
                    var diamond = Mathf.Abs(Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f) - 0.22f);
                    color.a *= 0.70f;
                    color = Color.Lerp(color, accent, diamond < 0.020f ? 0.34f : 0f);
                }
                else if (skin == "button")
                {
                    color = Color.Lerp(new Color32(58, 34, 8, 255), new Color32(202, 136, 34, 255), Mathf.Clamp01(1f - centerY * 1.6f));
                    color = Color.Lerp(color, new Color32(255, 214, 96, 255), Mathf.Clamp01(1f - Mathf.Abs(v - 0.54f) * 4f) * 0.14f);
                }
                else if (skin == "card")
                {
                    color = Color.Lerp(new Color32(17, 19, 20, 255), new Color32(52, 38, 27, 255), v);
                    color = Color.Lerp(color, accent, Mathf.Clamp01(1f - Mathf.Abs(v - 0.22f) * 6f) * 0.15f);
                    var lowerFade = Mathf.Clamp01((0.42f - v) * 2.4f);
                    color = Color.Lerp(color, Color.black, lowerFade * 0.20f);
                }
                else if (skin == "bar")
                {
                    color = Color.Lerp(color, accent, 0.42f + Mathf.Clamp01(1f - centerY * 2.4f) * 0.20f);
                }

                if (skin != "backdrop" && skin != "arena")
                {
                    if (edge < 2f)
                    {
                        color = Color.Lerp(color, Color.black, 0.70f);
                    }
                    else if (edge < 5f)
                    {
                        color = Color.Lerp(color, accent, skin == "slot" ? 0.28f : 0.62f);
                    }
                    else if (edge < 8f)
                    {
                        color = Color.Lerp(color, new Color32(255, 225, 154, 255), 0.12f);
                    }

                    var corner = (u < 0.12f || u > 0.88f) && (v < 0.12f || v > 0.88f);
                    if (corner && Mathf.Abs(Mathf.Abs(u - 0.5f) - Mathf.Abs(v - 0.5f)) < 0.025f)
                    {
                        color = Color.Lerp(color, accent, 0.42f);
                    }
                }

                if (noise > 0.86f && skin != "bar")
                {
                    color = Color.Lerp(color, Color.white, 0.035f);
                }

                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private Sprite CreateImpactSprite(string effectKind, bool ring)
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var p = new Vector2(x, y) - center;
                var radius = p.magnitude / (size * 0.5f);
                var angle = Mathf.Atan2(p.y, p.x);
                var color = AttackColor(effectKind, 1f);

                if (ring)
                {
                    var band = 1f - Mathf.Clamp01(Mathf.Abs(radius - 0.46f) * 28f);
                    var inner = 1f - Mathf.Clamp01(Mathf.Abs(radius - 0.28f) * 22f);
                    var outer = 1f - Mathf.Clamp01(Mathf.Abs(radius - 0.64f) * 18f);
                    var spokes = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * AttackSpokeCount(effectKind))), 16f) * 0.42f;
                    color.a = Mathf.Clamp01((band + inner * 0.34f + outer * 0.22f + spokes * Mathf.Clamp01(1f - radius)) * 0.82f);
                }
                else
                {
                    color.a = AttackMask(effectKind, radius, angle, p / (size * 0.5f));
                }

                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private Sprite CreateAttackTrailSprite()
    {
        const int width = 192;
        const int height = 40;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var u = x / (float)(width - 1);
                var v = Mathf.Abs(y / (float)(height - 1) - 0.5f) * 2f;
                var taper = Mathf.Sin(u * Mathf.PI);
                var body = Mathf.Clamp01(1f - v * (1.55f + u * 2.35f));
                var core = Mathf.Pow(body, 4.2f) * Mathf.Pow(taper, 0.42f);
                var blade = Mathf.Clamp01(1f - Mathf.Abs(v - (0.08f + u * 0.24f)) * 10f) * Mathf.Pow(1f - u, 0.28f);
                var backEdge = Mathf.Clamp01(1f - Mathf.Abs(v - (0.34f - u * 0.12f)) * 9f) * Mathf.Clamp01(1f - u * 1.25f);
                var head = Mathf.Clamp01(1f - Mathf.Abs(u - 0.88f) * 9.5f - v * 2.4f);
                var noise = Mathf.Repeat(Mathf.Sin(x * 23.11f + y * 71.57f) * 913.37f, 1f);
                var alpha = Mathf.Clamp01(core * 1.05f + blade * 0.42f + backEdge * 0.18f + head * 0.82f);
                alpha *= 0.78f + noise * 0.22f;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), height);
    }

    private Sprite CreateChargeGlowSprite()
    {
        const int size = 160;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var p = new Vector2(x, y) - center;
                var radius = p.magnitude / (size * 0.5f);
                var angle = Mathf.Atan2(p.y, p.x);
                var outer = 1f - Mathf.Clamp01(Mathf.Abs(radius - 0.54f) * 22f);
                var inner = 1f - Mathf.Clamp01(Mathf.Abs(radius - 0.30f) * 34f);
                var glyph = Mathf.Pow(Mathf.Abs(Mathf.Sin(angle * 4f)), 18f) * Mathf.Clamp01(1f - radius * 1.12f);
                var core = Mathf.Pow(Mathf.Clamp01(1f - radius * 2.8f), 1.8f) * 0.40f;
                var alpha = Mathf.Clamp01(outer * 0.72f + inner * 0.40f + glyph * 0.36f + core);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private Sprite CreateSparkSprite()
    {
        var externalSpark = Resources.Load<Texture2D>("CardBattle/VFX/spark_sprite_strip9_0");
        if (externalSpark != null)
        {
            externalSpark.filterMode = FilterMode.Bilinear;
            var frameSize = Mathf.Max(1, externalSpark.height);
            var frameCount = Mathf.Max(1, externalSpark.width / frameSize);
            var frame = Mathf.Clamp(frameCount / 2, 0, frameCount - 1);
            return Sprite.Create(
                externalSpark,
                new Rect(frame * frameSize, 0, frameSize, externalSpark.height),
                new Vector2(0.5f, 0.5f),
                frameSize);
        }

        const int width = 64;
        const int height = 18;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var u = Mathf.Abs(x / (float)(width - 1) - 0.5f) * 2f;
                var v = Mathf.Abs(y / (float)(height - 1) - 0.5f) * 2f;
                var ray = Mathf.Clamp01(1f - u * 0.95f - v * 2.9f);
                var core = Mathf.Clamp01(1f - u * 2.4f - v * 1.4f);
                var alpha = Mathf.Clamp01(ray * 0.82f + core * 0.55f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), height);
    }

    private Sprite CreateImpactFlashSprite()
    {
        const int size = 160;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var p = new Vector2(x, y) - center;
                var radius = p.magnitude / (size * 0.5f);
                var angle = Mathf.Atan2(p.y, p.x);
                var core = Mathf.Pow(Mathf.Clamp01(1f - radius * 2.8f), 2.2f);
                var cross = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2f)), 20f) * Mathf.Clamp01(1f - radius * 0.95f);
                var star = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 5f)), 16f) * Mathf.Clamp01(1f - radius * 1.18f);
                var ring = Mathf.Clamp01(1f - Mathf.Abs(radius - 0.34f) * 18f) * 0.42f;
                var alpha = Mathf.Clamp01(core + cross * 0.72f + star * 0.38f + ring);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private Sprite CreateDissolveSprite(Color32 tint)
    {
        const int size = 96;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave
        };

        var tintColor = (Color)tint;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var u = x / (float)(size - 1);
                var v = y / (float)(size - 1);
                var noise = Mathf.Repeat(Mathf.Sin(x * 37.17f + y * 91.73f) * 415.926f, 1f);
                var drift = Mathf.Clamp01((u + v) * 0.42f);
                var fleck = noise > 0.70f ? Mathf.Clamp01((noise - 0.70f) * 3.3f) : 0f;
                var edgeAsh = Mathf.Clamp01((Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f) - 0.42f) * 5f);
                var alpha = Mathf.Clamp01(fleck * (0.55f + drift) + edgeAsh * 0.35f);
                var color = Color.Lerp(new Color(1f, 0.82f, 0.38f, alpha), tintColor, 0.34f);
                color.a = alpha;
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private Sprite CreateRippleSprite()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var radius = (new Vector2(x, y) - center).magnitude / (size * 0.5f);
                var outer = 1f - Mathf.Clamp01(Mathf.Abs(radius - 0.50f) * 34f);
                var inner = 1f - Mathf.Clamp01(Mathf.Abs(radius - 0.42f) * 42f);
                var alpha = Mathf.Clamp01(outer * 0.92f + inner * 0.34f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private Sprite CreateDissolveShardSprite(Color32 tint)
    {
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        var tintColor = (Color)tint;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var u = Mathf.Abs(x / (float)(size - 1) - 0.5f);
                var v = Mathf.Abs(y / (float)(size - 1) - 0.5f);
                var diamond = Mathf.Clamp01(1f - (u + v) * 2.1f);
                var color = Color.Lerp(new Color(1f, 0.92f, 0.48f, diamond), tintColor, 0.42f);
                color.a = diamond;
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private Sprite CreatePortraitSprite(bool enemy)
    {
        const int size = 160;
        Color low = enemy ? (Color)new Color32(44, 18, 30, 255) : (Color)new Color32(22, 45, 56, 255);
        Color high = enemy ? (Color)new Color32(130, 52, 54, 255) : (Color)new Color32(70, 128, 142, 255);
        Color accent = enemy ? (Color)Gold() : (Color)Blue();
        Color body = enemy ? (Color)new Color32(150, 56, 72, 255) : (Color)new Color32(82, 166, 188, 255);
        Color skin = enemy ? (Color)new Color32(245, 190, 96, 255) : (Color)new Color32(238, 194, 154, 255);
        Color dark = enemy ? (Color)new Color32(34, 10, 18, 255) : (Color)new Color32(17, 28, 35, 255);
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var u = x / (float)(size - 1);
                var v = y / (float)(size - 1);
                var color = Color.Lerp(low, high, v);
                var grain = Mathf.Repeat(Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f, 1f) - 0.5f;
                color = Color.Lerp(color, Color.white, grain * 0.035f);

                var glow = 1f - Mathf.Clamp01(Mathf.Sqrt(Mathf.Pow((u - 0.50f) / 0.38f, 2f) + Mathf.Pow((v - 0.55f) / 0.44f, 2f)));
                color = Color.Lerp(color, accent, glow * (enemy ? 0.25f : 0.20f));

                var cloakWidth = enemy ? 0.17f + Mathf.Clamp01(0.58f - v) * 0.38f : 0.15f + Mathf.Clamp01(0.54f - v) * 0.30f;
                var inBody = v > 0.13f && v < 0.55f && Mathf.Abs(u - 0.50f) < cloakWidth;
                if (inBody)
                {
                    var shade = Mathf.Abs(u - 0.50f) / Mathf.Max(0.01f, cloakWidth);
                    color = Color.Lerp(body, dark, shade * 0.35f);
                }

                var head = Mathf.Pow((u - 0.50f) / 0.12f, 2f) + Mathf.Pow((v - 0.68f) / 0.13f, 2f);
                if (head < 1f)
                {
                    color = Color.Lerp(skin, accent, enemy ? 0.24f : 0.04f);
                }

                var shoulder = Mathf.Pow((u - 0.50f) / 0.29f, 2f) + Mathf.Pow((v - 0.45f) / 0.12f, 2f);
                if (shoulder < 1f && v < 0.49f)
                {
                    color = Color.Lerp(color, body, 0.70f);
                }

                if (!enemy)
                {
                    var blade = Mathf.Abs(v - (0.22f + (u - 0.57f) * 1.55f));
                    if (u > 0.57f && u < 0.81f && v > 0.20f && v < 0.84f && blade < 0.018f)
                    {
                        color = Color.Lerp(color, new Color32(214, 240, 246, 255), 0.90f);
                    }
                }
                else
                {
                    var leftHorn = Mathf.Abs(v - (0.78f + (0.38f - u) * 0.42f));
                    var rightHorn = Mathf.Abs(v - (0.78f + (u - 0.62f) * 0.42f));
                    if ((u > 0.20f && u < 0.39f && leftHorn < 0.026f) || (u > 0.61f && u < 0.80f && rightHorn < 0.026f))
                    {
                        color = Color.Lerp(color, new Color32(252, 214, 116, 255), 0.88f);
                    }
                }

                if (x < 3 || y < 3 || x > size - 4 || y > size - 4)
                {
                    color = Color.Lerp(color, accent, 0.50f);
                }

                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private RectTransform CreateEmpty(RectTransform parent, string name)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj.GetComponent<RectTransform>();
    }

    private void AddSmallFrame(RectTransform rect)
    {
        AddOutline(rect.gameObject, new Color32(255, 255, 255, 24), new Vector2(1, -1));
    }

    private void AddOutline(GameObject obj, Color color, Vector2 distance)
    {
        var outline = obj.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = distance;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetTopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static float TopDistance(float y)
    {
        return y < 0 ? -y : y;
    }

    private void Log(string message)
    {
        logs.Insert(0, message);
        if (logs.Count > 6)
        {
            logs.RemoveAt(logs.Count - 1);
        }

        logText.text = string.Join("\n", logs);
    }

    private void DamageEnemy(int amount, string source)
    {
        if (weakTurns > 0)
        {
            amount = Mathf.Max(0, amount - 2);
            weakTurns = 0;
            Log("虚弱生效：本次我方攻击伤害 -2。");
        }

        var blocked = Mathf.Min(amount, enemyBlock);
        var damage = amount - blocked;
        enemyBlock -= blocked;
        enemyHp = Mathf.Clamp(enemyHp - damage, 0, MaxEnemyHp);
        enemyActionText.text = source + "命中：" + damage;
        Log(source + "造成 " + amount + " 点伤害，敌方护甲抵消 " + blocked + " 点。");
        StartCoroutine(AttackFlash(false, damage > 0 ? "-" + damage : "格挡", enemyActiveCard, AttackEffectForSource(source, false)));
        CheckEnd();
    }

    private void DamagePlayer(int amount, string source)
    {
        if (vulnerableTurns > 0)
        {
            amount += 2;
            vulnerableTurns = 0;
            Log("易伤生效：本次我方受到伤害 +2。");
        }

        var blocked = Mathf.Min(amount, playerBlock);
        var damage = amount - blocked;
        playerBlock -= blocked;
        playerHp = Mathf.Clamp(playerHp - damage, 0, MaxPlayerHp);
        enemyActionText.text = "我方受到 " + damage + " 点伤害";
        Log(source + "造成 " + amount + " 点伤害，我方护甲抵消 " + blocked + " 点。");
        StartCoroutine(AttackFlash(true, damage > 0 ? "-" + damage : "格挡", activeHeroCard, AttackEffectForSource(source, true)));
        CheckEnd();
    }

    private void GainBlock(int amount)
    {
        playerBlock += amount;
        Log("我方获得 " + amount + " 点护甲。");
        StartCoroutine(SupportFlash("护甲 +" + amount, activeHeroCard));
    }

    private void Heal(int amount)
    {
        var before = playerHp;
        playerHp = Mathf.Clamp(playerHp + amount, 0, MaxPlayerHp);
        Log("我方回复 " + (playerHp - before) + " 点生命。");
        StartCoroutine(SupportFlash("回复 +" + (playerHp - before), activeHeroCard));
    }

    private void GainEnergy(int amount)
    {
        energy += amount;
        Log("我方获得 " + amount + " 点费用。");
        StartCoroutine(SupportFlash("能量 +" + amount, activeHeroCard));
    }

    private void ApplyWeak(int turns)
    {
        weakTurns = Mathf.Max(weakTurns, turns);
        Log("敌方给我方挂上虚弱：下次攻击伤害 -2。");
    }

    private void ApplyVulnerable(int turns)
    {
        vulnerableTurns = Mathf.Max(vulnerableTurns, turns);
        Log("敌方给我方挂上易伤：下次受到伤害 +2。");
    }

    private bool CanPlay(CardData card)
    {
        return playerTurn && scrollOpen && !gameOver && !inputLocked && energy >= card.Cost && !discardedCards.Contains(card.Id) && !(card.Once && usedOnce.Contains(card.Id));
    }

    private void PlayCard(CardData card)
    {
        if (card == null)
        {
            return;
        }

        StartCoroutine(PlayCardRoutine(card));
    }

    private IEnumerator PlayCardRoutine(CardData card)
    {
        inputLocked = true;
        energy -= card.Cost;
        if (card.Once)
        {
            usedOnce.Add(card.Id);
        }

        Log("打出「" + card.Name + "」，消耗 " + card.Cost + " 点费用。");
        Render();

        yield return CardPunch(card.Id);
        card.Play();
        if (IsAttackCard(card))
        {
            yield return new WaitForSecondsRealtime(0.64f);
        }
        else
        {
            yield return new WaitForSecondsRealtime(0.16f);
        }

        yield return DiscardCard(card.Id);
        discardedCards.Add(card.Id);
        inputLocked = false;
        Render();
    }

    private void EndTurn()
    {
        if (!playerTurn || gameOver || inputLocked)
        {
            return;
        }

        StartCoroutine(EndTurnRoutine());
    }

    private IEnumerator EndTurnRoutine()
    {
        inputLocked = true;
        playerTurn = false;
        CloseBambooScrollImmediate();
        Render();
        yield return MoveEndTurnLeverToEnemyTurn();

        enemyBlock = 0;
        var intent = intents[intentIndex];
        Log("我方结束回合，敌方执行「" + intent.Name + "」。");
        intent.Run();
        Render();

        yield return new WaitForSecondsRealtime(intent.Icon == "防" ? 0.42f : 0.82f);

        if (!gameOver)
        {
            intentIndex = (intentIndex + 1) % intents.Length;
            round += 1;
            playerTurn = true;
            energy = BaseEnergy;
            playerBlock = 0;
            usedOnce.Clear();
            discardedCards.Clear();
            Log("第 " + round + " 回合开始，费用重置为 " + BaseEnergy + "，我方护甲清零。");
            Render();
            yield return MoveEndTurnLeverToPlayerTurn();
            yield return DrawHandFromDeck();
        }

        inputLocked = false;
        Render();
    }

    private IEnumerator MoveEndTurnLeverToEnemyTurn()
    {
        yield return PlayEndTurnLeverModelAnimation(true);
        SampleEndTurnLeverAnimation(true);
    }

    private IEnumerator MoveEndTurnLeverToPlayerTurn()
    {
        yield return PlayEndTurnLeverModelAnimation(false);
        SampleEndTurnLeverAnimation(false);
    }

    private IEnumerator PlayEndTurnLeverModelAnimation(bool toEnemy)
    {
        if (endTurnLeverAnimation == null || endTurnLeverClip == null)
        {
            yield break;
        }

        var state = endTurnLeverAnimation[endTurnLeverClip.name];
        if (state == null)
        {
            yield break;
        }

        var fromTime = toEnemy ? LeverFrameToClipTime(state, LeverPlayerFrame) : LeverFrameToClipTime(state, LeverEnemyFrame);
        var toTime = toEnemy ? LeverFrameToClipTime(state, LeverEnemyFrame) : LeverFrameToClipTime(state, LeverReturnFrame);
        var playTime = LeverSwitchDuration;
        state.enabled = true;
        state.weight = 1f;
        state.wrapMode = WrapMode.ClampForever;
        state.time = fromTime;
        state.speed = (toTime - fromTime) / playTime;
        endTurnLeverAnimation.Play(endTurnLeverClip.name);

        for (var t = 0f; t < playTime; t += Time.unscaledDeltaTime)
        {
            if (endTurnLeverCamera != null)
            {
                endTurnLeverCamera.Render();
            }
            yield return null;
        }

        state.speed = 0f;
        state.time = toTime;
        endTurnLeverAnimation.Sample();
        endTurnLeverAnimation.Stop();
        state.enabled = true;
        endTurnLeverAnimation.Sample();

        if (endTurnLeverCamera != null)
        {
            endTurnLeverCamera.Render();
        }
    }

    private void SampleEndTurnLeverAnimation(bool toEnemy)
    {
        if (endTurnLeverAnimation == null || endTurnLeverClip == null)
        {
            return;
        }

        var state = endTurnLeverAnimation[endTurnLeverClip.name];
        if (state == null)
        {
            return;
        }

        state.enabled = true;
        state.weight = 1f;
        state.wrapMode = WrapMode.ClampForever;
        state.speed = 0f;
        state.time = LeverFrameToClipTime(state, toEnemy ? LeverEnemyFrame : LeverPlayerFrame);
        endTurnLeverAnimation.Sample();
        if (endTurnLeverCamera != null)
        {
            endTurnLeverCamera.Render();
        }
    }

    private float LeverFrameToClipTime(AnimationState state, float frame)
    {
        var normalizedFrame = Mathf.InverseLerp(LeverPlayerFrame, LeverReturnFrame, frame);
        return Mathf.Clamp01(normalizedFrame) * state.length;
    }

    private void CheckEnd()
    {
        if (enemyHp <= 0)
        {
            Finish(true);
        }
        else if (playerHp <= 0)
        {
            Finish(false);
        }
    }

    private void Finish(bool win)
    {
        gameOver = true;
        playerTurn = false;
        resultTitleText.text = win ? "胜利" : "失败";
        resultBodyText.text = win ? "敌方出战角色被击败。" : "我方角色生命归零。";
        resultOverlay.SetActive(true);
        Log(win ? "战斗胜利。" : "战斗失败。");
    }

    private void ResetGame()
    {
        round = 1;
        playerHp = MaxPlayerHp;
        playerBlock = 0;
        energy = BaseEnergy;
        enemyHp = MaxEnemyHp;
        enemyBlock = 0;
        intentIndex = 0;
        weakTurns = 0;
        vulnerableTurns = 0;
        playerTurn = true;
        gameOver = false;
        inputLocked = true;
        usedOnce.Clear();
        discardedCards.Clear();
        logs.Clear();
        StopAllCoroutines();
        draggingCard = null;
        isDraggingCard = false;
        scrollOpen = false;
        ResetHandToDrawPile();
        SampleEndTurnLeverAnimation(false);
        resultOverlay.SetActive(false);
        enemyActionText.text = "等待我方结束回合";
        Log("牌局开始：敌方意图已公开。");
        Render();
        StartCoroutine(DrawHandFromDeck());
    }

    private void Render()
    {
        if (energyText == null || playerCardHpText == null || playerCardBlockText == null || enemyHpText == null || debuffText == null)
        {
            return;
        }

        var intent = intents[intentIndex];
        energyText.text = "能量 " + energy;
        playerBlockText.text = "护甲 " + playerBlock;
        playerHpText.text = "生命 " + playerHp;
        playerCardHpText.text = "生命 " + playerHp;
        playerCardBlockText.text = "护甲 " + playerBlock;
        enemyHpText.text = "生命 " + enemyHp + "/60";
        enemyBlockText.text = "护甲 " + enemyBlock;
        if (playerHpFill != null)
        {
            playerHpFill.fillAmount = Mathf.Clamp01(playerHp / (float)MaxPlayerHp);
        }
        if (enemyHpFill != null)
        {
            enemyHpFill.fillAmount = Mathf.Clamp01(enemyHp / (float)MaxEnemyHp);
        }
        if (fieldDivider != null)
        {
            SetTopLeft(fieldDivider, 0, playerTurn && !gameOver ? 312 : 520, 1500, 3);
        }
        intentIconText.text = intent.Icon;
        intentIconText.transform.parent.GetComponent<Image>().color = intent.Tint;
        intentNameText.text = intent.Name;
        intentDescText.text = intent.Description;
        debuffText.text = BuildDebuffText();
        turnText.text = playerTurn && !gameOver ? "我方行动" : "敌方行动";
        statusBar.text = playerTurn && !gameOver
            ? "第 " + round + " 回合 | 出战：我方角色 | 生命 " + playerHp + " | 护甲 " + playerBlock
            : "第 " + round + " 回合 | 出战：敌方角色 | 生命 " + enemyHp + " | 护甲 " + enemyBlock;
        foreach (var view in cardViews)
        {
            var canPlay = CanPlay(view.Card);
            view.Button.interactable = canPlay;
            var cardImage = view.Root.GetComponent<Image>();
            if (cardImage != null)
            {
                cardImage.color = canPlay ? Color.white : new Color(0.62f, 0.62f, 0.62f, 0.82f);
            }
            if (discardedCards.Contains(view.Card.Id))
            {
                view.Note.text = "已弃牌";
            }
            else if (gameOver)
            {
                view.Note.text = "已结束";
            }
            else if (!playerTurn)
            {
                view.Note.text = "敌方行动中";
            }
            else if (view.Card.Once && usedOnce.Contains(view.Card.Id))
            {
                view.Note.text = "本回合已用";
            }
            else if (energy < view.Card.Cost)
            {
                view.Note.text = "费用不足";
            }
            else
            {
                view.Note.text = string.Empty;
            }
        }
    }

    private string BuildDebuffText()
    {
        if (weakTurns <= 0 && vulnerableTurns <= 0)
        {
            return "暂无负面状态";
        }

        var lines = new List<string>();
        if (weakTurns > 0)
        {
            lines.Add("虚弱：下次攻击伤害 -2");
        }
        if (vulnerableTurns > 0)
        {
            lines.Add("易伤：下次受击伤害 +2");
        }

        return string.Join("\n", lines.ToArray());
    }

    private IEnumerator AttackFlash(bool enemyAttacking, string text, RectTransform target, string effectKind)
    {
        var attacker = enemyAttacking ? enemyActiveCard : activeHeroCard;
        if (attacker == null || target == null)
        {
            yield break;
        }

        attacker.SetAsLastSibling();
        target.SetAsLastSibling();
        RaiseHandLayer();

        var attackerStart = attacker.anchoredPosition;
        var start = target.anchoredPosition;
        var attackerScale = attacker.localScale;
        var targetScale = target.localScale;
        var targetImage = target.GetComponent<Image>();
        var targetBaseColor = targetImage != null ? targetImage.color : Color.white;
        var fromCenter = CombatFocusPosition(attacker);
        var toCenter = CombatFocusPosition(target);
        var direction = (toCenter - fromCenter).normalized;
        if (direction.sqrMagnitude < 0.01f)
        {
            direction = enemyAttacking ? Vector2.down : Vector2.up;
        }
        var normal = new Vector2(-direction.y, direction.x);

        var windup = attackerStart - direction * AttackWindupDistance(effectKind) + normal * (enemyAttacking ? -8f : 8f);
        var hitPosition = AnchoredPositionForFocus(attacker, Vector2.Lerp(fromCenter, toCenter, AttackLungeReach(effectKind)));
        var lineImage = attackLine.GetComponent<Image>();
        var lineColor = AttackColor(effectKind, 1f);
        ConfigureAttackLine(fromCenter, toCenter, lineColor, 0f);
        SetCombatFlash(0f, lineColor);
        combatFlash.gameObject.SetActive(true);
        attackLine.gameObject.SetActive(true);
        PrepareChargeGlow(fromCenter, lineColor);
        PrepareAttackTrails(fromCenter, toCenter, lineColor);

        for (var t = 0f; t < 0.16f; t += Time.unscaledDeltaTime)
        {
            var p = EaseOut(t / 0.16f);
            attacker.anchoredPosition = Vector2.Lerp(attackerStart, windup, p) + normal * Mathf.Sin(p * Mathf.PI) * 5f;
            attacker.localScale = attackerScale;
            AnimateChargeGlow(CombatFocusPosition(attacker), lineColor, p, 0.72f);
            SetCombatFlash(p * 0.08f, lineColor);
            SetImageAlpha(lineImage, lineColor, p * 0.22f);
            AnimateAttackTrails(fromCenter, toCenter, lineColor, p * 0.22f, p * 0.12f);
            yield return null;
        }

        for (var t = 0f; t < 0.12f; t += Time.unscaledDeltaTime)
        {
            var p = Mathf.Clamp01(t / 0.12f);
            var q = EaseIn(p);
            attacker.anchoredPosition = Vector2.Lerp(windup, hitPosition, q) + normal * Mathf.Sin(p * Mathf.PI) * AttackSideSnap(effectKind);
            attacker.localScale = attackerScale;
            var liveFrom = CombatFocusPosition(attacker);
            AnimateChargeGlow(liveFrom, lineColor, 1f - p, 0.78f);
            SetCombatFlash(0.08f + p * 0.06f, lineColor);
            ConfigureAttackLine(liveFrom, toCenter, lineColor, Mathf.Lerp(0.32f, 1f, q));
            AnimateAttackTrails(liveFrom, toCenter, lineColor, Mathf.Lerp(0.46f, 1f, q), p);
            yield return null;
        }

        attacker.anchoredPosition = hitPosition;
        toCenter = CombatFocusPosition(target);
        PlaceImpactEffects(toCenter);
        impactFlash.sizeDelta = AttackFlashSize(effectKind);
        impactBurst.sizeDelta = AttackBurstSize(effectKind);
        impactRing.sizeDelta = AttackRingSize(effectKind);
        impactFlash.localScale = Vector3.one * 0.34f;
        impactBurst.localScale = Vector3.one * 0.46f;
        impactRing.localScale = Vector3.one * 0.20f;
        impactFlash.localEulerAngles = new Vector3(0, 0, AttackEffectRotation(effectKind, enemyAttacking) + 18f);
        impactBurst.localEulerAngles = new Vector3(0, 0, AttackEffectRotation(effectKind, enemyAttacking));
        impactRing.localEulerAngles = new Vector3(0, 0, -AttackEffectRotation(effectKind, enemyAttacking) * 0.35f);
        impactFlash.gameObject.SetActive(true);
        impactBurst.GetComponent<Image>().sprite = CreateImpactSprite(effectKind, false);
        impactRing.GetComponent<Image>().sprite = CreateImpactSprite(effectKind, true);
        impactBurst.gameObject.SetActive(true);
        impactRing.gameObject.SetActive(true);
        PrepareImpactSlashes(toCenter, direction, effectKind);
        for (var i = 0; i < impactRipples.Count; i++)
        {
            var ripple = impactRipples[i];
            ripple.sizeDelta = new Vector2(188f + i * 42f, 188f + i * 42f);
            ripple.localScale = Vector3.one * 0.08f;
            ripple.localEulerAngles = Vector3.zero;
            ripple.gameObject.SetActive(true);
            SetImageAlpha(ripple.GetComponent<Image>(), AttackRingColor(effectKind, 1f), 0f);
        }
        PrepareImpactSparks(toCenter, effectKind);
        RaiseCombatEffectLayer();
        target.localScale = targetScale;
        TriggerTableShake(direction, AttackShakeStrength(effectKind), 0.24f);
        TintHitTarget(targetImage, targetBaseColor, effectKind, 0.55f);

        floatingText.text = text;
        floatingText.gameObject.SetActive(true);
        floatingText.rectTransform.anchoredPosition = FloatingTextAnchorForCenter(toCenter + new Vector2(0f, 46f));
        RaiseCombatEffectLayer();

        var pushDistance = AttackTargetPush(effectKind);
        for (var t = 0f; t < 0.055f; t += Time.unscaledDeltaTime)
        {
            var p = Mathf.Clamp01(t / 0.055f);
            target.anchoredPosition = start + direction * pushDistance + normal * Mathf.Sin(p * Mathf.PI * 3f) * 4f;
            var liveImpactCenter = CombatFocusPosition(target);
            PlaceImpactEffects(liveImpactCenter);
            SetImageAlpha(impactFlash.GetComponent<Image>(), Color.white, 0.95f);
            SetImageAlpha(impactBurst.GetComponent<Image>(), AttackColor(effectKind, 1f), 1f);
            SetImageAlpha(impactRing.GetComponent<Image>(), AttackRingColor(effectKind, 1f), 0.92f);
            SetImageAlpha(lineImage, lineColor, 0.95f);
            SetCombatFlash(0.20f, lineColor);
            AnimateChargeGlow(liveImpactCenter, lineColor, 1f - p, 0.55f);
            AnimateImpactSlashes(effectKind, liveImpactCenter, direction, p * 0.22f);
            AnimateAttackTrails(CombatFocusPosition(attacker), liveImpactCenter, lineColor, 1f, 1.10f + p * 0.18f);
            yield return null;
        }

        yield return new WaitForSecondsRealtime(0.050f);

        for (var t = 0f; t < 0.48f; t += Time.unscaledDeltaTime)
        {
            var p = Mathf.Clamp01(t / 0.48f);
            var recoil = (1f - EaseOut(p)) * pushDistance;
            var jitter = Mathf.Sin(p * Mathf.PI * 12f) * (1f - p) * 4f;
            var alpha = Mathf.Pow(1f - p, 0.62f);
            target.anchoredPosition = start + direction * recoil + normal * jitter;
            TintHitTarget(targetImage, targetBaseColor, effectKind, Mathf.Pow(1f - p, 2.0f) * 0.46f);
            var liveImpactCenter = CombatFocusPosition(target);
            PlaceImpactEffects(liveImpactCenter);
            SetImageAlpha(impactFlash.GetComponent<Image>(), Color.white, Mathf.Pow(1f - p, 2.2f) * 0.95f);
            SetImageAlpha(impactBurst.GetComponent<Image>(), AttackColor(effectKind, 1f), alpha);
            SetImageAlpha(impactRing.GetComponent<Image>(), AttackRingColor(effectKind, 1f), alpha * 0.88f);
            SetImageAlpha(lineImage, lineColor, alpha * 0.70f);
            SetCombatFlash(Mathf.Pow(1f - p, 1.7f) * 0.18f, lineColor);
            AnimateChargeGlow(liveImpactCenter, lineColor, 1f - p, 0.45f);
            AnimateImpactSlashes(effectKind, liveImpactCenter, direction, p);
            AnimateAttackTrails(fromCenter, liveImpactCenter, lineColor, alpha * 0.74f, 1.08f + p * 0.92f);
            impactFlash.localScale = Vector3.one * Mathf.Lerp(0.34f, 1.75f, EaseOut(p));
            impactBurst.localScale = Vector3.one * Mathf.Lerp(0.46f, 1.42f, EaseOut(p));
            impactRing.localScale = Vector3.one * Mathf.Lerp(0.20f, 1.86f, EaseOut(p));
            impactFlash.localEulerAngles = new Vector3(0, 0, AttackEffectRotation(effectKind, enemyAttacking) + 18f + p * AttackSpin(effectKind) * 0.45f);
            impactBurst.localEulerAngles = new Vector3(0, 0, AttackEffectRotation(effectKind, enemyAttacking) + p * AttackSpin(effectKind));
            impactRing.localEulerAngles = new Vector3(0, 0, -p * AttackSpin(effectKind) * 0.55f);
            AnimateImpactRipples(effectKind, p);
            AnimateImpactSparks(effectKind, liveImpactCenter, p, direction);
            floatingText.color = new Color(1f, 0.86f, 0.45f, Mathf.Sin(Mathf.Clamp01(p * 1.08f) * Mathf.PI));
            floatingText.rectTransform.anchoredPosition = FloatingTextAnchorForCenter(liveImpactCenter + new Vector2(0f, 48f + p * 42f));
            yield return null;
        }

        var targetReturnStart = target.anchoredPosition;
        for (var t = 0f; t < 0.20f; t += Time.unscaledDeltaTime)
        {
            var p = EaseOut(t / 0.20f);
            attacker.anchoredPosition = Vector2.Lerp(hitPosition, attackerStart, p);
            attacker.localScale = attackerScale;
            target.anchoredPosition = Vector2.Lerp(targetReturnStart, start, p);
            target.localScale = targetScale;
            SetImageAlpha(lineImage, lineColor, 1f - p);
            SetCombatFlash((1f - p) * 0.04f, lineColor);
            AnimateAttackTrails(fromCenter, toCenter, lineColor, 1f - p, 1.72f + p * 0.42f);
            yield return null;
        }

        attacker.anchoredPosition = attackerStart;
        attacker.localScale = attackerScale;
        target.anchoredPosition = start;
        target.localScale = targetScale;
        if (targetImage != null)
        {
            targetImage.color = targetBaseColor;
        }
        attackLine.gameObject.SetActive(false);
        for (var i = 0; i < attackTrails.Count; i++)
        {
            attackTrails[i].gameObject.SetActive(false);
        }
        combatFlash.gameObject.SetActive(false);
        chargeGlow.gameObject.SetActive(false);
        impactFlash.gameObject.SetActive(false);
        impactBurst.gameObject.SetActive(false);
        impactRing.gameObject.SetActive(false);
        for (var i = 0; i < impactSlashes.Count; i++)
        {
            impactSlashes[i].gameObject.SetActive(false);
        }
        for (var i = 0; i < impactRipples.Count; i++)
        {
            impactRipples[i].gameObject.SetActive(false);
        }
        for (var i = 0; i < impactSparks.Count; i++)
        {
            impactSparks[i].gameObject.SetActive(false);
        }
        floatingText.gameObject.SetActive(false);
    }

    private void TriggerTableShake(Vector2 direction, float strength, float duration)
    {
        if (table == null)
        {
            return;
        }

        if (tableShakeRoutine != null)
        {
            StopCoroutine(tableShakeRoutine);
            table.anchoredPosition = Vector2.zero;
        }

        tableShakeRoutine = StartCoroutine(TableShake(direction, strength, duration));
    }

    private IEnumerator TableShake(Vector2 direction, float strength, float duration)
    {
        var normal = new Vector2(-direction.y, direction.x);
        for (var t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            var p = Mathf.Clamp01(t / duration);
            var decay = 1f - EaseOut(p);
            var punch = Mathf.Sin(p * Mathf.PI * 10f) * strength;
            var rattle = Mathf.Sin(p * Mathf.PI * 17f) * strength * 0.42f;
            table.anchoredPosition = direction * punch * decay + normal * rattle * decay;
            yield return null;
        }

        table.anchoredPosition = Vector2.zero;
        tableShakeRoutine = null;
    }

    private void TintHitTarget(Image targetImage, Color baseColor, string effectKind, float amount)
    {
        if (targetImage == null)
        {
            return;
        }

        var hitColor = Color.Lerp(Color.white, AttackRingColor(effectKind, 1f), 0.25f);
        var color = Color.Lerp(baseColor, hitColor, Mathf.Clamp01(amount));
        color.a = baseColor.a;
        targetImage.color = color;
    }

    private IEnumerator SupportFlash(string text, RectTransform target)
    {
        var start = target.anchoredPosition;
        var floatStart = FloatingTextPosition(target);
        floatingText.text = text;
        floatingText.gameObject.SetActive(true);

        for (var t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
        {
            var p = t / 0.5f;
            var alpha = Mathf.Sin(p * Mathf.PI);
            floatingText.color = new Color(0.65f, 0.95f, 0.6f, alpha);
            floatingText.rectTransform.anchoredPosition = floatStart + new Vector2(0, p * 35f);
            target.anchoredPosition = start + new Vector2(0, Mathf.Sin(p * Mathf.PI) * 5f);
            yield return null;
        }

        target.anchoredPosition = start;
        floatingText.gameObject.SetActive(false);
    }

    private Vector2 FloatingTextPosition(RectTransform target)
    {
        if (target != null)
        {
            return FloatingTextAnchorForCenter(CombatFocusPosition(target) + new Vector2(0f, 50f));
        }

        return Vector2.zero;
    }

    private Vector2 FloatingTextAnchorForCenter(Vector2 center)
    {
        if (floatingText == null)
        {
            return Vector2.zero;
        }

        var rect = floatingText.rectTransform;
        var size = rect.sizeDelta;
        return new Vector2(center.x + 750f - size.x * 0.5f, center.y - 450f + size.y * 0.5f);
    }

    private IEnumerator CardPunch(string cardId)
    {
        var view = cardViews.Find(item => item.Card.Id == cardId);
        if (view == null)
        {
            yield break;
        }

        var start = view.Root.anchoredPosition;
        var group = EnsureCanvasGroup(view.Root);
        group.alpha = 1f;
        view.Root.gameObject.SetActive(true);
        RaiseHandLayer();
        view.Root.SetAsLastSibling();
        for (var t = 0f; t < 0.20f; t += Time.unscaledDeltaTime)
        {
            var p = t / 0.20f;
            view.Root.anchoredPosition = start + new Vector2(0, Mathf.Sin(p * Mathf.PI) * 34f);
            yield return null;
        }
        view.Root.anchoredPosition = start;
        view.Root.localScale = Vector3.one;
    }

    private IEnumerator DiscardCard(string cardId)
    {
        var view = cardViews.Find(item => item.Card.Id == cardId);
        if (view == null)
        {
            yield break;
        }

        var group = EnsureCanvasGroup(view.Root);
        var start = view.Root.anchoredPosition;
        var dissolve = view.Root.Find("Card Dissolve") as RectTransform;
        var shards = EnsureDissolveShards(view);
        RaiseHandLayer();
        Image dissolveImage = null;
        if (dissolve != null)
        {
            dissolveImage = dissolve.GetComponent<Image>();
            AlignDissolveOverlay(view.Root, dissolve);
            SetImageAlpha(dissolveImage, Color.white, 0f);
            dissolve.gameObject.SetActive(true);
            dissolve.SetAsLastSibling();
        }

        for (var i = 0; i < shards.Count; i++)
        {
            var shard = shards[i];
            shard.gameObject.SetActive(true);
            shard.localScale = Vector3.one;
            shard.localEulerAngles = new Vector3(0, 0, i * 23f);
            shard.anchoredPosition = DissolveShardStart(i);
            SetImageAlpha(shard.GetComponent<Image>(), Color.white, 0f);
            shard.SetAsLastSibling();
        }
        view.Root.SetAsLastSibling();

        for (var t = 0f; t < 0.72f; t += Time.unscaledDeltaTime)
        {
            var raw = t / 0.72f;
            var shatter = Mathf.Clamp01(raw / 0.32f);
            view.Root.anchoredPosition = start;
            view.Root.localEulerAngles = Vector3.zero;
            group.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01((raw - 0.26f) / 0.74f));
            if (dissolve != null)
            {
                AlignDissolveOverlay(view.Root, dissolve);
                SetImageAlpha(dissolveImage, Color.white, Mathf.Sin(raw * Mathf.PI) * 0.82f);
            }
            for (var i = 0; i < shards.Count; i++)
            {
                var shard = shards[i];
                var dir = DissolveShardDirection(i);
                shard.anchoredPosition = DissolveShardStart(i) + dir * Mathf.Lerp(0f, 88f, shatter) + new Vector2(0, -raw * 18f);
                shard.localEulerAngles = new Vector3(0, 0, i * 23f + raw * 180f * (i % 2 == 0 ? 1f : -1f));
                SetImageAlpha(shard.GetComponent<Image>(), Color.white, Mathf.Sin(raw * Mathf.PI) * 0.95f);
            }
            yield return null;
        }

        group.alpha = 0f;
        if (dissolve != null)
        {
            dissolve.gameObject.SetActive(false);
            dissolve.localScale = Vector3.one;
            dissolve.localEulerAngles = Vector3.zero;
        }
        for (var i = 0; i < shards.Count; i++)
        {
            shards[i].gameObject.SetActive(false);
        }
        view.Root.localScale = Vector3.one;
        view.Root.localEulerAngles = Vector3.zero;
        view.Root.anchoredPosition = start;
        view.Root.gameObject.SetActive(false);
    }

    private IEnumerator DrawHandFromDeck()
    {
        inputLocked = true;
        CloseBambooScrollImmediate();
        foreach (var view in cardViews)
        {
            view.Root.gameObject.SetActive(true);
            view.Root.anchoredPosition = DrawPilePositionInHand();
            view.Root.localScale = Vector3.one * 0.42f;
            view.Root.localEulerAngles = Vector3.zero;
            EnsureCanvasGroup(view.Root).alpha = 0f;
        }

        for (var i = 0; i < cardViews.Count; i++)
        {
            yield return DrawSingleCard(cardViews[i], i * 0.035f);
        }

        inputLocked = false;
        Render();
    }

    private IEnumerator DrawSingleCard(CardView view, float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }

        var group = EnsureCanvasGroup(view.Root);
        var start = DrawPilePositionInHand();
        var end = BambooScrollPositionInHand();
        view.Root.SetAsLastSibling();
        PlaySound("draw", 0.52f, 0.98f + delay * 1.4f);
        for (var t = 0f; t < 0.34f; t += Time.unscaledDeltaTime)
        {
            var p = EaseOut(t / 0.34f);
            view.Root.anchoredPosition = Vector2.Lerp(start, end, p) + new Vector2(0, Mathf.Sin(p * Mathf.PI) * -30f);
            view.Root.localScale = Vector3.Lerp(Vector3.one * 0.42f, Vector3.one * 0.16f, p);
            group.alpha = Mathf.Sin(p * Mathf.PI);
            yield return null;
        }

        view.Root.anchoredPosition = end;
        view.Root.localScale = Vector3.one;
        group.alpha = 0f;
        view.Root.gameObject.SetActive(false);
    }

    private bool IsAttackCard(CardData card)
    {
        return card.Id == "slash" || card.Id == "heavy";
    }

    private string AttackEffectForSource(string source, bool enemyAttacking)
    {
        if (!string.IsNullOrEmpty(source))
        {
            if (source.IndexOf("裂焰", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "flame";
            }
            if (source.IndexOf("迅斩", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "slash";
            }
            if (source.IndexOf("强攻", StringComparison.OrdinalIgnoreCase) >= 0 || source.IndexOf("破阵", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "break";
            }
        }

        return enemyAttacking ? "pierce" : "slash";
    }

    private Color AttackColor(string effectKind, float alpha)
    {
        Color color;
        switch (effectKind)
        {
            case "flame":
                color = new Color(1f, 0.36f, 0.12f, alpha);
                break;
            case "pierce":
                color = new Color(1f, 0.18f, 0.16f, alpha);
                break;
            case "break":
                color = new Color(1f, 0.74f, 0.20f, alpha);
                break;
            default:
                color = new Color(0.28f, 0.82f, 1f, alpha);
                break;
        }

        return color;
    }

    private Color AttackRingColor(string effectKind, float alpha)
    {
        switch (effectKind)
        {
            case "flame":
                return new Color(1f, 0.78f, 0.22f, alpha);
            case "pierce":
                return new Color(1f, 0.48f, 0.38f, alpha);
            case "break":
                return new Color(1f, 0.92f, 0.42f, alpha);
            default:
                return new Color(0.66f, 0.95f, 1f, alpha);
        }
    }

    private Vector2 AttackBurstSize(string effectKind)
    {
        switch (effectKind)
        {
            case "flame":
                return new Vector2(154f, 154f);
            case "pierce":
                return new Vector2(172f, 92f);
            case "break":
                return new Vector2(168f, 168f);
            default:
                return new Vector2(166f, 112f);
        }
    }

    private Vector2 AttackFlashSize(string effectKind)
    {
        switch (effectKind)
        {
            case "flame":
                return new Vector2(210f, 210f);
            case "pierce":
                return new Vector2(238f, 132f);
            case "break":
                return new Vector2(230f, 230f);
            default:
                return new Vector2(220f, 156f);
        }
    }

    private float AttackWindupDistance(string effectKind)
    {
        switch (effectKind)
        {
            case "break":
                return 58f;
            case "pierce":
                return 44f;
            case "flame":
                return 50f;
            default:
                return 48f;
        }
    }

    private float AttackLungeReach(string effectKind)
    {
        switch (effectKind)
        {
            case "break":
                return 0.66f;
            case "pierce":
                return 0.70f;
            default:
                return 0.64f;
        }
    }

    private float AttackSideSnap(string effectKind)
    {
        switch (effectKind)
        {
            case "break":
                return 12f;
            case "flame":
                return 10f;
            default:
                return 8f;
        }
    }

    private float AttackTargetPush(string effectKind)
    {
        switch (effectKind)
        {
            case "break":
                return 28f;
            case "pierce":
                return 22f;
            case "flame":
                return 24f;
            default:
                return 20f;
        }
    }

    private float AttackShakeStrength(string effectKind)
    {
        switch (effectKind)
        {
            case "break":
                return 11f;
            case "flame":
                return 9f;
            case "pierce":
                return 8f;
            default:
                return 7f;
        }
    }

    private Vector2 AttackRingSize(string effectKind)
    {
        switch (effectKind)
        {
            case "pierce":
                return new Vector2(190f, 108f);
            case "break":
                return new Vector2(188f, 188f);
            default:
                return new Vector2(166f, 166f);
        }
    }

    private float AttackEffectRotation(string effectKind, bool enemyAttacking)
    {
        if (effectKind == "flame")
        {
            return 18f;
        }
        if (effectKind == "pierce")
        {
            return enemyAttacking ? -90f : 90f;
        }
        if (effectKind == "break")
        {
            return -20f;
        }

        return enemyAttacking ? -38f : 32f;
    }

    private float AttackSpin(string effectKind)
    {
        switch (effectKind)
        {
            case "flame":
                return 62f;
            case "break":
                return -36f;
            case "pierce":
                return 10f;
            default:
                return 24f;
        }
    }

    private float AttackSpokeCount(string effectKind)
    {
        switch (effectKind)
        {
            case "flame":
                return 7f;
            case "pierce":
                return 2f;
            case "break":
                return 6f;
            default:
                return 4f;
        }
    }

    private float AttackMask(string effectKind, float radius, float angle, Vector2 p)
    {
        var core = Mathf.Clamp01(1f - radius * 3.0f);
        if (effectKind == "flame")
        {
            var plume = Mathf.Pow(Mathf.Abs(Mathf.Sin(angle * 3f + radius * 4f)), 2f);
            return Mathf.Clamp01((core * 0.9f + plume * 0.72f) * Mathf.Clamp01(1f - radius * 0.78f));
        }
        if (effectKind == "pierce")
        {
            var spear = Mathf.Clamp01(1f - Mathf.Abs(p.y) * 13f) * Mathf.Clamp01(1f - Mathf.Abs(p.x) * 0.74f);
            var tip = Mathf.Clamp01(1f - Mathf.Abs(p.x - 0.36f) * 5f - Mathf.Abs(p.y) * 3.8f);
            return Mathf.Clamp01(spear + tip + core * 0.35f);
        }
        if (effectKind == "break")
        {
            var cracks = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 6f)), 18f) * Mathf.Clamp01(1f - radius);
            var ring = Mathf.Clamp01(1f - Mathf.Abs(radius - 0.30f) * 11f);
            return Mathf.Clamp01(core + cracks * 0.95f + ring * 0.45f);
        }

        var arc = Mathf.Clamp01(1f - Mathf.Abs(radius - (0.34f + Mathf.Sin(angle + 0.4f) * 0.09f)) * 17f);
        var sweep = Mathf.Clamp01(1f - Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, 8f)) / 118f);
        return Mathf.Clamp01(core * 0.44f + arc * sweep);
    }

    private void ResetHandToDrawPile()
    {
        CloseBambooScrollImmediate();
        foreach (var view in cardViews)
        {
            view.Root.gameObject.SetActive(true);
            view.Root.anchoredPosition = DrawPilePositionInHand();
            view.Root.localScale = Vector3.one * 0.42f;
            view.Root.localEulerAngles = Vector3.zero;
            EnsureCanvasGroup(view.Root).alpha = 0f;
            ResetDissolveVisuals(view.Root);
        }
    }

    private void SetCombatFlash(float alpha, Color tint)
    {
        if (combatFlash == null)
        {
            return;
        }

        var image = combatFlash.GetComponent<Image>();
        var color = Color.Lerp(new Color(0.02f, 0.02f, 0.03f, 1f), tint, 0.20f);
        color.a = Mathf.Clamp01(alpha);
        image.color = color;
    }

    private void PrepareChargeGlow(Vector2 center, Color color)
    {
        if (chargeGlow == null)
        {
            return;
        }

        chargeGlow.anchoredPosition = center;
        chargeGlow.localScale = Vector3.one * 0.62f;
        chargeGlow.localEulerAngles = Vector3.zero;
        chargeGlow.gameObject.SetActive(true);
        SetImageAlpha(chargeGlow.GetComponent<Image>(), Color.Lerp(Color.white, color, 0.62f), 0f);
    }

    private void AnimateChargeGlow(Vector2 center, Color color, float progress, float maxAlpha)
    {
        if (chargeGlow == null)
        {
            return;
        }

        var p = Mathf.Clamp01(progress);
        chargeGlow.anchoredPosition = center;
        chargeGlow.localScale = Vector3.one * Mathf.Lerp(0.62f, 1.22f, EaseOut(p));
        chargeGlow.localEulerAngles = new Vector3(0, 0, p * 145f);
        SetImageAlpha(chargeGlow.GetComponent<Image>(), Color.Lerp(Color.white, color, 0.66f), Mathf.Sin(p * Mathf.PI) * maxAlpha);
    }

    private void PrepareImpactSlashes(Vector2 center, Vector2 direction, string effectKind)
    {
        for (var i = 0; i < impactSlashes.Count; i++)
        {
            var slash = impactSlashes[i];
            slash.gameObject.SetActive(true);
            slash.anchoredPosition = center;
            slash.localScale = Vector3.one * 0.55f;
            slash.sizeDelta = new Vector2(180f, 22f);
            slash.localEulerAngles = new Vector3(0, 0, ImpactSlashAngle(direction, i));
            SetImageAlpha(slash.GetComponent<Image>(), AttackColor(effectKind, 1f), 0f);
        }
    }

    private void AnimateImpactSlashes(string effectKind, Vector2 center, Vector2 direction, float progress)
    {
        var normal = new Vector2(-direction.y, direction.x);
        var p = Mathf.Clamp01(progress);
        for (var i = 0; i < impactSlashes.Count; i++)
        {
            var slash = impactSlashes[i];
            var delay = i * 0.055f;
            var local = Mathf.Clamp01((p - delay) / 0.58f);
            if (p < delay || local >= 1f)
            {
                SetImageAlpha(slash.GetComponent<Image>(), AttackColor(effectKind, 1f), 0f);
                continue;
            }

            var side = (i - (impactSlashes.Count - 1f) * 0.5f) * 14f;
            var lead = Mathf.Lerp(-22f, 30f + i * 7f, EaseOut(local));
            slash.anchoredPosition = center + direction * lead + normal * side;
            slash.sizeDelta = new Vector2(Mathf.Lerp(160f, 300f + i * 22f, EaseOut(local)), Mathf.Lerp(34f, 8f, local));
            slash.localScale = Vector3.one * Mathf.Lerp(0.72f, 1.28f, Mathf.Sin(local * Mathf.PI));
            slash.localEulerAngles = new Vector3(0, 0, ImpactSlashAngle(direction, i) + Mathf.Lerp(-7f, 5f, local));
            var color = Color.Lerp(Color.white, AttackColor(effectKind, 1f), 0.72f);
            SetImageAlpha(slash.GetComponent<Image>(), color, Mathf.Pow(1f - local, 0.85f) * 0.92f);
        }
    }

    private float ImpactSlashAngle(Vector2 direction, int index)
    {
        var baseAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        switch (index % 4)
        {
            case 0:
                return baseAngle + 26f;
            case 1:
                return baseAngle - 32f;
            case 2:
                return baseAngle + 78f;
            default:
                return baseAngle - 76f;
        }
    }

    private void AnimateImpactRipples(string effectKind, float progress)
    {
        for (var i = 0; i < impactRipples.Count; i++)
        {
            var ripple = impactRipples[i];
            var offset = i * 0.16f;
            var local = Mathf.Clamp01((progress - offset) / 0.68f);
            var visible = progress >= offset && local < 1f;
            if (!visible)
            {
                SetImageAlpha(ripple.GetComponent<Image>(), AttackRingColor(effectKind, 1f), 0f);
                continue;
            }

            var burst = EaseOut(local);
            ripple.localScale = Vector3.one * Mathf.Lerp(0.12f, 3.20f + i * 0.38f, burst);
            ripple.localEulerAngles = new Vector3(0, 0, local * (i % 2 == 0 ? 28f : -24f));
            var ringColor = Color.Lerp(Color.white, AttackRingColor(effectKind, 1f), 0.72f);
            SetImageAlpha(ripple.GetComponent<Image>(), ringColor, Mathf.Pow(1f - local, 1.18f) * 0.98f);
        }
    }

    private void PrepareAttackTrails(Vector2 from, Vector2 to, Color color)
    {
        for (var i = 0; i < attackTrails.Count; i++)
        {
            attackTrails[i].gameObject.SetActive(true);
            ConfigureAttackTrail(attackTrails[i], from, to, color, 0f, i, 0f);
        }
    }

    private void AnimateAttackTrails(Vector2 from, Vector2 to, Color color, float alpha, float progress)
    {
        ConfigureAttackLine(from, to, Color.Lerp(Color.white, color, 0.78f), alpha * 0.48f);
        for (var i = 0; i < attackTrails.Count; i++)
        {
            ConfigureAttackTrail(attackTrails[i], from, to, color, alpha, i, progress);
        }
    }

    private void ConfigureAttackTrail(RectTransform trail, Vector2 from, Vector2 to, Color color, float alpha, int index, float progress)
    {
        if (trail == null)
        {
            return;
        }

        var delta = to - from;
        var distance = Mathf.Max(1f, delta.magnitude);
        var direction = delta / distance;
        var normal = new Vector2(-direction.y, direction.x);
        var maxIndex = Mathf.Max(1f, attackTrails.Count - 1f);
        var layer = index / maxIndex;
        var wave = Mathf.Sin((progress * 2.65f + index * 0.31f) * Mathf.PI);
        var offset = (index - maxIndex * 0.5f) * 8.5f + wave * (10f + layer * 7f);
        var tail = Mathf.Clamp01(index * 0.012f + Mathf.Max(0f, progress - 1f) * 0.22f);
        var head = Mathf.Clamp01(0.86f + Mathf.Min(progress, 1f) * 0.20f - index * 0.012f);
        var trailFrom = Vector2.Lerp(from, to, tail) + normal * offset;
        var trailTo = Vector2.Lerp(from, to, head) + normal * offset * 0.36f;
        var localDelta = trailTo - trailFrom;
        var length = Mathf.Max(92f, localDelta.magnitude * (1.04f + layer * 0.22f));
        var thickness = Mathf.Lerp(46f, 7f, layer);
        var image = trail.GetComponent<Image>();
        var trailColor = Color.Lerp(Color.white, color, 0.48f + layer * 0.48f);
        var trailAlpha = alpha * Mathf.Lerp(1.0f, 0.18f, layer);

        trail.anchoredPosition = trailFrom + localDelta * 0.5f;
        trail.sizeDelta = new Vector2(length, thickness);
        trail.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(localDelta.y, localDelta.x) * Mathf.Rad2Deg + (index - maxIndex * 0.5f) * 2.4f);
        trail.localScale = Vector3.one;
        SetImageAlpha(image, trailColor, trailAlpha);
    }

    private void PlaceImpactEffects(Vector2 center)
    {
        if (impactFlash != null)
        {
            impactFlash.anchoredPosition = center;
        }

        if (impactBurst != null)
        {
            impactBurst.anchoredPosition = center;
        }

        if (impactRing != null)
        {
            impactRing.anchoredPosition = center;
        }

        for (var i = 0; i < impactRipples.Count; i++)
        {
            if (impactRipples[i] != null)
            {
                impactRipples[i].anchoredPosition = center;
            }
        }
    }

    private void PrepareImpactSparks(Vector2 center, string effectKind)
    {
        for (var i = 0; i < impactSparks.Count; i++)
        {
            var spark = impactSparks[i];
            spark.gameObject.SetActive(true);
            spark.anchoredPosition = center;
            spark.localScale = Vector3.one;
            spark.localEulerAngles = new Vector3(0, 0, i * 19f);
            SetImageAlpha(spark.GetComponent<Image>(), AttackRingColor(effectKind, 1f), 0f);
        }
    }

    private void AnimateImpactSparks(string effectKind, Vector2 center, float progress, Vector2 attackDirection)
    {
        var baseRotation = Mathf.Atan2(attackDirection.y, attackDirection.x) * Mathf.Rad2Deg;
        for (var i = 0; i < impactSparks.Count; i++)
        {
            var spark = impactSparks[i];
            var delay = (i % 8) * 0.018f;
            var local = Mathf.Clamp01((progress - delay) / 0.54f);
            if (progress < delay || local >= 1f)
            {
                SetImageAlpha(spark.GetComponent<Image>(), AttackRingColor(effectKind, 1f), 0f);
                continue;
            }

            var fan = ((i % 9) - 4f) * 13f;
            var scatter = (i / 9) * 47f + (i % 2 == 0 ? 0f : 180f);
            var angle = (baseRotation + fan + scatter) * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var side = new Vector2(-dir.y, dir.x);
            var distance = Mathf.Lerp(12f, 148f + (i % 5) * 18f, EaseOut(local));
            var drift = Mathf.Sin(local * Mathf.PI * 2.4f + i) * (7f + i % 6);
            spark.anchoredPosition = center + dir * distance + side * drift;
            spark.sizeDelta = new Vector2(Mathf.Lerp(62f, 7f, local) * (1f + (i % 3) * 0.20f), Mathf.Lerp(14f, 2f, local));
            spark.localEulerAngles = new Vector3(0, 0, angle * Mathf.Rad2Deg);
            var color = Color.Lerp(Color.white, AttackRingColor(effectKind, 1f), 0.58f);
            SetImageAlpha(spark.GetComponent<Image>(), color, Mathf.Pow(1f - local, 1.12f) * 0.98f);
        }
    }

    private List<RectTransform> EnsureDissolveShards(CardView view)
    {
        var shards = new List<RectTransform>();
        for (var i = 0; i < view.Root.childCount; i++)
        {
            var child = view.Root.GetChild(i) as RectTransform;
            if (child != null && child.name == "Dissolve Shard")
            {
                shards.Add(child);
            }
        }

        while (shards.Count < 14)
        {
            var shard = CreateImage(view.Root, "Dissolve Shard", Color.white);
            shard.anchorMin = new Vector2(0, 1);
            shard.anchorMax = new Vector2(0, 1);
            shard.pivot = new Vector2(0.5f, 0.5f);
            shard.sizeDelta = new Vector2(10f + (shards.Count % 3) * 3f, 10f + (shards.Count % 4) * 2f);
            var image = shard.GetComponent<Image>();
            image.sprite = CreateDissolveShardSprite(view.Card.Tint);
            image.raycastTarget = false;
            shard.gameObject.SetActive(false);
            shards.Add(shard);
        }

        return shards;
    }

    private void ResetDissolveVisuals(RectTransform cardRoot)
    {
        var dissolve = cardRoot.Find("Card Dissolve") as RectTransform;
        if (dissolve != null)
        {
            AlignDissolveOverlay(cardRoot, dissolve);
            dissolve.gameObject.SetActive(false);
            dissolve.localScale = Vector3.one;
            dissolve.localEulerAngles = Vector3.zero;
            SetImageAlpha(dissolve.GetComponent<Image>(), Color.white, 0f);
        }

        for (var i = 0; i < cardRoot.childCount; i++)
        {
            var child = cardRoot.GetChild(i) as RectTransform;
            if (child != null && child.name == "Dissolve Shard")
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    private static void AlignDissolveOverlay(RectTransform cardRoot, RectTransform dissolve)
    {
        if (cardRoot == null || dissolve == null)
        {
            return;
        }

        var size = cardRoot.rect.size;
        if (size.x <= 0.1f || size.y <= 0.1f)
        {
            size = new Vector2(128f, 184f);
        }

        dissolve.anchorMin = new Vector2(0f, 1f);
        dissolve.anchorMax = new Vector2(0f, 1f);
        dissolve.pivot = new Vector2(0f, 1f);
        dissolve.anchoredPosition = Vector2.zero;
        dissolve.sizeDelta = size;
        dissolve.localScale = Vector3.one;
        dissolve.localEulerAngles = Vector3.zero;
    }

    private Vector2 DissolveShardStart(int index)
    {
        var column = index % 4;
        var row = index / 4;
        return new Vector2(24f + column * 26f + (row % 2) * 7f, -34f - row * 34f);
    }

    private Vector2 DissolveShardDirection(int index)
    {
        var angle = (index * 137.5f + 24f) * Mathf.Deg2Rad;
        var strength = 0.72f + (index % 5) * 0.12f;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.75f) * strength;
    }

    private void RaiseHandLayer()
    {
        if (scrollSheet != null)
        {
            scrollSheet.SetAsLastSibling();
        }
        if (scrollLeftRoll != null)
        {
            scrollLeftRoll.SetAsLastSibling();
        }
        if (scrollRightRoll != null)
        {
            scrollRightRoll.SetAsLastSibling();
        }
        if (handRoot != null)
        {
            handRoot.SetAsLastSibling();
        }
    }

    private void RaiseCombatEffectLayer()
    {
        if (combatFlash != null)
        {
            combatFlash.SetAsLastSibling();
        }
        if (attackLine != null)
        {
            attackLine.SetAsLastSibling();
        }
        for (var i = 0; i < attackTrails.Count; i++)
        {
            if (attackTrails[i] != null)
            {
                attackTrails[i].SetAsLastSibling();
            }
        }
        if (chargeGlow != null)
        {
            chargeGlow.SetAsLastSibling();
        }
        if (impactFlash != null)
        {
            impactFlash.SetAsLastSibling();
        }
        if (impactBurst != null)
        {
            impactBurst.SetAsLastSibling();
        }
        if (impactRing != null)
        {
            impactRing.SetAsLastSibling();
        }
        for (var i = 0; i < impactSlashes.Count; i++)
        {
            if (impactSlashes[i] != null)
            {
                impactSlashes[i].SetAsLastSibling();
            }
        }
        for (var i = 0; i < impactRipples.Count; i++)
        {
            if (impactRipples[i] != null)
            {
                impactRipples[i].SetAsLastSibling();
            }
        }
        for (var i = 0; i < impactSparks.Count; i++)
        {
            if (impactSparks[i] != null)
            {
                impactSparks[i].SetAsLastSibling();
            }
        }
        if (floatingText != null)
        {
            floatingText.rectTransform.SetAsLastSibling();
        }
    }

    private Vector2 DrawPilePositionInHand()
    {
        return TableTopLeftToHand(150f, 245f);
    }

    private Vector2 BambooScrollPositionInHand()
    {
        return TableTopLeftToHand(968f, 486f);
    }

    private Vector2 TableTopLeftToHand(float x, float y)
    {
        var handTopLeft = HandTopLeftInTable();
        return new Vector2(x - handTopLeft.x, -(y - handTopLeft.y));
    }

    private CanvasGroup EnsureCanvasGroup(RectTransform rect)
    {
        var group = rect.GetComponent<CanvasGroup>();
        return group != null ? group : rect.gameObject.AddComponent<CanvasGroup>();
    }

    private Vector2 CenterPosition(RectTransform rect)
    {
        return new Vector2(rect.anchoredPosition.x + rect.sizeDelta.x * 0.5f - 750f, rect.anchoredPosition.y - rect.sizeDelta.y * 0.5f + 450f);
    }

    private Vector2 CombatFocusPosition(RectTransform rect)
    {
        return RectCenterInTable(CombatFocusRect(rect));
    }

    private Vector2 AnchoredPositionForFocus(RectTransform rect, Vector2 focus)
    {
        return rect.anchoredPosition + focus - CombatFocusPosition(rect);
    }

    private RectTransform CombatFocusRect(RectTransform rect)
    {
        if (rect == null)
        {
            return null;
        }

        var heroArt = rect.Find("Hero Art") as RectTransform;
        if (heroArt != null)
        {
            return heroArt;
        }

        var enemyArt = rect.Find("Enemy Art") as RectTransform;
        return enemyArt != null ? enemyArt : rect;
    }

    private Vector2 RectCenterInTable(RectTransform rect)
    {
        if (rect == null || table == null)
        {
            return Vector2.zero;
        }

        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var worldCenter = (corners[0] + corners[2]) * 0.5f;
        var localCenter = table.InverseTransformPoint(worldCenter);
        return new Vector2(localCenter.x, localCenter.y);
    }

    private void ConfigureAttackLine(Vector2 from, Vector2 to, Color color, float alpha)
    {
        var delta = to - from;
        attackLine.anchoredPosition = from + delta * 0.5f;
        attackLine.sizeDelta = new Vector2(Mathf.Max(110f, delta.magnitude * 1.06f), 30f);
        attackLine.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        attackLine.localScale = Vector3.one;
        SetImageAlpha(attackLine.GetComponent<Image>(), color, alpha);
    }

    private void SetImageAlpha(Image image, Color color, float alpha)
    {
        if (image == null)
        {
            return;
        }

        color.a = Mathf.Clamp01(alpha);
        image.color = color;
    }

    private float EaseOut(float value)
    {
        value = Mathf.Clamp01(value);
        return 1f - Mathf.Pow(1f - value, 3f);
    }

    private float EaseIn(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * value;
    }

    private void UpdateCardHover(Event current)
    {
        if (current == null || cards == null || table == null || handRoot == null)
        {
            return;
        }

        if (!scrollOpen || inputLocked || gameOver || !playerTurn || isDraggingCard)
        {
            ClearCardHover();
            return;
        }

        CardView nextHover = null;
        for (var i = cardViews.Count - 1; i >= 0; i--)
        {
            var view = cardViews[i];
            if (view.Root == null || !view.Root.gameObject.activeSelf || discardedCards.Contains(view.Card.Id))
            {
                continue;
            }

            if (CardScreenRect(view).Contains(current.mousePosition))
            {
                nextHover = view;
                break;
            }
        }

        if (hoveredCard != nextHover)
        {
            ClearCardHover();
            hoveredCard = nextHover;
            if (hoveredCard != null)
            {
                RaiseHandLayer();
                hoveredCard.Root.SetAsLastSibling();
            }
        }

        if (hoveredCard != null)
        {
            ApplyCardHoverVisual(hoveredCard, true);
        }

        foreach (var view in cardViews)
        {
            if (view != hoveredCard && view.Root.gameObject.activeSelf && !discardedCards.Contains(view.Card.Id))
            {
                ApplyCardHoverVisual(view, false);
            }
        }
    }

    private void ClearCardHover()
    {
        if (hoveredCard == null || isDraggingCard)
        {
            hoveredCard = null;
            return;
        }

        ApplyCardHoverVisual(hoveredCard, false);
        hoveredCard = null;
    }

    private void ApplyCardHoverVisual(CardView view, bool active)
    {
        var targetPosition = active ? HoverCardPosition(view) : view.HomePosition;
        var targetScale = active ? 1.5f : 1f;
        view.Root.anchoredPosition = Vector2.Lerp(view.Root.anchoredPosition, targetPosition, 0.24f);
        view.Root.localScale = Vector3.Lerp(view.Root.localScale, Vector3.one * targetScale, 0.24f);
        view.Root.localEulerAngles = Vector3.zero;
        UpdateCardTextForHover(view, active);
    }

    private void UpdateCardTextForHover(CardView view, bool active)
    {
        ApplyCardTextQuality(view.TitleText, active ? 14 : 12, true);
        ApplyCardTextQuality(view.CostText, active ? 13 : 11, true);
        ApplyCardTextQuality(view.TypeText, active ? 10 : 8, false);
        ApplyCardTextQuality(view.BodyText, active ? 10 : 8, false);
        ApplyCardTextQuality(view.Note, active ? 9 : 8, false);
    }

    private void ApplyCardTextQuality(Text label, int size, bool bold)
    {
        if (label == null)
        {
            return;
        }

        label.fontSize = size;
        label.resizeTextForBestFit = false;
        label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.material = font.material;
    }

    private Vector2 HoverCardPosition(CardView view)
    {
        return view.HomePosition + new Vector2(-view.Root.sizeDelta.x * 0.25f, 58f);
    }

    private void HandleCardDrag(Event current)
    {
        if (current == null || cards == null || table == null || handRoot == null)
        {
            return;
        }

        if (inputLocked || gameOver || !playerTurn)
        {
            if (isDraggingCard)
            {
                CancelCardDrag();
            }
            return;
        }

        if (current.type == EventType.MouseDown && current.button == 0)
        {
            for (var i = cardViews.Count - 1; i >= 0; i--)
            {
                var view = cardViews[i];
                if (!CanPlay(view.Card) || !CardScreenRect(view).Contains(current.mousePosition))
                {
                    continue;
                }

                BeginCardDrag(view, current.mousePosition);
                current.Use();
                return;
            }
        }

        if (isDraggingCard && draggingCard != null && (current.type == EventType.MouseDrag || current.type == EventType.MouseMove || current.type == EventType.Repaint))
        {
            UpdateCardDrag(current.mousePosition);
            if (current.type != EventType.Repaint)
            {
                current.Use();
            }
        }

        if (isDraggingCard && draggingCard != null && current.type == EventType.MouseUp && current.button == 0)
        {
            var droppedOnPlayZone = HiddenPlayZone().Contains(ScreenToTablePoint(current.mousePosition));
            var view = draggingCard;
            draggingCard = null;
            isDraggingCard = false;

            if (droppedOnPlayZone && CanPlay(view.Card))
            {
                view.Root.localScale = Vector3.one;
                PlayCard(view.Card);
            }
            else
            {
                StartCoroutine(ReturnDraggedCard(view));
            }

            current.Use();
        }
    }

    private void BeginCardDrag(CardView view, Vector2 mousePosition)
    {
        draggingCard = view;
        isDraggingCard = true;
        hoveredCard = null;
        RaiseHandLayer();
        view.Root.SetAsLastSibling();
        view.Root.localScale = Vector3.one;
        EnsureCanvasGroup(view.Root).alpha = 1f;

        var tablePoint = ScreenToTablePoint(mousePosition);
        var handTopLeft = HandTopLeftInTable();
        var cardTopLeft = new Vector2(handTopLeft.x + view.Root.anchoredPosition.x, handTopLeft.y - view.Root.anchoredPosition.y);
        dragOffset = tablePoint - cardTopLeft;
    }

    private void UpdateCardDrag(Vector2 mousePosition)
    {
        var tablePoint = ScreenToTablePoint(mousePosition);
        var handTopLeft = HandTopLeftInTable();
        var cardTopLeft = tablePoint - dragOffset;
        draggingCard.Root.anchoredPosition = new Vector2(cardTopLeft.x - handTopLeft.x, -(cardTopLeft.y - handTopLeft.y));
    }

    private void CancelCardDrag()
    {
        var view = draggingCard;
        draggingCard = null;
        isDraggingCard = false;
        if (view != null)
        {
            StartCoroutine(ReturnDraggedCard(view));
        }
    }

    private IEnumerator ReturnDraggedCard(CardView view)
    {
        var start = view.Root.anchoredPosition;
        for (var t = 0f; t < 0.18f; t += Time.unscaledDeltaTime)
        {
            var p = EaseOut(t / 0.18f);
            view.Root.anchoredPosition = Vector2.Lerp(start, view.HomePosition, p);
            yield return null;
        }

        view.Root.anchoredPosition = view.HomePosition;
        view.Root.localScale = Vector3.one;
        view.Root.localEulerAngles = Vector3.zero;
        if (hoveredCard == view)
        {
            hoveredCard = null;
        }
    }

    private Rect HiddenPlayZone()
    {
        return new Rect(300f, 170f, 760f, 405f);
    }

    private Rect CardScreenRect(CardView view)
    {
        var handTopLeft = HandTopLeftInTable();
        var scale = Mathf.Max(Mathf.Abs(view.Root.localScale.x), Mathf.Abs(view.Root.localScale.y));
        return TableRect(handTopLeft.x + view.Root.anchoredPosition.x, handTopLeft.y - view.Root.anchoredPosition.y, view.Root.sizeDelta.x * scale, view.Root.sizeDelta.y * scale);
    }

    private Vector2 HandTopLeftInTable()
    {
        return new Vector2(handRoot.anchoredPosition.x, -handRoot.anchoredPosition.y);
    }

    private static Vector2 ScreenToTablePoint(Vector2 screenPoint)
    {
        var scale = CanvasScale();
        var tableLeft = Screen.width * 0.5f - 750f * scale;
        var tableTop = Screen.height * 0.5f - 450f * scale;
        return new Vector2((screenPoint.x - tableLeft) / scale, (screenPoint.y - tableTop) / scale);
    }

    private void OnGUI()
    {
        if (!string.IsNullOrEmpty(bootstrapError))
        {
            GUI.color = Color.red;
            GUI.Label(new Rect(20, 20, Screen.width - 40, 220), bootstrapError);
            GUI.color = Color.white;
            return;
        }

        if (cards == null || cards.Length == 0 || table == null)
        {
            return;
        }

        UpdateCardHover(Event.current);
        HandleCardDrag(Event.current);

        if (gameOver)
        {
            if (InvisibleButton(RootRect(732, 470, 136, 42), true))
            {
                ResetGame();
            }
            return;
        }

        if (InvisibleButton(TableRect(996, 476, 58, 196), playerTurn && !gameOver && !inputLocked))
        {
            ToggleBambooScroll();
        }

        if (InvisibleButton(TableRect(1110, 344, 112, 54), playerTurn && !gameOver && !inputLocked))
        {
            EndTurn();
        }

        if (InvisibleButton(TableRect(164, 858, 72, 34), true))
        {
            ResetGame();
        }

        GUI.enabled = true;
    }

    private static bool InvisibleButton(Rect rect, bool enabled)
    {
        GUI.enabled = enabled;
        var clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
        GUI.enabled = true;
        return clicked;
    }

    private static Rect TableRect(float x, float y, float width, float height)
    {
        var scale = CanvasScale();
        var tableLeft = Screen.width * 0.5f - 750f * scale;
        var tableTop = Screen.height * 0.5f - 450f * scale;
        return new Rect(tableLeft + x * scale, tableTop + y * scale, width * scale, height * scale);
    }

    private static Rect RootRect(float x, float y, float width, float height)
    {
        var scale = CanvasScale();
        var rootLeft = Screen.width * 0.5f - 800f * scale;
        var rootTop = Screen.height * 0.5f - 450f * scale;
        return new Rect(rootLeft + x * scale, rootTop + y * scale, width * scale, height * scale);
    }

    private static float CanvasScale()
    {
        var widthScale = Screen.width / 1600f;
        var heightScale = Screen.height / 900f;
        var logWidth = Mathf.Log(widthScale, 2f);
        var logHeight = Mathf.Log(heightScale, 2f);
        return Mathf.Pow(2f, (logWidth + logHeight) * 0.5f);
    }

    private Color32 ColorText()
    {
        return new Color32(18, 18, 18, 255);
    }

    private Color32 Muted()
    {
        return new Color32(84, 84, 84, 255);
    }

    private Color32 Gold()
    {
        return new Color32(215, 195, 126, 255);
    }

    private Color32 Red()
    {
        return new Color32(231, 102, 86, 255);
    }

    private Color32 Blue()
    {
        return new Color32(102, 184, 223, 255);
    }

    private sealed class CardData
    {
        public CardData(string id, string name, string type, int cost, string text, Color32 tint, bool once, Action play)
        {
            Id = id;
            Name = name;
            Type = type;
            Cost = cost;
            Text = text;
            Tint = tint;
            Once = once;
            Play = play;
        }

        public string Id { get; }
        public string Name { get; }
        public string Type { get; }
        public int Cost { get; }
        public string Text { get; }
        public Color32 Tint { get; }
        public bool Once { get; }
        public Action Play { get; }
    }

    private sealed class IntentData
    {
        public IntentData(string name, string icon, string description, Color32 tint, Action run)
        {
            Name = name;
            Icon = icon;
            Description = description;
            Tint = tint;
            Run = run;
        }

        public string Name { get; }
        public string Icon { get; }
        public string Description { get; }
        public Color32 Tint { get; }
        public Action Run { get; }
    }

    private sealed class CardView
    {
        public CardView(CardData card, Button button, Text note, RectTransform root, Vector2 homePosition, Text titleText, Text costText, Text typeText, Text bodyText)
        {
            Card = card;
            Button = button;
            Note = note;
            Root = root;
            HomePosition = homePosition;
            TitleText = titleText;
            CostText = costText;
            TypeText = typeText;
            BodyText = bodyText;
        }

        public CardData Card { get; }
        public Button Button { get; }
        public Text Note { get; }
        public RectTransform Root { get; }
        public Vector2 HomePosition { get; }
        public Text TitleText { get; }
        public Text CostText { get; }
        public Text TypeText { get; }
        public Text BodyText { get; }
    }

    private sealed class BuiltCard
    {
        public BuiltCard(Button button, Text note, RectTransform root, Text title, Text cost, Text type, Text body)
        {
            Button = button;
            Note = note;
            Root = root;
            Title = title;
            Cost = cost;
            Type = type;
            Body = body;
        }

        public Button Button { get; }
        public Text Note { get; }
        public RectTransform Root { get; }
        public Text Title { get; }
        public Text Cost { get; }
        public Text Type { get; }
        public Text Body { get; }
    }


private void ApplyCardBackgroundMaterial(RectTransform rect, string editorAssetPath)
    {
        var image = rect.GetComponent<Image>();
        if (image == null)
        {
            return;
        }

#if UNITY_EDITOR
        var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(editorAssetPath);
        if (material != null)
        {
            image.material = material;
        }
#endif
    }
}

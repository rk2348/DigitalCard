using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 対戦会場の3Dスタジアム。素材ファイルを使わず、すべて実行時にコードで組み立てる。
///
/// 構成(単位はメートル、原点がフィールド中央):
///   石畳の円形闘技場(半径16.5m、金の象嵌の六芒星・苔・四隅のクリスタルの石柱・センターの魔法陣)
///   → 外側の石畳 → LEDボード付きの壁 → すり鉢状の観客席(16段・観客は約7000人)
///   → 屋根と照明塔(光の筋つき) → 大型ビジョン3面 → 夕暮れの空と星。
/// 夜のナイター照明の雰囲気になるよう、Bloom・トーンマップ・ビネットのポストエフェクトもここで設定する。
///
/// キャラクターは Player1Spot / Player2Spot に立つ。歓声(Cheer)やLEDの色(FlashLed)は
/// バトルの進行に合わせて BattleManager から呼ぶ。
/// </summary>
public sealed class Stadium : MonoBehaviour
{
    public const float FieldHalfX = 22f;
    public const float FieldHalfZ = 13f;
    public const float FighterX = 9.5f;

    // 観客席の形(角丸長方形)。内周は x=±27, z=±19
    private const float StandStraightX = 15f;
    private const float StandStraightZ = 7f;
    private const float StandInnerRadius = 12f;
    private const int StandTiers = 16;
    private const float TierDepth = 1.5f;
    private const float TierRise = 0.85f;
    private const float WallHeight = 1.3f;
    private const int ArcSteps = 14;
    private const int StraightSteps = 12;

    public Vector3 Player1Spot => new Vector3(-FighterX, ArenaHeight, 0f);
    public Vector3 Player2Spot => new Vector3(FighterX, ArenaHeight, 0f);

    private readonly List<Transform> crowdGroups = new List<Transform>();
    private readonly List<float> crowdPhases = new List<float>();
    private readonly List<TextMeshPro> screenTitles = new List<TextMeshPro>();
    private readonly List<TextMeshPro> screenSubtitles = new List<TextMeshPro>();
    private Material ledMaterial;
    private Material trimMaterial;
    private Material lightStickMaterial;
    private Material emblemMaterial;
    private Material emblemGlowMaterial;
    private Color ledFlashColor;
    private float ledFlashUntil;
    private float excitement;

    // ==================== 公開API ====================

    /// <summary>大型ビジョンの表示を変える。</summary>
    public void SetScreens(string title, string subtitle)
    {
        foreach (TextMeshPro text in screenTitles) text.text = title;
        foreach (TextMeshPro text in screenSubtitles) text.text = subtitle;
    }

    /// <summary>観客を沸かせる(strengthは0〜1程度、大きいほど激しく跳ねる)。</summary>
    public void Cheer(float strength = 1f)
    {
        excitement = Mathf.Max(excitement, strength);
    }

    /// <summary>LEDボードと縁の光を一時的に指定色にする。</summary>
    public void FlashLed(Color color, float duration = 1.5f)
    {
        ledFlashColor = color;
        ledFlashUntil = Time.time + duration;
    }

    private void Update()
    {
        float t = Time.time;
        excitement = Mathf.MoveTowards(excitement, 0f, Time.deltaTime * 0.35f);

        // 観客: グループごとに位相をずらして跳ねる
        float amplitude = 0.025f + excitement * 0.35f;
        float speed = 5f + excitement * 6f;
        for (int i = 0; i < crowdGroups.Count; i++)
        {
            float jump = Mathf.Abs(Mathf.Sin(t * speed + crowdPhases[i])) * amplitude;
            crowdGroups[i].localPosition = new Vector3(0f, jump, 0f);
        }

        // LEDボード: 流れる模様 + 属性色を巡回(演出中は指定色)
        if (ledMaterial != null)
        {
            ledMaterial.SetTextureOffset("_BaseMap", new Vector2(-t * (0.35f + excitement), 0f));
            Color cycle = Color.HSVToRGB(Mathf.Repeat(t * 0.03f, 1f), 0.55f, 1f);
            Color target = Time.time < ledFlashUntil ? ledFlashColor : cycle;
            Color current = ledMaterial.GetColor("_BaseColor");
            ledMaterial.SetColor("_BaseColor", Color.Lerp(current, StadiumKit.Hdr(target, 2.2f + excitement * 2f), Time.deltaTime * 6f));
            trimMaterial.SetColor("_BaseColor", Color.Lerp(trimMaterial.GetColor("_BaseColor"),
                StadiumKit.Hdr(Time.time < ledFlashUntil ? ledFlashColor : OrisamoUI.Gold, 2.5f), Time.deltaTime * 6f));
        }

        if (lightStickMaterial != null)
        {
            lightStickMaterial.SetColor("_BaseColor", Color.white * (2.2f + Mathf.Sin(t * 7f) * 0.6f + excitement * 3f));
        }

        if (emblemMaterial != null)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 1.6f);
            emblemMaterial.SetColor("_BaseColor", StadiumKit.Hdr(OrisamoUI.Arcane, 1.6f + pulse * 1.2f));
            emblemGlowMaterial.SetColor("_BaseColor", OrisamoUI.WithAlpha(OrisamoUI.Arcane, 0.25f + pulse * 0.15f));
        }
    }

    // ==================== 生成 ====================

    /// <summary>スタジアム一式を生成する。cameraには描画設定(背景色・ポストエフェクト・描画距離)を適用する。</summary>
    public static Stadium Build(Camera camera)
    {
        Stadium stadium = new GameObject("Stadium").AddComponent<Stadium>();
        stadium.BuildAll(camera);
        return stadium;
    }

    private void BuildAll(Camera camera)
    {
        Random.State previousRandom = Random.state;
        Random.InitState(20261006); // 観客の配置などを毎回同じにする

        SetupEnvironment(camera);
        BuildSky();
        BuildGround();
        BuildField();
        BuildStands();
        BuildRoofAndLights();
        BuildScreens();
        StadiumKit.AmbientMotes(transform, new Vector3(FieldHalfX * 2f, 7f, FieldHalfZ * 2f));
        SetScreens("ORISAMO", "STADIUM");

        Random.state = previousRandom;
    }

    private void SetupEnvironment(Camera camera)
    {
        Color fogColor = new Color(0.12f, 0.09f, 0.20f);
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.36f, 0.34f, 0.52f);
        RenderSettings.ambientEquatorColor = new Color(0.26f, 0.21f, 0.30f);
        RenderSettings.ambientGroundColor = new Color(0.09f, 0.08f, 0.11f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0042f;
        RenderSettings.fogColor = fogColor;

        if (camera != null)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = fogColor;
            camera.nearClipPlane = 0.2f;
            camera.farClipPlane = 1500f;
            camera.allowHDR = true;
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            if (cameraData != null)
            {
                cameraData.renderPostProcessing = true;
                cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
        }

        // 主光源: ナイター照明を想定した、やや暖色の強い光
        Light sun = null;
        foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type == LightType.Directional) { sun = light; break; }
        }
        if (sun == null) sun = new GameObject("Key Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(58f, -28f, 0f);
        sun.color = new Color(1f, 0.95f, 0.88f);
        sun.intensity = 1.25f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.65f;

        // ポストエフェクト
        Volume volume = new GameObject("Stadium Post FX").AddComponent<Volume>();
        volume.transform.SetParent(transform, false);
        volume.isGlobal = true;
        volume.priority = 10f;
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(1.3f);
        bloom.scatter.Override(0.72f);
        Tonemapping tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.ACES);
        ColorAdjustments color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.35f);
        color.contrast.Override(14f);
        color.saturation.Override(12f);
        Vignette vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.3f);
        vignette.smoothness.Override(0.45f);
        volume.profile = profile;
    }

    /// <summary>夕暮れから夜へのグラデーションの空と星。</summary>
    private void BuildSky()
    {
        const int rings = 24;
        const int segments = 48;
        const float radius = 130f; // フォグで空のグラデーションが消えないよう、観客席のすぐ外側に置く
        Color horizon = new Color(0.72f, 0.36f, 0.48f);
        Color middle = new Color(0.20f, 0.13f, 0.36f);
        Color zenith = new Color(0.03f, 0.03f, 0.09f);

        StadiumKit.MeshBuilder sky = new StadiumKit.MeshBuilder();
        for (int r = 0; r < rings; r++)
        {
            float v0 = (float)r / rings, v1 = (float)(r + 1) / rings;
            float e0 = Mathf.Lerp(-0.15f, 1f, v0) * Mathf.PI / 2f;
            float e1 = Mathf.Lerp(-0.15f, 1f, v1) * Mathf.PI / 2f;
            Color c0 = SkyColor(v0, horizon, middle, zenith), c1 = SkyColor(v1, horizon, middle, zenith);
            for (int s = 0; s < segments; s++)
            {
                float a0 = Mathf.PI * 2f * s / segments, a1 = Mathf.PI * 2f * (s + 1) / segments;
                Vector3 p00 = SpherePoint(a0, e0) * radius, p01 = SpherePoint(a0, e1) * radius;
                Vector3 p11 = SpherePoint(a1, e1) * radius, p10 = SpherePoint(a1, e0) * radius;
                sky.AddTriangle(p00, p01, p11, c0, c1, c1, Vector3.zero);
                sky.AddTriangle(p00, p11, p10, c0, c1, c0, Vector3.zero);
            }
        }
        GameObject skyObject = StadiumKit.CreateMeshObject("Sky", transform, sky.Build(), StadiumKit.Unlit(StadiumKit.Blend.Opaque, Color.white));
        skyObject.GetComponent<MeshRenderer>().receiveShadows = false;

        StadiumKit.MeshBuilder stars = new StadiumKit.MeshBuilder();
        for (int i = 0; i < 420; i++)
        {
            float azimuth = Random.value * Mathf.PI * 2f;
            float elevation = Mathf.Lerp(0.25f, 1.45f, Mathf.Sqrt(Random.value));
            Vector3 center = SpherePoint(azimuth, elevation) * (radius * 0.95f);
            Vector3 right = Vector3.Cross(Vector3.up, center).normalized;
            Vector3 up = Vector3.Cross(center, right).normalized;
            float size = Random.Range(0.22f, 0.6f);
            Color c = Color.Lerp(new Color(0.75f, 0.8f, 1f), new Color(1f, 0.9f, 0.75f), Random.value) * Random.Range(0.6f, 1.6f);
            stars.AddQuad(center - (right + up) * size, center - (right - up) * size, center + (right + up) * size, center + (right - up) * size,
                -center.normalized, c);
        }
        StadiumKit.CreateMeshObject("Stars", transform, stars.Build(), StadiumKit.Unlit(StadiumKit.Blend.Additive, Color.white * 1.5f, OrisamoUI.SoftGlow.texture));
    }

    private static Color SkyColor(float v, Color horizon, Color middle, Color zenith)
    {
        return v < 0.35f ? Color.Lerp(horizon, middle, v / 0.35f) : Color.Lerp(middle, zenith, (v - 0.35f) / 0.65f);
    }

    private static Vector3 SpherePoint(float azimuth, float elevation)
    {
        return new Vector3(Mathf.Cos(azimuth) * Mathf.Cos(elevation), Mathf.Sin(elevation), Mathf.Sin(azimuth) * Mathf.Cos(elevation));
    }

    private void BuildGround()
    {
        GameObject ground = StadiumKit.CreateFlatDecal("Ground", transform, 600f, StadiumKit.Lit(new Color(0.06f, 0.055f, 0.075f), 0.1f));
        ground.transform.localPosition = new Vector3(0f, -0.02f, 0f);

        // 闘技場の外側の石畳(闘技場と壁の間)。大きめの敷石を少しずつ色を変えて並べる
        float ax = StandStraightX + StandInnerRadius, az = StandStraightZ + StandInnerRadius;
        StadiumKit.MeshBuilder grout = new StadiumKit.MeshBuilder();
        grout.AddQuad(new Vector3(-ax, 0f, -az), new Vector3(-ax, 0f, az), new Vector3(ax, 0f, az), new Vector3(ax, 0f, -az), Vector3.up, Color.white);
        StadiumKit.CreateMeshObject("PavingGrout", transform, grout.Build(), StadiumKit.Lit(new Color(0.07f, 0.065f, 0.08f), 0.05f));

        StadiumKit.MeshBuilder[] paving = NewBuilders(StoneTones.Length);
        const float tile = 2.4f;
        const float gap = 0.09f;
        for (float x = -ax; x < ax; x += tile)
        {
            for (float z = -az; z < az; z += tile)
            {
                Vector2 center = new Vector2(x + tile / 2f, z + tile / 2f);
                if (center.magnitude < ArenaRadius - 0.5f) continue; // 闘技場の下は敷かない
                float x0 = x + gap, x1 = Mathf.Min(x + tile, ax) - gap;
                float z0 = z + gap, z1 = Mathf.Min(z + tile, az) - gap;
                paving[Random.Range(0, paving.Length)].AddQuad(new Vector3(x0, 0.01f, z0), new Vector3(x0, 0.01f, z1),
                    new Vector3(x1, 0.01f, z1), new Vector3(x1, 0.01f, z0), Vector3.up, Color.white);
            }
        }
        for (int i = 0; i < paving.Length; i++)
        {
            StadiumKit.CreateMeshObject("Paving", transform, paving[i].Build(), StadiumKit.Lit(StoneTones[i] * 0.72f, 0.12f));
        }
    }

    // ==================== 闘技場 ====================

    // 背景画像(BattleArenaBackdrop)と同じ、浮遊島の石造りの円形闘技場
    private const float ArenaRadius = 16.5f;
    private const float ArenaHeight = 0.32f;
    private const float RimWidth = 1.4f;
    private const float InlayRadius = 13.6f;

    private static readonly Color[] StoneTones =
    {
        new Color(0.30f, 0.29f, 0.32f),
        new Color(0.26f, 0.25f, 0.29f),
        new Color(0.34f, 0.32f, 0.33f),
        new Color(0.23f, 0.23f, 0.27f),
    };

    private static StadiumKit.MeshBuilder[] NewBuilders(int count)
    {
        StadiumKit.MeshBuilder[] builders = new StadiumKit.MeshBuilder[count];
        for (int i = 0; i < count; i++) builders[i] = new StadiumKit.MeshBuilder();
        return builders;
    }

    /// <summary>石畳の円形闘技場・金の象嵌の魔法陣・苔・クリスタルの石柱・センターの魔法陣。</summary>
    private void BuildField()
    {
        Transform field = new GameObject("Field").transform;
        field.SetParent(transform, false);

        // 土台(敷石の目地から見える暗い石)と側面
        float top = ArenaHeight;
        StadiumKit.CreateMeshObject("ArenaBase", field, StadiumKit.CreateAnnulus(0f, ArenaRadius, 120, Color.white),
            StadiumKit.Lit(new Color(0.08f, 0.075f, 0.09f), 0.05f)).transform.localPosition = new Vector3(0f, top - 0.01f, 0f);
        StadiumKit.MeshBuilder side = new StadiumKit.MeshBuilder();
        for (int i = 0; i < 120; i++)
        {
            float a0 = Mathf.PI * 2f * i / 120, a1 = Mathf.PI * 2f * (i + 1) / 120;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            side.AddQuad(d0 * ArenaRadius, d0 * ArenaRadius + Vector3.up * (top + 0.14f), d1 * ArenaRadius + Vector3.up * (top + 0.14f), d1 * ArenaRadius,
                (d0 + d1).normalized, Color.white);
        }
        StadiumKit.CreateMeshObject("ArenaSide", field, side.Build(), StadiumKit.Lit(new Color(0.20f, 0.19f, 0.22f), 0.08f), true);

        // 敷石: 同心円の輪を角度方向に割った石を、目地の隙間をあけて並べる
        StadiumKit.MeshBuilder[] stones = NewBuilders(StoneTones.Length);
        float[] ringEdges = { 0f, 2.2f, 4.6f, 7.0f, 9.4f, 11.8f, ArenaRadius - RimWidth };
        for (int r = 0; r < ringEdges.Length - 1; r++)
        {
            float inner = ringEdges[r], outer = ringEdges[r + 1];
            int count = r == 0 ? 1 : Mathf.Max(6, Mathf.RoundToInt(Mathf.PI * (inner + outer) / 2.6f));
            float offset = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < count; i++)
            {
                float a0 = offset + Mathf.PI * 2f * i / count, a1 = offset + Mathf.PI * 2f * (i + 1) / count;
                AddStone(stones[Random.Range(0, stones.Length)], inner, outer, a0, a1, top, count > 1 ? 0.07f : 0f);
            }
        }
        // 外縁の縁石(少し高く、大きい石)
        StadiumKit.MeshBuilder[] curb = NewBuilders(StoneTones.Length);
        const int curbCount = 40;
        for (int i = 0; i < curbCount; i++)
        {
            float a0 = Mathf.PI * 2f * i / curbCount, a1 = Mathf.PI * 2f * (i + 1) / curbCount;
            AddStone(curb[Random.Range(0, curb.Length)], ArenaRadius - RimWidth + 0.06f, ArenaRadius, a0, a1, top + 0.14f, 0.08f);
        }
        for (int i = 0; i < stones.Length; i++)
        {
            StadiumKit.CreateMeshObject("Flagstones", field, stones[i].Build(), StadiumKit.Lit(StoneTones[i], 0.2f));
            StadiumKit.CreateMeshObject("Curb", field, curb[i].Build(), StadiumKit.Lit(StoneTones[i] * 1.12f, 0.15f));
        }

        // 金の象嵌: 外周の円・内側の円・六芒星・中心へ伸びる放射線
        float y = top + 0.012f;
        StadiumKit.MeshBuilder inlay = new StadiumKit.MeshBuilder();
        AddRing(inlay, InlayRadius, 0.16f, y);
        AddRing(inlay, InlayRadius - 0.45f, 0.06f, y);
        AddRing(inlay, 5.0f, 0.14f, y);
        for (int i = 0; i < 6; i++)
        {
            float a0 = Mathf.PI * 2f * i / 6 + Mathf.PI / 6f;
            float a1 = Mathf.PI * 2f * (i + 2) / 6 + Mathf.PI / 6f;
            AddFlatLine(inlay, Polar(InlayRadius, a0), Polar(InlayRadius, a1), 0.11f, y, Color.white);
            float spoke = Mathf.PI * 2f * i / 6;
            AddFlatLine(inlay, Polar(5.0f, spoke), Polar(InlayRadius - 0.45f, spoke), 0.06f, y, Color.white);
        }
        StadiumKit.CreateMeshObject("GoldInlay", field, inlay.Build(), StadiumKit.Unlit(StadiumKit.Blend.Opaque, StadiumKit.Hdr(OrisamoUI.GoldDeep, 1.35f)));

        // キャラクターの立ち位置: 暗い石の台座と金の円
        Material markMaterial = StadiumKit.Unlit(StadiumKit.Blend.Opaque, StadiumKit.Hdr(OrisamoUI.Gold, 1.25f));
        foreach (int s in new[] { -1, 1 })
        {
            GameObject pad = StadiumKit.CreateMeshObject("FighterPad", field, StadiumKit.CreateAnnulus(0f, 2.45f, 64, Color.white),
                StadiumKit.Lit(new Color(0.16f, 0.15f, 0.19f), 0.35f));
            pad.transform.localPosition = new Vector3(s * FighterX, y + 0.004f, 0f);
            GameObject mark = StadiumKit.CreateMeshObject("FighterMark", field, StadiumKit.CreateAnnulus(2.45f, 2.68f, 64, Color.white), markMaterial);
            mark.transform.localPosition = new Vector3(s * FighterX, y + 0.008f, 0f);
        }

        BuildMoss(field, top);
        BuildCrystals(field);

        // センターの魔法陣(ゆっくり脈打つ光)
        emblemMaterial = StadiumKit.Unlit(StadiumKit.Blend.Additive, Color.white, OrisamoUI.ThinRing.texture);
        GameObject ring = StadiumKit.CreateFlatDecal("EmblemRing", field, 11.8f, emblemMaterial);
        ring.transform.localPosition = new Vector3(0f, top + 0.05f, 0f);
        ring.AddComponent<SimpleSpin>().degreesPerSecond = 6f;
        emblemGlowMaterial = StadiumKit.Unlit(StadiumKit.Blend.Additive, Color.white, OrisamoUI.SoftGlow.texture);
        StadiumKit.CreateFlatDecal("EmblemGlow", field, 13f, emblemGlowMaterial).transform.localPosition = new Vector3(0f, top + 0.045f, 0f);

        Transform orbs = new GameObject("ElementOrbs").transform;
        orbs.SetParent(field, false);
        orbs.localPosition = new Vector3(0f, top + 0.055f, 0f);
        orbs.gameObject.AddComponent<SimpleSpin>().degreesPerSecond = -10f;
        ElementType[] elements = { ElementType.Fire, ElementType.Wind, ElementType.Dark, ElementType.Water, ElementType.Earth, ElementType.Light };
        for (int i = 0; i < elements.Length; i++)
        {
            float angle = Mathf.PI * 2f * i / elements.Length;
            Material orbMaterial = StadiumKit.Unlit(StadiumKit.Blend.Additive,
                StadiumKit.Hdr(ElementAffinity.GetElementColor(elements[i]), 2.4f), OrisamoUI.SoftGlow.texture);
            GameObject orb = StadiumKit.CreateFlatDecal("Orb", orbs, 2.2f, orbMaterial);
            orb.transform.localPosition = new Vector3(Mathf.Cos(angle) * 3.3f, 0f, Mathf.Sin(angle) * 3.3f);
        }
    }

    /// <summary>扇形の敷石1枚(上面のみ)。gapの分だけ四辺を内側に詰めて目地を作る。</summary>
    private static void AddStone(StadiumKit.MeshBuilder builder, float inner, float outer, float a0, float a1, float y, float gap)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) * outer / 0.8f));
        float ri = inner + (inner > 0f ? gap : 0f), ro = outer - gap;
        float da = ro > 0f ? gap / ro : 0f;
        a0 += da;
        a1 -= da;
        Vector3 up = Vector3.up * y;
        for (int i = 0; i < steps; i++)
        {
            float b0 = Mathf.Lerp(a0, a1, (float)i / steps), b1 = Mathf.Lerp(a0, a1, (float)(i + 1) / steps);
            Vector3 d0 = new Vector3(Mathf.Cos(b0), 0f, Mathf.Sin(b0)), d1 = new Vector3(Mathf.Cos(b1), 0f, Mathf.Sin(b1));
            builder.AddQuad(d0 * ri + up, d0 * ro + up, d1 * ro + up, d1 * ri + up, Vector3.up, Color.white);
        }
    }

    private static void AddRing(StadiumKit.MeshBuilder builder, float radius, float width, float y)
    {
        const int segments = 128;
        float ri = radius - width / 2f, ro = radius + width / 2f;
        Vector3 up = Vector3.up * y;
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.PI * 2f * i / segments, a1 = Mathf.PI * 2f * (i + 1) / segments;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            builder.AddQuad(d0 * ri + up, d0 * ro + up, d1 * ro + up, d1 * ri + up, Vector3.up, Color.white);
        }
    }

    private static Vector2 Polar(float radius, float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

    /// <summary>縁石や敷石に生えた苔(柔らかい緑のしみ)。</summary>
    private static void BuildMoss(Transform parent, float top)
    {
        Material moss = StadiumKit.Unlit(StadiumKit.Blend.Alpha, new Color(0.20f, 0.30f, 0.12f, 0.75f), OrisamoUI.SoftGlow.texture);
        Material mossLight = StadiumKit.Unlit(StadiumKit.Blend.Alpha, new Color(0.32f, 0.42f, 0.16f, 0.55f), OrisamoUI.SoftGlow.texture);
        for (int i = 0; i < 46; i++)
        {
            float angle = Random.value * Mathf.PI * 2f;
            bool onRim = Random.value < 0.7f;
            float radius = onRim ? ArenaRadius - Random.Range(0f, RimWidth + 0.8f) : Random.Range(6f, ArenaRadius - 2f);
            GameObject patch = StadiumKit.CreateFlatDecal("Moss", parent, Random.Range(0.8f, onRim ? 2.6f : 1.4f), Random.value < 0.5f ? moss : mossLight);
            patch.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, top + 0.16f + i * 0.0004f, Mathf.Sin(angle) * radius);
            patch.transform.localRotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
            patch.transform.localScale = new Vector3(1f, 1f, Random.Range(0.45f, 0.9f));
        }
    }

    /// <summary>闘技場の四隅に立つ、青く光るクリスタルの石柱(背景画像のアーチの結晶と同じ意匠)。</summary>
    private static void BuildCrystals(Transform parent)
    {
        Color crystalColor = new Color(0.35f, 0.62f, 1f);
        Material stone = StadiumKit.Lit(new Color(0.27f, 0.26f, 0.29f), 0.12f);
        Material crystal = StadiumKit.Unlit(StadiumKit.Blend.Opaque, StadiumKit.Hdr(crystalColor, 2.2f));
        Material glow = StadiumKit.Unlit(StadiumKit.Blend.Additive, OrisamoUI.WithAlpha(crystalColor, 0.55f), OrisamoUI.SoftGlow.texture);

        foreach (float degrees in new[] { 45f, 135f, 225f, 315f })
        {
            float rad = degrees * Mathf.Deg2Rad;
            Transform pillar = new GameObject("CrystalPillar").transform;
            pillar.SetParent(parent, false);
            pillar.localPosition = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * (ArenaRadius + 1.2f);
            pillar.localRotation = Quaternion.Euler(0f, -degrees, 0f);

            // 台座(3段の石)
            AddBlock(pillar, stone, new Vector3(0f, 0.3f, 0f), new Vector3(2.4f, 0.6f, 2.4f));
            AddBlock(pillar, stone, new Vector3(0f, 1.6f, 0f), new Vector3(1.5f, 2.0f, 1.5f));
            AddBlock(pillar, stone, new Vector3(0f, 2.75f, 0f), new Vector3(1.9f, 0.3f, 1.9f));

            // 台座の上で回る結晶と、その光
            GameObject gem = StadiumKit.CreateMeshObject("Crystal", pillar, CreateCrystalMesh(0.65f, 1.7f), crystal);
            gem.transform.localPosition = new Vector3(0f, 4.3f, 0f);
            gem.AddComponent<SimpleSpin>().degreesPerSecond = 25f;
            StadiumKit.CreateFlatDecal("CrystalPool", pillar, 3.6f, glow).transform.localPosition = new Vector3(0f, 2.92f, 0f);

            Light light = new GameObject("CrystalLight").AddComponent<Light>();
            light.transform.SetParent(pillar, false);
            light.transform.localPosition = new Vector3(0f, 4.3f, 0f);
            light.type = LightType.Point;
            light.color = crystalColor;
            light.range = 9f;
            light.intensity = 2.2f;
            light.shadows = LightShadows.None;
        }
    }

    private static void AddBlock(Transform parent, Material material, Vector3 center, Vector3 size)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(block.GetComponent<Collider>());
        block.name = "Stone";
        block.transform.SetParent(parent, false);
        block.transform.localPosition = center;
        block.transform.localScale = size;
        block.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>上下に尖った六角柱の結晶。</summary>
    private static Mesh CreateCrystalMesh(float radius, float height)
    {
        StadiumKit.MeshBuilder builder = new StadiumKit.MeshBuilder();
        const int sides = 6;
        Vector3 topTip = Vector3.up * height * 0.5f, bottomTip = Vector3.down * height * 0.5f;
        float bandY = height * 0.12f;
        for (int i = 0; i < sides; i++)
        {
            float a0 = Mathf.PI * 2f * i / sides, a1 = Mathf.PI * 2f * (i + 1) / sides;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius, d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
            Vector3 u0 = d0 + Vector3.up * bandY, u1 = d1 + Vector3.up * bandY;
            Vector3 l0 = d0 + Vector3.down * bandY, l1 = d1 + Vector3.down * bandY;
            Vector3 n = (d0 + d1).normalized;
            builder.AddQuad(l0, u0, u1, l1, n, Color.white);
            builder.AddTriangle(u0, topTip, u1, Color.white, Color.white, Color.white, n);
            builder.AddTriangle(l1, bottomTip, l0, Color.white, Color.white, Color.white, n);
        }
        return builder.Build();
    }

    private static void AddFlatLine(StadiumKit.MeshBuilder builder, Vector2 from, Vector2 to, float width, float y, Color color)
    {
        Vector2 dir = (to - from).normalized;
        Vector2 side = new Vector2(-dir.y, dir.x) * (width / 2f);
        Vector2 ext = dir * (width / 2f);
        Vector2 a = from - ext - side, b = from - ext + side, c = to + ext + side, d = to + ext - side;
        builder.AddQuad(new Vector3(a.x, y, a.y), new Vector3(b.x, y, b.y), new Vector3(c.x, y, c.y), new Vector3(d.x, y, d.y), Vector3.up, color);
    }

    /// <summary>壁・LEDボード・すり鉢状の観客席と観客。</summary>
    private void BuildStands()
    {
        Transform stands = new GameObject("Stands").transform;
        stands.SetParent(transform, false);

        List<Vector3> points = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        StadiumKit.MeshBuilder risers = new StadiumKit.MeshBuilder();
        StadiumKit.MeshBuilder treads = new StadiumKit.MeshBuilder();
        StadiumKit.MeshBuilder[] crowd = { new StadiumKit.MeshBuilder(), new StadiumKit.MeshBuilder(), new StadiumKit.MeshBuilder() };
        StadiumKit.MeshBuilder sticks = new StadiumKit.MeshBuilder();

        Color[] shirtPalette =
        {
            new Color(0.85f, 0.22f, 0.25f), new Color(0.22f, 0.45f, 0.90f), new Color(0.95f, 0.80f, 0.30f),
            new Color(0.92f, 0.92f, 0.95f), new Color(0.30f, 0.75f, 0.45f), new Color(0.65f, 0.35f, 0.85f),
            new Color(0.95f, 0.55f, 0.25f), new Color(0.20f, 0.20f, 0.26f),
        };

        for (int tier = 0; tier < StandTiers; tier++)
        {
            float radius = StandInnerRadius + tier * TierDepth;
            float nextRadius = radius + TierDepth;
            float height = WallHeight + tier * TierRise;
            float below = tier == 0 ? 0f : height - TierRise;

            StadiumKit.RoundedRectPath(StandStraightX, StandStraightZ, radius, ArcSteps, StraightSteps, points, normals);
            List<Vector3> outerPoints = new List<Vector3>();
            List<Vector3> outerNormals = new List<Vector3>();
            StadiumKit.RoundedRectPath(StandStraightX, StandStraightZ, nextRadius, ArcSteps, StraightSteps, outerPoints, outerNormals);

            int count = points.Count;
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Vector3 inward = -(normals[i] + normals[j]).normalized;

                // 段の立ち上がり(最下段は壁)
                risers.AddQuad(points[i] + Vector3.up * below, points[i] + Vector3.up * height,
                    points[j] + Vector3.up * height, points[j] + Vector3.up * below, inward, Color.white);
                // 座面
                treads.AddQuad(points[i] + Vector3.up * height, outerPoints[i] + Vector3.up * height,
                    outerPoints[j] + Vector3.up * height, points[j] + Vector3.up * height, Vector3.up, Color.white);

                // 観客を並べる
                Vector3 from = Vector3.Lerp(points[i], outerPoints[i], 0.5f);
                Vector3 to = Vector3.Lerp(points[j], outerPoints[j], 0.5f);
                float length = Vector3.Distance(from, to);
                Vector3 tangent = (to - from).normalized;
                int people = Mathf.Max(1, Mathf.RoundToInt(length / 0.56f));
                for (int p = 0; p < people; p++)
                {
                    if (Random.value < 0.1f) continue; // 空席
                    Vector3 basePos = Vector3.Lerp(from, to, (p + Random.Range(0.25f, 0.75f)) / people)
                        + Vector3.up * height + inward * Random.Range(-0.15f, 0.15f);
                    float personHeight = Random.Range(0.62f, 0.78f);
                    float personWidth = personHeight * 0.68f;
                    Vector3 right = tangent * (personWidth / 2f);
                    Color shirt = shirtPalette[Random.Range(0, shirtPalette.Length)] * Random.Range(0.45f, 0.95f);
                    shirt.a = 1f;
                    crowd[Random.Range(0, crowd.Length)].AddQuad(basePos + right, basePos + right + Vector3.up * personHeight,
                        basePos - right + Vector3.up * personHeight, basePos - right, inward, shirt);

                    if (Random.value < 0.06f)
                    {
                        // ペンライト
                        Vector3 stickPos = basePos + Vector3.up * (personHeight + 0.18f) + tangent * Random.Range(-0.15f, 0.15f);
                        Color stickColor = ElementAffinity.GetElementColor((ElementType)Random.Range(0, 6));
                        Vector3 sr = tangent * 0.09f, su = Vector3.up * 0.09f;
                        sticks.AddQuad(stickPos - sr - su, stickPos - sr + su, stickPos + sr + su, stickPos + sr - su, inward, stickColor);
                    }
                }
            }
        }

        // 最上段の背面の壁
        float topHeight = WallHeight + (StandTiers - 1) * TierRise;
        float backRadius = StandInnerRadius + StandTiers * TierDepth;
        StadiumKit.RoundedRectPath(StandStraightX, StandStraightZ, backRadius, ArcSteps, StraightSteps, points, normals);
        for (int i = 0; i < points.Count; i++)
        {
            int j = (i + 1) % points.Count;
            risers.AddQuad(points[i] + Vector3.up * topHeight, points[i] + Vector3.up * (topHeight + 7f),
                points[j] + Vector3.up * (topHeight + 7f), points[j] + Vector3.up * topHeight, -normals[i], Color.white);
        }

        StadiumKit.CreateMeshObject("Risers", stands, risers.Build(), StadiumKit.Lit(new Color(0.17f, 0.16f, 0.22f), 0.2f));
        StadiumKit.CreateMeshObject("Seats", stands, treads.Build(), StadiumKit.Lit(new Color(0.26f, 0.10f, 0.15f), 0.25f));

        Material crowdMaterial = StadiumKit.Unlit(StadiumKit.Blend.Cutout, Color.white, CreatePersonTexture());
        for (int g = 0; g < crowd.Length; g++)
        {
            Transform group = StadiumKit.CreateMeshObject("Crowd" + g, stands, crowd[g].Build(), crowdMaterial).transform;
            crowdGroups.Add(group);
            crowdPhases.Add(g * 1.9f);
        }
        lightStickMaterial = StadiumKit.Unlit(StadiumKit.Blend.Additive, Color.white * 2.5f, OrisamoUI.SoftGlow.texture);
        StadiumKit.CreateMeshObject("LightSticks", crowdGroups[0], sticks.Build(), lightStickMaterial);

        BuildLedBoards(stands);
    }

    /// <summary>壁の前面のLEDボードと、壁の上端の光る縁取り。</summary>
    private void BuildLedBoards(Transform parent)
    {
        List<Vector3> points = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        StadiumKit.RoundedRectPath(StandStraightX, StandStraightZ, StandInnerRadius - 0.03f, ArcSteps, StraightSteps, points, normals);

        StadiumKit.MeshBuilder led = new StadiumKit.MeshBuilder();
        StadiumKit.MeshBuilder trim = new StadiumKit.MeshBuilder();
        float distance = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            int j = (i + 1) % points.Count;
            float length = Vector3.Distance(points[i], points[j]);
            Vector3 inward = -normals[i];
            led.AddQuad(points[i] + Vector3.up * 0.22f, points[i] + Vector3.up * 1.08f, points[j] + Vector3.up * 1.08f, points[j] + Vector3.up * 0.22f,
                inward, Color.white, new Vector2(distance / 6f, 0f), new Vector2((distance + length) / 6f, 1f));
            trim.AddQuad(points[i] + Vector3.up * 1.2f, points[i] + Vector3.up * 1.3f, points[j] + Vector3.up * 1.3f, points[j] + Vector3.up * 1.2f,
                inward, Color.white);
            distance += length;
        }

        ledMaterial = StadiumKit.Unlit(StadiumKit.Blend.Opaque, Color.white * 2f, CreateLedTexture());
        StadiumKit.CreateMeshObject("LedBoards", parent, led.Build(), ledMaterial);
        trimMaterial = StadiumKit.Unlit(StadiumKit.Blend.Opaque, StadiumKit.Hdr(OrisamoUI.Gold, 2.5f));
        StadiumKit.CreateMeshObject("WallTrim", parent, trim.Build(), trimMaterial);
    }

    /// <summary>屋根・照明・光の筋・スポットライト。</summary>
    private void BuildRoofAndLights()
    {
        Transform roof = new GameObject("Roof").transform;
        roof.SetParent(transform, false);

        float topHeight = WallHeight + (StandTiers - 1) * TierRise;
        float outerRadius = StandInnerRadius + StandTiers * TierDepth;
        float innerRadius = StandInnerRadius + 6f;
        float outerY = topHeight + 7f, innerY = topHeight + 9f;

        List<Vector3> outer = new List<Vector3>(), outerN = new List<Vector3>();
        List<Vector3> inner = new List<Vector3>(), innerN = new List<Vector3>();
        StadiumKit.RoundedRectPath(StandStraightX, StandStraightZ, outerRadius, ArcSteps, StraightSteps, outer, outerN);
        StadiumKit.RoundedRectPath(StandStraightX, StandStraightZ, innerRadius, ArcSteps, StraightSteps, inner, innerN);

        StadiumKit.MeshBuilder canopy = new StadiumKit.MeshBuilder();
        StadiumKit.MeshBuilder lamps = new StadiumKit.MeshBuilder();
        for (int i = 0; i < outer.Count; i++)
        {
            int j = (i + 1) % outer.Count;
            canopy.AddQuad(inner[i] + Vector3.up * innerY, outer[i] + Vector3.up * outerY, outer[j] + Vector3.up * outerY,
                inner[j] + Vector3.up * innerY, Vector3.down, Color.white);
            // 屋根の内側の縁の鼻先
            canopy.AddQuad(inner[i] + Vector3.up * (innerY - 1.2f), inner[i] + Vector3.up * innerY, inner[j] + Vector3.up * innerY,
                inner[j] + Vector3.up * (innerY - 1.2f), -innerN[i], Color.white);

            if (i % 2 == 0)
            {
                // 照明の列(内側を向いた発光パネル)
                Vector3 center = Vector3.Lerp(inner[i], inner[j], 0.5f) + Vector3.up * (innerY - 0.6f) - innerN[i] * 0.05f;
                Vector3 tangent = (inner[j] - inner[i]).normalized;
                Vector3 r = tangent * 0.9f, u = Vector3.up * 0.35f;
                lamps.AddQuad(center - r - u, center - r + u, center + r + u, center + r - u, -innerN[i], Color.white);
            }
        }
        StadiumKit.CreateMeshObject("Canopy", roof, canopy.Build(), StadiumKit.Lit(new Color(0.08f, 0.08f, 0.11f), 0.3f, 0.4f));
        StadiumKit.CreateMeshObject("FloodLamps", roof, lamps.Build(),
            StadiumKit.Unlit(StadiumKit.Blend.Opaque, StadiumKit.Hdr(new Color(1f, 0.96f, 0.88f), 6f)));

        // 光の筋(照明から地面へ伸びる円錐)とスポットライト
        StadiumKit.MeshBuilder beams = new StadiumKit.MeshBuilder();
        Vector3[] beamSources =
        {
            new Vector3(StandStraightX + 10f, innerY, StandStraightZ + 10f), new Vector3(-StandStraightX - 10f, innerY, StandStraightZ + 10f),
            new Vector3(-StandStraightX - 10f, innerY, -StandStraightZ - 10f), new Vector3(StandStraightX + 10f, innerY, -StandStraightZ - 10f),
            new Vector3(0f, innerY, StandStraightZ + innerRadius), new Vector3(0f, innerY, -StandStraightZ - innerRadius),
        };
        foreach (Vector3 source in beamSources)
        {
            Vector3 target = new Vector3(source.x * 0.25f, 0f, source.z * 0.25f);
            AddLightCone(beams, source, target, 6.5f, new Color(1f, 0.95f, 0.85f, 0.07f));
        }
        StadiumKit.CreateMeshObject("LightBeams", roof, beams.Build(), StadiumKit.Unlit(StadiumKit.Blend.Additive, Color.white));

        for (int i = 0; i < 4; i++)
        {
            Light spot = new GameObject("Floodlight" + i).AddComponent<Light>();
            spot.transform.SetParent(roof, false);
            spot.type = LightType.Spot;
            spot.transform.position = beamSources[i];
            spot.transform.LookAt(Vector3.zero);
            spot.spotAngle = 70f;
            spot.innerSpotAngle = 40f;
            spot.range = 140f;
            spot.intensity = 1600f;
            spot.color = i % 2 == 0 ? new Color(1f, 0.93f, 0.85f) : new Color(0.85f, 0.9f, 1f);
            spot.shadows = LightShadows.None;
        }
    }

    private static void AddLightCone(StadiumKit.MeshBuilder builder, Vector3 apex, Vector3 target, float radius, Color color)
    {
        const int segments = 24;
        Vector3 axis = (target - apex).normalized;
        Vector3 side = Vector3.Cross(axis, Vector3.up).normalized;
        Vector3 up = Vector3.Cross(side, axis).normalized;
        Color clear = new Color(color.r, color.g, color.b, 0f);
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.PI * 2f * i / segments, a1 = Mathf.PI * 2f * (i + 1) / segments;
            Vector3 p0 = target + (side * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * radius;
            Vector3 p1 = target + (side * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * radius;
            builder.AddTriangle(apex, p0, p1, color, clear, clear, -axis);
        }
    }

    /// <summary>大型ビジョン(奥の正面1面と左右の端に2面)。</summary>
    private void BuildScreens()
    {
        CreateScreen(new Vector3(0f, 15.5f, 33f), Quaternion.identity, new Vector2(20f, 8.5f));
        CreateScreen(new Vector3(-46f, 16f, 0f), Quaternion.Euler(0f, -90f, 0f), new Vector2(16f, 7f));
        CreateScreen(new Vector3(46f, 16f, 0f), Quaternion.Euler(0f, 90f, 0f), new Vector2(16f, 7f));
    }

    private void CreateScreen(Vector3 position, Quaternion rotation, Vector2 size)
    {
        Transform root = new GameObject("BigScreen").transform;
        root.SetParent(transform, false);
        root.SetPositionAndRotation(position, rotation);

        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.name = "Frame";
        Object.Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(root, false);
        frame.transform.localPosition = new Vector3(0f, 0f, 0.35f);
        frame.transform.localScale = new Vector3(size.x + 1f, size.y + 1f, 0.6f);
        frame.GetComponent<Renderer>().sharedMaterial = StadiumKit.Lit(new Color(0.05f, 0.05f, 0.07f), 0.5f, 0.6f);

        Mesh panel = StadiumKit.CreateStandingQuad(size.x, size.y);
        GameObject screen = StadiumKit.CreateMeshObject("Panel", root, panel,
            StadiumKit.Unlit(StadiumKit.Blend.Opaque, Color.white * 1.15f, CreateScreenTexture()));
        screen.transform.localPosition = new Vector3(0f, -size.y / 2f, 0f);

        CreateScreenText(root, size, screenTitles, 0.18f, 0.62f, true);
        CreateScreenText(root, size, screenSubtitles, -0.26f, 0.32f, false);
    }

    private static void CreateScreenText(Transform parent, Vector2 size, List<TextMeshPro> registry, float yRatio, float heightRatio, bool title)
    {
        TextMeshPro text = new GameObject(title ? "Title" : "Subtitle").AddComponent<TextMeshPro>();
        text.transform.SetParent(parent, false);
        text.transform.localPosition = new Vector3(0f, size.y * yRatio, -0.05f);
        text.rectTransform.sizeDelta = new Vector2(size.x * 0.92f, size.y * heightRatio * 0.6f);
        if (OrisamoUI.Font != null) text.font = OrisamoUI.Font;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = 1f;
        text.fontSizeMax = title ? 40f : 18f;
        text.fontStyle = title ? FontStyles.Bold : FontStyles.Normal;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.color = title ? OrisamoUI.GoldLight : OrisamoUI.Parchment;
        registry.Add(text);
    }

    // ==================== テクスチャ ====================

    /// <summary>観客のシルエット(頭+肩)。</summary>
    private static Texture2D CreatePersonTexture()
    {
        const int w = 64, h = 96;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        Color32[] pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = x + 0.5f - w / 2f;
                bool head = new Vector2(dx, y + 0.5f - 74f).magnitude < 13f;
                float shoulderHalf = Mathf.Lerp(30f, 24f, Mathf.InverseLerp(0f, 54f, y));
                bool body = y < 54f && Mathf.Abs(dx) < shoulderHalf && !(y > 44f && Mathf.Abs(dx) > shoulderHalf - (y - 44f) * 1.2f);
                // 上が明るく下が暗い陰影
                byte shade = (byte)(Mathf.Lerp(0.55f, 1f, (float)y / h) * 255f);
                pixels[y * w + x] = new Color32(shade, shade, shade, (byte)(head || body ? 255 : 0));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>LEDボードの流れる模様(矢羽根とグラデーション)。</summary>
    private static Texture2D CreateLedTexture()
    {
        const int w = 256, h = 32;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        Color32[] pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float chevron = Mathf.Repeat(x + Mathf.Abs(y - h / 2f) * 2.2f, 64f);
                float stripe = chevron < 20f ? 1f : chevron < 26f ? 0.45f : 0.12f;
                float scan = (y % 4 == 0) ? 0.75f : 1f; // LEDの走査線
                float wave = 0.75f + 0.25f * Mathf.Sin(x / (float)w * Mathf.PI * 2f);
                byte v = (byte)(Mathf.Clamp01(stripe * scan * wave) * 255f);
                pixels[y * w + x] = new Color32(v, v, v, 255);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>大型ビジョンの背景(紫〜紺のグラデーションと細かい格子)。</summary>
    private static Texture2D CreateScreenTexture()
    {
        const int w = 128, h = 64;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        Color32[] pixels = new Color32[w * h];
        Color top = new Color(0.30f, 0.12f, 0.45f), bottom = new Color(0.05f, 0.06f, 0.20f);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color c = Color.Lerp(bottom, top, (float)y / h);
                float edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                if (edge < 2f) c = Color.Lerp(c, OrisamoUI.Gold, 0.8f);
                if (x % 8 == 0 || y % 8 == 0) c *= 0.85f;
                c.a = 1f;
                pixels[y * w + x] = c;
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }
}

/// <summary>Y軸回りにゆっくり回し続ける(魔法陣・属性の光の演出用)。</summary>
public sealed class SimpleSpin : MonoBehaviour
{
    public float degreesPerSecond = 10f;

    private void Update()
    {
        transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
    }
}

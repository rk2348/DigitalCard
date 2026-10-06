using System.Collections;
using UnityEngine;

/// <summary>
/// 攻撃の属性ごとのエフェクト。素材ファイルを使わず、パーティクルと手続き生成のメッシュだけで作る。
///
///   火: 火球の爆発 + 火の粉 + 黒煙 + 炎の輪
///   水: 水しぶき + 波紋 + 泡
///   風: 竜巻 + 3本の斬撃
///   地: 地面から突き出す岩の槍 + 岩の破片 + 土煙
///   光: 天から降る光の柱 + 十字の閃光 + きらめき
///   闇: 周囲の闇を吸い込む収縮 → 闇の炸裂 + 黒い靄
///
/// どの属性でも、命中の瞬間に属性色の光(ポイントライト)と衝撃波を出す。
/// power は演出の強さ(1 = 通常。強攻撃・必殺技・効果抜群ほど大きくする)。
/// </summary>
public static class ElementalEffects
{
    // ==================== 属性の色 ====================

    /// <summary>エフェクト用の、属性色をより鮮やかにした色。</summary>
    public static Color EffectColor(ElementType element)
    {
        switch (element)
        {
            case ElementType.Fire: return new Color(1f, 0.42f, 0.08f);
            case ElementType.Water: return new Color(0.22f, 0.62f, 1f);
            case ElementType.Wind: return new Color(0.42f, 1f, 0.58f);
            case ElementType.Earth: return new Color(0.82f, 0.58f, 0.28f);
            case ElementType.Light: return new Color(1f, 0.9f, 0.55f);
            case ElementType.Dark: return new Color(0.62f, 0.25f, 1f);
            default: return Color.white;
        }
    }

    // ==================== 溜め(攻撃前) ====================

    /// <summary>攻撃する直前、足元の魔法陣と属性のオーラを立ち上らせる。</summary>
    public static void Charge(ElementType element, Vector3 ground, float height, float power)
    {
        Color color = EffectColor(element);
        StadiumKit.Host.StartCoroutine(RuneCircle(ground, color, 6f * Mathf.Sqrt(power), 0.75f));
        FlashLight(ground + Vector3.up * height * 0.5f, color, 3f * power, 10f, 0.7f);

        Vector3 center = ground + Vector3.up * 0.2f;
        switch (element)
        {
            case ElementType.Fire:
                Aura(center, color, Blend(StadiumKit.Blend.Additive), 70, 2.4f, 5.5f, 0.5f, 1.1f, -0.25f, 0f);
                break;
            case ElementType.Water:
                Aura(center, color, BubbleMaterial(), 45, 2.2f, 3f, 0.25f, 0.55f, -0.1f, 0f);
                break;
            case ElementType.Wind:
                Aura(center, color, Blend(StadiumKit.Blend.Additive), 80, 2.6f, 4f, 0.25f, 0.5f, 0f, 6f, stretch: true);
                break;
            case ElementType.Earth:
                Debris(ground + Vector3.up * 0.3f, color, (int)(14 * power), 2.5f, -0.35f, 1.2f, 0.12f, 0.35f);
                Aura(center, Color.Lerp(color, Color.white, 0.2f), Blend(StadiumKit.Blend.Additive), 40, 2.2f, 2.5f, 0.3f, 0.6f, -0.05f, 0f);
                break;
            case ElementType.Light:
                Aura(center, color, Blend(StadiumKit.Blend.Additive), 70, 2.2f, 6f, 0.15f, 0.4f, -0.2f, 2f);
                break;
            case ElementType.Dark:
                Aura(center, new Color(0.12f, 0.04f, 0.2f, 0.85f), Blend(StadiumKit.Blend.Alpha), 40, 2.4f, 2.4f, 1f, 2f, -0.1f, 1.5f);
                Aura(center, color, Blend(StadiumKit.Blend.Additive), 40, 2f, 3.5f, 0.2f, 0.5f, -0.1f, 3f);
                break;
        }
    }

    // ==================== 命中 ====================

    /// <summary>命中した瞬間のエフェクト。chest は被弾した側の胸の高さ、ground は足元。</summary>
    public static void Impact(ElementType element, Vector3 chest, Vector3 ground, float power)
    {
        Color color = EffectColor(element);
        FlashLight(chest, color, 9f * power, 18f, 0.45f);
        StadiumKit.Shockwave(ground, color, 6f + 4f * power, 0.6f);
        StadiumKit.Burst(chest, Color.Lerp(color, Color.white, 0.5f), (int)(30 * power), 10f * power, 0.35f);

        switch (element)
        {
            case ElementType.Fire: FireImpact(chest, ground, color, power); break;
            case ElementType.Water: WaterImpact(chest, ground, color, power); break;
            case ElementType.Wind: WindImpact(chest, ground, color, power); break;
            case ElementType.Earth: EarthImpact(chest, ground, color, power); break;
            case ElementType.Light: LightImpact(chest, ground, color, power); break;
            case ElementType.Dark: StadiumKit.Host.StartCoroutine(DarkImpact(chest, ground, color, power)); break;
        }
    }

    /// <summary>
    /// ヒットストップ(命中の瞬間に一瞬だけ時間を止めて、打撃の重さを出す)。
    /// </summary>
    public static void HitStop(float seconds)
    {
        StadiumKit.Host.StartCoroutine(HitStopRoutine(seconds));
    }

    private static IEnumerator HitStopRoutine(float seconds)
    {
        float previous = Time.timeScale;
        Time.timeScale = 0.04f;
        yield return new WaitForSecondsRealtime(seconds);
        Time.timeScale = previous <= 0.05f ? 1f : previous;
    }

    // ---------- 火 ----------

    private static void FireImpact(Vector3 chest, Vector3 ground, Color color, float power)
    {
        // 火球: 黄色→橙→赤へ色が変わりながら膨らむ炎
        ParticleSystem fireball = Particles("Fireball", chest, Blend(StadiumKit.Blend.Additive), (int)(55 * power));
        ParticleSystem.MainModule main = fireball.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f * power, 8f * power);
        main.startSize = new ParticleSystem.MinMaxCurve(1.2f * power, 2.6f * power);
        main.gravityModifier = -0.35f;
        main.startColor = Color.white;
        SphereShape(fireball, 0.6f);
        ColorOverLife(fireball, StadiumKit.Hdr(new Color(1f, 0.95f, 0.6f), 5f), StadiumKit.Hdr(new Color(1f, 0.45f, 0.08f), 3.5f), StadiumKit.Hdr(new Color(0.7f, 0.08f, 0.02f), 1.5f));
        GrowOverLife(fireball, 0.6f, 1.4f);
        fireball.Play();

        // 火の粉
        ParticleSystem embers = Particles("Embers", chest, Blend(StadiumKit.Blend.Additive), (int)(70 * power));
        main = embers.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 14f * power);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(StadiumKit.Hdr(new Color(1f, 0.75f, 0.3f), 5f), StadiumKit.Hdr(new Color(1f, 0.35f, 0.05f), 4f));
        main.gravityModifier = 0.5f;
        SphereShape(embers, 0.4f);
        Stretch(embers, 0.06f);
        Noise(embers, 1.2f, 1.5f);
        StadiumKit.FadeOut(embers);
        embers.Play();

        Smoke(chest + Vector3.up * 0.8f, new Color(0.12f, 0.1f, 0.1f, 0.75f), (int)(14 * power), 2.2f * power);
        StadiumKit.Shockwave(ground, new Color(1f, 0.55f, 0.15f), 9f * power, 0.75f);
        StadiumKit.Pillar(ground, color, 7f * power, 4f * power, 0.6f);
    }

    // ---------- 水 ----------

    private static void WaterImpact(Vector3 chest, Vector3 ground, Color color, float power)
    {
        // 水しぶき: 上向きの円錐に勢いよく飛び、重力で落ちる
        ParticleSystem splash = Particles("Splash", ground + Vector3.up * 0.5f, Blend(StadiumKit.Blend.Additive), (int)(140 * power));
        ParticleSystem.MainModule main = splash.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 15f * power);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
        main.startColor = new ParticleSystem.MinMaxGradient(StadiumKit.Hdr(new Color(0.75f, 0.9f, 1f), 3f), StadiumKit.Hdr(color, 2.5f));
        main.gravityModifier = 2.4f;
        ParticleSystem.ShapeModule shape = splash.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 38f;
        shape.radius = 1.4f;
        shape.rotation = new Vector3(-90f, 0f, 0f); // 円錐を上へ向ける
        Stretch(splash, 0.05f);
        StadiumKit.FadeOut(splash);
        splash.Play();

        // 胸の高さで砕ける水の塊
        ParticleSystem burst = Particles("WaterBurst", chest, Blend(StadiumKit.Blend.Additive), (int)(40 * power));
        main = burst.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f * power);
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.8f * power);
        main.startColor = StadiumKit.Hdr(color, 2f);
        SphereShape(burst, 0.5f);
        StadiumKit.FadeOut(burst);
        burst.Play();

        // 泡
        Aura(chest, color, BubbleMaterial(), (int)(30 * power), 1.6f, 2.5f, 0.2f, 0.55f, -0.3f, 0f);

        // 波紋: 3重の輪が少しずつずれて広がる
        StadiumKit.Host.StartCoroutine(Ripples(ground, color, power));
    }

    private static IEnumerator Ripples(Vector3 ground, Color color, float power)
    {
        for (int i = 0; i < 3; i++)
        {
            StadiumKit.Host.StartCoroutine(RingRoutine(ground + Vector3.up * 0.02f * i, Color.Lerp(color, Color.white, 0.3f),
                OrisamoUI.ThinRing.texture, (5f + i * 2.5f) * power, 0.9f));
            yield return new WaitForSeconds(0.13f);
        }
    }

    // ---------- 風 ----------

    private static void WindImpact(Vector3 chest, Vector3 ground, Color color, float power)
    {
        // 竜巻: 足元から渦を巻いて立ち上る筋
        ParticleSystem tornado = Particles("Tornado", ground + Vector3.up * 0.2f, Blend(StadiumKit.Blend.Additive), 0);
        ParticleSystem.MainModule main = tornado.main;
        main.duration = 0.6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(StadiumKit.Hdr(Color.Lerp(color, Color.white, 0.5f), 3f), StadiumKit.Hdr(color, 2.5f));
        ParticleSystem.ShapeModule shape = tornado.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1.8f * power;
        shape.rotation = new Vector3(90f, 0f, 0f);
        ParticleSystem.VelocityOverLifetimeModule velocity = tornado.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(0f);
        velocity.y = new ParticleSystem.MinMaxCurve(9f * power);
        velocity.z = new ParticleSystem.MinMaxCurve(0f);
        velocity.orbitalY = new ParticleSystem.MinMaxCurve(9f);
        velocity.radial = new ParticleSystem.MinMaxCurve(0.6f);
        Stretch(tornado, 0.09f);
        StadiumKit.FadeOut(tornado);
        ParticleSystem.EmissionModule emission = tornado.emission;
        emission.rateOverTime = 260f * power;
        tornado.Play();

        // 斬撃: 3本の光の筋が胸の前を素早く横切る
        StadiumKit.Host.StartCoroutine(Slashes(chest, color, power));

        // 飛び散る風の粒
        ParticleSystem gust = Particles("Gust", chest, Blend(StadiumKit.Blend.Additive), (int)(50 * power));
        main = gust.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 20f * power);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startColor = StadiumKit.Hdr(Color.Lerp(color, Color.white, 0.6f), 3f);
        SphereShape(gust, 0.5f);
        Stretch(gust, 0.08f);
        StadiumKit.FadeOut(gust);
        gust.Play();
    }

    private static IEnumerator Slashes(Vector3 chest, Color color, float power)
    {
        float[] angles = { 35f, -30f, 10f };
        for (int i = 0; i < angles.Length; i++)
        {
            Vector3 offset = new Vector3(0f, (i - 1) * 0.7f, 0f);
            StadiumKit.Host.StartCoroutine(Streak(chest + offset, angles[i], Color.Lerp(color, Color.white, 0.4f), 7f * power, 0.32f, 0.35f));
            yield return new WaitForSeconds(0.07f);
        }
    }

    // ---------- 地 ----------

    private static void EarthImpact(Vector3 chest, Vector3 ground, Color color, float power)
    {
        // 岩の槍: 足元の周りから斜めに突き出す
        int spikes = Mathf.RoundToInt(7 * power);
        for (int i = 0; i < spikes; i++)
        {
            float angle = (i + Random.value * 0.5f) / spikes * Mathf.PI * 2f;
            float radius = Random.Range(1.6f, 3.2f);
            Vector3 position = ground + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            float height = Random.Range(2.2f, 4.2f) * power;
            StadiumKit.Host.StartCoroutine(RockSpike(position, ground, height, Random.Range(0.5f, 0.9f), i * 0.025f));
        }

        Debris(chest, color, (int)(26 * power), 11f * power, 2.2f, 1.6f, 0.15f, 0.45f);
        Smoke(ground + Vector3.up * 0.6f, new Color(0.45f, 0.36f, 0.25f, 0.7f), (int)(16 * power), 2.6f * power);
        StadiumKit.Shockwave(ground, new Color(1f, 0.75f, 0.4f), 11f * power, 0.5f);
    }

    private static IEnumerator RockSpike(Vector3 position, Vector3 center, float height, float width, float delay)
    {
        yield return new WaitForSeconds(delay);
        GameObject spike = StadiumKit.CreateMeshObject("RockSpike", null, SpikeMesh(), StadiumKit.Lit(new Color(0.42f, 0.34f, 0.27f), 0.15f), true);
        // 外側へ少し傾けて、中心から突き上げたように見せる
        Vector3 outward = position - center;
        outward.y = 0f;
        Quaternion tilt = Quaternion.AngleAxis(Random.Range(14f, 28f), Vector3.Cross(Vector3.up, outward.normalized));
        spike.transform.rotation = tilt * Quaternion.Euler(0f, Random.value * 360f, 0f);
        Vector3 scale = new Vector3(width, height, width);

        const float rise = 0.12f;
        for (float t = 0f; t < rise; t += Time.deltaTime)
        {
            float p = OrisamoUI.EaseOutBack(t / rise);
            spike.transform.position = position + Vector3.down * height * (1f - p) * 0.6f;
            spike.transform.localScale = new Vector3(scale.x, scale.y * Mathf.Max(0.05f, p), scale.z);
            yield return null;
        }
        spike.transform.position = position;
        spike.transform.localScale = scale;
        yield return new WaitForSeconds(0.55f);
        const float sink = 0.35f;
        for (float t = 0f; t < sink; t += Time.deltaTime)
        {
            float p = t / sink;
            spike.transform.position = position + Vector3.down * height * p;
            yield return null;
        }
        Object.Destroy(spike);
    }

    private static Mesh spikeMesh;

    /// <summary>底面が五角形の、先の尖った岩(高さ1・底面の半径0.5)。</summary>
    private static Mesh SpikeMesh()
    {
        if (spikeMesh != null) return spikeMesh;
        StadiumKit.MeshBuilder builder = new StadiumKit.MeshBuilder();
        const int sides = 5;
        Vector3 tip = new Vector3(0.08f, 1f, -0.05f);
        for (int i = 0; i < sides; i++)
        {
            float a0 = Mathf.PI * 2f * i / sides, a1 = Mathf.PI * 2f * (i + 1) / sides;
            Vector3 b0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 0.5f;
            Vector3 b1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 0.5f;
            Vector3 normal = Vector3.Cross(b1 - b0, tip - b0).normalized;
            if (Vector3.Dot(normal, (b0 + b1) * 0.5f) < 0f) normal = -normal;
            builder.AddTriangle(b0, tip, b1, Color.white, Color.white, Color.white, normal);
        }
        spikeMesh = builder.Build();
        spikeMesh.RecalculateNormals();
        return spikeMesh;
    }

    // ---------- 光 ----------

    private static void LightImpact(Vector3 chest, Vector3 ground, Color color, float power)
    {
        StadiumKit.Pillar(ground, color, 22f, 2.6f * power, 0.9f);
        StadiumKit.Pillar(ground, Color.white, 22f, 1.1f * power, 0.6f);
        StadiumKit.Host.StartCoroutine(CrossFlare(chest, color, power));

        // きらめき: 長く漂う星の粒
        ParticleSystem sparkle = Particles("Sparkle", chest, Blend(StadiumKit.Blend.Additive), (int)(60 * power));
        ParticleSystem.MainModule main = sparkle.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 7f * power);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startColor = new ParticleSystem.MinMaxGradient(StadiumKit.Hdr(Color.white, 5f), StadiumKit.Hdr(color, 4f));
        main.gravityModifier = -0.05f;
        SphereShape(sparkle, 1f);
        Noise(sparkle, 0.8f, 1f);
        StadiumKit.FadeOut(sparkle);
        sparkle.Play();

        StadiumKit.Host.StartCoroutine(RingRoutine(ground + Vector3.up * 0.04f, Color.white, OrisamoUI.ThinRing.texture, 12f * power, 0.8f));
    }

    private static IEnumerator CrossFlare(Vector3 chest, Color color, float power)
    {
        StadiumKit.Host.StartCoroutine(Streak(chest, 0f, Color.white, 12f * power, 0.5f, 0.55f, centered: true));
        StadiumKit.Host.StartCoroutine(Streak(chest, 90f, Color.white, 9f * power, 0.45f, 0.55f, centered: true));
        yield return new WaitForSeconds(0.05f);
        StadiumKit.Host.StartCoroutine(Streak(chest, 45f, color, 6f * power, 0.3f, 0.45f, centered: true));
        StadiumKit.Host.StartCoroutine(Streak(chest, -45f, color, 6f * power, 0.3f, 0.45f, centered: true));
    }

    // ---------- 闇 ----------

    private static IEnumerator DarkImpact(Vector3 chest, Vector3 ground, Color color, float power)
    {
        // 収縮: 周囲から闇の粒が吸い込まれる
        ParticleSystem implode = Particles("Implode", chest, Blend(StadiumKit.Blend.Additive), (int)(80 * power));
        ParticleSystem.MainModule main = implode.main;
        main.startLifetime = 0.32f;
        main.startSpeed = -10f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        main.startColor = StadiumKit.Hdr(color, 3f);
        ParticleSystem.ShapeModule shape = implode.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 3.4f;
        shape.radiusThickness = 0f;
        Stretch(implode, 0.05f);
        implode.Play();

        // 黒い球が膨らむ
        StadiumKit.Host.StartCoroutine(VoidSphere(chest, power));
        yield return new WaitForSeconds(0.3f);

        // 炸裂
        FlashLight(chest, color, 10f * power, 18f, 0.4f);
        StadiumKit.Burst(chest, color, (int)(70 * power), 13f * power, 0.5f);
        Smoke(chest, new Color(0.08f, 0.02f, 0.12f, 0.85f), (int)(18 * power), 2.6f * power);
        StadiumKit.Shockwave(ground, color, 12f * power, 0.7f);
        StadiumKit.Host.StartCoroutine(RingRoutine(ground + Vector3.up * 0.05f, new Color(0.35f, 0.1f, 0.6f), OrisamoUI.ThickRing.texture, 8f * power, 0.9f));
    }

    private static IEnumerator VoidSphere(Vector3 chest, float power)
    {
        Material core = StadiumKit.Unlit(StadiumKit.Blend.Alpha, new Color(0.02f, 0f, 0.05f, 0.95f), OrisamoUI.SoftGlow.texture);
        Material rim = StadiumKit.Unlit(StadiumKit.Blend.Additive, StadiumKit.Hdr(new Color(0.6f, 0.2f, 1f), 2.5f), OrisamoUI.ThickRing.texture);
        GameObject root = new GameObject("VoidSphere");
        root.transform.position = chest;
        GameObject coreQuad = Billboard(root.transform, core);
        GameObject rimQuad = Billboard(root.transform, rim);
        coreQuad.transform.localPosition = Vector3.zero;
        rimQuad.transform.localPosition = Vector3.zero;
        Color coreColor = core.GetColor("_BaseColor");
        Color rimColor = rim.GetColor("_BaseColor");

        const float duration = 0.7f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = t / duration;
            float size = p < 0.45f ? Mathf.Lerp(0.3f, 3.2f * power, OrisamoUI.EaseOutCubic(p / 0.45f)) : Mathf.Lerp(3.2f * power, 5f * power, (p - 0.45f) / 0.55f);
            root.transform.localScale = Vector3.one * size;
            float fade = p < 0.45f ? 1f : 1f - (p - 0.45f) / 0.55f;
            core.SetColor("_BaseColor", new Color(coreColor.r, coreColor.g, coreColor.b, coreColor.a * fade));
            rim.SetColor("_BaseColor", rimColor * fade);
            FaceCamera(root.transform);
            yield return null;
        }
        Object.Destroy(root);
    }

    // ==================== 共通の部品 ====================

    private static Material Blend(StadiumKit.Blend blend) => StadiumKit.Unlit(blend, Color.white, OrisamoUI.SoftGlow.texture);

    private static Material BubbleMaterial() => StadiumKit.Unlit(StadiumKit.Blend.Additive, Color.white, OrisamoUI.ThickRing.texture);

    private static ParticleSystem Particles(string name, Vector3 position, Material material, int burstCount)
    {
        ParticleSystem ps = StadiumKit.CreateParticleSystem(name, position, material);
        ParticleSystem.MainModule main = ps.main;
        main.maxParticles = 2000;
        if (burstCount > 0)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burstCount) });
        }
        return ps;
    }

    /// <summary>足元から立ち上るオーラ(溜めの演出)。orbital が0より大きいと渦を巻く。</summary>
    private static void Aura(Vector3 center, Color color, Material material, int count, float radius, float riseSpeed,
        float minSize, float maxSize, float gravity, float orbital, bool stretch = false)
    {
        ParticleSystem ps = Particles("Aura", center, material, 0);
        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.4f); // 円周から少しだけ外へ
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = color.a < 1f ? color : (Color)StadiumKit.Hdr(color, 2.8f);
        main.gravityModifier = gravity;
        // 足元に水平な円を置き、そこから上昇させる(Circleは円の面内で外向きに放出するため、上昇は速度で与える)
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.rotation = new Vector3(90f, 0f, 0f);
        ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(riseSpeed * 0.5f, riseSpeed);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        if (orbital > 0f) velocity.orbitalY = new ParticleSystem.MinMaxCurve(orbital);
        if (stretch) Stretch(ps, 0.08f);
        StadiumKit.FadeOut(ps);
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = count / main.duration;
        ps.Play();
    }

    /// <summary>岩の破片(立方体のメッシュのパーティクル)。</summary>
    private static void Debris(Vector3 position, Color color, int count, float speed, float gravity, float lifetime, float minSize, float maxSize)
    {
        ParticleSystem ps = Particles("Debris", position, StadiumKit.Lit(Color.Lerp(new Color(0.35f, 0.28f, 0.22f), color, 0.25f), 0.1f), count);
        ParticleSystem.MainModule main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = gravity;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.6f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.separateAxes = true;
        rotation.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
        rotation.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
        rotation.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        ps.Play();
    }

    /// <summary>ゆっくり膨らみながら昇っていく煙。</summary>
    private static void Smoke(Vector3 position, Color color, int count, float size)
    {
        ParticleSystem ps = Particles("Smoke", position, Blend(StadiumKit.Blend.Alpha), count);
        ParticleSystem.MainModule main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.startColor = color;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.12f;
        SphereShape(ps, 0.8f);
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = gradient;
        GrowOverLife(ps, 0.6f, 1.8f);
        ps.Play();
    }

    private static void SphereShape(ParticleSystem ps, float radius)
    {
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;
    }

    private static void Stretch(ParticleSystem ps, float velocityScale)
    {
        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = velocityScale;
        renderer.lengthScale = 1.5f;
    }

    private static void Noise(ParticleSystem ps, float strength, float frequency)
    {
        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = strength;
        noise.frequency = frequency;
    }

    private static void ColorOverLife(ParticleSystem ps, Color start, Color middle, Color end)
    {
        // HDRの明るさはstartColorに掛けられないため、粒の色(白)にグラデーションを掛け、
        // 明るさはマテリアルの色で持ち上げる
        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial.SetColor("_BaseColor", Color.white * Mathf.Max(start.maxColorComponent, 1f));
        float scale = 1f / Mathf.Max(start.maxColorComponent, 1f);
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(start * scale, 0f), new GradientColorKey(middle * scale, 0.35f), new GradientColorKey(end * scale, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = gradient;
    }

    private static void GrowOverLife(ParticleSystem ps, float from, float to)
    {
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from, 1f, to));
    }

    /// <summary>属性色のポイントライトを一瞬灯して、周りの床やキャラクターを照らす。</summary>
    public static void FlashLight(Vector3 position, Color color, float intensity, float range, float duration)
    {
        StadiumKit.Host.StartCoroutine(FlashLightRoutine(position, color, intensity, range, duration));
    }

    private static IEnumerator FlashLightRoutine(Vector3 position, Color color, float intensity, float range, float duration)
    {
        Light light = new GameObject("FlashLight").AddComponent<Light>();
        light.type = LightType.Point;
        light.transform.position = position;
        light.color = color;
        light.range = range;
        light.shadows = LightShadows.None;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = t / duration;
            light.intensity = intensity * (p < 0.15f ? p / 0.15f : 1f - (p - 0.15f) / 0.85f);
            yield return null;
        }
        Object.Destroy(light.gameObject);
    }

    /// <summary>足元に浮かんで回る魔法陣(溜めの演出)。</summary>
    private static IEnumerator RuneCircle(Vector3 ground, Color color, float size, float duration)
    {
        Material outer = StadiumKit.Unlit(StadiumKit.Blend.Additive, StadiumKit.Hdr(color, 3f), OrisamoUI.ThinRing.texture);
        Material inner = StadiumKit.Unlit(StadiumKit.Blend.Additive, StadiumKit.Hdr(color, 2.2f), OrisamoUI.ThickRing.texture);
        Material glow = StadiumKit.Unlit(StadiumKit.Blend.Additive, OrisamoUI.WithAlpha(color, 0.6f), OrisamoUI.SoftGlow.texture);
        GameObject root = new GameObject("RuneCircle");
        root.transform.position = ground + Vector3.up * 0.1f;
        GameObject ring = StadiumKit.CreateFlatDecal("Outer", root.transform, size, outer);
        GameObject core = StadiumKit.CreateFlatDecal("Inner", root.transform, size * 0.62f, inner);
        StadiumKit.CreateFlatDecal("Glow", root.transform, size * 1.3f, glow).transform.localPosition = Vector3.down * 0.01f;
        Color[] baseColors = { outer.GetColor("_BaseColor"), inner.GetColor("_BaseColor"), glow.GetColor("_BaseColor") };
        Material[] materials = { outer, inner, glow };

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = t / duration;
            float scale = Mathf.Lerp(0.4f, 1f, OrisamoUI.EaseOutCubic(Mathf.Min(1f, p * 3f)));
            root.transform.localScale = Vector3.one * scale;
            ring.transform.localRotation = Quaternion.Euler(0f, p * 160f, 0f);
            core.transform.localRotation = Quaternion.Euler(0f, -p * 240f, 0f);
            float fade = p < 0.7f ? 1f : 1f - (p - 0.7f) / 0.3f;
            for (int i = 0; i < materials.Length; i++) materials[i].SetColor("_BaseColor", baseColors[i] * fade);
            yield return null;
        }
        Object.Destroy(root);
    }

    /// <summary>地面に沿って広がる輪(テクスチャ指定版)。</summary>
    private static IEnumerator RingRoutine(Vector3 position, Color color, Texture texture, float radius, float duration)
    {
        Material material = StadiumKit.Unlit(StadiumKit.Blend.Additive, StadiumKit.Hdr(color, 2.5f), texture);
        GameObject ring = StadiumKit.CreateFlatDecal("Ring", null, 1f, material);
        ring.transform.position = position + Vector3.up * 0.1f;
        Color baseColor = material.GetColor("_BaseColor");
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = OrisamoUI.EaseOutCubic(t / duration);
            ring.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, radius * 2f, p);
            material.SetColor("_BaseColor", baseColor * (1f - p));
            yield return null;
        }
        Object.Destroy(ring);
    }

    /// <summary>
    /// カメラに向いた細長い光の筋(斬撃・十字の閃光)。angle はカメラから見た傾き(度)。
    /// centered なら中心から両側へ伸び、そうでなければ片側から反対側へ走り抜ける。
    /// </summary>
    private static IEnumerator Streak(Vector3 center, float angle, Color color, float length, float thickness, float duration, bool centered = false)
    {
        Material material = StadiumKit.Unlit(StadiumKit.Blend.Additive, StadiumKit.Hdr(color, 4f), OrisamoUI.SoftGlow.texture);
        GameObject root = new GameObject("Streak");
        root.transform.position = center;
        GameObject quad = Billboard(root.transform, material);
        Color baseColor = material.GetColor("_BaseColor");

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = t / duration;
            FaceCamera(root.transform);
            root.transform.Rotate(0f, 0f, angle, Space.Self);
            float grow = OrisamoUI.EaseOutCubic(Mathf.Min(1f, p * 3f));
            float width = length * grow;
            quad.transform.localScale = new Vector3(width, thickness * (1f - p * 0.6f), 1f);
            quad.transform.localPosition = centered ? Vector3.zero : new Vector3(Mathf.Lerp(-length * 0.5f, 0f, grow), 0f, 0f);
            material.SetColor("_BaseColor", baseColor * (1f - p));
            yield return null;
        }
        Object.Destroy(root);
    }

    /// <summary>1×1の板(親をカメラへ向けて使う)。</summary>
    private static GameObject Billboard(Transform parent, Material material)
    {
        StadiumKit.MeshBuilder builder = new StadiumKit.MeshBuilder();
        builder.AddQuad(new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            Vector3.back, Color.white);
        return StadiumKit.CreateMeshObject("Billboard", parent, builder.Build(), material);
    }

    private static void FaceCamera(Transform transform)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
    }
}

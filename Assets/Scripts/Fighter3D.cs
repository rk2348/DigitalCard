using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// スタジアムに立つキャラクター。
/// スマホで撮影・背景切り抜きした写真(透過PNG)を PhotoSculpture で厚みのある立体にして立たせる。
/// ライティングと影で立体感が出るよう URP Lit で描き、カメラの方へゆっくり向き直りつつ、
/// 相手の方へ少し体を向け、左右にわずかに揺らして厚みが見えるようにする。
/// 同じ形を少し大きくして裏側だけを属性色で光らせ、輪郭が光るオーラにする。
/// 写真が無い場合(デモ対戦など)は DemoCharacterArt のイラストを同じ方法で立体化する。
/// 足元には属性色の光る台座と影を置く。演出(登場・突進・被弾・防御・必殺・ダウン・勝利)はコルーチンで呼ぶ。
/// </summary>
public sealed class Fighter3D : MonoBehaviour
{
    public CharacterStats Stats { get; private set; }
    public bool IsPlayer1 { get; private set; }
    public float Height { get; private set; }
    public Vector3 HeadPoint => visual.position + Vector3.up * (Height + 0.4f);
    public Vector3 ChestPoint => visual.position + Vector3.up * (Height * 0.55f);
    public Vector3 Home => transform.position;

    /// <summary>相手の方へ体を向ける角度(度)。正面だけだと厚みが見えないため。</summary>
    private const float TurnTowardOpponent = 24f;

    private Transform visual;
    private Transform figure;
    private readonly List<Material> tintMaterials = new List<Material>();
    private readonly List<Color> baseColors = new List<Color>();
    private Material ringMaterial;
    private Material auraMaterial;
    private Color elementColor;
    private Color ringColor;
    private bool busy;
    private bool fainted;
    private bool appeared;
    private float idlePhase;

    /// <summary>キャラクターを生成して spot に立たせる。最初は非表示(PlayEntranceで登場させる)。</summary>
    public static Fighter3D Create(CharacterStats stats, Vector3 spot, bool isPlayer1, Transform parent)
    {
        Fighter3D fighter = new GameObject("Fighter_" + stats.characterName).AddComponent<Fighter3D>();
        fighter.transform.SetParent(parent, false);
        fighter.transform.position = spot;
        fighter.Initialize(stats, isPlayer1);
        return fighter;
    }

    private void Initialize(CharacterStats stats, bool isPlayer1)
    {
        Stats = stats;
        IsPlayer1 = isPlayer1;
        elementColor = ElementAffinity.GetElementColor(stats.element);
        ringColor = elementColor;
        idlePhase = UnityEngine.Random.value * 10f;

        // 台座(光るリングと光だまり)と影
        ringMaterial = StadiumKit.Unlit(StadiumKit.Blend.Additive, StadiumKit.Hdr(elementColor, 2.2f), OrisamoUI.ThickRing.texture);
        StadiumKit.CreateFlatDecal("PlatformRing", transform, 5.4f, ringMaterial).transform.localPosition = Vector3.up * 0.06f;
        Material poolMaterial = StadiumKit.Unlit(StadiumKit.Blend.Additive, OrisamoUI.WithAlpha(elementColor, 0.55f), OrisamoUI.SoftGlow.texture);
        StadiumKit.CreateFlatDecal("PlatformGlow", transform, 8f, poolMaterial).transform.localPosition = Vector3.up * 0.055f;
        Material shadowMaterial = StadiumKit.Unlit(StadiumKit.Blend.Alpha, new Color(0f, 0f, 0f, 0.7f), OrisamoUI.SoftGlow.texture);
        StadiumKit.CreateFlatDecal("Shadow", transform, 3.4f, shadowMaterial).transform.localPosition = Vector3.up * 0.07f;

        visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);

        Texture2D texture = stats.photoSprite != null && stats.photoSprite.texture != null
            ? stats.photoSprite.texture
            : DemoCharacterArt.Create(stats);
        BuildSculpture(texture);

        visual.localScale = Vector3.zero;
    }

    /// <summary>写真を立体化して立たせる。</summary>
    private void BuildSculpture(Texture2D texture)
    {
        PhotoSculpture.Result sculpture = PhotoSculpture.Build(texture, 4.4f, 5.6f);
        Height = sculpture.height;

        figure = new GameObject("Figure").transform;
        figure.SetParent(visual, false);
        figure.localRotation = FacingRotation(0f);

        Material material = PhotoSculpture.CreateMaterial(texture);
        GameObject body = StadiumKit.CreateMeshObject("Sculpture", figure, sculpture.mesh, material, true);
        RegisterTint(material);

        // 輪郭のオーラ: 同じ形を少し大きくし、裏側の面だけを属性色で加算描画する(本体に隠れない縁だけが光る)
        auraMaterial = StadiumKit.Unlit(StadiumKit.Blend.Additive, StadiumKit.Hdr(elementColor, 1.8f), texture);
        auraMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);
        GameObject aura = StadiumKit.CreateMeshObject("Aura", figure, sculpture.mesh, auraMaterial);
        aura.transform.localScale = new Vector3(1.07f, 1.05f, 1.6f);
        aura.transform.localPosition = new Vector3(0f, -Height * 0.025f, 0f);
        body.transform.SetAsLastSibling();
    }

    private void RegisterTint(Material material)
    {
        tintMaterials.Add(material);
        baseColors.Add(material.GetColor("_BaseColor"));
    }

    private void Update()
    {
        if (busy || fainted || visual == null) return;
        float t = Time.time + idlePhase;
        visual.localPosition = Vector3.up * (0.12f + Mathf.Sin(t * 1.8f) * 0.1f);
        float breathe = 1f + Mathf.Sin(t * 3.6f) * 0.012f;
        if (appeared) visual.localScale = new Vector3(1f / breathe, breathe, 1f);

        if (ringMaterial != null)
        {
            float pulse = 1.8f + Mathf.Sin(t * 2.4f) * 0.6f;
            ringMaterial.SetColor("_BaseColor", Color.Lerp(ringMaterial.GetColor("_BaseColor"), StadiumKit.Hdr(ringColor, pulse), Time.deltaTime * 8f));
        }
        if (auraMaterial != null)
        {
            // レアカード(突然変異)は輪郭のオーラが虹色に移り変わる
            Color auraColor = Stats.isMutation ? Color.HSVToRGB(Mathf.Repeat(t * 0.22f, 1f), 0.65f, 1f) : elementColor;
            auraMaterial.SetColor("_BaseColor", StadiumKit.Hdr(auraColor, (Stats.isMutation ? 2f : 1.4f) + Mathf.Sin(t * 2.4f) * 0.5f));
        }
    }

    private void LateUpdate()
    {
        if (figure == null || fainted) return;
        // カメラの方へ向き直りつつ、相手の方へ少し体を向け、ゆっくり左右に揺らして厚みを見せる
        float sway = Mathf.Sin((Time.time + idlePhase) * 0.9f) * 7f;
        figure.rotation = Quaternion.Slerp(figure.rotation, FacingRotation(sway), Time.deltaTime * 4f);
    }

    /// <summary>立体の表面(-Z)をカメラへ向け、相手の方へ TurnTowardOpponent 度ひねった向き。</summary>
    private Quaternion FacingRotation(float extraYaw)
    {
        Camera cam = Camera.main;
        Vector3 toCamera = cam != null ? cam.transform.position - figure.position : Vector3.back;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude < 0.001f) toCamera = Vector3.back;
        float turn = (IsPlayer1 ? -TurnTowardOpponent : TurnTowardOpponent) + extraYaw;
        return Quaternion.LookRotation(-toCamera) * Quaternion.Euler(0f, turn, 0f);
    }

    /// <summary>台座のリングの色を変える(攻撃側/防御側の目印)。nullで属性色に戻す。</summary>
    public void SetRoleColor(Color? color)
    {
        ringColor = color ?? elementColor;
    }

    // ==================== 演出 ====================

    /// <summary>光の柱とともに出現する。</summary>
    public IEnumerator PlayEntrance()
    {
        busy = true;
        if (Stats.isMutation)
        {
            // レアカード: 金の光の柱と火花が先に立ち、少し溜めてから登場する
            OrisamoAudio.Play(OrisamoAudio.Sfx.Rare);
            StadiumKit.Pillar(transform.position, OrisamoUI.Gold, 26f, 5.5f, 2.4f);
            StadiumKit.Burst(transform.position + Vector3.up * 3f, OrisamoUI.GoldLight, 120, 6f, 0.5f);
            StadiumKit.Shockwave(transform.position, OrisamoUI.Gold, 10f, 0.9f);
            yield return new WaitForSeconds(0.6f);
        }
        OrisamoAudio.Play(OrisamoAudio.Sfx.Charge, 0.7f, 0.8f);
        StadiumKit.Pillar(transform.position, elementColor, 16f, 3.6f, 1.6f);
        yield return new WaitForSeconds(0.25f);
        StadiumKit.Shockwave(transform.position, elementColor, 7f, 0.7f);
        StadiumKit.Burst(transform.position + Vector3.up * 1.5f, elementColor, 60, 10f);
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            float s = Mathf.Max(0f, OrisamoUI.EaseOutBack(t / 0.6f));
            visual.localScale = Vector3.one * s;
            yield return null;
        }
        visual.localScale = Vector3.one;
        appeared = true;
        busy = false;
    }

    /// <summary>相手に向かって突進する。ぶつかる瞬間に onImpact を呼ぶ。</summary>
    public IEnumerator PlayLunge(Vector3 targetPosition, Action onImpact)
    {
        busy = true;
        Vector3 start = visual.localPosition;
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;
        float reach = Mathf.Max(0f, direction.magnitude - 3.4f);
        direction.Normalize();

        // 溜め(少し下がって縮む)
        yield return Animate(0.2f, p =>
        {
            visual.localPosition = start - direction * (0.8f * OrisamoUI.EaseOutCubic(p));
            visual.localScale = new Vector3(1f + 0.1f * p, 1f - 0.12f * p, 1f);
        });
        // 突進
        Vector3 back = start - direction * 0.8f;
        yield return Animate(0.14f, p =>
        {
            visual.localPosition = Vector3.Lerp(back, start + direction * reach + Vector3.up * 0.4f, p * p);
            visual.localScale = new Vector3(1.12f, 0.92f, 1f);
        });
        onImpact?.Invoke();
        yield return new WaitForSeconds(0.12f);
        // 戻り
        Vector3 hit = visual.localPosition;
        yield return Animate(0.35f, p =>
        {
            float e = OrisamoUI.EaseOutCubic(p);
            visual.localPosition = Vector3.Lerp(hit, start, e) + Vector3.up * Mathf.Sin(p * Mathf.PI) * 0.8f;
            visual.localScale = Vector3.Lerp(new Vector3(1.12f, 0.92f, 1f), Vector3.one, e);
        });
        busy = false;
    }

    /// <summary>被弾: 赤く光り、攻撃された方向と反対へ弾かれる。</summary>
    public IEnumerator PlayHit(Vector3 attackerPosition, bool heavy)
    {
        busy = true;
        Vector3 start = visual.localPosition;
        Vector3 away = transform.position - attackerPosition;
        away.y = 0f;
        away.Normalize();
        float distance = heavy ? 1.8f : 1f;
        Color flash = heavy ? new Color(4f, 3.2f, 2.6f, 1f) : new Color(3f, 0.7f, 0.55f, 1f);

        yield return Animate(heavy ? 0.5f : 0.38f, p =>
        {
            float knock = p < 0.3f ? OrisamoUI.EaseOutCubic(p / 0.3f) : 1f - OrisamoUI.EaseOutCubic((p - 0.3f) / 0.7f);
            visual.localPosition = start + away * distance * knock + new Vector3(Mathf.Sin(p * 70f) * 0.08f * (1f - p), 0f, 0f);
            Tint(flash, 1f - p);
        });
        Tint(Color.white, 0f);
        busy = false;
    }

    /// <summary>防御成功: 前面に光の盾が開く。</summary>
    public IEnumerator PlayGuard()
    {
        Material shieldMaterial = StadiumKit.Unlit(StadiumKit.Blend.Additive, StadiumKit.Hdr(OrisamoUI.DefenseColor, 3f), OrisamoUI.ThickRing.texture);
        Material glowMaterial = StadiumKit.Unlit(StadiumKit.Blend.Additive, OrisamoUI.WithAlpha(OrisamoUI.DefenseColor, 0.8f), OrisamoUI.SoftGlow.texture);
        Transform shield = new GameObject("Shield").transform;
        shield.position = ChestPoint + Vector3.right * (IsPlayer1 ? 1.4f : -1.4f);
        Mesh quad = CenteredQuad();
        StadiumKit.CreateMeshObject("Ring", shield, quad, shieldMaterial);
        StadiumKit.CreateMeshObject("Glow", shield, quad, glowMaterial).transform.localScale = Vector3.one * 1.4f;
        shield.rotation = Quaternion.LookRotation(Vector3.right * (IsPlayer1 ? 1f : -1f));
        StadiumKit.Burst(shield.position, OrisamoUI.DefenseColor, 30, 6f, 0.35f);

        Color ringBase = shieldMaterial.GetColor("_BaseColor");
        Color glowBase = glowMaterial.GetColor("_BaseColor");
        yield return Animate(0.6f, p =>
        {
            shield.localScale = Vector3.one * Mathf.Lerp(1f, 4.4f, OrisamoUI.EaseOutCubic(p));
            float fade = 1f - p * p;
            shieldMaterial.SetColor("_BaseColor", ringBase * fade);
            glowMaterial.SetColor("_BaseColor", OrisamoUI.WithAlpha(glowBase, glowBase.a * fade));
        });
        Destroy(shield.gameObject);
    }

    /// <summary>必殺技の溜め: 光の柱が立ち、ひと回り大きくなる。</summary>
    public IEnumerator PlaySpecialCharge()
    {
        busy = true;
        OrisamoAudio.Play(OrisamoAudio.Sfx.Special);
        StadiumKit.Pillar(transform.position, elementColor, 20f, 4.4f, 1.3f);
        StadiumKit.Burst(ChestPoint, elementColor, 50, 7f);
        yield return Animate(0.9f, p =>
        {
            visual.localScale = Vector3.one * (1f + Mathf.Sin(p * Mathf.PI) * 0.18f);
            Tint(StadiumKit.Hdr(elementColor, 2.5f), Mathf.Sin(p * Mathf.PI) * 0.6f);
        });
        Tint(Color.white, 0f);
        visual.localScale = Vector3.one;
        busy = false;
    }

    /// <summary>
    /// 戦闘不能: 衝撃でひしゃげ → 攻撃と反対側へ放物線を描いて吹き飛びながら倒れる → 着地して跳ね返り、
    /// 少し滑って横たわる → 色が抜け、魂のような光の粒が昇る。体は消さずにフィールドに残す。
    /// 足元を支点に、上端が吹き飛ぶ向きへ倒れるよう回転させる。onLanded は最初に地面に落ちた瞬間に呼ぶ(カメラの揺れ用)。
    /// </summary>
    public IEnumerator PlayFaint(Vector3 attackerPosition, Action onLanded = null)
    {
        fainted = true;
        busy = true;

        Vector3 away = transform.position - attackerPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = IsPlayer1 ? Vector3.left : Vector3.right;
        away.Normalize();
        Vector3 awayLocal = transform.InverseTransformDirection(away);
        Vector3 axisLocal = transform.InverseTransformDirection(Vector3.Cross(Vector3.up, away));
        Vector3 start = visual.localPosition;
        Color impactFlash = new Color(4f, 3.6f, 3.2f, 1f);

        // A: 衝撃でひしゃげる
        yield return Animate(0.12f, p =>
        {
            visual.localScale = Vector3.Lerp(Vector3.one, new Vector3(1.18f, 0.82f, 1f), Mathf.Sin(p * Mathf.PI));
            Tint(impactFlash, 1f - p * 0.4f);
        });

        // B: 吹き飛びながら倒れる(横はすぐに勢いが落ち、縦は放物線)
        const float distance = 4.4f;
        const float peak = 2.4f;
        Vector3 landed = start + awayLocal * distance;
        landed.y = 0.25f; // 横たわった時に厚みの分だけ床から浮かせる
        yield return Animate(0.75f, p =>
        {
            Vector3 flat = Vector3.Lerp(start, landed, OrisamoUI.EaseOutCubic(p));
            visual.localPosition = flat + Vector3.up * (4f * peak * p * (1f - p));
            visual.localRotation = Quaternion.AngleAxis(90f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p * 1.15f)), axisLocal);
            visual.localScale = Vector3.Lerp(new Vector3(1.18f, 0.82f, 1f), Vector3.one, Mathf.Clamp01(p * 3f));
            Tint(impactFlash, 0.6f * (1f - p));
        });
        Tint(Color.white, 0f);

        // C: 着地 → 跳ね返り → 滑って止まる
        Vector3 landedWorld = transform.TransformPoint(new Vector3(landed.x, 0f, landed.z));
        StadiumKit.Burst(landedWorld + Vector3.up * 0.3f + away * (Height * 0.4f), new Color(0.78f, 0.72f, 0.62f), 60, 4.5f, 0.7f);
        StadiumKit.Shockwave(landedWorld + away * (Height * 0.4f), new Color(1f, 0.9f, 0.7f), 5f, 0.45f);
        OrisamoAudio.Play(OrisamoAudio.Sfx.Land);
        onLanded?.Invoke();

        Vector3 bounced = landed + awayLocal * 0.9f;
        yield return Animate(0.36f, p =>
        {
            float arc = Mathf.Sin(p * Mathf.PI);
            visual.localPosition = Vector3.Lerp(landed, bounced, OrisamoUI.EaseOutCubic(p)) + Vector3.up * (arc * 0.7f);
            visual.localRotation = Quaternion.AngleAxis(90f - arc * 14f, axisLocal);
        });
        StadiumKit.Burst(transform.TransformPoint(new Vector3(bounced.x, 0.3f, bounced.z)) + away * (Height * 0.4f), new Color(0.78f, 0.72f, 0.62f), 25, 3f, 0.5f);
        OrisamoAudio.Play(OrisamoAudio.Sfx.Land, 0.5f, 1.2f);

        Vector3 rest = bounced + awayLocal * 0.35f;
        yield return Animate(0.28f, p =>
        {
            float arc = Mathf.Sin(p * Mathf.PI);
            visual.localPosition = Vector3.Lerp(bounced, rest, OrisamoUI.EaseOutCubic(p)) + Vector3.up * (arc * 0.15f);
            visual.localRotation = Quaternion.AngleAxis(90f - arc * 4f, axisLocal);
        });
        visual.localPosition = rest;
        visual.localRotation = Quaternion.AngleAxis(90f, axisLocal);

        // D: 色が抜けて、光の粒が昇る(体はそのまま横たわらせておく)
        SetRoleColor(new Color(0.25f, 0.25f, 0.3f));
        Color grey = new Color(0.35f, 0.35f, 0.42f, 1f);
        Color auraStart = auraMaterial != null ? auraMaterial.GetColor("_BaseColor") : Color.black;
        yield return Animate(0.7f, p =>
        {
            Tint(grey, 0.75f * p);
            if (auraMaterial != null) auraMaterial.SetColor("_BaseColor", Color.Lerp(auraStart, Color.black, p));
        });
        Vector3 bodyCenter = transform.TransformPoint(rest + Vector3.up * 0.4f) + away * (Height * 0.5f);
        StadiumKit.Burst(bodyCenter, elementColor, 45, 2.5f, 0.45f);
    }

    /// <summary>勝利: 2回跳ねて喜ぶ。</summary>
    public IEnumerator PlayVictory()
    {
        busy = true;
        OrisamoAudio.Play(OrisamoAudio.Sfx.Victory);
        StadiumKit.Burst(ChestPoint, OrisamoUI.Gold, 80, 9f);
        Vector3 start = visual.localPosition;
        for (int hop = 0; hop < 2; hop++)
        {
            yield return Animate(0.42f, p =>
            {
                visual.localPosition = start + Vector3.up * (Mathf.Sin(p * Mathf.PI) * 1.2f);
                visual.localScale = new Vector3(1f, 1f + Mathf.Sin(p * Mathf.PI) * 0.08f, 1f);
            });
        }
        busy = false;
    }

    // ==================== 補助 ====================

    private void Tint(Color flash, float amount)
    {
        for (int i = 0; i < tintMaterials.Count; i++)
        {
            tintMaterials[i].SetColor("_BaseColor", Color.Lerp(baseColors[i], flash * baseColors[i].a, amount));
        }
    }

    private static IEnumerator Animate(float duration, Action<float> step)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            step(t / duration);
            yield return null;
        }
        step(1f);
    }

    private static Mesh centeredQuad;

    private static Mesh CenteredQuad()
    {
        if (centeredQuad != null) return centeredQuad;
        StadiumKit.MeshBuilder builder = new StadiumKit.MeshBuilder();
        builder.AddQuad(new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            Vector3.back, Color.white);
        centeredQuad = builder.Build();
        return centeredQuad;
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 手続き生成された2Dモンスターアイコン(MonsterSpriteBuilder.Build())にアタッチされ、
/// 待機バウンス・攻撃演出・被弾フラッシュ・戦闘不能演出・頭上ダメージ数値を担当する。
/// UI(RectTransform / Image)ベースで動作するため、Canvas上にそのまま配置できる。
/// </summary>
public class MonsterVisual2D : MonoBehaviour
{
    private readonly List<Image> images = new List<Image>();
    private readonly List<Color> originalColors = new List<Color>();
    private RectTransform rectTransform;
    private Vector2 homeAnchoredPosition;
    private Coroutine idleCoroutine;
    private bool isFainted = false;

    /// <summary>
    /// 子オブジェクトのImageと初期色を記録し、待機位置を保存する。
    /// MonsterSpriteBuilder.Build()内で全パーツ生成後に呼び出される。
    /// </summary>
    public void Initialize()
    {
        rectTransform = GetComponent<RectTransform>();

        images.Clear();
        originalColors.Clear();
        GetComponentsInChildren(true, images);
        foreach (var img in images)
        {
            originalColors.Add(img.color);
        }

        homeAnchoredPosition = rectTransform.anchoredPosition;
    }

    public void StartIdle()
    {
        StopIdle();
        if (!isFainted) idleCoroutine = StartCoroutine(IdleBob());
    }

    public void StopIdle()
    {
        if (idleCoroutine != null)
        {
            StopCoroutine(idleCoroutine);
            idleCoroutine = null;
        }
    }

    private IEnumerator IdleBob()
    {
        float t = Random.Range(0f, 10f); // 開始位相をずらして複数体が同期しないようにする
        while (true)
        {
            t += Time.deltaTime;
            float y = Mathf.Sin(t * 1.5f) * 6f; // ピクセル単位の上下バウンス
            rectTransform.anchoredPosition = homeAnchoredPosition + new Vector2(0f, y);
            yield return null;
        }
    }

    /// <summary>
    /// 相手の方向(towardRight)へ攻撃する演出。
    /// 「① 溜め(少し後ろに引きつつ縦に伸びる)」→「② 突進(前方向へ素早く移動しつつ横に伸びる)」→
    /// 「③ 戻り(元の位置・形に戻る)」の3段階で、勢いのある攻撃らしい動きを表現する。
    /// プレイヤー側は右向き、敵側は左向きに突進させる想定。
    /// </summary>
    public IEnumerator PlayAttackLunge(bool towardRight, float lungeDistance, float duration)
    {
        StopIdle();

        float dir = towardRight ? 1f : -1f;
        Vector2 anticipatePos = homeAnchoredPosition + new Vector2(-dir * lungeDistance * 0.25f, 0f);
        Vector2 lungePos = homeAnchoredPosition + new Vector2(dir * lungeDistance, 0f);

        Vector3 normalScale = Vector3.one;
        Vector3 squashScale = new Vector3(0.85f, 1.15f, 1f); // 溜め：縦に伸びて力をためる
        Vector3 stretchScale = new Vector3(1.25f, 0.8f, 1f); // 突進：横に伸びて勢いを表現

        float anticipateDuration = duration * 0.25f;
        float thrustDuration = duration * 0.35f;
        float returnDuration = duration * 0.4f;

        yield return MoveAndScale(homeAnchoredPosition, anticipatePos, normalScale, squashScale, anticipateDuration);
        yield return MoveAndScale(anticipatePos, lungePos, squashScale, stretchScale, thrustDuration);
        yield return MoveAndScale(lungePos, homeAnchoredPosition, stretchScale, normalScale, returnDuration);

        if (!isFainted) StartIdle();
    }

    private IEnumerator MoveAndScale(Vector2 fromPos, Vector2 toPos, Vector3 fromScale, Vector3 toScale, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float ratio = t / duration;
            rectTransform.anchoredPosition = Vector2.Lerp(fromPos, toPos, ratio);
            rectTransform.localScale = Vector3.Lerp(fromScale, toScale, ratio);
            yield return null;
        }
        rectTransform.anchoredPosition = toPos;
        rectTransform.localScale = toScale;
    }

    /// <summary>
    /// 一定時間だけ待機位置へ軽くパルス(拡大→縮小)させる演出。防御成功時などに使用。
    /// </summary>
    public IEnumerator PlayGuardPulse(float duration)
    {
        Vector3 normalScale = Vector3.one;
        Vector3 pulseScale = Vector3.one * 1.15f;

        yield return ScaleTo(normalScale, pulseScale, duration * 0.3f);
        yield return ScaleTo(pulseScale, normalScale, duration * 0.7f);
    }

    private IEnumerator ScaleTo(Vector3 from, Vector3 to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            rectTransform.localScale = Vector3.Lerp(from, to, t / duration);
            yield return null;
        }
        rectTransform.localScale = to;
    }

    /// <summary>
    /// 被弾時に全パーツを一瞬指定色に光らせてから元の色へ戻す。
    /// knockbackDistanceを指定すると、光っている間だけ弾かれるようにノックバックする
    /// (knockAwayRight=trueなら右方向、falseなら左方向へ弾かれる)。
    /// </summary>
    public IEnumerator PlayHitFlash(Color flashColor, float duration, float knockbackDistance = 0f, bool knockAwayRight = true)
    {
        for (int i = 0; i < images.Count; i++)
        {
            if (images[i] != null) images[i].color = flashColor;
        }

        Vector2 startPos = rectTransform.anchoredPosition;
        float knockDir = knockAwayRight ? 1f : -1f;
        Vector2 knockPos = startPos + new Vector2(knockDir * knockbackDistance, 0f);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float ratio = t / duration;

            for (int i = 0; i < images.Count; i++)
            {
                if (images[i] == null) continue;
                images[i].color = Color.Lerp(flashColor, originalColors[i], ratio);
            }

            if (knockbackDistance > 0f)
            {
                // 前半(0〜0.4)で弾かれ、後半(0.4〜1.0)で元の位置に戻る三角形カーブ
                float knockRatio = ratio < 0.4f ? (ratio / 0.4f) : (1f - (ratio - 0.4f) / 0.6f);
                rectTransform.anchoredPosition = Vector2.Lerp(startPos, knockPos, Mathf.Clamp01(knockRatio));
            }

            yield return null;
        }

        for (int i = 0; i < images.Count; i++)
        {
            if (images[i] != null) images[i].color = originalColors[i];
        }

        if (knockbackDistance > 0f)
        {
            rectTransform.anchoredPosition = startPos;
        }
    }

    /// <summary>
    /// 命中位置に衝撃波(リングが拡大しながらフェードアウト)エフェクトを表示する。
    /// effectParentを親としてUI要素を生成するため、同じCanvas配下を指定すること。
    /// </summary>
    public void SpawnImpactBurst(Color color, Transform effectParent, float maxSizePixels)
    {
        Transform parent = effectParent != null ? effectParent : transform.root;

        GameObject go = new GameObject("ImpactBurst2D", typeof(RectTransform), typeof(Image));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.position = rectTransform.position;
        rt.sizeDelta = Vector2.one * (maxSizePixels * 0.3f);

        Image img = go.GetComponent<Image>();
        img.sprite = ProceduralSprite2D.GetRingSprite();
        img.color = color;
        img.raycastTarget = false;

        StartCoroutine(AnimateImpactBurst(rt, img, maxSizePixels));
    }

    private IEnumerator AnimateImpactBurst(RectTransform rt, Image img, float maxSizePixels)
    {
        float duration = 0.35f;
        Vector2 startSize = rt.sizeDelta;
        Vector2 endSize = Vector2.one * maxSizePixels;
        Color startColor = img.color;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float ratio = t / duration;
            rt.sizeDelta = Vector2.Lerp(startSize, endSize, ratio);
            Color c = startColor;
            c.a = Mathf.Lerp(startColor.a, 0f, ratio);
            img.color = c;
            yield return null;
        }

        Destroy(rt.gameObject);
    }

    /// <summary>
    /// 戦闘不能演出：沈みながら回転し、透明になって非表示になる。
    /// </summary>
    public IEnumerator PlayFaint(float duration)
    {
        isFainted = true;
        StopIdle();

        Vector2 startPos = rectTransform.anchoredPosition;
        Vector2 endPos = startPos + new Vector2(0f, -60f);
        Quaternion startRot = rectTransform.localRotation;
        Quaternion endRot = startRot * Quaternion.Euler(0f, 0f, 80f);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float ratio = t / duration;
            rectTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, ratio);
            rectTransform.localRotation = Quaternion.Slerp(startRot, endRot, ratio);
            for (int i = 0; i < images.Count; i++)
            {
                if (images[i] == null) continue;
                Color c = images[i].color;
                c.a = Mathf.Lerp(originalColors[i].a, 0f, ratio);
                images[i].color = c;
            }
            yield return null;
        }

        gameObject.SetActive(false);
    }

    /// <summary>
    /// 頭上にダメージ数値を表示し、上昇しながらフェードアウトさせる。
    /// effectParentを親としてUI要素を生成するため、同じCanvas配下を指定すること。
    /// </summary>
    public void SpawnDamageNumber(int damage, Color color, Transform effectParent, float headHeightPixels)
    {
        Transform parent = effectParent != null ? effectParent : transform.root;

        GameObject go = new GameObject("DamageNumber2D", typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.position = rectTransform.position + new Vector3(0f, headHeightPixels, 0f);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = damage.ToString();
        tmp.fontSize = 36;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        StartCoroutine(AnimateDamageNumber(rt, tmp));
    }

    private IEnumerator AnimateDamageNumber(RectTransform rt, TextMeshProUGUI tmp)
    {
        float duration = 0.9f;
        Vector3 start = rt.position;
        Vector3 end = start + new Vector3(0f, 50f, 0f);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float ratio = t / duration;
            rt.position = Vector3.Lerp(start, end, ratio);
            Color c = tmp.color;
            c.a = Mathf.Lerp(1f, 0f, ratio);
            tmp.color = c;
            yield return null;
        }

        Destroy(rt.gameObject);
    }
}

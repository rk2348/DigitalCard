using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// 手続き生成されたモンスターモデル(MonsterBuilder.Build())にアタッチされ、
/// 待機モーション・攻撃演出・被弾フラッシュ・戦闘不能演出・頭上ダメージ数値を担当する。
/// </summary>
public class MonsterVisual : MonoBehaviour
{
    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Color> originalColors = new List<Color>();
    private Vector3 homeLocalPosition;
    private Coroutine idleCoroutine;
    private bool isFainted = false;

    /// <summary>
    /// 子オブジェクトのRendererと初期色を記録し、待機位置を保存する。
    /// MonsterBuilder.Build()内で全パーツ生成後に呼び出される。
    /// </summary>
    public void Initialize()
    {
        renderers.Clear();
        originalColors.Clear();
        GetComponentsInChildren(true, renderers);
        foreach (var r in renderers)
        {
            originalColors.Add(r.material.color);
        }
        homeLocalPosition = transform.localPosition;
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
            float y = Mathf.Sin(t * 1.5f) * 0.05f;
            transform.localPosition = homeLocalPosition + new Vector3(0f, y, 0f);
            yield return null;
        }
    }

    /// <summary>
    /// 対象のワールド座標方向へ軽く突進してから元の位置へ戻る攻撃演出。
    /// </summary>
    public IEnumerator PlayAttackLunge(Vector3 towardWorldPosition, float lungeDistance, float duration)
    {
        StopIdle();

        Vector3 direction = (towardWorldPosition - transform.position).normalized;
        Vector3 localDirection = transform.InverseTransformDirection(direction);
        Vector3 startLocal = transform.localPosition;
        Vector3 lungeLocal = startLocal + localDirection * lungeDistance;

        yield return MoveLocal(startLocal, lungeLocal, duration * 0.4f);
        yield return MoveLocal(lungeLocal, startLocal, duration * 0.6f);

        if (!isFainted) StartIdle();
    }

    private IEnumerator MoveLocal(Vector3 from, Vector3 to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            transform.localPosition = Vector3.Lerp(from, to, t / duration);
            yield return null;
        }
        transform.localPosition = to;
    }

    /// <summary>
    /// 被弾時に全パーツを一瞬指定色に光らせてから元の色へ戻す。
    /// </summary>
    public IEnumerator PlayHitFlash(Color flashColor, float duration)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] != null) renderers[i].material.color = flashColor;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float ratio = t / duration;
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] == null) continue;
                renderers[i].material.color = Color.Lerp(flashColor, originalColors[i], ratio);
            }
            yield return null;
        }

        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] != null) renderers[i].material.color = originalColors[i];
        }
    }

    /// <summary>
    /// 戦闘不能演出：沈みながら回転し、透明になって非表示になる。
    /// </summary>
    public IEnumerator PlayFaint(float duration)
    {
        isFainted = true;
        StopIdle();

        Vector3 startPos = transform.localPosition;
        Vector3 endPos = startPos + new Vector3(0f, -0.8f, 0f);
        Quaternion startRot = transform.localRotation;
        Quaternion endRot = startRot * Quaternion.Euler(0f, 0f, 90f);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float ratio = t / duration;
            transform.localPosition = Vector3.Lerp(startPos, endPos, ratio);
            transform.localRotation = Quaternion.Slerp(startRot, endRot, ratio);
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] == null) continue;
                Color c = renderers[i].material.color;
                c.a = Mathf.Lerp(1f, 0f, ratio);
                renderers[i].material.color = c;
            }
            yield return null;
        }

        gameObject.SetActive(false);
    }

    /// <summary>
    /// 頭上にダメージ数値を表示し、上昇しながらフェードアウトさせる。
    /// 常にメインカメラの方向を向く3Dテキストとして生成される。
    /// </summary>
    public void SpawnDamageNumber(int damage, Color color, float headHeight)
    {
        GameObject go = new GameObject("DamageNumber3D");
        go.transform.position = transform.position + new Vector3(0f, headHeight, 0f);

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.text = damage.ToString();
        tmp.fontSize = 6;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;

        StartCoroutine(AnimateDamageNumber(go, tmp));
    }

    private IEnumerator AnimateDamageNumber(GameObject go, TextMeshPro tmp)
    {
        float duration = 0.9f;
        Vector3 start = go.transform.position;
        Vector3 end = start + new Vector3(0f, 0.8f, 0f);
        Camera cam = Camera.main;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float ratio = t / duration;
            go.transform.position = Vector3.Lerp(start, end, ratio);
            if (cam != null) go.transform.rotation = cam.transform.rotation; // 常にカメラの方を向く
            Color c = tmp.color;
            c.a = Mathf.Lerp(1f, 0f, ratio);
            tmp.color = c;
            yield return null;
        }

        Destroy(go);
    }
}

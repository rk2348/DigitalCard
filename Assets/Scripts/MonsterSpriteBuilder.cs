using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CharacterStatsの情報(属性・名前・ステータス)から、簡易的な2Dモンスターアイコンを
/// 円形Imageの組み合わせで手続き的に生成するヘルパークラス。
/// 実際のイラスト/スプライト素材が用意できるまでの「仮アイコン」として利用する想定。
/// 同じキャラクター名+属性からは常に同じ見た目が生成される(シード固定)。
///
/// 【依存関係】
/// ElementAffinity クラスに GetElementColor(ElementType) が必要です。
/// </summary>
public static class MonsterSpriteBuilder
{
    /// <summary>
    /// CharacterStatsを元に手続き的な2Dモンスターアイコンを生成し、
    /// 指定のスポーン地点(RectTransform)の子として配置する。
    /// MonsterVisual2Dコンポーネントが自動的にアタッチされ、初期化される。
    ///
    /// stats.photoSpriteが設定されている場合(＝バトル前のQRスキャンで実物写真を
    /// 読み込んだプレイヤーキャラクターの場合)は、その写真をそのままアイコンとして使う。
    /// 未設定の場合(敵AIキャラクターなど)は、従来通り円形パーツの手続き生成を行う。
    /// </summary>
    public static GameObject Build(CharacterStats stats, RectTransform spawnPoint, float scale = 1f)
    {
        if (stats.photoSprite != null)
        {
            return BuildFromPhoto(stats, spawnPoint, scale);
        }

        return BuildProcedural(stats, spawnPoint, scale);
    }

    /// <summary>
    /// 撮影・背景切り抜き済みの写真(stats.photoSprite)を1枚のImageとして表示するアイコンを作る。
    /// MonsterVisual2Dは子のImageを自動収集して演出に使うため、このImage1枚だけでも
    /// 待機バウンス・攻撃演出・被弾フラッシュ・戦闘不能演出はそのまま動作する。
    /// </summary>
    private static GameObject BuildFromPhoto(CharacterStats stats, RectTransform spawnPoint, float scale)
    {
        GameObject root = new GameObject($"Monster2D_{stats.characterName}", typeof(RectTransform));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.SetParent(spawnPoint, false);
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.localScale = Vector3.one * scale;

        // 体格のばらつき(最大HPである程度傾向をつける。手続き生成版と同じ考え方)
        float targetSize = Mathf.Lerp(140f, 220f, Mathf.InverseLerp(0, 200, stats.maxHp));

        Texture2D tex = stats.photoSprite.texture;
        float aspect = tex != null && tex.height > 0 ? (float)tex.width / tex.height : 1f;
        Vector2 size = aspect >= 1f
            ? new Vector2(targetSize, targetSize / aspect)
            : new Vector2(targetSize * aspect, targetSize);

        GameObject photoGo = new GameObject("Photo", typeof(RectTransform), typeof(Image));
        RectTransform photoRect = photoGo.GetComponent<RectTransform>();
        photoRect.SetParent(rootRect, false);
        photoRect.sizeDelta = size;
        photoRect.anchoredPosition = Vector2.zero;

        Image img = photoGo.GetComponent<Image>();
        img.sprite = stats.photoSprite;
        img.preserveAspect = true;

        MonsterVisual2D visual = root.AddComponent<MonsterVisual2D>();
        visual.Initialize();

        return root;
    }

    /// <summary>
    /// 写真が無いキャラクター(敵AIなど)向けの、従来通りの円形パーツ手続き生成。
    /// </summary>
    private static GameObject BuildProcedural(CharacterStats stats, RectTransform spawnPoint, float scale)
    {
        int seed = (stats.characterName + stats.element).GetHashCode();
        System.Random rng = new System.Random(seed);

        GameObject root = new GameObject($"Monster2D_{stats.characterName}", typeof(RectTransform));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.SetParent(spawnPoint, false);
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.localScale = Vector3.one * scale;

        Color baseColor = ElementAffinity.GetElementColor(stats.element);

        // 体格のばらつき(最大HPである程度傾向をつける。値域は環境に合わせて調整してください)
        float bodySize = Mathf.Lerp(90f, 150f, Mathf.InverseLerp(0, 200, stats.maxHp));
        float headSize = bodySize * 0.55f;

        // 胴体
        RectTransform body = CreateCircle(rootRect, "Body", bodySize, baseColor);
        body.anchoredPosition = Vector2.zero;

        // 頭(胴体の上に重ねる)
        RectTransform head = CreateCircle(rootRect, "Head", headSize, Color.Lerp(baseColor, Color.white, 0.2f));
        head.anchoredPosition = new Vector2(0f, bodySize * 0.45f + headSize * 0.35f);

        // 目(2つ)
        float eyeSize = headSize * 0.16f;
        for (int i = -1; i <= 1; i += 2)
        {
            RectTransform eye = CreateCircle(head, "Eye", eyeSize, Color.black);
            eye.anchoredPosition = new Vector2(headSize * 0.22f * i, headSize * 0.05f);
        }

        // 特徴パーツ(見た目のバリエーション用。位置をランダムにずらす)
        float featureSize = bodySize * 0.22f;
        RectTransform feature = CreateCircle(rootRect, "Feature", featureSize, Color.Lerp(baseColor, Color.white, 0.5f));
        float featureOffsetX = ((float)rng.NextDouble() * 2f - 1f) * bodySize * 0.3f;
        feature.anchoredPosition = new Vector2(featureOffsetX, bodySize * 0.55f);

        // 演出用コンポーネントをアタッチして初期化
        MonsterVisual2D visual = root.AddComponent<MonsterVisual2D>();
        visual.Initialize();

        return root;
    }

    private static RectTransform CreateCircle(Transform parent, string name, float size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(size, size);

        Image img = go.GetComponent<Image>();
        img.sprite = ProceduralSprite2D.GetCircleSprite();
        img.color = color;

        return rect;
    }
}

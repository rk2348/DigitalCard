using UnityEngine;

/// <summary>
/// CharacterStatsの情報(属性・名前・ステータス)から、簡易的な3Dモンスターモデルを
/// プリミティブの組み合わせで手続き的に生成するヘルパークラス。
/// 本物の3Dモデル/アニメーションが用意できるまでの「仮モデル」として利用する想定。
/// 同じキャラクター名+属性からは常に同じ見た目が生成される(シード固定)。
///
/// 【依存関係】
/// ElementAffinity クラスに以下のメソッドが必要です(未実装の場合は追加してください):
///   public static Color GetElementColor(ElementType element)
/// 例: 火=赤系、水=青系、木=緑系 のように属性ごとの代表色を返す。
/// </summary>
public static class MonsterBuilder
{
    /// <summary>
    /// CharacterStatsを元に手続き的なモンスターモデルを生成し、
    /// 指定のスポーン地点(Transform)の子として配置する。
    /// MonsterVisualコンポーネントが自動的にアタッチされ、初期化される。
    /// </summary>
    public static GameObject Build(CharacterStats stats, Transform spawnPoint, float modelScale = 1f)
    {
        int seed = (stats.characterName + stats.element).GetHashCode();
        System.Random rng = new System.Random(seed);

        GameObject root = new GameObject($"Monster_{stats.characterName}");
        root.transform.SetParent(spawnPoint, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * modelScale;

        Color baseColor = ElementAffinity.GetElementColor(stats.element);

        // 体格のばらつき(最大HPである程度傾向をつける。値域は環境に合わせて調整してください)
        float bodyScale = Mathf.Lerp(0.8f, 1.4f, Mathf.InverseLerp(0, 200, stats.maxHp));
        float limbLength = Mathf.Lerp(0.3f, 0.7f, (float)rng.NextDouble());

        // 胴体
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = new Vector3(bodyScale, bodyScale * 0.9f, bodyScale);
        body.transform.localPosition = new Vector3(0f, bodyScale * 0.5f, 0f);
        ApplyColor(body, baseColor);

        // 頭
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(root.transform, false);
        float headScale = bodyScale * 0.55f;
        head.transform.localScale = Vector3.one * headScale;
        head.transform.localPosition = body.transform.localPosition + new Vector3(0f, bodyScale * 0.6f + headScale * 0.4f, 0f);
        ApplyColor(head, Color.Lerp(baseColor, Color.white, 0.2f));

        // 目(2つ)
        for (int i = -1; i <= 1; i += 2)
        {
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = "Eye";
            eye.transform.SetParent(head.transform, false);
            eye.transform.localScale = Vector3.one * 0.18f;
            eye.transform.localPosition = new Vector3(0.28f * i, 0.05f, 0.45f);
            ApplyColor(eye, Color.black);
        }

        // 手足(4本、ランダムな長さでバリエーションをつける)
        Vector3[] limbOffsets =
        {
            new Vector3(0.5f, 0f, 0.3f),
            new Vector3(-0.5f, 0f, 0.3f),
            new Vector3(0.5f, 0f, -0.3f),
            new Vector3(-0.5f, 0f, -0.3f),
        };
        foreach (var offset in limbOffsets)
        {
            GameObject limb = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            limb.name = "Limb";
            limb.transform.SetParent(root.transform, false);
            limb.transform.localScale = new Vector3(0.15f, limbLength, 0.15f);
            limb.transform.localPosition = body.transform.localPosition
                + new Vector3(offset.x * bodyScale, -limbLength * 0.6f, offset.z * bodyScale);
            ApplyColor(limb, Color.Lerp(baseColor, Color.black, 0.3f));
        }

        // 背中の特徴パーツ(見た目のバリエーション用、トゲ/ヒレなどのイメージ)
        GameObject feature = GameObject.CreatePrimitive(PrimitiveType.Cube);
        feature.name = "Feature";
        feature.transform.SetParent(root.transform, false);
        feature.transform.localScale = new Vector3(0.15f, 0.4f, 0.15f);
        feature.transform.localPosition = body.transform.localPosition + new Vector3(0f, bodyScale * 0.5f, -bodyScale * 0.3f);
        feature.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        ApplyColor(feature, Color.Lerp(baseColor, Color.white, 0.5f));

        // 演出用コンポーネントをアタッチして初期化
        MonsterVisual visual = root.AddComponent<MonsterVisual>();
        visual.Initialize();

        return root;
    }

    private static void ApplyColor(GameObject go, Color color)
    {
        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer == null) return;

        // URP/Built-inどちらでも動くよう、共有マテリアルを複製してから色を設定
        Material mat = new Material(renderer.sharedMaterial);
        mat.color = color;
        renderer.material = mat;
    }
}

using UnityEngine;

/// <summary>
/// キャラクターの3Dオブジェクト（現状は仮のプリミティブ、将来はキャラクターモデル）を
/// 生成するための共通処理。CharacterCreationManagerとQRCharacterStatusDisplayの
/// 両方から呼び出される。
/// </summary>
public static class CharacterModelUtility
{
    /// <summary>
    /// キャラクターの3Dオブジェクトを生成する。
    /// modelPrefabが設定されていればそれを使用（＝本番のキャラクターモデル差し替え口）。
    /// 未設定の場合は、属性に応じて色分けした仮のカプセルを生成する。
    /// </summary>
    /// <param name="stats">表示対象のキャラクターステータス</param>
    /// <param name="spawnPoint">表示位置・回転の基準（未設定ならワールド原点）</param>
    /// <param name="modelPrefab">キャラクターモデルのプレハブ（未設定なら仮のカプセル）</param>
    /// <param name="existingInstance">既に表示中のインスタンス（あれば破棄してから生成し直す）</param>
    /// <returns>生成された3Dオブジェクト</returns>
    public static GameObject SpawnModel(CharacterStats stats, Transform spawnPoint, GameObject modelPrefab, GameObject existingInstance)
    {
        if (existingInstance != null)
        {
            Object.Destroy(existingInstance);
        }

        Vector3 spawnPosition = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion spawnRotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        GameObject instance;

        if (modelPrefab != null)
        {
            // 【将来ここが本番のキャラクターモデルに差し替わる想定】
            instance = Object.Instantiate(modelPrefab, spawnPosition, spawnRotation, spawnPoint);
        }
        else
        {
            // プレハブ未設定時は仮の3Dプリミティブ（カプセル）を表示
            instance = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            instance.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            if (spawnPoint != null)
            {
                instance.transform.SetParent(spawnPoint);
            }

            Renderer renderer = instance.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = GetElementColor(stats.element);
            }
        }

        instance.name = "CharacterModel_" + stats.characterName;

        // ゆっくり回転させる演出（不要であれば削除可）
        if (instance.GetComponent<SimpleRotator>() == null)
        {
            instance.AddComponent<SimpleRotator>();
        }

        return instance;
    }

    /// <summary>
    /// 属性ごとの仮の表示色。実際のカード(闇・火・光・水・地・風)のアイコンの色に揃えてある。
    /// </summary>
    public static Color GetElementColor(ElementType element)
    {
        switch (element)
        {
            case ElementType.Fire: return new Color(0.85f, 0.16f, 0.16f);  // 火：赤
            case ElementType.Wind: return new Color(0.26f, 0.65f, 0.36f);  // 風：緑
            case ElementType.Dark: return new Color(0.42f, 0.30f, 0.58f);  // 闇：紫
            case ElementType.Water: return new Color(0.16f, 0.67f, 0.89f); // 水：水色
            case ElementType.Earth: return new Color(0.55f, 0.43f, 0.26f); // 地：茶
            case ElementType.Light: return new Color(0.96f, 0.79f, 0.36f); // 光：金
            default: return Color.white;
        }
    }
}

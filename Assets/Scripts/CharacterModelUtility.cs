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
    /// 属性ごとの仮の表示色。実際のカラーパレットが決まったら調整してください。
    /// </summary>
    public static Color GetElementColor(ElementType element)
    {
        switch (element)
        {
            case ElementType.Fire: return new Color(0.9f, 0.3f, 0.2f);
            case ElementType.Wind: return new Color(0.4f, 0.8f, 0.4f);
            case ElementType.Thunder: return new Color(0.95f, 0.85f, 0.2f);
            case ElementType.Water: return new Color(0.2f, 0.5f, 0.9f);
            case ElementType.Earth: return new Color(0.6f, 0.4f, 0.2f);
            case ElementType.Light: return new Color(0.95f, 0.95f, 0.85f);
            default: return Color.white;
        }
    }
}

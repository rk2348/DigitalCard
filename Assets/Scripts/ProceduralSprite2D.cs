using UnityEngine;

/// <summary>
/// 画像素材が無い状態でも簡易的な円形アイコンを表示できるよう、
/// 手続き的に白い円スプライトを1枚だけ生成してキャッシュするユーティリティ。
/// 色は個々のImageコンポーネント側(Image.color)で指定して使い回す。
/// </summary>
public static class ProceduralSprite2D
{
    private static Sprite cachedCircle;
    private static Sprite cachedRing;

    /// <summary>
    /// 白い円スプライトを取得する(初回のみ生成、以降はキャッシュを返す)。
    /// </summary>
    public static Sprite GetCircleSprite()
    {
        if (cachedCircle != null) return cachedCircle;
        cachedCircle = CreateCircleSprite(64);
        return cachedCircle;
    }

    /// <summary>
    /// 白いリング(輪っか)スプライトを取得する(初回のみ生成、以降はキャッシュを返す)。
    /// 攻撃命中時の衝撃波エフェクトなどに使用する。
    /// </summary>
    public static Sprite GetRingSprite()
    {
        if (cachedRing != null) return cachedRing;
        cachedRing = CreateRingSprite(64, 9f);
        return cachedRing;
    }

    private static Sprite CreateRingSprite(int diameter, float thickness)
    {
        Texture2D tex = new Texture2D(diameter, diameter, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[diameter * diameter];
        float outerRadius = diameter / 2f;
        float innerRadius = outerRadius - thickness;
        Vector2 center = new Vector2(outerRadius, outerRadius);

        for (int y = 0; y < diameter; y++)
        {
            for (int x = 0; x < diameter; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);

                byte alpha;
                if (dist < innerRadius - 1f || dist > outerRadius)
                {
                    alpha = 0;
                }
                else if (dist >= innerRadius && dist <= outerRadius - 1f)
                {
                    alpha = 255;
                }
                else
                {
                    // 内側/外側の境界をなめらかにフェードさせる
                    float edge = dist < innerRadius ? (dist - (innerRadius - 1f)) : (outerRadius - dist);
                    alpha = (byte)(Mathf.Clamp01(edge) * 255);
                }

                pixels[y * diameter + x] = new Color32(255, 255, 255, alpha);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, diameter, diameter), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite CreateCircleSprite(int diameter)
    {
        Texture2D tex = new Texture2D(diameter, diameter, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[diameter * diameter];
        float radius = diameter / 2f;
        Vector2 center = new Vector2(radius, radius);

        for (int y = 0; y < diameter; y++)
        {
            for (int x = 0; x < diameter; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);

                byte alpha;
                if (dist <= radius - 1f)
                {
                    alpha = 255; // 内部は完全不透明
                }
                else if (dist >= radius)
                {
                    alpha = 0; // 外側は完全透明
                }
                else
                {
                    // 境界1pxだけなめらかにフェードさせてジャギーを抑える
                    alpha = (byte)(Mathf.Clamp01(radius - dist) * 255);
                }

                pixels[y * diameter + x] = new Color32(255, 255, 255, alpha);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, diameter, diameter), new Vector2(0.5f, 0.5f), 100f);
    }
}

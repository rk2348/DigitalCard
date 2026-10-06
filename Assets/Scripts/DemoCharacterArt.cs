using UnityEngine;

/// <summary>
/// 写真の無いキャラクター(開発者用のデモ対戦など)のために、切り抜き写真の代わりになる
/// キャラクターのイラスト(透過テクスチャ)を手続き生成する。
/// 生成したテクスチャは本番の写真と同じく PhotoSculpture で立体化されるので、デモでも本番と同じ見た目を確認できる。
///
/// 開発者が試したい写真があれば、Assets/Resources/DemoPhotos に透過PNGを置くと、そちらが優先して使われる。
/// 同じ名前・属性からは毎回同じ見た目になる。
/// </summary>
public static class DemoCharacterArt
{
    private const int Width = 256;
    private const int Height = 320;

    public static Texture2D Create(CharacterStats stats)
    {
        int seed = (stats.characterName + stats.element).GetHashCode();
        Texture2D[] photos = Resources.LoadAll<Texture2D>("DemoPhotos");
        if (photos != null && photos.Length > 0) return photos[Mathf.Abs(seed) % photos.Length];
        return Draw(ElementAffinity.GetElementColor(stats.element), new System.Random(seed));
    }

    private static Texture2D Draw(Color baseColor, System.Random rng)
    {
        float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        Color body = Color.Lerp(baseColor, Color.white, 0.12f);
        Color belly = Color.Lerp(baseColor, Color.white, 0.55f);
        Color dark = Color.Lerp(baseColor, Color.black, 0.55f);
        Color outline = Color.Lerp(baseColor, Color.black, 0.75f);

        float bodyRx = R(70f, 92f), bodyRy = R(82f, 100f);
        Vector2 bodyCenter = new Vector2(128f, 52f + bodyRy);
        float headR = R(54f, 66f);
        Vector2 headCenter = new Vector2(128f, bodyCenter.y + bodyRy * 0.72f + headR * 0.55f);
        bool horns = rng.NextDouble() < 0.5;
        float earSpread = R(0.45f, 0.75f);
        float earLength = R(36f, 58f);
        float eyeSpread = R(18f, 26f);
        float eyeR = R(12f, 16f);
        bool tail = rng.NextDouble() < 0.6;

        Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Color32[] pixels = new Color32[Width * Height];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);

                // シルエット(符号付き距離。負が内側)
                float d = Ellipse(p, bodyCenter, bodyRx, bodyRy);
                d = Mathf.Min(d, Circle(p, headCenter, headR));
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 earBase = headCenter + new Vector2(side * headR * earSpread, headR * 0.6f);
                    Vector2 earTip = earBase + new Vector2(side * earLength * (horns ? 0.25f : 0.45f), earLength);
                    d = Mathf.Min(d, Capsule(p, earBase, earTip, horns ? 10f : 17f));
                    d = Mathf.Min(d, Ellipse(p, new Vector2(128f + side * bodyRx * 0.48f, 30f), 30f, 24f)); // 足
                    d = Mathf.Min(d, Capsule(p, bodyCenter + new Vector2(side * bodyRx * 0.8f, bodyRy * 0.2f),
                        bodyCenter + new Vector2(side * (bodyRx + 22f), -bodyRy * 0.15f), 15f)); // 腕
                }
                if (tail) d = Mathf.Min(d, Capsule(p, bodyCenter + new Vector2(bodyRx * 0.6f, -bodyRy * 0.5f), new Vector2(Width - 18f, 70f), 12f));

                float alpha = Mathf.Clamp01(0.5f - d);
                if (alpha <= 0f) { pixels[y * Width + x] = new Color32(0, 0, 0, 0); continue; }

                // 塗り: 胴体色 → お腹 → 角は濃い色 → 縁取り
                Color c = body;
                if (Ellipse(p, bodyCenter + new Vector2(0f, -bodyRy * 0.12f), bodyRx * 0.6f, bodyRy * 0.62f) < 0f) c = belly;
                if (horns && p.y > headCenter.y + headR * 0.7f && Circle(p, headCenter, headR) > 0f) c = Color.Lerp(Color.white, OrisamoUI.Gold, 0.5f);
                c *= Mathf.Lerp(0.85f, 1.08f, (float)y / Height); // 上ほど明るく
                if (d > -4.5f) c = outline;

                // 顔
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 eye = headCenter + new Vector2(side * eyeSpread, 2f);
                    if (Circle(p, eye, eyeR) < 0f) c = Color.white;
                    if (Circle(p, eye + new Vector2(side * 1.5f, -1f), eyeR * 0.55f) < 0f) c = new Color(0.08f, 0.06f, 0.1f);
                    if (Circle(p, eye + new Vector2(side * 1.5f - 3f, 3f), eyeR * 0.2f) < 0f) c = Color.white;
                    if (Ellipse(p, headCenter + new Vector2(side * (eyeSpread + 16f), -16f), 10f, 6f) < 0f) c = Color.Lerp(c, new Color(1f, 0.5f, 0.55f), 0.6f);
                }
                if (Mathf.Abs(Ellipse(p, headCenter + new Vector2(0f, -18f), 12f, 7f)) < 1.6f && p.y < headCenter.y - 18f) c = dark;

                c.a = alpha;
                pixels[y * Width + x] = c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    private static float Circle(Vector2 p, Vector2 center, float radius) => (p - center).magnitude - radius;

    private static float Ellipse(Vector2 p, Vector2 center, float rx, float ry)
    {
        Vector2 q = new Vector2((p.x - center.x) / rx, (p.y - center.y) / ry);
        return (q.magnitude - 1f) * Mathf.Min(rx, ry);
    }

    private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float radius)
    {
        Vector2 pa = p - a, ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude - radius;
    }
}

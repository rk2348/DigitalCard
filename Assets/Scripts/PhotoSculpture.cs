using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 背景を切り抜いた写真(透過PNG)を、厚みのある立体メッシュにする。
///
/// 手順:
///   1. 写真の不透明な部分を格子(長辺 resolution マス)に落とし込み、キャラクターの形(マスク)を作る
///   2. 輪郭からの距離を求め、中心ほど厚く・縁ほど丸く落ちる高さを付ける(クッキーやぬいぐるみのような膨らみ)
///   3. 表面と裏面を作り、輪郭で貼り合わせる(輪郭の頂点は高さ0なので隙間ができない)。輪郭のギザギザはならして滑らかにする
///   4. 表面にも裏面にも写真を貼る(裏から見ると左右反転した写真になる)
/// 細かい縁は写真のアルファで切り抜く(アルファクリップ)ので、輪郭は写真どおりの形で表示される。
/// メッシュの手前(表面)は -Z 向き、ピボットは足元の中央。
/// </summary>
public static class PhotoSculpture
{
    /// <summary>生成結果。meshの大きさ(m)も返す。</summary>
    public struct Result
    {
        public Mesh mesh;
        public float width;
        public float height;
        public float thickness;
    }

    /// <summary>
    /// 写真から立体メッシュを作る。height は仕上がりの高さ(m)、maxWidth を超える場合は幅に合わせて縮める。
    /// </summary>
    public static Result Build(Texture2D source, float height, float maxWidth, int resolution = 112)
    {
        Texture2D texture = EnsureReadable(source);
        float aspect = texture.height > 0 ? (float)texture.width / texture.height : 1f;
        float width = height * aspect;
        if (width > maxWidth)
        {
            width = maxWidth;
            height = width / aspect;
        }

        int gw = Mathf.Max(8, Mathf.RoundToInt(resolution * Mathf.Min(1f, aspect)));
        int gh = Mathf.Max(8, Mathf.RoundToInt(resolution * Mathf.Min(1f, 1f / aspect)));
        bool[,] inside = BuildMask(texture, gw, gh);
        RemoveSpecks(inside, gw, gh, Mathf.Max(12, gw * gh / 400));

        // 角(頂点)の分類: 0=未使用, 1=輪郭, 2=内部
        int[,] corner = new int[gw + 1, gh + 1];
        for (int i = 0; i <= gw; i++)
        {
            for (int j = 0; j <= gh; j++)
            {
                int insideCount = 0, total = 0;
                for (int di = -1; di <= 0; di++)
                {
                    for (int dj = -1; dj <= 0; dj++)
                    {
                        int ci = i + di, cj = j + dj;
                        total++;
                        if (ci >= 0 && cj >= 0 && ci < gw && cj < gh && inside[ci, cj]) insideCount++;
                    }
                }
                corner[i, j] = insideCount == 0 ? 0 : insideCount == total ? 2 : 1;
            }
        }

        float[,] distance = DistanceToOutline(corner, gw, gh, out float maxDistance);
        Vector2[,] position = SmoothOutline(corner, gw, gh, width, height);

        // 厚み: 小さめのキャラクターでも立体感が出るよう、幅と高さの小さい方に比例させる
        float thickness = Mathf.Clamp(Mathf.Min(width, height) * 0.13f, 0.18f, 0.55f);
        float roundRadius = Mathf.Max(2.5f, maxDistance * 0.75f);

        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        int[,] frontIndex = new int[gw + 1, gh + 1];
        int[,] backIndex = new int[gw + 1, gh + 1];

        for (int i = 0; i <= gw; i++)
        {
            for (int j = 0; j <= gh; j++)
            {
                frontIndex[i, j] = backIndex[i, j] = -1;
                if (corner[i, j] == 0) continue;

                float r = Mathf.Clamp01(distance[i, j] / roundRadius);
                float h = thickness * Mathf.Sqrt(1f - (1f - r) * (1f - r)); // 縁は丸く、中央は平らに近い
                Vector2 p = position[i, j];
                Vector2 uv = new Vector2(p.x / width + 0.5f, p.y / height);

                frontIndex[i, j] = vertices.Count;
                vertices.Add(new Vector3(p.x, p.y, -h));
                uvs.Add(uv);
                backIndex[i, j] = vertices.Count;
                vertices.Add(new Vector3(p.x, p.y, h));
                uvs.Add(uv);
            }
        }

        for (int i = 0; i < gw; i++)
        {
            for (int j = 0; j < gh; j++)
            {
                if (!inside[i, j]) continue;
                int f00 = frontIndex[i, j], f01 = frontIndex[i, j + 1], f11 = frontIndex[i + 1, j + 1], f10 = frontIndex[i + 1, j];
                int b00 = backIndex[i, j], b01 = backIndex[i, j + 1], b11 = backIndex[i + 1, j + 1], b10 = backIndex[i + 1, j];
                // 表面(-Z向き)は手前から見て時計回り、裏面は逆回り
                triangles.Add(f00); triangles.Add(f01); triangles.Add(f11);
                triangles.Add(f00); triangles.Add(f11); triangles.Add(f10);
                triangles.Add(b00); triangles.Add(b11); triangles.Add(b01);
                triangles.Add(b00); triangles.Add(b10); triangles.Add(b11);
            }
        }

        Mesh mesh = new Mesh { name = "PhotoSculpture", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (texture != source) Object.Destroy(texture);
        return new Result { mesh = mesh, width = width, height = height, thickness = thickness };
    }

    /// <summary>
    /// 立体化した写真用のマテリアル(URP Lit + アルファクリップ)。ライティングと影で立体感が出る。
    /// ビルドでシェーダーのキーワードが除外されないよう、Resources/Stadium に同じ設定のマテリアルを置いてある。
    /// </summary>
    public static Material CreateMaterial(Texture texture)
    {
        Material template = Resources.Load<Material>("Stadium/StadiumLitCutout");
        Material material = template != null ? new Material(template) : StadiumKit.Lit(Color.white);
        material.SetTexture("_BaseMap", texture);
        material.mainTexture = texture;
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", 0.35f);
        material.SetFloat("_Cull", (float)CullMode.Back);
        material.SetFloat("_Smoothness", 0.35f);
        material.SetFloat("_Metallic", 0f);
        material.EnableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = (int)RenderQueue.AlphaTest;
        return material;
    }

    // ==================== 内部処理 ====================

    /// <summary>マスごとに数点のアルファを調べ、一定以上あれば「キャラクターの一部」とみなす。</summary>
    private static bool[,] BuildMask(Texture2D texture, int gw, int gh)
    {
        Color32[] pixels = texture.GetPixels32();
        int tw = texture.width, th = texture.height;
        bool[,] inside = new bool[gw, gh];
        for (int i = 0; i < gw; i++)
        {
            for (int j = 0; j < gh; j++)
            {
                int hits = 0;
                for (int si = 0; si < 3; si++)
                {
                    for (int sj = 0; sj < 3; sj++)
                    {
                        int x = Mathf.Clamp((int)((i + (si + 0.5f) / 3f) / gw * tw), 0, tw - 1);
                        int y = Mathf.Clamp((int)((j + (sj + 0.5f) / 3f) / gh * th), 0, th - 1);
                        if (pixels[y * tw + x].a > 110) hits++;
                    }
                }
                inside[i, j] = hits >= 3;
            }
        }
        return inside;
    }

    /// <summary>背景除去の取り残しなど、小さな島を消す。</summary>
    private static void RemoveSpecks(bool[,] inside, int gw, int gh, int minimumCells)
    {
        bool[,] visited = new bool[gw, gh];
        List<Vector2Int> component = new List<Vector2Int>();
        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        for (int i = 0; i < gw; i++)
        {
            for (int j = 0; j < gh; j++)
            {
                if (!inside[i, j] || visited[i, j]) continue;
                component.Clear();
                stack.Push(new Vector2Int(i, j));
                visited[i, j] = true;
                while (stack.Count > 0)
                {
                    Vector2Int c = stack.Pop();
                    component.Add(c);
                    foreach (Vector2Int d in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                    {
                        Vector2Int n = c + d;
                        if (n.x < 0 || n.y < 0 || n.x >= gw || n.y >= gh || visited[n.x, n.y] || !inside[n.x, n.y]) continue;
                        visited[n.x, n.y] = true;
                        stack.Push(n);
                    }
                }
                if (component.Count < minimumCells)
                {
                    foreach (Vector2Int c in component) inside[c.x, c.y] = false;
                }
            }
        }
    }

    /// <summary>内部の頂点について、輪郭までのおおよそのユークリッド距離(マス単位)を求める(2パスのチャンファー距離)。</summary>
    private static float[,] DistanceToOutline(int[,] corner, int gw, int gh, out float maxDistance)
    {
        const float diagonal = 1.41421356f;
        float[,] d = new float[gw + 1, gh + 1];
        for (int i = 0; i <= gw; i++)
            for (int j = 0; j <= gh; j++)
                d[i, j] = corner[i, j] == 2 ? float.MaxValue : 0f;

        for (int j = 0; j <= gh; j++)
        {
            for (int i = 0; i <= gw; i++)
            {
                if (d[i, j] == 0f) continue;
                float best = d[i, j];
                if (i > 0) best = Mathf.Min(best, d[i - 1, j] + 1f);
                if (j > 0) best = Mathf.Min(best, d[i, j - 1] + 1f);
                if (i > 0 && j > 0) best = Mathf.Min(best, d[i - 1, j - 1] + diagonal);
                if (i < gw && j > 0) best = Mathf.Min(best, d[i + 1, j - 1] + diagonal);
                d[i, j] = best;
            }
        }
        maxDistance = 0f;
        for (int j = gh; j >= 0; j--)
        {
            for (int i = gw; i >= 0; i--)
            {
                if (d[i, j] == 0f) continue;
                float best = d[i, j];
                if (i < gw) best = Mathf.Min(best, d[i + 1, j] + 1f);
                if (j < gh) best = Mathf.Min(best, d[i, j + 1] + 1f);
                if (i < gw && j < gh) best = Mathf.Min(best, d[i + 1, j + 1] + diagonal);
                if (i > 0 && j < gh) best = Mathf.Min(best, d[i - 1, j + 1] + diagonal);
                d[i, j] = best;
                maxDistance = Mathf.Max(maxDistance, best);
            }
        }
        return d;
    }

    /// <summary>
    /// 頂点の位置(m)を求める。輪郭の頂点だけは、隣り合う輪郭の頂点との平均へ数回ならし、
    /// 格子由来の階段状のギザギザを滑らかにする。
    /// </summary>
    private static Vector2[,] SmoothOutline(int[,] corner, int gw, int gh, float width, float height)
    {
        Vector2[,] p = new Vector2[gw + 1, gh + 1];
        for (int i = 0; i <= gw; i++)
            for (int j = 0; j <= gh; j++)
                p[i, j] = new Vector2(((float)i / gw - 0.5f) * width, (float)j / gh * height);

        Vector2Int[] neighbours = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
        for (int iteration = 0; iteration < 3; iteration++)
        {
            Vector2[,] next = (Vector2[,])p.Clone();
            for (int i = 0; i <= gw; i++)
            {
                for (int j = 0; j <= gh; j++)
                {
                    if (corner[i, j] != 1) continue;
                    Vector2 sum = p[i, j];
                    int count = 1;
                    foreach (Vector2Int n in neighbours)
                    {
                        int ni = i + n.x, nj = j + n.y;
                        if (ni < 0 || nj < 0 || ni > gw || nj > gh || corner[ni, nj] != 1) continue;
                        sum += p[ni, nj];
                        count++;
                    }
                    next[i, j] = sum / count;
                }
            }
            p = next;
        }
        return p;
    }

    /// <summary>読み取り不可のテクスチャ(Resourcesから読んだ画像など)は、読み取れる複製を作る。</summary>
    private static Texture2D EnsureReadable(Texture2D source)
    {
        if (source.isReadable) return source;
        RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, rt);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        copy.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return copy;
    }
}

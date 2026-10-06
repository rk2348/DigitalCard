using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 3Dスタジアムを組み立てるための共通部品(マテリアル・メッシュ生成・エフェクト)。
///
/// マテリアルについて:
/// ・固体(地面・観客席・柱など)は、プリミティブ生成時の既定マテリアル(URP Lit)を複製して色だけ変える。
///   既定マテリアルはパイプラインが必ず持っているため、ビルドでもシェーダーが欠けない。
/// ・発光・半透明(光の柱、照明、LEDボード、観客、エフェクト)は URP Particles/Unlit を使う。
///   ビルドでシェーダーとキーワードが除外されないよう、Resources/Stadium に同じ設定のマテリアルを置いてあり、
///   それを複製して使う(見つからない場合はエディタ用に Shader.Find で代用する)。
///   Particles/Unlit は頂点カラーを乗算するので、1つのメッシュに色違いの観客などをまとめて描ける。
/// </summary>
public static class StadiumKit
{
    public enum Blend { Opaque, Cutout, Alpha, Additive }

    private static Material litTemplate;
    private static readonly Dictionary<Blend, Material> unlitTemplates = new Dictionary<Blend, Material>();

    /// <summary>URP Litの不透明マテリアル。両面描画にしてあるので、手続き生成した面の向きを気にしなくてよい。</summary>
    public static Material Lit(Color color, float smoothness = 0.25f, float metallic = 0f)
    {
        if (litTemplate == null)
        {
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            litTemplate = probe.GetComponent<Renderer>().sharedMaterial;
            Object.Destroy(probe);
        }

        Material material = new Material(litTemplate);
        material.SetColor("_BaseColor", color);
        material.color = color;
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Cull", (float)CullMode.Off);
        return material;
    }

    /// <summary>URP Particles/Unlitのマテリアル。colorはHDR(1を超える値)にするとBloomで光って見える。</summary>
    public static Material Unlit(Blend blend, Color color, Texture texture = null)
    {
        if (!unlitTemplates.TryGetValue(blend, out Material template) || template == null)
        {
            template = Resources.Load<Material>("Stadium/Stadium" + blend);
            if (template == null) template = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            unlitTemplates[blend] = template;
        }

        Material material = new Material(template);
        Configure(material, blend);
        material.SetColor("_BaseColor", color);
        if (texture != null) material.SetTexture("_BaseMap", texture);
        return material;
    }

    private static void Configure(Material material, Blend blend)
    {
        bool transparent = blend == Blend.Alpha || blend == Blend.Additive;
        material.SetFloat("_Surface", transparent ? 1f : 0f);
        material.SetFloat("_Blend", blend == Blend.Additive ? 2f : 0f);
        material.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
        material.SetFloat("_DstBlend", blend == Blend.Additive ? (float)BlendMode.One
            : blend == Blend.Alpha ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
        material.SetFloat("_ZWrite", transparent ? 0f : 1f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.SetFloat("_AlphaClip", blend == Blend.Cutout ? 1f : 0f);
        material.SetFloat("_Cutoff", 0.5f);

        if (transparent) material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        else material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (blend == Blend.Cutout) material.EnableKeyword("_ALPHATEST_ON");
        else material.DisableKeyword("_ALPHATEST_ON");

        material.SetOverrideTag("RenderType", transparent ? "Transparent" : blend == Blend.Cutout ? "TransparentCutout" : "Opaque");
        material.renderQueue = transparent ? (int)RenderQueue.Transparent
            : blend == Blend.Cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
    }

    public static Color Hdr(Color color, float intensity)
    {
        return new Color(color.r * intensity, color.g * intensity, color.b * intensity, color.a);
    }

    /// <summary>メッシュとマテリアルからGameObjectを作る。</summary>
    public static GameObject CreateMeshObject(string name, Transform parent, Mesh mesh, Material material, bool castShadows = false)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        return go;
    }

    /// <summary>地面に寝かせた正方形の板(光彩・リングなどのテクスチャを貼って使う)。</summary>
    public static GameObject CreateFlatDecal(string name, Transform parent, float size, Material material)
    {
        MeshBuilder builder = new MeshBuilder();
        float h = size / 2f;
        builder.AddQuad(new Vector3(-h, 0f, -h), new Vector3(-h, 0f, h), new Vector3(h, 0f, h), new Vector3(h, 0f, -h),
            Vector3.up, Color.white);
        return CreateMeshObject(name, parent, builder.Build(), material);
    }

    /// <summary>ピボットが下端中央の縦長の板(キャラクター写真や光の柱用)。</summary>
    public static Mesh CreateStandingQuad(float width, float height)
    {
        MeshBuilder builder = new MeshBuilder();
        float w = width / 2f;
        builder.AddQuad(new Vector3(-w, 0f, 0f), new Vector3(-w, height, 0f), new Vector3(w, height, 0f), new Vector3(w, 0f, 0f),
            Vector3.back, Color.white);
        return builder.Build();
    }

    // ==================== メッシュ生成 ====================

    /// <summary>頂点・UV・色・法線を溜めて1つのメッシュにする簡易ビルダー。</summary>
    public sealed class MeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();

        public int VertexCount => vertices.Count;

        /// <summary>a→b→c→d の順(左下→左上→右上→右下)で四角形を追加する。</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color color,
            Vector2? uvMin = null, Vector2? uvMax = null)
        {
            Vector2 min = uvMin ?? Vector2.zero;
            Vector2 max = uvMax ?? Vector2.one;
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            uvs.Add(new Vector2(min.x, min.y)); uvs.Add(new Vector2(min.x, max.y));
            uvs.Add(new Vector2(max.x, max.y)); uvs.Add(new Vector2(max.x, min.y));
            for (int i = 0; i < 4; i++) { normals.Add(normal); colors.Add(color); }
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }

        /// <summary>頂点ごとに色を指定する三角形(光の円錐のグラデーション用)。</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc, Vector3 normal)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            colors.Add(ca); colors.Add(cb); colors.Add(cc);
            uvs.Add(new Vector2(0.5f, 1f)); uvs.Add(Vector2.zero); uvs.Add(Vector2.right);
            for (int i = 0; i < 3; i++) normals.Add(normal);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        }

        public Mesh Build()
        {
            Mesh mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// 角丸長方形の外周を一定数の点で返す(スタジアムの観客席・壁の形)。
    /// 角の円の中心は (±halfStraightX, ±halfStraightZ) に固定し、半径radiusだけを変えれば
    /// どの半径でも同じ点数・同じ並びになるので、段ごとの外周を素直につなげられる。
    /// </summary>
    public static void RoundedRectPath(float halfStraightX, float halfStraightZ, float radius, int arcSteps, int straightSteps,
        List<Vector3> points, List<Vector3> outwardNormals)
    {
        points.Clear();
        outwardNormals.Clear();
        Vector2[] centers =
        {
            new Vector2(halfStraightX, halfStraightZ), new Vector2(-halfStraightX, halfStraightZ),
            new Vector2(-halfStraightX, -halfStraightZ), new Vector2(halfStraightX, -halfStraightZ),
        };
        for (int corner = 0; corner < 4; corner++)
        {
            float startAngle = corner * 90f;
            for (int i = 0; i < arcSteps; i++)
            {
                float angle = (startAngle + 90f * i / arcSteps) * Mathf.Deg2Rad;
                Vector3 normal = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                points.Add(new Vector3(centers[corner].x, 0f, centers[corner].y) + normal * radius);
                outwardNormals.Add(normal);
            }

            // 次の角までの直線部分
            float endAngle = (startAngle + 90f) * Mathf.Deg2Rad;
            Vector3 edgeNormal = new Vector3(Mathf.Cos(endAngle), 0f, Mathf.Sin(endAngle));
            Vector3 from = new Vector3(centers[corner].x, 0f, centers[corner].y) + edgeNormal * radius;
            Vector2 next = centers[(corner + 1) % 4];
            Vector3 to = new Vector3(next.x, 0f, next.y) + edgeNormal * radius;
            for (int i = 0; i < straightSteps; i++)
            {
                points.Add(Vector3.Lerp(from, to, (float)i / straightSteps));
                outwardNormals.Add(edgeNormal);
            }
        }
    }

    /// <summary>地面に寝かせた円環(フィールドのセンターサークルなど)。</summary>
    public static Mesh CreateAnnulus(float innerRadius, float outerRadius, int segments, Color color)
    {
        MeshBuilder builder = new MeshBuilder();
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.PI * 2f * i / segments;
            float a1 = Mathf.PI * 2f * (i + 1) / segments;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            builder.AddQuad(d0 * innerRadius, d0 * outerRadius, d1 * outerRadius, d1 * innerRadius, Vector3.up, color);
        }
        return builder.Build();
    }

    // ==================== エフェクト ====================

    private static EffectHost host;

    /// <summary>エフェクトのコルーチンを動かすための常駐オブジェクト(シーンごとに作り直される)。</summary>
    public static EffectHost Host
    {
        get
        {
            if (host == null) host = new GameObject("StadiumEffects").AddComponent<EffectHost>();
            return host;
        }
    }

    public sealed class EffectHost : MonoBehaviour { }

    /// <summary>火花のように四方へ飛び散る光の粒。</summary>
    public static void Burst(Vector3 position, Color color, int count = 40, float speed = 9f, float size = 0.45f)
    {
        ParticleSystem ps = CreateParticleSystem("Burst", position, Unlit(Blend.Additive, Color.white, OrisamoUI.SoftGlow.texture));
        ParticleSystem.MainModule main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size * 1.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(Hdr(color, 3f), Hdr(Color.Lerp(color, Color.white, 0.6f), 4f));
        main.gravityModifier = 0.6f;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;
        FadeOut(ps);
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        ps.Play();
    }

    /// <summary>勝利時の紙吹雪。</summary>
    public static void Confetti(Vector3 position, float radius = 9f)
    {
        ParticleSystem ps = CreateParticleSystem("Confetti", position, Unlit(Blend.Alpha, Color.white, OrisamoUI.RoundedFlat.texture));
        ParticleSystem.MainModule main = ps.main;
        main.duration = 3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(0.08f, 0.14f);
        main.startSizeZ = new ParticleSystem.MinMaxCurve(1f, 1f); // X/Yと同じ指定方法に揃える
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0.25f;
        main.maxParticles = 1500;
        Gradient palette = new Gradient();
        palette.SetKeys(
            new[]
            {
                new GradientColorKey(OrisamoUI.Gold, 0f), new GradientColorKey(new Color(1f, 0.35f, 0.4f), 0.25f),
                new GradientColorKey(new Color(0.35f, 0.75f, 1f), 0.5f), new GradientColorKey(new Color(0.5f, 1f, 0.55f), 0.75f),
                new GradientColorKey(Color.white, 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(palette) { mode = ParticleSystemGradientMode.RandomColor };

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(radius * 2f, 0.5f, radius * 2f);

        ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.8f;
        noise.frequency = 0.4f;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 260f;
        ps.Play();
    }

    /// <summary>フィールドの上をゆっくり漂う光の粒(常駐)。</summary>
    public static ParticleSystem AmbientMotes(Transform parent, Vector3 area)
    {
        ParticleSystem ps = CreateParticleSystem("AmbientMotes", parent.position + Vector3.up * area.y * 0.5f,
            Unlit(Blend.Additive, Color.white, OrisamoUI.SoftGlow.texture));
        ps.transform.SetParent(parent, true);
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.duration = 5f;
        main.stopAction = ParticleSystemStopAction.None;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.startColor = new ParticleSystem.MinMaxGradient(Hdr(OrisamoUI.Gold, 2.5f), Hdr(new Color(0.7f, 0.6f, 1f), 2.5f));
        main.maxParticles = 400;
        main.prewarm = true;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = area;
        ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        // X/Y/Zは同じ指定方法(ここでは最小〜最大の2値)に揃える必要がある
        velocity.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        FadeInOut(ps);
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 40f;
        ps.Play();
        return ps;
    }

    /// <summary>足元から立ち上る光の柱(登場・必殺技の演出)。</summary>
    public static void Pillar(Vector3 position, Color color, float height = 14f, float width = 3.2f, float duration = 1.4f)
    {
        Host.StartCoroutine(PillarRoutine(position, color, height, width, duration));
    }

    private static IEnumerator PillarRoutine(Vector3 position, Color color, float height, float width, float duration)
    {
        GameObject root = new GameObject("Pillar");
        root.transform.position = position;
        Material material = Unlit(Blend.Additive, Hdr(color, 2.5f), OrisamoUI.FadeDown.texture);
        Mesh quad = CreateStandingQuad(width, height);
        // 上ほど薄くなるよう、テクスチャを上下反転して使う
        material.SetTextureScale("_BaseMap", new Vector2(1f, -1f));
        for (int i = 0; i < 3; i++)
        {
            GameObject plane = CreateMeshObject("Plane", root.transform, quad, material);
            plane.transform.localRotation = Quaternion.Euler(0f, i * 60f, 0f);
        }

        Color baseColor = material.GetColor("_BaseColor");
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = t / duration;
            float widthScale = p < 0.15f ? Mathf.Lerp(0.2f, 1.1f, p / 0.15f) : Mathf.Lerp(1.1f, 0.05f, (p - 0.15f) / 0.85f);
            root.transform.localScale = new Vector3(widthScale, Mathf.Lerp(0.6f, 1.15f, OrisamoUI.EaseOutCubic(p * 2f)), widthScale);
            material.SetColor("_BaseColor", baseColor * (1f - p * 0.6f));
            yield return null;
        }
        Object.Destroy(root);
    }

    /// <summary>地面に沿って広がる衝撃波のリング。</summary>
    public static void Shockwave(Vector3 position, Color color, float radius = 7f, float duration = 0.6f)
    {
        Host.StartCoroutine(ShockwaveRoutine(position, color, radius, duration));
    }

    private static IEnumerator ShockwaveRoutine(Vector3 position, Color color, float radius, float duration)
    {
        Material material = Unlit(Blend.Additive, Hdr(color, 3f), OrisamoUI.ThickRing.texture);
        GameObject ring = CreateFlatDecal("Shockwave", null, 1f, material);
        ring.transform.position = position + Vector3.up * 0.08f;
        Color baseColor = material.GetColor("_BaseColor");
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = OrisamoUI.EaseOutCubic(t / duration);
            ring.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, radius * 2f, p);
            material.SetColor("_BaseColor", baseColor * (1f - p));
            yield return null;
        }
        Object.Destroy(ring);
    }

    private static ParticleSystem CreateParticleSystem(string name, Vector3 position, Material material)
    {
        GameObject go = new GameObject(name);
        go.transform.position = position;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.duration = 1f;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return ps;
    }

    private static void FadeOut(ParticleSystem ps)
    {
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = gradient;
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
    }

    private static void FadeInOut(ParticleSystem ps)
    {
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = gradient;
    }
}

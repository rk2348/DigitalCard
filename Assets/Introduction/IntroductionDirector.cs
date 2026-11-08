using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Evaluates a real Unity stage at any time. No recorded footage or frame files.</summary>
public class IntroductionDirector : MonoBehaviour
{
    public IntroductionSequence sequence;
    public Camera movieCamera;
    public Font japanese;
    public AudioSource music;
    GameObject stage;
    readonly List<Object> owned = new List<Object>();
    Transform fire, water, projectile, halo;
    readonly List<Transform> sparks = new List<Transform>();
    Canvas canvas;
    Text heading, caption, chapter, center, cardName, cardStep, hpText;
    RectTransform card;
    RawImage cardArt;
    Image scan, flash, hpFire, hpWater;
    GameObject health;
    float elapsed;
    public float SampledTime { get; private set; }

    void Start()
    {
        if (!Application.isPlaying) return;
        elapsed = 0;
        EnsureStage();
        if (music != null && sequence != null && sequence.music != null)
        { music.clip = sequence.music; music.loop = true; music.Play(); }
    }
    void Update()
    {
        if (!Application.isPlaying || sequence == null) return;
        Evaluate(elapsed);
        if (music != null) music.volume = sequence.musicVolume * Mathf.Clamp01(elapsed) * Mathf.Clamp01((sequence.Duration - elapsed) / 4);
        elapsed += Time.unscaledDeltaTime;
        if (elapsed >= sequence.Duration && music != null) music.Stop();
    }
    void OnDisable() => ReleaseStage();
    static void Release(Object item) { if (item == null) return; if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
    public void ReleaseStage()
    {
        Release(stage); stage = null;
        foreach (var item in owned) Release(item);
        owned.Clear(); sparks.Clear();
    }
    T Own<T>(T item) where T : Object { item.hideFlags = HideFlags.DontSave; owned.Add(item); return item; }
    Material Material(Color color)
    {
        var unlit = Shader.Find("Universal Render Pipeline/Unlit"); var m = Own(unlit != null && unlit.isSupported ? new Material(unlit) : StadiumKit.Unlit(StadiumKit.Blend.Opaque, color));
        m.SetColor("_BaseColor", color); return m;
    }
    Transform Primitive(string name, PrimitiveType type, Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(stage.transform, false);
        go.transform.localPosition = pos; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = Material(color);
        var collider = go.GetComponent<Collider>(); if (collider != null) Release(collider);
        return go.transform;
    }
    Transform Ring(string name, float radius, float y, Color color)
    {
        var go = new GameObject(name); go.transform.SetParent(stage.transform, false);
        var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.loop = true; line.positionCount = 96;
        line.widthMultiplier = .055f; line.sharedMaterial = Material(color);
        for (int i = 0; i < 96; i++) { float a = i * Mathf.PI * 2 / 96; line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius)); }
        return go.transform;
    }
    Transform Character(string name, ElementType element, out Texture2D art)
    {
        art = Own(DemoCharacterArt.Create(new CharacterStats(name) { element = element, maxHp = 170 }));
        var sculpture = PhotoSculpture.Build(art, 4.4f, 5.6f);
        var go = new GameObject(name); go.transform.SetParent(stage.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = Own(sculpture.mesh);
        go.AddComponent<MeshRenderer>().sharedMaterial = Own(PhotoSculpture.CreateMaterial(art));
        return go.transform;
    }
    T UI<T>(string name, Transform parent, Vector2 size, Vector2 pos) where T : Component
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.sizeDelta = size; rect.anchoredPosition = pos;
        return go.AddComponent<T>();
    }
    Text Text(string name, Transform parent, string value, int size, Vector2 bounds, Vector2 pos, Color color)
    {
        var t = UI<Text>(name, parent, bounds, pos); t.font = japanese; t.fontSize = size; t.text = value;
        t.color = color; t.alignment = TextAnchor.MiddleCenter;
        t.resizeTextForBestFit = true; t.resizeTextMinSize = 15; t.resizeTextMaxSize = size; return t;
    }
    public void EnsureStage()
    {
        if (stage != null) return;
        stage = new GameObject("Movie Stage (Unity objects)") { hideFlags = HideFlags.DontSave };
        stage.transform.SetParent(transform, false);
        var gold = new Color(1,.76f,.28f);
        var cyan = new Color(.2f,.78f,1);
        Primitive("Arena", PrimitiveType.Cylinder, new Vector3(0,-.4f,0), new Vector3(22,.4f,22), new Color(.055f,.075f,.13f));
        Ring("Gold outer ring", 10.5f,.02f,gold); Ring("Inner ring",8.8f,.025f,cyan);
        Ring("Center circle",2,.03f,gold);
        for (int i=0;i<48;i++)
        {
            float a = i * Mathf.PI * 2 / 48;
            var pillar = Primitive("Arena light " + i, PrimitiveType.Cube, new Vector3(Mathf.Cos(a)*13,1.1f,Mathf.Sin(a)*13), new Vector3(.13f,2.2f,.13f), i%2==0?gold:cyan);
            pillar.rotation = Quaternion.Euler(0,-a*Mathf.Rad2Deg,0);
        }
        fire = Character("ホムラドラゴン", ElementType.Fire, out var fireArt);
        water = Character("ミナモスライム", ElementType.Water, out var waterArt);
        projectile = Primitive("Attack light", PrimitiveType.Sphere, Vector3.zero, Vector3.one, gold);
        halo = Ring("Summoning circle", 2.4f,.08f,gold);
        for (int i=0;i<24;i++) sparks.Add(Primitive("Summoning spark " + i, PrimitiveType.Sphere, Vector3.zero, Vector3.one*.09f, i%2==0?gold:cyan));
        var light = new GameObject("Key light").AddComponent<Light>(); light.transform.SetParent(stage.transform,false);
        light.type = LightType.Directional; light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(35,-25,0);
        var fill = new GameObject("Fill light").AddComponent<Light>(); fill.transform.SetParent(stage.transform,false);
        fill.type = LightType.Directional; fill.intensity = .8f; fill.transform.rotation = Quaternion.Euler(25,150,0);
        canvas = new GameObject("Movie graphics").AddComponent<Canvas>(); canvas.transform.SetParent(stage.transform,false);
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = movieCamera; canvas.planeDistance = .5f;
        var scaler = canvas.gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280,720); scaler.matchWidthOrHeight = .5f;
        var bottom = UI<Image>("Caption panel",canvas.transform,new Vector2(1280,155),new Vector2(0,-282)); bottom.color = new Color(.015f,.025f,.055f,.96f);
        heading = Text("Heading",canvas.transform,"",40,new Vector2(1200,65),new Vector2(0,-250),gold);
        caption = Text("Caption",canvas.transform,"",24,new Vector2(1200,60),new Vector2(0,-310),Color.white);
        chapter = Text("Chapter",canvas.transform,"",18,new Vector2(1180,36),new Vector2(0,322),gold);
        center = Text("Hero title",canvas.transform,"",62,new Vector2(1100,160),new Vector2(0,150),Color.white);
        var panel = UI<Image>("Character card",canvas.transform,new Vector2(335,410),new Vector2(-340,15)); panel.color = gold; card = panel.rectTransform;
        var inset = UI<Image>("Card face",card,new Vector2(325,400),Vector2.zero); inset.color = new Color(.06f,.08f,.16f);
        cardArt = UI<RawImage>("Character art",card,new Vector2(230,260),new Vector2(0,25)); cardArt.texture = fireArt;
        cardName = Text("Card name",card,"",26,new Vector2(310,50),new Vector2(0,-130),gold);
        cardStep = Text("Card step",card,"",19,new Vector2(310,48),new Vector2(0,172),Color.white);
        Text("Card footer",card,"ORIGINAL SUMMONER",16,new Vector2(310,35),new Vector2(0,-180),Color.white);
        scan = UI<Image>("Scan line",card,new Vector2(270,3),Vector2.zero); scan.color = cyan;
        health = new GameObject("Battle display",typeof(RectTransform)); health.transform.SetParent(canvas.transform,false);
        hpFire = UI<Image>("Fire HP",health.transform,new Vector2(320,10),new Vector2(-365,266)); hpFire.color = new Color(1,.45f,.25f);
        hpWater = UI<Image>("Water HP",health.transform,new Vector2(320,10),new Vector2(365,266)); hpWater.color = cyan;
        hpText = Text("HP labels",health.transform,"",21,new Vector2(1180,44),new Vector2(0,292),Color.white);
        flash = UI<Image>("Transitions",canvas.transform,new Vector2(1280,720),Vector2.zero); flash.color = Color.clear;
    }

    public void Evaluate(float time)
    {
        if (sequence == null || sequence.shots.Count == 0) return;
        EnsureStage();
        SampledTime = Mathf.Clamp(time,0,sequence.Duration-1f/IntroductionSequence.Fps);
        int index = sequence.FindShot(SampledTime,out float local);
        var shot = sequence.shots[index]; float duration = Mathf.Max(1,shot.seconds);
        float u = Mathf.Clamp01(local/duration), smooth = Mathf.SmoothStep(0,1,u);
        movieCamera.transform.position = Vector3.Lerp(shot.cameraFrom,shot.cameraTo,smooth);
        movieCamera.transform.LookAt(shot.lookAt); movieCamera.fieldOfView = shot.fieldOfView;
        bool capture = shot.kind == IntroductionShotKind.Capture, naming = shot.kind == IntroductionShotKind.Name;
        bool summon = shot.kind == IntroductionShotKind.Summon, battle = shot.kind == IntroductionShotKind.Battle;
        bool choice = shot.kind == IntroductionShotKind.Choice;
        heading.text = shot.heading; caption.text = shot.caption;
        chapter.text = $"ORISAMO     /     {index+1:00}   {shot.kind.ToString().ToUpperInvariant()}";
        center.text = shot.kind == IntroductionShotKind.Title ? "ORIGINAL SUMMONER" : shot.kind == IntroductionShotKind.Closing ? "つくって、召喚、バトル。" : choice ? (local%6<2?"強":local%6<4?"普":"弱") : "";
        center.color = new Color(1,.92f,.7f,Mathf.Clamp01(local/.7f));
        card.gameObject.SetActive(capture || naming);
        card.localScale = Vector3.one * Mathf.Lerp(.85f,1,Mathf.Clamp01(local/1.2f));
        card.localRotation = Quaternion.Euler(0,0,Mathf.Sin(local*.8f)*1.5f);
        cardName.text = naming ? "ホムラドラゴン".Substring(0,Mathf.Min(7,(int)(local*3))) : "キミの作品";
        cardStep.text = capture ? (u<.5f?"01  作品を取り込む":"02  背景を切り抜く") : "03  名前をつけて登録";
        cardArt.color = Color.Lerp(new Color(.4f,.5f,.65f),Color.white,Mathf.Clamp01(local/2));
        scan.gameObject.SetActive(capture); scan.rectTransform.anchoredPosition = new Vector2(0,145-Mathf.Repeat(local*.4f,1)*250);
        float bounce = Mathf.Sin(local*2.5f)*.10f;
        fire.localPosition = new Vector3(-3,bounce,0); water.localPosition = new Vector3(3,Mathf.Sin(local*2.5f+1)*.1f,0);
        fire.localRotation = Quaternion.Euler(0,-12+Mathf.Sin(local)*8,0); water.localRotation = Quaternion.Euler(0,12-Mathf.Sin(local)*8,0);
        float scale = summon ? Mathf.SmoothStep(0,1,Mathf.Clamp01(local/2.5f)) : 1;
        fire.localScale = water.localScale = Vector3.one*scale;
        water.gameObject.SetActive(!(capture||naming||shot.kind==IntroductionShotKind.Hook));
        halo.localPosition = new Vector3(-3,0,0); halo.localScale = Vector3.one * (1+Mathf.Sin(local*3)*.07f);
        float cycle = Mathf.Repeat(local,3)/3, impact = Mathf.Clamp01(1-Mathf.Abs(cycle-.68f)*18);
        projectile.gameObject.SetActive(battle);
        projectile.localPosition = Vector3.Lerp(new Vector3(-3,2.4f,0),new Vector3(3,2.4f,0),Mathf.Clamp01(cycle/.68f));
        projectile.localScale = Vector3.one*(.25f+impact*1.8f);
        if (battle) { fire.localPosition += Vector3.right*Mathf.Sin(cycle*Mathf.PI)*.7f; water.localRotation *= Quaternion.Euler(0,0,-impact*18); }
        for (int i=0;i<sparks.Count;i++)
        {
            float a = i*Mathf.PI*2/sparks.Count+local*.8f;
            sparks[i].gameObject.SetActive(summon||battle||shot.kind==IntroductionShotKind.Hook);
            sparks[i].localPosition = new Vector3(-3+Mathf.Cos(a)*2.1f,Mathf.Repeat(i*.27f+local*.7f,4.8f),Mathf.Sin(a)*2.1f);
        }
        health.SetActive(choice||battle);
        float hp = battle ? Mathf.Max(.12f,1-Mathf.Floor(local/3)*.23f-impact*.1f) : 1;
        hpFire.rectTransform.sizeDelta = new Vector2(320*(battle?Mathf.Max(.35f,1-u*.6f):1),10);
        hpWater.rectTransform.sizeDelta = new Vector2(320*hp,10);
        hpText.text = $"ホムラドラゴン                                                      ミナモスライム   {(int)(hp*170)} / 170";
        float fade = Mathf.Max(1-Mathf.Clamp01(local/.35f),1-Mathf.Clamp01((duration-local)/.35f));
        flash.color = new Color(.015f,.025f,.055f,fade);
        Canvas.ForceUpdateCanvases();
    }
}

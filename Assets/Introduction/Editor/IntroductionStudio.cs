using System;
using System.IO;
using UnityEditor;
using UnityEditor.Media;
using UnityEditor.SceneManagement;
using Unity.Collections;
using UnityEngine;

public sealed class IntroductionStudio : EditorWindow
{
    IntroductionDirector director;
    RenderTexture preview;
    Vector2 scroll;
    float seconds;
    bool playing;
    double lastTick;
    string status = "構成アセットのカット順・秒数・字幕・カメラを編集できます。";

    [MenuItem("オリサモ/紹介動画/動画制作ウィンドウ")]
    public static void Open() => GetWindow<IntroductionStudio>("紹介動画スタジオ");
    void OnEnable() { EditorApplication.update += Tick; lastTick = EditorApplication.timeSinceStartup; }
    void OnDisable()
    {
        EditorApplication.update -= Tick;
        if (preview != null) { preview.Release(); DestroyImmediate(preview); }
    }
    void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (playing && director != null && director.sequence != null && !EditorApplication.isPlaying)
        {
            seconds = Mathf.Min(director.sequence.Duration-1f/IntroductionSequence.Fps,seconds+(float)(now-lastTick));
            if (seconds >= director.sequence.Duration-1f/IntroductionSequence.Fps) playing = false;
            Repaint();
        }
        lastTick = now;
    }
    void OnGUI()
    {
        EditorGUILayout.LabelField("オリジナルサモナー / Unity動画制作",EditorStyles.boldLabel);
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorGUILayout.HelpBox("編集・書き出しはPlayモードを停止して行ってください。",MessageType.Info); return; }
        if (director == null) director = FindFirstObjectByType<IntroductionDirector>();
        if (director == null || director.sequence == null)
        {
            if (GUILayout.Button("Unity動画シーンを開く / 初期作成"))
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                if (File.Exists(IntroductionBuilder.ScenePath)) EditorSceneManager.OpenScene(IntroductionBuilder.ScenePath);
                director = FindFirstObjectByType<IntroductionDirector>();
                if (director == null || director.sequence == null) IntroductionBuilder.Build();
            }
            return;
        }
        var sequence = director.sequence;
        EditorGUILayout.LabelField($"{sequence.Duration:0.##}秒  /  1280 × 720  /  30fps  /  Unity標準MP4出力");
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (preview == null) { preview = new RenderTexture(1280,720,24); preview.Create(); }
        Rect view = GUILayoutUtility.GetAspectRect(16f/9f,GUILayout.MaxHeight(360));
        if (Event.current.type == EventType.Repaint)
        {
            try { Render(director,preview,seconds); GUI.DrawTexture(view,preview,ScaleMode.ScaleToFit,false); }
            catch (Exception ex) { playing = false; status = ex.Message; }
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(playing?"一時停止":"映像プレビュー再生")) playing = !playing;
        if (GUILayout.Button("先頭へ")) { seconds=0; playing=false; }
        EditorGUILayout.EndHorizontal();
        seconds = EditorGUILayout.Slider("再生位置（秒）",seconds,0,Mathf.Max(0,sequence.Duration-1f/IntroductionSequence.Fps));
        float offset=0;
        for (int i=0;i<sequence.shots.Count;i++)
        {
            var shot=sequence.shots[i];
            if (GUILayout.Button($"{i+1:00}   {offset:0.0}秒   {shot.heading}")) { seconds=offset+.5f; playing=false; }
            offset+=Mathf.Max(1,shot.seconds);
        }
        var serialized=new SerializedObject(sequence); serialized.Update();
        EditorGUILayout.PropertyField(serialized.FindProperty("music"),new GUIContent("BGM"));
        EditorGUILayout.PropertyField(serialized.FindProperty("musicVolume"),new GUIContent("BGM音量"));
        EditorGUILayout.PropertyField(serialized.FindProperty("shots"),new GUIContent("カット構成（並び替え・追加・削除）"),true);
        serialized.ApplyModifiedProperties();
        EditorGUILayout.HelpBox("各カットのKind＝演出、Seconds＝長さ、Camera From/To＝移動の始点・終点、Look At＝注視点。映像プレビューは無音。音楽はシーンのPlayかMP4で確認できます。",MessageType.Info);
        if (GUILayout.Button("構成を保存")) AssetDatabase.SaveAssets();
        if (GUILayout.Button("UnityからMP4を書き出す（BGM付き）"))
        {
            playing=false;
            string folder=Path.GetFullPath("deliverables/OriginalSummoner_Unity"); Directory.CreateDirectory(folder);
            string path=EditorUtility.SaveFilePanel("Unity動画を書き出す",folder,"オリサモ_UnityNative_"+DateTime.Now.ToString("yyyyMMdd_HHmmss"),"mp4");
            if (!string.IsNullOrEmpty(path))
            {
                try { ExportMovie(director,path); status="完成: "+path; EditorUtility.RevealInFinder(path); }
                catch (OperationCanceledException) { status="書き出しを中止しました。"; }
                catch (Exception ex) { status=ex.Message; Debug.LogException(ex); }
            }
        }
        EditorGUILayout.HelpBox(status,MessageType.Info);
        EditorGUILayout.EndScrollView();
    }
    static void Render(IntroductionDirector d,RenderTexture target,float time)
    {
        var oldTarget=d.movieCamera.targetTexture; var oldActive=RenderTexture.active;
        try { d.movieCamera.targetTexture=target; d.Evaluate(time); d.movieCamera.Render(); }
        finally { d.movieCamera.targetTexture=oldTarget; RenderTexture.active=oldActive; }
    }
    static void ReadPixels(RenderTexture target,Texture2D pixels)
    {
        var old=RenderTexture.active;
        try { RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0); pixels.Apply(); }
        finally { RenderTexture.active=old; }
    }
    public static void ExportMovie(IntroductionDirector d,string destination)
    {
        var sequence=d.sequence;
        if (sequence == null || sequence.shots.Count == 0) throw new InvalidOperationException("カットを1つ以上設定してください。");
        foreach (var shot in sequence.shots)
            if (shot == null || (float.IsNaN(shot.seconds) || float.IsInfinity(shot.seconds)) || shot.seconds<1) throw new InvalidOperationException("カットの秒数は1秒以上にしてください。");
        var clip=sequence.music;
        float[] music=null;
        if (clip != null)
        {
            clip.LoadAudioData(); music=new float[clip.samples*clip.channels];
            if (!clip.GetData(music,0)) throw new InvalidOperationException("BGMのLoad TypeをDecompress On Loadにしてください。");
        }
        destination=Path.GetFullPath(destination); Directory.CreateDirectory(Path.GetDirectoryName(destination));
        string pending=Path.Combine(Path.GetDirectoryName(destination),".unity-"+Guid.NewGuid().ToString("N")+".mp4");
        var target=new RenderTexture(1280,720,24); var pixels=new Texture2D(1280,720,TextureFormat.RGBA32,false);
        float original=d.SampledTime;
        try
        {
            target.Create();
            var video=new VideoTrackAttributes { frameRate=new MediaRational(IntroductionSequence.Fps),width=1280,height=720,includeAlpha=false };
            var audio=new AudioTrackAttributes { sampleRate=new MediaRational(48000),channelCount=2,language="ja" };
            using (var encoder=clip == null?new MediaEncoder(pending,video):new MediaEncoder(pending,video,audio))
            using (var ownedBuffer=new NativeArray<float>(1600*2,Allocator.Persistent))
            {
                var buffer=ownedBuffer;
                for (int frame=0;frame<sequence.FrameCount;frame++)
                {
                    if (!Application.isBatchMode && EditorUtility.DisplayCancelableProgressBar("Unityで動画を書き出し",$"{frame+1} / {sequence.FrameCount}",(float)frame/sequence.FrameCount)) throw new OperationCanceledException();
                    Render(d,target,(float)frame/IntroductionSequence.Fps); ReadPixels(target,pixels);
                    if (!encoder.AddFrame(pixels)) throw new IOException("映像のエンコードに失敗: "+frame);
                    if (clip != null)
                    {
                        for (int sample=0;sample<1600;sample++)
                        {
                            double time=(frame*1600L+sample)/48000.0;
                            double source=time*clip.frequency; int a=(int)(source%clip.samples), b=(a+1)%clip.samples;
                            float blend=(float)(source-Math.Floor(source));
                            float gain=sequence.musicVolume*Mathf.Clamp01((float)time)*Mathf.Clamp01((sequence.Duration-(float)time)/4);
                            for (int channel=0;channel<2;channel++)
                            { int c=Mathf.Min(channel,clip.channels-1); buffer[sample*2+channel]=Mathf.Lerp(music[a*clip.channels+c],music[b*clip.channels+c],blend)*gain; }
                        }
                        if (!encoder.AddSamples(buffer)) throw new IOException("音声のエンコードに失敗: "+frame);
                    }
                    if (frame%300==0) Debug.Log("UNITY_NATIVE_FRAME "+frame);
                }
            }
            File.Copy(pending,destination,true);
            Debug.Log("UNITY_NATIVE_COMPLETE "+destination);
        }
        finally
        {
            target.Release(); DestroyImmediate(target); DestroyImmediate(pixels);
            EditorUtility.ClearProgressBar(); d.Evaluate(original);
            if (File.Exists(pending)) File.Delete(pending);
        }
    }
    [MenuItem("オリサモ/紹介動画/標準構成を検証して書き出す")]
    public static void ExportInEditor()
    {
        try
        {
            var d=FindFirstObjectByType<IntroductionDirector>();
            if (d == null || d.sequence == null) { IntroductionBuilder.Build(); d=FindFirstObjectByType<IntroductionDirector>(); }
            if (d == null || d.sequence == null) return;
            Verify(d);
            ExportMovie(d,"deliverables/OriginalSummoner_Unity/オリジナルサモナー_UnityNative.mp4");
            File.WriteAllText("tmp/native-review/verified.txt","Unity scene render + deterministic seek + native MediaEncoder export completed.");
            Open();
        }
        catch (Exception ex) { Debug.LogException(ex); }
    }
    public static void ExportBatch()
    {
        try
        {
            if (!File.Exists(IntroductionBuilder.SequencePath)) IntroductionBuilder.Build();
            else EditorSceneManager.OpenScene(IntroductionBuilder.ScenePath);
            var d=FindFirstObjectByType<IntroductionDirector>();
            if (d == null || d.sequence == null) throw new InvalidOperationException("Unity動画シーンを作成してください。");
            Verify(d);
            ExportMovie(d,"deliverables/OriginalSummoner_Unity/オリジナルサモナー_UnityNative.mp4");
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    static void Verify(IntroductionDirector d)
    {
        string folder=Path.GetFullPath("tmp/native-review"); Directory.CreateDirectory(folder);
        var target=new RenderTexture(1280,720,24); var pixels=new Texture2D(1280,720,TextureFormat.RGBA32,false);
        try
        {
            target.Create(); float offset=0;
            for (int i=0;i<d.sequence.shots.Count;i++)
            {
                Render(d,target,offset+d.sequence.shots[i].seconds*.5f); ReadPixels(target,pixels);
                File.WriteAllBytes(Path.Combine(folder,$"shot-{i:00}.png"),pixels.EncodeToPNG());
                offset+=d.sequence.shots[i].seconds;
            }
            Render(d,target,4); ReadPixels(target,pixels); byte[] first=pixels.EncodeToPNG();
            Render(d,target,56); Render(d,target,4); ReadPixels(target,pixels); byte[] second=pixels.EncodeToPNG();
            if (Convert.ToBase64String(first)!=Convert.ToBase64String(second)) throw new InvalidOperationException("シーク後にフレームが一致しません。");
            Debug.Log("UNITY_NATIVE_SEEK_VERIFIED");
        }
        finally { target.Release(); DestroyImmediate(target); DestroyImmediate(pixels); }
    }
}




using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class IntroductionBuilder
{
    public const string ScenePath = "Assets/Scenes/Introduction.unity";
    public const string SequencePath = "Assets/Introduction/IntroductionSequence.asset";
    [MenuItem("オリサモ/紹介動画/シーンを作成")]
    public static void Build()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var sequence = AssetDatabase.LoadAssetAtPath<IntroductionSequence>(SequencePath);
        if (sequence == null)
        {
            sequence = ScriptableObject.CreateInstance<IntroductionSequence>(); sequence.SetDefaults();
            sequence.music = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Introduction/Music.wav");
            AssetDatabase.CreateAsset(sequence,SequencePath);
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var cam = new GameObject("Movie Camera").AddComponent<Camera>();
        cam.gameObject.AddComponent<AudioListener>(); cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.015f,.025f,.055f); cam.nearClipPlane = .1f; cam.farClipPlane = 150;
        var director = new GameObject("Introduction Director").AddComponent<IntroductionDirector>();
        director.movieCamera = cam; director.sequence = sequence;
        director.japanese = AssetDatabase.LoadAssetAtPath<Font>("Assets/Introduction/Japanese.ttf");
        director.music = director.gameObject.AddComponent<AudioSource>(); director.music.playOnAwake = false;
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),ScenePath);
        AssetDatabase.SaveAssets();
        director.Evaluate(1);
    }
    // Compatibility for the previous batch entry point. All encoding is now inside Unity.
    public static void Export() => IntroductionStudio.ExportBatch();
}

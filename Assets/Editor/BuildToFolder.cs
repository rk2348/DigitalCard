using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// メニュー「オリサモ > ビルド > Build_3 にビルド」で、プロジェクト直下の Build_3 フォルダに
/// Windows版をビルドする(Build Settings に登録されている有効なシーンを使う)。
/// </summary>
public static class BuildToFolder
{
    private const string FolderName = "Build_3";

    [MenuItem("オリサモ/ビルド/Build_3 にビルド")]
    public static void BuildToBuild3()
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string folder = Path.Combine(projectRoot, FolderName);
        Directory.CreateDirectory(folder);

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(folder, "DigitalCard.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildToFolder] {FolderName} にビルドしました ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0}秒): {folder}");
            EditorUtility.RevealInFinder(Path.Combine(folder, "DigitalCard.exe"));
        }
        else
        {
            Debug.LogError($"[BuildToFolder] ビルドに失敗しました: {summary.result} (エラー {summary.totalErrors} 件)");
        }
    }
}

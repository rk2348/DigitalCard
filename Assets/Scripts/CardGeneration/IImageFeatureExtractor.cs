using UnityEngine;

namespace Orisamo.CardGeneration
{
    /// <summary>
    /// 画像特徴量抽出アルゴリズムの抽象インターフェース。
    /// 現状は簡易ヒューリスティック（SimpleImageFeatureExtractor）で実装しているが、
    /// 将来的に本格的なAIモデル（例: Unity Sentis / Barracuda等によるCNN推論）へ
    /// 差し替える場合も、CardGenerator側のコードは変更せずに済む。
    /// </summary>
    public interface IImageFeatureExtractor
    {
        ImageFeatures Extract(Color32[] pixels, int width, int height);
    }
}

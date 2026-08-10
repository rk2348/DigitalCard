namespace Orisamo.CardGeneration
{
    /// <summary>
    /// イラスト画像から抽出した特徴量（すべて0.0〜1.0に正規化）。
    /// ステータス・スキル生成の入力として使用する。
    /// </summary>
    [System.Serializable]
    public struct ImageFeatures
    {
        /// <summary>線・輪郭の複雑さ（エッジ密度）</summary>
        public float lineComplexity;

        /// <summary>使用色数の豊富さ</summary>
        public float colorVariety;

        /// <summary>平均彩度</summary>
        public float averageSaturation;
    }
}

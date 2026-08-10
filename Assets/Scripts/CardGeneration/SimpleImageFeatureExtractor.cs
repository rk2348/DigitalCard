using System.Collections.Generic;
using UnityEngine;

namespace Orisamo.CardGeneration
{
    /// <summary>
    /// 暫定の画像特徴量抽出アルゴリズム。
    /// 設計書10章の方針「まず簡易な特徴量による暫定ロジックで運用開始し、精度向上は
    /// 段階的に行う」に沿った実装。
    ///
    /// 抽出する特徴量:
    ///  - lineComplexity: 輝度勾配（簡易エッジ検出）の密度
    ///  - colorVariety: 量子化した色バケットの使用種類数
    ///  - averageSaturation: HSVの平均彩度
    ///
    /// 処理負荷軽減のため、画像全体ではなく格子状にサンプリングして計算する。
    /// </summary>
    public class SimpleImageFeatureExtractor : IImageFeatureExtractor
    {
        private const int SampleGridSize = 96; // サンプリング解像度（1辺あたり）
        private const float EdgeThreshold = 0.12f; // この値以上の輝度勾配を「線」とみなす
        private const int ColorBucketCount = 64; // 4段階×3チャンネル = 64通り

        public ImageFeatures Extract(Color32[] pixels, int width, int height)
        {
            if (pixels == null || pixels.Length == 0 || width <= 1 || height <= 1)
                return new ImageFeatures();

            int gridW = Mathf.Min(SampleGridSize, width);
            int gridH = Mathf.Min(SampleGridSize, height);

            var luminance = new float[gridW, gridH];
            var colorBuckets = new HashSet<int>();
            float saturationSum = 0f;
            int sampleCount = 0;

            for (int gy = 0; gy < gridH; gy++)
            {
                int sy = gy * height / gridH;
                for (int gx = 0; gx < gridW; gx++)
                {
                    int sx = gx * width / gridW;
                    Color32 c = pixels[sy * width + sx];

                    float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
                    luminance[gx, gy] = 0.299f * r + 0.587f * g + 0.114f * b;

                    colorBuckets.Add(QuantizeColor(c));

                    Color.RGBToHSV(new Color(r, g, b), out _, out float s, out _);
                    saturationSum += s;
                    sampleCount++;
                }
            }

            float edgeHits = 0f;
            int edgeSamples = 0;
            for (int gy = 1; gy < gridH - 1; gy++)
            {
                for (int gx = 1; gx < gridW - 1; gx++)
                {
                    float dx = luminance[gx + 1, gy] - luminance[gx - 1, gy];
                    float dy = luminance[gx, gy + 1] - luminance[gx, gy - 1];
                    float magnitude = Mathf.Sqrt(dx * dx + dy * dy);
                    if (magnitude >= EdgeThreshold) edgeHits++;
                    edgeSamples++;
                }
            }

            return new ImageFeatures
            {
                lineComplexity = edgeSamples > 0 ? Mathf.Clamp01(edgeHits / edgeSamples) : 0f,
                colorVariety = Mathf.Clamp01((float)colorBuckets.Count / ColorBucketCount),
                averageSaturation = sampleCount > 0 ? Mathf.Clamp01(saturationSum / sampleCount) : 0f
            };
        }

        /// <summary>RGB各チャンネルを4段階（2bit）に量子化し、64通りのバケットIDにする。</summary>
        private static int QuantizeColor(Color32 c)
        {
            int r = c.r >> 6;
            int g = c.g >> 6;
            int b = c.b >> 6;
            return (r << 4) | (g << 2) | b;
        }
    }
}

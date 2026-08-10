using UnityEngine;

namespace Orisamo.Data
{
    /// <summary>
    /// 6属性間のダメージ倍率マスタ。
    /// インスペクタ上で編集可能にし、属性・数値バランスが未確定な間も
    /// 実装を進められるようにする。
    /// 行=攻撃側属性、列=防御側属性。
    /// </summary>
    [CreateAssetMenu(fileName = "AttributeMatrix", menuName = "Orisamo/AttributeMatrixSO")]
    public class AttributeMatrixSO : ScriptableObject
    {
        private const int AttributeCount = 6;

        [Tooltip("index = attacker * 6 + defender。値は倍率（1.5=有利, 0.5=不利, 1.0=等倍）")]
        [SerializeField]
        private float[] multipliers = CreateDefaultMatrix();

        private static float[] CreateDefaultMatrix()
        {
            var arr = new float[AttributeCount * AttributeCount];
            for (int i = 0; i < arr.Length; i++) arr[i] = 1.0f;
            return arr;
        }

        public float GetMultiplier(CardAttribute attacker, CardAttribute defender)
        {
            if (multipliers == null || multipliers.Length != AttributeCount * AttributeCount)
            {
                Debug.LogWarning("AttributeMatrixSO: 相性表が未初期化のため等倍(1.0)を返します。");
                return 1.0f;
            }

            int a = (int)attacker;
            int d = (int)defender;
            return multipliers[a * AttributeCount + d];
        }

        public void SetMultiplier(CardAttribute attacker, CardAttribute defender, float value)
        {
            if (multipliers == null || multipliers.Length != AttributeCount * AttributeCount)
                multipliers = CreateDefaultMatrix();

            int a = (int)attacker;
            int d = (int)defender;
            multipliers[a * AttributeCount + d] = value;
        }

        /// <summary>インスペクタ以外（エディタ拡張・初期データ投入等）から一括設定する場合に使用。</summary>
        public void SetAll(float[] newMatrix)
        {
            if (newMatrix == null || newMatrix.Length != AttributeCount * AttributeCount)
            {
                Debug.LogError($"AttributeMatrixSO.SetAll: 要素数は{AttributeCount * AttributeCount}である必要があります。");
                return;
            }
            multipliers = newMatrix;
        }
    }
}

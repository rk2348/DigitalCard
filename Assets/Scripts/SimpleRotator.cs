using UnityEngine;

/// <summary>
/// アタッチされたオブジェクトをゆっくり回転させる汎用スクリプト。
/// 現在はキャラクター作成時の仮3Dプリミティブの見栄え用に使用。
/// 将来的に本番のキャラクターモデルに差し替えた後もそのまま流用できる。
/// </summary>
public class SimpleRotator : MonoBehaviour
{
    [Tooltip("1秒あたりの回転角度（Y軸周り）")]
    [SerializeField] private float rotationSpeed = 40f;

    private void Update()
    {
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
    }
}

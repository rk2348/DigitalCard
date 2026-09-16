using UnityEngine;

/// <summary>
/// アタッチされたオブジェクトをY軸周りにゆっくり回転させる演出用コンポーネント。
/// </summary>
public class SimpleRotator : MonoBehaviour
{
    [SerializeField] private float degreesPerSecond = 30f;

    private void Update()
    {
        transform.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.World);
    }
}

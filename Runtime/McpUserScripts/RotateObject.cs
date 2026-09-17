using UnityEngine;

/// <summary>
/// アタッチしたオブジェクトを指定した軸・速度で回転させ続けるシンプルなコンポーネント。
/// MCP からの依頼で追加されたユーザースクリプト。
/// </summary>
public class RotateObject : MonoBehaviour
{
    [Tooltip("回転軸（ローカル空間）")]
    [SerializeField] private Vector3 rotationAxis = Vector3.up;

    [Tooltip("回転速度（度/秒）")]
    [SerializeField] private float degreesPerSecond = 90f;

    private void Update()
    {
        transform.Rotate(rotationAxis, degreesPerSecond * Time.deltaTime, Space.Self);
    }
}

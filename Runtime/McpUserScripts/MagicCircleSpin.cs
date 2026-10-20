using UnityEngine;

/// <summary>
/// 魔法陣をゆっくり回し続けるコンポーネント。プレファブの最上位に付ける。
/// 既定はワールドの上方向まわりに毎秒 15 度（水平に置いた魔法陣が面内で回る）。
/// MCP からの依頼で追加されたユーザースクリプト。
/// </summary>
public class MagicCircleSpin : MonoBehaviour
{
    [Tooltip("回転軸（ワールド空間）")]
    [SerializeField] private Vector3 axis = Vector3.up;

    [Tooltip("回転速度（度/秒）。負で逆回り")]
    [SerializeField] private float degreesPerSecond = 15f;

    private void Update()
    {
        transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.World);
    }
}

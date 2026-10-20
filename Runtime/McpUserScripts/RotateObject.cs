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

    [Tooltip("回転のスイッチ。オフの間は止まる")]
    [SerializeField] private bool rotating = true;

    /// <summary>回転軸（ローカル空間）。</summary>
    public Vector3 RotationAxis
    {
        get => rotationAxis;
        set => rotationAxis = value;
    }

    /// <summary>回転速度（度/秒）。</summary>
    public float DegreesPerSecond
    {
        get => degreesPerSecond;
        set => degreesPerSecond = value;
    }

    /// <summary>回転のスイッチ。</summary>
    public bool Rotating
    {
        get => rotating;
        set => rotating = value;
    }

    private void Update()
    {
        if (!rotating) return;
        transform.Rotate(rotationAxis, degreesPerSecond * Time.deltaTime, Space.Self);
    }
}

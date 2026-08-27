// Runtime/Poly_Ling_Main/Core/Context/WorkAxisContext.cs
// 作業用ローカル軸（Work Axis）
//
// 回転 / 曲げ など「任意の軸まわりの変形」の基準となる直交フレーム。
// 原点と回転（クォータニオン）で表し、X/Y/Z の3軸を派生させる。
//
// 【座標系】Origin は必ずワールド座標で保持する。
//   ギズモ描画 (ToolContext.WorldToScreenPos) と、複数メッシュ選択の重心計算
//   (RotateTool.UpdatePivot がいったんワールドで平均する方式) に合わせる。
//   メッシュのローカル座標へは変換しない。モデルを移動しても軸は追従しない。
//
// 【WorkPlaneContext とは別物】WorkPlaneContext は頂点追加の配置平面であり、
//   Mode = CameraParallel のときカメラ向きで軸が上書きされる。作業軸としては
//   使えないため、雛形だけを流用して別クラスとして定義する。
//
// Undo は現時点では未配線。要求時は WorkPlaneContext の WorkPlaneChangeRecord と
// MeshUndoController の _workPlaneStack 周辺をそのまま写せるよう、
// Snapshot / CreateSnapshot / ApplySnapshot を先に用意してある。

using System;
using UnityEngine;

namespace Poly_Ling.Context
{
    // ================================================================
    // スナップショット（Undo用。先に定義）
    // ================================================================

    /// <summary>作業軸の状態スナップショット。</summary>
    [Serializable]
    public struct WorkAxisSnapshot
    {
        public Vector3    Origin;
        public Quaternion Rotation;
        public bool       IsVisible;
        public float      Length;

        /// <summary>他のスナップショットと異なるか。</summary>
        public bool IsDifferentFrom(WorkAxisSnapshot other)
        {
            return Vector3.Distance(Origin, other.Origin) > 0.0001f ||
                   Quaternion.Angle(Rotation, other.Rotation) > 0.001f ||
                   Mathf.Abs(Length - other.Length) > 0.0001f ||
                   IsVisible != other.IsVisible;
        }

        /// <summary>変更内容の説明。</summary>
        public string GetChangeDescription(WorkAxisSnapshot before)
        {
            if (Vector3.Distance(Origin, before.Origin) > 0.0001f)
                return "Move WorkAxis";
            if (Quaternion.Angle(Rotation, before.Rotation) > 0.001f)
                return "Rotate WorkAxis";
            if (Mathf.Abs(Length - before.Length) > 0.0001f)
                return "Resize WorkAxis";
            if (IsVisible != before.IsVisible)
                return IsVisible ? "Show WorkAxis" : "Hide WorkAxis";
            return "Change WorkAxis";
        }
    }

    // ================================================================
    // 作業軸
    // ================================================================

    /// <summary>
    /// 作業用ローカル軸。原点（ワールド座標）と回転で直交フレームを定義する。
    /// </summary>
    [Serializable]
    public class WorkAxisContext
    {
        // === フィールド ===

        [SerializeField] private Vector3    _origin   = Vector3.zero;
        [SerializeField] private Quaternion _rotation = Quaternion.identity;
        [SerializeField] private bool       _isVisible = true;
        [SerializeField] private float      _length    = DefaultLength;

        /// <summary>軸長の既定値（ワールド単位）。</summary>
        public const float DefaultLength = 1f;

        /// <summary>軸長の下限。0 や負値で六角錐が退化するのを防ぐ。</summary>
        public const float MinLength = 1e-3f;

        // === プロパティ ===

        /// <summary>軸の原点（ワールド座標）。</summary>
        public Vector3 Origin
        {
            get => _origin;
            set => _origin = value;
        }

        /// <summary>軸の回転（ワールド基準）。</summary>
        public Quaternion Rotation
        {
            get => _rotation;
            set
            {
                // 縮退クォータニオンを弾く。normalized は零ベクトルで NaN を返すため。
                _rotation = (value.x * value.x + value.y * value.y +
                             value.z * value.z + value.w * value.w) > 1e-8f
                    ? value.normalized
                    : Quaternion.identity;
            }
        }

        /// <summary>ギズモを表示するか。</summary>
        public bool IsVisible
        {
            get => _isVisible;
            set => _isVisible = value;
        }

        /// <summary>
        /// 軸長（ワールド単位）。六角錐ギズモの長さの基準であり、
        /// Y 軸先端のワールド位置は Origin + AxisY * Length になる。
        /// 矢印ギズモは従来どおり画面固定長なのでこの値を使わない。
        /// </summary>
        public float Length
        {
            get => _length;
            set => _length = Mathf.Max(MinLength, value);
        }

        /// <summary>軸方向（ワールド）。</summary>
        public Vector3 AxisX => _rotation * Vector3.right;
        public Vector3 AxisY => _rotation * Vector3.up;
        public Vector3 AxisZ => _rotation * Vector3.forward;

        /// <summary>Y 軸先端のワールド座標（Origin + AxisY * Length）。</summary>
        public Vector3 YTip => _origin + AxisY * _length;

        /// <summary>オイラー角（度）。UI 入力用。</summary>
        public Vector3 EulerAngles
        {
            get => _rotation.eulerAngles;
            set => Rotation = Quaternion.Euler(value);
        }

        // === コンストラクタ ===

        public WorkAxisContext() { }

        public WorkAxisContext(WorkAxisContext other) { CopyFrom(other); }

        // === メソッド ===

        /// <summary>既定状態（原点・無回転）へ戻す。Undo 記録は呼び出し側で行う。</summary>
        public void Reset()
        {
            _origin    = Vector3.zero;
            _rotation  = Quaternion.identity;
            _isVisible = true;
            _length    = DefaultLength;
        }

        /// <summary>回転だけをワールド軸へ戻す。原点は維持する。</summary>
        public void AlignToWorld()
        {
            _rotation = Quaternion.identity;
        }

        /// <summary>
        /// Y 軸をワールド点 target へ向ける。Origin と Length は変えない
        /// （向きだけを合わせる。軸の長さは「長さ」欄で別に指定する）。
        ///
        /// 最小回転（Quaternion.FromToRotation）を左から掛けるため、Y まわりの
        /// ロール＝X/Z 軸の捻れは保たれる。target が Origin と重なるときは
        /// 向きが決まらないので何もせず false を返す。
        /// </summary>
        public bool AimYAt(Vector3 target)
        {
            Vector3 d   = target - _origin;
            float   len = d.magnitude;
            if (len < 1e-4f) return false;

            Rotation = Quaternion.FromToRotation(AxisY, d / len) * _rotation;
            return true;
        }

        public void CopyFrom(WorkAxisContext other)
        {
            if (other == null) return;
            _origin    = other._origin;
            _rotation  = other._rotation;
            _isVisible = other._isVisible;
            _length    = other._length;
        }

        public WorkAxisSnapshot CreateSnapshot()
        {
            return new WorkAxisSnapshot
            {
                Origin    = _origin,
                Rotation  = _rotation,
                IsVisible = _isVisible,
                Length    = _length
            };
        }

        public void ApplySnapshot(WorkAxisSnapshot snapshot)
        {
            _origin    = snapshot.Origin;
            _rotation  = snapshot.Rotation;
            _isVisible = snapshot.IsVisible;
            _length    = Mathf.Max(MinLength, snapshot.Length);
        }

        // === 座標変換 ===
        //
        // 曲げ / 回転の実装で使う。引数・戻り値ともワールド座標。

        /// <summary>ワールド座標を作業軸ローカル座標へ変換する。</summary>
        public Vector3 WorldToLocal(Vector3 worldPos)
            => Quaternion.Inverse(_rotation) * (worldPos - _origin);

        /// <summary>作業軸ローカル座標をワールド座標へ変換する。</summary>
        public Vector3 LocalToWorld(Vector3 localPos)
            => _origin + _rotation * localPos;

        /// <summary>ワールド方向ベクトルを作業軸ローカルへ変換する（平行移動なし）。</summary>
        public Vector3 WorldToLocalDirection(Vector3 worldDir)
            => Quaternion.Inverse(_rotation) * worldDir;

        /// <summary>作業軸ローカル方向ベクトルをワールドへ変換する（平行移動なし）。</summary>
        public Vector3 LocalToWorldDirection(Vector3 localDir)
            => _rotation * localDir;
    }
}

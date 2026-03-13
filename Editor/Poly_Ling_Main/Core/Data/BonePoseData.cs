// Assets/Editor/Poly_Ling/Data/BonePoseData.cs
// ボーンポーズデータ
// InputLayers（差分入力）→ 合成 → LocalMatrix
// ベースはVector3.zero / Quaternion.identity（BoneTransformが初期姿勢を保持）
//
// MeshContext上での位置:
//   MeshContext
//   ├── BoneTransform    エクスポート設定・初期姿勢（既存）
//   ├── BindPose         スキニング基準（既存）
//   ├── BonePoseData     作業中の差分ポーズ（本クラス）
//   ├── LocalMatrix      BoneTransform × BonePoseData.LocalMatrix
//   └── WorldMatrix      ComputeWorldMatricesで計算

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Serialization;

namespace Poly_Ling.Data
{
    // ================================================================
    // PoseLayer: 1つの入力ソースからの差分
    // ================================================================

    /// <summary>
    /// ポーズレイヤー（1入力ソース分の差分データ）
    /// VMD, IK, Physics, Manual など
    /// </summary>
    [Serializable]
    public class PoseLayer
    {
        /// <summary>レイヤー名（"VMD", "IK", "Physics", "Manual"等）</summary>
        public string Name;

        /// <summary>位置差分（PreBindPoseからの加算）</summary>
        public Vector3 DeltaPosition = Vector3.zero;

        /// <summary>回転差分（PreBindPoseへの乗算、Quaternion）</summary>
        public Quaternion DeltaRotation = Quaternion.identity;

        /// <summary>ブレンドウェイト（0.0〜1.0）</summary>
        public float Weight = 1f;

        /// <summary>有効フラグ</summary>
        public bool Enabled = true;

        /// <summary>ゼロ差分か（何も入力されていないか）</summary>
        public bool IsZero =>
            DeltaPosition == Vector3.zero &&
            DeltaRotation == Quaternion.identity;

        /// <summary>クリア</summary>
        public void Clear()
        {
            DeltaPosition = Vector3.zero;
            DeltaRotation = Quaternion.identity;
            Weight = 1f;
        }

        /// <summary>コピー</summary>
        public PoseLayer Clone()
        {
            return new PoseLayer
            {
                Name = Name,
                DeltaPosition = DeltaPosition,
                DeltaRotation = DeltaRotation,
                Weight = Weight,
                Enabled = Enabled
            };
        }
    }

    // ================================================================
    // BonePoseData: ボーンポーズ管理本体
    // ================================================================

    /// <summary>
    /// ボーンのポーズデータ
    /// 
    /// 構造:
    ///   InputLayers (差分入力、複数ソース、ベース=zero/identity)
    ///   → 合成結果 (Position/Rotation/LocalMatrix)
    /// 初期姿勢はBoneTransformが保持する。
    /// </summary>
    [Serializable]
    public class BonePoseData
    {
        // ================================================================
        // InputLayers（差分入力）
        // ================================================================

        /// <summary>
        /// 入力レイヤーリスト（順序 = 合成順序）
        /// 典型: [0]VMD → [1]IK → [2]Physics → [3]Manual
        /// </summary>
        private List<PoseLayer> _layers = new List<PoseLayer>();

        // ================================================================
        // 合成結果（キャッシュ）
        // ================================================================

        private Vector3 _blendedPosition;
        private Quaternion _blendedRotation;
        private Matrix4x4 _localMatrix = Matrix4x4.identity;
        private bool _dirty = true;

        // ================================================================
        // 有効状態
        // ================================================================

        /// <summary>
        /// BonePoseDataが有効か
        /// falseの場合、MeshContext.LocalMatrixはBoneTransformにフォールバック
        /// </summary>
        public bool IsActive { get; set; } = true;

        // ================================================================
        // 合成結果プロパティ
        // ================================================================

        /// <summary>合成後の最終位置</summary>
        public Vector3 Position
        {
            get { EnsureRecalculated(); return _blendedPosition; }
        }

        /// <summary>合成後の最終回転</summary>
        public Quaternion Rotation
        {
            get { EnsureRecalculated(); return _blendedRotation; }
        }

        /// <summary>合成後のローカル変換行列</summary>
        public Matrix4x4 LocalMatrix
        {
            get { EnsureRecalculated(); return _localMatrix; }
        }

        /// <summary>レイヤー数</summary>
        public int LayerCount => _layers.Count;

        /// <summary>レイヤー一覧（読み取り専用）</summary>
        public IReadOnlyList<PoseLayer> Layers => _layers;

        /// <summary>何かレイヤー入力があるか</summary>
        public bool HasInput
        {
            get
            {
                for (int i = 0; i < _layers.Count; i++)
                {
                    if (_layers[i].Enabled && !_layers[i].IsZero)
                        return true;
                }
                return false;
            }
        }

        // ================================================================
        // コンストラクタ
        // ================================================================

        public BonePoseData() { }

        // ================================================================
        // レイヤー操作
        // ================================================================

        /// <summary>
        /// レイヤーを取得（なければ作成）
        /// </summary>
        public PoseLayer GetOrCreateLayer(string name)
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Name == name)
                    return _layers[i];
            }

            var layer = new PoseLayer { Name = name };
            _layers.Add(layer);
            return layer;
        }

        /// <summary>
        /// レイヤーを取得（なければnull）
        /// </summary>
        public PoseLayer GetLayer(string name)
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Name == name)
                    return _layers[i];
            }
            return null;
        }

        /// <summary>
        /// レイヤーに差分を設定
        /// </summary>
        public void SetLayer(string name, Vector3 deltaPosition, Quaternion deltaRotation, float weight = 1f)
        {
            var layer = GetOrCreateLayer(name);
            layer.DeltaPosition = deltaPosition;
            layer.DeltaRotation = deltaRotation;
            layer.Weight = weight;
            layer.Enabled = true;
            _dirty = true;
        }

        /// <summary>
        /// レイヤーの回転のみ設定
        /// </summary>
        public void SetLayerRotation(string name, Quaternion deltaRotation, float weight = 1f)
        {
            var layer = GetOrCreateLayer(name);
            layer.DeltaRotation = deltaRotation;
            layer.Weight = weight;
            layer.Enabled = true;
            _dirty = true;
        }

        /// <summary>
        /// レイヤーの位置のみ設定
        /// </summary>
        public void SetLayerPosition(string name, Vector3 deltaPosition, float weight = 1f)
        {
            var layer = GetOrCreateLayer(name);
            layer.DeltaPosition = deltaPosition;
            layer.Weight = weight;
            layer.Enabled = true;
            _dirty = true;
        }

        /// <summary>
        /// レイヤーをクリア（ゼロに戻す、レイヤー自体は残る）
        /// </summary>
        public void ClearLayer(string name)
        {
            var layer = GetLayer(name);
            if (layer != null)
            {
                layer.Clear();
                _dirty = true;
            }
        }

        /// <summary>
        /// レイヤーを削除
        /// </summary>
        public bool RemoveLayer(string name)
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Name == name)
                {
                    _layers.RemoveAt(i);
                    _dirty = true;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 全レイヤーをクリア（ゼロポーズに戻る）
        /// </summary>
        public void ClearAllLayers()
        {
            _layers.Clear();
            _dirty = true;
        }

        /// <summary>
        /// レイヤーの有効/無効を切り替え
        /// </summary>
        public void SetLayerEnabled(string name, bool enabled)
        {
            var layer = GetLayer(name);
            if (layer != null)
            {
                layer.Enabled = enabled;
                _dirty = true;
            }
        }

        // ================================================================
        // 合成計算
        // ================================================================

        /// <summary>
        /// 強制再計算
        /// </summary>
        public void Recalculate()
        {
            _blendedPosition = Vector3.zero;
            _blendedRotation = Quaternion.identity;

            for (int i = 0; i < _layers.Count; i++)
            {
                var layer = _layers[i];
                if (!layer.Enabled || layer.Weight <= 0f)
                    continue;

                float w = Mathf.Clamp01(layer.Weight);

                // 位置: 加算
                _blendedPosition += layer.DeltaPosition * w;

                // 回転: 差分をWeightでSlerpしてから乗算
                Quaternion weightedDelta = Quaternion.Slerp(
                    Quaternion.identity, layer.DeltaRotation, w);
                _blendedRotation = weightedDelta * _blendedRotation;
            }

            _localMatrix = Matrix4x4.TRS(_blendedPosition, _blendedRotation, Vector3.one);
            _dirty = false;
        }

        private void EnsureRecalculated()
        {
            if (_dirty) Recalculate();
        }

        /// <summary>
        /// ダーティフラグを立てる（外部からレイヤーを直接変更した場合）
        /// </summary>
        public void SetDirty()
        {
            _dirty = true;
        }

        // ================================================================
        // Snapshot（Undo用）
        // ================================================================

        /// <summary>
        /// スナップショットを作成
        /// </summary>
        public BonePoseDataSnapshot CreateSnapshot()
        {
            var snapshot = new BonePoseDataSnapshot
            {
                IsActive = IsActive,
                Layers = new List<PoseLayer>(_layers.Count)
            };

            for (int i = 0; i < _layers.Count; i++)
                snapshot.Layers.Add(_layers[i].Clone());

            return snapshot;
        }

        /// <summary>
        /// スナップショットから復元
        /// </summary>
        public void ApplySnapshot(BonePoseDataSnapshot? snapshot)
        {
            if (!snapshot.HasValue) return;

            var s = snapshot.Value;
            IsActive = s.IsActive;

            _layers.Clear();
            if (s.Layers != null)
            {
                for (int i = 0; i < s.Layers.Count; i++)
                    _layers.Add(s.Layers[i].Clone());
            }

            _dirty = true;
        }

        // ================================================================
        // DTO変換（シリアライズ）
        // ================================================================

        /// <summary>
        /// DTOに変換（保存用）
        /// PreBindPose + Manualレイヤーのみ保存
        /// VMD/IK/Physicsはトランジェント
        /// </summary>
        public BonePoseDataDTO ToDTO()
        {
            var dto = new BonePoseDataDTO();
            dto.isActive = IsActive;

            // Manualレイヤーのみ保存
            var manual = GetLayer("Manual");
            if (manual != null && !manual.IsZero)
            {
                dto.manualDeltaPosition = new float[]
                    { manual.DeltaPosition.x, manual.DeltaPosition.y, manual.DeltaPosition.z };
                dto.manualDeltaRotation = new float[]
                    { manual.DeltaRotation.x, manual.DeltaRotation.y,
                      manual.DeltaRotation.z, manual.DeltaRotation.w };
                dto.manualWeight = manual.Weight;
            }

            return dto;
        }

        /// <summary>
        /// DTOから復元
        /// </summary>
        public static BonePoseData FromDTO(BonePoseDataDTO dto)
        {
            if (dto == null) return null;

            var data = new BonePoseData
            {
                IsActive = dto.isActive
            };

            // Manualレイヤー復元
            if (dto.manualDeltaPosition != null)
            {
                var layer = data.GetOrCreateLayer("Manual");
                layer.DeltaPosition = new Vector3(
                    dto.manualDeltaPosition[0],
                    dto.manualDeltaPosition[1],
                    dto.manualDeltaPosition[2]);
                if (dto.manualDeltaRotation != null)
                {
                    layer.DeltaRotation = new Quaternion(
                        dto.manualDeltaRotation[0],
                        dto.manualDeltaRotation[1],
                        dto.manualDeltaRotation[2],
                        dto.manualDeltaRotation[3]);
                }
                layer.Weight = dto.manualWeight;
                layer.Enabled = true;
            }

            data._dirty = true;
            return data;
        }

        // ================================================================
        // Clone
        // ================================================================

        public BonePoseData Clone()
        {
            var clone = new BonePoseData
            {
                IsActive = IsActive
            };

            for (int i = 0; i < _layers.Count; i++)
                clone._layers.Add(_layers[i].Clone());

            clone._dirty = true;
            return clone;
        }

        // ================================================================
        // デバッグ
        // ================================================================

        public override string ToString()
        {
            EnsureRecalculated();
            return $"BonePoseData(Layers:{_layers.Count}, Final:P={_blendedPosition} R={_blendedRotation.eulerAngles})";
        }
    }

    // ================================================================
    // Snapshot（Undo用）
    // ================================================================

    /// <summary>
    /// BonePoseDataの状態スナップショット
    /// IsActive + 全レイヤーの完全コピー
    /// </summary>
    [Serializable]
    public struct BonePoseDataSnapshot
    {
        public bool IsActive;
        public List<PoseLayer> Layers;

        public bool IsDifferentFrom(BonePoseDataSnapshot other)
        {
            if (IsActive != other.IsActive) return true;

            int countA = Layers?.Count ?? 0;
            int countB = other.Layers?.Count ?? 0;
            if (countA != countB) return true;

            for (int i = 0; i < countA; i++)
            {
                var a = Layers[i];
                var b = other.Layers[i];
                if (a.Name != b.Name) return true;
                if (a.Enabled != b.Enabled) return true;
                if (Mathf.Abs(a.Weight - b.Weight) > 0.0001f) return true;
                if (Vector3.Distance(a.DeltaPosition, b.DeltaPosition) > 0.0001f) return true;
                if (Quaternion.Angle(a.DeltaRotation, b.DeltaRotation) > 0.01f) return true;
            }

            return false;
        }
    }
}
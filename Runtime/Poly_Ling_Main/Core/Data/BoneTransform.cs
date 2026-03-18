// Assets/Editor/MeshCreators/BoneTransform.cs
// エクスポート時のローカルトランスフォーム設定
// BoneTransformUI は BoneTransformUI.cs に分離済み

using Poly_Ling.Serialization;
using Poly_Ling.Localization;
using Poly_Ling.EditorBridge;
using System;
using UnityEngine;

namespace Poly_Ling.Data
{
    // ================================================================
    // スナップショット（Undo用）
    // ================================================================

    [Serializable]
    public struct BoneTransformSnapshot
    {
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Scale;
        public bool UseLocalTransform;
        public bool HasBoneTransform;

        public bool IsDifferentFrom(BoneTransformSnapshot other)
        {
            return Vector3.Distance(Position, other.Position) > 0.0001f ||
                   Vector3.Distance(Rotation, other.Rotation) > 0.0001f ||
                   Vector3.Distance(Scale, other.Scale) > 0.0001f ||
                   UseLocalTransform != other.UseLocalTransform ||
                   HasBoneTransform != other.HasBoneTransform;
        }

        public string GetChangeDescription(BoneTransformSnapshot before)
        {
            if (UseLocalTransform != before.UseLocalTransform)
                return UseLocalTransform ? "Enable Local Transform" : "Disable Local Transform";
            if (HasBoneTransform != before.HasBoneTransform)
                return HasBoneTransform ? "Enable HasBoneTransform" : "Disable HasBoneTransform";
            if (Vector3.Distance(Position, before.Position) > 0.0001f)
                return "Change Export Position";
            if (Vector3.Distance(Rotation, before.Rotation) > 0.0001f)
                return "Change Export Rotation";
            if (Vector3.Distance(Scale, before.Scale) > 0.0001f)
                return "Change Export Scale";
            return "Change Export Settings";
        }
    }

    // ================================================================
    // エクスポート設定
    // ================================================================

    [Serializable]
    public class BoneTransform
    {
        [SerializeField] private Vector3 _position = Vector3.zero;
        [SerializeField] private Vector3 _rotation = Vector3.zero;
        [SerializeField] private Vector3 _scale = Vector3.one;
        [SerializeField] private bool _useLocalTransform = false;
        [SerializeField] private bool _hasBoneTransform = false;

        private bool _isExpanded = true;
        private int _selectedRotationAxis = 0;

        public Vector3 Position  { get => _position; set => _position = value; }
        public Vector3 Rotation  { get => _rotation; set => _rotation = value; }
        public Vector3 Scale     { get => _scale;    set => _scale    = value; }

        public bool UseLocalTransform
        {
            get => _useLocalTransform;
            set => _useLocalTransform = value;
        }

        /// <remarks>
        /// TODO: 将来的に廃止予定。IMeshView.HasBoneWeight に統合する。
        /// </remarks>
        public bool HasBoneTransform
        {
            get => _hasBoneTransform;
            set => _hasBoneTransform = value;
        }

        public Quaternion RotationQuaternion => Quaternion.Euler(_rotation);
        public Matrix4x4  TransformMatrix    => Matrix4x4.TRS(_position, RotationQuaternion, _scale);

        public bool IsExpanded
        {
            get => _isExpanded;
            set => _isExpanded = value;
        }

        public int SelectedRotationAxis
        {
            get => _selectedRotationAxis;
            set => _selectedRotationAxis = Mathf.Clamp(value, 0, 2);
        }

        public BoneTransform()             { ResetInternal(); }
        public BoneTransform(BoneTransform other) { CopyFrom(other); }

        private void ResetInternal()
        {
            _position         = Vector3.zero;
            _rotation         = Vector3.zero;
            _scale            = Vector3.one;
            _useLocalTransform = false;
            _hasBoneTransform  = false;
        }

        public void Reset() { ResetInternal(); }

        public void CopyFrom(BoneTransform other)
        {
            if (other == null) return;
            _position          = other._position;
            _rotation          = other._rotation;
            _scale             = other._scale;
            _useLocalTransform = other._useLocalTransform;
            _hasBoneTransform   = other._hasBoneTransform;
        }

        public BoneTransformSnapshot CreateSnapshot()
        {
            return new BoneTransformSnapshot
            {
                Position          = _position,
                Rotation          = _rotation,
                Scale             = _scale,
                UseLocalTransform = _useLocalTransform,
                HasBoneTransform   = _hasBoneTransform
            };
        }

        public void ApplySnapshot(BoneTransformSnapshot snapshot)
        {
            _position          = snapshot.Position;
            _rotation          = snapshot.Rotation;
            _scale             = snapshot.Scale;
            _useLocalTransform = snapshot.UseLocalTransform;
            _hasBoneTransform   = snapshot.HasBoneTransform;
        }

        public void ApplyToGameObject(GameObject go, bool asLocal = true)
        {
            if (go == null) return;

            if (!_useLocalTransform)
            {
                if (asLocal)
                {
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale    = Vector3.one;
                }
                else
                {
                    go.transform.position   = Vector3.zero;
                    go.transform.rotation   = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                }
                return;
            }

            if (asLocal)
            {
                go.transform.localPosition = _position;
                go.transform.localRotation = RotationQuaternion;
                go.transform.localScale    = _scale;
            }
            else
            {
                go.transform.position   = _position;
                go.transform.rotation   = RotationQuaternion;
                go.transform.localScale = _scale;
            }
        }

        public static BoneTransform FromSerializable(BoneTransformDTO data)
        {
            if (data == null) return new BoneTransform();
            var s = new BoneTransform();
            s._useLocalTransform = data.useLocalTransform;
            s._hasBoneTransform   = data.exportAsSkinned;
            s._position          = data.GetPosition();
            s._rotation          = data.GetRotation();
            s._scale             = data.GetScale();
            return s;
        }

        public BoneTransformDTO ToSerializable()
        {
            var data = new BoneTransformDTO
            {
                useLocalTransform = _useLocalTransform,
                exportAsSkinned   = _hasBoneTransform
            };
            data.SetPosition(_position);
            data.SetRotation(_rotation);
            data.SetScale(_scale);
            return data;
        }

        /// <summary>現在の選択からトランスフォームを取得</summary>
        public void CopyFromSelection()
        {
            var t = PLEditorBridge.I.GetActiveTransform();
            if (t != null)
            {
                _position = t.localPosition;
                _rotation = t.localEulerAngles;
                _scale    = t.localScale;
            }
        }

        public override string ToString()
            => $"BoneTransform(Use:{_useLocalTransform}, P:{_position}, R:{_rotation}, S:{_scale})";
    }
}

// ================================================================
// Undoシステム統合
// ================================================================

namespace Poly_Ling.UndoSystem
{
    using Poly_Ling.Tools;
using Poly_Ling.Data;
using Poly_Ling.View;

    public class BoneTransformChangeRecord : IUndoRecord<BoneTransform>
    {
        public UndoOperationInfo Info { get; set; }

        public BoneTransformSnapshot Before;
        public BoneTransformSnapshot After;
        public string Description;

        public BoneTransformChangeRecord(
            BoneTransformSnapshot before,
            BoneTransformSnapshot after,
            string description = null)
        {
            Before      = before;
            After       = after;
            Description = description ?? after.GetChangeDescription(before);
        }

        public void Undo(BoneTransform context) { context?.ApplySnapshot(Before); }
        public void Redo(BoneTransform context) { context?.ApplySnapshot(After);  }
    }
}

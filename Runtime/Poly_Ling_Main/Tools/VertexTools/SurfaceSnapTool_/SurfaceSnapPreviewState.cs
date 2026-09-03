// SurfaceSnapPreviewState.cs
// 「面に張り付け」のプレビュー状態。複数メッシュ対応。
// UnityEditor非依存。
// Runtime/Poly_Ling_Main/Tools/VertexTools/SurfaceSnapTool_/ に配置
//
// 【ShrinkPreviewState との違い】
//   ・対象が複数メッシュ（ShrinkPreviewState.cs:20 はビフォー1枚）。
//   ・停止パラメータを持たない。行き先はメッシュごとの Goal 配列に持つ。
//   ・可視状態を触らない。張り付け後もリファレンスは見えていたほうが確認しやすい。
//
// 【カメラを動かしても結果が変わらない理由】
//   行き先 Goal は「計算」時にローカル座標として確定させる。
//   スライダーは Backup と Goal の補間しかしないので、
//   プレビュー中にカメラ姿勢が変わっても再計算は起きない。
//
// 【補間をローカルで行ってよい理由】
//   ワールド変換は頂点ごとにアフィンなので、
//   ローカルでの線形補間はワールドでの線形補間と一致する。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.Tools
{
    /// <summary>「面に張り付け」のプレビュー状態。</summary>
    public class SurfaceSnapPreviewState
    {
        /// <summary>1 メッシュぶんのプレビュー対象。</summary>
        public class Target
        {
            /// <summary>MeshContextList 索引。</summary>
            public int MeshIndex;

            /// <summary>対象メッシュ。</summary>
            public MeshContext Context;

            /// <summary>計算前のローカル座標。</summary>
            public Vector3[] Backup;

            /// <summary>張り付け後のローカル座標。ヒットしなかった頂点は Backup と同値。</summary>
            public Vector3[] Goal;

            /// <summary>行き先が変わった頂点数。</summary>
            public int MovedCount;
        }

        private readonly List<Target> _targets = new List<Target>();
        private bool _isActive;

        public bool IsActive   => _isActive;
        public int  TargetCount => _targets.Count;

        /// <summary>張り付け対象になった頂点の総数。</summary>
        public int MovedVertexCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _targets.Count; i++) n += _targets[i].MovedCount;
                return n;
            }
        }

        // ================================================================
        // 開始
        // ================================================================

        /// <summary>
        /// プレビューを開始する。targets の中身はこの後書き換えないこと。
        /// </summary>
        public bool Start(List<Target> targets)
        {
            if (_isActive) return false;
            if (targets == null || targets.Count == 0) return false;

            _targets.Clear();
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t?.Context?.MeshObject == null) continue;
                if (t.Backup == null || t.Goal == null) continue;
                _targets.Add(t);
            }

            if (_targets.Count == 0) return false;

            _isActive = true;
            return true;
        }

        // ================================================================
        // 適用
        // ================================================================

        /// <summary>スライダー値 [0,1] を反映する。</summary>
        public void Apply(ModelContext model, float slider, ToolContext toolCtx)
        {
            if (!_isActive) return;

            float s = Mathf.Clamp01(slider);
            Write(model, toolCtx, s);
        }

        /// <summary>計算前の座標へ戻す。プレビューは続く。</summary>
        public void Restore(ModelContext model, ToolContext toolCtx)
        {
            if (!_isActive) return;
            Write(model, toolCtx, 0f);
        }

        private void Write(ModelContext model, ToolContext toolCtx, float s)
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                var t  = _targets[i];
                var mo = t.Context?.MeshObject;
                if (mo == null) continue;

                int n = Mathf.Min(t.Backup.Length, mo.VertexCount);
                if (t.Goal.Length < n) n = t.Goal.Length;

                if (s <= 0f)
                {
                    for (int v = 0; v < n; v++)
                        mo.Vertices[v].Position = t.Backup[v];
                }
                else if (s >= 1f)
                {
                    for (int v = 0; v < n; v++)
                        mo.Vertices[v].Position = t.Goal[v];
                }
                else
                {
                    for (int v = 0; v < n; v++)
                        mo.Vertices[v].Position = Vector3.Lerp(t.Backup[v], t.Goal[v], s);
                }

                mo.InvalidatePositionCache();
                toolCtx?.SyncMeshContextPositionsOnly?.Invoke(t.Context);
                if (model != null)
                    Poly_Ling.UI.BlendOperation.SyncMirrorSide(model, t.Context, toolCtx);
            }

            toolCtx?.Repaint?.Invoke();
        }

        // ================================================================
        // 終了
        // ================================================================

        /// <summary>座標を戻してプレビューを破棄する。</summary>
        public void End(ModelContext model, ToolContext toolCtx)
        {
            if (!_isActive) return;

            Write(model, toolCtx, 0f);

            _targets.Clear();
            _isActive = false;
        }

        /// <summary>座標を戻さずにプレビューを畳む。確定後に使う。</summary>
        public void Commit()
        {
            _targets.Clear();
            _isActive = false;
        }
    }
}

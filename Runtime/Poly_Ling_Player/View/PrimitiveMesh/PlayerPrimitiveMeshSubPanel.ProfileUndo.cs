// PlayerPrimitiveMeshSubPanel.ProfileUndo.cs
// 図形生成サブパネル：回転体プロファイル・2D押し出しループの編集 Undo。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Revolution;
using Poly_Ling.Profile2DExtrude;
using Poly_Ling.NohMask;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Core;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 生成
        // ================================================================

        // ================================================================
        // プロファイル編集 Undo（回転体 / 2D押し出し）
        // ================================================================
        // MeshUndoController のサブウィンドウスタックにプロファイル全体の
        // スナップショットを積む。既存の Undo/Redo ボタン（_editOps.PerformUndo）
        // でそのまま巻き戻る。記録対象は点の移動/挿入/削除・リセット・変換・
        // プリセット選択・メッシュ取込・CSV読込。選択・アンカー・ビュー・
        // 連続パラメータスライダー（ドーナツ半径等の形状パラメータ）は対象外。

        private const string RevUndoStackId = "PlayerEdit/RevProfileEdit";
        private const string P2dUndoStackId = "PlayerEdit/P2dLoopsEdit";

        private sealed class RevProfileUndoContext { public List<Vector2> Profile; }
        private sealed class P2dLoopsUndoContext   { public List<Loop>     Loops;   }

        private sealed class RevProfileUndoRecord : IUndoRecord<RevProfileUndoContext>
        {
            public UndoOperationInfo Info { get; set; }
            public List<Vector2> Before;
            public List<Vector2> After;
            public void Undo(RevProfileUndoContext ctx) => ctx.Profile = CloneRevProfile(Before);
            public void Redo(RevProfileUndoContext ctx) => ctx.Profile = CloneRevProfile(After);
        }

        private sealed class P2dLoopsUndoRecord : IUndoRecord<P2dLoopsUndoContext>
        {
            public UndoOperationInfo Info { get; set; }
            public List<Loop> Before;
            public List<Loop> After;
            public void Undo(P2dLoopsUndoContext ctx) => ctx.Loops = CloneP2dLoops(Before);
            public void Redo(P2dLoopsUndoContext ctx) => ctx.Loops = CloneP2dLoops(After);
        }

        private UndoStack<RevProfileUndoContext> _revUndoStack;
        private RevProfileUndoContext            _revUndoCtx;
        private UndoStack<P2dLoopsUndoContext>   _p2dUndoStack;
        private P2dLoopsUndoContext              _p2dUndoCtx;

        private List<Vector2> _revEditBefore;   // 回転体：編集前スナップショット
        private List<Loop>    _p2dEditBefore;   // 2D押し出し：編集前スナップショット
        private bool _revUndoApplying;          // undo/redo 適用中は記録抑止
        private bool _p2dUndoApplying;

        private static List<Vector2> CloneRevProfile(List<Vector2> src)
            => src == null ? null : new List<Vector2>(src);

        private static List<Loop> CloneP2dLoops(List<Loop> src)
        {
            if (src == null) return null;
            var r = new List<Loop>(src.Count);
            for (int i = 0; i < src.Count; i++) r.Add(new Loop(src[i]));
            return r;
        }

        private static bool RevProfileEquals(List<Vector2> a, List<Vector2> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if ((a[i] - b[i]).sqrMagnitude > 1e-12f) return false;
            return true;
        }

        private static bool P2dLoopsEquals(List<Loop> a, List<Loop> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                Loop la = a[i], lb = b[i];
                if (la == null || lb == null) return false;
                if (la.IsHole != lb.IsHole) return false;
                if (la.Points.Count != lb.Points.Count) return false;
                for (int j = 0; j < la.Points.Count; j++)
                    if ((la.Points[j] - lb.Points[j]).sqrMagnitude > 1e-12f) return false;
            }
            return true;
        }

        private void EnsureRevUndoStack()
        {
            if (_revUndoStack != null) return;
            var undo = GetUndoController?.Invoke();
            if (undo == null) return;
            undo.RemoveSubWindowStack(RevUndoStackId);   // パネル再生成時の重複ID回避
            _revUndoCtx   = new RevProfileUndoContext { Profile = CloneRevProfile(_revProfile) };
            _revUndoStack = undo.CreateSubWindowStack(RevUndoStackId, "回転体プロファイル編集", _revUndoCtx);
            _revUndoStack.OnUndoPerformed += _ => ApplyRevUndoContext();
            _revUndoStack.OnRedoPerformed += _ => ApplyRevUndoContext();
        }

        private void EnsureP2dUndoStack()
        {
            if (_p2dUndoStack != null) return;
            var undo = GetUndoController?.Invoke();
            if (undo == null) return;
            undo.RemoveSubWindowStack(P2dUndoStackId);   // パネル再生成時の重複ID回避
            _p2dUndoCtx   = new P2dLoopsUndoContext { Loops = CloneP2dLoops(_p2dLoops) };
            _p2dUndoStack = undo.CreateSubWindowStack(P2dUndoStackId, "2D押し出しプロファイル編集", _p2dUndoCtx);
            _p2dUndoStack.OnUndoPerformed += _ => ApplyP2dUndoContext();
            _p2dUndoStack.OnRedoPerformed += _ => ApplyP2dUndoContext();
        }

        /// <summary>回転体：編集前スナップショットを取得（記録の起点）。</summary>
        private void RevBegin()
        {
            if (_revUndoApplying) { _revEditBefore = null; return; }
            _revEditBefore = GetUndoController?.Invoke() == null ? null : CloneRevProfile(_revProfile);
        }

        /// <summary>回転体：変化があればサブウィンドウスタックへ記録。</summary>
        private void RevCommit(string desc)
        {
            var before = _revEditBefore;
            _revEditBefore = null;
            if (_revUndoApplying || before == null) return;
            var undo = GetUndoController?.Invoke();
            if (undo == null) return;
            var after = CloneRevProfile(_revProfile);
            if (RevProfileEquals(before, after)) return;
            EnsureRevUndoStack();
            if (_revUndoStack == null) return;
            _revUndoCtx.Profile = CloneRevProfile(after);
            _revUndoStack.Record(new RevProfileUndoRecord { Before = before, After = after }, desc);
            undo.FocusSubWindow(RevUndoStackId);
        }

        /// <summary>回転体：Undo/Redo で復元されたスナップショットをパネルへ反映。</summary>
        private void ApplyRevUndoContext()
        {
            _revUndoApplying = true;
            try
            {
                _revProfile = CloneRevProfile(_revUndoCtx?.Profile);
                _revSel.Clear();
                _revSelIdx = -1;
                _revP.CurrentPreset = ProfilePreset.Custom;
                D();
                RefreshRevCanvas();
                RefreshRevPointUI();
            }
            finally { _revUndoApplying = false; }
        }

        /// <summary>2D押し出し：編集前スナップショットを取得（記録の起点）。</summary>
        private void P2dBegin()
        {
            if (_p2dUndoApplying) { _p2dEditBefore = null; return; }
            _p2dEditBefore = GetUndoController?.Invoke() == null ? null : CloneP2dLoops(_p2dLoops);
        }

        /// <summary>2D押し出し：変化があればサブウィンドウスタックへ記録。</summary>
        private void P2dCommit(string desc)
        {
            var before = _p2dEditBefore;
            _p2dEditBefore = null;
            if (_p2dUndoApplying || before == null) return;
            var undo = GetUndoController?.Invoke();
            if (undo == null) return;
            var after = CloneP2dLoops(_p2dLoops);
            if (P2dLoopsEquals(before, after)) return;
            EnsureP2dUndoStack();
            if (_p2dUndoStack == null) return;
            _p2dUndoCtx.Loops = CloneP2dLoops(after);
            _p2dUndoStack.Record(new P2dLoopsUndoRecord { Before = before, After = after }, desc);
            undo.FocusSubWindow(P2dUndoStackId);
        }

        /// <summary>2D押し出し：Undo/Redo で復元されたスナップショットをパネルへ反映。</summary>
        private void ApplyP2dUndoContext()
        {
            _p2dUndoApplying = true;
            try
            {
                _p2dLoops = CloneP2dLoops(_p2dUndoCtx?.Loops);
                _p2dSel.Clear();
                _p2dSelPt = -1;
                _p2dSelLoop = (_p2dLoops == null || _p2dLoops.Count == 0)
                    ? 0 : Mathf.Clamp(_p2dSelLoop, 0, _p2dLoops.Count - 1);
                D();
                RefreshP2dCanvas();
                RefreshP2dPointUI();
            }
            finally { _p2dUndoApplying = false; }
        }
    }
}

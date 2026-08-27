// PLMeshValidator.cs
// 描画提出するメッシュの中身を検証する診断コード。恒久コードではない。
//
// 【なぜ必要か】
//   Graphics.DrawMesh / Graphics.RenderMesh はインデックス値を検証しない。
//   頂点数を超えるインデックスが 1 本でもあると GPU が確保外を読む。
//   その結果は描画実行時に現れ、C# 側では例外も警告も出ない。
//   同様に bounds に NaN が入るとカリング処理が破綻する。
//
// 【検査のコスト】
//   Mesh の InstanceID をキーに 1 回だけ検査する。以降は HashSet の
//   参照 1 回で抜けるため、毎フレームの負荷にはならない。
//   ただしメッシュを再構築しても InstanceID は変わらないため、
//   再構築後の内容は捕捉できない（次段の課題）。
//
// 【出力】
//   異常が見つかった場合のみ PLCamDbg へ 1 行出す。正常時は無出力。

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Core
{
    public static class PLMeshValidator
    {
        private static readonly HashSet<int> _checked = new HashSet<int>();
        private static int _reportLeft = 200;   // 報告の上限

        /// <summary>
        /// 提出直前のメッシュとマテリアルを検証する。異常時のみ記録する。
        /// </summary>
        public static void Check(Mesh mesh, Material mat, string tag)
        {
            if (_reportLeft <= 0) return;
            if (mesh == null) return;

            int id = mesh.GetInstanceID();
            if (_checked.Contains(id)) return;
            _checked.Add(id);

            int vtx = mesh.vertexCount;
            var b   = mesh.bounds;

            bool badBounds =
                float.IsNaN(b.center.x)  || float.IsNaN(b.center.y)  || float.IsNaN(b.center.z)  ||
                float.IsNaN(b.extents.x) || float.IsNaN(b.extents.y) || float.IsNaN(b.extents.z) ||
                float.IsInfinity(b.center.x)  || float.IsInfinity(b.center.y)  || float.IsInfinity(b.center.z) ||
                float.IsInfinity(b.extents.x) || float.IsInfinity(b.extents.y) || float.IsInfinity(b.extents.z);

            bool hugeBounds =
                Mathf.Abs(b.extents.x) > 1e6f || Mathf.Abs(b.extents.y) > 1e6f || Mathf.Abs(b.extents.z) > 1e6f;

            bool noShader = (mat != null && mat.shader == null);

            int subCount = mesh.subMeshCount;
            int worstSub = -1, worstMax = -1, worstMin = -1;
            bool badIndex = false;
            long idxTotal = 0;

            for (int s = 0; s < subCount; s++)
            {
                int[] idx;
                try { idx = mesh.GetIndices(s); }
                catch (System.Exception) { badIndex = true; worstSub = s; break; }

                if (idx == null) continue;
                idxTotal += idx.Length;

                int mn = int.MaxValue, mx = -1;
                for (int i = 0; i < idx.Length; i++)
                {
                    int v = idx[i];
                    if (v < mn) mn = v;
                    if (v > mx) mx = v;
                }
                if (idx.Length == 0) { mn = 0; mx = -1; }

                if (mx >= vtx || mn < 0)
                {
                    badIndex = true;
                    worstSub = s; worstMax = mx; worstMin = mn;
                    break;
                }
            }

            if (!badIndex && !badBounds && !hugeBounds && !noShader) return;

            _reportLeft--;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark(
                "BAD " + tag
                + " id=" + id
                + " name=\"" + mesh.name + "\""
                + " vtx=" + vtx
                + " sub=" + subCount
                + " idxTotal=" + idxTotal
                + " badIndex=" + badIndex
                + " worstSub=" + worstSub
                + " idxMin=" + worstMin
                + " idxMax=" + worstMax
                + " badBounds=" + badBounds
                + " hugeBounds=" + hugeBounds
                + " noShader=" + noShader
                + " bC=" + b.center.ToString("F3")
                + " bE=" + b.extents.ToString("F3"));
        }

        /// <summary>検査済み集合を捨てる。メッシュ再構築後に再検査したい場合に呼ぶ。</summary>
        public static void Reset()
        {
            _checked.Clear();
        }
    }
}

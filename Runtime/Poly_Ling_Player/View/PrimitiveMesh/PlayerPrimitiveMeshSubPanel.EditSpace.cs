// PlayerPrimitiveMeshSubPanel.EditSpace.cs
// 図形生成サブパネル：作業空間（断面系アダプタ）との受け渡し。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置
//
// 【何をするか】（PolyLing_UV_Billboard_Design.md 7 章）
//   断面（回転体・2D押し出し・フリル A/B・パイプ）の元データはこのパネルが持つ。
//   作業空間の代理は「反映（→メッシュ）」で作った常設の線分オブジェクト。
//   作業空間の「反映」は、今の「取り込み（メッシュ→断面）」と同じ読み方で代理から断面を作り、
//   パネルの断面へ入れる。「取消し」は、パネルの断面から線分を作り直して代理を置き換える。
//
// 【読み方は 1 か所】
//   ReadProfileFromMesh が取り込みの規則（ExtractPolyline / ExtractLoops、フリル・パイプの正規化）を持つ。
//   既存の取り込みボタンも作業空間も同じ関数を通す。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Revolution;
using Poly_Ling.Profile2DExtrude;
using Poly_Ling.Tools;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    /// <summary>作業空間で編集できる断面の種類。</summary>
    public enum ProfileEditKind
    {
        Revolution = 0,
        Profile2D  = 1,
        FrillA     = 2,
        FrillB     = 3,
        Pipe       = 4,
    }

    public partial class PlayerPrimitiveMeshSubPanel
    {
        /// <summary>「作業空間で開く」ボタンが呼ぶ。Viewer が配線する（アクティブな線分オブジェクトで開く）。</summary>
        public Action<ProfileEditKind> OnOpenProfileEditSpace;

        // ================================================================
        // 種類の名前
        // ================================================================

        /// <summary>コマンドの引数・戻り値に使う名前。</summary>
        public static string KindName(ProfileEditKind k)
        {
            switch (k)
            {
                case ProfileEditKind.Revolution: return "revolution";
                case ProfileEditKind.Profile2D:  return "profile2d";
                case ProfileEditKind.FrillA:     return "frillA";
                case ProfileEditKind.FrillB:     return "frillB";
                default:                         return "pipe";
            }
        }

        /// <summary>名前から種類を引く。大文字小文字は無視。</summary>
        public static bool TryParseKind(string name, out ProfileEditKind k)
        {
            foreach (ProfileEditKind v in Enum.GetValues(typeof(ProfileEditKind)))
            {
                if (string.Equals(KindName(v), name ?? "", StringComparison.OrdinalIgnoreCase))
                { k = v; return true; }
            }
            k = ProfileEditKind.Revolution;
            return false;
        }

        /// <summary>画面に出す種類名。</summary>
        public static string KindDisplayName(ProfileEditKind k)
        {
            switch (k)
            {
                case ProfileEditKind.Revolution: return "回転体の断面";
                case ProfileEditKind.Profile2D:  return "2D押し出しの輪郭";
                case ProfileEditKind.FrillA:     return "フリルの断面 A";
                case ProfileEditKind.FrillB:     return "フリルの断面 B";
                default:                         return "パイプの断面";
            }
        }

        private BeltProfileEdit BeltEditOf(ProfileEditKind k)
        {
            switch (k)
            {
                case ProfileEditKind.FrillA: return _frillEdit;
                case ProfileEditKind.FrillB: return _frillEditB;
                case ProfileEditKind.Pipe:   return _pipeEdit;
                default:                     return null;
            }
        }

        /// <summary>フリル・パイプのエディタから種類を引く（ボタンの配線用）。</summary>
        private ProfileEditKind KindOf(BeltProfileEdit ed)
        {
            if (ReferenceEquals(ed, _frillEditB)) return ProfileEditKind.FrillB;
            if (ReferenceEquals(ed, _pipeEdit))   return ProfileEditKind.Pipe;
            return ProfileEditKind.FrillA;
        }

        // ================================================================
        // 読み取り（取り込みの規則）
        // ================================================================

        /// <summary>メッシュから読んだ断面。種類によって Points か Loops のどちらかを持つ。</summary>
        private sealed class ProfileReadResult
        {
            public List<Vector3> Points;
            public List<Loop>    Loops;
        }

        /// <summary>
        /// メッシュの 2 頂点ラインから断面を読む。失敗理由を返す（成功なら null）。
        /// フリル・パイプは断面座標の規則（rung 長で正規化）に合わせて正規化する。
        /// </summary>
        private string ReadProfileFromMesh(ProfileEditKind k, MeshObject mesh, out ProfileReadResult result)
        {
            result = null;
            if (mesh == null) return T("NoSelectedMesh");
            var lineFaces = LineProfileExtractor.CollectLineFaceIndices(mesh);

            switch (k)
            {
                case ProfileEditKind.Revolution:
                {
                    var pts = LineProfileExtractor.ExtractPolyline(mesh, lineFaces);
                    if (pts == null || pts.Count < 2) return T("NoLinesFound");
                    result = new ProfileReadResult { Points = new List<Vector3>(pts) };
                    return null;
                }
                case ProfileEditKind.Profile2D:
                {
                    var loops = LineProfileExtractor.ExtractLoops(mesh, lineFaces);
                    if (loops == null || loops.Count == 0) return T("NoLinesFound");
                    result = new ProfileReadResult { Loops = loops };
                    return null;
                }
                default:
                {
                    var ed = BeltEditOf(k);
                    if (ed == null) return "断面の種類が不正です";
                    List<Vector3> pts = null;
                    if (ed.ClosedLoop)
                    {
                        // 閉ループ断面。複数ループがあれば点数が最多のものを採る。
                        var loops = LineProfileExtractor.ExtractLoops(mesh, lineFaces);
                        if (loops != null)
                            foreach (var lp in loops)
                            {
                                if (lp?.Points == null || lp.Points.Count < 3) continue;
                                if (pts == null || lp.Points.Count > pts.Count) pts = lp.Points;
                            }
                    }
                    else
                    {
                        pts = LineProfileExtractor.ExtractPolyline(mesh, lineFaces);
                    }
                    if (pts == null || pts.Count < 2) return T("NoLinesFound");
                    var norm = NormalizeBeltProfile(pts);
                    if (norm == null) return T("ProfileDegenerate");
                    result = new ProfileReadResult { Points = norm };
                    return null;
                }
            }
        }

        /// <summary>
        /// 読んだ断面をパネルへ入れる（パネルの Undo に desc で記録する）。
        /// srcMasterIndex は取り込み元の索引（オブジェクトグループの作り直し用の控え）。
        /// </summary>
        private void StoreProfile(ProfileEditKind k, ProfileReadResult r, int srcMasterIndex, string desc)
        {
            switch (k)
            {
                case ProfileEditKind.Revolution:
                    _revProfileSrcIndex = srcMasterIndex;
                    RevBegin();
                    _revProfile = new List<Vector3>(r.Points);
                    _revSel.Clear(); _revSelIdx = -1;
                    _revP.CurrentPreset = ProfilePreset.Custom;
                    RevCommit(desc);
                    if (_statusLabel != null) _statusLabel.text = T("ImportedPoints", r.Points.Count);
                    D(); RefreshRevCanvas(); RefreshRevPointUI();
                    return;

                case ProfileEditKind.Profile2D:
                    _p2dProfileSrcIndex = srcMasterIndex;
                    P2dBegin();
                    _p2dLoops   = r.Loops;
                    _p2dSelLoop = 0;
                    _p2dSel.Clear();
                    _p2dSelPt   = -1;
                    P2dCommit(desc);
                    if (_statusLabel != null) _statusLabel.text = T("ImportedLoops", r.Loops.Count);
                    D(); RebuildSettings();
                    return;

                default:
                {
                    var ed = BeltEditOf(k);
                    ed.SourceMasterIndex = srcMasterIndex;
                    BeltBegin(ed);
                    ed.Points = r.Points;
                    ed.Sel.Clear(); ed.SelectedIndex = -1;
                    BeltCommit(ed, desc);
                    SetBeltStatus(T("ImportedPoints", r.Points.Count));
                    D(); RefreshBeltCanvas(ed); RefreshBeltPointUI(ed);
                    return;
                }
            }
        }

        // ================================================================
        // 作業空間の断面系アダプタから使う口
        // ================================================================

        /// <summary>代理（線分オブジェクト）から読んだ断面が、パネルの断面と一致するか。読めなければ false。</summary>
        public bool ProfileMatchesMesh(ProfileEditKind k, MeshObject mesh)
        {
            if (ReadProfileFromMesh(k, mesh, out var r) != null) return false;
            switch (k)
            {
                case ProfileEditKind.Revolution:
                    EnsureRevProfile();
                    return RevProfileEquals(r.Points, _revProfile);
                case ProfileEditKind.Profile2D:
                    EnsureP2DLoops();
                    return P2dLoopsEquals(r.Loops, _p2dLoops);
                default:
                {
                    var ed = BeltEditOf(k);
                    EnsureBeltProfile(ed);
                    return BeltProfileEquals(r.Points, ed.Points);
                }
            }
        }

        /// <summary>
        /// 代理から断面を読み、パネルの断面へ入れる（作業空間の「反映」）。
        /// 失敗理由を返す（成功なら null）。パネルの Undo に desc で記録する。
        /// </summary>
        public string ApplyProfileFromMesh(ProfileEditKind k, MeshObject mesh, int srcMasterIndex, string desc)
        {
            string err = ReadProfileFromMesh(k, mesh, out var r);
            if (err != null) return err;
            StoreProfile(k, r, srcMasterIndex, desc);
            return null;
        }

        /// <summary>
        /// パネルの断面から線分のメッシュを作る（作業空間の「取消し」で代理を置き換える中身）。
        /// 「反映（→メッシュ）」と同じ作り方。姿勢の焼き込みは掛けない（代理の姿勢はオブジェクトが持つ）。
        /// </summary>
        public MeshObject BuildProfileLineMesh(ProfileEditKind k, string name, out string error)
        {
            error = null;
            MeshObject mo;
            switch (k)
            {
                case ProfileEditKind.Revolution:
                    EnsureRevProfile();
                    mo = _revProfile != null && _revProfile.Count >= 2
                        ? LineProfileExtractor.PolylineToLineMesh(_revProfile, name, _revP.CloseLoop) : null;
                    break;
                case ProfileEditKind.Profile2D:
                    EnsureP2DLoops();
                    mo = _p2dLoops != null && _p2dLoops.Count > 0
                        ? LineProfileExtractor.LoopsToLineMesh(_p2dLoops, name) : null;
                    break;
                default:
                {
                    var ed = BeltEditOf(k);
                    EnsureBeltProfile(ed);
                    mo = ed.Points != null && ed.Points.Count >= 2
                        ? LineProfileExtractor.PolylineToLineMesh(ed.Points, name, ed.ClosedLoop) : null;
                    break;
                }
            }
            if (mo == null || mo.FaceCount == 0) { error = T("NoLinesFound"); return null; }
            return mo;
        }

        /// <summary>選択オブジェクトから取り込む（既存の「取り込み」ボタン）。</summary>
        private void ImportProfileFromSelected(ProfileEditKind k)
        {
            var mesh = GetSelectedMeshObject?.Invoke();
            string err = ApplyProfileFromMesh(k, mesh, ResolveMasterIndexOf(mesh), "メッシュ取込");
            if (err == null) return;
            if (k == ProfileEditKind.Revolution || k == ProfileEditKind.Profile2D)
            { if (_statusLabel != null) _statusLabel.text = err; }
            else SetBeltStatus(err);
        }

        /// <summary>「作業空間で開く」ボタン。</summary>
        private void RequestOpenProfileEditSpace(ProfileEditKind k)
        {
            if (OnOpenProfileEditSpace == null)
            {
                if (_statusLabel != null) _statusLabel.text = "作業空間の受け口がありません";
                return;
            }
            OnOpenProfileEditSpace(k);
        }
    }
}

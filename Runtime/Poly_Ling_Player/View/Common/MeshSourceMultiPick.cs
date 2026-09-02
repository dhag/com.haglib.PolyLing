// MeshSourceMultiPick.cs
// 描画オブジェクトの複数選択状態。UIToolkit の一覧と組み合わせて使う。
// Runtime/Poly_Ling_Player/View/Common/ に配置
//
// 【移設元】Runtime/Poly_Ling_Player/View/PrimitiveMesh/PlayerPrimitiveMeshSubPanel.BeltProfile.cs
//   private sealed class MeshSourceMultiPick → public sealed class（内容は移設元のまま）
//
// 【組み立て側】一覧の組み立てと再取得（BuildMeshSourceMultiRow / RefreshMeshSourceMultiPick）は
//   パネル固有のテキスト辞書・ダーティ通知に依存するため移していない。使う側で用意する。

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    /// <summary>描画オブジェクトの複数選択状態。選択はラベルで保持し、一覧再取得後も復元する。</summary>
    public sealed class MeshSourceMultiPick
    {
        public List<(string Label, int MasterIndex, MeshObject Mesh)> Candidates
            = new List<(string, int, MeshObject)>();
        public readonly HashSet<string> SelectedLabels = new HashSet<string>();
        public VisualElement ListContainer;

        /// <summary>
        /// 候補の並び順で、選択されているメッシュを返す。
        /// 面を持たないもの（グループ用の空オブジェクト等）は数に入れない。
        /// </summary>
        public List<MeshObject> CurrentList()
            => CurrentList(false, null);

        /// <summary>
        /// 候補の並び順で、選択されているメッシュを返す。
        /// includeChildren が true のときは、チェックした項目を「本体＋子孫」へ展開し、
        /// それぞれを別々の配置元として並べる（結合しない）。これで rung ごとの
        /// 巡回・抽選が子孫に対して効く。
        /// 展開結果は MasterIndex で重複排除するため、ルートと子の両方をチェックしても
        /// 二重には入らない。面を持たないものは数に入れない。
        /// </summary>
        public List<MeshObject> CurrentList(
            bool includeChildren, Func<int, List<(int MasterIndex, MeshObject Mesh)>> expand)
            => Resolve(SelectedMasterIndices(), includeChildren, expand, GetCandidateMesh);

        /// <summary>
        /// チェックの入っている候補の MasterIndex を、候補の並び順で返す。
        /// コマンド（CreatePlaceObjectCommand.SourceMasterIndices）へ載せるのに使う。
        /// </summary>
        public List<int> SelectedMasterIndices()
        {
            var list = new List<int>();
            foreach (var e in Candidates)
                if (SelectedLabels.Contains(e.Label)) list.Add(e.MasterIndex);
            return list;
        }

        /// <summary>候補一覧から MasterIndex で引く。一覧に無ければ null。</summary>
        private MeshObject GetCandidateMesh(int masterIndex)
        {
            foreach (var e in Candidates)
                if (e.MasterIndex == masterIndex) return e.Mesh;
            return null;
        }

        /// <summary>
        /// MasterIndex の列を配置元の MeshObject 列へ解決する。
        /// 展開・重複排除・面なしの除外の規則は CurrentList と同一で、実装もここ 1 本。
        ///
        /// コマンド経由の生成は候補一覧を持たないので、索引から MeshObject を引く
        /// get を呼び出し側（モデルを持つ側）から受け取る。
        /// </summary>
        public static List<MeshObject> Resolve(
            IEnumerable<int> masterIndices, bool includeChildren,
            Func<int, List<(int MasterIndex, MeshObject Mesh)>> expand,
            Func<int, MeshObject> get)
        {
            var list  = new List<MeshObject>();
            var added = new HashSet<int>();
            if (masterIndices == null) return list;

            foreach (int mi in masterIndices)
            {
                if (includeChildren && expand != null)
                {
                    var sub = expand(mi);
                    if (sub != null)
                    {
                        foreach (var s in sub)
                        {
                            if (!HasFace(s.Mesh)) continue;
                            if (!added.Add(s.MasterIndex)) continue;
                            list.Add(s.Mesh);
                        }
                        continue;
                    }
                }

                var mo = get?.Invoke(mi);
                if (!HasFace(mo)) continue;
                if (!added.Add(mi)) continue;
                list.Add(mo);
            }
            return list;
        }

        /// <summary>面を1枚以上持つか。頂点だけのオブジェクトは配置しても何も出ないため除く。</summary>
        private static bool HasFace(MeshObject mo) => mo != null && mo.FaceCount > 0;
    }
}

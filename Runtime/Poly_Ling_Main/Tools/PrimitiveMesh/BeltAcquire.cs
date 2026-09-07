// BeltAcquire.cs
// 梯子（基準ベルト）の取り込み方を表す列挙と、その手順を掛け直す入口。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【なぜ「取り込み方」を持つか】
//   フリル・パイプ・藤壺は、取り込んだ時点の梯子の点列をコマンドへ焼き込む。
//   そのままだと、取り込み元を直しても出力先は古い形のままになる。
//
//   追随させるために「どの頂点だったか」を控える案は成り立たない。
//   頂点IDは人が手で管理するもので、図形生成で作ったメッシュには付いていない
//   （Vertex の既定 Id は 0 ＝ 未設定）。頂点番号は編集で動く。
//   どちらも、手で作る人に番号の管理を強いることになる。
//
//   代わりに「どうやって取り込んだか」だけを控える。取り込みは
//   検出器と選択辞書で再現できるので、作り直しのたびに掛け直せばよい。
//   人が管理するのはメッシュ側の目印（開始タグ三角形）か、
//   面を入れた選択辞書の名前だけで済む。
//
// 【取り直せないときは据え置く】
//   ソースが無い・辞書が無い・検出 0 本のときは、控えた点列をそのまま使う。
//   黙って別の面を読むより、古いままの方が原因を追える。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>梯子をどうやって取り込んだか。</summary>
    public enum BeltAcquireMethod
    {
        /// <summary>取り込み方を記録していない。作り直しでは控えた点列をそのまま使う。</summary>
        Baked = 0,

        /// <summary>はしごを自動検索（BeltStackDetector）。開始タグ三角形が起点。</summary>
        AutoLadder = 1,

        /// <summary>円環を自動検索（BeltRingDetector）。目印は要らない。</summary>
        AutoRing = 2,

        /// <summary>パーツ選択辞書の面から取り込む（BeltStripExtractor）。</summary>
        SelectionSet = 3,
    }

    /// <summary>取り直しの結果。</summary>
    public sealed class BeltAcquireResult
    {
        /// <summary>取り直せたか。false のときは控えた点列を使うこと。</summary>
        public bool Ok;

        /// <summary>取り直した梯子。Ok が false のときは空。</summary>
        public List<BeltCsvEntry> Belts = new List<BeltCsvEntry>();

        /// <summary>段グループの数（上下展開したとき）。</summary>
        public int GroupCount;

        /// <summary>人へ出す説明。成否にかかわらず入れる。</summary>
        public string Message = "";

        public static BeltAcquireResult Fail(string message)
            => new BeltAcquireResult { Ok = false, Message = message ?? "" };
    }

    /// <summary>梯子の取り込みを掛け直す。</summary>
    public static class BeltAcquire
    {
        /// <summary>
        /// 記録した取り込み方で、ソースから梯子を取り直す。
        /// </summary>
        /// <param name="source">取り込み元の描画オブジェクト。選択辞書を引くため MeshContext で受ける。</param>
        /// <param name="method">取り込み方。</param>
        /// <param name="crossRows">上下（左右レール側）へ横断して段グループにまとめるか。</param>
        /// <param name="setName">SelectionSet のときの辞書名。</param>
        public static BeltAcquireResult Acquire(
            MeshContext source, BeltAcquireMethod method, bool crossRows, string setName)
        {
            if (method == BeltAcquireMethod.Baked)
                return BeltAcquireResult.Fail("取り込み方が記録されていない（控えた点列を使う）");

            var mesh = source?.MeshObject;
            if (mesh == null)
                return BeltAcquireResult.Fail("取り込み元のメッシュがない");

            List<BeltAutoStrip> rows;
            int groupCount = 0;
            string message;

            switch (method)
            {
                case BeltAcquireMethod.AutoLadder:
                {
                    rows = BeltStackDetector.Detect(mesh, crossRows, out message);
                    break;
                }

                case BeltAcquireMethod.AutoRing:
                {
                    var rings = BeltRingDetector.Detect(mesh, out message);
                    if (rings == null || rings.Count == 0)
                        return BeltAcquireResult.Fail($"円環を検出できない（{message}）");
                    rows = BeltStackExpander.ExpandAll(mesh, rings, crossRows, out groupCount);
                    break;
                }

                case BeltAcquireMethod.SelectionSet:
                {
                    if (string.IsNullOrEmpty(setName))
                        return BeltAcquireResult.Fail("選択辞書の名前が記録されていない");

                    var set = FindSet(source, setName);
                    if (set == null)
                        return BeltAcquireResult.Fail($"選択辞書「{setName}」が見つからない");
                    if (set.Faces == null || set.Faces.Count == 0)
                        return BeltAcquireResult.Fail($"選択辞書「{setName}」に面が入っていない");

                    var strip = BeltStripExtractor.Extract(mesh, set.Faces);
                    if (!strip.Ok)
                        return BeltAcquireResult.Fail($"選択面から梯子を作れない（{strip.Message}）");

                    var baseRow = new BeltAutoStrip
                    {
                        Closed      = strip.Closed,
                        FlipWinding = strip.FlipWinding,
                    };
                    baseRow.Left .AddRange(strip.Left);
                    baseRow.Right.AddRange(strip.Right);
                    baseRow.Faces.AddRange(strip.Faces);

                    rows    = BeltStackExpander.ExpandAll(
                        mesh, new List<BeltAutoStrip>(1) { baseRow }, crossRows, out groupCount);
                    message = strip.Message;
                    break;
                }

                default:
                    return BeltAcquireResult.Fail($"未対応の取り込み方: {method}");
            }

            if (rows == null || rows.Count == 0)
                return BeltAcquireResult.Fail($"梯子を検出できない（{message}）");

            var result = new BeltAcquireResult
            {
                Ok         = true,
                GroupCount = groupCount,
                Message    = message ?? "",
            };

            foreach (var st in rows)
            {
                if (st == null || st.RungCount < 2) continue;

                var e = new BeltCsvEntry
                {
                    Closed      = st.Closed,
                    FlipWinding = st.FlipWinding,
                    GroupId     = st.GroupId,
                    RowIndex    = st.RowIndex,
                    RowCount    = st.RowCount,
                };

                for (int i = 0; i < st.RungCount; i++)
                {
                    int li = st.Left[i];
                    int ri = st.Right[i];
                    if (li < 0 || li >= mesh.VertexCount) continue;
                    if (ri < 0 || ri >= mesh.VertexCount) continue;
                    e.Left .Add(mesh.Vertices[li].Position);
                    e.Right.Add(mesh.Vertices[ri].Position);
                }

                if (st.StartPoint >= 0 && st.StartPoint < mesh.VertexCount)
                    e.StartPoint = mesh.Vertices[st.StartPoint].Position;
                if (st.EndPoint >= 0 && st.EndPoint < mesh.VertexCount)
                    e.EndPoint = mesh.Vertices[st.EndPoint].Position;

                if (e.HasData) result.Belts.Add(e);
            }

            if (result.Belts.Count == 0)
                return BeltAcquireResult.Fail($"検出した梯子に使える点列がない（{message}）");

            return result;
        }

        /// <summary>
        /// 名前でパーツ選択辞書を引く。見つからなければ null。
        /// 辞書は MeshContext.PartsSelectionSetList が持つ（MeshObject 側ではない）。
        /// </summary>
        private static PartsSelectionSet FindSet(MeshContext source, string name)
        {
            var sets = source?.PartsSelectionSetList;
            if (sets == null) return null;
            for (int i = 0; i < sets.Count; i++)
                if (sets[i] != null && sets[i].Name == name) return sets[i];
            return null;
        }
    }
}

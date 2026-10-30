// ModifierSpace.cs
// オブジェクトグループの項目（モディファイア）を、スキンド化のあとにどの空間で評価するか。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【何が問題か】（PolyLing_作り直し整備計画.md 4 章 2・段 5）
//   頂点の格納空間は種別で違う。MeshFilter はローカル、スキンドはバインド姿勢のワールド。
//   生成は空間を変換しない。梯子の頂点の格納値をそのまま点列にし（ObjectGroupOps.RefreshBelts）、
//   ウェイトも格納値の位置で突き合わせ（BeltWeightBinder）、出力の格納値へそのまま書く
//   （PrimitiveMeshReplaceExisting）。梯子と出力の格納空間が違うと出力が飛ぶ。
//
// 【決めた扱い】（段 5、段 10 で改めた）
//   ・梯子を読む項目（CreateBeltPrimitiveCommand 系で梯子の取り込み元を持つもの）は、
//     生成物を行列 M で出力の格納空間へ移してから書く。作成時も同じ行列を掛ける（段 10）。
//       M ＝（出力の枠の バインド→格納）×（梯子の 格納→バインド）
//     出力の枠は、新規オブジェクトとして作った項目では出力の親（置き方は出力の姿勢が持つ。
//     段 8 の StripPoseOwnedByOutput と揃える）、既存へ追加で作ったものは出力そのもの。
//     一括スキンド化のあと（両方スキンド）は M が単位になる。
//   ・揺れ鎖（placeSpringBoneLadderChains）は自分でバインド空間で読む（SpringBoneLadderPlacer）。
//   ・入力の読む空間は PLParam.MeshRefSpace で宣言する（段 10）。
//     Model の入力はスキンドでも読める（VertexToBindWorldMatrix。スキンドは単位）。
//     Local の入力（原型・断面）がスキンドなら、スキンド化で控えた ローカル→バインド の行列
//     （MeshObject.SkinBakeMatrix）の逆で元のローカルへ戻して読む（LocalView）。控えが無いものは外す。
//   ・生成物が梯子以外の入力の格納空間で出来る項目（貼り付けの SourceIndex など）は、その引数に
//     PLParam.OutputSpaceSource を付け、梯子と同じ行列 M で移す（SpaceSourceKey）。
//   ・どちらも持たない項目で出力がスキンド化済みのものは外す。
//
// 【ポーズ】評価はバインド空間で行う。ポーズ（BonePoseData）は WorldMatrix にだけ入り、
//   BindWorldMatrix には入らないので、歩かせている最中に作り直しても結果は変わらない。

using System;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>項目の評価空間。</summary>
    public enum ModifierSpaceKind
    {
        /// <summary>入力も出力もスキンドでない。今のまま流す。</summary>
        Same = 0,
        /// <summary>項目が自分でバインド空間で読む（揺れ鎖）。</summary>
        SelfBind = 1,
        /// <summary>生成物を行列 M で出力の格納空間へ移して書く。</summary>
        Transform = 2,
        /// <summary>評価できない（理由は reason）。</summary>
        Unsupported = 3,
    }

    public static class ModifierSpace
    {
        /// <summary>揺れ鎖の action。</summary>
        public const string SpringLadderAction = "placeSpringBoneLadderChains";

        /// <summary>照会に出す短い名前。</summary>
        public static string Label(ModifierSpaceKind k)
        {
            switch (k)
            {
                case ModifierSpaceKind.Same:      return "そのまま";
                case ModifierSpaceKind.SelfBind:  return "バインド空間で読む";
                case ModifierSpaceKind.Transform: return "生成物を入力（梯子・貼り付け元）の格納空間から出力の格納空間へ移して書く";
                default:                          return "評価できない";
            }
        }

        /// <summary>
        /// 生成物がその入力の格納空間で出来る入力のキー。無ければ null。
        /// 梯子を読む型なら梯子の取り込み元（BeltSourceIndex）、それ以外は PLParam.OutputSpaceSource を付けた引数
        /// （貼り付けの SourceIndex など）。評価器の分類・作り直しの行列・評価器の署名・作成時の行列が同じこの関数を使う。
        /// </summary>
        public static string SpaceSourceKey(Type t)
        {
            if (t == null) return null;
            if (typeof(CreateBeltPrimitiveCommand).IsAssignableFrom(t))
            {
                string k = PanelCommandFactory.KeyOfProperty(t, nameof(CreateBeltPrimitiveCommand.BeltSourceIndex));
                return string.IsNullOrEmpty(k) ? null : k;
            }
            foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                var a = (PLParamAttribute)Attribute.GetCustomAttribute(p, typeof(PLParamAttribute));
                // int の配列（パイプの梯子化の取り込み元）は先頭を生成物の座標系とする。
                // 並びの格納の行列がそろっているかは Classify が見る。
                if (a == null || !a.OutputSpaceSource || (p.PropertyType != typeof(int) && p.PropertyType != typeof(int[]))) continue;
                string k = PanelCommandFactory.KeyOfProperty(t, p.Name);
                if (!string.IsNullOrEmpty(k)) return k;
            }
            return null;
        }

        /// <summary>
        /// 項目の評価空間を決める。Unsupported のときは reason に理由。
        ///
        /// 入力の読む空間は PLParam.MeshRefSpace の宣言で決まる（段 10）。
        ///   Model の入力 … スキンドでも読める（VertexToBindWorldMatrix で読む。スキンドは単位）
        ///   梯子          … 生成物を行列 M で出力の格納空間へ移して書く（梯子の格納空間で生成するので）
        ///   ほかの Local  … スキンドなら、スキンド化で控えた行列でローカルへ戻して読む（LocalView）。
        ///                   控えが無い（PMX などで読み込んだスキンド）ものは評価できない
        /// 梯子を持たない項目で出力がスキンド化済みのものは評価できない（生成物の座標系が出力に合わない）。
        /// </summary>
        public static ModifierSpaceKind Classify(ModifierEvaluator.Entry e, out string reason)
        {
            reason = "";
            if (e == null) { reason = "項目がありません"; return ModifierSpaceKind.Unsupported; }

            bool anySkinned = false;
            foreach (var kv in e.Inputs) if (kv.Value.IsSkinned) { anySkinned = true; break; }
            bool outSkinned = false;
            foreach (var o in e.Outputs) if (o.IsSkinned) { outSkinned = true; break; }

            if (string.Equals(e.Step?.Action, SpringLadderAction, StringComparison.Ordinal))
                return anySkinned || outSkinned ? ModifierSpaceKind.SelfBind : ModifierSpaceKind.Same;

            string beltKey = SpaceSourceKey(e.CommandType);
            var belt = beltKey != null ? e.Input(beltKey) : null;

            // ローカルで読む入力（梯子以外）がスキンド化済みなら、控えた行列が要る。
            foreach (var kv in e.Inputs)
            {
                var mc = kv.Value;
                if (!mc.IsSkinned) continue;
                if (beltKey != null && string.Equals(kv.Key, beltKey, StringComparison.Ordinal)) continue;
                if (e.ModelInputs.Contains(mc)) continue;
                if (mc.MeshObject != null && mc.MeshObject.HasSkinBakeMatrix) continue;
                reason = $"ローカルで読む入力「{mc.Name}」（{kv.Key}）がスキンド化済みで、"
                       + "スキンド化のときの ローカル→バインド の行列が控えられていない（元の座標系が引けない）";
                return ModifierSpaceKind.Unsupported;
            }

            // 梯子を読む項目と、生成物が入力の格納空間で出来る項目（PLParam.OutputSpaceSource）は、
            // 生成物をその入力の格納空間から出力の格納空間へ移す（作成時と同じ。段 10）。
            if (beltKey != null)
            {
                // 生成物の座標系の入力が複数（パイプの梯子化）なら、格納→バインド の行列がそろっていること。
                // 生成器は全部を格納値のまま 1 つにまとめるので、行列が違うと 1 つの M では移せない。
                if (belt != null)
                {
                    Matrix4x4 m0 = belt.VertexToBindWorldMatrix;
                    foreach (var kv in e.Inputs)
                    {
                        if (!string.Equals(kv.Key, beltKey, StringComparison.Ordinal) || ReferenceEquals(kv.Value, belt)) continue;
                        if (!SameMatrix(m0, kv.Value.VertexToBindWorldMatrix))
                        {
                            reason = $"生成物の座標系の入力「{belt.Name}」と「{kv.Value.Name}」（{beltKey}）の"
                                   + "格納→バインド の行列が違う（1 つの行列で出力へ移せない）";
                            return ModifierSpaceKind.Unsupported;
                        }
                    }
                    return ModifierSpaceKind.Transform;
                }
                if (outSkinned)
                {
                    reason = "出力がスキンド化済みで、生成物の座標系の入力（梯子の取り込み元など）が無い（控えた点列の座標系が引けない）";
                    return ModifierSpaceKind.Unsupported;
                }
                return ModifierSpaceKind.Same;
            }

            if (outSkinned)
            {
                reason = "出力がスキンド化済みで、この項目は生成物を出力の格納空間へ移す読み方を持たない";
                return ModifierSpaceKind.Unsupported;
            }

            return ModifierSpaceKind.Same;
        }

        private static bool SameMatrix(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(a[i] - b[i]) > 1e-5f) return false;
            return true;
        }

        /// <summary>
        /// 生成物（梯子の格納空間）を出力の格納空間へ移す行列 M（段 10 で常に掛ける）。
        /// 呼ぶ前に ComputeWorldMatrices を済ませること。
        ///
        ///   M ＝（出力の枠の バインド→格納）×（梯子の 格納→バインド）
        ///
        /// 出力の枠は、poseOwnedByOutput（新規オブジェクトとして作った項目。置き方は出力の姿勢が持つ。
        /// 段 8 の StripPoseOwnedByOutput）なら出力の親の BindWorldMatrix。出力自身の姿勢（BoneTransform）は
        /// 枠に入れない（作ったあとに出力を動かしても、作り直しで梯子へ戻さない）。それ以外（既存へ追加で
        /// 作ったもの）は出力の VertexToBindWorldMatrix。スキンドの出力は格納値がバインド空間なので単位。
        /// </summary>
        public static bool TryComputeMatrix(MeshContext belt, MeshContext output, bool poseOwnedByOutput, out Matrix4x4 m)
        {
            m = Matrix4x4.identity;
            if (belt == null || output == null) return false;
            m = OutputFrameBindToVertex(output, poseOwnedByOutput) * belt.VertexToBindWorldMatrix;
            return true;
        }

        /// <summary>
        /// 作ったときの追加先モードが新規オブジェクト・新規モデルか（置き方は出力の姿勢が持つ。段 8・段 10）。
        /// StripPoseOwnedByOutput と同じ判定。梯子の行列 M の出力の枠を決める（TryComputeMatrix）。
        /// 作り直し（RunObjectGroupStep）と評価器の署名（ModifierEvaluator.OutputFrameSig）が同じこの判定を使う（段 E）。
        /// </summary>
        public static bool IsPoseOwnedByOutput(Type t, ObjectGroupStep step)
        {
            if (t == null || step == null) return false;
            var rb = PanelCommandFactory.RebuildKeys(t);
            if (!rb.HasMode) return false;
            string modeText = step.GetArg(rb.ModeKey);
            if (string.IsNullOrEmpty(modeText) || !Enum.TryParse(modeText, true, out Poly_Ling.Player.PrimitiveAddMode mode)) return false;
            return mode == Poly_Ling.Player.PrimitiveAddMode.NewObject || mode == Poly_Ling.Player.PrimitiveAddMode.NewModel;
        }

        /// <summary>
        /// モデル空間で読む入力（PLParam.MeshRefSpace = Model）の頂点の位置（段 E）。
        /// VertexToBindWorldMatrix × 格納値。ポーズを含まないバインド姿勢のワールドで、
        /// 評価器の署名（ModifierEvaluator.Take の Matrix）と同じ行列を使う。
        /// CPU で求めるので、描画バッファに載っていないオブジェクト（隠したもの）も読める。
        /// 生成器（辺から帯・頂点へ藤壺）はこれで入力を読む。GPU の表示位置（ポーズ込み）を読むと、
        /// ポーズを付けたまま作り直したときに結果がポーズで変わり、署名とも食い違う。
        /// </summary>
        public static Vector3[] BindWorldPositions(MeshContext mc)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return null;
            var m = mc.VertexToBindWorldMatrix;
            var result = new Vector3[mo.VertexCount];
            for (int i = 0; i < result.Length; i++)
                result[i] = m.MultiplyPoint3x4(mo.Vertices[i].Position);
            return result;
        }

        /// <summary>出力の枠の バインド→格納（TryComputeMatrix の説明を参照）。</summary>
        public static Matrix4x4 OutputFrameBindToVertex(MeshContext output, bool poseOwnedByOutput)
        {
            if (output == null || output.IsSkinned) return Matrix4x4.identity;
            if (!poseOwnedByOutput) return output.BindWorldToVertexMatrix;
            var model  = output.ParentModelContext;
            int parent = output.HierarchyParentIndex;
            var pmc    = model != null && parent >= 0 && parent < model.MeshContextCount ? model.GetMeshContext(parent) : null;
            return pmc != null ? pmc.BindWorldMatrixInverse : Matrix4x4.identity;
        }

        /// <summary>
        /// 生成物へ行列を掛ける（位置と法線。法線は逆転置）。生成の控え（ModifierFrameTrace）にも知らせる。
        /// 作成時（梯子の行列）と書き戻し（PrepareForOutput）が使う。mo は書き換えてよいものであること。
        /// </summary>
        public static void ApplyMatrix(MeshObject mo, Matrix4x4 m)
        {
            if (mo == null) return;
            Matrix4x4 nm = m.inverse.transpose;
            foreach (var v in mo.Vertices)
            {
                if (v == null) continue;
                v.Position = m.MultiplyPoint3x4(v.Position);
                if (v.Normals != null)
                    for (int i = 0; i < v.Normals.Count; i++)
                    {
                        Vector3 n = nm.MultiplyVector(v.Normals[i]);
                        v.Normals[i] = n.sqrMagnitude > 1e-20f ? n.normalized : v.Normals[i];
                    }
            }
            mo.InvalidatePositionCache();
            ModifierFrameTrace.Transformed(mo, m);
        }

        /// <summary>
        /// ローカルで読む入力の、読むための形（段 10）。スキンドでスキンド化の行列の控えがあれば、
        /// その逆で元のローカルへ戻した複製を返す。それ以外は元の MeshObject をそのまま返す。
        /// 複製は読むだけに使うこと（書き戻さない）。
        /// </summary>
        public static MeshObject LocalView(MeshObject mo)
        {
            if (mo == null || !mo.IsSkinnedKind || !mo.HasSkinBakeMatrix) return mo;
            var copy = mo.Clone();
            Matrix4x4 inv = mo.SkinBakeMatrix.inverse;
            Matrix4x4 nm  = mo.SkinBakeMatrix.transpose;   // 逆行列の逆転置
            foreach (var v in copy.Vertices)
            {
                if (v == null) continue;
                v.Position = inv.MultiplyPoint3x4(v.Position);
                if (v.Normals != null)
                    for (int i = 0; i < v.Normals.Count; i++)
                    {
                        Vector3 n = nm.MultiplyVector(v.Normals[i]);
                        v.Normals[i] = n.sqrMagnitude > 1e-20f ? n.normalized : v.Normals[i];
                    }
            }
            copy.InvalidatePositionCache();
            return copy;
        }

        // ================================================================
        // 書き戻しへの受け渡し
        //
        // 作り直しは入れ子の Dispatch で生成コマンドを流し、ReplaceExisting が出力へ書く。
        // その間だけ、どの行列で移すか・ウェイトを残すかをここに置く（RunObjectGroupStep が立てる）。
        // ================================================================

        /// <summary>書き戻しの指定 1 回ぶん。</summary>
        public sealed class WriteBack
        {
            /// <summary>生成物へ掛ける行列。</summary>
            public Matrix4x4 Matrix = Matrix4x4.identity;
            /// <summary>出力の描画オブジェクト（この相手へ書くときだけ効かせる）。</summary>
            public MeshContext Output;
        }

        private static WriteBack _current;

        /// <summary>今の書き戻しの指定。無ければ null。</summary>
        public static WriteBack Current => _current;

        /// <summary>指定を立てる。戻り値を Dispose すると外れる。</summary>
        public static IDisposable Begin(WriteBack wb)
        {
            var prev = _current;
            _current = wb;
            return new Scope(prev);
        }

        private sealed class Scope : IDisposable
        {
            private readonly WriteBack _prev;
            private bool _done;
            public Scope(WriteBack prev) { _prev = prev; }
            public void Dispose()
            {
                if (_done) return;
                _done = true;
                _current = _prev;
            }
        }

        /// <summary>
        /// 生成物を出力の種別に合わせる。ReplaceExisting が中身を入れ替える前に呼ぶ。
        ///   ・wb があれば行列を掛ける（法線は逆転置）
        ///   ・出力が MeshFilter なら、ウェイトを捨てる（残すと、その頂点だけボーンの欄を引いて飛ぶ）
        ///   ・出力がスキンドなら、ウェイトの無い頂点があれば失敗（同じ理由で飛ぶ）
        /// mo は書き換えてよい複製であること。戻り値は失敗理由。成功なら null。
        /// </summary>
        public static string PrepareForOutput(MeshObject mo, MeshContext output, WriteBack wb)
            => PrepareForOutput(mo, output, wb, checkWeights: true);

        /// <summary>
        /// checkWeights が false なら、スキンドの出力でもウェイトの欠けを失敗にしない
        /// （逆方向の試しの作り直しで形だけを比べるとき。段 6）。
        /// </summary>
        public static string PrepareForOutput(MeshObject mo, MeshContext output, WriteBack wb, bool checkWeights)
        {
            if (mo == null || output == null) return null;

            if (wb != null && ReferenceEquals(wb.Output, output))
                ApplyMatrix(mo, wb.Matrix);

            if (output.IsSkinned)
            {
                if (!checkWeights) return null;
                int missing = 0;
                foreach (var v in mo.Vertices)
                    if (v != null && v.BoneWeight == null) missing++;
                if (missing > 0)
                    return $"出力「{output.Name}」はスキンド化済みだが、作り直した頂点のうち {missing} 個にウェイトが無い"
                         + "（梯子のウェイトを引き継げなかった。そのまま書くと頂点が飛ぶ）";
            }
            else if (wb != null && ReferenceEquals(wb.Output, output))
            {
                foreach (var v in mo.Vertices)
                {
                    if (v == null) continue;
                    v.BoneWeight       = null;
                    v.MirrorBoneWeight = null;
                }
            }

            return null;
        }
    }
}

// PrimitiveMeshFactory.cs
// 図形生成コマンドから MeshObject を1つ作る。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【なぜ Runtime に1本置くか】
//   図形種別ごとの分岐は、これまで PlayerPrimitiveMeshSubPanel.Generate の private
//   スイッチにしか無かった。コマンド経由の生成を足すと同じ分岐が2箇所になる。
//   分岐をここへ集め、パネルのプレビューもディスパッチャもここを呼ぶ形にする。
//
// 【共通後処理の順序】
//   重複頂点の結合 → パーツID/サブIDの割当 → 回転・拡大の焼き込み
//   結合の許容値はローカル空間で評価させたいので、焼き込みは必ず結合の後に行う。
//   平行移動は焼き込まない（追加先ごとの扱いは呼出し側が持つ）。
//
// 【プレビュー】
//   forPreview のときは重複頂点の結合を飛ばす。結合は全頂点の二重ループなので、
//   頂点数の多い形状ではプレビュー再生成のたびに止まる。
//   また焼き込みは指定にかかわらず両方行う（見た目を変えないため）。
//
// 【藤壺（配置）だけ外部解決が要る理由】
//   配置元がモデル内の描画オブジェクトで、コマンドは索引しか持たない。
//   索引 → MeshObject の解決はモデルを持つ側（ディスパッチャ）に渡す。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Frill;
using Poly_Ling.Pipe;
using Poly_Ling.PlaceObject;
using Poly_Ling.Profile2DExtrude;
using Poly_Ling.Revolution;
using Poly_Ling.Ribbon;
using Poly_Ling.GlyphText;
using Poly_Ling.NohMask;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>
    /// 配置元の索引を MeshObject の列へ解決する。
    /// includeChildren が true のときは子孫も含め、ルートのローカル空間へ移して返す。
    /// </summary>
    public delegate List<MeshObject> PlaceSourceResolver(int[] masterIndices, bool includeChildren);

    /// <summary>図形生成コマンド → MeshObject。</summary>
    public static class PrimitiveMeshFactory
    {
        /// <summary>重複頂点の結合の許容値。パネルの既定と同じ。</summary>
        public const float MergeEpsilon = 0.001f;

        /// <summary>
        /// 直前の文字生成で、フォントに無くて置けなかった字数。
        ///
        /// 生成の成否には関わらない表示専用の値で、図形生成パネルの情報欄が読む。
        /// 文字以外の図形を作ったときは触らない（前回の値が残る）。
        /// 文字の生成のたびに上書きするので、読むのは Build の直後に限る。
        /// </summary>
        public static int LastTextMissingGlyphs { get; private set; }

        // ================================================================
        // 入口
        // ================================================================

        /// <summary>
        /// コマンドから MeshObject を作る。作れないときは null を返す。
        /// </summary>
        /// <param name="cmd">図形生成コマンド。</param>
        /// <param name="forPreview">
        /// プレビュー用。重複頂点の結合を飛ばし、回転・拡大は指定にかかわらず焼き込む。
        /// </param>
        /// <param name="resolvePlaceSources">
        /// 藤壺（配置）の配置元を解決する。それ以外の図形では使わない。
        /// </param>
        public static MeshObject Build(
            CreatePrimitiveMeshCommand cmd,
            bool forPreview = false,
            PlaceSourceResolver resolvePlaceSources = null)
        {
            if (cmd == null) return null;

            MeshObject mo = Generate(cmd, resolvePlaceSources);
            if (mo == null) return null;

            if (!forPreview && cmd.Placement.MergeDuplicateVertices && mo.VertexCount >= 2)
                MeshMergeHelper.MergeAllVerticesAtSamePosition(mo, MergeEpsilon);

            AssignPartsIds(mo, cmd.ShapeName);

            if (forPreview)
                PrimitiveMeshTransform.ApplyRotationScale(
                    mo, cmd.Placement.PlaceRotation, cmd.Placement.PlaceScale);
            else
                PrimitiveMeshTransform.ApplyRotationScale(
                    mo, cmd.BakedRotation, cmd.BakedScale);

            return mo;
        }

        // ================================================================
        // 図形ごとの生成
        // ================================================================

        private static MeshObject Generate(
            CreatePrimitiveMeshCommand cmd, PlaceSourceResolver resolvePlaceSources)
        {
            switch (cmd)
            {
                case CreateCubeCommand c:         return CubeMeshGenerator.Generate(c.Params);
                case CreateSphereCommand c:       return SphereMeshGenerator.Generate(c.Params);
                case CreateCylinderCommand c:     return CylinderMeshGenerator.Generate(c.Params);
                case CreateCapsuleCommand c:      return CapsuleMeshGenerator.Generate(c.Params);
                case CreatePlaneCommand c:        return PlaneMeshGenerator.Generate(c.Params);
                case CreatePyramidCommand c:      return PyramidMeshGenerator.Generate(c.Params);
                case CreateStadiumBoxCommand c:   return StadiumBoxMeshGenerator.Generate(c.Params);
                case CreatePipeStadiumCommand c:  return PipeStadiumMeshGenerator.Generate(c.Params);
                case CreateNGonGearCommand c:     return NGonGearMeshGenerator.Generate(c.Params);
                case CreateNGonStarCommand c:     return NGonStarMeshGenerator.Generate(c.Params);
                case CreateInvoluteGearCommand c: return InvoluteTrochoidGearMeshGenerator.Generate(c.Params);
                case CreateRibbonBowCommand c:    return RibbonBowMeshGenerator.Generate(c.Params);
                case CreateNohMaskCommand c:      return NohMaskMeshGenerator.GenerateFromFiles(c.Params);

                case CreateRevolutionCommand c:   return GenerateRevolution(c);
                case CreateProfile2DCommand c:    return GenerateProfile2D(c);
                case CreateTextMeshCommand c:     return GenerateText(c);

                case CreateFrillCommand c:        return GenerateFrill(c);
                case CreatePipeCommand c:         return GeneratePipe(c);
                case CreatePlaceObjectCommand c:  return GeneratePlaceObject(c, resolvePlaceSources);

                default:
                    Debug.LogWarning($"[PrimitiveMeshFactory] 未対応のコマンド: {cmd.GetType().Name}");
                    return null;
            }
        }

        // ── 回転体 ──────────────────────────────────────────────────

        private static MeshObject GenerateRevolution(CreateRevolutionCommand c)
        {
            var profile = c.Params.Profile != null
                ? new List<Vector2>(c.Params.Profile)
                : new List<Vector2>();
            return RevolutionMeshGenerator.Generate(profile, c.Params);
        }

        // ── 2D 押し出し ─────────────────────────────────────────────

        private static MeshObject GenerateProfile2D(CreateProfile2DCommand c)
        {
            var loops = new List<Loop>();
            if (c.Params.Loops != null)
                foreach (var ld in c.Params.Loops) loops.Add(ld.ToLoop());

            var mo = Profile2DExtrudeMeshGenerator.Generate(loops, c.Params.MeshName,
                new Profile2DGenerateParams
                {
                    Scale         = c.Params.Scale,
                    Offset        = c.Params.Offset,
                    FlipY         = c.Params.FlipY,
                    Thickness     = c.Params.Thickness,
                    SegmentsFront = c.Params.SegmentsFront,
                    SegmentsBack  = c.Params.SegmentsBack,
                    EdgeSizeFront = c.Params.EdgeSizeFront,
                    EdgeSizeBack  = c.Params.EdgeSizeBack,
                    EdgeInward    = c.Params.EdgeInward,
                    SymmetryMode  = c.Params.SymmetryMode,
                });

            PrimitiveMeshPostProcess.ApplyPivotOffset(mo, c.Params.Pivot);
            return mo;
        }

        // ── 文字 ────────────────────────────────────────────────────

        /// <summary>
        /// 見つからなかった字数はここでは持ち帰らない（パネルの情報欄はパネル側が更新する）。
        /// フォントが開けない・輪郭が0本のときは null を返す。
        /// </summary>
        private static MeshObject GenerateText(CreateTextMeshCommand c)
        {
            var font = PlyFontLibrary.Open(c.Params.FontFamily);
            if (font == null) { LastTextMissingGlyphs = 0; return null; }

            var loops = TextOutlineBuilder.Build(font, c.Params.Text ?? "",
                new TextLayoutParams
                {
                    Segment       = c.Params.Segment,
                    LetterSpacing = c.Params.LetterSpacing,
                    LineSpacing   = c.Params.LineSpacing,
                },
                out int missing);

            LastTextMissingGlyphs = missing;

            if (loops == null || loops.Count == 0) return null;

            return Profile2DExtrudeMeshGenerator.Generate(loops, c.Params.MeshName,
                new Profile2DGenerateParams
                {
                    Scale         = c.Params.Size,
                    Offset        = Vector2.zero,
                    FlipY         = false,
                    Thickness     = c.Params.Thickness,
                    SegmentsFront = c.Params.SegmentsFront,
                    SegmentsBack  = c.Params.SegmentsBack,
                    EdgeSizeFront = c.Params.EdgeSizeFront,
                    EdgeSizeBack  = c.Params.EdgeSizeBack,
                    EdgeInward    = c.Params.EdgeInward,
                    SymmetryMode  = false,
                });
        }

        // ── フリル ──────────────────────────────────────────────────

        private static MeshObject GenerateFrill(CreateFrillCommand c)
        {
            var profileA = ToList(c.ProfileA);
            var profileB = ToList(c.ProfileB);

            // 融合ありはレール行ごと、融合なしは梯子ごとにパーツIDを 0 から連番にする。
            var partsIds = new PartsIdCounter();

            if (c.Params.ConnectShared)
            {
                var inputs = new List<FrillBeltInput>();
                foreach (var belt in EachPreprocessedBelt(c))
                    inputs.Add(ToFrillInput(belt, c.Params.HeightScale, c.Params.ProfileFlip));

                var joined = FrillMeshGenerator.Generate(
                    inputs, profileA, profileB, c.Params.TwoProfiles,
                    true, c.Params.RungSeam, c.Params.MeshName, partsIds);

                var solid = BeltShapeOps.ApplySolidify(joined,
                    c.Params.Thickness, c.Params.SegmentsFront, c.Params.SegmentsBack,
                    c.Params.EdgeSizeFront, c.Params.EdgeSizeBack, c.Params.EdgeInward,
                    c.Params.MeshName);

                if (c.Params.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(solid);
                PrimitiveMeshPostProcess.ApplyPivotOffset(solid, c.Params.Pivot);
                return solid;
            }

            var single = new List<FrillBeltInput>(1) { null };
            var mo = new MeshObject(c.Params.MeshName);

            foreach (var belt in EachPreprocessedBelt(c))
            {
                single[0] = ToFrillInput(belt, c.Params.HeightScale, c.Params.ProfileFlip);

                var part = FrillMeshGenerator.Generate(
                    single, profileA, profileB, c.Params.TwoProfiles,
                    false, c.Params.RungSeam, c.Params.MeshName, partsIds);

                part = BeltShapeOps.ApplySolidify(part,
                    c.Params.Thickness, c.Params.SegmentsFront, c.Params.SegmentsBack,
                    c.Params.EdgeSizeFront, c.Params.EdgeSizeBack, c.Params.EdgeInward,
                    c.Params.MeshName);

                MeshObjectAppendOps.Append(mo, part);
            }

            if (c.Params.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(mo);
            PrimitiveMeshPostProcess.ApplyPivotOffset(mo, c.Params.Pivot);
            return mo;
        }

        /// <summary>
        /// 生成入力へ変換する。高さ倍率は「全体 × 梯子ごと」の掛け算で合成する。
        /// プロファイル補間パラメータは段番号から決める。
        /// N 段グループの段 r は左レール r/N・右レール (r+1)/N になり、
        /// 隣り合う段が共有するレールでは同じ値になる。
        /// </summary>
        private static FrillBeltInput ToFrillInput(BeltCsvEntry b, float globalHeightScale, bool flip)
        {
            int n = Mathf.Max(1, b.RowCount);
            int r = Mathf.Clamp(b.RowIndex, 0, n - 1);

            float t0 = (float)r / n;
            float t1 = (float)(r + 1) / n;

            if (flip) { t0 = 1f - t0; t1 = 1f - t1; }

            return new FrillBeltInput
            {
                Left        = b.Left,
                Right       = b.Right,
                Closed      = b.Closed,
                FlipWinding = b.FlipWinding,
                HeightScale = globalHeightScale * b.HeightScale,
                TLeft       = t0,
                TRight      = t1,
            };
        }

        // ── パイプ ──────────────────────────────────────────────────

        private static MeshObject GeneratePipe(CreatePipeCommand c)
        {
            var profile = ToList(c.Profile);
            var mo = new MeshObject(c.Params.MeshName);

            // 梯子1本＝パーツ1つ。全ベルトで同じカウンタを共有し、0 から連番にする。
            var partsIds = new PartsIdCounter();

            foreach (var b in EachPreprocessedBelt(c))
            {
                var part = PipeMeshGenerator.Generate(
                    b.Left, b.Right, b.Closed, b.FlipWinding,
                    profile, c.ProfileClosed, c.Params.CapEnds,
                    b.StartPoint, b.EndPoint,
                    c.Params.MeshName, partsIds);

                part = BeltShapeOps.ApplySolidify(part,
                    c.Params.Thickness, c.Params.SegmentsFront, c.Params.SegmentsBack,
                    c.Params.EdgeSizeFront, c.Params.EdgeSizeBack, c.Params.EdgeInward,
                    c.Params.MeshName);

                MeshObjectAppendOps.Append(mo, part);
            }

            if (c.Params.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(mo);
            PrimitiveMeshPostProcess.ApplyPivotOffset(mo, c.Params.Pivot);
            return mo;
        }

        // ── 藤壺（配置） ────────────────────────────────────────────

        /// <summary>
        /// 各基準ベルトの rung 中心へ配置元オブジェクトを複製する。
        /// 間引きは段（RowIndex）→ rung（rung 番号）の順に効かせ、
        /// 置かない rung は割り当てを進めない。
        ///
        /// 向き補正は段番号を反転させるので、段間引きは補正後の番号で判定する。
        /// スプライン分割は rung 数を変えるだけなので、段を通した後に掛ける。
        /// </summary>
        private static MeshObject GeneratePlaceObject(
            CreatePlaceObjectCommand c, PlaceSourceResolver resolvePlaceSources)
        {
            var mo = new MeshObject(c.Params.MeshName);

            if (resolvePlaceSources == null) return mo;
            var srcs = resolvePlaceSources(c.SourceMasterIndices, c.Params.IncludeChildren);
            if (srcs == null || srcs.Count == 0) return mo;

            float userScale = c.Params.Scale <= 0f ? 1f : c.Params.Scale;

            // Combine は全 rung 共通の1メッシュ。連結は生成ごとに1回だけ行う。
            MeshObject combined = (c.Params.Mode == PlaceSourceMode.Combine)
                ? MeshObjectAppendOps.Combine(srcs, c.Params.MeshName)
                : null;

            // Random は生成開始時に1個だけ作り、ベルト→rung の固定順で引く。
            // 同じシード・同じ入力なら同一結果になる。
            var rnd = (c.Params.Mode == PlaceSourceMode.Random)
                ? new System.Random(c.Params.RandomSeed)
                : null;

            // Sequence の巡回位置はベルトをまたいで連続させる。
            int seqIndex = 0;

            // 配置1インスタンス＝パーツ1つ。ベルトをまたいで 0 から連番にする。
            var partsIds = new PartsIdCounter();

            // 間引き。間隔は 1 以上へ、開始位置は間隔で割った余りへ丸める。
            int rungStride = Mathf.Max(1, c.Params.RungStride);
            int rungOffset = ((c.Params.RungOffset % rungStride) + rungStride) % rungStride;
            int rowStride  = Mathf.Max(1, c.Params.RowStride);
            int rowOffset  = ((c.Params.RowOffset % rowStride) + rowStride) % rowStride;

            if (c.Belts == null) return mo;

            foreach (var raw in c.Belts)
            {
                if (raw == null || !raw.HasData) continue;

                var oriented = BeltShapeOps.ApplyOrient(raw, c.Orient);

                int row = Mathf.Max(0, oriented.RowIndex);
                if ((row % rowStride) != rowOffset) continue;

                var b = BeltShapeOps.ApplySpline(oriented, c.Spline);

                int n = Mathf.Min(b.Left.Count, b.Right.Count);
                var perRung = new MeshObject[n];
                for (int i = 0; i < n; i++)
                {
                    // rung 間引き。置かない rung は null のままにする
                    // （PlaceObjectMeshGenerator は null の rung を飛ばす）。
                    // 巡回位置・抽選は実際に置く rung だけで進める。
                    if ((i % rungStride) != rungOffset) continue;

                    switch (c.Params.Mode)
                    {
                        case PlaceSourceMode.Sequence:
                            perRung[i] = srcs[seqIndex];
                            seqIndex   = (seqIndex + 1) % srcs.Count;
                            break;
                        case PlaceSourceMode.Random:
                            perRung[i] = srcs[rnd.Next(srcs.Count)];
                            break;
                        default:
                            perRung[i] = combined;
                            break;
                    }
                }

                var part = PlaceObjectMeshGenerator.Generate(
                    b.Left, b.Right, b.Closed, b.FlipWinding,
                    perRung, c.Params.MeshName, userScale, c.Params.RollSteps, c.Params.ScaleMode,
                    partsIds);

                MeshObjectAppendOps.Append(mo, part);
            }

            return mo;
        }

        // ================================================================
        // 共通
        // ================================================================

        /// <summary>向き補正とスプライン分割を掛けた基準ベルトを順に返す。</summary>
        private static IEnumerable<BeltCsvEntry> EachPreprocessedBelt(CreateBeltPrimitiveCommand c)
        {
            if (c.Belts == null) yield break;
            foreach (var raw in c.Belts)
            {
                if (raw == null || !raw.HasData) continue;
                var b = BeltShapeOps.Preprocess(raw, c.Orient, c.Spline);
                if (b == null || !b.HasData) continue;
                yield return b;
            }
        }

        /// <summary>
        /// パーツID / サブIDを割り当てる。
        ///
        /// 厚み付けと重複頂点の結合で頂点数が変わるため、確定した頂点列に対して呼ぶこと。
        ///
        /// フリル／パイプは生成器が複数パーツへ分けているので、パーツIDはそのまま使い
        /// サブIDだけを振り直す。藤壺は配置元のサブIDをそのまま使うので何もしない。
        /// それ以外の図形は「1つのパーツを作った」扱いで、パーツID 0 とサブID 0.. を振る。
        ///
        /// 既存オブジェクトへ追加するときの番号のずらしは追加側（Viewer）が行う。
        /// </summary>
        public static void AssignPartsIds(MeshObject mo, string shapeName)
        {
            if (mo == null || mo.VertexCount == 0) return;

            switch (shapeName)
            {
                case "PlaceObject":
                    return;

                case "Frill":
                case "Pipe":
                    PartsIdOps.AssignSubIdByPartsId(mo);
                    return;

                default:
                    PartsIdOps.SetPartsId(mo, 0);
                    PartsIdOps.AssignSubIdByPartsId(mo);
                    return;
            }
        }

        private static List<Vector2> ToList(Vector2[] src)
            => src != null ? new List<Vector2>(src) : new List<Vector2>();
    }
}

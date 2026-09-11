// PanelCommand.FileIO.cs
// ファイル入出力（PMX / MQO / OBJ / VRM・プロジェクト）の操作要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間。PanelCommand.cs から分割）

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Ops;
using Poly_Ling.Tools.SpringBoneRig;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Data
{
    // ================================================================
    // ファイル入出力（P8）
    //
    // 【名前】
    //   Poly_Ling.Commands 側に ImportMqoCommand / ImportObjCommand（ICommand）が
    //   既にある。あちらはコールバックを受け取る内部用なので、外から送るものは
    //   〜FileCommand と別名にする（ImportPmxFileCommand と同じ規則）。
    //
    // 【関門】
    //   受け口が PLSandbox を通す。出力は TryResolveWrite、入力は TryResolveRead。
    //   Ignore を付けた入力パスと List<string> はコマンドが持ち、受け口が詰め替える。
    // ================================================================

    /// <summary>
    /// PMX ファイルを書き出す。
    ///
    /// ReplaceMaterialNames と SourcePmxPath は PMXExportSettings 側で Ignore に
    /// してあるものの受け皿。前者は List、後者は入力パスで、どちらも設定へ
    /// 直接載せられない。
    /// </summary>
    [PLCommand(Description = "現在のモデルを PMX ファイルへ書き出す。作業フォルダの下だけへ書ける。")]
    [PLResult("requestedPath", PLResultKind.Text,    Description = "指定された経路")]
    [PLResult("resolved",      PLResultKind.Flag,    Description = "作業フォルダの関門を通ったか")]
    [PLResult("path",          PLResultKind.Text,    Description = "実際に書いた経路", Optional = true)]
    [PLResult("exists",        PLResultKind.Flag,    Description = "書き出し先が実在するか")]
    [PLResult("files",         PLResultKind.Integer, Description = "数えたファイルの数")]
    [PLResult("bytes",         PLResultKind.Text,    Description = "大きさの合計。10 進の文字列")]
    public class ExportPmxFileCommand : PanelCommand
    {
        [PLParam(Description = "書き出し先のパス。作業フォルダからの相対でも絶対でもよい",
                 Required = true)]
        public string FilePath { get; }

        [PLParam(Description = "書き出し設定。省いた項目は既定値のまま")]
        public Poly_Ling.PMX.PMXExportSettings Settings { get; }

        [PLParam(Description = "部分差し替えで対象にする材質名。空にすると全材質")]
        public string[] ReplaceMaterialNames { get; }

        [PLParam(Description = "部分差し替えの元 PMX のパス。空にすると通常の書き出し")]
        public string SourcePmxPath { get; }

        public ExportPmxFileCommand(
            int modelIndex,
            string filePath,
            Poly_Ling.PMX.PMXExportSettings settings = null,
            string[] replaceMaterialNames = null,
            string sourcePmxPath = "")
            : base(modelIndex)
        {
            FilePath             = filePath ?? "";
            Settings             = settings ?? Poly_Ling.PMX.PMXExportSettings.CreateFullExport();
            ReplaceMaterialNames = replaceMaterialNames ?? System.Array.Empty<string>();
            SourcePmxPath        = sourcePmxPath ?? "";
        }
    }

    /// <summary>
    /// MQO ファイルを読み込む。
    ///
    /// BoneWeightCsvPath / BoneCsvPath は MQOImportSettings 側で Ignore に
    /// してある入力パスの受け皿。BaseDir は読み込み側が実ファイルの位置から
    /// 決めるので持たない。
    /// </summary>
    [PLCommand(Description = "MQO ファイルを読み込む。作業フォルダの下だけを読める。")]
    public class ImportMqoFileCommand : PanelCommand
    {
        [PLParam(Description = "読み込む MQO のパス。作業フォルダからの相対でも絶対でもよい",
                 Required = true)]
        public string FilePath { get; }

        [PLParam(Description = "読み込み設定。省いた項目は既定値のまま")]
        public Poly_Ling.MQO.MQOImportSettings Settings { get; }

        [PLParam(Description = "ボーンウェイト CSV のパス。空にすると使わない")]
        public string BoneWeightCsvPath { get; }

        [PLParam(Description = "ボーン定義 CSV のパス。空にすると使わない")]
        public string BoneCsvPath { get; }

        [PLParam(Description = "読込後にボーン名から Humanoid の割当を自動で行う")]
        public bool HumanoidAutoMap { get; }

        [PLParam(Description = "読込後に原点 CSV を適用する")]
        public bool ApplyOriginCsv { get; }

        [PLParam(Description = "適用する原点 CSV のパス。ApplyOriginCsv が false のときは使わない")]
        public string OriginCsvPath { get; }

        [PLParam(Description = "原点 CSV の回転列（rotX,rotY,rotZ）も適用する")]
        public bool OriginCsvIncludeRotation { get; }

        public ImportMqoFileCommand(
            int modelIndex,
            string filePath,
            Poly_Ling.MQO.MQOImportSettings settings = null,
            string boneWeightCsvPath = "",
            string boneCsvPath = "",
            bool humanoidAutoMap = false,
            bool applyOriginCsv = false,
            string originCsvPath = "",
            bool originCsvIncludeRotation = false)
            : base(modelIndex)
        {
            FilePath                 = filePath ?? "";
            Settings                 = settings ?? Poly_Ling.MQO.MQOImportSettings.CreateDefault();
            BoneWeightCsvPath        = boneWeightCsvPath ?? "";
            BoneCsvPath              = boneCsvPath ?? "";
            HumanoidAutoMap          = humanoidAutoMap;
            ApplyOriginCsv           = applyOriginCsv;
            OriginCsvPath            = originCsvPath ?? "";
            OriginCsvIncludeRotation = originCsvIncludeRotation;
        }
    }

    /// <summary>MQO ファイルを書き出す。</summary>
    [PLCommand(Description = "現在のモデルを MQO ファイルへ書き出す。作業フォルダの下だけへ書ける。")]
    [PLResult("requestedPath", PLResultKind.Text,    Description = "指定された経路")]
    [PLResult("resolved",      PLResultKind.Flag,    Description = "作業フォルダの関門を通ったか")]
    [PLResult("path",          PLResultKind.Text,    Description = "実際に書いた経路", Optional = true)]
    [PLResult("exists",        PLResultKind.Flag,    Description = "書き出し先が実在するか")]
    [PLResult("files",         PLResultKind.Integer, Description = "数えたファイルの数")]
    [PLResult("bytes",         PLResultKind.Text,    Description = "大きさの合計。10 進の文字列")]
    public class ExportMqoFileCommand : PanelCommand
    {
        [PLParam(Description = "書き出し先のパス。作業フォルダからの相対でも絶対でもよい",
                 Required = true)]
        public string FilePath { get; }

        [PLParam(Description = "書き出し設定。省いた項目は既定値のまま")]
        public Poly_Ling.MQO.MQOExportSettings Settings { get; }

        public ExportMqoFileCommand(
            int modelIndex,
            string filePath,
            Poly_Ling.MQO.MQOExportSettings settings = null)
            : base(modelIndex)
        {
            FilePath = filePath ?? "";
            // パネルの既定と同じ。MQO⇔Unity は X のみ反転（AxisFlip.MqoToUnity）。
            Settings = settings ?? Poly_Ling.MQO.MQOExportSettings.CreateFromCoordinate(
                0.01f, flipZ: false, flipX: true);
        }
    }

    /// <summary>
    /// OBJ ファイルを読み込む。
    /// BaseDir は読み込み側が実ファイルの位置から決めるので持たない。
    /// </summary>
    [PLCommand(Description = "OBJ ファイルを読み込む。作業フォルダの下だけを読める。")]
    public class ImportObjFileCommand : PanelCommand
    {
        [PLParam(Description = "読み込む OBJ のパス。作業フォルダからの相対でも絶対でもよい",
                 Required = true)]
        public string FilePath { get; }

        [PLParam(Description = "読み込み設定。省いた項目は既定値のまま")]
        public Poly_Ling.OBJ.ObjImportSettings Settings { get; }

        [PLParam(Description = "読込後にボーン名から Humanoid の割当を自動で行う")]
        public bool HumanoidAutoMap { get; }

        [PLParam(Description = "読込後に原点 CSV を適用する")]
        public bool ApplyOriginCsv { get; }

        [PLParam(Description = "適用する原点 CSV のパス。ApplyOriginCsv が false のときは使わない")]
        public string OriginCsvPath { get; }

        [PLParam(Description = "原点 CSV の回転列（rotX,rotY,rotZ）も適用する")]
        public bool OriginCsvIncludeRotation { get; }

        public ImportObjFileCommand(
            int modelIndex,
            string filePath,
            Poly_Ling.OBJ.ObjImportSettings settings = null,
            bool humanoidAutoMap = false,
            bool applyOriginCsv = false,
            string originCsvPath = "",
            bool originCsvIncludeRotation = false)
            : base(modelIndex)
        {
            FilePath                 = filePath ?? "";
            Settings                 = settings ?? Poly_Ling.OBJ.ObjImportSettings.CreateDefault();
            HumanoidAutoMap          = humanoidAutoMap;
            ApplyOriginCsv           = applyOriginCsv;
            OriginCsvPath            = originCsvPath ?? "";
            OriginCsvIncludeRotation = originCsvIncludeRotation;
        }
    }

    /// <summary>OBJ ファイルを書き出す。材質を出すときは同名の .mtl も隣に作られる。</summary>
    [PLCommand(Description = "現在のモデルを OBJ ファイルへ書き出す。作業フォルダの下だけへ書ける。")]
    [PLResult("requestedPath", PLResultKind.Text,    Description = "指定された経路")]
    [PLResult("resolved",      PLResultKind.Flag,    Description = "作業フォルダの関門を通ったか")]
    [PLResult("path",          PLResultKind.Text,    Description = "実際に書いた経路", Optional = true)]
    [PLResult("exists",        PLResultKind.Flag,    Description = "書き出し先が実在するか")]
    [PLResult("files",         PLResultKind.Integer, Description = "数えたファイルの数")]
    [PLResult("bytes",         PLResultKind.Text,    Description = "大きさの合計。10 進の文字列")]
    public class ExportObjFileCommand : PanelCommand
    {
        [PLParam(Description = "書き出し先のパス。作業フォルダからの相対でも絶対でもよい",
                 Required = true)]
        public string FilePath { get; }

        [PLParam(Description = "書き出し設定。省いた項目は既定値のまま")]
        public Poly_Ling.OBJ.ObjExportSettings Settings { get; }

        public ExportObjFileCommand(
            int modelIndex,
            string filePath,
            Poly_Ling.OBJ.ObjExportSettings settings = null)
            : base(modelIndex)
        {
            FilePath = filePath ?? "";
            Settings = settings ?? Poly_Ling.OBJ.ObjExportSettings.CreateDefault();
        }
    }

    /// <summary>
    /// VRM 1.0 ファイルを書き出す。
    /// Authors は Vrm10ExportSettings 側で Ignore にしてある List の受け皿。
    /// </summary>
    [PLCommand(Description = "現在のモデルを VRM 1.0 ファイルへ書き出す。作業フォルダの下だけへ書ける。")]
    [PLResult("requestedPath", PLResultKind.Text,    Description = "指定された経路")]
    [PLResult("resolved",      PLResultKind.Flag,    Description = "作業フォルダの関門を通ったか")]
    [PLResult("path",          PLResultKind.Text,    Description = "実際に書いた経路", Optional = true)]
    [PLResult("exists",        PLResultKind.Flag,    Description = "書き出し先が実在するか")]
    [PLResult("files",         PLResultKind.Integer, Description = "数えたファイルの数")]
    [PLResult("bytes",         PLResultKind.Text,    Description = "大きさの合計。10 進の文字列")]
    public class ExportVrmFileCommand : PanelCommand
    {
        [PLParam(Description = "書き出し先のパス。作業フォルダからの相対でも絶対でもよい",
                 Required = true)]
        public string FilePath { get; }

        [PLParam(Description = "書き出し設定。省いた項目は既定値のまま")]
        public Poly_Ling.Vrm.Vrm10ExportSettings Settings { get; }

        [PLParam(Description = "作者名（VRM Meta の authors）。空にするとモデル側の値を使う")]
        public string[] Authors { get; }

        public ExportVrmFileCommand(
            int modelIndex,
            string filePath,
            Poly_Ling.Vrm.Vrm10ExportSettings settings = null,
            string[] authors = null)
            : base(modelIndex)
        {
            FilePath = filePath ?? "";
            Settings = settings ?? Poly_Ling.Vrm.Vrm10ExportSettings.CreateDefault();
            Authors  = authors ?? System.Array.Empty<string>();
        }
    }

    /// <summary>プロジェクトを .mfproj（JSON）へ保存する。</summary>
    [PLCommand(Description = "プロジェクトを .mfproj ファイルへ保存する。作業フォルダの下だけへ書ける。")]
    [PLResult("requestedPath", PLResultKind.Text,    Description = "指定された経路")]
    [PLResult("resolved",      PLResultKind.Flag,    Description = "作業フォルダの関門を通ったか")]
    [PLResult("path",          PLResultKind.Text,    Description = "実際に書いた経路", Optional = true)]
    [PLResult("exists",        PLResultKind.Flag,    Description = "書き出し先が実在するか")]
    [PLResult("files",         PLResultKind.Integer, Description = "数えたファイルの数")]
    [PLResult("bytes",         PLResultKind.Text,    Description = "大きさの合計。10 進の文字列")]
    public class SaveProjectFileCommand : PanelCommand
    {
        [PLParam(Description = "保存先の .mfproj のパス。作業フォルダからの相対でも絶対でもよい",
                 Required = true)]
        public string FilePath { get; }

        public SaveProjectFileCommand(int modelIndex, string filePath)
            : base(modelIndex)
        {
            FilePath = filePath ?? "";
        }
    }

    /// <summary>
    /// .mfproj（JSON）からプロジェクトを読み込む。
    /// 編集中のプロジェクトは置き換わる。
    /// </summary>
    [PLCommand(Description = ".mfproj ファイルからプロジェクトを読み込む。編集中のプロジェクトは置き換わる。")]
    public class LoadProjectFileCommand : PanelCommand
    {
        [PLParam(Description = "読み込む .mfproj のパス。作業フォルダからの相対でも絶対でもよい",
                 Required = true)]
        public string FilePath { get; }

        public LoadProjectFileCommand(int modelIndex, string filePath)
            : base(modelIndex)
        {
            FilePath = filePath ?? "";
        }
    }

    /// <summary>
    /// プロジェクトを CSV へ保存する。
    /// FilePath はプロジェクトファイル（任意名の .csv）で、モデルフォルダは
    /// 同じディレクトリ直下に作られる。
    /// </summary>
    [PLCommand(Description = "プロジェクトを CSV へ保存する。モデルフォルダは同じディレクトリ直下に作られる。")]
    [PLResult("requestedPath", PLResultKind.Text,    Description = "指定された経路")]
    [PLResult("resolved",      PLResultKind.Flag,    Description = "作業フォルダの関門を通ったか")]
    [PLResult("path",          PLResultKind.Text,    Description = "実際に書いたプロジェクト CSV の経路。モデルフォルダは同じディレクトリ直下に別途できる", Optional = true)]
    [PLResult("exists",        PLResultKind.Flag,    Description = "書き出し先が実在するか")]
    [PLResult("files",         PLResultKind.Integer, Description = "数えたファイルの数。プロジェクト CSV の 1 本だけを数える")]
    [PLResult("bytes",         PLResultKind.Text,    Description = "プロジェクト CSV の大きさ。10 進の文字列")]
    public class SaveProjectCsvCommand : PanelCommand
    {
        [PLParam(Description = "保存先のプロジェクト CSV のパス。作業フォルダからの相対でも絶対でもよい",
                 Required = true)]
        public string FilePath { get; }

        public SaveProjectCsvCommand(int modelIndex, string filePath)
            : base(modelIndex)
        {
            FilePath = filePath ?? "";
        }
    }

    /// <summary>
    /// CSV からプロジェクトを読み込む。
    /// Merge を立てると、指定ファイルと同じフォルダのメッシュを現在の
    /// プロジェクトへ足す（名前が重なるものは置き換える）。
    /// </summary>
    [PLCommand(Description = "CSV からプロジェクトを読み込む。追加マージにすると現在のプロジェクトへ足す。")]
    public class LoadProjectCsvCommand : PanelCommand
    {
        [PLParam(Description = "読み込むプロジェクト CSV のパス。作業フォルダからの相対でも絶対でもよい",
                 Required = true)]
        public string FilePath { get; }

        [PLParam(Description = "現在のプロジェクトへ足す。false にすると置き換える")]
        public bool Merge { get; }

        public LoadProjectCsvCommand(int modelIndex, string filePath, bool merge = false)
            : base(modelIndex)
        {
            FilePath = filePath ?? "";
            Merge    = merge;
        }
    }

    /// <summary>
    /// 2 本のボーンが決める平面へ選択頂点を寄せる。実処理は PlanarizeAlongBonesTool。
    ///
    /// 実処理が編集対象メッシュ 1 本にしか効かない（PlanarizeAlongBonesTool.cs:140）ため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    ///
    /// BoneIndexA / BoneIndexB は BoneNames の並び（ツールが組むボーン一覧）の索引で、
    /// MeshContextList の索引ではない。
    /// </summary>
    [PLCommand(Description = "2 本のボーンが決める平面へ選択頂点を寄せる。")]
    public class PlanarizeAlongBonesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "PlanarizeBoneA",
                 Description = "基準ボーン A。ツールのボーン一覧内の索引", Required = true)]
        public int                BoneIndexA { get; }

        [PLParam(TextKey = "PlanarizeBoneB",
                 Description = "基準ボーン B。ツールのボーン一覧内の索引。A と別であること", Required = true)]
        public int                BoneIndexB { get; }

        [PLParam(TextKey = "PlanarizePlaneMode",
                 Description = "平面の置き方。MinMovement / AnchorToA")]
        public PlanePlacementMode PlaneMode  { get; }

        [PLParam(TextKey = "PlanarizeBlend",
                 Description = "寄せる度合い。0 = 動かさない、1 = 完全に平面へ。既定は 1",
                 Min = 0.0, Max = 1.0)]
        public float              Blend      { get; }

        public PlanarizeAlongBonesCommand(
            int modelIndex, int[] masterIndices,
            int boneIndexA, int boneIndexB,
            PlanePlacementMode planeMode = PlanePlacementMode.MinMovement,
            float blend                  = 1f,
            ulong[] objectIds            = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            BoneIndexA    = boneIndexA;
            BoneIndexB    = boneIndexB;
            PlaneMode     = planeMode;
            Blend         = blend;
        }
    }

    /// <summary>
    /// 選択頂点を結合する。実処理は MergeVerticesTool。
    ///
    /// 実処理が編集対象メッシュ 1 本にしか効かない（MergeVerticesTool.cs:119）ため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    /// </summary>
    [PLCommand(Description = "選択頂点を結合する。")]
    public class MergeVerticesCommand : PanelCommand
    {
        /// <summary>結合の仕方。</summary>
        public enum MergeMode
        {
            /// <summary>距離を見ず、選択頂点を 1 点（重心）へ寄せる。</summary>
            Centroid,
            /// <summary>しきい値以下の距離にある頂点どうしだけを結合する。</summary>
            Threshold
        }

        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "MergeVerticesMode",
                 Description = "結合の仕方。Centroid / Threshold", Required = true)]
        public MergeMode Mode      { get; }

        /// <summary>Threshold モードの距離しきい値。Centroid では読まれない。</summary>
        [PLParam(TextKey = "MergeVerticesThreshold",
                 Description = "Threshold モードの距離しきい値。既定は 0.001",
                 Min = 0.0001)]
        public float     Threshold { get; }

        public MergeVerticesCommand(
            int modelIndex, int[] masterIndices,
            MergeMode mode,
            float threshold   = 0.001f,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            Mode          = mode;
            Threshold     = threshold;
        }
    }
}

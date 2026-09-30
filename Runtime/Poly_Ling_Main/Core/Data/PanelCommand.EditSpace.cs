// PanelCommand.EditSpace.cs
// 作業空間（ビルボード上の 2D 編集）の開始・反映・取消し・終了・ロック切替の要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【実体は Viewer】
//   作業空間の状態（開いている作業空間、退避したビュー）は Viewer が持つので、
//   受け口は PolyLingPlayerViewerCore.EditSpace.cs にある。
//   作業空間は同時に 1 つだけ開ける。
//
// 【戻り値】
//   どのコマンドも、実行後の作業空間の状態（kind / sourceMasterIndex / proxyMasterIndex /
//   locked / hasChanges）を返す。閉じた後は isOpen = false だけ。

using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>UV の作業空間を開く。</summary>
    [PLCommand(Category = "uv", Writes = PLWriteScope.AddOnly, Effects = PLCommandEffect.CreatesObject,
        Description = "描画オブジェクトの UV を作業空間で開く。UV を XY（Z=0）に並べた代理メッシュを作り、基準ビューへ正対させる（ビルボード）。代理は頂点移動などの既存ツールで編集し、applyEditSpace で元の UV へ書き戻す。faceIndices を空にすると全ての面、materialIndex を負にするとマテリアルで絞らない。作業空間は同時に 1 つだけ開ける。")]
    [PLResult("isOpen",            PLResultKind.Flag,    Description = "作業空間が開いているか")]
    [PLResult("kind",              PLResultKind.Text,    Description = "作業空間の種類（uv）")]
    [PLResult("sourceMasterIndex", PLResultKind.Integer, Description = "元の描画オブジェクトの masterIndex")]
    [PLResult("proxyMasterIndex",  PLResultKind.Integer, Description = "代理メッシュの masterIndex")]
    [PLResult("locked",            PLResultKind.Flag,    Description = "ビルボードをロック中か")]
    [PLResult("hasChanges",        PLResultKind.Flag,    Description = "未反映の変更があるか")]
    [PLResult("faces",             PLResultKind.Integer, Description = "対象の面の数")]
    public sealed class OpenUvEditSpaceCommand : PanelCommand
    {
        [PLParam(IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "UV を編集する描画オブジェクトの masterIndex", Required = true)]
        public int MasterIndex { get; }

        [PLParam(Description = "対象にする面の番号。空なら全ての面（3 頂点未満の面は常に対象外）")]
        public int[] FaceIndices { get; }

        [PLParam(Description = "対象にするマテリアルの番号。負ならマテリアルで絞らない")]
        public int MaterialIndex { get; }

        [PLParam(Description = "作業倍率。代理頂点 = (倍率 × U, 倍率 × V, 0)", Min = 0.001)]
        public float UvScale { get; }

        [PLParam(Description = "下絵のテクスチャの縦横比で表示する。代理頂点 = (倍率 × U, 倍率 × 高さ ÷ 幅 × V, 0)。既定は false（UV 0〜1 を正方形で表示）")]
        public bool UseImageRatio { get; }

        public OpenUvEditSpaceCommand(int modelIndex, int masterIndex, int[] faceIndices = null,
                                      int materialIndex = -1, float uvScale = 10f, bool useImageRatio = false)
            : base(modelIndex)
        {
            MasterIndex   = masterIndex;
            FaceIndices   = faceIndices ?? new int[0];
            MaterialIndex = materialIndex;
            UvScale       = uvScale;
            UseImageRatio = useImageRatio;
        }
    }

    /// <summary>断面（回転体・2D押し出し・フリル・パイプ）の作業空間を開く。</summary>
    [PLCommand(Category = "geometry.create.profile", Writes = PLWriteScope.ModelWide,
        Description = "断面の線分オブジェクト（図形生成パネルの「反映（→メッシュ）」で作ったもの）を代理として作業空間を開く。代理を基準ビューへ正対させ（ビルボード）、頂点移動や billboardProfile ツールで編集し、applyEditSpace で図形生成パネルの断面へ取り込む（今の「取り込み」と同じ読み方。フリル・パイプは単位長へ正規化）。代理はモデルに残る常設のオブジェクトなので、作業中の編集は通常の Undo に残り、閉じても消えない。平面制約は既定でオン（setEditSpacePlaneConstraint で外せる）。作業空間は同時に 1 つだけ開ける。")]
    [PLResult("isOpen",            PLResultKind.Flag,    Description = "作業空間が開いているか")]
    [PLResult("kind",              PLResultKind.Text,    Description = "作業空間の種類（revolution / profile2d / frillA / frillB / pipe）")]
    [PLResult("sourceMasterIndex", PLResultKind.Integer, Description = "元の描画オブジェクトの masterIndex（断面は元データがパネルにあるので -1）")]
    [PLResult("proxyMasterIndex",  PLResultKind.Integer, Description = "代理（線分オブジェクト）の masterIndex")]
    [PLResult("locked",            PLResultKind.Flag,    Description = "ビルボードをロック中か")]
    [PLResult("hasChanges",        PLResultKind.Flag,    Description = "未反映の変更があるか（代理から読んだ断面がパネルの断面と違うか）")]
    [PLResult("faces",             PLResultKind.Integer, Description = "代理の線分の数")]
    public sealed class OpenProfileEditSpaceCommand : PanelCommand
    {
        [PLParam(IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "代理にする線分オブジェクトの masterIndex", Required = true)]
        public int MasterIndex { get; }

        [PLParam(Description = "断面の種類。revolution / profile2d / frillA / frillB / pipe", Required = true)]
        public string Kind { get; }

        [PLParam(Description = "断面を持つ図形生成パネル。primitive（図形生成）/ live（新しい図形生成・MCP サンドボックス）。既定は primitive")]
        public string Panel { get; }

        public OpenProfileEditSpaceCommand(int modelIndex, int masterIndex, string kind, string panel = "primitive")
            : base(modelIndex)
        {
            MasterIndex = masterIndex;
            Kind        = kind ?? "";
            Panel       = string.IsNullOrEmpty(panel) ? "primitive" : panel;
        }
    }

    /// <summary>作業空間の平面制約を切り替える。</summary>
    [PLCommand(Category = "uv", Writes = PLWriteScope.None,
        Description = "開いている作業空間の平面制約を切り替える。制約中は移動・回転・拡大縮小を代理のローカル XY 面の中に限る。UV は制約を外せない（失敗を返す）。断面系は既定でオンで、外すと 3D の点を扱える。")]
    [PLResult("isOpen",            PLResultKind.Flag,    Description = "作業空間が開いているか")]
    [PLResult("kind",              PLResultKind.Text,    Description = "作業空間の種類")]
    [PLResult("sourceMasterIndex", PLResultKind.Integer, Description = "元の描画オブジェクトの masterIndex（無ければ -1）")]
    [PLResult("proxyMasterIndex",  PLResultKind.Integer, Description = "代理の masterIndex")]
    [PLResult("locked",            PLResultKind.Flag,    Description = "ビルボードをロック中か")]
    [PLResult("hasChanges",        PLResultKind.Flag,    Description = "未反映の変更があるか")]
    [PLResult("faces",             PLResultKind.Integer, Description = "対象の面（断面系は線分）の数")]
    [PLResult("planeConstrained",  PLResultKind.Flag,    Description = "平面制約が効いているか")]
    public sealed class SetEditSpacePlaneConstraintCommand : PanelCommand
    {
        [PLParam(Description = "true で平面制約をオン、false でオフ", Required = true)]
        public bool Enabled { get; }

        public SetEditSpacePlaneConstraintCommand(int modelIndex, bool enabled)
            : base(modelIndex) { Enabled = enabled; }
    }

    /// <summary>断面の作業空間の下絵（作業板スロット）を置く。</summary>
    [PLCommand(Category = "underlay", Writes = PLWriteScope.None,
        Description = "開いている断面の作業空間の代理に、参考画像（作業板スロット）を固定する。2 隅は代理のローカル XY 座標なので、代理を動かしても回しても断面との位置関係が変わらない。作業ビューでビルボードをロックしている間だけ表示する。モデルと一緒に保存する（JSON・CSV）。代理は安定 ID で指すので、オブジェクトの追加・削除で別の対象を指さない。Undo には入らない。UV の作業空間では使えない。")]
    [PLResult("objectId",  PLResultKind.Text,    Description = "固定した代理の安定 ID（10 進）")]
    [PLResult("filePath",  PLResultKind.Text,    Description = "画像ファイルの絶対パス")]
    [PLResult("corner0",   PLResultKind.Text,    Description = "画像の 1 隅（代理のローカル XY）")]
    [PLResult("corner1",   PLResultKind.Text,    Description = "向かい合う隅（代理のローカル XY）")]
    [PLResult("width",     PLResultKind.Integer, Description = "画像の幅（画素）")]
    [PLResult("height",    PLResultKind.Integer, Description = "画像の高さ（画素）")]
    public sealed class SetEditSpacePlateUnderlayCommand : PanelCommand
    {
        [PLParam(Description = "画像ファイルのパス（png / jpg / tga / bmp）。作業フォルダの下だけを読める。空にすると今の画像のまま置き方・表示調整だけ変える")]
        public string FilePath { get; }

        [PLParam(Description = "置き方の引数を使わず、今の置き方を保つ。まだ置いていなければ既定の置き方（代理の範囲の高さに合わせ、縦横比を保って中央に置く）にする")]
        public bool KeepPlacement { get; }

        [PLParam(Description = "画像の 1 隅（代理のローカル XY）。corner1 と縦か横が揃うと既定の置き方になる")]
        public Vector2 Corner0 { get; }

        [PLParam(Description = "corner0 と向かい合う隅（代理のローカル XY）")]
        public Vector2 Corner1 { get; }

        [PLParam(Description = "コントラスト（0〜1）。負なら今の値のまま")]
        public float Contrast { get; }

        [PLParam(Description = "明るさ（0〜1）。負なら今の値のまま")]
        public float Intensity { get; }

        public SetEditSpacePlateUnderlayCommand(int modelIndex, string filePath = "", bool keepPlacement = false,
                                                Vector2 corner0 = default, Vector2 corner1 = default,
                                                float contrast = -1f, float intensity = -1f)
            : base(modelIndex)
        {
            FilePath      = filePath ?? "";
            KeepPlacement = keepPlacement;
            Corner0       = corner0;
            Corner1       = corner1;
            Contrast      = contrast;
            Intensity     = intensity;
        }
    }

    /// <summary>断面の作業空間の下絵（作業板スロット）を外す。</summary>
    [PLCommand(Category = "underlay", Writes = PLWriteScope.None,
        Description = "開いている断面の作業空間の代理に固定した参考画像（作業板スロット）を外す。Undo には入らない。")]
    [PLResult("removed", PLResultKind.Flag, Description = "外したか（もともと無ければ false）")]
    public sealed class ClearEditSpacePlateUnderlayCommand : PanelCommand
    {
        public ClearEditSpacePlateUnderlayCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>作業空間の代理を元データへ反映する。</summary>
    [PLCommand(Category = "uv", Writes = PLWriteScope.ModelWide, Effects = PLCommandEffect.UV,
        Description = "開いている作業空間の代理メッシュを元データへ反映する（UV なら元の描画オブジェクトの UV へ書き戻す。断面系なら図形生成パネルの断面へ取り込む）。元の構造が変わっている・代理の面と元の面が 1 対 1 にならないなどの不整合があれば、元データを変えずに失敗を返す。反映は元データ側の 1 回の Undo になる。")]
    [PLResult("isOpen",            PLResultKind.Flag,    Description = "作業空間が開いているか")]
    [PLResult("kind",              PLResultKind.Text,    Description = "作業空間の種類（uv）")]
    [PLResult("sourceMasterIndex", PLResultKind.Integer, Description = "元の描画オブジェクトの masterIndex")]
    [PLResult("proxyMasterIndex",  PLResultKind.Integer, Description = "代理メッシュの masterIndex")]
    [PLResult("locked",            PLResultKind.Flag,    Description = "ビルボードをロック中か")]
    [PLResult("hasChanges",        PLResultKind.Flag,    Description = "未反映の変更があるか")]
    [PLResult("faces",             PLResultKind.Integer, Description = "対象の面の数")]
    public sealed class ApplyEditSpaceCommand : PanelCommand
    {
        public ApplyEditSpaceCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>作業空間の未反映の変更を捨てる。</summary>
    [PLCommand(Category = "uv", Writes = PLWriteScope.ModelWide, Effects = PLCommandEffect.VertexPosition,
        Description = "開いている作業空間の未反映の変更を捨て、代理メッシュを元データの状態へ戻す。元データは変えない。作業空間は開いたまま。")]
    [PLResult("isOpen",            PLResultKind.Flag,    Description = "作業空間が開いているか")]
    [PLResult("kind",              PLResultKind.Text,    Description = "作業空間の種類（uv）")]
    [PLResult("sourceMasterIndex", PLResultKind.Integer, Description = "元の描画オブジェクトの masterIndex")]
    [PLResult("proxyMasterIndex",  PLResultKind.Integer, Description = "代理メッシュの masterIndex")]
    [PLResult("locked",            PLResultKind.Flag,    Description = "ビルボードをロック中か")]
    [PLResult("hasChanges",        PLResultKind.Flag,    Description = "未反映の変更があるか")]
    [PLResult("faces",             PLResultKind.Integer, Description = "対象の面の数")]
    public sealed class CancelEditSpaceCommand : PanelCommand
    {
        public CancelEditSpaceCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>作業空間を閉じる。</summary>
    [PLCommand(Category = "uv", Writes = PLWriteScope.ModelWide, Effects = PLCommandEffect.DeletesObject,
        Description = "開いている作業空間を閉じる。UV は一時オブジェクトの代理メッシュを消して開く前の選択へ戻す。断面系は代理（常設の線分オブジェクト）を残し、ビルボードを開く前の状態へ戻す。どちらもビューを開く前へ戻す。未反映の変更があるときは閉じずに失敗を返すので、applyEditSpace か cancelEditSpace を先に行う。")]
    [PLResult("isOpen", PLResultKind.Flag, Description = "作業空間が開いているか（閉じた後は false）")]
    public sealed class CloseEditSpaceCommand : PanelCommand
    {
        public CloseEditSpaceCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>作業空間のビルボードのロックを切り替える。</summary>
    [PLCommand(Category = "uv", Writes = PLWriteScope.ModelWide,
        Description = "開いている作業空間のビルボードのロックを切り替える。ロック中は代理メッシュを基準ビューへ正対させ、解除すると代理の姿勢（WorldMatrix）のまま表示して 3D で回り込める。代理のローカル座標は変えない。")]
    [PLResult("isOpen",            PLResultKind.Flag,    Description = "作業空間が開いているか")]
    [PLResult("kind",              PLResultKind.Text,    Description = "作業空間の種類（uv）")]
    [PLResult("sourceMasterIndex", PLResultKind.Integer, Description = "元の描画オブジェクトの masterIndex")]
    [PLResult("proxyMasterIndex",  PLResultKind.Integer, Description = "代理メッシュの masterIndex")]
    [PLResult("locked",            PLResultKind.Flag,    Description = "ビルボードをロック中か")]
    [PLResult("hasChanges",        PLResultKind.Flag,    Description = "未反映の変更があるか")]
    [PLResult("faces",             PLResultKind.Integer, Description = "対象の面の数")]
    public sealed class SetEditSpaceBillboardLockCommand : PanelCommand
    {
        [PLParam(Description = "true でロック（基準ビューへ正対）、false で解除", Required = true)]
        public bool Locked { get; }

        public SetEditSpaceBillboardLockCommand(int modelIndex, bool locked)
            : base(modelIndex) { Locked = locked; }
    }

    /// <summary>作業空間の下絵の表示を切り替える。</summary>
    [PLCommand(Category = "uv", Writes = PLWriteScope.None,
        Description = "開いている作業空間の下絵（UV なら対象マテリアルのテクスチャ）の表示を切り替える。下絵は作業ビューで、ビルボードをロックしている間だけ代理の UV 0〜1 に重ねて出る。0〜1 の枠は下絵の表示にかかわらず出る。保存しない。")]
    [PLResult("isOpen",            PLResultKind.Flag,    Description = "作業空間が開いているか")]
    [PLResult("kind",              PLResultKind.Text,    Description = "作業空間の種類（uv）")]
    [PLResult("sourceMasterIndex", PLResultKind.Integer, Description = "元の描画オブジェクトの masterIndex")]
    [PLResult("proxyMasterIndex",  PLResultKind.Integer, Description = "代理メッシュの masterIndex")]
    [PLResult("locked",            PLResultKind.Flag,    Description = "ビルボードをロック中か")]
    [PLResult("hasChanges",        PLResultKind.Flag,    Description = "未反映の変更があるか")]
    [PLResult("faces",             PLResultKind.Integer, Description = "対象の面の数")]
    [PLResult("showUnderlay",      PLResultKind.Flag,    Description = "下絵を表示する設定か")]
    [PLResult("underlayTexture",   PLResultKind.Text,    Description = "下絵に使うテクスチャの名前。無ければ空")]
    public sealed class SetEditSpaceUnderlayCommand : PanelCommand
    {
        [PLParam(Description = "true で下絵を表示、false で非表示", Required = true)]
        public bool Visible { get; }

        public SetEditSpaceUnderlayCommand(int modelIndex, bool visible)
            : base(modelIndex) { Visible = visible; }
    }

    /// <summary>UV の作業空間を画像の縦横比で表示するかを切り替える。</summary>
    [PLCommand(Category = "uv", Writes = PLWriteScope.ModelWide,
        Description = "開いている UV の作業空間を、下絵のテクスチャの縦横比で表示するかを切り替える。オンでは代理頂点 = (倍率 × U, 倍率 × 高さ ÷ 幅 × V, 0) にして画素が正方形に見えるようにする（Maya の Use Image Ratio に当たる）。UV の値は変わらない。未反映の変更があるときは切り替えない（先に applyEditSpace か cancelEditSpace）。切り替えると代理を今の UV から作り直し、それより前の作業中の編集は Undo で戻せなくなる。下絵のテクスチャが無ければオンでも縦横比 1。")]
    [PLResult("isOpen",            PLResultKind.Flag,    Description = "作業空間が開いているか")]
    [PLResult("kind",              PLResultKind.Text,    Description = "作業空間の種類（uv）")]
    [PLResult("sourceMasterIndex", PLResultKind.Integer, Description = "元の描画オブジェクトの masterIndex")]
    [PLResult("proxyMasterIndex",  PLResultKind.Integer, Description = "代理メッシュの masterIndex")]
    [PLResult("locked",            PLResultKind.Flag,    Description = "ビルボードをロック中か")]
    [PLResult("hasChanges",        PLResultKind.Flag,    Description = "未反映の変更があるか")]
    [PLResult("faces",             PLResultKind.Integer, Description = "対象の面の数")]
    [PLResult("useImageRatio",     PLResultKind.Flag,    Description = "画像の縦横比で表示しているか")]
    [PLResult("scaleU",            PLResultKind.Number,  Description = "U の作業倍率")]
    [PLResult("scaleV",            PLResultKind.Number,  Description = "V の作業倍率")]
    public sealed class SetEditSpaceImageRatioCommand : PanelCommand
    {
        [PLParam(Description = "true で画像の縦横比で表示、false で UV 0〜1 を正方形で表示", Required = true)]
        public bool Enabled { get; }

        public SetEditSpaceImageRatioCommand(int modelIndex, bool enabled)
            : base(modelIndex) { Enabled = enabled; }
    }
}

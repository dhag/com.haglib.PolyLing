# fix_assemblies.ps1
# 実行場所: 任意（パスはフルパス）
# 操作:
#   A) Editor/Tools の .EditorUI.cs 22本 を Runtime/Tools へ移動
#   B) Editor/Tools の *Settings.cs / *.Texts.cs 31本を削除（Runtime側に同名が存在）
#   C) Editor/UI/SkinWeightPaint/SkinWeightPaintShared.cs を削除（Runtime側に同名が存在）

$pkg    = "D:\UNITY保存の品\TEO3.0開発中\PolyLing\Packages\com.haglib.polyling"
$edBase = "$pkg\Editor\Poly_Ling_Main\Tools"
$rtBase = "$pkg\Runtime\Poly_Ling_Main\Tools"

$moved   = 0
$deleted = 0
$missing = 0

function MoveToRuntime($rel) {
    $src  = "$edBase\$rel"
    $dst  = "$rtBase\$rel"
    $dstDir = Split-Path $dst -Parent
    if (!(Test-Path $src)) { Write-Host "MISSING: $rel"; $script:missing++; return }
    if (!(Test-Path $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
    Move-Item $src $dst -Force
    $srcMeta = "$src.meta"
    if (Test-Path $srcMeta) { Move-Item $srcMeta "$dst.meta" -Force }
    Write-Host "MOVED: $rel"
    $script:moved++
}

function DeleteFromEditor($rel) {
    $f = "$edBase\$rel"
    if (!(Test-Path $f)) { Write-Host "MISSING(skip): $rel"; $script:missing++; return }
    Remove-Item $f
    $m = "$f.meta"
    if (Test-Path $m) { Remove-Item $m }
    Write-Host "DELETED: $rel"
    $script:deleted++
}

# ================================================================
# A) .EditorUI.cs を Editor -> Runtime へ移動
# ================================================================
$editorUiFiles = @(
    "TransformTools\RotateTool_\RotateTool.EditorUI.cs",
    "TransformTools\ScaleTool_\ScaleTool.EditorUI.cs",
    "TransformTools\MoveTool_\MoveTool.EditorUI.cs",
    "TransformTools\SculptTool_\SculptTool.EditorUI.cs",
    "TransformTools\ObjectMoveTool_\ObjectMoveTool.EditorUI.cs",
    "TransformTools\PivotOffsetTool_\PivotOffsetTool.EditorUI.cs",
    "TransformTools\SkinWeightPaintTool_\SkinWeightPaintTool.EditorUI.cs",
    "SelectTools\SelectTool_\SelectTool.EditorUI.cs",
    "SelectTools\AdvancedSelectTool_\AdvancedSelectTool.EditorUI.cs",
    "SelectTools\AdvancedSelectTool_\AdvancedSelectToolSub\BeltSelectMode.EditorUI.cs",
    "SelectTools\AdvancedSelectTool_\AdvancedSelectToolSub\ConnectedSelectMode.EditorUI.cs",
    "SelectTools\AdvancedSelectTool_\AdvancedSelectToolSub\EdgeLoopSelectMode.EditorUI.cs",
    "SelectTools\AdvancedSelectTool_\AdvancedSelectToolSub\ShortestPathSelectMode.EditorUI.cs",
    "TopologyTools\Modify\FlipFaceTool_\FlipFaceTool.EditorUI.cs",
    "TopologyTools\Modify\EdgeBevelTool_\EdgeBevelTool.EditorUI.cs",
    "TopologyTools\Modify\EdgeTopologyTool_\EdgeTopologyTool.EditorUI.cs",
    "TopologyTools\Modify\Extrude\EdgeExtrudeTool_\EdgeExtrudeTool.EditorUI.cs",
    "TopologyTools\Modify\Extrude\FaceExtrudeTool_\FaceExtrudeTool.EditorUI.cs",
    "TopologyTools\Modify\Extrude\LineExtrudeTool_\LineExtrudeTool.EditorUI.cs",
    "TopologyTools\Modify\KnifeTool_\KnifeTool.EditorUI.cs",
    "TopologyTools\Modify\MergeVerticesTool_\MergeVerticesTool.EditorUI.cs",
    "TopologyTools\Create\AddFaceTool_\AddFaceTool.EditorUI.cs"
)
foreach ($f in $editorUiFiles) { MoveToRuntime $f }

# ================================================================
# B) Editor側の重複 *Settings.cs / *.Texts.cs を削除
# ================================================================
$dupFiles = @(
    "TransformTools\RotateTool_\RotateSettings.cs",
    "TransformTools\RotateTool_\RotateTool.Texts.cs",
    "TransformTools\ScaleTool_\ScaleSettings.cs",
    "TransformTools\ScaleTool_\ScaleTool.Texts.cs",
    "TransformTools\MoveTool_\MoveSettings.cs",
    "TransformTools\MoveTool_\MoveTool.Texts.cs",
    "TransformTools\SculptTool_\SculptSettings.cs",
    "TransformTools\SculptTool_\SculptTool.Texts.cs",
    "TransformTools\ObjectMoveTool_\ObjectMoveSettings.cs",
    "TransformTools\ObjectMoveTool_\ObjectMoveTool.Texts.cs",
    "TransformTools\PivotOffsetTool_\PivotOffsetTool.Texts.cs",
    "TransformTools\SkinWeightPaintTool_\SkinWeightPaintSettings.cs",
    "SelectTools\SelectTool_\SelectTool.Texts.cs",
    "SelectTools\AdvancedSelectTool_\AdvancedSelectSettings.cs",
    "SelectTools\AdvancedSelectTool_\AdvancedSelectTool.Texts.cs",
    "TopologyTools\Modify\FlipFaceTool_\FlipFaceTool.Texts.cs",
    "TopologyTools\Modify\EdgeBevelTool_\EdgeBevelSettings.cs",
    "TopologyTools\Modify\EdgeBevelTool_\EdgeBevelTool.Texts.cs",
    "TopologyTools\Modify\EdgeTopologyTool_\EdgeTopologySettings.cs",
    "TopologyTools\Modify\EdgeTopologyTool_\EdgeTopologyTool.Texts.cs",
    "TopologyTools\Modify\Extrude\EdgeExtrudeTool_\EdgeExtrudeSettings.cs",
    "TopologyTools\Modify\Extrude\EdgeExtrudeTool_\EdgeExtrudeTool.Texts.cs",
    "TopologyTools\Modify\Extrude\FaceExtrudeTool_\FaceExtrudeSettings.cs",
    "TopologyTools\Modify\Extrude\FaceExtrudeTool_\FaceExtrudeTool.Texts.cs",
    "TopologyTools\Modify\Extrude\LineExtrudeTool_\LineExtrudeTool.Texts.cs",
    "TopologyTools\Modify\KnifeTool_\KnifeSettings.cs",
    "TopologyTools\Modify\KnifeTool_\KnifeTool.Texts.cs",
    "TopologyTools\Modify\MergeVerticesTool_\MergeVerticesSettings.cs",
    "TopologyTools\Modify\MergeVerticesTool_\MergeVerticesTool.Texts.cs",
    "TopologyTools\Create\AddFaceTool_\AddFaceSettings.cs",
    "TopologyTools\Create\AddFaceTool_\AddFaceTool.Texts.cs"
)
foreach ($f in $dupFiles) { DeleteFromEditor $f }

# ================================================================
# C) SkinWeightPaintShared.cs を Editor から削除
# ================================================================
$swp = "$pkg\Editor\Poly_Ling_Main\UI\SkinWeightPaint\SkinWeightPaintShared.cs"
if (Test-Path $swp) {
    Remove-Item $swp
    if (Test-Path "$swp.meta") { Remove-Item "$swp.meta" }
    Write-Host "DELETED: SkinWeightPaintShared.cs"
    $deleted++
} else {
    Write-Host "MISSING(skip): SkinWeightPaintShared.cs"
    $missing++
}

Write-Host ""
Write-Host "完了: moved=$moved  deleted=$deleted  missing=$missing"

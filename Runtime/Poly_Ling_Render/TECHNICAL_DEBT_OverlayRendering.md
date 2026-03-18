# 技術的負債: オーバーレイ描画の暫定実装 [解決済み]

## 概要

オーバーレイ描画（選択メッシュの頂点・ワイヤフレームを最前面表示）で、
パフォーマンスを犠牲にした暫定的な実装を行っていた問題。

**ステータス: 解決済み** — Idle oneshot + SelectionPipeline統合により修正完了。

## 解決方法

### 1. Idle oneshotシステム (UpdateMode/Profile)

`PrepareUnifiedDrawing`がUpdateProfileで制御され、Idle時は重い処理をスキップ：

```
Idle mode (AllowSelectionSync=false, AllowGpuVisibility=false):
  → UpdateAllSelectionFlags スキップ
  → GPU Dispatch スキップ

Normal mode (AllowSelectionSync=true, AllowGpuVisibility=true):
  → 1回だけ全処理実行
  → ConsumeNormalMode() で自動的にIdleに降格
```

### 2. SelectionPipeline統合

選択変更時の更新を単一パスに統合：

```
選択変更 → RequestNormal() → mode=Normal
  ↓ 同フレームRepaintイベント
PrepareUnifiedDrawing (Normal mode):
  SyncSelectionFromModel(_model)
  SetActiveMesh(0, unifiedMeshIndex)
  UpdateAllSelectionFlags()        ← 1回だけ
  DispatchClearBuffersGPU()        ← 1回だけ
  ComputeScreenPositionsGPU()      ← 1回だけ
  DispatchFaceVisibilityGPU()      ← 1回だけ
  DispatchLineVisibilityGPU()      ← 1回だけ
ConsumeNormalMode() → mode=Idle
```

**以前**: 選択変更1回につき`UpdateAllSelectionFlags`が最大3回実行
**現在**: 選択変更1回につき1回、Idleフレームでは0回

### 3. `_selectedVertices`プロキシ廃止

`_selectedVertices`プロキシプロパティを廃止し、
全箇所で`_selectionState.Vertices`を直接参照に統一。
`_selectedVerticesFallback`フィールドも削除。
`_selectionState`は`new SelectionState()`で初期化されnullにならない。

## 更新履歴

| 日付 | 内容 |
|------|------|
| 2026-01-10 | 初版作成（毎フレームUpdateAllSelectionFlags等を呼ぶ暫定実装） |
| 2026-01-10 | SyncSelectionFromLegacy()を毎フレーム呼ぶ暫定実装を追加 |
| 2026-01-10 | NotifyUnifiedTransformChanged()をSyncMeshFromData内で呼ぶように変更（イベント駆動） |
| 2026-02-13 | Idle oneshotシステム導入により毎フレーム実行問題を解決 |
| 2026-02-13 | SelectionPipeline統合により3回→1回に削減 |
| 2026-02-13 | `_selectedVertices`プロキシ廃止、`_selectionState.Vertices`直接参照に統一 |

## 担当

Claude（AI）+ yoshihiro（確認）

# 技術的負債: オーバーレイ描画の暫定実装

## 概要

オーバーレイ描画（選択メッシュの頂点・ワイヤフレームを最前面表示）を実装するため、
パフォーマンスを犠牲にした暫定的な実装を行った。本番運用前に修正が必要。

## 問題のあるコード

**ファイル:** `Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory_UnifiedSystem.cs`

**メソッド:** `PrepareUnifiedDrawing()`

```csharp
// 【暫定】毎フレーム選択状態を同期（TECHNICAL_DEBT参照）
SyncSelectionFromLegacy();

// 選択フラグを直接更新
var bufMgr = _unifiedAdapter.BufferManager;
if (bufMgr != null)
{
    bufMgr.SetActiveMesh(0, _selectedIndex);        // ← 毎フレーム呼び出し
    bufMgr.UpdateAllSelectionFlags();               // ← 毎フレーム呼び出し（重い）
    
    // 面・線分の可視性計算（Culledフラグ設定）
    var viewport = new Rect(0, 0, camera.pixelWidth, camera.pixelHeight);
    bufMgr.DispatchClearBuffersGPU();               // ← 毎フレーム呼び出し
    bufMgr.ComputeScreenPositionsGPU(...);          // ← 毎フレーム呼び出し
    bufMgr.DispatchFaceVisibilityGPU();             // ← 毎フレーム呼び出し
    bufMgr.DispatchLineVisibilityGPU();             // ← 毎フレーム呼び出し
}
```

**ファイル:** `Editor/MeshFactory_/SimpleMeshFactory/SimpleMeshFactory.cs`

**メソッド:** `SyncMeshFromData()`

```csharp
// ★GPUバッファの位置情報を更新
NotifyUnifiedTransformChanged();  // ← メッシュ同期時に呼び出し（頂点移動時）
```

## パフォーマンス影響

### 毎フレーム実行される処理

| 処理 | 内容 | 負荷 |
|------|------|------|
| `SyncSelectionFromLegacy` | `_selectedVertices`→`_selectionState.Vertices`同期 | 軽い〜中程度 |
| `SetActiveMesh` | FlagManagerの選択インデックス設定 | 軽い |
| `UpdateAllSelectionFlags` | 全頂点(65536)+全ライン(33190)をCPUループ → GPUアップロード | **非常に重い** |
| `DispatchClearBuffersGPU` | GPU計算シェーダ実行 | 中程度 |
| `ComputeScreenPositionsGPU` | GPU計算シェーダ実行（MVP変換） | 中程度 |
| `DispatchFaceVisibilityGPU` | GPU計算シェーダ実行（背面カリング） | 中程度 |
| `DispatchLineVisibilityGPU` | GPU計算シェーダ実行（ライン可視性） | 中程度 |

### イベント駆動で実行される処理

| 処理 | トリガー | 内容 | 負荷 |
|------|----------|------|------|
| `NotifyUnifiedTransformChanged` | SyncMeshFromData | 頂点移動時に位置バッファ更新 | 中程度 |

### 特に問題な処理: `UpdateAllSelectionFlags()`

```csharp
// UnifiedBufferManager_Update.cs
public void UpdateAllSelectionFlags()
{
    // 全頂点をループ（65536回）
    for (int meshIdx = 0; meshIdx < _meshCount; meshIdx++)
    {
        for (uint v = 0; v < meshInfo.VertexCount; v++)
        {
            // フラグ計算
            _vertexFlags[globalIdx] = flags;
        }
    }
    _vertexFlagsBuffer.SetData(_vertexFlags, ...);  // GPUアップロード
    
    UpdateAllLineSelectionFlags();  // さらに全ライン（33190回）ループ
}
```

## 本来あるべき実装

### DirtyLevelシステムによる差分更新

既存の`DirtyLevel`列挙型と更新システムを活用すべき：

```csharp
public enum DirtyLevel
{
    None = 0,
    Selection = 1,  // 選択変更時
    Camera = 2,     // カメラ変更時
    Topology = 3,   // トポロジー変更時
}
```

### 正しいフロー

```
[選択変更時]
_selectedVertices変更
  → SyncSelectionFromLegacy()  // _selectionState.Verticesに反映
  → NotifySelectionChanged()
  → MarkSelectionDirty()
  → _dirtyLevel = DirtyLevel.Selection

[カメラ変更時]  
NotifyCameraChanged()
  → MarkCameraDirty()
  → _dirtyLevel = DirtyLevel.Camera

[描画前（1回だけ）]
ProcessUpdates()
  → if (_dirtyLevel >= Selection) UpdateAllSelectionFlags()
  → if (_dirtyLevel >= Camera) ComputeScreenPositions() + Visibility計算
  → _dirtyLevel = None
```

### 修正方針

1. `_selectedVertices`を変更する全箇所で`SyncSelectionFromLegacy()`を呼ぶ
2. `SyncSelectionFromLegacy()`内で`NotifySelectionChanged()`を呼ぶ（実装済み）
3. `ProcessUpdates()`が正しく`DirtyLevel`を検知することを確認
4. `PrepareUnifiedDrawing()`から毎フレーム呼び出しを削除
5. `ProcessUpdates()`経由で必要な更新だけ実行

### 選択変更箇所（要対応）

`_selectedVertices`を変更している箇所（`SimpleMeshFactory_Selection.cs`等）：
- `SelectAll()` - line 100-104
- `InvertSelection()` - line 129
- `DeselectAll()` - line 144
- `SelectConnectedVertices()` - line 165
- `MergeSelectedVertices()` - line 195-198
- `SimpleMeshFactory_SelectionSets.cs` - line 283

これらの箇所で選択変更後に`SyncSelectionFromLegacy()`を呼ぶか、
`_selectedVertices`の代わりに`_selectionState`を直接操作するように変更すべき。

## 関連ファイル

- `Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory_UnifiedSystem.cs` - 暫定実装箇所
- `Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory.cs` - SyncSelectionFromLegacy()
- `Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory_Selection.cs` - 選択操作
- `Editor/MeshFactory/Core/Buffers/UnifiedBufferManager_Update.cs` - 更新処理
- `Editor/MeshFactory/Core/Update/DirtyLevel.cs` - 更新レベル定義
- `Editor/MeshFactory/Core/UnifiedMeshSystem.cs` - 更新フロー管理

## 暫定実装を行った理由

1. `ProcessUpdates()`を呼んでも`VertexSelected`フラグがバッファに反映されなかった
2. 原因: `_selectedVertices`と`_selectionState.Vertices`が同期されていなかった
3. 原因調査より動作確認を優先し、毎フレーム同期する暫定実装を採用

## 更新履歴

| 日付 | 内容 |
|------|------|
| 2026-01-10 | 初版作成（毎フレームUpdateAllSelectionFlags等を呼ぶ暫定実装） |
| 2026-01-10 | SyncSelectionFromLegacy()を毎フレーム呼ぶ暫定実装を追加 |
| 2026-01-10 | NotifyUnifiedTransformChanged()をSyncMeshFromData内で呼ぶように変更（イベント駆動） |

## 担当

Claude（AI）+ yoshihiro（確認）

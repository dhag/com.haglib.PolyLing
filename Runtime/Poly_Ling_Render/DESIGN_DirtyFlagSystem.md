# DirtyFlag システム設計

## 概要

変更の種類に応じて適切な更新処理のみを実行するシステム。
現在は毎フレームすべての更新処理を実行しており、パフォーマンス問題がある。

## DirtyLevel 定義（既存）

```
Level 1: Mouse     - マウス位置変更（ヒットテスト、ホバー）
Level 2: Camera    - カメラパラメータ変更（MVP行列、カリング、スクリーン座標）
Level 3: Selection - 選択状態変更（選択フラグ）
Level 4: Transform - 頂点位置変更（位置、法線、バウンディングボックス）
Level 5: Topology  - トポロジー変更（全バッファ再構築）
```

## 変更イベントと必要な更新処理

### 1. パネル上で表示形態が変更されたとき

**トリガー例:**
- ワイヤフレーム表示ON/OFF
- 頂点表示ON/OFF  
- 背面カリングON/OFF
- 頂点インデックス表示ON/OFF

**必要な更新:**
- 再描画のみ（DirtyLevel.None）
- 背面カリング変更時はCamera level（可視性再計算）

**現状:** 適切に処理されている（Repaint()で対応）

**対処:** 
- 背面カリング変更時のみ `NotifyCameraChanged()` を呼ぶ
- 他は `Repaint()` のみでOK

---

### 2. 選択メッシュオブジェクトが変更されたとき

**トリガー:**
- 階層パネルで別のメッシュオブジェクトをクリック
- `_selectedIndex` の変更

**必要な更新:**
- `SetActiveMesh()` - アクティブメッシュ設定
- `UpdateAllSelectionFlags()` - 選択フラグ再計算
- カメラ関連処理（新しいメッシュのカリング計算）

**現状:** 毎フレーム `SetActiveMesh()` + `UpdateAllSelectionFlags()` を呼んでいる

**対処:**
```csharp
// _selectedIndex 変更時に呼ぶ
private void OnSelectedMeshChanged(int newIndex)
{
    _selectedIndex = newIndex;
    _unifiedAdapter.SetActiveMesh(0, _selectedIndex);
    _unifiedAdapter.NotifySelectionChanged();  // Selection dirty
    _unifiedAdapter.NotifyCameraChanged();     // カリング再計算
}
```

---

### 3. カメラの視点などが変更されたとき

**トリガー:**
- マウスドラッグによる回転
- ズーム
- パン

**必要な更新:**
- MVP行列更新
- スクリーン座標計算（GPU）
- 背面カリング再計算（GPU）
- ホバー判定

**現状:** `UpdateUnifiedFrame()` で毎フレーム `UpdateCamera()` を呼んでいる

**対処:** 
- カメラパラメータの変更検知を追加
- 変更があった場合のみ `MarkCameraDirty()`

```csharp
private Vector3 _lastCameraPos;
private Quaternion _lastCameraRot;
private float _lastFov;

private void UpdateUnifiedFrame(...)
{
    bool cameraChanged = (_cameraPosition != _lastCameraPos) 
                      || (_cameraTarget != _lastCameraTarget)
                      || (fov != _lastFov);
    
    if (cameraChanged)
    {
        _unifiedAdapter.UpdateCamera(...);  // これが MarkCameraDirty を呼ぶ
        _lastCameraPos = _cameraPosition;
        _lastCameraTarget = _cameraTarget;
        _lastFov = fov;
    }
    
    // マウス位置は毎フレーム更新（軽い処理）
    _unifiedAdapter.UpdateMousePosition(mousePosition);
}
```

---

### 4. メッシュオブジェクトの頂点位置など（位相不変の変更）

**トリガー:**
- 頂点のドラッグ移動
- スケール変更
- その他頂点位置変更

**必要な更新:**
- 位置バッファ更新（GPU）
- 法線再計算
- バウンディングボックス更新

**現状:** `SyncMeshFromData()` で `NotifyUnifiedTransformChanged()` を呼んでいる（OK）

**対処:** 現状維持
```csharp
// SyncMeshFromData() 内
NotifyUnifiedTransformChanged();  // → NotifyTransformChanged() → MarkTransformDirty()
```

---

### 5. メッシュオブジェクトのピボット（トランスフォーム）変更

**トリガー:**
- ピボット位置変更
- 回転
- スケール（オブジェクト全体）

**必要な更新:**
- トランスフォーム行列更新
- 位置バッファ更新（ワールド座標再計算）

**現状:** 対応なし？

**対処:**
```csharp
private void OnPivotChanged()
{
    _unifiedAdapter.NotifyTransformChanged();
}
```

---

### 6. 面の追加など（位相変更）

**トリガー:**
- 面の追加/削除
- 頂点の追加/削除
- エッジの追加/削除
- メッシュのマージ/分割

**必要な更新:**
- 全バッファ再構築
- インデックスマッピング再構築

**現状:** 一部で `NotifyTopologyChanged()` を呼んでいる

**対処:**
```csharp
// 位相変更操作後に呼ぶ
private void OnTopologyChanged()
{
    RebuildUnifiedSystemFromModel();  // 既存メソッド
    // これが内部で NotifyTopologyChanged() を呼ぶべき
}
```

---

## 頂点/エッジ/面の選択変更

**トリガー:**
- クリックで頂点選択
- Shift+クリックで追加選択
- ボックス選択
- 全選択/選択解除

**必要な更新:**
- `_selectionState` への反映
- 選択フラグバッファ更新

**現状:** 
- `_selectedVertices` を変更後、`SyncSelectionFromLegacy()` を毎フレーム呼んでいる

**対処:**
```csharp
// 選択変更時に呼ぶ（毎フレームではなく）
private void OnSelectionChanged()
{
    SyncSelectionFromLegacy();
    _unifiedAdapter.NotifySelectionChanged();
}
```

**選択変更箇所（SimpleMeshFactory_Selection.cs）:**
- `SelectAll()` - 全選択
- `InvertSelection()` - 選択反転
- `DeselectAll()` - 選択解除
- `SelectConnectedVertices()` - 連結選択
- `MergeSelectedVertices()` - 頂点マージ

これらすべての末尾で `OnSelectionChanged()` を呼ぶ。

---

## 実装手順

### Phase 1: 選択変更の最適化

1. `OnSelectionChanged()` メソッドを追加
2. `_selectedVertices` 変更箇所で `OnSelectionChanged()` を呼ぶ
3. `PrepareUnifiedDrawing()` から `SyncSelectionFromLegacy()` を削除

### Phase 2: メッシュオブジェクト選択の最適化

1. `OnSelectedMeshChanged()` メソッドを追加
2. `_selectedIndex` 変更箇所で呼ぶ
3. `PrepareUnifiedDrawing()` から `SetActiveMesh()` + `UpdateAllSelectionFlags()` を削除

### Phase 3: カメラ変更の最適化

1. カメラパラメータの変更検知を追加
2. 変更時のみ更新処理を実行

### Phase 4: 可視性計算の最適化

1. GPU計算を毎フレームではなくCamera dirty時のみ実行
2. `PrepareUnifiedDrawing()` から GPU dispatch を削除
3. `ExecuteUpdates(Camera)` で実行

---

## 最終的な PrepareUnifiedDrawing

```csharp
private void PrepareUnifiedDrawing(...)
{
    // DirtyLevel に基づく更新は UpdateUnifiedFrame() で完了済み
    
    // ContextIndex → UnifiedMeshIndex に変換
    int unifiedMeshIndex = _unifiedAdapter.ContextToUnifiedMeshIndex(_selectedIndex);
    
    _unifiedAdapter.PrepareDrawing(
        camera,
        showWireframe,
        showVertices,
        showUnselectedWireframe,
        showUnselectedVertices,
        unifiedMeshIndex,
        pointSize,
        alpha);
}
```

---

## 補足: 現在の更新フロー

```
DrawPreview()
  ├── HandleInput()           // 入力処理
  ├── UpdateUnifiedFrame()    // フレーム更新
  │     ├── BeginFrame()
  │     ├── UpdateCamera()    // → MarkCameraDirty
  │     ├── UpdateMousePosition() // → MarkMouseDirty
  │     ├── ProcessUpdates()  // DirtyLevel に基づく更新
  │     └── ExecuteUpdates()  // 実際の更新実行
  └── PrepareUnifiedDrawing() // 描画準備（ここで毎フレーム更新している = 問題）
        ├── SyncSelectionFromLegacy()     // 【削除予定】
        ├── SetActiveMesh()               // 【削除予定】
        ├── UpdateAllSelectionFlags()     // 【削除予定】
        ├── DispatchClearBuffersGPU()     // 【削除予定】
        ├── ComputeScreenPositionsGPU()   // 【削除予定】
        ├── DispatchFaceVisibilityGPU()   // 【削除予定】
        ├── DispatchLineVisibilityGPU()   // 【削除予定】
        └── PrepareDrawing()              // メッシュ構築のみ
```

これらの「削除予定」処理は `ExecuteUpdates()` 内で DirtyLevel に応じて実行されるべき。

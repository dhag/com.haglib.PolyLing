# 頂点位置アクセスパターン分析

## 問題の概要

現在、頂点位置（`MeshObject.Vertices[i].Position`）は複数箇所から直接アクセスされている。
ワールド変換を導入する場合、これらすべてが影響を受ける。

## アクセスパターン分類

### 1. GPUバッファ構築時（UnifiedBufferManager_Build.cs）

```csharp
// line 171, 414, 445
_positions[globalIdx] = vertex.Position;
_positions[globalIdx] = meshObject.Vertices[v].Position;
```

**問題度: 低**
- ここはローカル座標をGPUバッファに転送する箇所
- ワールド変換はGPU側で行うので、ここはローカル座標のままでOK

---

### 2. ワイヤフレーム/頂点描画（UnifiedRenderer.cs）

```csharp
// line 266-267
var positions = _bufferManager.Positions;  // CPU配列
Vector3 p1 = positions[v1];
Vector3 p2 = positions[v2];
```

**問題度: 高**
- `_bufferManager.Positions`はローカル座標のCPU配列
- ワイヤフレームメッシュ構築時にこれを使っている
- **対処**: GPU変換後の座標を使うか、CPU側で変換行列を適用

---

### 3. 面ハイライト描画（SimpleMeshFactory_Preview.cs）

```csharp
// line 267, 330
Vector3 worldPos = displayMatrix.MultiplyPoint3x4(meshObject.Vertices[vi].Position);
```

**問題度: 中**
- `displayMatrix`で変換している（良い）
- ただし`MeshObject.Vertices`に直接アクセス
- **対処**: `displayMatrix`にワールド変換を含めればOK

---

### 4. 頂点インデックス描画（SimpleMeshFactory_Preview.cs）

```csharp
// line 395
Vector3 transformedPos = matrix.MultiplyPoint3x4(meshObject.Vertices[i].Position);
```

**問題度: 中**
- 同上、matrixで変換している
- **対処**: matrixにワールド変換を含めればOK

---

### 5. MoveTool - 選択中心計算（MoveTool.cs）

```csharp
// line 814
_selectionCenter += ctx.MeshObject.Vertices[vi].Position;
```

**問題度: 高**
- ギズモ位置の計算
- ワールド座標で表示したい場合、変換が必要
- **対処**: ワールドモード時はWorldMatrixで変換

---

### 6. MoveTool - ドラッグ開始位置記録（MoveTool.cs）

```csharp
// line 595, 700, 777
_dragStartPositions = ctx.MeshObject.Vertices.Select(v => v.Position).ToArray();
```

**問題度: 中**
- ドラッグ開始時のローカル座標を記録
- ローカル座標で記録するのは正しい（データ変更はローカルで行う）
- **対処**: そのままでOK

---

### 7. MoveTool - エッジ/面の中心計算（MoveTool.cs）

```csharp
// line 1030-1031, 1067, 1105
Vector3 p1 = ctx.MeshObject.Vertices[edge.V1].Position;
```

**問題度: 高**
- エッジ/面のギズモ位置計算
- ワールド座標で表示したい場合、変換が必要
- **対処**: ワールドモード時はWorldMatrixで変換

---

## 危険度まとめ

| 箇所 | 用途 | 危険度 | 対処方針 |
|------|------|--------|----------|
| UnifiedBufferManager_Build | GPUバッファ構築 | 低 | そのまま（ローカル座標） |
| UnifiedRenderer | ワイヤフレーム描画 | **高** | GPU変換後座標を使用 |
| Preview - 面ハイライト | 2D描画 | 中 | displayMatrixに統合 |
| Preview - 頂点インデックス | 2D描画 | 中 | matrixに統合 |
| MoveTool - 選択中心 | ギズモ位置 | **高** | WorldMatrix変換追加 |
| MoveTool - ドラッグ開始 | 位置記録 | 低 | そのまま（ローカル） |
| MoveTool - エッジ/面中心 | ギズモ位置 | **高** | WorldMatrix変換追加 |

---

## 推奨アーキテクチャ

### A案: GPU変換後座標をCPUに読み戻し

```
[ローカル座標] → GPU変換 → [ワールド座標バッファ]
                              ↓
                         ReadBack to CPU
                              ↓
                    [_worldPositions CPU配列]
```

**メリット**: 既存コードの変更が少ない
**デメリット**: GPU→CPU読み戻しのオーバーヘッド

### B案: 描画は全てGPU、インタラクションはCPU変換

```
[描画]
ローカル座標 → GPU変換 → ワールド座標 → 描画

[インタラクション（ギズモ位置等）]
ローカル座標 → CPU変換（WorldMatrix適用） → ワールド座標
```

**メリット**: 読み戻し不要
**デメリット**: CPU側で変換処理が必要

### C案: PositionProviderインターフェース

```csharp
interface IPositionProvider
{
    Vector3 GetPosition(int meshIndex, int vertexIndex);
    Vector3[] GetAllPositions(int meshIndex);
}

class LocalPositionProvider : IPositionProvider { ... }
class WorldPositionProvider : IPositionProvider { ... }
```

**メリット**: 切り替えが容易、テストしやすい
**デメリット**: 設計変更が大きい

---

## 推奨: B案 + 一部C案

1. **GPUバッファ構築**: ローカル座標のまま
2. **GPU描画**: Compute Shaderでワールド変換
3. **CPU側（ギズモ等）**: ヘルパーメソッドで変換
   ```csharp
   // MeshContext拡張
   public Vector3 LocalToWorld(Vector3 localPos) 
       => WorldMatrix.MultiplyPoint3x4(localPos);
   
   public Vector3 WorldToLocal(Vector3 worldPos)
       => WorldMatrix.inverse.MultiplyPoint3x4(worldPos);
   ```
4. **MoveTool等**: 表示モードに応じて変換を適用
   ```csharp
   Vector3 pos = meshObject.Vertices[vi].Position;
   if (displayMode == World)
       pos = meshContext.LocalToWorld(pos);
   ```

---

## 影響を受ける主要ファイル

| ファイル | 変更内容 |
|---------|----------|
| MeshContext.cs | WorldMatrix, LocalToWorld(), WorldToLocal() 追加 |
| UnifiedRenderer.cs | GPU変換後バッファ使用 or 変換行列適用 |
| SimpleMeshFactory_Preview.cs | displayMatrixにWorldMatrix統合 |
| MoveTool.cs | 表示モードに応じた座標変換 |
| ToolContext.cs | 表示モード情報追加 |

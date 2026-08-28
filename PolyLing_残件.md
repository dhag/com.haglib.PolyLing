# PolyLing 残件一覧

最終更新: 2026-08-27（1-1 / 1-2 解決・回避済）

このファイルは、調査の過程で見つかったが未着手・保留にした項目をまとめたもの。
新たに残件が出たら末尾の分類に追記する。

---

## 0. 追記ルール

- 1 項目につき **場所（file:line）／症状／確認済みの事実／未確認の点** を書く
- 「未確認の点」を空欄にしない。断定できないことは断定できないと書く
- 対処したら削除せず「済」に変更し、対処内容と日付を残す

---

## 1. 最優先（1-1 / 1-2 は解決済。1-3 / 1-4 が未解決）

### 1-1. Player ビルドのクラッシュ 【原因特定・回避済】 2026-08-27

**症状**
モデルを読み込んだ状態でカメラ操作を繰り返すと、数十秒後にネイティブクラッシュする。

**原因（クラッシュダンプで確定）**

Unity の D3D12 実装の不具合。**PolyLing のコードではない。**

```
UnityPlayer.dll!D3D12ScratchAllocator::DestroyScratch(int)          ← アクセス違反
UnityPlayer.dll!D3D12ScratchAllocator::ReleaseExcessScratch(uint,uint)
UnityPlayer.dll!D3D12ScratchAllocator::ReclaimMemory(bool)
UnityPlayer.dll!GfxDeviceD3D12::QueueExecute(...)
UnityPlayer.dll!GfxDeviceD3D12::FlushInternal(bool)
UnityPlayer.dll!GfxDeviceD3D12::EndCurrentRenderSubPass()
UnityPlayer.dll!GfxDeviceD3D12::EndRenderPassImpl()
UnityPlayer.dll!GfxDevice::EndRenderPass()
UnityPlayer.dll!GfxDeviceWorker::RunCommand(ThreadedStreamBuffer&)
UnityPlayer.dll!GfxDeviceWorker::RunGfxDeviceWorker(void*)          ← レンダースレッド
UnityPlayer.dll!Thread::RunThreadWrapper(void*)
```

| 項目 | 値 |
|---|---|
| 例外 | `0xC0000005` ACCESS_VIOLATION（読み取り） |
| 参照アドレス | `0x0000000000005DBD`（null 近傍 + オフセット 24,013） |
| 発生コード | `UnityPlayer.dll + 0x95B0A7` |
| 発生スレッド | `GfxDeviceWorker`（レンダースレッド。メインスレッドではない） |

**機構**

メインスレッドがフレーム途中でコマンドキューのフラッシュを起こすと、
レンダースレッド側の D3D12 一時メモリ（スクラッチ）回収処理が破綻する。

PolyLing が行っているのは「フラッシュを起こすこと」だけ。
`ComputeBuffer.GetData` はフラッシュ＋GPU 完了待ちを行うため、
ホバー判定・表示用カリングのたびにフレーム途中のフラッシュが発生していた。

**切り分けの経過（すべて実測）**

| 検証 | 結果 |
|---|---|
| `getdata=0`（GetData を全停止） | 落ちない |
| `getdata=0 flush=1`（GL.Flush のみ、待たない） | **落ちる** → 待機ではなくフラッシュが引き金 |
| `getdata=0 flush=2`（フレーム開始時に 1 回だけ） | 落ちる（頻度は下がる） → 1 回でも落ちる |
| 読み戻し回数の削減 / カメラ台数削減 / RenderMesh 置換 / ドライバ更新 | いずれも頻度が下がるだけ |
| **グラフィックス API を D3D11 に変更** | **落ちない** |

**回避策（適用済み）**

`Edit > Project Settings > Player > Other Settings`

1. `Auto Graphics API for Windows` のチェックを外す
2. `Graphics APIs for Windows` に `Direct3D11` を追加
3. `Direct3D11` を `Direct3D12` より上へ移動

`D3D12ScratchAllocator` が 1 度も実行されなくなる。

**未確認の点**

- Unity のバージョンを上げれば直るか（Unity 6000.3.12f1 で発生）
- D3D12 のまま回避する方法があるか
- Unity 側へのバグ報告の要否
- プロセスに注入されているトレンドマイクロの DLL
  （`tmmon64.dll` / `TmUmEvt64.dll` / `TmAMSIProvider64.dll`）が
  関与しているか。未検証

**副作用の確認が必要**

D3D11 に切り替えたことで、Compute シェーダー・GPU バッファ・描画に
差異が出ないかを確認すること。

---

### 1-2. エディタで時々ものすごく遅くなる（当初の相談内容）【解決・回避済】 2026-08-27

**症状**
Update が数秒に 1 度しか呼ばれなくなる。CPU 4%（≒1 コア分）、GPU 2%、
ディスク 0%、メモリ余裕あり。タスクマネージャの待機チェーンは
「ネットワーク I/O の終了を待機」と表示。
Unity エディタの再起動で改善することもあれば、PC 再起動が必要なこともあった。

**原因**
**1-1 と同一。** Unity の D3D12 実装の不具合
（`D3D12ScratchAllocator::DestroyScratch` でのアクセス違反）。

**確認の経過**

| 検証 | 結果 |
|---|---|
| `cull=0 hit=0`（フラッシュを起こす 2 経路を停止） | エディタでも軽快に動く |
| **グラフィックス API を D3D11 に変更** | **解消。快調** |

Player 側と同じ切り替えでエディタ側も解消した。
これにより、当初の相談内容と Player クラッシュが同一原因であることが確定した。

**回避策（適用済み）**
Player 側と同じ。`Project Settings > Player > Other Settings` で
`Auto Graphics API for Windows` を外し、`Direct3D11` を `Direct3D12` より上へ。

エディタが Player Settings に従わない場合は、起動オプションに
`-force-d3d11` を付ける。

**残っていた疑問（解消済み）**

- 待機チェーンが「ネットワーク I/O」と表示した理由は未解明のまま。
  ただしレンダースレッドの待機がそう分類されたものと整合する。
  実害が消えたため追わない。

---

### 1-3. ミラーの頂点移動が反映されない

**症状**
頂点を移動してもミラー側のメッシュに反映されない。

**確認済みの事実**

- `readback: false` の差し戻し後も再現するため、その変更が原因ではない
- 診断スイッチはすべて通常動作（`xform=0 rebuild=1`）
- `G15`（`GetSkinnedMirrorPositions` → `_skinnedMirrorPositionBuffer.GetData`）が
  セッション中 **0 回**。ミラー頂点のワールド座標が CPU に戻っていない
- `WritebackTransformedVertices` は `_expandedPositions`（G14）だけを使って
  `ctx.UnityMesh` を更新しており、ミラー側バッファを参照していない

**場所**
`UnifiedBufferManager_Mirror.cs:226`（`GetSkinnedMirrorPositions`）
`UnifiedSystemAdapter.cs:507-`（`WritebackTransformedVertices`）

**未確認の点**

- `GetSkinnedMirrorPositions()` の呼び出し元が存在するのか、条件を満たさないのか
- `WritebackTransformedVertices` のループが `MeshType.BakedMirror` / `MirrorSide` を
  どう扱っているか
- `_expandedPositions` にミラー頂点が含まれる設計か（`UnifiedBufferManager_Build.cs` 要確認）

---

### 1-4. 不可視データのワイヤだけが表示される

**症状**
MQO から読んだ不可視データのワイヤだけが表示される。2026-08-27 に発生。

**確認済みの事実**

- 空メッシュガード（`MeshSceneRenderer.cs` 3 か所）は条件を**追加**しただけなので、
  描画を増やすことはあり得ない
- `Graphics.DrawMesh` → `RenderMesh` 置換は差し戻し済み

**未確認の点**
原因未特定。差し戻し後に再現するかを確認していない。
ワイヤ側（`UnifiedRenderer` の `MeshInfo` ベースの構築）が
`IsVisible` 相当のフラグをどう見ているかを未調査。

---

## 2. 書き戻し（Writeback）の非効率と欠陥

### 2-1. 意図的に残されたリーク

**場所**
`UnifiedSystemAdapter.cs:637-642`、`:703-704`

```
// 【ここでは破棄しない】
// ここで旧 Mesh を破棄すると矩形選択が一部頂点で効かなくなる不具合が出た。
// リークは残るが、破棄は個別操作の経路 (ReplaceUnityMesh) に限定する。
ctx.UnityMesh = regenerated;
```

**問題**
リークを容認する対処は不可。根本は「描画提出済みの `Mesh` を同一フレーム内で
差し替えている」設計にある。破棄すれば競合し、破棄しなければ漏れる。
どちらも対症療法。

**確認済みの事実**
`MeshContext.cs:44-51` にも同じ経緯が記載されている。
今回の計測セッションでは条件を満たさず、この経路は発動していなかった
（`mesh=152` が一定だった）。

---

### 2-2. `WritebackTransformedVertices` の早期スキップ

**場所**
`UnifiedSystemAdapter.cs:692-693`

```csharp
if (meshObject.VertexCount != vertexCount)
    continue;
```

同 `:622-623`

```csharp
if (regenerated != null && regenerated.vertexCount == expandedVertexCount)
```

**問題**
条件を満たさない場合、`UnityMesh` が古いまま残る、または頂点を書き戻さないまま
`ctx.UnityMesh` に代入される。**でたらめなデータが表示されている可能性がある。**

**未確認の点**
何件スキップしているか、スキップした側がどうなるかを確認していない。

---

### 2-3. `UnityMesh` への直接代入が 41 か所

**確認済みの事実**

- `MeshContext.cs:53` で `DestroyReplacedUnityMesh = true`
- `ReplaceUnityMesh` 経由は 13 か所、直接代入は 41 か所
- `MeshContext.cs:58-63` に危険性が自分のコードで明記されている
  > 直接代入すると旧 Mesh が到達不能なまま常駐し、Undo / ミラー再構築 / 変換の
  > たびに積み上がる（長時間の編集で目に見えて重くなる原因）

**主な箇所**
`UnifiedSystemAdapter.cs:642,704` / `MirrorBranchOps.cs:753` /
`PlayerCommandDispatcher.cs` 8 か所 / `PolyLingPlayerViewerCore.cs:6519,6798,7370` /
各インポータ

**未確認の点**
41 か所のうち、どれが実際にリークするか（同一インスタンス再代入なら漏れない）。

---

## 3. 同期 GPU 読み戻しの構造（1-1 の周辺）

### 3-1. ホバー 1 イベントあたり 5〜6 回の同期読み戻し

**場所**

| 記号 | 場所 | 内容 |
|---|---|---|
| G1 | `UnifiedBufferManager_Update.cs:1032` | `ComputeScreenPositionsGPU` |
| G2 | `:1062` | `DispatchVertexHitTestGPU` |
| G3 | `:1098` | `DispatchVertexSnapHitTestGPU`（現在無効） |
| G4 | `:1127` | `DispatchLineHitTestGPU` |
| G5/G6 | `:1321,1326` | `DispatchFaceHitTestGPU` |

`PlayerViewportPanel.cs:789` が `PointerMoveEvent` ごとに無条件でホバー経路を起動する。
間引きも同一座標判定もない。

**改善案（未着手）**

1. `AsyncGPUReadback` への置き換え
2. 5 つの `Dispatch` をまとめ、結果バッファを 1 本にして読み戻しを 1 回に集約
3. 最近接判定を GPU 側で完結させ、`int` 数個だけ読み戻す（転送量が最小）

現在は「全頂点の距離配列を CPU へ転送 → CPU ループで最小値を探す」形で、
転送量と CPU ループの両方が無駄。

---

### 3-2. 表示用カリングの読み戻しは結果が使われていない

**場所**
`UnifiedSystemAdapter.cs:885`（`cullDisplay`）、`MeshSceneRenderer.cs:563`（`cullSubmit`）

**確認済みの事実**

- 両方とも後続は `DispatchFaceVisibilityGPU` / `DispatchLineVisibilityGPU` /
  `DispatchApplyMirrorCullGPU` の 3 つだけで、GPU 内で完結する
- `_screenPositions` は単一のグローバル配列。4 ビューポート分読み戻しても
  最後の 1 回しか残らない（`PlayerViewportManager.cs:1065-1074` の記述が裏付け）
- **1,381 回のうち約 4 分の 3 は結果が捨てられている**

**注意**
2026-08-27 に `readback: false` で削減を試みたが、クラッシュは直らず、
副作用の疑いが出たため**差し戻した**。効率改善としては有効だが、
クラッシュ対策にはならない。再着手する場合は副作用の検証を先に行うこと。

---

### 3-3. 同一イベント内でスクリーン座標を 2 回計算している

**場所**
`UnifiedMeshSystem_Process.cs:129`（CPU 版 `ComputeScreenPositions`）
→ 直後に `ProcessMouseUpdate` が GPU 版で再計算して読み戻す

全頂点ぶんの投影計算を CPU と GPU で二重に行っている。

---

## 4. データ分類の欠陥

### 4-1. 空オブジェクトが `DrawableMeshes` に載る

**確認済みの事実**

- `TypedMeshIndices.cs:260-310` の分類は `MeshType` のみを見ており、頂点数を見ていない
- `MeshType.Group`（値 6）は `Drawable` に入らない設計だが、
  **`MeshType.Group` を設定する箇所がコード全体に 0 件**
- `MQOImporter.cs:1175` で `meshObject.Type = MeshType.Mesh;` と固定している
- MQO の階層グループ（頂点 0 の入れ物）が `MeshType.Mesh` のまま `Drawable` に載る
- 実測 46 件（頂点 18,992 のモデル）。`moVtx=0 moFace=0`

**対処済み**
描画側にガードを追加（`MeshSceneRenderer.cs:409, 1043, 1111` に `vertexCount <= 0`）。
**発生源は未対処。**

**修正案（未決定）**

- A: インポータで頂点 0・面 0 のオブジェクトに `MeshType.Group` を設定
- B: `TypedMeshIndices` の分類に頂点数を加える
- C: 描画側ガードのみで済ませる（GPU バッファは空オブジェクト分を確保し続ける）

**未確認の点**

- `MeshCategory.Group` の UI 表示が実装されているか
- MQO 以外（PMX / OBJ）でも同じ空オブジェクトが発生するか
- `MeshContext.Type`（`MeshContext.cs:774`）が `MeshObject.Type` に委譲しているか

---

## 5. 初期調査で見つけて保留にしたもの

### 5-1. edit mode で効かない `Object.Destroy`

**場所**

| ファイル:行 | 対象 |
|---|---|
| `PlayerViewport.cs:96` | `_camGo`（`Cam.enabled = true` のカメラ） |
| `PlayerViewport.cs:177` | `RT` |
| `PrimitivePreviewViewport.cs:58,119,147,149` | 各種 |
| `PolyLingPlayerViewerCore.cs:6494,6773,7345` | `Mesh` |

**問題**
`PolyLingEditorWindow.cs:15` は `EditorWindow` なので Player コアは edit mode でも動く。
edit mode の `Object.Destroy` はオブジェクトを破棄しない。

`MeshContext.cs:78-84` に `isPlaying` で分岐して `DestroyImmediate` を使う正しい実装
（`DestroyMesh`）があるのに、上記はそれを使っていない。

---

### 5-2. 死んだ経路の無条件 `Debug.LogError`

**場所**
`UnifiedMeshSystem_Process.cs:189`

```csharp
public void ProcessHoverOnly(Vector2 mousePosition)
{
    _mousePosition = mousePosition;
    ProcessMouseUpdate(cpuOnly: true);
    Debug.LogError("cpuOnly: trueは禁止");   // ← 無条件
}
```

Player 経路からは呼ばれていない（`PlayerViewportManager.cs:1391-1395` に
不使用の理由が明記）。現状は死んだ経路。

---

### 5-3. `PolyLingEditorWindow.cs:62` の無条件 `Repaint()`

`OnEditorUpdate` が毎回 `Repaint()` を呼んでいる。

---

### 5-4. `Debug.Log` が 813 か所

うち改行を含むものが 25 か所。

**関連事象**
Unity Console の `StacktraceWithHyperlinks` が `ArgumentOutOfRangeException` を投げ、
それ自体が Console に記録されて自己増殖するループが観測された。
Console をクリアすると一時的に解消するが、発生は止まらない。

**未確認の点**
どのログ行がパーサ破綻の引き金かは特定していない。

---

## 6. 死んだコード・未使用

| 場所 | 内容 |
|---|---|
| `UnifiedBufferManager_Update.cs:1262,1277` | `DebugPrintCullingStats` — 呼び出し元 0 件。`GetData` を 2 回持つ |
| `BufferManagerBase.cs:52,68` | `CreateBuffer<T>` / `CreateBuffer` — 呼び出し元 0 件 |
| `UnifiedBufferManager_Mirror.cs:262` | `_mirrorScreenPosBuffer` が `ReleaseAllBuffers` の一覧から漏れている（専用の解放処理はある） |

---

## 7. シェーダー関連（Player ビルド）

### 7-1. `Shader.Find` のフォールバックに null チェックが無い

**場所**
`MaterialDataConverter.cs:125-128`

```csharp
shader = Shader.Find(SHADER_URP_LIT) ?? Shader.Find(SHADER_STANDARD);
// ↓ null チェック無し
var mat = new Material(shader);
```

**確認済みの事実**

- `SHADER_STANDARD`（`"Standard"`）は URP ビルドでは必ず null（`:196`）
- Player ビルドでは `Shader.Find` はビルドに含まれるシェーダーしか返さない

**対処済み（設定側）**
`Project Settings > Graphics > Always Included Shaders` に URP の
`Lit` / `Unlit` / `SimpleLit` を追加して起動するようになった。

**未対処（コード側）**
フォールバックが null のまま `new Material(shader)` に渡す欠陥は残っている。

---

## 8. 診断コードの撤去（調査完了後に実施）

以下は調査専用。恒久コードではない。**クラッシュ解決後にすべて削除すること。**

| ファイル | 内容 |
|---|---|
| `PLCamDbg.cs`（新規） | ログ出力とスイッチ読み込み |
| `PLResStat.cs`（新規） | リソース生存数カウント |
| `PLMeshValidator.cs`（新規） | メッシュ検証 |
| `UnifiedBufferManager_Update.cs` | `G1`〜`G14` のマーク、`getdata` ガード、`NewCB` 包み |
| `UnifiedBufferManager_Mirror.cs` | `G15`、`NewCB`、`LiveCB--` |
| `BufferManagerBase.cs` | `G16`、`NewCB`、`LiveCB--` |
| `UnifiedBufferManager.cs` | `NewCB` 40 か所、`LiveCB--`、`PLResStat` 計上 |
| `UnifiedSystemAdapter.cs` | `C1`〜`C6`、`WB`、`PLResStat` 計上、`cull` スイッチ |
| `UnifiedMeshSystem_Process.cs` | `dbgSrc` 引数 |
| `MeshSceneRenderer.cs` | `SubM`、`EmptyMesh`、`adapter`/`xform` スイッチ、`PLResStat` 計上 |
| `UnifiedRenderer.cs` | `SubW`、`PLMeshValidator.Check` |
| `PlayerViewportManager.cs` | `S0`〜`S6`、`cams`/`wire` スイッチ |
| `PolyLingPlayerViewer.cs` | `R1`/`R2`、`F1`/`F2`、`ReportStat` |
| `PolyLingPlayerViewerCore.cs` | `LoadDbg 01`〜`18`、`_loadDbgSubmitLeft` |
| `PlayerViewport.cs` | `RT create` / `RT release` |
| `UpdateMode.cs` | `FromMode` のスイッチ上書き |
| `MeshBridgeDefault.cs` | `LiveMesh++` 4 か所 |
| `MeshContext.cs` | `LiveMesh--` |

**残すべきもの（恒久修正）**

- `MeshSceneRenderer.cs:409,1043,1111` の `vertexCount <= 0` ガード
  ※ ただし 4-1 の発生源を直せば不要になる可能性がある

**削除済み**

- `PLRenderMeshHelper.cs`（`RenderMesh` 差し戻しに伴い不要）

---

## 9. 差し戻した変更（記録）

| 変更 | 理由 | 日付 |
|---|---|---|
| `Graphics.DrawMesh` → `Graphics.RenderMesh`（10 か所） | クラッシュに効果なし。副作用の疑い | 2026-08-27 |
| `ComputeScreenPositionsGPU` の `readback` 引数 | クラッシュに効果なし。副作用の疑い | 2026-08-27 |

---

## 10. 環境情報（参考）

| 項目 | 値 |
|---|---|
| Unity | 6000.3.12f1 |
| GPU | NVIDIA GeForce RTX 4070 Laptop GPU |
| ドライバ | 更新済み（更新前は 31.0.15.3141 / 2023-03-16） |
| CPU 論理コア数 | 24 |
| RAM | 64 GB |
| VRAM | 8 GB |
| Managed Stripping Level | Disabled |
| 常駐 | トレンドマイクロ系 8 プロセス |



忘備録

・ランタイムで漢字がでないという不具合あり：解決。
　ただしエディタ拡張で下記は未対応
----
Editor/CreateRuntime/CreatePlayerViewer.cs:288 は今後も Text Settings 未設定の PanelSettings を作り続ける。ここに、プロジェクト内の PanelTextSettings を検索して自動割当する処理を入れれば同じ問題は再発しない。

手順2のメニュー表記が不明な件も含めて、FontAsset と PanelTextSettings をプログラムから生成する Editor スクリプト（FontAsset.CreateFontAsset を使用）を用意することもできる。この場合メニュー探しは不要になる。
----

・「図形生成」ツールででマテリアルを設定できるようにしたい。：解決

・「図形生成」「高度な図形」「文字」でフォントが存在するフォルダを指定したい。複数のフォルダを指定したい。：解決

・不可視オブジェクトもGPUに送って計算だけしてるようだ（未確認）。効率悪い。空オブジェクトが DrawableMeshes に載る、はブロック済みだが別件。ブロックの効率も不明。

・頂点IDの拡張、SubID,PartsID




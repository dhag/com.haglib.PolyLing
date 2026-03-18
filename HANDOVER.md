# PolyLing Runtime移行 引継ぎ書

作成日: 2026-03-15

---

## 移行方針（最重要）

### 目標
- 全機能をUnityランタイム（スタンドアロンビルド）で動作させる
- 近い将来、**メインパネル（PolyLing EditorWindow）もランタイム化する**
- Editor固有部分（AssetDatabase書き込み、EditorWindow GUI）は  
  **リモートBridge（WebSocket）経由でEditorに委譲する**

### 設計原則
```
[Runtime / スタンドアロン]              [Editor]
  PolyLingCore (MonoBehaviour)    ←→   RemoteServer (WebSocket)
  PanelContext / PanelCommand              ↓
  全ロジック                         EditorBridgeImpl
  GPU描画 (UnifiedSystemAdapter)         AssetDatabase書き込み
  ネットワーク                            EditorWindow GUI
```

**PanelCommand / PanelContext は既にEditor非依存**。  
ロジック（CommandHandlers群）も既にEditor非依存。  
残る課題はUIレイヤー（EditorGUILayout）とファイルI/Oのみ。

---

## 現在の作業状態

### 完了（コード変更済み・適用待ち）

| ファイル群 | 作業 | ZIPファイル |
|---|---|---|
| **カテゴリB** 14ファイル | `using UnityEditor` 削除、`PreviewRenderUtility` を `#if UNITY_EDITOR` でラップ、`PolyLing_WorkPlane.cs` の `UnityEditor.Selection` → `PLEditorBridge` 置換 | `CategoryB_using_removed.zip` |
| **カテゴリH,I,K** 4ファイル | `PolyLing_SummaryNotify.cs` の RemoteServer EditorWindow取得を `#if UNITY_EDITOR` でラップ、PMX/MQOインポーター/エクスポーターの AssetDatabase 読み取りを `#if UNITY_EDITOR` でラップ | `Category_HiK.zip` |
| **その他** 3ファイル | `MaterialListPanel.cs`（DragAndDrop/ObjectField を `#if UNITY_EDITOR`）、`AxisGizmo.cs`（EditorStyles を `#if UNITY_EDITOR`）、`EditorBridgeNull.cs`（変更なし） | `Category_misc.zip` |

### 完了（変更不要・移動のみ）

以下は **そのままRuntimeフォルダに移動すれば動作する**（コード変更不要）：

- **RUNTIME_OK** 253ファイル（Editor依存ゼロ確認済み）
- **BRIDGE_ONLY** 10ファイル（PLEditorBridge.I経由のみ、EditorBridgeNullがRuntime時のstub）
- `CommandQueue.cs`, `EditorBridge.cs`, `IEditorBridge.cs`
- `RemoteServerCore.cs`, `UndoManager.cs`, `UndoStack.cs`, `VMDPlayer.cs`（既に `#if UNITY_EDITOR` 実装済み）

**計 約270ファイルが移動のみで対応可能。**

---

## 残件一覧

### 残件1: ViewportCore.cs（最重要）
**ファイル:** `Poly_Ling_Main/UI/MeshListPanelV2/ViewportCore.cs`  
**問題:** `Handles.BeginGUI/color/DrawAAPolyLine/EndGUI` を50箇所直接呼び出し  
**解決策:** `UnityEditor_Handles`（= `GLGizmoDrawer` の static alias、GL.*ベースで実装済み）に全置換  
**作業:** 機械的置換のみ。`using static Poly_Ling.Gizmo.GLGizmoDrawer;` は既にある。  
**注意:** `GLGizmoDrawer.cs` はEditor依存ゼロで既にRuntime移動可能。

### 残件2: IEditToolのDrawSettingsUI分離
**対象:** 約50のToolファイル（MoveTool, SelectTool, EdgeBevelTool等）  
**問題:** `IEditTool.DrawSettingsUI()` が `EditorGUILayout.*` を呼び出す  
**将来方針:** メインパネルをRuntimeに移す際にUI描画をuGUI/UIToolkitに全面移行する。  
**現時点での推奨:** 触らない。`DrawSettingsUI()` はUIレイヤー。メインパネルUI移行時に同時に対処する。  
**絶対にやってはいけないこと:**  
- `DrawSettingsUI()` 内を `#if UNITY_EDITOR` でラップして移植しようとすること
- → ロジック部分と描画部分が混在しており、ラップでは解決しない

### 残件3: ParameterUndoHelper.cs / ParameterUndoHelper_A.cs
**問題:** `UnityEditor.EditorGUI.DisabledScope` を使用（UIレイヤーのみ）  
**解決策:** `#if UNITY_EDITOR` でラップするだけ（2ファイル各2箇所）  
**作業規模:** 小さい。単独で対処可能。

### 残件4: MuscleValueCopier.cs
**問題:** `UnityEditor.EditorGUI.PropertyField/GetPropertyHeight` を使用  
**解決策:** `#if UNITY_EDITOR` でラップ  
**作業規模:** 小さい。2箇所のみ。

### 残件5: VMDTestPanel.cs
**問題:** `EditorUtility.OpenFilePanel/DisplayDialog`、`SceneView.RepaintAll`  
**解決策:** `#if UNITY_EDITOR` でラップ（ファイル選択ダイアログ部分のみ）  
**作業規模:** 小さい。

### 残件6: partial class PolyLingのランタイム化（最大の作業）
**現状:** `PolyLing : EditorWindow` がpartial classで以下に分かれている

| ファイル | 内容 | Runtime化 |
|---|---|---|
| `PolyLing.cs` | EditorWindow本体、CreateGUI、OnEnable | **Editor残留**（GUIレイヤー） |
| `PolyLing_GUI*.cs` (4ファイル) | EditorGUILayout描画 | **Editor残留** |
| `PolyLing_Model.cs` | モデルロード/セーブGUI | **Editor残留** |
| `PolyLing_CommandHandlers_*.cs` (5ファイル) | PanelCommand処理 | **移動可能**（RUNTIME_OK確認済み） |
| `PolyLing_BoneInput.cs`, `PolyLing_Selection.cs` | 入力ロジック | **移動可能** |
| `PolyLing_SummaryNotify.cs` | PanelContext管理（修正済み） | **移動可能** |
| `PolyLing_ActiveMesh.cs`, `PolyLing_SelectionSets.cs` | ロジック | **移動可能** |

**移行設計:**  
```csharp
// 新設クラス（Runtime）
public class PolyLingCore : MonoBehaviour {
    // _model, _project, _undoController, _toolContext を保持
    // DispatchPanelCommand, NotifyPanels を保持
    // CommandHandlers_* の内容をここに移す
}

// Editor残留
public partial class PolyLing : EditorWindow {
    [SerializeField] private PolyLingCore _core;
    // GUIのみ: CreateGUI, OnGUI系
    // PolyLingCore のイベントを受けてRepaint()
}
```

**注意:** `_model`（ModelContext）が現在 PolyLing の private フィールド。  
partial class 分割で `PolyLing_CommandHandlers_*` がこれを参照している。  
PolyLingCore への移行時に **フィールドの所有権を移す必要がある**。

### 残件7: ファイルI/O系（ASSET_WRITE / DIRECT_ASSET_WRITE）
**対象ファイル:**
- `PolyLing_FileIO.cs`: `AssetDatabase.CreateAsset/SaveAssets` を直接呼び出し
- `PolyLing_MeshSave.cs`: 同上  
- `PolyLing_ModelExport.cs`: 同上  
- `PolyLing_MeshLoad.cs`: `AssetDatabase.LoadAssetAtPath`

**将来方針:** RemoteBridge経由でEditorに委譲する。  
具体的には:
1. Runtime側: ファイルパス文字列を引数に取る純粋ロジック関数のみ保持
2. Editor側: ダイアログ表示 → パス取得 → Runtime関数を呼ぶ薄いラッパー
3. または: `IEditorBridge` に `SaveMesh(MeshObject, string path)` 等を追加してBridge化

**現時点での推奨:** これらのファイルは **Editor残留のまま** にして、  
Runtimeからは `IEditorBridge` 経由でのみアクセスする設計に段階的に移行する。

### 残件8: asmdef構成
現時点でasmdefが存在しない。Runtime移行後に必要になる。

```
Packages/com.haglib.polyling/
  Runtime/                              ← com.haglib.polyling.runtime.asmdef
    （Runtimeに移動した全ファイル）
  Editor/                               ← com.haglib.polyling.editor.asmdef
    references: ["com.haglib.polyling.runtime"]
    includePlatforms: ["Editor"]
    （Editor残留ファイル）
```

**重要:** asmdefを追加した瞬間に、Editorコードから見えなくなる型が発生する。  
**必ず全ファイルの移動が完了してからasmdefを追加すること。**  
途中でasmdefを追加すると今回と同じ大量のコンパイルエラーが発生する。

---

## やってはいけないこと（過去の失敗から）

1. **asmdefを先に追加してはいけない**  
   → 型の重複・参照切れが大量発生する。移動完了後に1回だけ追加する。

2. **不用意に型を複製してはいけない**  
   → `CameraSnapshot`, `TPoseBackup`, `MorphExpressionDTO` を両アセンブリに置いたことで型不一致エラーが多発した。型は1か所にしか存在させない。

3. **GUI（EditorGUILayout）を含むメソッドをそのままRuntimeに持ってきてはいけない**  
   → `#if UNITY_EDITOR` でラップしても `UNITY_EDITOR` はRuntimeアセンブリでは定義されないため機能しない。GUIコードはEditorに残すか、UIToolkit/uGUIに書き直す必要がある。  
   （例外: `UnityEditor_Handles` = GLGizmoDrawer aliasはRuntime動作可能）

4. **CPUヒットテストはPMXモデルには実用不可**  
   → 数万頂点で1フレーム20秒以上かかる。GPU計算シェーダ（UnifiedCompute.compute）が必須。

5. **PreviewRenderUtilityはEditor専用**  
   → RemoteViewportCore, UnifiedRenderer, UnifiedSystemAdapterで使用。Runtime時は `#if UNITY_EDITOR` でラップ済み。

---

## 現在の技術的な正しい状態

```
Editor/
  Poly_Ling_Main/
    Poly_Ling_Editor/   ← partial class PolyLing (EditorWindow) - Editor残留
      PolyLing.cs, PolyLing_GUI*.cs, PolyLing_Model.cs ...
      CommandHandlers_*.cs ← ロジックのみ、将来PolyLingCoreに移動
    Core/               ← データ・ロジック・GPU描画 - Runtime移動対象
    Selection/          ← Runtime移動対象
    Tools/              ← ロジック部分はRuntime移動対象、DrawSettingsUIはEditor残留
    UI/                 ← EditorWindowパネル群 - Editor残留（将来uGUI化）
    Poly_Ling_Render/   ← GPU描画 - Runtime移動対象（Editor依存ゼロ確認済み）
    Poly_Ling_Remote/   ← ネットワーク - Runtime移動対象
  Resources/
    UnifiedCompute.compute ← Runtime/Resources/に移動必要
  Shaders/
    MeshFactory*.shader    ← Runtime/Resources/に移動必要
```

---

## 次のステップ（推奨順）

1. 上記「完了・移動のみ」270ファイルをRuntimeフォルダに移動（コード変更なし）
2. `UnifiedCompute.compute` と `MeshFactory*.shader` を `Runtime/Resources/` に移動
3. `ViewportCore.cs` の `Handles.*` → `UnityEditor_Handles.*` 置換（残件1）
4. `ParameterUndoHelper.cs/A.cs`、`MuscleValueCopier.cs`、`VMDTestPanel.cs` の小修正（残件3-5）
5. asmdef追加（全移動完了後に1回のみ）
6. `PolyLingCore` 設計・実装（残件6）- **最大作業、慎重に**
7. RemoteBridge経由でのEditor委譲設計（残件7）


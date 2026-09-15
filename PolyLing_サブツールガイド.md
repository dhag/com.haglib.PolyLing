--------------------------------------------
# サブツール（図形生成）作成のガイド

図形を 1 つ足す、カテゴリを 1 つ足す。そのとき踏む行を全部書いてある。
**全行を踏むまで未完了。** 行番号は書かない（ファイルが分割されるたびに腐るため）。
場所はファイル名とシンボル名で指す。

---

## 大原則

**検査を通せ。文書を信じるな。**

| 検査 | 見るもの |
|---|---|
| `queryCommandAudit` | 引数の付け忘れ、action の衝突、未対応の型、説明の壊れ |
| `queryUiAutomationAudit` | 未登録のセクション、安全度未指定のボタン、**未登録の部品** |

どちらも全項目 0 でなければ終わっていない。
`queryUiAutomationAudit` は「今そこに組み上がっている右ペイン」しか見ない。
図形ごとに諸元 UI を組み直す図形生成パネルでは、**その図形を選んだ状態で検査すること**。
選んでいない図形の未登録は検査に出ない（`UiAutomationAudit.CollectStrayElements` は
登録済みパネルの実体を辿るだけ）。

---

## A. 図形を 1 つ足す

| # | 場所 | 内容 | 忘れると |
|---|---|---|---|
| 1 | `PlayerPrimitiveMeshSubPanel.Shapes.cs` `ShapeKind` | **末尾**に追加 | — |
| 2 | 同 `ShapeKeys` | `ShapeKind` と同じ並び・同じ文字列 | `_shapeBtns` の添字とずれる |
| 3 | 同 カテゴリ配列（`BasicShapes` / `AdvancedShapes` / `MechanismShapes` / `MechanismBShapes` / `SpringBoneShapes` / `SandboxShapes`） | どれか 1 つへ | ボタンが出ない |
| 4 | 同 `RebuildSettings` の `switch` | `BuildXxxUI(_settingsContainer)` | 諸元 UI が「未対応」になる |
| 5 | `PlayerPrimitiveMeshSubPanel.Naming.cs` `Name()` / `SetName()` | 名前欄の読み書き | 非重複名の候補が効かない |
| 6 | `PlayerPrimitiveMeshSubPanel.Command.cs` `BuildCreateCommand` | `new CreateXxxCommand(mi, _xxxP, pl)`。`pl` を必ず渡す | 生成ボタンが何もしない |
| 7 | `PanelCommand.Primitive.cs` | `CreatePrimitiveMeshCommand` 派生。`ShapeName` は `ShapeKeys` と同じ文字列 | MCP から作れない |
| 8 | `PrimitiveMeshFactory.Generate` の `switch` | 生成器の呼び出し | 未登録警告で `null` が返る |
| 9 | `PrimitiveMeshTexts.cs` `Texts` | 図形名・各行ラベル・ヒント・派生諸元 | キーがそのまま画面に出る |
| 10 | 派生諸元を出す図形は `PlayerPrimitiveMeshSubPanel.Mechanism.cs` `RefreshMechInfo` の `switch` | `RefreshXxxInfo()` | 情報ラベルが他図形のまま固まる |
| 11 | 前提がある図形は `CanGenerate()` | 生成できない条件 | 生成ボタンが押せてしまう |
| 12 | 検査 | 上の 2 つとも全項目 0 | — |

生成器は `Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/`（機構部品は `Gears/` 配下）。

---

## B. 諸元 UI の組み方（ここを外すと MCP から触れない）

**諸元 UI の部品は `PlayerPrimitiveMeshSubPanel.Rows.cs` の行ヘルパだけで組む。**

| ヘルパ | 部品 |
|---|---|
| `SR(Lx, min, max, get, set)` | スライダ＋数値欄 |
| `IR(Lx, min, max, get, set)` | 整数スライダ＋数値欄 |
| `TR(Lx, get, set)` | チェック |
| `DD(Lx, choices, get, set)` | **選択式（ドロップダウン）** |
| `V3F` / `SB` | 3 成分の数値欄 / 小ボタン |
| `NF` | 名前欄 |
| `SL` / `GearHint` / `ShapeTitle` / `Sep` | 見出し・説明・区切り（操作部品ではない） |

これらは `RowTarget.Add(ラベルのキー, 部品, 表示文字)` を通り、`UiDynamicControls` へ載る。
載ったものだけが `uiDescribe` / `uiGetValue` / `uiSetValue` から触れる。

### 禁止

- **`new DropdownField(...)` / `new Toggle(...)` / `new Button(...)` を直に作って親へ `Add` する。**
  コンパイルは通り、画面にも出て、人は操作できる。しかし MCP からは存在しないのと同じで、
  `queryUiAutomationAudit` の「未登録の部品」に落ちる。
- **行を作る補助関数を `static` にする。** `RowTarget` はインスタンスのプロパティなので、
  `static` にした時点で登録が構造的に不可能になる。補助関数はインスタンスメソッドで書く。

### 行ヘルパで足りないとき

新しい種類の部品が要るなら、その場で作らず `Rows.cs` にヘルパを足す。
`DD` がその例（`OrientationDD` が生の `DropdownField` を返していたため、
歯車系全図形の「配置面」が MCP から変えられなくなっていた）。

### ID の重複

ID は「ラベルのキー」。同じ図形の中で同じキーを 2 回使うと、後から登録した方しか引けない。
別の意味の行には別のキーを与える。

---

## C. カテゴリを 1 つ足す

配列を 1 本足すだけでは済まない。7 か所。

| # | 場所 | 内容 | 忘れると |
|---|---|---|---|
| 1 | `PlayerPrimitiveMeshSubPanel.Shapes.cs` | `ShapeCategory` に追加／図形配列／`ShapesOf`／`RememberedShape`／`LoadShapeMemory`／`CategoryOf`／`Select`／`_lastXxx` フィールド | 切り替え・記憶が効かない |
| 2 | `PrimitiveShapeMemory.cs` | `Entry` の保存欄／`Set` の `switch`／`NameOf` の `switch` | 起動をまたぐと選択が戻る |
| 3 | `PlayerLayoutRoot.LeftPaneButtons.cs` | ボタンのプロパティと `MakeBtn` | ボタンが出ない |
| 4 | `PolyLingPlayerViewerCore.Panels.cs` | `ShowLiveXxxPrimitivePanel`（`SetInteractionMode` → `ShowRightPanel` → `SetCategory`） | 開けない |
| 5 | `PolyLingPlayerViewerCore.Layout.Panels.cs` | `clicked += ShowLiveXxxPrimitivePanel` | 押せない |
| 6 | `PolyLingPlayerViewerCore.UiAutomation.cs` | `RegisterUiPanel("primitiveXxx", …)` | MCP から開けない／監査に出ない |
| 7 | `PrimitiveMeshTexts.cs` | `ShapeCategoryXxx` | — |

**`ShapeCategory` の列挙値の名前は `PrimitiveShapeMemory` の保存欄（JSON のキー）と対応する。**
表示名を変えるときは列挙値ではなくテキスト辞書と左ペインのボタン文字を変える
（例：「機構部品」→「機構部品A」は `ShapeCategory.Mechanism` を残したまま行った）。

右ペインのセクションは図形生成で 1 枚（`LivePrimitiveSection`）を共用する。
カテゴリごとに `RegisterUiPanel` で別パネル ID を付けるが、セクションは同じものを渡す。

---

## D. 3D ビュー表示・姿勢の成立条件

- 黄色ワイヤは `LiveWireInMainViewport` が立つインスタンスだけ。立っているのは
  3D 連携とサンドボックス。ショートカット経路 `ShowPrimitiveShape` が開く `_primitiveSubPanel`
  は立っていない → **表示確認は 3D 連携パネルで行う**
- `Generate(true)` が `null` なら出ない
- プレビュー中の例外は `catch { }` で消える → 原因調査は `polyling_call` の失敗理由で行う
- 姿勢は `CurrentPlacement()` 経由でしかコマンドに載らない
- パネル既定は回転ベイク OFF・スケールベイク ON。`PrimitivePlacement.Default` は回転ベイク ON。
  **パネル経路で `Default` を使わない**
- 姿勢フォールドを出す図形は必ず生成へ反映する。反映しない図形は `PoseApplicable` と
  `RefreshCommonUiVisibility` で隠す。材質は `ShapeUsesMaterialSlot` で同様

---

## E. 禁止事項

- 表の一部だけ実施して完了と報告
- パネルからファクトリ／Ops を呼んでモデルへ直接入れる
- パネル経路で `PrimitivePlacement.Default` を使う
- 生成器内で平行移動・回転・拡大を入れる（ローカル空間でピボット適用まで。
  平行移動・回転・拡大は `PrimitiveMeshFactory` が行う）
- 例外の握りつぶし、`?.Invoke` による未配線の素通りを新たに足す
- 既存図形の丸写しで `Params`・名前・テキストキーを流用したまま残す
- **行ヘルパを通さずに操作部品を作る（B 節）**

---

## F. 完了条件

1. compile errors = 0（3 csproj）
2. `queryCommandAudit` 全項目 0
3. `queryUiAutomationAudit` 全項目 0。**足した図形を選んだ状態で**実行すること
4. `polyling_call` で新コマンドを既定値で実行し成功、vertices/faces > 0
5. 配置付き（`worldPosition`・`placeRotation`・`bakeRotation=false`）で再実行し成功
6. `uiSetValue` で足した行（特に選択式）を実際に変えてみて、値が返ること
7. 目視項目（ユーザー確認）：3D 連携パネルでの黄色ワイヤ、ギズモ追従、原点マーカー／くさび、
   生成後の姿勢。描画オブジェクトの姿勢を返す `PLResult` は無いため MCP では確認できない

## G. 完了報告の書式

A・B・C 各表の全行を「済／未／対象外＋ファイル名:シンボル名」で列挙。
「未」が 1 行でもあれば完了と書かない。
--------------------------------------------------------

# PolyLing モディファイア枠組み計画

1 つのオブジェクトに処理を積み重ねる非破壊の「処理列」と、処理の種類をクラス 1 つで足せる「モディファイアを作る枠組み」を作る。厚み・ベベル・ブーリアン・配列・曲げ／ねじり・格子をこの上に載せ、ミラーとサブディビジョンも同じ操作体系にそろえる。この文書を正本にする。

進め方：1 段＝1 会話。各段の始めにコードを読み直して file:line 付きの変更計画を出し、承認を得てから実装する。終えたら「状態」を更新する。

作成: 2026-11-01。きっかけはエンドユーザーの評価（汎用モディファイアの組み合わせと操作 UI に、支持を妨げる差がある）。

関連: `計画と規約/PolyLing_作り直し整備計画.md`（オブジェクト間の生成スタック）、`計画と規約/PolyLing_モディファイア高速化計画.md`、`PolyLing_モーション編集構造計画.md`。

## 1. 目標

- 処理の種類はクラス 1 つで足せる。作者が書くのは「名前・引数・計算本体」と、2 つの宣言（位置だけか面構成も変えるか／他のどのオブジェクトを参照するか）だけ
- 作者が書かずに済むもの：コマンド、MCP、保存・読込・リモート転送、Undo、引数欄、処理範囲の絞り込み、評価のきっかけ
- 1 つのオブジェクトを「元メッシュ → 処理列 → 結果」で持ち、処理の追加・並べ替え・一時無効化・引数の変更・削除・適用を画面と MCP から行える
- 引数欄は用途に合った操作（数値欄＋スライダー、選択ボタン、オブジェクト選択、チェックボックス）。MCP 用の文字列の引数編集は「詳細」として残す
- 代表的な 3 つの連続作業で、途中変更・Undo・保存再読込まで Blender と比べる

## 2. 現状（2026-11-01 にコードで確認）

### 2.1 使える土台

| もの | 内容 | 根拠 |
| --- | --- | --- |
| 評価器 | action ごとの処理器 `IModifierHandler`（Refresh・SyncDrag・Release）を登録表で持つ。きっかけは GPU バッファ作り直しの入口と頂点ドラッグ中の同期の 2 か所 | `Core/Ops/ModifierEvaluator.cs:14-20, 132-156, 326-353` |
| 処理器の前例 | サブディビジョンが処理器として登録され、ドラッグ中はステンシルで位置だけ追随、面構成が変わったら作り直す | `Core/Ops/SubdivisionSync.cs:1-45`、`ModifierEvaluator.cs:343-346` |
| 依存グラフ | 全スタックの項目の入出力（ObjectId）から導き、依存の順に流す。自分の出力を自分で読む項目は循環として外す | `Core/Ops/ModifierGraph.cs:15, 134-143` |
| 項目の器 | `ModifierStackStep`（ScenarioItemId・Action・Args・MeshRefIds・OutputObjectIds）。保存は CSV（`ModifierStackCsv.cs`）・JSON（`ModifierStackDTO.cs`）・リモート二進（`RemoteProgressiveSerializer.cs`） | `Core/Data/ModifierStack.cs:347-422, 588-601` |
| 引数の宣言 | `PLParam` に型・Min・Max・Step・IsMeshRef・MeshRefSpace。列挙型もそのまま載る | `PanelCommand.Subdivision.cs:23-32`、`PanelCommand.Deform.cs:587-599` |
| 型の自動発見 | コマンドは型名から action を作って自動登録。探す範囲は本体アセンブリだけ | `PanelCommandFactory.cs:114-136`、`PLParamAudit.cs:109` |
| 位置だけの変形の前例 | `IMeshDeformer`（作業軸ローカルの点 → 点、副作用なし）を 1 つ書いて登録表に 1 行足せば増える | `Tools/Deformers/IMeshDeformer.cs:108-138`、`DeformerRegistry.cs:4-6, 25-33` |

### 2.2 各処理の実処理と依存

| 処理 | 実処理 | 今の依存 | 枠組みに載せるときの扱い |
| --- | --- | --- | --- |
| 厚み | `FaceGroupSolidifier.Build(mesh, faces, params, name)`（純粋に近い） | 選択面（`SolidifyTool.cs:135`）、結果は別メッシュ | 面の組を処理範囲から渡す |
| ベベル | `EdgeBevelTool.ExecuteBevel(ToolContext)`（`EdgeBevelTool.cs:468-860`）。入口は `ApplyBevelFromCommand`（`:247`） | ToolContext（下の表）・辺 1 本を頂点番号で指定（`PanelCommand.ToolConfirm.cs:41-45`） | 実処理を「メッシュ・辺の組・量・分割・フィレット・材質 → メッシュ＋ドラッグ用の頂点（索引・基準位置・向き）」に切り出す。Undo・選択・GPU 通知は呼び出し側に残す |
| ブーリアン | `BooleanOps.Perform(op, a, aToWorld, b, bToWorld, worldToResult, …)`（純粋） | 入力 A を書き換えうる（`PanelCommand.Topology.cs:37-39`） | 相手 B は参照オブジェクト（グラフの辺） |
| 曲げ・ねじり・波など | `IMeshDeformer.Evaluate`（純粋）、適用は `DeformApplier`（モデル・作業軸・選択に依存） | 選択・作業軸（`PanelCommand.Deform.cs:425-428`） | 包む部品 1 つで全デフォーマを載せる。作業軸は処理の引数に持つ |
| 格子 | `LatticeDeformer`（`IMeshDeformer`。`Tools/Deformers/Lattice/LatticeDeformer.cs:31`、`Evaluate` `:136-139`）＋ `LatticeGrid`・`TrilinearLatticeInterpolator`。`DeformerRegistry` には未登録（`:13-16`）、`Params` は null（`:44`） | 適用は Player 層 `LatticeToolHandler.ExecuteFromCommand`（`LatticeToolHandler.cs:511`）→ `DeformApplier.Begin(model, axis)`（`:238, 271, 343`）・`Apply`（`:647`）・`Commit`（Undo `:389-405`）。選択（`:520`）・作業軸（`:523`）に依存。受け口 `PolyLingPlayerViewerCore.CreateCommands.Topology.cs:382-394` | デフォーマを包む部品とは別の種類 1 つにする。引数はセル数・中心・大きさ・動かした点の索引とずれ・作業軸。格子は `LatticeGrid.SetCells`（`LatticeGrid.cs:132`）→ `SetCenterSize`（`:175`）→ `Rebuild`（`:195`）→ `SetCurrent`（`:248`）で引数から組める（コマンド経路と同じ順。`LatticeToolHandler.cs:540-571`） |
| 配列 | 一定間隔の繰り返しは、クラス名の検索では見当たらない。`createObjectArray`（歪み複製）は記録されない | — | 新規に作る |
| ミラー | メッシュ属性 MirrorType（表示上のミラー）と実体化 `BakeMirrorCommand`。MirrorType は 45 ファイルで使われ、MQO・PMX の読込、MQO の書き出し、ヒューマノイド VRM のミラーにも入っている。効き方は下の「ミラーの効き方」 | `PanelCommand.MeshAttribute.cs:87`、`PanelCommand.Mirror.cs:36` | 3.6 |
| サブディビジョン | 処理器あり（ケージ → 子） | — | 3.6 |

#### ベベルの実処理が使う依存（段 0 で確認。`EdgeBevelTool.cs`）

| 依存 | 箇所 | 切り出し後 |
| --- | --- | --- |
| `ctx.ActiveMeshObject` | `:470, 962-968` | 引数のメッシュ |
| `ctx.SelectionState.Edges`（辺の入力。終わりに消す） | `:279-280, 957, 858` | 引数の辺の組 |
| `ctx.CurrentMaterialIndex`（足す面の材質） | `:479` | 引数 |
| `ctx.SyncMesh` | `:859` | 呼び出し側 |
| `ctx.UndoController`・`ActiveMeshContext`（Undo） | `:275-277, 433-441` | 呼び出し側（使うのは `ApplyBevelFromCommand`・`EndBevel` だけ） |
| インスタンスの `_targetEdges` | `:76, 953-983` | 局所変数 |
| `_dragAmount`（量） | `:477` | 引数 |
| 設定 `Segments`・`Fillet` | `:478, 535, 675` | 引数 |
| `_dragVertices`（ドラッグ用の出力） | `:842-851` | 戻り値 |

- メッシュへの副作用は `PartsIdOps.AssignNewVertices`（`:589`）と `meshObject.RemoveVertices`（`:888-910`。選択・パーツ選択辞書も付け替える。注記 `:898-902`）
- 補助の `GetInwardOffset`（`:912`）・`FindAdjacentFacesWithEdgeVertices`（`:992`）・`RemoveOrphanVertices`（`:888`）は引数が `MeshObject` だけ
- 処理列では辺を頂点 ID で持つ（3.3）。今のコマンドは頂点番号

#### ミラーの効き方（段 0 で確認）

- 表示：ミラー側は別の描画オブジェクト（`MeshType.MirrorSide`／`BakedMirror`）。作るのは MQO 読込（`MQOImporter.cs:315-354`）と `SetMirrorEnabledCommand`（`PanelCommand.MeshAttribute.cs:71-98`）。描画は普通のオブジェクトとして（`MeshSceneRenderer.cs:603-608`）。`MorphMirrorPolicy.cs:20-23` は「`SymmetryMeshCache` で描画時に写す」と書くが、`SymmetryCache` の getter（`MeshContext.Attributes.cs:372`）を読む箇所は無い（Invalidate・Clear だけ）
- 編集の追随：位置は `PlayerViewportManager.Renderer.cs:279-330`（MirrorPair・BakedMirror）、位相は `MirrorBranchOps.Propagate.cs:15-62`（同じ添字に同じ位相操作を掛ける）。実体側から丸ごと作り直す `RebuildDerivedMirrorGeometry`（`MirrorBranchOps.Create.cs:500, 593`）は呼び出し元が無い
- 書き出し：MQO の `mirror`・`mirror_axis`・`mirror_dis` は MirrorType から書く（`MQOExporter.Legacy.cs:330-337`）。ミラー側オブジェクトは型で判定して出さない（`MQOExporter.Text.cs:106-117`）
- MirrorType はミラー側を作る判定から外せる（`MirrorBranchOps.Create.cs:35-41`）
- 1 つのメッシュの中で N→2N に展開する `MirrorBaker.BakeMirror(MeshObject, axis, offset, threshold, flipU)` は新しい `MeshObject` を返し、モデルに触れない（`MirrorBaker.cs:245-272`）

### 2.3 足りないもの

- 1 つのオブジェクトに処理を重ねる形が無い（2.1 の依存グラフ）
- 項目に有効／無効の欄が無い。並べ替えは「1 つ上へ足す」だけ。引数はキーのドロップダウン＋文字列欄（`PlayerModifierStackSubPanel.cs:83-92`）
- 1 種類足すのに、コマンドのクラス・ディスパッチャの分岐（例 `PlayerCommandDispatcher.Subdivision.cs:24-45`）・パネルが要る

### 2.4 処理列を 1 節点で載せるときに触る所（段 0 で確認）

- 項目の集め方：`ModifierEvaluator.AddGroup`（`ModifierEvaluator.cs:390-447`）が実行項目 1 つにつき Entry 1 つを作る。型は `PanelCommandFactory.ResolveType(s.Action)`（`:398`）、入力は `InputKeys`（`:407-426`）、出力は `OutputObjectIds`（`:428`）。処理列のスタックはここで分岐し、Entry 1 つ（入力＝元メッシュと有効な全項目の参照オブジェクト、出力＝結果）にまとめる。呼び出し元は `GetGraph`（`:576`）・`ReleaseGroup`（`:1232`）・`ModifierGraph.CycleReason`（`ModifierGraph.cs:236`）・`SubdivisionSync.cs:97`
- 処理器の引き当てが `e.Step.Action`：`RefreshModel`（`:1016`）・`ReleaseGroup`（`:1236`）・`SyncFromInput`（`:1297`）・`ModifierGraph.ClassifyStatus`（`ModifierGraph.cs:157`）。処理列の節点には Step と別の処理器キーを持たせる
- 項目の同一性（`ReferenceEquals(e.Step, step)`）で引く所：`PrimeStep`（`:627`）・`IsDirty`（`:664`）・`MarkRebuilt`（`:1202`）・`EvaluatePending`（`:1130`）・`LiveViews.cs:696`・`PlayerCommandDispatcher.ModifierStack.cs:624-625`・`PlayerCommandDispatcher.ModifierInverse.cs:182-183`。処理列の項目からはスタック単位で引く
- FollowsDrag は型の属性から取る（`InfoOf` `:535-`）。処理列の節点は「有効な項目がすべて位置だけ」で決める
- 構造キーは全項目を混ぜる（`ModifierGraph.cs:294-313`）。処理列のスタックは元・結果・参照の ID だけを混ぜ、列の中の並べ替え・無効化でグラフを作り直さないようにする

## 3. 設計

### 3.1 処理列

- 1 つの処理列 ＝ 1 つのモディファイアスタック。スタックに「処理列である」印を足す
- 項目 1 つ ＝ 処理 1 つ（Action＝処理の種類名、Args＝引数、MeshRefIds＝参照オブジェクト）。項目に「有効」の欄を足す。並べ替え・指し示しは ScenarioItemId で行う（位置で指さない。`ModifierStack.cs:349-357` と同じ考え）
- 元メッシュは処理列の入力、結果は出力。どちらも描画オブジェクト。元メッシュは結果の親のケージとして置く（サブディビジョンと同じ置き方）
- 中間結果はオブジェクトにせず、メモリ上で次の項目へ渡す
- 依存グラフには処理列を 1 つの節点として載せる（入力＝元メッシュと全項目の参照オブジェクト、出力＝結果）。列の中の並べ替え・無効化・引数の変更はグラフの形を変えない
- 保存・リモート転送・Undo はスタックの既存の仕組みに乗る。足す欄（処理列の印・項目の有効）は CSV・JSON・二進のすべてに入れる
- 「適用」＝ 処理列を解除し、結果を普通のメッシュとして残す（評価器の Release）

### 3.2 モディファイアの書き方（作者から見た枠組み）

- 1 種類 ＝ クラス 1 つ。インターフェース（仮名 `IMeshModifier`）と、種類名・表示名・分類を載せる属性（仮名 `PLMeshModifier`）
- 引数は `PLParam` を付けたプロパティ。欄・MCP の説明・保存はここから作る
- 宣言
  - 変える範囲：位置だけ（ドラッグ中も追随）／面構成も（ドラッグを離したときに作り直す）
  - 参照するオブジェクト：`PLParam(IsMeshRef)` と同じ宣言で表す
- 計算本体：入力メッシュ・引数・処理範囲・参照オブジェクト（解決済み）を受け取り、出力メッシュを返す。モデル・選択・画面に触れない
- 位置だけの処理は、頂点数を変えない前提で「位置の配列 → 位置の配列」の速い口も持てる（ドラッグ中の追随用）
- 発見：読み込まれている全アセンブリから属性で探す。本体を触らずに別パッケージからも足せる
- 既存の `IMeshDeformer` は包む部品 1 つでモディファイアになる（位置だけ）

### 3.3 処理範囲

- 全体／保存した面・辺の組。組は頂点 ID で持ち、頂点番号・選択に依存しない
- 範囲の外は枠組みが元のまま通す。作者は範囲の中だけを扱う
- 頂点グループに当たるものはパーツ選択辞書 `PartsSelectionSet`（描画オブジェクトごと。`MeshContext.cs:337`、保存 `:342`）。頂点・辺・面・線と、頂点 ID・Face.Id・辺の両端の頂点 ID の控えを持ち（`PartsSelectionSet.cs:31-83`）、`ResolveByIds`（`:508`）で ID から引き直せる。範囲の選択肢に「パーツ選択辞書（名前で指定し、ID で引き直す）」を入れる
- 重み付きの頂点グループは無い（`Core/Data` を `WeightMap|VertexWeightGroup|ScalarField|PerVertexWeight|VertexColor` で検索して 0 件。骨のウェイトは除く）

### 3.4 評価

- 処理列 1 種類ぶんの処理器を 1 つ作り、評価器に登録する（`ModifierEvaluator.cs:343-353` の登録表）
- Refresh：元メッシュか参照オブジェクトの署名が変わったら、列を先頭から流して結果へ書く
- SyncDrag：列の項目がすべて位置だけなら、位置の口だけで結果を書き直す。面構成を変える項目があれば、要評価の印だけ立て、離したときに流す
- 項目ごとの結果の控えはメモリ上だけに持ち、保存しない（サブディビジョンのステンシルと同じ）。変わった項目より前の控えは使い回す
- 高速化計画の道筋（移動・回転・拡大では作り直さない）に乗せる
- 失敗した項目は、項目の状態に理由を出して以降を流さない（評価器の Failed・Blocked）
- 結果を書き換えるコマンドは受け口で拒否する（`ModifierOutputGuard.cs` の既存の保護）

### 3.5 共通のコマンドと画面

- コマンドは全種類共通の 6 本：追加・引数の変更・並べ替え・有効の切り替え・削除・適用。種類名と引数は文字列で受け、種類ごとのコマンドは作らない
- 照会：処理列の中身（項目・種類・引数・有効・状態）と、登録されている種類の一覧（引数の宣言つき）
- 処理列パネル：種類を選んで追加・↑↓・有効の切り替え・削除・適用、選んだ項目の引数欄
- 引数欄は `PLParam` から自動で作る：数値 → 数値欄＋スライダー（Min・Max・Step）、列挙型・軸 → 選択ボタン、IsMeshRef → オブジェクト選択、真偽 → チェックボックス、範囲 → 面・辺の組の登録ボタン。文字列の欄は「詳細」に残す
- 既存のモディファイアスタックのパネルの引数欄も同じ部品で作る
- 宣言が足りない引数（Min・Max の無い数値など）は宣言を足す。漏れは `PLParamAudit` で検査する

### 3.6 ミラーとサブディビジョン

- ミラー：MirrorType は MQO・PMX の読込、MQO の書き出し、ヒューマノイド VRM のミラーにも使われている（2.2）。そのため属性は廃止しない。ミラーは 2 種類に分ける（段 0 で決定。根拠は 2.2「ミラーの効き方」）
  - 「ミラー（表示）」：末尾固定・並べ替え不可。表示ミラーは結果オブジェクトから派生する別オブジェクトなので、項目は結果の MirrorType・軸・距離を書き換える
  - 「ミラー（実体化）」：列のどこにでも置ける普通の種類（面構成も変える）。`MirrorBaker.BakeMirror` を呼ぶ
  - 結果を作り直したときにミラー側をどの口で追随させるかは段 8 で確かめる（`RebuildDerivedMirrorGeometry` は呼び出し元が無い）
- サブディビジョン：処理列の項目「サブディビジョン」を作り、`SubdivisionStencil` を使い回す。既存の subdivideMesh のスタックはそのまま動かし、処理列パネルにも同じ見た目で出す

## 4. 段

### 段 0：棚卸しの確定

- 格子の実処理の所在、ベベルの実処理を切り出すときに外す依存（ToolContext のどれを使っているか）を file:line で確定する
- MirrorType の効き方（表示・編集・書き出し）を読み、3.6 の未決を決める
- 頂点グループに当たるものの有無を確かめる（3.3）
- 評価器・グラフに「1 つの節点として載せる」ときに触る所（`ModifierGraph` の項目の集め方、`ModifierEvaluator.CollectEntries`）を確定する
- 状態：完了（2026-11-01）。結果は 2.2・2.4・3.3・3.6・5。ソースは変えていない

### 段 1：枠組みの核

- `IMeshModifier`・`PLMeshModifier`・登録表（全アセンブリから発見）、処理範囲の受け渡し
- 試験用の最小の種類 1 つ（例：全体を平行移動する）で、発見・引数の往復（`PanelCommandFactory` の ToArgs と同じ文字列の対）を確かめる
- 状態：未着手

### 段 2：データと保存

- スタックの「処理列である」印、項目の「有効」欄
- CSV・JSON・リモート二進の読み書き。二進はファイル保存の一時コマンドで往復を確かめる
- 状態：未着手

### 段 3：評価

- 処理列の処理器（Refresh・SyncDrag・Release）、グラフに 1 つの節点として載せる、項目ごとの控え、失敗の扱い、結果の保護
- 状態：未着手

### 段 4：共通コマンドと照会

- 6 本のコマンドと 2 つの照会、MCP からの通し確認（追加 → 引数変更 → 並べ替え → 無効化 → Undo → 保存再読込）
- 状態：未着手

### 段 5：画面

- 処理列パネル、`PLParam` からの引数欄の自動生成、既存スタックのパネルの引数欄の置き換え、UI 自動操作の ID 登録
- 状態：未着手

### 段 6：最初の種類と作成ガイド

- 厚み（`FaceGroupSolidifier.Build`）、デフォーマを包む部品（曲げ・ねじり・波ほか登録済みの全デフォーマ）
- 「板 → 厚み → 曲げ」を画面と MCP で通す
- `PolyLing_モディファイア作成ガイド.md`（作者向け。クラス 1 つの書き方、宣言、試し方）
- 状態：未着手

### 段 7：残りの種類

- ベベル（実処理の切り出し）、ブーリアン（`BooleanOps.Perform`、相手は参照オブジェクト）、格子、配列（新規）
- 既存の破壊的コマンドは、切り出した実処理を 1 回呼んで書き込む形に直す（実装を二重に持たない）
- 状態：未着手

### 段 8：ミラーとサブディビジョン

- 3.6 のとおり
- 状態：未着手

### 段 9：Blender との比較

- 「板 → 厚み → ベベル → 曲げ」「箱 → ブーリアン → ベベル」「髪 → パイプ → 揺れ鎖」をシナリオにし、MCP で流す
- 合否：途中で値を変えると下流が追随する／Undo で戻る／保存再読込の後に結果の頂点数・面数・位置が一致する／必要な操作の回数
- Blender 側は同じ作業の手順と操作回数を表にして並べる
- 状態：未着手

### 段 10：モーション（別の作業線。この計画では方針だけ置く）

- レイヤーの重みを、既存のスカラートラックの形で時間変化させられるようにする。Studio も同じ評価の写しにそろえる
- 本体の再生を、平らなトラックではなく編集構造の評価（`sampleMotionEdit` は小数フレームを受ける。`PolyLing_モーション編集構造計画.md:193`）で行う。平らなトラックは書き出し・他ツール向けの写しにし、標本化の細かさは書き出し時に選べるようにする
- 着手するときは `PolyLing_モーション編集構造計画.md` に段を足して進める
- 状態：未着手

## 5. 未決事項

- 結果を作り直したときのミラー側の追随の口（段 8。3.6）
- 処理列パネルを右ペインのどこに置くか（段 5 の始めに案を出す）

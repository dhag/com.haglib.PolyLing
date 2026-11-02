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
| 厚み | `FaceGroupSolidifier.Build(mesh, faces, params, name)`（純粋に近い） | 選択面（`SolidifyTool.cs:135`）、結果は別メッシュ | 面の組を処理範囲から渡す。段 6 で種類 `thickness`（`ThicknessMeshModifier`）にした |
| ベベル | `EdgeBevelTool.ExecuteBevel(ToolContext)`（`EdgeBevelTool.cs:468-860`）。入口は `ApplyBevelFromCommand`（`:247`） | ToolContext（下の表）・辺 1 本を頂点番号で指定（`PanelCommand.ToolConfirm.cs:41-45`） | 実処理を「メッシュ・辺の組・量・分割・フィレット・材質 → メッシュ＋ドラッグ用の頂点（索引・基準位置・向き）」に切り出す。Undo・選択・GPU 通知は呼び出し側に残す |
| ブーリアン | `BooleanOps.Perform(op, a, aToWorld, b, bToWorld, worldToResult, …)`（純粋） | 入力 A を書き換えうる（`PanelCommand.Topology.cs:37-39`） | 相手 B は参照オブジェクト（グラフの辺）。段 7b で種類 `boolean`（`BooleanMeshModifier`）にした |
| 曲げ・ねじり・波など | `IMeshDeformer.Evaluate`（純粋）、適用は `DeformApplier`（モデル・作業軸・選択に依存） | 選択・作業軸（`PanelCommand.Deform.cs:425-428`） | 包む部品 1 つで全デフォーマを載せる。作業軸は処理の引数に持つ。段 6 で基底 `DeformerMeshModifier<T>` と種類 6 つ（rotate・move・scale・bend・twist・wave）にした |
| 格子 | `LatticeDeformer`（`IMeshDeformer`。`Tools/Deformers/Lattice/LatticeDeformer.cs:31`、`Evaluate` `:136-139`）＋ `LatticeGrid`・`TrilinearLatticeInterpolator`。`DeformerRegistry` には未登録（`:13-16`）、`Params` は null（`:44`） | 適用は Player 層 `LatticeToolHandler.ExecuteFromCommand`（`LatticeToolHandler.cs:511`）→ `DeformApplier.Begin(model, axis)`（`:238, 271, 343`）・`Apply`（`:647`）・`Commit`（Undo `:389-405`）。選択（`:520`）・作業軸（`:523`）に依存。受け口 `PolyLingPlayerViewerCore.CreateCommands.Topology.cs:382-394` | デフォーマを包む部品とは別の種類 1 つにする。引数はセル数・中心・大きさ・動かした点の索引とずれ・作業軸。格子は `LatticeGrid.SetCells`（`LatticeGrid.cs:132`）→ `SetCenterSize`（`:175`）→ `Rebuild`（`:195`）→ `SetCurrent`（`:248`）で引数から組める（コマンド経路と同じ順。`LatticeToolHandler.cs:540-571`）。段 7b で組み立てを `LatticeDeformer.Configure` にまとめ、種類 `lattice`（`LatticeMeshModifier`）にした |
| 配列 | 一定間隔の繰り返しは、クラス名の検索では見当たらない。`createObjectArray`（歪み複製）は記録されない | — | 新規に作る |
| ミラー | メッシュ属性 MirrorType（表示上のミラー）と実体化 `BakeMirrorCommand`。MirrorType は 45 ファイルで使われ、MQO・PMX の読込、MQO の書き出し、ヒューマノイド VRM のミラーにも入っている。効き方は下の「ミラーの効き方」 | `PanelCommand.MeshAttribute.cs:87`、`PanelCommand.Mirror.cs:36` | 3.6 |
| サブディビジョン | 処理器あり（ケージ → 子） | — | 3.6 |

#### ベベルの実処理が使う依存（段 0 で確認。`EdgeBevelTool.cs`。段 7a で `EdgeBevelOps` に切り出し済み、行番号は切り出し前のもの）

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
- 依存グラフには処理列を 1 つの節点として載せる（入力＝元メッシュと全項目〔無効な項目を含む〕の参照オブジェクト、出力＝結果）。元メッシュは各項目の `MeshRefIds["chain.source"]`、結果は各項目の `OutputObjectIds` に持つ（段 3）。列の中の並べ替え・無効化・引数の変更はグラフの形を変えない
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
- 決めた名前と置き場（段 1）：`Runtime/Poly_Ling_Main/Core/Modifiers/`、名前空間 `Poly_Ling.Modifiers`
  - `IMeshModifier.Evaluate(MeshModifierInput, out error) → MeshObject`（入力を書き換えず新しいメッシュを返す）。位置の口は任意の `IMeshModifierPositions.EvaluatePositions(input, src, dst, out error)`
  - 属性 `PLMeshModifierAttribute(kind)`：DisplayName・Category・Change（`MeshModifierChange.PositionsOnly`／`Topology`）・Description
  - 既存の `PLModifierAttribute`（PanelCommand に付ける、スタックへの記録の宣言）とは別物。どちらも型を名指しで読むので混ざらない。両ファイルの頭に相互の注記を置いた
  - 登録表 `MeshModifierRegistry`：`AppDomain.CurrentDomain.GetAssemblies()` を走査。引数は `[PLParam]` の付いた public の読み書きできるプロパティ、キーはプロパティ名の先頭小文字。属性の欠け・種類名の重複・コマンドの action 名との衝突・PLParam の付け忘れ・扱えない型は `Problems` に積んで登録しない
  - 引数の文字列：`ToArgs(modifier, scope)`／`FromArgs(kind, args, …)`。値は `PanelCommandFactory.TryFormatValue`／`TryParseValue`、入れ子は `FlattenNestedValue`／`TryBuildNestedValue`（`PanelCommandNested.cs` に公開の口を足した）。未知のキーと Required の欠けは失敗
  - 検査：`queryMeshModifierAudit`（`MeshModifierAudit`）。発見・往復（既定値×3 種類の範囲）・範囲の自己試験・指定の引数の往復

### 3.3 処理範囲

- 全体／保存した面・辺の組。組は頂点 ID で持ち、頂点番号・選択に依存しない
- 範囲の外は枠組みが元のまま通す。作者は範囲の中だけを扱う
- 頂点グループに当たるものはパーツ選択辞書 `PartsSelectionSet`（描画オブジェクトごと。`MeshContext.cs:337`、保存 `:342`）。頂点・辺・面・線と、頂点 ID・Face.Id・辺の両端の頂点 ID の控えを持ち（`PartsSelectionSet.cs:31-83`）、`ResolveByIds`（`:508`）で ID から引き直せる。範囲の選択肢に「パーツ選択辞書（名前で指定し、ID で引き直す）」を入れる
- 重み付きの頂点グループは無い（`Core/Data` を `WeightMap|VertexWeightGroup|ScalarField|PerVertexWeight|VertexColor` で検索して 0 件。骨のウェイトは除く）
- 決めた持ち方（段 1。`Core/Modifiers/MeshModifierScope.cs`）
  - 種類 `MeshModifierScopeKind`：All／Ids／PartsSet。引数の文字列は `scope.kind`・`scope.vertexIds`・`scope.edgeVertexIds`（両端の頂点 ID を 2 個ずつ）・`scope.faceVertexIds`（面の頂点 ID を平らに）と `scope.faceSizes`（面ごとの頂点数）・`scope.partsSet`
  - 引き直し `TryResolve(mesh, partsSets, out ResolvedMeshModifierScope, out error)`。ID が複数当たれば全件。辺は当たった組のうち実際に面の辺であるものだけ、面は頂点 ID の集まりが一致する面
  - PartsSet は辞書の複製に `ResolveByIds` を掛ける（利用者の辞書は書き換えない）。控えが無い辞書・無い名前は失敗。控えの無い要素は番号のまま使わず範囲に入れない（`NoId` に数える）。線分は頂点だけを範囲に入れる
  - 選択などの索引の組から作る口 `MeshModifierScope.FromIndices`（未設定 ID の要素は控えない）
  - 解決結果は頂点・辺・面と、触れる頂点（3 つの和）。`ContainsVertex` で範囲の内外を引く

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
  - 結果を作り直したときのミラー側の追随の口は段 8 で決めた（`MirrorBranchOps.RebuildMirrorSideOf`。段 8 の「決めたこと」）
- サブディビジョン：処理列の項目「サブディビジョン」を作り、`SubdivisionStencil` を使い回す。既存の subdivideMesh のスタックはそのまま動かし、処理列パネルにも同じ見た目で出す
- 段 8 で完了

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
- 状態：完了（2026-11-01）。決めた名前と持ち方は 3.2・3.3 の末尾
  - 足したファイル：`Core/Modifiers/IMeshModifier.cs`・`MeshModifierScope.cs`・`MeshModifierRegistry.cs`・`MeshModifierAudit.cs`・`Kinds/TranslateMeshModifier.cs`（種類 `translate`、引数 `offset`、位置の口あり）
  - 変えたファイル：`PanelCommandNested.cs`（入れ子の公開の口 2 本）、`PanelCommand.Query.cs`（`QueryMeshModifierAuditCommand`）、`PlayerCommandDispatcher.cs`（プロジェクトの門の前で受ける）、`PLModifierAttribute.cs`（注記）
  - 確認（MCP）：`queryMeshModifierAudit` で translate を発見、発見の問題 0、往復の不一致 0、範囲の自己試験 9 件すべて合格（頂点と面の並びを入れ替えたメッシュへ ID の組・パーツ選択辞書とも同じ頂点・辺・面へ引き直す／辞書を書き換えない／控え無し・無い名前は失敗／平行移動が範囲内だけを動かし位置の口と一致）。指定の引数 `offset="1.5,2,-3"`＋`scope.partsSet="髪,前"`、ID の組（頂点・辺・面）とも往復一致。綴り違いのキー `ofset` は失敗になる。`queryCommandAudit` は全項目 0

### 段 2：データと保存

- スタックの「処理列である」印、項目の「有効」欄
- CSV・JSON・リモート二進の読み書き。二進はファイル保存の一時コマンドで往復を確かめる
- 状態：完了（2026-11-01）
  - 足した欄：`ModifierStack.IsChain`（既定 false）、`ModifierStackStep.Enabled`（既定 true）。どちらも Clone で写す。写しはすべて Clone 経由（Undo `MeshListRecords.cs`、シナリオへ写す `ModifierStackOps.BuildScenarioSteps`・`saveScenarioFromModifierStack`、逆方向 `PlayerCommandDispatcher.ModifierInverse.cs`）で、他に欄ごとに写す所・比べる所は無い。新しく作る所は既定値のまま
  - CSV（`ModifierStackCsv.cs`、版 1.9）：g 行の末尾に `isChain`（1/0）、s 行の末尾に `enabled`（1/0）。列の無い 1.8 以前は isChain=0・enabled=1。TryValidate の上限は g 9・s 10
  - JSON（`ModifierStackDTO.cs`）：スタックに `isChain`、項目に `disabled`。有効を既定にするため項目は「無効」の向きで持ち、欄の無い旧データは有効として読む
  - 二進（PLRM v13）：末尾に `WriteModifierChainFlags`／`ReadModifierChainFlags`（`RemoteProgressiveSerializer.Extras.cs`。スタック数、スタックごとに IsChain と項目ごとの Enabled）。v12 以前は読まず既定値。v12 の受け手は末尾を読まずに終わる。版の履歴は `RemoteBinaryProtocol.cs`
  - 一時コマンドは作らなかった。既存の `saveProjectBinary`／`loadProjectBinary`（デバッグ用）でファイル往復ができ、印の設定は保存した CSV の手書きで足りたため
  - 確認（MCP）：変更前のコードで 2 項目のスタック（鉄塔＋デッキ）を CSV・.mfproj・.plrf（v12）に保存し、変更後に 3 つとも読み込んで、全スタック isChain=0・全項目 enabled=1 になった。CSV を手で isChain=1・項目 e2 を enabled=0 にして読み込み、`queryProjectSyncDiff` は via=binary・via=csv とも差なし。.mfproj に `isChain: true`・`disabled: true` が出た。.mfproj と .plrf をそれぞれ保存→読込→CSV 保存し、手書きの CSV と `queryCsvFolderDiff` で違う行 0（印も残る）。`queryCommandAudit` の完了条件の項目は 0（この段はコマンドを変えていない）。試験のファイルは片付けた

### 段 3：評価

- 処理列の処理器（Refresh・SyncDrag・Release）、グラフに 1 つの節点として載せる、項目ごとの控え、失敗の扱い、結果の保護
- 状態：完了（2026-11-01）
  - 決めた持ち方：元メッシュは各項目の `MeshRefIds["chain.source"]`、結果は各項目の `OutputObjectIds` に持つ（保存形式は変えない）。出力の保護（`ModifierOutputGuard`）・参照切れの片付け・ダイジェストが項目単位の ID で動くので、そのまま乗る。全項目で値が食い違えば失敗。節点の入力には無効な項目の参照も入れる（2.4 の「有効な全項目」から変えた。有効の切り替えでグラフを作り直さないため）
  - 足したファイル：`Core/Modifiers/ModifierChainHandler.cs`（処理器。キー `@chain`。Refresh は元メッシュの署名と項目ごとの指紋〔前の指紋・種類名・引数・参照の ID と署名〕を比べ、変わった項目から先だけ流す。控えはメモリだけ。位置だけの種類は範囲の外を入力の位置へ戻す。失敗した項目以降は Blocked、理由は評価器の状態へ。SyncDrag は有効な項目がすべて位置だけのとき位置の口〔無ければ Evaluate〕で結果の位置だけ書く。Release は控えを捨てロックを外す）
  - 変えたファイル：`ModifierEvaluator.cs`（`Entry.IsChain`・`HandlerKey`・`ActionLabel`・`Owns`、`AddChainEntry`、`SetHandlerResult`、処理器の引き当てを `HandlerKey`、項目の同一性を `Owns`、ドラッグ追随の判定）、`ModifierGraph.cs`（`HandlerKey`、構造キーは処理列を元・結果・参照の ID だけで混ぜる）、`SubdivisionSync.cs`（処理列の節点を除く）、`LiveViews.cs`・`PlayerCommandDispatcher.ModifierInverse.cs`・`PlayerCommandDispatcher.ModifierStack.cs`（`Owns`。処理列は評価空間を持たない）、`PlayerCommandDispatcher.ModifierEval.cs`・`PanelCommand.Query.cs`（`queryModifierGraph` に `structureKey`）
  - 確認（MCP）：手書きの CSV（translate 2〜3 項目）を読み、節点 1 つ・状態 Handler・結果が元 +offset の和。元の頂点をコマンドで動かすと結果が追随。画面のギズモのドラッグ中も結果が追随（離す前の画面で確認）。enabled=0 の CSV でその項目が効かない。同じ読み込みの中で並べ替え・無効化しても構造キーは同じで、使い回しが効く（「流した 1 件・使い回し 2 件」）。未登録の種類名の項目で Failed、以降は Blocked、理由が状態に出る。結果へ頂点の移動を掛けると拒否。`deleteModifierStack` で結果が普通のメッシュとして残り編集できる。既存のスタック（subdivideMesh＝Handler、createRadialPlace＝Evaluate）の評価は変わらない。`queryProjectSyncDiff` は via=binary・via=csv とも差なし。`queryCommandAudit` の完了条件の項目は 0
  - 構造キーは描画オブジェクト・スタックの同一性を混ぜるので、読み込みをまたいで比べられない。並べ替え・無効化は一時コマンドで同じ読み込みの中で確かめ、確かめた後に消した（段 4 の共通コマンドが置き換える）
  - 確かめの中で直した不具合：失敗した項目の後の項目で、前の結果の控えを引く前に失敗を見ていなかった（索引が範囲外）

### 段 4：共通コマンドと照会

- 6 本のコマンドと 2 つの照会、MCP からの通し確認（追加 → 引数変更 → 並べ替え → 無効化 → Undo → 保存再読込）
- 状態：完了（2026-11-01）
  - 決めたこと
    - 処理列は最初の追加で作る：`addMeshModifier` で stackName を省くと sourceMasterIndex を元メッシュにした処理列（名前 `<元>_chain`）を作り、結果（`<元>_mod`）を元メッシュの子・ロックつきで置く（サブディビジョンの子と同じ置き方）。作った直後に `PrimeGroup`。元メッシュは通常メッシュ・スキンド化していないこと、すでにどれかの処理列の元メッシュか結果なら拒否
    - 元メッシュの面の塗りは処理器が止める（結果を初めて見た Refresh で `HideFaceShading=true`、Release で false。`ModifierChainHandler`）
    - 最後の項目の削除は処理列ごと外し、結果の描画オブジェクトを消す（元メッシュだけ残る）
    - 引数は `FromArgs` で検査し `ToArgs` で正規化して持つ。引数の変更は渡したキーだけを今の引数に重ねる。参照の引数（PLParam IsMeshRef）は索引で受けて ObjectId を `MeshRefIds[キー]` に入れる（元メッシュ・結果は指せない）。照会と引数の変更では MeshRefIds から今の索引へ引き直す
    - 評価で失敗した項目はコマンドの失敗にせず、状態として照会に出す
    - Undo は書き換えを `SuspendRecording` の中で行い、評価の後に一覧のスナップショット＋スタックの変更を 1 件（`RecordSubdivisionUndo`）
  - 名前：コマンド `addMeshModifier`・`setMeshModifierArgs`・`moveMeshModifier`（beforeItemId、空＝末尾）・`setMeshModifierEnabled`・`removeMeshModifier`・`applyMeshModifierChain`、照会 `queryMeshModifierChain`・`queryMeshModifierKinds`
  - 足したファイル：`Core/Data/PanelCommand.ModifierChain.cs`、`Player/View/Core/PlayerCommandDispatcher.ModifierChain.cs`
  - 変えたファイル：`PanelCommand.Query.cs`（照会 2 つ）、`PlayerCommandDispatcher.cs`（`DispatchModifierChain` の呼び出し）、`ModifierChainHandler.cs`（元メッシュの塗り）、`PlayerCommandDispatcher.ModifierStack.cs`（`mergeModifierStack`・`setModifierStackStepArgs` で処理列を拒否）
  - 確認（MCP）：箱に translate を 2 つ足し、引数の変更・並べ替え・無効化で結果の頂点が期待どおり動き、構造キーは変わらない。各操作の Undo・Redo で中身と結果の頂点が戻る（最初の追加の Undo で結果が消え、元メッシュの塗りが戻る）。未知のキー・型違い・未登録の種類名・無い項目 ID・処理列の元メッシュからの重ねての作成・処理列への `setModifierStackStepArgs` はどれも失敗し、モデルの版は変わらない。参照の引数は一時の試験用の種類（確かめた後に片付けた）で、索引→ObjectId・入力に参照が載る・結果が参照の形に追随・元メッシュ／結果／無い索引の拒否を確かめた。保存再読込で項目・有効・結果が残り、その後のコマンドも効く。適用で結果が普通のメッシュとして編集でき、Undo で処理列に戻り結果の編集は拒否される。最後の項目の削除と Undo。見た目は各段で `unity_capture`。`queryProjectSyncDiff` は読み込んだプロジェクトで via=binary・via=csv とも差なし
  - 段の範囲外で見つけたこと：`createEmptyModel` だけの新しいプロジェクトでも `queryProjectSyncDiff` via=csv が models[0].materialReferences（A=0 B=1）の差を出す（処理列なしで再現）。`queryCommandAudit` の「Category が正典に無い」が 10（scenario.log・external.comfyui・geometry.create.profile。この段で触れていないコマンド）

### 段 5：画面

- 処理列パネル、`PLParam` からの引数欄の自動生成、既存スタックのパネルの引数欄の置き換え、UI 自動操作の ID 登録
- 状態：完了（2026-11-01）
  - 決めたこと
    - 置き場：左ペイン「選択」のモディファイアスタックの隣に「処理列」ボタン（`leftPane.modifierChainBtn`）、右ペインは 3D 操作を保つセクション（元メッシュの選択を処理範囲にするため）。対象は編集対象のオブジェクト。それがどれかの処理列の元メッシュか結果ならその処理列を出し、そうでなければ「追加」で新しく作る
    - 引数欄の部品 `ParamFieldBuilder`（`Player/View/SubPanels/Common/`）。入力は「キー・型・PLParam・今の値」（`ParamFieldSpec`）で、処理列は `MeshModifierInfo.Params`、既存スタックは新しく足した `PanelCommandFactory.TryGetArgParam`（`PanelCommandArgParams.cs`。入れ子は ToArgs と同じ道で辿る）から作る。見出しは PLParam の Description の最初の句（`TextKey` はどこからも読まれていない）。範囲は Min・Max、無ければ LimitKey（スキーマ生成と同じ）。範囲が無ければ数値欄だけ。Vector2・Vector3 は成分ごとの数値欄、範囲があれば成分ごとにスライダーも付ける。列挙型は 6 個以下なら選択ボタン、それより多ければドロップダウン。IsMeshRef の int[]・配列などは欄を作らず「詳細」の文字列欄で書く。作り直した欄には `PlayerLayoutRoot.ApplyDarkTheme` を掛ける
    - 送る時機：数値欄（isDelayed）・選択・チェックは確定時に送る。処理列のスライダーは動かしている間 `setMeshModifierArgs` の `preview=true`（Undo を積まない。最初の 1 回で変更前を受け口に控える）、離したとき・フォーカスを外したときに `preview=false`（控えを変更前として Undo 1 件）。数値欄で確定しても同じ控えで 1 件になる。控えが残ったまま別の書き込みコマンドが来たら、その前に今の状態で Undo 1 件にして閉じる（`DispatchCore` の門の直後。書き込まないコマンドと UI 自動操作〔Category "ui"〕では閉じない）。既存スタックのスライダーは作り直しが重いので離したときだけ送る
    - 再描画：`FinishGroupEdit` の `ChangeKind.ListStructure` から `_sectionRefreshPairs` で読み直す。引数欄は項目・種類・キーの組（`ParamFieldBuilder.Signature`）が変わったときだけ作り直し、それ以外は値だけを合わせる。動かしている最中の欄は書き換えない
    - 処理範囲：全体／登録した選択（`IModelView.BuildModifierScopeFromSelection` → `MeshModifierScope.FromIndices`。範囲のキーは全部送り、前の組の残りを重ねない）／パーツ選択辞書（元メッシュの辞書の名前）。頂点 ID の無い要素は範囲に入れず、件数を出す
    - 既存スタックのパネル：選んだ項目の書けるキーを同じ部品で出す。キーのドロップダウン＋文字列欄は「詳細」の折り畳みへ移した（ID はそのまま）。処理列のスタックは欄を出さず処理列パネルへ案内する
    - 範囲の検査：`PLParamAudit.RunRanges`（`queryCommandAudit` に出す）。処理列の種類の数値の引数で Min・Max も LimitKey も無いものは完了条件、スタックに記録されるコマンドの書ける数値の引数で範囲の無いものは参考。translate の offset に `OffsetMin`・`OffsetMax`（±10）を足した。`placeSpringBoneLadderChains.tailLength` に上限を足し、図形生成パネルの行も同じ const を参照するようにした
  - 足したファイル：`Core/Data/PanelCommandArgParams.cs`、`Core/Modifiers/ModifierChainOps.cs`（元・結果の引き当て、今の引数、節点の状態。受け口と写しが共用）、`Player/View/SubPanels/Common/ParamFieldBuilder.cs`、`Player/View/SubPanels/Model/PlayerModifierChainSubPanel.cs`
  - 変えたファイル：`PanelCommand.ModifierChain.cs`（6 本に PLUiRoute、`setMeshModifierArgs` に `preview`）、`PlayerCommandDispatcher.ModifierChain.cs`（下見の控え・閉じ方、共通処理を `ModifierChainOps` へ）、`PlayerCommandDispatcher.cs`（門の直後で下見を閉じる）、`ViewInterfaces.cs`・`LiveViews.cs`・`ModelSummary.cs`・`PlayerProjectView.cs`（処理列の写し、選択から範囲）、`MeshModifierRegistry.cs`（`ScopeToArgs`）、`TranslateMeshModifier.cs`、`PLParamAudit.cs`・`PanelCommandFactoryAudit.cs`、`PlayerModifierStackSubPanel.cs`、配線（`PlayerLayoutRoot.LeftPaneButtons.cs`・`RightPane.cs`、`PolyLingPlayerViewerCore.cs`・`.Layout.cs`・`.Layout.Panels.cs`・`.Panels.cs`・`.UiAutomation.cs`）、`PanelCommand.SpringBoneLadder.cs`・`PlayerPrimitiveMeshSubPanel.SpringBoneChain.cs`、`PlayerLogSubPanel.cs`（既存の UI 自動操作の登録漏れ 1 件）、`PolyLing_機能の場所.md`
  - 確認（MCP。画面の操作は uiClick・uiSetValue）：箱を作り、パネルから translate を追加（Undo で結果が消え処理列なしに戻り、Redo で戻る）→ もう 1 つ追加 → 数値欄で offset を変更（Undo・Redo）→ スライダーで 3 回動かしてから数値欄で確定すると Undo 1 回で動かす前に戻り、Redo で確定値になる → ↓・↑（並びが変わり、選択が項目に付いていく。Undo・Redo）→ 有効の切り替え（状態 Disabled、結果からその項目の移動が消える。Undo・Redo）→ 処理範囲を今の選択から登録（頂点 3 つだけが動く）→ 全体へ戻す → 削除（Undo・Redo）→ 適用（Undo で処理列に戻る・Redo）→ 最後の項目の削除で結果が消える（Undo で戻る）。各段で `queryMeshModifierChain` と結果の頂点（`getRawData`）、見た目は `unity_capture`。既存スタック（createCube）の引数欄が新しい部品で出て、数値欄での書き換えで作り直され Undo で戻る。`queryUiAutomationAudit` は処理列の項目・既存スタックの項目を選んだ状態とも全項目 0。`queryCommandAudit` の処理列の種類の範囲の宣言漏れ 0。`queryProjectSyncDiff` は via=binary で差なし、via=csv は段 4 で見つけた models[0].materialReferences の差だけ
  - 確かめていないこと：人がマウスでスライダーを離したとき（PointerCaptureOut）の確定。UI 自動操作では起こせないため。Undo のまとまりは同じ受け口の口（preview=true の連続 → preview=false）で確かめた
  - 残したもの（参考の件数）：スタックの書ける数値の引数で範囲の無いもの 236。大半は置き場所（worldPosition・placeRotation・placeScale・materialIndex）と乱数の種で、範囲の出どころがコードに無い。髪の startAxial などは形の種類で単位（長さ／角度）が変わり、円筒の edgeRadius の上限は寸法から決まるので、固定の範囲を書けない。これらは数値欄だけになる

### 段 6：最初の種類と作成ガイド

- 厚み（`FaceGroupSolidifier.Build`）、デフォーマを包む部品（曲げ・ねじり・波ほか登録済みの全デフォーマ）
- 「板 → 厚み → 曲げ」を画面と MCP で通す
- `PolyLing_モディファイア作成ガイド.md`（作者向け。クラス 1 つの書き方、宣言、試し方）
- 状態：完了（2026-11-01）
  - 決めたこと
    - 厚み：種類名は `thickness`（`solidify` は `SolidifyCommand` の action 名と重なり、登録表が外す）。対象の面は、全体なら 3 頂点以上の全面、それ以外は範囲の面、範囲に面が無ければ全頂点が範囲に入る面。0 面なら失敗。出力は 1 つのメッシュで、対象の面を外し、対象の面だけが使っていた頂点を外して索引を詰め（LineGroup が使う頂点・入力で孤立していた頂点は残す。LineGroup の索引は付け替える）、立体を末尾に足す。ID は新しく振らない：立体の頂点の ID は元の頂点の ID（`SourceToVertices`）、殻の面の ID は元の面の ID（新しく足した `FaceGroupSolidifier.Result.FaceSources`）、側面は 0。これで下流の範囲（頂点 ID）が厚みの後も同じ頂点の写しに当たる。引数の範囲は既存の LimitKey `Solidify.*`
    - デフォーマ：デフォーマごとに種類を 1 つ出す（引数でデフォーマを選ぶ形は、引数がデフォーマごとに違い `IDeformerParams` が宣言を持たないので採らない）。基底 `DeformerMeshModifier<TDeformer>` が作業軸（引数 `axisOrigin`・`axisRotation`。オブジェクトのローカル座標）・範囲・`DeformContext`（範囲内の頂点から作る）・`Evaluate` と位置の口を持ち、種類のクラスは引数と `WriteParams` だけを書く。DeformApplier からはモデル・選択（→範囲）・作業軸（→引数）・GPU のワールド座標とバインド表示（→ローカルで計算）・マグネット・Undo（→共通コマンド）を外した。DeformApplier 自体は変えていない。曲げはカメラ追従を切る。引数のキーと角度の範囲は ApplyXxxDeformCommand にそろえた。移動 ±10・倍率の上限 10・波の振幅 ±10・周期数 0〜20 はこの段で決めた値
    - 格子はこの段の部品に含めない（段 7 で別の種類にする）
    - 画面は変えていない（種類の一覧は登録表から、引数欄は ParamFieldBuilder から作られる）
  - 足したファイル：`Core/Modifiers/Kinds/ThicknessMeshModifier.cs`・`DeformerMeshModifier.cs`・`DeformerMeshModifiers.cs`、`PolyLing_モディファイア作成ガイド.md`
  - 変えたファイル：`FaceGroupSolidifier.cs`（`FaceSources`）、`MeshModifierAudit.cs`（5 番目の検査「種類の自己試験」：デフォーマの包む漏れ・位置だけの全種類で Evaluate と位置の口の一致と範囲の外が動かないこと・厚みの頂点数／面数／ID）、`PlayerCommandDispatcher.cs`・`PanelCommand.Query.cs`（`kindFailures`）、`ModifierEvaluator.cs`（下記の不具合）、`IMeshModifier.cs`（ガイドへの注記）、`McpGuide/primer.md`（ガイドの行）
  - 確かめの中で直した不具合
    - 評価器：処理器の節点（処理列）は GPU バッファの作り直しの入口（`RefreshModel`）でしか流れず、ドラッグに追随しない節点は `SyncFromInput` が印を立てるだけで、`EvaluatePending` は Evaluate の節点しか流さなかった。位置だけの移動ではバッファの作り直しが起きないので、面構成を変える項目を含む処理列は元メッシュの頂点を動かしても結果が古いまま残った（段 3〜5 の処理列は位置だけの種類だけで表に出なかった）。`EvaluatePending` で、印の立った処理器の節点も依存の順に処理器の Refresh で流すようにした（ログは「処理器で作り直した」）
    - 段 1 の範囲の試験メッシュ（`MeshModifierAudit.BuildTestMesh`）が、`new Face(a, b, c, d)` が `Face(v0, v1, v2, materialIndex)` に解決されて四角形でなく三角形になっていた。材質番号を明示した（範囲の自己試験の期待値は変わらない）
  - 確認（MCP。画面の操作は uiClick・uiSetValue）：`queryMeshModifierAudit` は発見の問題・往復・範囲・種類の自己試験とも 0（種類 8 つ、デフォーマ 6 つすべて包む）。板（4×8 分割）に処理列パネルから厚みを追加（頂点 90・面 88・穴 0）→ 曲げを追加し数値欄で 90 度（上端が x=1.273＝半径に並ぶ）。厚みのスライダーを 3 回動かして数値欄で確定すると Undo 1 回で動かす前、Redo で確定値。角の分割数（整数欄）2 で頂点 138・面 136。↑で曲げ→厚みの順にすると結果が変わり Undo・Redo で並びが戻る。曲げの無効化で曲げが消え Undo・Redo。元メッシュに `repairVertexIds AssignMissing` を掛け、上半分の面を選択から厚みの範囲に登録すると上半分だけが立体（頂点 107＝残す 25＋立体 82、面 96、下半分の縁の穴 1）、曲げにも同じ範囲を登録すると下半分は動かず立体だけが曲がる。元メッシュの頂点を `moveSelectedVertices` で動かすと、厚みを含む列はコマンドの終わりに「処理器で作り直した」（2.1 ms）で追随し、厚みを無効にした曲げだけの列は評価のパスなしで位置の口で追随。Undo で移動と結果が戻る。削除・適用（結果が普通のメッシュとして残り、元メッシュの塗りが戻る）とそれぞれ Undo・Redo。CSV・二進（.plrf）に保存して読み込むと項目・引数・結果（頂点数・面数・位置）が残る。`queryProjectSyncDiff` は via=binary で差なし、via=csv は段 4 で見つけた models[0].materialReferences の差だけ。`queryUiAutomationAudit` は厚み・曲げの項目を選んだ状態とも誤り 0。`queryCommandAudit` の処理列の種類の範囲の宣言漏れ 0（他の件数は段 5 と同じ既存のもの）。既存のサブディビジョンは頂点の移動に評価のパスなしで追随する（変わらない）。見た目は各段で `unity_capture`。試験のファイルは片付けた
  - 確かめていないこと：人がマウスで頂点をドラッグしている最中と離した瞬間（UI 自動操作では起こせない。離したときは `EvaluateModifiersAfterDrag` → `RunModifierEvaluation` → `EvaluatePending` で、コマンドの終わりと同じ口を通る）。人がマウスでスライダーを離したときの確定（段 5 から）
  - 重さ：元メッシュの移動での流し直し（厚み＋曲げ、板 45 頂点）はパス全体で 2.1 ms。スライダーの下見 1 回ごとの時間と、大きいメッシュでの時間は測っていない

### 段 7：残りの種類

- ベベル（実処理の切り出し）、ブーリアン（`BooleanOps.Perform`、相手は参照オブジェクト）、格子、配列（新規）
- 既存の破壊的コマンドは、切り出した実処理を 1 回呼んで書き込む形に直す（実装を二重に持たない）
- 状態：完了（2026-11-01。7a・7b とも完了）
- 1 会話に収まらないので 7a・7b に分けた（2026-11-01）
  - 7a：ベベル（切り出し・種類・既存コマンドの置き換え）と配列。参照オブジェクトを持たない、面構成を変える種類
  - 7b：ブーリアンと格子。ブーリアンは枠組みに参照の行列を足す
- 既存の破壊的コマンドで直さないもの（コードで確かめた）
  - 厚み：`SolidifyTool` はすでに `FaceGroupSolidifier.Build` を 1 回呼ぶだけ
  - ブーリアン：`booleanMesh` の受け口（`PlayerCommandDispatcher.TPoseMergeMorph.cs`）はすでに `BooleanOps.Perform` を 1 回呼んで書き込む

#### 段 7a：ベベルと配列

- 状態：完了（2026-11-01）
- 決めたこと
  - ベベルの実処理を `Core/Ops/EdgeBevelOps.Execute(mesh, edges, amount, segments, fillet, materialIndex)` に切り出した（`EdgeBevelTool.ExecuteBevel` の Pass 1〜3・孤立頂点の除去・辺の収集・隣接面の探索・内向きの向きをそのまま移した）。渡されたメッシュをその場で書き換える
    - 戻り値：集めた辺の数（0 ならメッシュに触れない）・ベベルした辺の数・ドラッグ用の頂点・`SourceOf`（処理後の頂点ごとの処理前の番号。新しい頂点は複製元の辺の端）・`Created`（新しく作った頂点か）
    - 選択・パーツ選択辞書の付け替えは移さない。孤立頂点の除去は `MeshObject.RemoveVertices` を通り、付け替えはその `VerticesRemoved` イベントを MeshContext が購読して行う。ツールは生きたメッシュ、種類は `Clone()`（イベントを写さない）を渡すので、付け替えはツールのときだけ起きる
    - 部品 ID の採番（`PartsIdOps.AssignNewVertices`）はメッシュの中だけで済むので ops に残した。ログ・Undo・選択の辺の消去・GPU への通知はツール側
    - `EdgeBevelTool` は `ExecuteBevel` が ops を 1 回呼ぶ形になり、`_targetEdges`・`BevelEdgeInfo`・`CollectTargetEdges` を外した
  - 種類 `bevel`（`BevelMeshModifier`、Topology）
    - 引数 `amount`（0.0001〜10）・`segments`（1〜16）・`fillet`・`materialIndex`（0〜255）
    - 辺は範囲の辺（保存は頂点 ID の組）を (V1, V2) の順に並べて渡す。全体の範囲・辺の無い範囲・ベベルできる辺が無いときは失敗
    - ID：新しい頂点には `SourceOf` の元の頂点の ID を写す。書き換えた面は元の面の ID のまま、足した面は 0
  - 種類 `array`（`ArrayMeshModifier`、Topology。新規）
    - 引数 `count`（1〜64。元を含む）・`relativeOffset`（対象の外接箱の大きさに対する倍率、±10）・`constantOffset`（±10）
    - k 個目の写しは k ×（倍率 × 箱の大きさ ＋ 一定量）だけずらす。全体は全頂点・全面・LineGroup を写し、それ以外は厚みと同じ規則の面とその頂点を写す。元は残し、写しを末尾に足す
    - ID：写しの頂点・面は元と同じ ID（重複）。下流の範囲は写しのすべてに当たる（確認：配列 → ベベルで 3 個すべての辺がベベルされた）
- 足したファイル：`Core/Ops/EdgeBevelOps.cs`、`Core/Modifiers/Kinds/BevelMeshModifier.cs`・`ArrayMeshModifier.cs`
- 変えたファイル：`EdgeBevelTool.cs`、`MeshModifierAudit.cs`（種類の自己試験 4・5：ベベルの頂点数・面数・ID・全体の範囲の失敗・入力を書き換えない、配列の全体と面 1 枚の範囲）、`PolyLing_モディファイア作成ガイド.md`（5 節）
- 確認（MCP）
  - 置き換え前後の一致：変更前のコードで箱 3 つに `edgeBevel` を 4 回（分割 1 平坦／分割 3 フィレット→分割 2 平坦の 2 回／角丸・分割ありの箱に分割 2 フィレット）掛けて `getRawData` と CSV を控え、変更後に同じ手順で比べた。`getRawData` は位置・頂点 ID・面・面の UV 番号まで一致。CSV の差は objectId と、`createCube` が振る面 ID だけ（面の頂点・UV・法線の番号は全行一致）
  - `queryMeshModifierAudit`：発見・往復・範囲・種類の自己試験とも 0（種類 10。ベベル・配列の試験 6 件合格）
  - 箱（`repairVertexIds AssignMissing`）に処理列パネルからベベルを追加すると全体の範囲で Failed（理由が状態に出る）→ 辺 1 本の選択を範囲に登録して Ok（頂点 10・面 7、新しい頂点は辺の端の ID）→ 数値欄で量・分割数・フィレットを変更、Undo・Redo → 配列を追加し個数 3・一定の間隔 x=0.3（頂点 48・面 30）→ ↑で配列→ベベルの順（写し 3 個すべてがベベルされる）、Undo・Redo → 配列の無効化（頂点 16・面 10）、Undo・Redo → 配列の範囲を面 1 枚の選択に（その面だけが写る。頂点 24・面 12）→ 元メッシュの面の頂点を `moveSelectedVertices` で動かすと「処理器で作り直した」で写しも追随、Undo で戻る → CSV・二進（.plrf）に保存して読み込むと項目・引数・範囲・結果の位置が残る → 削除・適用とそれぞれ Undo・Redo → ベベルの量のスライダーを 2 回動かして数値欄で確定すると Undo 1 回で動かす前、Redo で確定値
  - `queryUiAutomationAudit`：配列・ベベルの項目を選んだ状態とも誤り 0
  - `queryCommandAudit`：処理列の種類の範囲の宣言漏れ 0、action 衝突 0（他の件数は段 5 と同じ既存のもの）
  - `queryProjectSyncDiff`：via=binary 差なし、via=csv は既存の models[0].materialReferences の差だけ
  - 見た目は各段で `unity_capture`（`SandBox/Captures/stage7a`）。試験のファイルは片付けた
- 確かめていないこと：マウスのドラッグによるベベル（`StartBevel` → `UpdateBevel` → `TryTakeBevelFromDrag`）。UI 自動操作では起こせない。同じ `ExecuteBevel`（→ ops）を通る

#### 段 7b：ブーリアンと格子

- 状態：完了（2026-11-01）
- 決めたこと
  - 枠組み（参照の行列）
    - `MeshModifierInput.ReferenceToSource`（キー → 「参照のローカル → 元メッシュのローカル」＝`元.BindWorldToVertexMatrix × 参照.VertexToBindWorldMatrix`）。`ModifierChainHandler.RunItem` が作り、項目の控えに持ち、`SyncDrag` にも渡す
    - 項目の指紋（`ItemSig`）に参照ごとのこの行列の署名を混ぜる（相対なので、相手と元メッシュを一緒に動かしても変わらない）
    - `ModifierEvaluator.AddChainEntry` は `chain.source` 以外の参照を `MatrixInputs` に入れ、参照が 1 つでもあれば元メッシュも入れる（参照の無い処理列は今までどおり位置の署名だけ）
    - `MarkMatrixChanged` は状態 Handler の処理列の節点も見る（サブディビジョンの節点は見ない）。印は `EvaluatePending` が処理器で流す
  - ブーリアン：種類 `boolean`（`Kinds/BooleanMeshModifier.cs`、Topology）
    - 引数 `other`（int、IsMeshRef。Required にしない：パネルの「追加」は引数なしで足すため。相手が無いうちは失敗で理由を出す）・`op`（既定 Subtract）・`mergeVertices`（既定 true）・`mergeThreshold`（0〜0.1、既定 `DefaultMergeThreshold`）・`epsilon`（0〜0.01、既定 `DefaultEpsilon`、0 以下は既定値）
    - 既存の LimitKey `Boolean.MergeThreshold` は下限しか無く（`ParameterLimits.cs:139`）スライダーが作れないので使わない
    - 本体は `BooleanOps.Perform(op, 入力, 単位, 相手, ReferenceToSource, 単位, …)` を 1 回。範囲は全体だけ。結果の頂点で入力の頂点から結合距離（`ActualMergeThreshold`、下限 1e-6）以内のものに一番近い入力の頂点の ID を写し（格子ハッシュ）、他の頂点と全面は 0
  - 格子：種類 `lattice`（`Kinds/LatticeMeshModifier.cs`、PositionsOnly）。`DeformerMeshModifier<LatticeDeformer>` を継ぐ（作業軸は段 6 の基底の `axisOrigin`・`axisRotation`）
    - 引数 `cellsX/Y/Z`（1〜32、既定 2）・`center`（±10、既定 0）・`size`（`LatticeGrid.MinThickness`〜20、既定 1）・`pointIndices`（int[]）・`pointOffsets`（float[]）。配列は「詳細」で書く。点が無ければ恒等。中心 ±10 と大きさの上限 20 はこの段で決めた値
    - 共用の組み立て `LatticeDeformer.Configure(cells, center, size, indices, offsets, out error)`：セル数 → 中心と大きさ → 作り直し → 長さと索引の範囲の検査 → `SetCurrent`。失敗の文言はコマンドの受け口と同じ
    - 基底に `TryWriteParams`（既定は `WriteParams` を呼んで true）を足し、格子はそれを上書きして写せない引数を失敗にする
    - `LatticeToolHandler.ExecuteFromCommand` は `BeginPlacement → Configure（失敗なら Cancel）→ BeginDeform → ApplyDeform → Commit` にした（空の検査は残す）。違い：長さが合わないとき、残っていたセッションを先に取り消してから失敗する
  - 取り込み口：`LatticeToolHandler.CommitToModifierChain`（PLToolAction）と格子パネルの「処理列へ取り込む（非破壊）」（`lattice.toChain`、変形中だけ有効）
    - 選択を持つ描画オブジェクトが 1 つ・スキンド化していない・処理列の結果でないこと。ツールが表示に使う行列（`TransformRowMatrix(バインド表示)`）で作業軸をオブジェクトのローカルへ移す。拡大が一様でない・せん断・鏡映は失敗。一様な拡大は中心・大きさ・ずれを割り戻す
    - 範囲は変形の対象と同じ頂点（`DeformApplier.CollectSelectedVertices` を公開にして共用）。全頂点なら全体。ID の無い頂点だけなら失敗
    - プレビューを取り消して `addMeshModifier`（処理列があればそのスタック名）を送る。Undo 1 件。結果の文言は PLToolState `chainImportMessage`
- 確かめの中で直した不具合
  - 処理列の処理器（段 3 から）：失敗の後で失敗の前の引数へ戻すと、指紋が最後に書いたときと同じになり、「書いた結果がそのまま残っていれば何もしない」の早期の戻りで状態が Failed のまま残った（ブーリアンの範囲を選択→全体へ戻して発見）。失敗したときに「最後に書いた」の印（`WrittenFp`）を外すようにした（項目の控えは残るので使い回しは効く）
- 足したファイル：`Core/Modifiers/Kinds/BooleanMeshModifier.cs`・`LatticeMeshModifier.cs`
- 変えたファイル：`IMeshModifier.cs`（`ReferenceToSource`）、`ModifierChainHandler.cs`（行列・指紋・失敗の印）、`ModifierEvaluator.cs`（`AddChainEntry`・`MarkMatrixChanged`）、`DeformerMeshModifier.cs`（`TryWriteParams`）、`LatticeDeformer.cs`（`Configure`）、`DeformApplier.cs`（`CollectSelectedVertices` を公開）、`LatticeToolHandler.cs`（`ExecuteFromCommand`・取り込み口）、`PlayerLatticeSubPanel.cs`（ボタン）、`MeshModifierAudit.cs`（自己試験 6・7）、`PolyLing_モディファイア作成ガイド.md`（3.3・6・7・8）、`PolyLing_機能の場所.md`
- 確認（MCP）
  - 置き換え前後の一致：変更前のコードで 2 つの箱（回転・角丸つき／一部選択）に `applyLatticeDeform` を 2 回（2×2×2・作業軸 Z20°／3×1×2・作業軸 X10°）と失敗 2 回（索引の範囲外・長さ違い）を掛けて CSV を控え、変更後に同じ手順で比べた。失敗の文言は同じ。`repairVertexIds` が振る頂点 ID は毎回変わるので両方 `ClearAll` してから比べ、違う行は objectId と `createCube` が振る面 ID だけ（頂点の行・面の頂点と UV の番号は全行一致）
  - `queryMeshModifierAudit`：発見・往復・範囲・種類の自己試験とも 0（種類 12。ブーリアン・格子の試験 9 件合格）
  - ブーリアン：箱 A（2×2×2 分割、ID あり）に処理列パネルからブーリアンを追加（相手が無いうちは Failed と理由）→ 引数欄で相手 B（(0.4,0.4,0.4) に置いた辺 0.6 の箱）を選ぶと差で頂点 56・面 108・穴 0、角 (0.5,0.5,0.5) が消え、A と同じ位置の頂点は A の ID、切り口は 0 → 和で頂点 68・面 132 → B をオブジェクトとして動かす（`moveObjects`）と lastPass「moveObjects … 処理器で作り直した」で頂点 72・面 140、Undo・Redo で 56/108 ↔ 72/140 → B の頂点を動かす（`moveSelectedVertices`）と 70/136、Undo で戻る → A の頂点を動かすと lastPass「処理器で作り直した」、Undo で戻る → 選択の面を範囲に登録すると Failed（範囲は全体だけ）、全体へ戻すと Ok（上の不具合を直してから）
  - 格子：ブーリアンの後に格子をパネルから追加 → 詳細で `pointOffsets` だけ書くと Failed（長さ違い）、`pointIndices` も書くと Ok（頂点数・面数はブーリアンのまま。(0.5,0.1,0.4) が重み 0.16 で +0.048 動く）→ セル数を 1 にすると Failed（索引の範囲外）、索引を 7 にして Ok → 面 1 枚の選択を範囲に登録すると、その面の頂点 ID の頂点だけが動く → ↑で格子→ブーリアンの順（頂点 114・面 220）、Undo・Redo → 無効化（Disabled、56/108）、Undo・Redo → CSV・二進（.plrf）に保存して読み込むと項目・引数・参照（other の ObjectId）・結果（114/220、位置）が残り、読み込んだ後も B の移動で流し直される（格子は使い回し）→ 削除・適用とそれぞれ Undo・Redo
  - 取り込み口：オブジェクトの行列が T(1,0,0)・R_y(90°)・S(2) の箱を全選択し、作業軸（原点 (1,0.2,0)・Z20°）で格子ツールの姿勢設定 → 変形開始 →「処理列へ取り込む」で処理列が作られ、`axisOrigin=(0,0.1,0)`・`axisRotation=(0,-90,20)`・中心と大きさがツールの値の 1/2（計算どおり）、ツールは Idle に戻り、Undo 1 回で処理列が消える
  - `queryUiAutomationAudit`：ブーリアン・格子の項目を選んだ状態とも誤り 0
  - `queryCommandAudit`：処理列の種類の範囲の宣言漏れ 0、action 衝突 0（他の件数は段 5 と同じ既存のもの）
  - `queryProjectSyncDiff`：via=binary 差なし、via=csv は既存の models[0].materialReferences の差だけ
  - 見た目は各段で `unity_capture`（`SandBox/Captures/stage7b`）。試験のファイルは片付けた
- 確かめていないこと
  - 格子ツールで制御点を動かしてから取り込んだときの、ずれの割り戻しと見た目の一致。制御点はビューポートのドラッグでしか動かせず、UI 自動操作では起こせない（ずれは中心・大きさと同じ 1/s で割り戻す。式は上の確認の作業軸と同じ）
  - マウスでのドラッグ中の追随（段 5・6 から）
- 段の範囲外で見つけたこと
  - `repairVertexIds` のように頂点 ID だけを変えるコマンドは、評価器のきっかけ（GPU バッファの作り直し・頂点ドラッグの同期・コマンドの終わりの行列の署名）のどれにも当たらず、下流の項目が流し直されない。署名（`MeshSignature.Positions`）は頂点 ID を含むので、次に何かのきっかけが来れば流れる。処理列に限らずモディファイアスタック全体の話
- 決めてあったこと（7a の計画で決めた）
  - 枠組み：今は相手のオブジェクトを動かしても処理列が作り直されない（`ModifierChainHandler.RunItem` は参照をメッシュだけで渡す。`ModifierEvaluator.MarkMatrixChanged` は Evaluate の節点しか見ず、`AddChainEntry` は MatrixInputs を埋めていない）。`MeshModifierInput` に「参照のローカル → 元メッシュのローカル」の行列（`元.BindWorldToVertexMatrix × 参照.VertexToBindWorldMatrix`。作り直しと同じバインド空間）を足し、処理列の節点の MatrixInputs に参照と元メッシュを入れ、`MarkMatrixChanged` が処理列の節点も見るようにし、項目の指紋にこの行列を混ぜる
  - ブーリアン：範囲は全体だけ（CSG は閉じた立体が要る）。結果の頂点で入力 A の頂点と同じ位置（結合距離以内）のものには A の ID を写し、それ以外と面は 0
  - 格子：`DeformerMeshModifier<LatticeDeformer>` を継ぎ、作業軸を段 6 にそろえる。格子の組み立て（セル数 → 中心と大きさ → ずれの検査と SetCurrent）を共用の関数にし、`LatticeToolHandler.ExecuteFromCommand` もそれを呼ぶ。動かした点の索引とずれは「詳細」の文字列欄。格子ツールで動かした点の取り込み口は 7b で決める

### 段 8：ミラーとサブディビジョン

- 3.6 のとおり
- 状態：完了（2026-11-01）
- 決めたこと
  - 枠組みに足したもの（`IMeshModifier.cs`・`MeshModifierRegistry.cs`）
    - 属性 `PLMeshModifier.PinnedLast`（末尾固定・処理列に 1 つだけ・並べ替え不可）。登録表の `MeshModifierInfo.PinnedLast`。受け口：追加は普通の種類を末尾固定の項目の前へ入れ、末尾固定の種類の 2 つ目は失敗。並べ替えは末尾固定の項目を動かせず、他の項目を後ろへ置かない（末尾へ＝末尾固定の前へ）。パネルは ↑↓ を使えなくする
    - 任意の口 `IMeshModifierDragFollow.FollowPositions(src, out dst)`：面構成を変える種類が、最後の `Evaluate` の控え（項目の控えに残るインスタンスの private の欄）で位置だけを写す。登録表の `HasDragFollow`（位置だけの種類が持つと問題に積む）
    - 処理列のドラッグ追随（`ModifierChainHandler.FollowsDrag`）は「有効な項目が位置だけか追随の口を持つ」で決める。`SyncDrag` は項目ごとに頂点数が変わってよい形にした。面構成を変える項目を含む列は、写した後に評価器が要評価の印も立て（`ModifierEvaluator.SyncFromInput`・`ModifierChainHandler.HasTopologyItem`）、離したとき・コマンドの終わりに作り直す（法線・統合の組はそこで直る）
    - `queryMeshModifierKinds` に `dragFollow`・`pinnedLast`
  - ミラー（表示）：種類 `mirrorDisplay`（`Kinds/MirrorDisplayMeshModifier.cs`、PositionsOnly・恒等・位置の口あり・PinnedLast）
    - 引数 `axis`（`SymmetryAxis` X/Y/Z）・`distance`（±10。この段で決めた値）・`type`（新しい列挙 `MirrorDisplayType` Separate=1／Merge=2）
    - 結果の MirrorType・MirrorAxis（1/2/4）・MirrorDistance は処理列が持つ。受け口の `SyncChainDisplayMirror`（`PlayerCommandDispatcher.ModifierChain.cs`）を、追加・引数（下見を含む）・並べ替え・有効・削除の書き換えの直後に SuspendRecording の中で呼ぶ：有効な項目があれば属性を書き、ミラー側が無ければ `EnableMirror`（setMirrorEnabled と同じ）、軸か距離が変われば対の軸を合わせてミラー側を作り直す。無ければ `DisableMirror`。Undo は一覧の写しとミラー対の控え（`MeshFilterToSkinnedRecord`）に乗る
    - 最後の項目の削除は結果と一緒にミラー側も消す。適用は今のまま（結果がミラー付きの普通のメッシュとして残る）
    - 処理列の結果への `setMirrorEnabled` は受け口（`PlayerCommandDispatcher.MeshAttributes.cs`）で拒否し、「ミラー（表示）」の項目へ案内する
  - ミラー側の追随の口（5. の未決を決めた）：Core に `MirrorBranchOps.RebuildMirrorSideOf(model, real, out mirror)`（対 1 つだけ。鏡像の作り直し・線分・選択の消去・対の張り直し・スキンドならウェイト写し・種別）を足した。`ModifierChainHandler.WriteResult` の後に `RebuildMirrorSide`（ミラー側の UnityMesh は遅延破棄）、ドラッグ中は `SyncDrag` で `MirrorBranchOps.MirrorPoint`（軸・距離）で写す（`MirrorPair.MirrorPosition` は距離を使わないため使わない）。モデル全体を作り直す `RebuildDerivedMirrorGeometry` は使わない（呼び出し元は今も無い）。Player 側の `ReplaceMirrorSideContents`（スタックの作り直し）も中身の作り直しをこの関数に任せた（Undo・UnityMesh・GPU への送りは呼び出し側に残す）
  - ミラー（実体化）：種類 `mirrorBake`（`Kinds/MirrorBakeMeshModifier.cs`、Topology・追随の口あり）
    - 引数 `axis`（`SymmetryAxis`）・`planeOffset`（±10）・`threshold`（0.00001〜0.1。既存の LimitKey `Mirror.Threshold` は下限しか無いので使わない）・`flipU`・`projectBoundaryToPlane`（既定 true）。範囲は全体だけ
    - 本体は `MirrorBaker.BakeMirror` を 1 回。頂点 ID は `NewToOriginalIndex` で元の ID を写す（両側同じ ID）。面は 0
    - 追随の口は `MirrorBaker.MapPositions`（新しく足した公開の関数。BakeMirror の Step 1・5 と同じ式）
  - サブディビジョン：種類 `subdivision`（`Kinds/SubdivisionMeshModifier.cs`、Topology・追随の口あり）
    - 引数 `level`（`SubdivisionOps.LevelMin`〜`LevelMax`）。範囲は全体だけ
    - 本体は `SubdivisionOps.TryBuild` を 1 回。`TryBuild` に「頂点の点の元の入力頂点」（`vertexPointSources`）を返す版を足し（使われる頂点の判定は `TryBuild` の 1 か所）、頂点の点に元の ID を写す。他の頂点と面は 0
    - 追随の口は控えたステンシルの `Apply`
  - 既存の subdivideMesh のスタックを処理列パネルに出す
    - 写し：`ModifierStackView.IsSubdivision`（`ViewInterfaces.cs`）。`LiveViews` が `SubdivisionSync.CollectLinks` の親・子を処理列の元・結果の欄に入れ、つながりの項目に ItemId を入れる
    - パネル（`PlayerModifierChainSubPanel`）：編集対象が親か子なら、その項目を種類 `subdivision` の宣言（表示名・引数 level）で同じ見た目に出す。回数の確定は `setModifierStackStepArgs`（スライダーを動かしている間は送らない）、削除は `releaseSubdivision deleteChild=true`、適用は `deleteChild=false`。追加・↑↓・有効・処理範囲・詳細は使えなくし、対象欄に理由を出す
    - `SubdivideMeshCommand` に子を渡す形（既存のサブディビジョンパネルの「作る」と同じ）は、子がスタックの出力なので `ModifierOutputGuard` が拒否する（MCP で確かめた）。そのため回数は `setModifierStackStepArgs` で送る
- 足したファイル：`Core/Modifiers/Kinds/MirrorDisplayMeshModifier.cs`・`MirrorBakeMeshModifier.cs`・`SubdivisionMeshModifier.cs`
- 変えたファイル：`IMeshModifier.cs`、`MeshModifierRegistry.cs`、`ModifierChainHandler.cs`（追随・ミラー側・`HasTopologyItem`）、`ModifierEvaluator.cs`（`SyncFromInput` の印）、`MeshModifierAudit.cs`（自己試験 8〜10）、`SubdivisionOps.cs`（`TryBuild` の版）、`MirrorBaker.cs`（`MapPositions`）、`MirrorBranchOps.Create.cs`（`RebuildMirrorSideOf`）、`PolyLingPlayerViewerCore.GeneratedMesh.cs`（`ReplaceMirrorSideContents`）、`PlayerCommandDispatcher.ModifierChain.cs`（末尾固定・`SyncChainDisplayMirror`・最後の削除・照会）、`PlayerCommandDispatcher.MeshAttributes.cs`（setMirrorEnabled の拒否）、`PanelCommand.Query.cs`（照会の結果の宣言）、`ViewInterfaces.cs`・`LiveViews.cs`、`PlayerModifierChainSubPanel.cs`、`PolyLing_モディファイア作成ガイド.md`（3.1・5・7・8）
- 確認（MCP。画面の操作は uiClick・uiSetValue）
  - 置き換え前後の一致（`ReplaceMirrorSideContents`）：変更前のコードで「createCube（スタックとして残す）→ setMirrorEnabled → setModifierStackStepArgs（高さと分割数）」のミラー側を CSV に控え、変更後に同じ手順で比べた。違う行は objectId と作成日時だけ（ミラー側の頂点・面・法線・mirrorpairs.csv は一致）
  - `queryMeshModifierAudit`：発見・往復・範囲・種類の自己試験とも 0（種類 15。サブディビジョン・ミラー（実体化）・ミラー（表示）の試験 10 件合格）
  - 処理列パネルから箱（ID あり）にサブディビジョン（頂点 168→698・面 182→696）→ ミラー（表示）（ミラー側のオブジェクトができ、結果と同じ頂点数）→ ミラー（実体化）を追加すると末尾固定の前に入る（並びはサブディビジョン・実体化・表示）。表示の項目を選ぶと ↑ は押せず、実体化の項目では ↓ が押せない。`moveMeshModifier` で表示を動かすのと 2 つ目の表示の追加は失敗、実体化を末尾へ動かすと表示の前に入る（Undo・Redo）。実体化の `planeOffset` を引数欄で変えるとミラー側も作り直され（1356→1395 頂点）、Undo・Redo でミラー側も戻る。表示の `distance` を 1 にするとミラー側の頂点が x→2−x に写る。表示の無効化でミラー側が消え、Undo で戻り Redo で消える。下見（preview=true 2 回）→ 確定で Undo 1 回で下見の前に戻る
  - 元メッシュの全頂点を `moveSelectedVertices` で動かすと、結果とミラー側が同じだけ動き、`lastPass` は「処理器で作り直した」（面構成を変える項目を含むので作り直しの印が立つ）。Undo で戻る
  - 処理列の結果への `setMirrorEnabled` は拒否
  - CSV・二進（.plrf）に保存して読み込むと項目・引数・結果（頂点 1396・面 1392）・ミラー側（位置）が残り、読み込んだ後も表示の距離の変更と頂点の移動にミラー側が追随する
  - 実体化の削除・適用・最後の項目（表示）の削除（結果とミラー側が一緒に消える）とそれぞれ Undo・Redo
  - 既存の subdivideMesh：親を選ぶと処理列パネルに「サブディビジョン」1 項目で出て、追加は押せない。回数を 2 にすると子が 26→98 頂点、Undo・Redo。親の頂点の移動に子が追随する（今までどおり）。削除（子が消える）・適用（スタックが外れ子が残る）とそれぞれ Undo
  - `queryUiAutomationAudit`：既存の subdivideMesh の項目・実体化・サブディビジョン・表示の項目を選んだ状態とも誤り 0
  - `queryCommandAudit`：処理列の種類の範囲の宣言漏れ 0、action 衝突 0（他の件数は段 5 と同じ既存のもの）
  - `queryProjectSyncDiff`：via=binary 差なし、via=csv は既存の models[0].materialReferences の差だけ
  - 見た目は各段で `unity_capture`（`SandBox/Captures/stage8`）。試験のファイルは片付けた
- 確かめていないこと：マウスで頂点をドラッグしている最中の追随（UI 自動操作では起こせない。`moveSelectedVertices` は同じ `SyncFromInput` → `SyncDrag` を通る）
- 段の範囲外で見つけたこと
  - 処理列の結果は元メッシュの `MeshObject.Clone()` で作るので（`CreateChainResult`、段 4）、元メッシュの BoneTransform（位置など）を写したまま元メッシュの子に置かれ、元メッシュに位置があると結果がその分ずれて二重に動いて見える（createCube `worldPosition=(0.8,0,0)` で結果のローカルも (0.8,0,0)、親子で 1.6）。サブディビジョンの子は新しい MeshObject なのでローカルが単位
  - 既存のサブディビジョンパネルの「作る」は子を `targetMasterIndex` に渡すので、子がある状態で回数を変えると `ModifierOutputGuard` が拒否する（ログ「出力は編集できません → cage_subdiv」）

### 段 9：Blender との比較

- 「板 → 厚み → ベベル → 曲げ」「箱 → ブーリアン → ベベル」「髪 → パイプ → 揺れ鎖」をシナリオにし、MCP で流す
- 合否：途中で値を変えると下流が追随する／Undo で戻る／保存再読込の後に結果の頂点数・面数・位置が一致する／必要な操作の回数
- Blender 側は同じ作業の手順と操作回数を表にして並べる
- 状態：完了（2026-11-01）。手順と回数の表・合否・劣る点は `PolyLing_モディファイア比較_Blender_Maya.md`（Maya も並べた）
- 決めたこと
  - シナリオ 3 本はシナリオ置き場のフォルダ「枠組み段9」（`枠組み9_板_厚み_ベベル_曲げ`・`枠組み9_箱_ブーリアン_ベベル`・`枠組み9_髪_パイプ_揺れ鎖`）。各段の後に確認の項目を置き、照会・`getRawData`・`unity_capture` を見て `continueScenario` で進める。後半に途中変更 → Undo → Redo → CSV 保存・読込 → 二進保存・読込。取り込み用の写しは `SandBox/stage9fw/scn/`
  - ベベルの範囲はパーツ選択辞書（`savePartsSet` → `scope.kind=PartsSet`）で渡す（`repairVertexIds` が振る ID は毎回変わり、シナリオに書けないため）
  - 「髪 → パイプ → 揺れ鎖」は既存のモディファイアスタックで行う。処理列の種類は出力がメッシュ 1 つで（`IMeshModifier.cs:103`）、ボーンを作る揺れ鎖は載らない
  - 操作回数は 3 者同じ基準（比較の文書の 1.）。PolyLing は画面の経路から数え、1 回通しての実測はしていない
  - 段 8 の範囲外 1（BoneTransform）を直した：`CreateChainResult`（`PlayerCommandDispatcher.ModifierChain.cs`）は `MeshObject.Clone()` をやめ、新しい `MeshObject` に頂点・面・線分だけを写す（`WriteResult` と同じ写し方）。Clone は BoneTransform に加えて IK・剛体・PmxBone・揺れもの・HumanBodyBone も写していた。処理列を含む保存 CSV は `_McpTrash` の下にしか無いので、既存の保存データの変換はしない
  - 段 8 の範囲外 2（サブディビジョンパネルの「作る」）は 3 つの作業で使わないので直さない
- 確認（MCP）
  - 修正：位置 (0.8,0,0) の板に translate を足し、CSV の結果の `boneTransform` が修正前 `True,0.8,0,0,…`、修正後 `False,0,0,0,…`。3 面図で結果が元と重なる
  - 3 本とも途中変更で下流が追随し、Undo・Redo で戻り、CSV・二進の保存再読込で結果の頂点数・面数・位置（作業 3 はボーン 5 本の BoneTransform も）が一致（比較の文書の 2.）
  - `queryMeshModifierAudit`：発見・往復・範囲・種類の自己試験とも 0。`queryUiAutomationAudit`：誤り 0。`queryCommandAudit`：処理列の種類の範囲の宣言漏れ 0、action 衝突 0（他の件数は段 5 と同じ既存のもの）
  - `queryProjectSyncDiff`：via=binary・via=csv とも差はパーツ選択辞書の作成日時 4 件だけ（下の範囲外 6）。段 4 から出ていた models[0].materialReferences の差はこの読み込みでは出なかった
- 段の範囲外で見つけたこと（比較の文書の 5. に詳しく。5. 未決事項に載せた）
  1. ベベルで連続した辺の間に穴が空く（`EdgeBevelOps.cs:259-306` の Pass 1.5 が、隣の面を共有する 2 辺しかまとめない）
  2. パーツ選択辞書の範囲は、同じ頂点 ID の頂点が複数あると 1 つにしか当たらない（`PartsSelectionSet.cs:552`）。ID の組の範囲は全件に当たる
  3. ブーリアンの切り口の辺をベベルの範囲に入れられない（切り口の頂点 ID は 0）。角度で範囲を決める手段が無い
  4. 梯子の根元は作り直しのたびに形から決め直す（`EdgeRibbonFaceOps.cs:412-420`）。両端の高さの順が入れ替わる編集でパイプの始まりと鎖の根元が反対の端へ移る
  5. `saveProjectBinary` は保存先のフォルダが無いと失敗する
  6. パーツ選択辞書の作成日時が保存再読込で変わる
  7. `createEdgePipe`・`placeSpringBoneLadderChains` に PLUiRoute が無い

### 段 10：モーション（別の作業線。この計画では方針だけ置く）

- レイヤーの重みを、既存のスカラートラックの形で時間変化させられるようにする。Studio も同じ評価の写しにそろえる
- 本体の再生を、平らなトラックではなく編集構造の評価（`sampleMotionEdit` は小数フレームを受ける。`PolyLing_モーション編集構造計画.md:193`）で行う。平らなトラックは書き出し・他ツール向けの写しにし、標本化の細かさは書き出し時に選べるようにする
- 着手するときは `PolyLing_モーション編集構造計画.md` に段を足して進める
- 状態：未着手

## 5. 未決事項

- ~~結果を作り直したときのミラー側の追随の口（段 8。3.6）~~ → 段 8 で `MirrorBranchOps.RebuildMirrorSideOf` に決めた
- ~~処理列の結果が元メッシュの BoneTransform を写したまま子に置かれる件（段 8）~~ → 段 9 で直した
- 既存のサブディビジョンパネルの「作る」で子があると拒否される件（段 8 の「段の範囲外で見つけたこと」）
- 段 9 で見つけたこと（段 9 の「段の範囲外で見つけたこと」1〜7、`PolyLing_モディファイア比較_Blender_Maya.md` の 5.）。Blender との比較で効くのは次の 3 つ
  - ベベルの連続した辺の穴（1）
  - 範囲の指定の手間：パーツ選択辞書が同じ ID の写しの 1 つにしか当たらない（2）、切り口の辺を範囲に入れられない・角度で範囲を決める手段が無い（3）、頂点 ID の付与が要る。作業 2 で Blender 20 回に対し 37 回、差 17 回のうち 8 回がここ
  - 梯子の根元を作り直しのたびに形から決め直す（4）

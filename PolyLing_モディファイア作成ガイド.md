# 処理列の種類（モディファイア）作成ガイド

処理列（1 つのオブジェクトに処理を積み重ねる非破壊の列）に、処理の種類を 1 つ足すときの手順。
**作者が書くのはクラス 1 つ。** 行番号は書かない（ファイルが分かれるたびに腐るため）。場所はファイル名とシンボル名で指す。

設計の正本は `PolyLing_モディファイア枠組み計画.md`（3.2〜3.5）。

---

## 1. 何ができるか

作者が書くもの

- クラス 1 つ：属性 `PLMeshModifier`、`PLParam` を付けた引数のプロパティ、計算本体 `Evaluate`
- 位置だけの種類なら、任意で速い口 `EvaluatePositions`

作者が書かずに済むもの（枠組みが受け持つ）

| もの | 受け持つ所 |
|---|---|
| 発見（登録） | `MeshModifierRegistry`（読み込まれている全アセンブリを属性で探す。別パッケージからも足せる） |
| コマンド・MCP | 共通の 6 本（`addMeshModifier`・`setMeshModifierArgs`・`moveMeshModifier`・`setMeshModifierEnabled`・`removeMeshModifier`・`applyMeshModifierChain`）と照会 2 本（`queryMeshModifierChain`・`queryMeshModifierKinds`） |
| 保存・読込・リモート転送 | モディファイアスタックの CSV・JSON・二進。引数は文字列の対で持つ |
| Undo | 共通コマンドの受け口 |
| 引数欄 | 処理列パネル（`PlayerModifierChainSubPanel`）が `ParamFieldBuilder` で `PLParam` から作る |
| 処理範囲の絞り込み | `MeshModifierScope` を引き直して `MeshModifierInput.Scope` に渡す。位置だけの種類は範囲の外を枠組みが入力の位置へ戻す |
| 評価のきっかけ | `ModifierChainHandler`（評価器の処理器 `@chain`）。変わった項目より前の結果は使い回す |

---

## 2. クラス 1 つの書き方

置き場は `Runtime/Poly_Ling_Main/Core/Modifiers/Kinds/`（本体の外のパッケージに置いてもよい）。名前空間は `Poly_Ling.Modifiers` を使う。
最小の例は `TranslateMeshModifier`（平行移動）。

```csharp
[PLMeshModifier("translate", DisplayName = "平行移動", Category = "deform",
                Change = MeshModifierChange.PositionsOnly,
                Description = "範囲内の頂点を、オブジェクトのローカル座標で offset だけ動かす")]
public sealed class TranslateMeshModifier : IMeshModifier, IMeshModifierPositions
{
    [PLParam(Description = "移動量（ローカル座標）", Min = -10f, Max = 10f, Step = 0.01)]
    public Vector3 Offset { get; set; } = Vector3.zero;

    public MeshObject Evaluate(MeshModifierInput input, out string error) { ... }
    public bool EvaluatePositions(MeshModifierInput input, Vector3[] src, Vector3[] dst, out string error) { ... }
}
```

決まり

- public な引数なしコンストラクタを持つ（`Activator.CreateInstance` で作る）
- public なプロパティには、引数かどうかにかかわらず必ず `PLParam` を付ける。引数にしないものは `PLParam(Ignore = true)`。付け忘れは登録されない（発見の問題に出る）
- 引数は public に読み書きできるプロパティ。キーはプロパティ名の先頭を小文字にしたもの
- `Evaluate` は入力（`input.Source`）・モデル・選択・画面に触れない。入力を `Clone()` して新しいメッシュを返す
- 失敗したら `null` を返し、`error` に理由を入れる。理由は項目の状態に出て、以降の項目は流れない（Blocked）

---

## 3. 宣言

### 3.1 属性 `PLMeshModifier`（`Core/Modifiers/IMeshModifier.cs`）

| 欄 | 内容 |
|---|---|
| 種類名（コンストラクタ引数） | 項目の Action に入る。全種類で一意。**コマンドの action 名と同じ名前は使えない**（例：`solidify` は `SolidifyCommand` と重なるので、厚みは `thickness`） |
| `DisplayName` | 処理列パネルの種類の一覧に出る |
| `Category` | 一覧の並べ分け（`deform`・`topology` など） |
| `Change` | `PositionsOnly`（頂点の位置だけ。ドラッグ中も追随）／`Topology`（面構成も変える。ドラッグを離したときに作り直す） |
| `Description` | `queryMeshModifierKinds` に出る説明 |
| `PinnedLast` | 末尾固定（段 8）。処理列に 1 つだけ置け、いつも最後で並べ替えできない。追加は普通の種類をこの項目の前へ入れ、並べ替えはこの項目を動かさない。列の最後の形（結果）に掛かるもの用（例：「ミラー（表示）」`mirrorDisplay`） |

`PLModifierAttribute`（PanelCommand に付ける、スタックへの記録の宣言）とは別物。混ぜない。

### 3.2 引数 `PLParam`

- **数値（float・int・Vector2・Vector3）には範囲を宣言する。** `Min`・`Max`、または `LimitKey`（`ParameterLimits` の `<キー>.Min`・`<キー>.Max`）。無いとスライダーが付かず、`queryCommandAudit` の「処理列の種類の範囲の宣言漏れ」に落ちる（完了条件）
- 整数は `Step = 1`
- `Description` の最初の句が引数欄の見出しになる。短く分かる言葉で書く
- 列挙型は 6 個以下なら選択ボタン、それより多ければドロップダウン。真偽はチェック
- 他のオブジェクトを参照する引数は `IsMeshRef = true`（int）。評価のときは `input.References[キー]` に解決済みのメッシュが入る。元メッシュ・結果は指せない。書き方は 3.3
- 配列などは欄を作らず、パネルの「詳細」の文字列欄で書く

### 3.3 参照オブジェクトを使う種類（例：`BooleanMeshModifier`）

- `input.References[キー]` のメッシュは**相手のローカル（頂点の格納空間）**の位置を持つ。元メッシュのローカルで読むには `input.ReferenceToSource[キー]`（相手のローカル → 元メッシュのローカル。`元.BindWorldToVertexMatrix × 相手.VertexToBindWorldMatrix`）を掛ける。入力（`input.Source`）はどの項目でも元メッシュのローカルなので、この行列はどの位置の項目でも使える
- 流し直しは枠組みが受け持つ：相手の頂点の移動（位置・形・ID の署名）と、相手や元メッシュのオブジェクトの移動（この行列の署名。書き込みのあるコマンドの終わりに評価器が見る）。作者は何もしない
- `Required = true` にしない。処理列パネルの「追加」は引数なしで足し、相手は引数欄のオブジェクト選択で選ぶ。相手が無いうちは `Evaluate` で失敗にし、理由を `error` に入れる
- 範囲が意味を持たない（相手と閉じた立体で演算する等）なら、全体以外の範囲を失敗にする（ブーリアン）

---

## 4. 処理範囲

`input.Scope`（`ResolvedMeshModifierScope`、`Core/Modifiers/MeshModifierScope.cs`）は入力のメッシュの索引で持つ。

| 使うもの | 中身 |
|---|---|
| `IsAll` | 全体か |
| `ContainsVertex(i)` | 頂点が範囲に入るか（頂点・辺の両端・面の頂点の和） |
| `Vertices`・`Edges`・`Faces` | 範囲の頂点・辺・面 |
| `AffectedVertices` | 範囲が触れる頂点 |

決まり

- **範囲の外は入力のまま通す。** 位置だけの種類は枠組みが範囲の外を戻すが、面構成を変える種類は作者が受け持つ
- 範囲は頂点 ID で保存されている。項目ごとに、その項目の入力へ ID で引き直す。下流の項目の範囲が上流の結果にも当たるよう、**作った頂点には元の頂点の ID を付ける**（5 節）
- ID の無い頂点は範囲に入らない。試すときは元メッシュに `repairVertexIds mode=AssignMissing` を明示して掛ける（勝手に振る処理は足さない）
- 範囲の引数（`scope.*`）は全種類共通。作者は宣言しない

---

## 5. 面構成を変える種類の決まり（例：`ThicknessMeshModifier`・`BevelMeshModifier`・`ArrayMeshModifier`）

- 出力は 1 つのメッシュ。範囲の外の面はそのまま残す
- 外した面だけが使っていた頂点は外し、索引を詰める。`LineGroup` の `Order`・`ParentVertex` も付け替える（`LineGroup` が使う頂点は外さない）
- **ID を新しく振らない。** `MeshObject.AddVertex`／`AddFace` は未設定の ID を自動で振るので、出力を組むときは `AddVertexRaw` と `Faces.Add` を使い、ID を元から写す
  - 作った頂点：元の頂点の ID（1 つの頂点から複数作れば同じ ID が複数になる。重複は許される）
  - 作った面：元の面があればその ID、無ければ 0（未設定）
- 実処理が別の所にある（ツールと共用する）なら、ツールの動作を変えずに写しの情報を足す。厚みでは `FaceGroupSolidifier.Result` に `SourceToVertices`（頂点）と `FaceSources`（面）がある
- ツールの実処理を切り出すとき（例：ベベル `EdgeBevelOps`）
  - 実処理は「渡されたメッシュをその場で書き換え、写し先を返す」形にする。ツールは編集対象の生きたメッシュを、種類は入力の `Clone()` を渡す。実装は 1 つだけ持つ
  - 頂点・面を消すのは `MeshObject.RemoveVertices`／`RemoveFaces` を通す。選択・パーツ選択辞書の付け替えは、そのイベントを MeshContext が購読して行う。`Clone()` はイベントを写さないので、種類の写しでは付け替えは起きない（実処理から付け替えを外に出す必要はない）
  - Undo・選択の消去・GPU への通知・ログは実処理に入れず、ツール側に残す（種類は下見で何度も流れる）
  - 写し先は「処理後の頂点ごとの元の頂点番号」と「新しく作った頂点か」を返す（`EdgeBevelOps.Result.SourceOf`・`Created`）。種類はこれで新しい頂点に元の ID を写す。ツールは使わないので動作は変わらない
  - 置き換えたら、変更前のコードでコマンドの結果（`getRawData`・保存した CSV）を控え、変更後と比べる
- 範囲の要る種類（ベベルは辺）は、全体の範囲・要る要素の無い範囲を失敗にし、理由を `error` に入れる。全体を勝手に対象にしない。対象を範囲以外で決める口を設けるなら、引数で明示して選ばせる（ベベルの `limitMethod=Angle` は、隣の 2 面の角度が `angle` 以上の辺を全体または範囲の中から拾う）
- 下見（スライダーのドラッグ中）でも毎回流し直されるので、重さを確かめる
- **ドラッグ中も追随させたい**なら `IMeshModifierDragFollow.FollowPositions(src, out dst)` を実装する（段 8。例：`SubdivisionMeshModifier`・`MirrorBakeMeshModifier`）
  - 最後の `Evaluate` で対応（ステンシル、頂点の出自など）を private の欄に控え、入力の位置から出力の位置を求める。面構成は最後の `Evaluate` のままとみなす。控えが無い・数が合わないときは false
  - 控えはプロパティにしない（public なプロパティはすべて引数として拾われ、`PLParam` が無いと登録されない）。引数でない値を外に出すときはメソッドにする
  - 枠組みは `Evaluate` を呼んだ同じインスタンスにこれを呼ぶ。この口を持つ項目を含む処理列も、ドラッグ中に位置だけ写し、離したとき・コマンドの終わりに作り直す（法線・統合の組などはそこで直る）
  - 試験：位置を動かした入力の `Evaluate` と、元の入力で `Evaluate` した後の `FollowPositions` が一致すること（`MeshModifierAudit` 8・9）。統合などの組が位置で変わる種類は、組の変わらない動かし方で比べる

---

## 6. デフォーマを包む

`IMeshDeformer`（`Tools/Deformers/`）は、基底 `DeformerMeshModifier<TDeformer>`（`Core/Modifiers/Kinds/DeformerMeshModifier.cs`）を継ぐ薄いクラスで種類になる。例は `DeformerMeshModifiers.cs`。

- 基底が受け持つもの：作業軸（引数 `axisOrigin`・`axisRotation`。オブジェクトのローカル座標）、範囲、`DeformContext`（範囲内の頂点から作る）、`Evaluate` と位置の口
- 作者が書くもの：属性（`Change = PositionsOnly`）、デフォーマの引数のプロパティ（範囲付き）、`WriteParams`（引数をデフォーマの Params へ写す）
- 引数がデフォーマへ写せないことがある（配列の長さ違い・索引の範囲外など）なら、`TryWriteParams` を上書きして false と理由を返す（例：`LatticeMeshModifier`。格子の組み立ては `LatticeDeformer.Configure` を格子変形コマンドの受け口と共用する）
- `DeformerRegistry` に登録しないデフォーマ（格子）も、この基底で種類にできる
- 新しいデフォーマを `DeformerRegistry` に登録したら、包むクラスも 1 つ足す。足していないと `queryMeshModifierAudit` の「種類の自己試験」で「登録されたデフォーマがすべて種類になっている」が失敗する
- カメラなど画面に依存する設定は切る（曲げは `UseCameraBendPlane = false`）

---

## 7. 試し方

| 検査 | 見るもの | 合格 |
|---|---|---|
| `queryMeshModifierAudit` | 発見・引数の往復（既定値×3 種類の範囲）・範囲の自己試験・種類の自己試験（デフォーマの包む漏れ・位置の口と `Evaluate` の一致・範囲の外が動かない・厚み・ベベル（辺のつなぎ目の穴・蓋・角度で決める辺・厚みの後の辞書）・配列・ブーリアン・格子・サブディビジョン・ミラー（実体化）・追随の口と `Evaluate` の一致・末尾固定）。面構成を変える種類を足したら、頂点数・面数・ID の写しの試験を `MeshModifierAudit.KindSelfTest` に足す | `problems`・`roundTripFailures`・`scopeFailures`・`kindFailures` が 0 |
| `queryMeshModifierAudit kind=<種類> argKeys=… argValues=…` | 指定の引数の往復 | 失敗 0 |
| `queryCommandAudit` | 「処理列の種類の範囲の宣言漏れ」 | 0 |
| `queryUiAutomationAudit` | 引数欄が UI 自動操作に載っているか | 誤り 0。**足した種類の項目を処理列パネルで選んだ状態で**実行する |
| `queryProjectSyncDiff via=binary`・`via=csv` | 保存・転送の往復 | 差なし |

画面での確かめ（MCP の UI 自動操作で行える）

1. 左ペイン「選択」→「処理列」（`leftPane.modifierChainBtn`）
2. `modifierChain.kind` で種類を選び `modifierChain.add`
3. 引数欄（`modifierChain.arg.<キー>`、スライダーは `.slider`）を変える。スライダーは下見で Undo を積まず、数値欄の確定で Undo 1 件になる
4. 並べ替え（`up`・`down`）・有効（`enabled`）・削除（`remove`）・適用（`apply`）と、それぞれ Undo・Redo
5. 元メッシュの選択を `modifierChain.scope.fromSelection` で範囲にし、範囲の中だけが変わること
6. 元メッシュの頂点を動かす（`moveSelectedVertices`）。位置だけの列は評価のパスなしで追随し、面構成を変える項目を含む列はコマンドの終わり（ドラッグなら離したとき）に「処理器で作り直した」になる（`queryModifierGraph` の `lastPass`）
7. 参照オブジェクトを使う種類は、相手の頂点の移動（`moveSelectedVertices`）とオブジェクトの移動（`moveObjects`）の両方で結果が追随すること、それぞれ Undo・Redo で戻ること
8. 各段で `queryMeshModifierChain`・`getRawData`・`unity_capture` を見る

MCP で試すときの注意

- 処理列パネルの対象は**編集対象のオブジェクト**。`createCube` の直後は作ったものが編集対象になる。元メッシュは `selectDrawablesByName` で選び直してから「追加」する（`selectAllElements` も対象を変える）
- `repairVertexIds` は選択中の描画オブジェクトに掛かる。元メッシュを選んでから掛ける。処理列を作った後に元メッシュの ID だけを変えても、評価のきっかけが無いので結果は流し直されない（項目の引数の変更などで流れる）。ID は処理列を作る前に付ける

---

## 8. よくある失敗

- 種類名がコマンドの action 名と重なり、登録されない（発見の問題に出る）
- 数値の引数に範囲を書き忘れ、スライダーが出ない
- 面構成を変える種類で `AddVertex` を使い、ID が自動で振られる。下流の範囲が別の頂点に当たる
- 面構成を変える種類で範囲の外の面を落とす
- 位置だけの種類なのに頂点数を変える（評価で失敗になる）
- `Evaluate` の中で入力を書き換える（使い回しの控えが壊れる）
- 参照の引数を `Required` にして、処理列パネルの「追加」（引数なし）が失敗する
- 相手の位置を `ReferenceToSource` を掛けずに読む（相手のオブジェクトを動かしても結果がずれたまま。指紋は変わるので流れはする）
- 試験メッシュで `new Face(a, b, c, d)` と書く。4 引数は `Face(v0, v1, v2, materialIndex)` に解決され三角形になる。四角形は材質番号まで 5 引数で書く
- 計算のための値を public なプロパティで持ち、`PLParam` が無いので登録されない（発見の問題に出る）。メソッドか private の欄にする
- 既存の処理（`MirrorBaker.BakeMirror`・`SubdivisionOps.TryBuild` など）が頂点 ID を写さないのに、そのまま返す。出自の表（`NewToOriginalIndex`・`vertexPointSources`）で元の ID を写す
- 結果オブジェクトの属性（MirrorType など）を `Evaluate` の中で書く。`Evaluate` はメッシュを返すだけ。結果オブジェクトや別のオブジェクトを変える種類（「ミラー（表示）」）は、処理列の受け口（`PlayerCommandDispatcher.ModifierChain.cs` の `SyncChainDisplayMirror`）と処理器（`ModifierChainHandler`）に書く

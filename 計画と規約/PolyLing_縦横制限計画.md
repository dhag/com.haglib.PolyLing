# PolyLing 縦横制限計画（Shift で操作座標を画面の横・縦にそろえる）

## 1. 目的
Shift を押してマウスを動かすと、PolyLing が処理する操作座標を画面の横または縦にそろえる。
OS のポインターは動かさない。頂点移動・ナイフ・点や線の指定・制御点の編集で共通に使う。

## 2. 入力経路の調査結果（コードで確認済み）
| 経路 | 場所 | 座標 |
|---|---|---|
| 押下・移動・解除 | `PlayerViewportPanel.OnPointerDown/Move/Up` | `ToViewportCoord` 済み（Y=0 下） |
| ホバー | 同 `OnPointerHover` → Viewer `EnterHoverChanged` → `PlayerViewportManager.NotifyPointerHover` | パネルローカル（Y=0 上） |
| GPU ヒットテスト | `NotifyPointerHover` 内 `adapter.UpdateFrame`（同期読み戻し）。座標は `_lastMousePos` に残り `PresentAll` の再計算でも使われる | Y=0 上 |
| ツールのホバー | 同 `ToHandlerHoverPos` → `UpdateHover` / `_activeToolHoverCallback` | Y=0 下 |
| クリック・押下・ドラッグ開始 | `PlayerVertexInteractor` が `GetHoverHit`（直前ホバー結果）を読む | — |

→ 補正は `PlayerViewportPanel` で 1 回だけ行い、Y=0 下の操作座標とホバー用のローカル座標の両方をそこから作る。
ドラッグ開始のしきい値（経路長）は実座標で判定する（クリックとドラッグの区別を変えないため）。

## 3. 既存の Shift の用途
| 用途 | 場所 | 扱い |
|---|---|---|
| 追加選択（クリック・矩形） | `MoveToolHandler.Commands.cs`、`ObjectMoveToolHandler`、`EdgeBridgeToolHandler`、`BillboardProfileToolHandler.Profile` | 方向未決定（4px 未満）は実座標を出すので、クリックは変わらない。矩形選択中は制限を使わない |
| カメラ粗動 | `CameraSpeedModifier`（右・中ボタン） | 右・中ボタン操作中は制限を使わない |
| `ViewportInputState.IsShiftHeld` を渡すだけ | 多数のハンドラ（EnrichCtx 系） | 変更なし |

## 4. 対象操作と基準点
| 操作 | ハンドラ | 使い方 | 基準点 | 状態 |
|---|---|---|---|---|
| 頂点の自由移動（中央ギズモ含む） | `MoveToolHandler` | 対象を動かす | 押下位置 `_pressOriginPos`／途中押下はその時点の操作位置 | 段5 |
| X・Y・Z 軸ギズモのドラッグ | `MoveToolHandler` | なし（既存の軸制限を優先） | — | 段5 |
| ナイフ シンプル切断（クリック式・ドラッグ式） | `KnifeToolHandler` | 点を指定 | 1 点目 `KnifeTool._simpleP0` | 段5 |
| ナイフ 頂点端点の吸着 | `KnifeTool.SnapSimplePoint` | 制限方向上の候補のみ採用 | — | 段5 |
| オブジェクト移動（原点だけ移動を含む） | `ObjectMoveToolHandler`（`ObjectMoveTool.IsFreeMoveDragging`） | 中央の自由移動＝対象を動かす。X/Y/Z 軸・回転リング・矩形／投げ縄＝なし | ドラッグ開始位置 | 段6 確認済み |
| ピボット | `PivotOffsetToolHandler` | 中央ギズモ＝対象を動かす。軸＝なし | ドラッグ開始位置 | 段6 実装（左ペインのボタンは撤去済みで画面から入れない。同じ経路は上の「原点だけ移動」で確認） |
| 面追加の点指定 | `AddFaceToolHandler` | 点を指定（吸着候補は `SnapCandidateFilter`＝`AcceptsCandidate`） | 直前の点を今のカメラで画面へ写した位置 | 段6 確認済み |
| 点指定図形の点指定 | `PointDefinedToolHandler` | 同上 | 直前に確定した点を画面へ写した位置 | 段6 確認済み |
| 処理列の制御点 | `ModifierGizmoToolHandler`（作業軸は `WorkAxisToolHandler.IsFreeHandleDragging`） | ラティス制御点の中央・作業軸の原点／Y 先端＝対象を動かす。軸・回転・ロール・矩形＝なし | ドラッグ開始位置 | 段7 確認済み |
| 格子 | `LatticeToolHandler` | 移動モードの中央＝対象を動かす。軸・拡大縮小・回転・矩形＝なし | ドラッグ開始位置 | 段7 確認済み |
| 断面（線分群 Profile） | `BillboardProfileToolHandler` | 点・ハンドル・挿入待ちの点＝対象を動かす。矩形＝なし | 挿入は押下位置、他はドラッグ開始位置 | 段7 確認済み |
| 線分群 Line／Freeform の点指定 | `BillboardProfileToolHandler`（`TryLinePointAnchor`） | 点を 1 つ以上置いていれば点を指定（Freeform の手描き中＝なし）。吸着候補は `SnapCandidateFilter`＝`AcceptsCandidate` | 直前の点を今のカメラで画面へ写した位置（頂点になっていれば GPU の値、まだなら `DisplayWorldMatrix`） | 段7 確認済み |
| 作業軸ツール単体（変形の作業軸フェーズを含む） | `WorkAxisToolHandler` | 原点（移動の中央）・Y 先端＝対象を動かす。軸・回転・ロール＝なし。吸着先は `SnapCandidateFilter`＝`AcceptsCandidate` で絞る | ドラッグ開始位置（吸着候補は掴んだずれ `_grabOffset` を引いてポインタ側で判定） | 段7 確認済み |
| カメラツール | `CameraToolHandler` | 中央ギズモ（注視点・3 面の Target の自由移動）＝対象を動かす。軸・リング＝なし | ドラッグ開始位置 | 段7 確認済み |
| ボーン編集からの直接経路 | （`ConnectBoneEditorObjectMove`。段7 で撤去） | — | — | 段7 撤去（経緯は §7 段7 の追記） |
| 独立キャンバス | UV（`PlayerUVEditorSubPanel.Canvas`）・2D押し出し・回転体・ベルト断面 | `CanvasPointerAxis`（点のドラッグだけ対象を動かす） | ドラッグ開始位置（キャンバスローカル） | 段6 確認済み |

## 5. 部品
- `View/Core/PointerAxisConstraint.cs`：状態（使い方・基準点・Shift・固定方向・引き継ぎ量）と補正。しきい値 `DirectionThresholdPx`。吸着判定 `AcceptsCandidate`。
- `View/Core/IPointerAxisClient.cs`：ハンドラが現在の操作状態から使い方と基準点を返す任意実装インターフェース。
- パネル：補正、Shift の押下・解除での再通知（KeyDown/KeyUp）、FocusOut・キャプチャ喪失・Esc での解除。
  ドラッグ開始（OnDragBegin）の直後に問い合わせ直す（押下を受け持たないハンドラは開始で初めて「対象を動かす」になるため）。
- Esc：`PointerAxisConstraint.SuppressUntilShiftUp`。その Shift の押下が終わるまで制限を外す。
  取り消された操作（問い合わせが None）は解除、Esc で終わらない操作は補正量を引き継いで続く（跳ばない）。ビューポートとキャンバスで同じ。
- Viewer：ツール切替で解除。問い合わせを現在ハンドラへ転送（ボーン編集の直接経路への問い合わせは段7 で経路ごと撤去）。
- `View/Common/CanvasPointerAxis.cs`：独立キャンバス用。Begin／Apply／End と、Shift・Esc を UI ルートで受けて点の移動をやり直す（replay）。
  キャンバスの name を `polylingCanvas.<id>`（uv / p2d / rev / belt）にする。
- 確認用コマンド `uiCanvasPointer`：キャンバスへ実際と同じ UI Toolkit のイベント（UV は Mouse 系、他は Pointer 系）を送る。

## 6. 段
- 段0 調査一覧（本書）
- 段1 共通部品
- 段2 接続口
- 段3 パネル組み込み
- 段4 ツール切替での解除
- 段5 頂点自由移動・ナイフ（ドラッグ式・クリック式）で確認
- 段6 他の操作へ展開
- 段7 段6 の未確認の確認・未対応の対応・既存の不具合（原点の目印の Shift クリック）

## 7. 進捗

### 2026-11-15 段0〜段5 完了
- 段1 `PointerAxisConstraint`（しきい値 `DirectionThresholdPx`=4、吸着許容 `CandidateTolerancePx`=1）。
- 段2 `IPointerAxisClient`。
- 段3 `PlayerViewportPanel`：押下・移動・解除を `ProcessDown/ProcessMove/ProcessUp` に分け、`ConstrainPos` で 1 回だけ補正。
  ホバー（GPU ヒット判定）には補正後の座標を Y=0 上へ戻して渡す。経路長（クリック／ドラッグ判定）は実座標。
  Shift のキーは UI ルートで TrickleDown で受け、最後にポインタが動いたパネル（`s_shiftKeyTarget`）だけが出し直す
  （フォーカスがパネルに無くても届く。PointerEnter/Leave は子要素の出入りでも届くため使わない）。
  Esc・FocusOut・キャプチャ喪失で解除。右・中ボタン操作中は制限しない。
- 段4 `SetInteractionMode` で解除。
- 段5 `MoveToolHandler`（自由移動・中央ギズモ＝対象を動かす、X/Y/Z 軸ギズモ・矩形選択・選択専用＝なし）、
  `KnifeToolHandler`（シンプル切断の 1 点目あり＝点を指定）、`KnifeTool.SecondPointSnapFilter`（2 点目の頂点吸着の絞り込み）。
- 確認用コマンド `uiViewportPointer`（down/move/up/shiftDown/shiftUp/escape を実入力と同じ処理へ通し、各段の操作座標と状態を返す）。

### 確認結果（uiViewportPointer と実キー入力）
| 項目 | 結果 |
|---|---|
| 頂点（面）の自由移動、押下時から Shift | 横に固定。面は画面の横方向だけ動く |
| 移動の途中で Shift を押す・離す | 押した時点の操作位置が基準。離した後は補正量を引き継ぎ跳ばない。操作終了で引き継ぎ量は 0 |
| X 軸ギズモ + Shift | 制限しない（既存の軸制限のまま） |
| ナイフ クリック式 | 2 点目が 1 点目と同じ高さ（p0=(40,493) p1=(345,493)）、14 面を切断 |
| ナイフ ドラッグ式 | 縦に固定（p0=(140,633) p1=(140,323)）、21 面を切断 |
| Shift だけの押下 | マウス移動なしで操作座標が更新 |
| Esc | ナイフの段を破棄し、制限も解除 |
| 頂点端点の吸着 | 制限方向から外れる頂点には吸着しない（Shift なしでは p1=(318.8,493) に吸着、Shift ありでは p1=(321,489) のまま） |
| Shift クリックの追加選択 | 維持（2 面とも選択） |
| 実キーの Shift | UI ルートで受け、ポインタのあるパネルだけが反応 |

### 2026-11-15 段6
実装：§4 の段6 の行すべて。共通の変更：
- パネル：ドラッグ開始直後の問い合わせ直し（ObjectMoveTool は 1 回ごとの差分を足すので、これが無いと最初の 1 回だけ補正前の座標で表示がずれた）。
- Esc の扱いを `SuppressUntilShiftUp` へ一本化（オブジェクト移動のように Esc で終わらない操作で、Reset 直後に制限が取り直されていたため）。
- 吸着候補の絞り込み：面追加は GPU ホバー頂点・非選択オブジェクト・ボーン／原点の各段で、点指定図形は各候補で、制限方向から外れるものを捨てて次の候補へ進む。

確認結果（uiViewportPointer／uiCanvasPointer とキャプチャ）：
| 項目 | 結果 |
|---|---|
| オブジェクト移動、押下時から Shift | 原点の目印が (191,481)→(260,481)。横だけ。ドラッグ最初の回から補正済み |
| オブジェクト移動、途中で Shift・離す | 押した時点の操作位置が基準。離すと補正量を引き継ぎ跳ばない。確定位置＝最後の操作座標（(254,395)） |
| 原点だけ移動（originOnly）の中央ギズモ | 原点だけが横に +14px。メッシュは動かない |
| X 軸ギズモ + Shift | use=None（既存の軸制限のまま） |
| オブジェクト移動中の Esc | 制限が外れ、補正量を引き継いで続く（op が (220,482) のまま跳ばない）。Shift を押し直すと再び固定 |
| 面追加 | 2 点目が 1 点目と同じ高さ。基準点は置いた点を画面へ写した位置 |
| 面追加の吸着 | 制限中、制限線から 3.4px 外れた頂点 #0 に吸着せず新規点（Shift なしの同じ位置では頂点 #0 に吸着） |
| 点指定図形 | 4 点 (60,150)(160,150)(160,437)(128,437)。4 点目は近くの頂点（y≈440）に吸着せず制限線上 |
| ナイフ（段5 の回帰） | Esc で段を捨て use=None、以後は実座標 |
| 2D押し出しキャンバス | 押下時から Shift で y 固定（pos.y=0.4000 のまま）。キーの Shift で途中から基準を取り直し、Esc で外れて跳ばず、Shift 押し直しで再固定 |
| 回転体キャンバス | 途中の Shift で横固定（pos.y=0.7538 のまま） |
| ベルト断面（パイプ）キャンバス | 途中の Shift で縦固定、Esc で外れて跳ばない |
| UV キャンバス | 途中の Shift で横固定（uv.v=0.9249 のまま）。確定後も同じ値 |

対象外（コードで確認）：
- 回転・拡大縮小：ビューのドラッグは MoveToolHandler の `OnToolDragExtra` から `RotateToolHandler`／`ScaleToolHandler.GizmoDrag(pos)` へ渡り、位置から角度・倍率を求める（`InteractionMode.cs`）。点や対象の位置を指定しない。この状態で MoveToolHandler は None を返す（確認：use=None）。
- スカルプト・スキンWペイント：ドラッグはブラシの軌跡（スカルプトは半径ドラッグもある）。点や対象の位置を指定しない。
- 回転体・2D押し出しキャンバスで Shift を押しながら点を押すのは既存の選択操作（回転体はトグルでドラッグしない）。制限は動かし始めてから Shift を押したときに効く。UV も同じ。

未確認・未対応：
- 未確認（実装済み）：処理列の制御点（`ModifierGizmoToolHandler`）、格子（`LatticeToolHandler`。姿勢設定が始まらず state=0 のまま。168 頂点選択済み、ログに出力なし）、断面の線分群（`BillboardProfileToolHandler`）、ボーン編集の直接経路（ボーンエディタを開くと操作モードがオブジェクト移動になり、この経路は使われない状態になる）、ピボット（画面から入れない）。
- 未対応：作業軸ツール単体（`WorkAxisToolHandler`。頂点・ボーンに吸着するので候補の絞り込みが要る）、`BillboardProfileToolHandler` の Line／Freeform の点指定、カメラツール。
- 段6 と無関係の既存の動き：オブジェクト移動で原点の目印を Shift クリックしても追加選択にならない。クリックがツールの `TryPickObject`（Shift＝追加）と `TrySelectIndicatorAtScreenPos`（Shift＝反転）の両方に届き、打ち消し合う（制限は use=None で座標は実座標のまま）。
  → 段7 で対応・確認済み。

### 2026-11-15 段7

調べて分かったこと（コードと画面で確認）：
- 格子の姿勢設定が始まらなかった原因：モデルに作業軸オブジェクトが無かった。`CurrentWorkAxis()`（`ModelContext.ResolveWorkAxis`）が null で `BeginPlacement` が false を返していた。
  その false を `PLToolSurface.TryInvoke` が見ずに成功として返していたので、invokeToolAction の結果にもログにも出ず、格子パネルは「頂点を選択してから開始してください」のままだった。
- ボーン編集の直接経路が働く条件：ボーンエディタの区画（`BoneEditorSection`）を表示したまま、操作モードが ObjectMove／PivotOffset 以外になったとき（`IsBoneEditorObjectMoveRouted`）。
  ボーンエディタを開くと ObjectMove になる（`ShowBoneEditorPanel`）。区画を残したままモードだけを変えるのは、R・G の一時選択（`EnterSelectSubTool`→SelectOnly）と D の面削除（`EnterDeleteFaceMode`）。
  常駐リストのマテリアルリストを開いても、ボーンエディタ表示中はモードは ObjectMove のまま（画面で確認）。
- 原点の目印のクリック：PivotOffset は `PivotOffsetToolHandler` がピック対象をすべて外しているので、選べるのは目印側（`TrySelectIndicatorAtScreenPos`）だけ。ObjectMove はツール側（`ObjectMoveTool.TryPickObject`、同じピックフィルタ）が受け持つ。
  両方が受けると Shift（追加と反転）だけでなく Ctrl（反転と反転）も打ち消し合っていた。
- カメラツール：中央ギズモのドラッグは `ComputeFreeDelta` で注視点（3 面は共有 Target）を画面の動きどおりに動かす。他の中央の自由移動と同じ形なので対象にした。

変更：
- `PLToolSurface.TryInvoke`：bool を返す操作が false を返したら失敗にする（「〜は実行されませんでした」）。ツールが任意の `IPLToolActionReason.LastActionFailure` を持てば理由を添える。
- `LatticeToolHandler`：`IPLToolActionReason` を実装。姿勢設定・選択フィット・変形開始が実行されなかった理由（作業軸オブジェクトが無い・選択が無い など）を `LastActionFailure`（概要 lastActionFailure）に残す。`ExecuteFromCommand` の失敗理由もこれを使う。
- `PlayerLatticeSubPanel`：案内欄に lastActionFailure を出す。
- `WorkAxisToolHandler`：`IPointerAxisClient`（原点・Y 先端の自由移動＝ObjectMove）。`SnapCandidateFilter` を足し、`ResolveSnapTarget` で吸着先を画面へ写し、掴んだずれ（`_grabOffset`）を引いてポインタ側で判定する（制限線はポインタの基準点を通るため）。Viewer で `AcceptsCandidate` を結線。
- `BillboardProfileToolHandler`：Line／Freeform の点指定（`TryLinePointAnchor`）と、`ResolveHoverVertex` での吸着候補の絞り込み（`AcceptsSnapVertex`、`SnapCandidateFilter`）。Viewer で結線。
- `CameraToolHandler`：`IPointerAxisClient`（中央ギズモ＝ObjectMove）。
- `TrySelectIndicatorAtScreenPos`：PivotOffset のときだけ動く。

確認結果（uiViewportPointer とキャプチャ。ビューは 414×770）：
| 項目 | 結果 |
|---|---|
| 格子、作業軸オブジェクトなしで beginPlacement | invokeToolAction が「beginPlacement は実行されませんでした：作業軸オブジェクトがありません…」で失敗。パネルの案内も同じ文 |
| 格子、作業軸を作った後 | 姿勢設定 → 変形開始（対象 78 頂点、格子点 27） |
| 格子の制御点、押下時から Shift | 横に固定（op y=228.4 のまま）。画面でも制御点が同じ高さで右へ |
| 処理列の取り込み | 頂点 ID が無いと「選択した頂点に頂点 ID がありません」。repairVertexIds の後に取り込み、項目 lattice（e1）ができた |
| 処理列の制御点（LatticePoints） | 縦に固定（op x=360 のまま） |
| 処理列の作業軸（AxisMove）原点 | 横に固定。原点が +55px 横へ |
| 処理列の作業軸 Y 先端 | 縦に固定（op x=265 のまま） |
| 断面 Profile の点 | 横に固定（点が同じ高さで右へ） |
| 断面 Profile のハンドル | 縦に固定（ハンドルの x はそのまま） |
| 断面 Profile の挿入待ちの点 | 縦に固定（押した位置が基準。挿入点は x=175 のまま下へ） |
| 線分群 Line | 2 点目は 1 点目と同じ高さ（基準＝置いた点）。3 点目は 2 点目と同じ x（基準＝置いた頂点を GPU で写した位置） |
| 線分群 Line の吸着 | 制限線から 4px 外れた既存頂点に吸着せず新しい点（頂点数 173。吸着していれば 172）。Shift なしでは同じ位置で吸着する |
| 線分群 Freeform | 2 点目は 1 点目と同じ高さ、3 点目は 2 点目と同じ x（L 字） |
| 作業軸ツール単体、原点 | 制限線から 3.2px 外れたオブジェクト原点（描画オブジェクトへの吸着）に吸着せず、x=203.78 のまま。Shift なしでは吸着（207,385）。制限線上の原点には Shift ありでも吸着 |
| カメラツール（LookAt）中央 | 横に固定。視線が横だけに振れる（画面の上下は動かない） |
| ボーン編集の直接経路 | ボーンエディタ（描画オブジェクトの姿勢タブ）→ 矩形選択(一時) R で SelectOnly。原点の目印からの Shift ドラッグで use=ObjectMove（MoveToolHandler は None → `_objectMoveHandler`）、原点が横だけ動いた |
| オブジェクト移動で原点の目印を Shift クリック | 追加選択になる（Sphere に RoundedCube が加わる）。Shift なしのクリックは単独選択 |

未確認・未対応：
- Ctrl クリック（反転）は uiViewportPointer に Ctrl が無いので操作で確認していない（コードでは ObjectMove はツールの反転だけになる）。
- PivotOffset（目印側の選択）は画面から入れないので確認していない。
- ObjectMove で、選択中の唯一のオブジェクトの原点がギズモ中心と重なっていて、そこを Shift クリックした場合、以前は目印側が選択を外していた。今はツールがギズモ中心に当たるので選択は変わらない。
- 既存の動き：ボーンエディタ表示中に R の一時選択でドラッグすると、MoveToolHandler の矩形選択と直接経路のオブジェクト移動が同じドラッグで両方動く。→ 下の「段7 追記」で直接経路を撤去。
- 作業軸の SetWorkAxis が Undo で戻らないのは仕様（`PolyLingPlayerViewerCore.CreateCommands.Topology.cs` の作業軸の節に「Undo も積まない（マウス経路・パネル経路とも積んでいないので、そこは変えない）」）。変えるかは未決定。
- 線分群の頂点が作業軸の頂点吸着の候補にならなかった件 → 原因を特定し対応（下の「段7 追記：線分群の点が当たらない原因」）。

### 2026-11-15 段7 追記：ボーン編集の直接経路を撤去
- 経緯：ボーンエディタを開くと ObjectMove を強制する（`ShowBoneEditorPanel`）。ツールの区画があるあいだ常駐リストはモードを変えない（`ApplyRightPaneViewportMode` は `_activeToolSection` があると何もしない）。
  そのため直接経路（旧 BoneInputHandler の後継）が働くのは R・G の一時選択（SelectOnly）、D の面削除、処理列のハンドル（ModifierGizmo）の間だけで、どれも利用者はオブジェクト移動を意図していない。
  実際に R のドラッグで矩形選択とオブジェクト移動が両方動いていた。
- 変更：`ConnectBoneEditorObjectMove`（クリック・ドラッグの橋渡し）、`OnPointerMoved` 内のホバーの橋渡し、`IsBoneEditorObjectMoveRouted`、`_boneDragAllowed`／`_boneDragBegun`、縦横制限の問い合わせの `_objectMoveHandler` への分岐を削除。
  ObjectMove の入力とホバーは通常の経路（`SetToolHandler(_objectMoveHandler)` と `RegisterActiveToolHandler`）が受け持つ。
- 確認：
  - ボーンエディタ（描画オブジェクトの姿勢タブ）→ R → 原点の目印から Shift ドラッグ：use=None、オブジェクトは動かず（目印・ギズモの位置がそのまま）、矩形選択だけ行われ、ドラッグ後はオブジェクト移動に戻る。
  - オブジェクト移動の Shift ドラッグ（回帰）：横に固定、use=ObjectMove。
  - 例外ログなし。

### 2026-11-15 段7 追記：線分群の点が当たらない原因（対応済み）
- 実行中の値（一時ログ。調べた後に削除）：GPU のホバー処理の吸着判定・通常ホバーの結果、`GetSnapHoverElement`、作業軸の吸着先。
  | 状態 | 吸着判定 snap | 通常ホバー hover |
  |---|---|---|
  | 折れ線の群の点（処理列なし／あり、Undo・Redo 後も） | 169（当たる） | 169 |
  | 同じ群の点を滑らかにした（ハンドルを持たせた）後 | -1 | -1 |
  処理列の有無・Undo・Redo は関係しない。段7 で当たらなかった点は、ハンドルを持つ群（滑らかにした点を含む群）の点だった。
- 原因（コード）：`LineGroupEditOps.SyncChordVisibility` は、ハンドルを持つ群の弦（2 頂点の面）を曲線の重ね描きで見せるため `FaceFlags.Hidden` にする。
  GPU の `ComputeFaceVisibility` は非表示の面を先に返すので、2 頂点の面の端点の頂点をカリングから戻す処理（`face.vertexCount < 3` の分岐）を通らない。
  頂点は既定でカリング済みなので、ハンドルを持つ群の点は裏面カリング有効時に、通常ホバー・吸着判定のどちらにも当たらない。
  `SyncChordVisibility` の注記「頂点の表示は 3 頂点以上の面だけで決まるので、群の点は表示・選択できるまま」は GPU 側の実際と合っていない。
- 影響：作業軸の頂点吸着だけでなく、GPU ホバーを使う操作すべて（面追加の吸着、線分群 Line の始点の吸着、頂点選択など）で、ハンドルを持つ群の点に当たらない。
- 対応：弦に「非表示」とは別の面フラグ `FaceFlags.CurveChord`（1<<3。「曲線で見せる弦」）を足した。
  - `LineGroupEditOps.SyncChordVisibility`：ハンドルを持つ群の弦に CurveChord を付けて Hidden を外す。持たない群の弦は両方外す（以前の保存データの弦の Hidden もここで外れる）。
  - `Face.IsCurveChord`・`Face.IsLineHidden`（Hidden か CurveChord）を足し、線分の可視性（`UnifiedBufferManager_Build.BuildLineData`・`UnifiedBufferManager_Visibility.UpdateLineVisibilityFlags`）は IsLineHidden で決める。面の可視性は IsHidden のまま。
    これで弦の線は描かず・当てず、面は非表示にならないので、GPU の `ComputeFaceVisibility` が端点の頂点をカリングから戻す。
  - ミラー（`MirrorBranchOps.Create`）は Hidden と同じく CurveChord も写す。保存（ModelSerializer・CSV）は面フラグをバイトのまま読み書きするので変更なし。
  - 面の表示・非表示の「すべて表示」が曲線の弦を出してしまう問題も、弦が Hidden でなくなったので起きない。
- 確認：直方体に線分群（2 点）→ Profile で弦へ点を挿入 → その点を滑らかに。曲線だけが描かれ、弦の直線は描かれない。
  作業軸（頂点にスナップ）の原点を挿入点の近く (177,213) へドラッグ → 原点が点 (175,209.6) に吸着した（以前は吸着しなかった）。コンパイルエラー・例外なし。
- 残り：以前の保存データでは、読み込み後に線分群のコマンドが一度通るまで弦に Hidden が残り、その群の点は当たらない（読み込み時に `SyncChordVisibility` を通す口は付けていない）。

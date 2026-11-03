# キャプチャへのカーソル表示：チュートリアル用の撮影計画

状態：段 1〜3 実装済み（2026-11-02）。残りは「確かめられなかったこと」の 2 件

## 結果（2026-11-02）

### 計画と変えた点
- `queryCapturePosition` は `GetScreenPositions` を読まない。読み戻しは `PresentAll` の末尾だけで、ポインタ移動かカメラ変更が一度も無いと走らず、値は最後に触ったビューのカメラのもの。照会のときにメイン 3D の slot を計算し直し、頂点 1 個だけを読む（`PlayerViewportManager.TryReadPerspectiveVertex`）
- `mouse` は `Input.mousePosition` を使わない。プロジェクトの入力設定が Input System のみで例外になる。UI ルートでポインタ移動を控えた位置を使う。Play の後で一度も動いていないと失敗する
- 値の原点はカメラの画素で左上。隠した頂点とカメラの後ろの頂点は `w=0`

### 確かめたこと
- コンパイルエラー 0。`queryUiAutomationAudit` は全項目 0。`queryCommandAudit` は参考の行を除くと 2 項目が 0 でない（カテゴリの「正典に無い 10」、スキーマに「出せなかった 3」）。どちらも今回触っていないコマンドで、今回足した分は 0
- 矢印の先と赤枠の中心：左ペイン（`leftPane.captureBtn`）・右ペイン（`capture.run`）で画像を見て一致
- 切り出し：`MainView`・`TriView`・`Window`・`panelId`。範囲外のボタンは描かれず `cursorInside=false`（`cursorX=1.597`）
- 比率の指定：四隅の近くと中央で、矢印の先が指定の位置から 1px 以内（画素で測定）
- 縮小：`maxLongEdge=300` で比率は一致。矢印の大きさは等倍 15×24px、縮小 14×23px
- `none`：変更前の画像と画素で比べ、矢印の範囲の外はすべて一致。軸線の色が違う組があったが（軸線の上だけ）、変更後の画像どうしでも両方の色が出る
- 頂点の照会：カメラを回した後（X 35・Y 130）に表の頂点へ描いて重なりを確認。裏の頂点は背面カリングを入れたとき `visible=false`。カリングを切っていると `true`
- `mouse`：マウスを動かして `uiCapture cursor=mouse` とパネルのボタン（`uiClick capture.run`）で、動かした位置に描かれた
- Player ビルド：`uiCapture` の `control`・`position` と `queryCapturePosition` が動き、赤枠も写る

### 確かめられなかったこと
- ショートカット `K M`：コンピュータ操作でキーを送っても撮影が走らなかった。実機のキーで確かめる必要がある
- `unity_capture` の新しい引数：MCP サーバの再ビルドと再起動の後に確かめる。コンパイルは通っている（exe のロックのエラーだけ）

## 目的
チュートリアルは MCP での自動生成を想定する。読む人が「どこを操作するのか」を画像で分かるように、キャプチャに矢印カーソルを描けるようにする。

## 方針
- **MCP の範囲で作る。** Claude Desktop のコンピュータ操作は使わない
  - 実際のマウスを占有する。画面を見て位置を判断するので ID で指定できず、作り直しても同じ結果になる保証がない
  - 今の `uiClick` の経路を通らないので、シナリオの赤枠連携も使えない
- 矢印は実際のカーソルを撮るのではなく、PolyLing が撮影画像へ描く
- 位置は 3 通りから選ぶ
  - ボタンや入力欄の ID を指定する（MCP の自動生成で主に使う）
  - 撮影範囲に対する比率で指定する（ナイフの線の端など、MCP 側が位置を知っているとき）
  - 実際のマウス位置を使う（人が撮るとき）
- 頂点の位置は新しい照会で求め、比率の指定で渡す

## 現状（コードで確認したこと）
- 撮影は `ScreenCapture.CaptureScreenshotAsTexture()`。カーソルを描く処理はない：`PlayerScreenCapture.cs:100`
- 切り出しの矩形は撮影前に `worldBound` から求めている（パネル座標を左下原点のピクセルへ変換）：`PlayerScreenCapture.cs:212-233`
- 撮影の入口は 1 本（`StartCapture`）。パネルのボタン・ショートカット・`uiCapture` が通る：`PolyLingPlayerViewerCore.ViewAids.cs:47-94`
- `uiClick` はボタンへ `NavigationSubmitEvent` を送るだけで、マウスを使わない：`UiAutomationService.cs:273`
- OS のカーソルを動かす処理（`SetCursorPos` など）は、PolyLing と MCP サーバのソースにない
- ボタンや入力欄の位置は `worldBound` で取れる。赤枠も同じ値で置いている：`UiAutomationHighlight.cs:153`
- 頂点の画面位置は `_viewportManager.GetScreenPositions()` にある。全メッシュを連結した配列で、メッシュごとのオフセットを足して引く：`MoveToolHandler.Commands.cs:421, 447-454`
- `unity_capture` は引数を名前ごとに `uiCapture` へ渡している。新しい引数は MCP サーバ側にも足す必要がある：`UnityCaptureTools.cs:117-128`

## 変更

### 段 1：PolyLing 側（矢印の描画と指定）
1. `PlayerScreenCapture.cs`
   - 矢印の指定を受け取る引数を足す。位置は `Capture` の呼び出し時に、切り出し矩形と同じ時点で確定する
   - `Shoot` で切り出しの後、縮小の前に矢印を描く。範囲外なら描かない
   - 矢印はコードで描く（白地に黒の縁取り）。画像素材は足さない
   - 縮小後の画像で同じ大きさに見えるよう、`maxLongEdge` から描く大きさを決める
   - 位置の結果（画像に対する比率、範囲内か）を `onDone` と一緒に返す
2. `PanelCommand.UiAutomation.cs`（`UiCaptureCommand`・`UiCaptureStatusCommand`）
   - `Cursor`：`none`（既定）/ `control` / `position` / `mouse`
   - `CursorControlId`：`control` のときのボタンや入力欄の ID
   - `CursorX`・`CursorY`：`position` のときの位置。撮影範囲に対する比率（0〜1、左上原点）
   - `uiCaptureStatus` の結果に `cursorX`・`cursorY`・`cursorInside` を足す
3. `PolyLingPlayerViewerCore.ViewAids.cs`・`PolyLingPlayerViewerCore.UiAutomation.cs`・`UiAutomationCaptureJobs.cs`
   - `StartCapture` と受付簿に矢印の指定と結果を通す
   - `control` は `_uiAutomationRegistry` で ID を引く。登録がない・表示されていないときは、撮る前に理由を返す
   - `mouse` は `Input.mousePosition` を使う（左下原点で撮影画像と同じ向き）
4. `PlayerCaptureSubPanel.cs`
   - 「マウスカーソルを描く」のチェックを足す（既定は切、`RecentPaths` に保存、UI 自動操作の ID は `capture.cursor`）
   - チェックが効くのは人の操作（パネルのボタンとショートカット）だけ。`uiCapture` は引数に従う

### 段 2：頂点の位置の照会
1. 新しい照会コマンド `queryCapturePosition`
   - 引数：`MasterIndex`・`VertexIndex`・撮影範囲（`Target`）
   - 結果：撮影範囲に対する比率の `x`・`y`、範囲内か、頂点が見えているか
   - 頂点の位置は GPU の値（`GetScreenPositions`）を読む。CPU で投影し直さない
2. 確認してから作ること
   - `GetScreenPositions` の定義を読み、MCP からの呼び出しで最新の値が読めるか（ポインタが動かなくても更新されているか）を確かめる
   - 値の原点と向き、どのビューの値かを実測で確かめる
   - 最新の値が読めないときは、毎フレームの読み戻しを足さず、照会の時に 1 回だけ読む方法を取る

### 段 3：MCP サーバと文書
1. `UnityCaptureTools.cs`：`unity_capture` に `cursor`・`cursor_control_id`・`cursor_x`・`cursor_y` を足し、`uiCapture` へ渡す。結果の文言に位置を載せる
2. **MCP サーバの再ビルドは手作業になる。** 再ビルドまでは `polyling_call` の `uiCapture` と `read_image` で同じことができる
3. 文書を直す：`MCP操作手順書.md`・`McpGuide/primer.md`・`PolyLing_機能の場所.md`・`作業ログ.md`

## 確認
- `unity_validate_changes` でコンパイルエラー 0。`queryUiAutomationAudit` の未登録が増えていないこと
- 矢印の位置：左ペイン・右ペインのボタンそれぞれで `cursor=control` で撮り、`uiHighlight` の赤枠と重ねて、枠の中心に矢印の先があることを画像で見る
- 切り出し：`MainView`・`TriView`・`Window`・`panelId` で撮る。範囲外のボタンを指定すると、矢印が描かれず `cursorInside=false` になること
- 縮小：`maxLongEdge` の有無で、矢印の位置が比率で一致し、見た目の大きさも同じであること
- 比率の指定：`position` で四隅の近くと中央を撮り、指定どおりの位置に描かれること
- 頂点の照会：既知の形状の頂点を照会し、その位置へ `position` で描いて画像で重なりを見る。ビューを回した後と、裏側の頂点でも見る
- 指定なし（`none`）の画像が変更前と同じであること
- `mouse`：人の操作が要るので実機で 1 回撮ってもらう。ショートカット `K M` で撮り、実際のカーソル位置に描かれること
- Player ビルドでも `uiCapture` が同じように動くこと

## この計画でやらないこと
- シナリオをチュートリアルの MD にする機能（画像の BASE64 埋め込みを含む）。この計画の結果を使う別の作業とする（`PolyLing_チュートリアル計画.md`）
- ドラッグの軌跡や、クリックを示す印。必要になってから足す
- ギズモの軸を指す位置の取得。`querySelectionState` の原点は文字列の診断用なので、チュートリアルで要ることが分かってから別途決める

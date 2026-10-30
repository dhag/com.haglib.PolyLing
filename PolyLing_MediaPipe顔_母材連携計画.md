# MediaPipe 顔：母材 A の連携を母材 B・C へ広げる計画

状態：未着手（承認待ち）

## 呼び方
- 母材O：MediaPipe の顔（createNohMask の出力、頂点の並びは MediaPipe と同じ）
- 母材A：頂点を減らした顔（現行は「A_羽根なし」）。母材O との連携は「A_MP基準」（A の正面撮影を MediaPipe にかけ、A の表面へ逆投影した 478 点＋外縁。母材O と同じ並び）
- 母材B・C：母材A と連携の取れている別の母材（A と同じ場所に、人の目で合わせて置いたもの）

## 現状（コードで確認したこと）
- 頭部生成コマンド `mediaPipeHeadBuild` の写す先は 1 つだけ
  - 引数 `TargetMasterIndex`（int）：`PanelCommand.Deform.cs:345-347`
  - 処理：`PlayerCommandDispatcher.DeformSkin.cs:875-911`
- 頭部 `HeadMasterIndex` は必須で、写す先だけが欲しいときも頭部の結果が必ず 1 つ作られる（`DeformSkin.cs:804-826`）
- 写し方（大きさ合わせ・左右の対応・対称化・三角形ごとのアフィン・外周）は `MediaPipeHeadBuilder.Prepare`（`MediaPipeHeadBuilder.cs:76-162`）で作る。`BuildTarget`（`:222-246`）は呼ぶたびに Prepare をやり直す
- 写す先の頂点は、ワールド XY をそのまま写し方に通す。内側か外側かは A_MP基準 の面との Z の差（`insideDepthTolerance`）で決める（`:190-191`）。したがって B・C は A と同じ場所に置かれていることが前提

## 変更
1. `MediaPipeHeadBuilder.cs`
   - Prepare の結果（Field）を 1 回だけ作り、複数の写す先に使う。`BuildTarget` を「Field を受け取って 1 つの写す先を写す」形に分け、写し方そのもの（MapPoint・UV の補間）は変えない
2. `PanelCommand.Deform.cs`（MediaPipeHeadBuildCommand）
   - `TargetMasterIndex`（int）を `TargetMasterIndices`（int[]、IsMeshRef）に置き換える。後方互換は持たない
   - `HeadMasterIndex` を任意にする（-1 で頭部を作らない）。-1 のとき `MakeSymmetric` は使えない（失敗として返す）
   - 結果：`targetMasterIndex`・`targetInsideVertices`・`targetOutsideVertices`・`targetTexturedFaces` を、写す先の並び順の整数配列に置き換える
3. `PlayerCommandDispatcher.DeformSkin.cs`（case MediaPipeHeadBuildCommand）
   - 写す先ごとに、今の 1 件分の処理（複製・ワールド→ローカル・`ApplyTargetTexture`・材質の写し・`model.Add`）を回す
   - 頭部が -1 のときは頭部の結果と左右対称の半身を作らない
   - Undo は今と同じく全部まとめて 1 件（MeshFilterToSkinnedRecord）
   - 写す先の名前に、今と同じく `_MP` を付ける
4. シナリオ「A頭部_MediaPipe顔から生成」（フォルダ MediaPipe頭部）
   - 写す先の引数を `targetMasterIndex` から `targetMasterIndices` に直し、写す先の名前（A・B・C）を引数で並べて渡せるようにする
   - 頭部（Cタイプ_頭くっつけ用）を渡すのをやめ、`headMasterIndex=-1` にする
   - 顔面テクスチャの貼り付け・口の縫い付け面の取り外し・法線の整えのうち、A だけに当てていた項目は、B・C にも当てるか項目ごとに決めて報告する

## 確認
- データ：`データとツール/サンプルメッシュ/MediaPipe頭部生成_テスト10_A羽根なし_4人.mqo`（A_MP基準・A_羽根なし・MP_girl3 などが入っている）に B・C を入れたもの
- 3 つの csproj でコンパイルエラー 0
- A の結果が今と一致すること：girl3 で、新しいコマンドで作った A の結果と、保存済みの「A_羽根なし_MP_girl3」の頂点位置の差の最大を出す（0 であること）
- B・C：写す先ごとの内側・外側の頂点数、顔面の材質を当てた面の数を報告する。内側が 0 なら A と同じ場所に置かれていない
- B・C の結果を正面・横から `unity_capture` で撮る（変更前の B・C も同じ視点で撮る）
- 頭部 -1・写す先 3 つで 1 回実行 → Undo で 3 つとも消え、Redo で戻ること
- 結果は `データとツール/サンプルメッシュ/MediaPipe頭部生成_テスト11_ABC.mqo` に保存する

## 対象外
- 写し方そのもの（アフィン・外周・奥行き判定）の変更
- B・C 用の別の基準（B・C の表面へ逆投影した点）を作ること

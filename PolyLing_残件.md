# PolyLing 残件

見つけたが手を付けていないもの。1 件 1 行。直したら行を消す。

- boolean_demo：細かく割った曲面どうしの差で穴が残る（球 738 − 円柱 170 で 1e-4 まとめ・T 字解消後も 6 個）。原因は未特定。diagnoseBoolean の LoopArea が全穴で -1（輪をたどれない）

- 新コマンド案：既存のメッシュの頂点を、最寄りの揺れ鎖（ボーン）へ自動でウェイト付けする。いまは floodSkinWeight / setSkinWeightNumeric / skinWeightPaint しかなく、既存のスカートを鎖へ結び付けられない

- MQO 読込の置換（Replace）：未実装（指定すると拒否する）。既存オブジェクトを全部消すので、MeshListChangeRecord では戻らないモデル単位の情報（Humanoid 割当の作業表、T ポーズ退避、MorphExpressions、ObjectGroups、メッシュ選択セット）の Undo が要る

- PMX 読込の追加（Append）・置換（Replace）：PMXImportSettings.ImportMode を読むコードが無く、常に新規モデルになる。MQO の追加読込（PolyLingPlayerViewerCore.Import.cs の AppendImportedModel）と同じ作りで直す。PMX はモーフ（MorphExpressions）も移す必要がある

- 新コマンド案：辺の重なりを検出する照会コマンド。3 枚以上の面が使う辺、同じ向きで共有された辺（裏表の食い違い）、重複面、同じ位置の頂点、辺どうしの交差・同一直線上の重なり、辺の途中に乗った頂点（T 字）を返す。いまは getRawData の生データを手元で計算するしかない（face_skin_05 の点検で実施）

- 新コマンド案：面の辺と一致しない線分（2 頂点の面）を検出する照会コマンド。ナイフで辺が分割された後、元の辺に沿って残った線分を返す（face_skin で 84 本中 12 本）。queryLineGroups は群ごとの線分数を数えるだけで、面の辺との一致は見ない

- 新コマンド案：閉じた線分群の内側に格子状の四角形の面を置く（例：顔の輪郭の線分群の中にメッシュを置く）。線分群を 1 つ指定する。閉じていなければ、閉じたものと仮定して置く。いまは createPlane で格子を置き、はみ出した面を deleteFaces で消している（face_skin_04 のおでこ）






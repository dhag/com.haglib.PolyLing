# PolyLing 残件

見つけたが手を付けていないもの。1 件 1 行。直したら行を消す。

- boolean_demo：細かく割った曲面どうしの差で穴が残る（球 738 − 円柱 170 で 1e-4 まとめ・T 字解消後も 6 個）。原因は未特定。diagnoseBoolean の LoopArea が全穴で -1（輪をたどれない）

- 新コマンド案：既存のメッシュの頂点を、最寄りの揺れ鎖（ボーン）へ自動でウェイト付けする。いまは floodSkinWeight / setSkinWeightNumeric / skinWeightPaint しかなく、既存のスカートを鎖へ結び付けられない

- MQO 読込の置換（Replace）：未実装（指定すると拒否する）。既存オブジェクトを全部消すので、MeshListChangeRecord では戻らないモデル単位の情報（Humanoid 割当の作業表、T ポーズ退避、MorphExpressions、ObjectGroups、メッシュ選択セット）の Undo が要る

- PMX 読込の追加（Append）・置換（Replace）：PMXImportSettings.ImportMode を読むコードが無く、常に新規モデルになる。MQO の追加読込（PolyLingPlayerViewerCore.Import.cs の AppendImportedModel）と同じ作りで直す。PMX はモーフ（MorphExpressions）も移す必要がある






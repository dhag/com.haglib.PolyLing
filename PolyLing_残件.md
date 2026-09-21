# PolyLing 残件

見つけたが手を付けていないもの。1 件 1 行。直したら行を消す。

- boolean_demo：細かく割った曲面どうしの差で穴が残る（球 738 − 円柱 170 で 1e-4 まとめ・T 字解消後も 6 個）。原因は未特定。diagnoseBoolean の LoopArea が全穴で -1（輪をたどれない）
- T ポーズ（applyTPose）：スキン済み・モーフありの PMX で、ボーンだけ T ポーズになりスキンが取り残される。ログ上は BakeSkinnedVertices が 783 オブジェクト・1,096,667 頂点を書き戻して完了し、警告なし。書き戻した位置が元のままか、表示に反映されないかは未確認
- 新コマンド案：既存のメッシュの頂点を、最寄りの揺れ鎖（ボーン）へ自動でウェイト付けする。いまは floodSkinWeight / setSkinWeightNumeric / skinWeightPaint しかなく、既存のスカートを鎖へ結び付けられない

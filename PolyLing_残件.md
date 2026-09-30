# PolyLing 残件

見つけたが手を付けていないもの。1 件 1 行。直したら行を消す。
詳細があるものは括弧内のメモを見ること。

最終更新: 2026-09-30。

## 姿勢（詳細は `PolyLing_姿勢_残件.md`。番号はそちらと同じ）

- A-1 済（2026-09-30）：面追加・新規頂点のウェイト継承・面押し出し・ナイフ・点指定図形がバインド表示に追従することを実地で確認。点指定図形の GPU 値が無いときの落ち先が WorldMatrix 直掛けだったのを VertexMatrix(i, showBindPose) に直した
- A-2 済（2026-09-30）：MQO の階層の未確認部分。BakedMirror 86 件（mirror 133 件のうち空の入れ物 47 件を除く）、depth 5 以上 112 件の親子は全件一致（最深 17）、obj1 は空ルートで子なし。queryModelStructure に全 MeshContext の階層項目を追加
- A-3 取り下げ（2026-09-30）：旧データ（workaxis.csv / ModelDTO.workAxis を含むプロジェクト）の移行確認。旧プロジェクトには対応しない方針。移行コード（MigrateLegacy・ReadWorkAxisCsv・ModelDTO.workAxis）も削除した
- A-4 済（2026-09-30）：PolyLingPlayerViewerCore.* の ComputeWorldMatrices 21 か所を点検。取りこぼしは Undo の MeshList 分岐（ポーズ・BoneTransform 記録）で、UpdateTransform を足した。同時に setBoneTransformValue の通常モードが UpdateTransform を呼ばない件、RemoteOwnership が AddOnly より先にモデル有無で拒否する件も直した
- B-1 取り下げ（2026-09-30）：UseLocalTransform の false に 3 つの意味が同居している件。false の箇所は必ず値も単位にしており、保存データ・読込直後とも違反 0 件で実害なし。「false なら値も単位」を規約 6.1 として明文化した
- B-2 済（2026-09-30）：ComputeBindPosesFromList（読込時用）。前提が誤りで、元からポーズ層は積んでいなかった。食い違っていた端の扱い（UseLocalTransform を見ない・BoneTransform=null や範囲外の親を子孫ごと飛ばす）を、CalculateWorldMatrices が BindLocalMatrix を積む形にしてインスタンス側 BindWorldMatrix と同じ規則にそろえた。PMX 読込 → MQO（ボーン付き）書出 → importBonesFromArmature 読込で、BindPose 252 本が PMX と一致し、修正前後も完全一致
- B-3 済（2026-09-30）：UnityClipApplier.RestWorldOf をスキンドも BindWorldMatrix（RestWorldMatrix）へ寄せた。取込直後は同値で、VMD→モーション焼き込みの出力が修正前後で完全一致。BindPose がずれる操作（スキン固定移動など）の後はバインド階層側が正しい
- B-4 方針化（2026-09-30）：付与親・VRM コンストレイント・IK は保持と書き戻しのみで、評価は実行側（Unity・VRM とも形式側で解決しない）。IK は PMX / VMD 経路だけで評価。詳細は PolyLing_姿勢_残件.md 残件 B-4
- B-5 済（2026-09-30）：リモート同期の作業軸。フェッチで届くこと、接続後の値の変更が push（`workAxisChanged`）で届くことを実地で確認。作業軸ライブラリ（`ProjectContext.WorkAxes`）は引き続き送らない（下の「リモート同期」R-1 で扱う）

- 未確認：T ポーズ変換（TPoseConverter。B-2 で直した CalculateWorldMatrices を使う）を修正後に実機で試していない。A ポーズのボーン付きデータで変換前後の見た目を確かめる
- 未確認：B-3 で結果が変わる側（スキン固定でボーンを動かして BindPose と BindWorldMatrix がずれた状態）での VMD→モーション焼き込みを実機で比べていない。beginBoneTransformSliderDrag がモードを受け取らないため、コマンドからは作れない
- setBoneTransformValue を beginBoneTransformSliderDrag なしで呼ぶと、前回 begin したときのモード（_activeBoneEditMode）のまま動く。begin がモードを受け取るようにするか、begin なしの呼び出しは通常モードに固定する

## リモート同期

- R-1 規約 4（CSV/JSON 対称）を「CSV / JSON / バイナリ対称」に広げ、保存項目を足すときは 3 つを同時に直す。症状：ビューアの取得（`RemoteProgressiveSerializer` の PLRH / PLRM / PLRS / PLRD）は送る項目を 1 つずつ手書きしており、規約 4 の対象外なので保存項目の追加に取り残される。確認済みの抜け：作業軸ライブラリ（`ProjectContext.WorkAxes`。保存は `ProjectSerializer.cs:260` と `CsvProjectSerializer.WriteWorkAxisLibraryCsv`）。`project_bundle`（`RemoteServerCore.cs:419` の `TryBuildProjectJson`）も `ProjectDTO.Create` にモデルを足すだけで `ProjectSerializer.FromProjectContext` を通さないため、同じく作業軸ライブラリが抜ける。手順：(1) 抜けを機械的に見つける検査コマンドを作る（同じプロジェクトを「保存の JSON」と「バイナリで送って受け側で復元したものを JSON にしたもの」の 2 通りで作って差分を出す）→ (2) 抜けの一覧を出す → (3) まとめて直す（作業軸ライブラリは PLRH の版を上げて足す）。未確認：作業軸ライブラリ以外の抜けの有無（検査で洗い出す）

## 読込

- MQO 読込の置換（Replace）：未実装（指定すると拒否する）。既存オブジェクトを全部消すので、MeshListChangeRecord では戻らないモデル単位の情報（Humanoid 割当の作業表、T ポーズ退避、MorphExpressions、ObjectGroups、メッシュ選択セット）の Undo が要る
- PMX 読込の追加（Append）・置換（Replace）：PMXImportSettings.ImportMode は画面で設定されるだけで、読込処理は読んでいない。常に新規モデルになる。MQO の追加読込（PolyLingPlayerViewerCore.Import.cs の AppendImportedModel）と同じ作りで直す。PMX はモーフ（MorphExpressions）も移す必要がある

## 新コマンド案

- 既存のメッシュの頂点を、最寄りの揺れ鎖（ボーン）へ自動でウェイト付けする。いまは floodSkinWeight / setSkinWeightNumeric / skinWeightPaint しかなく、既存のスカートを鎖へ結び付けられない
- 辺の重なりを検出する照会コマンド。3 枚以上の面が使う辺、同じ向きで共有された辺（裏表の食い違い）、重複面、同じ位置の頂点、辺どうしの交差・同一直線上の重なり、辺の途中に乗った頂点（T 字）を返す。いまは getRawData の生データを手元で計算するしかない（face_skin_05 の点検で実施）
- 面の辺と一致しない線分（2 頂点の面）を検出する照会コマンド。ナイフで辺が分割された後、元の辺に沿って残った線分を返す（face_skin で 84 本中 12 本）。queryLineGroups は群ごとの線分数を数えるだけで、面の辺との一致は見ない
- 閉じた線分群の内側に格子状の四角形の面を置く（例：顔の輪郭の線分群の中にメッシュを置く）。線分群を 1 つ指定する。閉じていなければ、閉じたものと仮定して置く。いまは createPlane で格子を置き、はみ出した面を deleteFaces で消している（face_skin_04 のおでこ）

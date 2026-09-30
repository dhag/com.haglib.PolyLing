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

## 作業空間（第 4 段階の検討課題。旧 2D キャンバスは当分廃止しない。詳細は `PolyLing_UV_Billboard_Design.md`）

作業空間で代われない・一部しか代われない旧キャンバスの機能（2026-09-30 のコード調査）。
- UV エディタ：任意位置のアンカーと範囲基準のプリセット（中心・左上・左下）。作業空間の回転・拡大縮小の中心は選択の重心か代理の原点（UV 0,0）だけ
- UV エディタ：移動・拡大縮小・軸角・回転を数値でまとめて 1 回で適用する操作。作業空間はツールごとに別
- UV エディタ：未選択のとき全 UV を変換の対象にする動作。作業空間は選択が要る（頂点の全選択の手段は未確認）
- UV エディタ：開いた後のマテリアル切替（表示・選択・下絵を絞る）。作業空間は開くときに面で絞るだけで、下絵は面数最多のマテリアルに固定
- UV エディタ：テクスチャが無いときのマテリアル色の背景、UV 数・選択数の表示、後からのフィット。作業空間に無い
- UV エディタ：0.25 グリッド、Shift トグル、選択解除、ホバー強調の作業空間側の対応は未確認
- 断面共通：線分クリックで挿入した点の z を前後から補間する。作業空間（BillboardProfile）で置く・挿入する点は z=0
- 断面共通：投げ縄選択が BillboardProfile に無い（通常の選択ツールで代われるかは未確認）
- 断面共通：断面 CSV の読み書き、断面リセット（既定の形）。作業空間に無い
- 断面共通：下絵の不透明度、ドラッグ・ホイールでの位置・大きさ調整。作業板は隅の数値・コントラスト・明るさだけ
- 断面共通：選択点の座標の数値入力が作業空間で使えるかは未確認
- 回転体：R ≥ 0 の制約が反映に無い（負の R をそのまま取り込む）。プリセット 6 種は無い（パネルで適用 → 作業空間の取消しで代理へ取り込める）
- 回転体：代理の群を閉じても CloseLoop が変わらない。取り込むのは点数最多の 1 群だけで、作業空間で足した群は捨てる
- 2D押し出し：穴フラグの手動指定が反映で失われる（取り込みは巻き順で穴を決め直す。LineProfileExtractor.ExtractLoops）
- 2D押し出し：巻き順を保つループの左右反転が無い。Scale X=-1 で鏡映すると巻き順が逆になり、反映で外周と穴が入れ替わる
- 2D押し出し：別ループの点を押すとそのループを全選択する操作、ループ切替（◀▶）が無い。ループ複製・ループ同士のブーリアンの代替は未確認
- 2D押し出し：各ループ 3 点以上の制約が作業空間に無い。閉ループが 2 点になると反映でそのループが消える
- フリル/パイプ：A/B の切替と、もう一方の灰色表示が無い（作業空間は A・B を別々に開く）
- フリル/パイプ：x=1 の目印が無い。反映のたびに NormalizeToUnitSpan が掛かり、作業空間での全体の移動・拡大縮小は打ち消される。取り込むのは 1 本だけ（開：最長、閉：点数最多）
- 断面共通：作業空間で付けたベジェのハンドルは反映で折れ線（1 区間 8 分割）になり、取消し・開き直しで戻らない
- 断面共通：「反映→メッシュ」は姿勢を焼き込み（ApplyPoseForDirectMeshCreate）、取消しの BuildProfileLineMesh は焼き込まない。焼き込みが効く設定では代理から読む断面の座標がずれる（未実地確認）

調査中に見つけた旧キャンバス側の不具合。
- 2D押し出しの穴フラグの切替が Undo に入らない（PlayerPrimitiveMeshSubPanel.Profile2D.cs:443、P2dBegin/Commit を通らない）
- 断面キャンバスの下絵の原点プリセットは値を保存するだけで、UpdateRevBgEl / UpdateP2dBgEl / UpdateBeltBgEl は画像の中心基準で置き、原点を見ていない
- UV エディタのマテリアル絞り込みが、ヒットテスト・全選択・CollectAllUVVertices に効いていない（非表示マテリアルの UV もハンドル・変換・マグネットの対象になる）

作業空間のその他。
- BillboardView を Current にする UI・コマンドが無い。作業空間中の基準ビュー固定（GetEditSpaceReferenceViewport）の Current での動作は未実地確認
- 方向スロットの下絵が作業空間の終了で戻ること、ビルド版での動作は未確認
- ローカル指定の移動（moveSelectedVertices space=Local）で、ワールドとの往復により X・Y に 1e-9 程度の誤差が乗る（UV の反映値に出る）
- UI 自動操作の uiDescribe で、一部だけ作り直す箇所（UiDynamicControls を Begin せずに Add し直す）の項目が重複して出る。押す対象は Find が画面に付いている要素を選ぶよう直したが、一覧の重複は残る

## 新コマンド案

- 既存のメッシュの頂点を、最寄りの揺れ鎖（ボーン）へ自動でウェイト付けする。いまは floodSkinWeight / setSkinWeightNumeric / skinWeightPaint しかなく、既存のスカートを鎖へ結び付けられない
- 辺の重なりを検出する照会コマンド。3 枚以上の面が使う辺、同じ向きで共有された辺（裏表の食い違い）、重複面、同じ位置の頂点、辺どうしの交差・同一直線上の重なり、辺の途中に乗った頂点（T 字）を返す。いまは getRawData の生データを手元で計算するしかない（face_skin_05 の点検で実施）
- 面の辺と一致しない線分（2 頂点の面）を検出する照会コマンド。ナイフで辺が分割された後、元の辺に沿って残った線分を返す（face_skin で 84 本中 12 本）。queryLineGroups は群ごとの線分数を数えるだけで、面の辺との一致は見ない
- 閉じた線分群の内側に格子状の四角形の面を置く（例：顔の輪郭の線分群の中にメッシュを置く）。線分群を 1 つ指定する。閉じていなければ、閉じたものと仮定して置く。いまは createPlane で格子を置き、はみ出した面を deleteFaces で消している（face_skin_04 のおでこ）

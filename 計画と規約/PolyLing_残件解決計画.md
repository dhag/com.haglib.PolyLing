# PolyLing 残件解決計画

`PolyLing_残件.md` の残件を解決する順番と段の範囲。この文書を正本にする。
進め方：1 段＝1 会話。各段の始めにコードを読んで変更計画を出し、承認を得てから実装する。
段を終えたら、`PolyLing_残件.md` の該当行を消し、この文書の「状態」を更新する。

作成: 2026-10-28。

## 段 1：残件資料の整理 — 状態：済（2026-10-28）

- 全行を現行コードと照合し、直っていた行（PMX 読込の追加・置換、PLRH の作業軸ライブラリ）と、姿勢の済・取り下げ・方針化の行を消した
- 残りを 5 分類（不具合／未実装／実機確認だけ／作業空間の検討課題／新コマンド案）に並べ替えた
- 内容が古くなっていた行を書き直した（B-3 の確認は beginBoneTransformSliderDrag が Mode を受け取るのでコマンドから作れる／新コマンド案のスカートの結び付けは skinWeightAutoHeat がある前提で書き直し）

## 段 2：保存・同期の対称性（残件 1 の R-1） — 状態：済（2026-10-28。検査コマンド queryProjectSyncDiff。RoboFArms CSV＋作業軸辞書・alicia_vrm・A4・T5・zzParamRT で差分 0）

- 検査コマンドを作る：同じプロジェクトを「保存の JSON（ProjectSerializer.FromProjectContext）」と「バイナリで送って受け側で復元したものの JSON」の 2 通りで作り、差分を出す
- 検査で出た抜けの一覧を作り、まとめて直す
- `project_bundle`（RemoteServerCore.TryBuildProjectJson）は ProjectSerializer.FromProjectContext を通す形にそろえる（テクスチャ画像を載せる ModelBundle.BuildDto の役目は保つ）
- 規約 4 を「CSV / JSON / バイナリ対称」に広げて明文化する
- 完了条件：代表プロジェクトで検査の差分が 0

## 段 3：不具合の修正（残件 1） — 状態：済

- UV エディタのマテリアル絞り込み：ヒットテスト・全選択・CollectAllUVVertices。絞り込みを 1 か所の列挙に集め、描画・矩形選択・移動開始もそれを通す
- 2D押し出しの穴フラグの切替を Undo に入れる（P2dBegin/Commit）
- 断面キャンバスの下絵の原点を、回転体・2D押し出し・フリル/パイプの 3 か所の配置に効かせる
- setBoneTransformValue：begin なしの呼び出しは通常モードに固定する
- 完了条件：各項目を MCP で操作して確かめる

残件 1 のうち、ローカル指定移動の 1e-9 誤差と uiDescribe の重複は、段 6 で扱う。

## 段 4：MQO 読込の置換（残件 2） — 状態：済

- PMX の置換（ReplaceCurrentModelWithImported → ModelReplaceRecord）と同じ経路を MQO にも使う。モデル単位の情報の Undo を別に作らない
- コマンド経路（PolyLingPlayerViewerCore.CreateCommands.FileIO.cs の拒否）とパネル経路の両方を通す
- 完了条件：置換 → Undo → Redo でモデルが元どおり・置換後どおりになる

## 段 5：実機確認（残件 3 のうち姿勢の 2 件） — 状態：済（2026-10-28。Zunko_MMD.pmx・test2.vmd。T ポーズ変換後に BindWorldMatrix が古いまま残る不具合を直した〔applyTPose の後に ComputeWorldMatrices〕。B-3 は修正後の式どおりの整列をログで確認。beginBoneTransformSliderDrag の Mode・OriginOnly をコンストラクタ引数にしてコマンドから渡せるようにした。PMX 読込の T ポーズ変換が種別確定前に呼ばれ、頂点の焼き込みと BindPose の撮り直しを飛ばしていたのを直した〔PMXImporter で変換前に SkinKindOps.RecomputeAll〕。頂点の焼き込みでモーフの基準位置が焼かれず差分が壊れるのを直した〔TPoseConverter.BakeSkinnedVertices が BasePositions も同じスキンで焼く。TPoseBackup に基準位置を控えて Undo で戻す〕。剛体・ジョイントが焼き込みで動かず A ポーズの位置に残るのを直した〔関連ボーンの SkinningMatrix で位置・回転を動かす。Undo で戻す〕。PMX 読込の T ポーズ変換をモーフ・剛体・ジョイントの作成後へ移し、読込時変換と読込後の applyTPose の結果を一致させた〔Zunko の CSV 保存で ID・日時以外の差 0〕）

- T ポーズ変換：A ポーズのボーン付きデータで変換前後の見た目を確かめる
- B-3：スキン固定でボーンを動かした状態で VMD→モーション焼き込みを修正前の式と比べる
- 不具合が見つかれば、その段の中で直すか残件に戻すかを報告して決める

## 段 6：残りの不具合（残件 1 の 2 件） — 状態：済（2026-10-28。ローカル指定の移動は、基準の行列と同じ行列の頂点へローカル量をそのまま足すようにし、ワールドとの往復をやめた〔MoveToolHandler.Commands / Move の MakeWorldToLocal〕。SandBox/さ/textcurve_check3.mqo の UV 作業空間〔倍率 0.01・カメラ回転〕で、修正前は移動量に 1 刻み〔3.7e-9〕のずれが出ていたのが、修正後は座標・反映した UV とも float の計算値と一致。uiDescribe の重複は原因が 3 つあった：一覧が同じ ID の古い控えを全部出していた〔UiAutomationRegistry.Controls で ID ごとに 1 件に〕、_uiRowTarget が同じ置き場を 2 回目の出どころとして登録させていた〔RegisterObjectCore で同じ置き場を二重登録しない〕、髪の幅配分・フリルの断面エディタ・高さ倍率の作り直しで古い控えが残っていた〔UiDynamicControls.RemoveUnder を作り直す前に呼ぶ〕。髪の房の筒の本数 3→4→3、フリルの A/B 切替のあと、図形生成：高度な図形で全項目が 1 回ずつ出て、値の入力・ボタンの押下が効くことを確認）

- ローカル指定の移動（moveSelectedVertices space=Local）で、ワールドとの往復により X・Y に 1e-9 程度の誤差が乗る（UV の反映値に出る）
- uiDescribe で、一部だけ作り直す箇所（UiDynamicControls を Begin せずに Add し直す）の項目が重複して出る
- 完了条件：各項目を MCP で操作して確かめる

## 対象外

- 残件 3 のうち作業空間の確認 3 件、残件 2 の BillboardView の Current 化、残件 4（作業空間の検討課題）、残件 5（新コマンド案）。別計画で扱う

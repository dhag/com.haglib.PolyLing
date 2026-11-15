# PolyLing モーフ整理計画

作成 2026-11-15。この文書を正本にして、段ごとに進める（1 段＝1 会話）。
後方互換は考えない（旧形式の読み込みは作らない。要るときは別途コンバータ）。

---

## 1. 目的

困りごと（操作者の指摘）

1. モーフの数が膨大で管理できない。
2. 大きく形を変えるモーフ（笑い・目を閉じるなど）を、あとで気軽に見られない・編集しにくい。真顔と見比べるのに 1 つずつ切り替える手間がある。
3. 元のモデルを基に「一部だけのモデル」（顔・まぶたなど）を作り、そこから読み込む作業が気軽にできない。

運用の前提：ウェイトは PMX、ジオメトリは MQO に置いてセットで管理している（今後も続く）。MQO で顔やまぶただけを読み込むのに便利な方式にする。

---

## 2. 現状（コードで確認したこと）

### 2.1 持ち方

- 頂点モーフ 1 個は「隠しオブジェクト 1 個」（種類 Morph の `MeshContext`）。中身はベースのメッシュの丸ごとの複製（面・UV・ウェイトも複製）で、頂点位置が変形後の絶対位置。これに基準位置の写し（`MorphBaseData.BasePositions`）が付く（`MeshContext.Attributes.cs:138-152`）。差分は書き出しと試し表示のときだけ計算する（`MorphBaseData.GetSparseOffsets`）。
- 隠しオブジェクトは「モーフ × ベースのオブジェクト」ごとに 1 個できる。
  - PMX 読み込み：材質グループごとにベースを複製（`PMXImporter.Morph.cs:290-341`）。
  - VRM・glTF 読み込み：同じ形（`VrmToPolyLingConverter.cs:38-39` の注記）。FBX 読み込み：同じ形（`FbxImporter.cs:768-799`）。
  - 差分からの生成：ミラー実体があればミラー側にもう 1 個（`PlayerCommandDispatcher.TPoseMergeMorph.cs` の `CreateMirrorMorphMeshContextInDispatcher`）。
- 表情（`MorphExpression`）は隠しオブジェクトをオブジェクト番号＋重みで指す（`MeshEntries`）。グループモーフは読み込みで子の中身を親へ展開し、子の表情は一覧から外す（`PMXImporter.Morph.cs:129-225`）。子の名前は `GroupChildren` に残し、PMX 書き出しはそこから子を作り直す（`PMXExporter.Morph.cs` の `AddMissingGroupChildren`）。

### 2.2 見る・比べる

- 試し表示は 2 系統で、どちらも「ベースに差分 × ウェイトを足す」スライダーだけ。
  - オブジェクトリストのモーフ欄：ベースの頂点位置を直接書き換えて戻す（`MeshListOps.cs:914-1007`）。
  - 表情パネル・モーション再生：表示用の足し込み（`WorkingPositions`）に書く（`MorphPreviewState.cs:50-91`、`MotionClipApplier.cs:729-769`）。
- 真顔と笑いを一発で入れ替える操作は無い。隠しオブジェクトは 1 個ずつ非表示で作られる。

### 2.3 一部だけのモデルから作る

- 差分からのモーフ生成は、2 つのモデルのオブジェクト数が同じでないと失敗し、同じ並び番号どうしを比べる（`PlayerCommandDispatcher.TPoseMergeMorph.cs:477-485`）。一部だけのモデルは使えない。
- MQO 部分読み込み（頂点位置の差し替え）は描画オブジェクトを書き換えるだけで、モーフにはならない。自動の対応付けは頂点数の一致だけで、名前は見ない（`MQOPartialMatchHelper.AutoMatch`）。Undo の記録が無い（`PlayerCommandDispatcher.PartialImport.cs:102-160`）。
- 実データでは、顔だけの MQO と全体の MQO でオブジェクト名が一致している（`顔のみ.mqo` の「顔」と `_0N_モデル_Ref_id再修正.mqo` の「顔」。テスト用データ/MQOデータ）。頂点の無い仕切りオブジェクトは読み込みの一覧に入らない（`MQOPartialMatchHelper.BuildMQOList`）。

### 2.4 今ある穴

- 頂点を消したときにモーフの基準位置を直す処理は呼ばれていない（`MorphBaseData.RemoveVertices` に「呼び出し元 0 件」の注記）。
- そのため、モーフを持つオブジェクトには頂点数を変えるモディファイアを適用できない制限が入っている（`PlayerCommandDispatcher.ModifierChain.cs:525, 1044`）。MCP の利用シーンにも「モーフがあると頂点の増減で壊れる」という扱いがある（`BuiltinScenes.cs:87-90`、`queryModelState` の `topologyLocked`）。
- モーフのミラー設定（`MorphMirrorPolicy` の NoMirror・MirrorOf）は、設定する入口（コマンド・画面）が無い。`MorphMirrorOps.ResolveAllMirrorOfMorphs` の呼び出し元も無い。
- VMD の直接適用のモーフは未実装（`VMDApplier.cs:550-553` が空）。VMD の表情は、モーションクリップへ変換して表情トラックとして当てる経路だけが動く。

---

## 3. 新しい構造

### 3.1 用語

- **形**：名前の付いた頂点位置のひとそろい。「笑い」「目を閉じる」など。従来の頂点モーフ 1 本に当たる。
- **基本の形**：真顔。形の番号 0（予約）。
- **今の形**：モデルがいま取っている形。モデルに 1 つ。

### 3.2 形の一覧（モデルが持つ）

`ModelContext.Shapes`。1 件の中身：

- 番号（モデル内で一意。消しても再利用しない）、名前、英語名、パネル（眉・目・口・その他）
- 種類（位置／UV）
- 読み込み元（MQO ファイル、対応の組、倍率・反転。3.8）
- 作成日時

`ModelContext.CurrentShapeId`（今の形）、`PreviousShapeId`（入れ替え用）。

### 3.3 頂点が持つデータ

`Vertex.Shapes`（既定 null）。中身は（形の番号, 位置）の並び。`Vertex.ControlPoints` と同じ持ち方。

規則

- `Vertex.Position` は常に「今の形」での位置。
- 今の形以外の形は、基本の形と位置が違う頂点だけが持つ（持たない＝基本の形と同じ）。
- 今の形が基本の形でない間は、基本の形の位置を番号 0 として持つ。番号 0 を持たない頂点は「その形の間に作られた頂点」で、どの形でも今の位置にあるものとして扱う。
- UV の形は UV スロット 0 に同じ規則を当てる。
  - 段 6 の実装：形の種類 `ShapeDef.Kind`（位置／UV）、頂点の `Vertex.UVShapes`（形の番号, UV）。今の形が 0 以外の間は UV を持つ全頂点が番号 0 の UV の控えを持つ（位置と同じ）。切り替え・複製・削除・Undo・点検は位置と UV を同じ手順で扱う（`ShapeOps`）。保存はメッシュ CSV の `vsu` 行、`shapes.csv` の shape 行の 6 列目（版 1.2）、JSON の `VertexDTO.sui`・`suv`・`ShapeDefDTO.kind`、バイナリの `MeshFieldFlags.UVShapes`（0x200000）と PLRM 版 16。
  - UV の形は PMX の UV モーフとして書き出す。ブレンドシェイプ（VRM・glTF・FBX・プレファブ）には出さない（書き出しは警告を出す）。左右反転と MQO 読み込みは断る（頂点位置の形だけ）。
  - 追加 UV1〜4 のモーフ（PMX の種類 4〜7）は旧モーフのまま読む。PolyLing は追加 UV を読み込んでおらず、入れる先が無い（段 7 で見直す）。

頂点に持たせる理由

- 頂点の削除（`MeshObject.RemoveVertices`、唯一の入口）、複製（`Vertex.Clone`、`MeshObject.Clone`）、Undo のスナップショット（`MeshObjectSnapshot` は `MeshObject` を丸ごと持つ）で、何もしなくても付いて回る。
- 別の配列に持つと、頂点の増減・並べ替えのたびに索引を付け替える処理を全操作へ入れる必要がある（2.4 の穴はこれ）。
- 頂点を新しく作る操作（ナイフ・押し出しなど）で作られた頂点は、データを持たないので「どの形でも同じ位置」になる。壊れはしない。形ごとに補間するかは将来の改善（6 の 4）。

### 3.4 今の形の切り替え

X から Y へ（対象はモデルの全描画オブジェクト。ミラー側も含む）

1. X が基本の形でなければ、各頂点について：番号 0 を持てば、今の位置が番号 0 と違うときだけ X として控え（同じなら X を消す）、位置を番号 0 へ戻して番号 0 を消す。持たなければ何もしない。
2. Y が基本の形でなければ、各頂点について：今の位置を番号 0 として控え、Y を持てばその位置にして Y を消す。
3. 表示を更新する（MQO の頂点位置読み込みと同じ更新経路）。

- 切り替えは Undo の記録 1 件にする。Undo は記録した順に戻る（`UndoGroup.cs:54-58`）ので、形の中での頂点編集と切り替えの順序は保たれる。
- 「入れ替え」は今の形と直前の形の切り替え。真顔と笑いの見比べはこれ 1 回。
- 切り替えの前に、試し表示（`WorkingPositions`）が残っていれば終える。

### 3.5 表情

- 頂点モーフ 1 本＝形 1 つ。名前・英語名・パネルは形が持つ。
- `MorphExpression` はグループ（形の名前 × 重みの並び＝今の `GroupChildren`）と VRM の付帯データだけにする。`MeshEntries`・`IsSymmetric` は廃止。
- 名前の解決は「表情の名前 → 形の名前」の順（モーションの表情トラック、VMD、VRM）。
- グループの子は必ず形として残るので、今の「子を親へ展開して子の表情を外す」処理と `AddMissingGroupChildren` は要らなくなる。
- 段 5 で決めたこと（`ShapeBlendOps` 冒頭が正本）
  - 解決の口は `ShapeBlendOps.Resolve`（表情の名前 → 形の名前。どちらも Name、無ければ NameEnglish）。展開：頂点の表情は同じ名前の形があればその形（重み 1）、無ければ旧モーフのエントリ。グループは旧モーフのエントリ（同じ名前の形がある旧モーフを除く）＋ 子のうち形であるもの。旧モーフのエントリは取り込みで子を重み付きで展開したもの（VRM はノードごとの重み）なので、今までの値を変えないようエントリのまま使う。
  - **旧モーフと形が同じ名前のとき**：形を使い、旧モーフは使わない（試し表示・再生・PMX・プレファブ・VRM・glTF・FBX の全部）。理由：形が正本で、旧モーフは段 6 より前の読み込みが作る一時のもの。両方を足すと二重に動く。旧モーフの名前は `MorphBaseData.MorphName`。
  - 形 1 つは頂点の表情 1 つとして書き出す（PMX の頂点モーフ、VRM の表情）。同じ名前の頂点の表情があれば、その表情の付帯データ（VRM のプリセット・上書き）で形を指す。
  - 表情を形の名前で作る・変えるコマンドは `addExpression`（グループを作る）・`setExpressionShapes`（子のうち形を指すものだけを置き換える。形でない子と旧モーフのエントリは触らない。頂点の表情には使えない）。

### 3.6 重み付きの試し表示・モーション再生

- 置き場所は今と同じ `WorkingPositions`（基本の形 ＋ Σ 重み ×（形 − 基本の形））。
- 差分を取る口を 1 つにする（`ShapeOps.GetOffsets`。今の形がどれでも、基本の形との差を返す）。今の `GetMorphOffsets` の呼び出し元は全部ここへ寄せる。
- `WorkingPositions` は `Position` への足し込みなので、今の形が基本の形でないと二重に動く。表情の試し表示とモーション再生は、始めるときに基本の形へ切り替える（切り替えの記録が 1 件残る）。
- 段 5 の実装：足し込みは `ShapeBlendBuffer`（チャンネルごとの重み）1 つ。表情の試し表示（`MorphPreviewState`・`MorphExpressionHandler`）、オブジェクトリストのモーフ欄の試し表示（`MeshListOps`。以前は頂点位置を直接書き換えていた）、モーション再生（`MotionClipApplier`）が使う。旧モーフの差分を取る口は `ShapeOps.GetLegacyMorphOffsets` だけ（`GetMorphOffsets` の呼び出し元はここ 1 つ）。
  - 切り替えのきっかけ：表情のパネルは人の操作（ウェイト・行の重みを動かす、形を足す・外す）で試し表示を始めるときだけ。画面の更新（Undo・Redo の後など）では始めない（始めると切り替えが Undo と食い違う）。オブジェクトリストのモーフ欄は `startMorphPreview`。モーションは表情が当たるクリップのフレームを当てるとき（`MotionClipHandler`）。切り替えられなかったときは試し表示を始めない。
  - 形を切り替えたら足し込みを捨て（`ShapeOps.SwitchModel`）、表情の試し表示とモーフ欄の試し表示を終える（3.4）。
  - 表示中の位置は `queryDisplayedPositions` で読める（頂点位置 ＋ 足し込み）。

### 3.7 ミラー

- ミラー側のオブジェクト（MirrorSide・BakedMirror）も、自分の頂点に形のデータを持つ。切り替えは実体側と同時に行う。
- 実体側からミラー側を作り直す処理（`MirrorBranchOps.RebakeDerivedMirrorVertices`、`MirrorPair` の同期）を、形のデータにも広げる。今の `SyncAllMirrorMorphs`（ミラー側のモーフを作り直す）はこれに置き換わる。
- `MorphMirrorPolicy`（NoMirror・MirrorOf）は廃止。代わりに「形を左右反転して新しい形を作る」コマンドを置く（対称頂点の対応は `MorphMirrorOps.BuildSymmetryVertexMap` を使う）。
- 左右反転の規則（段 3 で決めた。`mirrorShape`、`ShapeOps.MirrorShape`）
  - ミラーの関係にあるオブジェクト（対の実体側・ミラー側、実体化したミラーとその元、表示ミラー）は、元の形の写しにする。ミラー側の形は実体側の鏡映と決まっているので、左右を入れ替えても同じ形になる。
  - それ以外のオブジェクトは、同じメッシュの中の対称頂点へ、差分（形 − 基本の形）を鏡映して写す。対応は基本の形の位置で取る（今の形に入っていても同じ結果）。対称面はメッシュのミラー軸・ミラー位置（`MorphMirrorOps.ResolveAxis`）。
  - 相手が見つからない頂点は基本の形のまま。元の形で動いていたのに相手が無かった頂点の数を結果（`unmatchedVertices`）に返す。

### 3.8 MQO との連携

- **形へ読み込む**：今の形を笑いにして MQO を読むと、MQO に入っているオブジェクトだけが笑いの位置になる。入っていないオブジェクトは基本の形のまま。読み込みそのものは既存の頂点位置差し替え（`MQOPartialImportOps.ExecuteVertexPositionImport`）を使う。新しい形を作って読み込むまでを 1 コマンドでも行える。
- **対応付け**：①形が覚えている組 → ②名前の一致 → ③頂点数の一致（相手が 1 つに決まるときだけ）。決まらないオブジェクトは読まずに報告する。頂点数が合わないものも読まずに報告する。
- **読み込み元を覚える**：形ごとに MQO ファイルと対応の組・倍率・反転を持つ。メタセコで直したあとは「再読み込み」1 回。
- **MQO へ書き出す**：今の形のまま既存の部分書き出しを使う。
- 読み込みに Undo の記録を付ける（今は無い）。
- 既存の「モーフの基準を追従させる」処理（`PMXPartialImportOps.RemapMorphBasesAfterVertexChange`）は要らなくなる。基本の形で読めば基本の形が変わり、形の側は絶対位置なので動かない。

基本の形を MQO で直したときの扱い：形は絶対位置で持つので、基本の形だけを直すと「形 − 基本の形」が変わる。差分を保ったまま基本の形を差し替える読み込み（全部の形を同じだけ動かす）を選べるようにする。既定は「差分を保つ」（今の動きと同じ）。

### 3.9 取り込みと書き出し

- 取り込み（PMX・VRM・glTF・FBX）：隠しオブジェクトを作らず、形の一覧と頂点のデータにする。グループモーフ・VRM の表情は表情（3.5）にする。
- 段 6 の実装（規則の正本は各取り込みの注記：`PMXImporter.Morph.cs`・`VrmToPolyLingConverter.cs` の BuildShapes・BuildExpressions・`FbxImporter.cs` 冒頭）
  - PMX：頂点モーフ・UV モーフ 1 本＝形 1 つ（差分 0 件も残す）。形の位置は頂点と同じ計算（PMX の位置＋差分 → 軸の反転 → 倍率）。グループモーフは子の名前 × 重みの表情。子の展開と子の表情の削除（旧 Phase 2・3）はしない。頂点モーフの表情は作らない。
  - PMX のミラー：PMX の値を両側に入れたあと、ミラーの対・実体化したミラーは、基本の位置が鏡映で合う頂点（1e-4 以内。点検と同じ幅）だけミラー側の形を実体側から作り直す。合わない頂点は PMX の値のまま（作り直すと左右の位置の違いが差分に入る）。値が変わった頂点の数は警告に出す。
  - VRM・glTF：モーフターゲットの名前 1 つ＝形 1 つ（ノードをまたいで 1 つ）。位置はスキンのベイク行列を (p + d) に掛ける。VRM の表情はグループの表情。**同じ名前のターゲットに付いた重みがノードごとに違う、または一部のノードだけを指すときは、バインドどおりの差分を焼いた専用の形「表情名_ターゲット名」を作り、表情はそれを重み 1 で指す**（1 つの重みに丸めるとファイルの値が変わるため）。同じ名前の形を重み 1 で指すだけの表情は作らず、VRM の付帯データがあるときだけ頂点の表情として残す。
  - FBX：チャンネル名 1 つ＝形 1 つ。位置は `bakeU · ToUnity(p + d)`。表情は作らない。
  - 追加読み込み（Append）：形の番号を振り直し、名前が重なれば「名前_n」に改名して表情の子も追随させる。追加先が基本の形でなければ足す頂点に番号 0 の控えを付ける。Undo は `ShapeListRecord` を同じ組に入れる。
  - BlendShapeSync CSV（`importMorphCsv`・`exportMorphCsv`）は形の名前で読み書きする（`MorphCsvIO.cs` 冒頭）。
  - 旧モーフを作らなくなったので、`PMXExporter.Morph.cs` の `AddMissingGroupChildren` は読み込んだモデルでは何もしない（グループの子が形・UV の形として必ず残るため）。撤去は段 7。
- 書き出し：
  - PMX（`PMXExporter.Morph.cs` の `BuildShapeMorph`）：形ごと・オブジェクトごとに差分を取って 1 本のモーフにまとめる。
  - プレファブ・VRM・glTF（`HierarchyMorphBuilder`）：オブジェクトごとに、差分のある形をブレンドシェイプにする。割り当ての鍵をモーフのオブジェクト番号から形の番号に変える（`MorphShapeSlot.MorphContextIndex`、`Vrm10SceneAssembler.cs:455-504`）。
  - FBX（`FbxModelWriter.cs:699-711, 962-964`）、モーションの標本（`ModelMotionSampler.MorphWeights`）：同じく鍵を形の番号に変える。
- 書き出しは今の形に関係なく、基本の形をベースに出す。
- 段 5 の実装：各書き出しの入口（PMX・FBX・glTF・VRM・プレファブ）で、`ModifierExportView.For` の後に `ShapeExportView.For` を通す。形の控えを持つ行を、基本の形へ切り替えた写しに差し替えたモデルを渡す（実モデル・表示・Undo には触れない）。割り当ての鍵は `MorphTargetKey`（形の番号か旧モーフの索引）。プレファブ・VRM・glTF・FBX はオブジェクトごとに差分のある形をブレンドシェイプ（名前は形の名前）にし、どのオブジェクトにも差分の無い形は警告する。MQO・OBJ・STL の全体の書き出しは今の形のまま（形を持てない形式）。
- プレファブの書き出しはブレンドシェイプを載せていなかった（`HierarchyExportOptions` に `ExportMorphTargets` が無く、既定の false のまま。旧モーフも同じ）。段 5 で既定 true の設定を足し、書き出しの窓と MCP の受け口（`exportMorphTargets`）から変えられるようにした。

### 3.10 保存

- CSV：モデルのフォルダに形の一覧（`shapes.csv`）。メッシュの CSV に頂点ごとの形の行（頂点番号・形の番号・位置）。JSON：`ModelDTO` に形の一覧、`VertexDTO` に形の欄。バイナリ（リモート送信）：版を上げて同じ欄を足す。3 つ同時に直す。
- ファイルにはメモリのまま書く（段 1 で変更）。頂点の位置は今の形の位置、今の形の番号は `shapes.csv` の `current` 行（JSON は `currentShapeId`、バイナリは PLRM 版 14）。今の形が基本でないとき、基本の位置は各頂点の形の行の番号 0 に入る。基本の形へ直してから書く方式は、保存の 3 経路（メッシュ単位で書く）すべてに変換を挟むことになるので採らなかった。外の道具（Blender アドオン）は、番号 0 の行があればそれを基本の位置として読む（段 8）。
- **段 8 の実装（Blender アドオン `poly_ling_py/polyling_io`。規則の正本は契約 §7.2・§7.4・§7.5、`format/shapes.py` 冒頭）**：
  - 形式の層：`format/shapes.py`（新規）が `ShapeOps.SwitchMesh` の写し（切り替え・float32 の完全一致）と表情 → 形の名前の解決（`ShapeBlendOps.Resolve`・`ExpandExpression` の写し）を持つ。CSV は `vsh`・`vsu` 行を頂点の `shi`/`shp`・`sui`/`suv`（JSON と同じ欄名）として読み書きし、`shapes.csv`（版 1.2）・`morphgroups.csv`（版 2.0。1.0 のエントリは飛ばして警告）を読み書きする。旧モーフ（種類 Morph の行・`.mfproj` の要素）を含むデータは本体と同じ文言のエラーで読まない。`*.morph.csv` は読まずに警告し、保存で消す。旧の行・欄（`morphBaseData`・`mb` など、editorstate の `selectedVertexMorph*`）は捨てる。
  - 取り込み：メッシュごとに頂点を基本の形へ移してから作るので、基準キー（Basis）は今の形に関係なく基本の形。位置の形 1 つ＝シェイプキー 1 つ（差分のあるオブジェクトだけ。今の形のキーは値 1）。結び付けはオブジェクトの `polyling_shape_keys`。
  - 書き出し：基準キーを基本の形、シェイプキーを形として、基本の形と成分が違う頂点だけに控えを付け、最後に今の形へ移す（3.10 の規則どおり、今の形の位置と番号 0 の控え）。形に当たらないシェイプキーは新しい形。名前を変えたシェイプキーは並び順で引いて形の名前も変える。
  - **UV の形は Blender に表さない**：控え（基本の形へ移したもの）をオブジェクトの `polyling_uv_shapes` に残し、頂点数・面のつながりが変わらなければそのまま戻す（変われば外して警告）。理由：シェイプキーは位置しか持てない。UV マップは 1 メッシュ 8 個までで（bpy 5.2 で確認）、small の顔肌 1 つに UV の形が 33 個ある。頂点番号で持つ控えはこのアドオンの既存の決まり（契約 §7.4）と同じ扱い。
  - 表情：`morphExpressions` は `polyling_model` の JSON のまま往復する（VRM の付帯データ `vrm` も）。モーションの表情の割り当て（`motion.expression_targets`）は本体と同じ解決にした。アドオンに VRM Add-on の表情と結び付ける処理は無い（検索で 0 件。VRM Add-on とつなぐのは Humanoid の割り当てだけ）。
- 実際の欄：メッシュ CSV は頂点行の直後に `vsh,個数,形の番号,x,y,z,…`。JSON は `VertexDTO.shi`（番号）・`shp`（位置）。バイナリは `MeshFieldFlags.Shapes`（0x100000）。
- 廃止する欄：`morph.csv`、`morphBaseData`、`morphParentIndex`／`morphParentName`、`morphMirror`、表情の `meshEntries`／`meshIndices`。

### 3.11 画面

- 左ペイン「ボーン・モーフ」に「形」のボタンを足す。右のパネルは次のもの。
  - 形の一覧と絞り込み。行を選ぶと今の形が切り替わる。
  - 「直前の形と入れ替え」ボタン。
  - 追加（基本の形から／今の見た目を新しい形として）、複製、削除、名前・英語名・パネルの変更、左右反転した形を作る。
  - MQO から読み込む・再読み込み・MQO へ書き出す。
  - 段 3 で作った分（MQO 以外）：パネル ID は `shape`。一覧の先頭の行は基本の形で、選んでいる行＝今の形。複製は専用のコマンドを置かず `addShape` の `sourceShape` に元の形を渡す。名前・英語名・パネルの欄と複製・左右反転・削除は今の形に働く。
- 表情のパネル（モーフエクスプレッション編集）は「形の名前 × 重み」の編集にする。
  - 段 5 で作った分（パネル ID は `morph`）：上の欄と「作る」でグループの表情を作る。選んだ表情の行は「形」（子のうち形）・「形（固定）」（同じ名前の形がある頂点の表情）・「旧モーフ」（段 7 まで）。行を選んで「重み」の欄で変える（離したとき・UI 自動操作では即確定）、「選んだ形を外す」、「足す形」と「足す」（グループだけ）。パネルはコマンドとツールの窓口を呼ぶだけ。
- 撤去：オブジェクトリストのモーフのタブ、「モーフ生成・差分から」のパネル（差分から作る用途は「今の見た目を新しい形として」と MQO 読み込みで足りる）。

### 3.12 廃止するもの

種類 Morph（`MeshType.Morph`・`MeshCategory.Morph`）、`MorphBaseData`、`MorphParentIndex`、`MorphMirrorPolicy`・`MirrorOfMorphIndex`、`MorphMeshEntry`、モーフの選択（`SelectedMorphIndices`・選択カテゴリ Morph）、`MorphPreviewState.FindBaseMeshIndex`（名前規則で親を探す処理）、`SyncAllMirrorMorphs`・`FindMirrorMorph`、関連する Undo 記録（`MorphConversionRecord`・`MorphCreateRecord`・`MorphMirrorUndoRecords`）、`VertexDataTransferOps` の MorphBase の転送、TPose の控えの `MorphBasePositions`。

コマンドの置き換え

| 今 | これから |
|---|---|
| convertMeshToMorph / convertMorphToMesh | 形の追加（今の見た目から）／形をモデルとして取り出す |
| createMorphFromDiff | 形の追加 ＋ MQO 読み込み |
| createMorphSet / setMorphEntryWeights | 表情の作成・重みの変更（形の名前で指す） |
| setMorphExpressionAttributes / deleteMorphExpression | 表情の名前変更・削除として残す。形の名前・英語名・パネルは形の名前変更コマンドで変える |
| startMorphPreview / applyMorphPreview / endMorphPreview | 形と重みを指定する試し表示（1 系統にまとめる） |
| selectAllMorphs / deselectAllMorphs | 廃止 |
| expandMorphExpressionToModel | 形をモデルとして取り出す |
| importMorphCsv / exportMorphCsv | 形の名前で読み書きする形に直す |

### 3.13 法線（段 2 で確認）

- 法線は形ごとには持たない。頂点が持つ法線（`Vertex.Normals`）は全部の形で共有する。
- 確認したこと：形に入って頂点を動かしても、切り替えても、`Vertex.Normals` は変わらない（立方体の角の頂点。位置は y=0.5 → 1.0、法線は (0,1,0) のまま。`getRawData`）。
- 切り替えは表示用メッシュを `ToUnityMesh` で作り直す（`ShapeOps.SwitchModel`）。`ToUnityMesh` は「法線を保持する」（`MeshObject.PreserveNormals`、既定 true）のオブジェクトでは保持している法線をそのまま使い、保持しない設定で法線が無い（全部が既定値の）ときだけ自動で計算する（`MeshBridgeDefault.cs:161-162` の注記）。頂点移動と同じ規則なので、切り替えのための追加の処理は置かない。
- 頂点の向きごと変える操作は法線も回す。スキンの焼き込み（`TPoseConverter.BakeSkinnedVertices`。スキンごと確定・T ポーズ変換・この姿勢で確定）は、ウェイトのある頂点の法線を位置と同じスキン行列で回す（段 3 の初めに直した。それまでは位置だけ焼いていて、スキンドの状態の陰影が合わなかった）。法線は全部の形で共有なので、形ごとの処理は無い。
- 大きく形を変える形で陰影が合わなくなるときは、その形に入った状態で法線の再計算を行う（全部の形で共有されるので、基本の形の陰影も変わる）。形ごとに法線を持つかは、書き出し（段 5）でブレンドシェイプの法線の差分が要るかを見て決める。
- **段 5 で決めた：書き出しで法線の差分は持たない**（ブレンドシェイプの法線の差分は 0、PMX には法線のモーフが無い）。理由：法線は全部の形で共有なので、PolyLing のデータでは形の間の法線の差は常に 0。書き出しのときだけ計算すると、PolyLing で見ている陰影と書き出した先の陰影が食い違う。受け側で計算させたいときは、受け側の設定（Unity の FBX 取り込みの Blend Shape Normals など）で行う。形ごとに法線を持つのは将来の改善。

### 3.14 モディファイアの適用・コピー（段 2 で決めた）

- 形を持つオブジェクトに、頂点数を変えるモディファイアは適用できない（旧モーフと同じく断る。形は頂点番号で対応するため）。頂点数が変わらない適用では、今の形の位置だけが結果になり、他の形は残る。見直しは段 7。
- **段 7 で見直した**：処理列の適用（1 個・途中まで・すべて）と外す操作は、元メッシュと参照を形ごとにその形にした写しから処理列を流し直し、結果の頂点に形の控えとして付ける（`ModifierChainHandler.AttachShapes`）。差分の無い形は流さない。どれかの形で頂点数か面の並びが今の形の結果と違えば、何も変えずに断る。サブディビジョンのスタック（処理列でないもの）は従来どおり：頂点数が変わらなければ形を写し、変われば断る。サブディビジョンを外すと結果の形は落とす。書き出し（`ModifierExportView`）も同じ規則で、畳んだ結果に形を付けてから `ShapeExportView` が基本の形へ切り替える（段 5 の残しの解消）。
- コピーと貼り付けは形を運ばない。貼るときの今の形や貼り先のモデルが、写したときと違いうるため。貼った頂点は、どの形でも同じ位置になる。

---

## 4. 影響範囲

「Morph」を含むファイルは Runtime に 199 個。構造に触れている（種類 Morph・基準データ・親番号・エントリ・差分の取得）のは 107 個。区画ごとの主なもの：

| 区画 | 主なファイル |
|---|---|
| データ | `Vertex.cs`、`MeshContext.Attributes.cs`、`MorphBaseData.cs`、`MorphExpression.cs`、`MorphMirrorPolicy.cs`、`MeshCategory.cs`、`TypedMeshIndices.cs`、`MirrorPair.cs`、`TPoseBackup.cs`、`ModelStateSnapshot.cs`、`MeshSelectionSet.cs` |
| モデル | `ModelContext.cs`（選択）、`.Data.cs`（表情）、`.Highlight.cs`（ミラー同期）、`.MeshList.cs`・`.Selection.cs`（番号の付け替え） |
| 操作 | `MeshListOps.cs`、`MorphMirrorOps.cs`、`MirrorBranchOps.*.cs`、`MeshContextCloneOps.cs`、`SelectionClipboardOps.cs`、`SelectionDuplicateOps.cs`、`TPoseConverter.cs`、`VertexDataTransferOps.cs`、`MeshFilterToSkinnedConverter.cs` |
| コマンド | `PanelCommand.Morph.cs`（15 個）、`PlayerCommandDispatcher.BoneMorphUv.cs`・`.TPoseMergeMorph.cs`・`.PartialImport.cs`・`.ModifierChain.cs`・`.ObjectOrigin.cs`・`.Blend.cs`・`.Query.Build.cs`、`PLCommandAttribute.cs`、`BuiltinScenes.cs` |
| 試し表示・再生 | `MorphPreviewState.cs`、`MorphExpressionHandler.cs`、`MotionClipApplier.cs`、`ModelMotionSampler.cs`、`VMDApplier.cs` |
| 取り込み | `PMXImporter.Morph.cs`、`VrmToPolyLingConverter.cs`、`FbxImporter.cs`、`PMXPartialImportOps.cs`、`MQOPartialImportOps.cs`、`MQOPartialMatchHelper.cs`、`MorphCsvIO.cs` |
| 書き出し | `PMXExporter.Morph.cs`・`.Mesh.cs`、`HierarchyMorphBuilder.cs`、`HierarchyBuilder.cs`、`FbxModelWriter.cs`、`Vrm10SceneAssembler.cs`、`PolyLingToVrmLibConverter.cs` |
| 保存 | `ModelDTO.Mesh.cs`・`.Morph.cs`、`MorphExpressionDTO.cs`、`ModelSerializer*.cs`、`CsvMeshSerializer*.cs`、`CsvModelSerializer*.cs`、`RemoteProgressiveSerializer*.cs`、`RemoteDataProvider.cs` |
| Undo | `MeshListRecords.cs`、`MorphMirrorUndoRecords.cs`、`MeshAttributeChangeRecord.cs`、`ModelUndoRecords.cs`、`TPoseUndoRecord.cs` |
| 窓口・画面 | `ViewInterfaces.cs`、`LiveViews.cs`、`MeshSummary.cs`、`ModelSummary.cs`、`SummaryBuilder.cs`、`MeshListSubPanel*.cs`、`PlayerMorphSubPanel.cs`、`PlayerMorphCreateSubPanel.cs`、`PlayerMeshSelectionSetSubPanel.cs`、`PlayerVertexTransferSubPanel.cs`、`PlayerPartialImportSubPanel.cs`、`PlayerLayoutRoot.LeftPaneButtons.cs`、リモート（`RemoteHtmlClient.cs`・`RemoteSelectionStore.cs`・ListClient） |
| 外部 | Blender アドオン（`データとツール/ツール/poly_ling_py/polyling_io` の `format/csv_mesh.py`・`csv_project.py`、`blender/importer.py`・`exporter.py`・`meshread.py`・`motion.py`） |
| 文書 | `PolyLing_外部連携の契約.md`（§5.1・§7.2・§7.4・§7.5）、`PolyLing_機能の場所.md`、`MCP操作手順書.md`、`PolyLing_コマンド追加の手引き.md` |

Editor 側のアセンブリ（`Packages/com.haglib.polyling/Editor`）にモーフを扱うコードは無い（検索 0 件）。

### 注意が要る箇所（思わぬ不具合の元）

1. **全頂点の位置へ変換を掛ける操作**は、形のデータにも同じ変換を掛けないと形だけ取り残される。検索（位置へ行列・倍率・鏡映を掛けて代入している箇所）で当たったのは 19 ファイル：`TPoseConverter.cs`、`MeshFilterToSkinnedConverter.cs`、`SkinKindConverter.cs`、`MirrorBranchOps.Create.cs`、`SelectionClipboardOps.cs`、`ModifierChainHandler.cs`、`ModifierSpace.cs`、`ObjectMoveTool.Apply.cs`、`ObjectArrayGenerator.cs`、`PrimitiveMeshTransform.cs`、`SurfacePasteOps.cs`、`MQOImporter.MirrorBone.cs`、`PMXHelper.cs`、`PMXImporter.Bone.cs`、`PlayerCommandDispatcher.BoneMorphUv.cs`・`.DeformSkin.cs`・`.ObjectOrigin.cs`・`.TPoseMergeMorph.cs`、`PlayerViewportManager.Renderer.cs`。段 2 で 1 つずつ「形のデータにも掛ける／新しく作る頂点なので不要」を判定する。検索式に掛からない書き方が残っていないかも段 2 で確かめる。
   **段 2 の判定結果**（共通の口は `ShapeOps`。規則は `ShapeOps.cs` の「一括の変換」の注記）

   | 扱い | 箇所 |
   |---|---|
   | 形にも同じ行列を掛ける＋ Undo の記録に形の控えを足す | 拡大縮小ベイク・形の拡大縮小・原点の設定・姿勢くさび（`PlayerCommandDispatcher.ObjectOrigin.cs`、記録は `ObjectScaleBakeRecord`・`ObjectPoseUndoRecord`）、原点だけ移動（`ObjectMoveTool.Apply.cs`・`.cs`、`PlayerCommandDispatcher.BoneMorphUv.cs`。開始時の控えから毎回掛け直す。記録は `PivotMoveRecord`）、ピボット移動（`PivotOffsetTool.cs`。19 ファイルの外）、T ポーズ焼き込み（`TPoseConverter.cs`。頂点ごとのスキン行列。控えは `TPoseBackup.ShapeSnapshots`） |
   | 形にも同じ行列を掛ける（Undo は一覧を丸ごと複製する記録なので追加なし） | スキンド変換（`MeshFilterToSkinnedConverter.cs`）、スキンの種別の変換（`SkinKindConverter.cs` の 2 か所）、マージ（`PlayerCommandDispatcher.TPoseMergeMorph.cs`） |
   | ミラー側の形を実体側から鏡映して作る・取り直す | `MirrorBranchOps.Create.cs`（`BuildMirroredMeshObject`・`RebakeDerivedMirrorVertices`）、`MirrorPair.SyncPositions`、`PlayerViewportManager.Renderer.cs`（実体化したミラーの同期）、`PlayerCommandDispatcher.Blend.cs`、同じメッシュへのミラー実体化（`MirrorBaker.cs`。19 ファイルの外。作る頂点が形を引き継いでいなかった） |
   | 形を落とす（出力・変形して作る別オブジェクト・補間で作る頂点・クリップボード） | モディファイアの列の出力（`ModifierChainHandler.cs` の入力の写しと書き込み、`PlayerCommandDispatcher.ModifierChain.cs` の結果の作成）、配列の複製（`ObjectArrayGenerator.cs`）、顔の変形で作る別オブジェクト（`PlayerCommandDispatcher.DeformSkin.cs` の 5 か所）、辺上・三角の付け替えで作る頂点（`EdgeTriangleOps.cs`・`TriangleRelocateOps.cs`）、コピー（`SelectionClipboardOps.Capture`） |
   | 何もしない（その時点で形が無い） | 生成（`PrimitiveMeshTransform.cs`・`SurfacePasteOps.cs`）、取り込み（`MQOImporter.MirrorBone.cs`・`PMXHelper.cs`・`PMXImporter.Bone.cs`）。**段 6 で取り込みを形にするとき、PMX の倍率と軸の変換は形にも掛ける** |
   | 何もしない（読むだけの写し・今の形への頂点編集・旧モーフ） | `ModifierSpace.cs`、頂点の移動・回転・拡大・スカルプト・ブレンド・頂点データの転送・頂点の結合、`MirrorPair.cs:451` と `MorphMirrorOps.cs`（旧モーフ。段 7 で撤去） |
   | 何もしない（同じ位置への複製なので形もそのまま正しい） | 頂点の分離・切り離し・選択の複製・連結や部品 ID での分割、位相の Undo 記録 |

   形の控えは、位置と同じ計算（同じ関数・同じ行列を同じ順で）で出す。形の比較は成分の完全一致なので、式が違うと（行列 2 回と合成行列 1 回など）丸めの差で、動かしていない頂点が「形で動いた頂点」になる。行列を渡す口（`TransformShapes`）は位置も同じ行列 1 回で出している所（スキンド変換・スキンの種別の変換の焼き込み・マージ）だけで使い、ほかは位置に使う関数を `MapShapes`・`MapShapesFrom` へ渡す。位置を同じ計算で出せない所（T ポーズ焼き込みの GPU の読み戻し）は `MapVertexShapesKeepingEqual`。モデルを丸ごと複製する処理（`DeepCloneModelContext`）は、形の一覧と今の形の番号も写す。

   段 1 の穴だった点：`Vertex.Clone` は形の控えも複製する。複製してから位置だけ書き換えると、新しい頂点が複製元の形の位置を持ったままになる。上の表の「掛ける」「落とす」はこれへの対応を含む。今後、頂点を複製して位置を変える処理を足すときは、同じ規則で `ShapeOps` を呼ぶ。

2. **Undo**：頂点移動の記録は頂点番号と位置だけを持つ（`MeshUndoRecord_Base.cs:96-128`）。形の切り替えを記録に入れ忘れると、別の形で戻して形を壊す。切り替えは必ず記録する。
3. **保存の往復**：CSV の書き出しは `MeshDTO` を経由する（`CsvMeshSerializer.cs:72-87`）。`VertexDTO` に欄を足し忘れると、保存で形が消える。
4. **モディファイアの出力**は入力から作り直されるので、形のデータを持てない。入力の今の形に追随する（切り替えで入力の位置が変わるので作り直しが走る）。
5. **展開頂点の並び**：PMX・プレファブの書き出しは展開の規則（`MeshExpansion`、`HierarchyMorphBuilder` 冒頭の注記）に従う。形の差分は頂点番号で持つので、今と同じ写し方を使う。自前で展開順を書かない。
6. **リモート送信のバイナリ**は断片（モデル単位・メッシュ単位）でも丸ごとでも送る。形の一覧はモデル側、頂点のデータはメッシュ側に入れる。

---

## 5. 段

各段の終わりに 3 つの csproj のコンパイル 0 エラーと、画面のキャプチャでの確認を行う。旧構造は段 7 まで残し、途中の段でも今の機能が動く状態を保つ。

| 段 | 内容 | 確かめること |
|---|---|---|
| 1 | **土台**。`Vertex.Shapes` と複製、形の一覧・今の形（`ModelContext`）、`ShapeOps`（切り替え・差分・正規化）、コマンド（形の追加・削除・名前変更・切り替え・入れ替え・一覧の照会・検査）、Undo の記録、保存 3 形式に欄を足す | 立方体で形を 2 つ作り、切り替え・入れ替え・頂点移動・頂点削除・Undo／Redo・保存して読み直し、が全部の形で合う |
| 2 | **位置を一括で書き換える操作とミラー**。4 の注意 1 の 19 ファイルの判定と対応、ミラー側への同期、検査コマンドに「ミラー側と実体側の形が合うか」を足す | T ポーズ焼き込み・原点の移動・ミラー実体化・マージの前後で、形の差分が保たれる |
| 3 | **画面**。「形」のパネル（一覧・切り替え・入れ替え・追加・複製・削除・名前・左右反転） | 画面の操作だけで真顔と笑いを 1 回で入れ替えられる |
| 4 | **MQO 連携**。形へ読み込む・対応付け（覚えた組 → 名前 → 頂点数）・読み込み元を覚える・再読み込み・書き出し・Undo・基本の形の差し替え | `顔のみ.mqo` を形として読み、メタセコ側を直した想定のファイルで再読み込みできる。決まらない組は読まずに報告される |
| 5 | **使う側の切り替え**。試し表示（1 系統に）、モーションの表情、PMX・プレファブ・VRM・glTF・FBX の書き出し、表情パネルを、形から読むようにする | 手で作った形が PMX・VRM・FBX に出て、読み戻して差分が一致する |
| 6 | **取り込みの切り替え**。PMX・VRM・glTF・FBX の読み込みを形にする。表情を「形の名前 × 重み」にする | ゼロちゃんの PMX を読み、書き戻した PMX のモーフ数・差分の件数が元と一致する。オブジェクトリストにモーフのオブジェクトが増えない |
| 7 | **旧構造の撤去**。3.12 の廃止、保存の旧欄の削除、モーフがあると頂点を増減できない制限の見直し（モディファイア・利用シーン・`topologyLocked`） | 「Morph」の検索で旧構造の残りが無い。確認済みシナリオが通る |
| 8 | **Blender アドオンと文書**。`poly_ling_py` の読み書きと表情の割り当てを新しい保存形式に合わせる。契約・機能の場所・MCP 操作手順書を直す | Blender で読み・そのまま書きで、形（シェイプキー）が元と一致する |

操作者の困りごとが解ける段：2 番（見比べ）＝段 3、3 番（一部だけ読む）＝段 4、1 番（数が膨大）＝段 6。

---

## 6. 決めたこと（既定。異論があれば段に入る前に直す）

1. 画面の呼び名は「形」「基本の形」「今の形」。
2. 形のデータは頂点に持たせる（3.3）。
3. 表情の試し表示・モーション再生は、始めるときに基本の形へ切り替える（3.6）。
4. 新しく作られた頂点は、どの形でも同じ位置（形ごとの補間はしない。将来の改善）。
5. モーフのミラー設定は廃止し、「左右反転した形を作る」コマンドにする（3.7）。
6. モディファイアの出力は形を持たない（入力の今の形に追随）。
7. ファイルにはメモリのまま（今の形の位置と、今の形の番号）書く（3.10。段 1 で「基本の形で書く」から変更）。
8. 基本の形を MQO で差し替えるときの既定は「形の差分を保つ」（3.8）。

## 7. 調べ残しだった 3 件の結果（2026-11-15 に確認）

1. **シナリオ**：置き場は `<persistentDataPath>/PolyLing/scenarios`（`ScenarioLibrary.cs:28-33, 75-76`）。82 ファイルを検索し、モーフのコマンドを使うシナリオは無い。当たったのは `spring_frill_skin_vrm.csv:168` と `spring_pipe_skin_vrm.csv:163` の VRM 書き出しの設定 `settings.exportMorphTargets` だけ。→ コマンドの置き換え（3.12）でシナリオは壊れない。書き出し設定の名前 `ExportMorphTargets`（VRM・glTF・FBX・プレファブ）は変えない。
2. **モーションスタジオ**：表情は「名前とウェイト」だけで扱う。タイムラインは表情トラックを名前で持ち、送信は `expressions:[{name, provider, w}]`（`timeline.html:1913-1917`）、受信の記録は `frame.morphs` の名前 → ウェイト（`timeline.html:1978`）、VRM ビューアは名前で VRM の表情へ当てる（`vrm_viewer.html:142-185`）、VRMA の変換も表情トラックの名前だけ（`plmotion_vrma.js:221, 479`）。モデルのモーフの構造（オブジェクト・基準位置・エントリ）は読まない。→ スタジオ側の変更は不要。本体側は名前の解決を「表情の名前 → 形の名前」にする（3.5）だけ。
3. **リモートの画面と ListClient**：モーフを「オブジェクトの一種」として中継している。段 7 で次を直す。
   - オブジェクト一覧の要求の種別 `"morph"`（`RemoteServerCore.Messages.cs:286`）と、選択の中継の `morph` 欄（同 `:488-490, 534`、`RemoteServerCore.Push.cs:244, 270`、`RemoteSelectionStore.cs:51-105`、`ListClientBase.cs:297`）を廃止する。
   - オブジェクトごとの欄 `IsMorph`・`MorphName`・`MorphPanel`（`RemoteDataProvider.cs:45-47, 232`）と、HTML クライアントがそれを要求している箇所（`RemoteHtmlClient.cs:72, 266`）を廃止する。
   - ListClient は本体の `MeshListSubPanel` をそのまま使っている（`MeshListClient.cs:1-4`）ので、モーフのタブの撤去（3.11）で同時に消える。
   - 代わりに、形の一覧と今の形を返す要求と、切り替えの通知を足す（リモートから形を切り替えられるようにするため）。

---

## 8. 進み具合

| 段 | 状態 | 日付 | メモ |
|---|---|---|---|
| 1 | 完了 | 2026-11-15 | 足したもの：`ShapeData.cs`、`Vertex.Shapes`、`ModelContext.Shapes.cs`、`ShapeOps.cs`、`ShapeRecords.cs`、`PanelCommand.Shape.cs`（addShape / deleteShape / setShapeAttributes / setCurrentShape / swapShape / queryShapes / queryShapeAudit。すべて NoUi）、`PlayerCommandDispatcher.Shape.cs`、保存 3 形式。確認：立方体で形 2 つ、切り替え・入れ替え・頂点の書き換え・頂点削除・Undo／Redo・CSV 保存して読み直し、`queryProjectSyncDiff`（binary・csv、基本の形のときと形に入っているとき）差 0、`queryShapeAudit` 0。残し：直前の形（入れ替え用）は保存しない。頂点が持つ法線（Vertex.Normals）を形ごとにどう扱うかは未確認（段 2 で確かめる）。UV の形は段 6。旧モーフの構造には触れていない。 |
| 2 | 完了 | 2026-11-15 | 足したもの：`ShapeOps` の一括変換の口（`TransformShapes`・`TransformVertexShapes`・`TransformShapesFrom`・`CaptureShapes`・`RestoreShapes`・`MirrorShapesFrom`・`SyncMirrorShapes`・`ClearShapes`）、点検の 2 項目（`mirrorMismatch`・`modifierOutputEntries`）。箇所ごとの判定は 4 章 注意 1 の表。法線の方針は 3.13。直した既存の不具合：形の拡大縮小・原点の Undo で、ミラー側のオブジェクトだけ戻らなかった（記録の Undo に作り直しを足した）。立方体（形 2 つ）で確認したもの：形の拡大縮小と Undo、原点の設定と Undo、ミラー実体化（同じメッシュへ）と解除、ミラー側オブジェクトの作成、ミラーありでの拡大縮小と Undo、マージ。`queryShapeAudit` 0、`queryProjectSyncDiff`（binary・csv、形に入った状態・ミラーとマージ後）差 0、コンパイルエラー 0。**2 回目の確認（同日）で動かして確かめたもの**（立方体。形に入った状態で、全部の形の位置を保存 CSV の `vsh` 行で照合）：ピボット移動（`movePivot`。基本の形でも）と Undo／Redo、モディファイアの出力の追随・適用と Undo・頂点数を変える適用の拒否、コピーと貼り付け（基本の形でも）、原点だけ移動（ボーンのスライダー）と Undo／Redo、スキンド変換と解除とその Undo、スキンごと確定（焼き込み。90 度と 37 度）と Undo、`freezeCurrentPose` と Undo、姿勢くさびの取り込みと Undo、形の拡大縮小（フレームを回した倍率）、モデルブレンドのミラー（重み 1 のクローンへの適用）。**このとき直した不具合 2 件**：(1) ブレンド用のクローン（`DeepCloneModelContext`）が形の一覧と今の形の番号を写していなかった（頂点の控えだけが残り、点検が 336 件）。(2) 位置と形の控えを違う式で出していた所で丸めの差が出て、動かしていない頂点が「形で動いた頂点」になった（原点だけ移動 2 回で 1 → 48）。位置と同じ関数を通す口（`ShapeOps.MapShapes`・`MapShapesFrom`）へ替えた：`ObjectMoveTool.Apply.cs`（2 か所）、`PlayerCommandDispatcher.BoneMorphUv.cs`、`PivotOffsetTool.cs`、`PlayerCommandDispatcher.ObjectOrigin.cs`（4 か所）、`SkinKindConverter.cs`（解除）。T ポーズ焼き込み（`TPoseConverter.cs`）は位置が GPU の読み戻しなので、焼く前に位置と同じだった控えを焼いた後の位置にそろえる口（`MapVertexShapesKeepingEqual`）にした。規則は `ShapeOps.cs` の「一括の変換」の注記。直した後、上の操作で `queryShapes` の動いた頂点の数が変わらないこと、`queryShapeAudit` 0、`queryProjectSyncDiff`（binary・csv）差 0、コンパイルエラー 0 を確かめた。基本の形の状態でも、原点だけ移動と Undo、形の拡大縮小、スキンド変換、スキンごと確定（90 度）、解除を実行し、全部の形の位置が計算値と合った。`queryCommandAudit` は道具として出せない 3・分類が正典に無い 10 で、段 1 より前から増えていない。**3 回目の確認（同日。段 3 の会話の初め）**：残っていた 3 つを動かした。(a) `applyTPose`（Robot Kyle.pmx を Humanoid 自動割当で読み、形 2 つ）：基本の形のとき・形に入っているときとも、動いた頂点の数は [1,1] のまま、点検 0、形の差分は長さを保って回り、Undo で元の値・Redo で同じ値。(b) ギズモのドラッグによる原点だけ移動（実際のマウス）：両方の状態で、今の位置と全部の形の位置が原点の移動量だけ同じにずれ、動いた頂点の数は変わらず、点検 0。ただし **Undo が記録されていなかった**（形とは無関係の既存の不具合）。`ObjectMoveTool.OnMouseUp` は原点だけ移動のとき確定を呼び出し側に任せるが、画面で使われる `ObjectMoveToolHandler.OnLeftDragEnd` が `TryTakeOriginOnlyDrag` を呼んでいなかった。`MovePivotCommand` として送るように直し（`MovePivotCommand` に `moveWithChildren` を足し、パネルの「子を一緒に移動」の値を運ぶ）、ドラッグ → Undo → Redo で全部の形の位置が戻る・やり直せることを確かめた。(c) ピボット移動ツールのドラッグ：画面に入口が無いので実行できない（左ペインのボタンは非表示で押したときの処理もつながっていない。`PolyLingPlayerViewerCore.Layout.cs` の `ToolPivotOffsetBtn`。`InteractionMode.PivotOffset` へ入る呼び出しは無い）。原点だけ移動は (b) の経路だけ。**白一色の件の原因と対処**：`TPoseConverter.BakeSkinnedVertices` が位置だけを焼き、法線（`Vertex.Normals`）を回していなかった（形とは無関係の既存の不具合。T ポーズ変換・この姿勢で確定も同じ経路）。スキンドの表示は保持した法線をそのまま使うので、焼く前の向きの法線で塗られていた。解除すると頂点が親のローカルへ戻り、表示が姿勢行列を法線にも掛けるので合っていた。ウェイトのある頂点の法線を位置と同じスキン行列（逆転置）で焼くようにし、`TPoseBackup.VertexNormals` に控えて Undo・元の姿勢への復元で戻す。90 度焼いて法線が (0,1,0) → (0,0,1)、Undo で (0,1,0)、Redo で (0,0,1)、キャプチャの陰影が合うことを確かめた。**残し**：`TPoseBackup` の法線の控えと形の控え（`ShapeSnapshots`）はファイルに保存していない（保存して読み直した後の「元の姿勢に戻す」では、法線と形の位置は戻らない）。最初の CSV 読み込み直後のキャプチャが 1 回だけ白一色になった（再読み込みでは再現せず、原因は未特定）。 |
| 3 | 完了 | 2026-11-15 | 足したもの：パネル `PlayerShapeSubPanel.cs`（パネル ID `shape`。左ペイン「ボーン・モーフ」の「形」＝`leftPane.shapeBtn`）、窓口（`IModelView.Shapes`・`CurrentShapeId`・`PreviousShapeId`、`ShapeView`）、コマンド `mirrorShape`（`ShapeOps.MirrorShape`）、`MorphMirrorOps.BuildSymmetryVertexMap` の位置の配列を受ける口。パネルはコマンドを送るだけ。**複製のコマンドは足していない**（`addShape` の `sourceShape` に元の形を渡すのが複製。パネルの「複製」は名前を「元の名前＋のコピー」にして送る）。左右反転の規則は 3.7。形のコマンドは画面に入口ができたので NoUi を外し、経路（`PLUiRoute`）と画面の操作の宣言を付けた（`queryShapes`・`queryShapeAudit` は NoUi のまま）。パネルの決まり：一覧の先頭の行は基本の形、選んでいる行＝今の形、名前・英語名・パネルの欄と複製・左右反転・削除は今の形（基本の形を除く）に働く。削除は確認の窓を出さない（Undo で戻せる。UI 自動操作の安全度は destructive）。MQO のボタンは置いていない（段 4）。確認（`SandBox/形テスト/形テスト.csv`、画面の操作＝`uiClick`・`uiSetValue` だけ）：一覧の行で笑いへ切り替え → 「直前の形と入れ替え」1 回で基本の形（キャプチャで確認）→ もう 1 回で笑い。左右反転（頂点 0 の動きが対称の頂点 1 へ入る。保存 CSV の `vsh` 行）、複製、基本の形から追加、今の見た目から追加、名前・英語名・パネルの変更、削除を行い、`queryShapes` と `vsh` 行で照合。Undo 6 回で名前の変更の前の状態（パネルの表示も追随）、Redo 6 回で同じ状態（`vsh` 行が一致）。`queryShapeAudit` 0、`queryProjectSyncDiff`（binary・csv）差 0、コンパイルエラー 0、`queryUiAutomationAudit` 全項目 0、`queryCommandAudit` は道具として出せない 3・分類が正典に無い 10 のまま。**残し**：`queryCommandAudit` の [参考]「実行のボタン」に `setCurrentShape`・`setShapeAttributes` が出る（行を選ぶ・欄を変えると確定する操作で、押すボタンが無い）。左右反転は、ミラーの関係に無いオブジェクトで対称の相手が見つからない頂点の動きを落とす（結果の `unmatchedVertices` に数える。パネルには出していない）。ミラーの対・表示ミラーを持つモデルでの左右反転は動かしていない（規則は元の形の写し）。大きく形を変える形の陰影は 3.13 のとおり（笑いの角は法線が基本の形のまま）。 |
| 4 | 完了 | 2026-11-15 | 足したもの：`ShapeSource`・`ShapeSourcePair`（`ShapeData.cs`）、`ShapeDef.Source`、`ModelContext.BaseShapeSource`（基本の形の読み込み元。計画では「形ごと」だけだったが、全体の MQO を読み直すのに要るので足した）、保存 3 形式（shapes.csv の `source` 行・JSON の `ShapeDefDTO.source`／`ModelDTO.baseShapeSource`・PLRM 版 15）、窓口（`ShapeView.SourcePath`・`SourcePairCount`、`IModelView.BaseShape`）、`MQO/ShapeMqoOps.cs`（対応付け・形へ読み込む・書き出す）、Undo の記録 `ShapeStateRecord`（形の一覧・読み込み元・今の形・全描画オブジェクトの位置と形の控えを前後で丸ごと。形を作る・切り替える・読むを 1 件）、コマンド `importShapeMqo`（`newShape` で作って読むまで 1 コマンド）・`reloadShapeMqo`・`exportShapeMqo`（`PanelCommand.ShapeMqo.cs`、`PlayerCommandDispatcher.ShapeMqo.cs`。経路と画面の操作を宣言。パスはコマンドの引数で直接渡せる）、「形」のパネルの MQO の区画（パス欄・「…」は UserOnly でパス欄を埋めるだけ・倍率・反転・差分を保つ・読み込む・新しい形を作って読み込む・再読み込み・書き出し先・書き出す。結果は `PlayerCommandDispatcher.LastShapeMqoSummary` を状態の欄へ）。作業空間を開いている間は 3 つとも断る。新しいコマンドからは `RemapMorphBasesAfterVertexChange` を呼ばない（旧モーフを書き換えると形の Undo で戻せない。既存の部分読み込みの呼び出しはそのまま。段 7 で撤去）。法線は再計算しない（3.13）。**計画に無かった直し**：既存の頂点位置差し替え（`MQOPartialImportOps.TransferVertexAttributes`）と部分書き出し（`MQOPartialExportOps.TransferToMQO`）は、モデル側が展開済み（PMX 由来）であることを前提に展開順で写していた。MQO から読んだモデルは展開しておらず（頂点の並びは MQO と同じ）、UV スロットが法線の計算で増えるため展開頂点数も合わない（`顔`：MQO 5262・モデル 5266）。このため `顔のみ.mqo` と全体の MQO のモデルは対応付けられず（既存の `matchMqoSourceByVertexCount` も 0 組）、組を手で渡しても別の頂点へ写していた。モデル側の頂点数が MQO と同じときは頂点番号で写すようにした（`MQOPartialImportOps.IsVertexWise`。展開済みのメッシュで頂点数が一致するのは全頂点が UV 1 個・孤立点なしのときだけで、そのときは展開順と同じ）。対応付けの頂点数の比べ方も同じ 2 通り（`ShapeMqoOps.Fits`）。確認（全体のモデル `_0N_モデル_Ref_id再修正.mqo`、全部の形の位置は保存 CSV の `vsh` 行を集計）：`顔のみ.mqo` を新しい形で読み、`顔` と `顔+`（MirrorSide）の 5096 頂点だけが形の位置（MQO×0.01・X 反転）になり、ほかのオブジェクトは基本の形のまま。Undo 1 回で形ごと消え、Redo で保存結果がバイト単位で一致。SandBox の写しを読んだ形を基本の形に切り替えてから、写しの 10 頂点を動かして `reloadShapeMqo` → その形へ切り替わって 20 頂点（ミラー側を含む）だけ変わり、ほかの形と基本の形は不変、組は覚えた組（remembered）。Undo で切り替え前・読み直し前に戻り、Redo で一致。決まらない組（同じ頂点数の `顔A`・`顔B`）と頂点数が合わないもの（`舌_1` の名前で 2548 頂点）は読まずに `unresolved`・`mismatched` に返る（組めるものが無いときは失敗にして同じ名前を出す）。名前が違って頂点数で 1 つに決まるもの（`顔X`）は vertexCount で組む。基本の形の差し替え：差分を保つ → 動いた控え 10192、`顔読み`・`作業` の差分は 1e-8 以内で保たれ、動いた頂点の数は 5096・5096 のまま。保たない → 形は絶対位置のまま、基本の形と同じになった控え 10116 を消し、動いた頂点の数は 76・0。保存して読み直し（CSV）後に `reloadShapeMqo` が動く。書き出しは読み直した写しと頂点がすべて一致。画面の操作（`uiSetValue`・`uiClick`）で新しい形を作って読み込む・再読み込み・書き出しを行い、キャプチャで顔だけが形の位置になることを見た。`queryShapeAudit` 0、コンパイルエラー 0、`queryUiAutomationAudit` 全項目 0、`queryCommandAudit` は道具として出せない 3・分類が正典に無い 10 のまま。`queryProjectSyncDiff`（binary・csv）は初め 34 だった（`materialReferences[].data.baseMapPath`。形とは無関係の既存の不具合：アセットでないテクスチャで `GetAssetPath` が空文字を返し、JSON は `""`、CSV・バイナリは読むときに null にしていた）。`MaterialDataConverter.GetTexturePath` で空を null にそろえ、形を読んだ状態で binary・csv とも差 0 を確かめた。**残し**：部分書き出しは位置を倍率で割って小数 6 桁で書くので、元が 4 桁の MQO を書き戻すと 2548 頂点のうち 38 頂点で最後の桁がずれる（例 7.4159 → 7.415901。既存の書き出しの丸め）。頂点番号で写すときは UV・ウェイトの書き戻しはしない。段 3 から持ち越した 3 件（`TPoseBackup` の法線と形の控えを保存していない、ミラーの対・表示ミラーを持つモデルの左右反転、`unmatchedVertices` をパネルに出していない）は段 4 で触った所に当たらず、そのまま。最初の CSV 読み込み直後の白一色は今回は再現しなかった。 |
| 5 | 完了 | 2026-11-15 | 足したもの：`ShapeBlendOps.cs`（鍵 `MorphTargetKey`、名前の解決 `Resolve`・`ExpandExpression`、差分 `CollectOffsets`、足し込み `ShapeBlendBuffer`）、`ShapeExportView.cs`（書き出しの写し）、`ShapeOps.GetSparseOffsets`・`GetLegacyMorphOffsets`、コマンド `addExpression`・`setExpressionShapes`（`PanelCommand.Expression.cs`。経路と画面の操作を宣言）・`queryDisplayedPositions`（NoUi）、窓口 `MorphExpressionView.Children`・`HasSameNameShape`・`MorphEntryView.ShadowedByShape`、ツールの窓口 `previewShapeWeight`。変えたもの：PMX（形 1 つ＝頂点モーフ 1 本。同じ名前の頂点の表情は形で置き換え、元の並びを保つ）、`HierarchyMorphBuilder`・`HierarchyBuilder`・`Vrm10SceneAssembler`・`FbxModelWriter`・`ModelMotionSampler`（鍵を形の番号へ。差分は `MorphShapeSlot.Offsets`）、試し表示 3 経路とモーション再生（1 系統。3.6）、表情パネル（3.11）、`start/apply/endMorphPreview`（頂点位置を書き換えず足し込みに。経路を宣言）、`MotionClipApplier.FindExpression`・`ExpandToVertexMorphs` は撤去（`ShapeBlendOps` へ）。決めたこと：同じ名前の旧モーフと形（3.5）、法線（3.13）。**計画に無かった直し**：プレファブの書き出しがブレンドシェイプを載せていなかった（3.9）。確認：立方体（形 2 つ、今の形＝目閉じ）を PMX・VRM・FBX・プレファブへ書き、PMX・VRM・FBX は読み戻した旧モーフの差分（v − mb）と形の差分（vsh 行）を頂点ごとに照合して食い違い最大 2.3e-08、基本の形で書かれていることを確認（読み戻しの基準位置＝基本の形）。プレファブはメッシュアセットのブレンドシェイプが形の差分と一致。全体のモデル `_0N_モデル_Ref_id再修正.mqo` に `顔のみ.mqo` を `importShapeMqo`（新しい形、今の形のまま）して PMX・VRM・FBX に書き、顔・顔+ の 2548 頂点ずつが基本の位置 1.2e-07 以内で対応、差分の食い違い最大 1.0e-08（PMX）・4.7e-10（VRM・FBX）、ほかのオブジェクトに旧モーフは無い。試し表示（画面の操作だけ：表情を作る・形を足す・行の重み 0.5・ウェイト 1 / 0.5）で表示位置が「基本 ＋ Σ 重み × 差分」に一致（`queryDisplayedPositions`）、始めたときに基本の形へ切り替わり Undo 1 回で目閉じに戻って足し込みも消える。モーション再生（表情トラック「混ぜ」＋形の名前「笑い」＋無い名前）で同じく一致、Undo 1 件。旧モーフだけのモデル（PMX を読んだまま）で、表情パネルの試し表示・オブジェクトリストのモーフ欄の試し表示（頂点位置は変わらない）・モーション再生・PMX/VRM/FBX の書き出しと読み戻し（差分一致）が動く。`queryShapeAudit` 0、`queryProjectSyncDiff`（binary・csv、全体のモデルに形を読んだ状態）差 0、3 つの csproj と Unity のコンパイルエラー 0、`queryUiAutomationAudit` 全項目 0、`queryCommandAudit` は道具として出せない 3・分類が正典に無い 10 のまま。**残し**：モディファイアを適用して書き出すとき、今の形が基本の形でなければ畳まれた結果の行は今の形の位置で出て形も持たない（3.14。段 7 の見直し）。VMD の直接適用（`VMDApplier.ApplyMorphWeight`）は空のまま（VMD の表情はクリップへ変換する経路で新しい解決を通る）。FBX のモーション書き出しの表情の重み（`ModelMotionSampler` の鍵）は、ボーンと Humanoid のある形付きモデルで動かしていない。段 4 から持ち越した残し（部分書き出しの小数 6 桁、頂点番号で写すときの UV・ウェイト、`TPoseBackup` の控えの保存、ミラーを持つモデルの左右反転と `unmatchedVertices`）は段 5 で触った所に当たらずそのまま。白一色のキャプチャは再現しなかった。 |
| 6 | 完了 | 2026-11-15 | 変えたもの：PMX・VRM・glTF・FBX の読み込みを形にした（3.9 の段 6 の実装）。読み込みの結果に `Shapes` を足し、`ImportCommands`・`PolyLingPlayerIO`・追加読み込みで移す。UV の形を入れた（3.3）：`ShapeKind`・`ShapeUV`・`Vertex.UVShapes`、`ShapeOps`（切り替え・写し・控え・点検）、`ShapeListRecord`・`ShapeModelState`、保存 3 形式（`vsu` 行・shapes.csv 版 1.2・`sui`/`suv`/`kind`・`MeshFieldFlags.UVShapes`・PLRM 版 16）、PMX の UV モーフの書き出し、ブレンドシェイプ・左右反転・MQO 読み込みからの除外、形のパネルの「（UV）」、`queryShapes` の `kinds`。`importMorphCsv`・`exportMorphCsv` を形の名前で読み書きするようにした。**決めたこと**：UV モーフを形にする（ゼロちゃんの UV モーフ 66 本を旧モーフで読むと隠しオブジェクトが増えるため）、追加 UV1〜4 のモーフは旧モーフのまま、VRM の重みがそろわないバインドは専用の形、ミラー側は基本の位置が鏡映で合う頂点だけ実体側から作り直す（3.9）。追加読み込みは形の番号の振り直し・改名・Undo を足した。確認（ゼロちゃん `_0_ぜろちゃんN_材質修正.pmx`）：オブジェクトのモーフ 0、形 249（頂点モーフ 183＋UV モーフ 66）、点検 0。書き戻した PMX はモーフ 272 本が元の並び・種類・件数で一致（PolyLing のメタモーフ 70 本が足される）、差分の値は 1.2e-6 以内。ただし足首・足爪・足首リング・スポーツぱんつのミラー側を実体側から作り直したため、5 本（あ_H・視線C・GD_・むち化B・_顎あけHL_E）で件数が 1 増え、値が最大 0.005（PMX 単位）変わった（PMX の値自体が左右非対称）。小さい切り出し（顔・まつ毛・眉の 7 材質、形 133・グループ 10）で、`queryProjectSyncDiff` binary・csv とも差 0（基本の形のときと UV の形に入っているとき）、UV の形に入ったまま書いた PMX が元と一致、UV の形の削除と Undo、複製（種類を引き継ぐ）、左右反転（ミラーの対は写し・unmatched 0、UV の形は断る）、追加読み込み（今の形が基本でない状態で形 +133・改名 133、点検 0、Undo で戻る）、BlendShapeSync CSV の書き出しと読み込み（重みや組が形と合わない 2 行は読まない）。段 5 の書き出しの読み戻し（`SandBox/段6/tools/cmp4.py`。形 ↔ 形）：cube の PMX・VRM・FBX は食い違い 3.0e-8 以内、big（`顔読み`）の PMX・VRM・FBX は 1.3e-7 以内（対応しない 8 頂点は面に使われない頂点で、動いていない）。ずん子の VRM（形 39・表情 24）を書いて読み戻し：表情は一致、形は 37 で差分一致（減った 2 つは差分 0 の形で、段 5 の書き出しがブレンドシェイプにしない）。表情パネルの画面の操作（表情を選ぶ・形を足す・行の重み 0.5・ウェイト 0.5・外す）と、モーション（グループ・形の名前・無い名前）で表示位置が「基本 ＋ Σ 重み × 差分」に一致。形のパネルで切り替え・入れ替え（UV の形を含む）をキャプチャで確認。3 つの csproj と Unity のコンパイルエラー 0、`queryUiAutomationAudit` 全項目 0、`queryCommandAudit` は道具として出せない 3・分類が正典に無い 10 のまま。**段 5 の残しのうち解消**：FBX のモーション書き出しの表情の重み（ずん子に形付きで書き、`aa`→あ が 0→100、`目閉じ` が 50 の曲線になる）。ミラーの対を持つモデルの左右反転（写しになり unmatched 0）。**残し**：glTF の書き出しの差分は形より最大 1.7e-5 ずれる（ファイルの値を直接読んで確認。読み込みと同じ変換器の VRM は 0 なので書き出し側。UniGLTF が Unity の Mesh から書く経路）。ゼロちゃん全体の `queryProjectSyncDiff` と大きい PMX の読み込みは MCP の応答が 60 秒で切れる（Unity 側は終わる）ので、小さい切り出しで確かめた。表示ミラーを持つモデルの左右反転・`unmatchedVertices` のパネル表示、部分書き出しの小数 6 桁、`TPoseBackup` の控えの保存、モディファイア適用時の書き出し、VMD の直接適用は触った所に当たらずそのまま。白一色のキャプチャは再現しなかった。 |
| 7 | 完了 | 2026-11-15 | **撤去したもの**：3.12 の全部（種類 Morph、`MorphBaseData`、`MorphParentIndex`、ミラー適用の欄、`MorphMeshEntry`、モーフの選択と選択カテゴリ Morph、`FindBaseMeshIndex`、`SyncAllMirrorMorphs`・`FindMirrorMorph`、関連の Undo 記録、`VertexDataTransferOps` の MorphBase、TPose の `MorphBasePositions`）、オブジェクトリストのモーフのタブ（ListClient も同じパネルなので同時に消えた）、選択セットのモーフのタブ、`PlayerMorphCreateSubPanel`、部分読み込みの「表情移植」と `RemapMorphBases`、旧モーフの書き出し・CSV の旧行、`ParameterLimits` の `MorphPreview.Weight`。**コマンド**：convertMeshToMorph・convertMorphToMesh・createMorphFromDiff・createMorphSet・setMorphEntryWeights・start/apply/endMorphPreview・expandMorphExpressionToModel・selectAllMorphs・deselectAllMorphs を撤去。足したもの（経路を宣言）：`setExpressionAttributes`・`deleteExpression`（setMorphExpressionAttributes・deleteMorphExpression の改名）、`previewShapes`・`endShapePreview`（形のパネルの試し表示の区画）、`extractShapeModel`（形のパネルの「今の形をモデルとして取り出す」）。`queryModelStructure` の `morphs` を `shapes`・`expressions` に替えた。**保存**：CSV・JSON は種類 Morph の行を含むデータを読まずにエラーを出す（`ModelSerializer.RetiredMorphMessage`）。旧欄は読まない。バイナリは PLRM 17・PLRS 12 に上げ、それより前は読まない。**追加 UV1〜4**：段 6 では旧モーフで残す決定だったが、旧モーフを撤去したので読み込みで捨てて警告し、グループの子からも外す。書き出しでも元の PMX から写さない（PolyLing は追加 UV を持たず、旧実装は差分を UV0 へ足していた）。**頂点の増減の制限**：`topologyLocked` は hasSkinWeights だけ。InvalidatesMorphs は押し出し・分離・結合・穴/辺群合わせ・ミラー実体化から外し、ブーリアン・2D ブーリアン・貼り付けに残す。利用シーン「モーフ・表情作成」は名前を保ち、topologyLocked と頂点の並びを隠す扱いを外し、明示コマンドを新しい名前へ。適用・書き出しは 3.14。**リモート**：オブジェクトの種別 morph と IsMorph・MorphName・MorphPanel を撤去、query `shapes` と push `shapeChanged` を足した。**計画に無かった変更**：試し表示を形のパネルから送る経路にした（表情パネルの試し表示はツールの窓口のまま）、`VMDApplier` の空の適用を撤去（VMD の表情はクリップへ変換する経路で当たる）、`previewShapes` が基本の形へ切り替えたときにパネルへ知らせる（知らせないと形のパネルの「今の形」が古いままだった）、サブディビジョンを外したときは形を落とす。**確認**：シナリオ（確認済みは `roboF_T5_SK__アホ毛_はしご厚み揺れボーン` の 1 本。入力の MQO が `データとツール/データ/` へ移っていたので引数で渡して最後まで流れた）、82 本のどれも撤去したコマンドを使わない（queryScenarios で Morph 0 件）。段 5 の書き出し（`SandBox/段7`、cmp4.py）：cube の PMX・VRM・FBX は 3.0e-8 以内、big（今の形＝顔読み）は 1.3e-7 以内（対応しない 8 頂点は段 6 と同じ）。small.pmx の書き戻しは段 6 の出力とバイト一致、`queryProjectSyncDiff` binary・csv 差 0、点検 0。zero.pmx の書き戻しはモーフ 342 本・差分 1.2e-6 以内（ただし PolyLing のメタモーフ 30 本の名前が `__PLM_顔肌+` → `__PLM_顔肌++` のように + が 1 つ増える。zero.pmx 自体がメタモーフを持つ PolyLing の書き出しで、ミラー側オブジェクトの名前に + が重なっている。原因は調べていない）。形（2 個、今の形＝目閉じ）付きの立方体で、処理列（ベベル）のすべて適用・サブディビジョン 1 個の適用：どの形でも適用前の結果と位置が一致、Undo・Redo で保存結果が一致。押し出し（頂点 +12）と Undo・Redo、点検 0。処理列を残したまま今の形＝笑いで PMX・VRM・FBX を書いて読み戻し、各形の位置が適用前の結果と 9e-8 以内。画面：形のパネルの試し表示（重み 0.5 で表示位置 = 基本＋0.5×差分）、表情パネルの作成・名前・削除と Undo、オブジェクトリストのタブ（Mesh・Bone・剛体・Joint）。3 つの csproj と Unity のコンパイルエラー 0、`queryUiAutomationAudit` 全項目 0、`queryCommandAudit` は道具として出せない 3・分類が正典に無い 10 のまま。**残し**：旧データ（種類 Morph を含む CSV・JSON、PLRM 16 以前）を読む変換は作っていない（元の PMX などから読み直す）。オブジェクトリストのパネルは右ペインの道具の区画が開いていると上へ押し込まれる（MCP から「閉じる」を押す ID が無い。既存）。 |
| 8 | 完了 | 2026-11-15 | **変えたもの（Python。C# は変えていない）**：`データとツール/ツール/poly_ling_py/polyling_io` の `format/shapes.py`（新規）・`format/csv_mesh.py`・`format/csv_project.py`・`format/__init__.py`・`blender/importer.py`・`blender/exporter.py`・`blender/meshread.py`・`blender/motion.py`・`blender/__init__.py`（中身は 3.10 の「段 8 の実装」）。文書：`PolyLing_外部連携の契約.md`（§3.6・§5.1・§7.2・§7.4・§7.5）、`PolyLing_機能の場所.md`（「ボーン・モーフ」の表：撤去した「モーフ生成・差分から」を消し、「モーフエクスプレッション編集」「形」の説明と言い換えを今の機能に。Blender の読み書きの行）、`MCP操作手順書.md`（モデル状態の `hasMorphs`・`topologyLocked`、形と表情のコマンド）。McpGuide（primer.md・hints.txt）には該当なし。**計画に無かった変更**：editorstate.csv の `selectedVertexMorphIndex`／`Name` を読み書きしないようにした（C# が段 7 で廃止済み。残すと旧の欄を書き戻す）。**確認**（作業環境の bpy 5.2.4。手元の Blender は MCP から動かせない）：PolyLing で今の C# が保存したもの（`SandBox/段8`：cube〔形 2・今の形＝目閉じ〕、small.pmx を読んで保存した small〔形 133・うち UV 33、表情 10〕と、その今の形を UV の形・位置の形にしたもの、`.mfproj` 2 つ）を読んで書き（`SandBox/段8/bl_shapes.py`、結果 `bl_out/bl_shapes.json`）：全部の形での位置・UV（基本の形を含む）が全頂点で一致（cube 167・small 14886 頂点、食い違い 0）、Basis が基本の形（0 件の違い）、形の一覧・今の形・表情が一致。small の CSV 3 つは 9 ファイルとも、`.mfproj` 2 つは丸ごとバイト一致。cube は `Model.mesh.csv` だけ違い、`vsu` の番号 0 の行が 167 行増える（段 5 の CSV を読んだまま保存したもので、今の形が 0 以外なのに UV の番号 0 の控えが無かった。書き出しは今の規則〔`SwitchMesh`〕どおりに付ける。内容は同じ）。`cmp4.py`：cube は食い違い 0、small は 1（まばたき_MB 1.7e-5。元どうしを比べても同じ 1 で、同じ位置にある頂点の対応の取り方による）。編集（`bl_edit.py`）：シェイプキーの頂点を動かす・名前を変える・新しく足すを書き、形の位置・名前の変更・新しい形（番号 3）になった。書いたものを PolyLing で読み、`queryShapeAudit` 0、`queryProjectSyncDiff`（binary・csv）差 0（cube・UV の形が今の形の small・位置の形が今の形の small・`.mfproj`・編集した cube）。旧モーフを含む CSV（`SandBox/pytest/alicia2`）は「旧モーフ…を 60 個含む保存データは読めません…」で止まる。表情の割り当て：small のグループの表情 10 個が子の形のシェイプキー（顔肌と顔肌+）へ当たり、形の名前「あ」も当たる。C# を変えていないので、コンパイル・`queryUiAutomationAudit`・`queryCommandAudit` は回していない。**インストール先**：`C:\Users\dhag\AppData\Roaming\Blender Foundation\Blender\5.2\scripts\addons\polyling_io` はこの会話から見られない（保護されたフォルダで許可できない）。最後の入れ替えは `verify1031/bl_reinstall.json`（2026-10-30）で、その後に手元は 10-31・11-01 にコミットがあるので、インストール先はそれより古い。入れ替えと比較は `SandBox/段8/bl_install.py` を Blender で実行する（違いを `bl_install.json` に書いてから手元の版を入れる。比較の基準は段 8 の前の版 `SandBox/段8/polyling_io_before`）。**段 7 までの残しのうち触った所**：旧データ（種類 Morph を含む CSV・JSON）を読む変換は作っていない（段 7）→ アドオンも同じく読まずにエラー。**Windows の Blender（同日、追加）**：使っている Blender は 5.3.0（ストア版。アドオンは `…\Blender\5.3\scripts\addons`。5.2 のフォルダは読まない。5.3 には PolyLing IO が入っていなかった）。`bl_install.py` で手元の版を入れて有効にし、画面から `bl_shapes_gui.py`（`bl_shapes.py` を画面で流す入口。ストア版の exe はコマンドから直接起動できない）で同じ 6 件を流した（結果 `SandBox/段8/bl_win/bl_shapes.json`）：全件で形の位置・UV・ほかの欄の食い違い 0、Basis が基本の形、モデルのフォルダの中身は作業環境（bpy 5.2.4）の出力とバイト一致、`.mfproj` は元とバイト一致。**MCP の道具を足した（同日）**：`blender_run`・`blender_status`（MCP サーバの `BlenderRunTools.cs`、起動引数 `--blender`。MCP操作手順書.md の 1-8）。MCP サーバのコンパイルは通った（エラーは動いている exe をコピーできない 2 件だけ）。再起動のあと、ストア版には起動名が無かったので、操作者がストア版の中身を `C:\Program Files\Blender Foundation\Blender 5.3` へ写した。`blender_run` で `bl_shapes.py` を流し（アドオンは手元の `poly_ling_py` を渡す）、6 件とも同じ結果（食い違い 0、Basis が基本の形、small の CSV・`.mfproj` はバイト一致。出力 `SandBox/段8/bl_mcp`、ログ `SandBox/_blender`）、189 秒で終了コード 0。**残し**：シェイプキーの基準が Basis でないものも、キーの位置をそのまま形にする（警告）。Blender で消したシェイプキーの形は一覧から消さない。途中のシェイプキーを消したあとに名前を変えたものは引けず、新しい形になる。頂点数・面のつながりを変えると UV の形の控えは外れる。名前が 63 バイトを超える形は Blender がシェイプキーの名前を詰める（結び付けは `polyling_shape_keys` が持つので往復は崩れない。警告を出す）。 |

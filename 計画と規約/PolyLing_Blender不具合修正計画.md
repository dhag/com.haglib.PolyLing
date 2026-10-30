# PolyLing Blender 連携 不具合修正計画

Blender 連携の作業中に見つけた不具合を直す。
出どころ：`PolyLing_Blender入出力計画.md` の 6（1〜14）、`PolyLing_Blender連携計画.md` の 6（段 ③ の受けたクリップ、「往復のずれ」の GetHumanPose だけでマッスルにしている箇所）。
項目番号は入出力計画 6 の番号をそのまま使う。連携計画 6 の 2 件は A・B と呼ぶ。

作成：2026-10-20

---

## 0. 進め方の決まり

- この文書が正本。会話が切れたら、次はこの文書だけを読んで続きから始める。
- 1 段＝1 回の会話で閉じる。段の終わりに「完了条件」をすべて満たし、チェック欄と「4. 進捗」を更新してから終える。
- 承認はこの計画書に対して 1 回（2026-10-20 承認済み）。段の途中で止まって承認を取りに行かない。
- 段の範囲外で見つけた問題は手を広げず「6. 残件」に書くだけにする。
- 推測で書かない。C# と Python で同じ規則を持つものは C# の該当行を読んでから同じ規則で書く。
- 根本原因を直す。読み書きの両側（C# と Python）で同じ規則を持つものは両方を揃える。
- C# の完了は 4 csproj（PolyLing.Runtime・PolyLing.Editor・PolyLing.Vrm10・Assembly-CSharp）のコンパイル 0 件。Python は作業環境の bpy 4.2（入出力計画の段 ②〜④ と同じ）で確かめる。
- 試験の出力は `SandBox/BugFix/<段>/` に置く。キャプチャは `SandBox/Captures/BugFix/<段>/`。
- 範囲外（載せない）：VRM Add-on の挙動、正準骨格の親指 Proximal の残差、旧骨格で作った既存の `.plmotion` の bakedBones。

---

## 1. 着手時点の事実（2026-10-20 にソースを読み直して確認）

### 1（materialprops.csv の行が読込で捨てられる）— 今も起きる
- 書き：`CsvModelSerializer.ModelCsv.cs:383-385` は最後の欄にテクスチャのパスを書く。空なら行末が `,` になる。
- 分け方：`CsvModelSerializer.Texture.cs:164-194` の `Split` は行末の `,` で空の欄を作らない（189-192 の非引用欄の処理）ので 9 欄になる。
- 読み：`CsvModelSerializer.ModelCsv.cs:400` が `cols.Length < 10` で捨てる。
- 再現（今回）：A4 を読み込んでいた PolyLing で `saveProjectCsv` した `BugFix/survey/alicia_vrm/Zunko_MMD/materialprops.csv` は 40 バイト（見出し行だけ）。元の `SandBox/bintest/A4/Zunko_MMD/materialprops.csv` は 3260 バイト。
- 同じ分け方の写しが他に 4 つある（`CsvProjectSerializer.cs:558`、`ObjectGroupCsv.cs:429`、`MeshRenameCsvHelper.cs:221`、`MeshSelSetCsvHelper.cs:156`）。
- Python（`format/csv_project.py:278-296`）は 9 欄以上を読むので落とさない。

### 2（名前モードの CSV に mirrorGeometryDerived を書かない）— 今も起きる
- C#：`CsvMeshSerializer.Write.cs:64-72` は索引モードの側だけで書く。読み（`CsvMeshSerializer.Read.cs:125-127`）は両方のモードで読む。
- Python：`format/csv_mesh.py:702-706` も C# と同じ書き方。
- C# で名前モードを書く呼び出し元は Runtime・Editor に無い（`useNameBased` を true で渡す箇所が無い）。名前モードを書くのは Python（エクスポートの「名前で参照」）だけで、そこで失われる。

### 3（中身が空になっても消さない CSV）— 今も起きる
- `CsvModelSerializer.cs` の `SaveModel`：中身があるときだけ書き、無いときは古いファイルを残す。humanoid.export（143）・morphgroups（147）・meshselsets（154）・objectgroups（158）・mirrorpairs（167）・editorstate（171）・workplane（175）・tposebackup（182）・springbonegroups（186）。
- 消す作りのもの（datastore の `WriteOrDeleteDataStoreCsv`、164）と、読みはファイルがあれば読む（368-441）ので、同じフォルダへ保存し直すと古い中身が戻る。
- Python は空なら消す（`_write_or_delete`）。

### 4（tposebackup.csv の null 判定）— 今も起きる
- `CsvModelSerializer.EditorCsv.cs:425-426`：boneRot と vtxPos が両方 0 件なら、worldMat・bindPose があっても null を返す。
- .mfproj（`ModelSerializer_Vrm.cs:721-754` の `FromTPoseBackupDTO`）は DTO があれば中身が空でも null にしない。CSV と JSON で食い違う。
- Python（`format/csv_project.py:513-545`）はどれか 1 件あれば残す（C# とも JSON とも違う）。

### 5（springbonegroups.csv の空の名前）— 今も起きる
- 書き：`CsvModelSerializer.VrmCsv.cs:35-36` は `Esc("")` ＝空文字の行を書く。読み：同 46 行目で空行として飛ばす。後ろのグループの番号が 1 つずつ前にずれる（per-bone の grp 番号はこの並びへの参照、同 25-27）。
- Python：読み `format/csv_project.py:547-550`、書き 1178-1181。同じ形式。

### 6（スキンドメッシュのウェイトの無い頂点の行列）— 今も起きる
- 描画：`UnifiedBufferManager_Update.cs:85-105`。スキンド種別（`IsSkinned`）のメッシュの行はバインド表示で単位、通常は `SkinningMatrix`（`MeshContext.Transform.cs:472`、WorldMatrix·BindPose）。ウェイトの無い頂点はこの自分の行を使う。
- 編集：`MeshContext.Transform.cs:553-555` の `VertexMatrix` はウェイトの無い頂点に、種別によらず BindWorldMatrix／DisplayWorldMatrix を返す。
- 食い違うのは、通常表示でメッシュ自身の BindPose が単位でないとき、バインド表示で BindWorldMatrix が単位でないとき。行列の規則が描画と編集の 2 か所に書かれていることが原因。
- Blender 側（`blender/importer.py`）はこの頂点を Armature 修飾子のまま動かさない。

### 7（.mfproj を読んで CSV に保存すると行が足りない）— 原因を特定
- .mfproj の読込は `PolyLingPlayerViewerCore.Import.cs:897` → `ProjectSerializer.ToProjectContext`（`ProjectSerializer.cs:346`）→ `ModelSerializer.ToModelContext`（`ModelSerializer_ModelContext.cs:202`）。
- `ToModelContext` は 221-279 でメッシュごとの復元を自前で書いており、`ToMeshContext`（同 528-615）が呼ぶ 590-612 の復元（IK・IK リンク・BindPose・ボーンのモデル回転・剛体・ジョイント・スプリングボーン・humanBodyBone・MirrorBoneIndex・Humanoid 可動域・VRM 一人称・VRM 制約）を 1 つも呼ばない。
- そのため bone.csv の humanBodyBone・bindPose、pmx_physics.csv の rigidBody、humanoid.export.csv（humanBodyBone から作る）が欠ける（記録の 4 ファイル・17380 行）。同じ理由で IK・ジョイント・スプリングボーン・可動域・VRM 一人称・VRM 制約も .mfproj の読込で失われる。

### 8（Zunko_MMD の Spine の割り当て）— 今も起きる
- `HumanoidBoneMapping.cs:403-426` の `FindBoneByAliases` は別名ごとに「完全一致 → 部分一致」を回す。冒頭の注記（392 行）は「完全一致 → 部分一致」の優先度と書いており、実装と食い違う。
- Spine の別名は `{ "Spine", "上半身", "背骨" }`（227 行）。A4 の Zunko_MMD には「上半身」（`model.csv:10`、索引 6）があるが、最初の別名 "Spine" の部分一致で「kimono_spine_02.x」（同 104 行、索引 100）が先に当たる。
- 呼び出し：`UnityClipApplier.cs:609`（再生）、`HumanoidBoneMapping.cs:472・517・611`（自動割り当て・CSV 読込）。
- Python：`format/humanoid.py:211-224` が同じ規則。

### 9（グループの表情）— 今も起きる。原因は 2 つ
- 保存：`MorphExpression.GroupChildren`（`MorphExpression.cs:132`）は DTO（`MorphExpressionDTO.cs:152-176`）・CSV（`CsvModelSerializer.ModelCsv.cs:493-519`）・転送（`RemoteProgressiveSerializer.cs:179-191`）のどれにも無い。保存して読み直すと消え、PMX 書き出し（`PMXExporter.Morph.cs:302-304`）はそのグループモーフを作らない。
- 再生：`MotionClipApplier.cs:451-457` はグループを `GroupChildren` の名前だけで辿る。一方、取り込みはグループの子のモーフメッシュを重み付きで `MeshEntries` に展開して持ち（PMX：`PMXImporter.Morph.cs:191-197`、VRM：`VrmToPolyLingConverter.cs:1480`）、子の表情そのものは表情一覧から外す（VRM：1446-1447、PMX：同 212 以降）。そのため保存しなくても、子の名前が一覧に無く何も動かない。
- モーフのパネルの試し当て（`MorphExpressionHandler.cs:113` → `MorphPreviewState.cs:121-136`）は種類によらず `MeshEntries` を使う。再生だけ規則が違う。
- Python：`blender/motion.py:46-49` が C# の再生と同じ規則（グループは何にも当たらない）。

### 10（AliciaSolid が白一色）— 原因を特定
- A4 の AliciaSolid の `materials.csv` はどの材質もテクスチャのパス欄が空で、`textures` フォルダが無い（元にした `データとツール/だいじなデータ/…/参考データ（頂点IDなど）/AliciaSolid` も同じ）。保存データに画像が無い。
- 再現（今回）：`importVrmFile`（AliciaSolid.vrm、`settings.extractTextures` = false）→ `saveProjectCsv`（`SandBox/BugFix/survey/alicia_noextract`）→ `loadProjectCsv` で白一色（`SandBox/Captures/BugFix/survey/alicia_noextract_reload_0001.png`）。保存したフォルダには `textures` に 6 枚あり、`materials.csv` は A4 と同じくパス欄が空（2357 バイト、A4 と同じ大きさ）。extractTextures が既定（オン）なら材質にパスが入り、写る（`alicia_vrm_import_0001.png`）。
- ~~原因：保存の `AddFromMaterial` が画像を書くが材質との対応を残さない~~（段 ① で誤りと分かった。下の訂正）。
- 【段 ① で訂正】上の再現で `textures` に 6 枚あったのは、同じプロジェクトに同名のモデル「AliciaSolid」が複数あり（先に extractTextures オンで読んだもの）、CSV の保存で同じモデルフォルダへ重ね書きされたため。Play をやり直した状態で extractTextures = false だけを読むと、材質に画像は無く `textures` も空（`SandBox/BugFix/s1/after/alicia_noextract`）。取り込みの設定の説明（`PlayerImportSubPanel.cs:947-948`）どおり、切ると材質にテクスチャは付かない。
- 結論：A4 の AliciaSolid が白いのは、保存データに画像が無いため（テクスチャを付けずに読んだものを保存したデータ）。コードの不具合ではない。白一色（陰の色も無い）なのは 1 の materialprops の欠落が重なっていたため（1 は段 ① で直した）。
- 別に見つけた不具合：同名のモデルが複数あると CSV の保存で同じモデルフォルダへ重ね書きされる（6 に記録）。

### 11（シェイプキーの名前を変えると形が失われる）— 今も起きる
- 取り込み（`blender/importer.py:293-315`）はモーフとシェイプキーを名前（`polyling_morphs` の `shapeKey`）だけで結ぶ。書き出し（`blender/exporter.py:214-223`）と表情（`blender/motion.py:539-540・558`）は名前で引くので、名前を変えると見つからず差分 0 になる。
- Blender のシェイプキー（KeyBlock）は ID ではないのでカスタムプロパティを持てない。結び付けに使えるのは名前と並び順だけ。

### 12（.mfproj 由来の CSV で相対パスのテクスチャが写らない）— 今も起きる
- `format/csv_project.py:1286-1303` は `_unityProjectRoot` を根にしたパスも探すが、`_unityProjectRoot` を入れる箇所がどこにも無い（1289 行が唯一の出現）。
- 表示（`blender/materials.py:39-58` の `resolve_texture`）も候補に Unity プロジェクトの根を持たない。C# は `Assets/…` を Unity プロジェクトの根から探す（`ModelTextureTransfer.cs:225-233`）。

### 13（トラックごとにキー時刻が違う bakedBones）— 今も起きる
- 読込（`blender/motion.py:625`）は正準骨格の全トラックのキー時刻の和集合を、駆動するどのボーンにも打つ（祖先のキーで子の Blender の回転が変わるので和集合が要る）。
- 書き出し（同 993-997）はボーンのキーの時刻がそのトラックの元のキー時刻と同じときだけ元のトラックを使う。和集合を打ったボーンは一致しないので格子で標本化される。

### 14（motion.py の注記の食い違い）— 今も起きる
- 冒頭の注記 30 行目は「正準の親で割り当てのある一番近い祖先」、実装（978-981 の `local_of`）は正準骨格の親（`CANON_PARENT`）そのもの。`Rig.canon_parent`（497-504）は呼び出し元が無い。

### A（回転を GetHumanPose だけでマッスルにしている箇所）— 今もある。誤差は未計測
- `VmdMotionBake.cs:152`（正準骨格 `CanonHumanPoseRig` に回転を写して GetHumanPose）、`MediaPipePoseSolver.cs:304`（同じく正準骨格）、`UnityClipExportWindow.cs:453・754`（Editor。モデル自身の Avatar で GetHumanPose）。
- RefineMuscles を通しているのは `VrmaCodec.Read.cs:462` と `CanonBoneMotion.cs:623` だけ。

### B（ブリッジから受けたクリップをファイルにできない）— 今も起きる
- パネル（`PlayerMotionClipTestSubPanel.cs`）は自分の `_filePath` を持ち、JSON保存（704-706）・VRMA 書き出し（842-845）・BVH 書き出し（916）・995 行の書き出し・読み直し（1115）はどれも元ファイルのパスをコマンドへ渡す。受けたクリップは `AdoptExternalClip`（671-680）で `_filePath` が null になり、全部使えない。
- 窓口 `motionClip` も元ファイルのパス（`SourcePath`、`PlayerCommandDispatcher.Motion.cs:95`）を持っており、パネルと二重に持っている。

---

## 2. 決定事項（項目ごとの直し方）

すべて「直す」。直さない部分は理由を付けて 6 に書く。

### 2.1 CSV・.mfproj・転送の保存形式（C#）
- **1**：`CsvModelSerializer` の `Split`（Texture.cs）を、行末の `,` の後ろに空の欄を作る分け方に直す（CSV の決まりどおり）。この `Split` を使う `CsvModelSerializer.*.cs` の読みを全部見て、欄の数で分岐するものの結果が変わらないことを確かめる。同じ分け方の写し 4 つは、行末が空になる行を読むかを段 ① で調べ、読むものがあれば同じ段で同じく直し、無ければ 6 に書く。Python は 9 欄以上を読むので変えない（注記だけ直す）。
- **2**：`mirrorGeometryDerived` を両方のモードで書く（C# `CsvMeshSerializer.Write.cs`、Python `format/csv_mesh.py`）。
- **3**：1 の 9 ファイルを、中身が無ければ消す（datastore と同じ `WriteOrDelete…` の形）。editorstate・workplane は呼び出し元が渡さないときも消す（ファイルの有無と中身を一致させる）。
- **4**：CSV の tposebackup は「ファイルがある ⇔ TPoseBackup がある」にする（.mfproj と同じ）。書きは null で消す・null でなければ空でも見出しだけ書く。読みは中身によらず null にしない。Python も同じにする。
- **5**：空の名前を `""`（引用した空文字）として書く。C# の `Split`・Python の `split_line` はこれを空の欄 1 つとして読むので、読みは変えない。C# と Python の書きを揃える。
- **7**：`ModelSerializer.ToModelContext` のメッシュごとの復元（221-279）を `ToMeshContext(meshDTO, true)` の呼び出しに置き換え、復元の規則を 1 か所にする。
- **9（保存）**：`GroupChildren` を保存する。DTO は `MorphExpressionDTO.groupChildren`（`[{name, weight}]`、無ければ空）。CSV は morphgroups.csv の行のエントリの後ろに `,<子の数>,<名前>:<重み>…` を足す（名前は Esc、`:` は最後のものを区切りとする。欄が無い旧ファイルは 0 件）。転送は PLRM の付帯を v10 に上げて表情ごとに子の一覧を書く。Python の CSV も同じ形で読み書きする（.mfproj は Python が知らない欄も残すので変えない）。
- **10**：【段 ① で変更】直さない（コードの不具合ではない。1 の 10 の訂正）。段 ① で一度入れた「パス欄の無い画像の保存名を materials.csv に書く」変更は、前提が誤りだったので取り消した。

### 2.2 モーション・表情・表示（C#）
- **6**：描画の行列表の規則を `MeshContext` の 1 つのメソッド（メッシュ自身の行の行列、`showBindPose` を取る）にまとめ、`UnifiedBufferManager.UpdateTransformMatrices` と `VertexMatrix` のウェイトの無い頂点の分岐の両方がそれを使う。
- **8**：`FindBoneByAliases` を「全別名で完全一致 → 全別名で部分一致」の 2 巡にする（注記どおり）。C# と Python（`format/humanoid.py`）を揃える。
- **9（再生）**：`ExpandToVertexMorphs` のグループを、パネルの試し当てと同じく `MeshEntries` で展開する（`GroupChildren` は PMX 書き出しの構造のためのもので、再生には使わない）。Python（`blender/motion.py` の表情の割り当て）も同じ規則にする。
- **A**：段 ② の最初に、3 か所それぞれで「GetHumanPose のマッスルを SetHumanPose で戻した回転」と元の回転の差を倍精度の角度で測る（一時コード。測ったら消す）。差が 0.1°（VRMA 整備計画の bakedBones の許容）を超える箇所は、`VrmaCodec.Read.cs` と同じく `CanonBoneMotion.RefineMuscles` を通す。`UnityClipExportWindow` はモデル自身の Avatar で取っているので、超える場合は Humanoid ボーンのワールド回転を正準骨格へ写す（`VmdMotionBake` と同じ写し方）を経て通す。超えない箇所は直さず、測った値を 4 と 6 に書く。【段 ② で変更】VmdMotionBake は 0.1° を大きく超えたが、RefineMuscles を通しても 0.1° 以下にならない（骨によっては悪くなる）ので、通さずに 6 へ回した。MediaPipePoseSolver は同じ変換なので同じ扱い。
- **B**：元ファイルのパスの持ち主を窓口 `motionClip`（`SourcePath`）だけにし、パネルの `_filePath` はそれを読む。コマンド `saveMotionClipJson`（filePath）を足し、窓口のクリップを `MotionClipSerializer.ToSaveJson`（`queryMotionClipJson` と同じ中身）で作業フォルダへ書いて、窓口の `SourcePath` をそのファイル・種類を PolyLing モーションにする。パネルの JSON保存は、元ファイルが無いクリップのときこのコマンドを使う。保存後は読み直し・VRMA・BVH 書き出しが使える。契約 §3.7 に足す。

### 2.3 Python（polyling_io）
- **2・4・5・8・9**：2.1・2.2 のとおり C# と揃える。
- **6**：直さない（Blender の Armature 修飾子では、ウェイトの無い頂点だけにメッシュ自身の行列を掛ける表し方が無い）。取り込みで、スキンド種別のメッシュにウェイトの無い頂点があり、そのメッシュの SkinningMatrix（WorldMatrix·BindPose）が単位でないときに警告を出す。6 に残す。
- **11**：シェイプキーを引く処理を 1 つの関数にまとめ、書き出しと表情の割り当ての両方が使う。規則：名前で引く。無ければ、取り込み時のシェイプキーの数と今の数が同じときに限り、取り込み時の並び順の位置にあるシェイプキーを、その名前がどのモーフの名前でもないなら名前を変えたものとして使い、警告「シェイプキー 旧名 → 新名 を名前の変更として扱った」を出す。数が違えば今と同じく差分 0 と警告。取り込みで並び順の位置を `polyling_morphs` に残す。
- **12**：.mfproj の読込（`format`）で `_unityProjectRoot` を入れる：インポートの設定「Unity プロジェクトのフォルダ」（空なら自動）、自動は .mfproj のフォルダから上へたどって `Assets` と `ProjectSettings` を両方持つ最初のフォルダ。CSV の textures の写しと `resolve_texture` の候補の両方で使う。
- **13**：読込でアクションに打ったキー時刻（和集合）を `polyling_key_times` に残す。書き出しで、ボーンのキーの時刻がこれと同じなら、そのトラックは「Blender から作った値」と「元のトラックをその時刻で補間した値」（読込と同じ補間）を比べ、全時刻で許容差のうちなら元のトラックを書く。
- **14**：注記を実装（正準骨格の親）に合わせて直し、`Rig.canon_parent` を消す。
- **1**：注記（`csv_project.py:278`）を C# の修正後の状態に直す。

---

## 3. 段と完了条件

### 段 ① C#：CSV・.mfproj・転送の保存形式（1・2・3・4・5・7・9 の保存・10）
対象：`FolderSerializer/CsvModelSerializer*.cs`・`CsvMeshSerializer.Write.cs`、`ModelSerializer_ModelContext.cs`、`MorphExpressionDTO.cs`（と変換）、`RemoteProgressiveSerializer*.cs`、`ModelTextureTransfer.cs`、必要なら `Split` の写し 4 つ
- [x] 2.1 の全項目（10 は直さないに変更）。
- 完了条件（直す前に下の手順を流して失敗を記録し、直した後に通す）：
  - 1：`loadProjectCsv`（`SandBox/bintest/A4/project.csv`）→ `saveProjectCsv`（`SandBox/BugFix/s1/a4_rt`）→ `queryCsvFolderDiff`（元と）で materialprops.csv の差が 0。
  - 3・4・5：A4 を読み、表情・揃え・ミラー対・T ポーズ控え・コライダーグループ名を消す操作（無ければ `SandBox/BugFix/s1/` に用意した CSV を読む）→ 同じフォルダへ保存 → 読み直して中身が空。tposebackup は worldMat・bindPose だけの CSV を読み・保存して行が残る。springbonegroups は 2 番目が空の名前の 3 件を読み・保存して 3 件・並びが同じ。
  - 2：Python で書いた名前モードの CSV（mirrorGeometryDerived が True のメッシュを含む）を読み・索引モードで保存して True が残る。
  - 7：A4 を CSV から読んで `saveProjectFile`（`SandBox/BugFix/s1/A4.mfproj`）→ `loadProjectFile` → `saveProjectCsv`（`a4_mf`）を、CSV から読んで保存したもの（`a4_rt`）と `queryCsvFolderDiff` で差 0。
  - 9：グループの表情を持つモデル（A4 の AliciaSolid）を CSV・.mfproj・転送のバイナリ（`saveProjectBinary`・`loadProjectBinary`）で保存して読み直し、`GroupChildren` の名前と重みが一致。
  - ~~10~~：直さないに変更したので外す（1 の 10 の訂正）。
  - 4 csproj のコンパイル 0。A4 の正面キャプチャが直す前と同じ。

### 段 ② C#：モーション・表情・表示（6・8・9 の再生・A・B）
対象：`MeshContext.Transform.cs`、`UnifiedBufferManager_Update.cs`、`HumanoidBoneMapping.cs`、`MotionClipApplier.cs`、A の 3 か所（測った結果による）、`PanelCommand.Motion.cs`・`PlayerCommandDispatcher.Motion.cs`・`MotionClipHandler.cs`・`PlayerMotionClipTestSubPanel.cs`・`PolyLingPlayerViewerCore.CreateCommands.cs`、契約 §3.7
- [x] 2.2 の全項目（A は測った結果、詰めても完了条件を満たさないので直さず 6 へ）。
- 完了条件：
  - 6：スキンド種別のメッシュで、メッシュ自身の BindPose を単位でない値にしたモデル（`SandBox/BugFix/s2/` に用意）のウェイトの無い頂点を、頂点移動のコマンド（入口は段 ② で確かめる）で画面上の目印の位置へ動かし、キャプチャで頂点が目印に重なる（直す前はメッシュ自身の BindPose の分だけずれる）。通常表示とバインド表示の両方。
  - 8：A4 の Zunko_MMD で `pl_s4.plmotion.json` を窓口 `motionClip` に読み、Spine の割り当てが「上半身」（直す前は kimono_spine_02.x）。Zunko・Alicia・RoboFArms の自動割り当て（`AutoMapFromEmbeddedCSV`）を直す前後で比べ、変わった行を 4 に全部書く。
  - 9：A4 の AliciaSolid で表情 `aa` だけを持つモーションを当てると口が動く（キャプチャ。直す前は動かない）。
  - A：測った値を 4 に書き、0.1° を超えた箇所は直した後に同じ測り方で 0.1° 以下。
  - B：`loadMotionClipJson`（fresh_Z の中身）→ `saveMotionClipJson`（`SandBox/BugFix/s2/ext.plmotion.json`）の中身が `queryMotionClipJson` とバイト一致。続けて `convertMotionToVrma`・`exportMotionJson` がそのファイルで成功し、窓口の `sourcePath` がそのファイル。直す前はコマンドが無い。
  - 4 csproj のコンパイル 0。

### 段 ③ Python：polyling_io（2・4・5・6 の警告・8・9・11・12・13・14・1 の注記）
対象：`データとツール/poly_ling_py/polyling_io/format/`（csv_mesh.py・csv_project.py・humanoid.py）、`blender/`（importer.py・exporter.py・materials.py・motion.py・`__init__.py`）
- [x] 2.3 の全項目（12 の再現は RoboFArms_cs.mfproj にテクスチャのパスが無いので作った試験ファイルで行った。4 の段 ③）。
- 完了条件（作業環境の bpy 4.2。直す前の失敗を記録してから直す）：
  - 2・4・5・9：段 ① の C# の出力と、同じデータを Python で読んで書いたものが行で一致（A4 と段 ① の試験 CSV）。
  - 8：Zunko_MMD の自動割り当てで Spine が「上半身」。
  - 11：A4 を取り込み、シェイプキー「あ」の名前を変えて書き出すと、そのモーフの頂点が元と一致し警告に旧名 → 新名が出る。表情「あ」のトラックがそのシェイプキーに当たる。
  - 12：`SandBox/BlenderIO/RoboFArms_cs.mfproj` を読んで CSV に書くと `Assets/…` のテクスチャが textures に写る。
  - 13：トラックごとにキー時刻が違う .plmotion.json（`SandBox/BugFix/s3/` に C# で作る）を読んでそのまま書くとバイト一致。入出力計画の段 ④ の 5 本も引き続きバイト一致。
  - 入出力計画の段 ①・③ の往復の検査（A4・RoboFArms・T5）が引き続き一致。

### 段 ④ 文書
対象：`PolyLing_外部連携の契約.md`（§3.7・§5.1・§7）、`PolyLing_Blender入出力計画.md`・`PolyLing_Blender連携計画.md` の 6（この計画へ移したことの記載）、`PolyLing_機能の場所.md`（増えたコマンド・設定があれば）
- [x] 段 ①〜③ で変えた規則（グループの表情、シェイプキーの引き方、Unity プロジェクトのフォルダ、saveMotionClipJson、morphgroups.csv の欄、springbonegroups.csv の空の名前）。
- 完了条件：記載が実装と 1 項目ずつ一致。

---

## 4. 進捗

| 段 | 状態 | 日付 | メモ |
|---|---|---|---|
| ① | 完了 | 2026-10-20 | **直したもの**：1 `CsvModelSerializer.Texture.cs` の `Split`（行末の `,` の後ろに空の欄を作る。この `Split` を使う読みは全部、欄の数で有無を見るか件数で範囲を決めているので結果は変わらないことを確かめた）。2 `CsvMeshSerializer.Write.cs`（mirrorGeometryDerived を両モードで書く）。3 `CsvModelSerializer.cs`（9 ファイルを中身が無ければ消す。消す処理は `DeleteIfExists`）。4 `CsvModelSerializer.cs`（TPoseBackup が null なら消す・あれば空でも書く）と `.EditorCsv.cs`（読みで null にしない）。5 `CsvModelSerializer.VrmCsv.cs`（空の名前を `""`）。7 `ModelSerializer_ModelContext.cs`（`ToModelContext` のメッシュ復元を `ToMeshContext(meshDTO, true)` に置き換え）。9 `MorphExpressionDTO.cs`（`groupChildren`、子が無ければ null で JSON から省く）、`CsvModelSerializer.ModelCsv.cs`（morphgroups.csv のエントリの後ろに `,<子の数>,<名前:重み>…`、子が無ければ旧形式と同じ行）、`RemoteProgressiveSerializer.cs`・`.Extras.cs`（PLRM v10 で子の一覧）。10 は直さない（1 の 10 の訂正）。`Split` の写し 4 つ（CsvProjectSerializer・ObjectGroupCsv・MeshRenameCsvHelper・MeshSelSetCsvHelper）は行末の空欄を要る読みが無い（欄が無いときの既定値が空と同じ）ので変えていない。**コンパイル**：4 csproj とも 0、Unity の再読込でエラー 0。**直す前→直した後**（`SandBox/BugFix/s1/before`・`after`・`final`）：1 A4 を CSV で読んで保存し元と比べて materialprops.csv 2 ファイル 318 行の差 → 差なし。3 中身のある `tiny/full` を保存したフォルダへ中身の無い `tiny/stripped` を保存して morphgroups.csv・springbonegroups.csv が残る → 消える。4 worldMat・bindPose だけの tposebackup.csv を読んで保存するとファイルが無くなる → 同じ行で残る。5 2 番目が空の名前の 3 件を読み・保存・読み・保存して 2 件 → 3 件・並び同じ。7 A4 を .mfproj に保存して読み CSV に保存したものと CSV から保存したものの差 4 ファイル 17380 行 → 差なし。9 AliciaSolid.vrm を読み CSV・.mfproj・バイナリ（`saveProjectBinary`）で保存し、それぞれ読んで CSV に保存したもの 3 つが行で一致（morphgroups.csv に子が入る。例 `aa,…,1,bs_face.mouth_a:1`）。2 は C# に名前モードで書く呼び出し元が無いので、コンパイルと書く行の確認だけ（往復は段 ③ で Python の出力を C# で読んで確かめる）。**キャプチャ**：一時 Game ビューにメイン画面の領域が無く撮れなかった（5 の 2）が、操作者が直した後に撮影。直した後の A4（CSV から読込）を `fitCameraToModel` で正面から撮り、Zunko_MMD がテクスチャ付きで写る（`SandBox/Captures/BugFix/s1/a4_final_fit_0001.png`）。 |
| ② | 完了（A は直さず 6 へ） | 2026-10-20 | **直したもの**：6 `MeshContext.Transform.cs` に `TransformRowMatrix(showBindPose)`（描画の行列表の規則の唯一の置き場）を足し、`UnifiedBufferManager_Update.cs` の行列表と `VertexMatrix` のウェイトの無い頂点（とメッシュ・頂点が無いとき）がそれを使う。8 `HumanoidBoneMapping.FindBoneByAliases` を全別名で完全一致 → 全別名で部分一致の 2 巡に。9 `MotionClipApplier.ExpandToVertexMorphs` のグループを MeshEntries で展開（パネルの試し当てと同じ）。B `MotionClipHandler`（状態 `sourceKind`、`SaveJson`、`OnSourceChanged`）、`PanelCommand.Motion.cs`（`SaveMotionClipJsonCommand`）、`PlayerCommandDispatcher.Motion.cs`（受け口）、`PlayerMotionClipTestSubPanel.cs`（元ファイル・種類を窓口の状態から読む。`_filePath` を持たない。JSON保存は元ファイルの無いクリップなら saveMotionClipJson、読み直し・VRMA・BVH・FBX は読み込んだときの種類で）、`PolyLingPlayerViewerCore.Layout.TestPanels.cs`（配線）、契約 §3.7。**コンパイル**：4 csproj とも 0、Unity の再読込でエラー 0。**直す前→直した後**（`SandBox/BugFix/s2/`、キャプチャは `SandBox/Captures/BugFix/s2/`）：8 A4 の Zunko_MMD に Spine だけ 40° 前へ倒すモーション（`spine.plmotion.json`）を当てて横から撮ると、直す前は上半身はまっすぐで着物の一部だけが後ろへ跳ねる（`before_spine_Z_0001.png`）→ 上半身が前へ倒れる（`after_spine_Z_0001.png`）。9 VRM から読んだ AliciaSolid に表情 aa だけのモーションを当て、窓口の報告が「表情: 1/1」（直す前のコードでは GroupChildren の名前が一覧に無く 0 件になる。GroupChildren で辿る分岐を消した）。口が開く絵は撮れていない（6 の段 ② の 2）。B `loadMotionClipJson` → `saveMotionClipJson`（`ext.plmotion.json`）の中身が `queryMotionClipJson` と同じ（CRLF・BOM なし、97 行）、窓口の `sourcePath` がそのファイル・`sourceKind` が 1、そのファイルで `convertMotionToVrma`・`exportMotionJson` が成功（エラー 0）。直す前はコマンドが無い。6 は描画と編集が同じメソッドを呼ぶ形にした（規則が 1 か所）。ウェイトの無い頂点を MCP のコマンドで動かして確かめる手段が無く（頂点移動のコマンドが無い）、動きでは確かめていない。8 の自動割り当ての前後の比較は、直す前の割り当てを控えていなかったので行っていない。**A**：`VmdMotionBake` に一時コードを入れ、キャッチボール（トマホーク）.vmd を Zunko_MMD（体幹・腕・脚 21 本を割り当て）で焼いて、GetHumanPose のマッスルを SetHumanPose で戻した正準ローカル回転と元の差を倍精度で測った（249 フレーム）。直す前（GetHumanPose だけ）：RightHand 最大 153.73°、RightLowerArm 97.32°、RightUpperArm 63.98°、LeftUpperLeg 43.47°、LeftLowerLeg 42.18° ほか 16 本が 0.1° 超え（ほぼ全フレーム）。RefineMuscles を通した後：RightHand 60.99°、RightUpperArm 71.67°、RightLowerArm 59.15°、LeftUpperLeg 41.28° と、下がらない・上がる骨がある（Hips 0.03°→0.79°、RightFoot 13.46°→17.68°）。完了条件（0.1° 以下）を満たさないので VmdMotionBake・MediaPipePoseSolver の変更は取り消し、一時コードも消した（6 に記録）。UnityClipExportWindow は Editor の窓でシーンの Animator とクリップのアセットを使うため、MCP から測れなかった（6）。 |
| ③ | 完了 | 2026-10-20 | **直したもの**（`データとツール/poly_ling_py/polyling_io/`）：2 `format/csv_mesh.py`（mirrorGeometryDerived を両モードで書く）。4 `format/csv_project.py`（tposebackup.csv はファイルがあれば中身が空でも残す）。5 同（空の名前を `""`）。8 `format/humanoid.py` の `find_bone_by_aliases`（全別名で完全一致 → 全別名で部分一致）。9 `format/csv_project.py`（morphgroups.csv の `,<子の数>,<名前:重み>…` を読み書き。子があるときだけ `groupChildren`）、`blender/motion.py` の `expression_targets`（type 0 も meshEntries で展開）。11 `blender/meshread.py` に `morph_shape_keys`（名前 → 数が同じなら取り込み時の並び順の位置で、その名前がどのモーフの名前でもなければ名前の変更として使う）、`importer.py`（`polyling_morphs` に `keyOrder`）、`exporter.export_morphs`・`motion.expression_targets`・`motion._expression_name_for` がそれを使う。書き出しの警告「「顔」：シェイプキー あ → あ_改名 を名前の変更として扱った」。12 `format/__init__.py`（`load(path, unity_project_root)`・`find_unity_project_root`。.mfproj の各モデルに `_csvUnityProjectRoot`。`_csv` で始まるので .mfproj には書かれない）、`format/csv_project.py`（textures の写しの根）、`blender/materials.resolve_texture`（候補に足す）、`blender/importer.py`・`blender/__init__.py`（インポートの設定「Unity プロジェクトのフォルダ」）。13 `blender/motion.py`（読込で `polyling_key_times` を残し、書き出しでキーの時刻がそれと同じなら元のトラックを読込と同じ補間で比べる）。14 同（注記を正準骨格の親に直し、`Rig.canon_parent` を消した）。6 `blender/importer.py`（スキンド種別でウェイトの無い頂点があり WorldMatrix·BindPose が単位でなければ警告）。1 `format/csv_project.py` の注記。**確かめ方**：作業環境（Linux、bpy 4.2.0）に操作者が添付した ZIP（polyling_io・A4・RoboFArms・BlenderIO・BugFix/s1）を置き、直す前のコード（git の元の版）と直した後のコードで同じ試験を流した。試験の出力は作業環境だけ（`SandBox/BugFix/s3/` に置いたのは C# で作った試験データ）。**直す前→直した後**：2 名前モードで書いた A4 の Zunko_MMD に mirrorGeometryDerived の行が無く読み直して False → 行があり True。Python が名前モードで書いた Tiny（B1 が True、`SandBox/BugFix/s3/tiny_name`）を C# で読んで索引モードで保存（`tiny_name_cs`）して True が残る。4 見出しだけの tposebackup.csv を読むと None・保存でファイルが消える → 空の控えになり保存で残る。5・9 段 ① の C# の出力（`s1/final/a4_rt`・`after/alicia_from_bin`・`after/tiny_rt`・`after/tiny_rt2`・`after/target`）を Python で読んで書くと、`alicia_from_bin` の morphgroups.csv 12 行（子の欄が落ちる）と `tiny_rt`・`tiny_rt2` の springbonegroups.csv 1 行（`""` が空行）が違う → 全部行で一致。8 Zunko_MMD の自動割り当ての Spine が kimono_spine_02.x → 上半身。直す前後で変わった行はこの 1 行だけ（AliciaSolid 10 行・Zunko_MMD 51 行・RoboFArms 0 行の割り当てを比べた）。9 AliciaSolid に表情 happy（type 0）だけのモーション（`s3/expr_happy.plmotion.json`）を読むと当たる表情 0・未解決 happy → 1・シェイプキー bs_face.eye_smile と bs_face.eyeblow_smile（重み 1）。11 A4 を取り込みシェイプキー「あ」を「あ_改名」にして CSV へ書くと、Zunko_MMD.morph.csv が元と 654 行違い警告は「シェイプキー「あ」が無いので差分 0」→ 元と一致・警告は旧名 → 新名。続けて `varied.plmotion.json`（表情 あ を持つ）を読むと あ が未解決 → `key_blocks["あ_改名"]` に当たる。12 RoboFArms_cs.mfproj（と T5_cs・A4_cs）には材質のテクスチャのパスが無いので、RoboFArms_cs の 1 つ目の材質の baseMapPath を `Assets/Tex/robo_tex.png` にした .mfproj を、`Assets` と `ProjectSettings` を持つ作業環境のフォルダの下の `SandBox/BlenderIO/` に置いて試した（作業フォルダは別の場所）：CSV に書くと textures が空 → robo_tex.png が写る。Blender の取り込みで画像が付かない → 付く。設定に別のフォルダを入れると付かない（設定が自動より優先）。13 トラックごとにキー時刻が違うモーション（`s3/src_varied.plmotion.json`、bakedBones 10 本・時刻 2〜5 個ずつ）を C# で `loadMotionClipJson` → `saveMotionClipJson`（`s3/varied.plmotion.json`、36699 バイト。作業環境へは中身を写した。大きさと行数 2061 が一致）。Zunko_MMD で読んでそのまま書くとバイトが違い bakedChanged 10 → バイト一致・bakedChanged 0（回転の差の最大 3.0e-7）。6 Zunko_MMD の「服」のメッシュの bindPose を単位でない値にしウェイトを 2 頂点外した .mfproj を取り込むと警告なし → 「ウェイトの無い頂点 2 個は…」の警告。**引き続き一致**：入出力計画の段 ④ の 5 本（pl_s4back・pl_s4・pl_mocopi・pl_s2・probe_pl）は読んでそのまま書いてバイト一致（直す前後とも）。段 ① の形式の往復：RoboFArms_cs・T5_cs・synth_cs の .mfproj はバイト一致、A4 の CSV は全ファイル行一致、RoboFArms の CSV はモデルのファイルが全部行一致。段 ③ の Blender 経由の往復（A4・RoboFArms の CSV はモデルのファイルが全部一致、RoboFArms_cs・T5_cs はバイト一致）は直す前後とも同じ結果。端末への反映は MCP の差分当てで行い、15 ファイルの大きさが作業環境と一致することを確かめた。 |
| ④ | 完了 | 2026-10-20 | **直した文書**：契約 §5.1（アクションの `polyling_key_times`、自動割り当ての 2 巡の照合、キーの決まりの和集合と `polyling_key_times` の比べ方、表情のグループを meshEntries で展開）、§7.1（取り込みの設定「Unity プロジェクトのフォルダ」とテクスチャの探し方）、§7.2（`polyling_morphs` の `keyOrder`）、§7.3（ウェイトの無い頂点の警告）、§7.4（「シェイプキーの引き方」を足し、「書き出さないもの」のシェイプキーの名前の変更を直した）、§7.5 を新設（CSV の欄の分け方・中身が無い 9 ファイルを消す・tposebackup.csv・springbonegroups.csv・morphgroups.csv・グループの子の置き場所・mirrorGeometryDerived）。§3.7 は実装と食い違いが無く、変えていない。入出力計画 6 の 1〜14 と連携計画 6 の 2 件（A・B）に移管と結果を書いた。機能の場所 §2「モーション」と §3・§3-1 に `saveMotionClipJson`（パネルの「JSON保存」）と「Unity プロジェクトのフォルダ」を足した。`HumanoidBoneMapping.cs` の `FindBoneByAliases` の注記（392 行。「完全一致 → 候補名がエイリアスを含む → エイリアスが候補名を含む」）が実装（2 巡、逆向きは見ない）と食い違っていたので注記だけ直した（コードは変えていない）。**コンパイル**：4 csproj とも 0。**根拠を確かめた file:行**（全部読んで記載と一致）：グループの表情の再生 `MotionClipApplier.cs:450-465`（Vertex・Group を `MorphPreviewState.BuildMorphBasePairs`）・`blender/motion.py:550-557`（type 0・1 を meshEntries、無ければ meshIndices を重み 1）。groupChildren の保存 `MorphExpressionDTO.cs:190・212-218・272-274`（子が無ければ null）、`RemoteProgressiveSerializer.cs:161`（v10）・`.Extras.cs:74-107`、`CsvModelSerializer.ModelCsv.cs:518-523`（書き：子があれば `,<数>` と `Esc("名前:重み")`）・`580-592`（読み：`LastIndexOf(':')`、欄が無ければ `PInt` の既定 0）、`format/csv_project.py:331-349・1056-1061`（同じ）。springbonegroups `CsvModelSerializer.VrmCsv.cs:38-39`（空は `""`）・`46-53`（読み）、`Split` が引用欄の中身を返す `CsvModelSerializer.Texture.cs:182-196`、`csv_project.py:1205-1210・568-571`。tposebackup `CsvModelSerializer.cs:200-205`・`.EditorCsv.cs:425-427`、`csv_project.py:534-566・1184-1203`。中身が無いファイルを消す `CsvModelSerializer.cs:147-211`（9 ファイル。editorstate・workplane は渡されないときも消す）・`DeleteIfExists`（`.Texture.cs:103-107`）、Python は `_write_or_delete`（`csv_project.py:912-917`）を 1041・1065・1097・1101・1147・1171・1182・1203・1208 行で。行末の空欄 `CsvModelSerializer.Texture.cs:206-207`・`.ModelCsv.cs:400`（10 欄未満を捨てる）・`csv_project.py:279-283`（9 欄以上）。mirrorGeometryDerived `CsvMeshSerializer.Write.cs:72-73`・`.Read.cs:125-127`・`format/csv_mesh.py:706-707`。saveMotionClipJson `PanelCommand.Motion.cs:379-393`・`PlayerCommandDispatcher.Motion.cs:101-115`（窓口なし・クリップなしは notReady、経路は `PLSandbox.TryResolveWrite`）・`MotionClipHandler.cs:160-199`（BOM なし UTF-8、`SourcePath`・`SourceKind`=1）・`PlayerMotionClipTestSubPanel.cs:75-76`（パネルは窓口の状態を読む）・`719-730`（元ファイルが無ければこのコマンド）・`513`（ボタンの文字「JSON保存」）。シェイプキーの引き方 `blender/meshread.py:523-545`・`importer.py:315-316`（`keyOrder`）・`exporter.py:213-223`（警告の文）・`motion.py:538・1183`。Unity プロジェクトのフォルダ `blender/__init__.py:32-41`・`importer.py:709-710`・`format/__init__.py:17-40`・`csv_project.py:1318-1328`・`blender/materials.py:55-58`、C# `ModelTextureTransfer.cs:225-233`。キー時刻 `motion.py:626・633`（和集合を `polyling_key_times` に）・`934・999-1031`（書き出しの比べ方。腰を当てずに読んだ Hips は 1013-1014 で元のまま）・`142-160`（`_sample_vec`：線形、回転は Slerp、端は端の値）・`859-862`（`_same_times`、1e-3 フレーム）。照合の 2 巡 `HumanoidBoneMapping.cs:403-431`・`format/humanoid.py:211-233`。ウェイトの無い頂点の警告 `importer.py:652-661`・`space.py:126-131`（要素の差 1e-5）・`MeshContext.Transform.cs:597-608`。7 の結果 `ModelSerializer_ModelContext.cs:227`。 |

---

## 5. 改善案（操作者の作業が要るもの）

- 入出力計画 5 の 1 と同じ（MCP から Blender を動かせない。Windows の Blender での確認は操作者の作業）。
- 段 ① で Unity の Game ビューが小さくなり（969×425）、PolyLing のメイン画面の領域が無くなって `unity_capture`（MainView・TriView）が「切り出し範囲を取得できませんでした」で失敗した。Unity エディタのレイアウト（Game ビューの大きさ）は MCP から変えられない。操作者が Game ビューを広げれば撮れる。改善案：MCP サーバに Game ビューの大きさを指定する道具を足す（エディタ拡張側で `EditorWindow` の位置を設定）。

---

## 6. 残件（範囲外で見つけたもの・直さないもの）

- 6（Python 側）：Blender の Armature 修飾子ではウェイトの無い頂点だけにメッシュ自身の行列を掛けられないので直さない。段 ③ で警告だけ出す。
- 10：A4（と元にした参考データ）の AliciaSolid は保存データに画像が無いので白のまま。AliciaSolid.vrm から作り直すかは操作者の判断。
- 段 ① で見つけたもの：同じ名前のモデルが複数あると、CSV の保存（`CsvProjectSerializer.cs:243-244` がモデル名だけからフォルダ名を作る）で同じモデルフォルダへ重ね書きされる。AliciaSolid が 3 体ある状態（`importVrmFile` を続けて呼んだ後）で保存すると、project.csv には `model,0,AliciaSolid`〜`model,2,AliciaSolid` が並び、フォルダは 1 つで textures は 1 体目の画像・materials.csv は 3 体目のもの（`SandBox/BugFix/s1/after/alicia_seq`）。
- 段 ② の A：VMD を正準骨格に写した回転は、GetHumanPose のマッスルでも RefineMuscles で詰めたマッスルでも再現できない（測った値は 4 の段 ②）。RefineMuscles は VRMA 読込の s4 では 0.0000° まで詰まったが、この VMD の姿勢では詰まらない。原因（可動域の外の姿勢か、LM の初期値と局所解か、2 自由度の骨か）は未調査。VmdMotionBake・MediaPipePoseSolver・UnityClipExportWindow は GetHumanPose だけのまま。調べるなら別の作業線にする（一時コードの測り方は 4 の段 ② のとおり）。UnityClipExportWindow の往復誤差は、操作者が Editor の窓でクリップを焼いたものを読み戻して比べる必要がある。
- 段 ② の 2：表情を当てても、PolyLing の画面で口が開いて見えなかった（VRM の AliciaSolid の aa〔グループ〕も、A4 の Zunko_MMD の「あ」〔頂点モーフ。今回の修正と関係しない経路〕も）。窓口の報告では表情は結び付いている（1/1）。画面に出ない原因は未調査（範囲外）。
- 段 ① で見つけたもの：`Split` の写し 4 つ（`CsvProjectSerializer.cs`・`ObjectGroupCsv.cs`・`MeshRenameCsvHelper.cs`・`MeshSelSetCsvHelper.cs`）も行末の空欄を作らない。今の読みは影響を受けない（段 ① の進捗に記載）ので変えていない。
- 段 ③ で見つけたもの：CSV はボーン以外のメッシュの bindPose を書かない（C# `CsvMeshSerializer.cs:94` で `WriteBoneData` はボーンだけ、bindPose はその中の `CsvMeshSerializer.Write.cs:192-197`。Python `format/csv_mesh.py` も同じ）。.mfproj は持つ。そのため CSV から取り込んだときの 6 の警告は WorldMatrix だけで決まる。
- 段 ③ で見つけたもの：段 ③ の 12 の完了条件に書いた `RoboFArms_cs.mfproj` には材質のテクスチャのパスが無く、そのままでは試せなかった（作ったファイルで試した。4 の段 ③）。`SandBox/BlenderIO/` の T5_cs・synth_cs・A4_cs にも `Assets/…` のパスは無い（A4_cs は `textures\…` と絶対パス。ZeroChanTPose_cs は見ていない）。
- 調査中の副作用：`saveProjectCsv` に作業フォルダからの相対で `BugFix/survey/alicia_vrm/project.csv` を渡したため、プロジェクト直下に `BugFix/survey/alicia_vrm/` ができた（SandBox の外。中身は試験用の CSV と画像）。

## 7. 残件の締め（2026-10-31。この計画は完了）

- 6（Python 側）：直さない（上の記述のとおり）。
- 10（AliciaSolid が白）：試験データに画像が無いだけでコードの不具合ではない。作り直さない。
- 同じ名前のモデルの CSV 保存：直した。`CsvProjectSerializer.BuildModelFolderNames` でフォルダ名を 1 か所で決め、重なれば `_2`・`_3`…（大文字小文字は区別しない）。書き出しと project.csv が同じ表を使う。確認：`__AちゃんH.pmx` を 2 回読んで `saveProjectCsv` → project.csv が `model,0,__AちゃんH`／`model,1,__AちゃんH_2`、フォルダ 2 つ。`loadProjectCsv` で 2 体とも戻った（`SandBox/verify1031/same/`）。
- 段 ② の A（VMD のマッスル化）：直さない（段 ② で決定）。原因の調査は行わない。
- 段 ② の 2（表情が画面に出ない）：今のコードでは出る。`loadMotionClipJson` で `__AちゃんH.pmx` の頂点モーフ「あ」と `Zunko_VRM10K.vrm` の表情グループ aa を重み 1 と 0 で当て、正面から撮って口が開く・閉じるを確かめた（`SandBox/Captures/PolyLingCapture_0004`〜`0007`）。コードの変更なし。
- `Split` の写し 4 つ：読みに影響しないので変えない。
- CSV がボーン以外の bindPose を書かない：記録のみ（.mfproj が持つ）。変えない。
- `RoboFArms_cs.mfproj` のテクスチャのパス：記録のみ。
- 調査中にできたプロジェクト直下の `BugFix/`：既に無い（2026-10-31 確認）。

# PolyLing Blender 入出力計画（プロジェクト・モーション）

Blender で PolyLing のデータを読み書きするアドオンを作る。
- 対象形式：`.mfproj`（JSON）と CSV フォルダの両方、モーション `.plmotion.json`（v4）。
- `PolyLing_Blender連携計画.md` の段 ②（`.plmotion` 読み書き）はこの計画の段 ④ へ移した。

作成：2026-10-19

---

## 0. 進め方の決まり

- この文書が正本。会話が切れたら、次はこの文書だけを読んで続きから始める。
- 1 段＝1 回の会話で閉じる。段の終わりに「完了条件」をすべて満たし、チェック欄と「4. 進捗」を更新してから終える。
- 承認はこの計画書に対して 1 回（2026-10-19 承認済み）。段の途中で止まって承認を取りに行かない。
- 段の範囲外で見つけた問題は手を広げず「6. 残件」に書くだけにする。
- C# 側の読み書き（`ProjectSerializer`・`CsvProjectSerializer`・`CsvModelSerializer`・`CsvMeshSerializer`・`MotionClipSerializer`）が仕様の正本。Python 側は推測で書かず、C# の該当行を読んでから同じ規則で書く。

---

## 1. 着手時点の事実（調査済み）

- `.mfproj` は `ProjectDTO` をそのまま JSON にしたもの（`ProjectSerializer.cs` の `ToJson`、Newtonsoft・Indented・null 省略）。
- CSV フォルダは同じ内容をモデル・メッシュ単位の CSV に分けたもの。書き出し前に `MeshDTO` を往復させる（`CsvMeshSerializer.cs` の WriteFile）ので、項目は `.mfproj` と同じ。
- プロジェクトにモーションは入らない（`ProjectDTO` の motions はコメントアウトされた将来用）。モーションは別ファイル `.plmotion.json` v4（契約 §5）。
- 頂点のボーンウェイトの番号は MeshContextList の索引（`ModelContext.Selection.cs:317`）。
- 頂点の空間：MeshFilter 系はローカル、Skinned 系はバインド（ワールド）空間（`MeshTypes.cs` の `SkinKind`）。
- ボーン・オブジェクトの姿勢は `boneTransform`（ローカル位置・オイラー角・スケール、`Quaternion.Euler`）。
- 頂点は複数の UV・法線を持ち、面の `uvi`・`ni` で選ぶ。
- 座標：Blender (x, y, z) ⇔ PolyLing (x, z, y)、面の頂点の並びは逆（契約 §3.6）。
- 既存の Blender 資産は `データとツール/poly_ling_py/polyling_bridge.py`（Blender 3.6 以上、WebSocket でメッシュの送受信のみ）。

---

## 2. 決定事項

### 2.1 置き場所と構成
- アドオンは `データとツール/poly_ling_py/polyling_io/`（ブリッジとは別のアドオン）。Blender の「ファイル > インポート／エクスポート」に入れる。Blender 3.6 以上。
- `format/`：bpy を使わない層。`.mfproj`・CSV フォルダ・`.plmotion.json` を読み書きし、`ProjectDTO` と同じ形の辞書（JSON の欄名そのまま）にする。知らない行・欄は捨てずに保持して書き戻す。
- `blender/`：辞書 ⇔ Blender の層。

### 2.2 取り込み（辞書 → Blender）
- モデル → コレクション。メッシュ → オブジェクト（親子・トランスフォーム）。
- ボーン → モデルごとに 1 つのアーマチュア。元のローカル回転はカスタムプロパティにも残す。
- ウェイト → 頂点グループ（名前はボーン名）。UV・法線 → 面の角単位。
- 頂点 ID・面 ID・部品 ID・フラグ → 属性として保持（振り直さない）。
- モーフ → シェイプキー。2 頂点の面 → 辺。マテリアル → Principled BSDF。
- Blender で表せないもの（IK・剛体・揺れもの・VRM 設定・選択セット・オブジェクトグループなど）は生の JSON のまま保持。

### 2.3 書き出し（Blender → 辞書）
- 2.2 で付けた情報から `ProjectDTO` の形へ戻し、どちらの形式でも書く。
- Blender で頂点数が変わったメッシュは、頂点番号を参照する保持データを外して警告する。
- （段 ③ で決定）元の値を使う決まり：Blender に表れない欄は保持した JSON の値。Blender に表れる欄（頂点・面・姿勢・ボーン・ウェイト・シェイプキー）は Blender から作り、作った値が元の値で作ったものと許容差（行列の要素で 1e-4、ボーンのレストは 1e-5）のうちなら元の値を使う。float は C# の書き方（.mfproj は G7 で戻れば G7・だめなら G9、CSV は G9）と同じ 10 進になる double にする。
- （段 ③ で決定）元の法線の置き場所と使いどき：取り込みで角ごとの元の法線を属性 `pl_normal`（PolyLing 座標）、元の `ni`・`uvi` を `pl_ni`・`pl_uvi`、取り込み時の頂点位置を `pl_co` に残す。書き出しでは角の法線に `pl_normal` を使い、「動いた頂点」（位置が `pl_co` と違う）とそれと面を共有する頂点の角だけ Blender が計算した法線を使う。頂点ごとの法線・UV の並びは `pl_ni`・`pl_uvi` の番号で元へ戻す（番号の食い違いや欠けがあれば出てきた順に振り直す）。Blender でカスタム法線だけを編集した変更は書き出さない。
- （段 ③ で決定）取り込みの直後に書き出しと同じ読み方で読み直し、戻らない頂点の欄・面だけを `polyling_vertex_extra`・`polyling_face_extra` に残す（変更しないメッシュは元と一致させるため）。ウェイトは元の bw（重み 0 の枠の番号も）を属性 `pl_bw_index`・`pl_bw_weight` に残し、頂点グループの中身が同じなら元の bw を使う。
- （段 ③ で決定）頂点数・面のつながりが変わったかは、取り込み時の指紋 `polyling_topology`（頂点数・面の元の面番号と頂点の並び・線分の辺）と比べて決める。変わったメッシュで外すもの：選択（頂点・辺・面・線分）・選択セット・法線除外セット・線分群・制御点・作れなかった面・頂点と面の控え・T ポーズの頂点控え。モーフは要素の番号がずれないように要素を残し、保持した基準（basePositions・baseNormals・baseUVs・頂点の控え）を外して、今の親の形とシェイプキーの差分で作り直す。
- （段 ③ で決定）姿勢は取り込みの逆：MeshFilter 系と空は Local = C·matrix_basis·C。Skinned 系は頂点をバインド空間のまま書く（アーマチュアに対して動いていれば頂点に焼き込む）。ボーンはレストの変化から bindPose（取り込み時のレスト `polyling_rest_matrix` で向きの付け替え分を戻す）、ポーズから WorldMatrix（ポーズ行列 = C·(WorldMatrix·bindPose)·C·レスト）を戻し、Local = hierarchy[親]⁻¹·WorldMatrix。生成ミラー側の姿勢は PolyLing では実体側から決まる（`SyncDerivedMirrorTransforms`）ので、Blender で生成ミラー側だけを動かすと警告する。
- （段 ③ で決定）書き出さないもの：PolyLing 由来でないオブジェクト・ボーン（警告）、シェイプキーの名前の変更、マテリアル自身の設定の変更（どちらも保持した値のまま。面に当てるマテリアルの付け替えは書く）。Blender で消した要素は番号がずれないように空の要素で埋めて警告する。

### 2.4 モーション
- `PolyLing_Blender連携計画.md` 2.1・2.2 の決定（割り当て表、bakedBones・表情・lookAt、回転の変換式）をそのまま引き継ぐ。
- （段 ④ で決定）割り当て表はアーマチュアのオブジェクトの `polyling_humanoid`（PropertyGroup。正準骨格の 54 本）。自動の割り当ては PolyLing の再生（`UnityClipApplier.ResolveNode`）と同じ順：ボーンの `humanBodyBone` → 埋め込みの別名表（`HumanoidBoneMapping.EmbeddedMapping`、あいまい検索も同じ）→ Humanoid 名そのもの。VRM Add-on からの取り込みは `armature.data.vrm_addon_extension`：VRM0 は `vrm0.humanoid.human_bones` の `bone`・`node.bone_name`、VRM1 は `vrm1.humanoid.human_bones.<名前>.node.bone_name`（VRM1 の thumbMetacarpal/Proximal/Distal は Unity の ThumbProximal/Intermediate/Distal）。アドオンのソース（`editor/vrm0・vrm1/property_group.py`）で確認。
- （段 ④ で決定）回転の式（PolyLing 空間で計算し、Blender とは R_bl = C·R_pl·C）。正準ワールド W_c は `CanonSkeleton` の親から順に W_c(子) = W_c(親)·q（トラックの無いボーンは親のまま。`ApplyCanonLocals` と同じ）。T ポーズ整列 A は `UnityClipApplier.CanonAlignment`（腕 8 本、「脚も合わせる」で脚 4 本）。モデルのワールド差分 Δ = W_c·A⁻¹、ボーンの姿勢の回転 = Δ_bl·回転(P0)。P0 は PolyLing のレスト（BindWorldMatrix）でのポーズ行列 = C·(WorldMatrix·bindPose)·C·（取り込み時のレスト）。トラックの無い Humanoid は祖先に付いていく。書き出しは逆（Δ = C·(回転(P)·回転(P0)⁻¹)·C、W_c = Δ·A、q = W_c(正準の親)⁻¹·W_c。トラックを書かないボーンの W_c は親のまま、割り当ての無いボーンの元のトラックは W_c の組み立てに使う）。
- （段 ④ で決定）Hips：読込の既定は「腰の回転と位置も当てる」（`ModelMotionSampler` の T と同じ。位置 = レスト位置 + pos × 腰の高さ、腰の高さはレストの Hips の Y）。切ると PolyLing のパネル再生と同じく Hips を当てない（その読み方で書き出すと Hips は元のトラックのまま）。
- （段 ④ で決定）キー：読込は元のキー時刻に線形キー（フレーム = t × frameRate、t = 0 がフレーム 0。接線は線形として読み警告）。書き出しはトラックごとに「Blender のキー時刻が元と同じで、値が許容差（回転の成分 2e-5、位置 1e-5、表情 1e-6）のうち」なら元のトラック、違えば frameRate の格子で標本化。bakedBones が 1 本でも変わったら元の muscles・body は書かず rotationSource を "bakedBones" にする（PolyLing が読込時に焼き直す）。muscles・body・bones（二次骨）・metadata など Blender に表さない欄は、読んだファイルを丸ごとアクションの `polyling_motion` に残して戻す。元の無いアクションは rotationSource "bakedBones"・metadata.createdWith "PolyLing IO (Blender)" で書く。
- （段 ④ で決定）表情：PolyLing と同じ展開（`MotionClipApplier.FindExpression`・`ExpandToVertexMorphs`）。モデルの表情を名前→英語名で引き、頂点モーフはそのモーフのシェイプキーへ値×重み。グループは GroupChildren を辿る（保存データに無いので何にも当たらない。6 の 9）。表情の定義に無い名前は同じ名前のシェイプキー。新しく足すトラックは provider・category を書かない（PolyLing は provider で引かない）。シェイプキーのアクションはメッシュの Key ごと。
- （段 ④ で決定）視線：アーマチュアの子の空オブジェクト「＜アーマチュア＞_視線」（`polyling_lookat`）。位置 = Head のレスト位置 + lookAtOffsetFromHeadBone、回転 = C·R(q)·C·Rx(−90°)（矢印＝ローカル +Z が視線）。目のボーンは動かさない（目は bakedBones の LeftEye・RightEye）。
- （段 ④ で決定）書式：`MotionClipSerializer.ToJson` と同じ形（欄の順は DTO の宣言順、表情トラックは category・provider が先＝C# の出力で確認。float は float32 を G7 で戻れば G7・だめなら G9、ちょうど半分は 0 から遠い側へ。DTO が初期値を持つ欄は無くても書く）。知らない欄は既知の欄の後ろに元の順で書く。

---

## 3. 段と完了条件

### 段 ① 形式の層（bpy なし）
対象：`polyling_io/format/`
- [x] `.mfproj` 読み書き。
- [x] CSV フォルダ読み書き（C# の全行種に対応、知らない行は保持）。
- [x] CSV ⇔ 辞書 ⇔ `.mfproj` の相互変換。
- 完了条件：既存のテストデータで、読んで書き戻した結果が元と一致（`.mfproj` は JSON 値で一致、CSV は行で一致）。CSV から作った `.mfproj` を PolyLing で読み、CSV を直接読んだものと一致。

### 段 ② Blender への取り込み
対象：`polyling_io/blender/`（取り込み）
- [x] 2.2 の全項目。
- 完了条件：テストデータを Blender で読み、形状・ボーン・ウェイト・UV・モーフ・マテリアルを確認（Blender の実行が MCP からできなければ操作者の作業）。

### 段 ③ Blender からの書き出しと往復
対象：`polyling_io/blender/`（書き出し）
- [x] 2.3。
- 完了条件：Blender で読んでそのまま書いたものを PolyLing で読み込みキャプチャ。変更しなかった項目が元と一致。

### 段 ④ モーション（旧 Blender 連携計画 段 ②）
対象：`polyling_io/format/`（plmotion）・`polyling_io/blender/`（アクション）
- [x] `.plmotion.json` v4 ⇔ アクション（bakedBones・表情・lookAt）。割り当て表。
- 完了条件：Blender で書いた `.plmotion.json` を `queryMotionJson` でエラー 0。PolyLing の `.plmotion.json` を Blender で読み、書き戻したものと元を全キー比較。

### 段 ⑤ 文書
対象：`PolyLing_外部連携の契約.md`、マニュアル、機能索引
- [x] 手順と制約。
- 完了条件：記載が段 ①〜④ の実装と一致。

---

## 4. 進捗

| 段 | 状態 | 日付 | メモ |
|---|---|---|---|
| ① | 完了 | 2026-10-19 | 置き場所 `データとツール/poly_ling_py/polyling_io/format/`（`mfproj.py`・`csvutil.py`・`csv_mesh.py`・`csv_project.py`・`__init__.py` の `load`/`save`）。CSV の行の仕様は C# から洗い出した（float は G9 で .NET と同じく 0 から遠い側へ丸める）。検査（試験フォルダ `SandBox/BlenderIO/`、Python は端末の Linux 環境 3.10）：**.mfproj** は読んで書き戻すと C# の出力とバイト一致（es2test・RoboFArms・T5・synth）。**CSV** は SandBox の全プロジェクト 18 本（bintest/A4・A5、ZeroChan、mqoref、roboF 系ほか）で読んで書き戻すと全ファイル行一致（除外：project.csv の日時行、書き出し専用の humanoid.export.csv は行の順だけ違う＝C# も読込直後に書けばメッシュ順になる、中身の無いモデルフォルダは C# と同じく読まない）。**CSV→辞書** を C#（`loadProjectCsv`→`saveProjectFile`）と JSON の値で比較：A4・RoboFArms・T5 で一致（違いは作成日時と、6 の C# の不具合 1 だけ）。名前モード・オブジェクトグループ・VRM 表情・下絵・Avatar 設定・座標規約・メッシュ選択辞書は、Python で書いた名前モードの CSV を C# で読み、Python の読みと JSON の値で一致（違いは 6 の 2 だけ）。**.mfproj→CSV**：C# の RoboFArms.mfproj から書いた CSV が元の CSV と行一致（違いは currentModelIndex〔JSON に無い〕と 6 の 1 の行だけ）。テクスチャは CSV の textures を写す。.mfproj から書くときは実在するマテリアルのパスの画像を写す。 |
| ② | 完了 | 2026-10-20 | 置き場所 `polyling_io/__init__.py`（bl_info・ファイル > インポート > PolyLing プロジェクト。bpy が無ければ登録しないので format は素の Python でも使える）、`polyling_io/blender/`（`space.py` 座標と行列・`importer.py`・`materials.py`・`__init__.py` 演算子）。入れ方は `polyling_io` フォルダを Blender の addons へ置くか zip にしてインストール。**置き方**：プロジェクト→コレクション「PolyLing ＜名前＞」（polyling_project）、モデル→子コレクション（polyling_model＝meshDTOList 以外、polyling_raw_meshes＝剛体・ジョイント・作業軸の MeshDTO）、メッシュ→オブジェクト（Group・Helper・頂点の無いメッシュは空オブジェクト）、ボーン→モデルごとに 1 つのアーマチュア、モーフ→親メッシュのシェイプキー（親は `MorphPreviewState.FindBaseMeshIndex` と同じ規則、差分は頂点位置−basePositions）。どの要素も polyling_index・polyling_object_id・polyling_mesh（JSON。形を作ったメッシュは vertices・faces を除く）を持つ。ボーンは polyling_local_rotation にも元のローカル回転。モーフの MeshDTO は polyling_morphs、作れない面は polyling_unbuilt_faces、制御点は polyling_control_points。頂点 ID・サブ ID・部品 ID・フラグは点の属性 pl_vertex_*、面は pl_face_index（元の面番号）・pl_face_id・pl_face_flags、2 頂点の面は辺にして pl_line_face_index、ミラー側ウェイトは pl_mirror_bone_*。UV・法線は面の角単位（uvi・ni）。材質は使う番号だけをスロットにし、マテリアルの polyling_material_index・polyling_material（JSON）で元の番号へ戻す。**空間（C# で確認）**：MeshFilter 系は頂点ローカル・matrix_basis＝C·BoneTransform·C、親とのずれは matrix_parent_inverse で吸収（生成ミラーは S·H·S、ModelContext.MeshList.cs:386-417）。Skinned 系は頂点バインド空間・単位行列でアーマチュアの子＋Armature 修飾子（MeshContext.Transform.cs の IsSkinned、MeshFilterToSkinnedConverter.cs:704-712）。ボーンのレストは保存された bindPose の逆（null は単位。読込で撮り直さない＝CsvMeshSerializer.Read.cs:199・ModelSerializer_Vrm.cs:356）、向きは子の頭の平均へ。WorldMatrix·BindPose が単位でないボーンはその変形どおりのポーズを付ける（UnifiedBufferManager_Update.cs:104 と同じ変形）。**検査**（試験データ A4・RoboFArms の CSV、RoboFArms_cs・T5_cs の .mfproj。Blender 3.6.23（Linux 版本体）・4.2.0・5.2.2（bpy）の 3 版）：評価後の全頂点のワールド位置が PolyLing の規則（非スキンド WorldMatrix・スキンド Σw·WorldMatrix·BindPose）と 4e-6 以内、ボーンの頭が WorldMatrix と 1e-6 以内、UV 1e-9、シェイプキーの差分 0、面の並び・頂点 ID・材質番号の不一致 0。法線は最大 0.0035 ずれ、面と逆を向いた法線（RoboFArms で 23 角）は Blender のカスタム法線で表せない（段 ③ は元の法線を使うかを決めること）。画面：Blender（Cycles）で正面を撮り、PolyLing の正面キャプチャと形・左右・テクスチャが一致。ずんこで「あ」「まばたき」と両腕の回転、アリシアで「mouth_a」と左腕の回転が効くことを画像で確認（`SandBox/BlenderIO/stage2/`、PolyLing 側は `SandBox/Captures/BlenderIO/stage2_pl_*`）。Blender は MCP から動かせなかった（5 の 1）。 |
| ③ | 完了 | 2026-10-20 | 置き場所 `polyling_io/blender/exporter.py`（辞書へ戻す・`export_project`）、`meshread.py`（メッシュ → vertices・faces。取り込みの検算にも使う）、`space.py` に逆変換（`decompose_trs` ほか）、`importer.py` に書き出し用の控え（2.3）、`blender/__init__.py` に［ファイル > エクスポート > PolyLing プロジェクト］（拡張子 .mfproj なら JSON、.csv なら CSV フォルダ。名前で参照の切り替えあり）。決めたことは 2.3 に書いた。**読む→そのまま書く**（作業環境の Blender 3.6.23 Linux 版・bpy 4.2.0・bpy 5.2.2 の 3 版、A4〔CSV〕・RoboFArms〔CSV〕・RoboFArms_cs・T5_cs〔.mfproj〕）：書いた辞書と元を全欄比較して、違いは CSV を読んだ時刻で埋まる欄（モーフと表情の createdAt。CSV に無い欄で C# も読込時刻を入れる）と、CSV のプロジェクト行の作成・更新日時（書いた時刻）だけ。取り込みで控えに回った頂点・面は 0。.mfproj は RoboFArms_cs・T5_cs で C# の元ファイルとバイト一致（3 版とも）。CSV は元のフォルダと行で一致（違いは日時の行と、段 ① と同じ humanoid.export.csv の行の順だけ）。3 版の出力は互いに同じ（日時を除く）。**PolyLing で読む**（`loadProjectCsv`／`loadProjectFile` → `saveProjectCsv` → `queryCsvFolderDiff`）：A4〔書いた CSV〕・RoboFArms〔書いた CSV〕・T5・RoboFArms_cs〔書いた .mfproj〕とも、元を同じ手順で読んで保存したものと「差なし」。A4 の書いた .mfproj は、C# の A4_cs.mfproj を読んだものと比べて materialprops.csv だけ違う（6 の 1：C# の CSV 読込が落とすシェーダー固有プロパティを Python 側は持っている）。元の CSV を読んだものとは 6 の 7 の分だけ違う。正面キャプチャ（`SandBox/Captures/BlenderIO/stage3_*_orig` と `stage3_*_blender`）で形・左右・テクスチャが一致。奥の大きな人物と左の産業用ロボットの腕は同じデータを続けて撮っても動く（`stage3_*_again`。T5 を読んでも映る）ので比較から外した。**編集して書く**（bpy 4.2）：A4 で「face」の面を 1 枚消す・「body_top」の頂点を 1 つ動かす・LeftForeArm を回す、RoboFArms_cs でオブジェクトを動かす・頂点を動かす・ボーンを回す。警告どおり face の保持データを外しモーフ 49 個を作り直し、動いた頂点の周りだけ法線を Blender の値にした。書いたものを Blender で読み直すと評価後の頂点のワールド位置が編集した場面と 6e-7 以内（RoboFArms の 0.1 の差は動かした実体の生成ミラーで、PolyLing の規則どおり実体に付いて動いたもの）。A4 の編集版は PolyLing で読める（`stage3_A4_edit`）。出力は `SandBox/BlenderIO/stage3/`（記録は `logs/`、PolyLing の保存は `cs_*_orig`・`cs_*_bl`）。 |
| ④ | 完了 | 2026-10-20 | 置き場所 `polyling_io/format/plmotion.py`（読み書き・検査。version 1〜3 の引き上げは C# と同じ）、`format/humanoid.py`（正準骨格・方向表・整列の範囲・埋め込みの別名表の写しと自動割り当て）、`blender/motion.py`（割り当て表・読込・書き出し）、`blender/__init__.py`（［ファイル > インポート／エクスポート > PolyLing モーション (.plmotion.json)］、3D ビューのサイドバー［PolyLing］に割り当て表〔自動で割り当て・VRM アドオンから取り込む〕とモーションの読み書き）。決めたことは 2.4 に書いた。**形式の層**：PolyLing が書いた .plmotion.json（`exportMotionJson` で今の C# に書き直させた `SandBox/BlenderIO/stage4/pl/` の pl_s4・pl_s4back・pl_mocopi〔BVH から。rotationSource bakedBones〕・pl_s2・probe_pl〔接線・カテゴリ・ラップ・lookAt・absentMuscles を持たせた手書きを C# に書かせたもの〕）と既存の C# 出力（VrmaPlan・BVH・fbx の 9 本）を読んで書き戻すとバイト一致。手書きのファイル（s2_src・probe_src）は C# がそれを書き直したものと JSON の値で一致（違いは C# が足す bakedBones・metadata だけ）。**読んで書き戻す**（作業環境の Blender 3.6.23 Linux 版・bpy 4.2.0・bpy 5.2.2 の 3 版、A4 の AliciaSolid・Zunko_MMD）：5 本とも元とバイト一致（全キー比較で違い 0）。元のキー時刻で Blender から作り直した値と元の値の差は回転の成分 8.1e-7、Hips の位置 3.8e-7、視線 1.3e-7、表情 0 以下（許容差のうちなので元を書いている）。**Blender で書いたもの**：編集（Alicia の左前腕・Zunko の左上腕にキーを足す。edit_A・edit_Z）と一から（Zunko で腕・脚・頭・表情 2 本・視線を手で付けた fresh_Z）を `queryMotionJson` にかけてエラー 0・警告 0。書いたものを Blender で読み直すとボーンの頭の位置が書く前と 8.5e-7 以内。3 版の出力はバイト一致（`stage4/logs/md5_3versions.txt`）。VRM Add-on（2026-10-19 の main を bpy 4.2 に登録）の VRM0・VRM1 の割り当てを取り込めることを確かめた。**キャプチャ**（PolyLing はモーションの窓口 `motionClip` の load で当て、Blender は「腰を当てない」で読み Cycles で正面から撮影。`SandBox/Captures/BlenderIO/stage4_pair_*`＝左 PolyLing・右 Blender、単体は `stage4_pl_*`・`stage4_bl_*`）：fresh_Z（1 秒）・edit_Z（4 秒、上げた左腕）・pl_s4（Zunko 4 秒）・pl_mocopi（Alicia 5 秒）・pl_s4back と edit_A（Alicia 2 秒）で姿勢が一致。違いは二次骨（bones の髪・帯のトラック。Blender では当てない）だけ。AliciaSolid は両方とも白一色で写る（6 の 10）。作業の出力は `SandBox/BlenderIO/stage4/`（`pl/` PolyLing の出力、`bl/` と `b36/` Blender の出力、`logs/`・`scripts/`・`render/`）。 |
| ⑤ | 完了 | 2026-10-20 | **契約**（`PolyLing_外部連携の契約.md`）：§5 に `rotationSource`（JSON の例と、再生の正本・`MotionClipSerializer.Parse` で bakedBones だけのファイルが "bakedBones" になること）を足し、§5.1「Blender との対応」（表すもの・割り当て表〔自動の規則・VRM アドオンからの取り込み〕・P0 と A・読込の式・Hips・書き出しの式・キーの決まり・表情と視線・読めないもの・書式）を新設。§7「Blender アドオン PolyLing IO（プロジェクトの読み書き）」（7.1 置き場所・入れ方・入口の表、7.2 Blender への置き方と控え、7.3 空間と姿勢、7.4 書き出し：元の値を使う決まりと許容差・Blender から作るもの・頂点数が変わったときに外すもの・書き出さないもの）を新設し、旧 §7 を §8 にした。連携計画の段 ②（旧）の「変換式・座標の決まりを契約に書く」は §5.1 で済み。**機能索引**（`PolyLing_機能の場所.md`）：§3「Blender で PolyLing のデータを扱う」（入れ方と 5 行）を足し、「この表を直すとき」を §4 にして Blender のメニューの行を足した。**マニュアル**：リポジトリに PolyLing 本体のマニュアルに当たるファイルが無かった（6 の 15）。**照合**：記載した式・欄・メニュー名・既定値・許容差を `format/plmotion.py`・`format/humanoid.py`・`blender/motion.py`・`blender/__init__.py`・`importer.py`・`exporter.py`（と `meshread.py`・`space.py`・`materials.py`・`format/csv_project.py`）と 1 項目ずつ照らして食い違い 0（許容差 2e-5・1e-5・1e-6・1e-3 フレーム・行列 1e-4・レスト 1e-5、既定値〔腰を当てる オン・脚 オフ・シーンを合わせる オン・名前で参照 オフ〕、演算子名、メニューとパネルの文字、属性名・カスタムプロパティ名、DTO の欄の順は `MotionClipDTO.cs` の宣言順と一致）。記載した C# の名前（`MotionClipDTO.cs`・`MotionClipApplier.cs` の冒頭と `FindExpression`・`ExpandToVertexMorphs`、`MotionClipSerializer.Parse`・`ToJson`、`UnityClipApplier.ResolveNode`・`CanonAlignment`、`UnityClipApplier.Apply.cs` の `ApplyCanonLocals`、`CanonSkeleton.cs` の `Table`、`ModelMotionSampler.cs` の T、`HumanoidBoneMapping.EmbeddedMapping`・`FindBoneByAliases`、`ModelContext.MeshList.cs` の `SyncDerivedMirrorTransforms`、`BoneTransform.cs` の `TransformMatrix`、`UnifiedBufferManager_Update.cs` の SkinningMatrix の注記、`MorphPreviewState.FindBaseMeshIndex`、`ProjectSerializer.cs`）は読んで確かめた。行番号は書いていない。文書を書いていて見つけた実装の食い違いは 6 の 11〜14。5 の 1 の手順をモーションを含めて書き直した。 |

---

## 5. 改善案（操作者の作業が要るもの）

1. **MCP から Blender を動かせない**（段 ② で確認）。device_bash は Linux の仮想環境で、Windows の blender.exe もアプリも呼べない（道具の説明どおり、接続したフォルダしか見えない）。段 ②〜④ は作業環境（クラウド）の Blender 3.6.23 Linux 版と bpy 4.2/5.2 で読込・書き出し・数値検査・描画まで行った。Windows の Blender での確認は操作者の作業（決まりの詳細は契約 §5.1・§7）：
   - 入れ方：`データとツール/poly_ling_py/polyling_io` フォルダをフォルダごと zip にし、Blender の［編集 > プリファレンス > アドオン］で入れて「PolyLing IO」を有効にする（3.6〜4.1 は「インストール…」、4.2 以降は右上のメニューの「Install from Disk」）。
   - プロジェクトの読込：［ファイル > インポート > PolyLing プロジェクト (.mfproj/.csv)］で `SandBox/bintest/A4/project.csv` と `SandBox/RoboFArms/RoboFArms.csv` を開く。正面（テンキー 1 のあと Ctrl＋テンキー 1 で反対側＝キャラクターの正面）で `SandBox/BlenderIO/stage2/` の画像と同じに見えればよい。
   - プロジェクトの書き出し：［ファイル > エクスポート > PolyLing プロジェクト (.mfproj/.csv)］で .mfproj か .csv の名前を付けて書き（書くのは選んだオブジェクトが入っているプロジェクト）、PolyLing の左の「ファイル」→「プロジェクト読み込み」で開く（段 ③ は作業環境で確認済み）。
   - モーションの読込：A4 を開いた状態で「Zunko_MMD_Armature」を選び、3D ビューのサイドバー（N キー）のタブ［PolyLing］の「モーションを読む」で `SandBox/BlenderIO/stage4/pl/pl_s4.plmotion.json` を「腰の回転と位置も当てる」を切って読む（割り当て表が空なら自動で割り当てる）。4 秒のフレーム（4 × fps。fps はファイルの frameRate に合わせてある）を正面から見て、`SandBox/Captures/BlenderIO/stage4_pair_s4_Z_t4.png`（左 PolyLing・右 Blender）と同じ姿勢ならよい。同じく「AliciaSolid_Armature」に `pl_mocopi.plmotion.json` を読んで 5 秒（`stage4_pair_mocopi_A_t5.png`）。
   - モーションの書き出し：同じアーマチュアを選んで「モーションを書く」で書く。読んだまま書いたものは元とバイト一致する（段 ④ で確認済み）。キーを足して書いたものは、PolyLing のコマンド `queryMotionJson` でエラー 0、または左の「その他」→「モーション」で読み込んで再生できればよい。
   - VRM Add-on for Blender が入っていれば、VRM モデルのアーマチュアで「VRM アドオンから取り込む」が押せ、表が埋まる。
   - 改善案：PolyLingMcpServer に道具 `blender_run`（起動引数 `--blender <blender.exe のパス>`、`blender.exe -b --factory-startup --python <作業フォルダ内のスクリプト> -- <引数>` を走らせ、標準出力と書き出した画像のパスを返す）を足す。スクリプトと出力は書き込み許可フォルダ（SandBox・データとツール）に限る。MCP サーバの再ビルドと起動引数の追加は操作者の作業。

---

## 6. 残件（範囲外で見つけたもの）

C# の CSV 読み書きの不具合（段 ① で確認。Python 側は 1・3・4 を正しく扱い、2・5 は C# と同じ書き方にした）：
1. `materialprops.csv` で、テクスチャのパスが空のプロパティは行末が `,` になり、`Split` が空欄を落として 9 列になるため、読込（`CsvModelSerializer.ModelCsv.cs` の `cols.Length < 10`）で全部捨てられる。A4・RoboFArms で実際に消える（CSV を読み直すとシェーダー固有プロパティが無くなる）。
2. 名前モードの CSV では `mirrorGeometryDerived` を書かない（`CsvMeshSerializer.Write.cs` の索引モード側だけ）。読みは両方のモードで読むので、書けば失われない。
3. 中身が空になっても消さないファイルがある（morphgroups・meshselsets・objectgroups・mirrorpairs・editorstate・workplane・tposebackup・springbonegroups・humanoid.export）。同じフォルダへ保存し直すと古い中身が読込で復活する。Python 側は空なら消す。
4. `tposebackup.csv` は boneRot と vtxPos が両方空だと、worldMat・bindPose の行があっても null として読む。
5. `springbonegroups.csv` で名前が空のグループは空行になり、読込で飛ばされて後ろの番号がずれる（Python 側も同じ形式なので、空の名前はずれる。直すなら書式の変更が要る）。

段 ② で見つけたもの（C# を読んで確認。今の試験データでは起きない）：
6. スキンドメッシュのウェイトの無い頂点の行列が、描画と編集で食い違う。描画はメッシュ自身の欄に SkinningMatrix（WorldMatrix·BindPose）を入れる（`UnifiedBufferManager_Update.cs:85-104`、頂点の欄はメッシュ自身 `MeshContext.Transform.cs` の注記 `UnifiedBufferManager_Build.cs:344-363`）が、`MeshContext.VertexMatrix` はウェイトの無い頂点に DisplayWorldMatrix を返す。メッシュ自身の BindPose が単位でないときだけずれる。Blender 側はこの頂点を動かさない（PolyLing の描画とはずれる）。

段 ③ で見つけたもの：
7. C# で `.mfproj` を読んでから CSV に保存すると、CSV を読んでから保存したものより行が足りない。A4 で、C# 自身が書いた `SandBox/BlenderIO/A4_cs.mfproj` を `loadProjectFile` → `saveProjectCsv` したもの（`SandBox/BlenderIO/stage3/cs_A4mf_orig`）と、`bintest/A4/project.csv` を `loadProjectCsv` → `saveProjectCsv` したもの（`stage3/cs_A4_orig`）を `queryCsvFolderDiff` で比べると、前者には bone.csv の `humanBodyBone`・`bindPose` の行、pmx_physics.csv の `rigidBody` の行、AliciaSolid の humanoid.export.csv が無い（4 ファイル・17380 行）。A4_cs.mfproj には `humanBodyBone`・`bindPose`・`rigidBodyData` の欄がある。原因の箇所は未調査。

段 ④ で見つけたもの（C# を読んで確認）：
8. モーションの再生で、A4 の Zunko_MMD の Spine が「kimono_spine_02.x」に割り当たる。このモデルのボーンは humanBodyBone を持たないので `UnityClipApplier.ResolveNode` は埋め込みの別名表を使い、`HumanoidBoneMapping.FindBoneByAliases` は別名ごとに「完全一致 → 部分一致」を回すため、最初の別名 "Spine" の部分一致（大小無視）が "上半身" より先に当たる。Blender 側の自動割り当ても同じ規則にしてある（表はパネルで直せる）。
9. グループの表情（MorphExpression の type 0）は保存すると子（`GroupChildren`）が失われる。`GroupChildren` は DTO に無く（`MorphExpression.cs:132`、Serialization に書く箇所が無い）、`MotionClipApplier.ExpandToVertexMorphs`（`:451-456`）はグループを `GroupChildren` だけで展開するので、CSV・.mfproj から読んだグループの表情はモーションで何も動かさない。A4 の AliciaSolid では VRM の表情（aa・blink・happy など 12 本）がこれに当たる（meshEntries は持っている）。Blender 側も PolyLing と同じく何にも当てない。
10. A4 の AliciaSolid は PolyLing の画面でも Blender（Cycles）でも白一色で写る（段 ④ のキャプチャ）。原因は未調査。

段 ⑤ で見つけたもの（ソースを読んで確認。直していない。実際に起きるかは試していない）：
11. Blender でシェイプキーの名前を変えると、そのモーフの形が書き出しで失われる。2.3 は「シェイプキーの名前の変更は書き出さない（保持した値のまま）」だが、`exporter.export_morphs` はシェイプキーを取り込み時の名前（`polyling_morphs` の `shapeKey`）で引くので、名前を変えたものは見つからず、警告「シェイプキー … が無いので差分 0 にした」を出してモーフの頂点を basePositions のまま書く。モーションの表情（`motion.expression_targets`）も同じ名前で引くので、そのシェイプキーに当たらなくなる。
12. Blender から `.mfproj` 由来のプロジェクトを CSV で書くと、`Assets/…` などの相対パスのテクスチャが写らない。`format/csv_project.py` の textures の写し（.mfproj から読んだとき）は、パスそのものと `_unityProjectRoot` を根にしたパスを探すが、`_unityProjectRoot` を入れる箇所が無いので、相対パスは Blender の作業フォルダ基準になる。C#（`ModelTextureTransfer.cs` の `ResolveFullPath`）は `Assets/…` を Unity のプロジェクトの根から探す。
13. `.plmotion.json` の `bakedBones` のトラックごとにキー時刻が違うファイルは、読んでそのまま書いても元のトラックに戻らず格子で標本化される。`motion.import_motion` は正準骨格の全トラックのキー時刻を合わせたものを、駆動するどのボーンにも打つ。`export_motion` はボーンのキーのフレームがそのトラックの元のキー時刻と同じ数・同じ時刻のときだけ元のトラックを使う。段 ④ の試験データ（全キー比較で一致）では起きていない。
14. `blender/motion.py` の冒頭の注記は、書き出しを「q = W_c(正準の親で割り当てのある一番近い祖先)⁻¹·W_c」と書いているが、実装（`export_motion` の `local_of`）は 2.4 どおり正準骨格の親そのもの（`CANON_PARENT`）を使う。その注記に当たる `Rig.canon_parent` はどこからも呼ばれていない。
15. マニュアル：リポジトリ（`Packages/com.haglib.polyling`・`データとツール`・`Assets`）に PolyLing 本体のマニュアルに当たるファイルが無い。`README.md` は導入と作業メモ、`MCP操作手順書.md` は MCP 向け、`データとツール/VRMビュワー/polyling_motion_studio/polyling_motion_studio_manual.html` はモーションスタジオのもの。Blender アドオンの入れ方と使い方は機能索引（`PolyLing_機能の場所.md` §3）と契約 §7.1 に書いた。

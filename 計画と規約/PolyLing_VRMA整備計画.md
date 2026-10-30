# PolyLing VRMA 整備計画

VRMA（VRMC_vrm_animation 1.0）の読み書きを C#・JS とも仕様どおりにし、PolyLing モーション（`.plmotion.json`）との不整合をなくす。
Blender 連携はこの計画が終わってから行う（対象外）。

作成：2026-10-11
仕様：https://github.com/vrm-c/vrm-specification/blob/master/specification/VRMC_vrm_animation-1.0/README.md

---

## 0. 進め方の決まり

- この文書が正本。会話が切れたら、次はこの文書だけを読んで続きから始める。
- 1 段＝1 回の会話で閉じる。段の終わりに「完了条件」をすべて満たし、チェック欄を更新してから終える。
- 承認はこの計画書に対して 1 回。段の途中で止まって承認を取りに行かない。
- 段の範囲外で見つけた問題は手を広げず「6. 残件」に書くだけにする。
- 大きいファイル（`timeline.html` 約 165KB など）は該当箇所だけ読む。
- 後方互換は考えない（モーション関連は過去資産なし）。

---

## 1. 着手時点の事実（調査済み）

### 1.1 仕様との突き合わせ
| 実装 | 判定 | 根拠 |
|---|---|---|
| C# 正準経路の書き出し（`UnityClipCanonVrmAnimation.ConvertToFile`） | 必須事項は満たす。表情・視線は書かない（任意なので違反ではないが `.plmotion` の表情を捨てる） | `UnityClipCanonVrmAnimation.cs:125-199`、`VrmAnimationExporter.cs:102-143`、`VrmAnimationUtil.cs`（目は humanBones に載らない） |
| C# モデル経由の書き出し（`UnityClipVrmAnimationSource`） | 違反：レストが T ポーズとは限らない（モデルのレスト位置のまま） | `UnityClipVrmAnimationSource.cs:57` |
| C# VMD 直接書き出し（`VmdVrmAnimationExport` の直接経路） | 腕だけ T ポーズ整列（決定どおり）。経路そのものを廃止対象とする | `VmdVrmAnimationExport.cs:251` |
| C# 読込 | 無い | `MotionSourceKind { Vmd, PolyLingMotionJson }`（`PanelCommand.Motion.cs:8-15`） |
| JS 書き出し（`timeline.html` `exportVrma`） | 違反：`lookUp/lookDown/lookLeft/lookRight` を preset に書きうる。重みを丸めない。指が骨格に無く捨てる | `timeline.html:1904, 2054-2058, 1912-1919` |
| JS 読込（`importVrma`） | 違反：表情の重みを [0,1] に丸めない。CUBICSPLINE は値だけ取り線形、STEP も線形。sparse を拒否。lookAt を捨てる | `timeline.html:1993, 1957-1958, 1945, 1995` |

### 1.2 不整合
- `bakedBones` の意味：C# は「元アバターの骨ローカル回転（`sourceRest` があればレスト補正）」（`UnityClipApplier.cs:96-98, 157-166`）。JS は「正準骨格上のローカル回転、`sourceRest` 空」（`timeline.html:1893, 1997, 1868-1875`）。
- マッスル→骨：JS `muscle_pose.js` は C# が実測で捨てた合成規則（dof 同時で最大 57.81° の差。`CanonMuscleSolver.cs:7-15`）。JS の表示・マッスルだけの VRMA 保存・ライブ受信の姿勢が C# と一致しない。
- 表情名→VRM プリセットの表は `.vrm` 書き出し（`Vrm10SceneAssembler.cs:592-620`）にしかない。
- `.plmotion` に視線の欄が無い（`MotionClipDTO.cs`）。
- VRMA への経路が 3 本（`CreateCommands.FileIO.cs:566, 611, 686`）、パネルも 3 つ（モーションパネルの VRMA 節、`PlayerUnityClipToVrmaSubPanel`、`PlayerVmdToVrmaSubPanel`）。
- C# の VRMA 書き出しは UniVRM の `VrmAnimationExporter` 任せで、表情・視線を書けない（`VrmAnimationExporterImpl.cs:38-40`）。

### 1.3 使い続けるもの
- `MotionClipDTO` / `MotionClipSerializer`（形式と検査）
- `CanonHumanPoseRig`（`GetHumanPose` / `SetHumanPose`）、`CanonMuscleSolver`
- `VmdMotionBake`（VMD＋モデル→マッスル＋表情）、`VmdPoseSession`（`UnityClipVrmAnimationSource.Build` を使うので `Build` は残す）

---

## 2. 決定事項

### 2.1 `.plmotion`（version 4 にする）
- **`bakedBones`** ＝正準骨格（VRM T ポーズ、レストの全ローカル回転が単位）上の Humanoid ローカル回転。targetKind は `humanoid`。
  Hips の `pos` は「レスト位置からの差 ÷ レストの腰の高さ」（腰高 1 の正規化）。
- **再生の正は `muscles`＋`body`**。`muscles` が無く `bakedBones` だけのファイルは、C# が読込時に正準骨格でマッスル＋body へ焼く。
- **C# が書く `.plmotion` には `bakedBones` を必ず添える**（マッスルから Unity（`SetHumanPose`）で解いた値）。Unity の外（JS など）は骨を動かすときにこれを使い、マッスルから自前で合成しない。
- **`lookAt`** トラックを追加：id・targetKind とも `lookAt`。キーは `rot`（VRMA の lookAt ノードの局所回転と同じ意味。Unity 左手系 `[x,y,z,w]`）。クリップに `lookAtOffsetFromHeadBone`（`[x,y,z]`、メートル、Unity 左手系、任意）。
- **`sourceRest`** は廃止（`bakedBones` の意味が正準で固定されるため不要）。
- 表情トラックの `provider`：`vrm1` は名前が VRM 表情名（preset 名または custom 名）。それ以外は 2.3 の表で VRM 名へ解決する。

### 2.2 VRMA の書き出し（C#・JS 共通の規則）
- レスト骨格は「5. 正準 VRMA 骨格」の表（C#・JS で同じ値）。レストの各ノードの回転は単位、拡大縮小なし。
- 回転チャンネル＝`bakedBones`（無ければマッスルから Unity で解いた値）。目（leftEye/rightEye）は humanBones に載せない。
- Hips の translation ＝ Hips のレスト位置＋正規化差分×レストの腰の高さ。
- 表情：preset 名は preset、それ以外は custom。`lookUp/lookDown/lookLeft/lookRight` は書かず件数を報告。重みは [0,1] に丸める。表情ノードは translation の X に重み。
- 視線：`lookAt` トラックがあれば lookAt ノードの rotation として書く。無く目のマッスルがあれば、正準骨格で目の向きを解き、左右の視線方向の平均から yaw・pitch（Extrinsic ZXY）を作って書く。`offsetFromHeadBone` はクリップの値、無ければ書かない。
- 補間は LINEAR、時刻は秒。拡大縮小チャンネルは書かない。Hips 以外の translation は書かない。

### 2.3 表情名→VRM 表情名の表
- `Vrm10SceneAssembler.ResolvePreset` と `PresetByName`（`Vrm10SceneAssembler.cs:592-620`）を Runtime 本体へ移し、`.vrm` 書き出しと `.vrma` 書き出しで共用する。

### 2.4 VRMA の読込（C#・JS 共通の規則）
- 最初の animation だけを読む（仕様どおり）。
- サンプラ：LINEAR / STEP / CUBICSPLINE を仕様どおりに評価。accessor：sparse、正規化整数、byteStride に対応。ノード：TRS と matrix の両方。
- 回転：各 Humanoid ボーンのレストワールド回転を基準に「親 Humanoid のレストワールド回転 · q · 自分のレストワールド回転⁻¹」で正準へ移す（レスト階層は任意の回転・拡大縮小を許す）。glTF⇔Unity は X 反転。
- Hips の位置：レスト位置からの差 ÷ レストの腰の高さ。
- 表情：translation の X を [0,1] に丸める。provider は `vrm1`。
- 視線：lookAt ノードの rotation を `lookAt` トラックへ、`offsetFromHeadBone` をクリップへ。C# はさらに目のマッスルへも反映する。
- 拡大縮小チャンネル、Hips 以外の translation、未対応の拡張は読まずに件数を報告。

### 2.5 経路
- VRMA への出口は「`.plmotion` → `.vrma`」1 本。VMD は `VmdMotionBake`、Unity クリップは `.plmotion` を経由する。
- VRMA からの入口は「`.vrma` → `.plmotion`」1 本。モーションパネルは `MotionSourceKind.Vrma` で直接読める。
- UniVRM への依存（`IVrmAnimationExporter` / `PLVrmAnimationBridge` / `VrmAnimationExporterImpl` / `VrmAnimationExporterNull`）はやめる。GLB の読み書きは Runtime 本体で自前に行う。

### 2.6 ライブ
- PolyLing が送る `muscles` メッセージに、Unity で解いた骨の回転（正準骨格上のローカル回転、`bones:{"<Humanoid名>":[x,y,z,w]}`）を添える。JS はこれで骨を動かす。

---

## 3. 段と完了条件

### 段 ① 契約・DTO・シリアライザ
対象：`MotionClipDTO.cs`、`MotionClipSerializer.cs`、`PolyLing_外部連携の契約.md`
- [x] version 4：`lookAt` トラック、`lookAtOffsetFromHeadBone` を追加。`sourceRest` を削除。
- [x] 検査：`lookAt` の id・targetKind・rot だけ、`bakedBones` は humanoid のみ・id は HumanBodyBones 名・scl 不可・pos は Hips だけ、`lookAtOffsetFromHeadBone` は 3 個。
- [x] version 1〜3 は 4 へ引き上げて読む（`sourceRest` は捨てて警告。旧版の `bakedBones` も意味が正準と限らないため捨てて警告）。
- [x] 契約 §5 を 2.1 に合わせて書き直す。
- [x] `MotionClipApplier.SetClip` の `sourceRest` 受け渡しを削除（`LoadSourceRestCsv` 以下は段 ⑤）。
- [x] PolyLing_mocopi の `MocopiClipBuilder`：mocopi 骨の `bakedBones`・`sourceRest`・ソース rest CSV の書き出しをやめた（`MocopiRecorderApp` の呼び出しも合わせた）。
- 完了条件：3 本の csproj でコンパイルエラー 0。既存の `.plmotion` を `queryMotionJson` で読んで version 4 として通る。

### 段 ② C# 共通部品と VRMA 書き出し
対象：新規 `Motion/CanonBoneMotion.cs`（マッスル⇄正準回転・視線）、新規 `Motion/VrmaCodec.cs`（GLB 書き出し）、新規 `Motion/VrmExpressionNames.cs`（2.3）、`Vrm10SceneAssembler.cs`（表の参照先を変更）、`MotionClipSerializer`（保存時に `bakedBones` を添える）
- [x] マッスル＋body → 正準回転（`SetHumanPose`）、正準回転＋Hips → マッスル＋body（`GetHumanPose`）。
- [x] 目のマッスル⇄lookAt 回転。
- [x] `.plmotion` → `.vrma`（2.2 のすべて）。
- 完了条件：コンパイル 0。マッスル・表情・目のマッスルを持つ `.plmotion` から書いた `.vrma` を、UniVRM の `VrmAnimationImporter` で読ませて例外なし。humanBones に目が無い、表情に look 系が無い、拡大縮小チャンネルが無いことを JSON で確認。

### 段 ③ C# VRMA 読込
対象：`VrmaCodec.cs`（GLB 読込）、`PanelCommand.Motion.cs`（`MotionSourceKind.Vrma`）、`PlayerCommandDispatcher.Motion.cs`、モーションパネルの読込
- [x] 2.4 のすべて。
- 完了条件：コンパイル 0。段 ② で書いた `.vrma` を読み戻し、元の `.plmotion` とマッスル・body・表情・lookAt を全キーで比較（許容差を計画書に記録）。モデルに当てた再生を変更前後でキャプチャ。

### 段 ④ 経路・パネル・コマンドの整理
対象：`PanelCommand.Vrm.cs`、`CreateCommands.FileIO.cs`、`PlayerCommandDispatcher.cs`、`PlayerMotionClipTestSubPanel.cs`、`PlayerUnityClipToVrmaSubPanel.cs`、`PlayerVmdToVrmaSubPanel.cs`、`UnityClipVrmAnimationSource.cs`（書き出し部）、`VmdVrmAnimationExport.cs`（直接経路）、`UnityClipCanonVrmAnimation.cs`、`IVrmAnimationExporter.cs` ほか橋渡し一式、UI 自動操作・機能索引の登録
- [x] VRMA コマンドを「書き出し（`.plmotion`→`.vrma`）」「読込（`.vrma`→`.plmotion`）」「VMD＋モデル→`.plmotion`＋`.vrma`」に整理。
- [x] 旧経路・旧パネル・UniVRM 橋渡しを削除（削除は `delete_path`）。
- 完了条件：コンパイル 0。MCP から 3 コマンドを実行し出力を段 ②③ の検査に通す。パネルからも同じ操作をしてキャプチャ。

### 段 ⑤ `sourceRest` 経路の削除と `bakedBones` 読込時焼き込み
対象：`UnityClipApplier*.cs`（`ApplyRetargetedBody`・ソース rest 一式・UnityBone CSV 読込）、`MotionClipApplier.cs`、`MotionClipHandler.cs`、`VmdPoseSession.cs`／`VmdNodeWorldSampler.cs`（参照があれば）
- [x] `bakedBones` だけのファイルを読込時にマッスル＋body へ焼く。
- 完了条件：コンパイル 0。`bakedBones` だけの `.plmotion`（段 ② の出力からマッスルを除いたもの）を再生し、元の再生とキャプチャで一致。

### 段 ⑥ ライブに骨の回転を添える
対象：`MotionLiveHandler.cs`、`MotionLiveJson.cs`、契約 §4.1
- [x] 2.6。
- 完了条件：コンパイル 0。再生配信を受けた JSON に `bones` が入ることを確認。

### 段 ⑦ JS（モーションスタジオ）
対象：`timeline.html`（`.plmotion` 読み書き・VRMA 読み書き）、`muscle_pose.js`、`vrm_viewer.html`、`polyling_motion_studio.html`、マニュアル
- [x] `.plmotion` version 4（`lookAt`・`bakedBones` の意味・`sourceRest` 廃止）。
- [x] 骨の回転は `bakedBones` かライブの `bones`。`muscle_pose.js` の自前合成を廃止。
- [x] VRMA 読み書きを 2.2・2.4 に合わせる。正準 VRMA 骨格は 5 の表。
- 完了条件：組み込みブラウザで、段 ② の `.vrma`・`.plmotion` を読み、JS で書いた `.vrma` を C#（段 ③）で読んで全キー比較。

---

## 4. 進捗

| 段 | 状態 | 日付 | メモ |
|---|---|---|---|
| ① | 完了 | 2026-10-11 | dotnet build（PolyLing.Runtime / Editor / Vrm10 / Assembly-CSharp）0 件、Unity 再コンパイル 0 件。queryMotionJson：v3（sourceRest・bakedBones 入り）→ 警告 2 件で v4 として通る／v4 正常 → 0 件／v4 異常 → 期待した 7 件のエラー／既存 POSE01（v3）→ 通る。exportMotionJson で書き直したファイルは version 4、lookAt・lookAtOffsetFromHeadBone を保持。試験ファイルは `SandBox/VrmaPlan/s1_*` |
| ② | 完了 | 2026-10-11 | 新規：`Motion/CanonSkeleton.cs`（5 の表の正本）、`Motion/CanonBoneMotion.cs`（＋`MotionMuscleSampler`）、`Motion/VrmaCodec.cs`、`Motion/VrmExpressionNames.cs`。`CanonHumanPoseRig` の骨格を `CanonSkeleton` に替えた（再生・MediaPipe・VMD 焼き込みも同じ骨格になる）。旧骨格と新骨格で同じマッスルを当てたローカル回転の差は 300 標本・全ボーンで最大 0.000°（一時コードで実測後に削除）。`MotionClipSerializer.Save` が muscles を持つ DTO に bakedBones（frameRate 格子・駆動ボーン＋Hips）を添える。`Vrm10SceneAssembler` の表は `VrmExpressionNames` を引く（英語名→本名→PMX 名の順。本名が preset 名でも当たるようにした）。`convertUnityClipToVrma` の中身を `VrmaCodec` に差し替え（引数の整理は段 ④）。Unity コンパイル 0 件。試験：`SandBox/VrmaPlan/s2_src`（マッスル 12・目 4・body・表情 5〔あ/まばたき/上/happy(1.3 まで)/myCustom〕・lookAtOffset）→ exportMotionJson で `s2_baked`（bakedBones 10 本、エラー 0）→ `.vrma` 2 本（muscles 経路・bakedBones 経路）。JSON：humanBones 52（目・あご無し）、preset aa/blink/happy・custom myCustom、`上`→lookUp は書かず 1 件報告、重み 0～1（丸め 34 件）、channel は rotation と translation（Hips と表情ノードだけ）、LINEAR、scale 無し、lookAt ノード（Head の子）と offsetFromHeadBone あり。2 経路の回転差 最大 0.045°、Hips 差 0。s2 と WALK00_F の `.vrma` を UniVRM の ScriptedImporter（VrmAnimationImporter）で取り込み、エラー・例外 0（確認用ファイルは削除） |
| ③ | 完了 | 2026-10-11 | 新規 `Motion/VrmaCodec.Read.cs`（`VrmaCodec.Load` / `Decode`。`VrmaCodec` は partial に）。`MotionSourceKind.Vrma`（=2）を追加し、`exportMotionJson`・`motionClip` ツールの `load`（sourceKind=2）・モーションパネルのソース種別（「VRM アニメーション (.vrma)」、拡張子 vrma）から読める。検査コード `VRMA_INVALID` / `VRMA_NOT_READ` を追加。読込は使うチャンネルの入力時刻の和を格子にして線形キーにする（STEP は段の 1e-4 秒前にも点、CUBICSPLINE は区間 4 等分の点を足す）。書くマッスルはファイルにあるボーンの分（目は lookAt があるときだけ）。**段 ② の修正**：トラックの無いマッスルを 0 でなくレスト（T ポーズ）のマッスル値で解くようにした（`CanonBoneMotion.RestMuscles`、`MotionMuscleSampler.Sample` の引数）。0 で解くと駆動されていないボーンが「マッスル 0 の姿勢」になり、回転は単位扱いなのに body（重心）だけその姿勢で決まって、読み戻しの body が 2.1 cm ずれた。試験と許容差：(a) s2（段 ②）の `.vrma` → `s3_from_vrma`：元のマッスル 12 本は最大差 3e-5（目 4 本は lookAt 経由で 7e-4）、body 位置 1.6e-5・向き 0.03°、表情は全キー一致（あ→aa、まばたき→blink、happy は 0～1 に丸めた値と一致、上＝lookUp は元から書かれない）、bakedBones の回転 0.07° 以内・Hips 位置 1e-7。許容差はマッスル 1e-3、body 位置 1e-4、回転 0.1°、表情 0 とする。元で駆動されていなかったボーンのうち親指は単位から 4～7.3° ずれる（Unity のマッスルが親指の単位姿勢を表せない分。表現の限界で、ずれではない）。(b) 自作の `s3_edge.vrma`（Armature が matrix で 0.01 倍、Hips のレスト回転 Y90°・Spine のレスト回転 Y-90°、Hips は CUBICSPLINE、Spine は正規化 short＋byteStride 12、Hips 位置は sparse、表情は STEP、scale チャンネル・未対応拡張・2 本目の animation つき）→ 期待値（Python で計算）と一致：Hips 回転 t=0.5/1 で z=0.1736/0.3420、Spine t=1 で z=-0.2588、Hips 位置 t=0/0.5/1 で (0,-1,0)/(0,-0.444,0.111)/(0,0.111,0.222)、blink は 0.4999 秒で 0・0.5 秒で 1、警告 3 件（拡張・animation・scale）。(c) Alicia にモーションを当てて t=1.8 秒で、元の `.plmotion` と `.vrma` 直読みのキャプチャが一致（`SandBox/Captures/VrmaPlan/s3_*_t18_final_0001.png`） |
| ④ | 完了 | 2026-10-11 | コマンドは 3 本：書き出し `convertMotionToVrma`（`ConvertMotionToVrmaCommand`。`.plmotion`→`.vrma`、`VrmaCodec.WriteFile`）、読込 `exportMotionJson`（sourceKind=Vrma）、VMD＋モデル `exportVmdToMotion`（`ExportVmdToMotionCommand`。`VmdMotionBake.Bake`→`.plmotion` 保存→`vrmaFilePath` 指定時は保存した DTO から `.vrma`）。旧コマンド `exportVrmAnimation`・`convertUnityClipToVrma`・`exportVmdToVrma`・`exportVmdToMotionJson` は削除。`delete_path` で削除：`SubPanels/UnityClip/`（`PlayerUnityClipToVrmaSubPanel`）、`UnityClipCanonVrmAnimation.cs`、`VRM/IVrmAnimationExporter.cs`、`PLVrmAnimationBridge.cs`、`VrmAnimationExporterNull.cs`、`VrmAnimationExportTypes.cs`、`Poly_Ling_Vrm10/VrmAnimationExporterImpl.cs`。`UnityClipVrmAnimationSource.ExportToFile` を削除（`Build`/`PoseFrom` は VmdPoseSession 用に残す）。`VmdVrmAnimationExport.cs` は採取時刻・探針の補助だけ。モーションパネルの VRMA 節は倍率・可動域 CSV・橋渡し確認を外し `ConvertMotionToVrmaCommand` を送る。VMD パネルは「VMD→モーション・VRMA書き出し」1 ボタン＋「VRMA も書く」（同名 `.vrma`）。UI 自動操作・機能索引・左ペイン・WorkAxis を更新（`queryUiAutomationAudit` 未登録節 0）。コンパイル 0（4 csproj）。MCP 実行（`__AちゃんH.pmx`＋`キャッチボール（トマホーク）.vmd`）：`exportVmdToMotion`→`SandBox/VrmaPlan/s4_vmd.plmotion.json`＋`s4_vmd.vrma`（249 フレーム、表情 28→preset 8・custom 18・look 2 落とし、lookAt は目マッスルから）、`convertMotionToVrma`→`s4_from_plmotion.vrma`、`exportMotionJson`(Vrma)→`s4_back.plmotion.json`（エラー 0、bakedBones 54・muscles 93・表情 26）。両 `.vrma` の JSON 検査：ボーン 52・目なし・look preset なし・rotation/translation のみ・LINEAR・translation は Hips と表情だけ・lookAt ノードあり。UniVRM の ScriptedImporter で `s4_check.vrma` を取り込みエラー 0（確認後に削除）。パネルのキャプチャ：`SandBox/Captures/VrmaPlan/s4_vmdpanel_0001.png`・`s4_vmdpanel_only_0001.png`・`s4_vmdpanel_save_0001.png`・`s4_motionpanel_0001.png`。パネルの読込・保存・書き出しボタンはファイルダイアログを開く UserOnly のため MCP からは押していない（6 残件）。 |
| ⑤ | 完了 | 2026-10-11 | 新規 `CanonBoneMotion.BakeMuscles`：muscles が無く bakedBones があるとき、bakedBones（と目に使う lookAt）のキー時刻の和を格子に、正準骨格へ回転＋Hips を入れて GetHumanPose でマッスル＋body を作る（接線つき区間は 4 等分点も足す。書くマッスルは bakedBones にあるボーンの分。目は LeftEye/RightEye が無く lookAt があれば lookAt から解く。正準骨格に無いボーンは使わず警告。元の body は捨てて警告）。`MotionClipSerializer.Parse` が検証のあとに呼ぶ。再生は muscles＋body だけ：`MotionClipApplier` の bakedBones ビュー・`HasSourceRest`/`LoadSourceRestCsv`/`ClearSourceRest`/`BodyMode` を削除、結び付け報告と駆動ノードから bakedBones を外した。`UnityClipApplier`：`BodySource`/`BodyMode`/`ResolvedBodyMode`、`ApplyRetargetedBody`・`ApplyLocalRotationToBone`・`ParentOf`/`DepthOf`、ソース rest 一式（`SourceRest`/`_sourceRest`/`HasSourceRest`/`SetSourceRest`/`LoadSourceRestCsv`/`ClearSourceRest`）、未使用になった `QMul`/`QConj`/`QNorm`/`QMir`/`Mx`/`Q.From` を削除。`UnityClipDTO.bakedBones` を削除。`motionClip` ツールの `loadBind`・`bindBoneCount` とモーションパネルの「バインドポーズ CSV」節（`bindPoseFile`/`bindPosePath`/`openBindPose`/`browseBindPose`）を削除。トラック一覧・総数は bones だけ。`VmdPoseSession`/`VmdNodeWorldSampler` に参照は無かった。コンパイル 0（4 csproj）、Unity 再コンパイル後の実行ログのエラー 0。試験（`__AちゃんH.pmx`、`motionClip` の `load`／`setTime`）：bakedBones だけのファイルは `exportMotionJson` で写した `s5_s2_bakedonly`・`s5_s4_bakedonly` の `muscles`・`body` を `removedMuscles`・`removedBody` へ改名して作った（読込では無視される）。`queryMotionJson`：s2 → muscles 23（bakedBones の 9 ボーンの全 dof）、s4 → muscles 93（元 95 のうちあご 2 本は正準骨格に無い）、どちらもエラー 0。(a) s4（全 dof あり）：t=1.8 で元と一致（`s5_s4_orig_t18_0001` / `s5_s4_bakedonly_t18_0001`）。t=4.0 は全身では一致、脚の拡大で上げた左足先が数 px ずれる（`s5_s4_*_t40_legs_*`）。原因はその姿勢（左ひざ Stretch 0.994）で GetHumanPose のねじりの配分が元と変わること（Left Upper Leg Twist −0.321→−0.174、Left Lower Leg Twist 0.059→−0.039。ほかの脚マッスルの差は 3e−4 以下）。段 ④ の `.vrma` 読み戻し `s4_back` も同じ −0.174 で、段 ⑤ の新しいコードに固有ではない。(b) s2：右腕の位置が元と違った（`s5_s2_orig_t18_0002` / `s5_s2_bakedonly_t18_0001`）。原因は「駆動されているボーンのトラックの無い dof」の値が再生（0）と bakedBones 焼き込み（T ポーズの値）で食い違っていたこと。**追補（同日、承認済み）**：`.plmotion` に `absentMuscles`（`"zero"` / `"rest"`、省略は `"zero"`）を追加し、再生（`UnityClipApplier.ApplyMuscleValues`。T ポーズの値は `CanonMuscleSolver.RestMuscles`）・焼き込みと VRMA 書き出し（`MotionMuscleSampler.Sample`）が同じ値を使うようにした。トラックが 1 本も無いボーンは従来どおり（再生は当てない、焼き込みは T ポーズの値）。選ぶ場所：モーションパネル「無い dof の値」（`motionClipTest.absentMuscles`）、`motionClip` ツール `setAbsentMuscles(mode,time)`／状態 `absentMuscles`、`exportMotionJson` の引数 `absentMuscles`（パネルの JSON 保存はこれで選択を書く）。検査は値が zero / rest 以外ならエラー。契約 §5 に追記。ライブ受信は常に 0。確認：s2_src を zero / rest で書き出し（`s5b_s2_zero` / `s5b_s2_rest`）、それぞれの bakedBones だけのファイルを t=1.8 で元と比べて一致（`s5b_zero_orig_t18_0001`＝`s5b_zero_bakedonly_t18_0001`、`s5b_rest_orig_t18_0001`＝`s5b_rest_bakedonly_t18_0001`）。zero のファイルを読んでツールで rest へ切り替えた姿勢も rest と一致（`s5b_zero_switched_rest_t18_0001`）。パネルの選択欄（`s5b_motionpanel_absent_0001`）を uiSetValue で切り替えて状態が zero になることを確認。`absentMuscles:"foo"` はエラーで書き出さない。コンパイル 0（4 csproj）、`queryUiAutomationAudit` でモーションパネルの指摘なし |
| ⑥ | 完了 | 2026-10-11 | `MotionLiveFrame` に `BoneRot`／`BoneValid`（HumanBodyBones 添字）と `ClearBones` を追加し、`MotionLiveJson.Build` が有効な骨を `"bones":{"<HumanBodyBones 名>":[x,y,z,w]}` で書く（無ければ欄ごと書かない）。`MotionLiveHandler.BroadcastFrame` が送る前に `FillBones`：`CanonBoneMotion` を 1 つ持ち、フレームのマッスル（＋rootT/rootQ。無ければレストの body）を正準骨格に当てて、駆動されているボーン（マッスルを 1 本でも持つ）と Hips のローカル回転を入れる。値の無いマッスルは、駆動されているボーンの dof なら 0（受信側 `ApplyMuscleFrame` と同じ）、駆動されていないボーンは T ポーズの値（単位ローカル）。外から受けて転送するフレームには付けない（受信スレッドで Avatar を使えないため）。状態 `bonesError`・`lastBoneCount` を追加。`absentMuscles = "rest"` のクリップの再生配信は、駆動されているボーンのトラックの無い dof を T ポーズの値で埋めて `muscles` に載せる（`MotionClipApplier.TrySampleMuscleFrame`。値は新設の `UnityClipApplier.CanonRestMuscles`）。契約 §4.1 に `bones` を追記。コンパイル 0（4 csproj）、Unity 再コンパイル後のエラー 0。確認（`__AちゃんH.pmx`、`motionLive.accept(12361,false)`、組み込みブラウザの WebSocket で受信、`motionClip.playFrame(1.8)`）：`s5b_s2_zero` → source playback、muscles 12、rootT/rootQ あり、bones 10 本（Hips・LeftUpperLeg・LeftLowerLeg・Spine・Head・LeftUpperArm・RightUpperArm・LeftLowerArm・LeftEye・RightEye）、Hips・Head・LeftUpperArm が同ファイルの bakedBones（t=1.8）と全桁一致。`s5b_s2_rest` → muscles 23（Right Arm Front-Back 0.3000543 を補って送る）、RightUpperArm が bakedBones と全桁一致 |
| ⑦ | 完了 | 2026-10-11 | 新規 `polyling_motion_studio/plmotion_vrma.js`（`window.PLVrma`／Node では `module.exports`。DOM 不使用）：正準骨格の表（`CanonSkeleton.Table` と同値）、`encode(dto,{fps})`（`VrmaCodec` の書き出しを写したもの。回転は bakedBones だけから作り、無ければ「PolyLing で書き直して」の例外。視線は lookAt トラック、無ければ bakedBones の目から `CanonBoneMotion.TryGetLookAt` と同じ式。表情の名前の表・look preset 落とし・重複・丸めも C# と同じ。ノード順は骨格→lookAt→表情）、`decode(buffer,name)`（`VrmaCodec.Read` の全移植：GLB／.gltf、data URI、matrix 分解、sparse・正規化整数・byteStride、STEP／CUBICSPLINE の格子、ワールド差分の式、Hips 正規化、lookAt と offset、同じ日本語の警告。muscles・body は作らず bakedBones・表情・lookAt の v4 DTO を返す）。`timeline.html`：`muscle_pose.js` → `plmotion_vrma.js`。`isBodyTrack` → `isAuxTrack`（body・lookAt）。`sendPose` は骨のトラックだけを送り、Hips の pos ＝ 正規化差 × `REST_HIPS_Y`（rootT／rootT0 を廃止。マッスルだけのときは 1 回だけ注意を出す）。`importPolyLing` は v1〜v4。v3 以前の bakedBones と sourceRest は読まずに注意。lookAt をボーン型トラック（plRole lookAt）に、`absentMuscles`・`lookAtOffsetFromHeadBone` を plMotion に保持。読込時に bakedBones の控え（`bakedSig`）を取る。`exportPolyLing` は v4（sourceRest なし、lookAt・offset・absentMuscles を書く。bakedBones は Hips 以外の pos と全 scl を落とす。muscles があり bakedBones が読込時から変わっていれば muscles と body を書かず、トーストで知らせる）。VRMA 読込＝`PLVrma.decode`→`importPolyLing`、保存＝`PLVrma.encode(exportPolyLing())`。旧 VRMA コード（`DEFAULT_SKEL`・`m4*`・`glbParse`・`accRead` など）は削除。`polyling_motion_studio.html`：ライブ受信は `m.bones`（＋新設の `m.hips` × 腰の高さ）をそのまま表示、bones の無いフレームは表示せず件数を出す。`vrm_viewer.html`：Hips の位置＝レストの腰＋pos × VRM の腰の高さ（rootT／rootScaleRef の経路を削除）。`muscle_pose.js` は `delete_path`。C#：`MotionLiveHandler` が外から受けたフレームもメインスレッドで `FillBones` してから転送（最新 1 件。source／seq／time は送り手のまま）。`MotionLiveFrame` に `HasHips`／`HipsOffset`、`MotionLiveJson.Build` が `"hips":[x,y,z]`（正規化位置）を書く。契約 §4.1・マニュアル・画面のヘルプを更新。PolyLing.Runtime コンパイル 0、Unity 再コンパイル後のエラー 0。確認：(a) JS の `decode(s3_edge.vrma)` が段 ③ の期待値と一致（警告 3 件も同文）。(b) JS の `encode(s2_baked)` と C# の `s2_from_baked.vrma`：ノード・拡張・チャンネル構成が一致、回転差 1.3e−5°、Hips 7e−9、表情 4e−7、lookAt（bakedBones の目から）2.7e−6°、`上` 落とし・丸め 34 件も同じ。(c) JS で 10 fps で書いた `.vrma` を C# で読んだ `s7_js_back` と、C# で書いた `s7_cs_fps10.vrma` を読んだ `s7_cs_back` の全キー（3213 個）：マッスル 1.1e−6、body 位置 2.7e−7・向き 3.4e−6°、bakedBones 回転 6.4e−5°・Hips 1e−8、lookAt 2.4e−6°、表情 0（許容差は段 ③ のもの）。JS の `.vrma` は MCP で 2 進を渡せないため、バッファ表を重複除去して data URI に入れた .gltf 形式（`SandBox/VrmaPlan/s7_js_fps10_gltf.json`）で渡した（JS の decode で元の GLB と同一の DTO になることを確認）。キャプチャ `SandBox/Captures/VrmaPlan/s7_js_back_t1_0002.png`＝`s7_cs_back_t1_0001.png`。(d) `timeline.html` を jsdom で動かし（undo_stack.js 同梱、ResizeObserver・TextEncoder だけ補う）：s2_baked を読む→v4 で保存（bakedBones の回転差 1.2e−7、Hips 以外の pos なし）→タイムラインの VRMA 保存が C# の `s2_from_baked.vrma` と構成一致・回転 1.3e−5°。骨を 1 キー変えると muscles 0・body なしで保存。s3_edge.vrma を VRMA 読込（警告 3 件）、5 フレーム目の pose が骨 2 本・Hips の pos を持つ。(e) ライブ：`motionLive.accept(12361)` に組み込みブラウザから 2 本つなぎ、片方から送ったマッスルの JSON が、もう片方へ bones 4 本・hips 付きで転送されることを確認。組み込みブラウザはローカルの HTML を開けないため、モーションスタジオの画面そのものはブラウザでは動かしていない（6 残件） |

---

## 5. 正準 VRMA 骨格

正本は `Runtime/Poly_Ling_Main/Motion/CanonSkeleton.cs` の `Table`（変えたらこの表も直す）。C#・JS で同じ値を使う。
VRM T ポーズ、+Z 正面、Unity 左手系（キャラクターの左が -X）、メートル、レストのローカル回転はすべて単位・拡大縮小なし。
位置は「親から見たローカル位置」。Hips の親は根（原点）。各ボーンの「自分→子」の向きは `UnityClipApplier.CanonDirTable` と同じ。
glTF へは X を反転して書く（位置 (-x, y, z)、回転 (x, -y, -z, w)）。

- `.vrma` に載せるのは目・あご以外の 52 本。目（LeftEye / RightEye）は C# の Humanoid で視線のマッスルを解くためだけに持つ（`.vrma` には載せない）。
- VRM の humanBones 名は Unity 名の頭を小文字にしたもの。ただし親指は 1 段ずれる（Unity ThumbProximal / Intermediate / Distal ＝ VRM thumbMetacarpal / thumbProximal / thumbDistal）。
- lookAt ノードは Head の子に置く（translation は offsetFromHeadBone があればその値）。表情ノードは最後に並べる（UniVRM の読込が表情ノードを後ろから消すため）。

| ボーン | 親 | x | y | z |
|---|---|---|---|---|
| Hips | （根） | 0 | 1 | 0 |
| Spine | Hips | 0 | 0.1 | 0 |
| Chest | Spine | 0 | 0.12 | 0 |
| UpperChest | Chest | 0 | 0.12 | 0 |
| Neck | UpperChest | 0 | 0.16 | 0 |
| Head | Neck | 0 | 0.1 | 0 |
| LeftEye（C# のみ） | Head | -0.03 | 0.07 | 0.08 |
| RightEye（C# のみ） | Head | 0.03 | 0.07 | 0.08 |
| LeftUpperLeg | Hips | -0.09 | -0.05 | 0 |
| LeftLowerLeg | LeftUpperLeg | -0.0105 | -0.419869 | 0 |
| LeftFoot | LeftLowerLeg | 0 | -0.42 | 0 |
| LeftToes | LeftFoot | 0 | 0 | 0.12 |
| RightUpperLeg | Hips | 0.09 | -0.05 | 0 |
| RightLowerLeg | RightUpperLeg | 0.0105 | -0.419869 | 0 |
| RightFoot | RightLowerLeg | 0 | -0.42 | 0 |
| RightToes | RightFoot | 0 | 0 | 0.12 |
| LeftShoulder | UpperChest | -0.02 | 0.12 | 0 |
| LeftUpperArm | LeftShoulder | -0.09993 | 0.00373 | 0 |
| LeftLowerArm | LeftUpperArm | -0.26 | 0 | 0 |
| LeftHand | LeftLowerArm | -0.24 | 0 | 0 |
| LeftThumbProximal | LeftHand | -0.02 | -0.01 | 0.025 |
| LeftThumbIntermediate | LeftThumbProximal | -0.028284 | 0 | 0.028284 |
| LeftThumbDistal | LeftThumbIntermediate | -0.021213 | 0 | 0.021213 |
| LeftIndexProximal | LeftHand | -0.08 | 0 | 0.02 |
| LeftIndexIntermediate | LeftIndexProximal | -0.04 | 0 | 0 |
| LeftIndexDistal | LeftIndexIntermediate | -0.025 | 0 | 0 |
| LeftMiddleProximal | LeftHand | -0.08 | 0 | 0 |
| LeftMiddleIntermediate | LeftMiddleProximal | -0.045 | 0 | 0 |
| LeftMiddleDistal | LeftMiddleIntermediate | -0.03 | 0 | 0 |
| LeftRingProximal | LeftHand | -0.075 | 0 | -0.02 |
| LeftRingIntermediate | LeftRingProximal | -0.04 | 0 | 0 |
| LeftRingDistal | LeftRingIntermediate | -0.025 | 0 | 0 |
| LeftLittleProximal | LeftHand | -0.07 | 0 | -0.04 |
| LeftLittleIntermediate | LeftLittleProximal | -0.03 | 0 | 0 |
| LeftLittleDistal | LeftLittleIntermediate | -0.02 | 0 | 0 |
| Right* | （Left* の x の符号を反転したもの。親も Right*） | | | |

指は 2026-10-14 に VRM 1.0 の T ポーズ（`tpose.md` 1.7・1.8）へ合わせた（`PolyLing_Blender連携計画.md` 6）。旧値は親指 Intermediate (-0.029233, -0.012881, 0.024073)・Distal (-0.021926, -0.009717, 0.018023)（水平面で X から 39.5°、18.8° 下向き）、4 本の指の Intermediate・Distal は 1.25〜1.35° 上向き。

---

## 6. 残件（範囲外で見つけたもの）

- 段 ② で `MotionClipSerializer.Save` が bakedBones を焼く前にキーを並べ替えるようにしたため、Save では「キーを並べ替えた」警告が出なくなった（読込の Parse では従来どおり出る）。
- モーションパネルの VRMA 書き出しの節は、ソース種別が「PolyLing モーション」のときだけ使える（段 ④ で決着：書き出しは `.plmotion`→`.vrma` の 1 本。VMD・VRMA 読込時は理由を表示し、JSON保存か VMD パネルへ誘導）。
- 段 ④ のパネル操作（VMD パネルの「開く」「保存」、モーションパネルの「開く」「VRMA 書き出し」）はファイルダイアログを開く UserOnly のため MCP から押せない。手での確認が要る（操作者の作業）。モーションパネルは `motionClip` ツールの `load` で読ませてもパネル側の `_filePath`・時刻表示は更新されない（パネルの読込はダイアログ経由だけ）。
- Editor の `UnityClipExportWindow`（Play 外）からの Save でも bakedBones を焼く（Avatar を Edit モードで組む）。段 ② では Play 中の経路だけ確かめた。

- 【解決済み・段 ⑤ 追補】駆動されているボーンのトラックの無い dof の値が再生（0）と bakedBones 焼き込み（T ポーズの値）で食い違っていた件は、`absentMuscles` で選べるようにして揃えた（4 進捗の段 ⑤）。
- ライブ受信（`ApplyMuscleFrame`）は届かなかったマッスルを常に 0 で解く（`absentMuscles` を持たない）。PolyLing の再生配信は段 ⑥ で `rest` の dof を `muscles` に補って送るので揃う。外部の送り手（mocopi・MediaPipe）から選べるようにするかは未決定。
- 【解決済み・段 ⑦】外から受けて転送する `muscles` フレームに `bones` が付かなかった件は、メインスレッドで `FillBones` してから転送するようにした（転送は最新 1 件だけになった）。
- 【解決済み・段 ⑦】JS は `.plmotion` の `absentMuscles` を読み書きで保つ。
- 段 ⑦：組み込みブラウザはローカルの HTML（file://）を開けないため、モーションスタジオの画面（`polyling_motion_studio.html`・`timeline.html`・`vrm_viewer.html`）をブラウザで操作した確認はしていない。JS は Node と jsdom で確かめた。ブラウザで段 ② の `.vrma`・`.plmotion` を読む・保存する・ライブ受信を表示するのは手での確認が要る（操作者の作業）。
- 段 ⑦：タイムラインは、マッスルのあるモーションで bakedBones を変えると保存時に muscles と body を書かない。bakedBones を変えずにマッスルのトラックだけ変えた場合は両方をそのまま書くので、bakedBones と食い違う（JS ではマッスルから骨を解けないため検出だけもしていない）。
- 段 ⑦：ライブ受信の bones は駆動されているボーンと Hips だけなので、途中で駆動されなくなったボーンはボーン編集・VRM 表示で前の回転が残る（受信開始の最初のフレームだけ reset で T ポーズに戻す）。
- 段 ⑤ の試験用 bakedBones だけのファイルは、muscles・body を消さずにキー名を改名して作った（MCP からは大きな JSON の一部を削れないため。読込は未知のキーを無視する）。
- Editor の `UnityClipExportWindow` は今も `<root>_bones.csv`（UnityBone v3）を書くが、PolyLing 本体に読む側は無くなった（段 ⑤ で `LoadSourceRestCsv` を削除）。
- PolyLing_mocopi は PolyLing を GitHub の固定コミットで取り込んでいる（`Packages/manifest.json`）。`MocopiToPolyLing.csproj` は `Library/ScriptAssemblies/PolyLing.Runtime.dll`（古い版）を参照するため、MCP の compile では今回の変更を検証できない。しかも変更前から `MocopiMuscleSender.cs:253`（`MotionLiveFrame.Source` / `MotionLiveJson.SourceMocopi` が無い）でエラーが出る＝固定コミットがすでに古い。PolyLing をプッシュし、mocopi 側でパッケージを更新してからコンパイル確認が要る（操作者の作業）。

## 7. 残件の締め（2026-10-31。この計画は完了）

- Save で並べ替えの警告が出なくなった件・VRMA 書き出しの節の使える条件：記録のみ（仕様）。
- ライブ受信の届かなかったマッスル：選べるようにした。muscles のフレームに任意の `"absentMuscles":"rest"`（`MotionLiveFrame.AbsentRest`）。無ければ従来どおり 0。`UnityClipApplier.ApplyMuscleFrame(…, absentRest)` と、転送用の骨を作る `MotionLiveHandler.FillBones` が同じ規則を使う。送り手（mocopi・MediaPipe）は今のまま（欄なし＝0）。
- `UnityClipExportWindow` の `<root>_bones.csv`：読む側が無いので書かないようにした（`BuildBoneCsv`・`CollectBones`・`DfsOrder`・オプション「bones.csv は Skinned…」を削除）。limits.csv は従来どおり。
- ライブ受信で駆動されなくなった骨に前の回転が残る件：モーションスタジオ（`データとツール/VRMビュワー/polyling_motion_studio/polyling_motion_studio.html`）で、前のフレームにあった骨が今回無ければ `reset`（T ポーズへ戻してから当てる）を付けるようにした。
- タイムラインでマッスルのトラックだけ変えたとき bakedBones と食い違う件：JS ではマッスルから骨を解けないので直さない（記録のみ）。
- 段 ⑤ の試験用ファイルの作り方：記録のみ。
- ファイルダイアログを使うパネル操作、ブラウザでのモーションスタジオの確認、Editor の窓（Play 外）からの Save、mocopi 側のコンパイル：道具で届かないので `PolyLing_残件.md` の 6 へ移した。

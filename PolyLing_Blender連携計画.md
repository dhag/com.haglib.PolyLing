# PolyLing Blender 連携計画（モーション）

Blender とモーションを行き来させる。道筋は 2 本とも確保する。
- A：Blender の VRM アドオン経由の `.vrma`（PolyLing 側は VRMA 整備計画で仕様どおりになった）
- B：ブリッジ（`データとツール/poly_ling_py/polyling_bridge.py`）で `.plmotion.json`（v4）を直接読み書き・送受信

前提：`PolyLing_VRMA整備計画.md` 完了（段 ①〜⑦）。
作成：2026-10-11

---

## 0. 進め方の決まり

- この文書が正本。会話が切れたら、次はこの文書だけを読んで続きから始める。
- 1 段＝1 回の会話で閉じる。段の終わりに「完了条件」をすべて満たし、チェック欄を更新してから終える。
- 承認はこの計画書に対して 1 回。段の途中で止まって承認を取りに行かない。
- 段の範囲外で見つけた問題は手を広げず「6. 残件」に書くだけにする。
- 後方互換は考えない（モーション関連は過去資産なし）。
- 外部（VRM アドオン）の仕様・データ構造は、使う前にそのソースで確認してから書く。推測で実装しない。

---

## 1. 着手時点の事実（調査済み）

- Blender 側の既存資産は `polyling_bridge.py`（bl_info version 0.2.0、blender 3.6 以上）だけ。WebSocket・Duplex 封筒でメッシュの送受信（`putMesh`）を行う。モーションの処理は無い。
- 契約 `PolyLing_外部連携の契約.md`：四元数は `[x,y,z,w]`（Blender は `(w,x,y,z)`）。§3.6 メッシュ：Blender (x,y,z) ⇔ PolyLing (x,z,y)、面の並びは逆。
- `.plmotion` v4 と VRMA の規則は `PolyLing_VRMA整備計画.md` §2（2.1 形式、2.2 書き出し、2.4 読込）と §5（正準 VRMA 骨格）が正本。
- MCP の道具に Blender を起動・操作するものは無い。Blender 上の確認は手作業（操作者の作業）になる。
- 外部の事実：VRM Add-on for Blender は VRM アニメーション（.vrma）の読み書きに対応している。

---

## 2. 決定事項

### 2.1 ヒューマノイドの割り当て（VRM アドオンの有無によらない）
- ブリッジはアーマチュアごとに割り当て表（HumanBodyBones 名 → Blender のボーン名）を持つ。保存先はアーマチュアのカスタムプロパティ。パネルで編集できる。
- VRM アドオンが入っていて、そのアーマチュアにヒューマノイドの割り当てがあれば、表へ取り込むボタンを出す。取り込みは任意。アドオンが読めないときはボタンが無効になるだけ。
- 読み書きは常にブリッジの表だけを使う（アドオンの有無で動作を変えない）。
- アドオン内の割り当ての格納場所は段 ② の着手時にアドオンのソースで確認する。

### 2.2 `.plmotion` ⇔ Blender アクション
- 対象は bakedBones・表情・lookAt。muscles・body は作らない（PolyLing が読込時に bakedBones から焼く。VRMA 整備計画 段 ⑤）。
- 回転：正準骨格上のローカル回転（レスト単位）と Blender のポーズボーン回転の変換は、VRMA 読込（2.4）と同じ「親 Humanoid のレストワールド回転 · q · 自分のレストワールド回転⁻¹」の式とその逆。レストワールド回転は Blender のアーマチュアのレスト（edit bone）から取る。座標は Blender (x,y,z) ⇔ PolyLing (x,z,y) に Z 正面の違いを合わせる（式は段 ② で確定し、契約に書く）。
- Hips の位置：「レスト位置からの差 ÷ レストの腰の高さ」（v4 の定義どおり）。
- 表情：シェイプキーの値。名前の対応は表情トラック名＝シェイプキー名を既定とし、provider は `vrm1` 以外を書くときの扱いを段 ② で決める。
- lookAt：Blender 側の表現は段 ② で決める（VRM アドオンが lookAt を持つならそれに合わせる）。
- 補間：書き出しはフレームごとに標本化（`frameRate` 格子）、読込は線形キー。

### 2.3 送受信
- ブリッジの既存の WebSocket 接続に、モーションの送信（Blender → PolyLing）と取得（PolyLing → Blender）を足す。中身は `.plmotion` v4 の JSON。C# 側に受け口のコマンドを足す。
- （段 ③ で決定）モーションの変換はブリッジに書かず、PolyLing IO アドオン（`polyling_io/blender/motion.py`）を使う。ブリッジは単独のアドオンのまま、モーションのボタンを押したときだけ `polyling_io` を読み込む（入っていない・有効でなければそのボタンだけ失敗）。`motion.py` にファイルと同じ処理の文字列の入口（`export_motion_text`・`import_motion_text`。ファイル版は `build_motion`・`import_motion_clip` を通す同じ処理）と `armature_of` を足した。
- （段 ③ で決定）C# の受け口はコマンド 2 本（契約 §3.7）。送信 `loadMotionClipJson`（motionJson・time）は `MotionClipSerializer.Parse` で読み、モーションの試し再生の窓口 `motionClip`（`MotionClipHandler.LoadJson`。ファイルの `load` と同じ `SetClipCore` を通す）に読み込んで今のモデルへ当てる。取得 `queryMotionClipJson` は窓口のクリップを `MotionClipSerializer.ToSaveJson`（`Save` と同じ整え `PrepareForSave` を写しに掛けて文字列にする）で返す。取得は Blender から取りに行く（PolyLing からの押し出し〔契約 §3.5〕は作らない）。

---

## 3. 段と完了条件

### 段 ① 道筋 A：VRM アドオン経由の往復確認
対象：コード変更なし（ずれが出たら原因を記録するだけ）
- [x] PolyLing で `.vrma` を書く（`convertMotionToVrma`）。
- [x] Blender（VRM アドオン）で VRM モデルに読み込み、そのまま `.vrma` で書き出す（操作者の作業）。
- [x] PolyLing で読み戻し（`exportMotionJson` sourceKind=Vrma）、元と全キー比較。
- 完了条件：VRMA 整備計画 段 ③ の許容差で比較結果を記録。ずれがあれば箇所と原因を「6. 残件」に。

### 段 ② 道筋 B：ブリッジの `.plmotion` 読み書き（ファイル）
**【2026-10-19】この段は `PolyLing_Blender入出力計画.md` の段 ④ へ移した。以後はそちらを正本とする。**
対象：`polyling_bridge.py`
- [ ] 2.1 の割り当て表（保存・パネル編集・VRM アドオンからの取り込み）。
- [ ] 2.2 の書き出し（アクション → `.plmotion.json` v4）と読込（`.plmotion.json` → アクション）。
- [ ] 変換式・座標の決まりを契約に書く。
- 完了条件：Blender で書いた `.plmotion.json` を `queryMotionJson` でエラー 0。PolyLing の `.plmotion.json` を Blender で読み、書き戻したものと元を全キー比較（許容差は段 ①）。Blender 上の操作は操作者の作業。

### 段 ③ 道筋 B：送受信
対象：`polyling_bridge.py`、C# の受け口（コマンド・ハンドラ）、契約
- [x] 2.3。
- 完了条件：コンパイル 0（4 csproj）。Blender から送ったモーションを PolyLing で再生してキャプチャ。PolyLing から取得したモーションが段 ② の読込と同じ結果。

### 段 ④ 文書
対象：`PolyLing_外部連携の契約.md`、マニュアル、機能索引
- [x] 道筋 A・B の手順と制約。
- 完了条件：記載が段 ①〜③ の実装と一致。

---

## 4. 進捗

| 段 | 状態 | 日付 | メモ |
|---|---|---|---|
| ① | 完了 | 2026-10-12 | 試験ファイルは `SandBox/BlenderPlan/`。`convertMotionToVrma` で `s2_src`→`b1_s2.vrma`、`s4_vmd`→`b1_s4.vrma`。Blender（VRM Add-on、モデル `Zunko_VRM10K.vrm`、シーンは既定の 24fps・1〜250 フレーム）で読み込み→書き出し `b1_*_blender.vrma`。`exportMotionJson`（Vrma）で 4 本とも エラー 0（Blender 側は leftEye/rightEye を読まない警告 VRMA_NOT_READ）。比較は一時コード（削除済み）で 1/240 秒ごとに線形評価、比べる区間は短い方の長さ。**s2**（Blender 経由 vs PolyLing 読み戻し）：body 位置 1.3e-5・回転 0.04°（許容内）。マッスル 4 本が許容超え（最大 4.5e-3、Left Forearm Stretch）、bakedBones 4 本が 0.1° 超え（最大 0.36°、LeftLowerArm）、表情 aa 4.5e-3・happy 3.7e-3・blink 0.1。最大点は 0.400 秒・1.600 秒で、1/24 秒の格子に乗らない時刻（9.6・38.4 フレーム目）。長さは 2 秒 → 10.375 秒。UpperChest・lookAt・myCustom が消える。**s4**：bakedBones 32 本が 0.1° 超え（最大 RightHand 73.1°・RightLowerArm 71.9° @4.6 秒）、Hips 位置 2.5 cm、マッスル 60 本が許容超え、表情は VRM preset 8 本だけ残り値は一致、カスタム 18 本が消える。ずれの箇所と原因は 6 に記録。**2026-10-13 修正**：回転→マッスルで `GetHumanPose` の値を `CanonBoneMotion.RefineMuscles`（鎖ごとの LM＋Hips に合わせた body 位置の補正）で詰めるようにした（呼び出しは `VrmaCodec.Read.cs` の読込と `CanonBoneMotion.BakeMuscles`）。4 本を読み戻して倍精度で再比較：**s4 PolyLing だけの往復**（`s4_vmd` と `b1_s4_pl`）はマッスル最大差 3.0e-6、bakedBones 0.0000°、body 位置 2.5e-7（修正前はマッスル差 3.48、bakedBones 179.8°）。**s2 の往復**はマッスル 6.7e-4、body 位置 7.3e-5（許容内）、表情 happy 0.299 は元の値 1.299 を VRMA 仕様どおり [0,1] に丸めた差。**Blender 経由と PolyLing の読み戻しを Blender のキー時刻で比較**：s2 は bakedBones 0.026°、マッスル最大 1.08e-3（Left Arm Down-Up の 1 本だけ 1e-3 を超える）。s4 は bakedBones 最大 2.415°（RightLowerArm @4.708、0.1° 超えは 4.25〜4.75 秒の 8 本）、Hips 位置 5.4e-7、body 位置 1.0e-3。1/240 秒の格子で比べると s4 は RightHand 53.9° などになるが、これはキーの格子（30fps と 24fps）が違うためで、キーの間の補間の差。`.vrma` の読込時間は 0.25〜0.9 秒。|
| ② | 移管 | 2026-10-20 | `PolyLing_Blender入出力計画.md` の段 ④ で完了（チェック欄はそちら）。 |
| ③ | 完了 | 2026-10-20 | 決めたことは 2.3。**Python**：`polyling_bridge.py`（0.3.0。パネルに「モーション」の「送る」「取得」、`send_motion`・`receive_motion`）、`polyling_io/blender/motion.py`（文字列の入口と `armature_of`）、`polyling_io/blender/__init__.py`（`armature_of` を使う）。**C#**：`PanelCommand.Motion.cs`（`LoadMotionClipJsonCommand`・`QueryMotionClipJsonCommand`）、`PlayerCommandDispatcher.Motion.cs`（受け口。`GetMotionClipHandler`）、`PolyLingPlayerViewerCore.CreateCommands.cs`（配線）、`MotionClipHandler.cs`（`LoadJson`・`Clip`・状態 `sourcePath`・`SetClipCore`）、`MotionClipSerializer.cs`（`ToSaveJson`・`PrepareForSave`。`Save` も `PrepareForSave` を通す）、`PlayerMotionClipTestSubPanel.cs`（`AdoptExternalClip`）と `PolyLingPlayerViewerCore.Layout.TestPanels.cs`（配線）。契約は §3.7 を新設、§5.1 に 1 行。**コンパイル**：PolyLing.Runtime・PolyLing.Editor・PolyLing.Vrm10・Assembly-CSharp の 4 本 0 件、Unity の再読込でエラー 0。**ブリッジの検査**（作業環境の bpy 4.2.0、A4 を取り込んだ .blend。Windows の PolyLing へは届かないので、PolyLing のリモートサーバの模型 `SandBox/BlenderIO/stage5_bridge/scripts/mock_polyling.py` を相手にした。模型は受けた command の params を C# の `JsonParser`〔`RemoteProtocol.cs`〕と同じ手順で読む写し。試験は `t_bridge.py`）：送信は fresh_Z・pl_s4（5 MB）・pl_s2・probe_pl を読んだアクションを「送る」と、模型が読み出した `motionJson` が `export_motion` のファイルとも元ファイルともバイト一致、`time` は今のフレーム 15 ÷ 30 = 0.5。取得は同じ 4 本の中身を返させて「取得」で作ったアクションの F カーブ（アーマチュア・シェイプキー・視線の空）の全キーと `polyling_motion` が、同じファイルを `import_motion` で読んだものと一致（違い 0）。**PolyLing**（Play、A4 を `loadProjectCsv`、今のモデルは Zunko_MMD）：`loadMotionClipJson` に Blender で書いた fresh_Z の中身（MCP の引数の長さのため bakedBones・lookAt のキーを t = 0・0.5・1 の 3 つに間引き、表情は全キー。t = 1 の値は元と同じ）を time = 1 で送ると成功（bakedBones 5・焼いた muscles 15・表情 2・警告 0）。キャプチャ `SandBox/Captures/BlenderIO/link3_freshZ_t1_0002.png` は段 ④ の `stage4_pair_freshZ_t1.png` と同じ姿勢（左腕を上げ、左脚を開き、頭を回す）。壊れた JSON（version 9）は `invalidArgs` で断る。**取得**：窓口 `motionClip` の `load` で `stage4/pl/probe_pl.plmotion.json`（C# の出力。接線・ラップ・lookAt・absentMuscles・metadata を持つ）を読み、`queryMotionClipJson` が返した `motionJson`（`sourcePath` は元ファイル）を元ファイルと比べるとバイト一致（16320 バイト。返った中身は `SandBox/BlenderIO/stage5_bridge/probe_query.plmotion.json`）。中身が同じなので Blender の読込の結果も同じ（上の取得の検査でファイルの中身を返させたものと同じ）。作業の出力は `SandBox/BlenderIO/stage5_bridge/`。Windows の Blender での手順は 5。 |
| ④ | 完了 | 2026-10-20 | **契約**（`PolyLing_外部連携の契約.md`）：§5.2「Blender とモーションを行き来させる 2 つの道筋」を新設（A・B の要るもの・PolyLing → Blender・Blender → PolyLing・対象のモデル・残るものの表、道筋 A の制約 6 項目、道筋 B の性質）。道筋 B の決まりは §3.7（段 ③）・§5.1・§7（入出力計画 段 ⑤）にあるので §5.2 からはそこを指す。**機能索引**（`PolyLing_機能の場所.md`）：§3 に「3-1. 動いている PolyLing とつなぐ（PolyLing Bridge）」（入れ方と 5 行：接続・メッシュの送信・取り込み・モーションの送る・取得）と「3-2. VRM Add-on 経由で行き来させる（.vrma）」（3 行）を足し、§4 にブリッジのボタンの文字の場所を足した。**マニュアル**：リポジトリに PolyLing 本体のマニュアルに当たるファイルが無い（入出力計画 6 の 15）ので書いていない。**照合**：道筋 A は VRM Add-on for Blender のソース（GitHub の main、2026-10-19 のコミット 2efbdfc）を読んで確かめた：メニューの文字「VRM Animation (.vrma)」（`importer/import_scene.py`・`exporter/export_scene.py` の `menu_import`・`menu_export`）、VRM 1.0 のアーマチュアが要ること（同 `draw` の「VRM Animation import/export requires a VRM 1.0 armature」）、書き出しがシーンの `frame_start`〜`frame_end` を毎フレーム標本化すること（`exporter/vrm_animation_exporter.py`）、読込が lookAt ノードの位置しか見ないこと（`importer/vrm_animation_importer.py`）。段 ① で記録した挙動（6）から変わっていない。PolyLing 側の文字（「VRMA 書き出し」「JSON保存」、種類「VRM アニメーション (.vrma)」「PolyLing モーション」）は `PlayerMotionClipTestSubPanel.cs`、コマンド `convertMotionToVrma`・`exportMotionJson`・`loadMotionClipJson`・`queryMotionClipJson` は `polyling_describe`、ブリッジの文字（［PolyLing連携］・「接続」「切断」「送信」「新規として送信」「取り込み」「送る」「取得」）は `polyling_bridge.py` の `bl_label` とパネルの `text` で確かめ、食い違い 0。 |

---

## 5. 改善案（操作者の作業が要るもの）

- MCP サーバに `blender --background --python <スクリプト>` を実行する道具を足せば、Blender 上の確認（段 ①〜③）を自動化できる。MCP サーバの再コンパイルが要る。
- 段 ③ の Windows の Blender での確認（MCP から Blender を動かせないので操作者の作業。決まりは契約 §3.7・§5.1）：
  1. 入れる：`データとツール/poly_ling_py/polyling_io` をフォルダごと zip にして入れ「PolyLing IO」を有効にする（`PolyLing_Blender入出力計画.md` 5 の 1）。`データとツール/poly_ling_py/polyling_bridge.py` を［編集 > プリファレンス > アドオン］で入れ（4.2 以降は「Install from Disk」）、「PolyLing Bridge」を有効にする（版 0.3.0。前の版が入っていれば入れ替える）。（2026-10-26：`polyling_bridge.py` は廃止。ブリッジは PolyLing IO に含まれる。`PolyLing_外部受け渡し計画.md` 段 ④）
  2. PolyLing を Play し、左の「ファイル」→「プロジェクト読み込み」で `SandBox/bintest/A4/project.csv` を開き、モデルを Zunko_MMD にする。リモートサーバを開始する。
  3. Blender で同じ `project.csv` を［ファイル > インポート > PolyLing プロジェクト］で読み、3D ビューのサイドバー［PolyLing］の［PolyLing連携］で一覧を更新して「接続」。
  4. 送信：「Zunko_MMD_Armature」を選び、［PolyLing］タブの「モーションを読む」で `SandBox/BlenderIO/stage4/bl/fresh_Z.plmotion.json` を読む（腰の回転と位置も当てる は切る）。フレーム 30 にして［PolyLing連携］の「モーション」の「送る」。PolyLing の画面が `SandBox/Captures/BlenderIO/link3_freshZ_t1_0002.png`（左腕を上げ、左脚を開き、頭を回す）と同じ姿勢になり、左の「その他」→「モーション」のパネルのクリップ名が fresh_Z、長さ 1.00 秒になればよい。
  5. 取得：PolyLing の「モーション」パネルで `SandBox/BlenderIO/stage4/pl/pl_s4.plmotion.json` を開く。Blender で「Zunko_MMD_Armature」を選び「取得」（操作の調整パネルで 腰の回転と位置も当てる を切る）。フレーム 120 を正面から見て `SandBox/Captures/BlenderIO/stage4_pair_s4_Z_t4.png` の右（Blender）と同じ姿勢ならよい。続けて［PolyLing］タブの「モーションを書く」で書いたファイルが `pl_s4.plmotion.json` とバイト一致すればよい（PolyLing が返す中身は元ファイルとバイト一致することを段 ③ で確認済み）。

---

## 6. 残件（範囲外で見つけたもの）

段 ③ で見つけたもの：
- `loadMotionClipJson` で受けたクリップは元ファイルが無いので、モーションのパネルの「JSON保存」・読み直し・VRMA 書き出しが使えない（どれもパネルが元ファイルを `exportMotionJson`・`convertMotionToVrma` に渡す作り。`PlayerMotionClipTestSubPanel.OnSaveJson` ほか）。受けたクリップを PolyLing でファイルにするには、窓口のクリップを書くコマンド（`queryMotionClipJson` と同じ `ToSaveJson` の中身を作業フォルダへ書く）が要る。【`PolyLing_Blender不具合修正計画.md` の B へ移した（2026-10-20）。結果：段 ② で直した】コマンド `saveMotionClipJson`（filePath）を足し、元ファイルとその種類の持ち主を窓口 `motionClip`（`sourcePath`・`sourceKind`）だけにした。パネルは窓口の状態を読み、JSON保存は元ファイルの無いクリップならこのコマンドを使う。保存後は読み直し・VRMA・BVH・FBX 書き出しが使える（契約 §3.7）。

段 ① で確認した VRM Add-on の挙動（ソース `importer/vrm_animation_importer.py`・`exporter/vrm_animation_exporter.py` で確認）：
- 長さ：読込はシーンの fps で毎フレームにキーを打ち、シーンのフレーム範囲を変えない。書き出しは `scene.frame_start`〜`frame_end` を毎フレーム標本化する。そのため長さはシーンの範囲（既定 1〜250 フレーム＝10.375 秒）になり、キーはシーン fps（既定 24）の格子に打ち直される。s2 の許容超え（最大点が 1/24 秒の格子外の 0.4・1.6 秒）はこの打ち直しの時刻と一致する。シーンを 30fps・クリップの長さにして再試験すれば切り分けられる（未実施）。
- lookAt：読込は lookAt ノードの translation チャンネルだけを見る（無ければ視線を作らない）。VRMA 仕様（VRMC_vrm_animation 1.0 README の LookAt）は lookAt ノードの局所回転を視線とする。PolyLing は仕様どおり回転で書くので、アドオンを通すと視線が消える。
- 表情：読込・書き出しともモデルにある表情だけを扱う。Zunko_VRM10K に無い表情（myCustom、s4 のカスタム 18 本）は消える。
- 骨：書き出しはモデルに割り当てのある骨だけ書く。Zunko_VRM10K に upperChest は無い（`.vrm` の JSON で確認）ので UpperChest が消える。leftEye/rightEye は humanBones に書かれる（仕様は定義不可）が回転は書かれない。

往復のずれ（2026-10-13 に倍精度の角度で再計測。一時コードは削除済み。Unity の `Quaternion.Angle` は内積が 1−1e-6 を超えると 0 を返し、約 0.16° 未満を 0 とするため使わない）：
- Blender は回転を忠実に通す。`b1_s4_blender.vrma` の全ボーンの回転は、`b1_s4.vrma` のキーを各時刻で slerp した回転と最大 0.043°（Spine）、Hips 位置は 4.9e-7 で一致する。s4 を Blender 経由にしたときの大きなずれ（RightHand 73.1°、RightLowerArm 71.9°、Hips 位置 2.5 cm）は Blender ではなく、PolyLing の読込の `GetHumanPose`（下）で生じる。
- Blender 版で LM でも残るずれ：250 フレーム中 49 フレームが 0.01° を超え、最大 2.41°（t=4.7083、RightLowerArm）。残るボーンは LowerArm・LowerLeg（マッスルが Stretch と Twist の 2 本だけのボーン）。Blender はキーの間を slerp で打ち直すので、2 自由度のボーンではマッスルで表せない回転になる。
- 正準の T ポーズ（全ローカル回転が単位）そのものがマッスルで表せない：レストで `GetHumanPose`→`SetHumanPose` すると親指 Proximal 4.006°・Intermediate と Distal 7.285°、ほかのボーン最大 0.384°。親指は LM でも 7.284° から下がらない（Thumb 2・3 Stretched を −3〜3 で総当りしても最小 7.284°）。親指の Intermediate・Distal の位置を水平（y=0）にした骨格では Intermediate・Distal は 0.000° になるが Proximal が 9.086° になる。s2（`b1_s2.vrma`）は LM 後も全フレームで RightUpperLeg に 0.259° 残る（そのとき Right Lower Leg Stretch が 1.000 を僅かに超えて MUSCLE_OUT_OF_RANGE の警告が出る）。正準骨格の変更が要るので未対応。
- 【2026-10-14 対応】正準骨格の指が VRM 1.0 の T ポーズ（`tpose.md` 1.7 四指は X に平行、1.8 親指は水平で X と Z から 45°）を満たしていなかった（親指は X から 39.5°・18.8° 下向き、四指の先は 1.25〜1.35° 上向き）。`CanonSkeleton` の指の Intermediate・Distal の位置を仕様の向きへ直した（長さは同じ、親指 Proximal の付け根の位置は仕様に無いので据え置き）。同じ値を写す `UnityClipApplier` の `CanonDirTable`（指 20 行）と `CanonMuscleTable`（指 30 行の軸。正準 Avatar で測り直した値。同じ測り方で旧骨格から旧表を再現できることを確かめた）、`PolyLing_VRMA整備計画.md` 5 の表を直した。モーションスタジオ `muscle_pose.js` の `CANON_ROWS` は MCP で届かないので ZIP で渡した。
  - 実測（倍精度、一時コードは削除済み）：レストの往復は四指・親指 Intermediate・Distal が 0.000°（旧は親指 Intermediate・Distal 7.285°、四指 Proximal 0.108〜0.215°）、親指 Proximal は 10.102°（旧 4.006°）、ほかの骨 0.384° のまま。親指 Proximal の残りは、Unity の親指 Proximal のマッスル 0 の向き（Zero (0, 0.123091, 0.123091, 0.984732)。骨の向きによらず同じ値だった）が骨の軸まわりのねじりを含み、親指 Proximal にねじりの自由度が無いため：ねじり＝2·atan(|v·d|/w)（v は Zero のベクトル部、d は骨の向き）が旧骨格 4.006°・新骨格 10.102° で実測と一致。仕様の向き d=(∓1,0,1)/√2 では 10.10° が下限（レストの局所回転を単位のまま・Unity 既定の可動域のままでは消せない）。
  - 新骨格で `s4_vmd` の muscles から書き直した `.vrma`（`b2_s4.vrma`）は、読み戻して bakedBones を作り直すと全ボーン 0.0000°。`s2_src` から書いた `b2_s2.vrma` は指のマッスルを持たず指がレスト（単位）になるので、親指 Proximal に 9.99° 残る（ほかは RightUpperLeg 0.259°、旧と同じ）。旧骨格で書いた `.vrma`（`b1_*`）の親指は新骨格では表せず、親指 Proximal が最大 12.77° ずれる。
  - 既存の `.plmotion` に保存されている bakedBones は旧骨格で作ったもので、次に保存するまで muscles と食い違う（`convertMotionToVrma` は bakedBones があればそれを書くので、旧骨格の指の回転のまま `.vrma` になる）。
- 同じ「回転を GetHumanPose だけでマッスルにする」処理がほかにもある（RefineMuscles は通していない。往復の誤差は未計測）：`VmdMotionBake.cs:152`（VMD＋モデル → マッスル）、`MediaPipePoseSolver.cs:304`、`UnityClipExportWindow.cs:453・754`。【`PolyLing_Blender不具合修正計画.md` の A へ移した（2026-10-20）。結果：直さない】`VmdMotionBake` で測ると GetHumanPose だけでは 16 本が 0.1° を超えた（最大 RightHand 153.73°）が、`CanonBoneMotion.RefineMuscles` を通しても 0.1° 以下にならず骨によっては悪くなった（RightUpperArm 63.98°→71.67° ほか）ので、変更は取り消した。`MediaPipePoseSolver` は同じ変換なので同じ扱い。`UnityClipExportWindow` は Editor の窓でシーンとアセットを使うため MCP から測れなかった。詰まらない原因は未調査（その計画書の 4 の段 ② と 6）。
- 【2026-10-13 修正済み。§4 の段 ① 参照】PolyLing だけの往復（`s4_vmd` → `b1_s4.vrma` → 読み戻し）でも許容を超える（bakedBones：RightLowerArm 179.8°・RightHand 156.6°・RightUpperArm 23.7° @4.63 秒、脚・左腕 6〜7.7°。マッスル：Right Forearm Twist In-Out 差 3.48 ほか 24 本）。`s4_vmd` には -1〜1 の外のマッスルがある（`queryMotionJson` の MUSCLE_OUT_OF_RANGE 警告 35 件）。キーを書き出して確認（一時コード、削除済み）：179.8° は t=4.6333 の 1 キーだけで、前後のキー（4.6000・4.6667・4.7000）は一致。`s4_vmd` 自体が 4.63〜4.73 秒で前腕のねじりを 1 フレームで大きく跳ばしている（RightLowerArm の bakedBones は 4.6333→4.6667 で 165.4°、4.7000→4.7333 で 157.2° 回る。Right Forearm Twist In-Out は 1.719→-1.932、-1.996→1.970）。読み戻しはこの最初の跳びを 1 フレーム早い 4.6333 に置き、そのキーの RightLowerArm が元から 180° 回った値になる（マッスルは 1.719→-1.761）。
- **原因と修正の可否（2026-10-13 実測、一時コードは削除済み）**：`.vrma` 読込は回転を正準骨格へ入れて `GetHumanPose` でマッスルにする（`VrmaCodec.Read.cs:457-458`。bakedBones だけの `.plmotion` は `CanonBoneMotion.BakeMuscles` の 413-414 で同じ）。そのマッスルを `SetHumanPose` で回転へ戻すと元の回転にならない。`b1_s4.vrma` の各キーで比べると 249 フレーム中 238 フレームで 0.1° を超える（最大 179.97°）。主なずれはねじりのマッスルで、`GetHumanPose` の値がおよそ半分になる（例：Left Arm Twist In-Out −0.119 に対し回転を再現する値は −0.239、Left Upper Leg Twist In-Out も同様）。`GetHumanPose` の値を初期値に、鎖（体幹・左右の腕・左右の脚・指 10 本）ごとのマッスルを Levenberg–Marquardt で「`SetHumanPose` の結果のローカル回転が元の回転に一致する」ように詰めると、`b1_s4.vrma` は 249 フレームすべてで誤差 0.0000°、元の `s4_vmd` のマッスルとの差は最大 0.0051 になる（`GetHumanPose` のままでは最大 3.48）。この段落の判定は `Quaternion.Angle`（約 0.16° 未満を 0 とする）で行ったもの。倍精度での再計測は次の「往復のずれ」。`b1_s4.vrma` は倍精度でも LM 後 0.0000°。VRMA 整備計画で全キーを比べたのは s2 だけ（s4 は段 ④ で読み戻しの検査、段 ⑤ で t=1.8・4.0 秒のキャプチャ比較のみ）。

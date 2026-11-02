# PolyLing モーション編集構造計画（version 5：クリップとレイヤー）

Motion Studio で編集できるデータ（キー・接線・ラップ・クリップ・バンドル・レイヤー）を、PolyLing 本体でも数値で操作できるようにする計画。この文書を正本にする。
グラフ表示などの Studio の画面は本体に作らない。操作はコマンド（MCP・シナリオ）と、パネルの数値欄で行う。

進め方：1 段＝1 会話。着手前にコードを読み直して変更計画を出し、承認を得てから実装する。終えたら「状態」を更新する。

作成: 2026-10-31。状態：計画承認（2026-10-31）、段 1 完了（2026-10-31）、段 2 完了（2026-10-31）、段 3 完了（2026-10-31）、段 4 完了（2026-10-31）、段 5 完了（2026-10-31）、段 6 完了（2026-10-31）。

方針の決定（2026-10-31）：形式を version 5 に上げ、クリップとレイヤーを持たせて本体でも評価する（案 B）。

## 1. 確認した事実（2026-10-31、コードで確認）

### 1.1 本体のモーション

| 項目 | 事実 | 根拠 |
| --- | --- | --- |
| 窓口 | モーションの試し再生の窓口 `motionClip` が、読み込んだクリップを `MotionClipDTO` で持つ。操作は読込・時刻を当てる・倍率・absentMuscles・可動域だけで、キー・接線・ラップを変える口は無い | `MotionClipHandler.cs` |
| 書き換えの口 | `EditClip(Action<MotionClipDTO>, newName, time)` だけ。使うのは `createLipSyncFromVoicevox` 1 本 | `MotionClipHandler.cs` `EditClip`、`PlayerCommandDispatcher.Motion.cs:143` |
| 取り消し | 窓口は Undo を持たない（先頭のコメント）。本体の取り消しは `IUndoRecord` の記録で組む（モデル以外の記録の例：`EditorStateRecords.cs`・`SpringBoneUndoRecords.cs`） | `MotionClipHandler.cs:5-7`、`Runtime/UndoSystem/Core/IUndoRecord.cs` |
| 形式 | version 4。トラック `MotionTrackDTO`（id・`preWrapMode`・`postWrapMode`）／`MotionScalarTrackDTO`、キー `MotionKeyDTO`（`t`・`pos`・`rot`・`scl`・`posTan`・`rotTan`・`sclTan`）／`MotionScalarKeyDTO`（`t`・`v`・`tan`）、接線 `MotionTangentDTO`。クリップ・バンドル・レイヤーの項目は無い | `MotionClipDTO.cs` |
| 読み書き | Newtonsoft で DTO に読む。知らない欄は捨てる。書くときの version は常に `CurrentVersion` | `MotionClipSerializer.cs:92-148`・`:278` |
| 評価の正本 | `MotionCurveMath.cs`。使う所は 9 ファイル（`MotionClipApplier`・`CanonBoneMotion`・`VrmaCodec`・`ModelMotionSampler`・`MotionClipRootUpgrade`・`MotionClipSerializer`・`UnityClipRootMotion`・Editor の `PolyLingMotionToUnityClip`） | `search_text "MotionCurveMath."` |
| トラックを読む所 | `bones`・`bakedBones`・`muscles`・`expressions` を読むのは 30 ファイル | `search_text` |

### 1.2 Motion Studio のデータ（`timeline.html`）

| 項目 | 事実 | 根拠 |
| --- | --- | --- |
| クリップ | `Clip`：`start`・`len`（フレーム）・`loop`・`weight`・`keys`（チャンネル名 → `{f, v, tan, t0}`。`f` はクリップの先頭からのフレーム、`t0` は読み込んだときの元の秒） | `Clip`（`:395-427`） |
| トラック | `Track`：`type`（bone・morph・muscle）・`name`・`uname`・`plRole`（bones・bakedBones・body・lookAt・expressions・muscles）・`plTargetKind`・`preWrapMode`・`postWrapMode`・`clips` | `Track`（`:428-485`） |
| バンドルとレイヤー | `Bundle`：`clipIds`・`childIds`・`parentId`・`layer`（`{mode, weight, mute, solo}`）。所属・親・重ねる順・実効の重みとミュートは `layerInfo` | `:489-519` |
| 評価 | `Track.sampleCh`（基底＋レイヤー合成）→ `sampleClips`（有効なクリップのクロスフェード）→ `sampleContinuous`（全クリップのキーを 1 列）。キーの評価は `plmotion_curve.js`（`MotionCurveMath.cs` の写し） | `:440-481` |
| 回転の混ぜ方 | クロスフェード・override・additive の重みは `Q.slerp`（内積 0.9995 を境に線形）。`plmotion_curve.js` の `slerp`（本体の `Quaternion.Slerp` を測って合わせた、内積 0.95f を境に線形）とは別の式 | `:359-363`・`:366`、`plmotion_curve.js` |
| 保存 | `.plmotion.json` version 4。キーを持つクリップが 2 本以上・重み 1 未満・レイヤーに属するトラックは、`Track.sample` をフレーム 0〜最終フレームで標本化して書く（接線なし、ラップ Clamp）。それ以外はキー・接線・`t0` をそのまま書く | `plNeedsBake`・`plMakeTrack`（`:2187-2214`） |
| 読込 | version 1〜4 だけ読む。トラックごとにクリップ 1 本（先頭 0、長さ＝最終キーのフレーム＋1）を作る | `importPolyLing`（`:2137-2172`）、`plAddTrack` |
| JSON保存 | Studio 独自の形（`MotionTimeline.toJSON`）。`.plmotion.json` とは別 | `MotionStudio_機能強化計画.md` 1.3 |

### 1.3 ほかの読み手

- Python（Blender アドオン）`polyling_io/format/plmotion.py`：version 1〜4 でなければ読まない（`:56`）。書く version は常に 4（`:18`・`:72`）。知らない欄は残して書き戻す（`:8`・`:15`）
- 外部連携の契約 `PolyLing_外部連携の契約.md` §5 が `.plmotion.json` の形を定める（version 4）

## 2. 決めたこと

### 2.1 形の持たせ方：編集の構造と、平らなトラックの両方を持つ

- version 5 は、今の平らなトラック（`bones`・`bakedBones`・`body`・`lookAt`・`expressions`・`muscles`）をそのまま持ち、編集の構造（クリップ・バンドル・レイヤー）を新しい欄 `edit` に足す
- 平らなトラックは、編集の構造を評価した結果にする。Studio の今の保存の規則と同じ（標本化の条件・接線・`t0`・ラップ）
- 1.1 の 30 ファイルは平らなトラックを読むので変えない。編集の構造を評価して平らなトラックを作る所（以下「平らにする」）を、本体に 1 か所だけ作る
- `edit` が無いファイル（version 4 以前と、構造を使わない version 5）は、今と同じに扱う。編集するときに、Studio の読込と同じ形（トラックごとにクリップ 1 本）の構造を作る
- 再生は平らなトラックで行う。標本化したトラックは整数フレームでだけ Studio の再生と一致する（フレームの間は標本の間の補間）。これは今の Studio の保存と同じ

### 2.2 本体と Studio で評価を同じにする

- 平らにする評価（クロスフェード・レイヤー合成・標本化）の規則の正本を、本体の C# にする。Studio の `timeline.html` はその写しにする（`plmotion_curve.js` と同じ扱い）
- 回転の混ぜ方は `plmotion_curve.js` の `slerp`（＝本体の `Quaternion.Slerp`）にそろえる。Studio の `Q.slerp`（0.9995 境）を使う所は置き換える（段 2）。置き換えた後に Studio の再生が変わる大きさを測って書く

### 2.3 version の扱い

- `CurrentVersion` を 5 にする。本体・Studio・Python が書くファイルは version 5 になる。読みは 1〜5
- version 4 以前のファイルは今までどおり読める（`edit` なし）

## 3. 変更計画

### 段 1：形式（version 5）の定義と読み書き — 状態：完了（2026-10-31）

- `edit` の中身を決めて `MotionClipDTO.cs` に足す。Studio の `Track`・`Clip`・`Bundle` の保存する欄を元にする（表示だけの欄 `color`・`expanded`・`view` などは入れない）。平らなトラックとの対応（`plRole` と id）の持ち方もここで決める
- `MotionClipSerializer`：読み（1〜5）、検証（クリップの範囲・キーの重複・バンドルの循環・レイヤーの値の範囲など。コードを `MotionFileCodes` に足す）、書き
- Studio：`importPolyLing` が version 5 の `edit` からトラック・クリップ・バンドル・レイヤーを作る。`exportPolyLing` が `edit` を書く。version の確認を 1〜5 にする
- Python `plmotion.py`：version 1〜5 を読み、`edit` を残して書き戻す
- 契約文書 §5 に version 5 を書く
- 確認：Studio で作った状態（段 5 の試験の状態）を保存し、本体で読んで書き戻したものが同じ中身になること。Python で読み書きして同じになること。version 4 のファイルが今までどおり読めること

実施結果：
- 決めた形（契約文書 §5「編集の構造 edit」に書いた）
  - `edit` = `{tracks, bundles}`。トラックは `id`・`role`・`targetKind`・`target`・`name`・`uname`・`category`・`provider`・`preWrapMode`・`postWrapMode`・`clips`。`type` は `role` から決まるので持たない。ラップは Studio のトラックの値そのまま（null 可）
  - クリップは `id`・`name`・`start`・`len`・`loop`・`weight` と、チャンネルごとのキー列 `rot`・`pos`・`scl`（`{f, v[], tan[]?, t0?}`）・`w`（`{f, v, tan?, t0?}`）。`f`・`start`・`len` は float
  - バンドルは `id`・`name`・`clipIds`・`childIds`・`parentId`・`layer {mode, weight, mute, solo}`
  - 表示だけの欄（`color`・`expanded`・`view`・`curveCh`・`group`・`collapsed`）と `boneMap` は持たない
  - 平らなトラックとの対応は `(role, targetKind, target)`。expressions は name が同じで path の無いもの
- 本体
  - `MotionClipDTO.cs`：`CurrentVersion` 5、`edit`（`MotionEditDTO`・`MotionEditTrackDTO`・`MotionEditClipDTO`・`MotionEditKeyDTO`・`MotionEditScalarKeyDTO`・`MotionEditBundleDTO`・`MotionEditLayerDTO`）、冒頭の版の説明
  - `MotionClipSerializer.cs`：読み 1〜5、version 4 以前の `edit` は捨てて `DROPPED_FIELD` の警告、`ValidateEdit`。コード `INVALID_EDIT`・`EDIT_DUPLICATE_ID`・`EDIT_UNMATCHED_TRACK`・`EDIT_DUPLICATE_KEY`・`EDIT_BUNDLE_LINK`・`EDIT_BUNDLE_CYCLE`。キーが `len` を越えるのはエラーにしない（`timeline.html` の右端の縮めはキーを消さない）
  - `MotionClipHandler.EditClip`（`createLipSyncFromVoicevox`）は `edit` を null にする
  - `PrepareForSave` の `BakeBones`（muscles から bakedBones を作り直す）は変えていない。edit との食い違いの扱いは段 3 の項目のまま
- Studio（`timeline.html`）
  - `importPolyLing`：version 1〜5。`edit` があればトラック（edit の順）とバンドルを作り、`edit` の当たらない平らなトラックはクリップ 1 本で後ろに足す。edit の id と重ならないよう、edit を作った後と最後に `bumpUid`
  - `exportPolyLing`：version 5。常に `edit` を書く。平らなトラックを書かなかったトラックは入れず、そのクリップの id はバンドルの `clipIds` から除く
  - `bClone`：元に親があるとき、複製を親の `childIds` に入れる（子のバンドルを貼ったときの食い違いを直した）。`bMakeFrom`：子にしたバンドルを元の親の `childIds` から除く
  - 保存の表示・コメント、`polyling_motion_studio.html` のボタンの説明とヘルプ、マニュアルの「読み書きできるファイル」を v5 に
- Python
  - `format/plmotion.py`：`CURRENT_VERSION` 5、読み 1〜5、version 4 以前の `edit` は捨てて警告、`edit` の欄の型・順・初期値（C# の宣言順）、`validate` に `_validate_edit`（C# と同じ検査）
  - `blender/motion.py`：書き出しで bakedBones・表情・視線のトラックを 1 本でも作り直したら `edit` を書かない（警告）
  - `format/__init__.py`・`addon/session.py`・`net/client.py` のコメントの版
- 契約文書 §5：形の版、`edit` の節、読みと書きの版、§5.1 の書式（欄の順・初期値・version 5）、Blender の元の無いアクションの version
- 確認（内蔵ブラウザ・serve_root.py・Zunko_VRM10K.vrm、本体は Play 中のエディタ、Python と比較は手元の PC の `.venv` の python で `SandBox/curve_test/stage1_cmp.py`）
  - Studio：`curve_test` を読み段 5 の試験の状態を作った。version 5 の保存から `edit` を除き version を 4 にしたものは `stage5_test.plmotion.json` と同一。保存は `stage5_test` に `edit` を足したものと同一だったので、`stage1_edit.json`（edit だけ）と `stage1_build.py` で `SandBox/curve_test/stage1_studio.plmotion.json` を組み立て、fetch した中身が画面の出力と同一であることを確かめた
  - Studio の中の往復：その保存を読み直して書くと同一、全トラック・フレーム 0〜60 の `Track.sample` も同一、重ねる順 A,B,H,C,D,E,F,G
  - 本体：`exportMotionJson`（PolyLingMotionJson → `stage1_cs.plmotion.json`）でエラー・警告 0。Studio の保存と float32 に丸めて比べ、`edit` と平らなトラック（bones・expressions・bakedBones・lookAt ほか）の違いは 0 件。本体が足したのは muscles 9 本・body・rotationSource "bakedBones"（bakedBones だけのファイルを読むときの既存の規則）
  - 本体の書いたものを Studio で読む：edit の 6 トラックとバンドルは元と値が同一（接線の欄の並びだけ C# の宣言順）。本体の足した body と muscles 9 本はクリップ 1 本のトラックで後ろに付き、書くと edit にも入る。レイヤーの合成を含む再生の差の最大 2.0e-8
  - Python：本体の書いたファイルを読んで書くとバイト一致（142716 字）。Studio の保存を読むと検査のエラー・警告 0、読んで書いた中身の違い 0 件。version 4 に edit を付けたものは edit を捨てて警告
  - 検査：壊したファイル 6 本（当たる平らなトラックが無い・キーの重複・parentId と childIds の食い違い・循環・レイヤーの重み 1.5・表情に rot）を本体（`queryMotionJson`）と Python で読み、どれも意図したエラー（`EDIT_UNMATCHED_TRACK`・`EDIT_DUPLICATE_KEY`・`EDIT_BUNDLE_LINK`・`EDIT_BUNDLE_CYCLE`・`INVALID_EDIT`）になった
  - version 4 のファイル（`curve_test`・`stage4_test`）：本体は `queryMotionJson` で valid（version 4、エラー・警告 0）、`exportMotionJson` で version 5・edit なしで書けた（Python で読んで書くとバイト一致）。Python は 5 へ引き上げ、中身の違い 0 件。Studio は `curve_test` を読んで書いたものから edit を除き version を 4 にすると、段 7 の保存 `stage7_save_a.plmotion.json` と同一。`stage4_test` は読んで書き、読み直して書いても同一・再生も同一
  - Studio の `bClone`・`bMakeFrom`：段 5 の状態で子のバンドル B を複製すると親 N の `childIds` に入り（重ねる順 A,B,B_copy,H,…）、子のバンドル D を別のバンドルに束ねると元の親 C の `childIds` から除かれた。どちらも parentId と childIds の食い違い 0
  - 画面：「PolyLingモーション保存」を実クリックし、version 5 の Blob（81962 バイト）が画面の出力と同一、表示は「PolyLing v5 保存」
  - コンソールのエラー：区切りごとに読み、`stage1_studio.plmotion.json` を作る前に fetch した 2 件の 404 だけ。Unity のログのエラー 0 件。C# はエディタで再コンパイルしてエラー 0、Python の変更した 5 ファイルは `py_compile` で通った

### 段 2：本体の評価（平らにする） — 状態：完了（2026-10-31）

- `timeline.html` の `layerInfo`・`sampleCh`・`sampleClips`・`sampleContinuous`・`plNeedsBake`・`plMakeTrack`（`plKeyMap`・`plBakeKeys` を含む）を C# に移す（新しいファイル。キーの評価は `MotionCurveMath` を使う）
- Studio の `Q.slerp` を `plmotion_curve.js` の `slerp` に置き換える（2.2）
- 確認：段 3〜5 の試験の状態（`SandBox/curve_test/`）を version 5 で保存し、本体で平らにした結果と、Studio が書いた平らなトラックを全キーで比べる。差の最大を書く

実施結果：
- 本体
  - 新しいファイル `Motion/MotionEditFlattener.cs`（静的クラス）。`Flatten(edit, frameRate, out maxFrame)` が edit のトラックごとに平らなトラック（`FlatTrack`：ボーンは `MotionTrackDTO`、スカラーは `MotionScalarTrackDTO`／表情は `MotionExpressionTrackDTO`、標本化したか）を返す。`Apply(dto)` が `(role, targetKind, target)` で当たる平らなトラックのキー・ラップ（表情は category・provider も）を置き換え、duration を決め直す。先頭のコメントに規則（ラップ・キー時刻・重み・1 列・クリップの組・レイヤー・標本化・回転・書き分け・長さ）を書いた
  - 移した規則と Studio の対応：`trackWrap`→`BuildTrack`、`keySec`→`KeySec`、`evalRows`→`BuildEvaluator`・`Eval`（`MotionCurveMath` の `TryEvaluateRotation`・`TryEvaluateVector3`・`EvaluateScalar`、回転は写しに `EnsureQuaternionContinuity`）、`KIND`→`Rest`・`Weight`・`Mix`、`Clip.sampleChannel`・`sampleAt`→`SampleChannel`・`SampleAt`、`sampleContinuous`・`sampleClips`・`sampleCh`→同名、`layerInfo`→`BuildLayerInfo`、`plNeedsBake`→`NeedsBake`、`plMaxFrame`→`Flatten` の最終フレーム、`plBakeKeys`→`BakeBone`・`BakeScalar`、`plKeyMap`→`KeyMap`、`plMakeTrack` と `exportPolyLing` の書き分け→`MakeTrack`、duration→`Apply`
  - 丸めは JS の `Math.round`（`floor(x+0.5)`）、7 桁は `Math.Round(…, 7, AwayFromZero)`、同じ start のクリップは元の順（安定な並べ替え）。回転の混ぜ方は入力を正規化して近い側にそろえた `Quaternion.Slerp`
  - Studio との違いとして残るもの：Studio は muscles・body を書かなかったとき（`plLastDropped`）もそれらのトラックを最終フレームに含めるが、edit にはそのトラックが無いので本体の最終フレームには入らない
  - 本体のどこからも呼ばない（段 3 で窓口が呼ぶ）。試験の口として `exportMotionJson` に引数 `reflattenEdit`（既定 false）と結果 `reflattened`（置き換えた本数）を足した（`PanelCommand.Motion.cs`・`PlayerCommandDispatcher.Motion.cs`）。`queryCommandAudit` は付け忘れ・衝突・引数の対応なし・未対応の型・説明・XML タグ・複数コンストラクタ すべて 0
- Studio（`timeline.html`）
  - `KIND.quat` の `mix`・`weight` を `PLCurve.slerp` に替え、`Q.slerp` を消した。これでクロスフェード・override（mix）、クリップの重み・`sampleContinuous` の重み付きキー・additive の重み（weight）が本体と同じ式になる
  - `KIND` と `sampleContinuous` の前に、規則の正本が `MotionEditFlattener.cs` であることを書いた
  - 置き換えで Studio の再生（`Track.sample`、全トラック・整数フレーム 0〜最終フレーム、回転は符号をそろえて）が変わった大きさ：段 3 の状態 最大 2.0e-4（Chest rot[2]、フレーム 21、0.2260375 → 0.2262356）、段 4 の状態 0、段 5 の状態 最大 4.0e-4（Spine rot[0]、フレーム 54、−0.0914703 → −0.0910703）。差し替え後の値は、ページ上で `KIND.quat` を差し替えて取った値と、ファイルを直して読み直した後の値が全トラック・全フレームで同一
- 確認（内蔵ブラウザ・serve_root.py・Zunko_VRM10K.vrm、本体は Play 中のエディタ、組み立てと比較は手元の PC の `.venv` の python）
  - 試験の状態の作り方：段 3 は会話「Motion Studio 機能強化 段3」の `_edit`（curve_test を読み、Spine 重み 0.5・まばたき 重み 0.7・Chest に重なるクリップ・LeftUpperArm を単独ループ・あ にループクリップ）、段 5 は会話「POLYLING本体のデータ操作計画」のスクリプト、段 4 は画面操作で作った状態でスクリプトが無いので `stage4_test.plmotion.json` を読んだ状態を使った。置き換え前の保存の平らな部分（version 4・edit なし）は、段 3・段 5 が `stage3_test`・`stage5_test` と同一。段 4 は読込の正規化で Spine の 1 キーの 2 成分が 3e-17 違う（−0.1305259853353411 → −0.13052598533534113、0.9914448886106773 → …774）
  - 置き換え後の version 5 の保存：`SandBox/curve_test/stage2_s3_studio.plmotion.json`・`stage2_s4_studio.plmotion.json`・`stage1_studio.plmotion.json`（段 5。作り直した）。数の差分（`stage2_s*_patch.*`）と `stage2_build.py` で組み立て、fetch した中身が画面の出力と同一（トラック・クリップ・バンドルの id は読み込むたびに番号が進むので、組み立てに使った出力と同一、id の番号を除いて今の出力と同一）
  - 本体：平らなトラックを壊した入力（edit に当たるトラックをキー 1 個・レスト、duration 0.5。`stage2_broken.py`）を `exportMotionJson`（`reflattenEdit` true）で書き、`stage2_cmp.py` で Studio の保存と比べた。3 本とも エラー・警告 0、置き換え 6 本、duration 2（Studio と一致）、edit 一致、キーの数・接線・ラップ・category などの違い 0 件
    - 段 3：6 トラック・251 キー。時刻の差の最大 5.0e-8（Spine キー 35、1.1666667 / 1.16666675：float の表し方）、値の差の最大 3.1e-7（表情 あ、t=1.1666667、0.8291667 / 0.8291664）
    - 段 4：6 トラック・21 キー。時刻の差 0、値の差の最大 5.1e-8（Spine t=1.8 rot[3]、float の表し方）
    - 段 5：6 トラック・308 キー。時刻の差の最大 5.0e-8（Hips キー 35）、値の差の最大 1.4e-7（Hips t=0.8666667 pos[2]、0.02987705 / 0.02987691）
  - コンソールのエラー：区切りごと（読み直しの前を含む）に読み 0 件。Unity のログのエラー 0 件。C# はエディタで再コンパイルしてエラー 0

### 段 3：本体の編集の口と取り消し — 状態：完了（2026-10-31）

- 窓口 `motionClip` が編集の構造を持ち、変えたら平らにし直してフレームを当て直す
- 口：トラック・クリップ（追加・削除・開始・長さ・ループ・重み）、キー（追加・削除・値・時刻）、接線（モード・傾き・重み）、ラップ、バンドル（作る・解く・入れ子）、レイヤー（方式・重み・ミュート・ソロ・重ねる順）
- 取り消しの記録を足す。載せるスタックは着手時に `UndoStack` と既存の記録を読んで決める
- `muscles` を持つクリップで `bakedBones` を変えたときの扱いを、着手時に `CanonBoneMotion` と `MotionClipSerializer.Parse` を読んで決める（Studio は `muscles` と `body` を書かない）
- 保存（`saveMotionClipJson`・パネルの JSON 保存）は `edit` と平らなトラックを書く

実施結果：
- 決めたこと
  - edit は読込では作らず、最初の口で作る（`MotionEditOps.EnsureEdit`。Studio の `importPolyLing` の写し）。edit の当たらない平らなトラックを、トラックごとにクリップ 1 本で後ろに足す。並びは bones・body・lookAt・bakedBones・expressions・muscles。クリップは先頭 0、長さ `max(1, ceil(maxT·fps − 1e-4) + 1)`。キーは `f = max(0, round(t·fps))`、回転は正規化、`t0 = t`。id は `t`・`c`・`b` に edit の id の数字の最大値＋1。path を持つ表情と派生のトラックは足さない
  - 口の流れ（`MotionEditOps.Run`）：EnsureEdit → 平らにし直す → 操作 → 整え（クリップを start 順、空のチャンネルは null、無いクリップを clipIds から除く）→ 平らにし直す → 派生の作り直し。窓口は写しに対して行い、失敗したらクリップを変えずに `Error` に理由を入れる
  - 平らにし直しは `MotionEditFlattener.Reflatten`（`Apply` との違い：当たる平らなトラックが無ければ作る、bakedBones でキーが残らないトラックは平らなトラックを除く、長さの規則の元の長さは読込時の長さ＝Studio の `meta.duration`）
  - 派生：bakedBones が正本（`BakedBonesArePrimary`）なら muscles・body は派生、muscles が正本なら bakedBones は派生。派生のトラックは edit に足さず、口ではエラー。bakedBones が正本で口が平らな bakedBones か lookAt を変えたら、muscles・body を捨てて `BakeMuscles` で作り直し（Parse が Studio の保存を読むのと同じ結果）、edit の muscles・body のトラックを除く。muscles が正本のときの bakedBones は保存の `BakeBones` が作り直す（今のまま）
  - 取り消し：口 1 回で記録 1 件（変える前と後の DTO の `MotionClipSerializer.ToJson`）。`MeshUndoController.CreateSubWindowStack("PlayerEdit/MotionClipEdit")` に積み、既存の Undo・Redo（`performUndo`・`performRedo`）で戻る。読込・Clear・EditClip で世代を進め、世代の違う記録は何もしない
  - 保存：`saveMotionClipJson`・`queryMotionClipJson` は今のまま edit と平らなトラックを書く。`PrepareForSave` は平らなトラックの無い bakedBones の edit トラックを書かない（`MotionEditOps.DropUnmatchedBakedTracks`、Studio と同じ）。パネルの JSON 保存は、口で変えた後（窓口の状態 `editedSinceLoad`）は元ファイルがあっても `SaveMotionClipJsonCommand` で窓口のクリップを書く
  - `EditClip`（createLipSyncFromVoicevox）は edit を捨てない。書き換えの前後で平らなトラックを比べ、中身の変わったトラックの edit トラックはクリップ 1 本の形で作り直し（id は残す）、平らなトラックの無くなったものは除き、新しい平らなトラックは足す（`MotionEditOps.SyncEditAfterFlatChange`）。edit の無いクリップには作らない
  - 確かめる手段：コマンドは足さず、口は窓口の `[PLToolAction]` として既存の `invokeToolAction` で呼ぶ（引数は argKeys・argValues）。状態は `queryToolState`（`error`・`lastCreatedId`・`hasEdit`・`editedSinceLoad`）
- 口（窓口 `motionClip`、Studio の対応）
  - `editAddTrack`・`editRemoveTrack`（`addTrack`・`removeTrack`）、`editSetWrap`（`Track.preWrapMode`・`postWrapMode`）
  - `editAddClip`（`Track.addClip`）、`editRemoveClip`（`removeClip`）、`editSetClip`（クリップのインスペクタ。重みは 0～1 に丸める）
  - `editCaptureKey`（「キー追加」＝ `captureKey(lf, track.sample(start + lf))`）、`editSetKeyValue`（キーの値）、`editRemoveKey`（`removeKeyAt`）、`editMoveKey`（`moveKey`。移り先にキーがあればエラー）
  - `editSetTangentMode`（`setMode`）、`editSetTangentSlope`（`setSlope`）、`editSetWeightedMode`（`setWeightedMode`）、`editSetTangentWeight`（`setWeight`）
  - `editMakeBundle`（`bMakeFrom`）、`editUngroup`（`bUngroup`）、`editCloneBundle`（`bClone`）、`editDeleteBundle`（`bDeleteWithClips`）、`editSetLayer`（バンドルのインスペクタのレイヤー欄）、`editSwapLayer`（`layerSwap`）
- 変更したファイル
  - 新：`Motion/MotionEditOps.cs`（操作の中身）、`ToolHandlers/MotionClipHandler.Edit.cs`（口・記録・流れ）
  - `MotionEditFlattener.cs`：`Reflatten`・`SampleTrackChannel`（Studio の `Track.sampleCh`）・`KeySeconds`・`RoundJs`・`NormalizeQuatJs`
  - `MotionCurveMath.cs`：`ResolveIn`・`ResolveOut` を public（接線を Free にするときの今の傾き）
  - `MotionClipSerializer.cs`：`PrepareForSave` の `DropUnmatchedBakedTracks`、`Clone`・`FromJsonRaw`
  - `MotionClipHandler.cs`：partial、読込の長さ・世代、`EditClip` の edit の合わせ、`_lastTime`
  - `PolyLingPlayerViewerCore.Layout.TestPanels.cs`：`GetUndoController`・`OnClipEdited` の配線。`PlayerMotionClipTestSubPanel.cs`：`AdoptEditedClip`、JSON 保存の分岐
- 確認（本体は Play 中のエディタ・空のモデル、Studio は内蔵ブラウザ・serve_root.py。比較は Studio のページで、本体の保存を fetch して行った。edit は id の数字を除き数を float32 に丸めて全欄、平らなトラックは段 2 と同じく全キーで時刻・値〔回転は符号をそろえる〕・接線・ラップ）
  - 段 3 の状態（`stage2_s3_studio`）：32 の操作（全 20 種の口）を本体と Studio に同じ順で当てた。edit の違いは、キーを打った（`captureKey`）キーの値 7 個だけで差の最大 3.0e-8（評価の float と double の違い）。平らなトラック 193 キー：時刻の差の最大 5.0e-8、値の差の最大 8.7e-8（Chest のキー 52 rot[2]）、接線・ラップ・長さの違い 0
  - 段 4 の状態（`stage2_s4_studio`）：15 の操作。edit は同一。194 キー：時刻 5.0e-8、値 5.1e-8、接線・ラップ・長さの違い 0
  - 段 5 の状態（`stage1_studio`）：13 の操作（レイヤーの方式・重み、重ねる順、子のバンドルの複製、解く、子を持つバンドルの削除、バンドルの入れ子ほか）。edit の違いはキーを打ったキーの値 3 個だけで差の最大 1.3e-8。308 キー：時刻 5.0e-8、値 3.0e-7（表情 あ のキー 7）、接線・ラップ・長さの違い 0
  - 取り消し（段 3 の状態で、全 20 種の口それぞれの直後に Undo・Redo）：Undo 後の保存は口の前の保存と、Redo 後の保存は口の後の保存と、すべてバイト一致（`stage3_undo_check.py`、`SandBox/curve_test/s3/`）。キーの無い bakedBones のトラックを足した直後（保存では書かない）も一致
  - edit の無いファイル：`curve_test`（v4）で 7 の操作。edit の違いは読込の正規化（回転を float で割るか double で割るか）とキーを打った値の 11 個で差の最大 3.0e-8。136 キー：時刻 5.0e-8、値 9.4e-8。`stage4_test`（v4）で 2 の操作：edit は同一、22 キー：値 5.1e-8。最初の口の Undo で edit の無い状態にバイト一致で戻った
  - 失敗した口（移り先にキーがある `editMoveKey`）は `error` に理由が入り、クリップは変わらない
  - C# はエディタで再コンパイルしてエラー 0。コマンドは足していない。Unity のログのエラー 0 件、ブラウザのコンソールのエラー 0 件
  - 確かめていないもの：パネルの JSON 保存の画面操作（`createLipSyncFromVoicevox` の edit の合わせは段 4 で確かめて直した）

### 段 4：コマンド — 状態：完了（2026-10-31）

- 照会：トラック・クリップ・キー（値・接線）・バンドル・レイヤーの一覧、時刻を指定した評価値
- 書き込み：段 3 の口ごとのコマンド
- 配線は `PolyLing_追加作業の必読.md` の A 節。入れ子の配列は送れないので、平坦な列で持つ
- 確認：`queryCommandAudit` 全項目 0。コマンドで段 5 の試験の状態を一から作り、保存が Studio で作ったものと同じ評価になること

実施結果：
- 決めたこと
  - 照会は窓口のクリップを変えない。edit の無いクリップは、写し（`MotionClipSerializer.Clone`）に `EnsureEdit`・`NormalizeEdit` を当てて答え、結果の `editHeld` を false にする。写しの id は最初の書き込みで作られる id と同じ（`curve_test` で照会した t1～t11・c2～c12 が、書き込みの後もそのまま。最初の `motionEditAddClip` は c13）
  - 照会の結果は並行配列（個数の列＋連結した値の列）。null は文字列なら空、数なら 0 と有無の列（`keyHasT0`、接線は `tanFlags` のビット）
  - 派生のトラックは `trackDerived`（1 なら書き込みはエラー）で示し、edit に入っていない平らなトラック（派生・path を持つ表情）は `uncoveredFlat`（`role:target`）で返す
  - 書き込みは窓口の口を呼ぶだけ。取り消しは口が積む記録を使い、コマンドでは積まない。口の `Error` が空でなければ `Fail`（InvalidArgs）、クリップが無ければ `Fail`（NotReady）。作る 4 本は `createdId` を返す。`time` を省く（-1）と直前に当てた時刻（窓口に `LastTime` を足した）
  - id の再利用はコマンドの説明に書いて示す（振り方 `MotionEditOps.Ids` は変えない）。確認：バンドル b32 を除いた直後の `motionEditCloneBundle` は b32 を返した
  - `Writes = None`（既存のモーションのコマンドと同じ。`RemoteOwnership` は素通しなので `case` は足していない）。振り分けは `DispatchMotionFile` の `default` から（モデルの null 門より前）
  - 書き込みの 20 本は画面の経路を段 5 で付ける。それまで `queryCommandAudit` の「[参考] PLUiRoute も NoUi も無い」に出る。照会の 4 本は `NoUi`
- コマンド（`Core/Data/PanelCommand.MotionEdit.cs`、受け口 `View/Core/PlayerCommandDispatcher.MotionEdit.cs`）
  - 照会：`queryMotionEditTracks`（トラックとクリップ）、`queryMotionEditKeys`（clipId、channel 省略で全チャンネル。値・t0・評価に使う秒・接線）、`queryMotionEditBundles`（中身・親子・レイヤーの設定、実効の重みとミュート、重ねる順）、`sampleMotionEdit`（trackIds 省略で全トラック、frames は小数可。`MotionEditFlattener.SampleTrackChannel`）
  - 書き込み：`motionEditAddTrack`・`RemoveTrack`・`SetWrap`・`AddClip`・`RemoveClip`・`SetClip`・`CaptureKey`・`SetKeyValue`・`RemoveKey`・`MoveKey`・`SetTangentMode`・`SetTangentSlope`・`SetWeightedMode`・`SetTangentWeight`・`MakeBundle`・`Ungroup`・`CloneBundle`・`DeleteBundle`・`SetLayer`・`SwapLayer`（名前の頭はどれも `motionEdit`）
- ほかの変更：`MotionEditFlattener.LayerSummary`（`BuildLayerInfo` の重ねる順と実効の重み・ミュートを返す。評価は変えていない）、`MotionClipHandler.LastTime`、`PlayerCommandDispatcher.Motion.cs` の `default`
- 確認（本体は Play 中のエディタ・空のモデル、Studio は内蔵ブラウザ・serve_root.py・Zunko_VRM10K.vrm。比較の関数は `SandBox/curve_test/stage4_cmp.js`）
  - `queryCommandAudit`：付け忘れ・action の衝突・引数の対応なし・未対応の型・説明の無い道具・XML タグ・複数コンストラクタ・機能カテゴリの未設定・利用シーンの categories・MeshRefSpace・WriteWhen すべて 0。「正典に無い」10 件（scenario.log・external.comfyui・geometry.create.profile）と「出せなかった 3」（Ignore で既定値の無い 3 本）は今回のコマンドではない
  - コマンドだけで段 5 の試験の状態を作った：`curve_test` を読み、クリップ 10 本（`motionEditAddClip`、重み・ループは `motionEditSetClip`）、キーは `motionEditCaptureKey` の後 `motionEditSetKeyValue`、バンドル 9 個と レイヤー 8 個（`motionEditMakeBundle`・`motionEditSetLayer`）。保存 `SandBox/curve_test/stage4_cmd.plmotion.json`
  - キーを打つ口は全チャンネルにキーを作る（Studio の `captureKey` と同じ）ので、ボーンのクリップは rot だけでなく pos・scl のキーも持つ。このため `stage1_studio`（スクリプトで rot だけのキーを持つ）とは同じ状態にならない。比較は Studio で同じ操作（`addClip` → `captureKey(lf, track.sample(start + lf))` → キーの値を書く → `bMakeFrom`・`normLayer`）を当てた export() と行った
    - edit：違いはキーを打ったキーの値 8 個だけで差の最大 6.0e-8（評価の float と double の違い。段 3 と同じ）。ほかの欄は id の数字を除き同一
    - 平らなトラック 308 キー：時刻の差の最大 5.0e-8、値の差の最大 1.5e-7（Hips のキー 26 pos[2]）、接線・ラップ・長さの違い 0。本体が足したのは muscles 9 本・body・rotationSource（段 1 と同じ）
    - `stage1_studio` との比較では、キーの数・接線・ラップ・時刻は一致し、値は Hips の pos が最大 0.17 違う（Hips の H_add のフレーム 15 に pos のキーができるため）
  - 照会の結果と保存の edit：`queryMotionEditTracks`・`queryMotionEditBundles` の全欄、`queryMotionEditKeys`（c2・c8・c10・c13 と c17 の w）の全欄が、保存の edit から作った列と float32 で一致。重ねる順と実効の重み・ミュートは Studio の `layerInfo`（A,B,H,C,D,E,F,G）と一致
  - `sampleMotionEdit`（全トラック・全チャンネル、フレーム 0～60 の 4 おきと 7.5・33.25 の 252 行）と Studio の `Track.sampleCh`：差の最大 3.7e-7（Hips pos[2]、フレーム 7.5）
  - 取り消し：6 種（SetClip・MoveKey・SetTangentMode・SwapLayer・CloneBundle・RemoveTrack）の直後に `performUndo`・`performRedo`。Undo 後の保存は前の保存と、Redo 後は後の保存とバイト一致（`stage3_undo_check.py`、`SandBox/curve_test/s4/`、`stage4_undo_run.py`）
  - 失敗したコマンド（移り先にキーがある `motionEditMoveKey`、無いクリップの `motionEditSetTangentMode`）は Fail で理由を返し、保存はその前とバイト一致
  - C# はエディタで再コンパイルしてエラー 0。Unity のログのエラーは、保存先を作業フォルダの直下にして拒まれた 1 件だけ。ブラウザのコンソールのエラー 0 件
- 追加（同日）：チャンネルを指定してキーを作る・除く口。それまでは口が全チャンネルに打つもの（`editCaptureKey`。Studio のインスペクタの「キー追加」＝ `captureKey` と同じ）しか無く、rot だけのキーを持つクリップを作れなかった
  - 本体：`MotionEditOps.SetChannelKey`（1 チャンネルのフレーム f にキーを作る。あれば値だけ変え、接線・t0 は残す。無ければ `{f, v}`。f は 0 以上・長さ未満、値の検査は `SetKeyValue` と同じ）・`RemoveChannelKey`（1 チャンネルのキーだけ除く。無ければエラー）、口 `editSetChannelKey`・`editRemoveChannelKey`、コマンド `motionEditSetChannelKey`・`motionEditRemoveChannelKey`
  - Studio（`timeline.html`）：`Clip.setChannelKey(ch, lf, v)`・`removeChannelKey(ch, lf)`（データの操作だけ。画面のボタンは無い）
  - 確認
    - `queryCommandAudit` の完了条件の項目すべて 0（コマンド 521）
    - コマンドだけで段 5 の状態を作り直した（キーはすべて `motionEditSetChannelKey`）。保存 `stage4_cmd2.plmotion.json` と `stage1_studio.plmotion.json`：平らなトラック 308 キーで時刻の差の最大 5.0e-8、値の差の最大 1.4e-7、接線・ラップ・長さの違い 0。edit の違いは `curve_test` から読んだクリップの回転 7 個だけで差の最大 6.0e-8（読込の正規化を float で割るか double で割るか。段 3 と同じ）。足したクリップ・キー・バンドル・レイヤーは id の数字を除き同一
    - Studio で `curve_test` を読み、同じ状態を `setChannelKey` で作った export() は `stage1_studio` と edit・平らなトラックとも同一（値の差 0）
    - 本体と Studio に同じ 3 つの操作（新しいキー scl、あるキー rot の値、pos のキーを除く）を当てた後の比較：平らなトラック 308 キーで値の差の最大 1.5e-7、接線・ラップ・長さの違い 0、edit の違いは上と同じ 7 個だけ
    - 取り消し：その 3 つの直後に `performUndo`・`performRedo`。Undo 後・Redo 後の保存はそれぞれ前・後の保存とバイト一致（`SandBox/curve_test/s4c/`）
    - 失敗（キーの無いチャンネルを除く、範囲外のフレーム、トラックに無いチャンネル、値の個数違い）は Fail で理由を返し、保存はその前とバイト一致。Studio の関数も同じ場合に false・null を返し何も変えない。派生のトラックのクリップへの呼び出しは試していない（この試験のクリップでは派生のトラックが edit に無い。コードでは他の口と同じ `ClipOf` の判定を通る）
    - C# はエディタで再コンパイルしてエラー 0。Unity のログのエラー 0 件、ブラウザのコンソールのエラー 0 件
- 追加（同日）：`createLipSyncFromVoicevox` の edit の合わせ（段 3 で確かめていなかったもの）を VOICEVOX で確かめ、直した
  - 見つけたこと：段 5 の状態（edit あり）に「こんにちは」（開始 0.2 秒）を入れると、あ はクリップ 1 本で作り直され（元のクリップはバンドルの clipIds から除かれる）、い・お は後ろに足された（設計どおり）。ただし保存の平らな あ はキー 37 個、edit を平らにし直すと 36 個で食い違った。口パクの終わりのキー（t=1.16246188、フレーム 34.87）と範囲外の既存のキー（t=1.16666675）が同じフレーム 35 に丸まり、edit（チャンネルごと・フレームごとに 1 キー）への取り込みで前のものが上書きされたため。食い違ったままだと次の口・Studio の読込でそのキーが黙って消える
  - 直したこと：`MotionClipHandler.EditClip` は edit があるとき、`SyncEditAfterFlatChange` の後に `MotionEditFlattener.Reflatten` で平らなトラックを edit から作り直す。`SyncEditAfterFlatChange`（と `EnsureEdit` の多重定義）は同じフレームに丸まって捨てたキーの数を返し、コマンドの結果 `droppedKeys` に出す
  - 確認：同じ状態・同じ呼び出しで `droppedKeys` 1。保存の平らなトラックと、それを `reflattenEdit` で平らにし直したものが 294 キーで同一（値・時刻の差 0）、Studio で読んで書いたものとも edit 同一・平らなトラックの差の最大 1.5e-7（接線・ラップ・長さの違い 0）。edit は直す前の保存と同一。edit の無いクリップ（`curve_test`）では `droppedKeys` 0、edit は作られず、あ のキーは秒のまま（1.11246181 と 1.16246188 も残る）。再コンパイルでエラー 0、Unity のログ・ブラウザのコンソールのエラー 0 件

### 段 5：パネルの数値欄 — 状態：完了（2026-10-31）

- 「モーションの試し再生」パネルに編集の節を足す（トラック・クリップ・キーを選ぶ、値・接線・ラップ、クリップの開始・長さ・ループ・重み、レイヤーの方式・重み・ミュート・ソロ・重ねる順）
- 部品は `PolyLing_追加作業の必読.md` の B 節に従って登録する
- 確認：`queryUiAutomationAudit` 全項目 0。`uiSetValue` で各欄を変えて反映されること

実施結果：
- 決めたこと
  - パネルの欄・ボタンは段 4 の書き込みコマンドを `DispatchFromPanel`（失敗理由を返す）で送る。受け口は口を呼ぶだけなので、取り消しは口の記録 1 件だけ。数の欄は確定したときだけ送る（`isDelayed`）。時刻はパネルの今の時刻。作った id は窓口の `lastCreatedId`
  - 表示は窓口のクリップ（`GetClip`）を `MotionEditOps.EditView` で見る。edit の無いクリップは写しに `EnsureEdit`・`NormalizeEdit` を当てたもの（照会のコマンドと同じ関数に寄せた。id も最初の書き込みと同じ）で、「最初の操作で作ります」と出す
  - 選択はトラック・クリップ・バンドルを id、キーをチャンネルとローカルフレームで持つ。作り直し（口・取り消し・読み直し）の後、id があればそのまま、無ければ先頭（無い id は持ち続けない）。フレームはクリップの範囲内なら保ち（キーの無いフレームも保つ。値の欄はそのフレームの評価値）、範囲外ならそのチャンネルの先頭のキー（無ければ 0）。作る操作の後は作ったものを選ぶ。キーの一覧は、選んでいるフレームにキーが無いとき「（なし）」を出す
  - 派生のトラックは一覧に「派生・変更不可」と付け、書き込みの欄を使えなくする。edit に入っていない平らなトラック（派生・path を持つ表情）は節の先頭に列挙する
  - 値は回転も四元数の x,y,z,w をそのまま出す（コマンドの値と同じ）。接線は Studio の `keySlopes` の写し `MotionEditOps.KeyTangentView`（保存の無い傾きは求めた値、重みは既定 1/3 を出し、どちらかを `edit.tan.info` に出す）
  - UI 自動操作の ID は `motionClipTest.edit.*`（`edit.track`・`edit.track.preWrap`／`postWrap`／`remove`、`edit.newTrack.role`／`targetKind`／`target`／`name`／`add`、`edit.clip`・`edit.clip.start`／`len`／`loop`／`weight`／`remove`、`edit.newClip.start`／`len`／`add`、`edit.key.channel`／`select`／`frame`／`value.x`～`w`／`capture`／`setChannel`／`removeChannel`／`remove`／`moveTo`／`move`、`edit.tan.comp`／`inMode`／`outMode`／`inSlope`／`outSlope`／`weightedMode`／`inWeight`／`outWeight`、`edit.bundle`・`edit.layer.enabled`／`mode`／`weight`／`mute`／`solo`／`up`／`down`、`edit.bundle.ungroup`／`cloneShift`／`clone`／`delete`、`edit.newBundle.clipIds`／`bundleIds`／`addClip`／`addBundle`／`name`／`make`、表示の `edit.info`・`edit.clip.info`・`edit.key.state`・`edit.tan.info`・`edit.bundle.info`・`edit.layer.order`）。選ぶ欄は id（キーはフレームの数、成分は番号）でも選べる（Setter）。どれも折り畳み「編集（数値）」を開く下準備を持つ。除く系（トラック・クリップ・チャンネルのキー・キー・バンドル）は Destructive、ほかは SafeWrite
  - 書き込みの 22 本に `PLUiRoute`（`leftPane.fold.Other` → `leftPane.motionClipTestBtn` → 対象を選ぶ欄 → 操作の欄・ボタン）。照会 4 本は `NoUi` のまま
- 変更したファイル
  - 新：`View/SubPanels/Motion/PlayerMotionClipTestSubPanel.Edit.cs`（編集の節）
  - `PlayerMotionClipTestSubPanel.cs`（partial、節の組み込み、`RefreshAll` から `RefreshEdit`）
  - `PolyLingPlayerViewerCore.Layout.TestPanels.cs`（`RunCommand = DispatchFromPanel`・`GetClip`）、`PolyLingPlayerViewerCore.UiAutomation.cs`（パネルの説明）
  - `Motion/MotionEditOps.cs`（`EditView`・`TangentView`・`KeyTangentView`）、`PlayerCommandDispatcher.MotionEdit.cs`（`TryMotionEditView` が `EditView` を使う。答えは変えていない）
  - `Core/Data/PanelCommand.MotionEdit.cs`（`PLUiRoute` 22 本）
- 確認（本体は Play 中のエディタ・空のモデル。保存は `saveMotionClipJson`）
  - `queryCommandAudit`：完了条件の項目すべて 0（コマンド 521）。「PLUiRoute も NoUi も無い」に `motionEdit*` は 0 本（全体 415）
  - `queryUiAutomationAudit`：全項目 0（下の「追加」で左ペインの残りを直した後。直す前は安全度未指定のボタン 145・未登録のボタンや入力欄 81 が左ペインに残っていた）
  - 欄の反映：`stage4_cmd2` を読み、22 本すべての経路を `uiSetValue`・`uiClick` で通した（クリップの開始・長さ・ループ・重み、ラップ前後、チャンネルのキーを作る・値を変える・除く、キーを打つ・動かす・除く、接線のモード入出・傾き入出・重み方式・重み入出、トラックを足す・除く、クリップを足す・除く、バンドルを作る〔追記ボタン経由〕・解く・複製・除く、レイヤーの有無・方式・重み・ミュート・ソロ・↑↓）。`queryMotionEditTracks`・`queryMotionEditKeys`・`queryMotionEditBundles` に反映。除いた直後の id の再利用（トラック t35・クリップ c36 を除いた後に作ったバンドルが b35）も表示と一致。失敗（移り先にキーがある移動、相手の無い ↓）は状態の欄に理由が出てクリップは変わらない
  - 取り消し：パネルの 8 種（クリップの重み、チャンネルのキーを作る、キーの値、接線のモード、接線の傾き、レイヤーの重み、バンドルの複製、ラップ）の直後に `performUndo`・`performRedo`。Undo 後・Redo 後の保存はそれぞれ前・後の保存とバイト一致。失敗した移動の後の保存も直前とバイト一致（`SandBox/curve_test/s5/`、`stage3_undo_out_s5.txt`）。複製の Undo の後、選んでいたバンドルは先頭へ移った
  - コマンドとの一致：`curve_test`（edit なし）に、パネルでクリップを足す（t1・10・30）→ 重み 0.5・ループ → rot のフレーム 5 にキーを作る → 入側を Auto → フレーム 20 でキーを打つ → c13 を束ねる（L）→ レイヤー additive・重み 0.4 をした保存 `panel_build.json` と、同じことを段 4 のコマンドでした `command_build.json` がバイト一致（115997 バイト）
  - 見た目：`uiCapture` で節の上・キーと接線・バンドルを撮った（`SandBox/Captures/s5_panel_0001`～`0005.png`）。値の欄が狭く数が切れたので見出しを別の行にした。キーの一覧が選んでいないフレームでも先頭を出していて、その先頭を選び直しても選択が進まなかったので、「（なし）」を出すようにした
  - C# はエディタで再コンパイルしてエラー 0（3 回）。Unity のログのエラーは区切りごとに読み 0 件
  - 確かめていないもの：パネルの「モーション」ボタンから人が開いたときの操作（MCP の `uiShowPanel`・`uiSetValue`・`uiClick` だけで確かめた）。パネルの「JSON保存」ボタン（保存ダイアログを開くので自動では押せない。段 3 からの残り）
- 追加（同日）
  - パネルの長さ表示：パネルを通さず窓口の `load`（`invokeToolAction`）で読むと「Length: 0.00s」のままでスライダーが動かなかった。パネルが長さを自分で持ち、取り直すのがパネルの読込・`loadMotionClipJson`・編集の口の後だけだったため。直したこと：パネルは `RefreshAll` で毎回窓口の `clipLength` から取り直し、今の時刻をその範囲に収める。窓口の `Load` が成功したら `OnClipReplaced(time)`（旧 `OnClipLoadedFromJson`。`LoadJson`・`EditClip` も呼ぶ）、`Clear` は `OnSourceChanged` を呼ぶ。パネルの受け口は `AdoptReplacedClip`（旧 `AdoptExternalClip`）。クリップが無いときも時刻の表示と編集の節を作り直す。確認：パネルを開いたまま `invokeToolAction` の load（time 0.5）で「Time: 0.50s / 2.00s」、スライダーの上限 2、clear で「0.00s / 0.00s」・編集の節は「クリップを読み込んでください」
  - 派生のトラックの表示：`s5/u00.json` から `rotationSource` を除いた `s5/derived_test.json`（muscles が正本になり bakedBones が派生）を読み、bakedBones の 4 トラックに「派生・変更不可」が付き、クリップの欄・キーを打つボタンが使えない（`uiSetValue`・`uiClick` が「control is disabled」）ことを確かめた。表情のトラックは付かない。クリップは変わらない（`editedSinceLoad` false）
  - 左ペインの監査の残り（`queryUiAutomationAudit`）：未登録のボタンや入力欄 81 は、画面分割の 3×3 ボタン 9 個と表示のチェック 72 個（4 画面 × 18 項目）で、配列で作るためプロパティの走査に掛かっていなかった。`leftPane.split.<モード名>`（`PlayerLayoutRoot.SplitModeButtons`）と `leftPane.display.<p/t/f/s>.<項目>`（`ViewportDisplayItemIds` など）で登録した（SafeWrite）。安全度未指定のボタン 145 は、左ペインの安全度を `LeftPaneSafety` が 6 個だけ名前で決めていたため。`PlayerLayoutRoot` の左ペインのボタンのプロパティ 151 個に `[UiControl(Safety = …)]` を付け、登録はそれを読む（`LeftPaneSafety` は消した）。押したときの処理を読んで、Destructive は「選択を削除」（`SubToolDeleteBtn`）と「切り取り」（`CutSelectionBtn`）、ほかはパネルを開く・モードの切り替え・選択・コピー・貼り付け・法線の再計算・回転の中心なので SafeWrite。`PolyLing_追加作業の必読.md` の B 節に書いた。確認：監査の全項目 0（ボタンや入力欄 2816 → 2897）、`leftPane.split.onlyPersp` で Perspective だけの 1 画面、`leftPane.display.p.selMesh` を false で Perspective の選択メッシュの面が消えた（どちらも戻した）
  - C# はエディタで再コンパイルしてエラー 0（2 回）。Unity のログのエラー 0 件

### 段 6：往復の検証と文書 — 状態：完了（2026-10-31）

- 本体で編集・保存 → Studio で読込 → Studio の再生と本体の評価を全フレームで比べる。逆向き（Studio で編集・保存 → 本体）も同じ
- VRMA 書き出し（本体・Studio）が平らなトラックから同じものになること
- `MCP操作手順書.md`・契約文書・Motion Studio のマニュアルに、version 5 と編集の口を書く

実施結果：
- 確かめ方（出力は `SandBox/curve_test/s6/`）
  - 比較は Studio のページで行った（`s6/stage6_cmp.js`・`s6/stage6_vrma.js`、edit と平らなトラックは段 4 の `stage4_cmp.js`）。比べたのは 3 つ：Studio の `Track.sampleCh`、本体の `sampleMotionEdit`、ファイルの平らなトラックを `PLCurve`（`MotionCurveMath.cs` の写し）で評価した値。本体に平らなトラックを評価するコマンドは無い。範囲は全トラック・全チャンネル・整数フレーム 0〜最終フレーム（どのファイルも 60）
  - 本体の大きな結果（全フレームの `sampleMotionEdit` など）は、Editor の 8770 を Python から呼んでファイルに書いた（`s6/pl_call.py`。呼ぶ並びは `s6/calls_*.json`）
  - Studio の出力は、確認用のサーバ `データとツール/サンドボックス_シナリオ用/verify1031/serve_root.py` に足した PUT（`SandBox/curve_test/s6/` の直下だけ）でそのままファイルにした。PUT した中身が `export()` と同一であることを確かめた
- 本体 → Studio（`stage4_cmd2.plmotion.json`、`s5/panel_build.json`）
  - Studio の再生と `sampleMotionEdit`（edit の 6 トラック・2562 値）：どちらも差の最大 9.9e-7（LeftUpperArm rot[2]、フレーム 15。float と double）。`sampleMotionEdit` と平らなトラックも 9.9e-7
  - 本体が足した派生の muscles 9 本・body は、Studio の再生と平らなトラックが最大 0.04（stage4_cmd2）・0.062（panel_build）違った → 不具合 1
- Studio → 本体（`stage1_studio.plmotion.json` と、Studio で新たに編集した `s6/studio_edit.plmotion.json`）
  - 新たな編集：stage1_studio を読み、Spine のクリップに rot のキーを作る（`setChannelKey`）、Chest のキーを動かす（`moveKey`）、LeftUpperArm のクリップをループ・重み 0.8、レイヤー H を override・0.4、E をソロ、LeftUpperArm に新しいクリップとそれを束ねた additive 0.5 のレイヤー
  - 本体で読み（エラー 0）、Studio の再生と `sampleMotionEdit`：最大 9.8e-7（stage1_studio）・7.8e-7（studio_edit）。Studio の再生と平らなトラック：3.5e-7
  - 往復：本体の保存（`s6/c1_cs_save`・`c2_cs_save`）を Studio で読んで書くと、edit は元の Studio の保存と同一（`cmpEdit`：id の数字を除き、数は float32 で全欄）、平らなトラック（308・366 キー）は時刻の差 0・値の差 2.0e-8、再生の差 2.0e-8。ただし本体が足した派生の muscles・body が edit と平らなトラックに入った → 不具合 1
- VRMA（上の 4 本と、studio_edit に lookAt トラックを足した `s6/look_track`、目の bakedBones を足した `s6/look_eyes`。本体は `convertMotionToVrma`、Studio は読込 → `MotionStudioVrma.export()` と、ファイルをそのまま `PLVrma.encode` したもの）
  - フレーム数 61・時刻・チャンネル（55、視線ありは 56）・表情（preset 2）と視線の割り当て・lookAt の offset は一致。値の差の最大：回転 9.9e-7、Hips の位置 1.6e-7、表情 8.9e-7、視線 1.2e-7（lookAt トラック）・1.75e-7（本体は目のマッスル、Studio は目の bakedBones から作る）
  - 指のノード 20 個（両手の全指の Intermediate・Distal）のレスト位置が違った → 不具合 2
- 不具合 1：Studio の保存で、本体が作った派生の muscles が正本になる
  - 原因：本体は bakedBones だけのファイルを読むと muscles・body を焼き、`rotationSource` を `"bakedBones"` にする（`MotionClipSerializer.Parse`）。焼いたキーは全トラックのキー時刻の和なので整数でないフレームを含む（stage4_cmd2 は 67 キーのうち 6 個）。Studio の `importPolyLing` は `rotationSource` を見ずにそれをクリップ 1 本のトラックにし（フレームごとに 1 キーで 61 キー）、`exportPolyLing` は `rotationSource` を書かずに平らなトラックと edit へ書き戻す（`plLastDropped` でないとき）。本体はそれを muscles が正本のファイルとして読み、edit の bakedBones 4 トラックが派生（`trackDerived` 1、編集できない）になり、再生も欠けた muscles から行う（`s6/b0_studio_resave_cmd2` で確かめた）
  - 直したこと（`timeline.html` の `importPolyLing`）：`rotationSource` が `"bakedBones"` で bakedBones を持つとき、muscles・body の平らなトラックと、その役割の edit のトラックを読まない（そのクリップはバンドルの `clipIds` から除く）。読込の表示にそう出す。本体の `MotionEditOps.IsDerivedRole`・`EnsureEdit`（派生のトラックは edit に足さない）と同じ扱い。muscles が正本のファイル（`rotationSource` なし）は今までどおり muscles・body を読み書きする（`s5/derived_test.json` で確かめた）
  - 確認：上の 4 本（本体の保存 2 本と本体の読み直しの保存 2 本）を Studio で読んで書く（`s6/d1`〜`d4`）と、muscles・body は無く、edit は元と同一、平らなトラックの値の差 1.4e-7 以内。本体で読むと `rotationSource` は `"bakedBones"`、派生のトラックは 0、保存（`s6/d*_cs`）の edit は元と同一、平らなトラックの値の差 1.4e-7 以内、作り直した muscles・body はキー数が元と同じで値の差 7.2e-7 以内
- 不具合 2：Studio の VRMA の指のレスト位置
  - 原因：`CanonSkeleton.Table` は 2026-10-14 に指を VRM 1.0 の T ポーズに合わせて変えた。`計画と規約/PolyLing_VRMA整備計画.md` の表と Python の `humanoid.py` は直っていたが、`plmotion_vrma.js` の `CANON_TABLE` だけ古い値のままだった
  - 直したこと：その 20 行を `CanonSkeleton.Table` の値に写し、表の前に正本と経緯を書いた。`decode` はこの表の親子だけを使うので読込は変わらない（本体の .vrma を読み直して警告 0）。`テスト用/polyling_motion_studio/` の写しは触っていない
  - 確認：VRMA の比較 6 本で、ノード・拡張・チャンネルの違いが 0 になった（値の差は上と同じ）
- 文書
  - `MCP操作手順書.md`：1-5「モーションの編集（edit）」（読込・保存・照会 4 本・書き込み 22 本・失敗と取り消し・パネルの編集の節・大きな結果をファイルにする方法）
  - 契約 §5：PolyLing の書き手の決まり（edit を編集する、`createLipSyncFromVoicevox` は edit を合わせる）を今の実装に直した。派生のトラックの扱いと、一致の範囲（整数フレーム、往復、キーの取り込み、VRMA）を足した
  - Studio のマニュアル：レイヤーの保存（edit に入る）、VRMA の行（平らなトラックから書く、本体と同じ中身）、「PolyLing との行き来」の節を足した
- エラー：Unity のログのエラー 0 件。C# は変えていない。ブラウザのコンソールのエラーは 8 件で、確認用のサーバを起動し直した間の接続失敗 5 件、PUT を拒むことを確かめた 403 が 2 件、本体の結果ができる前に読んだ 404 が 1 件。修正の後に増えたものは無い
- 確かめていないもの：左ペインの「モーション」ボタンから人が開く操作、パネルの「JSON保存」の保存ダイアログ（自動では押せない）

## 4. 対象外

- 本体でのグラフ表示・ドープシートなど、Studio の画面に当たるもの
- レイヤーの重みのキー（Studio にも無い）
- 平らなトラックを読む 30 ファイルの変更（2.1）

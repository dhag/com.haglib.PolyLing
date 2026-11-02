# PolyLing モディファイア比較（Blender・Maya）

`PolyLing_モディファイア枠組み計画.md` の段 9 の結果。代表的な 3 つの連続作業を PolyLing のシナリオで流し、途中変更・Undo・保存再読込の合否と、作るまでの操作回数を Blender・Maya と並べる。作成: 2026-11-01。

## 1. 数え方（3 者で同じ）

- 1 回 ＝ ボタン・メニュー項目を 1 回押す／1 欄に値を入れて確定／選択の 1 動作（クリック・矩形・ループ選択はそれぞれ 1）／モード切り替え・ショートカット 1 回
- ドロップダウンは「開く＋選ぶ」で 2。視点操作は数えない。形を手で整える操作（カーブの点を動かすなど）は数えない
- 出発点：PolyLing は空のモデル、Blender は既定シーン（作業 1・3 はキューブを消した後。消す操作は数えない）、Maya は空シーン
- PolyLing の回数は画面の経路から数えた（使うボタン・入力欄が `uiDescribe` に載っていることを確かめた。1 回通しての実測ではない）。作業 3 の生成コマンドには画面の経路の宣言（PLUiRoute）が無く、画面の回数は数えていない（5.）
- Blender は bpy 5.2.3 で既定値と挙動を実行して確かめたもの（表の【実測】）と、マニュアルから数えたもの。Maya はヘルプから数えたもので、手元では動かしていない。画面の位置が文書に無いものは【推定】

## 2. 合否（PolyLing。シナリオは `@settings` のシナリオ置き場のフォルダ「枠組み段9」）

| 作業 | シナリオ | 途中変更で下流が追随 | Undo・Redo | 保存再読込（CSV・二進）で一致 |
| --- | --- | --- | --- | --- |
| 板 → 厚み → ベベル → 曲げ | `枠組み9_板_厚み_ベベル_曲げ` | 合格：厚み 0.1→0.2 で 3 項目とも流し直し（「流した 3 件」）、元の頂点の移動で「処理器で作り直した」 | 合格 | 合格：頂点 109・面 96、位置 109 頂点すべて一致（CSV・二進とも） |
| 箱 → ブーリアン → ベベル | `枠組み9_箱_ブーリアン_ベベル` | 合格：B の `moveObjects` で「処理器で作り直した」（83→85 頂点）、演算を和にすると 91 頂点 | 合格 | 合格：頂点 85・面 124、位置 85 頂点すべて一致 |
| 髪 → パイプ → 揺れ鎖 | `枠組み9_髪_パイプ_揺れ鎖` | 合格：芯の毛先の移動で帯・揺れ鎖・パイプの 3 項目を作り直し、毛先のボーンが z 0.2 へ動く。パイプの厚みの変更で 16→32 頂点 | 合格 | 合格：パイプ 16 頂点の位置とボーン 5 本の BoneTransform が一致 |

- 作業 3 は既存のモディファイアスタック（オブジェクト間の生成スタック）で行った。処理列の種類は出力がメッシュ 1 つで（`IMeshModifier.cs:103`）、ボーンを作る揺れ鎖は載らないため
- 見た目は各段で `unity_capture`（`SandBox/Captures/stage9fw`）

## 3. 操作回数（作るまで）

| 作業 | PolyLing | Blender | Maya |
| --- | --- | --- | --- |
| 1. 板 → 厚み → ベベル（上端だけ）→ 曲げ | 28 | 37 | 23 |
| 2. 箱 → ブーリアン → ベベル | 37 | 20 | 19 |
| 3. 髪 → パイプ → 揺れ鎖 | 数えていない（5.） | 約 31〜33（揺れは追加アドオン） | 45 |

### 3.1 作業 1

| 手順 | PolyLing | 数 | Blender | 数 |
| --- | --- | --- | --- | --- |
| 板 | 図形生成 → 平面 → 高さ → 高さの分割 → 生成 | 5 | Grid を追加し、分割・回転・拡大・適用 | 12 |
| 頂点 ID | 頂点 ID パネル → 付与 | 2 | 不要 | 0 |
| 範囲 | 辺モード → 上端の辺を矩形選択 → パーツ選択辞書パネル → 登録 | 4 | 編集モード → 選択 → 頂点グループの＋ → 割り当て → 戻る（ベベル側の指定は下） | 7 |
| 厚み | 処理列パネル → 種類（2）→ 追加 → 厚み | 5 | Add Modifier → Solidify → 厚み | 5 |
| ベベル | 種類（2）→ 追加 → 量 → 分割 → 範囲の種類 → 辞書（2） | 8 | Add Modifier → Bevel → 制限方法 → グループ（2）→ 幅 | 8 |
| 曲げ | 種類（2）→ 追加 → 角度 | 4 | Add Modifier → Simple Deform → Bend → 角度 | 5 |
| 後から厚みを変える | 項目を選ぶ → 値 | 2 | 値 | 1 |

- Maya：Plane 7、Extrude（厚み）5、Bevel（辺を選ぶ）5、Bend 6
- Blender の曲げは原点を中心に左右対称にかかり、ローカル軸で計算する。平らな板では軸の選び方で変形しない（【実測】）。この作業では板を立てて回転を適用する手順が要った

### 3.2 作業 2

| 手順 | PolyLing | 数 | Blender | 数 |
| --- | --- | --- | --- | --- |
| 箱 A | 図形生成 → 直方体 → 角丸 0 → 分割 X・Y・Z → 生成 | 7 | 既定のキューブ | 0 |
| 箱 B | 幅・高さ・奥行き → 姿勢の折り畳み → 位置 X・Y・Z → 生成 | 8 | 追加して大きさ・位置 | 8 |
| 頂点 ID | A を選ぶ → パネル → 付与 | 3 | 不要 | 0 |
| ベベルの範囲 | 辺モード → 矩形選択 → 内側の辺を外す → 辞書パネル → 登録 | 5 | 不要（制限方法「角度」の既定で鋭い辺を拾う） | 0 |
| ブーリアン | 処理列パネル → 種類（2）→ 追加 → 相手（2） | 6 | A を選ぶ → Add Modifier → Boolean → 相手（2）→ B を隠す | 8 |
| ベベル | 種類（2）→ 追加 → 量 → 分割 → 範囲の種類 → 辞書（2） | 8 | Add Modifier → Bevel → 幅 | 4 |

- Maya：箱 2 つ 11、Difference 5、Bevel 3
- Blender のベベル（角度）はブーリアンの切り口の辺にもかかる（【実測】）。PolyLing は切り口の頂点の ID が 0 で（`BooleanMeshModifier`：入力と同じ位置の頂点だけ ID を写す）、切り口の辺を範囲に入れる手段が無い

### 3.3 作業 3

- Blender：カーブ 3、Bevel の Depth 3、アーマチュアのボーン 5 本 13、自動ウェイトの親子付け 4、揺れは本体に無く Wiggle 2 などの追加アドオンの導入 8〜10【推定】。カーブに自動ウェイトで親子付けしても頂点グループは空のままで変形しない（【実測】）。実用にはメッシュへ変換（+3）が要り、以後カーブを直しても形に反映されない。手で置いたボーンはカーブの編集に追随しない
- Maya：CV カーブ 10、Sweep Mesh 3、ジョイント 9、スキン 4、nHair で動的化と IK スプライン 19。ジョイントは手で置くので、カーブの編集に追随しない
- PolyLing：シナリオのコマンドは 4 本（辺の選択・`createEdgePipe`・帯の選択・`placeSpringBoneLadderChains`）。芯の頂点を動かすと、帯・パイプ・揺れ鎖のボーンまで流し直される（2.）。揺れ方の設定（`applySpringBoneByPrefix`）はこの作業に含めていない

## 4. Undo・保存再読込

- 3 者とも Undo は 1 回
- Blender：モディファイアの並びと値は保存して開き直しても残る（【実測】）。頂点グループは元メッシュを細分しても頂点に付いた値として引き継がれる（【実測】）
- Maya：構築履歴の polyModifier 系ノードは処理する部品を番号の並び（inputComponents）で持つ。上流で面構成が変わると番号が別の部品を指す（ヘルプの記述からの推論で、実測していない）
- PolyLing：範囲は頂点 ID で持つので、上流（厚み）の後も同じ頂点の写しに当たる（作業 1 で確認）

## 5. PolyLing が劣る点・不具合（段 9 で見つけたもの）

それぞれ計画書の 5. 未決事項に載せた。

1. ベベルで連続した辺の間に穴が空く（作業 1 で境界ループ 3、作業 2 で 4）。原因：`EdgeBevelOps.Execute` の Pass 1.5（`EdgeBevelOps.cs:259-306`）は、隣り合う 2 辺が隣の面を共有するときだけ端点を 1 つにまとめる。辺の間に別の辺（格子の線）が通る並びでは、2 辺の隣の面がどれも違うので、まとめられず同じ位置に頂点が 2 つずつ残る（作業 1 の頂点 85 と 91 が同じ位置）。既存のツールのベベルも同じ関数を通る
2. パーツ選択辞書を範囲にすると、同じ頂点 ID の頂点が複数あるとき（厚み・配列・ミラー（実体化）の後）1 つにしか当たらない。作業 1 で表の辺だけがベベルされ裏の辺が残った。範囲「ID の組」は全件に当たる（`MeshModifierScope.cs:209-220`。厚みの後に辺 1 本を ID の組で指定すると表と裏の両方がベベルされることを確かめた）。辞書の引き直し `PartsSelectionSet.ResolveByIds` は頂点 ID ごとに最初の頂点だけを持つ（`PartsSelectionSet.cs:552`）
3. ブーリアンの切り口の辺をベベルの範囲に入れられない（3.2）。Blender の制限方法「角度」に当たる「範囲を角度で決める」が無い
4. ベベルは範囲が要り、全体の範囲では失敗する（段 7a で決めた）。Blender は既定の「角度」で範囲の指定が要らない。作業 2 の差 17 回のうち、頂点 ID・範囲・辞書で 8 回
5. 範囲を ID で持つために、処理列を作る前に頂点 ID の付与が要る（2〜3 回）
6. 梯子（帯）の根元は作り直しのたびに形から決め直す（`EdgeRibbonFaceOps.cs:412-420`、`CompareStart` `:337-349`：上 → 手前 → 左）。芯の両端の高さの順が入れ替わる編集をすると、パイプの始まりと揺れ鎖の根元が反対の端へ移る。水平な芯（両端 y=0）の毛先を上へ 0.2 動かしたとき、パイプの最初の輪が x=0 の端から動かした端（x=0.9）へ移り（Undo で戻る）、鎖の根元のボーンも動かした端 (0.9,0.2,0) にあった。段 9 のシナリオは根元を上にした垂れた芯に直し、毛先の移動で根元が (0,1,0) のまま残ることを確かめた
7. `saveProjectBinary` は保存先のフォルダが無いと失敗する（`saveProjectCsv` は作る）
8. パーツ選択辞書の作成日時が、二進・CSV の往復で読み込んだ時刻に変わる（`queryProjectSyncDiff` の差 4 件：`models[].meshDTOList[].selectionSets[].createdAt`）
9. 作業 3 の生成コマンド（`createEdgePipe`・`placeSpringBoneLadderChains`）に画面の経路の宣言が無い

## 出典

- Blender：[Bevel Modifier](https://docs.blender.org/manual/en/latest/modeling/modifiers/generate/bevel.html)、[Simple Deform Modifier](https://docs.blender.org/manual/en/latest/modeling/modifiers/deform/simple_deform.html)、[Boolean Modifier](https://docs.blender.org/manual/en/latest/modeling/modifiers/generate/booleans.html)、[Curve Geometry](https://docs.blender.org/manual/en/latest/modeling/curves/properties/geometry.html)、[Armature Parenting](https://docs.blender.org/manual/en/latest/animation/armatures/skinning/parenting.html)、[Spline IK](https://docs.blender.org/manual/en/latest/animation/constraints/tracking/spline_ik.html)、[Undo & Redo](https://docs.blender.org/manual/en/latest/interface/undo_redo.html)、[Modifiers Introduction](https://docs.blender.org/manual/en/latest/modeling/modifiers/introduction.html)、[Wiggle 2](https://extensions.blender.org/add-ons/wiggle-2/)
- Maya：[polyModifier](https://help.autodesk.com/cloudhelp/2026/ENU/Maya-Tech-Docs/Nodes/polyModifier.html)、[polyBevel3 ノード](https://help.autodesk.com/cloudhelp/2026/ENU/Maya-Tech-Docs/Nodes/polyBevel3.html)、[polyCBoolOp](https://help.autodesk.com/cloudhelp/2026/ENU/Maya-Tech-Docs/Nodes/polyCBoolOp.html)、[polyPlane](https://help.autodesk.com/cloudhelp/2024/ENU/Maya-Tech-Docs/Commands/polyPlane.html)、[Construction History and Tweaks（SDK）](https://help.autodesk.com/cloudhelp/2017/ENU/Maya-SDK/files/Polygon_API_Construction_History_and_Tweaks.htm)、Maya 2026 Modeling ヘルプ（Boolean スタック・Sweep Mesh・Bend・Extrude）、Maya 2017 ヘルプ（nHair による動的 IK）

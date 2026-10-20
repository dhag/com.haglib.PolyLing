# PolyLing BVH 対応計画

BVH（関節の階層＋モーション）を読み書きする。正本はこの計画書。値の正本はコードの各所に書いた場所。

## 1. 目的

| 経路 | 中身 |
|---|---|
| BVH → .plmotion（→ .vrma） | BVH の関節を Humanoid に割り当て、正準骨格（VRM T ポーズ）のローカル回転と腰の正規化位置にし、マッスル＋body に焼く。.vrma は既存の `ConvertMotionToVrmaCommand` で書く |
| モーション＋モデル → BVH | VMD・.plmotion・.vrma・BVH を現在のモデルへ当て、モデルの骨格（IK ボーンを除く）の BVH を書く（例：MMD モデルの骨格の BVH） |

## 2. BVH の読み方（`BvhDocument` / `BvhCodec.Read`）

- 文法：`HIERARCHY` → `ROOT` 1 つ → `JOINT` / `End Site` の入れ子 → `MOTION` / `Frames:` / `Frame Time:` / 値。
  `ROOT` が 2 つ以上あるファイルはエラー。
- チャンネルは任意の並び（3 または 6）。回転は書かれた順に掛ける（`Zrotation Xrotation Yrotation` なら R = Rz·Rx·Ry）。
- 位置：根の位置チャンネルは OFFSET を置き換える（mocopi は根の OFFSET と 1 フレーム目の値が同じ）。
  根以外の位置チャンネルは読まない。OFFSET と食い違う関節があれば警告で数を出す（mocopi は全フレーム OFFSET と同じ）。
- レスト：回転チャンネルがすべて 0、根の位置が OFFSET の姿勢。
- 座標：BVH は右手系・Y 上・+Z 正面（キャラクターの左が +X）。glTF と同じく X 反転で Unity へ（位置 (-x, y, z)、回転 (x, -y, -z, w)）。
  mocopi・CMU の実ファイルで、左脚・左腕が +X にあることを確かめた。

## 3. 関節の割り当て

- 表は BVH 専用で、完全一致で引く（モデル用の `HumanoidBoneMapping.EmbeddedMapping` には足さない。
  `root` や `head` のような短い名前があいまい一致でモデル側の割り当てに影響するため）。
- 種類：`Auto`（各表の一致数が最大のもの）/ `Mocopi` / `Cmu`。割り当て CSV（1 行目は見出し、
  `Humanoid名,関節名[,別名…]`、`//` で始まる行は注釈）で表の行を上書き・追加できる。
- mocopi（SONY の受信コード `MocopiAvatar.cs` の `HUMAN_BONE_NAME_TO_MOCOPI_BONE_ID` から）

| Humanoid | 関節 | Humanoid | 関節 |
|---|---|---|---|
| Hips | root | LeftShoulder / LeftUpperArm / LeftLowerArm / LeftHand | l_shoulder / l_up_arm / l_low_arm / l_hand |
| Spine | torso_3 | LeftUpperLeg / LeftLowerLeg / LeftFoot / LeftToes | l_up_leg / l_low_leg / l_foot / l_toes |
| Chest | torso_5 | 右は l_ を r_ に | |
| UpperChest | torso_6 | | |
| Neck | neck_1 | | |
| Head | head | | |

- CMU（Bruce Hahne 版の BVH）

| Humanoid | 関節 | Humanoid | 関節 |
|---|---|---|---|
| Hips | Hips | LeftShoulder / LeftUpperArm / LeftLowerArm / LeftHand | LeftShoulder / LeftArm / LeftForeArm / LeftHand |
| Spine | LowerBack | LeftUpperLeg / LeftLowerLeg / LeftFoot / LeftToes | LeftUpLeg / LeftLeg / LeftFoot / LeftToeBase |
| Chest | Spine | 右は Left を Right に | |
| UpperChest | Spine1 | | |
| Neck | Neck1 | | |
| Head | Head | | |

  CMU の指（FingerBase・HandIndex1・Thumb）は計測していない関節なので割り当てない。
- 割り当てない関節（mocopi の torso_1・2・4・7、neck_2、CMU の LHipJoint・Neck など）の回転も、
  ワールド回転には効かせる（VRMA 読込と同じ）。

## 4. 正準骨格への変換（`BvhCodec.Read`）

- ワールドの差分 Δ = W·R⁻¹·A（W は各フレームのワールド回転、R はレストのワールド回転＝単位、A は T ポーズ整列）。
- T ポーズ整列 A：既存と同じ規則（正準の骨の向き → BVH のレストの骨の向きの最短弧）。
  算出は `UnityClipApplier.CanonAlignment`（レスト位置の表から求める静的関数）1 か所、
  範囲は `UnityClipApplier.IsAlignBone(名前, 脚も含めるか)` 1 か所。モデル経路と BVH 経路の両方がこれを使う。
  範囲：BVH 読込は常に腕 8 本＋脚 4 本（UpperLeg・LowerLeg）。モデル経路は既定で腕 8 本、
  オプション「脚も合わせる」（`AlignLegs`）で脚 4 本を加える。足首・つま先・体幹は含めない
  （2026-10-14 決定。CMU のレストは脚が約 20° 開いているため）。

### オプション「脚も合わせる」（モデル経路。2026-10-14）

| 使う所 | 渡し方 |
|---|---|
| 再生・ライブ受信 | 統合モーションテストのチェック「脚も T ポーズに合わせる」→ `motionClip` / `motionLive` の `setAlignLegs` |
| BVH 書き出し | `exportMotionToBvh` の `alignLegs`（パネルは同じチェック） |
| VMD の焼き込み | `exportVmdToMotion` の `alignLegs`（VMD→モーション・VRMA 書き出しのパネルのチェック。記憶は再生のチェックと同じキー） |

焼き込みと再生は同じ値にそろえる（範囲がずれると再生が焼き込みの逆にならない）。既定は切（従来どおり腕だけ）。
- 正準ローカル回転 = Δ_親⁻¹·Δ_自分（親は正準階層で割り当てのある一番近い祖先）。
- 腰の位置：床の高さ＝レストの全関節（End Site を含む）の最低の Y。腰の高さ H ＝ レストの Hips の Y − 床。
  基準＝レストを床（Y = 0）に置いたときの Hips 位置。正規化位置 ＝（Hips のワールド位置 − 基準）÷ H。
  モーションの床が Y = 0 であることを前提にする（mocopi・CMU とも満たす）。
- できた bakedBones を `CanonBoneMotion.BakeMuscles` でマッスル＋body に焼く（.plmotion の bakedBones 読込と同じ経路）。
- 時刻：全フレームをキーにする（t = フレーム番号 × Frame Time）。

## 5. モデルからの書き出し（`BvhCodec.ExportFromModel`）

- 当て方：`MotionClipApplier` でモデルへ当てる（パネル再生と同じ）。
- 骨格：`UnityClipVirtualSkeleton` のノード（半身モデルの仮想ミラーを含む）から IK ボーンを除いたもの。
  親は IK を飛ばした一番近い祖先。根が複数なら原点に `PolyLingRoot` を置いてまとめる。
- レスト：各関節のレストの回転は単位、OFFSET はレストのワールド位置の差（X 反転・倍率）。
- 根の動き：再生経路は Hips から見た姿勢しか当てない（body を当てない。`UnityClipApplier.cs` の恒久メモ）。
  マッスルを持つクリップでは、`CanonBoneMotion` で body から Hips の回転 Qc と正規化位置を求め、
  全関節に剛体変換 T ＝ 移動（Hips のレスト位置＋正規化位置×モデルの腰の高さ）·回転 Qc·移動（−Hips のレスト位置）を掛ける。
  モデルの腰の高さ ＝ Hips のレストのワールド Y（`VmdMotionBake` と同じ）。
- 各関節：G ＝ 回転（T·W）·回転（レストのワールド）⁻¹、ローカル回転 ＝ G_親⁻¹·G、
  ローカル位置 ＝ G_親⁻¹·（P − P_親）。
- チャンネル：根は位置＋回転、ほかは回転だけ。ただし全フレームのローカル位置が OFFSET から外れる関節（VMD のセンターなど）は位置も書く。
  回転の並びは Z→X→Y に固定。オイラー角は前のフレームに近い値へ 360° 単位でそろえる。
- 倍率 PositionScale（既定 1。モデルの単位 → BVH の単位）。VMD を当てるときの位置の倍率は PmxUnityRatio（パネル再生と同じ）。
- 付与親・IK は評価しない（`MotionClipApplier` の既知の制約）。

## 6. 入口

| 種類 | 内容 |
|---|---|
| 読込の共通口 | `MotionFileLoad.Load(path, MotionSourceKind, BvhReadOptions)`。`ExportMotionJsonCommand`・`ExportMotionToBvhCommand`・`MotionClipHandler` が同じ口を使う |
| コマンド | `MotionSourceKind.Bvh` を追加。`ExportMotionJsonCommand` に `BvhSkeleton`・`BvhMappingCsvPath` を追加。新規 `ExportMotionToBvhCommand`（モデル必須） |
| パネル | 統合モーションテストのソース種別に「BVH」。BVH 書き出しの区画（書き出し先・倍率・書き出しボタン） |

## 7. 検証

1. mocopi・CMU の BVH → .plmotion → モデルで再生し、キャプチャで確かめる。
2. モデル → BVH → 割り当て CSV で読み戻し、元のクリップと正準ローカル回転・腰の位置を比べる。
3. `queryCommandAudit` / `queryUiAutomationAudit` が全項目 0。

### 結果（2026-10-14）

- mocopi（jump.bvh）・CMU（01_01.bvh）とも読込エラー 0。マッスル 49 本・bakedBones 22 本。
- 往復（CMU → .plmotion → __AちゃんH で BVH → 割り当て CSV で読み戻し）：正準ローカル回転の差は最大 0.06°、腰の正規化位置の差は最大 1.1e-5。
- 元の BVH との骨の向きの差（Python で BVH を独立に順運動学で解いて比較）
  - レスト（mocopi の先頭の T ポーズ）：腕 0～1°。脚はレストの骨の向きの差そのもの（mocopi のすね 9°）。
  - CMU のレストは脚が外へ約 20° 開いている。整列が腕だけのときは脚が常に約 20° ずれた。
  - 動いている区間では最大 27°。脚で測ると、この式の正準の回転とマッスル経由の回転の差が最大 24°（マッスルへの焼き込みで生じる）。

### 結果（脚の整列を足したあと、2026-10-14）

- 骨の向きの差の最大：CMU の太もも 2～3°・すね 6～8°（以前 20～27°）、mocopi の太もも 8°・すね 13～15°（以前 10～21°）。腕は変わらない。
- 往復（CMU → __AちゃんH で BVH → 読み戻し）：体幹・腕は 0.06° のまま。脚だけ一定のずれが出る（太もも 3°・すね 8°・足首 6°）。
  モデル経路は脚を整列しないので、モデルのレストの脚の向き（PMX は脚が少し開いている）が、読み戻しの整列にそのまま乗るため。
- オプション「脚も合わせる」を入れて書き出すと、往復の差は全ボーン最大 0.06°（脚のずれが消える）。切のときは上の値のまま。

## 8. 残件

- 表示の引き直し：モーションの姿勢を当てたときの表示更新用に、正規入口（Enter*）を 1 つ設ける案。
- 未対応：MCP だけで書き出したときは、表示の引き直しが正規入口（EnterVerticesMoved）だけになる（UpdateTransform は規約で新規に呼ばない）。

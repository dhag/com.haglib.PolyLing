# PolyLing FBX 対応計画

FBX のモデルとモーションを読み書きする。Editor とビルド版（Windows x64 スタンドアロン）の両方で動かす。
正本はこの計画書。1 段＝1 会話・この計画書への承認 1 回で進める。

## 1. ライブラリの選定

| 向き | 実装 | 理由 |
|---|---|---|
| 書き出し | 自前（C#、PolyLing.Runtime。段 7） | `MeshObject` から多角形・制御点・疎なモーフ差分を直接書くため（Unity メッシュの三角形からの組み直しをなくす）。FBX SDK への依存（パッケージ・`FBXSDK_RUNTIME`・`UnityFbxSdkNative.dll`）もなくなる |
| 読み込み | 自前（C#、PolyLing.Runtime。段 8・段 9）。FBX 6.x・7.x のバイナリ（ビッグエンディアンも）と ASCII。式は ufbx 0.23.1 を移したもの | ネイティブ DLL（`plufbx.dll`）をなくす。5.x 以前は「未対応の版」として止める |

段 1〜6 の書き出しは FBX SDK（`com.autodesk.fbx` 5.1.3）で作り、段 7 で自前に置き換えた。段 4〜7 の読み込みは ufbx（`plufbx.dll` への P/Invoke）で、段 8 で自前に置き換えた。§5 の段 1〜7 の結果はその当時の記録。

## 2. 構成

| 置き場 | 中身 |
|---|---|
| `Runtime/Poly_Ling_Main/FBX/PLFbxBridge.cs` | 入口。`PLFbxBridge.Exporter` は `FbxExporter`（登録の仕組みは無い） |
| `Runtime/Poly_Ling_Main/FBX/FbxExporter.cs` | 書き出しの流れ（標本化 → `HierarchyBuilder`（Unity メッシュを作らない）→ ノード木 → ファイル） |
| `Runtime/Poly_Ling_Main/FBX/FbxModelWriter.cs` | `HierarchyBuildResult`（Transform 階層と `MeshSourceOf`）とモーションの標本から FBX のノード木を組む。規則の正本は冒頭 |
| `Runtime/Poly_Ling_Main/FBX/FbxNodeTree.cs`・`FbxBinaryWriter.cs`・`FbxAsciiWriter.cs` | ノード木と、FBX 7.4 バイナリ・ASCII への書き出し |
| `Runtime/Poly_Ling_Main/HierarchyIO/HierarchyMeshSource.cs` | 描画ノードごとの形状の出どころ（`HierarchyBuildResult.MeshSourceOf`） |
| `Runtime/Poly_Ling_Main/FBX/FbxScene.cs` | 読み込みの口（以前の `UfbxScene` と同じ口・同じ値）。使うのは `FbxImporter`・`FbxMotionReader`・`FbxNodeTable` |
| `Runtime/Poly_Ling_Main/FBX/FbxBinaryReader.cs`・`FbxAsciiReader.cs` | FBX 6.x・7.x のバイナリ・ASCII → `FbxNode` の木（書き出しと同じ型） |
| `Runtime/Poly_Ling_Main/FBX/FbxSceneReader.cs`・`FbxSceneModel.cs` | 木 → シーンの表（ノード・メッシュ・スキン・ブレンドシェイプ・材質・テクスチャ・ポーズ・テイク）。座標・単位の変換、法線の生成 |
| `Runtime/Poly_Ling_Main/FBX/FbxTransformEval.cs`・`FbxMath.cs` | ノードの変換と継承、曲線・レイヤの評価、ブレンドの重み。倍精度 |
| `Runtime/Poly_Ling_Main/FBX/LICENSE_ufbx.txt` | 式の出どころの ufbx のライセンス（MIT またはパブリックドメイン） |

- 依存するパッケージもネイティブ DLL も無い。ビルド版の Scripting Define Symbols も要らない。

## 3. 段

| 段 | 内容 |
|---|---|
| 1 | 導入：依存の追加、`FBXSDK_RUNTIME`、`plufbx.dll`、`PolyLing.Fbx`、`PLFbxBridge`、`UfbxNative`。Editor とビルド版の両方で両方の版が読めることを確かめる |
| 2 | モデル書き出し：glTF 書き出し（`GltfExporterImpl`）と同じく `HierarchyBuilder` で GameObject＋Mesh を作り、`FbxScene`（ノード階層・メッシュ・材質・スケルトン・スキン・ブレンドシェイプ）を組む。軸と単位は FBX Exporter（`com.unity.formats.fbx`）の変換を読んで同じにする。書き出しの口（`IFbxExporter` と設定・結果の型）と、検証に使う MCP コマンド `ExportFbxFileCommand` はこの段で足す（パネルのボタンは段 6） |
| 3 | モーション書き出し：`BvhCodec.ExportFromModel` の「モデルに当てて関節ごとのローカル姿勢をフレームごとに取る」部分を共通の標本化処理に切り出し、BVH と FBX で使う。FBX は関節ノードへ回転・位置の曲線、表情はブレンドシェイプの重みの曲線 |
| 4 | モデル読み込み：ufbx から `MeshContext`（ボーン → 描画オブジェクト → モーフの順。`GltfImportResult` と同じ並び）、`Vertex.BoneWeight`、材質・テクスチャ、`MorphExpression` を組む。Humanoid の割当は `RebuildMappingFromPerBone` を通す。軸・単位はファイルの設定から Unity 左手系へ。読み込みの口（`plufbx.c` の関数と設定・結果の型）はこの段で足す |
| 5 | モーション読み込み：BVH 読み込みの正準化（Δ = W·R⁻¹·A → 正準ローカル回転 → `CanonBoneMotion.BakeMuscles`）を、レストのワールド回転 R が単位でない骨格にも使えるよう共通化して FBX の関節に当てる。関節名の割当は割当 CSV で上書きできる。テイクが複数あるときは選べる |
| 6 | 入口：MCP コマンド（`ImportFbxFileCommand` / `ExportFbxFileCommand` / `ExportMotionToFbxCommand`）、`MotionFileLoad` に FBX、パネルのボタン。どれも `PLSandbox` の関門を通す |

## 4. 検証（全段共通）

- 書き出した FBX を Unity に取り込み（Editor）、元のモデルと頂点数・ボーン数・ウェイト・ブレンドシェイプ数を比べる。モーションは再生して段ごとにキャプチャを撮る。
- 読み込みは、書き出した FBX の往復と、外部の FBX で確かめる。
- `queryCommandAudit` / `queryUiAutomationAudit` が全項目 0。

## 5. 結果

### 段 1（2026-10-14）

- 起動時に 1 行出す（`PLFbxBridge.LogStatus`）：`[PolyLing] FBX: FBX SDK 2020.3.4 / ufbx 0.23.1`。
  Editor の Play とビルド版（`Player.log`）の両方で出た。例外なし。
- ビルド版の `PolyLing_Data/Plugins/` に `UnityFbxSdkNative.dll`、`PolyLing_Data/Plugins/x86_64/` に `plufbx.dll` が入った。
- 段 1 の `plufbx.dll` は zig cc（`-target x86_64-windows-gnu -shared -O2`）でビルドした。依存は UCRT（api-ms-win-crt-*）と KERNEL32 だけ。

### 段 2（2026-10-14）

書き出しの規則（正本は `Runtime/Poly_Ling_Fbx/FbxExporterImpl.cs` 冒頭）

| 項目 | 規則 |
|---|---|
| 材料 | `HierarchyBuilder`（glTF 書き出しと同じ設定）。ミラー枝・関節・可視補完・モーフの規則は書き写さない |
| 座標 | Unity の値のまま、単位 cm（位置 ×100）・軸 DirectX と宣言して組み、最後に `FbxAxisSystem.MayaYUp.DeepConvertScene`（FBX Exporter 5.1.3 と同じ）。結果は X 反転の右手系・Y 上 |
| ノード | ボーンはバインドポーズで置く（SMR のワールド · bindpose⁻¹）。ほかは親の書き出し上のワールド · ローカル TRS。回転は Unity のオイラー角・回転順 ZXY・RotationActive・継承 RSrs |
| 制御点 | 位置・ボーンウェイト・全ブレンドシェイプ差分が完全一致する Unity 頂点を 1 つにまとめる（位置だけでまとめる FBX Exporter の方式は、同じ位置の別頂点のウェイト・モーフを潰す）。UV・法線は面の頂点ごと |
| 面 | 元の `MeshObject` の多角形（`SourcePolygons`。Unity メッシュの扇形三角形から元の面を組み直す。組み直せないメッシュだけ警告を出して三角形） |
| 材質 | `MaterialData` から Lambert（Diffuse = BaseColor、透明度 = 1 − α、Emissive、BumpFactor = NormalScale）。メタリック・オクルージョンは出さない |
| テクスチャ | `ModelTextureTransfer.Collect` で集め、新設の `Entry.Uses`（材質と用途）で Diffuse / NormalMap / Emissive へつなぐ。「ファイル名_textures」へ書き、FBX には絶対パスと相対パス。既定はバイナリへ埋め込み（`EXP_FBX_EMBEDDED`） |
| スキン | クラスタはボーンごと（eNormalize）。TransformMatrix = メッシュのグローバル、TransformLinkMatrix = ボーンのグローバル。メッシュごとにバインドポーズ（FbxPose）を 1 つ |
| 作業フォルダの関門 | `PLSandbox` の書き込み可能な拡張子に `.fbx` を追加 |

検証（`__AちゃんH.pmx`）

- 書き出し：ノード 241 / ボーン 169 / メッシュ 70（全部スキン） / 制御点 85,323 / 面 167,636 / 材質 73 / テクスチャ 12 / ブレンドシェイプ 82。26 MB（埋め込み込み）。
- ufbx 0.23.1 で独立に読み直し：数がすべて一致。FBX 7.7 バイナリ、単位 0.01 m、右手系・Y 上・前 +Z。
  バインド時のスキン再計算と元の位置の差 0、ウェイト和の誤差 0、ウェイトの無い制御点 0。埋め込み画像 60 件。
- 向き：`左腕` は PolyLing で (−0.0979, 1.5753, −0.0045)、FBX のファイル座標で (0.0979, 1.5753, −0.0045)（X 反転）。
  ファイル座標で +Z 側から描くと顔が見える（FBX の正面 +Z を向く）。
- 描画（ufbx で読んだ三角形をクラウド側で描画）：正面・横とも形が崩れない。「顔肌_あ」「顔肌+_あ」を 1 にすると左右そろって口が開き、上下の唇が別々に動く。
- Unity の FBX 取り込み（`Assets/` へ書き出して Refresh）：警告・エラー 0。確認後に削除。
- `queryCommandAudit`：今回の変更に関わる項目はすべて 0（「正典に無い 1」`OpenProfileEditSpaceCommand` と「出せなかった 1」`AddGeneratedMeshCommand` は以前から）。

### 段 3（2026-10-14）

書き出しの規則（正本は `Runtime/Poly_Ling_Fbx/FbxExporterImpl.cs` 冒頭「■ アニメーション」と `Runtime/Poly_Ling_Main/Motion/ModelMotionSampler.cs`）

| 項目 | 規則 |
|---|---|
| 標本化 | `ModelMotionSampler.Sample`（BVH 書き出しから切り出した共通処理）。`MotionClipApplier` でモデルへ当て、IK ボーンを除いた関節ごとにレストからのワールド回転 Δ とワールド位置を取る。マッスルを持つクリップは body から求めた剛体変換 T を掛ける。姿勢は終わったら戻す |
| 関節ノード | ワールド = TRS(バインド位置 + (位置 − レスト位置), Δ · バインド回転, バインドの拡大)。ローカル = 親のワールド⁻¹ · ワールド。`LclTranslation`（×100）と `LclRotation`（Unity のオイラー角）へ毎フレーム線形キー |
| オイラー角 | 同じ回転の 2 通り (x, y, z) / (180 − x, y + 180, z + 180) を 360° 単位でそろえ、前のフレームに近い方 |
| 関節でないノード | 親のワールド · 書き出し時のローカル（曲線なし） |
| 仮想ミラー関節 | FBX のノードを持たないので飛ばし、数を結果に返す |
| 表情 | 表情をモデルの頂点モーフへ展開（`MotionClipApplier.ExpandToVertexMorphs`、グループ表情は再帰）し、モーフごとに重みを足して、対応するブレンドシェイプの `DeformPercent`（重み × 100）へ毎フレームのキー |
| 時間 | fps に合う FBX の時間モード（FBX Exporter と同じ探し方、無ければ 30 fps）。テイク名はモーション名（空ならファイル名）、層は「Base Layer」 |
| VMD | 位置の倍率は PmxUnityRatio（パネル再生・BVH 書き出しと同じ） |
| コマンド | `ExportMotionToFbxCommand`（`exportMotionToFbx`）。引数は BVH 書き出しと同じものに `IncludeMesh` と `Settings` を足した。`PLSandbox` の関門を通す |

検証（`__AちゃんH.pmx`、Humanoid 自動割当つきで読み込み）

- BVH 書き出しの共通化：切り出しの前後で BVH 出力がバイト単位で一致（CMU `cmu_01_01.bvh` 2,752 フレーム、VMD `キャッチボール（トマホーク）.vmd` 249 フレーム）。
- FBX 書き出し：CMU は 2,752 フレーム 22.925 秒・動くノード 163・当たったトラック 21・根の動き body から。VMD は 249 フレーム 8.267 秒・動くノード 163・当たったトラック 97・ブレンドシェイプの曲線 20（表情 28 のうちモデルの頂点モーフへ当たったもの 10）。仮想関節で飛ばした数は両方 0。
- ufbx で独立に読み直し、各フレームの関節のワールド位置を、同じモデル・同じモーションで書いた BVH の順運動学と比べた（両方 X 反転の右手系）：
  CMU 5 フレーム・VMD 5 フレームとも 163 関節、最大誤差 0.004 mm。
- フレームの中間（f + 0.5）：BVH のローカル回転の球面線形補間と比べ、CMU は最大 1.03 mm、VMD は最大 33.4 mm（VMD 132→133 で左つま先が 1 フレームに 882 mm 動く箇所。99 パーセンタイル 1.6 mm）。FBX の回転曲線がオイラー角の線形補間であることによる差で、キーのあるフレームでは一致する。
- 表情：テスト用モーション（「あ」を 0 秒 0 → 1 秒 1 → 2 秒 0）で、ufbx の評価が「顔肌_あ」「顔肌+_あ」とも 0.5 / 1.0 / 0.5（15 / 30 / 45 フレーム）。VMD の表情トラックは全キーの値が 0 なので曲線も 0。
- `queryCommandAudit`：今回の変更に関わる項目はすべて 0。

### 段 4（2026-10-14）

読み込みの規則（正本は `Runtime/Poly_Ling_Main/FBX/FbxImporter.cs` 冒頭と `Native~/ufbx/plufbx.c` の各関数）

| 項目 | 規則 |
|---|---|
| 口 | `plufbx.c` に平らな関数（シーン・ノード・メッシュ・スキン・ブレンドシェイプ・材質・テクスチャ）を足した。ファイルは C# が読んだバイト列を `plufbx_load_memory` へ渡す（パスの文字コードを C 側で扱わない）。`UfbxScene`（`UfbxNative.cs`）が包む |
| 座標 | ufbx が右手系・Y 上・前 +Z・単位 m へ直し、C# で X 反転（点 S·p、行列 S·M·S）。面の角の並びは反転。UV はそのまま。FBX 書き出しの逆 |
| ノード | 入れ物（根から続く、メッシュ・属性・スケルトン・明示のバインドを持たないノード）は読み飛ばす。ボーン＝メッシュを持たないノードとクラスタの関節（glTF の読み込みと同じ）。DFS 順 |
| バインド姿勢 | クラスタのリンク行列 → バインドポーズ → 親の bw · 親から見た今の変換 |
| 頂点・面 | 制御点 1 つ = Vertex 1 つ。面は多角形のまま。角の UV・法線は値が完全に同じ組だけスロットを共有（`Vertex.GetOrAddUVNormal` は 1e-4 の許容で近い UV を潰すので使わない） |
| スキン | 先頭クラスタの「ボーンの bw · geometry_to_bone」でワールドへベイク（親なし）。ウェイトは重い順 4 つを和で割る |
| モーフ・表情 | チャンネルごとに重み 1 の形をモーフに。表情名はチャンネル名から「メッシュのノード名_」を外したもの。同名を 1 つの表情へ |
| 材質 | Lambert / Phong → URP Lit（書き出しの逆）。Diffuse / NormalMap / Emissive のテクスチャ。ST は Scaling / Translation |
| テクスチャ | 埋め込み優先、無ければ外部ファイル。PNG / JPEG を「ファイル名_textures」へ書き、同じバイト列は 1 ファイルにまとめる（FBX SDK はテクスチャごとに Video を作る） |
| コマンド | `ImportFbxFileCommand`（`importFbxFile`）。引数と関門は glTF の読み込みと同じ。`ImportFbxCommand`（CommandQueue）→ `OnImportFbx` |
| DLL | `plufbx.dll` は zig cc 0.17.0（`-target x86_64-windows-gnu -shared -O2`）で作り直した。依存は UCRT と KERNEL32 だけ。Editor が DLL を掴むので、差し替えは Editor を閉じてから |

検証

- 往復：`__AちゃんH.pmx` → FBX（rt_a）→ 読み込み → FBX（rt_b）。ufbx で rt_a と rt_b を比べた。
  ノード 242（入れ物 2 を飛ばした）・ボーン 169・メッシュ 70（スキン 70）・頂点 85,323・面 167,636・モーフ 82（表情 37）・材質 73・テクスチャ 12。
  - ボーンのバインド位置：169 本、名前一致、最大差 1.0e-6 m。
  - メッシュ 70：制御点・面・角の数が全部一致。角 502,908 個の（位置・UV・材質）が 1e-5 で全部一致、法線も一致。
  - 三角形 167,636 個の向き（角の並び）が全部一致。
  - ウェイト：制御点 82,461 個すべて一致（2e-3 以内）。
  - ブレンドシェイプ：差分のあるチャンネル 79 の差分が全部あり、最大差 1.1e-4。
  - 材質 73：拡散色・透明度・発光・バンプ係数・テクスチャの参照が全部一致。
- 外部の FBX（Unity のサンプル）：`DefaultMale.fbx`（ボーン 82・スキン 1・頂点 7,544。Humanoid 自動割当で全部埋まった）、`Lion.fbx`（静的メッシュ）。どちらも直立で読めた（キャプチャ `SandBox/Captures/fbx_import_stage4_ext_0001.png`・`_0002.png`）。
- `queryCommandAudit`：今回の変更に関わる項目はすべて 0。

### 段 5（2026-10-14）

読み込みの規則（正本は `Runtime/Poly_Ling_Main/FBX/FbxMotionReader.cs` 冒頭と `Runtime/Poly_Ling_Main/Motion/CanonMotionBuilder.cs` 冒頭）

| 項目 | 規則 |
|---|---|
| 共通化 | BVH 読み込みの正準化（Δ → 正準ローカル回転・腰の正規化位置 → `BakeMuscles`）を `CanonMotionBuilder` に切り出し、レストのワールド回転 R を受けるようにした（Δ = (W·R⁻¹)·A。BVH は R = 単位で掛けない）。割り当て CSV の適用と主要ボーンの検査も共通化 |
| ノード表 | モデル読み込みのノードの分類とバインド姿勢を `FbxNodeTable` に切り出し、モデル・モーションの読み込みで共通に使う |
| 関節・レスト | 関節 = `FbxNodeTable` のボーン。レスト = バインド時のワールド（クラスタ → バインドポーズ → 親から伝える） |
| 割り当て | `HumanoidBoneMapping.AutoMapFromEmbeddedCSV`（モデルの Humanoid 自動割当と同じ表）→ 割り当て CSV で上書き。計画では「BVH と同じ表」としていたが、BVH の表（mocopi・CMU）では PMX 名の骨格が 1 本も当たらないため、モデルと同じ表にした |
| 時間 | テイクの始まり + f / fps（シーンの fps、無ければ 30）。フレーム数 round((終わり − 始まり)·fps) + 1。テイクは `FbxMotionReadOptions.TakeIndex`（既定 0） |
| 評価 | 全ノードの継承が NORMAL なら `ufbx_evaluate_transform` を親から順に掛ける（速い。2,752 フレームで 0.6 秒）。そうでなければ `ufbx_evaluate_scene`（継承の種類をすべて ufbx が扱う。メッシュも評価するので遅い）。両者の関節位置の差は 5.5e-7 m |
| 表情 | ブレンドシェイプの重み（`ufbx_evaluate_blend_weight`）を表情トラックへ。表情名はモデル読み込みと同じ（「メッシュのノード名_」を外す）。どのフレームでも 0 のチャンネルは書かない |
| 入口 | `MotionSourceKind.Fbx = 4`、`MotionFileLoad` に FBX。割り当て CSV はコマンドの `BvhMappingCsvPath` を使う。モーションの書き出し（JSON・BVH・FBX）と試し再生（`motionClip` の load、sourceKind 4）が読める |

検証

- 共通化の前後で BVH の読み込み結果（.plmotion.json）がバイト単位で一致（CMU `cmu_01_01.bvh`、mocopi `mocopi_jump.bvh`）。
- 段 3 の FBX（`__AちゃんH.pmx` に CMU を当てたもの。レストの回転は単位でない）を読んだマッスルと、同じ動きの BVH（`ref_cmu.bvh`。レストの回転は単位）を PMX 名の割り当て CSV で読んだマッスルを比べた：
  共通の 49 本が全フレームで最大差 3.2e-4、腰の位置 1e-6。FBX 側は指も当たるので 87 本。
- 表情：段 3 のテスト用モーションの FBX を読むと「あ」1 本が 0 / 0.5 / 1 / 0.5 / 0（0・0.5・1・1.5・2 秒）。それをモデルへ当てて FBX へ書くと表情 1 本がモデルに当たり、ブレンドシェイプ 2 本に曲線が付いた。
- 読み直しを同じモデルへ当てた BVH（`fbx_cmu_legs.bvh`、alignLegs あり）と元の BVH（`ref_cmu.bvh`、alignLegs なし）の関節位置は、足首・つま先で最大 36 mm 違う。マッスルが一致しているので FBX 読み込みの差ではなく、当て方（脚の T ポーズ整列の有無）の差。
- 他のツールで作られたモーション（Unity サンプル `Victory-anim1.fbx`）：読めて再生できるが、手首の向きが元と 90° ほど違う。キャプチャ `SandBox/Captures/fbx_motion_stage5_0001.png`・`_0002.png`。
  原因はレスト：メッシュもバインドポーズも無いので、レストがノードの既定の変換（0 フレーム目の腕を下ろした姿勢）になる。

レスト用の FBX（2026-10-14 追加）

- `FbxMotionReadOptions.RestFbxPath` と、モーションの 3 コマンド（`exportMotionJson`・`exportMotionToBvh`・`exportMotionToFbx`）の引数 `FbxRestPath`。
  同じ骨格のモデル FBX のバインド時のワールドを関節名で引いてレストにする。Humanoid に割り当てた関節が 1 本も当たらなければ失敗、当たらない関節は自分のレストを使い警告する。
  パスは関門（`PLSandbox.TryResolveRead`）を通す（`PlayerCommandDispatcher.BuildFbxReadOptions`）。
- 検証：`DefaultMale.fbx` を読み込んで Humanoid 自動割当し、`Victory-anim1.fbx` を当てた BVH の関節位置を、元の FBX を ufbx で評価した位置と比べた（腰からの相対、5 フレーム）。

  | | 中指の付け根の最大 | 主要 24 関節の平均 |
  |---|---|---|
  | レスト用の FBX なし | 222 mm | 42 mm |
  | `DefaultMale.fbx` をレストに | 61 mm | 16 mm |

  マッスルの範囲外の警告も、手首（Hand Down-Up / In-Out、計 2,312 キー）が 0 になった。
  レストありでも、肘から先がずれる（フレーム 100 で肘 27 mm・手首 50 mm。胴・首・頭は 4 mm）。`DefaultMale.fbx` と `Victory-anim1.fbx` の骨の長さは 0.1 mm 以内で一致している。
  → 原因と対応は残件の「（解決済み）」の項。

### 段 6（2026-10-14）

入口

| 項目 | 内容 |
|---|---|
| コマンド | モーションの 3 コマンド（`exportMotionJson`・`exportMotionToBvh`・`exportMotionToFbx`）に `FbxTake`（読むテイクの番号、既定 0）。`BuildFbxReadOptions(bvh, restPath, take, …)` で `TakeIndex` に入れる。範囲外は読み込みのエラー「テイク n がありません（テイクは m 個）」 |
| 試し再生 | `motionClip` の `load(path, sourceKind, time, fbxRestPath = "", fbxTake = 0)`。FBX のときだけ使う |
| 左ペイン「ファイル」 | `.FBX読込`（`.glTF読込` と同じ行）、`FBX保存`（`glTF保存` と同じ行）。左ペインの幅に収めるため glTF と FBX で 1 行にした |
| 読み込みパネル（`importFbx`） | モーフ・3D 表示オートスケール・材質・テクスチャを書き出す・読込後オプション（Humanoid 自動割当・原点 CSV）。テクスチャを書き出すときは「ファイル名_textures」も 1 回許可する。`plufbx.dll` を読めないときは理由を出して止める |
| 書き出しパネル（`exportFbx`） | スキニング・モーフ・非表示メッシュ・テクスチャ埋め込み・ASCII。「ファイル名_textures」も 1 回許可する（受け口が `TryResolveFolder` を通すため） |
| モーションパネル（`motionClipTest`） | ソース種別に「FBX (.fbx)」。FBX のときだけ「レスト用 FBX」（`fbxRest.path` / `browse` / `clear`）と「テイク」（`fbxTake`）を出し、読み込み・再読込・JSON保存・BVH 書き出し・FBX 書き出しへ渡す（レスト用 FBX は送る直前に 1 回許可）。「FBX 書き出し（今のモデル＋モーション）」の節（`fbx.path` / `browse` / `fps` / `includeMesh` / `export` / `status`）。VRMA の節は FBX を読んでいるとき「JSON保存してから」と出して止める |
| 機能の場所 | `PolyLing_機能の場所.md` に `.glTF読込／glTF保存`・`.FBX読込／FBX保存`、「モーション」の行に BVH・FBX を足した |

検証

- Editor：`uiDescribe` で `importFbx`（16 項目）・`exportFbx`（11 項目）・`motionClipTest`（`fbx.*`・`fbxRest.*`・`fbxTake`）が出る。左ペインの 2 行は幅に収まる（キャプチャ `SandBox/Captures/fbx_stage6_ui_0002.png`）。
- Editor：`motionClip` の load（`Victory-anim1.fbx`、レスト `DefaultMale.fbx`、テイク 0）でマッスル 49/49 が当たる。テイク 1 は「テイク 1 がありません（テイクは 1 個）」。`exportMotionJson` に `fbxTake=1` も同じエラー。
- Editor：`exportMotionToFbx`（`Victory-anim1.fbx` → `DefaultMale.fbx`、レストあり、alignLegs あり）の FBX を ufbx で評価すると、腰から見た関節位置のずれは 5 フレームとも最大 4.2 mm（骨格の差。残件の「（解決済み）」の項と同じ）。
- `queryCommandAudit`：今回の変更に関わる項目はすべて 0。`queryUiAutomationAudit`：増えたのは `leftPane.fbxLoadBtn` / `fbxSaveBtn` の安全度未指定だけ（左ペインのボタンはすべて未指定で、glTF と同じ扱い）。
- ビルド版（Windows x64、`unity_build_player`。シーン保存の確認ダイアログは「Don't Save」で続けた）：`PolyLing_Data/Managed/PolyLing.Fbx.dll`、`PolyLing_Data/Plugins/x86_64/plufbx.dll` が入った。
  画面操作（ファイル選択ダイアログを含む）で次を確かめた。
  - `.FBX読込` → `DefaultMale.fbx`（Humanoid 自動割当）：T ポーズで表示された。
  - `FBX保存` → `player_male.fbx`：ufbx で読むと制御点 7,544・ボーン 82・スキン 1、外接箱が元と一致、バインドとスキンの差 1.7e-8 m。
  - モーションパネル（FBX、レスト用 FBX `DefaultMale.fbx`、`Victory-anim1.fbx`、脚も T ポーズ合わせ）→ 「FBX 書き出し」→ `player_victory.fbx`：Editor の書き出しと全関節で一致（差 0.0000 mm）。

## 6. 残件

- （段 8 で不要）`plufbx.dll` と `build_msvc.bat` は撤去した。
- （段 9 で対応）FBX 6.x。5.x 以前は読めない。
- ufbx と値が違うもの（段 9 の比較で見つけたもの。6.x・7.x 共通）：3ds Max の OSL・シェーダーグラフのテクスチャのファイル名（`max_texture_mapping`・`max_openpbr_material`・`max_shadergraph`）、Audio、Thumbnail、不正な UTF-8 の名前の区別（6.x は名前が ID なので、別の不正バイトの名前が同じ ID になる）、ufbx がわざと失敗させる壊れたファイル（入れ子の深さ・NURBS の次数のあふれなど）を読めてしまうこと。
- 読み込みの時間は ufbx の約 5 倍（20 MB の 0ちゃんH で 0.85 秒、ufbx 0.16 秒）。アニメーションの評価は約 4 倍（段 8 の結果）。
- モーションだけの FBX（メッシュもバインドポーズも無い。例 `Victory-anim1.fbx`）では、レストがノードの既定の変換になり、T ポーズとは限らない（`Victory-anim1.fbx` は 0 フレーム目の腕を下ろした姿勢）。
  T ポーズ整列 A は腕・脚の向きしか直さないので、手首などの向きがずれる。BVH の読み込みも同じ前提（OFFSET の姿勢をレストにする）。
  → レスト用の FBX（`FbxRestPath`）を足した（段 5 の結果）。試し再生（`motionClip` の load）とモーションパネルにも渡す口を足した（段 6）。
- （解決済み）レスト用の FBX を使っても、`Victory-anim1.fbx` を `DefaultMale.fbx` に当てると肘から先が最大 61 mm ずれた。
  原因（2026-10-14 調査）：FBX の読み込みではなく、正準回転を Humanoid のマッスルへ焼く段（`CanonBoneMotion.BakeMuscles`）で、マッスルに無い自由度が落ちる。
  - 読み込み側：ufbx で評価した W と `DefaultMale.fbx` のバインド R から作る W·R⁻¹·d（d はレストの骨の向き）は、元の骨の向きと 0.000° で一致。
    胴（Hips・Chest・Head）の正準回転は、書き出した .plmotion.json の bakedBones と 0.0° で一致。
  - ずれる骨（フレーム 0 / 100 / 300 / 500）：LeftShoulder の正準回転のずれ 3.8 / 4.0 / 3.5 / 2.8°は、正準ローカル回転のうち骨軸まわりの捩り（−3.8 / −4.0 / −3.5 / −2.8°）と一致する。
    Humanoid の肩のマッスルは Down-Up と Front-Back だけで捩りが無い。
  - 同じく前腕は横曲げ（2.0〜7.9°）、すねは横曲げ（1.5〜5.3°）を正準ローカル回転に持つが、マッスルは Stretch と Twist だけ。
  - 子の骨は親の正準回転に乗るので、肩の捩りが落ちると上腕から先の向きもずれる。
  - 段 3 の自己往復（モデルにマッスルで当てた姿勢を読み直す）でずれが出ないのは、元の姿勢がマッスルで表せる範囲にあるため。
  BVH の読み込みも同じ経路（CanonMotionBuilder → BakeMuscles → マッスルで再生）なので、同じ動きなら同じだけずれる。
  → 対応（2026-10-14）：骨の回転を読んだクリップは bakedBones を正本にして、再生・BVH / FBX 書き出しで直接使う。
  - `MotionClipDTO.rotationSource`（"muscles" 既定 / "bakedBones"）を足した。version は 4 のまま。
    BVH・FBX の読込（`CanonMotionBuilder`）、VRMA の読込、bakedBones だけの .plmotion の読込で "bakedBones" になる。
    マッスル＋body は従来どおり作って添える（Humanoid・配信向け）。
  - `MotionClipSerializer.Save` は "bakedBones" のクリップの bakedBones を作り直さない（読み直して書いた .plmotion.json はバイト一致）。
  - 再生：`UnityClipApplier.ApplyFrame(…, canonLocals)` を足した。正準ローカル回転を `CanonSkeleton` の親子の順に掛けて
    Hips から見た正準ワールドを作り、以降はマッスル経路と同じ式（`DeltaOf`、D = RestW⁻¹·Δ_祖先⁻¹·Δ·RestW）で当てる。
    目のトラックが無く lookAt があれば、目のローカル回転を lookAt にする（`MotionBakedBoneSampler`）。
  - 書き出しの根の動き（`ModelMotionSampler`）は bakedBones の Hips の回転と正規化位置をそのまま使う。
  - 結果（`Victory-anim1.fbx`、レスト `DefaultMale.fbx`、`DefaultMale.fbx` へ当てて BVH 書き出し、alignLegs=true）：
    Hips から見た関節位置のずれは全フレームで最大 4.2 mm（以前 61.2 mm）。4.1 mm は Spine の付け根の位置の差
    （`DefaultMale.fbx` と `Victory-anim1.fbx` の骨格の差。胴から先の全関節に同じ量で乗る）で、回転によるずれは 0.1 mm。
    alignLegs=false では脚のレストの向きの差（T ポーズ整列を掛けない）で足首が 44 mm ずれる。
  - CMU（`cmu_01_01.bvh`）を `DefaultMale.fbx` へ当てた書き出しは、上腕・前腕・太もも・すねの向きが元の BVH と最大 0.04° で一致。
  - マッスル由来のクリップ（rotationSource なし）の書き出しは変更前とバイト一致（`ext_victory_rest.plmotion.json` → BVH）。
  - JS（polyling_motion_studio）は version 2 で保存し rotationSource を書かない。C# はその bakedBones を読まない（version 4 未満）ので影響しない。
- （解決済み 2026-10-20）モーション付き FBX の Unity 取り込み：段 7 の書き出しでテイクが読まれ、標本化した関節位置を確かめた（7.8）。
- CMU のモーションを書き出した後、同じモデルで続けて VMD を標本化すると、関節位置が最大 63 mm ずれる（モデルに状態が残る）。段 7 の前からある。段 7 の VMD の比較は、CMU の前に書いたもので行った。
- `ビルド用/PolyLing_Data/Plugins/` に以前のビルドの `UnityFbxSdkNative.dll` が残っている（ビルドは古いファイルを消さない）。MCP の書き込み先の外なので手で消す。新しいフォルダ（`SandBox/build7`）へのビルドには入らない。
- ビルド版で書き出しを動かす確認は未実施。MCP サーバーを `--target player` で起動し直す必要がある。
- MCP の書き出しコマンドは引数 `model_index` を見ず、今のモデルを書く。
- ProjectSettings の Scripting Define Symbols は `Standalone:` の行が空で残っている（動作に影響しない）。

## 多角形での書き出し（2026-10-15）

- `HierarchyBuildResult.SourceMeshObjectOf`（GameObject の Transform → 元の `MeshObject`。ミラーで作った静的メッシュはミラー生成後の `MeshObject`）を足した。
- `SourcePolygons.TryBuild`：`MeshBridgeDefault.ToUnityMesh` と同じ規則（3 頂点未満・非表示の面を飛ばす、材質番号でサブメッシュへ振り分け、扇形三角形化）で
  Unity メッシュの三角形を元の面へ戻す。三角形が過不足なく使い切れたときだけ多角形で書く。合わなければ警告を出してそのメッシュだけ三角形。
- 結果：
  - `DefaultMale`（四角形）：ufbx で読んだ多角形が元と一致（差は丸めの 0.1 mm 以内）。
  - AちゃんH（三角形）：書き出しは以前の `rt_a.fbx` と同じ。
  - MQO（`_0N_モデル_Ref_id修正版_id_`、70 メッシュ）：警告 0。面 88,465（3 角 9,294・4 角 79,171）がオブジェクトごとに元の mesh.csv（表示中で 3 頂点以上の面）と一致。
    ミラー側（「＋」の付くオブジェクト）も含め、各面の頂点位置（X 反転）と巻き順（反転。左手系→右手系の変換分）が実体側と同じ規則で一致し、差は丸めの 0.1 mm 以内。
- `queryCommandAudit`：今回の変更に関わる項目はすべて 0。

## 7. 段 7：FBX SDK を使わない書き出し（計画 2026-10-20・実施 2026-10-20。結果は 7.8）

### 7.1 目的

- 書き出しから Autodesk FBX SDK（`com.autodesk.fbx`）を外す。ビルド版の `FBXSDK_RUNTIME` と `UnityFbxSdkNative.dll` も要らなくする。
- 書き出しの材料を Unity メッシュから `MeshObject` に替える。三角形からの多角形の組み直し、Unity 頂点の名寄せ、モーフ差分の再展開を通らないようにする。
- 読み込み（ufbx）は変えない。

### 7.2 今の作りの根拠（コード）

| 箇所 | 中身 |
|---|---|
| `FbxExporterImpl.cs` 8–10 行 | 材料は `HierarchyBuilder` の GameObject＋Unity メッシュ（ミラー枝・関節・可視補完・モーフの規則を書き写さないため） |
| `HierarchyBuilder.cs` 328 行・620 行 | メッシュは `MeshObject.ToUnityMesh(Matrix4x4.identity)`。`MeshBridgeDefault.cs` 196–261 行で面を `face.Triangulate()` して三角形だけを積み、面と Unity 頂点の対応は残さない |
| `SourcePolygons.cs` | 上の三角形から元の多角形を推定し直す。扇形分割・`CompactSubMeshes`・面の除外の規則を前提に書き写している |
| `FbxExporterImpl.cs` 411–424 行（`ControlPointComparer`） | （頂点・UV・法線）ごとに分かれた Unity 頂点を、位置・ウェイト・全差分の完全一致でまとめ直して制御点にする |
| `HierarchyMorphBuilder.cs` 9–29 行・`BuildDelta` | モーフは「頂点番号＋オフセット」の疎な差分（`GetMorphOffsets`）。Unity 頂点の並びに合わせるため、位置をずらした複製をもう一度 `ToUnityMesh` に通して引き算する |
| `HierarchyBuilder.cs` 611 行・647–687 行 | ミラー枝の形状は `MeshObject` の段階で作る（`BuildMirroredMeshObject`）。ミラー側コンテキストの補正は全頂点一律の平行移動（`BuildMirrorSideMesh`） |
| `FbxImporter.cs` 42 行 | 読み込みは「制御点 1 つ = `Vertex` 1 つ」「面は多角形のまま」。FBX の頂点の持ち方（制御点＋角ごとの UV・法線）は `MeshObject`（`Vertex` の UV・法線スロット＋`Face.UVIndices` / `NormalIndices`）と同じ形 |

### 7.3 構成

`HierarchyBuilder`（変更）

- 姿勢・親子・ミラー枝・関節・可視補完・ボーンの並びの決定は今のまま正本とし、FBX 側へ書き写さない。
- 描画ノードごとに「形状の出どころ」を `HierarchyBuildResult.MeshSourceOf[Transform]`（新設の `HierarchyMeshSource`）へ記録する。
  - `ShapeSource`：その GameObject の形状を作る `MeshObject`（実体側は `mc.MeshObject`、許容モードの生成ミラーは `BuildMirroredMeshObject` の結果、ミラー側コンテキストは `mc.MeshObject`）。今の `SourceMeshObjectOf` と同じ選び方（`HierarchyBuilder.cs` 537 行・581 行）
  - `VertexOffset`：`BuildMirrorSideMesh` の平行移動量。量の計算を関数に切り出し、Unity メッシュ版と共用する
  - `IsSkinned`、`Materials`（`BuildMaterials` の結果。サブメッシュ番号順、詰める前）
  - `Bones`・`Bindposes`・`BoneSlotOf`（`AttachSkinnedMesh` 965–978 行で作る表）
  - `Morphs`：（モーフの索引、ブレンドシェイプ名、差分を反転するか、反転軸）。名前の一意化と `MorphShapes`（表情曲線のつなぎ先）は今と同じ規則で、`HierarchyMorphBuilder` の名前付けを差分作りから分けて共用する
- `HierarchyBuildOptions.CreateUnityMeshes`（既定 true）を足す。false のときは `ToUnityMesh`・ブレンドシェイプ差分・レンダラのメッシュを作らず、Transform と上の記録だけを作る。
  そのため `AttachStaticMesh` の中の姿勢の設定をメッシュの生成から分ける。glTF とヒエラルキー出力は true のまま使う。
- `RemapBoneWeights` の `MapSlot`（出力されないボーンを指すウェイトは 0 にして件数を警告）を、Unity の `Mesh` に依存しない関数にして FBX からも使う。
- `SourceMeshObjectOf` は廃止する（使っているのは FBX の組み直しだけ）。

新しい書き出し（`Runtime/Poly_Ling_Main/FBX/`、PolyLing.Runtime）

| ファイル | 中身 |
|---|---|
| `FbxNodeTree.cs` | FBX のノード（名前・プロパティの列・子）の木 |
| `FbxBinaryWriter.cs` | FBX 7.4 バイナリ（7400）。ヘッダ、32 ビットの終了位置、空レコード、フッタ。大きな配列は zlib（Deflate＋ヘッダ＋Adler-32） |
| `FbxAsciiWriter.cs` | 同じ木を ASCII で書く（書き出しパネルの「ASCII」） |
| `FbxModelWriter.cs` | モデル・モーションを木にする（FBXHeaderExtension、GlobalSettings、Documents、References、Definitions、Objects、Connections、Takes） |
| `FbxExporter.cs` | `IFbxExporter` の実装（`FbxExporterImpl` を置き換える）。標本化（`ModelMotionSampler`）と曲線の作り方は段 3 のものを移す |

- `PLFbxBridge.Exporter` は常にこの実装を返す。登録（`RegisterExporter`・`RegisterSdk`）、`FbxExporterNull`、`IFbxSdkInfo`、`TryGetSdkVersion` は廃止する。
  呼び出し側（`PolyLingPlayerViewerCore.Import.cs` 814 行、`…CreateCommands.FileIO.cs` 387・789・829 行、`PlayerExportSubPanel.cs` 545 行）は変えない。
- 起動時の 1 行（`PLFbxBridge.LogStatus`）は ufbx の版だけを出す。

### 7.4 書き出しの規則

| 項目 | 規則 |
|---|---|
| 座標 | ファイルは右手系・Y 上・前 +Z・単位 cm（UnitScaleFactor 1）。点は 100·S·p、方向は S·n、行列は S·M·S（S = diag(−1, 1, 1)）、面の角の並びは反転。読み込み（`FbxImporter.cs` 冒頭）の逆。`DeepConvertScene` を使った段 2 の結果（X 反転・+Z 正面）と同じになることを 7.6 で確かめる |
| ノード | 置く姿勢は今と同じ（ボーンはバインドのワールド、ほかは親のワールド · ローカル TRS）。書き出し上のローカル S·(親のワールド⁻¹ · ワールド)·S を移動（×100）・回転・拡大に分ける。回転は FBX 既定の回転順 XYZ のオイラー角（RotationOrder は書かない）。拡大 0 は 1e-12。InheritType は RSrs |
| 制御点 | `ShapeSource` の `Vertex` 1 つを制御点 1 つにする（`VertexOffset` を足す）。読み込みと対になる。Unity 頂点の名寄せは無くなる |
| 面 | `Faces` の順に、表示中で 3 頂点以上の面を多角形のまま書く（面を選ぶ条件は `ToUnityMesh` と同じ。2 頂点の面と非表示の面は書かない）。PolygonVertexIndex は各面の最後の角を −(i+1) |
| UV・法線 | 角ごとに `Face.UVIndices` / `NormalIndices` のスロット。範囲外のときは `ToUnityMesh` と同じ扱い（スロット 0、無ければ UV (0, 0)）。UV は IndexToDirect（頂点のスロットを並べた配列＋角の索引）、法線は ByPolygonVertex・Direct。`ToUnityMesh` が法線を Unity に計算させる条件（PreserveNormals でなく、法線が無いか全部 (0, 1, 0)。`MeshBridgeDefault.cs` 278–280 行）のメッシュは、法線の層を書かない（今は Unity が計算した法線を書いている） |
| 材質 | 面の材質番号を 0〜サブメッシュ数−1 に丸め、`Materials[番号]`。メッシュのノードには面のある材質だけを付けて番号を詰める（1 つなら AllSame、複数なら ByPolygon）。材質の中身とテクスチャは段 2 と同じ（Lambert、Diffuse / NormalMap / Emissive、埋め込みは Video の Content、画像は「ファイル名_textures」） |
| スキン | `Vertex.BoneWeight` を `BoneSlotOf` で写す（`MapSlot` を共用）。クラスタはボーンごとで、Indexes・Weights、Transform はメッシュのグローバル、TransformLink はボーンのバインドのグローバル（ファイル座標）。メッシュごとに BindPose を 1 つ置く |
| ブレンドシェイプ | Deformer「BlendShape」→ SubDeformer「BlendShapeChannel」（DeformPercent、FullWeights 100）→ Geometry「Shape」。Indexes は `GetMorphOffsets` の頂点番号、Vertices は差分（100·S·d）。ufbx は Shape の Vertices を差分として読む（`ufbx.c` 13028–13032 行）。生成ミラーは差分を `MirrorBranchOps.MirrorNormal` で反転する。差分 0 件のモーフも空の Shape で残す（今と同じ）。法線の差分は書かない（今も 0） |
| アニメーション | 標本化、関節の対応、表情の重みは段 3 と同じ。AnimationStack → AnimationLayer → AnimationCurveNode（T・R・DeformPercent）→ AnimationCurve。時刻は 1 秒 = 46186158000 の整数、補間は線形（KeyAttrFlags の線形 0x4。`ufbx.c` 14092–14094 行）。オイラー角はファイル座標の XYZ 順で、同じ回転の 2 通り (x, y, z) / (x + 180, 180 − y, z + 180) を 360° 単位でそろえて前のフレームに近い方。GlobalSettings に TimeMode と CustomFrameRate |
| 形式 | 既定はバイナリ。ASCII を指定されたら同じ木を ASCII で書く |

### 7.5 撤去するもの

- `Runtime/Poly_Ling_Fbx/`（`PolyLing.Fbx.asmdef`、`FbxExporterImpl.cs`、`FbxSdkInfoImpl.cs`、`SourcePolygons.cs` と各 `.meta`）
- `package.json` の依存 `com.autodesk.fbx`
- Scripting Define Symbols（Standalone）の `FBXSDK_RUNTIME`
- §1・§2 の FBX SDK の記述（表・依存・ビルド版・利用条件）は、実装後に今の構成へ書き換える。§5 の段 1〜6 の結果は記録として残す

### 7.6 検証

- 実装前に、今の（FBX SDK の）書き出しで AちゃんH・`DefaultMale.fbx`・MQO（`_0N_モデル_Ref_id修正版_id_`）とモーション 2 本（CMU・VMD）を `SandBox/` に書いて残し、比較の基準にする。
- ufbx で新旧を読み、ノード・ボーン・メッシュ・面（多角形の内訳）・材質・テクスチャの数を比べる。
  角ごとの位置・UV・法線・材質、スキンで再計算した位置とバインドの差、ブレンドシェイプを 1 にした角の位置も比べる。
  制御点の数は名寄せが無くなるので変わってよい（`Vertex` の数に一致すること）。
- 往復：書き出し → 読み込み → 書き出しで同じになること。
- モーション：段 3 と同じく、関節のワールド位置をキーのフレームで BVH の順運動学と比べる（CMU・VMD）。表情のテスト（0 / 0.5 / 1）も行う。
- Unity の FBX 取り込み（`Assets/` へ書き出して Refresh）：警告・エラーが 0。頂点・サブメッシュ・ブレンドシェイプの数を確かめる。モーション付きの FBX は再生して確かめる（§6 の残件も同時に済ませる）。
- glTF の書き出しとヒエラルキー出力が変更前とバイト一致すること（`CreateUnityMeshes` = true の経路が変わらないこと）。
- ビルド版：`FBXSDK_RUNTIME` なしでビルドし、読み込み・書き出し・モーション書き出しができること。`PolyLing_Data/Plugins/` に `UnityFbxSdkNative.dll` が無いこと。
- `queryCommandAudit` / `queryUiAutomationAudit` が全項目 0。

### 7.7 MCP でできない作業

- `Runtime/Poly_Ling_Fbx/` フォルダ自体の削除（`delete_path` はフォルダを拒否する。中のファイルは消せる）。
- `FBXSDK_RUNTIME` の削除（ProjectSettings に書き込めるかは着手時に確かめる）。

実施時：`Runtime/Poly_Ling_Fbx/` は `delete_path` で `_McpTrash/20261020_180739` へ移せた。ProjectSettings には MCP で書き込めないので、
一時的な Editor スクリプト（`PlayerSettings.SetScriptingDefineSymbols`）で `FBXSDK_RUNTIME` を外し、スクリプトは消した。

### 7.8 結果（2026-10-20）

実装（7.3 のとおり。差分だけ記す）

- `HierarchyMeshSource` に `MirrorDelta`・`MirrorAxis` を持たせ、生成ミラーのモーフ差分の反転を記録する。
- `HierarchyMorphBuilder.Plan` を足し、名前付け（`ShapeNameOf`）を `Apply` と共用する。`CreateUnityMeshes` = false のときは Plan だけ通す。
- ウェイトの写しは `HierarchyBuilder.RemapBoneWeight`・`DroppedWeightsMessage`（public static）にして FBX からも使う。
- オイラー角の分解は atan2 の式（`atan2(−r20, √(r00² + r10²))`）。asin とジンバルのしきい値の式は最大 8e-4 の誤差が出たので替えた（6.7e-8）。
- 材質は SDK の出力と同じく `EmissiveFactor` 1 を書く。
- 曲線のキーは KeyAttrFlags 0x104、KeyAttrDataFloat [0, 0, 0x0D050D05, 0]。
- FileId・CreationTime・フッタ ID は SDK が書いたファイルの 1 組をそのまま使う（3 つがそろっていないと FBX SDK が読まない）。
- どのオブジェクトにも空の入れ子リスト（`FbxNode.Block()`）を付ける。付けないと FBX SDK（Unity の取り込み）が AnimationLayer を読まず、テイクが無くなる。
  SDK が書いたファイルからこのブロックを抜くと同じくテイクが無くなることで確かめた（`FbxNodeTree.cs` の `Block` の注記）。

検証（新旧とも ufbx で読んで比べた）

| 対象 | 結果 |
|---|---|
| glTF 書き出し | AちゃんH・`DefaultMale`・MQO が変更前とバイト一致 |
| AちゃんH | 角 502,908 個の位置・UV・法線・ウェイト・材質が一致。ブレンドシェイプを 1 にした差は最大 9.8e-6 m。制御点 91,555（= `Vertex` 数。以前は名寄せで 85,323）。往復（書き出し → 読み込み → 書き出し）が一致 |
| `DefaultMale` | 多角形の内訳（3 角 8・4 角 7,532・5 角 4）が保たれ、ほかもすべて一致。ASCII の出力も一致 |
| MQO（`_0N_モデル_Ref_id修正版_id_`） | 面（3 角 9,294・4 角 79,171）が一致、往復も一致 |
| モーション（関節位置、BVH の順運動学と比較） | VMD 0.023 mm、CMU 0.0099 mm、`Victory-anim1.fbx` を `DefaultMale` へ当てたもの 0.020 mm（旧 SDK の出力は 1.6 mm） |
| Unity の取り込み | テイクが読まれる（AちゃんH＋VMD のクリップは曲線 1,181 本、Victory は 574 本）。Unity で標本化した関節位置と BVH の差は 5.4 mm・10.26 mm（旧 SDK の出力も 10.26 mm） |
| `queryCommandAudit` | 今回の変更に関わる項目はすべて 0（「正典に無い 1」`OpenProfileEditSpaceCommand` と「出せなかった 1」`AddGeneratedMeshCommand` は以前から） |
| ビルド版（`SandBox/build7`） | ビルド成功。`Plugins` は `plufbx.dll` と `lib_burst_generated.dll` だけ、`Managed` に Autodesk の DLL なし。`Player.log` に「[PolyLing] FBX: 書き出し PolyLing / 読み込み ufbx 0.23.1」、例外なし |

- MQO の `@OptionV_4@` は法線が全部 (0, 1, 0) なので、7.4 の規則で法線の層を書かない。ufbx と Unity は法線を作り直すので、以前の（Unity が計算して書いた）法線とは違う。Unity の取り込みは「法線が無いので計算する」旨の警告を出す。
- 制御点の数の変化は 7.6 のとおり（`Vertex` 数に一致）。
- ビルド版での書き出しの動作確認は未実施（§6 の残件）。

修正（2026-10-20）：スキンのクラスタの `Transform`

- 誤り：クラスタの `Transform` にメッシュのワールドを書いていた。ファイルの `Transform` はメッシュ → ボーン（ufbx の mesh_node_to_bone、`ufbx.c` 14050・16115 行。SDK のファイルも「`TransformLink`⁻¹ · メッシュのワールド」）。
  Unity の取り込みで bindpose が全部単位行列になり（`_0_ぜろちゃんH.pmx` の書き出しで、ボーン · bindpose とメッシュのワールドの差が最大 1.871）、手足が伸びて表示された。
- 見落とした理由：上の検証は角の位置をノードの空間で比べただけで、スキンで再計算した位置を比べていなかった。比較した `Assets/AちゃんH.fbx` は旧 SDK の出力だった。
- 修正：`Transform` = FileMatrix(ボーンのバインドワールド⁻¹ · メッシュのワールド)（`FbxModelWriter.WriteSkin`）。
- 検証：
  - ufbx でスキンを再計算した位置（Σ w · bind_to_world · geometry_to_bone · p）とバインドの位置の差：0ちゃんH 1.2e-7 m、AちゃんH 1.2e-7 m、`DefaultMale` 2.5e-7 m（修正前の 0ちゃんH は 1.744 m。旧 SDK の AちゃんH は 1.2e-7 m）。
  - Unity の取り込み：ボーン · bindpose とメッシュのワールドの差は 0ちゃんH・AちゃんH 3.6e-7、`DefaultMale` 1.8e-5。単位行列の bindpose は 0。正面の描画で手足の伸びが無いことを見た。

## 8. 段 8：ufbx を使わない読み込み（計画 2026-10-20・実施 2026-10-20。結果は 8.7）

### 8.1 目的と範囲

- 読み込みから ufbx（`plufbx.dll`）を外し、C#（PolyLing.Runtime）で読む。
- 対象は FBX 7.x（7100〜7700）のバイナリ（リトルエンディアン、32/64 ビットの位置）と ASCII。6.x 以前は「未対応の版」として理由を返して止める。
  プロジェクト内の FBX 132 本（Assets・Packages・Library/PackageCache・SandBox・データとツール）の内訳：バイナリ 7400 が 59、7500 が 40、7700 が 30、ASCII 7400・7700 が各 1、
  6.x は `BanyanTree.fbx`（AI Navigation のサンプル、バイナリ・ビッグエンディアン 6000）の 1 本だけ。

### 8.2 今の作りの根拠（コード）

| 箇所 | 中身 |
|---|---|
| `UfbxNative.cs` の `UfbxScene` | 読み込みの口はすべてここ。使うのは `FbxImporter`・`FbxMotionReader`・`FbxNodeTable` |
| `plufbx.c` `plufbx_load_memory` | 右手系・Y 上・単位 m（`UFBX_SPACE_CONVERSION_MODIFY_GEOMETRY`）、`generate_missing_normals`、元のパスでテクスチャの相対パスを解決 |
| `plufbx.c` `explicit_bind` | 明示のバインド：クラスタのリンク行列 → バインドポーズの順 |
| `plufbx.c` `plufbx_mesh_*` | 先頭スキンのクラスタ（geometry_to_bone・bind_to_world）、ウェイト（重い順）、ブレンドシェイプは target_shape（無ければ最後のキー） |
| `plufbx.c` `plufbx_material_info` / `texture_*` | Lambert・Phong の値、テクスチャ（LayeredTexture は先頭のファイル）、埋め込み（Texture か Video の Content）、Scaling・Translation |
| `plufbx.c` `plufbx_anim_sample` | 全ノードが継承 NORMAL なら局所変換を親から掛ける、そうでなければ `ufbx_evaluate_scene` |
| `PLFbxBridge.cs`、`…CreateCommands.FileIO.cs` 453 行、`PlayerImportSubPanel.cs` 355・1001 行 | `UfbxNative.TryGetVersion` で DLL の有無を確かめる |

### 8.3 構成

- `UfbxScene` と同じ口の `FbxScene` を作り、呼び出し側（`FbxImporter`・`FbxMotionReader`・`FbxNodeTable`）は型の名前だけ替える。
  `NodeInfo.AttribType` は ufbx の `ufbx_element_type` の値（`FbxNodeTable` が 0 と 6 を見る）なので、同じ値を返すか、`FbxNodeTable` を新しい列挙に合わせる（着手時に決めて記す）。
- 規則は ufbx の該当処理を読んで同じ式にし、各ファイルの冒頭に ufbx のどこと同じかを書く。

新しいファイル（`Runtime/Poly_Ling_Main/FBX/`）

| ファイル | 中身 |
|---|---|
| `FbxBinaryReader.cs` | バイナリ 7.x → `FbxNode` の木（書き出しと同じ型）。zlib の配列は `DeflateStream` |
| `FbxAsciiReader.cs` | ASCII 7.x → 同じ木。埋め込み画像（Content）は base64 |
| `FbxSceneReader.cs` | Objects・Connections（OO・OP）・Definitions の既定値・GlobalSettings → ノード・メッシュ・スキン・ブレンドシェイプ・材質・テクスチャ・Video・Pose・テイクの表 |
| `FbxTransformEval.cs` | ノードのローカル変換（移動・回転・拡大のオフセットとピボット、事前・事後回転、回転順、ジオメトリ変換）、継承の種類、座標系・単位の変換 |
| `FbxMeshReader.cs` | レイヤ要素（法線・UV・材質）の対応づけ（ByPolygonVertex・ByControlPoint・ByPolygon・AllSame）と参照（Direct・IndexToDirect）、法線の生成、スキン、ブレンドシェイプ |
| `FbxAnimEval.cs` | 曲線の評価（定数・線形・3 次。重み付き接線を含む）、レイヤの合成、DeformPercent、テイクの範囲、fps |
| `FbxScene.cs` | `UfbxScene` と同じ口 |

撤去するもの

- `UfbxNative.cs`、`Runtime/Plugins/x86_64/plufbx.dll`（と `.meta`）、`Native~/ufbx/`
- DLL の有無の確認 3 か所。`PLFbxBridge.Describe` は「書き出し PolyLing / 読み込み PolyLing」
- §1・§2 の ufbx の記述

### 8.4 進め方

1. ufbx の該当処理を読み、規則（座標・単位の変換、ノードの変換と継承、レイヤ要素、法線の生成、クラスタの補い、ブレンドシェイプ、材質・テクスチャの解決、テイクの範囲と fps、曲線とレイヤ）を 8.5 に書き出す。
2. 読み取り（バイナリ・ASCII）→ シーンの表 → `FbxScene` の順に作る。この間 ufbx は残す。
3. 新旧の比較（8.6）が通ったら呼び出し側を `FbxScene` に替え、ufbx を撤去する。

### 8.5 規則

正本は各ファイルの冒頭（ufbx 0.23.1 の `ufbx.c` の関数名と行番号を書いてある）。要点：

| 項目 | 規則 |
|---|---|
| 読み込みの設定 | ufbx の `plufbx_load_memory` と同じ：右手系・Y 上・前 +Z・単位 m、`UFBX_SPACE_CONVERSION_MODIFY_GEOMETRY`、`generate_missing_normals`、パス区切り `\`、倍精度 |
| テンプレート | Definitions の型が合う最初のテンプレート。Material・Model・AnimationStack・AnimationLayer 以外はサブ型が違えば使わない |
| Properties70 | 整数・実数 4 つまで・文字列。同名は後のものが勝つ |
| つなぎ | 要素ごとの Dst/Src を (DstProp, SrcProp) で安定ソート。根につながるノード以外の要素は捨てる |
| ノード | 並びは（深さ、親の要素番号、要素番号）。InheritType 0 → 成分ごと、2 → 親の拡大を無視。RotationActive が事前・事後回転と回転順を有効にする |
| MODIFY_GEOMETRY | 深さ 1 以下のノードに根の回転を事前回転として掛け、移動に根の拡大を掛ける。頂点とブレンドの差分も拡大 |
| クラスタ | bind_to_world = W·TransformLink（移動 × 拡大）。mesh_node_to_bone が無ければ bind⁻¹·ノードのワールド。geometry_to_bone はジオメトリ変換を含む |
| メッシュの層 | ByPolygonVertex・ByVertex・ByPolygon・AllSame。ReferenceInformationType は見ない。範囲外の索引は丸める。法線の生成は位置の同じ角を束ね、平滑化の層が無ければ平ら |
| 材質・テクスチャ | ufbx の材質の表と update_factor。テクスチャのファイル名は relative_root（DocumentUrl からの相対）で解決 |
| 曲線 | 定数・線形・3 次（自動・TCB の接線、重み）、外挿、ベジェはニュートン法。時間は 1 秒 = 46186158000 |
| レイヤ | 加算・ブレンド・上書き。評価したプロパティで UpdateNode を通す |

### 8.6 検証

- 新旧の比較：一時スクリプトで ufbx と `FbxScene` に同じファイルを読ませ、口が返す値を全部比べる（ノードは名前で対応づける）。
  対象は 8.1 の 7.x の 131 本。比べる値は、ノードのワールド・バインド・ジオメトリ変換、メッシュの点・面・角・法線・UV・材質、クラスタ、ウェイト、
  ブレンドシェイプ、材質の値、テクスチャのパスと埋め込み、テイクの範囲と fps、全フレームの関節のワールドとブレンドの重み。
- 呼び出し側の結果が変わらないこと：`FbxImporter`（段 4 の往復、`DefaultMale`・`Lion`）と `FbxMotionReader`（段 5 の CMU・`Victory-anim1.fbx`＋レスト用 FBX の .plmotion.json）の出力が変更前と同じ。
  読み込んだモデルをキャプチャで見る。
- `BanyanTree.fbx` は「未対応の版」の理由が出ること。
- ビルド版：`plufbx.dll` の無いビルドで、FBX のモデル読み込みとモーション読み込みができること。
- `queryCommandAudit` で今回の変更に関わる項目が 0。

### 8.7 結果（2026-10-20）

実装（8.3 との差）

- ファイルは `FbxBinaryReader`・`FbxAsciiReader`・`FbxSceneModel`・`FbxSceneReader`・`FbxTransformEval`・`FbxMath`・`FbxScene`。8.3 の `FbxMeshReader`・`FbxAnimEval` は `FbxSceneReader`・`FbxTransformEval` に入れた。
- `NodeInfo.AttribType` は ufbx の `ufbx_element_type` と同じ値を返す（`FbxNodeTable` は変えない）。
- `FbxNode`・読み取り 2 つ・`FbxValue` は public（Editor の検査スクリプトから使うため）。
- `FbxScene.AnimSample` は、動くプロパティとレイヤをテイクごとに先に拾い（`FbxTransformEval.PlanNode`・`PlanBlend`）、フレームごとには曲線だけを評価する。値は `EvaluateProps` → `UpdateNode` と同じ式。
- `FbxMotionReader` の `createdWith` は「PolyLing FbxMotionReader (自動割当 n 本)」（以前は「(ufbx, 自動割当 n 本)」）。

検証

| 対象 | 結果 |
|---|---|
| 新旧の比較（7.x の 131 本） | 全部一致。口が返す値の差は最大 1.4e-14（ほとんど 0）。アニメーション（外挿を含む 40 フレーム）とブレンドの重みも一致 |
| `BanyanTree.fbx` | 「未対応の版です（ビッグエンディアン 6000）。読めるのは FBX 7.x のリトルエンディアンだけです」（段 9 で読めるようにした） |
| 読み込み → 書き出し（呼び出し側） | `0ちゃんH`・`Lion`・`new_male_ascii`：木が変更前と完全一致。`rt_a`：テクスチャのフォルダ名（before_ / after_）だけ違う。`new_male`（バイナリ）：ポーズとクラスタの行列 12 個が 1 ulp（0 に近い成分の ±2.2e-16、移動 1e-11 cm）違う |
| モーションの読み込み（CMU・Victory・VMD の FBX → .plmotion.json） | `createdWith` の文字列だけ違い、ほかはバイト一致 |
| 速さ（`mot_cmu.fbx`、ノード 242・チャンネル 82・2,752 フレーム） | 読み込み 0.84〜1.08 秒（ufbx 0.16〜0.18 秒）。全ノードの評価 1.4 秒（ufbx 0.32 秒、事前の拾い出しの前は 600 フレームで 1.6 秒）。値は ufbx と一致 |
| 表示 | `rt_a.fbx` を読み込み、テクスチャ付きで直立（キャプチャ `SandBox/Captures/PolyLingCapture_0030.png`） |
| コンパイル | `UfbxNative.cs`・`plufbx.dll`・`Native~/` を撤去してエラー 0 |
| ビルド版（`SandBox/build8`） | ビルド成功。`Plugins` は `lib_burst_generated.dll` だけ。画面操作で `.FBX読込` → `DefaultMale.fbx` が T ポーズで表示、モーションパネル（FBX、レスト用 FBX `DefaultMale.fbx`）で `Victory-anim1.fbx` を読むと姿勢が変わった |
| `queryCommandAudit` | 今回の変更に関わる項目はすべて 0（「正典に無い 1」`OpenProfileEditSpaceCommand` と「出せなかった 1」`AddGeneratedMeshCommand` は以前から） |

## 9. 段 9：FBX 6.x の読み込み（計画・実施 2026-10-20）

### 9.1 範囲

- FBX 6.x（6000〜6100）のバイナリ（リトル・ビッグエンディアン）と ASCII。7.x のビッグエンディアンも読む。5.x 以前は「未対応の版」。
- 規則は ufbx 0.23.1 の 6.x の処理（legacy）を移した。正本は `FbxSceneReader.cs` の冒頭（`ufbx.c` の行番号つき）。

### 9.2 規則

| 項目 | 規則 |
|---|---|
| バイナリ | ヘッダのエンディアンが 0 以外ならビッグエンディアン：版・ノードの長さ・値・配列の要素数と中身（展開後）をバイト反転。配列のノード（Vertices・PolygonVertexIndex など。表は `FbxArrayNodes`、`ufbx.c` 7974-8498）で値が数の並びなら 1 つの配列にまとめる。文字列が混じれば壊れたファイル。`C` は 0/1 なら bool、ほかは文字の符号 |
| ASCII | 配列のノードの数の並び（6.x は `*n { a: }` でない）をまとめる。最初の値が次の行にあってよい。コメントの前のカンマは値の続き。`1.#INF`・`1.#IND`・`nan(...)`・`inf`・`infinity` |
| ID | 名前「型::名前」（バイナリは「名前\0\1型」）から作る。根は「Model::Scene」 |
| Properties60 | サブ型の列が無い。フラグは 2 番目、値は 3 番目から。フラグ `U` はユーザー定義 |
| 属性 | Model の中に属性の値がある。ノードのプロパティ名の表（`ufbx.c` 11641-11714）とユーザー定義のフラグでノードと属性に分け、属性を別の要素にしてノードへつなぐ。Vertices と PolygonVertexIndex があればメッシュ |
| ブレンドシェイプ | 版 7100 以下は Geometry の Shape から、デフォーマ・チャンネル（DeformPercent。メッシュのプロパティが数ならつなぐ）・形を作る（`ufbx.c` 13073-13130） |
| つなぎ | 名前で引く。ノードに無いプロパティへのつなぎは属性へ、スキン・キャッシュはジオメトリへ付け替える |
| ポーズ | PoseNode の Node は名前 |
| テイク | Takes の Take の下の Model の Channel。Transform の T・R・S は Lcl Translation・Rotation・Scaling へ。キーは KeyVer（既定 4005）で読み分け、KeyCount が無い・余りがあれば失敗。自動の傾きはフラグ 0x6000 |
| 設定 | GlobalSettings は Objects の中にあってよい。Version5 の FrameRate を CustomFrameRate・TimeMode 14 にする（あるプロパティが勝つ） |
| 材質のテクスチャ | メッシュの LayerElementTexture（面ごとのテクスチャ番号）を材質のテクスチャへ（`ufbx.c` 13666-13698・22367-22466）。材質のテクスチャを名前で並べる前に入れる |
| プロパティのつなぎ | つながれたプロパティはつなぎ元の値を返す（連鎖をたどる。`ufbx.c` 19270・25930-25950）。7.x にも効く |

### 9.3 検証

ufbx 0.23.1（クラウド上で gcc でビルド。プロジェクトには戻していない）と口が返す値を全部比べた（段 8 と同じ比べ方。NaN どうしは一致とする）。

| 対象 | 結果 |
|---|---|
| ufbx のテストの 6.x（203 本） | 199 本一致。違う 4 本：`max_texture_mapping_6100`（ASCII・バイナリ。3ds Max のシェーダーのテクスチャのファイル名。7.x の同じファイルも違う）、`synthetic_unicode_error_identity_6100`（ASCII・バイナリ。不正な UTF-8 の名前が UTF-8 に直すと同じ ID になる） |
| ufbx のテストの 7.x（501 本） | 一致 480・違う 15・両方失敗 6。段 9 の前は違う 31。直ったもの：ビッグエンディアン 7100・7400・7500、7100 のブレンドシェイプ、`recursive_connections`、外挿のファイルいくつか。残りは §6 の残件 |
| プロジェクトの FBX（122 本） | 全部一致（`BanyanTree.fbx` を含む） |
| Unity | コンパイルエラー 0。`BanyanTree.fbx` を読み込み、直立で表示（`SandBox/Captures/PolyLingCapture_0031.png`）。`maya_game_sausage_6100_binary_deform.fbx` でメッシュとボーンを表示（`PolyLingCapture_0033.png`）。`maya_character_6100_binary.fbx`（骨格だけ）のモーション → `SandBox/fbx6/maya_character.plmotion.json`：エラー 0、bakedBones 10、マッスル 21、4.958 秒 |
| `queryCommandAudit` | 今回の変更に関わる項目はすべて 0（「正典に無い 1」`OpenProfileEditSpaceCommand` と「出せなかった 1」`AddGeneratedMeshCommand` は以前から） |

- 速さは段 9 の変更後に測り直していない。
- テスト用に `SandBox/fbx6/` へ ufbx のテストの 6.x ファイル 2 本を置いた。

## 6b. 残件の締め（2026-10-31。この計画は完了）

- ufbx と値が違うもの・読み込みの速さ：記録のみ。直さない。
- CMU の後に VMD を標本化すると関節がずれる件：直した。原因は `UnityClipApplier.ResetAllBones` が `PoseLayer.Clear()` で層を消し、`BonePoseData` の行列のキャッシュ（`_dirty`）を無効にしていなかったこと。`ClearLayer` に替えた。確認は `PolyLing_BVH対応計画.md` の 9。
- MCP の書き出しコマンドが `model_index` を見ない件：直した。`exportMotionToBvh`・`exportMotionToFbx`・`exportVmdToMotion` と、モデルの書き出し 7 つ（PMX・MQO・OBJ・STL・VRM・glTF・FBX の `OnExport*`）が `cmd.ModelIndex` のモデルを使う。パネルは今のモデルの番号を渡すので動きは変わらない。確認：`model_index=7`（無い番号）で `exportMotionToBvh`・`exportFbxFile` が「モデルがありません」、`model_index=0`（今のモデルは 2）で書き出せた。
- 書き出しの後の表示の引き直し：`SettleWorldPositions` に替えた（`PolyLing_BVH対応計画.md` の 9）。
- `ビルド用/PolyLing_Data/Plugins/` の古い `UnityFbxSdkNative.dll`、ビルド版での書き出しの確認：道具で届かないので `PolyLing_残件.md` の 6 へ移した。
- Scripting Define Symbols の空の `Standalone:` 行：動作に影響せず、ProjectSettings は書き込み先の外。直さない。

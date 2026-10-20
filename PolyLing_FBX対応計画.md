# PolyLing FBX 対応計画

FBX のモデルとモーションを読み書きする。Editor とビルド版（Windows x64 スタンドアロン）の両方で動かす。
正本はこの計画書。1 段＝1 会話・この計画書への承認 1 回で進める。

## 1. ライブラリの選定

| 向き | ライブラリ | 理由 |
|---|---|---|
| 書き出し | Autodesk FBX SDK の C# バインディング（`com.autodesk.fbx` 5.1.3） | Unity の FBX Exporter と同じ土台。公開されている API が書き出し用途の部分集合 |
| 読み込み | ufbx（C、単一ソース、MIT またはパブリックドメイン）を `plufbx.dll` にして P/Invoke | `com.autodesk.fbx` は C# でダウンキャストできず、スキン（`FbxDeformer` → `FbxSkinDeformer`）を読めない。現在以外のテイク（`FbxAnimStack`）も型付きで辿れない。Unity も読み込み用途には推奨していない（5.1.3 マニュアル「Known issues and limitations」） |

`com.autodesk.fbx` で型付きで読めるのはノード階層・`EvaluateLocalTransform(FbxTime)`・`GetMesh`・`GetSkeleton`・
`GetBlendShapeDeformer`・`GetCurrentAnimationStack` まで（5.1.3 の API リファレンス）。

## 2. 構成

| 置き場 | 中身 |
|---|---|
| `Runtime/Poly_Ling_Main/FBX/PLFbxBridge.cs` | Runtime 側の口。書き出し実装（PolyLing.Fbx）の登録先。glTF の `PLGltfBridge` と同じ分離規約（`IVrm10Exporter.cs` 冒頭）で、Autodesk の型を PolyLing.Runtime に持ち込まない |
| `Runtime/Poly_Ling_Main/FBX/UfbxNative.cs` | `plufbx.dll` への P/Invoke。PolyLing.Runtime から直接呼ぶ |
| `Runtime/Poly_Ling_Fbx/`（アセンブリ `PolyLing.Fbx`） | `Autodesk.Fbx` を参照する唯一のアセンブリ。対象は Editor と WindowsStandalone64。`[RuntimeInitializeOnLoadMethod]` で `PLFbxBridge` へ登録する |
| `Runtime/Plugins/x86_64/plufbx.dll` | ufbx の DLL（Editor と Win64） |
| `Native~/ufbx/` | `ufbx.c`・`ufbx.h`（0.23.1）・`plufbx.c`（C の口）・`build_msvc.bat`・`LICENSE_ufbx.txt`。`~` 付きなので Unity は取り込まない |

- ufbx の構造体は C# へ直接渡さない。`plufbx.c` が平らな関数（数・配列の写し）にして公開する。
- 依存：`package.json` の `dependencies` に `com.autodesk.fbx: 5.1.3`。
- ビルド版：Player の Scripting Define Symbols（Standalone）に `FBXSDK_RUNTIME`。これが無いと
  `UnityFbxSdkNative.dll` がビルドへコピーされない（`com.autodesk.fbx` の `Editor/Scripts/PostProcessAddToBuild.cs`。
  コピー先は `<exe>_Data/Plugins`）。`Autodesk.Fbx.asmdef` 自体は定義の有無によらず Win64 でコンパイルされる。
- 利用条件：FBX SDK は Autodesk の FBX SDK License and Services Agreement に従う。

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

- `build_msvc.bat`（Visual Studio 2022 で `plufbx.dll` を作り直す）は未実行。段 4 の DLL は zig cc で作った（段 4 の表）。
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
- モーション付き FBX の Unity 取り込み・再生はまだ試していない（段 5 の FBX 読み込みで往復を確かめるときに併せて行う）。

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

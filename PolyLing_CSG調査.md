# CSG（ブーリアン演算）の穴 — 調査の整理

最終更新: 2026-09-17。この会話で実測・確認した事実だけを書く。推測は「試していない対策」に候補として分け、効果は未確認と明記する。

---

## 1. 背景

### 1.1 何が問題になったか

- 手本 `boolean_demo`（立方体 6×6×6 − 立方体 3×3×3）は水密な結果になる（穴 0）。
- 細かく割った曲面どうしの差では、結果に穴が残る。`Plane.cs` 冒頭の注記には、以前「球 738 頂点 − 円柱 170 頂点で 8 枚の脱落が残る」と記録されていた。
- 手本 `boolean_demo` の e1 / e8 には、以前「原因は `Node.ClipPolygons` が葉で裏側を捨てる設計」と書いてあったが、実測の裏付けが無かったので、この会話で実測値だけの記述に書き換えた。

### 1.2 演算の流れ（`BooleanOps.Perform`）

1. MeshObject → Unity Mesh（`ToUnityMesh`。三角形に分割）
2. Unity Mesh → CSG の `Model` → 多角形（三角形）
3. pb_CSG の BSP で演算（`CSG.Subtract` / `Union` / `Intersect` → `Node`）
4. 結果の多角形 → Unity Mesh（多角形ごとに扇形に三角形分割。`Model.cs:121-131`）
5. `FromUnityMesh(mergeVertices: false)`（頂点は三角形ごとにばらばら）
6. `MeshMergeHelper.MergeAllVerticesAtSamePosition(mergeThreshold)`（手本では 1e-4）
7. 手本では、この後に `resolveTJunctions`（tolerance 1e-4）を撃つ

---

## 2. 関連ファイル

| 役割 | パス（`Packages/com.haglib.polyling/Runtime/` から） |
|---|---|
| 演算本体 | `Poly_Ling_Main/Core/Ops/BooleanOps.cs` |
| 取り込んだ CSG（pb_CSG / csg.js 系） | `ThirdParty/ParaboxCSG/CSG.cs`・`Node.cs`・`Plane.cs`・`Polygon.cs`・`Model.cs`・`Vertex.cs`・`VertexUtility.cs` |
| コマンド定義 | `Poly_Ling_Main/Core/Data/PanelCommand.Topology.cs`（`BooleanMeshCommand`・`DiagnoseBooleanCommand`） |
| コマンドの実処理 | `Poly_Ling_Player/View/Core/PlayerCommandDispatcher.TPoseMergeMorph.cs` |
| 計測 | `Poly_Ling_Main/Core/Ops/BooleanDiagnostics.cs`・`BooleanDiagnosticsBsp.cs` |
| T 字解消 | `Poly_Ling_Main/Core/Ops/TJunctionOps.cs` |
| 頂点まとめ | `Poly_Ling_Main/Core/Ops/MeshMergeHelper.cs` |
| 穴の列挙 | `Poly_Ling_Main/Core/Ops/BridgeAutoPairOps.cs`（`CollectHoles`） |
| 手本 | `@settings/PolyLing/PolyLing/scenarios.csv` の `boolean_demo` |

---

## 3. 問題点（実測）

### 3.1 再現条件

| 項目 | 値 |
|---|---|
| A（削られる側） | 球。半径 0.5、経線 32 × 緯線 24、738 頂点・768 面、原点 |
| B（削る側） | 円柱。半径 0.2、高さ 1.5、円周 24 × 高さ 6、上下フタあり、170 頂点・192 面、位置 (0.1, -0.75, 0.05) |
| 演算 | 差（A − B） |
| epsilon | 1e-6 |
| mergeThreshold / T 字解消 tolerance | 1e-4 / 1e-4 |

### 3.2 計測値（`diagnoseBoolean`）

| 項目 | 値 |
|---|---|
| 入力の穴 A / B | 0 / 0 |
| 入力の多角形 A / B（平面が無効） | 1,472 / 336（0 / 0） |
| BSP の手順ごとの多角形数 | build A 1472 → build B 336 → A.Invert 1472 → A.ClipTo(B) 2457 → B.ClipTo(A) 120 → B.Invert 120 → B.ClipTo(A) 120 → B.Invert 120 → A.Build(B) 2577 → A.Invert 2577 |
| 結果の多角形 | 2,577 |
| 結果で平面が無効（法線が 0） | 101 |
| 結果で先頭 3 頂点の法線が多角形全体から 1° 以上ずれる | 0 |
| 結果の頂点数（三角形ごとにばらばら） | 11,262 |
| 穴（1e-7 でまとめ、T 字解消後） | **25**（頂点数 3〜10） |
| 穴（1e-4 でまとめ、T 字解消後） | **6**（頂点数 3, 4, 3, 3, 4, 3） |

1e-4 でまとめた後の 6 個の穴の重心（A のローカル）：

```
( 0.0346,  0.4356,  0.2414)
( 0.2043, -0.4392, -0.1190)
( 0.1084, -0.4642, -0.1489)
(-0.0936, -0.4806,  0.0987)
( 0.0100, -0.4829, -0.1273)
(-0.0315, -0.4879, -0.0991)
```

### 3.3 写した BSP で数えた分岐（`BooleanDiagnosticsBsp.cs`）

写した BSP の結果は、元の Node の結果と一致した（多角形数・無効数・頂点位置の和）。

| 分岐 | 球 − 円柱 | 立方体 − 立方体 |
|---|---|---|
| 葉で捨てた多角形（平面が有効） | 2,180 | 206 |
| 葉で捨てた多角形（平面が無効） | 389 | 0 |
| 平面が無効なまま「同一平面・裏向き」に振り分け | 101 | 0 |
| 平面が無効な節で下の節を見ずに返した回数 | 0 | 0 |

### 3.4 穴と無効な平面の関係

1e-7 でまとめた 25 個の穴のうち 15 個は、穴の頂点が「平面が無効な多角形」の頂点と 1e-4 以内で重なっていた（1〜4 頂点）。残り 10 個は重なっていない。

### 3.5 配置による差

同じ形でも、球を x = 3 に置き、円柱を同じ相対位置 (3.1, -0.75, 0.05) に置くと、穴は 30 / 6 になった（原点では 25 / 6）。演算は A のローカル空間で行うので、入力座標の float の丸めが違う。

### 3.6 比較：水密になる組

立方体 6×6×6 − 立方体 3×3×3（`boolean_demo` と同じ配置）は、多角形 432 / 108 → 465、平面が無効 0、穴 0 / 0。

---

## 4. 今までに実施した対策と結果

| 対策 | 実施 | 結果 |
|---|---|---|
| `Plane` の法線を正規化 | この会話より前（`Plane.cs` 冒頭の注記） | 注記によれば 8 枚の脱落が残った。この会話では再測していない |
| epsilon を 1e-6 / 1e-9 で比較 | この会話より前（手本 e7 の記述） | 記述では結果は同一。この会話では再測していない |
| 頂点まとめ（1e-4）＋ T 字解消 | 手本の手順 | 穴 25 → 6。0 にはならない |
| 平面が無効な多角形の法線を Newell 法で作り直して演算（計測用の試し、`fixInvalidPlanes`） | この会話 | 作り直した平面 534・残った無効 0・結果 2,608 多角形。穴は 31 / 6 で、1e-4 でまとめた後の 6 は減らない |
| `Node` の空の木で落ちる不具合を修正（`polygons` を空リストで初期化、`plane` が null のとき何もしない） | この会話 | 積で結果が空のときの NullReferenceException が消えた。球 − 円柱の結果（25 / 6・重心）は修正前と完全一致 |
| `booleanMesh` の失敗を `Fail` で返す／対象を報告する | この会話 | 空の結果で「ブーリアン失敗: 結果が空になった」が返る。穴には影響しない |
| `AssignMissingIds()` の呼び出しを削除 | この会話 | 穴には影響しない（ID の自動付与の規約違反の解消） |
| T 字解消・頂点まとめの高速化（格子の大きさ、格子探索） | この会話 | 計測時間 30 秒超 → 約 3 秒。結果（25 / 6・重心）は完全一致 |
| 穴ごとの面積を測る（`LoopArea`） | この会話 | 25 個とも -1（穴の辺を 1 周たどれなかった）。面が欠けたのか継ぎ目が割れたのかは、まだ切り分けられていない |

### 分かったこと

- 入力は閉じていて、平面が無効な多角形も無い。穴は演算の中か、その後で生じている。
- 頂点をほぼ完全一致（1e-7）でまとめた段階で、すでに穴がある。1e-4 まとめで生じた穴ではない。
- 平面が無効な多角形を作り直しても、1e-4 まとめ後の 6 個は減らない。平面が無効なことは、この 6 個の原因ではない。
- 平面が無効な節で下の節を見ずに返す経路は、一度も通っていない。

---

## 5. 試していない対策（候補。効果はどれも未確認）

### 5.1 原因を切り分けるための計測

1. **`LoopArea` を直して、穴の形を測る**
   境界の辺を 1 周たどれない理由を確かめる（頂点に境界の辺が 3 本以上つながる点があるのか、実装の誤りか）。そのうえで 6 個の穴の面積と辺を出し、「面が欠けた」か「継ぎ目が割れた」かを分ける。
2. **消えた破片の出どころを追う**
   写した BSP で多角形に「元の三角形（A / B の何番目）」の印を付けて演算し、6 個の穴の位置にあった破片が、どの手順（`ClipTo` の葉で捨てた / 同一平面で裏向きに振り分けた / 分割でできた）で消えたかを穴ごとに返す。
3. **分割でできた頂点どうしの食い違いを測る**
   隣り合う 2 つの多角形が同じ辺で分割されたとき、`SplitPolygon` は多角形ごとに交点を計算する（`Plane.cs` の Spanning 分岐。`VertexUtility.Mix(vi, vj, t)`）。同じ辺の交点が 2 つの多角形で何 m ずれているかを数え、1e-4 を超える組が穴の位置と重なるかを見る。

### 5.2 本体の直し方の候補

4. **分割点を辺ごとに共有する**
   同じ辺（2 頂点の組）を分割するときは、1 回だけ交点を計算して使い回す。上の 3 で食い違いが穴と重なると確認できた場合の候補。
5. **幾何計算を double にする**
   pb_CSG の頂点は Unity の `Vector3`（float）で、平面・分割の計算も float で行っている。元の csg.js は JavaScript の数値（64 ビット浮動小数点）で計算している。`Plane` / `SplitPolygon` / `Vertex` の位置を double にする。
6. **退化した破片を演算の途中で落とす、または除く**
   面積が float の刻み以下の破片（今回は平面が無効な 101 枚を含む）を、分割の直後に捨てるか、隣へ吸収する。ただし、平面を作り直す試し（4 章）では 6 個は減らなかったので、単独では効かない可能性がある。
7. **入力を三角形でなく元の多角形で渡す**
   現在は `ToUnityMesh` で三角形に分割してから CSG に渡している。同一平面の四角形を 1 枚の多角形として渡し、破片の数と分割回数を減らす。
8. **水密を保証する別の実装へ差し替える**
   辺を共有したまま切る方式（corefinement）の実装への置き換え。候補の実装、ライセンス、対応プラットフォーム（Player ビルド先）は、まだ調べていない。

### 5.3 後処理（根本対策ではない）

9. **小さな穴を埋める**
   1e-4 でまとめた後に残る 3〜4 頂点の穴を面で塞ぐ。原因は残るので、ほかの形で同じことが起きたときに検出できなくなる。

---

## 6. 計測の再現手順（MCP）

```
createSphere   params.meshName=DG_Sphere0 params.radius=0.5 params.longitudeSegments=32 params.latitudeSegments=24
createCylinder params.meshName=DG_Cyl0 params.radiusTop=0.2 params.radiusBottom=0.2 params.height=1.5
               params.radialSegments=24 params.heightSegments=6 params.capTop=true params.capBottom=true
               params.edgeRadius=0 placement.worldPosition=0.1,-0.75,0.05
diagnoseBoolean aMasterIndex=<球> bMasterIndex=<円柱> op=Subtract mergeThreshold=0.0001 epsilon=0.000001
               tJunctionTolerance=0.0001 [fixInvalidPlanes=true]
```

- 空のモデルなら、球が 0、円柱が 1 になる。
- 所要時間はおよそ 3〜10 秒（`fixInvalidPlanes=true` のとき長い）。段階ごとの時間は戻り値の `timingNames` / `timingMs` に入る。

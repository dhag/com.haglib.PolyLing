# Quadトポロジ優先 減数化（Unity向け）指示書
（説明＋疑似コードまとめ / 目的：別AIが実装するための仕様書）

## 目的
- 入力：四角（quad）主体だが一部に三角（tri）が混在するメッシュ（Unity `Mesh`：trianglesのみ）
- 出力：**四角格子の性質をできるだけ保ったまま**ポリゴン数を減らす（トポロジ最優先）
- 制約：
  - **通常のDecimate（頂点collapse/QEM）を使わない**（quad格子が崩壊しやすい）
  - seam（UVシーム）/ hard edge / boundary（境界）は壊さない
  - triを増やさない（可能なら減らす：tri→quad修復を先に行う）
  - UnityのMeshはtriしか持てないため、内部表現は「**tri2枚=quad**」として扱う

---

## コア思想（最重要）
- 普通の減数化：点（頂点）を潰して減らす → quad格子がtri化して崩壊
- 本手法：**quadの列（strip）を間引く**  
  - 2×1のquad並びを検出し、中央の共有線を消す  
  - 実装上は「**tri4枚 → tri2枚**」への置換（quad2枚→quad1枚）

---

## 用語
- **Face**：tri（Unity triangles上の1面）
- **Edge**：無向エッジ（a<bで正規化）
- **QuadPair**：tri2枚を1枚のquadとみなしたもの
- **Cut Edge**：境界/UVシーム/ハードエッジ（ここは跨がない・潰さない）
- **QuadPatch**：Cut Edgeで区切られたQuadPairの連結成分
- **U/V方向**：QuadPatch内の格子の2主方向（片方向だけ間引くと綺麗）

---

## 想定I/O
- Input: Unity `Mesh`（`vertices`, `uv`, `triangles`）
- Output: `mesh.triangles` を置換したMesh（頂点とuvは原則そのまま）

---

# 実装設計

## 1. データ構造（Half-edge“風”の最小セット）
> 本物のHalf-edgeは不要。隣接が取れれば十分。

```csharp
class Face {
    int v0, v1, v2;
    int triStart;      // triangles配列の開始インデックス（3の倍数）
    Vector3 normal;
}

class Edge {
    int a, b;          // a<b 正規化
    int faceL = -1;    // 片側面ID
    int faceR = -1;    // 反対側面ID
    bool boundary;
    bool seam;
    bool hard;
    int id;
}

class VertexAdj {
    List<int>[] vertexFaces;   // v -> faceIds
    List<int>[] vertexEdges;   // v -> edgeIds
}

class QuadPair {
    int id;
    int faceAId, faceBId;   // tri2枚
    Face tri0, tri1;        // 参照
    int[] cycle4;           // quadの周回4頂点（任意：保持すると後工程で便利）
    Vector3 avgNormal;
}

enum DirClass { Unknown, U, V }

class QuadLink {
    int toQuadId;
    int sharedEdgeId;  // quad間共有エッジ（collapse対象の特定）
    DirClass dir;      // U/V（パッチごとに決める）
}

class QuadNode {
    int id;
    Vector3 center;
    List<QuadLink> links;
}

class QuadGraph {
    Dictionary<int, QuadNode> nodes;
}

struct TriRef { int i0; }  // triangles配列中の triStart

struct ReplaceOp {
    int quadAId, quadBId;
    TriRef[] remove4;  // 消すtri4枚
    int[] add6;        // 追加tri2枚=6index
    float score;       // 採用優先度
}
```

---

## 2. 全体パイプライン（Multi-pass）
停止条件を明示し、各パスで再構築する（trianglesを書き換えると隣接が無効化するため）。

```csharp
ReduceQuadTopology_MultiPass(mesh, targetTriangleRatio, maxPass):

    tri0 = mesh.triangles.Length / 3
    targetTri = Round(tri0 * targetTriangleRatio)

    prevCollapsed = INF

    for pass in 0..maxPass-1:

        triNow = mesh.triangles.Length / 3
        if triNow <= targetTri: break

        collapsed = ReduceQuadTopology_OnePass(mesh, pass)

        if collapsed == 0: break
        if collapsed < prevCollapsed * 0.3f: break  // 収束（任意）
        prevCollapsed = collapsed
```

---

## 3. 1パス処理（概要）
```csharp
int ReduceQuadTopology_OnePass(mesh, pass):

    m = BuildMeshData(mesh)              // verts, uv, triangles
    BuildAdjacency(m)                    // faces, edges, vertexAdj
    PrecomputeSeamHelpers(m)             // posGroups, posEdgeUvPairs
    MarkCutEdges(m, passParams)          // boundary/seam/hard

    quadPairs = DetectQuadPairs(m, passParams)      // tri2枚=quad
    quadGraph = BuildQuadGraph(m, quadPairs)        // cutを跨がない隣接

    patches = BuildQuadPatches(quadGraph)

    targets = []
    for patch in patches:
        targets += BuildTargets_PatchUVDir(m, quadGraph, patch, passParams)

    ops = []
    for t in targets:
        if BuildReplaceOp(t, m, out op):
            op.score = Score(t, m, passParams)
            ops.Add(op)

    ops.SortByDescending(score)
    accepted = FilterOps_NoTriNoQuadConflict(ops)

    if accepted.Count == 0: return 0

    newTris = RebuildTriangles(mesh.triangles, RemoveSet(accepted), AddList(accepted))
    mesh.triangles = newTris
    mesh.RecalculateNormals()

    return accepted.Count
```

---

# 主要サブ処理（説明＋疑似コード）

## A. 隣接構築（BuildAdjacency）
```csharp
BuildAdjacency(meshData):

    verts = m.verts
    tris  = m.triangles

    faces = []
    edgesDict = Dictionary<(int a,int b), Edge>()
    vertexAdj = new VertexAdj(verts.Length)

    for i=0; i<tris.Length; i+=3:
        f = new Face(tris[i], tris[i+1], tris[i+2])
        f.triStart = i
        f.normal = ComputeTriNormal(f)
        faces.Add(f)
        vertexAdj.vertexFaces[f.v0].Add(faceId) ... etc.

        AddEdge(f.v0,f.v1, faceId)
        AddEdge(f.v1,f.v2, faceId)
        AddEdge(f.v2,f.v0, faceId)

AddEdge(a,b, faceId):
    key = (min(a,b), max(a,b))
    if not exists: create Edge{a=min,b=max, faceL=faceId}
    else: set faceR=faceId
```

---

## B. Cut判定
### B1. Boundary
`edge.faceL==-1 || edge.faceR==-1` を boundary とする。

### B2. Hard Edge（IsHardEdge）
最小版：左右面法線の角度がしきい値以上ならhard。
```csharp
IsHardEdge(edge, faces, hardAngleDeg):
    if edge.faceL<0 || edge.faceR<0: return true
    nL = faces[edge.faceL].normal
    nR = faces[edge.faceR].normal
    return AngleDeg(nL,nR) >= hardAngleDeg
```

### B3. UV seam（IsUvSeam）
UnityではUV分割は「同一位置の別頂点ID」によって表現されるので、
- 位置同一点グループでUVが複数種類 → seam（保守的）
- 位置エッジに複数UVペア → seam（より精密）
を使う。

#### 前処理：位置同一点グループ
```csharp
BuildPositionGroups(verts, posQuant):
    posGroups: Dict<PosKey, List<int vId>>
    key = Quantize(verts[v], posQuant)
    posGroups[key].Add(v)
```

#### 前処理：位置エッジ→UVペア集合
```csharp
BuildPosEdgeToUvPairs(tris, verts, uv, posQuant, uvQuant):
    map: Dict<PosEdgeKey, HashSet<UVPair>>
    for each tri:
        AddEdge(a,b); AddEdge(b,c); AddEdge(c,a)

AddEdge(va,vb):
    pk = MakePosEdgeKey(verts[va], verts[vb])
    up = MakeUVPair(uv[va], uv[vb]) // UVも量子化して安定化
    map[pk].Add(up)
```

#### 判定
```csharp
IsUvSeam(edge):
    if HasMultipleUV(posGroups[Quantize(pos(a))]): return true
    if HasMultipleUV(posGroups[Quantize(pos(b))]): return true
    if posEdgeUvPairs[MakePosEdgeKey(pos(a),pos(b))].Count > 1: return true
    return false
```

---

## C. QuadPair検出（DetectQuadPairs）
tri2枚が共有エッジを持つとき、以下を満たせばquad扱い：
- 法線角が小さい
- 4点がほぼ共面
- 投影2Dで凸

### C1. 平面性
```csharp
NearlyCoplanar(a,b,c,d):
    n = Normalize(cross(pb-pa, pc-pa))
    dist = abs(dot(pd-pa, n))
    return dist <= epsDist (例: epsDist = edgeLen*1e-3)
```

### C2. 凸性（投影2D）
```csharp
BuildPlaneBasis(n, out u, out w)
ProjectTo2D(p) => (dot(p-origin,u), dot(p-origin,w))
OrderCycleByAngle(4points2D) => 周回順
IsConvex2D(cycle) => 連続外積符号が全一致
```

### C3. quad生成（edgeごと）
```csharp
DetectQuadPairFromEdge(edge):
    fL,fR = faces[edge.faceL], faces[edge.faceR]
    if AngleDeg(fL.normal,fR.normal) > normalAngleDeg: false

    a=edge.a; b=edge.b
    c = ThirdVertex(fL,a,b)
    d = ThirdVertex(fR,a,b)
    if !NearlyCoplanar(a,b,c,d): false
    cycle4 = OrderCycleByAngle([a,b,c,d] projected)
    if !IsConvex2D(cycle4): false
    return QuadPair(fL,fR,cycle4)
```

---

## D. QuadGraph構築（cutを跨がない隣接）
`mesh Edge` を走査し、左右Faceが別々のQuadPairに属していればQuadGraphにリンクを張る（sharedEdgeId保持）。

```csharp
BuildQuadGraph(edges):
    faceToQuad = ...
    for each meshEdge e:
        if IsCutEdge(e): continue
        qL = faceToQuad[e.faceL]
        qR = faceToQuad[e.faceR]
        if qL!=qR:
            AddUndirectedLink(qL,qR, sharedEdgeId=e.id)
```

---

## E. QuadPatch抽出（連結成分）
```csharp
BuildQuadPatches(quadGraph):
    BFS/DFSで連結成分を取る
```

---

## F. PatchごとのU/V主方向推定とラベリング
Patch内のquad中心点群から2主方向（e1,e2）を推定し、隣接リンクをU/Vに分類する。

### F1. 主方向（2D PCA風）
- patch平均法線 `nPatch` を取り平面基底 `basisU,basisV` を構築
- quad centerを2Dへ投影
- 共分散2×2から最大固有ベクトル `e1` を求める（`tan(2θ)`式）
- `e2 = Perp(e1)`

```csharp
EigenVectorLargest(xx,xy,yy):
    theta = 0.5 * atan2(2*xy, xx-yy)
    return (cos(theta), sin(theta))
```

### F2. U/Vラベリング（LabelDirOnGraphEdges）
```csharp
LabelDirOnGraphEdges(patchQuadIds, basisU,basisV, e1,e2):
    for each quad q in patch:
        for each link q->to in patch:
            d3 = normalize(center[to]-center[q])
            d2 = normalize( (dot(d3,basisU), dot(d3,basisV)) )
            a = abs(dot(d2,e1)); b = abs(dot(d2,e2))
            link.dir = (a>=b) ? U : V
```

---

## G. 方向指定ストリップ抽出（ExtractStripsByDirection）
dirリンクだけを辿り、分岐（次数>=2）で切る。端点（次数<=1）から開始し、最後にループを拾う。

```csharp
ExtractStripsByDirection(patch, dir):
    for each quad q in patch:
        if DirDegree(q,dir)<=1:
            strip = FollowDirStrip(q,dir)
            collect
    collect remaining loops
```

---

## H. 「片方向だけ」間引く（ストリップ選択）
Patchごとに U/V のどちらを消すか選ぶ：
- strip数が多い方を消す、など簡易でよい

ストリップを「直交方向」のキーで並べ、1本おきに選ぶと均一になる。

```csharp
dirRemove = (stripsU.Count>=stripsV.Count)?U:V
strips = strips[dirRemove]
Sort strips by StripKey( averageCenter projected onto perpendicular axis )
selected = EveryOther(strips)
targets += BuildTargetsFromStrips(selected)
```

---

## I. Target→ReplaceOp（tri4→tri2置換）
### I1. GetOuterVertices（外周4頂点を周回順で返す）
**最重要：外周エッジ（出現回数1）から外周ループを復元し、共有エッジ端点を除去して4点にする。**

```csharp
GetOuterVertices(quadA, quadB, sharedEdge):

    patchTris = tri4枚（quadA2枚 + quadB2枚）
    edgeCount = Dict<UndirectedEdge, int>()

    for tri in patchTris:
        Count (v0,v1), (v1,v2), (v2,v0)

    boundaryEdges = edges with count==1
    boundaryAdj = vertex->neighbors on boundaryEdges

    loop = TraceLoop(boundaryAdj) // 6頂点になる想定
    remove sharedEdge endpoints s0,s1 from loop
    reduced should be 4 vertices, still in cyclic order
    return reduced
```

### I2. Winding一致（表裏合わせ）
```csharp
BuildAddTrianglesWithConsistentWinding(outer4, qA,qB):
    add6 = [v0,v1,v2, v0,v2,v3]
    nNew = normal(add6 first tri)
    nOld = normalize(sum of removed tri normals)
    if dot(nNew,nOld)<0:
        add6 = [v0,v2,v1, v0,v3,v2] // 反転
    return add6
```

### I3. UV裏返り/潰れチェック（UvFlipsOrDegenerate）
基準符号を remove4 のうち「十分面積のあるUV三角」から取り、
追加2三角のUV符号が一致し、面積がeps以上であることを確認。

```csharp
SignedArea2(u0,u1,u2) = (u1-u0) x (u2-u0)

UvFlipsOrDegenerate(op):
    refSign = sign of first good removed tri area
    for each added tri:
        area = SignedArea2(uv[a],uv[b],uv[c])
        if abs(area) < epsArea: return true
        if sign(area) != refSign: return true
    return false
```

epsAreaはパッチ内のUV面積最大値×1e-4等で相対化。

### I4. ReplaceOp生成
```csharp
BuildReplaceOp(target):
    if !GetOuterVertices(...): false
    add6 = BuildAddTrianglesWithConsistentWinding(...)
    op.remove4 = triStart of qA/qB の4tri
    op.add6 = add6
    if UvFlipsOrDegenerate(op): false
    return true
```

---

## J. 衝突排除（triStart/quadId）
同じtriを二重にremoveしない。同じquadPairを二重使用しない（安定化）。

```csharp
FilterOps_NoTriNoQuadConflict(ops):
    usedTriStarts=set
    usedQuads=set
    accepted=[]
    for op in ops (score降順):
        if op.quadAId in usedQuads or op.quadBId in usedQuads: continue
        if any r.i0 in usedTriStarts: continue
        accept: add all triStarts + quadIds
    return accepted
```

---

## K. triangles再構築（安全な適用）
配列をその場で削除しない。**remove対象triStartを飛ばして新配列を作り、addを末尾追加**。

```csharp
RebuildTriangles(oldTris, removeSet, addList):
    new = []
    for i=0; i<oldTris.Length; i+=3:
        if i in removeSet: continue
        new.add(oldTris[i..i+2])
    for each add6 in addList:
        new.add(add6)
    return new
```

---

# パラメータ指針（トポロジ最優先）
- `normalAngleDeg`（QuadPair判定）：10〜20°（小さめ推奨）
- `hardAngleDeg`（hard edge）：20〜30°（小さめ＝保守的）
- `coplanarEpsDist`：`edgeLen * 1e-3` から開始、後半は小さく
- `posQuant`：`bboxSize * 1e-6` 〜 `1e-5`
- `uvEps/uvQuant`：`1e-6` 程度
- Multi-pass：`maxPass=5`、後半ほど厳しく（hard判定増・coplanar厳格化）

---

# 停止条件（完成の定義）
- `triCount <= targetTri`  
- 置換が0件（`accepted.Count==0`）  
- 置換数が急減（例：前回の30%未満）  
- `pass >= maxPass`

---

# 実装上の注意（事故ポイント）
1. **非多様体**（1エッジに3面以上）があると前提が崩れる → 弾くか修復が必要
2. GetOuterVerticesが失敗しやすいケース：
   - 2×1ではなくT字接続
   - boundaryEdgesが単純ループでない（穴/自己交差）
3. outer4の順序が蝶ネクタイ（自己交差）になる → 凸判定/追加チェックで弾く
4. strip抽出で分岐を無理に繋がない（トポロジ優先）
5. 1パス後に隣接は必ず再構築（更新差分は面倒なので再構築が安全）

---

# 最小テスト計画
- 平面グリッド（完全quad）：
  - 1パスで縦or横方向が均等に半減すること
  - seam無し/hard無しで最大限進むこと
- シームありグリッド：
  - seam越えのcollapseが起きないこと
- 曲面（緩い曲率）：
  - hardAngle/normalAngleで保守的に止まること
- triポール（極）を含むメッシュ：
  - tri周辺が残り、quad格子が崩壊しないこと

---

# 参考：Target生成（Patch + U/V方式）
> 実装者が迷いがちな箇所なので明示しておく。

```csharp
BuildTargets_PatchUVDir(m, quadGraph, patch):

    nPatch = AvgNormalOfPatch(patch)
    BuildPlaneBasis(nPatch, out basisU, out basisV)
    (e1,e2) = PrincipalAxes2D(patch.centers projected)

    LabelDirOnGraphEdges(patch, basisU,basisV,e1,e2)

    stripsU = ExtractStripsByDirection(patch,U)
    stripsV = ExtractStripsByDirection(patch,V)

    dirRemove = ChooseRemovalDir(stripsU,stripsV)

    strips = (dirRemove==U)?stripsU:stripsV
    Sort strips by StripKey along perpendicular axis
    selected = SelectEveryOtherStrip(strips)

    return BuildCollapseTargetsFromStrips(selected, dirRemove)
```

---

## ここで完結する理由（終点）
本仕様は「quad格子を保つ」ために、操作を **quad列間引き（tri4→tri2置換）** に限定している。  
これ以上の削減（全体最適化や再配線、UV再展開）は別カテゴリ（Remeshing/QEM/Reparameterization）であり、トポロジ最優先の要件と衝突しやすい。

以上で「仕様としての終点」とする。

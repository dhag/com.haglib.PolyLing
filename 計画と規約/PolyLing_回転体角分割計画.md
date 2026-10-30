# PolyLing 回転体 角分割（ベベル）計画

作成: 2026-10-31
完了: 2026-10-31

## 目的

回転体で周方向の分割数を少なくすると角柱になる（4 分割なら四角柱）。
このとき、角を少しだけ面取りした角柱を作れるようにする。

方法: 各分割角 θ を θ−a と θ+a の 2 本に割る。
例: 4 分割、a=5° → −5, 5, 85, 95, 175, 185, 265, 275。

## 実装前の状態（コード確認済み）

- `RevolutionMeshGenerator.GenerateRevolution` / `GenerateSpiral` は、角度を `r * 2π / RadialSegments`（0 始まりの等分）で固定して生成していた。
- `RevolutionParams` には、角度をずらすパラメータも分割するパラメータもなかった。

## 変更計画

### 1. RevolutionParams.cs
`Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/RevolutionParams.cs`

- `float CornerSplitAngle` を追加する（単位は度、既定値 0）。
- 範囲定数 `CornerSplitAngleMin = 0`、`CornerSplitAngleMax = 60` を追加する（60 は `RadialSegmentsMin = 3` のときの半ステップ）。
- `PLParam(TextKey = "CornerSplitAngle", Description = "各分割角を ±この角度の 2 本に割って角を面取りする（度）。0 で従来どおり")` を付ける。
- `Default` と `Equals` にこの項目を加える。

### 2. RevolutionMeshGenerator.cs
`Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/RevolutionMeshGenerator.cs`

- 周方向の角度列（ラジアン）を返す関数を 1 つ追加する。
  - a = 0 のとき: `r * step`（r = 0..N）。従来どおり列数は N+1。
  - a > 0 のとき: 各 θ_r について θ_r−a と θ_r+a を並べ、最後は 2π−a で閉じる。周方向の頂点数は 2N、継ぎ目用に列数は 2N+1。
  - a は隣の稜線と重ならないよう、半ステップ未満に制限する。
- `GenerateRevolution` で `r * angleStep` と `radialSeg` に依存しているループ（側面の頂点、四角形、上下キャップ）を、角度列で回すように直す。シグネチャには角分割角を加えるか、角度列を渡す形にする。呼び出し元は `Generate` だけ（確認済み）。
- `GenerateSpiral` も同じ角度列を巻き数ぶん繰り返して使う。上がり幅は角度に比例させる（`angle / 2π * SpiralPitch`）。終端キャップの角度には角度列の最後の値を使う。
- UV の u は「列番号 ÷ (列数−1)」で振る。

### 3. パネル UI
`Runtime/Poly_Ling_Player/View/PrimitiveMesh/PlayerPrimitiveMeshSubPanel.Revolution.cs`

- `BuildRevolutionUI` の「円周分割数」行の下に `SR(T("CornerSplitAngle"), Min, Max, get, set+D())` の行を追加する。
- CSV 読込（`result.Success` ブロック）で、読んだ値を `_revP.CornerSplitAngle` に反映する。

### 4. 表示テキスト
`Runtime/Poly_Ling_Player/View/PrimitiveMesh/PrimitiveMeshTexts.cs`

- キー `CornerSplitAngle` を追加する: ja「角の分割角」、en「Corner Split」。hi は ja と同じにする。

### 5. CSV
`Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/RevolutionCSVIO.cs`

- `CSVLoadResult` に `float CornerSplitAngle` を追加し、初期値は currentParams から取る。
- `Save` で `$cornerSplitAngle=` を書き出す。
- `ParseParameter` に `cornersplitangle` を追加する。

## 確認手順

1. コンパイルし、エラーがないことを確認する。
2. Play で図形生成パネルの回転体を開く。
3. 円柱プロファイル、RadialSegments=4、CornerSplitAngle=5 で生成し、面取りした四角柱になっていることをキャプチャで確認する。
4. CornerSplitAngle=0 で従来と同じ形状（頂点数 = (N+1)×点数 + キャップ）になることを確認する。
5. Spiral=ON でも破綻しないことを確認する。
6. CSV に保存し、読み込み直して値が戻ることを確認する。

## 対象外

- 角を丸めること（1 つの角を 3 本以上に割る）。必要になったら、角分割数パラメータを追加して拡張する。

## 結果（2026-10-31）

- 1〜5 は計画どおりに実装した。
  - `GenerateRevolution` のシグネチャには `float cornerSplitDeg` を加えた。
  - 角度列の関数は `BuildRadialAngles(int radialSeg, float cornerSplitDeg)` とした。上限は半ステップ × 0.999。
  - らせんの下キャップも、始端の角度と上がり幅に合わせた。
- PolyLing.Runtime.csproj のコンパイルと Unity の再読込で、エラーは 0 件だった。
- `createRevolution` での確認結果:
  - 4 分割、a=5: 面 24。底面の頂点角度は −5, 5, 85, 95, 175, 185, 265, 275°（getRawData で確認）。
  - 4 分割、a=0: 頂点 10、面 12。従来と同じ。
  - らせん（4 分割、a=20、2 巻き、断面 4 点ループ）: 頂点 70、面 72、穴 0。TOP ビューで八角形の断面になることを確認した。
- 未確認: パネル UI からの操作と、CSV の保存・読込。回転体 CSV の入出力はパネル専用で、コマンドがない。

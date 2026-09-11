# PolyLing コマンド追加の手引き

新しい `PanelCommand` を 1 本足すときの手順と、MCP の道具一覧（JSON Schema）へ
自動で載るための条件をまとめる。

対象読者はこのパッケージを触る開発者。MCP 側の仕様には踏み込まない。

---

## 1. 手順

コマンドを 1 本足すときは次の 6 点を踏む。

| # | 場所 | 内容 |
|---|---|---|
| 1 | `Core/Data/PanelCommand.cs` | クラスに `[PLCommand(Description = "…")]`、全プロパティに `[PLParam]`、返すものがあれば `[PLResult]` |
| 2 | `Poly_Ling_Player/View/Core/PlayerCommandDispatcher.cs` | `Func<T, string>` のフック宣言と `switch` の `case` |
| 3 | `Poly_Ling_Player/View/Core/PolyLingPlayerViewerCore.CreateCommands.cs` | `Execute*` の受け口と、`_commandDispatcher.OnXxx = ExecuteXxx;` の配線 |
| 4 | `Poly_Ling_Remote/RemoteOwnership.cs` | `case` を足して所有権判定に載せる |
| 5 | 発行側 | パネルまたはツールハンドラから `SendCommand` |
| 6 | 検証 | 左ペイン「システムデバッグ → コマンド定義の検査」で **検査する** |

**スキーマ生成器への登録は要らない。**
`PLParamAudit.FindCommandTypes()` がアセンブリを走査して `PanelCommand` の
具象型を全部拾うので、条件を満たしていれば次回の `tools/list` に自動で載る。

### 受け口の形

失敗理由を文字列で返す。成功時は `null`。

```csharp
private string ExecuteXxx(XxxCommand cmd)
{
    if (cmd == null) return "コマンドが null";
    var h = _xxxHandler;
    if (h == null) return "Xxx ハンドラがありません";
    return h.ExecuteFromCommand(cmd, out string reason) ? null : reason;
}
```

ディスパッチャ側の `case` はこの形にそろえる。

```csharp
case XxxCommand c:
{
    if (model == null) { Fail("no current model"); return; }
    if (OnXxx == null) { Fail("xxx handler not wired"); return; }
    string xxReason = OnXxx.Invoke(c);
    if (xxReason != null) { Fail(xxReason); return; }
    return;
}
```

`?.Invoke` は使わない。未配線が無言で通ってしまう。

---

## 2. 道具として載るための条件

3 つある。どれか 1 つでも破れると、そのコマンドは道具一覧に出ない。

### 条件 1: コンストラクタ引数名がプロパティ名と一致していること

`PanelCommandFactory.FindProperty`（`PanelCommandFactory.cs:316`）が
**引数名でプロパティを引く**。大文字小文字は無視するが、綴りが違うと引けない。

```csharp
// 通らない
public CreateCubeCommand(int modelIndex, CubeParams prms, ...)   // プロパティは Params
public SetBoneTransformValueCommand(int modelIndex, ..., Field field, ...)  // プロパティは TargetField

// 通る
public CreateCubeCommand(int modelIndex, CubeParams @params, ...)
public SetBoneTransformValueCommand(int modelIndex, ..., Field targetField, ...)
```

**いちばん踏みやすい。** 過去に 31 本がこれで通っていなかった。
`params` のような予約語は逐語識別子（`@params`）でよい。

### 条件 2: 引数の型が対応表にあること

| 分類 | 型 |
|---|---|
| 基本 | `string` / `int` / `float` / `bool` / `ulong` |
| ベクトル | `Vector2` / `Vector3` / `Vector2Int` / `Vector3Int` |
| 列挙 | 任意の `enum` |
| 配列 | `int[]` / `float[]` / `bool[]` / `ulong[]` / `string[]` |
| 配列（平坦化） | `Vector2[]` / `Vector3[]` / `VertexPair[]` |
| 入れ子 | `PLParam` の付いた書き込めるメンバーを持つ構造体・クラス |

### 条件 3: 全プロパティに `PLParam` が付いていること

付いていないと `Create` が「PLParam が付いていないので外から与えられない」で失敗する。
形状に関係しない値（プレビューの視点角、UI の連動トグルなど）は `Ignore = true`。

**`Ignore` を付ける引数には、コンストラクタの既定値も必要。**
`Create` は `Ignore` の引数を既定値で埋めるので、既定値が無いと必ず失敗する。
`AddGeneratedMeshCommand.Mesh` がこれに当たり、意図的に道具から外れている。

---

## 3. 検査

左ペイン「システムデバッグ → コマンド定義の検査」→ **検査する**。
中身は `PanelCommandFactoryAudit.RunAll()`（`PanelCommandFactoryAudit.cs:214`）。

MCP からは `polyling_call queryCommandAudit` で同じものを回せる。
戻り値の `report` に全文、`toolsUsable` / `toolsSkipped` に数が入る。
モデルもプロジェクトも見ないので、何も読み込んでいない状態でも動く。

```
[PLParamAudit] コマンド N / 対象プロパティ N / 付与済み N / 付け忘れ 0 / 算出につき対象外 N
[PanelCommandFactoryAudit] コマンド N / action 衝突 0 / 引数の対応なし 0 / PLParam 付け忘れ 0 / 未対応の型 0
[PanelCommandSchema] 道具として出せた N / 出せなかった M
[PLCommand] 説明が無い道具 0
```

**すべて 0 なら通っている。** 0 でない項目には該当コマンド名が並ぶ。

同じ画面の **スキーマを書き出す** で、道具一覧を JSON として作業フォルダへ落とせる。
書き出し先は `PLSandbox` の関門を通るので、作業フォルダ（左ペイン「その他 → 作業フォルダ」）
が未設定だと拒否される。

---

## 4. 戻り値を返す

コマンドが結果の値を返すときは 2 つ。

| # | 場所 | 内容 |
|---|---|---|
| 1 | `Core/Data/PanelCommand.cs` | クラスへ `[PLResult("キー", PLResultKind.…)]` を返す項目の数だけ |
| 2 | ディスパッチャの `case` | `ReportData(CommandDataJson.New()….Build())` |

`PLResult` を 1 つも付けなければ `outputSchema` は出ない。
戻り値を返さないコマンドは何も足さなくてよい。

### 何を返してよいか

**番号列・座標列は返さない。** 件数と要約だけを返す。
量のあるものは `ModelContext.DataStore`（結果辞書）へ書き、
戻り値には名前・種類・件数・要約だけを載せる。

**例外は生データの 2 本だけ。** `getRawData` / `setRawData` は座標列・番号列を
そのまま運ぶ。新しいコマンドをこの仲間に入れないこと。
量のあるものが要るなら、まず結果辞書へ書けないかを考える。

結果辞書の種類は 3 つに限る。任意 JSON を入れると出力スキーマが組めなくなる。

| 種類 | 中身 |
|---|---|
| `IndexSet` | `PartsSelectionSet` をそのまま |
| `LoopSet` | 頂点列＋重心 |
| `ValueSet` | 名前と数値 or 文字列 |

### 安定 ID は文字列で返す

`ObjectId` は `DateTime.UtcNow.Ticks` から採番するので 10^17 台になる
（`ObjectIdAllocator.cs:32`）。`double` の整数表現の上限 2^53 を超えるため、
数値で返すと丸められる。`PLResultKind.Text` と `CommandDataBuilder.Text` を使う。
`FileInfo.Length`（`long`）も同じ理由で文字列にする。

### 既に対象を報告しているコマンド

生成系は受け口が `ReportTargets` で masterIndices と安定 ID を報告済み。
そこへ `ReportData` を呼ぶと対象が消える。`ReportDataKeepingTargets` を使うこと。

### `PLResult` は継承される

`Inherited = true`。図形生成のように基底 1 つが全 27 種の受け口を兼ねる系統は、
基底へ 1 度書けば全具象へ効く。`PLCommand` を `Inherited = false` にしてあるのは
説明文が 1 本ずつ違うためで、戻り値の形はそろっているので扱いを変えている。

### 可変長のものは 2 本で持つ

入れ子の配列は送れない。`JsonParser.ParseFlat` が `[` で始まる値を捨てる
（`RemoteProtocol.cs:227`）。配列は `"1,2,3"` の文字列 1 個で送ること。

そのため、頂点ごとに個数が違うようなものは「個数の列」と「連結した値の列」の
2 本に分ける。取得側も同じ形にそろえる。

```
uvCounts / uvs           頂点ごとのスロット数 と u,v を 2 個ずつ連結
faceSizes / faceVertices 面ごとの頂点数 と 頂点番号の連結列
```

飛び飛びの対象を返すときは、元の番号の列（`vertexIndices` / `faceIndices`）も
必ず載せる。無いと呼び出し側が書き戻せない。

### 応答のキーは `result`

`PolyLingEditorControlServer.HandleCall` が `KeyRaw` で `"result"` に入れる。
`"data"` は `ping` / `state` / `play` / `stop` が使う**文字列**の欄で、
クライアントが `GetString()` で読むため、オブジェクトを入れると例外で落ちる。

---

## 5. 型の対応表を増やすとき

新しい型を扱えるようにしたいときは、**3 か所を必ず一緒に直す**。
片方だけ変えると往復しなくなるか、スキーマと実装が食い違う。

| 表 | 場所 | 役割 |
|---|---|---|
| `TryParse` | `PanelCommandFactory.cs:364` | 文字列 → 値 |
| `TryFormat` | `PanelCommandFactory.cs:583` | 値 → 文字列 |
| `TryJsonType` | `PanelCommandSchema.cs:280` | JSON Schema の型名 |

加えて `IsDirectlyParsable`（`PanelCommandFactory.cs:350`）を同じ集合に合わせる。
これは「入れ子として展開すべきか」の判定に使う。

**判定を別の場所に書き写さないこと。** 4 つ目の表になり、必ずずれる。
実際 `PanelCommandFactoryAudit.IsSupported` が `Vector2` / `Vector3` を
入れ忘れていて、使える型を「未対応」と報告していた。
今は `PanelCommandFactory.IsSchemaRepresentable`（`PanelCommandNested.cs:79`）
の 1 本だけを見る形にしてある。

---

## 6. 型を選ぶときの目安

### 構造体はそのまま持ってよい

フィールドに `PLParam` を付けておけば、**入れ子として自動で展開される**。
手で平坦化する必要はない。

キーはプロパティ名を頭に付けたドット区切りになる。

```
params.widthTop
params.subdivisions
placement.worldPosition
params.loop.width        ← 2 段以上も同じ規則
```

深さの上限は 3（`PanelCommandNested.cs:44`）。

判定は `IsNestedType`（`PanelCommandNested.cs:88`）。
インターフェース・抽象型・`UnityEngine.Object` 派生・`System.*` は除かれる。
既定値は `static Default` があればそれを、無ければ引数なしコンストラクタを使う。

### 要素が構造体の配列は避ける

`ReorderEntry[]` / `BlendSourceSpec[]` / `BeltCsvEntry[]` / `LoopData[]` のような
**要素が構造体の配列は入れ子走査の対象外**。1 個の値しか組み立てられないため。

設計の時点で平行配列にしておくと、後で開き直す手間が省ける。

```csharp
// 固定長の組が並ぶとき
public int[] EntryValues { get; }        // 3 個ずつ

// 可変長の列が並ぶとき（SkinWeightPaintCommand.StepStarts と同じ形）
public float[] LoopPointValues { get; }  // 全ループの点を連結
public int[]   LoopStarts      { get; }  // ループ i の開始点番号。単調増加
public bool[]  LoopIsHole      { get; }
```

元の型は算出プロパティとして残せば、受け口は書き換えずに済む。
逆向きの変換は `static` メソッドで足す（`SplitBelts` / `SplitLoops` / `SplitMapping` ほか）。

### コンストラクタの多重定義はしない

`PickConstructor`（`PanelCommandFactory.cs:291`）は引数の多い方を選ぶだけで、
**同数のときの順序が決まっていない**。外から使われるコンストラクタが不定になる。

型の変換が要るなら、多重定義ではなく `static` の変換補助を足す。

```csharp
// これはしない
public SetMaterialColorCommand(int modelIndex, int slotIndex, Color baseColor)
public SetMaterialColorCommand(int modelIndex, int slotIndex, float[] baseColorRgba)

// こうする
public static float[] ToRgba(Color c) => new[] { c.r, c.g, c.b, c.a };
```

### 外から送るものでない引数

`MeshObject` のような「同じプロセスの中で実体を手渡しするだけ」の引数は
`Ignore = true` を付け、コンストラクタに既定値を持たせる。
道具一覧から外れるのが正しい。

### 設定型に `Ignore` を付けた入力パスと `List<string>` はコマンドが持つ

`PMXExportSettings` / `MQOImportSettings` / `Vrm10ExportSettings` のような設定型を
そのまま引数にすると、`Ignore = true` を付けたメンバーは外から渡せない。
渡す必要があるものは**コマンド側が別の引数として持ち、受け口で設定へ詰め替える**。

対象は 2 種類ある。

| 種類 | 理由 | コマンド側の型 |
|---|---|---|
| 入力パス | 関門（`PLSandbox`）を通してから入れる必要がある | `string` |
| `List<string>` | `TryParse` の対応表に無い | `string[]` |

```csharp
// コマンド
[PLParam(Description = "部分差し替えの元 PMX のパス。空にすると通常の書き出し")]
public string SourcePmxPath { get; }

// 受け口
if (!string.IsNullOrEmpty(cmd.SourcePmxPath))
{
    if (!PLSandbox.TryResolveRead(cmd.SourcePmxPath, out string srcPath, out string srcReason))
        return srcReason;
    settings.SourcePMXPath = srcPath;
}
```

**入力パスを素通しで設定へ入れないこと。** 関門を迂回できてしまう。

実例は `ExportPmxFileCommand`（`SourcePmxPath` / `ReplaceMaterialNames`）、
`ImportMqoFileCommand`（`BoneWeightCsvPath` / `BoneCsvPath`）、
`ExportVrmFileCommand`（`Authors`）。

### 入れ子の既定値は引数なしコンストラクタ

コマンドのコンストラクタに書いた `settings ?? Xxx.CreateDefault()` は、
**MCP 経路の既定値にはならない。** `CreateNestedDefault`（`PanelCommandNested.cs:131-143`）が
`static Default` か引数なしコンストラクタを使うので、効くのはフィールド初期化子のほう。

`??` が効くのは、同じプロセス内から `null` を渡して呼んだときだけ。

```
exportMqoFile の Scale
  パネルの既定   0.01   （CreateFromCoordinate(0.01f, ...)）
  MCP の既定    100     （MQOExportSettings.Scale = 100f の初期化子）
```

パネルの既定値と揃えたいなら、**フィールド初期化子のほうを直す**。
コマンドのコンストラクタに書いても外からは見えない。

---

## 7. 名前

### 道具名

型名から自動で作る。`ActionOf`（`PanelCommandFactory.cs:90`）。

```
SmoothEdgesCommand → smoothEdges
```

規則から外したいときだけ `ActionAliases`（`:59`）に 1 行。

### 引数名

プロパティ名を camelCase にしたもの。`KeyOf`（`:325`）。

**頭字語は崩れる。** `Camel` は先頭 1 文字しか小文字にしない。

```
UVIndices → uVIndices   ← 正しくない
```

このときだけ `ParamAliases`（`:75`）に 1 行足す。

```csharp
["ApplyUVChangesCommand.UVIndices"] = "uvIndices",
```

`Camel` の規則そのものは変えない。道具名も同じ関数を通るので、
190 本の名前が一斉に変わる。

---

## 8. 説明文の書き方

### コマンドの説明（`PLCommand`）

- 1 行で、その道具が何をするかを述べる
- 前提（選択が要る・作業フォルダが要る等）は 2 文目に短く添える
- **実装の都合は書かない。** どのクラスが正典か、どの経路を通るかは呼び出し側に関係がない

```csharp
// よくない
[PLCommand(Description = "面の裏表を反転する。実処理は FlipFaceTool。")]

// よい
[PLCommand(Description = "面の裏表を反転する。")]
```

### 引数の説明（`PLParam`）

- 型と食い違わせない。`Vector3` は数値 3 個の配列として出るので、
  説明で `"x,y,z"` のような文字列書式を語らない
- `null` と書かない。JSON からは送れない。「省くと〜」「空にすると〜」と書く
- 平坦化した配列は詰め方を書く（「x,y,z の順に 3 個ずつ並べる」）
- 上下限は `Min` / `Max` / `Step` か `LimitKey` に書く。説明文に数値を書かない

---

## 9. コードを機械的に挿し込むときの注意

過去に 2 度踏んだ誤りがある。どちらも構文としては正しいので、
構文検査（tree-sitter）では拾えない。

### 波括弧の無い制御文の直後に文を足すとスコープが変わる

```csharp
// 前
if (mapped > 0)
    SendCommand(new ApplyHumanoidMappingCommand(idx, mapping));

// 1 文を 2 文にすると、2 文目が if の外へ出る
if (mapped > 0)
    SplitMapping(mapping, out var names, out var idxs);   // ← if の本体
    SendCommand(new ApplyHumanoidMappingCommand(idx, names, idxs));  // ← 外
```

挿入前に、直前行が波括弧の無い制御文でないかを確認する。

### 置換が説明文まで波及する

`Description` 内の文言を一括置換すると、`///` の C# 向け説明文まで変わることがある。
`null` の扱いなどは、JSON 向け（`Description`）と C# 向け（`///`）で正しい表現が違う。

置換後に、対象が `Description = ` の行だけに現れることを確認する。

---

## 10. 現状（2026-09-09 実測）

| 項目 | 値 |
|---|---|
| 具象 `PanelCommand` | 245 |
| 道具として出せる | 244 |
| 出せない | 1（`AddGeneratedMesh`。外から送るものではないので正しい） |
| `outputSchema` が付く道具 | 52 |
| `PLParam` 未付与 | 0 |
| `PLCommand` 未付与 | 0 |
| action 衝突 / 引数の対応なし / 未対応の型 | いずれも 0 |
| `PlayerCommandDispatcher` の `Fail()` | 320（2026-09-04 時点。未再計測） |
| 同ファイルの無言 `return;` | 22（同上。成功扱い 2 + 後処理ヘルパー 20） |

道具の数は `polyling_tools`（MCP）、左ペイン「システムデバッグ → コマンド定義の検査」、
または `polyling_call queryCommandAudit` で数え直せる。
この表を書き換えるときは実測値を使うこと。

---

## 11. 関連ファイル

| ファイル | 役割 |
|---|---|
| `Core/Data/PanelCommand.cs` | コマンドの定義 |
| `Core/Data/PLParamAttribute.cs` | 引数のメタデータ |
| `Core/Data/PLCommandAttribute.cs` | コマンドの説明 |
| `Core/Data/PanelCommandFactory.cs` | 文字列 → コマンド、コマンド → 文字列 |
| `Core/Data/PanelCommandNested.cs` | 入れ子のドット区切り展開 |
| `Core/Data/PanelCommandSchema.cs` | JSON Schema の生成 |
| `Core/Data/PLParamAudit.cs` | `PLParam` 付け忘れの検査 |
| `Core/Data/PanelCommandFactoryAudit.cs` | 往復検査・網羅検査・まとめて回す `RunAll` |
| `Core/Config/PLSandbox.cs` | ファイル入出力を作業フォルダの下へ閉じ込める関門 |
| `Poly_Ling_Player/View/Core/PlayerCommandDispatcher.cs` | コマンドの振り分け |
| `Poly_Ling_Player/View/SubPanels/Common/PlayerCommandSchemaSubPanel.cs` | 検査と書き出しの UI |

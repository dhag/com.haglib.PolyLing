# MCP ベンチマーク課題

MCP の 3 条件（`generic` / `current` / `optimized`）を、同じ課題・同じ計測で比べるための手順。
条件の中身は `MCP操作手順書.md` 1-4 節、サーバの起動引数は `PolyLingMcpServer --help`（引数なし起動で表示）。

---

## 1. 条件

| 条件 | 起動引数 | 見え方 |
|---|---|---|
| generic | `--profile generic` | 全コマンドが 1 本ずつ `pl_<コマンド名>` の道具 |
| current | `--profile current` | `polyling_tools` で全件取得 → `polyling_call` |
| optimized | `--profile optimized` | `polyling_search` → `polyling_describe` → `polyling_call` |
| optimized＋利用シーン | `--profile optimized` ＋ WebClient で利用シーンを選ぶ | 上に加えて道具と検索範囲を利用シーンで絞る |

3 条件とも同じサーバ・同じ Unity で動き、違うのは道具の見せ方・運用指示（instructions）・
`optimized` での共通道具の既定値（`MCP操作手順書.md` 1-4 節）だけ。
条件は計測ログと CSV の `server` 列（`profile=…`）で区別できる。

### 条件とは別の要因（WebClient の切り替え）

サーバの条件と掛け合わせて測れる。CSV の `cache` / `compactK` 列に残る。

| 切り替え | 中身 |
|---|---|
| キャッシュ | Tool 定義・system・履歴の末尾に `cache_control` を付ける。効いた分は `cacheReadTokens` に出る |
| 履歴圧縮 K | 直近 K 往復より古い道具の結果を「何バイト省いたか」の 1 行にして送る。画面の表示は変えない |

まず両方オフで 3 条件を測り、次に同じ課題で切り替えを 1 つずつ入れる。
一度に 2 つ変えると、どちらが効いたか分からない。

---

## 2. 課題

依頼文は**利用者の言葉で書き、コマンド名を含めない。** コマンド名を書くと、
探す手間が要らなくなり条件の差が消える。依頼文は毎回そのまま貼る。

| # | 課題名 | 依頼文 | 成功条件（計測者が確かめる） |
|---|---|---|---|
| T1 | 照会 | いま読み込んでいるモデルに、オブジェクトがいくつあり、それぞれ何という名前かを一覧で教えて。 | 報告した数と名前が、計測者が別に取ったモデル構造と一致する |
| T2 | 生成と検証 | 新しい直方体を 1 つ作り、その頂点数と面数を数値で確かめて報告して。 | 直方体が 1 つ増えている。報告した頂点数・面数が、計測者が別に取った値と一致する |
| T3 | 手本の実行 | 保存してある手本「〈手本名〉」を実行し、各段が成功したかを報告して。 | 手本が最後まで実行され、報告した各段の成否が実行結果と一致する |
| T4 | ソース修正 | `Packages/com.haglib.polyling/Runtime/Poly_Ling_Main/Core/Data/PLCommandAttribute.cs` の先頭に 1 行コメントを足し、Unity でコンパイルが通ることを確かめてから、そのコメントを消して元に戻して。 | ファイルが元と同一。コンパイル成功を Unity 側で確かめたと報告している |
| T5 | 画面確認 | 右ペインにカメラのパネルを表示して、画面を撮り、表示されていることを確かめて。 | 撮った画像にカメラのパネルが写っている |

- T3 の〈手本名〉は計測を始める前に 1 つ決め、全条件で同じものを使う
- 課題の成否は AI の完了報告ではなく、上の成功条件で計測者が判定する

---

## 3. 初期状態

各課題の前に次をそろえる。そろわないと条件ではなく状態の差を測ることになる。

- 同じプロジェクトファイルを読み込んだ状態から始める（T2 は前回の直方体が残っていないこと）
- Unity は Play 中、前面、Run In Background オン
- WebClient のモデル名・`max_tokens`・料金単価を全条件で同じにする
- T4 は対象ファイルが元の内容であること

---

## 4. 手順

1. サーバを条件の引数で起動する。計測ログは条件ごとに別ファイルにする。

   ```
   PolyLingMcpServer.exe --project <Unity プロジェクト> --editable Packages/com.haglib.polyling --http <port>
                         --profile <条件> --metrics <保存先>/<条件>.jsonl
   ```

   `generic` は接続時に Unity から道具一覧を取るので、**Unity を起動してから** WebClient で接続する。
2. WebClient（`http://127.0.0.1:<port>/`）で「MCP サーバに接続」。状態行に `profile=<条件>` が出ることを確かめる
3. 「課題名」に `T1` などを入れ、「課題開始」（会話が消え、以後の往復がこの課題に記録される）
4. 依頼文を貼って送信。AI が終わるまで口を挟まない。追加の指示が要ったらその回数を別に控える（人の介入）
5. 成功条件で判定し、「成功で終了」か「失敗で終了」
6. T1〜T5 を終えたら「計測 CSV を保存」
7. 条件を変えてサーバを起動し直し、2 から繰り返す

条件の順番は毎回入れ替える（同じ順だと、前の条件の作業が Unity の状態に残る影響が偏る）。
繰り返し回数は全条件で同じにする。

---

## 5. 記録と集計

記録は 2 か所にあり、`taskId` で突き合わせる。

| 記録 | 場所 | 1 行 |
|---|---|---|
| WebClient の CSV | 保存した `polyling-metrics-*.csv` | Claude API の 1 往復（`round`）、課題の開始（`task_start`）・終了（`task_end`） |
| サーバの計測ログ | `--metrics` で指定した JSONL | `tools/list` 1 回（`kind=list`）、`tools/call` 1 回（`kind=call`） |

WebClient 自身が行う呼び出し（利用シーン一覧の取得）は `taskId` が空で、課題には数えない。

| 指標 | 出し方 |
|---|---|
| Cost / Successful Task | `task_end` の `costUsd` を `outcome=success` の課題で平均 |
| Input Tokens / Task | `task_end` の `inputTokens`（`cacheReadTokens`・`cacheWriteTokens` も併記） |
| LLM Rounds / Task | `task_end` の `round` |
| MCP Calls / Task | JSONL の `kind=call` を `taskId` ごとに数える |
| Tool Result Bytes / Task | JSONL の `kind=call` の `resultBytes` を `taskId` ごとに合計 |
| Tool 定義の固定費 | CSV の `toolsDefBytes`（毎往復送る道具定義）と JSONL の `kind=list` の `resultBytes` |
| 1 往復の送信量 | CSV の `requestBytes`（履歴圧縮の効果はここに出る） |
| Wall Time / Task | `task_end` の `wallMs` |
| Retry | JSONL の `ok=false` の件数 |
| Human Interventions / Task | 手順 4 で控えた回数 |
| Scenario Reuse Rate | JSONL の `polylingCommand` が `runScenario` の課題数 ÷ 手本を使えた課題数（使えたかは計測者が判定する） |
| 成功率 | `task_end` の `outcome` |

コストだけが下がり成功率が下がる条件は、改善とみなさない。

### サーバでの集計

計測ログはサーバ自身で集計できる（MCP サーバとしては起動しない）。

```
PolyLingMcpServer.exe --summarize-metrics <保存先>\generic.jsonl <保存先>\current.jsonl <保存先>\optimized.jsonl > 集計.md
```

コマンド別・道具別・課題別の表が出る。コマンド別の表は、影響・危険性の値付けを使われた順に進めるときの順番に使う。

サーバを `--metrics-search` 付きで起動しておくと、`polyling_search` の問い合わせと上位の結果も記録され、
集計に「検索と、その後に実行したコマンド」の表が加わる。次に実行したコマンドが上位の結果に無かった行が、
タグや説明の見直し候補になる。問い合わせ文は利用者の言葉なので、既定では記録しない。

---

## 6. 注意

- Claude Desktop から使うと `taskId` と利用シーンを送れない。比較の計測は WebClient で行う
- 計測ログには量と成否だけを書く。依頼文・応答本文・画像は残らない
- `generic` では、引数名が Anthropic API の規則（`^[a-zA-Z0-9_.-]{1,64}$`）に合わないコマンドは道具に出ない。
  出なかったコマンドは計測ログの `kind=list` の `problem` に名前が出る

この MCP サーバは Unity Editor 上の PolyLing（3D メッシュ編集ツール）を操作する。以下は操作者に言われなくても守る前提。

## 自分でやる（操作者に頼まない）
- Play の開始・停止、スクリプトの反映、ボタン操作、画面の確認はすべて道具でできる。「押してください」「確認してください」と頼まない。
- 頼むのは、道具で届かないもの（MCP サーバ自身の再ビルド・再起動、書き込み許可フォルダの外のファイル削除など）だけ。そのときは理由と代わりの案を添える。

## Unity と PolyLing の起動
- Unity Editor 自体が起動していない（unity_focus が Unity プロセスを見つけられない）ときは unity_editor_launch で起動し、unity_state が応答するまで待ってから unity_play する。
- polyling_* と unity_capture は Play 中でないと動かない。止まっていたら unity_play → unity_state で確認する。
- unity_play 直後は数秒つながらない（ポートに接続できません）。unity_state をもう一度呼べば通る。
- 状態は unity_state（Play 中か・コンパイル中か）。

## 複数の構成を扱うとき（Editor とビルド版を同時に動かす）
- 送り先は道具の instance に構成名で指定する（Assets/PolyLingBuild/ビルド構成.json、無ければパッケージ既定。既定は editor と player）。省くと MCP サーバの --target のまま。
- リストクライアントだけの構成には MCP の受付が無い。polyling_call・unity_capture は届かない（起動・終了・前面化だけ使える）。
- 詳しくは MCP操作手順書.md の 1-6。

## スクリプトを直したとき
- 一発で確かめる：unity_validate_changes（Play を抜けて反映し、この間に出たコンパイルエラーだけ返す。restore_play_mode=true で Play に戻る）。
- 手動なら unity_stop → unity_refresh → read_unity_log(contains="error CS") → unity_play。
- 事前の軽い検査は compile（dotnet build）。
- Play を抜けるとモデルは消える。検証に要るモデルは Play に入り直してから読み込み直す。

## 結果を見る
- 見た目は unity_capture（target=MainView / TriView / Window）。数値より先に、見て確かめられるものは見る。
- 画像に矢印を描くのは uiCapture の cursor（control / position / mouse）。頂点を指すときは queryCapturePosition の x・y を position へ渡す。詳しくは MCP操作手順書.md の 2-3。
- ログは read_unity_log。操作の前に length=0 で next_offset を控え、後で offset に渡すと、その操作の分だけ読める。

## PolyLing のコマンド
- polyling_search（やりたいことを日本語で）→ polyling_describe（引数）→ polyling_call。
- polyling_call の args は平らな JSON。値はすべて文字列、配列は "1,2,3"、入れ子は "settings.xxx"。
- 照会コマンド（query*）の詳細は結果辞書に入り、応答には見出ししか出ない。中身は saveProjectCsv で保存し、モデルフォルダの datastore.csv を search_text で読む。
- ファイルの読み書きは作業フォルダ（プロジェクト直下）からの相対パス。

## 画面の場所を聞かれたら（「〇〇はどこ？」「どのボタン？」）
1. PolyLing_機能の場所.md を search_text で引く（言い換え列がある）。無ければ polyling_call uiDescribe panelId=leftPane の説明から探す。
2. 言葉で答える（左の折り畳み名 → ボタン名 → 右で選ぶもの）。
3. 画面でも示す：uiReveal controlId=leftPane.xxxBtn（折り畳みを開き赤枠）→ uiHighlight controlId=leftPane.fold.Xxx add=true（折り畳みの見出しにも枠）。
- ボタンを押すのは uiClick（安全度 safeWrite のものだけ。unspecified は押せない）。
- コマンドを画面で行うときの操作は queryCommandUiOperation（経路、入力欄と見出し、値の表記、uiSetValue に渡す値、実行のボタン）。画面の文字は想像で書かず、ここから読む。argBuilt が 0 の欄は、経路のとおりに開いてから読み直す。

## シナリオと確認済みの印、チュートリアルの依頼
- シナリオを使う・作る依頼では、最初に queryScenarios で探す。確認済み（reviewed が 1）が先に並ぶ。
- 確認済みのシナリオは、describeScenario の前提が今のモデルと合えばそのまま使い、順序を変えない。上書きしない。変えるなら forkScenario で派生させる（派生は未確認になる）。未確認のシナリオは正解として扱わない。
- シナリオパネルの「確認済みにする」（scenario.confirm）は人だけが押す。uiClick は拒否する。操作者に指示されない限り、コンピュータ操作でも押さない。ファイルへ #review 行を書かない。
- チュートリアルの MD は exportScenarioTutorialDraft → 読者向けに原稿を直す → buildScenarioTutorial → queryScenarioTutorialBuild → 残った画像を read_image で見る。画面の文字は想像で書かず差し込む。画面に入口が無ければ作らず報告する。埋め込み済みの MD は読まない。
- 報告には使ったシナリオ名を書き、確認済みでなければ「未確認」と明記する。
- 手順の全部は MCP操作手順書.md の 5-7。使うコマンドは利用シーン「ヘルプ・チュートリアル作成」。

## 既定の動きを変えるとき
- 1 つの入力から複数の出力を並列に出せること（はしご → ベルトとボーンなど）はこのアプリの設計の前提。個別の例や単純な形だけで判断しない。
- 一覧・選択・書き出し・適用の既定を変える前に、依存の形（並列・分岐・多段）で何も失わないことを確かめる。モディファイアを畳む判定は `ModifierResultOps.IsFolded` だけに置き、形は `ModifierFoldSelfTest`（`queryMeshModifierAudit`）へ足す。

## 文書（読むきっかけ）
- PolyLing_機能の場所.md … 機能・ボタンの場所を聞かれたとき。
- PolyLing_姿勢の規約.md … 姿勢・行列（WorldMatrix / BindPose / BindWorldMatrix / BoneTransform / ポーズ層 / スキニング）に触れる前。
- PolyLing_姿勢_残件.md / PolyLing_残件.md … 残件・未確認事項を聞かれたとき、残件に手を付けるとき。直したら更新する。
- PolyLing_サブツール作成ガイド.md … サブツール（右ペインのパネル）を作る・直すとき。
- PolyLing_モディファイア作成ガイド.md … 処理列の種類（IMeshModifier）を作る・直すとき。
- MCP操作手順書.md … MCP の道具の細かい使い方で迷ったとき。チュートリアルを作るとき（5-7）。
- 作業ログ.md … 過去の経緯を知りたいとき。

## 書ける場所と消し方
- 書き込み：起動引数 --editable の範囲（控えの データとツール/ツール/PolyLingMcpServer/コピペ用.txt では Packages/com.haglib.polyling、Packages/com.haglib.net_duplexchannel、SandBox、データとツール、Assets、@settings、@mocopi）。
- 置き場所の名前（データとツール・サンドボックス_シナリオ用・SandBox など）は設定ファイルで決まる。MCP サーバは exe のフォルダから親へたどった ProjectLayout.json、PolyLing 本体は persistentDataPath/PolyLing/ProjectLayout.json（無ければ既定値）。2 つは別の設定。
- 作業で作るデータの置き場：デバッグは主に SandBox、シナリオ・ランタイムの生成物は主に データとツール/サンドボックス_シナリオ用。
- PolyLing 直下と データとツール 直下にファイルやフォルダを作るのは禁止（コマンドと MCP の書き込みは拒否される。device_bash やスクリプトでも作らない）。
- delete_path は削除ではなく _McpTrash へ移す。書ける場所の外は消せない。
- MCP サーバ自身（データとツール/ツール/PolyLingMcpServer）はソースを書けるが、動いている exe がロックされるので再ビルド・再起動は操作者に頼む。compile で出る「exe をコピーできない」だけのエラーは、コンパイル自体は通っている。

## 道具の癖
- search_text は大きいファイル（18MB の MQO で実測）を読み飛ばす（files_skipped が 1 以上）。そのときは read_lines で区切って読む。
- 長いコマンドは timeout_ms を伸ばす。応答が切れても Unity 側では走っていることがあるので、read_unity_log で終わりを確かめてからやり直す。

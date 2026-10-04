この MCP サーバは Unity Editor 上の PolyLing（3D メッシュ編集ツール）を操作する。以下は操作者に言われなくても守る前提。

## 自分でやる（操作者に頼まない）
- Play の開始・停止、スクリプトの反映、ボタン操作、画面の確認はすべて道具でできる。「押してください」「確認してください」と頼まない。
- 頼むのは、道具で届かないもの（MCP サーバ自身の再ビルド・再起動、書き込み許可フォルダの外のファイル削除など）だけ。そのときは理由と代わりの案を添える。

## Unity と PolyLing の起動
- Unity Editor 自体が起動していない（unity_focus が Unity プロセスを見つけられない）ときは unity_editor_launch で起動し、unity_state が応答するまで待ってから unity_play する。
- polyling_* と unity_capture は Play 中でないと動かない。止まっていたら unity_play → unity_state で確認する。
- unity_play 直後は数秒つながらない（ポートに接続できません）。unity_state をもう一度呼べば通る。
- 状態は unity_state（Play 中か・コンパイル中か）。

## スクリプトを直したとき
- 一発で確かめる：unity_validate_changes（Play を抜けて反映し、この間に出たコンパイルエラーだけ返す。restore_play_mode=true で Play に戻る）。
- 手動なら unity_stop → unity_refresh → read_unity_log(contains="error CS") → unity_play。
- 事前の軽い検査は compile（dotnet build）。
- Play を抜けるとモデルは消える。検証に要るモデルは Play に入り直してから読み込み直す。

## 結果を見る
- 見た目は unity_capture（target=MainView / TriView / Window）。数値より先に、見て確かめられるものは見る。
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

## 文書（読むきっかけ）
- PolyLing_機能の場所.md … 機能・ボタンの場所を聞かれたとき。
- PolyLing_姿勢の規約.md … 姿勢・行列（WorldMatrix / BindPose / BindWorldMatrix / BoneTransform / ポーズ層 / スキニング）に触れる前。
- PolyLing_姿勢_残件.md / PolyLing_残件.md … 残件・未確認事項を聞かれたとき、残件に手を付けるとき。直したら更新する。
- PolyLing_サブツール作成ガイド.md … サブツール（右ペインのパネル）を作る・直すとき。
- MCP操作手順書.md … MCP の道具の細かい使い方で迷ったとき。
- 作業ログ.md … 過去の経緯を知りたいとき。

## 書ける場所と消し方
- 書き込み：Packages/com.haglib.polyling、Packages/com.haglib.net_duplexchannel、SandBox、データとツール、@settings。
- delete_path は削除ではなく _McpTrash へ移す。書ける場所の外は消せない。
- MCP サーバ自身（データとツール/PolyLingMcpServer）はソースを書けるが、動いている exe がロックされるので再ビルド・再起動は操作者に頼む。compile で出る「exe をコピーできない」だけのエラーは、コンパイル自体は通っている。

## 道具の癖
- search_text は大きいファイル（18MB の MQO で実測）を読み飛ばす（files_skipped が 1 以上）。そのときは read_lines で区切って読む。
- 長いコマンドは timeout_ms を伸ばす。応答が切れても Unity 側では走っていることがあるので、read_unity_log で終わりを確かめてからやり直す。

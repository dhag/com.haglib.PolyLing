# PolyLing 軽量リストクライアント（同梱）

描画メッシュを持たず、現行メインパネルと同一の Model / Object(Mesh) / Material 各リストを
WebSocket 経由で表示する軽量クライアント。本パッケージ `com.haglib.polyling` に同梱。
別 Unity プロジェクトへ本パッケージを導入して使う。

## しくみ

- サーバ（PolyLing 本体）は起動時に OS 割り当てのポートで待ち受け、サーバ一覧に参加する。
  - サーバ一覧はマスター用ポート `127.0.0.1:8760`（`RemoteDirectory.MasterPort`）で受け付ける。
  - 最初に起動したサーバがマスターを兼ね、後から起動したサーバはマスターへ登録する。
  - マスターが終了すると、残ったサーバの 1 台がマスターを引き継ぎ、他は登録し直す。
- クライアントはマスターに一覧を問い合わせ、サーバが 1 台ならそのまま `ws://127.0.0.1:port/` へ接続する。
  複数あるときは選択肢（プロジェクト名・ユーザー名・pid・port）を表示し、選んだサーバへ接続する。
  切断後の再接続は、前回と同じプロセス（pid）が一覧にあればそこへ自動で戻る。
- 接続後 `project_header` を 1 回取得し、全モデルのメタ＋メッシュ Summary を復元。
  **`mesh_data` は取得しない**ため描画メッシュ本体は保持しない。
- 復元した `ProjectContext` を `PanelContext.Notify(new PlayerProjectView(...))` で
  現行メインパネルの実サブパネルへ流し込み、同一 UI を表示する:
  - `MeshListClient`     → `MeshListSubPanel`（オブジェクトリスト／Mesh/Bone/Morph/剛体タブ）
  - `ModelListClient`    → `ModelListSubPanel`
  - `MaterialListClient` → `PlayerMaterialListSubPanel`
- 一覧変更の push 受信で再取得・再表示。

## セットアップ

1. 新規 Unity プロジェクトを作成（Unity 6）。
2. 本パッケージ `com.haglib.polyling` と依存（`com.haglib.net_duplexchannel`、
   `com.unity.nuget.newtonsoft-json`）を導入。
3. 空の GameObject を 1 つ作成し、次の**いずれか 1 つ**をアタッチ:
   - `ModelListClient`
   - `MeshListClient`（オブジェクトリスト）
   - `MaterialListClient`
   `UIDocument` は自動付与される。
4. UIDocument に `PanelSettings` を割当てる
   （未割当時は `Resources/PolyLingListClient/PanelSettings.asset` を自動読込）。
5. 再生 / ビルドすると自動でサーバ一覧を問い合わせ、接続する。

## 別窓運用

- 用途別に本クライアントの**インスタンスを複数起動**する
  （例: オブジェクトリスト用ビルドと Material 用ビルドを別々に起動）。
- 各インスタンスが 1 リスト = 別 OS ウィンドウ。
  サーバが 1 台なら個別設定なしで全インスタンスが同一サーバへ接続する。
  複数台のときは各インスタンスで接続先を選ぶ。

## 選択・操作の同期（双方向）

- クライアント→サーバ：パネル操作を `PanelCommandRouter` がサーバの command プロトコルへ変換して送信。
  対応コマンド＝選択(selectMesh) / 表示 / ロック / ミラー / 改名 / 追加・削除・複製 /
  ボーンポーズ(init/active/reset/bake) / モデル(switch/rename/delete)。
  サーバ未対応（morph変換・プレビュー、bone transform 数値、material 各種、tree折り畳み等）は無視。
- 選択は操作者（ユーザー名）ごとに持つ。ユーザー名が空欄のクライアントは端末名を使う。
  サーバ本体（ホスト）の操作者名も端末名なので、**同じ PC で空欄のまま開いた別窓はホストと同一人物**として扱われる。
- 同一人物（ホストと同名）の窓：
  - クライアントでの選択は本体へそのまま流れ、本体の選択が変わる。
  - 本体の選択が変わると `selectionChanged` が同名の全窓へ送られ、選択・アクティブカテゴリ・現在モデルが連動する。
- 別名のユーザー（協働編集）：
  - クライアントでの選択はそのユーザーの選択としてサーバに記録され、本体や他ユーザーの画面は動かない。
  - 接続直後はホストの現在選択が初期値として 1 回送られる。

## デザイン共有

- サーバ右ペインと同一の意匠を継承する。背景色は `PlayerLayoutRoot.RightPaneBackgroundColor`、
  制御色（テキストボックス/ボタン/トグル/スライダ等）はサーバ自身の共有機構
  `PlayerLayoutRoot.ApplyDarkTheme(root)` をクライアントでも適用（単一ソース）。
- サブパネル再構築時にも再適用するため、選択・モデル切替後も暗テーマを維持する。

## 補足

- 接続先が未検出の間は一定間隔で再探索する（`Retry Seconds`）。
- `Auto Refresh Seconds` を 0 より大きくすると定期再取得する（既定 0 = push 契機のみ）。
- 表示対象はサーバの現在モデル（`CurrentModelIndex`）。ジオメトリ本体は非取得
  （MeshObject は空。名前・種別・階層・表示状態は Summary から表示）。

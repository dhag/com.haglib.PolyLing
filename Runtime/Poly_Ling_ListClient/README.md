# PolyLing 軽量リストクライアント（同梱）

描画メッシュを持たず、現行メインパネルと同一の Model / Object(Mesh) / Material 各リストを
WebSocket 経由で表示する軽量クライアント。本パッケージ `com.haglib.polyling` に同梱。
別 Unity プロジェクトへ本パッケージを導入して使う。

## しくみ

- サーバ（PolyLing 本体）が起動時に接続先を公開:
  `Application.persistentDataPath/PolyLing/endpoint.json`
  （company=HagiharaLab / product=PolyLing 時は
   `%LocalLow%/HagiharaLab/PolyLing/PolyLing/endpoint.json`）
- クライアントは endpoint.json を読み、`ws://host:port/` へ自動接続。
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
5. 再生 / ビルドすると自動で endpoint.json を探索・接続する。

## 別窓運用

- 用途別に本クライアントの**インスタンスを複数起動**する
  （例: オブジェクトリスト用ビルドと Material 用ビルドを別々に起動）。
- 各インスタンスが 1 リスト = 別 OS ウィンドウ。
  全インスタンスが同じ endpoint.json を共有するため個別設定なしで同一サーバへ接続する。

## 選択・操作の同期（双方向）

- クライアント→サーバ：パネル操作を `PanelCommandRouter` がサーバの command プロトコルへ変換して送信。
  対応コマンド＝選択(selectMesh) / 表示 / ロック / ミラー / 改名 / 追加・削除・複製 /
  ボーンポーズ(init/active/reset/bake) / モデル(switch/rename/delete)。
  サーバ未対応（morph変換・プレビュー、bone transform 数値、material 各種、tree折り畳み等）は無視。
- サーバ→クライアント：サーバ `Tick()` が選択のスナップショット差分を検知し `selectionChanged` を broadcast。
  クライアントは受信して選択とアクティブカテゴリ・現在モデルを反映（再フェッチ不要）。
  クライアント接続時は現在の選択が自動配信される。
- 複数ウィンドウ間も、サーバ経由で選択・カレントモデルが相互同期する。

## 補足

- 接続先が未検出の間は一定間隔で再探索する（`Retry Seconds`）。
- `Auto Refresh Seconds` を 0 より大きくすると定期再取得する（既定 0 = push 契機のみ）。
- 表示対象はサーバの現在モデル（`CurrentModelIndex`）。ジオメトリ本体は非取得
  （MeshObject は空。名前・種別・階層・表示状態は Summary から表示）。

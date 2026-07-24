# PolyLing 軽量リストクライアント（同梱）

描画メッシュを持たず、Model / Material / Mesh の各リストを WebSocket 経由で表示する軽量クライアント。
本パッケージ `com.haglib.polyling` に同梱。別 Unity プロジェクトへ本パッケージを導入して使う。

## しくみ

- サーバ（PolyLing 本体）が起動時に接続先を公開:
  `Application.persistentDataPath/PolyLing/endpoint.json`
  （company=HagiharaLab / product=PolyLing 時は
   `%LocalLow%/HagiharaLab/PolyLing/PolyLing/endpoint.json`）
- クライアントは endpoint.json を読み、`ws://host:port/` へ自動接続。
- 接続後 `project_header` を 1 回取得し、全モデルのメタ＋メッシュ Summary を復元。
  **`mesh_data` は取得しない**ため描画メッシュ本体は保持しない。
  頂点数 / 面数は Summary から取得して表示する。
- 一覧変更の push 受信、または「更新」ボタンで再取得。

## セットアップ

1. 新規 Unity プロジェクトを作成（Unity 6）。
2. 本パッケージ `com.haglib.polyling` と依存（`com.haglib.net_duplexchannel`、
   `com.unity.nuget.newtonsoft-json`）を導入。
3. 空の GameObject を 1 つ作成し、次の**いずれか 1 つ**をアタッチ:
   - `ModelListClient`
   - `MaterialListClient`
   - `MeshListClient`
   `UIDocument` は自動付与される。
4. UIDocument に `PanelSettings` を割当てる
   （未割当時は `Resources/PolyLingListClient/PanelSettings.asset` を自動読込）。
5. 再生 / ビルドすると自動で endpoint.json を探索・接続する。

## 別窓運用

- 用途別に本クライアントの**インスタンスを複数起動**する
  （例: Mesh 用ビルドと Material 用ビルドを別々に起動）。
- 各インスタンスが 1 リスト = 別 OS ウィンドウ。
  全インスタンスが同じ endpoint.json を共有するため個別設定なしで同一サーバへ接続する。

## 補足

- 接続先が未検出の間は一定間隔で再探索する（`Retry Seconds`）。
- `Auto Refresh Seconds` を 0 より大きくすると定期再取得する（既定 0 = push 契機のみ）。
- 表示専用。編集コマンド送信は本コンポーネントの対象外。

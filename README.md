ユニティエディタ上で動くモデリングツール。
簡単なメッシュをヒエラルキー上に追加したり、メッシュやプレファブを作ったりすることが手軽に。
・点や線分のカリングを計算シェーダで
・ヒットテストの一部を計算シェーダで

-----------------------------------------------
基本の設定方法（使い方）
[window]-[Package Manager]
+Install package from git URL...
https://github.com/dhag/com.haglib.PolyLing.git
https://github.com/dhag/com.haglib.net_duplexchannel.git

エディタ拡張 "PolyLing/CreateRuntime/Create Player Viewer"実行。または下記の作業を行う。
--------
パネルセッティングを新規作成する。
  (New Panel Settingsファイルを作る)
　  [Create][UIツールキット][パネルセッティング]
　  Assets/New Panel Settings.assetが生成される

空のゲームオブジェクトを作る
　UI Documentをアタッチ。
　　パネルセッティングをつける。UIドキュメントにNew Panel Settingsをアタッチ.
　PolyLingPlayerViewerをアタッチ。
　　　必要ならサーバモードかクライアントモードかを設定する。
--------

ModelListClient.cs / MaterialListClient.cs / MeshListClient.cs
----------------------------------
各種パラメータ置き場

C:\Users\<ユーザー名>\AppData\LocalLow\HagiharaLab\PolyLing\PolyLing
例えばユーザー名がdhagなら
C:\Users\dhag\AppData\LocalLow\HagiharaLab\PolyLing\PolyLing
----------------------------------
PMX
1.読みこみ
2.アバター用ヒューマンマッピング、AutoMap、Apply
3.Tポーズ変換
----------------------------------
MQOの半身モデルの場合、
ツリー構造を保つための特殊な名前（@@て_*ミラー分岐ルート）の空のオブジェクトをつけておくこと
たとえば
@@て_ミラー分岐ルート
@@あし_ミラー分岐ルート

またはオブジェクトリストでミラー分岐ルートにチェックする。例えば左腕と左足（もも）に

MQO スキニングする場合（人間・動物）
1.読みこみ
2.オブジェクト姿勢-オブジェクト姿勢-原点CSV読み込み　
（ローカルが設定されている場合この操作不要）
3.メッシュからボーンとスキンの生成-変換実行
4.アバター用ヒューマンマッピング、AutoMap、Apply
（オブジェクト名が標準と異なる場合CSVから）
3.Tポーズ変換-Tポーズに変換
（すでにTポーズの場合この操作不要）


MQO スキニングしない場合（メカ人間）
1.読みこみ
2.オブジェクト姿勢-オブジェクト姿勢-原点CSV読み込み　
（ローカルが設定されている場合この操作不要）
3.アバター用ヒューマンマッピング、「ボーン以外も候補に含める」にチェック、AutoMap、Apply
または
Tポーズ変換-CSVを読みこんでマッピング-Tポーズに変換




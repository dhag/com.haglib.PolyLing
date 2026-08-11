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




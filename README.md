ユニティエディタ上で動くモデリングツール。
簡単なメッシュをヒエラルキー上に追加したり、メッシュやプレファブを作ったりすることが手軽に。
・点や線分のカリングを計算シェーダで
・ヒットテストの一部を計算シェーダで

-----------------------------------------------
基本の設定方法（使い方）
+Install package from git URL...
https://github.com/dhag/com.haglib.PolyLing.git
https://github.com/dhag/com.haglib.net_duplexchannel


パネルセッティングを新規作成する。
  (New Panel Settingsファイルを作る)
　  [Create][UIツールキット][パネルセッティング]
　  Assets/New Panel Settings.assetが生成される

空のゲームオブジェクトを作る
　UI Documentをアタッチ。
　　パネルセッティングをつける。UIドキュメントにNew Panel Settingsをアタッチ.
　PolyLingPlayerViewerをアタッチ。
　　　必要ならサーバモードかクライアントモードかを設定する。



----------------------------------
サーバーモードでログがコピーできない。copyボタンが欲しい。
サーバーモードでログがクリアできない。見かけはクリアできるが、他のボタンを押したあと戻ると全部残ってる。
サーバーモードでログリアルタイムで反映されないような気がする。



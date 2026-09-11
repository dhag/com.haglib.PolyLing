--------------------------------------------
サブツール作成のガイド

必須変更箇所の表（全行を踏むまで未完了）
パネル PlayerPrimitiveMeshSubPanel：ShapeKind 末尾追加と ShapeKeys 同順（.cs:313-325）／カテゴリ配列（.cs:331-354）／パラメータ初期値関数でピボット既定を明示（.cs:377-403 の形）／RebuildSettings の case（.cs:1508-1556）／名前欄 NF と値変更時の D()（.cs:1564-1568）／BuildCreateCommand の case で pl を渡す（Command.cs:96-148）／Name()・SetName()（.cs:4745-4825）／前提がある図形は CanGenerate()（.cs:5097-5147）／PrimitiveMeshTexts のキー
コマンド：PanelCommand.cs に CreatePrimitiveMeshCommand 派生（:5145-5159 の形。ShapeName は ShapeKeys と同じ文字列、:5073）
生成：PrimitiveMeshFactory.Generate の case（:130。未登録は :170 で警告して null）／生成器はローカル空間でピボット適用まで。平行移動・回転・拡大は入れない（Factory.cs:13、:104-109 が行う）
触らない箇所：ディスパッチャ（PlayerCommandDispatcher.cs:1768 が基底型で受ける）、受け口（CreateCommands.cs:116-133）
3Dビュー表示の成立条件
黄色ワイヤは LiveWireInMainViewport が立つインスタンスだけ（.cs:1159）。立っているのは 3D連携（ViewerCore.cs:5106）とサンドボックス（:5149）。ショートカット経路 ShowPrimitiveShape（:6248-6255）が開く _primitiveSubPanel は立っていない → 表示確認は 3D連携パネル（:5197-5200 のボタン）で行う
Generate(true) が null なら出ない（.cs:4662-4693）
プレビュー中の例外は catch { }（.cs:1089）で消える → 原因調査は polyling_call の失敗理由（CreateCommands.cs:124-125）で行う
姿勢の成立条件
姿勢は CurrentPlacement()（Command.cs:53-67）経由でしかコマンドに載らない
パネル既定は回転ベイク OFF・スケールベイク ON（.cs:432-433）、PrimitivePlacement.Default は回転ベイク ON（PanelCommand.cs:5051）。パネル経路で Default を使わない
ベイクしない成分は BuildPrimitiveMeshContext が描画オブジェクトの姿勢へ入れる（ViewerCore.cs:9010-9020）
配置ギズモ・原点マーカー仮表示は _livePrimitiveSubPanel に直結（ViewerCore.cs:5160-5195、:1991-1997）
姿勢フォールドを出す図形は必ず生成へ反映する。反映しない図形は PoseApplicable（.cs:218-221）・RefreshCommonUiVisibility（.cs:486-501）で隠す（.cs:476-485 の方針）。材質は ShapeUsesMaterialSlot（.cs:119-122）で同様
禁止事項
表 1 の一部だけ実施して完了と報告
パネルからファクトリ／Ops を呼んでモデルへ直接入れる（Command.cs:5-7）
パネル経路で PrimitivePlacement.Default を使う
生成器内で平行移動・回転・拡大を入れる
例外の握りつぶし、?.Invoke による未配線の素通りを新たに足す
既存図形の丸写しで Params・名前・テキストキーを流用したまま残す
完了条件
compile errors=0（3 csproj）
polyling_call queryCommandAudit 全項目 0
polyling_call で新コマンドを既定値で実行し成功、vertices/faces > 0（PanelCommand.cs:5063-5066）
配置付き（worldPosition・placeRotation・bakeRotation=false）で再実行し成功
目視項目（ユーザー確認）：3D連携パネルでの黄色ワイヤ、ギズモ追従、原点マーカー／くさび、生成後の姿勢。描画オブジェクトの姿勢を返す PLResult は存在しないため MCP では確認できない
完了報告の書式
表 1 の全行を「済／未／対象外＋file:line」で列挙。「未」が 1 行でもあれば完了と書かない
--------------------------------------------------------
// BuiltinScenes.cs
// 同梱する既製の利用シーン。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【なぜコードに持つか】
//   ファイル（scenes.csv）に書き出すと、次の版で既製品を直しても利用者の手元へ届かない。
//   コードに持ち、SceneLibrary が利用者の定義と合わせて返す。
//
// 【読み取り専用】
//   既製品と同じ名前での登録・上書き・削除は SceneLibrary が断る。変えたいときは別の名前で複製する。
//
// 【中身の根拠】
//   PolyLing_利用シーン_カテゴライズ設計方針.md 10 節の初期 3 件と、
//   コマンドの分類（PLCommand.Category）の集計から足した 21 件（計 24 件）。
//   ExplicitCommands・Tools に書く名前は実在のコマンド名・MCP サーバの道具名に限る。
//   危険性の扱いは、各コマンドの PLCommand.Hazards（Phase 3 で 55 本に付けた値）に効く。
//   Hazards が未設定のコマンドには効かない。

using System.Collections.Generic;

namespace Poly_Ling.Data
{
    public static class BuiltinScenes
    {
        private static List<SceneDefinition> _all;

        /// <summary>既製の利用シーン（呼ぶたびに写しを返す）。</summary>
        public static List<SceneDefinition> All()
        {
            if (_all == null) _all = Build();
            var list = new List<SceneDefinition>(_all.Count);
            foreach (var s in _all) list.Add(s.Clone());
            return list;
        }

        /// <summary>既製の利用シーンの名前か（大小文字は区別しない）。</summary>
        public static bool IsBuiltin(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (_all == null) _all = Build();
            foreach (var s in _all)
                if (string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static List<SceneDefinition> Build()
        {
            var list = new List<SceneDefinition>();

            // ---- 新規形状作成：トポロジの変更を許す段階
            var create = new SceneDefinition
            {
                Name        = "新規形状作成",
                Description = "形をゼロから作る段階。頂点・面の追加や削除、押し出し、ミラー、材質と UV の設定を使う。",
                Notes       = "スキンウェイトやモーフを付ける前の段階を想定している。付けたあとで形を変えると壊れるので、その段階ではスキニングやモーフ・表情作成の利用シーンへ切り替える。",
            };
            create.IncludeCategories.AddRange(new[] { "geometry", "selection", "query", "transform.object", "mirror", "material", "uv" });
            create.StateAssumptions["topologyLocked"] = false;
            create.VerificationPolicy = PLCommandVerification.Topology | PLCommandVerification.Normals;
            list.Add(create);

            // ---- スキニング：ボーンとウェイトを付ける段階
            var skin = new SceneDefinition
            {
                Name        = "スキニング",
                Description = "ボーンを組み、スキンウェイトを付ける段階。ボーンの配置、Humanoid 割当、ウェイトの塗りと正規化を使う。",
                Notes       = "ウェイトを付けたあとで頂点を増減すると、付けたウェイトの対応が崩れる。多数のオブジェクトへ同じ改変を承知で行う場合は、利用者の承認を得てから実行し、あとでウェイトを付け直す。バインドポーズの書き換えは元に戻せない。",
            };
            skin.IncludeCategories.AddRange(new[] { "rig", "geometry.position", "selection", "query" });
            // 頂点データの転送（ウェイトを別オブジェクトから写す）は分類が geometry.attribute なので明示して足す。
            skin.ExplicitCommands.Add("transferVertexData");
            skin.StateAssumptions["hasBones"] = true;
            skin.HazardPolicy[PLCommandHazard.InvalidatesSkinWeights] = PLHazardAction.RequireConfirmation;
            skin.HazardPolicy[PLCommandHazard.ChangesVertexOrder]     = PLHazardAction.RequireConfirmation;
            skin.HazardPolicy[PLCommandHazard.ChangesBindPose]        = PLHazardAction.RequireConfirmation;
            skin.VerificationPolicy = PLCommandVerification.SkinWeights | PLCommandVerification.Visual;
            list.Add(skin);

            // ---- モーフ・表情作成：頂点数と並びを保つ段階
            var morph = new SceneDefinition
            {
                Name        = "モーフ・表情作成",
                Description = "表情などのモーフを作り、調整する段階。頂点位置の編集とモーフの作成・変換・プレビューを使う。",
                Notes       = "モーフは頂点番号で基準メッシュと対応している。頂点の並びを変える操作は出さない。頂点を増減する操作は承認を求める。やむを得ず形を変える場合は、あとでモーフを作り直す前提で行う。",
            };
            morph.IncludeCategories.AddRange(new[] { "morph", "geometry.position", "selection", "query" });
            morph.StateAssumptions["topologyLocked"] = true;
            morph.HazardPolicy[PLCommandHazard.ChangesVertexOrder] = PLHazardAction.Hide;
            morph.HazardPolicy[PLCommandHazard.InvalidatesMorphs]  = PLHazardAction.RequireConfirmation;
            morph.VerificationPolicy = PLCommandVerification.MorphIntegrity | PLCommandVerification.VertexCount | PLCommandVerification.Visual;
            list.Add(morph);

            // ---- 既存形状の改変
            list.Add(Make("既存形状の改変",
                "できている形を直す段階。選択、頂点移動、作業軸による変形、面の分割・削除による部分置換を使う。",
                "スキンウェイトやモーフが付いたモデルでは、頂点の増減で対応が崩れる。その操作は承認を得てから行う。",
                new[] { "selection", "query", "geometry.position", "geometry.deform", "geometry.topology", "workaxis", "transform.object", "mirror" },
                null,
                PLCommandVerification.Geometry | PLCommandVerification.Visual,
                hazards: new[] { (PLCommandHazard.InvalidatesSkinWeights, PLHazardAction.RequireConfirmation),
                                 (PLCommandHazard.InvalidatesMorphs,      PLHazardAction.RequireConfirmation) }));

            // ---- トポロジ確定後の調整
            list.Add(Make("トポロジ確定後の調整",
                "面構成を確定したあとの仕上げ。頂点位置、法線、材質、軽微な修正を扱う。",
                "頂点の並びを変える操作は出さない。頂点を分ける操作は承認を求める。",
                new[] { "geometry.position", "geometry.attribute", "material", "selection", "query" },
                null,
                PLCommandVerification.VertexCount | PLCommandVerification.Normals | PLCommandVerification.Visual,
                states: new[] { ("topologyLocked", true) },
                hazards: new[] { (PLCommandHazard.ChangesVertexOrder, PLHazardAction.Hide),
                                 (PLCommandHazard.MaySplitVertices,   PLHazardAction.RequireConfirmation),
                                 (PLCommandHazard.InvalidatesMorphs,  PLHazardAction.RequireConfirmation) }));

            // ---- Humanoid 骨格作成
            list.Add(Make("Humanoid骨格作成",
                "ボーンを整え、Humanoid の割当と可動域を付けて姿勢を確かめる段階。",
                "バインドポーズの書き換えは元に戻せないので承認を求める。",
                new[] { "rig.skeleton", "transform.object", "object.list", "object.attribute", "selection", "query" },
                null,
                PLCommandVerification.Visual,
                hazards: new[] { (PLCommandHazard.ChangesBindPose, PLHazardAction.RequireConfirmation) }));

            // ---- リジッド機構
            list.Add(Make("リジッド機構",
                "部品を階層に組み、軸と原点を決め、ボーンで剛体的に動かす段階。",
                "部品はウェイト 1.0 で 1 本のボーンへ付ける（convertToSkinned / convertMeshFilterToSkinned）。",
                new[] { "object.list", "object.attribute", "transform.object", "workaxis", "rig.skeleton", "selection", "query" },
                new[] { "convertMeshFilterToSkinned", "convertToSkinned", "convertToMeshFilter" },
                PLCommandVerification.Visual,
                hazards: new[] { (PLCommandHazard.ChangesBindPose, PLHazardAction.RequireConfirmation) }));

            // ---- SpringBone 設定
            list.Add(Make("SpringBone設定",
                "揺れ物のボーン鎖を置き、ウェイト、ジョイント、コライダーを付けて確かめる段階。",
                "はしご（梯子状ベルト）からの配置は acquireBeltStrips で点列を取ってから行う。",
                new[] { "dynamics.spring", "rig.skinning", "object.group", "selection", "query" },
                new[] { "acquireBeltStrips" },
                PLCommandVerification.SkinWeights | PLCommandVerification.Visual,
                states: new[] { ("hasBones", true) },
                hazards: new[] { (PLCommandHazard.InvalidatesSkinWeights, PLHazardAction.RequireConfirmation) }));

            // ---- アニメーション編集
            list.Add(Make("アニメーション編集",
                "ボーンの姿勢、マッスル可動域、モーフの効きを扱い、モーションを変換・書き出す段階。",
                "animation 分類のコマンドはモーション JSON・VMD・VRMA の検査・変換・書き出しで、キーを 1 つずつ編集するコマンドは無い。",
                new[] { "animation", "rig.skeleton.pose", "selection", "query" },
                new[] { "setHumanLimit", "clearHumanLimit", "startMorphPreview", "applyMorphPreview", "endMorphPreview" },
                PLCommandVerification.Visual,
                hazards: new[] { (PLCommandHazard.ChangesBindPose, PLHazardAction.RequireConfirmation) }));

            // ---- VRM 仕上げ・検証
            list.Add(Make("VRM仕上げ・検証",
                "VRM のメタ・視線・一人称、揺れ物、表情を整えて書き出し、確かめる段階。",
                "書き出しは作業フォルダの下だけへ書ける。",
                new[] { "vrm", "dynamics.spring", "rig.skeleton.humanoid", "io.export", "query" },
                new[] { "setMorphExpressionAttributes", "startMorphPreview", "applyMorphPreview", "endMorphPreview", "importVrmFile" },
                PLCommandVerification.Visual,
                states: new[] { ("hasHumanoid", true) }));

            // ---- 機械部品作成
            list.Add(Make("機械部品作成",
                "歯車、ねじ、軸受、継手、ブラケットなどの機械部品を作り、配置・結合する段階。",
                "3D プリント向けの書き出しは exportStlFile を使う。",
                new[] { "geometry.create.mechanical", "geometry.create.primitive", "geometry.topology", "workaxis", "transform.object", "object.list", "selection", "query" },
                new[] { "createSpline", "exportStlFile" },
                PLCommandVerification.Topology | PLCommandVerification.Normals,
                states: new[] { ("topologyLocked", false) }));

            // ---- 線分群・2D プロファイル編集
            list.Add(Make("線分群・2Dプロファイル編集",
                "線分群（LineGroup）の点列とハンドルを編集し、回転体・2D 押し出し・パイプ・フリルの断面に使う段階。",
                "",
                new[] { "linegroup", "selection", "query" },
                new[] { "createRevolution", "createProfile2D", "createPipe", "createFrill", "setMeshBillboard" },
                PLCommandVerification.Geometry | PLCommandVerification.Visual));

            // ---- 派生生成と再構築
            list.Add(Make("派生生成と再構築",
                "既存の形から派生物（パイプ、フリル、藤壺、歪み複製、穴つなぎ）を作り、オブジェクトグループで作り直す段階。",
                "ソースを直したら rebuildObjectGroup で出力先へ反映する。",
                new[] { "geometry.create.derived", "object.group", "selection", "query" },
                null,
                PLCommandVerification.Topology | PLCommandVerification.Visual,
                hazards: new[] { (PLCommandHazard.AffectsMultipleObjects, PLHazardAction.RequireConfirmation) }));

            // ---- 髪・装飾物作成
            list.Add(Make("髪・装飾物作成",
                "髪の房、リボン、フリル、パイプなどの装飾物を作る段階。",
                "揺れ物にする場合は、このあと SpringBone設定 の利用シーンへ切り替える。",
                new[] { "object.group", "geometry.position", "selection", "query" },
                new[] { "createHairStrand", "createRibbonBow", "createFrill", "createPipe", "createEdgePipe", "createObjectArray", "acquireBeltStrips" },
                PLCommandVerification.Visual));

            // ---- UV 展開・材質設定
            list.Add(Make("UV展開・材質設定",
                "UV を展開・修正し、材質の枠・色・シェーダー・テクスチャを設定する段階。",
                "頂点を分ける操作・並びを変える操作は承認を求める。",
                new[] { "uv", "material", "selection", "query" },
                null,
                PLCommandVerification.UVSeams | PLCommandVerification.Visual,
                hazards: new[] { (PLCommandHazard.MaySplitVertices,   PLHazardAction.RequireConfirmation),
                                 (PLCommandHazard.ChangesVertexOrder, PLHazardAction.RequireConfirmation) }));

            // ---- 形状の転写・フィッティング
            list.Add(Make("形状の転写・フィッティング",
                "別の形に合わせて変形し、頂点データ・法線・対称の片側を転写する段階。",
                "スキンウェイトやモーフの付いた対象では、壊す操作は承認を求める。",
                new[] { "geometry.deform", "selection", "query" },
                new[] { "surfaceSnap", "transferVertexData", "applyNormalTransplant", "applyReferenceSymmetry", "createBlendClone" },
                PLCommandVerification.Geometry | PLCommandVerification.Visual,
                hazards: new[] { (PLCommandHazard.InvalidatesSkinWeights, PLHazardAction.RequireConfirmation),
                                 (PLCommandHazard.InvalidatesMorphs,      PLHazardAction.RequireConfirmation) }));

            // ---- 外部ツール往復
            list.Add(Make("外部ツール往復",
                "外部ツールで直した MQO / PMX から、構造を保って頂点位置・UV・ウェイトだけを差し替える段階。",
                "組は頂点数（PMX は名前を先に）で決まる。差し替える前に組を確かめる。",
                new[] { "io.import", "selection", "query" },
                new[] { "exportMqoFile", "exportPmxFile" },
                PLCommandVerification.VertexCount | PLCommandVerification.Visual,
                states: new[] { ("topologyLocked", true) },
                hazards: new[] { (PLCommandHazard.ChangesVertexOrder, PLHazardAction.RequireConfirmation) }));

            // ---- 左右対称の整備
            list.Add(Make("左右対称の整備",
                "ミラーの設定・実体化・解除、対称の移植、ボーンの左右対応を整える段階。",
                "",
                new[] { "mirror", "selection", "query" },
                new[] { "resolveMirrorBoneIndex" },
                PLCommandVerification.MirrorIntegrity,
                hazards: new[] { (PLCommandHazard.BreaksMirrorRelation, PLHazardAction.RequireConfirmation) }));

            // ---- 部品分割・選択セット管理
            list.Add(Make("部品分割・選択セット管理",
                "パーツ ID の採番、部品への分解・結合、選択セット・選択辞書の保存と読み込みを行う段階。",
                "",
                new[] { "selection", "object.list", "object.attribute", "query" },
                new[] { "assignPartsIds", "assignPartsIdsByBoneWeight" },
                PLCommandVerification.Visual));

            // ---- 姿勢の基準化
            list.Add(Make("姿勢の基準化",
                "T ポーズ化、ポーズのバインドポーズへの焼き込みなど、基準の姿勢を決める段階。",
                "バインドポーズの書き換えは元に戻せないので承認を求める。",
                new[] { "rig.skeleton.pose", "selection", "query" },
                null,
                PLCommandVerification.Visual,
                states: new[] { ("hasBones", true) },
                hazards: new[] { (PLCommandHazard.ChangesBindPose, PLHazardAction.RequireConfirmation) }));

            // ---- 形式変換・プロジェクト管理
            list.Add(Make("形式変換・プロジェクト管理",
                "各形式の読み込み・書き出し、プロジェクトの保存・読み込み、モデルの切り替え、エディタ拡張への送信を行う段階。",
                "読み書きは作業フォルダの下だけ。loadProjectFile は編集中のプロジェクトを置き換える。",
                new[] { "io", "object.model", "query" },
                null,
                PLCommandVerification.None));

            // ---- 手本作成・検証
            list.Add(Make("手本作成・検証",
                "手本（シナリオ）を作り、記録・実行・監査する段階。",
                "利用シーンを消したり改名したりしたら queryScenarioAudit を回す。",
                new[] { "scenario", "object.group", "mcp", "query" },
                null,
                PLCommandVerification.None));

            // ---- ヘルプ・チュートリアル作成
            list.Add(Make("ヘルプ・チュートリアル作成",
                "パネルを表示し、値の設定・強調・キャプチャをして、ヘルプやチュートリアルの素材を作る段階。",
                "",
                new[] { "ui", "query" },
                null,
                PLCommandVerification.Visual));

            // ---- 新規ツール・コマンド
            var dev = Make("新規ツール・コマンド",
                "PolyLing にツール・コマンドを足し、コンパイル、登録確認、動作確認をする段階。",
                "スクリプトを直したら unity_validate_changes で確かめる。コマンドの顔ぶれが変わると queryRevisions の版が変わる。",
                new[] { "tool", "query" },
                new[] { "queryRevisions", "queryOwnershipVerdict", "queryUiAutomationAudit" },
                PLCommandVerification.None);
            dev.Tools.AddRange(new[] { "list_files", "read_file", "read_lines", "search_text", "write_file", "apply_patch",
                                       "unity_validate_changes", "get_compile_errors", "read_unity_log",
                                       "unity_play", "unity_stop", "unity_focus", "unity_capture" });
            list.Add(dev);

            return list;
        }

        private static SceneDefinition Make(
            string name, string description, string notes,
            string[] categories, string[] commands,
            PLCommandVerification verification,
            (string key, bool value)[] states = null,
            (PLCommandHazard hazard, PLHazardAction action)[] hazards = null)
        {
            var s = new SceneDefinition { Name = name, Description = description, Notes = notes };
            if (categories != null) s.IncludeCategories.AddRange(categories);
            if (commands != null) s.ExplicitCommands.AddRange(commands);
            if (states != null) foreach (var st in states) s.StateAssumptions[st.key] = st.value;
            if (hazards != null) foreach (var h in hazards) s.HazardPolicy[h.hazard] = h.action;
            s.VerificationPolicy = verification;
            return s;
        }
    }
}

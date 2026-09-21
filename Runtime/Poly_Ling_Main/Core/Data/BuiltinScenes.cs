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
//   PolyLing_利用シーン_カテゴライズ設計方針.md 10 節の初期 3 件。
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

            return list;
        }
    }
}

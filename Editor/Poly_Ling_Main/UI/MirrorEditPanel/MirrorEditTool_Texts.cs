// Assets/Editor/Poly_Ling/Tools/MirrorEdit/MirrorEditTool_Texts.cs
// ミラー編集ツールのローカライズテキスト

using System.Collections.Generic;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools
{
    public partial class MirrorEditTool
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            // タイトル・ヘルプ
            ["Title"] = new() { ["en"] = "Mirror Edit Tool", ["ja"] = "ミラー編集ツール", ["hi"] = "かがみへんしゅう" },
            ["Help"] = new() { ["en"] = "Bake mirror mesh for editing, then write back to half mesh.\nOptionally blend between original and edited result.", ["ja"] = "ミラーメッシュを実体化して編集し、ハーフメッシュに書き戻します。\nオリジナルと編集結果をブレンドすることもできます。", ["hi"] = "かがみをやいて、へんしゅうして、もどすよ\nまぜることもできるよ" },

            // Step 1
            ["Step1_BakeMirror"] = new() { ["en"] = "Step 1: Bake Mirror", ["ja"] = "ステップ1: ミラー実体化", ["hi"] = "1: かがみをやく" },
            ["MirrorAxis"] = new() { ["en"] = "Mirror Axis", ["ja"] = "ミラー軸", ["hi"] = "かがみのじく" },
            ["Threshold"] = new() { ["en"] = "Boundary Threshold", ["ja"] = "境界閾値", ["hi"] = "さかいめ" },
            ["FlipU"] = new() { ["en"] = "Flip UV (U)", ["ja"] = "UV反転 (U)", ["hi"] = "UVはんてん" },
            ["BakeMirror"] = new() { ["en"] = "Bake Mirror to New Mesh", ["ja"] = "ミラーを実体化", ["hi"] = "かがみをやく" },

            // Step 2
            ["Step2_Edit"] = new() { ["en"] = "Step 2: Edit", ["ja"] = "ステップ2: 編集", ["hi"] = "2: へんしゅう" },
            ["EditHelp"] = new() { ["en"] = "Use Move/Sculpt/other tools to edit the baked mesh.", ["ja"] = "Move/Sculpt等のツールでベイクしたメッシュを編集してください。", ["hi"] = "ほかのつーるでへんしゅうしてね" },

            // Step 3
            ["Step3_WriteBack"] = new() { ["en"] = "Step 3: Write Back", ["ja"] = "ステップ3: 書き戻し", ["hi"] = "3: かきもどし" },
            ["WriteBackMode"] = new() { ["en"] = "Write Back Mode", ["ja"] = "書き戻しモード", ["hi"] = "もーど" },
            ["WriteBack"] = new() { ["en"] = "Write Back to New Mesh", ["ja"] = "新しいメッシュに書き戻し", ["hi"] = "かきもどす" },

            // Step 4: Blend
            ["Step4_Blend"] = new() { ["en"] = "Step 4: Blend", ["ja"] = "ステップ4: ブレンド", ["hi"] = "4: まぜる" },
            ["BlendSource"] = new() { ["en"] = "Original", ["ja"] = "オリジナル", ["hi"] = "もと" },
            ["BlendTarget"] = new() { ["en"] = "WriteBack", ["ja"] = "書き戻し", ["hi"] = "かきもどし" },
            ["BlendWeight"] = new() { ["en"] = "Blend Weight", ["ja"] = "ブレンド量", ["hi"] = "まぜぐあい" },
            ["CreateBlended"] = new() { ["en"] = "Create Blended Mesh", ["ja"] = "ブレンドメッシュを作成", ["hi"] = "まぜたずけいをつくる" },
            ["BlendRequiresBoth"] = new() { ["en"] = "Blend requires both Original and WriteBack meshes.", ["ja"] = "ブレンドにはオリジナルと書き戻しメッシュの両方が必要です。", ["hi"] = "りょうほうひつようだよ" },

            // Cleanup
            ["Cleanup"] = new() { ["en"] = "Cleanup", ["ja"] = "クリーンアップ", ["hi"] = "おそうじ" },
            ["DeleteBaked"] = new() { ["en"] = "Delete Baked", ["ja"] = "ベイク削除", ["hi"] = "やいたのをけす" },
            ["DeleteWriteBack"] = new() { ["en"] = "Delete WriteBack", ["ja"] = "書き戻し削除", ["hi"] = "もどしたのをけす" },
            ["DeleteBoth"] = new() { ["en"] = "Delete Both", ["ja"] = "両方削除", ["hi"] = "りょうほうけす" },

            // Workflow Status
            ["WorkflowStatus"] = new() { ["en"] = "Workflow Status", ["ja"] = "ワークフロー状態", ["hi"] = "じょうたい" },

            // メッセージ
            ["SelectMeshToBake"] = new() { ["en"] = "Select a mesh to bake mirror.", ["ja"] = "ミラーを実体化するメッシュを選択してください。", ["hi"] = "ずけいをえらんでね" },
            ["NoMeshSelected"] = new() { ["en"] = "No mesh selected.", ["ja"] = "メッシュが選択されていません。", ["hi"] = "えらんでないよ" },
            ["EmptyMesh"] = new() { ["en"] = "Mesh is empty.", ["ja"] = "メッシュが空です。", ["hi"] = "からっぽだよ" },
            ["BakeFailed"] = new() { ["en"] = "Bake failed.", ["ja"] = "ベイクに失敗しました。", ["hi"] = "しっぱいしたよ" },
            ["BakeSuccess"] = new() { ["en"] = "Baked: {0} → {1} vertices", ["ja"] = "ベイク完了: {0} → {1} 頂点", ["hi"] = "できた: {0} → {1} てん" },
            ["NoBakeResult"] = new() { ["en"] = "No bake result. Please bake first.", ["ja"] = "ベイク結果がありません。先にベイクしてください。", ["hi"] = "さきにやいてね" },
            ["SelectBakedMesh"] = new() { ["en"] = "Please select the baked mesh.", ["ja"] = "ベイクしたメッシュを選択してください。", ["hi"] = "やいたずけいをえらんでね" },
            ["SourceMeshNotFound"] = new() { ["en"] = "Source mesh '{0}' not found.", ["ja"] = "元のメッシュ '{0}' が見つかりません。", ["hi"] = "'{0}' がないよ" },
            ["WriteBackFailed"] = new() { ["en"] = "Write back failed.", ["ja"] = "書き戻しに失敗しました。", ["hi"] = "しっぱいしたよ" },
            ["WriteBackSuccess"] = new() { ["en"] = "Write back complete: {0}", ["ja"] = "書き戻し完了: {0}", ["hi"] = "できた: {0}" },

            // Blend messages
            ["NoContext"] = new() { ["en"] = "Context not available.", ["ja"] = "コンテキストがありません。", ["hi"] = "じゅんびできてないよ" },
            ["BlendMeshNotFound"] = new() { ["en"] = "Source or WriteBack mesh not found.", ["ja"] = "オリジナルまたは書き戻しメッシュが見つかりません。", ["hi"] = "ずけいがみつからないよ" },
            ["BlendVertexMismatch"] = new() { ["en"] = "Vertex count mismatch: {0} vs {1}", ["ja"] = "頂点数が一致しません: {0} vs {1}", ["hi"] = "てんすうがちがうよ: {0} vs {1}" },
            ["BlendSuccess"] = new() { ["en"] = "Created: {0} ({1}%)", ["ja"] = "作成完了: {0} ({1}%)", ["hi"] = "できた: {0} ({1}%)" },

            // Step 5: Register as Morph
            ["Step5_RegisterMorph"] = new() { ["en"] = "Step 5: Register as Morph", ["ja"] = "ステップ5: モーフ登録", ["hi"] = "5: もーふにする" },
            ["LastGenerated"] = new() { ["en"] = "Last Generated", ["ja"] = "最後に生成", ["hi"] = "さいごにつくった" },
            ["MorphBase"] = new() { ["en"] = "Morph Base", ["ja"] = "モーフ基準", ["hi"] = "もーふのもと" },
            ["MorphName"] = new() { ["en"] = "Morph Name", ["ja"] = "モーフ名", ["hi"] = "もーふのなまえ" },
            ["MorphPanel"] = new() { ["en"] = "Morph Panel", ["ja"] = "モーフパネル", ["hi"] = "もーふぱねる" },
            ["RegisterAsMorph"] = new() { ["en"] = "Register as Morph", ["ja"] = "モーフとして登録", ["hi"] = "もーふにする" },
            ["MorphRequiresGenerated"] = new() { ["en"] = "Create a blended mesh first (Step 4).", ["ja"] = "先にブレンドメッシュを作成してください（ステップ4）。", ["hi"] = "さきにまぜてね" },
            ["MorphMeshNotFound"] = new() { ["en"] = "Generated or source mesh not found.", ["ja"] = "生成メッシュまたはソースメッシュが見つかりません。", ["hi"] = "ずけいがないよ" },
            ["MorphVertexMismatch"] = new() { ["en"] = "Vertex count mismatch: {0} vs {1}", ["ja"] = "頂点数が一致しません: {0} vs {1}", ["hi"] = "てんすうがちがうよ" },
            ["MorphRegistered"] = new() { ["en"] = "Registered morph: {0}", ["ja"] = "モーフ登録完了: {0}", ["hi"] = "もーふにした: {0}" },
        };

        private static string T(string key) => L.GetFrom(Texts, key);
        private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }
}

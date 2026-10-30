// ModelBundle.cs
// 外部受け渡しのモデル 1 つ分（ModelDTO の JSON ＋ embeddedTextures）を組む。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置
//
// 【形】
//   .mfproj の models[] の 1 要素と同じ ModelDTO に、材質が参照するテクスチャ画像を
//   embeddedTextures（ファイル名とバイト列。JSON では base64）として添えたもの。JSON は UTF-8 のバイト列で運ぶ。
//   書き方は ProjectSerializer.ToJson と同じ（PolyLing_外部受け渡し計画.md 2）。
// 【使う所】
//   ・リモートの照会 model_bundle（モデル 1 つ）・project_bundle と push hierarchyBundle（全モデル）
//   ・コマンド sendModelToClient（クライアントへの要求 putModel）
//   ・クライアントからのコマンド putModel（モデルの受け入れ。FromBytes → ToModel）

using System.Text;
using Poly_Ling.Context;

namespace Poly_Ling.Serialization
{
    public static class ModelBundle
    {
        /// <summary>モデルを ModelDTO にし、テクスチャ画像を embeddedTextures に載せる。モデルが無ければ null。</summary>
        public static ModelDTO BuildDto(ModelContext model)
        {
            var dto = ModelSerializer.FromModelContext(model);
            if (dto == null) return null;
            AttachTextures(dto, model);
            return dto;
        }

        /// <summary>
        /// 材質が参照するテクスチャ画像を dto.embeddedTextures に載せる（JSON はパスしか持たないため）。
        /// BuildDto と、プロジェクト丸ごとの ProjectSerializer.FromProjectContext(embedTextures: true) が使う。
        /// </summary>
        public static void AttachTextures(ModelDTO dto, ModelContext model)
        {
            if (dto == null || model == null) return;
            dto.embeddedTextures = ModelTextureTransfer.ToEmbedded(ModelTextureTransfer.Collect(model));
        }

        /// <summary>モデル 1 つ分の JSON の UTF-8 バイト列。モデルが無ければ null。</summary>
        public static byte[] ToBytes(ModelContext model)
        {
            var dto = BuildDto(model);
            if (dto == null) return null;
            return Encoding.UTF8.GetBytes(ProjectSerializer.ToJson(dto) ?? "");
        }

        /// <summary>
        /// モデル 1 つ分の JSON の UTF-8 バイト列を ModelDTO に戻す（ToBytes の逆）。
        /// 空なら null。JSON として読めなければ例外をそのまま投げる。
        /// </summary>
        public static ModelDTO FromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            return ProjectSerializer.ModelFromJson(Encoding.UTF8.GetString(bytes));
        }

        /// <summary>
        /// ModelDTO からモデルを組み、embeddedTextures のテクスチャを材質へ当てる。DTO が無ければ null。
        /// 呼ぶたびに新しい ModelContext を作る（置き換えの Undo・Redo でも使う）。
        /// </summary>
        public static ModelContext ToModel(ModelDTO dto)
        {
            if (dto == null) return null;
            var model = ModelSerializer.ToModelContext(dto);
            if (model == null) return null;
            ModelTextureTransfer.Apply(model, dto.embeddedTextures);
            return model;
        }
    }
}

// Assets/Editor/Poly_Ling/Serialization/ProjectDTO.cs
// プロジェクトファイル (.mfproj) のシリアライズ用データ構造
// v1.0: 初期バージョン

using System;
using System.Collections.Generic;

namespace Poly_Ling.Serialization
{
    // ================================================================
    // プロジェクトファイル全体
    // ================================================================

    /// <summary>
    /// プロジェクトファイルのルートデータ
    /// </summary>
    [Serializable]
    public class ProjectDTO
    {
        /// <summary>ファイルフォーマットバージョン</summary>
        public string version = "1.0";

        /// <summary>プロジェクト名</summary>
        public string name;

        /// <summary>作成日時（ISO 8601形式）</summary>
        public string createdAt;

        /// <summary>更新日時（ISO 8601形式）</summary>
        public string modifiedAt;

        // ================================================================
        // モデルリスト
        // ================================================================

        /// <summary>モデルリスト</summary>
        public List<ModelDTO> models = new List<ModelDTO>();

        // ================================================================
        // 将来用：モーションリスト
        // ================================================================
        // public List<MotionData> motions = new List<MotionData>();

        // ================================================================
        // 将来用：テクスチャリスト
        // ================================================================
        // public List<TextureData> textures = new List<TextureData>();

        // ================================================================
        // ファクトリメソッド
        // ================================================================

        public static ProjectDTO Create(string projectName)
        {
            var now = DateTime.Now.ToString("o");
            return new ProjectDTO
            {
                version = "1.0",
                name = projectName,
                createdAt = now,
                modifiedAt = now
            };
        }

        /// <summary>
        /// 更新日時を現在時刻に設定
        /// </summary>
        public void UpdateModifiedAt()
        {
            modifiedAt = DateTime.Now.ToString("o");
        }
    }
}

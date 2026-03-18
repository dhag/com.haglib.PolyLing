// Assets/Editor/Poly_Ling/Core/Update/DirtyLevel.cs
// 更新レベル定義
// データ更新の階層を管理するためのフラグ

using System;

namespace Poly_Ling.Core
{
    /// <summary>
    /// 更新レベル（カスケード: 上位レベルは下位を含む）
    /// 
    /// Level 0: None      - 何も変更なし（再描画のみ）
    /// Level 1: Mouse     - マウス位置変更（ヒットテスト、ホバー）
    /// Level 2: Camera    - カメラパラメータ変更（MVP行列、カリング、スクリーン座標）
    /// Level 3: Selection - 選択状態変更（選択フラグ）
    /// Level 4: Transform - 頂点位置変更（位置、法線、バウンディングボックス）
    /// Level 5: Topology  - トポロジー変更（全バッファ再構築）
    /// </summary>
    [Flags]
    public enum DirtyLevel
    {
        /// <summary>変更なし</summary>
        None = 0,

        /// <summary>Level 1: マウス位置変更（ヒットテスト、ホバー）</summary>
        Mouse = 1 << 0,

        /// <summary>Level 2: カメラパラメータ変更</summary>
        Camera = 1 << 1,

        /// <summary>Level 3: 選択状態変更</summary>
        Selection = 1 << 2,

        /// <summary>Level 4: 頂点位置変更（トポロジー不変）</summary>
        Transform = 1 << 3,

        /// <summary>Level 5: トポロジー変更（全再構築）</summary>
        Topology = 1 << 4,

        // ============================================================
        // 複合フラグ
        // ============================================================

        /// <summary>全て</summary>
        All = Topology | Transform | Selection | Camera | Mouse,

        /// <summary>描画関連のみ（トポロジー以外）</summary>
        RenderOnly = Transform | Selection | Camera | Mouse,

        /// <summary>インタラクション関連（マウス＋カメラ）</summary>
        Interaction = Camera | Mouse,
    }

    /// <summary>
    /// DirtyLevel拡張メソッド
    /// </summary>
    public static class DirtyLevelExtensions
    {
        /// <summary>
        /// 指定フラグが含まれているか
        /// </summary>
        public static bool Has(this DirtyLevel level, DirtyLevel flag)
        {
            return (level & flag) != 0;
        }

        /// <summary>
        /// フラグを追加
        /// </summary>
        public static DirtyLevel With(this DirtyLevel level, DirtyLevel flag)
        {
            return level | flag;
        }

        /// <summary>
        /// フラグを除去
        /// </summary>
        public static DirtyLevel Without(this DirtyLevel level, DirtyLevel flag)
        {
            return level & ~flag;
        }

        /// <summary>
        /// 最高レベルを取得（0-5）
        /// </summary>
        public static int GetHighestLevel(this DirtyLevel level)
        {
            if (level.Has(DirtyLevel.Topology)) return 5;
            if (level.Has(DirtyLevel.Transform)) return 4;
            if (level.Has(DirtyLevel.Selection)) return 3;
            if (level.Has(DirtyLevel.Camera)) return 2;
            if (level.Has(DirtyLevel.Mouse)) return 1;
            return 0;
        }

        /// <summary>
        /// レベル名を取得
        /// </summary>
        public static string GetLevelName(this DirtyLevel level)
        {
            int highest = level.GetHighestLevel();
            return highest switch
            {
                5 => "Topology",
                4 => "Transform",
                3 => "Selection",
                2 => "Camera",
                1 => "Mouse",
                _ => "None"
            };
        }
    }
}

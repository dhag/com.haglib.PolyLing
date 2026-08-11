// Runtime/Poly_Ling_Main/Tools/ObjectArray/ObjectArrayParams.cs
// 歪み複製（オブジェクトリストを歪ませながら複数組つくる）のパラメータ。
// Runtime/Poly_Ling_Main/Tools/ObjectArray/ に配置

using UnityEngine;

namespace Poly_Ling.Tools.ObjectArray
{
    /// <summary>生成物の置き場所。</summary>
    public enum ObjectArrayOutputMode
    {
        /// <summary>出力先オブジェクトの子として、別々の描画オブジェクトで生成する（既定）。</summary>
        AsChild = 0,

        /// <summary>
        /// 出力先オブジェクトの中身（頂点・面）へ統合する。
        /// 出力先がルートのときは統合先が無いので新規オブジェクトを1つ作る。
        /// </summary>
        Inside = 1,
    }

    /// <summary>歪み複製のパラメータ。</summary>
    public class ObjectArrayParams
    {
        /// <summary>複製する組の数。1 なら元と同じ配置に歪みだけ掛かった1組を作る。</summary>
        public int Count = 2;

        /// <summary>
        /// 複製 i の位相 = PhaseStepDeg × i。
        /// デフォーマが IDeformerPhase を実装していないときは無視される。
        /// </summary>
        public float PhaseStepDeg = 90f;

        /// <summary>複製 i の位置ずらし = OffsetStep × i。作業軸ローカル。</summary>
        public Vector3 OffsetStep = Vector3.zero;

        /// <summary>置き場所。</summary>
        public ObjectArrayOutputMode OutputMode = ObjectArrayOutputMode.AsChild;

        /// <summary>出力先オブジェクトの MasterIndex。-1 でルート。</summary>
        public int TargetMasterIndex = -1;

        /// <summary>
        /// 生成物の名前。空なら元オブジェクト名を使う。
        /// どちらの場合も末尾に組番号が付き、最終的な一意化は呼び出し側が行う。
        /// </summary>
        public string NameBase = "";

        /// <summary>
        /// 組ごとに空の親オブジェクトを作り、その組の生成物をすべて子にする。
        /// 複製元が1本のときも包む。
        /// 「中に生成」は全部を1メッシュへ統合するので親に意味が無く、無視される。
        /// </summary>
        public bool GroupEachCopy = true;

        /// <summary>空の親の名前の素。末尾に組番号が付く。</summary>
        public string GroupNameBase = "Group";

        /// <summary>
        /// 上端（作業軸ローカル +Y の最大側）を固定する。既定 true。
        /// 歪みを上から下へ数えるのと同じ結果になり、上端は動かず、
        /// それ以外の位置は上端からの相対で決まる。
        /// 実装は「上端での変位を全頂点から引く」で、X / Y / Z の全成分を引く。
        /// </summary>
        public bool FixOrigin = true;

        public ObjectArrayParams Clone()
            => new ObjectArrayParams
            {
                Count             = Count,
                PhaseStepDeg      = PhaseStepDeg,
                OffsetStep        = OffsetStep,
                OutputMode        = OutputMode,
                TargetMasterIndex = TargetMasterIndex,
                NameBase          = NameBase,
                GroupEachCopy     = GroupEachCopy,
                GroupNameBase     = GroupNameBase,
                FixOrigin         = FixOrigin,
            };
    }
}

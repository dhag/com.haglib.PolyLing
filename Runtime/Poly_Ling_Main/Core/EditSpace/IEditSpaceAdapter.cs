// IEditSpaceAdapter.cs
// 作業空間（モデルとは別の空間にある、主に 2 次元のデータをビルボード上で編集する枠組み）の
// 種類ごとの差を受け持つ口。
// Runtime/Poly_Ling_Main/Core/EditSpace/ に配置
//
// 【作業空間とは】
//   元データ（UV、回転体の断面など）を代理メッシュとして作業板（ビルボード）上に置き、
//   既存の編集ツールで編集して元データへ戻す仕組み（PolyLing_UV_Billboard_Design.md 4 章）。
//   開始・反映・取消し・終了・ロックは種類によらず共通で、差はここに集める。
//
// 【Undo はここで扱わない】
//   反映・取消しは元データ・代理メッシュを書き換えるだけ。前後のスナップショットと記録は
//   呼び出し側（Viewer）が行う。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.EditSpace
{
    /// <summary>平面制約の方針（設計方針 5.2）。</summary>
    public enum EditSpacePlaneConstraint
    {
        /// <summary>必須。ロックを解除しても外せない（UV）。</summary>
        Required = 0,

        /// <summary>既定でオン。外すと 3D の点を扱える（回転体・パイプの断面）。</summary>
        DefaultOn = 1,
    }

    /// <summary>下絵の供給元（設計方針 8.2）。</summary>
    public enum EditSpaceBackdropSource
    {
        /// <summary>対象マテリアルのテクスチャ。作業板の UV 0〜1 に固定し、保存しない（UV）。</summary>
        SourceMaterialTexture = 0,

        /// <summary>利用者が置く参考画像。モデル内に保存する（プロファイル系）。</summary>
        UserImage = 1,
    }

    /// <summary>作業空間の種類ごとの差。</summary>
    public interface IEditSpaceAdapter
    {
        /// <summary>種類の名前（"uv" など）。コマンドの戻り値にも出す。</summary>
        string Kind { get; }

        /// <summary>画面に出す種類名。</summary>
        string DisplayName { get; }

        /// <summary>平面制約の方針。</summary>
        EditSpacePlaneConstraint PlaneConstraint { get; }

        /// <summary>下絵の供給元。</summary>
        EditSpaceBackdropSource BackdropSource { get; }

        /// <summary>
        /// 表示単位 1 あたりの代理ローカル長さ（設計方針 5.3 の単位換算）。X・Y 別。
        /// 数値入力は軸ごとにこの値で割って表示する。マグネット半径は X の値で割る。
        /// UV なら作業倍率 (sU, sV)。画像の縦横比で表示するときは sU ≠ sV。
        /// </summary>
        Vector2 DisplayUnitScale { get; }

        /// <summary>
        /// 代理が作業中だけの一時オブジェクトか（設計方針 9.1・9.2）。
        /// true（UV）：開いてから閉じるまでを履歴の範囲とし、閉じるときに代理を消して範囲の中の記録を除く。
        ///   開いている間は保存・読込・モデル切替を断る。
        /// false（断面系）：代理はモデルに常設のオブジェクト。作業中の編集は通常の履歴に残し、
        ///   閉じても消さない。保存も断らない。
        /// </summary>
        bool TemporaryProxy { get; }

        /// <summary>
        /// 代理が元データから動いているか。
        /// source は元データを持つ描画オブジェクト（UV）。元データがオブジェクトでない種類（断面系）は null。
        /// </summary>
        bool HasChanges(MeshContext source, MeshContext proxy);

        /// <summary>代理を元データの状態へ戻す。失敗理由を返す（成功なら null）。</summary>
        string Reset(MeshContext source, MeshContext proxy);

        /// <summary>
        /// 代理を元データへ反映する。検査を全部通ったときだけ書く。
        /// 失敗理由を返す（成功なら null）。problemSourceFaces に示すべき元の面番号、
        /// problemProxyFaces に選択して示すべき代理の面番号を入れる（設計方針 6.5）。
        /// </summary>
        string Apply(MeshContext source, MeshContext proxy,
                     List<int> problemSourceFaces, List<int> problemProxyFaces);
    }
}

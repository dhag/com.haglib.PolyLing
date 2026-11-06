// IMeshModifier.cs
// 処理列（1 つのオブジェクトに処理を積み重ねる非破壊の列）の「処理の種類」1 つぶんの契約。
// Runtime/Poly_Ling_Main/Core/Modifiers/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 3.2（段 1）
// 【作者向けの手順】PolyLing_モディファイア作成ガイド.md
//
// 【作者が書くもの】
//   クラス 1 つに、PLMeshModifier 属性（種類名・表示名・分類・変える範囲）、
//   PLParam を付けた引数のプロパティ（読み書きできるもの）、Evaluate（計算本体）。
//   コマンド・保存・引数欄・評価のきっかけは枠組みが受け持つ（段 2 以降）。
//   発見は MeshModifierRegistry が読み込まれている全アセンブリから属性で行う。
//
// 【PLModifierAttribute とは別物】
//   PLModifierAttribute（Core/Data/PLModifierAttribute.cs）は PanelCommand の型に付け、
//   生成コマンドがモディファイアスタックへどう記録されるかを宣言する。
//   PLMeshModifierAttribute は IMeshModifier の型に付け、処理列の種類を宣言する。
//   読む側はどちらも型を名指しで GetCustomAttribute するので、互いに拾い合わない。

using System;
using System.Collections.Generic;
using Poly_Ling.Data;
using UnityEngine;

namespace Poly_Ling.Modifiers
{
    /// <summary>種類が変える範囲。</summary>
    public enum MeshModifierChange
    {
        /// <summary>頂点の位置だけを変える（頂点数・面構成は入力のまま）。ドラッグ中も追随できる。</summary>
        PositionsOnly = 0,

        /// <summary>面構成も変える。ドラッグを離したときに作り直す。</summary>
        Topology = 1,
    }

    /// <summary>
    /// 処理列の種類の宣言。IMeshModifier を実装したクラスに 1 つ付ける。
    /// 付ける相手は PanelCommand ではない（PLModifierAttribute とは別物。ファイル頭の注記）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PLMeshModifierAttribute : Attribute
    {
        /// <summary>種類名。処理列の項目の Action に入る。全種類で一意。</summary>
        public string Kind { get; }

        /// <summary>表示名。空なら種類名を使う。</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>分類（任意）。種類の一覧の並べ分けに使う。</summary>
        public string Category { get; set; } = "";

        /// <summary>変える範囲。</summary>
        public MeshModifierChange Change { get; set; } = MeshModifierChange.Topology;

        /// <summary>説明（任意）。</summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// 末尾固定（段 8）。true の種類は処理列に 1 つだけ置け、いつも列の最後に居て並べ替えできない。
        /// 追加は普通の種類をこの項目の前へ入れ、並べ替えはこの項目を動かさず、他の項目をこの後ろへ置かない。
        /// 例：「ミラー（表示）」（結果オブジェクトから派生するミラー側を作るので、列の最後の形に掛かる）。
        /// </summary>
        public bool PinnedLast { get; set; } = false;

        /// <summary>モディファイアの追加メニューに出さない（試験用の種類など）。コマンドからは足せる。</summary>
        public bool HideFromMenu { get; set; } = false;

        /// <summary>全体にだけ掛かる種類（ブーリアン・ミラー・サブディビジョン）。頂点グループの欄を出さない。</summary>
        public bool WholeMeshOnly { get; set; } = false;

        public PLMeshModifierAttribute(string kind) { Kind = kind ?? ""; }
    }

    /// <summary>計算本体に渡すもの。計算本体はどれも書き換えない。</summary>
    public sealed class MeshModifierInput
    {
        /// <summary>入力のメッシュ（前の項目の結果、先頭なら元メッシュ）。書き換えない。</summary>
        public MeshObject Source;

        /// <summary>解決済みの処理範囲。Source の索引で持つ。</summary>
        public ResolvedMeshModifierScope Scope;

        /// <summary>
        /// 参照するオブジェクト（引数のキー → 解決済みのメッシュ）。
        /// 埋めるのは評価器（段 3）。参照を持たない種類では空。
        /// </summary>
        public IReadOnlyDictionary<string, MeshObject> References
            = new Dictionary<string, MeshObject>();

        /// <summary>
        /// 参照ごとの「参照のローカル（頂点の格納空間）→ 元メッシュのローカル」の行列（References と同じキー）。
        /// 元.BindWorldToVertexMatrix × 参照.VertexToBindWorldMatrix（作り直しと同じバインド空間。段 7b）。
        /// 参照の位置を元メッシュの空間で読む種類（ブーリアンなど）は、References の頂点にこれを掛ける。
        /// 処理列の Source はどの項目でも元メッシュのローカルなので、この行列はどの項目にも使える。
        /// 枠組みは相手や元メッシュのオブジェクトを動かすと（この行列が変わると）流し直す。
        /// </summary>
        public IReadOnlyDictionary<string, Matrix4x4> ReferenceToSource
            = new Dictionary<string, Matrix4x4>();
    }

    /// <summary>処理列の種類 1 つ。引数は PLParam を付けたプロパティで持つ。</summary>
    public interface IMeshModifier
    {
        /// <summary>
        /// 入力から出力のメッシュを作って返す。入力・モデル・選択・画面には触れない。
        /// 処理範囲の外は入力のまま通すこと。失敗したら null を返し、error に理由を入れる。
        /// </summary>
        MeshObject Evaluate(MeshModifierInput input, out string error);
    }

    /// <summary>
    /// 位置だけの種類が任意で持つ速い口（ドラッグ中の追随用）。
    /// 頂点数を変えない前提で、src（入力の位置）から dst（出力の位置）を書く。
    /// dst は src と同じ長さで、呼ぶ側が src の写しを入れて渡す。
    /// </summary>
    public interface IMeshModifierPositions
    {
        bool EvaluatePositions(MeshModifierInput input, Vector3[] src, Vector3[] dst, out string error);
    }

    /// <summary>
    /// 面構成を変える種類が任意で持つ、ドラッグ中の追随の口（段 8）。
    /// 最後の Evaluate で控えた対応（サブディビジョンのステンシル、ミラー実体化の頂点の出自など）で、
    /// 入力の位置 src（最後の Evaluate の入力と同じ頂点数）から出力の位置 dst（最後の Evaluate の出力と
    /// 同じ頂点数）を求める。面構成は最後の Evaluate のままとみなす。
    /// 枠組みは Evaluate を呼んだ同じインスタンスにこれを呼ぶ（項目の控えにインスタンスが残る）。
    /// 控えが無い・数が合わないときは false を返す（枠組みはその回の書き込みをやめ、離したときに作り直す）。
    /// 位置だけの種類は IMeshModifierPositions を使い、これを持たない。
    /// </summary>
    public interface IMeshModifierDragFollow
    {
        bool FollowPositions(Vector3[] src, out Vector3[] dst);
    }

    /// <summary>
    /// 追加メニューから足した直後の既定値（操作統合計画 段 3）。足した直後に形が変わる値を、入力の大きさに合わせて書く。
    /// プロパティの初期値は「キーの無い保存データ・シナリオの読み方」なので、そこでは変えない
    /// （MeshModifierRegistry.FromArgs はキーが無ければ初期値のまま。ToArgs は全キーを書く）。
    /// addMeshModifier の useAddDefaults=true（モディファイアパネルの追加）のときだけ呼ばれる。
    /// input は足す位置の入力（処理列があれば今の結果、無ければ元メッシュ）で、書き換えない。
    /// </summary>
    public interface IMeshModifierAddDefaults
    {
        void ApplyAddDefaults(MeshObject input);
    }

    /// <summary>
    /// 今の設定では形が変わらない種類が、次に何をすればよいかを出す口（カードに出す）。
    /// 他のアプリにも足しただけでは形が変わらないものがある（Maya のラティス、3ds Max の XForm）。形が変わる設定なら null。
    /// </summary>
    public interface IMeshModifierHint
    {
        string GetHint();
    }
}

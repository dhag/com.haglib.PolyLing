// PanelCommand.SpringBone.cs
// 揺れもの（VRM SpringBone）のオーサリングの操作要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間。PanelCommand.cs から分割）

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Ops;
using Poly_Ling.Tools.SpringBoneRig;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Data
{
    // ================================================================
    // 揺れもの（VRM SpringBone）のオーサリング
    // ================================================================
    //
    // 格納規約は MeshObject.cs「ボーン付帯データ格納規約」、
    // 実処理は Core/Ops/SpringBoneOps.cs を正典とする。
    //
    // 付帯先はボーンに限らない。階層に載るノード（ボーン、および
    // 非スキンドの描画オブジェクト）なら揺れデータを持てる。

    /// <summary>揺れの評価設定（モデル全体で 1 組）を変える。</summary>
    [PLCommand(Description = "揺れものの評価設定（固定タイムステップ・安定化フレーム数）を変える。")]
    public class SetSpringBoneSettingsCommand : PanelCommand
    {
        [PLParam(TextKey = "SpringBoneFixedDeltaTime",
                 Description = "揺れ評価の固定タイムステップ[秒]。0 にすると実時間で評価する",
                 Min = 0.0, Required = true)]
        public float FixedDeltaTime { get; }

        [PLParam(TextKey = "SpringBoneWarmupFrames",
                 Description = "揺れ評価を始めた直後に空回しする安定化フレーム数",
                 Min = 0, Required = true)]
        public int WarmupFrames { get; }

        public SetSpringBoneSettingsCommand(int modelIndex, float fixedDeltaTime, int warmupFrames)
            : base(modelIndex)
        {
            FixedDeltaTime = fixedDeltaTime;
            WarmupFrames   = warmupFrames;
        }
    }

    /// <summary>コライダーグループを足す。</summary>
    [PLCommand(Description = "揺れもののコライダーグループを 1 つ足す。同じ名前があればそれを使い回す。")]
    public class AddSpringBoneColliderGroupCommand : PanelCommand
    {
        [PLParam(TextKey = "SpringBoneGroupName",
                 Description = "足すグループの名前。空にすると通し番号で作る")]
        public string GroupName { get; }

        public AddSpringBoneColliderGroupCommand(int modelIndex, string groupName = "")
            : base(modelIndex)
        {
            GroupName = groupName;
        }
    }

    /// <summary>コライダーグループの名前を変える。</summary>
    [PLCommand(Description = "揺れもののコライダーグループの名前を変える。")]
    public class RenameSpringBoneColliderGroupCommand : PanelCommand
    {
        [PLParam(TextKey = "SpringBoneGroupIndex",
                 Description = "対象グループの索引", Min = 0, Required = true)]
        public int GroupIndex { get; }

        [PLParam(TextKey = "SpringBoneGroupNewName",
                 Description = "新しい名前", Required = true)]
        public string NewName { get; }

        public RenameSpringBoneColliderGroupCommand(int modelIndex, int groupIndex, string newName)
            : base(modelIndex)
        {
            GroupIndex = groupIndex;
            NewName    = newName;
        }
    }

    /// <summary>
    /// コライダーグループを消す。所属していたコライダー・チェーンの
    /// 参照索引はすべて詰め直される。
    /// </summary>
    [PLCommand(Description = "揺れもののコライダーグループを消す。参照している索引はすべて詰め直す。")]
    public class DeleteSpringBoneColliderGroupCommand : PanelCommand
    {
        [PLParam(TextKey = "SpringBoneGroupIndex",
                 Description = "消すグループの索引", Min = 0, Required = true)]
        public int GroupIndex { get; }

        public DeleteSpringBoneColliderGroupCommand(int modelIndex, int groupIndex)
            : base(modelIndex)
        {
            GroupIndex = groupIndex;
        }
    }

    /// <summary>
    /// 指定ノードを揺れチェーンの起点にする。
    /// ジョイントが付いていなければ既定値で同時に付ける。
    /// </summary>
    [PLCommand(Description = "指定したノードを揺れチェーンの起点にする。ジョイントが無ければ同時に付ける。")]
    public class SetSpringBoneChainRootCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "起点にするノードの masterIndex", Required = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "SpringBoneChainName",
                 Description = "チェーンの名前。空にするとノード名を使う")]
        public string ChainName { get; }

        [PLParam(TextKey = "SpringBoneCenterBoneName",
                 Description = "慣性の基準にするボーンの名前。空にするとワールド空間で評価する")]
        public string CenterBoneName { get; }

        [PLParam(TextKey = "SpringBoneColliderGroupIndices",
                 Description = "衝突させるコライダーグループの索引配列。空にすると衝突しない")]
        public int[] ColliderGroupIndices { get; }

        public SetSpringBoneChainRootCommand(
            int modelIndex, int masterIndex,
            string chainName = "", string centerBoneName = "", int[] colliderGroupIndices = null)
            : base(modelIndex)
        {
            MasterIndex          = masterIndex;
            ChainName            = chainName;
            CenterBoneName       = centerBoneName;
            ColliderGroupIndices = colliderGroupIndices;
        }
    }

    /// <summary>揺れチェーンの起点指定を外す。ジョイントは残る。</summary>
    [PLCommand(Description = "揺れチェーンの起点指定を外す。ジョイントは残る。")]
    public class ClearSpringBoneChainRootCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象ノードの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        public ClearSpringBoneChainRootCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex)
        {
            MasterIndices = masterIndices;
        }
    }

    /// <summary>選んだノードへ揺れジョイントを付ける（既にあれば値を上書きする）。</summary>
    [PLCommand(Description = "選んだノードへ揺れジョイントを付ける。既に付いていれば値を上書きする。")]
    public class SetSpringBoneJointCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象ノードの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "SpringBoneHitRadius",
                 Description = "当たり判定の半径", Min = 0.0, Required = true)]
        public float HitRadius { get; }

        [PLParam(TextKey = "SpringBoneStiffnessForce",
                 Description = "初期姿勢へ戻ろうとする力", Min = 0.0, Required = true)]
        public float StiffnessForce { get; }

        [PLParam(TextKey = "SpringBoneGravityPower",
                 Description = "重力の強さ。見た目の調整値で物理量ではない", Required = true)]
        public float GravityPower { get; }

        [PLParam(TextKey = "SpringBoneGravityDir",
                 Description = "重力の向き。長さ 0 を渡すと真下になる", Required = true)]
        public Vector3 GravityDir { get; }

        [PLParam(TextKey = "SpringBoneDragForce",
                 Description = "減衰。1 で完全に止まる", Min = 0.0, Max = 1.0, Required = true)]
        public float DragForce { get; }

        [PLParam(TextKey = "SpringBoneAngleLimitType",
                 Description = "揺れの向きを制限する形。None / Cone / Hinge / Spherical")]
        public SpringBoneAngleLimitType AngleLimitType { get; }

        // Quaternion は PanelCommandFactory が読める型に無い
        // （IsDirectlyParsable の一覧を参照）。度で持つほうが人にも読めるので、
        // ここはオイラー角[度]で受け、Quaternion への変換はディスパッチャで行う。
        [PLParam(TextKey = "SpringBoneLimitRotationEuler",
                 Description = "制限の向き。既定の向きからの回転をオイラー角[度]で指定する")]
        public Vector3 LimitRotationEuler { get; }

        [PLParam(TextKey = "SpringBonePitch",
                 Description = "制限の開き[ラジアン]。Cone/Hinge は開き角、Spherical は phi",
                 Min = 0.0, Max = 3.14159265)]
        public float Pitch { get; }

        [PLParam(TextKey = "SpringBoneYaw",
                 Description = "制限のもう一方の開き[ラジアン]。Spherical のときだけ使う",
                 Min = 0.0, Max = 1.57079633)]
        public float Yaw { get; }

        public SetSpringBoneJointCommand(
            int modelIndex, int[] masterIndices,
            float hitRadius, float stiffnessForce, float gravityPower,
            Vector3 gravityDir, float dragForce,
            SpringBoneAngleLimitType angleLimitType = SpringBoneAngleLimitType.None,
            Vector3 limitRotationEuler = default,
            float pitch = 3.14159265f, float yaw = 0f)
            : base(modelIndex)
        {
            MasterIndices  = masterIndices;
            HitRadius      = hitRadius;
            StiffnessForce = stiffnessForce;
            GravityPower   = gravityPower;
            GravityDir     = gravityDir;
            DragForce      = dragForce;
            AngleLimitType     = angleLimitType;
            LimitRotationEuler = limitRotationEuler;
            Pitch              = pitch;
            Yaw                = yaw;
        }
    }

    /// <summary>
    /// 揺れジョイントを外す。起点指定が残っていると出力できない
    /// チェーンになるため、同じノードの起点指定も一緒に外す。
    /// </summary>
    [PLCommand(Description = "選んだノードから揺れジョイントを外す。起点指定も一緒に外れる。")]
    public class ClearSpringBoneJointCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象ノードの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        public ClearSpringBoneJointCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex)
        {
            MasterIndices = masterIndices;
        }
    }

    /// <summary>
    /// 揺れチェーンの末端に子ボーンを 1 本足す。
    ///
    /// 末端のジョイントは tail 扱いで揺れないため、子の無いボーンで
    /// チェーンを終えると 1 段ぶん短くなる。その手当て。
    /// </summary>
    [PLCommand(Description = "揺れチェーンの末端に子ボーンを 1 本足す。末端は tail 扱いで揺れないための手当て。")]
    public class AddSpringBoneTailBoneCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "末端ボーンの masterIndex 配列。子ボーンを持つものは飛ばす", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "SpringBoneTailLength",
                 Description = "足すボーンの長さ[m]。0 以下にすると既定値を使う")]
        public float TailLength { get; }

        [PLParam(TextKey = "SpringBoneTailSuffix",
                 Description = "足すボーンの名前につける接尾辞。空にすると既定値を使う")]
        public string NameSuffix { get; }

        [PLParam(TextKey = "SpringBoneTailAddJoint",
                 Description = "足したボーンにも揺れジョイントを付ける。既定は true")]
        public bool AddJoint { get; }

        public AddSpringBoneTailBoneCommand(
            int modelIndex, int[] masterIndices,
            float tailLength = 0f, string nameSuffix = "", bool addJoint = true)
            : base(modelIndex)
        {
            MasterIndices = masterIndices;
            TailLength    = tailLength;
            NameSuffix    = nameSuffix;
            AddJoint      = addJoint;
        }
    }

    /// <summary>
    /// 階層を辿ってボーン列を選択する。揺れチェーンの対象を選ぶための入口。
    /// </summary>
    [PLCommand(Description = "起点から階層を辿ってノード列を選択する。揺れチェーンの対象を選ぶのに使う。")]
    [PLResult("nodes",           PLResultKind.Integer, Description = "選んだノードの数")]
    [PLResult("rootMasterIndex", PLResultKind.Integer, Description = "起点の masterIndex")]
    [PLResult("walk",            PLResultKind.Text,    Description = "使った辿り方")]
    [PLResult("additive",        PLResultKind.Flag,    Description = "既存の選択に足したか")]
    public class SelectBoneChainCommand : PanelCommand
    {
        [PLParam(TextKey = "SpringBoneChainRootIndex",
                 Description = "辿り始めるノードの masterIndex", Required = true)]
        public int RootMasterIndex { get; }

        [PLParam(TextKey = "SpringBoneChainWalk",
                 Description = "辿り方。FirstChild = 第 1 子だけの一本道 / AllDescendants = 子孫を全部",
                 Required = true)]
        public SpringBoneChainWalk Walk { get; }

        [PLParam(TextKey = "SelectAdditive",
                 Description = "今の選択へ足す。false にすると置き換える。既定は false")]
        public bool Additive { get; }

        public SelectBoneChainCommand(
            int modelIndex, int rootMasterIndex,
            SpringBoneChainWalk walk = SpringBoneChainWalk.FirstChild, bool additive = false)
            : base(modelIndex)
        {
            RootMasterIndex = rootMasterIndex;
            Walk            = walk;
            Additive        = additive;
        }
    }

    /// <summary>
    /// 描画オブジェクトの頂点に効いているボーンを選択する。
    /// 頂点選択があればその頂点だけ、無ければ全頂点を見る。
    /// </summary>
    [PLCommand(Description = "描画オブジェクトの頂点に効いているボーンを選択する。頂点選択があればその範囲だけを見る。")]
    public class SelectBonesByVertexWeightCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "SpringBoneMinWeight",
                 Description = "この値以上のウェイトを持つボーンだけを選ぶ",
                 Min = 0.0, Max = 1.0)]
        public float MinWeight { get; }

        [PLParam(TextKey = "SelectAdditive",
                 Description = "今の選択へ足す。false にすると置き換える。既定は false")]
        public bool Additive { get; }

        public SelectBonesByVertexWeightCommand(
            int modelIndex, int[] masterIndices, float minWeight = 0.01f, bool additive = false)
            : base(modelIndex)
        {
            MasterIndices = masterIndices;
            MinWeight     = minWeight;
            Additive      = additive;
        }
    }

    /// <summary>
    /// 揺れもの用のボーン鎖を置く。
    ///
    /// 作るのはボーンだけ。メッシュもウェイトも揺れ方も付けない。
    /// 揺れ方は SetSpringBoneJointCommand、先頭指定は
    /// SetSpringBoneChainRootCommand で別に送る。
    /// </summary>
    [PLCommand(Description = "揺れもの用のボーン鎖を置く。ボーンだけを作り、メッシュ・ウェイト・揺れ方は付けない。")]
    public class PlaceSpringBoneChainsCommand : PanelCommand
    {
        [PLParam(TextKey = "SpringBoneChainLayout",
                 Description = "並べ方。Single=折れ線に沿って1本 / Cylinder=まわりにN本 / Revolution=折れ線をN方向へ回す",
                 Required = true)]
        public SpringBoneChainLayout Layout { get; }

        [PLParam(TextKey = "SpringBoneAttachIndex",
                 Description = "親にするボーンの masterIndex。-1 で親を付けない")]
        public int AttachMasterIndex { get; }

        [PLParam(TextKey = "SpringBoneNamePrefix",
                 Description = "作るボーンの名前の接頭辞", Required = true)]
        public string NamePrefix { get; }

        /// <summary>
        /// 位置の基準にするボーンの masterIndex。-1 でワールド原点。
        ///
        /// 親とは別に持つ。親子関係を変えてもワールド位置は変わらないのが
        /// 当たり前なので、あとから親を付け替えて位置を直すことはできない。
        /// 作る時点で正しい場所に置くために、基準だけを別に指定する。
        /// </summary>
        [PLParam(TextKey = "SpringBoneOriginIndex",
                 Description = "位置の基準にするボーンの masterIndex。-1 でワールド原点。親にはしない")]
        public int OriginMasterIndex { get; }

        [PLParam(TextKey = "SpringBoneChainCount",
                 Description = "鎖の本数。Single では 1 として扱う", Min = 1)]
        public int ChainCount { get; }

        [PLParam(TextKey = "SpringBoneSegments",
                 Description = "1 本あたりの段数。Cylinder でのみ使う", Min = 1)]
        public int Segments { get; }

        [PLParam(TextKey = "SpringBoneTopRadius",
                 Description = "上端の半径[m]。Cylinder でのみ使う", Min = 0.0)]
        public float TopRadius { get; }

        [PLParam(TextKey = "SpringBoneBottomRadius",
                 Description = "下端の半径[m]。上端と変えると円錐になる。Cylinder でのみ使う", Min = 0.0)]
        public float BottomRadius { get; }

        [PLParam(TextKey = "SpringBoneChainHeight",
                 Description = "上端から下端までの高さ[m]。Cylinder でのみ使う", Min = 0.0)]
        public float Height { get; }

        [PLParam(TextKey = "SpringBoneStartAngle",
                 Description = "1 本目を置く角度[度]")]
        public float StartAngleDeg { get; }

        [PLParam(TextKey = "SpringBoneProfile",
                 Description = "折れ線。X が水平距離、Y が高さ（下が負）。Single / Revolution で使う")]
        public Vector2[] Profile { get; }

        [PLParam(TextKey = "SpringBoneAddTail",
                 Description = "鎖の先に短いボーンを 1 本足す。鎖の先は tail 扱いで揺れないための手当て")]
        public bool AddTailBone { get; }

        [PLParam(TextKey = "SpringBoneTailLength",
                 Description = "足すボーンの長さ[m]。0 以下で既定値", Min = 0.0)]
        public float TailLength { get; }

        public PlaceSpringBoneChainsCommand(
            int modelIndex,
            SpringBoneChainLayout layout,
            int attachMasterIndex,
            string namePrefix,
            int originMasterIndex = -1,
            int chainCount = 1,
            int segments = 5,
            float topRadius = 0.12f,
            float bottomRadius = 0.45f,
            float height = 0.6f,
            float startAngleDeg = 0f,
            Vector2[] profile = null,
            bool addTailBone = true,
            float tailLength = 0.05f)
            : base(modelIndex)
        {
            Layout            = layout;
            AttachMasterIndex = attachMasterIndex;
            NamePrefix        = namePrefix;
            OriginMasterIndex = originMasterIndex;
            ChainCount        = chainCount;
            Segments          = segments;
            TopRadius         = topRadius;
            BottomRadius      = bottomRadius;
            Height            = height;
            StartAngleDeg     = startAngleDeg;
            Profile           = profile;
            AddTailBone       = addTailBone;
            TailLength        = tailLength;
        }
    }

    /// <summary>
    /// ボーンの親を付け替える。ワールド位置は保つ。
    ///
    /// 揺れもの用に作った鎖を、あとから既存のボーンへ繋ぐために要る。
    /// </summary>
    [PLCommand(Description = "ボーンの親を付け替える。ワールド位置は保つ。")]
    public class SetBoneParentCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "親を変えるボーンの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "BoneNewParentIndex",
                 Description = "新しい親の masterIndex。-1 でモデル直下", Required = true)]
        public int ParentMasterIndex { get; }

        public SetBoneParentCommand(int modelIndex, int[] masterIndices, int parentMasterIndex)
            : base(modelIndex)
        {
            MasterIndices     = masterIndices;
            ParentMasterIndex = parentMasterIndex;
        }
    }

    /// <summary>
    /// 揺れものの当たり判定（collider）をボーンへ 1 つ足す。
    /// </summary>
    [PLCommand(Description = "揺れものの当たり判定をボーンへ 1 つ足す。")]
    public class AddSpringBoneColliderCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "付ける先のボーンの masterIndex", Required = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "SpringBoneColliderShape",
                 Description = "形。Sphere / Capsule / Plane など", Required = true)]
        public SpringBoneColliderShape Shape { get; }

        [PLParam(TextKey = "SpringBoneColliderOffset",
                 Description = "付ける先ボーンのローカル座標での中心", Required = true)]
        public Vector3 Offset { get; }

        [PLParam(TextKey = "SpringBoneColliderRadius",
                 Description = "半径[m]", Min = 0.0, Required = true)]
        public float Radius { get; }

        [PLParam(TextKey = "SpringBoneColliderTail",
                 Description = "カプセルのもう一方の端。ローカル座標")]
        public Vector3 Tail { get; }

        [PLParam(TextKey = "SpringBoneColliderNormal",
                 Description = "平面の法線")]
        public Vector3 Normal { get; }

        [PLParam(TextKey = "SpringBoneColliderGroupIndices",
                 Description = "所属する当たり判定のまとまりの索引配列")]
        public int[] GroupIndices { get; }

        public AddSpringBoneColliderCommand(
            int modelIndex, int masterIndex,
            SpringBoneColliderShape shape, Vector3 offset, float radius,
            Vector3 tail = default, Vector3 normal = default, int[] groupIndices = null)
            : base(modelIndex)
        {
            MasterIndex  = masterIndex;
            Shape        = shape;
            Offset       = offset;
            Radius       = radius;
            Tail         = tail;
            Normal       = normal;
            GroupIndices = groupIndices;
        }
    }

    /// <summary>
    /// 既にある当たり判定（collider）を書き換える。
    ///
    /// 【なぜ足したか】
    ///   当たり判定は足すことしかできず、半径や位置を直すには一度消して
    ///   作り直すしかなかった。消す手段も無かったため、間違えると
    ///   プロジェクトを作り直すことになる。
    /// </summary>
    [PLCommand(Description = "ボーンに付いている当たり判定を 1 つ書き換える。")]
    public class UpdateSpringBoneColliderCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "当たり判定が付いているボーンの masterIndex", Required = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "SpringBoneColliderIndex",
                 Description = "そのボーンが持つ当たり判定の何番目か（0 始まり）",
                 Min = 0, Required = true)]
        public int ColliderIndex { get; }

        [PLParam(TextKey = "SpringBoneColliderShape",
                 Description = "形。Sphere / Capsule / Plane など", Required = true)]
        public SpringBoneColliderShape Shape { get; }

        [PLParam(TextKey = "SpringBoneColliderOffset",
                 Description = "付いているボーンのローカル座標での中心", Required = true)]
        public Vector3 Offset { get; }

        [PLParam(TextKey = "SpringBoneColliderRadius",
                 Description = "半径[m]", Min = 0.0, Required = true)]
        public float Radius { get; }

        [PLParam(TextKey = "SpringBoneColliderTail",
                 Description = "カプセルのもう一方の端。ローカル座標")]
        public Vector3 Tail { get; }

        [PLParam(TextKey = "SpringBoneColliderNormal",
                 Description = "平面の法線")]
        public Vector3 Normal { get; }

        [PLParam(TextKey = "SpringBoneColliderGroupIndices",
                 Description = "所属する当たり判定のまとまりの索引配列")]
        public int[] GroupIndices { get; }

        public UpdateSpringBoneColliderCommand(
            int modelIndex, int masterIndex, int colliderIndex,
            SpringBoneColliderShape shape, Vector3 offset, float radius,
            Vector3 tail = default, Vector3 normal = default, int[] groupIndices = null)
            : base(modelIndex)
        {
            MasterIndex   = masterIndex;
            ColliderIndex = colliderIndex;
            Shape         = shape;
            Offset        = offset;
            Radius        = radius;
            Tail          = tail;
            Normal        = normal;
            GroupIndices  = groupIndices;
        }
    }

    /// <summary>
    /// 当たり判定（collider）を 1 つ消す。
    /// 同じボーンの後ろの当たり判定は 1 つずつ前へ詰まる。
    /// </summary>
    [PLCommand(Description = "ボーンに付いている当たり判定を 1 つ消す。後ろの番号は詰まる。")]
    public class DeleteSpringBoneColliderCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "当たり判定が付いているボーンの masterIndex", Required = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "SpringBoneColliderIndex",
                 Description = "消す当たり判定が何番目か（0 始まり）",
                 Min = 0, Required = true)]
        public int ColliderIndex { get; }

        public DeleteSpringBoneColliderCommand(int modelIndex, int masterIndex, int colliderIndex)
            : base(modelIndex)
        {
            MasterIndex   = masterIndex;
            ColliderIndex = colliderIndex;
        }
    }

    [PLCommand(Description = "控えておいた T ポーズをモデルへ適用する。")]
    public class ApplyTPoseCommand : PanelCommand
    {
        public ApplyTPoseCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>バックアップから元の姿勢に戻す</summary>
    [PLCommand(Description = "バックアップから元の姿勢に戻す</summary>")]
    public class RestoreTPoseCommand : PanelCommand
    {
        public RestoreTPoseCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>現在の姿勢をベースとしてバックアップを破棄する（Undo不可）</summary>
    [PLCommand(Description = "現在の姿勢をベースとしてバックアップを破棄する（Undo不可）</summary>")]
    public class BakeTPoseCommand : PanelCommand
    {
        public BakeTPoseCommand(int modelIndex) : base(modelIndex) { }
    }
}

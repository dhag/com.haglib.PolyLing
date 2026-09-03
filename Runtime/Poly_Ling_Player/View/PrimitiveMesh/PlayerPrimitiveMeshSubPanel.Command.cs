// PlayerPrimitiveMeshSubPanel.Command.cs
// 図形生成サブパネル：パネルの状態から図形生成コマンドを組み立てる。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置
//
// 【なぜコマンドにするか】
//   生成をコマンド経由にすると、パネルのボタンも自動検証も MCP も同じ経路を通る。
//   Ops を直接叩く経路が残っていると、ディスパッチャ側の欠陥が検査を素通りする。
//
// 【生成そのものはここではしない】
//   コマンド → MeshObject の変換は Poly_Ling.PrimitiveMesh.PrimitiveMeshFactory。
//   モデルへの反映（追加先の解決・Undo・再構築）はディスパッチャ側。
//   このファイルは「パネル状態 → コマンド」の一方向の写像だけを持つ。
//
// 【プレビューも同じコマンドを通す】
//   Generate() はここで組んだコマンドを PrimitiveMeshFactory へ渡す。
//   図形種別の分岐がパネルとファクトリの 2 箇所に分かれないようにする。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Frill;
using Poly_Ling.Pipe;
using Poly_Ling.PlaceObject;
using Poly_Ling.Profile2DExtrude;   // Profile2DParams.LoopData

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        /// <summary>コマンド送信。パネルが押されたときと同じ経路へ流す。</summary>
        public System.Action<PanelCommand> SendCommand;

        /// <summary>現在のモデル索引。コマンドに載せる。未配線なら 0。</summary>
        public System.Func<int> GetModelIndex;

        private int ModelIndex() => GetModelIndex?.Invoke() ?? 0;

        // ================================================================
        // 配置
        // ================================================================

        /// <summary>
        /// 配置と後処理の指定を組む。
        /// 回転・拡大の焼き込みは生の指定を渡し、
        /// 「既存へ追加は無条件に焼き込む」の解釈はコマンド側が持つ
        /// （CreatePrimitiveMeshCommand.BakeRotationEffective）。
        /// </summary>
        private PrimitivePlacement CurrentPlacement()
            => new PrimitivePlacement
            {
                WorldPosition          = _worldPos,
                PlaceRotation          = _rotEuler,
                PlaceScale             = _scale,
                BakeRotation           = _bakeRotation,
                BakeScale              = _bakeScale,
                IgnorePoseInArmature   = false,
                AddMode                = _addMode,
                AddTargetIndex         = _addTargetIndex,
                MaterialIndex          = EffectiveMaterialIndex,
                MergeDuplicateVertices = _mergeDuplicateVertices,
            };

        /// <summary>
        /// 回転・拡大を頂点へ入れない配置。ピボットの「重心」ボタンが、
        /// 姿勢の影響を受けない素の形状で重心を測るために使う。
        /// </summary>
        private PrimitivePlacement NeutralPlacement()
        {
            var p = CurrentPlacement();
            p.PlaceRotation = Vector3.zero;
            p.PlaceScale    = Vector3.one;
            return p;
        }

        // ================================================================
        // コマンド組み立て
        // ================================================================

        /// <summary>
        /// 現在の図形種別に対応する生成コマンドを組む。
        /// 穴つなぎと歪み複製は単一メッシュを返さないので null（別コマンドを使う）。
        /// </summary>
        public CreatePrimitiveMeshCommand BuildCreateCommand()
            => BuildCreateCommand(CurrentPlacement());

        private CreatePrimitiveMeshCommand BuildCreateCommand(PrimitivePlacement pl)
        {
            int mi = ModelIndex();

            switch (_current)
            {
                case ShapeKind.Cube:         return new CreateCubeCommand(mi, _cubeP, pl);
                case ShapeKind.Sphere:       return new CreateSphereCommand(mi, _sphereP, pl);
                case ShapeKind.Cylinder:     return new CreateCylinderCommand(mi, _cylP, pl);
                case ShapeKind.Capsule:      return new CreateCapsuleCommand(mi, _capsP, pl);
                case ShapeKind.Plane:        return new CreatePlaneCommand(mi, _planeP, pl);
                case ShapeKind.Pyramid:      return new CreatePyramidCommand(mi, _pyramidP, pl);
                case ShapeKind.StadiumBox:   return new CreateStadiumBoxCommand(mi, _stadiumP, pl);
                case ShapeKind.PipeStadium:  return new CreatePipeStadiumCommand(mi, _pipeStadiumP, pl);
                case ShapeKind.HairStrand:   return BuildHairStrandCommand(mi, pl);
                case ShapeKind.NGonGear:     return new CreateNGonGearCommand(mi, _ngonGearP, pl);
                case ShapeKind.NGonStar:     return new CreateNGonStarCommand(mi, _ngonStarP, pl);
                case ShapeKind.InvoluteGear: return new CreateInvoluteGearCommand(mi, _involGearP, pl);
                case ShapeKind.Ribbon:       return new CreateRibbonBowCommand(mi, _ribbonP, pl);
                case ShapeKind.NohMask:      return new CreateNohMaskCommand(mi, _nohP, pl);
                case ShapeKind.Text:         return new CreateTextMeshCommand(mi, _textP, pl);

                case ShapeKind.Revolution:   return BuildRevolutionCommand(mi, pl);
                case ShapeKind.Profile2D:    return BuildProfile2DCommand(mi, pl);

                case ShapeKind.Frill:        return BuildFrillCommand(mi, pl);
                case ShapeKind.Pipe:         return BuildPipeCommand(mi, pl);
                case ShapeKind.PlaceObject:  return BuildPlaceObjectCommand(mi, pl);

                // 穴つなぎは書き込み先の既存頂点を参照する面を足す（CreateHoleBridgeCommand）。
                // 歪み複製はモデルへ直接オブジェクトを挿入する（CreateObjectArrayCommand）。
                case ShapeKind.Bridge:
                case ShapeKind.ObjectArray:
                default:
                    return null;
            }
        }

        /// <summary>
        /// 髪の房。幅配分は配列なので、パネルが持つ実体をそのまま載せると
        /// コマンドを作った後のスライダ操作がコマンド側の値まで書き換えてしまう。
        /// 回転体・2D 押し出しと同じく、写してから載せる。
        /// </summary>
        private CreatePrimitiveMeshCommand BuildHairStrandCommand(int mi, PrimitivePlacement pl)
        {
            var p = _hairP;
            int n = Mathf.Clamp(p.LobeCount,
                Poly_Ling.HairStrand.HairStrandParams.LobeCountMin,
                Poly_Ling.HairStrand.HairStrandParams.LobeCountMax);

            var arr = new float[n];
            for (int i = 0; i < n; i++)
                arr[i] = (_hairP.LobeWidths != null && i < _hairP.LobeWidths.Length)
                    ? _hairP.LobeWidths[i]
                    : Poly_Ling.HairStrand.HairStrandParams.LobeWidthMin;
            p.LobeWidths = arr;

            return new CreateHairStrandCommand(mi, p, pl);
        }

        /// <summary>
        /// 回転体。編集中のプロファイルは _revProfile が持つので、
        /// コマンドに載せる前に RevolutionParams.Profile へ写す。
        /// </summary>
        private CreatePrimitiveMeshCommand BuildRevolutionCommand(int mi, PrimitivePlacement pl)
        {
            EnsureRevProfile();

            var p = _revP;
            p.Profile = _revProfile != null ? _revProfile.ToArray() : new Vector2[0];
            return new CreateRevolutionCommand(mi, p, pl);
        }

        /// <summary>
        /// 2D 押し出し。編集中のループは _p2dLoops が持つので、
        /// コマンドに載せる前に Profile2DParams.Loops へ写す。
        /// </summary>
        private CreatePrimitiveMeshCommand BuildProfile2DCommand(int mi, PrimitivePlacement pl)
        {
            EnsureP2DLoops();

            var p = _p2dP;
            if (_p2dLoops == null)
            {
                p.Loops = new Profile2DParams.LoopData[0];
            }
            else
            {
                var arr = new Profile2DParams.LoopData[_p2dLoops.Count];
                for (int i = 0; i < _p2dLoops.Count; i++)
                    arr[i] = new Profile2DParams.LoopData(_p2dLoops[i]);
                p.Loops = arr;
            }
            return new CreateProfile2DCommand(mi, p, pl);
        }

        private CreatePrimitiveMeshCommand BuildFrillCommand(int mi, PrimitivePlacement pl)
        {
            EnsureBeltProfile(_frillEdit);
            EnsureBeltProfile(_frillEditB);

            return new CreateFrillCommand(
                mi, _frillP,
                _frillEdit?.Points?.ToArray(),
                _frillEditB?.Points?.ToArray(),
                BeltsToCsv(_frillBelts).ToArray(),
                ToOrientOptions(_frillOrient),
                ToSplineOptions(_frillSpline),
                pl);
        }

        private CreatePrimitiveMeshCommand BuildPipeCommand(int mi, PrimitivePlacement pl)
        {
            EnsureBeltProfile(_pipeEdit);

            return new CreatePipeCommand(
                mi, _pipeP,
                _pipeEdit?.Points?.ToArray(),
                _pipeEdit != null && _pipeEdit.ClosedLoop,
                BeltsToCsv(_pipeBelts).ToArray(),
                ToOrientOptions(_pipeOrient),
                ToSplineOptions(_pipeSpline),
                pl);
        }

        private CreatePrimitiveMeshCommand BuildPlaceObjectCommand(int mi, PrimitivePlacement pl)
            => new CreatePlaceObjectCommand(
                mi, _placeP,
                _placeSrcPick != null ? _placeSrcPick.SelectedMasterIndices().ToArray() : new int[0],
                BeltsToCsv(_placeBelts).ToArray(),
                ToOrientOptions(_placeOrient),
                ToSplineOptions(_placeSpline),
                pl);

        /// <summary>パネル内部の向き補正 → コマンドに載せる形。</summary>
        private static BeltOrientOptions ToOrientOptions(BeltOrientOption o)
            => o == null
                ? BeltOrientOptions.Default
                : new BeltOrientOptions { SwapSides = o.SwapSides, ReverseOrder = o.ReverseOrder };

        /// <summary>パネル内部のスプライン設定 → コマンドに載せる形。</summary>
        private static BeltSplineOptions ToSplineOptions(BeltSplineOption o)
            => o == null
                ? BeltSplineOptions.Default
                : new BeltSplineOptions
                {
                    Enabled   = o.Enabled,
                    Segments  = o.Segments,
                    UseFirst  = o.UseFirst,
                    UseLast   = o.UseLast,
                    TrimStart = o.TrimStart,
                    TrimEnd   = o.TrimEnd,
                };

        // ================================================================
        // 出来上がったメッシュをそのまま置く
        // ================================================================

        /// <summary>
        /// プロファイル編集の「メッシュへ反映」のように、図形パラメータから決まらない
        /// メッシュをモデルへ置く。姿勢は ApplyPoseForDirectMeshCreate で頂点へ入れ済みなので、
        /// ディスパッチャ側で入れ直させない。
        /// </summary>
        private void SendGeneratedMesh(MeshObject mo, string meshName)
        {
            if (mo == null) return;
            if (SendCommand == null) { _statusLabel.text = "配線が足りません（SendCommand）"; return; }

            SendCommand(new AddGeneratedMeshCommand(
                ModelIndex(), mo, meshName, CurrentPlacement(), poseAlreadyBaked: true));
        }

        // ================================================================
        // 穴つなぎ
        // ================================================================

        /// <summary>
        /// 現在の種と設定から穴つなぎコマンドを組む。
        /// 種が片方でも未取込なら null。
        /// </summary>
        public CreateHoleBridgeCommand BuildHoleBridgeCommand()
        {
            if (!BridgeSeedsReady) return null;

            return new CreateHoleBridgeCommand(
                ModelIndex(),
                BridgeSeedMeshIndexA, BridgeSeedVertexA,
                BridgeSeedMeshIndexB, BridgeSeedVertexB,
                BridgeMeshName,
                _addMode, _addTargetIndex,
                _bridgeFlipCorresp, _bridgeFlipFaces, _bridgeSubdiv,
                BridgeSeedDirHintA, BridgeSeedDirHintB,
                // パネル経路では取り込みのたびに ApplyBridgeAutoFlags が走って
                // チェックボックスへ書き戻してある。その結果を載せるので再判定はしない。
                autoFlags: false);
        }

        /// <summary>
        /// コマンドの内容をパネルの穴つなぎ状態へ入れる。
        /// 生成そのものは呼出し側が GenerateBridge() で行う。
        ///
        /// パネルのボタン経路では、直前に自分が組んだ値がそのまま戻るので実質何も変わらない。
        /// 自動検証や MCP から来たときだけ、種と設定が実際に差し替わる。
        /// </summary>
        public void ApplyHoleBridgeCommand(CreateHoleBridgeCommand cmd)
        {
            if (cmd == null) return;

            ApplyBridgePick(_bridgeA, new HoleSeedPick
            {
                Ok = true, MeshIndex = cmd.MeshA, Vertex = cmd.VertexA, DirectionHint = cmd.DirectionHintA,
            });
            ApplyBridgePick(_bridgeB, new HoleSeedPick
            {
                Ok = true, MeshIndex = cmd.MeshB, Vertex = cmd.VertexB, DirectionHint = cmd.DirectionHintB,
            });

            SetBridgeName(cmd.Name);
            _bridgeSubdiv      = cmd.Subdivisions;
            _addMode           = cmd.AddMode;
            _addTargetIndex    = cmd.AddTargetIndex;

            if (cmd.AutoFlags)
            {
                // 両穴の巻き方向から決め直す。コマンドの値では上書きしない。
                // ここで上書きすると、面が裏返ったりねじれたりしたまま張られる。
                ApplyBridgeAutoFlags();
            }
            else
            {
                _bridgeFlipCorresp = cmd.FlipCorrespondence;
                _bridgeFlipFaces   = cmd.FlipFaces;
                _bridgeFlipCorrespToggle?.SetValueWithoutNotify(cmd.FlipCorrespondence);
                _bridgeFlipFacesToggle?.SetValueWithoutNotify(cmd.FlipFaces);
            }
        }
    }
}

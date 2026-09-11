// PlayerPrimitiveMeshSubPanel.Shapes.cs
// 図形生成サブパネル：図形種別・カテゴリの登録と、図形の選択・諸元 UI の切り替え。
// 図形を足す・カテゴリを移す（サンドボックスからの昇格を含む）ときに触るのはこのファイル。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Revolution;
using Poly_Ling.Profile2DExtrude;
using Poly_Ling.NohMask;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Core;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 図形種別
        // ================================================================

        // 末尾に足すこと。PrimitiveShapeMemory は列挙値の「名前」で保存するので値の並びは
        // 保存内容に影響しないが、_shapeBtns は添字を (int)ShapeKind で引く。
        public enum ShapeKind { Cube, Sphere, Cylinder, Capsule, Plane, Pyramid, Revolution, Profile2D, NohMask, Frill, Pipe, PlaceObject, ObjectArray, Text, Bridge, Ribbon, NGonGear, NGonStar, InvoluteGear, StadiumBox, PipeStadium, HairStrand,
                               HelicalGear, InternalGear, InvoluteRack, HelicalRack, StraightBevelGear, SpiralBevelGear, CylindricalWorm, WormWheel,
                               SpringBoneSingle, SpringBoneCylinder, SpringBoneRevolution,
                               EdgeRibbonFace, SpringBoneLadder,
                               // ── MCP用サンドボックス（SandboxShapes にだけ載せる）
                               McpCylinder,
                               // ── 点指定図形（高度な図形）
                               PointDefined }

        private static readonly string[] ShapeKeys =
            { "Cube","Sphere","Cylinder","Capsule","Plane","Pyramid","Revolution","Profile2D","NohMask","Frill","Pipe","PlaceObject","ObjectArray","Text","Bridge","Ribbon",
              "NGonGear","NGonStar","InvoluteGear","StadiumBox","PipeStadium","HairStrand",
              "HelicalGear","InternalGear","InvoluteRack","HelicalRack","StraightBevelGear","SpiralBevelGear","CylindricalWorm","WormWheel",
              "SpringBoneSingle","SpringBoneCylinder","SpringBoneRevolution",
              "EdgeRibbonFace","SpringBoneLadder",
              "McpCylinder",
              "PointDefined" };

        /// <summary>
        /// 図形カテゴリ（左ペインの「基本図形」/「高度な図形」/「機構部品」/「揺れものボーン」、
        /// および「MCP用サンドボックス」に対応）。
        /// </summary>
        public enum ShapeCategory { Basic, Advanced, Mechanism, SpringBone, Sandbox }

        // カテゴリ別の図形リスト。グリッドはこの内容だけを表示する。
        private static readonly ShapeKind[] BasicShapes =
            { ShapeKind.Cube, ShapeKind.Sphere, ShapeKind.Cylinder, ShapeKind.Capsule, ShapeKind.Plane, ShapeKind.Pyramid,
              ShapeKind.StadiumBox };
        private static readonly ShapeKind[] AdvancedShapes =
            { ShapeKind.Revolution, ShapeKind.Profile2D, ShapeKind.NohMask, ShapeKind.Frill, ShapeKind.Pipe, ShapeKind.Ribbon,
              ShapeKind.NGonGear, ShapeKind.NGonStar,
              ShapeKind.PipeStadium, ShapeKind.HairStrand,
              ShapeKind.PlaceObject, ShapeKind.ObjectArray, ShapeKind.Text, ShapeKind.Bridge,
              ShapeKind.EdgeRibbonFace, ShapeKind.PointDefined };

        // 揺れもの用のボーン鎖。作るのはボーンで、メッシュではない。
        //   「回転体」と同じくプロファイル（断面の折れ線）を持ち、
        //   同じプロファイルエディタをそのまま使う。
        private static readonly ShapeKind[] SpringBoneShapes =
            { ShapeKind.SpringBoneSingle, ShapeKind.SpringBoneCylinder, ShapeKind.SpringBoneRevolution,
              ShapeKind.SpringBoneLadder };

        // 機構部品。かみ合う歯車まわりをここへ集める。
        // インボリュート歯車は「高度な図形」からここへ移した。
        private static readonly ShapeKind[] MechanismShapes =
            { ShapeKind.InvoluteGear, ShapeKind.HelicalGear, ShapeKind.InternalGear,
              ShapeKind.InvoluteRack, ShapeKind.HelicalRack,
              ShapeKind.StraightBevelGear, ShapeKind.SpiralBevelGear,
              ShapeKind.CylindricalWorm, ShapeKind.WormWheel };

        // MCP用サンドボックス。試作中の図形はここにだけ載せる。
        // 登録（ShapeKind・ShapeKeys・RebuildSettings・Name/SetName・BuildCreateCommand）は
        // 本番の図形と同じ書き方で済ませてあるので、本番への昇格は
        // この配列から BasicShapes / AdvancedShapes などへ移すだけで済む。
        private static readonly ShapeKind[] SandboxShapes =
            { ShapeKind.McpCylinder };

        // ================================================================
        // パラメータ
        // ================================================================

        private ShapeKind _current = ShapeKind.Cube;
        private ShapeCategory _category = ShapeCategory.Basic;

        // カテゴリ別に「最後に選んだ図形」を保持する。パネルを開き直したときは
        // カテゴリ先頭ではなくこの値を選び直す。MemoryKey があれば起動をまたいで
        // PrimitiveShapeMemory（JSON）にも保存する。
        private ShapeKind _lastBasic     = ShapeKind.Cube;
        private ShapeKind _lastAdvanced  = ShapeKind.Revolution;
        private ShapeKind _lastSpringBone = ShapeKind.SpringBoneCylinder;
        private ShapeKind _lastMechanism = ShapeKind.InvoluteGear;
        private ShapeKind _lastSandbox   = ShapeKind.McpCylinder;

        /// <summary>
        /// 最後に選んだ図形の保存に使うパネル識別子。Build より前に設定する。
        /// 空のときはファイル保存を行わず、起動中のみの記憶になる。
        /// </summary>
        public string MemoryKey { get; set; }

        // ================================================================
        // 図形選択
        // ================================================================

        // 現在カテゴリに属する図形リスト。
        private ShapeKind[] CurrentCategoryShapes() => ShapesOf(_category);

        private static ShapeKind[] ShapesOf(ShapeCategory cat)
        {
            switch (cat)
            {
                case ShapeCategory.Advanced:   return AdvancedShapes;
                case ShapeCategory.Mechanism:  return MechanismShapes;
                case ShapeCategory.SpringBone: return SpringBoneShapes;
                case ShapeCategory.Sandbox:    return SandboxShapes;
                default:                       return BasicShapes;
            }
        }

        // 現在カテゴリの図形だけでボタングリッドを再構築する。
        // Build と SetCategory で共用（ボタン生成コードはここ1箇所）。
        private void PopulateShapeGrid()
        {
            if (_shapeGrid == null) return;
            _shapeGrid.Clear();
            for (int i = 0; i < _shapeBtns.Length; i++) _shapeBtns[i] = null;

            foreach (var kind in CurrentCategoryShapes())
            {
                int idx = (int)kind;
                var btn = new Button(() => Select((ShapeKind)idx)) { text = T(ShapeKeys[idx]) };
                btn.style.width = new StyleLength(new Length(33.3f, LengthUnit.Percent));
                btn.style.height = 26; btn.style.marginBottom = 2; btn.style.fontSize = 10;
                _shapeBtns[idx] = btn;
                _shapeGrid.Add(btn);
            }
        }

        /// <summary>
        /// カテゴリを切り替える。グリッドを再構築し、そのカテゴリ先頭の図形を選択する。
        /// 左ペインの「基本図形」/「高度な図形」ボタンから呼ぶ。
        /// </summary>
        public void SetCategory(ShapeCategory cat)
        {
            _category = cat;
            PopulateShapeGrid();
            Select(RememberedShape(cat));
        }

        /// <summary>
        /// 指定カテゴリで最後に選んだ図形。記憶値がそのカテゴリに属さない場合は
        /// カテゴリ先頭へフォールバックする。
        /// </summary>
        private ShapeKind RememberedShape(ShapeCategory cat)
        {
            var shapes = ShapesOf(cat);

            ShapeKind kind;
            switch (cat)
            {
                case ShapeCategory.Advanced:   kind = _lastAdvanced;   break;
                case ShapeCategory.Mechanism:  kind = _lastMechanism;  break;
                case ShapeCategory.SpringBone: kind = _lastSpringBone; break;
                case ShapeCategory.Sandbox:    kind = _lastSandbox;    break;
                default:                       kind = _lastBasic;      break;
            }

            if (System.Array.IndexOf(shapes, kind) >= 0) return kind;
            return shapes.Length > 0 ? shapes[0] : ShapeKind.Cube;
        }

        /// <summary>
        /// 保存済みの「最後に選んだ図形」を読み込む。MemoryKey が空のときは何もしない。
        /// </summary>
        private void LoadShapeMemory()
        {
            var basic = PrimitiveShapeMemory.Get(MemoryKey, ShapeCategory.Basic);
            if (basic.HasValue && System.Array.IndexOf(BasicShapes, basic.Value) >= 0)
                _lastBasic = basic.Value;

            var adv = PrimitiveShapeMemory.Get(MemoryKey, ShapeCategory.Advanced);
            if (adv.HasValue && System.Array.IndexOf(AdvancedShapes, adv.Value) >= 0)
                _lastAdvanced = adv.Value;

            var mech = PrimitiveShapeMemory.Get(MemoryKey, ShapeCategory.Mechanism);
            if (mech.HasValue && System.Array.IndexOf(MechanismShapes, mech.Value) >= 0)
                _lastMechanism = mech.Value;

            var sb = PrimitiveShapeMemory.Get(MemoryKey, ShapeCategory.SpringBone);
            if (sb.HasValue && System.Array.IndexOf(SpringBoneShapes, sb.Value) >= 0)
                _lastSpringBone = sb.Value;

            var sbx = PrimitiveShapeMemory.Get(MemoryKey, ShapeCategory.Sandbox);
            if (sbx.HasValue && System.Array.IndexOf(SandboxShapes, sbx.Value) >= 0)
                _lastSandbox = sbx.Value;
        }

        /// <summary>指定形状のカテゴリを返す。</summary>
        public ShapeCategory CategoryOf(ShapeKind k)
        {
            if (System.Array.IndexOf(SandboxShapes,    k) >= 0) return ShapeCategory.Sandbox;
            if (System.Array.IndexOf(SpringBoneShapes, k) >= 0) return ShapeCategory.SpringBone;
            if (System.Array.IndexOf(MechanismShapes,  k) >= 0) return ShapeCategory.Mechanism;
            if (System.Array.IndexOf(AdvancedShapes,   k) >= 0) return ShapeCategory.Advanced;
            return ShapeCategory.Basic;
        }

        /// <summary>
        /// 指定形状のサブメニューを開く (形状ボタンをクリックしたのと同じ)。
        /// 必要ならカテゴリを切り替えてグリッドを再構築してから選択する。生成はしない。
        /// </summary>
        public void SelectShape(ShapeKind k)
        {
            var cat = CategoryOf(k);
            if (_category != cat)
            {
                _category = cat;
                PopulateShapeGrid();
            }
            Select(k);
        }

        private void Select(ShapeKind k)
        {
            _current = k;

            // 選んだ図形をカテゴリ別に記憶する（次にパネルを開いたときの復元用）。
            var cat = CategoryOf(k);
            switch (cat)
            {
                case ShapeCategory.Advanced:   _lastAdvanced   = k; break;
                case ShapeCategory.Mechanism:  _lastMechanism  = k; break;
                case ShapeCategory.SpringBone: _lastSpringBone = k; break;
                case ShapeCategory.Sandbox:    _lastSandbox    = k; break;
                default:                       _lastBasic      = k; break;
            }
            PrimitiveShapeMemory.Set(MemoryKey, cat, k);

            for (int i = 0; i < _shapeBtns.Length; i++)
            {
                if (_shapeBtns[i] == null) continue;
                _shapeBtns[i].style.backgroundColor = (int)k == i
                    ? new StyleColor(new Color(0.25f, 0.45f, 0.65f))
                    : new StyleColor(new Color(0.25f, 0.25f, 0.25f));
            }
            // 名前欄を組み立てる前に非重複候補へ更新する。
            RefreshMeshNameCandidate();
            RebuildSettings();
            RefreshCommonUiVisibility();
            // 一覧はモデルが変わると古くなるので、図形を選び直すたびに取り直す。
            RefreshMaterialChoices();
            _dirty = true;

            // ブリッジへ入った / から出たときに種マーカーを出し入れする。
            OnBridgeSeedsChanged?.Invoke();

            // 点指定図形を選んだ / 外したときに 3D 操作モードを切り替えさせる。
            OnShapeSelected?.Invoke(k);
        }

        // ================================================================
        // 設定UI
        // ================================================================

        private void RebuildSettings()
        {
            // 旧 UI の TextField / ドロップダウンは破棄される。NF が呼ばれれば新しいものが入る。
            _nameField      = null;
            _addTargetField = null;

            _profileEditorContainer?.Clear();
            _settingsContainer?.Clear();
            switch (_current)
            {
                case ShapeKind.Cube:       BuildCubeUI(_settingsContainer);       break;
                case ShapeKind.Sphere:     BuildSphereUI(_settingsContainer);     break;
                case ShapeKind.Cylinder:   BuildCylinderUI(_settingsContainer);   break;
                case ShapeKind.Capsule:    BuildCapsuleUI(_settingsContainer);    break;
                case ShapeKind.Plane:      BuildPlaneUI(_settingsContainer);      break;
                case ShapeKind.Pyramid:    BuildPyramidUI(_settingsContainer);    break;
                case ShapeKind.Revolution: BuildRevolutionUI(_settingsContainer); break;
                case ShapeKind.SpringBoneSingle:
                case ShapeKind.SpringBoneCylinder:
                case ShapeKind.SpringBoneRevolution:
                    BuildSpringBoneChainUI(_settingsContainer); break;
                case ShapeKind.SpringBoneLadder:
                    BuildSpringBoneLadderUI(_settingsContainer); break;
                case ShapeKind.Profile2D:  BuildProfile2DUI(_settingsContainer);  break;
                case ShapeKind.NohMask:    BuildNohMaskUI(_settingsContainer);    break;
                case ShapeKind.Frill:      BuildFrillUI(_settingsContainer);      break;
                case ShapeKind.Pipe:       BuildPipeUI(_settingsContainer);       break;
                case ShapeKind.PlaceObject: BuildPlaceObjectUI(_settingsContainer); break;
                case ShapeKind.ObjectArray: BuildObjectArrayUI(_settingsContainer); break;
                case ShapeKind.EdgeRibbonFace: BuildEdgeRibbonFaceUI(_settingsContainer); break;
                case ShapeKind.PointDefined:   BuildPointDefinedUI(_settingsContainer);   break;
                case ShapeKind.Text:        BuildTextUI(_settingsContainer);        break;
                case ShapeKind.Bridge:      BuildBridgeUI(_settingsContainer);      break;
                case ShapeKind.Ribbon:      BuildRibbonUI(_settingsContainer);      break;
                case ShapeKind.NGonGear:     BuildNGonGearUI(_settingsContainer);     break;
                case ShapeKind.NGonStar:     BuildNGonStarUI(_settingsContainer);     break;
                case ShapeKind.InvoluteGear: BuildInvoluteGearUI(_settingsContainer); break;
                case ShapeKind.StadiumBox:   BuildStadiumBoxUI(_settingsContainer);   break;
                case ShapeKind.PipeStadium:  BuildPipeStadiumUI(_settingsContainer);  break;
                case ShapeKind.HairStrand:   BuildHairStrandUI(_settingsContainer);   break;

                // ── 機構部品（PlayerPrimitiveMeshSubPanel.Mechanism.cs） ──
                case ShapeKind.HelicalGear:       BuildHelicalGearUI(_settingsContainer);       break;
                case ShapeKind.InternalGear:      BuildInternalGearUI(_settingsContainer);      break;
                case ShapeKind.InvoluteRack:      BuildInvoluteRackUI(_settingsContainer);      break;
                case ShapeKind.HelicalRack:       BuildHelicalRackUI(_settingsContainer);       break;
                case ShapeKind.StraightBevelGear: BuildStraightBevelGearUI(_settingsContainer); break;
                case ShapeKind.SpiralBevelGear:   BuildSpiralBevelGearUI(_settingsContainer);   break;
                case ShapeKind.CylindricalWorm:   BuildCylindricalWormUI(_settingsContainer);   break;
                case ShapeKind.WormWheel:         BuildWormWheelUI(_settingsContainer);         break;

                // ── MCP用サンドボックス（View/McpSandbox/PlayerPrimitiveMeshSubPanel.*.cs） ──
                case ShapeKind.McpCylinder:       BuildMcpCylinderUI(_settingsContainer);       break;

                default:
                    var lbl = new Label(T("NotSupported"));
                    lbl.style.color = new StyleColor(new Color(0.8f, 0.5f, 0.3f));
                    lbl.style.whiteSpace = WhiteSpace.Normal;
                    _settingsContainer?.Add(lbl);
                    break;
            }
            PlayerLayoutRoot.ApplyDarkTheme(_settingsContainer);
            if (_profileEditorContainer != null)
                PlayerLayoutRoot.ApplyDarkTheme(_profileEditorContainer);

            // 暗色テーマはボタンの背景を一律に塗るので、選択中の強調はその後で付け直す。
            if (_current == ShapeKind.PointDefined) RefreshPointDefinedButtons();

            RefreshCreateButtonState();
        }
    }
}

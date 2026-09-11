// PlayerPrimitiveMeshSubPanel.Naming.cs
// 図形生成サブパネル：名前欄（重複しない名前の候補）と、既存へ追加するときの追加先選択。
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
        // コールバック
        // ================================================================

        /// <summary>
        /// 現在のモデルにある描画オブジェクト名の一覧を返す。
        /// 名前欄へ入れる非重複候補の算出に使う。未配線なら重複チェックをしない。
        /// </summary>
        public Func<List<string>> GetExistingMeshNames;

        /// <summary>現在表示中の名前欄。RebuildSettings のたびに NF が差し替える。</summary>
        private TextField _nameField;

        /// <summary>
        /// 追加先が AddToExisting のときに名前欄の代わりに出す追加先オブジェクト選択。
        /// RebuildSettings のたびに NF が差し替える。
        /// </summary>
        private DropdownField _addTargetField;

        /// <summary>_addTargetField の選択肢に対応する MeshContextList インデックス。</summary>
        private readonly List<int> _addTargetIndices = new List<int>();

        /// <summary>
        /// AddToExisting のときの追加先。-1 は「選択オブジェクトリストの先頭」。
        /// 名前欄のドロップダウンで選ぶ。
        /// </summary>
        private int _addTargetIndex = -1;

        /// <summary>
        /// 選択オブジェクトリストの先頭にある描画オブジェクトの MeshContextList インデックス。
        /// 追加先ドロップダウンの既定選択に使う。未配線・未選択は -1。
        /// </summary>
        public Func<int> GetFirstSelectedDrawableIndex;

        private string Name()
        {
            switch (_current)
            {
                case ShapeKind.Cube:       return _cubeP.MeshName;
                case ShapeKind.Sphere:     return _sphereP.MeshName;
                case ShapeKind.Cylinder:   return _cylP.MeshName;
                case ShapeKind.Capsule:    return _capsP.MeshName;
                case ShapeKind.Plane:      return _planeP.MeshName;
                case ShapeKind.Pyramid:    return _pyramidP.MeshName;
                case ShapeKind.Revolution: return _revP.MeshName;
                case ShapeKind.Profile2D:  return _p2dP.MeshName;
                case ShapeKind.NohMask:    return _nohP.MeshName;
                case ShapeKind.Frill:      return _frillP.MeshName;
                case ShapeKind.Pipe:       return _pipeP.MeshName;
                case ShapeKind.PlaceObject: return _placeP.MeshName;
                case ShapeKind.Text:       return _textP.MeshName;
                case ShapeKind.Ribbon:     return _ribbonP.MeshName;
                case ShapeKind.NGonGear:     return _ngonGearP.MeshName;
                case ShapeKind.NGonStar:     return _ngonStarP.MeshName;
                case ShapeKind.InvoluteGear: return _involGearP.MeshName;
                case ShapeKind.StadiumBox:   return _stadiumP.MeshName;
                case ShapeKind.PipeStadium:  return _pipeStadiumP.MeshName;
                case ShapeKind.HairStrand:   return _hairP.MeshName;

                case ShapeKind.HelicalGear:       return _helGearP.MeshName;
                case ShapeKind.InternalGear:      return _intGearP.MeshName;
                case ShapeKind.InvoluteRack:      return _rackP.MeshName;
                case ShapeKind.HelicalRack:       return _helRackP.MeshName;
                case ShapeKind.StraightBevelGear: return _strBevelP.MeshName;
                case ShapeKind.SpiralBevelGear:   return _spiBevelP.MeshName;
                case ShapeKind.CylindricalWorm:   return _wormP.MeshName;
                case ShapeKind.WormWheel:         return _wheelP.MeshName;

                case ShapeKind.McpCylinder:       return _mcpCylP.MeshName;
                // 歪み複製は生成物ごとに複製元名を使うため、ここでは固定名を返す。
                case ShapeKind.ObjectArray: return "ObjectArray";
                // 辺から帯面は書き込み先の既存オブジェクトへ足すだけで、名前を使わない。
                case ShapeKind.EdgeRibbonFace: return Poly_Ling.Tools.EdgeRibbonFaceTool.DefaultMeshName;
                case ShapeKind.Bridge:     return BridgeMeshName;
                default:                   return _current.ToString();
            }
        }

        /// <summary>現在の形状の名前を書き換える。</summary>
        private void SetName(string name)
        {
            switch (_current)
            {
                case ShapeKind.Cube:        _cubeP.MeshName    = name; break;
                case ShapeKind.Sphere:      _sphereP.MeshName  = name; break;
                case ShapeKind.Cylinder:    _cylP.MeshName     = name; break;
                case ShapeKind.Capsule:     _capsP.MeshName    = name; break;
                case ShapeKind.Plane:       _planeP.MeshName   = name; break;
                case ShapeKind.Pyramid:     _pyramidP.MeshName = name; break;
                case ShapeKind.Revolution:  _revP.MeshName     = name; break;
                case ShapeKind.Profile2D:   _p2dP.MeshName     = name; break;
                case ShapeKind.NohMask:     _nohP.MeshName     = name; break;
                case ShapeKind.Frill:       _frillP.MeshName   = name; break;
                case ShapeKind.Pipe:        _pipeP.MeshName    = name; break;
                case ShapeKind.PlaceObject: _placeP.MeshName   = name; break;
                case ShapeKind.Text:        _textP.MeshName    = name; break;
                case ShapeKind.Ribbon:      _ribbonP.MeshName  = name; break;
                case ShapeKind.NGonGear:     _ngonGearP.MeshName = name; break;
                case ShapeKind.NGonStar:     _ngonStarP.MeshName = name; break;
                case ShapeKind.InvoluteGear: _involGearP.MeshName = name; break;
                case ShapeKind.StadiumBox:   _stadiumP.MeshName   = name; break;
                case ShapeKind.PipeStadium:  _pipeStadiumP.MeshName = name; break;
                case ShapeKind.HairStrand:   _hairP.MeshName        = name; break;

                case ShapeKind.HelicalGear:       _helGearP.MeshName  = name; break;
                case ShapeKind.InternalGear:      _intGearP.MeshName  = name; break;
                case ShapeKind.InvoluteRack:      _rackP.MeshName     = name; break;
                case ShapeKind.HelicalRack:       _helRackP.MeshName  = name; break;
                case ShapeKind.StraightBevelGear: _strBevelP.MeshName = name; break;
                case ShapeKind.SpiralBevelGear:   _spiBevelP.MeshName = name; break;
                case ShapeKind.CylindricalWorm:   _wormP.MeshName     = name; break;
                case ShapeKind.WormWheel:         _wheelP.MeshName    = name; break;

                case ShapeKind.McpCylinder:       _mcpCylP.MeshName   = name; break;
                // 穴つなぎも非重複候補の対象にする（Name() は BridgeMeshName を返すため、
                // ここを欠かすと RefreshMeshNameCandidate が名前を書き戻せない）。
                case ShapeKind.Bridge:      SetBridgeMeshName(name); break;
            }
        }

        /// <summary>
        /// 末尾の "_数字" を落とした基底名を返す。"Frill_3" → "Frill"。
        /// 候補を作り直すたびに桁が伸びる（Frill_1_1 …）のを防ぐ。
        /// </summary>
        private static string StripNameSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            int us = name.LastIndexOf('_');
            if (us <= 0 || us == name.Length - 1) return name;

            for (int i = us + 1; i < name.Length; i++)
                if (!char.IsDigit(name[i])) return name;

            return name.Substring(0, us);
        }

        /// <summary>
        /// 既存の描画オブジェクト名と衝突しない候補を返す。
        /// 衝突していなければ入力をそのまま返す（利用者が付けた名前を勝手に削らない）。
        /// 衝突したときだけ末尾の "_数字" を落とし、_1, _2 ... を付け直す
        /// （ModelContext.GenerateUniqueMeshName と同じ規則。桁が伸び続けるのを防ぐ）。
        /// </summary>
        private string MakeUniqueMeshNameCandidate(string current)
        {
            if (string.IsNullOrEmpty(current)) return current;

            var existing = GetExistingMeshNames?.Invoke();
            if (existing == null || existing.Count == 0) return current;

            var used = new HashSet<string>(existing);
            if (!used.Contains(current)) return current;

            string baseName = StripNameSuffix(current);
            if (string.IsNullOrEmpty(baseName)) baseName = current;
            if (!used.Contains(baseName)) return baseName;

            int counter = 1;
            string name;
            do
            {
                name = $"{baseName}_{counter}";
                counter++;
            } while (used.Contains(name));

            return name;
        }

        /// <summary>
        /// 現在の形状の名前を、既存オブジェクトと重複しない候補へ更新する。
        /// 形状を選び直したときと、生成に成功した直後に呼ぶ。
        /// </summary>
        private void RefreshMeshNameCandidate()
        {
            string cur  = Name();
            string next = MakeUniqueMeshNameCandidate(cur);
            if (next == cur) return;

            SetName(next);
            // 表示中の名前欄があれば書き戻す（無ければ次の RebuildSettings が拾う）。
            _nameField?.SetValueWithoutNotify(next);
        }

        /// <summary>
        /// 名前欄。生成した TextField を控えておき、非重複候補への更新で書き戻す
        /// （設定UI 全体を作り直さずに名前だけ差し替えるため）。
        /// </summary>
        /// <summary>
        /// 名前欄。
        ///
        /// 追加先が「新しい描画オブジェクト」「新しいモデル」のときは、これから作る
        /// オブジェクトの名前を打つ TextField。
        /// 「既存の描画オブジェクトに追加」のときは、既存オブジェクトから追加先を選ぶ
        /// ドロップダウンへ差し替える（これから名前を付ける対象が無いため）。
        /// 2 つは同じ行に置き、display の切り替えだけで見せ分ける。
        /// </summary>
        private VisualElement NF(Func<string> get, Action<string> set)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 3;
            row.Add(ML(T("Name")));

            var f = new TextField { value = get() }; f.style.flexGrow = 1;
            f.RegisterValueChangedCallback(e => set(e.newValue));
            _nameField = f;
            row.Add(f);

            var dd = new DropdownField(new List<string>(), -1);
            dd.style.flexGrow = 1;
            dd.RegisterValueChangedCallback(e =>
            {
                int i = dd.index;
                _addTargetIndex = (i >= 0 && i < _addTargetIndices.Count) ? _addTargetIndices[i] : -1;
                D();
            });
            _addTargetField = dd;
            row.Add(dd);

            RefreshAddTargetChoices();
            RefreshNameFieldMode();
            return row;
        }

        /// <summary>
        /// 追加先ドロップダウンの選択肢を作り直す。
        /// 既定選択は「選択オブジェクトリストの先頭」。前回選んだ対象がまだ在れば維持する。
        /// </summary>
        private void RefreshAddTargetChoices()
        {
            if (_addTargetField == null) return;

            _addTargetIndices.Clear();
            var labels = new List<string>();

            var list = GetDrawableIndexList?.Invoke();
            if (list != null)
            {
                foreach (var (label, masterIndex) in list)
                {
                    labels.Add(label);
                    _addTargetIndices.Add(masterIndex);
                }
            }

            _addTargetField.choices = labels;

            if (labels.Count == 0)
            {
                _addTargetIndex = -1;
                _addTargetField.SetValueWithoutNotify(string.Empty);
                return;
            }

            // 選択候補は選択オブジェクトリストの先頭。前回の選択が残っていればそちらを優先。
            int want = _addTargetIndices.IndexOf(_addTargetIndex);
            if (want < 0)
            {
                int first = GetFirstSelectedDrawableIndex?.Invoke() ?? -1;
                want = _addTargetIndices.IndexOf(first);
            }
            if (want < 0) want = 0;

            _addTargetIndex = _addTargetIndices[want];
            _addTargetField.SetValueWithoutNotify(labels[want]);
        }

        /// <summary>名前欄（TextField）と追加先ドロップダウンの見せ分けを現在の追加先へ合わせる。</summary>
        private void RefreshNameFieldMode()
        {
            bool existing = _addMode == PrimitiveAddMode.AddToExisting;
            if (_nameField      != null) _nameField.style.display      = existing ? DisplayStyle.None : DisplayStyle.Flex;
            if (_addTargetField != null) _addTargetField.style.display = existing ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}

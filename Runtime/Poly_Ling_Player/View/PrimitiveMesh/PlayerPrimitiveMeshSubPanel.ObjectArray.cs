// PlayerPrimitiveMeshSubPanel.ObjectArray.cs
// 図形生成サブパネル：歪み複製（高度な図形）。
//
// 中身は PlayerObjectArraySubPanel をそのまま埋め込む（Embedded = true）。
// 「高度な図形」と「新しい高度」は同一クラスの別インスタンスなので、
// それぞれが自分の PlayerObjectArraySubPanel を1つ持ち、状態は共有しない。
//
// 【生成経路】他の図形と違い、1つの MeshObject を返さない。
//   モデルへ複数オブジェクトを挿入するため OnMeshCreated は通らず、
//   OnObjectArrayGenerate（Viewer 側の ExecuteObjectArray）へ流す。
//   3Dプレビュー / ライブワイヤは出ない（Generate() が null を返す）。
//
// 【UI の作り直し】RebuildSettings は _settingsContainer.Clear() を呼ぶが、
//   Clear は子を外すだけで VisualElement 自体は生きている。
//   ホルダーを保持して再 Add することで、チェック状態や入力値をそのまま残す。
//
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        /// <summary>描画オブジェクト一覧（表示名, MasterIndex）。複製元と出力先に使う。</summary>
        public Func<List<(string Label, int MasterIndex)>> GetDrawableIndexList;

        /// <summary>歪み複製の「生成」。パネルの状態は引数のサブパネルから読む。</summary>
        public Action<PlayerObjectArraySubPanel> OnObjectArrayGenerate;

        // ================================================================
        // 状態
        // ================================================================

        private PlayerObjectArraySubPanel _objArrayPanel;
        private VisualElement             _objArrayHolder;

        /// <summary>埋め込んでいる歪み複製サブパネル。未表示なら null。</summary>
        public PlayerObjectArraySubPanel ObjectArrayPanel => _objArrayPanel;

        // ================================================================
        // UI
        // ================================================================

        private void BuildObjectArrayUI(VisualElement c)
        {
            if (c == null) return;

            if (_objArrayHolder == null)
            {
                _objArrayHolder = new VisualElement();

                _objArrayPanel = new PlayerObjectArraySubPanel
                {
                    Embedded        = true,
                    GetDrawableList = () =>
                        GetDrawableIndexList?.Invoke() ?? new List<(string, int)>(),
                };
                _objArrayPanel.OnGenerate         = InvokeObjectArrayGenerate;
                _objArrayPanel.OnSelectionChanged = RefreshCreateButtonState;
                _objArrayPanel.Build(_objArrayHolder);
            }

            c.Add(ShapeTitle(T("ObjectArray")));
            c.Add(_objArrayHolder);

            // 一覧はモデルが変わると古くなるので、開くたびに取り直す。
            _objArrayPanel.Refresh();
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>生成ボタンから呼ぶ。挿入と Undo は Viewer 側が持つ。</summary>
        private void InvokeObjectArrayGenerate()
        {
            if (_objArrayPanel == null) return;

            if (OnObjectArrayGenerate == null)
            {
                // 行き先の設定不足ではなく、Viewer 側のコールバック未結線を指す内部エラー。
                _objArrayPanel.SetStatus("歪み複製の生成が結線されていません");
                return;
            }

            OnObjectArrayGenerate(_objArrayPanel);
        }
    }
}

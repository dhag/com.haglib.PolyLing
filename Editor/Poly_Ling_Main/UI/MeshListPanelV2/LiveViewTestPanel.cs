// LiveViewTestPanel.cs
// IProjectView統一インタフェースのテスト用パネル
// Open(IProjectView) で現物（LiveProjectView）を受け取り、カレントモデル情報を表示
// 現物でなければエラー表示

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.View;
using Poly_Ling.Data;

namespace Poly_Ling.MeshListV2
{
    public class LiveViewTestPanel : EditorWindow
    {
        private const string UxmlPackagePath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MeshListPanelV2/LiveViewTestPanel.uxml";
        private const string UxmlAssetsPath  = "Assets/Editor/Poly_Ling_Main/UI/MeshListPanelV2/LiveViewTestPanel.uxml";
        private const string UssPackagePath  = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MeshListPanelV2/LiveViewTestPanel.uss";
        private const string UssAssetsPath   = "Assets/Editor/Poly_Ling_Main/UI/MeshListPanelV2/LiveViewTestPanel.uss";

        private IProjectView _view;
        private bool _isLive;

        // UI要素
        private Label _statusLabel;
        private Label _modelName, _modelPath, _modelDirty;
        private Label _drawableCount, _boneCount, _morphCount, _totalCount;
        private Label _selDrawable, _selBone, _selMorph;
        private Label _fdName, _fdMaster, _fdVerts, _fdFaces, _fdDepth, _fdVisible;

        // ================================================================
        // 公開インタフェース
        // ================================================================

        public static LiveViewTestPanel Open(IProjectView view)
        {
            var window = GetWindow<LiveViewTestPanel>();
            window.titleContent = new GUIContent("LiveView Test");
            window.minSize = new Vector2(280, 360);
            window._view = view;
            window._isLive = view is LiveProjectView;
            window.Refresh();
            window.Show();
            return window;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void CreateGUI()
        {
            var root = rootVisualElement;
            var vt = TryLoad<VisualTreeAsset>(UxmlPackagePath, UxmlAssetsPath);
            if (vt != null) vt.CloneTree(root);
            else { root.Add(new Label($"UXML not found")); return; }

            var ss = TryLoad<StyleSheet>(UssPackagePath, UssAssetsPath);
            if (ss != null) root.styleSheets.Add(ss);

            _statusLabel    = root.Q<Label>("status-label");
            _modelName      = root.Q<Label>("model-name");
            _modelPath      = root.Q<Label>("model-path");
            _modelDirty     = root.Q<Label>("model-dirty");
            _drawableCount  = root.Q<Label>("drawable-count");
            _boneCount      = root.Q<Label>("bone-count");
            _morphCount     = root.Q<Label>("morph-count");
            _totalCount     = root.Q<Label>("total-count");
            _selDrawable    = root.Q<Label>("sel-drawable");
            _selBone        = root.Q<Label>("sel-bone");
            _selMorph       = root.Q<Label>("sel-morph");
            _fdName         = root.Q<Label>("fd-name");
            _fdMaster       = root.Q<Label>("fd-master");
            _fdVerts        = root.Q<Label>("fd-verts");
            _fdFaces        = root.Q<Label>("fd-faces");
            _fdDepth        = root.Q<Label>("fd-depth");
            _fdVisible      = root.Q<Label>("fd-visible");

            root.Q<Button>("refresh-btn")?.RegisterCallback<ClickEvent>(_ => Refresh());

            Refresh();
        }

        // ================================================================
        // 表示更新
        // ================================================================

        private void Refresh()
        {
            if (_statusLabel == null) return;

            // 現物チェック
            if (_view == null)
            {
                SetStatus("ERROR: IProjectView is null", true);
                ClearAll();
                return;
            }

            if (!_isLive)
            {
                SetStatus($"ERROR: 現物ではありません ({_view.GetType().Name})", true);
                ClearAll();
                return;
            }

            SetStatus($"OK: LiveProjectView (Project: {_view.ProjectName})", false);

            var model = _view.CurrentModel;
            if (model == null)
            {
                SL(_modelName, "Model: (null)");
                ClearModel();
                return;
            }

            // モデル基本情報
            SL(_modelName,     $"Model: {model.Name}");
            SL(_modelPath,     $"Path: {model.FilePath ?? "(none)"}");
            SL(_modelDirty,    $"Dirty: {model.IsDirty}");
            SL(_drawableCount, $"Drawable: {model.DrawableCount}");
            SL(_boneCount,     $"Bone: {model.BoneCount}");
            SL(_morphCount,    $"Morph: {model.MorphCount}");
            SL(_totalCount,    $"Total: {model.TotalMeshCount}");

            // 選択情報
            var selD = model.SelectedDrawableIndices;
            var selB = model.SelectedBoneIndices;
            var selM = model.SelectedMorphIndices;
            SL(_selDrawable, $"Drawable: [{string.Join(", ", selD ?? System.Array.Empty<int>())}]");
            SL(_selBone,     $"Bone: [{string.Join(", ", selB ?? System.Array.Empty<int>())}]");
            SL(_selMorph,    $"Morph: [{string.Join(", ", selM ?? System.Array.Empty<int>())}]");

            // 最初のDrawable
            var dList = model.DrawableList;
            if (dList != null && dList.Count > 0)
            {
                var d = dList[0];
                SL(_fdName,    $"Name: {d.Name}");
                SL(_fdMaster,  $"MasterIndex: {d.MasterIndex}");
                SL(_fdVerts,   $"Vertices: {d.VertexCount}");
                SL(_fdFaces,   $"Faces: {d.FaceCount}");
                SL(_fdDepth,   $"Depth: {d.Depth}");
                SL(_fdVisible, $"Visible: {d.IsVisible}");
            }
            else
            {
                SL(_fdName, "Name: (no drawables)");
                SL(_fdMaster, "MasterIndex: -"); SL(_fdVerts, "Vertices: -");
                SL(_fdFaces, "Faces: -"); SL(_fdDepth, "Depth: -"); SL(_fdVisible, "Visible: -");
            }
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void SetStatus(string msg, bool isError)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = msg;
            _statusLabel.EnableInClassList("error", isError);
        }

        private void ClearAll()
        {
            SL(_modelName, "Model: -"); ClearModel();
        }

        private void ClearModel()
        {
            SL(_modelPath, "Path: -"); SL(_modelDirty, "Dirty: -");
            SL(_drawableCount, "Drawable: -"); SL(_boneCount, "Bone: -");
            SL(_morphCount, "Morph: -"); SL(_totalCount, "Total: -");
            SL(_selDrawable, "Drawable: -"); SL(_selBone, "Bone: -"); SL(_selMorph, "Morph: -");
            SL(_fdName, "Name: -"); SL(_fdMaster, "MasterIndex: -"); SL(_fdVerts, "Vertices: -");
            SL(_fdFaces, "Faces: -"); SL(_fdDepth, "Depth: -"); SL(_fdVisible, "Visible: -");
        }

        private static void SL(Label l, string t) { if (l != null) l.text = t; }

        private static T TryLoad<T>(string pkg, string assets) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(pkg);
            return a != null ? a : AssetDatabase.LoadAssetAtPath<T>(assets);
        }
    }
}

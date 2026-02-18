// Assets/Editor/Poly_Ling_Main/UI/UVZPanel/UVZPanel.cs
// UVZパネル
// UV値をXY、カメラ深度をZとする新メッシュ生成 / XYZからUVへの書き戻し

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.Selection;

using Vertex = Poly_Ling.Data.Vertex;

namespace Poly_Ling.UI
{
    /// <summary>
    /// UVZPanel設定
    /// </summary>
    [Serializable]
    public class UVZPanelSettings : IToolSettings
    {
        public float UVScale = 10f;
        public float DepthScale = 1f;

        public IToolSettings Clone()
        {
            return new UVZPanelSettings
            {
                UVScale = this.UVScale,
                DepthScale = this.DepthScale,
            };
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is not UVZPanelSettings o) return true;
            return !Mathf.Approximately(UVScale, o.UVScale) ||
                   !Mathf.Approximately(DepthScale, o.DepthScale);
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is not UVZPanelSettings o) return;
            UVScale = o.UVScale;
            DepthScale = o.DepthScale;
        }
    }

    /// <summary>
    /// UVZパネル
    /// UV値をXY、カメラ平面からの深度をZとする新メッシュを生成する。
    /// また、XYZからUVへの書き戻しも行う。
    /// </summary>
    public class UVZPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel
        // ================================================================

        public override string Name => "UVZPanel";
        public override string Title => "UVZ";

        private UVZPanelSettings _settings = new UVZPanelSettings();
        public override IToolSettings Settings => _settings;

        public override string GetLocalizedTitle() => "UVZ";

        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/UVZPanel/UVZPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/UVZPanel/UVZPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/UVZPanel/UVZPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/UVZPanel/UVZPanel.uss";

        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel, _targetInfo, _cameraInfo, _statusLabel;
        private VisualElement _mainSection, _writebackSection;
        private FloatField _fieldUvScale, _fieldDepthScale;
        private PopupField<string> _writebackTarget;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<UVZPanel>();
            panel.titleContent = new GUIContent("UVZ");
            panel.minSize = new Vector2(300, 320);
            panel.SetContext(ctx);
            panel.Show();
        }

        // ================================================================
        // CreateGUI
        // ================================================================

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath)
                          ?? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPathAssets);
            if (visualTree != null)
                visualTree.CloneTree(root);
            else
            {
                root.Add(new Label($"UXML not found: {UxmlPath}"));
                return;
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath)
                          ?? AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPathAssets);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            BindUI(root);
        }

        private void BindUI(VisualElement root)
        {
            _warningLabel = root.Q<Label>("warning-label");
            _targetInfo = root.Q<Label>("target-info");
            _cameraInfo = root.Q<Label>("camera-info");
            _statusLabel = root.Q<Label>("status-label");
            _mainSection = root.Q<VisualElement>("main-section");
            _writebackSection = root.Q<VisualElement>("writeback-section");

            _fieldUvScale = root.Q<FloatField>("field-uv-scale");
            _fieldDepthScale = root.Q<FloatField>("field-depth-scale");

            if (_fieldUvScale != null)
            {
                _fieldUvScale.value = _settings.UVScale;
                _fieldUvScale.RegisterValueChangedCallback(evt =>
                {
                    _settings.UVScale = Mathf.Max(evt.newValue, 0.001f);
                });
            }

            if (_fieldDepthScale != null)
            {
                _fieldDepthScale.value = _settings.DepthScale;
                _fieldDepthScale.RegisterValueChangedCallback(evt =>
                {
                    _settings.DepthScale = Mathf.Max(evt.newValue, 0.001f);
                });
            }

            // 書き戻しターゲットPopup（UXML内にplaceholderを配置、動的に差し替え）
            var writebackTargetContainer = root.Q<VisualElement>("writeback-target-container");
            if (writebackTargetContainer != null)
            {
                _writebackTarget = new PopupField<string>("ターゲット", new List<string> { "(なし)" }, 0);
                _writebackTarget.AddToClassList("uvz-popup");
                writebackTargetContainer.Add(_writebackTarget);
            }

            // ボタン
            root.Q<Button>("btn-uv-to-xyz")?.RegisterCallback<ClickEvent>(_ => ExecuteUvToXyz());
            root.Q<Button>("btn-xyz-to-uv")?.RegisterCallback<ClickEvent>(_ => ExecuteXyzToUv());

            RefreshAll();
        }

        // ================================================================
        // コンテキスト
        // ================================================================

        protected override void OnContextSet()
        {
            RefreshAll();
        }

        protected override void OnUndoRedoPerformed()
        {
            base.OnUndoRedoPerformed();
            RefreshAll();
        }

        // ================================================================
        // 更新
        // ================================================================

        private void RefreshAll()
        {
            bool hasMesh = HasValidSelection;
            bool hasContext = _context != null;

            if (_warningLabel != null)
            {
                if (!hasContext)
                {
                    _warningLabel.text = "コンテキスト未設定";
                    _warningLabel.style.display = DisplayStyle.Flex;
                }
                else if (!hasMesh)
                {
                    _warningLabel.text = "メッシュが選択されていません";
                    _warningLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _warningLabel.style.display = DisplayStyle.None;
                }
            }

            bool showUI = hasContext && hasMesh;
            if (_mainSection != null)
                _mainSection.style.display = showUI ? DisplayStyle.Flex : DisplayStyle.None;
            if (_writebackSection != null)
                _writebackSection.style.display = showUI ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateTargetInfo();
            UpdateCameraInfo();
            UpdateWritebackTargetList();
        }

        private void UpdateTargetInfo()
        {
            if (_targetInfo == null) return;

            var mc = FirstSelectedMeshContext;
            var meshObj = mc?.MeshObject;
            if (meshObj == null)
            {
                _targetInfo.text = "メッシュ未選択";
                return;
            }

            int vertCount = meshObj.VertexCount;
            int faceCount = meshObj.FaceCount;
            int uvCount = 0;
            foreach (var v in meshObj.Vertices)
                uvCount += Mathf.Max(v.UVs.Count, 1);

            _targetInfo.text = $"{mc.Name}  V:{vertCount} F:{faceCount} UV頂点:{uvCount}";
        }

        private void UpdateCameraInfo()
        {
            if (_cameraInfo == null) return;

            if (_context == null)
            {
                _cameraInfo.text = "カメラ: -";
                return;
            }

            Vector3 cp = _context.CameraPosition;
            Vector3 ct = _context.CameraTarget;
            Vector3 forward = (ct - cp).normalized;
            _cameraInfo.text = $"カメラ Dir:({forward.x:F2}, {forward.y:F2}, {forward.z:F2})";
        }

        private void UpdateWritebackTargetList()
        {
            if (_writebackTarget == null || _context == null || Model == null) return;

            var choices = new List<string>();
            var meshList = Model.MeshContextList;
            int selectedIdx = Model.FirstSelectedIndex;

            for (int i = 0; i < meshList.Count; i++)
            {
                if (i == selectedIdx) continue; // 自身は除外
                var mc = meshList[i];
                if (mc?.MeshObject == null) continue;
                if (mc.MeshObject.Type != MeshType.Mesh) continue;
                choices.Add($"[{i}] {mc.Name}");
            }

            if (choices.Count == 0)
                choices.Add("(なし)");

            // PopupFieldのchoicesを更新
            // UIToolkitのPopupFieldはchoices差し替えに制限があるため再生成
            var container = _writebackTarget.parent;
            if (container != null)
            {
                container.Remove(_writebackTarget);
                int prevIndex = Mathf.Clamp(_writebackTarget.index, 0, choices.Count - 1);
                _writebackTarget = new PopupField<string>("ターゲット", choices, prevIndex);
                _writebackTarget.AddToClassList("uvz-popup");
                container.Add(_writebackTarget);
            }
        }

        /// <summary>
        /// 書き戻しターゲットのMeshContextインデックスを取得。無効なら-1。
        /// </summary>
        private int GetWritebackTargetIndex()
        {
            if (_writebackTarget == null || _context == null || Model == null) return -1;

            string val = _writebackTarget.value;
            if (string.IsNullOrEmpty(val) || val == "(なし)") return -1;

            // "[index] name" からインデックスを抽出
            int bracketEnd = val.IndexOf(']');
            if (bracketEnd < 2) return -1;

            string numStr = val.Substring(1, bracketEnd - 1);
            if (int.TryParse(numStr, out int idx))
                return idx;
            return -1;
        }

        // ================================================================
        // UV→XYZ 展開
        // ================================================================

        private void ExecuteUvToXyz()
        {
            if (_context == null || Model == null) return;

            var mc = FirstSelectedMeshContext;
            var meshObj = mc?.MeshObject;
            if (meshObj == null || meshObj.VertexCount == 0)
            {
                SetStatus("メッシュデータがありません");
                return;
            }

            if (_context.AddMeshContext == null)
            {
                SetStatus("メッシュ追加機能が利用できません");
                return;
            }

            float uvScale = _settings.UVScale;
            float depthScale = _settings.DepthScale;

            // カメラ情報
            Vector3 camPos = _context.CameraPosition;
            Vector3 camTarget = _context.CameraTarget;
            Vector3 camForward = (camTarget - camPos).normalized;
            if (camForward.sqrMagnitude < 0.001f)
                camForward = Vector3.forward;

            // --- UVZ展開メッシュの構築 ---
            // ToUnityMesh方式: (vertexIdx, uvIdx) ペアで頂点を分裂させる
            var vertexMapping = new Dictionary<(int vertexIdx, int uvIdx), int>();
            var newVertices = new List<Vertex>();

            for (int vIdx = 0; vIdx < meshObj.Vertices.Count; vIdx++)
            {
                var srcVert = meshObj.Vertices[vIdx];
                int uvCount = Mathf.Max(srcVert.UVs.Count, 1);

                // カメラ平面からの深度
                float depth = Vector3.Dot(srcVert.Position - camPos, camForward);

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    int newIdx = newVertices.Count;
                    vertexMapping[(vIdx, uvIdx)] = newIdx;

                    Vector2 uv = uvIdx < srcVert.UVs.Count
                        ? srcVert.UVs[uvIdx]
                        : Vector2.zero;

                    // Position = (U * scale, V * scale, depth * depthScale)
                    var newVert = new Vertex(new Vector3(
                        uv.x * uvScale,
                        uv.y * uvScale,
                        depth * depthScale));

                    // UVはそのまま保持
                    newVert.UVs.Add(uv);

                    // 法線はカメラ方向の逆
                    newVert.Normals.Add(-camForward);

                    newVertices.Add(newVert);
                }
            }

            // 面を複製（インデックスをリマップ）
            var newFaces = new List<Face>();
            foreach (var srcFace in meshObj.Faces)
            {
                if (srcFace == null || srcFace.VertexCount < 2) continue;

                var newFace = new Face();
                newFace.MaterialIndex = srcFace.MaterialIndex;
                newFace.Flags = srcFace.Flags;

                for (int ci = 0; ci < srcFace.VertexCount; ci++)
                {
                    int origVi = srcFace.VertexIndices[ci];
                    int uvSubIdx = ci < srcFace.UVIndices.Count ? srcFace.UVIndices[ci] : 0;

                    // マッピングにあればそれを使う、なければuvSubIdx=0にフォールバック
                    if (!vertexMapping.TryGetValue((origVi, uvSubIdx), out int newVi))
                    {
                        if (!vertexMapping.TryGetValue((origVi, 0), out newVi))
                            newVi = 0; // 最終フォールバック
                    }

                    newFace.VertexIndices.Add(newVi);
                    newFace.UVIndices.Add(0); // 新メッシュではUVs[0]のみ
                    newFace.NormalIndices.Add(0);
                }

                newFaces.Add(newFace);
            }

            // MeshObject作成
            var newMeshObj = new MeshObject($"{mc.Name}_UVZ");
            newMeshObj.Vertices = newVertices;
            newMeshObj.Faces = newFaces;
            newMeshObj.Type = MeshType.Mesh;
            newMeshObj.AssignMissingIds();

            // MeshContext作成してリストに追加
            var newMeshContext = new MeshContext
            {
                MeshObject = newMeshObj,
                UnityMesh = newMeshObj.ToUnityMesh(),
                OriginalPositions = newMeshObj.Positions.Clone() as Vector3[],
            };

            _context.AddMeshContext(newMeshContext);
            _context.Repaint?.Invoke();

            SetStatus($"UV→XYZ完了: {newVertices.Count}頂点, {newFaces.Count}面 → '{newMeshObj.Name}'");
            RefreshAll();
        }

        // ================================================================
        // XYZ→UV 書き戻し
        // ================================================================

        private void ExecuteXyzToUv()
        {
            if (_context == null || Model == null) return;

            var srcMc = FirstSelectedMeshContext;
            var srcMeshObj = srcMc?.MeshObject;
            if (srcMeshObj == null || srcMeshObj.VertexCount == 0)
            {
                SetStatus("ソースメッシュがありません");
                return;
            }

            int targetIdx = GetWritebackTargetIndex();
            if (targetIdx < 0 || targetIdx >= Model.MeshContextList.Count)
            {
                SetStatus("書き戻し先が選択されていません");
                return;
            }

            var targetMc = Model.GetMeshContext(targetIdx);
            var targetMeshObj = targetMc?.MeshObject;
            if (targetMeshObj == null)
            {
                SetStatus("ターゲットメッシュが無効です");
                return;
            }

            float uvScale = _settings.UVScale;
            if (uvScale < 0.001f) uvScale = 1f;

            // --- 書き戻しロジック ---
            // ソースメッシュ（UVZメッシュ）はToUnityMesh方式で展開されている。
            // (vertexIdx, uvIdx) ペアで分裂した頂点が並んでいる。
            //
            // ターゲットメッシュの各頂点のUVsを、ソースのPosition.xy / uvScaleで上書きする。
            //
            // 対応関係: ソースの頂点順は、ターゲットの頂点順にUVサブインデックス順で展開されたもの。
            // つまり: srcVertex[n] → (origVertIdx, origUvIdx) の逆算が必要。
            //
            // 復元ルール:
            //   ターゲット頂点iがuvCount個のUVを持つ → ソースの連続uvCount個の頂点がそれに対応

            // Undo記録: ターゲットメッシュのトポロジ変更として記録
            var undo = _context.UndoController;
            var before = undo?.CaptureMeshObjectSnapshot();

            // ターゲット側の元選択を一時保存・切り替えが必要だが、
            // 直接ターゲットのMeshObjectを操作する
            int srcIdx = 0;
            int updatedVerts = 0;

            for (int vi = 0; vi < targetMeshObj.Vertices.Count; vi++)
            {
                var targetVert = targetMeshObj.Vertices[vi];
                int uvCount = Mathf.Max(targetVert.UVs.Count, 1);

                // ターゲット頂点のUVsをクリアして再構築
                targetVert.UVs.Clear();

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    if (srcIdx < srcMeshObj.Vertices.Count)
                    {
                        var srcVert = srcMeshObj.Vertices[srcIdx];
                        float u = srcVert.Position.x / uvScale;
                        float v = srcVert.Position.y / uvScale;
                        targetVert.UVs.Add(new Vector2(u, v));
                        srcIdx++;
                    }
                    else
                    {
                        // ソースが足りない場合はゼロ
                        targetVert.UVs.Add(Vector2.zero);
                    }
                }
                updatedVerts++;
            }

            // ターゲットのUnityMesh更新
            if (targetMc.UnityMesh != null)
            {
                var newUnityMesh = targetMeshObj.ToUnityMesh();
                targetMc.UnityMesh.Clear();
                targetMc.UnityMesh.vertices = newUnityMesh.vertices;
                targetMc.UnityMesh.normals = newUnityMesh.normals;
                targetMc.UnityMesh.uv = newUnityMesh.uv;
                targetMc.UnityMesh.subMeshCount = newUnityMesh.subMeshCount;
                for (int s = 0; s < newUnityMesh.subMeshCount; s++)
                    targetMc.UnityMesh.SetTriangles(newUnityMesh.GetTriangles(s), s);
                targetMc.UnityMesh.RecalculateBounds();
            }

            // Undo記録
            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                _context.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    undo, before, after, "XYZ→UV書き戻し"));
            }

            _context.Repaint?.Invoke();

            SetStatus($"XYZ→UV完了: {updatedVerts}頂点のUVを更新 → '{targetMc.Name}'");
            RefreshAll();
        }

        // ================================================================
        // ステータス
        // ================================================================

        private void SetStatus(string text)
        {
            if (_statusLabel != null)
                _statusLabel.text = text;
        }

        // ================================================================
        // Update
        // ================================================================

        private int _lastMeshIndex = -1;

        private void Update()
        {
            if (_context == null) return;

            int currentMeshIndex = Model?.FirstSelectedIndex ?? -1;
            if (currentMeshIndex != _lastMeshIndex)
            {
                _lastMeshIndex = currentMeshIndex;
                RefreshAll();
            }
        }
    }
}

// Tools/RotateTool.cs
// 頂点回転ツール
// 全選択メッシュを対等に処理（primary/secondary区別なし）

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Localization;
using static Poly_Ling.Gizmo.GLGizmoDrawer;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Tools
{
    public partial class RotateTool : IEditTool
    {
        public string Name => "Rotate";
        public string DisplayName => "Rotate";
        public string GetLocalizedDisplayName() => L.Get("Tool_Rotate");
        public IToolSettings Settings => null;

        // 回転設定
        private float _rotX, _rotY, _rotZ;
        private bool _useSnap = false;
        private float _snapAngle = 15f;
        private bool _useOriginPivot = false;

        // 状態
        private Vector3 _pivot;
        private bool _isDirty = false;
        private bool _isSliderDragging = false;
        private ToolContext _ctx;

        // 全選択メッシュの影響頂点と開始位置
        private Dictionary<int, HashSet<int>> _multiMeshAffected = new Dictionary<int, HashSet<int>>();
        private Dictionary<int, Dictionary<int, Vector3>> _multiMeshStartPositions = new Dictionary<int, Dictionary<int, Vector3>>();
        // 開始時の「画面に見えているワールド座標」。GPU が出した値をドラッグ開始時に一度だけ写す。
        // 前方向（ローカル→ワールド）を CPU で計算し直してはならない（規約 10.6）。
        private Dictionary<int, Dictionary<int, Vector3>> _multiMeshStartWorld = new Dictionary<int, Dictionary<int, Vector3>>();
        // マグネット影響（非選択）: meshKey -> (vertexIndex -> weight)
        private Dictionary<int, Dictionary<int, float>> _multiMeshMagnetW = new Dictionary<int, Dictionary<int, float>>();

        // マグネット（比例編集）
        private bool _useMagnet = false;
        private float _magnetRadius = 0.5f;
        private FalloffType _magnetFalloff = FalloffType.Smooth;
        private DistanceMode _magnetDistanceMode = DistanceMode.Euclidean;

        // 軸-角度回転（Euler と排他）
        private bool _axisMode = false;
        private Vector3 _axisVec = new Vector3(0f, 1f, 0f);
        private float _axisAngle = 0f;

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos) { _ctx = ctx; return false; }
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) { _ctx = ctx; return false; }
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos) { _ctx = ctx; return false; }

        // ================================================================
        // Player ビュー用公開 API
        // ----------------------------------------------------------------
        // エディタ版 RotateTool.EditorUI が直接アクセスしていた private
        // フィールドを Player サブパネルから操作できるよう公開する。
        // ================================================================

        public float RotX { get => _rotX; set { _rotX = value; if (_isSliderDragging) UpdatePreview(); } }
        public float RotY { get => _rotY; set { _rotY = value; if (_isSliderDragging) UpdatePreview(); } }
        public float RotZ { get => _rotZ; set { _rotZ = value; if (_isSliderDragging) UpdatePreview(); } }
        public bool  UseSnap      { get => _useSnap;      set => _useSnap      = value; }
        public float SnapAngle    { get => _snapAngle;    set => _snapAngle    = Mathf.Max(0.1f, value); }
        public bool  UseOriginPivot { get => _useOriginPivot; set { _useOriginPivot = value; UpdatePivot(); if (_isSliderDragging) UpdatePreview(); } }
        public Vector3 PivotPublic  { get { if (!_isSliderDragging) { UpdateAffected(); UpdatePivot(); } return _pivot; } }
        public bool         UseMagnet          { get => _useMagnet;          set => _useMagnet = value; }
        public float        MagnetRadius       { get => _magnetRadius;       set => _magnetRadius = Mathf.Max(0.001f, value); }
        public FalloffType  MagnetFalloff      { get => _magnetFalloff;      set => _magnetFalloff = value; }
        public DistanceMode MagnetDistanceMode { get => _magnetDistanceMode; set => _magnetDistanceMode = value; }
        public bool  AxisMode  { get => _axisMode;  set { _axisMode = value; if (_isSliderDragging) UpdatePreview(); } }
        public float AxisVecX  { get => _axisVec.x; set { _axisVec.x = value; if (_isSliderDragging) UpdatePreview(); } }
        public float AxisVecY  { get => _axisVec.y; set { _axisVec.y = value; if (_isSliderDragging) UpdatePreview(); } }
        public float AxisVecZ  { get => _axisVec.z; set { _axisVec.z = value; if (_isSliderDragging) UpdatePreview(); } }
        public float AxisAngle { get => _axisAngle; set { _axisAngle = value; if (_isSliderDragging) UpdatePreview(); } }
        public int   GetTotalAffectedCountPublic() { UpdateAffected(); return GetTotalAffectedCount(); }

        /// <summary>スライダー変更後に回転プレビューを更新する。ドラッグ開始を通知する。</summary>
        public void BeginSliderDrag()
        {
            if (!_isSliderDragging)
            {
                _isSliderDragging = true;
                _ctx?.EnterTransformDragging?.Invoke();
            }
        }

        /// <summary>ドラッグ終了・Undo 記録。</summary>
        public void EndSliderDrag()
        {
            if (_ctx != null) ApplyRotation(_ctx);
            ExitSliderDragging();
            // 確定した回転はメッシュへ焼き込まれ、次のプレビューは焼き込み後の形状を
            // 基準に取り直す。角度を残すと同じ値がもう一度加算されるため 0 へ戻す。
            // ScaleTool.EndSliderDrag がスケールを 1 に戻すのと同じ扱い。
            _rotX = _rotY = _rotZ = 0f;
            _axisAngle = 0f;
        }

        /// <summary>回転をリセットして元の位置に戻す。</summary>
        public void RevertPublic() { RevertToStart(); _rotX = _rotY = _rotZ = 0f; _axisAngle = 0f; }

        /// <summary>
        /// 取り出せる回転プレビューがあるか。TryTakeRotationFromDrag が true を返す条件と同じ。
        /// </summary>
        public bool RotationDragPending
            => _ctx != null && _isDirty && _multiMeshStartPositions.Count > 0;

        /// <summary>
        /// ドラッグ／スライダーの回転量を取り出し、開始状態へ戻す。
        ///
        /// 【なぜ要るか】
        ///   1 ドラッグ = 1 コマンドにするため。ドラッグ中の適用はプレビューとして
        ///   扱い、確定時はここで開始状態へ戻して回転量だけを返す。呼び出し側
        ///   （RotateToolHandler）が RotateSelectionCommand を送り、実際の回転と
        ///   Undo 記録は EndSliderDrag → ApplyRotation が行う。
        ///   ObjectMoveTool.TryTakeOriginOnlyDrag と同じ形。
        ///
        /// 【戻す方法】
        ///   ApplyRotation が Undo 記録に使うのと同じ _multiMeshStartPositions を
        ///   書き戻す RevertToStart をそのまま使う。復元用の経路を別に作らない。
        ///
        /// 【何も返さない場合】
        ///   プレビューが無いときは false を返し、状態も戻さない。
        ///   呼び出し側は従来どおり EndSliderDrag で確定させる。
        /// </summary>
        /// <param name="axisMode">true なら axis と angleDeg、false なら euler が有効。</param>
        public bool TryTakeRotationFromDrag(
            out bool axisMode, out Vector3 euler, out Vector3 axis, out float angleDeg)
        {
            axisMode = _axisMode;
            euler    = new Vector3(_rotX, _rotY, _rotZ);
            axis     = _axisVec;
            angleDeg = _axisAngle;

            if (!RotationDragPending) return false;

            RevertToStart();
            ExitSliderDragging();
            _rotX = _rotY = _rotZ = 0f;
            _axisAngle = 0f;
            return true;
        }

        /// <summary>コンテキストを手動設定する（スライダーUI から使用）。</summary>
        public void SetContextPublic(ToolContext ctx) { _ctx = ctx; UpdateAffected(); UpdatePivot(); }

        public void OnActivate(ToolContext ctx)
        {
            _ctx = ctx;
            ResetState();
        }

        public void OnDeactivate(ToolContext ctx)
        {
            if (_isSliderDragging)
            {
                _isSliderDragging = false;
                ctx.ExitTransformDragging?.Invoke();
            }
            if (_isDirty) ApplyRotation(ctx);
            ResetState();
        }

        public void Reset() => ResetState();

        private void ResetState()
        {
            _rotX = _rotY = _rotZ = 0f;
            _isDirty = false;
            _isSliderDragging = false;
            _multiMeshAffected.Clear();
            _multiMeshStartPositions.Clear();
            _multiMeshStartWorld.Clear();
            _multiMeshMagnetW.Clear();
        }

        private void ExitSliderDragging()
        {
            if (!_isSliderDragging) return;
            _isSliderDragging = false;
            _ctx?.ExitTransformDragging?.Invoke();
        }

        private void UpdateAffected()
        {
            _multiMeshAffected.Clear();

            var model = _ctx?.Model;
            if (model == null) return;

            foreach (int meshIdx in model.SelectedDrawableMeshIndices)
            {
                var meshContext = model.GetMeshContext(meshIdx);
                if (meshContext == null || !meshContext.HasSelection)
                    continue;

                var meshObject = meshContext.MeshObject;
                if (meshObject == null)
                    continue;

                var affected = new HashSet<int>();
                foreach (var v in meshContext.SelectedVertices)
                    affected.Add(v);
                foreach (var edge in meshContext.SelectedEdges)
                {
                    affected.Add(edge.V1);
                    affected.Add(edge.V2);
                }
                foreach (var faceIdx in meshContext.SelectedFaces)
                {
                    if (faceIdx >= 0 && faceIdx < meshObject.FaceCount)
                    {
                        foreach (var vIdx in meshObject.Faces[faceIdx].VertexIndices)
                            affected.Add(vIdx);
                    }
                }
                foreach (var lineIdx in meshContext.SelectedLines)
                {
                    if (lineIdx >= 0 && lineIdx < meshObject.FaceCount)
                    {
                        var face = meshObject.Faces[lineIdx];
                        if (face.VertexCount == 2)
                        {
                            affected.Add(face.VertexIndices[0]);
                            affected.Add(face.VertexIndices[1]);
                        }
                    }
                }

                if (affected.Count > 0)
                {
                    _multiMeshAffected[meshIdx] = affected;
                }
            }
        }

        private int GetTotalAffectedCount()
        {
            int total = 0;
            foreach (var kv in _multiMeshAffected)
                total += kv.Value.Count;
            return total;
        }

        /// <summary>
        /// 【前方向は GPU の値を使う】
        /// 指定メッシュの頂点について、いま画面に見えているワールド座標を返す。
        /// 参照するのは GPU が _worldPositionBuffer に出した値
        /// （ToolContext.GetMeshWorldPositions）。スキンド頂点に実際に適用される行列は
        /// メッシュの WorldMatrix ではなくボーンの SkinningMatrix のブレンドなので、
        /// meshContext.LocalToWorld で代用してはならない。
        ///
        /// 【保険】GPU 値が取れないときだけ CPU 行列に落ちる。
        ///   通る条件はバッファ未構築・ビューポート未配線など、GPU 値が null の場合のみ。
        ///   落ち先は VertexMatrix(i, showBindPose) で、描画と同じ規則・同じ表示モード。
        ///   これは CPU 独自計算であり、本来は残すべきでないものを承知のうえで
        ///   保険として置いている（2026-09-15・利用者の指示による）。
        ///   外すと、GPU 値が取れない条件で回転が無反応になる。
        ///   新しいコードでこの分岐を真似しないこと。
        /// </summary>
        private Dictionary<int, Vector3> CaptureStartWorld(
            MeshContext meshContext, int meshKey, Dictionary<int, Vector3> startLocal)
        {
            var gpu = _ctx?.GetMeshWorldPositions?.Invoke(meshKey);
            var map = new Dictionary<int, Vector3>(startLocal.Count);
            bool showBind = _ctx?.ShowBindPose ?? false;

            foreach (var kv in startLocal)
            {
                int i = kv.Key;
                if (gpu != null && i >= 0 && i < gpu.Length) map[i] = gpu[i];
                // 【保険】GPU 値が無いときだけの CPU 経路。上の注記を参照。
                else map[i] = meshContext.VertexMatrix(i, showBind).MultiplyPoint3x4(kv.Value);
            }
            return map;
        }

        /// <summary>
        /// _pivot はワールド座標で保持する（2026-09-15 変更）。
        /// 以前は基準メッシュのローカル座標で持ち、使うたびに LocalToWorld で戻していたが、
        /// その往復に入るのはメッシュ 1 個の WorldMatrix で、スキンド頂点の表示位置とは
        /// 別物だった。GPU から取った重心をローカルへ畳むと壊れるため、ワールドのまま持つ。
        /// </summary>
        private Vector3 PivotWorld() => _pivot;

        private void UpdatePivot()
        {
            if (_useOriginPivot)
            {
                // 基準メッシュのローカル原点。_pivot はワールド保持なので、ここでワールドへ直す。
                var originMc = _ctx?.Model?.ActiveMeshContext;
                _pivot = originMc != null
                    ? (_ctx?.ShowBindPose ?? false
                        ? originMc.BindWorldMatrix.MultiplyPoint3x4(Vector3.zero)
                        : originMc.WorldMatrix.MultiplyPoint3x4(Vector3.zero))
                    : Vector3.zero;
                return;
            }

            if (GetTotalAffectedCount() == 0)
            {
                _pivot = Vector3.zero;
                return;
            }

            Vector3 sum = Vector3.zero;
            int totalCount = 0;
            var model = _ctx?.Model;
            bool showBind = _ctx?.ShowBindPose ?? false;

            foreach (var kv in _multiMeshAffected)
            {
                var meshContext = model?.GetMeshContext(kv.Key);
                var meshObject = meshContext?.MeshObject;
                if (meshObject == null) continue;

                // 前方向は GPU が出した値を使う（CPU で計算し直さない）。
                var gpu = _ctx?.GetMeshWorldPositions?.Invoke(kv.Key);

                foreach (int i in kv.Value)
                {
                    if (i >= 0 && i < meshObject.VertexCount)
                    {
                        // メッシュごとにローカル座標系が異なるため、ワールドで平均する。
                        // 【保険】GPU 値が無いときだけ CPU 行列に落ちる（CaptureStartWorld の注記を参照）。
                        sum += (gpu != null && i < gpu.Length)
                             ? gpu[i]
                             : meshContext.VertexMatrix(i, showBind)
                                          .MultiplyPoint3x4(meshObject.Vertices[i].Position);
                        totalCount++;
                    }
                }
            }

            if (totalCount == 0)
            {
                _pivot = Vector3.zero;
                return;
            }

            // _pivot はワールド座標で保持する（PivotWorld / PivotPublic の契約）。
            _pivot = sum / totalCount;
        }

        private void UpdatePreview()
        {
            if (GetTotalAffectedCount() == 0) return;

            var model = _ctx?.Model;
            if (model == null) return;

            // 初回: 全メッシュの開始位置を記録
            if (_multiMeshStartPositions.Count == 0)
            {
                UpdatePivot();
                _multiMeshMagnetW.Clear();
                _multiMeshStartWorld.Clear();
                foreach (var kv in _multiMeshAffected)
                {
                    var meshContext = model.GetMeshContext(kv.Key);
                    var meshObject = meshContext?.MeshObject;
                    if (meshObject == null) continue;

                    var startPos = new Dictionary<int, Vector3>();
                    foreach (int i in kv.Value)
                    {
                        if (i >= 0 && i < meshObject.VertexCount)
                            startPos[i] = meshObject.Vertices[i].Position;
                    }

                    // マグネット: 非選択の影響点を追加
                    if (_useMagnet)
                    {
                        var orig = new Vector3[meshObject.VertexCount];
                        for (int i = 0; i < meshObject.VertexCount; i++)
                            orig[i] = meshObject.Vertices[i].Position;
                        var affected = MagnetInfluence.Compute(meshObject, kv.Value, orig,
                            _magnetRadius, _magnetFalloff, _magnetDistanceMode);
                        if (affected.Count > 0)
                        {
                            var wmap = new Dictionary<int, float>();
                            foreach (var akv in affected)
                            {
                                if (!startPos.ContainsKey(akv.Key)) startPos[akv.Key] = orig[akv.Key];
                                wmap[akv.Key] = akv.Value;
                            }
                            _multiMeshMagnetW[kv.Key] = wmap;
                        }
                    }

                    _multiMeshStartPositions[kv.Key] = startPos;
                    // 開始時の表示ワールド座標を GPU から写す。以後この値を基準に回す。
                    _multiMeshStartWorld[kv.Key] = CaptureStartWorld(meshContext, kv.Key, startPos);
                }
            }

            // 回転適用（開始位置から計算）
            Quaternion rot;
            if (_axisMode)
            {
                Vector3 dir = _axisVec.sqrMagnitude > 1e-8f ? _axisVec.normalized : Vector3.up;
                rot = Quaternion.AngleAxis(_axisAngle, dir);
            }
            else
            {
                rot = Quaternion.Euler(_rotX, _rotY, _rotZ);
            }

            // rot はワールド軸まわりの回転。頂点はローカル座標なので、
            // 「ローカル→ワールド→回転→ローカル」の往復で適用する。
            // これによりメッシュの WorldMatrix に回転／スケールがあっても
            // ギズモの見た目と実際の回転が一致する。
            Vector3 pivotWorld = PivotWorld();
            bool showBindPose = _ctx?.ShowBindPose ?? false;

            foreach (var kv in _multiMeshStartPositions)
            {
                var meshContext = model.GetMeshContext(kv.Key);
                var meshObject = meshContext?.MeshObject;
                if (meshObject == null) continue;

                _multiMeshMagnetW.TryGetValue(kv.Key, out var wmap);
                _multiMeshStartWorld.TryGetValue(kv.Key, out var startWorldMap);

                foreach (var posKv in kv.Value)
                {
                    int i = posKv.Key;
                    if (i >= 0 && i < meshObject.VertexCount)
                    {
                        // マグネット影響点は重み付き回転（Slerp）、選択点はフル
                        Quaternion rq = rot;
                        if (wmap != null && wmap.TryGetValue(i, out float wt))
                            rq = Quaternion.Slerp(Quaternion.identity, rot, wt);

                        // 前方向は開始時に GPU から写した表示ワールド座標。
                        // 書き戻しだけ頂点単位の逆行列を使う（GPU 側に逆行列は無い）。
                        // 【保険】写せていない頂点だけ CPU 行列に落ちる（CaptureStartWorld の注記を参照）。
                        Vector3 startWorld = (startWorldMap != null && startWorldMap.TryGetValue(i, out var sw))
                            ? sw
                            : meshContext.VertexMatrix(i, showBindPose).MultiplyPoint3x4(posKv.Value);
                        Vector3 rotWorld   = pivotWorld + rq * (startWorld - pivotWorld);
                        var v = meshObject.Vertices[i];
                        v.Position = meshContext.VertexMatrix(i, showBindPose).inverse
                                                .MultiplyPoint3x4(rotWorld);
                        meshObject.Vertices[i] = v;
                    }
                    meshObject.InvalidatePositionCache();
                }
            }

            _isDirty = true;
            _ctx.SyncMesh?.Invoke();
        }

        private void ApplyRotation(ToolContext ctx)
        {
            if (!_isDirty || _multiMeshStartPositions.Count == 0) return;

            var model = ctx?.Model;
            var allEntries = new List<MeshMoveEntry>();

            foreach (var meshKv in _multiMeshStartPositions)
            {
                var meshContext = model?.GetMeshContext(meshKv.Key);
                var meshObject = meshContext?.MeshObject;
                if (meshObject == null) continue;

                var indices = new List<int>();
                var oldPos = new List<Vector3>();
                var newPos = new List<Vector3>();

                foreach (var posKv in meshKv.Value)
                {
                    int i = posKv.Key;
                    if (i >= 0 && i < meshObject.VertexCount)
                    {
                        Vector3 cur = meshObject.Vertices[i].Position;
                        if (Vector3.Distance(posKv.Value, cur) > 0.0001f)
                        {
                            indices.Add(i);
                            oldPos.Add(posKv.Value);
                            newPos.Add(cur);
                        }
                    }
                }

                if (indices.Count > 0)
                {
                    allEntries.Add(new MeshMoveEntry
                    {
                        MeshContextIndex = meshKv.Key,
                        Indices = indices.ToArray(),
                        OldPositions = oldPos.ToArray(),
                        NewPositions = newPos.ToArray()
                    });

                    if (meshContext.OriginalPositions != null)
                    {
                        foreach (int i in indices)
                        {
                            if (i < meshContext.OriginalPositions.Length)
                                meshContext.OriginalPositions[i] = meshObject.Vertices[i].Position;
                        }
                    }
                }
            }

            if (allEntries.Count > 0 && ctx.UndoController != null)
            {
                ctx.UndoController.FocusVertexEdit();
                var record = new MultiMeshVertexMoveRecord(allEntries.ToArray());
                {
                    string __dbgDesc = T("UndoRotate");
                    PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                    ctx.UndoController.VertexEditStack.Record(record, __dbgDesc);
                }
            }

            _multiMeshStartPositions.Clear();
            _multiMeshStartWorld.Clear();
            _multiMeshMagnetW.Clear();
            _isDirty = false;
        }

        private void RevertToStart()
        {
            var model = _ctx?.Model;

            foreach (var meshKv in _multiMeshStartPositions)
            {
                var meshContext = model?.GetMeshContext(meshKv.Key);
                var meshObject = meshContext?.MeshObject;
                if (meshObject == null) continue;

                foreach (var posKv in meshKv.Value)
                {
                    int i = posKv.Key;
                    if (i >= 0 && i < meshObject.VertexCount)
                    {
                        var v = meshObject.Vertices[i];
                        v.Position = posKv.Value;
                        meshObject.Vertices[i] = v;
                    }
                }
            }

            foreach (var kv in _multiMeshStartPositions)
            {
                var mo2 = _ctx?.Model?.GetMeshContext(kv.Key)?.MeshObject;
                mo2?.InvalidatePositionCache();
            }
            _multiMeshStartPositions.Clear();
            _multiMeshStartWorld.Clear();
            _multiMeshMagnetW.Clear();
            _isDirty = false;
            _ctx.SyncMesh?.Invoke();
        }

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        public void DrawGizmo(ToolContext ctx) { }

        private void DrawAxis(ToolContext ctx, Rect rect, Vector2 origin, Vector3 dir, Color col)
        {
            Vector2 end = ctx.LocalToScreen(_pivot + dir * 0.1f);
            Vector2 d = (end - origin).normalized * 40f;

            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
        }
    }
}

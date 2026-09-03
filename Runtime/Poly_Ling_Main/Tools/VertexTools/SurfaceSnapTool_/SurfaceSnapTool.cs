// SurfaceSnapTool.cs
// 「面に張り付け」ツール。
// ターゲットオブジェクトの頂点を、カメラ目線でリファレンスオブジェクト群の
// 最前面の面上へ移す。実処理は SurfaceSnapOps、プレビューは SurfaceSnapPreviewState。
// マウス入力は持たず、パネルの「計算」「決定」から呼ぶ。
// Runtime/Poly_Ling_Main/Tools/VertexTools/SurfaceSnapTool_/ に配置
//
// 【手順】
//   計算 … カメラ・ワールド座標を1回だけ読み、頂点ごとの行き先をローカル座標で確定する。
//   スライダー … Backup と行き先の補間のみ。カメラが動いても結果は変わらない。
//   決定 … Undo を1手記録して確定する。
//
// 【対象】選択中の描画オブジェクト全部（マルチセレクト対応）。
//   リファレンスに指定したオブジェクトとボーン表示用メッシュは対象外。
//   「選択頂点のみ」を立てると、各メッシュの MeshContext.SelectedVertices だけを動かす。
//
// 【ワールド座標】GetWorldPositions デリゲート経由で GPU の計算値を受け取る
//   （PlayerViewportManager.TryGetMeshWorldPositions:2226）。
//   CPU 側でスキニング規則を再実装しない。
//
// 【書き戻し】GPU 側に逆変換が無いため MeshContext.VertexMatrix(i).inverse を使う
//   （AddFaceTool.cs:911 と同じ規則）。
//
// 【Undo】複数メッシュの頂点座標だけを書き換えるため、
//   MultiMeshVertexSnapshot / MultiMeshVertexSnapshotRecord を MeshListStack へ
//   1 件だけ記録する（PipeAlignTool.cs:288-299 と同じ）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools
{
    /// <summary>どのカメラ目線で張り付けるか。</summary>
    public enum SurfaceSnapCameraKind
    {
        /// <summary>カレント（直前に操作したビューポート）。</summary>
        Current = 0,

        /// <summary>透視ビュー。</summary>
        Perspective = 1,

        /// <summary>上面ビュー。</summary>
        Top = 2,

        /// <summary>正面ビュー。</summary>
        Front = 3,

        /// <summary>側面ビュー。</summary>
        Side = 4,
    }

    /// <summary>カメラ目線でリファレンスの面上へ頂点を移すツール。</summary>
    public class SurfaceSnapTool : IEditTool
    {
        public string Name        => "SurfaceSnap";
        public string DisplayName => "Snap To Surface";

        // ================================================================
        // 設定
        // ================================================================

        private SurfaceSnapSettings _settings = new SurfaceSnapSettings();
        public IToolSettings Settings => _settings;

        /// <summary>張り付けに使うカメラ。</summary>
        public SurfaceSnapCameraKind CameraKind
        {
            get => _settings.CameraKind;
            set => _settings.CameraKind = value;
        }

        /// <summary>選択頂点だけを動かすか。false なら全頂点。</summary>
        public bool SelectedVerticesOnly
        {
            get => _settings.SelectedVerticesOnly;
            set => _settings.SelectedVerticesOnly = value;
        }

        /// <summary>面からカメラ側へ残す距離。</summary>
        public float SurfaceOffset
        {
            get => _settings.SurfaceOffset;
            set => _settings.SurfaceOffset = value;
        }

        /// <summary>リファレンスの裏面を対象にするか。</summary>
        public SurfaceSnapBackface Backface
        {
            get => _settings.Backface;
            set => _settings.Backface = value;
        }

        /// <summary>リファレンスオブジェクトの MeshContextList 索引。</summary>
        public IReadOnlyList<int> ReferenceIndices => _settings.ReferenceIndices;

        public bool IsReference(int meshIndex) => _settings.ReferenceIndices.Contains(meshIndex);

        /// <summary>リファレンス指定を足し引きする。プレビュー中なら破棄する。</summary>
        public void SetReference(int meshIndex, bool on)
        {
            bool has = _settings.ReferenceIndices.Contains(meshIndex);
            if (has == on) return;

            CancelPreview();

            if (on) _settings.ReferenceIndices.Add(meshIndex);
            else    _settings.ReferenceIndices.Remove(meshIndex);
        }

        /// <summary>モデルに無くなった索引を捨てる。</summary>
        public void PruneReferences(Func<int, bool> exists)
        {
            if (exists == null) return;
            _settings.ReferenceIndices.RemoveAll(i => !exists(i));
        }

        // ================================================================
        // 外部配線（Handler から設定）
        // ================================================================

        /// <summary>
        /// 指定 MeshContext の全頂点ワールド座標。GPU の計算値を返す経路を配線すること。
        /// </summary>
        public Func<MeshContext, Vector3[]> GetWorldPositions;

        /// <summary>
        /// ワールド座標の再計算要求（UpdateTransform）。計算の直前に1回だけ呼ぶ。
        /// 毎フレーム呼んではならない。
        /// </summary>
        public Action OnRequestUpdateTransform;

        /// <summary>指定種別のカメラを返す。取れなければ null。</summary>
        public Func<SurfaceSnapCameraKind, SurfaceSnapCamera?> GetCamera;

        // ================================================================
        // 状態
        // ================================================================

        private ToolContext _context;

        private readonly SurfaceSnapPreviewState _preview = new SurfaceSnapPreviewState();
        private float _slider;

        /// <summary>直前の実行結果の文言。</summary>
        public string LastResult { get; private set; } = "";

        /// <summary>プレビュー中か。</summary>
        public bool IsPreviewing => _preview.IsActive;

        /// <summary>現在のスライダー値。</summary>
        public float Slider => _slider;

        /// <summary>対象になる描画オブジェクトの数。</summary>
        public int TargetMeshCount => EnumerateTargets().Count;

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)                => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)                  => false;

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。</summary>
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)   { _context = ctx; }
        public void OnDeactivate(ToolContext ctx) { _context = null; }

        public void Reset()
        {
            CancelPreview();
            _settings.ReferenceIndices.Clear();
            _settings.CameraKind           = SurfaceSnapCameraKind.Current;
            _settings.SelectedVerticesOnly = false;
            _settings.SurfaceOffset        = 0f;
            _settings.Backface             = SurfaceSnapBackface.Both;
            _slider    = 0f;
            LastResult = "";
        }

        // ================================================================
        // 対象の集約
        // ================================================================

        /// <summary>1 メッシュぶんの対象。</summary>
        public struct SnapTarget
        {
            public int         MeshIndex;
            public MeshContext MeshContext;
        }

        /// <summary>
        /// 選択中の描画オブジェクトを走査する。
        /// ボーン表示用メッシュとリファレンス指定済みのものは対象外。
        /// </summary>
        private List<SnapTarget> EnumerateTargets()
        {
            var list = new List<SnapTarget>();

            var model = _context?.Model;
            if (model == null) return list;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) continue;
                if (mc.Type == MeshType.Bone) continue;
                if (_settings.ReferenceIndices.Contains(idx)) continue;

                list.Add(new SnapTarget { MeshIndex = idx, MeshContext = mc });
            }

            return list;
        }

        // ================================================================
        // 公開 API（SubPanel / Handler から呼び出し）
        // ================================================================

        public void TriggerCompute() => Compute();

        public void SetSlider(float value)
        {
            if (!_preview.IsActive) return;
            _slider = Mathf.Clamp01(value);
            _preview.Apply(_context?.Model, _slider, _context);
        }

        public void TriggerApply() => ApplyPreview();

        public void TriggerCancel()
        {
            if (!_preview.IsActive) return;
            CancelPreview();
            LastResult = "取り消しました";
        }

        /// <summary>
        /// プレビュー中なら破棄して元座標へ戻す。
        /// プレビュー結果は MeshObject に直接書かれているため、
        /// パネルを隠すだけでは未確定の形状が残る。
        /// </summary>
        public void CancelIfActive()
        {
            if (!_preview.IsActive) return;
            CancelPreview();
        }

        private void CancelPreview()
        {
            if (!_preview.IsActive) return;
            _preview.End(_context?.Model, _context);
            _slider = 0f;
        }

        // ================================================================
        // 計算
        // ================================================================

        private void Compute()
        {
            CancelPreview();
            LastResult = "";

            var model = _context?.Model;
            if (model == null) { Fail("モデルがありません"); return; }

            if (_settings.ReferenceIndices.Count == 0)
            {
                Fail("リファレンスオブジェクトが指定されていません");
                return;
            }

            var targets = EnumerateTargets();
            if (targets.Count == 0)
            {
                Fail("ターゲットオブジェクトがありません（リファレンス以外のオブジェクトを選択してください）");
                return;
            }

            if (GetWorldPositions == null) { Fail("ワールド座標の取得経路が未配線です"); return; }
            if (GetCamera == null)          { Fail("カメラの取得経路が未配線です");       return; }

            var cam = GetCamera(_settings.CameraKind);
            if (cam == null) { Fail("カメラを取得できません"); return; }

            // ワールド座標が要るのはこの時点だけ。毎フレームは呼ばない。
            OnRequestUpdateTransform?.Invoke();

            var projector = new SurfaceSnapProjector();
            int refMeshes = 0;

            foreach (int ri in _settings.ReferenceIndices)
            {
                var rc = model.GetMeshContext(ri);
                if (rc?.MeshObject == null) continue;

                var rw = GetWorldPositions(rc);
                if (rw == null) continue;

                projector.AddMesh(rc.MeshObject, rw);
                refMeshes++;
            }

            if (refMeshes == 0) { Fail("リファレンスのワールド座標を取得できません"); return; }

            projector.Build(cam.Value);

            if (projector.ValidTriangleCount == 0)
            {
                Fail("リファレンスに投影できる三角形がありません");
                return;
            }

            var previewTargets = new List<SurfaceSnapPreviewState.Target>();
            int movedTotal = 0;
            int missTotal  = 0;
            var failures   = new List<string>();

            foreach (var t in targets)
            {
                var mc = t.MeshContext;
                var mo = mc.MeshObject;

                var world = GetWorldPositions(mc);
                if (world == null)
                {
                    failures.Add($"{mc.Name}: ワールド座標を取得できません");
                    continue;
                }

                HashSet<int> sel = null;
                if (_settings.SelectedVerticesOnly)
                {
                    sel = mc.SelectedVertices;
                    if (sel == null || sel.Count == 0)
                    {
                        failures.Add($"{mc.Name}: 選択頂点がありません");
                        continue;
                    }
                }

                int n = mo.VertexCount;
                var backup = new Vector3[n];
                var goal   = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    backup[i] = mo.Vertices[i].Position;
                    goal[i]   = backup[i];
                }

                int moved = 0;
                int miss  = 0;
                int limit = Mathf.Min(n, world.Length);

                for (int i = 0; i < limit; i++)
                {
                    if (sel != null && !sel.Contains(i)) continue;

                    if (!projector.TryProject(
                            world[i], _settings.SurfaceOffset, _settings.Backface,
                            out Vector3 hitWorld))
                    {
                        miss++;
                        continue;
                    }

                    goal[i] = mc.VertexMatrix(i).inverse.MultiplyPoint3x4(hitWorld);
                    moved++;
                }

                missTotal += miss;

                if (moved == 0)
                {
                    failures.Add($"{mc.Name}: 張り付く頂点がありません");
                    continue;
                }

                previewTargets.Add(new SurfaceSnapPreviewState.Target
                {
                    MeshIndex  = t.MeshIndex,
                    Context    = mc,
                    Backup     = backup,
                    Goal       = goal,
                    MovedCount = moved,
                });

                movedTotal += moved;
            }

            if (previewTargets.Count == 0)
            {
                Fail(failures.Count > 0 ? string.Join(" / ", failures) : "張り付く頂点がありません");
                return;
            }

            if (!_preview.Start(previewTargets)) { Fail("プレビューを開始できません"); return; }

            _slider = 0f;
            _preview.Apply(model, 0f, _context);

            LastResult =
                $"計算完了: 対象 {previewTargets.Count} obj / 張り付け {movedTotal} 頂点 / 外れ {missTotal} 頂点\n"
              + $"リファレンス三角形 {projector.ValidTriangleCount}"
              + (projector.SkippedTriangleCount > 0
                    ? $"（カメラ後方で除外 {projector.SkippedTriangleCount}）"
                    : "");

            if (failures.Count > 0) LastResult += $"\n除外: {string.Join(" / ", failures)}";

            Debug.Log($"[SurfaceSnapTool] {LastResult}");
        }

        // ================================================================
        // 確定
        // ================================================================

        private void ApplyPreview()
        {
            var model = _context?.Model;
            if (model == null || !_preview.IsActive) return;

            float slider = _slider;
            int   objs   = _preview.TargetCount;
            int   moved  = _preview.MovedVertexCount;

            var undo = _context.UndoController;

            // プレビュー結果は MeshObject に直接書かれている。
            // Undo の before は計算前の座標なので、一度戻してから捕獲する。
            _preview.Restore(model, _context);
            var before = undo != null ? MultiMeshVertexSnapshot.Capture(model) : null;

            _preview.Apply(model, slider, _context);

            if (undo != null && before != null)
            {
                var after = MultiMeshVertexSnapshot.Capture(model);

                // MeshListStack の Context を今回のモデルに合わせる（Undo 時の復元先）。
                undo.SetModelContext(model);

                string desc = $"Snap To Surface ({objs} objs / {moved} verts)";
                var record  = new MultiMeshVertexSnapshotRecord(before, after, desc);
                PLDiag.UndoRecord("MeshList", desc, record);
                undo.MeshListStack.Record(record, desc);
            }

            _preview.Commit();
            _slider = 0f;

            LastResult = $"確定: オブジェクト {objs} / 張り付け {moved} 頂点（適用 {slider:F2}）";
            Debug.Log($"[SurfaceSnapTool] {LastResult}");

            _context.Repaint?.Invoke();
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void Fail(string reason)
        {
            LastResult = reason;
            Debug.LogWarning($"[SurfaceSnapTool] 計算中止: {reason}");
        }
    }

    // ================================================================
    // 設定クラス
    // ================================================================

    public class SurfaceSnapSettings : IToolSettings
    {
        /// <summary>張り付けに使うカメラ。</summary>
        public SurfaceSnapCameraKind CameraKind = SurfaceSnapCameraKind.Current;

        /// <summary>選択頂点だけを動かすか。</summary>
        public bool SelectedVerticesOnly = false;

        /// <summary>面からカメラ側へ残す距離。</summary>
        public float SurfaceOffset = 0f;

        /// <summary>リファレンスの裏面を対象にするか。</summary>
        public SurfaceSnapBackface Backface = SurfaceSnapBackface.Both;

        /// <summary>リファレンスオブジェクトの MeshContextList 索引。</summary>
        public List<int> ReferenceIndices = new List<int>();

        public IToolSettings Clone() => new SurfaceSnapSettings
        {
            CameraKind           = CameraKind,
            SelectedVerticesOnly = SelectedVerticesOnly,
            SurfaceOffset        = SurfaceOffset,
            Backface             = Backface,
            ReferenceIndices     = new List<int>(ReferenceIndices),
        };

        public void CopyFrom(IToolSettings other)
        {
            if (other is SurfaceSnapSettings s)
            {
                CameraKind           = s.CameraKind;
                SelectedVerticesOnly = s.SelectedVerticesOnly;
                SurfaceOffset        = s.SurfaceOffset;
                Backface             = s.Backface;
                ReferenceIndices     = new List<int>(s.ReferenceIndices);
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is SurfaceSnapSettings s)
            {
                if (CameraKind           != s.CameraKind)           return true;
                if (SelectedVerticesOnly != s.SelectedVerticesOnly) return true;
                if (SurfaceOffset        != s.SurfaceOffset)        return true;
                if (Backface             != s.Backface)             return true;
                if (ReferenceIndices.Count != s.ReferenceIndices.Count) return true;
                for (int i = 0; i < ReferenceIndices.Count; i++)
                    if (ReferenceIndices[i] != s.ReferenceIndices[i]) return true;
                return false;
            }
            return true;
        }
    }
}

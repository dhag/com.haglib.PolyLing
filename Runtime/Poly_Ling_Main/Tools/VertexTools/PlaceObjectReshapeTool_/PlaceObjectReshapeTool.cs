// PlaceObjectReshapeTool.cs
// 藤壺（オブジェクト配置）専用のツール「藤壺の整形」。
// 実処理は PlaceObjectReshapeOps。ここは対象の集約・Undo 記録・反映だけを担う。
// マウス入力を持たず、パネルの「開始」ボタンから TriggerExecute() で実行する。
// Runtime/Poly_Ling_Main/Tools/VertexTools/PlaceObjectReshapeTool_/ に配置
//
// 【2 つの方式】
//   ・Affine          … 対応点からアフィン変換を最小二乗で推定する
//   ・ThinPlateSpline … 対応点から薄板スプラインを推定する。lambda で平滑化する
//
// 【対象】選択中の描画オブジェクト全部（マルチセレクト対応）。
//   オブジェクトごとに独立して処理する。ボーン表示用メッシュは編集対象外。
//   どのパーツを触るかは TargetText（パーツID）で決める。空欄なら全パーツ。
//
// 【原型】Prototype に入っている MeshObject の頂点をそのまま BEFORE に使う。
//   複数オブジェクトの結合はパネル側で済ませてから渡す。
//
// 【Undo】複数メッシュの頂点座標だけを書き換えるため、
//   MultiMeshVertexSnapshot / MultiMeshVertexSnapshotRecord を MeshListStack へ
//   1 件だけ記録する（メッシュが何個でも Undo は1手）。
//
// 【反映】位相は変わらないので OnTopologyChanged() は使わない。
//   InvalidatePositionCache() → SyncMeshContextPositionsOnly → Repaint。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools
{
    /// <summary>藤壺の部品を原型の形へ張り直すツール。</summary>
    public class PlaceObjectReshapeTool : IEditTool
    {
        public string Name        => "PlaceObjectReshape";
        public string DisplayName => "Place Object Reshape";

        // ================================================================
        // 設定
        // ================================================================

        private PlaceObjectReshapeSettings _settings = new PlaceObjectReshapeSettings();
        public IToolSettings Settings => _settings;

        /// <summary>推定方式。</summary>
        public PlaceObjectReshapeMode Mode { get => _settings.Mode; set => _settings.Mode = value; }

        /// <summary>薄板スプラインの平滑化係数。</summary>
        public float Lambda { get => _settings.Lambda; set => _settings.Lambda = value; }

        /// <summary>対象のパーツID。空欄なら全パーツ。</summary>
        public string TargetText { get => _settings.TargetText; set => _settings.TargetText = value; }

        // ================================================================
        // 原型（パネル側が結合済みの MeshObject を入れる）
        // ================================================================

        /// <summary>原型メッシュ。実行の直前にパネル側から入れる。</summary>
        public MeshObject Prototype { get; set; }

        /// <summary>原型の頂点数。0 なら原型が未指定。</summary>
        public int PrototypeVertexCount => Prototype?.VertexCount ?? 0;

        // ================================================================
        // 実行結果（SubPanel 表示用）
        // ================================================================

        /// <summary>直前の実行結果の文言。</summary>
        public string LastResult { get; private set; } = "";

        /// <summary>対象になる描画オブジェクトの数。</summary>
        public int TargetMeshCount => EnumerateTargets().Count;

        // ================================================================
        // コンテキスト
        // ================================================================

        private ToolContext _context;

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
            _settings.Mode       = PlaceObjectReshapeMode.Affine;
            _settings.Lambda     = PlaceObjectReshapeSettings.DefaultLambda;
            _settings.TargetText = "";
            Prototype            = null;
            LastResult           = "";
        }

        // ================================================================
        // 対象の集約
        // ================================================================

        /// <summary>1 メッシュぶんの対象。</summary>
        public struct ReshapeTarget
        {
            public int         MeshIndex;
            public MeshContext MeshContext;
        }

        /// <summary>
        /// 選択中の描画オブジェクトを走査する。ボーン表示用メッシュは編集対象外。
        /// </summary>
        private List<ReshapeTarget> EnumerateTargets()
        {
            var list = new List<ReshapeTarget>();

            var model = _context?.Model;
            if (model == null) return list;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) continue;
                if (mc.Type == MeshType.Bone) continue;

                list.Add(new ReshapeTarget { MeshIndex = idx, MeshContext = mc });
            }

            return list;
        }

        // ================================================================
        // 公開 API（SubPanel / Handler から呼び出し）
        // ================================================================

        public void TriggerExecute() => Execute();

        /// <summary>
        /// 対象オブジェクトの選択頂点が属するパーツIDを「1,3,5」形式で返す。
        /// 選択が無ければ空文字。複数オブジェクトの選択はまとめて 1 本にする。
        /// </summary>
        public string CollectSelectedPartsIdText()
        {
            var targets = EnumerateTargets();
            if (targets.Count == 0) return "";

            var ids = new SortedSet<int>();
            foreach (var t in targets)
            {
                var sel = t.MeshContext.Selection;
                if (sel == null) continue;

                var part = PlaceObjectReshapeOps.CollectPartsIdsOfVertices(
                    t.MeshContext.MeshObject, sel.Vertices);
                foreach (int id in part) ids.Add(id);
            }
            return PlaceObjectReshapeOps.FormatPartsIds(ids);
        }

        // ================================================================
        // 実行
        // ================================================================

        private void Execute()
        {
            var model   = _context?.Model;
            var targets = EnumerateTargets();

            if (model == null || targets.Count == 0)
            {
                LastResult = "対象オブジェクトがありません";
                Debug.LogWarning($"[PlaceObjectReshapeTool] 実行中止: {LastResult}");
                return;
            }

            if (Prototype == null || Prototype.VertexCount < PlaceObjectReshapeOps.MinimumVertexCount)
            {
                LastResult = $"原型オブジェクトの頂点が {PlaceObjectReshapeOps.MinimumVertexCount} 個未満です";
                Debug.LogWarning($"[PlaceObjectReshapeTool] 実行中止: {LastResult}");
                return;
            }

            // 対象パーツの指定を先に読む。ここで落ちたら 1 メッシュも触らない。
            if (!PipeSmoothOps.ParseTargets(_settings.TargetText, out HashSet<int> targetIds, out string terr))
            {
                LastResult = $"対象パーツの指定が読めません: {terr}";
                Debug.LogWarning($"[PlaceObjectReshapeTool] {LastResult}");
                return;
            }

            // 原型の頂点位置とサブIDを 1 回だけ取り出す。
            int protoCount = Prototype.VertexCount;
            var protoPos   = new Vector3[protoCount];
            var protoSubId = new int[protoCount];
            for (int i = 0; i < protoCount; i++)
            {
                var v = Prototype.Vertices[i];
                protoPos[i]   = v != null ? v.Position : Vector3.zero;
                protoSubId[i] = v != null ? v.SubId    : 0;
            }

            var undo   = _context.UndoController;
            var before = undo != null ? MultiMeshVertexSnapshot.Capture(model) : null;

            int okMeshes   = 0;
            int partTotal  = 0;
            int movedTotal = 0;
            var failures   = new List<string>();

            foreach (var t in targets)
            {
                bool ok = PlaceObjectReshapeOps.Execute(
                    t.MeshContext.MeshObject,
                    protoPos, protoSubId, targetIds,
                    _settings.Mode, _settings.Lambda,
                    out int parts, out int moved,
                    out List<string> partFailures, out string reason);

                if (!ok)
                {
                    failures.Add($"{t.MeshContext.Name}: {reason}");
                    Debug.LogWarning($"[PlaceObjectReshapeTool] {t.MeshContext.Name}: {reason}");
                    continue;
                }

                foreach (var pf in partFailures)
                {
                    failures.Add($"{t.MeshContext.Name}: {pf}");
                    Debug.LogWarning($"[PlaceObjectReshapeTool] {t.MeshContext.Name}: {pf}");
                }

                okMeshes   ++;
                partTotal  += parts;
                movedTotal += moved;

                t.MeshContext.MeshObject.InvalidatePositionCache();
                _context.SyncMeshContextPositionsOnly?.Invoke(t.MeshContext);
            }

            if (okMeshes == 0)
            {
                LastResult = failures.Count > 0 ? string.Join(" / ", failures) : "対象がありません";
                return;
            }

            if (undo != null)
            {
                var after = MultiMeshVertexSnapshot.Capture(model);

                // MeshListStack の Context を今回のモデルに合わせる（Undo 時の復元先）。
                undo.SetModelContext(model);

                string desc = $"{ModeLabel(_settings.Mode)} ({okMeshes} objs / {partTotal} parts / {movedTotal} verts)";
                var record  = new MultiMeshVertexSnapshotRecord(before, after, desc);
                PLDiag.UndoRecord("MeshList", desc, record);
                undo.MeshListStack.Record(record, desc);
            }

            _context.Repaint?.Invoke();

            LastResult = $"完了: オブジェクト {okMeshes} / 整形パーツ {partTotal} / 移動 {movedTotal} 頂点";
            if (failures.Count > 0)
                LastResult += $"（除外 {failures.Count} 件）";

            Debug.Log($"[PlaceObjectReshapeTool] {LastResult}");
        }

        private static string ModeLabel(PlaceObjectReshapeMode mode)
        {
            switch (mode)
            {
                case PlaceObjectReshapeMode.ThinPlateSpline: return "Place Object Reshape (TPS)";
                default:                                     return "Place Object Reshape (affine)";
            }
        }
    }

    // ================================================================
    // 設定クラス
    // ================================================================

    public class PlaceObjectReshapeSettings : IToolSettings
    {
        /// <summary>
        /// 薄板スプラインの平滑化係数の初期値。
        /// 値の意味はモデルの寸法に依存するため目安でしかない。
        /// 大きいほど原型の形へ寄り、0 に近いほど現在の形をなぞる（整形されない）。
        /// </summary>
        public const float DefaultLambda = 1f;

        /// <summary>推定方式。</summary>
        public PlaceObjectReshapeMode Mode = PlaceObjectReshapeMode.Affine;

        /// <summary>薄板スプラインの平滑化係数。</summary>
        public float Lambda = DefaultLambda;

        /// <summary>対象のパーツID。空欄なら全パーツ。</summary>
        public string TargetText = "";

        public IToolSettings Clone() => new PlaceObjectReshapeSettings
        {
            Mode       = Mode,
            Lambda     = Lambda,
            TargetText = TargetText,
        };

        public void CopyFrom(IToolSettings other)
        {
            if (other is PlaceObjectReshapeSettings s)
            {
                Mode       = s.Mode;
                Lambda     = s.Lambda;
                TargetText = s.TargetText;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is PlaceObjectReshapeSettings s)
                return Mode       != s.Mode
                    || Lambda     != s.Lambda
                    || TargetText != s.TargetText;
            return true;
        }
    }
}

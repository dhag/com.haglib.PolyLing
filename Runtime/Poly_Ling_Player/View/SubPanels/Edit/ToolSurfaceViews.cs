// ToolSurfaceViews.cs
// パネルがツールの窓口（IToolSurface）を型付きのプロパティで読み書きするための薄い包み
// （操作経路統一計画.md E）。パラメータの多いパネルで、ハンドラと同じ名前のまま
// 窓口経由へ置き換えるために使う。値の正本はハンドラにあり、ここは何も持たない。

using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    /// <summary>スカルプト（ツール名 "sculpt"）。</summary>
    public sealed class SculptSurfaceView
    {
        private const string Tool = "sculpt";
        private readonly IToolSurface _s;
        public SculptSurfaceView(IToolSurface s) { _s = s; }

        public SculptMode   Mode             { get => _s.Get(Tool, "mode", SculptMode.Draw);                  set => _s.Set(Tool, "mode", value); }
        public float        BrushRadius      { get => _s.GetFloat(Tool, "brushRadius");                       set => _s.Set(Tool, "brushRadius", value); }
        public float        Strength         { get => _s.GetFloat(Tool, "strength");                          set => _s.Set(Tool, "strength", value); }
        public bool         Invert           { get => _s.GetBool(Tool, "invert");                             set => _s.Set(Tool, "invert", value); }
        public FalloffType  Falloff          { get => _s.Get(Tool, "falloff", FalloffType.Gaussian);          set => _s.Set(Tool, "falloff", value); }
        public DistanceMode DistanceMode     { get => _s.Get(Tool, "distanceMode", DistanceMode.Euclidean);   set => _s.Set(Tool, "distanceMode", value); }
        public float        MinBrushRadius   { get => _s.GetFloat(Tool, "minBrushRadius");                    set => _s.Set(Tool, "minBrushRadius", value); }
        public float        MaxBrushRadius   { get => _s.GetFloat(Tool, "maxBrushRadius");                    set => _s.Set(Tool, "maxBrushRadius", value); }
        public float        MinStrength      { get => _s.GetFloat(Tool, "minStrength");                       set => _s.Set(Tool, "minStrength", value); }
        public float        MaxStrength      { get => _s.GetFloat(Tool, "maxStrength");                       set => _s.Set(Tool, "maxStrength", value); }
        public bool         IsRadiusDragMode { get => _s.GetBool(Tool, "isRadiusDragMode");                   set => _s.Set(Tool, "isRadiusDragMode", value); }
    }

    /// <summary>頂点移動（ツール名 "move"）。</summary>
    public sealed class MoveSurfaceView
    {
        private const string Tool = "move";
        private readonly IToolSurface _s;
        public MoveSurfaceView(IToolSurface s) { _s = s; }

        public bool         UseMagnet          { get => _s.GetBool(Tool, "useMagnet");                               set => _s.Set(Tool, "useMagnet", value); }
        public float        MagnetRadius       { get => _s.GetFloat(Tool, "magnetRadius");                           set => _s.Set(Tool, "magnetRadius", value); }
        public FalloffType  MagnetFalloff      { get => _s.Get(Tool, "magnetFalloff", FalloffType.Smooth);           set => _s.Set(Tool, "magnetFalloff", value); }
        public DistanceMode MagnetDistanceMode { get => _s.Get(Tool, "magnetDistanceMode", DistanceMode.Euclidean);  set => _s.Set(Tool, "magnetDistanceMode", value); }
        public float        MinMagnetRadius    { get => _s.GetFloat(Tool, "minMagnetRadius");                        set => _s.Set(Tool, "minMagnetRadius", value); }
        public float        MaxMagnetRadius    { get => _s.GetFloat(Tool, "maxMagnetRadius");                        set => _s.Set(Tool, "maxMagnetRadius", value); }
        public MoveToolHandler.SelectionDragMode DragSelectMode
        {
            get => _s.Get(Tool, "dragSelectMode", MoveToolHandler.SelectionDragMode.Box);
            set => _s.Set(Tool, "dragSelectMode", value);
        }
        public bool         IsRadiusDragMode   { get => _s.GetBool(Tool, "isRadiusDragMode");                        set => _s.Set(Tool, "isRadiusDragMode", value); }
        public float        GizmoScreenOffsetX { get => _s.GetFloat(Tool, "gizmoScreenOffsetX");                     set => _s.Set(Tool, "gizmoScreenOffsetX", value); }
        public float        GizmoScreenOffsetY { get => _s.GetFloat(Tool, "gizmoScreenOffsetY");                     set => _s.Set(Tool, "gizmoScreenOffsetY", value); }
        public int          GetTotalAffectedCount() => _s.GetInt(Tool, "affectedCount");
    }

    /// <summary>
    /// デフォーマ（ツール名 "deform"）。デフォーマの設定は型がデフォーマごとに違うため、
    /// 読むときは今のデフォーマ名から DeformerRegistry で同じ型の入れ物を作り、窓口の
    /// "params.メンバー名" を詰めて返す（写し）。書くときは EditParams で写しを変え、
    /// 変わったメンバーだけを窓口へ送る。
    /// </summary>
    public sealed class DeformSurfaceView
    {
        private const string Tool = "deform";
        private readonly IToolSurface _s;
        public DeformSurfaceView(IToolSurface s) { _s = s; }

        public DeformToolHandler.DeformPhase Phase
        {
            get => _s.Get(Tool, "phase", DeformToolHandler.DeformPhase.WorkAxis);
            set => _s.Set(Tool, "phase", value);
        }
        public bool         ShowShapePreview   { get => _s.GetBool(Tool, "showShapePreview", true);                  set => _s.Set(Tool, "showShapePreview", value); }
        public bool         UseMagnet          { get => _s.GetBool(Tool, "useMagnet");                               set => _s.Set(Tool, "useMagnet", value); }
        public float        MagnetRadius       { get => _s.GetFloat(Tool, "magnetRadius");                           set => _s.Set(Tool, "magnetRadius", value); }
        public FalloffType  MagnetFalloff      { get => _s.Get(Tool, "magnetFalloff", FalloffType.Smooth);           set => _s.Set(Tool, "magnetFalloff", value); }
        public DistanceMode MagnetDistanceMode { get => _s.Get(Tool, "magnetDistanceMode", DistanceMode.Euclidean);  set => _s.Set(Tool, "magnetDistanceMode", value); }

        public string DeformerName  => _s.GetString(Tool, "deformerName");
        public bool   IsPreviewing  => _s.GetBool(Tool, "isPreviewing");
        public int    AffectedCount => _s.GetInt(Tool, "affectedCount");

        public void ApplyPreview()        => _s.Invoke(Tool, "applyPreview");
        public void Revert()              => _s.Invoke(Tool, "revert");
        public void CommitViaCommand()    => _s.Invoke(Tool, "commitViaCommand");
        public void RequestGizmoRefresh() => _s.Invoke(Tool, "requestGizmoRefresh");
        public void ResetParams()         => _s.Invoke(Tool, "resetParams");
        public void SelectDeformer(string name) => _s.Invoke(Tool, "selectDeformer", ("name", name));

        /// <summary>今のデフォーマの写し（表示名と設定の写し）。未選択なら null。</summary>
        public DeformerCopy Deformer
        {
            get
            {
                if (string.IsNullOrEmpty(DeformerName)) return null;
                return new DeformerCopy { DisplayName = _s.GetString(Tool, "deformerDisplayName"), Params = ReadParams() };
            }
        }

        public sealed class DeformerCopy
        {
            public string DisplayName;
            public object Params;
        }

        /// <summary>プレビューの範囲情報（DeformContext の写し）。</summary>
        public PreviewInfo PreviewContext
        {
            get
            {
                var g = _s.GetGroup(Tool, "previewContext");
                return new PreviewInfo
                {
                    SMin     = g.Item("sMin", 0f),
                    SMax     = g.Item("sMax", 0f),
                    HasRange = g.Item("hasRange", false),
                };
            }
        }

        public sealed class PreviewInfo
        {
            public float SMin, SMax;
            public bool  HasRange;
        }

        /// <summary>今のデフォーマの設定の写しを作る。</summary>
        private object ReadParams()
        {
            var p = Poly_Ling.Tools.Deformers.DeformerRegistry.Create(DeformerName)?.Params;
            if (p == null) return null;
            foreach (var m in Members(p))
            {
                if (!_s.TryGet(Tool, "params." + m.Key, out string raw)) continue;
                if (PanelCommandFactory.TryParseValue(raw, m.Type, out object v, out _)) m.Set(p, v);
            }
            return p;
        }

        /// <summary>設定の写しを set で変え、変わったメンバーだけを窓口へ送る。型が違えば false。</summary>
        public bool EditParams<T>(System.Action<T> set) where T : class
        {
            if (!(ReadParams() is T p)) return false;
            var members = Members(p);
            var before  = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var m in members)
            {
                PanelCommandFactory.TryFormatValue(m.Get(p), out string s);
                before[m.Key] = s;
            }
            set(p);
            foreach (var m in members)
            {
                if (!PanelCommandFactory.TryFormatValue(m.Get(p), out string s)) continue;
                if (before.TryGetValue(m.Key, out string old) && old == s) continue;
                _s.TrySet(Tool, "params." + m.Key, s, out _);
            }
            return true;
        }

        private struct Member
        {
            public string Key;
            public System.Type Type;
            public System.Func<object, object> Get;
            public System.Action<object, object> Set;
        }

        /// <summary>設定オブジェクトの公開フィールドと読み書きできるプロパティ（PLToolSurface の settings と同じ範囲）。</summary>
        private static System.Collections.Generic.List<Member> Members(object p)
        {
            var list = new System.Collections.Generic.List<Member>();
            var t = p.GetType();
            const System.Reflection.BindingFlags Pub =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            foreach (var f in t.GetFields(Pub))
            {
                if (f.IsInitOnly) continue;
                var ff = f;
                list.Add(new Member { Key = PLToolSurface.KeyOf(f.Name), Type = f.FieldType,
                    Get = o => ff.GetValue(o), Set = (o, v) => ff.SetValue(o, v) });
            }
            foreach (var pr in t.GetProperties(Pub))
            {
                if (!pr.CanRead || !pr.CanWrite || pr.GetIndexParameters().Length != 0) continue;
                var pp = pr;
                list.Add(new Member { Key = PLToolSurface.KeyOf(pr.Name), Type = pr.PropertyType,
                    Get = o => pp.GetValue(o), Set = (o, v) => pp.SetValue(o, v) });
            }
            return list;
        }
    }
}

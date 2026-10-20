// FbxTransformEval.cs
// ============================================================
// FBX 読み込み（段 8）のノードの変換とアニメーションの評価。ufbx 0.23.1 と同じ式（行番号は ufbx.c）。
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md 段 8（8.5）。
//   ノードの変換 ufbxi_get_transform 22839、ufbxi_update_node 22958（継承の種類を含む）。
//   ジオメトリ変換 ufbxi_get_geometry_transform 22761。
//   曲線 ufbx_evaluate_curve 30845、外挿 ufbxi_extrapolate_curve 25985、ベジェ ufbxi_find_cubic_bezier_t 25022。
//   プロパティの評価 ufbxi_evaluate_props 25767（レイヤの合成 ufbxi_combine_anim_layer 25707）。
//   ufbx_evaluate_transform 31070 と ufbx_evaluate_scene 26113 は、評価したプロパティで UpdateNode を通すのと同じ。
//   ブレンドの重み ufbx_evaluate_blend_weight 31175。
// ============================================================

using System;
using System.Collections.Generic;

namespace Poly_Ling.Fbx
{
    /// <summary>プロパティの引き先（評価した値 → 要素のプロパティ → テンプレート）。</summary>
    internal sealed class FbxPropLookup
    {
        public FbxPropSet Base;
        public Dictionary<string, (DVec3 v, long i)> Evaluated;

        public DVec3 Vec3(string name, DVec3 def)
        {
            if (Evaluated != null && Evaluated.TryGetValue(name, out var e)) return e.v;
            return Base.FindVec3(name, def);
        }

        public double Real(string name, double def)
        {
            if (Evaluated != null && Evaluated.TryGetValue(name, out var e)) return e.v.x;
            return Base.FindReal(name, def);
        }

        public long Enum(string name, long def, long max)
        {
            if (Evaluated != null && Evaluated.TryGetValue(name, out var e)) return e.i >= 0 && e.i <= max ? e.i : def;
            return Base.FindEnum(name, def, max);
        }
    }

    internal static class FbxTransformEval
    {
        public static readonly string[] TransformProps =
        {
            "Lcl Rotation", "Lcl Scaling", "Lcl Translation", "PostRotation", "PreRotation",
            "RotationOffset", "RotationOrder", "RotationPivot", "ScalingOffset", "ScalingPivot",
        };

        // GetTransform が使う Vec3 プロパティの並び（vals の添字）と既定値
        private static readonly string[] SlotNames =
        {
            "ScalingPivot", "RotationPivot", "ScalingOffset", "RotationOffset", "Lcl Translation",
            "Lcl Rotation", "Lcl Scaling", "PreRotation", "PostRotation",
        };
        private const int SlotCount = 9, SlotScaling = 6;

        private static int SlotOf(string name)
        {
            for (int i = 0; i < SlotCount; i++) if (SlotNames[i] == name) return i;
            return -1;   // RotationOrder
        }

        /// <summary>ufbxi_update_node。親は先に更新されていること。</summary>
        public static void UpdateNode(FbxNodeEl node, FbxPropSet props, Dictionary<string, (DVec3, long)> evaluated = null)
        {
            var look = new FbxPropLookup { Base = props, Evaluated = evaluated };
            int order = (int)look.Enum("RotationOrder", 0, 6);
            var vals = new DVec3[SlotCount];
            for (int i = 0; i < SlotCount; i++) vals[i] = look.Vec3(SlotNames[i], i == SlotScaling ? DVec3.One : DVec3.Zero);
            UpdateNodeCore(node, vals, order, node.IsRoot ? DTransform.Identity : GeometryTransform(props, node));
        }

        // ================================================================
        // まとめて評価するための下ごしらえ（値・式は UpdateNode / EvaluateProps と同じ）
        // ================================================================

        /// <summary>要素の 1 つの「動く」プロパティと、それを持つレイヤ。</summary>
        internal sealed class PropPlan
        {
            public string       Name;
            public FbxProp      Prop;
            public int          Slot;          // ノードの vals の添字（-1 = RotationOrder）
            public int[]        LayerIdx;
            public FbxValueEl[] Values;
            public FbxValueEl[] WeightValues;  // レイヤの重みが動くとき（それ以外 null）
            // つながったプロパティ（ufbx.c 25930-25950 ufbxi_evaluate_connected_prop）
            public bool         IsConnected;
            public FbxElem      SourceElem;
            public PropPlan     Source;        // つなぎ元が動くとき
            public DVec3        SourceStatic;  // つなぎ元が動かないとき（無ければ 0）
        }

        /// <summary>prop へつながる「プロパティからの」接続の最初のもの（ufbx.c 19270-19281）。</summary>
        private static FbxConn FindPropConn(FbxElem e, string prop)
        {
            foreach (var c in e.Dst) if (c.DstProp == prop && c.SrcProp.Length > 0) return c;
            return null;
        }

        private static PropPlan LayerPlan(FbxStackEl stack, FbxElem e, string name, FbxProp prop)
        {
            var li = new List<int>();
            var vs = new List<FbxValueEl>();
            var ws = new List<FbxValueEl>();
            for (int k = 0; k < stack.Layers.Count; k++)
            {
                var layer = stack.Layers[k];
                if (!layer.AnimProps.TryGetValue((e, name), out var av)) continue;
                var wp = layer.Props.Own.TryGetValue("Weight", out var wpp) ? wpp : null;
                FbxValueEl wv = null;
                if (wp != null && wp.Animated && layer.Blended) layer.AnimProps.TryGetValue((layer, "Weight"), out wv);
                li.Add(k); vs.Add(av); ws.Add(wv);
            }
            return new PropPlan
            {
                Name = name, Prop = prop, Slot = SlotOf(name),
                LayerIdx = li.ToArray(), Values = vs.ToArray(), WeightValues = ws.ToArray(),
            };
        }

        /// <summary>ノード 1 つ分：動かない値と、動くプロパティ。</summary>
        internal sealed class NodePlan
        {
            public FbxNodeEl  Node;
            public DVec3[]    BaseVals;
            public int        BaseOrder;
            public DTransform Geo;
            public PropPlan[] Props;          // null = 動くプロパティ無し
            public DVec3[]    Scratch;
        }

        /// <summary>ufbxi_evaluate_selected_props で評価の対象になるプロパティを拾う。</summary>
        internal static PropPlan[] PlanProps(FbxStackEl stack, FbxElem e, string[] names)
        {
            List<PropPlan> list = null;
            foreach (var name in names)
            {
                if (!e.Props.Own.TryGetValue(name, out var prop) || !(prop.Animated || prop.Connected)) continue;
                PropPlan plan = null;
                if (prop.Connected)
                {
                    // つなぎ元をたどる（循環なら止める）。たどれなければ動くプロパティとして扱う（ufbx.c 25930-25950）
                    var conn = FindPropConn(e, name);
                    for (int i = 0; i < 1000 && conn != null; i++)
                    {
                        var next = FindPropConn(conn.Src, conn.SrcProp);
                        if (next == null) break;
                        conn = next;
                    }
                    if (conn != null && FindPropConn(conn.Src, conn.SrcProp) == null)
                    {
                        plan = new PropPlan { Name = name, Prop = prop, Slot = SlotOf(name), IsConnected = true, SourceElem = conn.Src };
                        var sp = conn.Src.Props.Find(conn.SrcProp);
                        if (sp == null) plan.SourceStatic = DVec3.Zero;
                        else if (!sp.Animated) plan.SourceStatic = sp.Vec3;
                        else plan.Source = LayerPlan(stack, conn.Src, conn.SrcProp, sp);
                    }
                }
                if (plan == null) plan = LayerPlan(stack, e, name, prop);
                (list ?? (list = new List<PropPlan>())).Add(plan);
            }
            return list?.ToArray();
        }

        /// <summary>1 つのプロパティを時刻 time で評価する（EvaluateProps の 1 つ分）。</summary>
        internal static DVec3 EvalPlan(FbxStackEl stack, FbxElem e, PropPlan p, double time, double ktime)
        {
            if (p.IsConnected) return p.Source != null ? EvalPlan(stack, p.SourceElem, p.Source, time, ktime) : p.SourceStatic;
            var value = p.Prop.Vec3;
            int rotationOrder = -1;
            for (int k = 0; k < p.LayerIdx.Length; k++)
            {
                int li = p.LayerIdx[k];
                var layer = stack.Layers[li];
                double weight = layer.LayerWeight;
                var wv = p.WeightValues[k];
                if (wv != null)
                {
                    weight = EvalValue(wv, time, ktime).x / 100.0;
                    if (weight < 0.0) weight = 0.0;
                    if (weight > 0.99999f) weight = 1.0;
                }
                var v = EvalValue(p.Values[k], time, ktime);
                if (li == 0) value = v;
                else Combine(stack, e, time, ktime, layer, weight, p.Name, ref value, v, ref rotationOrder);
            }
            return value;
        }

        internal static NodePlan PlanNode(FbxStackEl stack, FbxNodeEl node)
        {
            var look = new FbxPropLookup { Base = node.Props };
            var plan = new NodePlan
            {
                Node = node,
                BaseVals = new DVec3[SlotCount],
                BaseOrder = (int)look.Enum("RotationOrder", 0, 6),
                Geo = node.IsRoot ? DTransform.Identity : GeometryTransform(node.Props, node),
                Props = node.IsRoot ? null : PlanProps(stack, node, TransformProps),
                Scratch = new DVec3[SlotCount],
            };
            for (int i = 0; i < SlotCount; i++) plan.BaseVals[i] = look.Vec3(SlotNames[i], i == SlotScaling ? DVec3.One : DVec3.Zero);
            return plan;
        }

        /// <summary>UpdateNode(node, props, EvaluateProps(...)) と同じ結果。</summary>
        internal static void UpdateNodePlanned(FbxStackEl stack, NodePlan plan, double time, double ktime)
        {
            if (plan.Props == null) { UpdateNodeCore(plan.Node, plan.BaseVals, plan.BaseOrder, plan.Geo); return; }
            var vals = plan.Scratch;
            Array.Copy(plan.BaseVals, vals, SlotCount);
            int order = plan.BaseOrder;
            foreach (var p in plan.Props)
            {
                var v = EvalPlan(stack, plan.Node, p, time, ktime);
                if (p.Slot >= 0) vals[p.Slot] = v;
                else { long o = F64ToI64(v.x); order = o >= 0 && o <= 6 ? (int)o : 0; }
            }
            UpdateNodeCore(plan.Node, vals, order, plan.Geo);
        }

        private static void UpdateNodeCore(FbxNodeEl node, DVec3[] vals, int order, DTransform geo)
        {
            if (!node.IsRoot)
            {
                node.Local = GetTransform(vals, order, node);
                node.NodeToParent = FbxMath.ToMatrix(node.Local);
            }
            else
            {
                node.Local = DTransform.Identity;
                node.NodeToParent = DMat.Identity;
                geo = DTransform.Identity;
            }

            var unscaledToParent = FbxMath.ToUnscaledMatrix(node.Local);
            node.InheritScale = node.Local.S;
            var parent = node.Parent;
            if (parent != null)
            {
                if (node.InheritMode == 0)
                {
                    node.NodeToWorld = DMat.Mul(parent.NodeToWorld, node.NodeToParent);
                    node.UnscaledNodeToWorld = DMat.Mul(parent.NodeToWorld, unscaledToParent);
                }
                else
                {
                    var t = node.Local;
                    var ps = node.InheritScaleNode != null ? node.InheritScaleNode.InheritScale : DVec3.One;
                    t.S = new DVec3(t.S.x * ps.x, t.S.y * ps.y, t.S.z * ps.z);
                    t.T = new DVec3(t.T.x * parent.InheritScale.x, t.T.y * parent.InheritScale.y, t.T.z * parent.InheritScale.z);
                    node.InheritScale = t.S;
                    node.NodeToWorld = DMat.Mul(parent.UnscaledNodeToWorld, FbxMath.ToMatrix(t));
                    node.UnscaledNodeToWorld = DMat.Mul(parent.UnscaledNodeToWorld, FbxMath.ToUnscaledMatrix(t));
                }
            }
            else
            {
                node.NodeToWorld = node.NodeToParent;
                node.UnscaledNodeToWorld = unscaledToParent;
            }

            if (!geo.IsIdentity)
            {
                node.GeometryToNode = FbxMath.ToMatrix(geo);
                node.HasGeometryTransform = true;
            }
            else
            {
                node.GeometryToNode = DMat.Identity;
                node.HasGeometryTransform = false;
            }
        }

        private static DTransform GetTransform(DVec3[] v, int order, FbxNodeEl node)
        {
            var sp = v[0];
            var rp = v[1];
            var so = v[2];
            var ro = v[3];
            var tr = v[4];
            var rot = v[5];
            var sc = v[6];
            var pre = v[7];
            var post = v[8];

            var t = DTransform.Identity;
            // adjust_post_rotation / adjust_post_scale は単位（MODIFY_GEOMETRY）
            FbxMath.SubTranslate(ref t, sp);
            FbxMath.MulScale(ref t, sc);
            FbxMath.AddTranslate(ref t, sp);
            FbxMath.AddTranslate(ref t, so);
            FbxMath.SubTranslate(ref t, rp);
            if (node.UseRotationSpace)
            {
                FbxMath.MulInvRotate(ref t, post, 0);
                FbxMath.MulRotate(ref t, rot, order);
                FbxMath.MulRotate(ref t, pre, 0);
            }
            else
            {
                FbxMath.MulRotate(ref t, rot, 0);
            }
            FbxMath.AddTranslate(ref t, rp);
            FbxMath.AddTranslate(ref t, ro);
            FbxMath.AddTranslate(ref t, tr);
            if (node.HasAdjust)
            {
                FbxMath.MulRotateQuat(ref t, node.AdjustPreRotation);
                t.T = new DVec3(t.T.x * node.AdjustTranslationScale, t.T.y * node.AdjustTranslationScale, t.T.z * node.AdjustTranslationScale);
            }
            return t;
        }

        private static DTransform GeometryTransform(FbxPropSet props, FbxNodeEl node)
        {
            var t = DTransform.Identity;
            FbxMath.MulScale(ref t, props.FindVec3("GeometricScaling", DVec3.One));
            FbxMath.MulRotate(ref t, props.FindVec3("GeometricRotation", DVec3.Zero), 0);
            FbxMath.AddTranslate(ref t, props.FindVec3("GeometricTranslation", DVec3.Zero));
            if (node.HasAdjust)
                t.T = new DVec3(t.T.x * node.AdjustTranslationScale, t.T.y * node.AdjustTranslationScale, t.T.z * node.AdjustTranslationScale);
            return t;
        }

        // ================================================================
        // 曲線
        // ================================================================

        public static double EvalCurve(FbxCurveEl c, double time, double def, double ktimeSecond, bool noExtrapolation = false)
        {
            if (c == null) return def;
            var keys = c.Keys;
            if (keys.Length <= 1) return keys.Length == 1 ? keys[0].Value : def;
            if (!noExtrapolation && (time < c.MinTime || time > c.MaxTime)) return Extrapolate(c, time, ktimeSecond);

            int begin = 0, end = keys.Length;
            while (end - begin >= 8)
            {
                int mid = (begin + end) >> 1;
                if (keys[mid].Time <= time) begin = mid + 1; else end = mid;
            }
            for (; begin < keys.Length; begin++)
            {
                ref FbxKey next = ref keys[begin];
                if (next.Time <= time) continue;
                if (begin == 0) return next.Value;
                ref FbxKey prev = ref keys[begin - 1];
                if (prev.Time == time) return prev.Value;
                double rcp = 1.0 / (next.Time - prev.Time);
                double t = (time - prev.Time) * rcp;
                switch (prev.Interp)
                {
                    case FbxInterp.ConstantPrev: return prev.Value;
                    case FbxInterp.ConstantNext: return next.Value;
                    case FbxInterp.Linear: return prev.Value * (1.0 - t) + next.Value * t;
                    default:
                    {
                        double x1 = prev.RightDx * rcp;
                        double x2 = 1.0 - next.LeftDx * rcp;
                        t = CubicBezierT(x1, x2, t);
                        double t2 = t * t, t3 = t2 * t;
                        double u = 1.0 - t, u2 = u * u, u3 = u2 * u;
                        double y0 = prev.Value, y3 = next.Value;
                        double y1 = y0 + prev.RightDy;
                        double y2 = y3 - next.LeftDy;
                        return u3 * y0 + 3.0 * (u2 * t * y1 + u * t2 * y2) + t3 * y3;
                    }
                }
            }
            return keys[keys.Length - 1].Value;
        }

        private static double CubicBezierT(double p1, double p2, double x0)
        {
            double p1_3 = p1 * 3.0, p2_3 = p2 * 3.0;
            double a = p1_3 - p2_3 + 1.0;
            double b = p2_3 - p1_3 - p1_3;
            double c = p1_3;
            double a_3 = 3.0 * a, b_2 = 2.0 * b;
            double t = x0, x1, t2, t3;
            t2 = t * t; t3 = t2 * t; x1 = a * t3 + b * t2 + c * t - x0; t -= x1 / (a_3 * t2 + b_2 * t + c);
            t2 = t * t; t3 = t2 * t; x1 = a * t3 + b * t2 + c * t - x0; t -= x1 / (a_3 * t2 + b_2 * t + c);
            t2 = t * t; t3 = t2 * t; x1 = a * t3 + b * t2 + c * t - x0; t -= x1 / (a_3 * t2 + b_2 * t + c);
            const double eps = 8.881784197001252e-16;
            if (Math.Abs(x1) <= eps) return t;
            for (int i = 0; i < 4; i++)
            {
                t2 = t * t; t3 = t2 * t; x1 = a * t3 + b * t2 + c * t - x0; t -= x1 / (a_3 * t2 + b_2 * t + c);
                t2 = t * t; t3 = t2 * t; x1 = a * t3 + b * t2 + c * t - x0; t -= x1 / (a_3 * t2 + b_2 * t + c);
                if (Math.Abs(x1) <= eps) return t;
            }
            return t;
        }

        private static double Extrapolate(FbxCurveEl c, double realTime, double ktimeSecond)
        {
            bool pre = realTime < c.MinTime;
            var key = pre ? c.Keys[0] : c.Keys[c.Keys.Length - 1];
            int mode = pre ? c.PreMode : c.PostMode;
            int repeat = pre ? c.PreRepeat : c.PostRepeat;
            if (mode == 0) return key.Value;
            if (mode == 3)
            {
                float dx = pre ? key.RightDx : key.LeftDx;
                float dy = pre ? key.RightDy : key.LeftDy;
                return key.Value + dy * ((realTime - key.Time) / dx);
            }
            if (repeat == 0) return key.Value;

            double scale = ktimeSecond;
            double minTime = Math.Round(c.MinTime * scale, MidpointRounding.ToEven);
            double maxTime = Math.Round(c.MaxTime * scale, MidpointRounding.ToEven);
            double time = realTime * scale;
            double delta = pre ? minTime - time : time - maxTime;
            double duration = maxTime - minTime;
            if (!(duration >= 1.0)) return key.Value;
            double rep = delta / duration;
            double repN = Math.Floor(rep);
            double repD = delta - repN * duration;
            if (repeat > 0 && repN >= repeat) { repN = repeat - 1; repD = duration; }
            if (mode == 2)
            {
                double parity = repN * 0.5 - Math.Floor(repN * 0.5);
                if (parity <= 0.25) repD = duration - repD;
            }
            if (pre) repD = duration - repD;
            double newTime = (minTime + repD) / scale;
            double value = EvalCurve(c, newTime, key.Value, ktimeSecond, true);
            if (mode == 4)
            {
                double vd = c.Keys[c.Keys.Length - 1].Value - c.Keys[0].Value;
                if (pre) vd = -vd;
                value += vd * (repN + 1.0);
            }
            return value;
        }

        public static DVec3 EvalValue(FbxValueEl v, double time, double ktime)
        {
            var r = v.Default;
            if (v.Curves[0] != null) r.x = EvalCurve(v.Curves[0], time, r.x, ktime);
            if (v.Curves[1] != null) r.y = EvalCurve(v.Curves[1], time, r.y, ktime);
            if (v.Curves[2] != null) r.z = EvalCurve(v.Curves[2], time, r.z, ktime);
            return r;
        }

        // ================================================================
        // プロパティの評価（レイヤの合成）
        // ================================================================

        /// <summary>
        /// 要素の props のうち names に入る「動く」プロパティを時刻 time で評価して返す（ufbxi_evaluate_selected_props）。
        /// 動かないプロパティは入れない（引く側が要素のプロパティへ戻る）。
        /// </summary>
        public static Dictionary<string, (DVec3, long)> EvaluateProps(FbxStackEl stack, FbxElem e, double time, double ktime, string[] names)
        {
            var plans = PlanProps(stack, e, names);
            if (plans == null) return null;
            var result = new Dictionary<string, (DVec3, long)>(StringComparer.Ordinal);
            foreach (var p in plans)
            {
                var value = EvalPlan(stack, e, p, time, ktime);
                result[p.Name] = (value, F64ToI64(value.x));
            }
            return result;
        }

        private static long F64ToI64(double d)
        {
            if (double.IsNaN(d)) return 0;
            if (d >= 9.2233720368547758e18) return long.MaxValue;
            if (d <= -9.2233720368547758e18) return long.MinValue;
            return (long)d;
        }

        private static double PowAbs(double v, double e)
        {
            if (e <= 0.0) return 1.0;
            if (e >= 1.0) return v;
            double sign = v < 0.0 ? -1.0 : 1.0;
            return sign * Math.Pow(v * sign, e);
        }

        private static void Combine(FbxStackEl stack, FbxElem e, double time, double ktime, FbxLayerEl layer, double weight,
                                    string name, ref DVec3 result, DVec3 value, ref int rotationOrder)
        {
            if (layer.ComposeRotation && layer.Blended && name == "Lcl Rotation" && rotationOrder < 0)
            {
                var ro = EvaluateProps(stack, e, time, ktime, new[] { "RotationOrder" });
                long o = ro != null && ro.TryGetValue("RotationOrder", out var rv) ? rv.Item2 : e.Props.FindInt("RotationOrder", 0);
                rotationOrder = o >= 0 && o <= 6 ? (int)o : 0;
            }
            if (layer.Additive)
            {
                if (layer.ComposeScale && name == "Lcl Scaling")
                    result = new DVec3(result.x * PowAbs(value.x, weight), result.y * PowAbs(value.y, weight), result.z * PowAbs(value.z, weight));
                else if (layer.ComposeRotation && name == "Lcl Rotation")
                {
                    var a = FbxMath.EulerToQuat(result, rotationOrder);
                    var b = FbxMath.Slerp(DQuat.Identity, FbxMath.EulerToQuat(value, rotationOrder), weight);
                    result = FbxMath.QuatToEuler(FbxMath.MulQuat(a, b), rotationOrder);
                }
                else result = new DVec3(result.x + value.x * weight, result.y + value.y * weight, result.z + value.z * weight);
            }
            else if (layer.Blended)
            {
                double rw = 1.0 - weight;
                if (layer.ComposeScale && name == "Lcl Scaling")
                    result = new DVec3(PowAbs(result.x, rw) * PowAbs(value.x, weight), PowAbs(result.y, rw) * PowAbs(value.y, weight), PowAbs(result.z, rw) * PowAbs(value.z, weight));
                else if (layer.ComposeRotation && name == "Lcl Rotation")
                {
                    var a = FbxMath.EulerToQuat(result, rotationOrder);
                    var b = FbxMath.EulerToQuat(value, rotationOrder);
                    result = FbxMath.QuatToEuler(FbxMath.Slerp(a, b, weight), rotationOrder);
                }
                else result = new DVec3(result.x * rw + value.x * weight, result.y * rw + value.y * weight, result.z * rw + value.z * weight);
            }
            else result = value;
        }

        private static readonly string[] DeformPercentName = { "DeformPercent" };

        /// <summary>ufbx_evaluate_blend_weight。</summary>
        public static double BlendWeight(FbxStackEl stack, FbxChannelEl ch, double time, double ktime)
        {
            var ev = EvaluateProps(stack, ch, time, ktime, DeformPercentName);
            double dp = ev != null && ev.TryGetValue("DeformPercent", out var v) ? v.Item1.x : ch.Props.FindReal("DeformPercent", ch.Weight * 100.0);
            return dp * 0.01;
        }

        internal static PropPlan[] PlanBlend(FbxStackEl stack, FbxChannelEl ch) => PlanProps(stack, ch, DeformPercentName);

        /// <summary>BlendWeight と同じ結果（plan は PlanBlend）。</summary>
        internal static double BlendWeightPlanned(FbxStackEl stack, FbxChannelEl ch, PropPlan[] plan, double time, double ktime)
        {
            double dp = plan != null ? EvalPlan(stack, ch, plan[0], time, ktime).x : ch.Props.FindReal("DeformPercent", ch.Weight * 100.0);
            return dp * 0.01;
        }
    }
}

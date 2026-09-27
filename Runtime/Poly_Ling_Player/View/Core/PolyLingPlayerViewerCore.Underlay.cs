// PolyLingPlayerViewerCore.Underlay.cs
// Player ビューアのコア：下絵コマンド（setUnderlay / clearUnderlay / queryUnderlay）の受け口。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 設定の正本は ModelContext.Underlay（UnderlayData）。画像は UnderlayConfig が読んで持つ。
// 表示の貼り直しは ApplyAllUnderlays（ViewAids.cs）。形状ではないので Undo は残さない。

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        /// <summary>setUnderlay の受け口。失敗理由を返す（成功なら null）。</summary>
        private string ExecuteSetUnderlay(SetUnderlayCommand c, ModelContext model, CommandDataBuilder data)
        {
            if (c == null) return "コマンドが null";
            if (model == null) return "モデルがありません";

            var dir = c.Direction;
            var cur = model.Underlay?.Get(dir);

            // 画像のパス。指定があれば作業フォルダの関門を通す。無ければ今の画像のまま。
            string path = cur?.FilePath ?? "";
            bool newImage = !string.IsNullOrEmpty(c.FilePath);
            if (newImage)
            {
                if (!Poly_Ling.Core.PLSandbox.TryResolveRead(c.FilePath, out string full, out string reason))
                    return reason;
                path = full;
            }
            if (string.IsNullOrEmpty(path)) return "この方向には画像がありません。filePath を指定してください";

            var tex = _underlay.Load(path, reload: newImage, out string loadError);
            if (tex == null) return loadError;

            var s = cur != null ? cur.Clone() : new UnderlaySlotData();
            s.FilePath = path;

            if (UnderlayData.IsModelAnchored(dir))
            {
                if (!c.KeepPlacement && c.Corner0 != c.Corner1)
                {
                    s.Corner0 = c.Corner0;
                    s.Corner1 = c.Corner1;
                }
                else if (!c.KeepPlacement || !s.HasCorners)
                {
                    // 既定の置き方：原点を中心に、1 画素 = MQO の 1 単位。
                    float unit = model.CoordinateConvention?.MqoUnityRatio ?? 0.01f;
                    UnderlayData.DefaultCorners(dir, tex.width, tex.height, unit, out var a, out var b);
                    s.Corner0 = a;
                    s.Corner1 = b;
                }
            }
            else if (!c.KeepPlacement)
            {
                s.TopLeft     = c.TopLeft;
                s.ScaleOrigin = c.ScaleOrigin;
                s.Scale       = new Vector2(Mathf.Max(0.01f, c.ScaleX), Mathf.Max(0.01f, c.ScaleY));
            }

            if (model.Underlay == null) model.Underlay = new UnderlayData();
            model.Underlay.Set(dir, s);
            model.IsDirty = true;

            ApplyAllUnderlays();
            _underlaySubPanel?.RefreshFields(dir);

            data.Text("direction", UnderlayData.NameOf(dir))
                .Text("filePath", path)
                .Int("width", tex.width)
                .Int("height", tex.height);
            return null;
        }

        /// <summary>clearUnderlay の受け口。失敗理由を返す（成功なら null）。</summary>
        private string ExecuteClearUnderlay(ClearUnderlayCommand c, ModelContext model, CommandDataBuilder data)
        {
            if (c == null) return "コマンドが null";
            if (model == null) return "モデルがありません";

            if (model.Underlay != null)
            {
                if (c.AllDirections) model.Underlay = null;
                else
                {
                    model.Underlay.Set(c.Direction, null);
                    if (model.Underlay.IsEmpty) model.Underlay = null;
                }
                model.IsDirty = true;
            }

            ApplyAllUnderlays();
            _underlaySubPanel?.Refresh();

            data.Int("remaining", CountUnderlays(model.Underlay));
            return null;
        }

        /// <summary>queryUnderlay の受け口。失敗理由を返す（成功なら null）。</summary>
        private string ExecuteQueryUnderlay(QueryUnderlayCommand c, ModelContext model, CommandDataBuilder data)
        {
            if (c == null) return "コマンドが null";
            if (model == null) return "モデルがありません";

            var dirs = new List<string>();
            var paths = new List<string>();
            var placements = new List<string>();
            var sizes = new List<string>();

            var u = model.Underlay;
            if (u != null)
            {
                foreach (UnderlayDirection dir in Enum.GetValues(typeof(UnderlayDirection)))
                {
                    var s = u.Get(dir);
                    if (s == null || s.IsEmpty) continue;

                    dirs.Add(UnderlayData.NameOf(dir));
                    paths.Add(s.FilePath);
                    placements.Add(UnderlayData.IsModelAnchored(dir)
                        ? $"corner0={V(s.Corner0)} corner1={V(s.Corner1)}"
                        : $"topLeft={V(s.TopLeft)} scaleOrigin={V(s.ScaleOrigin)} scale={V(s.Scale)}");

                    var tex = _underlay.Load(s.FilePath, reload: false, out _);
                    sizes.Add(tex != null ? $"{tex.width}x{tex.height}" : "");
                }
            }

            data.Int("count", dirs.Count);
            if (dirs.Count > 0)
            {
                data.Texts("directions", dirs)
                    .Texts("filePaths", paths)
                    .Texts("placements", placements)
                    .Texts("imageSizes", sizes);
            }
            return null;
        }

        private static int CountUnderlays(UnderlayData u)
        {
            if (u == null) return 0;
            int n = 0;
            foreach (UnderlayDirection dir in Enum.GetValues(typeof(UnderlayDirection)))
                if (!u.Get(dir).IsEmpty) n++;
            return n;
        }

        private static string F(float v) => v.ToString("G6", CultureInfo.InvariantCulture);
        private static string V(Vector2 v) => $"({F(v.x)}, {F(v.y)})";
        private static string V(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";
    }
}

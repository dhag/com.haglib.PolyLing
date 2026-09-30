// Remote/RemoteWorkAxisSync.cs
// 作業軸の値の push（サーバ→クライアント）。組み立てと適用を 1 か所にまとめる。
//
// 【何を送るか】
//   現在モデルの作業軸オブジェクト（MeshContext.IsWorkAxis）全部の値と、
//   使う作業軸の ID（ModelContext.ActiveWorkAxisObjectId）。
//   作業軸オブジェクトの追加・削除は一覧変更（meshListChanged → 再フェッチ）で届くので、
//   ここでは扱わない。受け側に無い ID の値は捨てる。
//
// 【いつ送るか】
//   サーバ側で作業軸の変更が確定したとき（PolyLingPlayerViewerCore.NotifyWorkAxisChanged）。
//   ギズモのドラッグ中はプレビューで、確定時の SetWorkAxisCommand で 1 回だけ届く。
//
// 【形式】
//   {"type":"push","event":"workAxisChanged","data":{...WorkAxisPushData...}}
//   data は JsonUtility で読み書きする。objectId は ulong を落とさないよう 10 進文字列。

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Remote
{
    [Serializable]
    public class WorkAxisPushItem
    {
        public string     objectId;
        public Vector3    origin;
        public Quaternion rotation;
        public float      length;
        public bool       isVisible;
    }

    [Serializable]
    public class WorkAxisPushData
    {
        public int    modelIndex;
        public string activeWorkAxisObjectId;
        public List<WorkAxisPushItem> axes = new List<WorkAxisPushItem>();
    }

    public static class RemoteWorkAxisSync
    {
        public const string EventName = "workAxisChanged";

        /// <summary>JsonUtility で push 全体から data だけを取り出すための外枠。</summary>
        [Serializable]
        private class Envelope
        {
            public WorkAxisPushData data;
        }

        /// <summary>push の data 部（JSON）を組み立てる。</summary>
        public static string BuildData(ModelContext model, int modelIndex)
        {
            var d = new WorkAxisPushData
            {
                modelIndex             = modelIndex,
                activeWorkAxisObjectId = (model?.ActiveWorkAxisObjectId ?? 0UL).ToString(CultureInfo.InvariantCulture),
            };

            if (model != null)
            {
                int count = model.MeshContextCount;
                for (int i = 0; i < count; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null || !mc.IsWorkAxis) continue;
                    var wa = mc.WorkAxis;
                    d.axes.Add(new WorkAxisPushItem
                    {
                        objectId  = mc.ObjectId.ToString(CultureInfo.InvariantCulture),
                        origin    = wa.Origin,
                        rotation  = wa.Rotation,
                        length    = wa.Length,
                        isVisible = wa.IsVisible,
                    });
                }
            }

            return JsonUtility.ToJson(d);
        }

        /// <summary>push 全体の JSON から data を読む。読めなければ null。</summary>
        public static WorkAxisPushData Parse(string pushJson)
        {
            if (string.IsNullOrEmpty(pushJson)) return null;
            try { return JsonUtility.FromJson<Envelope>(pushJson)?.data; }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// 受け取った値をモデルへ当てる。書き込みは WorkAxisContext.ApplySnapshot に通す
        /// （下限クランプを含めてサーバ側の SetWorkAxisCommand と同じ受け口）。
        /// </summary>
        /// <returns>何か 1 つでも当てたら true。</returns>
        public static bool Apply(ModelContext model, WorkAxisPushData data)
        {
            if (model == null || data == null) return false;

            bool applied = false;
            if (data.axes != null)
            {
                foreach (var item in data.axes)
                {
                    if (item == null || !TryParseId(item.objectId, out ulong id) || id == 0UL) continue;
                    var mc = FindWorkAxis(model, id);
                    if (mc == null) continue;   // 受け側に無い軸。再フェッチで届くのを待つ。

                    mc.WorkAxis.ApplySnapshot(new WorkAxisSnapshot
                    {
                        Origin    = item.origin,
                        Rotation  = item.rotation,
                        Length    = item.length,
                        IsVisible = item.isVisible,
                    });
                    applied = true;
                }
            }

            // 使う軸は、受け側に実在するときだけ切り替える（無い ID を覚えさせない）。
            if (TryParseId(data.activeWorkAxisObjectId, out ulong activeId)
                && activeId != 0UL && FindWorkAxis(model, activeId) != null
                && model.ActiveWorkAxisObjectId != activeId)
            {
                model.ActiveWorkAxisObjectId = activeId;
                applied = true;
            }

            return applied;
        }

        private static MeshContext FindWorkAxis(ModelContext model, ulong objectId)
        {
            int count = model.MeshContextCount;
            for (int i = 0; i < count; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && mc.IsWorkAxis && mc.ObjectId == objectId) return mc;
            }
            return null;
        }

        private static bool TryParseId(string s, out ulong id)
            => ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }
}

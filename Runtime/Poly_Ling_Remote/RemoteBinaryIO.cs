// Remote/RemoteBinaryIO.cs
// バイナリ転送の共用の読み書き部品。
//   RemoteBinarySerializer（メッシュ本体 PLRM / PLRD）と
//   RemoteProgressiveSerializer（PLRH / PLRM / PLRS）の両方が使う。
//
// 【決まり】ここの形は CSV（CsvMeshSerializer / CsvModelSerializer）と同じ項目を運ぶ。
//   CSV に項目を足したらここにも足し、版番号を上げること（PolyLing_追加作業の必読.md の E）。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Remote
{
    internal static class RemoteBinaryIO
    {
        // ================================================================
        // 基本型
        // ================================================================

        /// <summary>[2B 長さ] + UTF8。null は空文字。</summary>
        public static void WriteString(BinaryWriter w, string s)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s ?? "");
            w.Write((ushort)bytes.Length);
            w.Write(bytes);
        }

        public static string ReadString(BinaryReader r)
        {
            ushort len = r.ReadUInt16();
            return System.Text.Encoding.UTF8.GetString(r.ReadBytes(len));
        }

        /// <summary>[4B 長さ] + UTF8。64KB を超えうる文字列用。</summary>
        public static void WriteLongString(BinaryWriter w, string s)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s ?? "");
            w.Write(bytes.Length);
            w.Write(bytes);
        }

        public static string ReadLongString(BinaryReader r)
        {
            int len = r.ReadInt32();
            return System.Text.Encoding.UTF8.GetString(r.ReadBytes(len));
        }

        public static void WriteVector2(BinaryWriter w, Vector2 v) { w.Write(v.x); w.Write(v.y); }
        public static Vector2 ReadVector2(BinaryReader r) => new Vector2(r.ReadSingle(), r.ReadSingle());

        public static void WriteVector3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        public static Vector3 ReadVector3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        public static void WriteVector4(BinaryWriter w, Vector4 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); }
        public static Vector4 ReadVector4(BinaryReader r) => new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        public static void WriteQuaternion(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
        public static Quaternion ReadQuaternion(BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        public static void WriteMatrix(BinaryWriter w, Matrix4x4 m) { for (int i = 0; i < 16; i++) w.Write(m[i]); }
        public static Matrix4x4 ReadMatrix(BinaryReader r) { var m = new Matrix4x4(); for (int i = 0; i < 16; i++) m[i] = r.ReadSingle(); return m; }

        public static void WriteIntList(BinaryWriter w, IList<int> list)
        {
            int n = list?.Count ?? 0;
            w.Write(n);
            for (int i = 0; i < n; i++) w.Write(list[i]);
        }

        public static List<int> ReadIntList(BinaryReader r)
        {
            int n = r.ReadInt32();
            var list = new List<int>(n);
            for (int i = 0; i < n; i++) list.Add(r.ReadInt32());
            return list;
        }

        public static void WriteBoneWeight(BinaryWriter w, BoneWeight bw)
        {
            w.Write(bw.boneIndex0); w.Write(bw.boneIndex1); w.Write(bw.boneIndex2); w.Write(bw.boneIndex3);
            w.Write(bw.weight0);    w.Write(bw.weight1);    w.Write(bw.weight2);    w.Write(bw.weight3);
        }

        public static BoneWeight ReadBoneWeight(BinaryReader r) => new BoneWeight
        {
            boneIndex0 = r.ReadInt32(), boneIndex1 = r.ReadInt32(),
            boneIndex2 = r.ReadInt32(), boneIndex3 = r.ReadInt32(),
            weight0 = r.ReadSingle(), weight1 = r.ReadSingle(),
            weight2 = r.ReadSingle(), weight3 = r.ReadSingle(),
        };

        // ================================================================
        // 選択セット（CSV の ss / nx 行と同じ項目）
        //   ss 行は面・線分・辺の識別子の控えまで持つ。nx 行は頂点の控えまで。
        //   ここでは両方とも全項目を運ぶ（nx 側の控えは空で届くだけ）。
        // ================================================================

        public static void WritePartsSet(BinaryWriter w, PartsSelectionSet ss)
        {
            WriteString(w, ss.Name);
            w.Write((int)ss.Mode);

            w.Write(ss.Vertices.Count);
            foreach (var v in ss.Vertices) w.Write(v);

            w.Write(ss.Edges.Count);
            foreach (var e in ss.Edges) { w.Write(e.V1); w.Write(e.V2); }

            w.Write(ss.Faces.Count);
            foreach (var f in ss.Faces) w.Write(f);

            w.Write(ss.Lines.Count);
            foreach (var l in ss.Lines) w.Write(l);

            var vids = ss.VertexIds;
            w.Write(vids?.Count ?? 0);
            if (vids != null)
                foreach (var kv in vids)
                { w.Write(kv.Key); w.Write(kv.Value.Id); w.Write(kv.Value.PartsId); w.Write(kv.Value.SubId); }

            var fids = ss.FaceIds;
            w.Write(fids?.Count ?? 0);
            if (fids != null) foreach (var kv in fids) { w.Write(kv.Key); w.Write(kv.Value); }

            var lids = ss.LineIds;
            w.Write(lids?.Count ?? 0);
            if (lids != null) foreach (var kv in lids) { w.Write(kv.Key); w.Write(kv.Value); }

            var eids = ss.EdgeVertexIds;
            w.Write(eids?.Count ?? 0);
            if (eids != null)
                foreach (var kv in eids)
                { w.Write(kv.Key.V1); w.Write(kv.Key.V2); w.Write(kv.Value.V1); w.Write(kv.Value.V2); }
        }

        public static PartsSelectionSet ReadPartsSet(BinaryReader r)
        {
            var ss = new PartsSelectionSet(ReadString(r));
            ss.Mode = (MeshSelectMode)r.ReadInt32();

            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) ss.Vertices.Add(r.ReadInt32());

            n = r.ReadInt32();
            for (int i = 0; i < n; i++) { int a = r.ReadInt32(); int b = r.ReadInt32(); ss.Edges.Add(new VertexPair(a, b)); }

            n = r.ReadInt32();
            for (int i = 0; i < n; i++) ss.Faces.Add(r.ReadInt32());

            n = r.ReadInt32();
            for (int i = 0; i < n; i++) ss.Lines.Add(r.ReadInt32());

            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                int vi = r.ReadInt32(); int id = r.ReadInt32(); int parts = r.ReadInt32(); int sub = r.ReadInt32();
                ss.VertexIds[vi] = new VertexIdTriple(id, parts, sub);
            }

            n = r.ReadInt32();
            for (int i = 0; i < n; i++) { int k = r.ReadInt32(); ss.FaceIds[k] = r.ReadInt32(); }

            n = r.ReadInt32();
            for (int i = 0; i < n; i++) { int k = r.ReadInt32(); ss.LineIds[k] = r.ReadInt32(); }

            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                int a = r.ReadInt32(); int b = r.ReadInt32(); int c = r.ReadInt32(); int d = r.ReadInt32();
                ss.EdgeVertexIds[new VertexPair(a, b)] = new VertexPair(c, d);
            }

            return ss;
        }

        public static void WritePartsSetList(BinaryWriter w, List<PartsSelectionSet> list)
        {
            int n = 0;
            if (list != null) foreach (var s in list) if (s != null) n++;
            w.Write(n);
            if (list == null) return;
            foreach (var s in list) if (s != null) WritePartsSet(w, s);
        }

        public static List<PartsSelectionSet> ReadPartsSetList(BinaryReader r)
        {
            int n = r.ReadInt32();
            var list = new List<PartsSelectionSet>(n);
            for (int i = 0; i < n; i++) list.Add(ReadPartsSet(r));
            return list;
        }

        // ================================================================
        // 線分群（CSV の lg 行と同じ項目）
        // ================================================================

        public static void WriteLineGroups(BinaryWriter w, List<LineGroup> list)
        {
            // CSV と同じく、点の無い群は書かない。
            int n = 0;
            if (list != null) foreach (var g in list) if (g != null && g.Order != null && g.Order.Count > 0) n++;
            w.Write(n);
            if (list == null) return;

            foreach (var g in list)
            {
                if (g == null || g.Order == null || g.Order.Count == 0) continue;

                WriteString(w, g.Name);
                w.Write(g.Closed);
                w.Write(g.ParentVertex);
                w.Write(g.ParentVertexId);

                w.Write(g.Order.Count);
                for (int k = 0; k < g.Order.Count; k++)
                {
                    int id = (g.OrderVertexIds != null && k < g.OrderVertexIds.Count) ? g.OrderVertexIds[k] : 0;
                    w.Write(g.Order[k]);
                    w.Write(id);
                }

                bool hasHandles = g.HasHandles;
                w.Write(hasHandles ? g.PointHandles.Count : 0);
                if (hasHandles)
                {
                    foreach (var h0 in g.PointHandles)
                    {
                        var h = h0 ?? LinePointHandle.CreateDefault();
                        WriteVector3(w, h.InOffset);
                        WriteVector3(w, h.OutOffset);
                        WriteHandleConstraint(w, h.InConstraint);
                        WriteHandleConstraint(w, h.OutConstraint);
                    }
                }

                int lgCount = g.LengthGroups?.Count ?? 0;
                w.Write(lgCount);
                for (int k = 0; k < lgCount; k++)
                {
                    var lg = g.LengthGroups[k];
                    w.Write(lg?.Id ?? 0);
                    w.Write(lg?.Length ?? 0f);
                }
            }
        }

        public static List<LineGroup> ReadLineGroups(BinaryReader r)
        {
            int n = r.ReadInt32();
            var list = new List<LineGroup>(n);
            for (int i = 0; i < n; i++)
            {
                var g = new LineGroup(ReadString(r));
                g.Closed         = r.ReadBoolean();
                g.ParentVertex   = r.ReadInt32();
                g.ParentVertexId = r.ReadInt32();

                int count = r.ReadInt32();
                for (int k = 0; k < count; k++)
                {
                    g.Order.Add(r.ReadInt32());
                    g.OrderVertexIds.Add(r.ReadInt32());
                }

                int hCount = r.ReadInt32();
                for (int k = 0; k < hCount; k++)
                {
                    var h = new LinePointHandle();
                    h.InOffset      = ReadVector3(r);
                    h.OutOffset     = ReadVector3(r);
                    h.InConstraint  = ReadHandleConstraint(r);
                    h.OutConstraint = ReadHandleConstraint(r);
                    g.PointHandles.Add(h);
                }

                int lgCount = r.ReadInt32();
                for (int k = 0; k < lgCount; k++)
                {
                    int   lid = r.ReadInt32();
                    float len = r.ReadSingle();
                    g.LengthGroups.Add(new LineLengthGroup { Id = lid, Length = len });
                }

                list.Add(g);
            }
            return list;
        }

        private static void WriteHandleConstraint(BinaryWriter w, HandleConstraint c)
        {
            w.Write((int)c.Direction);
            w.Write((int)c.Length);
            w.Write(c.LengthGroupId);
            w.Write(c.Ratio);
        }

        private static HandleConstraint ReadHandleConstraint(BinaryReader r) => new HandleConstraint
        {
            Direction     = (HandleDirection)r.ReadInt32(),
            Length        = (HandleLength)r.ReadInt32(),
            LengthGroupId = r.ReadInt32(),
            Ratio         = r.ReadSingle(),
        };
    }
}

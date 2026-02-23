// Remote/RemoteDataProvider.cs
// ToolContext/ModelContext/MeshContextからリクエストされたフィールドを
// JSON形式で抽出するプロバイダー
//
// フィールド名はMeshContext/MeshObjectのプロパティ名と一致させる。
// 明示的マッピング辞書により、将来のフィールド追加はExtractorの登録のみで済む。

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Model;

namespace Poly_Ling.Remote
{
    /// <summary>
    /// フィールド抽出関数の型
    /// MeshContextとJsonBuilderを受け取り、値を書き込む
    /// </summary>
    public delegate void FieldExtractor(MeshContext mc, JsonBuilder jb);

    /// <summary>
    /// リモートデータプロバイダー
    /// </summary>
    public static class RemoteDataProvider
    {
        // ================================================================
        // フィールドマッピング辞書
        // ================================================================

        private static readonly Dictionary<string, FieldExtractor> _meshContextFields
            = new Dictionary<string, FieldExtractor>
        {
            // --- 基本属性 ---
            ["Name"]        = (mc, jb) => jb.Value(mc.Name),
            ["IsVisible"]   = (mc, jb) => jb.Value(mc.IsVisible),
            ["IsLocked"]    = (mc, jb) => jb.Value(mc.IsLocked),
            ["Type"]        = (mc, jb) => jb.Value(mc.Type.ToString()),
            ["MirrorType"]  = (mc, jb) => jb.Value(mc.MirrorType),
            ["Depth"]       = (mc, jb) => jb.Value(mc.Depth),
            ["ParentIndex"] = (mc, jb) => jb.Value(mc.ParentIndex),
            ["IsMorph"]     = (mc, jb) => jb.Value(mc.IsMorph),
            ["MorphName"]   = (mc, jb) => jb.Value(mc.MorphName),
            ["MorphPanel"]  = (mc, jb) => jb.Value(mc.MorphPanel),
            ["ExcludeFromExport"] = (mc, jb) => jb.Value(mc.ExcludeFromExport),

            // --- 統計 ---
            ["VertexCount"] = (mc, jb) => jb.Value(mc.VertexCount),
            ["FaceCount"]   = (mc, jb) => jb.Value(mc.FaceCount),

            // --- 頂点データ（重いため明示リクエスト時のみ） ---
            ["Positions"]   = (mc, jb) => WriteVector3Array(mc.MeshObject?.Positions, jb),
            ["Normals"]     = (mc, jb) => WriteNormals(mc, jb),

            // --- 選択状態 ---
            ["SelectedVertices"] = (mc, jb) => WriteIntSet(mc.SelectedVertices, jb),
            ["SelectedFaces"]    = (mc, jb) => WriteIntSet(mc.SelectedFaces, jb),
            ["SelectMode"]       = (mc, jb) => jb.Value(mc.SelectMode.ToString()),

            // --- ボーン ---
            ["BoneTransformPosition"] = (mc, jb) =>
                WriteVector3(mc.BoneTransform?.Position ?? Vector3.zero, jb),
            ["BoneTransformRotation"] = (mc, jb) =>
                WriteVector3(mc.BoneTransform?.Rotation ?? Vector3.zero, jb),
            ["WorldMatrix"] = (mc, jb) => WriteMatrix4x4(mc.WorldMatrix, jb),

            // --- ミラー ---
            ["MirrorAxis"]     = (mc, jb) => jb.Value(mc.MirrorAxis),
            ["MirrorDistance"]  = (mc, jb) => jb.Value(mc.MirrorDistance),
            ["IsBakedMirror"]  = (mc, jb) => jb.Value(mc.IsBakedMirror),
        };

        // ================================================================
        // フィールド登録API（拡張用）
        // ================================================================

        /// <summary>
        /// フィールドExtractorを追加登録
        /// </summary>
        public static void RegisterField(string fieldName, FieldExtractor extractor)
        {
            _meshContextFields[fieldName] = extractor;
        }

        /// <summary>
        /// 登録済みフィールド名一覧を取得
        /// </summary>
        public static IEnumerable<string> GetAvailableFields()
        {
            return _meshContextFields.Keys;
        }

        // ================================================================
        // クエリ処理
        // ================================================================

        /// <summary>
        /// メッシュリスト全体のクエリ
        /// fieldsがnullの場合はデフォルトフィールドを返す
        /// </summary>
        public static string QueryMeshList(ToolContext ctx, string[] fields)
        {
            if (ctx?.Model == null) return "[]";

            var meshList = ctx.Model.MeshContextList;
            var selectedIndices = ctx.Model.SelectedMeshIndices;
            string[] effectiveFields = fields ?? DefaultListFields;

            var jb = new JsonBuilder();
            jb.BeginObject();

            // メッシュ配列
            jb.Key("meshes").BeginArray();
            for (int i = 0; i < meshList.Count; i++)
            {
                var mc = meshList[i];
                jb.BeginObject();
                jb.KeyValue("index", i);
                jb.KeyValue("selected", selectedIndices.Contains(i));

                foreach (var field in effectiveFields)
                {
                    if (_meshContextFields.TryGetValue(field, out var extractor))
                    {
                        jb.Key(field);
                        extractor(mc, jb);
                    }
                }
                jb.EndObject();
            }
            jb.EndArray();

            // メタ情報
            jb.KeyValue("count", meshList.Count);
            jb.KeyValue("activeCategory", ctx.Model.ActiveCategory.ToString());

            jb.EndObject();
            return jb.ToString();
        }

        /// <summary>
        /// 特定メッシュの詳細クエリ
        /// </summary>
        public static string QueryMeshData(ToolContext ctx, int index, string[] fields)
        {
            if (ctx?.Model == null) return "null";

            var meshList = ctx.Model.MeshContextList;
            if (index < 0 || index >= meshList.Count) return "null";

            var mc = meshList[index];
            string[] effectiveFields = fields ?? DefaultDetailFields;

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("index", index);

            foreach (var field in effectiveFields)
            {
                if (_meshContextFields.TryGetValue(field, out var extractor))
                {
                    jb.Key(field);
                    extractor(mc, jb);
                }
            }

            jb.EndObject();
            return jb.ToString();
        }

        /// <summary>
        /// モデル情報クエリ
        /// </summary>
        public static string QueryModelInfo(ToolContext ctx)
        {
            if (ctx?.Model == null) return "null";

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("meshCount", ctx.Model.Count);
            jb.KeyValue("activeCategory", ctx.Model.ActiveCategory.ToString());
            jb.KeyValue("hasMeshSelection", ctx.Model.HasMeshSelection);
            jb.KeyValue("hasBoneSelection", ctx.Model.HasBoneSelection);
            jb.KeyValue("hasMorphSelection", ctx.Model.HasMorphSelection);

            jb.Key("selectedMeshIndices").BeginArray();
            foreach (int idx in ctx.Model.SelectedMeshIndices)
                jb.Value(idx);
            jb.EndArray();

            if (ctx.Project != null)
            {
                jb.KeyValue("modelCount", ctx.Project.ModelCount);
                jb.KeyValue("currentModelIndex", ctx.Project.CurrentModelIndex);
            }

            jb.EndObject();
            return jb.ToString();
        }

        /// <summary>
        /// 利用可能フィールド一覧をJSON配列で返す
        /// </summary>
        public static string QueryAvailableFields()
        {
            var jb = new JsonBuilder();
            jb.BeginArray();
            foreach (var key in _meshContextFields.Keys)
                jb.Value(key);
            jb.EndArray();
            return jb.ToString();
        }

        // ================================================================
        // デフォルトフィールド
        // ================================================================

        private static readonly string[] DefaultListFields = new[]
        {
            "Name", "IsVisible", "IsLocked", "Type", "VertexCount", "FaceCount", "Depth"
        };

        private static readonly string[] DefaultDetailFields = new[]
        {
            "Name", "IsVisible", "IsLocked", "Type",
            "VertexCount", "FaceCount",
            "MirrorType", "MirrorAxis",
            "Depth", "ParentIndex",
            "IsMorph", "MorphName",
            "SelectMode"
        };

        // ================================================================
        // JSONシリアライズヘルパー
        // ================================================================

        private static void WriteVector3(Vector3 v, JsonBuilder jb)
        {
            jb.BeginArray()
              .Value(v.x).Value(v.y).Value(v.z)
              .EndArray();
        }

        private static void WriteVector3Array(Vector3[] arr, JsonBuilder jb)
        {
            if (arr == null) { jb.RawValue("null"); return; }

            jb.BeginArray();
            for (int i = 0; i < arr.Length; i++)
            {
                jb.BeginArray()
                  .Value(arr[i].x).Value(arr[i].y).Value(arr[i].z)
                  .EndArray();
            }
            jb.EndArray();
        }

        private static void WriteNormals(MeshContext mc, JsonBuilder jb)
        {
            if (mc.MeshObject == null) { jb.RawValue("null"); return; }

            jb.BeginArray();
            for (int i = 0; i < mc.MeshObject.VertexCount; i++)
            {
                var v = mc.MeshObject.Vertices[i];
                var n = (v.Normals != null && v.Normals.Count > 0)
                    ? v.Normals[0] : Vector3.up;
                jb.BeginArray()
                  .Value(n.x).Value(n.y).Value(n.z)
                  .EndArray();
            }
            jb.EndArray();
        }

        private static void WriteIntSet(HashSet<int> set, JsonBuilder jb)
        {
            if (set == null) { jb.RawValue("[]"); return; }

            jb.BeginArray();
            foreach (int v in set)
                jb.Value(v);
            jb.EndArray();
        }

        private static void WriteMatrix4x4(Matrix4x4 m, JsonBuilder jb)
        {
            jb.BeginArray();
            for (int i = 0; i < 16; i++)
                jb.Value(m[i]);
            jb.EndArray();
        }
    }
}

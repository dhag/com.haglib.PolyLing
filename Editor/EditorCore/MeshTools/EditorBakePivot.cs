using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.EditorCore
{
    /// <summary>
    /// MeshFilterの静的メッシュに対してピボットを焼き込む処理のEditorCore実装。
    /// BakePivotTool（EditorWindow）はUIを保持し、本クラスを呼び出す。
    /// </summary>
    public static class EditorBakePivot
    {
        /// <summary>
        /// ターゲットGameObjectのメッシュ頂点をずらしてピボットを焼き込む。
        /// CanBake() が true の場合にのみ呼ぶこと。
        /// </summary>
        public static void BakeOnce(
            GameObject go,
            Vector3 targetPivotWorld,
            string outputFolder,
            bool alsoFixMeshCollider,
            bool recalcBounds,
            bool recalcNormals,
            bool recalcTangents)
        {
            var tr = go.transform;
            var mf = go.GetComponent<MeshFilter>();

            Vector3 offsetWorld = targetPivotWorld - tr.position;
            if (offsetWorld.sqrMagnitude < 1e-12f)
            {
                EditorUtility.DisplayDialog("No Change", "目標ピボットが現在の位置と同じである。", "OK");
                return;
            }

            Vector3 offsetLocal = tr.InverseTransformVector(offsetWorld);

            Undo.RegisterFullObjectHierarchyUndo(go, "Bake Pivot (Mesh)");
            Undo.RecordObject(mf, "Bake Pivot (Mesh)");
            Undo.RecordObject(tr, "Bake Pivot (Mesh)");

            Mesh src = mf.sharedMesh;

            var r = go.GetComponent<Renderer>();
            Bounds originalLocalBounds = r != null ? r.localBounds : new Bounds();

            Mesh dst = UnityEngine.Object.Instantiate(src);
            dst.name = $"{src.name}_PivotBaked";

            var v = dst.vertices;
            for (int i = 0; i < v.Length; i++) v[i] -= offsetLocal;
            dst.vertices = v;

            if (recalcBounds)   dst.RecalculateBounds();
            if (recalcNormals)  dst.RecalculateNormals();
            if (recalcTangents) dst.RecalculateTangents();

            EnsureFolder(outputFolder);

            string safeName = MakeSafeFileName(dst.name);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/{safeName}.asset");
            AssetDatabase.CreateAsset(dst, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            mf.sharedMesh = dst;

            if (r != null) r.localBounds = originalLocalBounds;

            foreach (var col in go.GetComponents<Collider>())
            {
                Undo.RecordObject(col, "Fix Collider");
                if      (col is BoxCollider     bc) bc.center -= offsetLocal;
                else if (col is SphereCollider  sc) sc.center -= offsetLocal;
                else if (col is CapsuleCollider cc) cc.center -= offsetLocal;
                else if (col is WheelCollider   wc) wc.center -= offsetLocal;
            }

            if (alsoFixMeshCollider)
            {
                var mc = go.GetComponent<MeshCollider>();
                if (mc != null)
                {
                    Undo.RecordObject(mc, "Bake Pivot (Mesh)");
                    mc.sharedMesh = null;
                    mc.sharedMesh = dst;
                }
            }

            tr.position += offsetWorld;

            EditorUtility.DisplayDialog("Done",
                $"Pivotを焼き込んだ。\nNew Mesh: {path}\nOffsetWorld: {offsetWorld:F4}", "OK");
        }

        /// <summary>ベイク可能かどうか検証する</summary>
        public static bool CanBake(GameObject go, out string reason)
        {
            reason = "";
            if (go == null) { reason = "Targetが無い。"; return false; }
            if (go.GetComponent<SkinnedMeshRenderer>() != null)
            {
                reason = "SkinnedMeshRendererは対象外である。MeshFilterの静的メッシュを選択すること。";
                return false;
            }
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) { reason = "MeshFilter(sharedMesh) が無い。"; return false; }
            return true;
        }

        /// <summary>Assets以下のフォルダパスを再帰的に作成する</summary>
        public static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            string[] parts = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0] != "Assets")
                throw new Exception("Output Folderは 'Assets/...' で指定する必要がある。");
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        /// <summary>ファイルシステムで使えない文字をアンダースコアに置換する</summary>
        public static string MakeSafeFileName(string s)
        {
            foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s;
        }
    }
}

// PrimitiveSpawner.cs
// ============================================================
// 基本図形をヒエラルキーへ配置する
// ============================================================
//
// 【役割】
//   PrimitiveSpawnDefinition が返す MeshObject を Unity Mesh に変換し、
//   MeshFilter + MeshRenderer を持つ GameObject としてシーンへ置く。
//   親が指定されていればその子として、ローカル TRS を初期化して配置する。
//
// 【スケールの扱い】
//   PrimitiveSpawnOptions.Scale は GameObject の Transform ではなく
//   頂点座標へ焼き込む（Transform.localScale は常に 1）。
//   焼き込みはパッケージ共通の PrimitiveMeshTransform に委ねる。
//
// 【メニュー / パネルから切り離してある理由】
//   [MenuItem] はコンパイル時に固定されるため、ここを純粋な API にしておくと
//   メニュー・パネル・スクリプトのどこからでも同じ配置ができる。
//
// 【配置】 Editor/PrimitiveSpawn/
// ============================================================

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Poly_Ling.AssetIO;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.EditorPrimitive
{
    /// <summary>配置時オプション。既定値は Default。</summary>
    public struct PrimitiveSpawnOptions
    {
        /// <summary>ピボット（回転原点）の位置。既定は底面。</summary>
        public PrimitivePivotMode Pivot;

        /// <summary>
        /// スケール。GameObject の Transform ではなく頂点へ焼き込む。
        /// 負値は鏡映として扱われ、面の巻き順が自動で戻される。
        /// </summary>
        public Vector3 Scale;

        /// <summary>Mesh を .asset として保存するか。false ならシーン内メッシュ。</summary>
        public bool SaveMeshAsset;

        /// <summary>SaveMeshAsset 時の保存先フォルダ（"Assets/..."）。</summary>
        public string MeshAssetFolder;

        /// <summary>
        /// 割り当てるマテリアル。null ならアクティブなレンダーパイプラインの
        /// 既定マテリアル（URP なら Lit、未設定なら Default-Diffuse）。
        /// </summary>
        public Material Material;

        /// <summary>生成後に選択状態にするか。</summary>
        public bool SelectAfterCreate;

        public static PrimitiveSpawnOptions Default => new PrimitiveSpawnOptions
        {
            Pivot             = PrimitivePivotMode.Bottom,
            Scale             = Vector3.one,
            SaveMeshAsset     = false,
            MeshAssetFolder   = "Assets/PolyLing/Meshes",
            Material          = null,
            SelectAfterCreate = true,
        };
    }

    public static class PrimitiveSpawner
    {
        // ============================================================
        // 配置エントリポイント
        // ============================================================

        /// <summary>ID 指定で既定オプション配置。</summary>
        public static GameObject Spawn(string id, GameObject parent)
            => Spawn(id, parent, PrimitiveSpawnOptions.Default);

        /// <summary>ID 指定で配置。</summary>
        public static GameObject Spawn(string id, GameObject parent, PrimitiveSpawnOptions opt)
        {
            if (!PrimitiveSpawnRegistry.TryGet(id, out var def))
            {
                Debug.LogError($"[PolyLing] 未登録の図形 ID です: {id}");
                return null;
            }
            return Spawn(def, parent, opt);
        }

        /// <summary>
        /// 定義から GameObject を作りシーンへ配置する。
        /// parent が null ならカレントステージのルート直下に置く。
        /// </summary>
        public static GameObject Spawn(
            PrimitiveSpawnDefinition def, GameObject parent, PrimitiveSpawnOptions opt)
        {
            if (def == null) return null;

            MeshObject mo = def.Generate(PrimitivePivot.ToVector(opt.Pivot));
            if (mo == null)
            {
                Debug.LogError($"[PolyLing] メッシュ生成に失敗しました: {def.Label}");
                return null;
            }

            BakeScale(mo, opt.Scale);

            // 行列版を単位行列で呼ぶ。
            //   引数なし版は頂点の名寄せキーに法線サブ index を含まないため、
            //   法線が分岐する頂点で三角形の参照が引けず面が欠落する
            //   （HierarchyExportWindow が単位行列を渡しているのと同じ理由）。
            Mesh mesh = mo.ToUnityMesh(Matrix4x4.identity);
            if (mesh == null)
            {
                Debug.LogError($"[PolyLing] Unity メッシュへの変換に失敗しました: {def.Label}");
                return null;
            }
            if (string.IsNullOrEmpty(mesh.name)) mesh.name = def.Label;

            if (opt.SaveMeshAsset)
                mesh = SaveMeshAsset(mesh, opt.MeshAssetFolder);

            // ── GameObject 生成 ──────────────────────────────────────
            //   コンポーネント追加まで済ませてから RegisterCreatedObjectUndo を呼ぶ。
            //   これで Undo が 1 手（生成の取り消し）にまとまる。
            var go = new GameObject(def.Label);

            // プレファブモード等、現在開いているステージへ入れる。
            StageUtility.PlaceGameObjectInCurrentStage(go);

            // 親指定があれば子にし、ローカル TRS を初期化する（親が null なら root 直下）。
            //   スケールは頂点へ焼き込み済みなので localScale は 1 のまま。
            GameObjectUtility.SetParentAndAlign(go, parent);
            GameObjectUtility.EnsureUniqueNameForSibling(go);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh     = mesh;
            mr.sharedMaterial = opt.Material != null ? opt.Material : DefaultMaterial();

            Undo.RegisterCreatedObjectUndo(go, $"Create {def.Label}");

            // Poly_Ling.Selection 名前空間と衝突するため UnityEditor.Selection を完全修飾する。
            if (opt.SelectAfterCreate)
                UnityEditor.Selection.activeGameObject = go;

            return go;
        }

        // ============================================================
        // スケールの焼き込み
        // ============================================================

        /// <summary>
        /// スケールを頂点へ焼き込む。原点基準の拡大縮小なので、
        /// 生成時に決めたピボット（底面／中心）は動かない。
        ///
        /// 法線の逆転置変換と、負スケール時の巻き順反転は
        /// PrimitiveMeshTransform 側で処理される。
        /// </summary>
        private static void BakeScale(MeshObject mo, Vector3 scale)
        {
            if (mo == null) return;
            if (PrimitiveMeshTransform.IsIdentityRotationScale(Vector3.zero, scale)) return;

            PrimitiveMeshTransform.ApplyRotationScale(mo, Vector3.zero, scale);

            // Vertex.Position を直接書き換えたので位置キャッシュを落とす
            // （MeshObject.cs の規約。ToUnityMesh はキャッシュを使わないが、
            //   この MeshObject を後段へ渡す拡張に備えて契約どおり呼んでおく）。
            mo.InvalidatePositionCache();
        }

        // ============================================================
        // マテリアル
        // ============================================================

        /// <summary>
        /// 既定マテリアル。Unity 標準の GameObject/3D Object と同じく、
        /// アクティブなレンダーパイプラインの既定マテリアルを使う
        /// （URP なら Lit、HDRP なら HDRP/Lit）。
        ///
        /// パイプライン未設定（ビルトイン）の場合のみ Default-Diffuse に落とす。
        /// これは HierarchyExportWindow.BuildMaterials と同じ従来動作。
        ///
        /// GraphicsSettings / RenderPipelineAsset は UnityEngine.CoreModule なので、
        /// asmdef に URP/HDRP への参照を足す必要はない。
        ///   ※ Poly_Ling.Rendering 名前空間と衝突しうるため完全修飾する。
        /// </summary>
        private static Material DefaultMaterial()
        {
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (rp != null && rp.defaultMaterial != null)
                return rp.defaultMaterial;

            return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");
        }

        // ============================================================
        // メッシュのアセット化
        // ============================================================

        /// <summary>
        /// mesh を folder 配下の一意な .asset として保存し、保存後の Mesh を返す。
        /// 保存自体はパッケージ共通の MeshAssetUtil（EditorBridge 経由）に委ねる。
        /// </summary>
        private static Mesh SaveMeshAsset(Mesh mesh, string folder)
        {
            if (mesh == null) return null;
            if (string.IsNullOrEmpty(folder)) folder = PrimitiveSpawnOptions.Default.MeshAssetFolder;

            if (!EnsureFolder(folder))
            {
                Debug.LogWarning($"[PolyLing] フォルダを作成できませんでした: {folder}（シーン内メッシュのまま配置します）");
                return mesh;
            }

            string baseName = string.IsNullOrEmpty(mesh.name) ? "Mesh" : SanitizeName(mesh.name);
            string path     = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{baseName}.asset");
            return MeshAssetUtil.SaveDeterministic(mesh, path);
        }

        /// <summary>"Assets/A/B" 形式のフォルダを、無ければ順に作る。</summary>
        private static bool EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return true;
            if (!folder.StartsWith("Assets/") && folder != "Assets") return false;

            string[] parts = folder.Split('/');
            string cur = parts[0];                       // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{cur}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
            return AssetDatabase.IsValidFolder(folder);
        }

        /// <summary>ファイル名に使えない文字を '_' に置換する。</summary>
        private static string SanitizeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}

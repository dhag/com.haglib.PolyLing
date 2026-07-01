using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.MeshBridge
{
    // ================================================================
    // IMeshBridge
    // ----------------------------------------------------------------
    // 目的:
    //   MeshObject（純データ: 頂点/面/UV/法線/ボーンウェイト）と
    //   UnityEngine.Mesh（描画用GPUメッシュ）の相互変換を、ただ1か所に集約する境界。
    //
    //   このインターフェースの実装(MeshBridgeDefault)だけが、
    //   `new Mesh()` / SetVertices / SetTriangles / RecalculateBounds などの
    //   「生の Unity Mesh API」を呼ぶ。アプリ本体・ツール・Undo・インポータは
    //   MeshObject 側の薄い委譲メソッド経由でここに到達する。
    //
    // なぜ境界化するか:
    //   - 生 Unity 呼び出しが各所に散ると、挙動把握・移植・差し替えが困難になる。
    //   - 変換アルゴリズム（頂点展開・三角形分割・サブメッシュ割当）は本質的に
    //     プラットフォーム非依存。実装を1か所に集めることで、他言語(Python/JS)へ
    //     移植する際の参照仕様としても使える。
    //   - 将来 Mesh.SetVertexBufferData 等の低レベルAPIへ差し替える場合も、
    //     実装クラスを差し替えるだけで全呼び出し元に波及させられる。
    //
    // 座標系の前提:
    //   MeshObject.Vertex.Position は Unity左手系(Y-up)で保持される。
    //   本ブリッジは座標系変換を行わない（位置をそのまま、または与えられた
    //   Matrix4x4 で線形変換するのみ）。PMX/MQO 等の系変換は各インポータ/
    //   エクスポータの責務であり、ここには持ち込まない。
    // ================================================================
    public interface IMeshBridge
    {
        // ------------------------------------------------------------
        // MeshObject -> Unity Mesh
        // ------------------------------------------------------------

        /// <summary>
        /// MeshObject から Unity Mesh を生成する（頂点順→UV順で展開）。
        /// </summary>
        /// <param name="source">変換元。null/空でも空Meshを返す。</param>
        /// <param name="materialCount">サブメッシュ数。-1 で MeshObject から自動算出。</param>
        Mesh ToUnityMesh(MeshObject source, int materialCount = -1);

        /// <summary>
        /// MeshObject から Unity Mesh を生成する（頂点位置に transform を適用）。
        /// 法線は逆転置行列で変換する。SkinnedMesh のベイク等で使用。
        /// </summary>
        Mesh ToUnityMesh(MeshObject source, Matrix4x4 transform, int materialCount = -1);

        /// <summary>
        /// MeshObject から Unity Mesh を生成する（頂点共有版）。
        /// (頂点Idx, UVサブIdx) または (頂点Idx, UVサブIdx, 法線サブIdx) が一致する
        /// Unity頂点を共有し、頂点数を抑える。
        /// </summary>
        Mesh ToUnityMeshShared(MeshObject source, int materialCount = -1);

        /// <summary>
        /// MeshObject から Unity Mesh を生成する（頂点共有版・transform適用）。
        /// </summary>
        Mesh ToUnityMeshShared(MeshObject source, Matrix4x4 transform, int materialCount = -1);

        // ------------------------------------------------------------
        // Unity Mesh -> MeshObject
        // ------------------------------------------------------------

        /// <summary>
        /// Unity Mesh から MeshObject を構築する（target は Clear されてから再構築される）。
        /// </summary>
        /// <param name="target">構築先。呼び出し前に保持していた内容は破棄される。</param>
        /// <param name="mesh">読み込み元。null なら target を空にして終了。</param>
        /// <param name="mergeVertices">同一位置の頂点を1つに統合するか。</param>
        /// <param name="includeBoneWeights">BoneWeight を読み込むか（スキンドメッシュ用）。</param>
        void FromUnityMesh(MeshObject target, Mesh mesh, bool mergeVertices, bool includeBoneWeights);

        // ------------------------------------------------------------
        // 既存 Unity Mesh への上書き適用（破棄を伴う一時Mesh経由）
        // ------------------------------------------------------------

        /// <summary>
        /// MeshObject の全データを既存の Unity Mesh へ上書きする（頂点/三角形/UV/法線）。
        /// 一時 Mesh を生成して内容をコピーし、一時 Mesh は破棄する。
        /// 既存の Mesh インスタンスを保持したまま中身だけ差し替えたい場合に使う
        /// （マテリアル参照やレンダラ設定を壊さないため）。
        /// </summary>
        void RebuildMeshInPlace(Mesh target, MeshObject source);

        /// <summary>
        /// MeshObject の頂点位置のみを既存の Unity Mesh へ上書きする（高速パス）。
        /// 三角形/UV は触らず、法線とバウンディングボックスのみ再計算する。
        /// </summary>
        void ApplyVertexPositionsInPlace(Mesh target, MeshObject source);
    }
}

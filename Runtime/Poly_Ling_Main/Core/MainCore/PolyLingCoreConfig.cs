// PolyLingCoreConfig.cs
// PolyLingCore初期化時に外部から注入するコールバック群
// ViewportCore（EditorWindow側）やRemoteClient側のViewportから渡す
// WebSocket経由の場合はスタブ実装を渡す

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Core;

namespace Poly_Ling.Core
{
    public class PolyLingCoreConfig
    {
        // ================================================================
        // 描画・座標変換コールバック（ViewportCoreから注入）
        // ================================================================

        /// <summary>ワールド座標 → スクリーン座標</summary>
        public Func<Vector3, Rect, Vector3, Vector3, Vector2> WorldToScreenPos;

        /// <summary>スクリーンデルタ → ワールドデルタ</summary>
        public Func<Vector2, Vector3, Vector3, float, Rect, Vector3> ScreenDeltaToWorldDelta;

        /// <summary>スクリーン座標から最近傍頂点インデックスを返す</summary>
        public Func<Vector2, MeshObject, Rect, Vector3, Vector3, float, int> FindVertexAtScreenPos;

        /// <summary>スクリーン座標 → Ray</summary>
        public Func<Vector2, Ray> ScreenPosToRay;

        // ================================================================
        // UIコールバック
        // ================================================================

        /// <summary>再描画要求。EditorではRepaint()、RemoteではWebSocket送信などに接続する</summary>
        public Action Repaint;

        /// <summary>メッシュ同期（GPUバッファ更新）</summary>
        public Action SyncMesh;

        /// <summary>メッシュ座標のみ同期</summary>
        public Action SyncMeshPositionsOnly;

        /// <summary>特定MeshContextの座標のみ同期</summary>
        public Action<MeshContext> SyncMeshContextPositionsOnly;

        /// <summary>ボーントランスフォーム同期</summary>
        public Action SyncBoneTransforms;

        // ================================================================
        // スタブ生成ヘルパー
        // ================================================================

        /// <summary>
        /// WebSocket経由など「描画を持たない」接続用のスタブ設定を返す
        /// </summary>
        public static PolyLingCoreConfig CreateStub()
        {
            return new PolyLingCoreConfig
            {
                WorldToScreenPos            = (_, __, ___, ____) => Vector2.zero,
                ScreenDeltaToWorldDelta     = (_, __, ___, ____, _____) => Vector3.zero,
                FindVertexAtScreenPos       = (_, __, ___, ____, _____, ______) => -1,
                ScreenPosToRay              = _ => new Ray(),
                Repaint                     = () => { },
                SyncMesh                    = () => { },
                SyncMeshPositionsOnly       = () => { },
                SyncMeshContextPositionsOnly = _ => { },
                SyncBoneTransforms          = () => { },
            };
        }
    }
}

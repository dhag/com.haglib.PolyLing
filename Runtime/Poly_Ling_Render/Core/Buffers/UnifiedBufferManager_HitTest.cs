// UnifiedBufferManager_HitTest.cs
// 統合バッファ：Level 2 カメラ更新・Level 1 ヒットテスト・インデックス変換。
// Runtime/Poly_Ling_Render/Core/Buffers/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Selection;

namespace Poly_Ling.Core
{
    public partial class UnifiedBufferManager
    {
        // ============================================================
        // Level 2: カメラ更新
        // ============================================================

        /// <summary>
        /// カメラ情報を更新
        /// </summary>
        public void UpdateCamera(
            Matrix4x4 viewMatrix,
            Matrix4x4 projectionMatrix,
            Vector3 cameraPosition,
            Vector3 cameraTarget,
            Rect viewport)
        {
            _cameraInfo[0] = new CameraInfo
            {
                ViewMatrix = viewMatrix,
                ProjectionMatrix = projectionMatrix,
                ViewProjectionMatrix = projectionMatrix * viewMatrix,
                CameraPosition = new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1),
                CameraTarget = new Vector4(cameraTarget.x, cameraTarget.y, cameraTarget.z, 1),
                ViewportSize = new Vector4(viewport.width, viewport.height, 1f / viewport.width, 1f / viewport.height),
                ClipPlanes = new Vector4(0.01f, 1000f, 0, 0)
            };

            Poly_Ling.Diagnostics.PLCamDbg.Wr("_cameraBuffer", _cameraBuffer, 0);
            _cameraBuffer.SetData(_cameraInfo);
        }

        /// <summary>
        /// スクリーン座標を計算（CPU側）
        /// 
        /// 【座標系の設計 - 重要】
        /// 既存システム（MeshGPURenderer.cs + Compute2D_GPU.compute）との互換性のため、
        /// スクリーン座標は viewport.x/y 付きの「グローバル座標」を使用する。
        /// 
        /// 呼び出し側では:
        /// - viewport に adjustedRect（タブオフセット付き）を渡すこと
        ///   adjustedRect.y = rect.y + tabHeight
        ///   tabHeight = GUIUtility.GUIToScreenPoint(Vector2.zero).y - position.y
        /// 
        /// - マウス座標も adjustedRect 座標系に変換してから比較すること
        ///   float rY = mousePos.y / rect.height;
        ///   float adjMouseY = tabHeight + rY * (rect.height - tabHeight);
        /// 
        /// GPU版（UnifiedCompute.compute）も同じ計算式を使用。
        /// </summary>
        public void ComputeScreenPositions(Matrix4x4 viewProjection, Rect viewport)
        {
            // ワールド変換が有効な場合はワールド座標を使用
            var positions = UseWorldPositions && _worldPositions != null ? _worldPositions : _positions;
            
            for (int i = 0; i < _totalVertexCount; i++)
            {
                Vector4 clipPos = viewProjection * new Vector4(
                    positions[i].x,
                    positions[i].y,
                    positions[i].z,
                    1f);

                if (clipPos.w <= 0)
                {
                    _screenPositions[i] = new Vector2(-10000, -10000); // 画面外
                    _screenPositions4[i] = new Vector4(-10000, -10000, 1f, 0f); // w=0で無効
                }
                else
                {
                    Vector2 ndc = new Vector2(clipPos.x / clipPos.w, clipPos.y / clipPos.w);
                    float screenX = viewport.x + (ndc.x * 0.5f + 0.5f) * viewport.width;
                    float screenY = viewport.y + (1f - (ndc.y * 0.5f + 0.5f)) * viewport.height;
                    float depth = clipPos.z / clipPos.w;

                    _screenPositions[i] = new Vector2(screenX, screenY);
                    _screenPositions4[i] = new Vector4(screenX, screenY, depth, 1f); // w=1で有効
                }
            }

            // 【_screenPosBuffer への SetData を撤去した理由】
            //   このバッファを ComputeShader.SetBuffer へ渡している箇所が 0 件で、
            //   シェーダーからも参照されていなかった。GPU が読むのは
            //   _screenPosBuffer4 と per-slot バッファで、そちらは
            //   ComputeScreenPositionsGPU が GPU 側で直接埋める。
            //   _cullingResults も同じ理由で撤去した（公開プロパティの呼出元 0 件）。
        }

        // ============================================================
        // Level 1: ヒットテスト
        // ============================================================

        /// <summary>
        /// ホバー要素を決めるときの「スクリーン距離の許容差」（ピクセル）。
        ///
        /// 【なぜ必要か】GPU 側のヒットテスト (UnifiedCompute.compute の
        /// ComputeVertexHitTest) は、スクリーン距離を「ヒット半径内かどうか」の
        /// 可否判定にしか使わず、半径内の要素には深度だけを書き込む。そのため
        /// 順位付けを深度だけで行うと、カーソルから 1px の頂点よりも 9px 離れた
        /// 手前の頂点が勝つ。密なメッシュではカーソルが 1px 動いただけで
        /// 半径 10px の円に出入りする要素集合が変わり、ホバーが画面上の遠い
        /// 別要素へ飛ぶ。押下時の微小なブレでも起きるため、掴む対象が
        /// 見えていたものと食い違う。
        ///
        /// そこでスクリーン距離をこの幅で量子化した「バンド」を第一キー、
        /// 深度を第二キーとして順位付けする。すなわち、この幅の中の差は
        /// 人間が識別できないので従来どおり手前を優先し、この幅を超えて
        /// 明らかに近い要素があればそちらを優先する。
        ///
        /// ヒット半径以上の値にすると全要素が同一バンドになり、
        /// 深度のみで選ぶ従来の挙動へ完全に戻る。
        /// </summary>
        public float HoverDistanceTolerance { get; set; } = 3f;

        /// <summary>
        /// ヒットテスト入力を設定
        /// </summary>
        public void SetHitTestInput(Vector2 mousePosition, float hitRadius, Rect previewRect, uint hitMode = 0xF)
        {
            _hitTestInput[0] = new HitTestInput
            {
                MousePosition = mousePosition,
                HitRadius = hitRadius,
                HitMode = hitMode,
                PreviewRect = new Vector4(previewRect.x, previewRect.y, previewRect.width, previewRect.height)
            };

            Poly_Ling.Diagnostics.PLCamDbg.Wr("_hitTestInputBuffer", _hitTestInputBuffer, 0);
            _hitTestInputBuffer.SetData(_hitTestInput);
        }

        /// <summary>
        /// 頂点ヒットテスト（CPU実行）
        /// 一定距離内の頂点群のうち、Zが最も小さい（手前の）ものを返す
        ///
        /// 【フィルタ】選択中メッシュ（MeshSelected）に属する頂点のみ候補とする。
        /// </summary>
        public int FindNearestVertex(Vector2 mousePosition, float hitRadius, bool backfaceCullingEnabled = true)
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalVertexCount; i++)
            {
                uint flags = _vertexFlags[i];

                // 選択メッシュに属さない頂点はスキップ
                if ((flags & (uint)SelectionFlags.MeshSelected) == 0)
                    continue;

                // 非表示チェック
                if ((flags & (uint)SelectionFlags.Hidden) != 0)
                    continue;
                
                // カリングチェック（バックフェースカリング有効時のみ）
                if (backfaceCullingEnabled && (flags & (uint)SelectionFlags.Culled) != 0)
                    continue;

                float dist = Vector2.Distance(mousePosition, _screenPositions[i]);
                if (dist < hitRadius)
                {
                    // 距離内の頂点の中で最も手前（Z小）を選択
                    float depth = GetVertexDepth((uint)i);
                    if (depth < nearestDepth)
                    {
                        nearestDepth = depth;
                        nearestIdx = i;
                    }
                }
            }

            return nearestIdx;
        }

        /// <summary>
        /// ラインヒットテスト（CPU実行）
        /// 一定距離内の線分群のうち、Zが最も小さい（手前の）ものを返す
        ///
        /// 【フィルタ】選択中メッシュ（MeshSelected）に属する線分のみ候補とする。
        /// </summary>
        public int FindNearestLine(Vector2 mousePosition, float hitRadius, bool backfaceCullingEnabled = true)
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalLineCount; i++)
            {
                uint flags = _lineFlags[i];

                // 選択メッシュに属さない線分はスキップ
                if ((flags & (uint)SelectionFlags.MeshSelected) == 0)
                    continue;

                // 非表示チェック
                if ((flags & (uint)SelectionFlags.Hidden) != 0)
                    continue;
                
                // カリングチェック（バックフェースカリング有効時のみ）
                if (backfaceCullingEnabled && (flags & (uint)SelectionFlags.Culled) != 0)
                    continue;

                var line = _lines[i];
                Vector2 p1 = _screenPositions[line.V1];
                Vector2 p2 = _screenPositions[line.V2];

                float dist = DistanceToLineSegment(mousePosition, p1, p2);
                if (dist < hitRadius)
                {
                    // 距離内の線分の中で最も手前（Z小）を選択
                    // 線分の深度は両端の平均
                    float depth1 = GetVertexDepth(line.V1);
                    float depth2 = GetVertexDepth(line.V2);
                    float avgDepth = (depth1 + depth2) * 0.5f;
                    
                    if (avgDepth < nearestDepth)
                    {
                        nearestDepth = avgDepth;
                        nearestIdx = i;
                    }
                }
            }

            return nearestIdx;
        }

        /// <summary>
        /// 面ヒットテスト（CPU実行、レイキャスト法）
        ///
        /// 【フィルタ】選択中メッシュ（MeshSelected）に属する面のみ候補とする。
        /// </summary>
        public int FindNearestFace(Vector2 mousePosition, bool backfaceCullingEnabled = true)
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int faceIdx = 0; faceIdx < _totalFaceCount; faceIdx++)
            {
                uint flags = _faceFlags[faceIdx];

                // 選択メッシュに属さない面はスキップ
                if ((flags & (uint)SelectionFlags.MeshSelected) == 0)
                    continue;

                // 非表示チェック
                if ((flags & (uint)SelectionFlags.Hidden) != 0)
                    continue;
                
                // カリングチェック（バックフェースカリング有効時のみ）
                if (backfaceCullingEnabled && (flags & (uint)SelectionFlags.Culled) != 0)
                    continue;

                var face = _faces[faceIdx];
                int vertexCount = (int)face.VertexCount;

                if (vertexCount < 3 || vertexCount > 16)
                    continue;

                // 多角形の頂点をスクリーン座標で取得
                Vector2[] polygon = new Vector2[vertexCount];
                float totalDepth = 0;
                bool allValid = true;

                // 三角形ファンからN-gonの頂点を復元
                int triCount = vertexCount - 2;

                // 最初の頂点
                uint baseIdx = _indices[face.IndexStart];
                if (baseIdx >= _totalVertexCount) { allValid = false; }
                else
                {
                    polygon[0] = _screenPositions[baseIdx];
                    totalDepth += GetVertexDepth(baseIdx);
                }

                // 各三角形の2番目の頂点
                for (int i = 0; i < triCount && allValid; i++)
                {
                    uint idx = _indices[face.IndexStart + i * 3 + 1];
                    if (idx >= _totalVertexCount) { allValid = false; break; }
                    polygon[i + 1] = _screenPositions[idx];
                    totalDepth += GetVertexDepth(idx);
                }

                // 最後の頂点
                if (allValid && triCount > 0)
                {
                    uint lastIdx = _indices[face.IndexStart + (triCount - 1) * 3 + 2];
                    if (lastIdx >= _totalVertexCount) { allValid = false; }
                    else
                    {
                        polygon[vertexCount - 1] = _screenPositions[lastIdx];
                        totalDepth += GetVertexDepth(lastIdx);
                    }
                }

                if (!allValid)
                    continue;

                // レイキャスト法で内外判定
                if (IsPointInPolygon(mousePosition, polygon, vertexCount))
                {
                    float avgDepth = totalDepth / vertexCount;
                    if (avgDepth < nearestDepth)
                    {
                        nearestDepth = avgDepth;
                        nearestIdx = faceIdx;
                    }
                }
            }

            return nearestIdx;
        }

        /// <summary>
        /// 頂点の深度を取得
        /// _screenPositions4.z にクリップ空間の深度が保存されている
        /// </summary>
        private float GetVertexDepth(uint vertexIndex)
        {
            if (vertexIndex < _totalVertexCount && _screenPositions4 != null)
            {
                // _screenPositions4.z = clipPos.z / clipPos.w（正規化デバイス座標の深度）
                // w=0なら無効な頂点なので最大値を返す
                if (_screenPositions4[vertexIndex].w > 0.5f)
                {
                    return _screenPositions4[vertexIndex].z;
                }
            }
            return float.MaxValue; // 無効な頂点は最も奥
        }

        /// <summary>
        /// 点が多角形内にあるか判定（レイキャスト法）
        /// </summary>
        private bool IsPointInPolygon(Vector2 point, Vector2[] polygon, int vertexCount)
        {
            int crossings = 0;

            for (int i = 0; i < vertexCount; i++)
            {
                int next = (i + 1) % vertexCount;
                Vector2 v0 = polygon[i];
                Vector2 v1 = polygon[next];

                // 右方向へのレイが辺と交差するか
                if ((v0.y <= point.y && v1.y > point.y) || (v1.y <= point.y && v0.y > point.y))
                {
                    float vt = (point.y - v0.y) / (v1.y - v0.y);
                    float xIntersect = v0.x + vt * (v1.x - v0.x);
                    if (point.x < xIntersect)
                    {
                        crossings++;
                    }
                }
            }

            // 奇数回交差 = 内部
            return (crossings & 1) != 0;
        }

        /// <summary>
        /// 点と線分の距離
        /// </summary>
        private float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float lenSq = line.sqrMagnitude;

            if (lenSq < 0.000001f)
                return Vector2.Distance(point, lineStart);

            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / lenSq);
            Vector2 projection = lineStart + t * line;
            return Vector2.Distance(point, projection);
        }

        /// <summary>
        /// 頂点ホバーフラグを設定
        /// </summary>
        public void SetHoverVertex(int globalVertexIndex)
        {
            // 既存の頂点ホバーをクリア
            _flagManager.ClearAllHoverFlags(_vertexFlags);

            // 新しいホバーを設定
            if (globalVertexIndex >= 0 && globalVertexIndex < _totalVertexCount)
            {
                _flagManager.SetHoverFlag(_vertexFlags, globalVertexIndex, true);
            }

            // アップロード
            if (_totalVertexCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_vertexFlagsBuffer", _vertexFlagsBuffer, 0);
                _vertexFlagsBuffer.SetData(_vertexFlags, 0, 0, _totalVertexCount);
            }
        }

        /// <summary>
        /// 線分ホバーフラグを設定
        /// 同じV1-V2を持つ全エントリにホバーフラグを設定（共有エッジ対応）
        /// </summary>
        public void SetHoverLine(int globalLineIndex)
        {
            // 既存の線分ホバーをクリア
            _flagManager.ClearAllHoverFlags(_lineFlags);

            // 新しいホバーを設定
            if (globalLineIndex >= 0 && globalLineIndex < _totalLineCount)
            {
                // 指定された線分のV1-V2を取得
                var targetLine = _lines[globalLineIndex];
                uint v1 = targetLine.V1;
                uint v2 = targetLine.V2;

                // 同じV1-V2を持つ全エントリにホバーフラグを設定
                for (int i = 0; i < _totalLineCount; i++)
                {
                    var line = _lines[i];
                    if ((line.V1 == v1 && line.V2 == v2) || (line.V1 == v2 && line.V2 == v1))
                    {
                        _flagManager.SetHoverFlag(_lineFlags, i, true);
                    }
                }
            }

            // アップロード
            if (_totalLineCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_lineFlagsBuffer", _lineFlagsBuffer, 0);
                _lineFlagsBuffer.SetData(_lineFlags, 0, 0, _totalLineCount);
            }
        }

        /// <summary>
        /// 面ホバーフラグを設定
        /// </summary>
        public void SetHoverFace(int globalFaceIndex)
        {
            // 既存の面ホバーをクリア
            _flagManager.ClearAllHoverFlags(_faceFlags);

            // 新しいホバーを設定
            if (globalFaceIndex >= 0 && globalFaceIndex < _totalFaceCount)
            {
                _flagManager.SetHoverFlag(_faceFlags, globalFaceIndex, true);
            }

            // アップロード
            if (_totalFaceCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_faceFlagsBuffer", _faceFlagsBuffer, 0);
                _faceFlagsBuffer.SetData(_faceFlags, 0, 0, _totalFaceCount);
            }
        }

        /// <summary>
        /// 全てのホバーフラグをクリア
        /// </summary>
        public void ClearHover()
        {
            _flagManager.ClearAllHoverFlags(_vertexFlags);
            _flagManager.ClearAllHoverFlags(_lineFlags);
            _flagManager.ClearAllHoverFlags(_faceFlags);

            if (_totalVertexCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_vertexFlagsBuffer", _vertexFlagsBuffer, 0);
                _vertexFlagsBuffer.SetData(_vertexFlags, 0, 0, _totalVertexCount);
            }
            if (_totalLineCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_lineFlagsBuffer", _lineFlagsBuffer, 0);
                _lineFlagsBuffer.SetData(_lineFlags, 0, 0, _totalLineCount);
            }
            if (_totalFaceCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_faceFlagsBuffer", _faceFlagsBuffer, 0);
                _faceFlagsBuffer.SetData(_faceFlags, 0, 0, _totalFaceCount);
            }
        }

        // ============================================================
        // インデックス変換
        // ============================================================

        /// <summary>
        /// グローバル頂点インデックスからメッシュインデックスとローカルインデックスを取得
        /// </summary>
        public bool GlobalToLocalVertexIndex(int globalIndex, out int meshIndex, out int localIndex)
        {
            meshIndex = -1;
            localIndex = -1;

            if (globalIndex < 0 || globalIndex >= _totalVertexCount)
                return false;

            for (int i = 0; i < _meshCount; i++)
            {
                var info = _meshInfos[i];
                if (globalIndex >= info.VertexStart && globalIndex < info.VertexStart + info.VertexCount)
                {
                    meshIndex = i;
                    localIndex = globalIndex - (int)info.VertexStart;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// ローカル頂点インデックスからグローバルインデックスを取得
        /// </summary>
        public int LocalToGlobalVertexIndex(int meshIndex, int localIndex)
        {
            if (meshIndex < 0 || meshIndex >= _meshCount)
                return -1;

            var info = _meshInfos[meshIndex];
            if (localIndex < 0 || localIndex >= info.VertexCount)
                return -1;

            return (int)info.VertexStart + localIndex;
        }

        /// <summary>
        /// メッシュインデックス(unified)とローカル面インデックスからグローバル面インデックスを取得
        /// </summary>
        public int LocalToGlobalFaceIndex(int meshIndex, int localFaceIndex)
        {
            if (meshIndex < 0 || meshIndex >= _meshCount)
                return -1;

            var info = _meshInfos[meshIndex];
            if (localFaceIndex < 0 || localFaceIndex >= info.FaceCount)
                return -1;

            return (int)info.FaceStart + localFaceIndex;
        }

        /// <summary>
        /// グローバルラインインデックスからメッシュインデックスとローカルインデックスを取得
        /// </summary>
        public bool GlobalToLocalLineIndex(int globalIndex, out int meshIndex, out int localIndex)
        {
            meshIndex = -1;
            localIndex = -1;

            if (globalIndex < 0 || globalIndex >= _totalLineCount)
                return false;

            for (int i = 0; i < _meshCount; i++)
            {
                var info = _meshInfos[i];
                if (globalIndex >= info.LineStart && globalIndex < info.LineStart + info.LineCount)
                {
                    meshIndex = i;
                    localIndex = globalIndex - (int)info.LineStart;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// グローバル面インデックスからメッシュインデックスとローカルインデックスを取得
        /// </summary>
        public bool GlobalToLocalFaceIndex(int globalIndex, out int meshIndex, out int localIndex)
        {
            meshIndex = -1;
            localIndex = -1;

            if (globalIndex < 0 || globalIndex >= _totalFaceCount)
                return false;

            for (int i = 0; i < _meshCount; i++)
            {
                var info = _meshInfos[i];
                if (globalIndex >= info.FaceStart && globalIndex < info.FaceStart + info.FaceCount)
                {
                    meshIndex = i;
                    localIndex = globalIndex - (int)info.FaceStart;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// ラインの頂点インデックスを取得（グローバルインデックス）
        /// </summary>
        public bool GetLineVertices(int globalLineIndex, out uint v1, out uint v2)
        {
            v1 = 0;
            v2 = 0;

            if (globalLineIndex < 0 || globalLineIndex >= _totalLineCount)
                return false;

            var line = _lines[globalLineIndex];
            v1 = line.V1;
            v2 = line.V2;
            return true;
        }

        /// <summary>
        /// ラインの頂点インデックスを取得（ローカルインデックス）
        /// </summary>
        public bool GetLineVerticesLocal(int globalLineIndex, out int meshIndex, out int localV1, out int localV2)
        {
            meshIndex = -1;
            localV1 = -1;
            localV2 = -1;

            if (!GetLineVertices(globalLineIndex, out uint gV1, out uint gV2))
                return false;

            // ラインの所属メッシュを取得
            if (!GlobalToLocalLineIndex(globalLineIndex, out meshIndex, out int _))
                return false;

            // 頂点のローカルインデックスを計算
            var info = _meshInfos[meshIndex];
            localV1 = (int)(gV1 - info.VertexStart);
            localV2 = (int)(gV2 - info.VertexStart);
            
            return true;
        }

        /// <summary>
        /// 線分が補助線かどうかを取得
        /// </summary>
        public bool GetLineType(int globalLineIndex, out bool isAuxLine)
        {
            isAuxLine = false;
            
            if (globalLineIndex < 0 || globalLineIndex >= _totalLineCount)
                return false;

            uint flags = _lineFlags[globalLineIndex];
            isAuxLine = (flags & (uint)SelectionFlags.IsAuxLine) != 0;
            return true;
        }

        /// <summary>
        /// 線分の所属面インデックス（ローカル）を取得
        /// </summary>
        public bool GetLineFaceIndex(int globalLineIndex, out int localFaceIndex)
        {
            localFaceIndex = -1;
            
            if (globalLineIndex < 0 || globalLineIndex >= _totalLineCount)
                return false;

            var line = _lines[globalLineIndex];
            
            // グローバル面インデックスをローカルに変換
            if (!GlobalToLocalFaceIndex((int)line.FaceIndex, out int meshIdx, out int localIdx))
                return false;
                
            localFaceIndex = localIdx;
            return true;
        }
    }
}

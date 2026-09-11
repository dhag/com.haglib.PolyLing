// Assets/Editor/Poly_Ling/Core/Buffers/UnifiedBufferManager_Update.cs
// 統合バッファ管理クラス - 更新処理
// 選択、カメラ、ヒットテストの更新
//
// 【分割先】このファイルから次へ分けてある。
//   UnifiedBufferManager_Gpu.cs        統合バッファ：GPU 計算。
//   UnifiedBufferManager_HitTest.cs    統合バッファ：Level 2 カメラ更新・Level 1 ヒットテスト・インデックス変換。
//   UnifiedBufferManager_Selection.cs  統合バッファ：Level 3 選択フラグ更新。

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
        // Level 4: Transform Matrix 更新
        // ============================================================

        /// <summary>
        /// 変換行列をGPUバッファにアップロード
        /// ModelContext.ComputeWorldMatrices() 呼び出し後に使用
        /// ボーンを含む全MeshContextの行列をアップロード
        /// </summary>
        public void UpdateTransformMatrices(List<MeshContext> meshContexts, bool useWorldTransform)
        {
            if (meshContexts == null || _transformMatrixBuffer == null)
                return;

            int contextCount = meshContexts.Count;
            
            // 配列サイズを確保（全MeshContext分）
            if (_transformMatrices == null || _transformMatrices.Length < contextCount)
            {
                _transformMatrices = new Matrix4x4[Mathf.Max(contextCount, 256)];
            }

            // バッファサイズが足りない場合は再作成
            if (_transformMatrixBuffer.count < contextCount)
            {
                if (_transformMatrixBuffer != null) Poly_Ling.Diagnostics.PLResStat.LiveCB--;
                _transformMatrixBuffer?.Release();
                _transformMatrixBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(Mathf.Max(contextCount, 256), sizeof(float) * 16));
            }

            // 全MeshContext（ボーン含む）の変換行列を設定
            for (int i = 0; i < contextCount; i++)
            {
                var ctx = meshContexts[i];
                if (ctx == null)
                {
                    _transformMatrices[i] = Matrix4x4.identity;
                    continue;
                }

                if (useWorldTransform)
                {
                    // MeshFilter（Type=Mesh かつ BoneWeight なし）: WorldMatrix を直接使用
                    //   → ローカル座標に WorldMatrix を適用してワールド座標を得る
                    // スキンドメッシュ（Type=Mesh かつ BoneWeight あり）: SkinningMatrix を使用
                    // ボーン（Type=Bone）: SkinningMatrix を使用
                    //   → スキンドメッシュ頂点の boneIndex がボーンを指すため、
                    //     SkinningMatrix = BoneWorldMatrix × BoneBindPose が必要
                    // ミラー側（MirrorSide / BakedMirror）も、スキンを持たなければ
                    // MeshFilter と同じ実体である。Type だけで弾くと SkinningMatrix 経路に
                    // 落ち、BindPose が WorldMatrix⁻¹ に更新された瞬間（Tポーズ変換など）
                    // SkinningMatrix = W·W⁻¹ = 単位 となって変換が丸ごと消える。
                    // ここは「行列表の中身」を決める場所であって、頂点の座標系の判定では
                    // ない。対象の型を明示すること。型で絞らずに IsSkinned だけで分けると、
                    // ボーンの欄が WorldMatrix になり、その欄を boneIndex で引く
                    // スキンド頂点が全部ボーンのワールド位置ぶん飛ぶ。
                    bool usesWorldMatrixDirect =
                        (ctx.Type == MeshType.Mesh ||
                         ctx.Type == MeshType.MirrorSide ||
                         ctx.Type == MeshType.BakedMirror) &&
                        !ctx.IsSkinned;
                    _transformMatrices[i] = usesWorldMatrixDirect ? ctx.WorldMatrix : ctx.SkinningMatrix;
                }
                else
                {
                    _transformMatrices[i] = ctx.LocalMatrix;
                }
            }

            // GPUにアップロード
            if (contextCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_transformMatrixBuffer", _transformMatrixBuffer, 0);
                _transformMatrixBuffer.SetData(_transformMatrices, 0, 0, contextCount);
            }
        }

        /// <summary>
        /// 単一メッシュの変換行列を更新
        /// </summary>
        public void UpdateTransformMatrix(int meshIndex, Matrix4x4 matrix)
        {
            if (meshIndex < 0 || meshIndex >= _meshCount)
                return;

            if (_transformMatrices == null || _transformMatrices.Length <= meshIndex)
                return;

            _transformMatrices[meshIndex] = matrix;
            
            // GPUにアップロード（部分更新）
            _transformMatrixBuffer?.SetData(_transformMatrices, meshIndex, meshIndex, 1);
        }

        // ============================================================
        // TransformVertices カーネル実行
        // ============================================================

        private int _kernelTransformVertices = -1;
        private int _kernelExpandVertices = -1;

        /// <summary>
        /// TransformVerticesカーネルを実行
        /// ローカル座標をワールド座標に変換
        /// </summary>
        /// <param name="useWorldTransform">true: ワールド変換適用, false: ローカル座標コピー</param>
        /// <param name="transformNormals">true: 法線も変換</param>
        /// <param name="readbackToCPU">true: 結果をCPU側に読み戻す（非推奨、後方互換用）</param>
        public void DispatchTransformVertices(bool useWorldTransform, bool transformNormals = false, bool readbackToCPU = true)
        {
            if (!_gpuComputeAvailable || _computeShader == null)
                return;

            if (_totalVertexCount == 0)
                return;

            // UseWorldPositionsフラグを設定
            UseWorldPositions = useWorldTransform;

            // カーネルを取得（初回のみ）
            if (_kernelTransformVertices < 0)
            {
                _kernelTransformVertices = _computeShader.FindKernel("TransformVertices");
                if (_kernelTransformVertices < 0)
                {
                    Debug.LogWarning("[UnifiedBufferManager] TransformVertices kernel not found");
                    return;
                }
            }

            // バッファをバインド
            _computeShader.SetBuffer(_kernelTransformVertices, "_PositionBuffer", _positionBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_WorldPositionBuffer", _worldPositionBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_TransformMatrixBuffer", _transformMatrixBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_BoneWeightsBuffer", _boneWeightsBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_BoneIndicesBuffer", _boneIndicesBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_NormalBuffer", _normalBuffer);
            // WorldNormalBufferは未実装のため、ダミーとしてNormalBufferをバインド
            _computeShader.SetBuffer(_kernelTransformVertices, "_WorldNormalBuffer", _normalBuffer);
            
            // ミラーバッファをバインド
            _computeShader.SetBuffer(_kernelTransformVertices, "_MirrorPositionBuffer", _mirrorPositionBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_SkinnedMirrorPositionBuffer", _skinnedMirrorPositionBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_MirrorBoneWeightsBuffer", _mirrorBoneWeightsBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_MirrorBoneIndicesBuffer", _mirrorBoneIndicesBuffer);

            // パラメータを設定
            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_UseWorldTransform", useWorldTransform ? 1 : 0);
            _computeShader.SetInt("_TransformNormals", transformNormals ? 1 : 0);
            _computeShader.SetInt("_ComputeMirror", _mirrorEnabled ? 1 : 0);

            // ディスパッチ
            int threadGroups = Mathf.CeilToInt(_totalVertexCount / 256.0f);
            Poly_Ling.Diagnostics.PLCamDbg.Dsp("TransformVertices", 0, null, threadGroups);
            _computeShader.Dispatch(_kernelTransformVertices, threadGroups, 1, 1);

            // CPU側に読み戻し（描画用）- 非推奨、後方互換用
            if (readbackToCPU && useWorldTransform)
            {
                if (_worldPositions == null || _worldPositions.Length < _totalVertexCount)
                    _worldPositions = new Vector3[_totalVertexCount];
                
                if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G12 before n=" + _totalVertexCount + " buf=" + _worldPositionBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _worldPositionBuffer.count + " arr=" + _worldPositions.Length);
                // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
                if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData)
                    _worldPositionBuffer.GetData(_worldPositions, 0, 0, _totalVertexCount);
                // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
                //   GetData = フラッシュ + GPU 完了待ち
                //   GL.Flush = フラッシュのみ（待たない）
                //   どちらが引き金かを分離するための診断。
                else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                    UnityEngine.GL.Flush();
                else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                    Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
                if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G12 after");
            }
        }

        /// <summary>
        /// ExpandVerticesカーネルを実行
        /// ワールド変換済み頂点をUV展開済み配列に展開
        /// </summary>
        /// <param name="transformNormals">法線も展開するか</param>
        public void DispatchExpandVertices(bool transformNormals = false)
        {
            if (!_gpuComputeAvailable || _computeShader == null)
                return;

            if (_totalExpandedVertexCount == 0)
                return;

            // 必要なバッファがすべて存在するか確認
            if (_expandedToOriginalBuffer == null || 
                _expandedPositionBuffer == null || 
                _expandedNormalBuffer == null ||
                _worldPositionBuffer == null ||
                _normalBuffer == null)
            {
                Debug.LogWarning("[UnifiedBufferManager] ExpandVertices: Required buffers not initialized");
                return;
            }

            // カーネルを取得（初回のみ）
            if (_kernelExpandVertices < 0)
            {
                _kernelExpandVertices = _computeShader.FindKernel("ExpandVertices");
                if (_kernelExpandVertices < 0)
                {
                    Debug.LogWarning("[UnifiedBufferManager] ExpandVertices kernel not found");
                    return;
                }
            }

            // バッファをバインド
            _computeShader.SetBuffer(_kernelExpandVertices, "_ExpandedToOriginalBuffer", _expandedToOriginalBuffer);
            _computeShader.SetBuffer(_kernelExpandVertices, "_WorldPositionBuffer", _worldPositionBuffer);
            _computeShader.SetBuffer(_kernelExpandVertices, "_ExpandedPositionBuffer", _expandedPositionBuffer);
            _computeShader.SetBuffer(_kernelExpandVertices, "_NormalBuffer", _normalBuffer);
            _computeShader.SetBuffer(_kernelExpandVertices, "_WorldNormalBuffer", _normalBuffer);  // TODO: 変換済み法線
            _computeShader.SetBuffer(_kernelExpandVertices, "_ExpandedNormalBuffer", _expandedNormalBuffer);

            // パラメータを設定
            _computeShader.SetInt("_ExpandedVertexCount", _totalExpandedVertexCount);
            _computeShader.SetInt("_TransformNormals", transformNormals ? 1 : 0);

            // ディスパッチ
            int threadGroups = Mathf.CeilToInt(_totalExpandedVertexCount / 256.0f);
            Poly_Ling.Diagnostics.PLCamDbg.Dsp("ExpandVertices", 0, null, threadGroups);
            _computeShader.Dispatch(_kernelExpandVertices, threadGroups, 1, 1);
        }

        // ================================================================
        // 【禁止事項】GPU 由来の座標を扱うときの拗らせ
        // ================================================================
        // 以下は実際に発生させた失敗である。繰り返さないこと。
        //
        // 1. 調べずに CPU 側で独自計算しない。
        //    GPU が _worldPositionBuffer にワールド座標を出しているのに、
        //    同じ規則を CPU で書き直すと、規則が食い違ったときに表示だけがずれる。
        //    まず GPU の値を使う経路を探すこと。
        //
        // 2.「今は呼ばれていないからできない」と決めつけない。
        //    呼び出し箇所が無いことは、呼び出しを足せない理由にならない。
        //    足せるかどうかを調べてから結論を出すこと。
        //
        // 3. カメラもモデルも動いていないのに読み戻しを毎フレーム呼ばない。
        //    WritebackTransformedVertices / GetWorldPositions は同期 GetData を伴う。
        //    ワールド座標が変わる契機（頂点移動・ボーン移動・再構築）でのみ更新し、
        //    ホバーのようにトポロジ・視点・頂点位置のいずれも変わらない操作では呼ばない。
        // ================================================================

        /// <summary>
        /// ワールド座標バッファの内容を取得（デバッグ用）
        /// </summary>
        public Vector3[] GetWorldPositions()
        {
            if (_worldPositionBuffer == null || _totalVertexCount == 0)
                return null;

            if (_worldPositions == null || _worldPositions.Length < _totalVertexCount)
                _worldPositions = new Vector3[_totalVertexCount];

            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G13 before n=" + _totalVertexCount + " buf=" + _worldPositionBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _worldPositionBuffer.count + " arr=" + _worldPositions.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData)
                _worldPositionBuffer.GetData(_worldPositions, 0, 0, _totalVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G13 after");
            return _worldPositions;
        }

        // 展開済み頂点のCPU配列（ReadBack用）
        private Vector3[] _expandedPositions;

        /// <summary>
        /// 展開済み頂点座標バッファの内容を取得
        /// </summary>
        public Vector3[] GetExpandedPositions()
        {
            if (_expandedPositionBuffer == null || _totalExpandedVertexCount == 0)
                return null;

            if (_expandedPositions == null || _expandedPositions.Length < _totalExpandedVertexCount)
                _expandedPositions = new Vector3[_totalExpandedVertexCount];

            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G14 before n=" + _totalExpandedVertexCount + " buf=" + _expandedPositionBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _expandedPositionBuffer.count + " arr=" + _expandedPositions.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData)
                _expandedPositionBuffer.GetData(_expandedPositions, 0, 0, _totalExpandedVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G14 after");
            return _expandedPositions;
        }
    }
}

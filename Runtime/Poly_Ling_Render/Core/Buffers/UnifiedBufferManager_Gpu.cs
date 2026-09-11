// UnifiedBufferManager_Gpu.cs
// 統合バッファ：GPU 計算。
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
        // GPU計算
        // ============================================================

        private int ThreadGroups(int count) => Mathf.CeilToInt(count / 64f);

        /// <summary>
        /// GPU でスクリーン座標を計算し、slot 専用バッファへ書き込む。
        /// </summary>
        /// <param name="readback">
        /// GPU → CPU の同期読み戻し（<c>ComputeBuffer.GetData</c>）を行うかどうか。
        ///
        /// 【必ず明示すること。既定値を付けない】
        ///   GetData はコマンドキューのフラッシュと GPU 完了待ちを伴う。呼び出しごとに
        ///   全頂点ぶん（float4 × 頂点数）を転送し、そのあと CPU で全頂点ループを
        ///   回して _screenPositions へ展開する。結果を CPU が読まない経路で
        ///   これを行うと、転送もループも丸ごと無駄になる。
        ///
        /// 【true にしてよい経路（2 つだけ）】
        ///   1. ホバーのヒットテスト（UnifiedMeshSystem.ProcessMouseUpdate）
        ///      FindNearestVertexFromGPU / FindNearestLineFromGPU が
        ///      _screenPositions を読む。
        ///   2. PresentAll 末尾のアクティブ slot 最終確定
        ///      矩形選択・投げ縄選択が GetScreenPositions() を読む。
        ///
        /// 【false にする経路】
        ///   表示用カリング（DispatchCullingForDisplay の per-slot 呼び出し、
        ///   MeshSceneRenderer の cullSubmit）。後続は GPU 内で完結する
        ///   FaceVisibility / LineVisibility / ApplyMirrorCull だけで、
        ///   CPU 側は結果を一切参照しない。
        ///
        /// 【_screenPositions は 1 本しかないことに注意】
        ///   slot ごとの配列ではないので、複数 slot が readback: true で呼ぶと
        ///   最後の 1 回しか残らない。読み戻す slot は常に 1 つに保つこと。
        /// </param>
        public void ComputeScreenPositionsGPU(
            Matrix4x4 viewProjection, Rect viewport, int slot, string dbgSrc, bool readback)
        {
            Poly_Ling.Diagnostics.PLCamDbg.Cap("v=" + _totalVertexCount + "/" + _vertexCapacity
                + " l=" + _totalLineCount + "/" + _lineCapacity
                + " f=" + _totalFaceCount + "/" + _faceCapacity
                + " mesh=" + (_meshInfos == null ? -1 : _meshInfos.Length)
                + " sp4=" + (_screenPositions4 == null ? -1 : _screenPositions4.Length));
            if (!_gpuComputeAvailable || _computeShader == null || _totalVertexCount <= 0)
            {
                // CPU フォールバックは readback の指定に関係なく _screenPositions を埋める。
                // CPU で計算する以上、結果は最初から CPU 側にあるため。
                ComputeScreenPositions(viewProjection, viewport);
                return;
            }

            // slot バッファは Initialize で CullingSlotCount 本を必ず確保する。
            // null になるのは範囲外の slot 番号を渡されたときだけで、それは呼び出し側の誤り。
            // 旧コードは _screenPosBuffer4 へフォールバックしていたが、そのバッファは
            // 読み手が 0 件で、書いても誰も使わなかった（撤去済み）。
            var screenBuf = GetSlotScreenPosBuffer(slot);
            if (screenBuf == null)
            {
                Debug.LogError($"[ComputeScreenPositionsGPU] slot={slot} のスクリーン座標バッファが無い。src={dbgSrc}");
                return;
            }

            // パラメータ設定
            _computeShader.SetMatrix("_ViewProjectionMatrix", viewProjection);
            _computeShader.SetVector("_ViewportParams", new Vector4(viewport.x, viewport.y, viewport.width, viewport.height));
            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_LineCount", _totalLineCount);
            _computeShader.SetInt("_FaceCount", _totalFaceCount);

            var posBuffer = UseWorldPositions ? _worldPositionBuffer : _positionBuffer;
            _computeShader.SetBuffer(_kernelScreenPos, "_PositionBuffer",            posBuffer);
            _computeShader.SetBuffer(_kernelScreenPos, "_ScreenPositionBuffer",      screenBuf);
            _computeShader.SetBuffer(_kernelScreenPos, "_VertexFlagsBuffer",         _vertexFlagsBuffer);

            int groups = ThreadGroups(_totalVertexCount);
            Poly_Ling.Diagnostics.PLCamDbg.Dsp("ScreenPos", 0, null, groups);
            _computeShader.Dispatch(_kernelScreenPos, groups, 1, 1);

            // 読み戻しを求められていない呼び出しはここで終わる。
            // 表示用カリングは後続の FaceVisibility / LineVisibility / ApplyMirrorCull が
            // GPU 内で slot バッファを読むだけなので、CPU へ戻す必要がない。
            if (!readback)
                return;

            // CPU 読み戻し（ホバー・ヒットテスト・CommitBoxSelect 用）
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G1 before n=" + _totalVertexCount + " buf=" + screenBuf.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + screenBuf.count + " arr=" + _screenPositions4.Length + " slot=" + slot + " src=" + dbgSrc);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData && Poly_Ling.Diagnostics.PLCamDbg.SwHotGetData)
                screenBuf.GetData(_screenPositions4, 0, 0, _totalVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G1 after");
            for (int i = 0; i < _totalVertexCount; i++)
                _screenPositions[i] = new Vector2(_screenPositions4[i].x, _screenPositions4[i].y);
        }

        /// <summary>
        /// GPUで頂点ヒットテストを実行
        /// </summary>
        public void DispatchVertexHitTestGPU(Vector2 mousePosition, float hitRadius, bool backfaceCullingEnabled = true)
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalVertexCount <= 0)
                return;

            _computeShader.SetVector("_MousePosition", mousePosition);
            _computeShader.SetFloat("_HitRadius", hitRadius);
            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_EnableBackfaceCulling", backfaceCullingEnabled ? 1 : 0);

            // 参照するのはヒットテスト専用 slot。表示用 slot 0〜3 と共用してはならない。
            _computeShader.SetBuffer(_kernelVertexHit, "_ScreenPositionBuffer",   GetSlotScreenPosBuffer(HitTestSlot));
            _computeShader.SetBuffer(_kernelVertexHit, "_VertexFlagsBuffer",       _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelVertexHit, "_VertexCulledBuffer",      GetVertexCulledBuffer(HitTestSlot) ?? _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelVertexHit, "_VertexHitDistanceBuffer", _hitVertexDistBuffer);

            Poly_Ling.Diagnostics.PLCamDbg.Dsp("VertexHit", 0, null, ThreadGroups(_totalVertexCount));
            _computeShader.Dispatch(_kernelVertexHit, ThreadGroups(_totalVertexCount), 1, 1);

            // 結果を読み戻し
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G2 before n=" + _totalVertexCount + " buf=" + _hitVertexDistBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _hitVertexDistBuffer.count + " arr=" + _hitVertexDistances.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData && Poly_Ling.Diagnostics.PLCamDbg.SwHotGetData)
                _hitVertexDistBuffer.GetData(_hitVertexDistances, 0, 0, _totalVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G2 after");
        }

        /// <summary>
        /// GPUで頂点ヒットテストを実行（吸着用・メッシュ選択を無視）。
        ///
        /// 出力は _snapHitVertexDistBuffer で、通常のホバー結果には影響しない。
        /// スクリーン座標とカリングフラグは DispatchVertexHitTestGPU と同じものを
        /// 使うため、必ず ComputeScreenPositionsGPU / DispatchFaceVisibilityGPU の
        /// 後に呼ぶこと。
        ///
        /// 【コスト】頂点数ぶんの GetData が 1 回増える。
        /// 呼び出し側（UnifiedMeshSystem）は必要なときだけ実行すること。
        /// </summary>
        public void DispatchVertexSnapHitTestGPU(Vector2 mousePosition, float hitRadius, bool backfaceCullingEnabled = true)
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalVertexCount <= 0)
                return;

            _computeShader.SetVector("_MousePosition", mousePosition);
            _computeShader.SetFloat("_HitRadius", hitRadius);
            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_EnableBackfaceCulling", backfaceCullingEnabled ? 1 : 0);

            _computeShader.SetBuffer(_kernelVertexSnapHit, "_ScreenPositionBuffer",       GetSlotScreenPosBuffer(HitTestSlot));
            _computeShader.SetBuffer(_kernelVertexSnapHit, "_VertexFlagsBuffer",           _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelVertexSnapHit, "_VertexCulledBuffer",          GetVertexCulledBuffer(HitTestSlot) ?? _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelVertexSnapHit, "_VertexSnapHitDistanceBuffer", _snapHitVertexDistBuffer);

            Poly_Ling.Diagnostics.PLCamDbg.Dsp("VertexSnapHit", 0, null, ThreadGroups(_totalVertexCount));
            _computeShader.Dispatch(_kernelVertexSnapHit, ThreadGroups(_totalVertexCount), 1, 1);

            // 結果を読み戻し
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G3 before n=" + _totalVertexCount + " buf=" + _snapHitVertexDistBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _snapHitVertexDistBuffer.count + " arr=" + _snapHitVertexDistances.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData && Poly_Ling.Diagnostics.PLCamDbg.SwHotGetData)
                _snapHitVertexDistBuffer.GetData(_snapHitVertexDistances, 0, 0, _totalVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G3 after");
        }

        /// <summary>
        /// GPUで線分ヒットテストを実行
        /// </summary>
        public void DispatchLineHitTestGPU(Vector2 mousePosition, float hitRadius, bool backfaceCullingEnabled = true)
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalLineCount <= 0)
                return;

            _computeShader.SetVector("_MousePosition", mousePosition);
            _computeShader.SetFloat("_HitRadius", hitRadius);
            _computeShader.SetInt("_LineCount", _totalLineCount);
            _computeShader.SetInt("_EnableBackfaceCulling", backfaceCullingEnabled ? 1 : 0);

            _computeShader.SetBuffer(_kernelLineHit, "_ScreenPositionBuffer", GetSlotScreenPosBuffer(HitTestSlot));
            _computeShader.SetBuffer(_kernelLineHit, "_LineBuffer",           _lineBuffer);
            _computeShader.SetBuffer(_kernelLineHit, "_LineFlagsBuffer",      _lineFlagsBuffer);
            _computeShader.SetBuffer(_kernelLineHit, "_LineCulledBuffer",     GetLineCulledBuffer(HitTestSlot) ?? _lineFlagsBuffer);
            _computeShader.SetBuffer(_kernelLineHit, "_LineHitDistanceBuffer",_hitLineDistBuffer);

            Poly_Ling.Diagnostics.PLCamDbg.Dsp("LineHit", 0, null, ThreadGroups(_totalLineCount));
            _computeShader.Dispatch(_kernelLineHit, ThreadGroups(_totalLineCount), 1, 1);

            // 結果を読み戻し
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G4 before n=" + _totalLineCount + " buf=" + _hitLineDistBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _hitLineDistBuffer.count + " arr=" + _hitLineDistances.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData && Poly_Ling.Diagnostics.PLCamDbg.SwHotGetData)
                _hitLineDistBuffer.GetData(_hitLineDistances, 0, 0, _totalLineCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G4 after");
        }

        /// <summary>
        /// GPUで面可視性を計算
        /// 注意: ClearBuffersの後、ComputeScreenPositionsGPUの後に実行すること
        /// </summary>
        /// <param name="slot">
        /// カリングスロット。表示用は 0〜ViewportSlotCount-1、ヒットテストは HitTestSlot。
        /// 既定値は付けない。付けると呼び出し側が slot を意識せず書けてしまい、
        /// 表示用 slot 0 をヒットテストが上書きする事故に戻る。
        /// </param>
        public void DispatchFaceVisibilityGPU(int slot)
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalFaceCount <= 0)
                return;

            var screenBuf  = GetSlotScreenPosBuffer(slot);
            if (screenBuf == null) return;
            var vCulledBuf = GetVertexCulledBuffer(slot);
            var fCulledBuf = GetFaceCulledBuffer(slot);
            if (vCulledBuf == null || fCulledBuf == null) return;

            _computeShader.SetInt("_FaceCount",   _totalFaceCount);
            _computeShader.SetInt("_VertexCount",  _totalVertexCount);

            _computeShader.SetBuffer(_kernelFaceVisibility, "_ScreenPositionBuffer", screenBuf);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_FaceBuffer",           _faceBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_FaceFlagsBuffer",      _faceFlagsBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_FaceCulledBuffer",     fCulledBuf);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_IndexBuffer",          _indexBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_VertexFlagsBuffer",    _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_VertexCulledBuffer",   vCulledBuf);

            Poly_Ling.Diagnostics.PLCamDbg.Dsp("FaceVisibility", 0, null, ThreadGroups(_totalFaceCount));
            _computeShader.Dispatch(_kernelFaceVisibility, ThreadGroups(_totalFaceCount), 1, 1);
        }

        /// <summary>
        /// GPUで線分可視性を計算（面ベース）
        /// 注意: DispatchFaceVisibilityGPUの後に実行すること
        /// 入力：面、出力：線分フラグ
        /// </summary>
        /// <param name="slot">カリングスロット（0〜CullingSlotCount-1）</param>
        /// <param name="slot">既定値を付けない理由は DispatchFaceVisibilityGPU を参照。</param>
        public void DispatchLineVisibilityGPU(int slot)
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalFaceCount <= 0)
                return;

            var lCulledBuf = GetLineCulledBuffer(slot);
            var fCulledBuf = GetFaceCulledBuffer(slot);
            if (lCulledBuf == null || fCulledBuf == null) return;

            _computeShader.SetInt("_LineCount", _totalLineCount);
            _computeShader.SetInt("_FaceCount", _totalFaceCount);

            _computeShader.SetBuffer(_kernelLineVisibility, "_FaceBuffer",       _faceBuffer);
            _computeShader.SetBuffer(_kernelLineVisibility, "_FaceFlagsBuffer",  _faceFlagsBuffer);
            _computeShader.SetBuffer(_kernelLineVisibility, "_FaceCulledBuffer", fCulledBuf);
            _computeShader.SetBuffer(_kernelLineVisibility, "_LineFlagsBuffer",  _lineFlagsBuffer);
            _computeShader.SetBuffer(_kernelLineVisibility, "_LineCulledBuffer", lCulledBuf);

            Poly_Ling.Diagnostics.PLCamDbg.Dsp("LineVisibility", 0, null, ThreadGroups(_totalFaceCount));
            _computeShader.Dispatch(_kernelLineVisibility, ThreadGroups(_totalFaceCount), 1, 1);
        }

        /// <summary>
        /// GPUの頂点フラグをCPU配列に読み戻す
        /// 背面カリング結果を取得するために使用
        /// </summary>
        public void ReadBackVertexFlags()
        {
            if (_vertexFlagsBuffer == null || _totalVertexCount <= 0)
                return;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G7 before n=" + _totalVertexCount + " buf=" + _vertexFlagsBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _vertexFlagsBuffer.count + " arr=" + _vertexFlags.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData)
                _vertexFlagsBuffer.GetData(_vertexFlags, 0, 0, _totalVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G7 after");
        }

        /// <summary>
        /// 指定スロットの GPU 頂点カリングバッファ (_VertexCulledBuffer) を
        /// CPU キャッシュ配列 (_vertexCulledCache) に読み戻す。
        ///
        /// 矩形選択・投げ縄選択の CPU ループで「表面の面に属さない頂点」を除外する
        /// ために使用。_vertexFlags は CPU 側の編集対象のためカリング情報を混ぜられない
        /// (CPU→GPU の SetData で消失する) ので、独立したキャッシュを持つ。
        ///
        /// 呼び出しタイミング: 矩形/投げ縄選択の確定直前 (OnLeftDragEnd 内)。
        /// GPU 計算 (DispatchFaceVisibilityGPU) は別経路で毎フレーム走っているので、
        /// ReadBack するだけで最新の結果が得られる。
        /// </summary>
        public void ReadBackVertexCulled(int slot = 0)
        {
            if (_totalVertexCount <= 0) return;
            var vCulledBuf = GetVertexCulledBuffer(slot);
            if (vCulledBuf == null) return;

            if (_vertexCulledCache == null || _vertexCulledCache.Length < _totalVertexCount)
                _vertexCulledCache = new uint[_totalVertexCount];

            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G8 before n=" + _totalVertexCount + " buf=" + vCulledBuf.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + vCulledBuf.count + " arr=" + _vertexCulledCache.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData)
                vCulledBuf.GetData(_vertexCulledCache, 0, 0, _totalVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G8 after");
        }

        /// <summary>
        /// 指定スロットの GPU 面カリングバッファ (_FaceCulledBuffer) を
        /// CPU キャッシュ (_faceCulledCache) に読み戻す。ReadBackVertexCulled と同型。
        /// 呼出前に該当スロットの ComputeScreenPositions + DispatchFaceVisibility を実行しておくこと。
        /// </summary>
        public void ReadBackFaceCulled(int slot = 0)
        {
            if (_totalFaceCount <= 0) return;
            var fCulledBuf = GetFaceCulledBuffer(slot);
            if (fCulledBuf == null) return;

            if (_faceCulledCache == null || _faceCulledCache.Length < _totalFaceCount)
                _faceCulledCache = new uint[_totalFaceCount];

            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G9 before n=" + _totalFaceCount + " buf=" + fCulledBuf.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + fCulledBuf.count + " arr=" + _faceCulledCache.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData)
                fCulledBuf.GetData(_faceCulledCache, 0, 0, _totalFaceCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G9 after");
        }

        // 【DebugPrintCullingStats を撤去した理由】 2026-08-28
        //   呼出元 0 件。内部に _vertexFlagsBuffer / _faceFlagsBuffer の
        //   同期 GetData を 2 本抱えたまま死んでいた（診断マーク G10 / G11）。
        //   復活させる場合は同期読み戻しを伴うことを踏まえて呼ぶこと。

        /// <summary>
        /// GPUで面ヒットテストを実行
        /// </summary>
        public void DispatchFaceHitTestGPU(Vector2 mousePosition, bool backfaceCullingEnabled = true)
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalFaceCount <= 0)
                return;

            _computeShader.SetVector("_MousePosition", mousePosition);
            _computeShader.SetInt("_FaceCount", _totalFaceCount);
            _computeShader.SetInt("_EnableBackfaceCulling", backfaceCullingEnabled ? 1 : 0);

            _computeShader.SetBuffer(_kernelFaceHit, "_ScreenPositionBuffer", GetSlotScreenPosBuffer(HitTestSlot));
            _computeShader.SetBuffer(_kernelFaceHit, "_FaceBuffer",          _faceBuffer);
            _computeShader.SetBuffer(_kernelFaceHit, "_FaceFlagsBuffer",     _faceFlagsBuffer);
            _computeShader.SetBuffer(_kernelFaceHit, "_FaceCulledBuffer",    GetFaceCulledBuffer(HitTestSlot) ?? _faceFlagsBuffer);
            _computeShader.SetBuffer(_kernelFaceHit, "_IndexBuffer",         _indexBuffer);
            _computeShader.SetBuffer(_kernelFaceHit, "_FaceHitBuffer",       _faceHitBuffer);

            Poly_Ling.Diagnostics.PLCamDbg.Dsp("FaceHit", 0, null, ThreadGroups(_totalFaceCount));
            _computeShader.Dispatch(_kernelFaceHit, ThreadGroups(_totalFaceCount), 1, 1);

            // 結果を読み戻し（旧 G5 / G6 の 2 本を 1 本に統合。診断マークは G5 に統一）
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G5 before n=" + _totalFaceCount + " buf=" + _faceHitBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _faceHitBuffer.count + " arr=" + _faceHit.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData && Poly_Ling.Diagnostics.PLCamDbg.SwHotGetData)
                _faceHitBuffer.GetData(_faceHit, 0, 0, _totalFaceCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G5 after");
        }

        /// <summary>
        /// GPU版: 最近接頂点を検索（深度バッファから）
        /// GPU側で距離がhitRadius内の頂点のみ深度を書き込んでいる
        /// hitRadius外は1e10が書き込まれている
        /// </summary>
        /// <summary>
        /// GPU版: 最近接頂点を検索（深度バッファから）
        /// GPU側で距離がhitRadius内の頂点のみ深度を書き込んでいる
        /// hitRadius外は1e10が書き込まれている
        /// GPU側でFLAG_MESH_SELECTEDチェック済み（非選択メッシュは1e10）
        /// </summary>
        public int FindNearestVertexFromGPU(float hitRadius)
        {
            Vector2 mouse = _hitTestInput != null && _hitTestInput.Length > 0
                ? _hitTestInput[0].MousePosition
                : Vector2.zero;
            float tol = Mathf.Max(0.01f, HoverDistanceTolerance);

            int nearestIdx   = -1;
            int nearestBand  = int.MaxValue;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalVertexCount; i++)
            {
                // GPU側でhitRadius外・非選択メッシュは1e10が書き込まれている。
                // ヒット可否の判定はここでやり直さない（半径・メッシュ選択・
                // カリングの判定は全て GPU 側で済んでいる）。
                float depth = _hitVertexDistances[i];
                if (depth >= 1e9f) continue;

                int band = (int)(Vector2.Distance(mouse, _screenPositions[i]) / tol);

                if (band < nearestBand || (band == nearestBand && depth < nearestDepth))
                {
                    nearestBand  = band;
                    nearestDepth = depth;
                    nearestIdx   = i;
                }
            }

            return nearestIdx;
        }

        /// <summary>
        /// GPU版（吸着用）: 最近接頂点を検索（深度バッファから）。
        /// DispatchVertexSnapHitTestGPU の結果を読む。メッシュ選択で絞られていないため
        /// 非選択オブジェクトの頂点も返り得る。未ヒットは -1。
        /// </summary>
        public int FindNearestSnapVertexFromGPU(float hitRadius)
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalVertexCount; i++)
            {
                float depth = _snapHitVertexDistances[i];
                if (depth < 1e9f && depth < nearestDepth)
                {
                    nearestDepth = depth;
                    nearestIdx = i;
                }
            }

            return nearestIdx;
        }



        /// <summary>
        /// GPU版: 最近接線分を検索（深度バッファから）
        /// GPU側で距離がhitRadius内の線分のみ深度を書き込んでいる
        /// hitRadius外は1e10が書き込まれている
        /// GPU側でFLAG_MESH_SELECTEDチェック済み（非選択メッシュは1e10）
        /// </summary>
        public int FindNearestLineFromGPU(float hitRadius)
        {
            Vector2 mouse = _hitTestInput != null && _hitTestInput.Length > 0
                ? _hitTestInput[0].MousePosition
                : Vector2.zero;
            float tol = Mathf.Max(0.01f, HoverDistanceTolerance);

            int nearestIdx   = -1;
            int nearestBand  = int.MaxValue;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalLineCount; i++)
            {
                // GPU側でhitRadius外・非選択メッシュは1e10が書き込まれている。
                // ヒット可否の判定はここでやり直さない。
                float depth = _hitLineDistances[i];
                if (depth >= 1e9f) continue;

                // 線分のスクリーン距離は両端の投影座標から線分距離で求める。
                // CPU 版ヒットテスト (FindNearestLine) と同じ式を使う。
                var line = _lines[i];
                float dist = DistanceToLineSegment(
                    mouse, _screenPositions[line.V1], _screenPositions[line.V2]);
                int band = (int)(dist / tol);

                if (band < nearestBand || (band == nearestBand && depth < nearestDepth))
                {
                    nearestBand  = band;
                    nearestDepth = depth;
                    nearestIdx   = i;
                }
            }

            return nearestIdx;
        }

        /// <summary>
        /// GPU版: 最近接面を検索（ヒットバッファから）
        /// GPU側でFLAG_MESH_SELECTEDチェック済み（非選択メッシュはhit=0, depth=1e10）
        /// </summary>
        public int FindNearestFaceFromGPU()
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalFaceCount; i++)
            {
                // x = ヒット可否、y = 深度。GPU 側で必ずまとめて書かれる。
                if (_faceHit[i].x > 0.5f && _faceHit[i].y < nearestDepth)
                {
                    nearestDepth = _faceHit[i].y;
                    nearestIdx = i;
                }
            }

            return nearestIdx;
        }

        // 【DispatchAllHitTestsGPU を撤去した理由】 2026-08-28
        //   呼出元 0 件。実際のホバー経路は UnifiedMeshSystem.ProcessMouseUpdate が
        //   Clear → ClearCulled → ScreenPos → Visibility → 各 HitTest の順で
        //   個別に呼んでおり、こちらは同じ手順の古い複製だった。
        //   手順を変えるときは ProcessMouseUpdate 側だけを直すこと。

        /// <summary>
        /// GPUでバッファをクリア（スクリーン座標・ヒット距離を初期化）
        /// per-slot カリングバッファは DispatchClearCulledBuffersGPU で別途クリアする。
        /// D3D11.0のUAV制限(8個)のため、2つのカーネルに分割
        /// </summary>
        public void DispatchClearBuffersGPU()
        {
            if (!_gpuComputeAvailable || _computeShader == null)
                return;

            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_LineCount", _totalLineCount);
            _computeShader.SetInt("_FaceCount", _totalFaceCount);

            // カーネル1: 頂点・線分のヒット距離のみ。
            // スクリーン座標のクリアは撤去した。ClearBuffers が書いていた
            // _ScreenPositionBuffer の実体（_screenPosBuffer4）は読み手 0 件で、
            // 実際に読まれる per-slot バッファは ComputeScreenPositions が
            // 全頂点を無条件に書くためクリアが要らない。
            // _VertexFlagsBuffer / _LineFlagsBuffer もカーネル本体が参照していないので
            // バインドをやめた（クリア対象でもない）。
            _computeShader.SetBuffer(_kernelClear, "_VertexHitDistanceBuffer", _hitVertexDistBuffer);
            _computeShader.SetBuffer(_kernelClear, "_LineHitDistanceBuffer",   _hitLineDistBuffer);

            int maxVertexLine = Mathf.Max(_totalVertexCount, _totalLineCount);
            // 【括弧を補った理由】診断行を後から挿入したとき中括弧を付けなかったため、
            // Dispatch が if の外に出て maxVertexLine == 0 でも実行されていた。
            // ThreadGroups(0) == 0 なので Dispatch(0,1,1) になる。
            if (maxVertexLine > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Dsp("Clear", 0, null, ThreadGroups(maxVertexLine));
                _computeShader.Dispatch(_kernelClear, ThreadGroups(maxVertexLine), 1, 1);
            }

            // カーネル2: 面ヒット（x=ヒット, y=深度 の float2 1 本）。
            // _FaceFlagsBuffer はカーネル本体が参照していないのでバインドをやめた。
            _computeShader.SetBuffer(_kernelClearFace, "_FaceHitBuffer", _faceHitBuffer);

            // 括弧を補った理由は上の Clear と同じ。
            if (_totalFaceCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Dsp("ClearFace", 0, null, ThreadGroups(_totalFaceCount));
                _computeShader.Dispatch(_kernelClearFace, ThreadGroups(_totalFaceCount), 1, 1);
            }
        }

        /// <summary>
        /// per-slot カリングバッファをクリア（全頂点・辺・面を「カリング済み」で初期化）。
        /// ComputeScreenPositionsGPU の前に呼ぶこと。
        /// </summary>
        // 永続ミラー（MirrorSide/BakedMirror）の per-slot 表示状態。
        // 描画準備（ClearCulledBuffers 発行）前に SetMirrorDisplay で slot ごとに設定する。
        // 既定は全 slot 表示(1)。要素数は CullingSlotCount に一致させること。
        // 末尾の HitTestSlot は常に表示(1)のまま使う。ヒットテスト経路は
        // DispatchApplyMirrorCullGPU を呼ばないため、ここの値は参照されない。
        //
        // 【頂点と辺を別に持つ理由】 2026-08-28
        //   ApplyMirrorCull カーネルの頂点ブロックと線分ブロックが同じ値を
        //   読んでいたため、「ミラーの辺だけ消して頂点は残す」ができなかった。
        private readonly int[] _showSelectedMirrorVertex   = CreateMirrorDisplayDefaults();
        private readonly int[] _showSelectedMirrorLine     = CreateMirrorDisplayDefaults();
        private readonly int[] _showUnselectedMirrorVertex = CreateMirrorDisplayDefaults();
        private readonly int[] _showUnselectedMirrorLine   = CreateMirrorDisplayDefaults();

        private static int[] CreateMirrorDisplayDefaults()
        {
            var a = new int[CullingSlotCount];
            for (int i = 0; i < a.Length; i++) a[i] = 1;
            return a;
        }

        /// <summary>
        /// 永続ミラーの表示可否を slot 単位で設定する（次回の per-slot カリング適用に反映）。
        ///
        /// 【引数をまとめた 2 引数版を用意しない理由】
        ///   「選択・非選択」だけを渡す旧 API を残すと、辺と頂点を指定し忘れた
        ///   呼び出しがまた生まれる。呼出元は PrepareViewport の 1 か所だけなので、
        ///   4 つとも明示させる。
        /// </summary>
        public void SetMirrorDisplay(
            int slot,
            bool showSelectedVertex,   bool showSelectedLine,
            bool showUnselectedVertex, bool showUnselectedLine)
        {
            if (slot < 0 || slot >= CullingSlotCount) return;
            _showSelectedMirrorVertex[slot]   = showSelectedVertex   ? 1 : 0;
            _showSelectedMirrorLine[slot]     = showSelectedLine     ? 1 : 0;
            _showUnselectedMirrorVertex[slot] = showUnselectedVertex ? 1 : 0;
            _showUnselectedMirrorLine[slot]   = showUnselectedLine   ? 1 : 0;
        }

        public void DispatchClearCulledBuffersGPU(int slot)
        {
            if (!_gpuComputeAvailable || _computeShader == null) return;
            var vBuf = GetVertexCulledBuffer(slot);
            var lBuf = GetLineCulledBuffer(slot);
            var fBuf = GetFaceCulledBuffer(slot);
            if (vBuf == null) return;

            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_LineCount",   _totalLineCount);
            _computeShader.SetInt("_FaceCount",   _totalFaceCount);

            // 頂点・辺
            _computeShader.SetBuffer(_kernelClearCulled, "_VertexFlagsBuffer",  _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelClearCulled, "_VertexCulledBuffer", vBuf);
            _computeShader.SetBuffer(_kernelClearCulled, "_LineFlagsBuffer",    _lineFlagsBuffer);
            _computeShader.SetBuffer(_kernelClearCulled, "_LineCulledBuffer",   lBuf);
            int maxVL = Mathf.Max(_totalVertexCount, _totalLineCount);
            // 括弧を補った理由は DispatchClearBuffersGPU と同じ。
            if (maxVL > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Dsp("ClearCulled", 0, null, ThreadGroups(maxVL));
                _computeShader.Dispatch(_kernelClearCulled, ThreadGroups(maxVL), 1, 1);
            }

            // 面
            if (fBuf != null)
            {
                _computeShader.SetBuffer(_kernelClearFaceCulled, "_FaceFlagsBuffer",  _faceFlagsBuffer);
                _computeShader.SetBuffer(_kernelClearFaceCulled, "_FaceCulledBuffer", fBuf);
                if (_totalFaceCount > 0)
                {
                    Poly_Ling.Diagnostics.PLCamDbg.Dsp("ClearFaceCulled", 0, null, ThreadGroups(_totalFaceCount));
                    _computeShader.Dispatch(_kernelClearFaceCulled, ThreadGroups(_totalFaceCount), 1, 1);
                }
            }
        }

        /// <summary>
        /// 永続ミラー（MirrorSide/BakedMirror）要素の最終カリングを per-slot で適用する。
        /// 表向き面による un-cull 上書きを受けないよう、ComputeFace/LineVisibility の後に呼ぶこと。
        /// SetMirrorDisplay(slot, ...) で設定した slot ごとの表示状態を参照する。
        /// </summary>
        public void DispatchApplyMirrorCullGPU(int slot)
        {
            if (!_gpuComputeAvailable || _computeShader == null) return;
            var vBuf = GetVertexCulledBuffer(slot);
            var lBuf = GetLineCulledBuffer(slot);
            if (vBuf == null || lBuf == null) return;

            bool inRange = slot >= 0 && slot < CullingSlotCount;
            int selVtx   = inRange ? _showSelectedMirrorVertex[slot]   : 1;
            int selLine  = inRange ? _showSelectedMirrorLine[slot]     : 1;
            int unselVtx = inRange ? _showUnselectedMirrorVertex[slot] : 1;
            int unselLine= inRange ? _showUnselectedMirrorLine[slot]   : 1;

            // 4 つとも表示なら、このカーネルは何も変えないので dispatch を省く。
            // 1 つでも 0 があれば実行する。
            if (selVtx != 0 && selLine != 0 && unselVtx != 0 && unselLine != 0) return;

            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_LineCount",   _totalLineCount);
            _computeShader.SetInt("_ShowSelectedMirrorVertex",   selVtx);
            _computeShader.SetInt("_ShowSelectedMirrorLine",     selLine);
            _computeShader.SetInt("_ShowUnselectedMirrorVertex", unselVtx);
            _computeShader.SetInt("_ShowUnselectedMirrorLine",   unselLine);
            _computeShader.SetBuffer(_kernelApplyMirrorCull, "_VertexFlagsBuffer",  _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelApplyMirrorCull, "_VertexCulledBuffer", vBuf);
            _computeShader.SetBuffer(_kernelApplyMirrorCull, "_LineFlagsBuffer",    _lineFlagsBuffer);
            _computeShader.SetBuffer(_kernelApplyMirrorCull, "_LineCulledBuffer",   lBuf);
            int maxVL = Mathf.Max(_totalVertexCount, _totalLineCount);
            // 括弧を補った理由は DispatchClearBuffersGPU と同じ。
            if (maxVL > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Dsp("ApplyMirrorCull", 0, null, ThreadGroups(maxVL));
                _computeShader.Dispatch(_kernelApplyMirrorCull, ThreadGroups(maxVL), 1, 1);
            }
        }

        /// <summary>
        /// 背面カリング無効時: 指定スロットの全カリングバッファをゼロ（可視）にクリアする。
        /// DispatchClearCulledBuffersGPU の後に呼ぶこと。
        /// </summary>
        /// <param name="slot">既定値を付けない理由は DispatchFaceVisibilityGPU を参照。</param>
        public void ClearCulledFlagsGPU(int slot)
        {
            var vBuf = GetVertexCulledBuffer(slot);
            var lBuf = GetLineCulledBuffer(slot);
            var fBuf = GetFaceCulledBuffer(slot);

            // zeros キャッシュが未確保の場合は確保する
            if (_zeroVertexCache == null || _zeroVertexCache.Length < _totalVertexCount)
                Array.Resize(ref _zeroVertexCache, Mathf.NextPowerOfTwo(Mathf.Max(1, _totalVertexCount)));
            if (_zeroLineCache   == null || _zeroLineCache.Length   < _totalLineCount)
                Array.Resize(ref _zeroLineCache,   Mathf.NextPowerOfTwo(Mathf.Max(1, _totalLineCount)));
            if (_zeroFaceCache   == null || _zeroFaceCache.Length   < _totalFaceCount)
                Array.Resize(ref _zeroFaceCache,   Mathf.NextPowerOfTwo(Mathf.Max(1, _totalFaceCount)));

            // 【括弧を補った理由】診断行を後から挿入したとき中括弧を付けなかったため、
            // SetData が if の外に出て「バッファが null でも呼ぶ」状態になっていた。
            // 本メソッドは背面カリング OFF のとき毎回通る経路。
            if (vBuf != null && _totalVertexCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("vBuf", vBuf, 0);
                vBuf.SetData(_zeroVertexCache, 0, 0, _totalVertexCount);
            }
            if (lBuf != null && _totalLineCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("lBuf", lBuf, 0);
                lBuf.SetData(_zeroLineCache, 0, 0, _totalLineCount);
            }
            if (fBuf != null && _totalFaceCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("fBuf", fBuf, 0);
                fBuf.SetData(_zeroFaceCache, 0, 0, _totalFaceCount);
            }
        }
    }
}

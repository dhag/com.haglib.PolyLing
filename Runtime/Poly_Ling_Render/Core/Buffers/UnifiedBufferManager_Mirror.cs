// Assets/Editor/Poly_Ling/Core/Buffers/UnifiedBufferManager_Mirror.cs
// 統合バッファ管理クラス - ミラー処理
// ミラー頂点の計算と管理

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Core
{
    public partial class UnifiedBufferManager
    {
        // ============================================================
        // ミラー設定
        // ============================================================

        private bool _mirrorEnabled = false;
        private SymmetryAxis _mirrorAxis = SymmetryAxis.X;
        private float _mirrorOffset = 0f;
        private Matrix4x4 _mirrorMatrix = Matrix4x4.identity;

        private int _mirrorVertexCount = 0;
        //private int _mirrorLineCount = 0;

        /// <summary>ミラー有効</summary>
        public bool MirrorEnabled => _mirrorEnabled;

        /// <summary>ミラー軸</summary>
        public SymmetryAxis MirrorAxis => _mirrorAxis;

        /// <summary>ミラー頂点数</summary>
        public int MirrorVertexCount => _mirrorVertexCount;

        /// <summary>ミラー位置バッファ</summary>
        public ComputeBuffer MirrorPositionBuffer => _mirrorPositionBuffer;

        // ============================================================
        // ミラー設定
        // ============================================================

        /// <summary>
        /// ミラー設定を更新
        /// </summary>
        public void SetMirrorSettings(bool enabled, SymmetryAxis axis, float offset = 0f)
        {
            _mirrorEnabled = enabled;
            _mirrorAxis = axis;
            _mirrorOffset = offset;

            // ミラー行列を計算
            _mirrorMatrix = ComputeMirrorMatrix(axis, offset);

            if (_mirrorEnabled)
            {
                UpdateMirrorPositions();
            }
        }

        /// <summary>
        /// SymmetrySettingsから設定を適用
        /// </summary>
        public void SetMirrorSettings(SymmetrySettings settings)
        {
            if (settings == null)
            {
                _mirrorEnabled = false;
                return;
            }

            SetMirrorSettings(settings.IsEnabled, settings.Axis, settings.PlaneOffset);
        }

        /// <summary>
        /// ミラー行列を計算
        /// </summary>
        private Matrix4x4 ComputeMirrorMatrix(SymmetryAxis axis, float offset)
        {
            Vector3 normal;
            switch (axis)
            {
                case SymmetryAxis.X:
                    normal = Vector3.right;
                    break;
                case SymmetryAxis.Y:
                    normal = Vector3.up;
                    break;
                case SymmetryAxis.Z:
                    normal = Vector3.forward;
                    break;
                default:
                    return Matrix4x4.identity;
            }

            // 反射行列: I - 2 * n * n^T
            // オフセット付きの場合は平行移動も必要
            Matrix4x4 reflection = Matrix4x4.identity;

            // 反射成分
            reflection.m00 = 1 - 2 * normal.x * normal.x;
            reflection.m01 = -2 * normal.x * normal.y;
            reflection.m02 = -2 * normal.x * normal.z;

            reflection.m10 = -2 * normal.y * normal.x;
            reflection.m11 = 1 - 2 * normal.y * normal.y;
            reflection.m12 = -2 * normal.y * normal.z;

            reflection.m20 = -2 * normal.z * normal.x;
            reflection.m21 = -2 * normal.z * normal.y;
            reflection.m22 = 1 - 2 * normal.z * normal.z;

            // オフセット（平面が原点から離れている場合）
            if (Mathf.Abs(offset) > 0.0001f)
            {
                Vector3 planePoint = normal * offset;
                Vector3 translation = 2 * Vector3.Dot(planePoint, normal) * normal;
                reflection.m03 = translation.x;
                reflection.m13 = translation.y;
                reflection.m23 = translation.z;
            }

            return reflection;
        }

        // ============================================================
        // ミラー位置更新
        // ============================================================

        /// <summary>
        /// ミラー頂点位置を更新
        /// </summary>
        public void UpdateMirrorPositions()
        {
            if (!_mirrorEnabled)
            {
                _mirrorVertexCount = 0;
                return;
            }

            _mirrorVertexCount = _totalVertexCount;

            // ミラー位置を計算
            for (int i = 0; i < _totalVertexCount; i++)
            {
                Vector3 pos = _positions[i];
                Vector4 pos4 = new Vector4(pos.x, pos.y, pos.z, 1f);
                Vector4 mirrorPos4 = _mirrorMatrix * pos4;
                _mirrorPositions[i] = new Vector3(mirrorPos4.x, mirrorPos4.y, mirrorPos4.z);
            }

            // GPUにアップロード
            if (_mirrorVertexCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_mirrorPositionBuffer", _mirrorPositionBuffer, 0);
                _mirrorPositionBuffer.SetData(_mirrorPositions, 0, 0, _mirrorVertexCount);
            }
        }

        /// <summary>
        /// 特定メッシュのミラー位置を更新
        /// </summary>
        public void UpdateMirrorPositions(int meshIndex)
        {
            if (!_mirrorEnabled || meshIndex < 0 || meshIndex >= _meshCount)
                return;

            var meshInfo = _meshInfos[meshIndex];
            int startIdx = (int)meshInfo.VertexStart;
            int count = (int)meshInfo.VertexCount;

            for (int i = 0; i < count; i++)
            {
                int globalIdx = startIdx + i;
                Vector3 pos = _positions[globalIdx];
                Vector4 pos4 = new Vector4(pos.x, pos.y, pos.z, 1f);
                Vector4 mirrorPos4 = _mirrorMatrix * pos4;
                _mirrorPositions[globalIdx] = new Vector3(mirrorPos4.x, mirrorPos4.y, mirrorPos4.z);
            }

            // 部分アップロード
            Poly_Ling.Diagnostics.PLCamDbg.Wr("_mirrorPositionBuffer", _mirrorPositionBuffer, 0);
            _mirrorPositionBuffer.SetData(_mirrorPositions, startIdx, startIdx, count);
        }

        // ============================================================
        // ミラーフラグ設定
        // ============================================================

        /// <summary>
        /// ミラー要素のフラグを設定
        /// </summary>
        public void SetMirrorFlags()
        {
            if (!_mirrorEnabled)
                return;

            // 頂点フラグにミラーマークを追加
            // Note: ミラー頂点は別バッファなので、描画時にフラグで判断
            // ここでは元の頂点にミラー表示が有効であることをマークするだけ
        }

        /// <summary>
        /// ミラー位置を取得
        /// </summary>
        public Vector3 GetMirrorPosition(int globalVertexIndex)
        {
            if (!_mirrorEnabled || globalVertexIndex < 0 || globalVertexIndex >= _mirrorVertexCount)
                return Vector3.zero;

            return _mirrorPositions[globalVertexIndex];
        }

        /// <summary>
        /// スキニング済みミラー位置を全取得（GPU→CPU読み戻し）
        /// TransformVertices実行後に呼び出すこと
        /// </summary>
        public Vector3[] GetMirrorPositions()
        {
            if (!_mirrorEnabled || _skinnedMirrorPositionBuffer == null || _totalVertexCount == 0)
                return null;

            if (_skinnedMirrorPositions == null || _skinnedMirrorPositions.Length < _totalVertexCount)
                _skinnedMirrorPositions = new Vector3[_totalVertexCount];

            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G15 before n=" + _totalVertexCount + " buf=" + _skinnedMirrorPositionBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _skinnedMirrorPositionBuffer.count + " arr=" + _skinnedMirrorPositions.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData)
                _skinnedMirrorPositionBuffer.GetData(_skinnedMirrorPositions, 0, 0, _totalVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G15 after");
            return _skinnedMirrorPositions;
        }

        /// <summary>
        /// スキニング済みミラー位置バッファを取得
        /// </summary>
        public ComputeBuffer SkinnedMirrorPositionBuffer => _skinnedMirrorPositionBuffer;

        /// <summary>
        /// 元の位置からミラー位置を計算
        /// </summary>
        public Vector3 ComputeMirrorPosition(Vector3 position)
        {
            if (!_mirrorEnabled)
                return position;

            Vector4 pos4 = new Vector4(position.x, position.y, position.z, 1f);
            Vector4 mirrorPos4 = _mirrorMatrix * pos4;
            return new Vector3(mirrorPos4.x, mirrorPos4.y, mirrorPos4.z);
        }

        /// <summary>
        /// ミラー行列を取得
        /// </summary>
        public Matrix4x4 GetMirrorMatrix()
        {
            return _mirrorMatrix;
        }

        // ============================================================
        // ミラースクリーン座標
        // ============================================================
        //
        // 【ComputeMirrorScreenPositions 一式を撤去した理由】 2026-08-28
        //   _mirrorScreenPosBuffer(float2) は本メソッドが SetData するだけで、
        //   ComputeShader.SetBuffer へ渡している箇所が 0 件だった。
        //   GetMirrorScreenPosBuffer() の呼出元も 0 件。
        //   すなわち「全ミラー頂点ぶんの投影計算を CPU で回し、GPU へ転送し、
        //   誰も読まない」処理だった。
        //   GPU が実際に使うミラースクリーン座標は _mirrorScreenPosBuffer4 で、
        //   これは UnifiedCompute.compute の ComputeScreenPositions カーネルが
        //   _UseMirror > 0 のときに GPU 側で直接埋める。
        //
        //   あわせて ReleaseMirrorBuffers() も撤去した。呼出元が 0 件で、
        //   _mirrorScreenPosBuffer が一度も解放されていなかった（残件 6 章の
        //   「ReleaseAllBuffers の一覧から漏れている」はこれが原因）。
    }
}

// UnifiedBufferManager_Visibility.cs
// MeshContext.IsVisible / IsLocked を GPU の Hidden / Locked ビットへ反映する部分更新。
//
// 【なぜ要るか】
//   Hidden / Locked を書いているのは Build の3箇所だけで、
//   いずれもバッファ全構築（RebuildAdapter）のときにしか走らない。
//     BuildVertexData  … UnifiedBufferManager_Build.cs
//     BuildLineData    … 同上
//     BuildFaceData    … 同上
//   可視・ロックの変更は ChangeKind.Attributes で通知され、
//   EnterSelectionChanged はバッファを作り直さないため GPU に届かない。
//   面だけは SubmitMeshes が毎フレーム MeshContext.IsVisible を見るので消え、
//   頂点と辺だけが残る、という食い違いが起きていた。
//
//   既存の UpdateAllSelectionFlags は HierarchyMask と選択ビットしか
//   クリアしないため Hidden / Locked には触れない。ここで別途扱う。
//
// 【何をするか】
//   頂点・線分・面の3配列について、Hidden / Locked ビットだけを立て直して
//   GPU へ転送する。位置・法線・トポロジには触れない。
//
// Runtime/Poly_Ling_Render/Core/Buffers/ に配置

using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Core
{
    public partial class UnifiedBufferManager
    {
        /// <summary>
        /// MeshContext の IsVisible / IsLocked から Hidden / Locked ビットを更新し、
        /// GPU へ転送する。RebuildAdapter は伴わない。
        ///
        /// 可視の判定は Build と同じ規則にそろえる。
        ///   頂点 … メッシュが可視 かつ その頂点を参照する面がすべて非表示ではない
        ///   線分 … メッシュが可視 かつ その線分を登録した面が非表示ではない
        ///   面   … メッシュが可視 かつ その面が非表示ではない
        /// </summary>
        public void UpdateAllVisibilityFlags()
        {
            if (!_isInitialized || _modelContext == null) return;

            UpdateVertexVisibilityFlags();
            UpdateLineVisibilityFlags();
            UpdateFaceVisibilityFlags();
        }

        // ================================================================
        // 頂点
        // ================================================================

        private void UpdateVertexVisibilityFlags()
        {
            if (_vertexFlags == null || _totalVertexCount == 0) return;

            for (int meshIdx = 0; meshIdx < _meshCount; meshIdx++)
            {
                var meshContext = ResolveMeshContext(meshIdx);
                if (meshContext?.MeshObject == null) continue;

                var meshInfo = _meshInfos[meshIdx];
                bool isVisible = meshContext.IsVisible;
                bool isLocked  = meshContext.IsLocked;

                // 面の非表示を頂点へ展開する規則は Build と共通のものを使う。
                // この関数は UpdateAllSelectionFlags 経由で描画準備のたびに走るため、
                // 非表示面が1つも無いメッシュでは配列を作らずに済ませる。
                bool[] vertexVisibleByFace = null;
                if (HasHiddenFace(meshContext.MeshObject))
                    vertexVisibleByFace = BuildVertexVisibilityByFace(meshContext.MeshObject);

                for (uint v = 0; v < meshInfo.VertexCount; v++)
                {
                    uint globalIdx = meshInfo.VertexStart + v;
                    if (globalIdx >= _totalVertexCount) break;

                    bool byFace = vertexVisibleByFace == null
                        || v >= vertexVisibleByFace.Length
                        || vertexVisibleByFace[v];
                    bool vertexVisible = isVisible && byFace;

                    _vertexFlags[globalIdx] =
                        SetHiddenLocked(_vertexFlags[globalIdx], !vertexVisible, isLocked);
                }
            }

            _vertexFlagsBuffer?.SetData(_vertexFlags, 0, 0, _totalVertexCount);
        }

        // ================================================================
        // 線分
        // ================================================================

        private void UpdateLineVisibilityFlags()
        {
            if (_lineFlags == null || _lines == null || _totalLineCount == 0) return;

            for (int lineIdx = 0; lineIdx < _totalLineCount; lineIdx++)
            {
                var line = _lines[lineIdx];
                int meshIdx = (int)line.MeshIndex;

                var meshContext = ResolveMeshContext(meshIdx);
                if (meshContext?.MeshObject == null) continue;

                var meshInfo = _meshInfos[meshIdx];

                // line.FaceIndex はグローバル。ローカルへ直して Face.IsHidden を見る。
                bool faceHidden = false;
                int localFaceIndex = (int)(line.FaceIndex - meshInfo.FaceStart);
                var faces = meshContext.MeshObject.Faces;
                if (localFaceIndex >= 0 && localFaceIndex < faces.Count)
                    faceHidden = faces[localFaceIndex].IsHidden;

                bool lineVisible = meshContext.IsVisible && !faceHidden;

                uint flags = SetHiddenLocked(_lineFlags[lineIdx], !lineVisible, meshContext.IsLocked);
                _lineFlags[lineIdx] = flags;

                // _lines 側の Flags も同じ値を保つ（描画側がどちらを見ても一致させる）。
                line.Flags = flags;
                _lines[lineIdx] = line;
            }

            _lineFlagsBuffer?.SetData(_lineFlags, 0, 0, _totalLineCount);
            _lineBuffer?.SetData(_lines, 0, 0, _totalLineCount);
        }

        // ================================================================
        // 面
        // ================================================================

        private void UpdateFaceVisibilityFlags()
        {
            if (_faceFlags == null || _faces == null || _totalFaceCount == 0) return;

            for (int meshIdx = 0; meshIdx < _meshCount; meshIdx++)
            {
                var meshContext = ResolveMeshContext(meshIdx);
                if (meshContext?.MeshObject == null) continue;

                var meshInfo = _meshInfos[meshIdx];
                var faces    = meshContext.MeshObject.Faces;
                bool isVisible = meshContext.IsVisible;
                bool isLocked  = meshContext.IsLocked;

                for (uint f = 0; f < meshInfo.FaceCount; f++)
                {
                    uint globalIdx = meshInfo.FaceStart + f;
                    if (globalIdx >= _totalFaceCount) break;

                    bool faceHidden = f < faces.Count && faces[(int)f].IsHidden;
                    bool faceVisible = isVisible && !faceHidden;

                    uint flags = SetHiddenLocked(_faceFlags[globalIdx], !faceVisible, isLocked);
                    _faceFlags[globalIdx] = flags;

                    var face = _faces[globalIdx];
                    face.Flags = flags;
                    _faces[globalIdx] = face;
                }
            }

            _faceFlagsBuffer?.SetData(_faceFlags, 0, 0, _totalFaceCount);
            _faceBuffer?.SetData(_faces, 0, 0, _totalFaceCount);
        }

        // ================================================================
        // 補助
        // ================================================================

        /// <summary>非表示の面が1つでもあるか。無ければ頂点展開を省略できる。</summary>
        private static bool HasHiddenFace(MeshObject meshObject)
        {
            var faces = meshObject?.Faces;
            if (faces == null) return false;
            for (int i = 0; i < faces.Count; i++)
                if (faces[i].IsHidden) return true;
            return false;
        }

        /// <summary>Hidden / Locked ビットだけを設定し、他のビットは触らない。</summary>
        private static uint SetHiddenLocked(uint flags, bool hidden, bool locked)
        {
            if (hidden) flags |=  (uint)SelectionFlags.Hidden;
            else        flags &= ~(uint)SelectionFlags.Hidden;

            if (locked) flags |=  (uint)SelectionFlags.Locked;
            else        flags &= ~(uint)SelectionFlags.Locked;

            return flags;
        }

        /// <summary>
        /// 統合メッシュ index から MeshContext を引く。
        ///
        /// _unifiedToContextMap は SyncSelectionFromModel が
        /// SelectedDrawableMeshIndices のぶんだけを毎回作り直すもので、
        /// 選択されていないメッシュは入っていない。ミラー側は選択対象から
        /// 外れているため、そちらを使うとフラグ更新から漏れる。
        /// バッファ構築時に全メッシュぶん作られる _unifiedToContextMeshIndex を使う。
        /// </summary>
        private MeshContext ResolveMeshContext(int unifiedMeshIndex)
        {
            if (_modelContext == null) return null;
            int ctxIdx = UnifiedToContextMeshIndex(unifiedMeshIndex);
            if (ctxIdx < 0) return null;
            return _modelContext.GetMeshContext(ctxIdx);
        }
    }
}

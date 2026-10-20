// MotionFileLoad.cs
// ============================================================
// モーションファイルを種類ごとに読む共通口。
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/Motion/ に配置。
// ExportMotionJsonCommand・ExportMotionToBvhCommand・MotionClipHandler（パネルの試し再生）が
// この 1 か所を通す（種類 → 読み手の振り分けを複数の場所に書かない。PolyLing_BVH対応計画.md 6）。
// ============================================================

using System;
using Poly_Ling.Data;
using Poly_Ling.VMD;

namespace Poly_Ling.Motion
{
    public static class MotionFileLoad
    {
        /// <summary>
        /// path を kind の読み手で読む。bvh は BVH のときだけ使う（null なら既定＝表の自動選択）。
        /// fbx は FBX のときだけ使う（null なら先頭のテイク、割り当て CSV は bvh.MappingCsvLines）。
        /// 失敗時は Dto が null で、理由は Issues に入る。
        /// </summary>
        public static MotionClipLoadResult Load(string path, MotionSourceKind kind, BvhReadOptions bvh = null,
                                                Poly_Ling.Fbx.FbxMotionReadOptions fbx = null)
        {
            switch (kind)
            {
                case MotionSourceKind.Vmd:
                {
                    var r = new MotionClipLoadResult();
                    try
                    {
                        var dto = MotionClipConverters.FromVMD(VMDData.LoadFromFile(path));
                        if (dto == null) r.Error(MotionFileCodes.FileReadFailed, "VMD を読み取れません");
                        else r.Dto = dto;
                    }
                    catch (Exception ex)
                    {
                        r.Error(MotionFileCodes.FileReadFailed, $"VMD の読込みに失敗: {ex.Message}");
                    }
                    return r;
                }
                case MotionSourceKind.Vrma: return VrmaCodec.Load(path);
                case MotionSourceKind.Bvh:  return BvhCodec.Load(path, bvh);
                case MotionSourceKind.Fbx:
                    return Poly_Ling.Fbx.FbxMotionReader.Load(path,
                        fbx ?? new Poly_Ling.Fbx.FbxMotionReadOptions { MappingCsvLines = bvh?.MappingCsvLines });
                default:                    return MotionClipSerializer.Load(path);
            }
        }
    }
}

// FbxSdkInfoImpl.cs
// ============================================================
// IFbxSdkInfo の Autodesk FBX SDK 実装
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md。分離の形は PLFbxBridge.cs 冒頭。
//
// 本アセンブリ（PolyLing.Fbx）だけが Autodesk.Fbx を参照する。
// ビルド版では FBXSDK_RUNTIME（Player の Scripting Define Symbols）が無いと
// ネイティブ DLL（UnityFbxSdkNative.dll）がビルドへコピーされず、呼ぶと例外になる
// （com.autodesk.fbx の Editor/Scripts/PostProcessAddToBuild.cs）。
// ============================================================

using UnityEngine;
using Autodesk.Fbx;
using Poly_Ling.Fbx;

namespace Poly_Ling.FbxImpl
{
    public class FbxSdkInfoImpl : IFbxSdkInfo
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PLFbxBridge.RegisterSdk(new FbxSdkInfoImpl());
        }

        public string Version => FbxManager.GetVersion(false);
    }
}

// PlayerCaptureRunner.cs
// 画面キャプチャをフレーム終端で1回だけ実行するためのコルーチンホスト。
// ScreenCapture.CaptureScreenshotAsTexture は描画完了後（フレーム終端）に
// 呼ぶ必要があるため、MonoBehaviour を介して WaitForEndOfFrame を待つ。
// 常駐はするが毎フレーム処理（Update / ポーリング）は一切持たない。
// Runtime/Poly_Ling_Player/View/Capture/ に配置

using System;
using System.Collections;
using UnityEngine;

namespace Poly_Ling.Player
{
    public class PlayerCaptureRunner : MonoBehaviour
    {
        private static PlayerCaptureRunner _instance;

        /// <summary>
        /// 実行用インスタンスを返す（無ければ隠し GameObject を1つ作る）。
        /// PlayerViewport のカメラ GameObject と同じ hideFlags 方針。
        /// </summary>
        public static PlayerCaptureRunner Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("PolyLingCaptureRunner")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _instance = go.AddComponent<PlayerCaptureRunner>();
                return _instance;
            }
        }

        /// <summary>フレーム終端まで待ってから action を1回だけ実行する。</summary>
        public void RunAtEndOfFrame(Action action)
        {
            if (action == null) return;
            StartCoroutine(EndOfFrame(action));
        }

        private IEnumerator EndOfFrame(Action action)
        {
            yield return new WaitForEndOfFrame();
            action();
        }
    }
}

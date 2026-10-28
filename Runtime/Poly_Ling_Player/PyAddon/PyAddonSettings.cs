// PyAddonSettings.cs
// Python アドオンの登録簿（公式の表と利用者が足したもの）と、Python 環境の設定の保存。
// 保存先: Application.persistentDataPath/PolyLing/PyAddons.json（テキストエディタで直してもよい）
// Runtime/Poly_Ling_Player/PyAddon/ に配置
//
// 【公式アドオン】
//   PolyLing が組み込みで知っているアドオン。スクリプトは poly_ling_py リポジトリ（Root）からの相対パス。
//   id は Python 側の Addon(id=…) と同じにする（capabilities "<id>.run" などの頭になる）。
// 【利用者のアドオン】
//   スクリプトの絶対パスで登録する。id はスクリプトのファイル名（拡張子なし）。Python 側の Addon の id も同じにすること。
// 【Python 環境】
//   Root … poly_ling_py リポジトリのフォルダ（子プロセスの作業フォルダ・公式スクリプトの基準）
//   Python … venv の python.exe。空なら Root/.venv/Scripts/python.exe（Windows）か Root/.venv/bin/python。
// 【コマンドで変えない】
//   どの実行ファイルを起動するかを決める設定なので、作業フォルダと同じく画面（ダイアログ）からだけ変える。

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Poly_Ling.Player
{
    /// <summary>アドオン 1 つの登録。</summary>
    public sealed class PyAddonDef
    {
        public string Id;
        public string Title;
        /// <summary>公式は Root からの相対パス、利用者のものは絶対パス。</summary>
        public string Script;
        [JsonIgnore] public bool Official;
    }

    /// <summary>登録簿と設定。読み書きはメインスレッドから。</summary>
    public sealed class PyAddonSettings
    {
        /// <summary>公式アドオンの表。</summary>
        public static readonly PyAddonDef[] OfficialAddons =
        {
            new PyAddonDef { Id = "mediapipe_face", Title = "顔メッシュ（MediaPipe）",
                             Script = "examples/mediapipe_face_to_polyling.py", Official = true },
            new PyAddonDef { Id = "mediapipe_live", Title = "MediaPipe ライブ送信",
                             Script = "examples/mediapipe_live_to_polyling.py", Official = true },
            new PyAddonDef { Id = "humanoid_fit", Title = "ヒューマノイドボーン当てはめ（MediaPipe）",
                             Script = "examples/mediapipe_humanoid_fit_to_polyling.py", Official = true },
            new PyAddonDef { Id = "hand_fit", Title = "指ボーン当てはめ（MediaPipe Hand）",
                             Script = "examples/mediapipe_hand_fit_to_polyling.py", Official = true },
        };

        [JsonProperty("root")]   public string Root   = "";
        [JsonProperty("python")] public string Python = "";
        [JsonProperty("user")]   public List<PyAddonDef> User = new List<PyAddonDef>();
        /// <summary>アドオンが describe で返した引数の宣言（最後に受けたもの）。</summary>
        [JsonProperty("specs")]  public Dictionary<string, JObject> Specs = new Dictionary<string, JObject>();
        /// <summary>最後に実行したときの引数の値。</summary>
        [JsonProperty("lastArgs")] public Dictionary<string, JObject> LastArgs = new Dictionary<string, JObject>();

        private static string FilePath
            => Path.Combine(Application.persistentDataPath, "PolyLing", "PyAddons.json");

        public static PyAddonSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonConvert.DeserializeObject<PyAddonSettings>(File.ReadAllText(FilePath));
                    if (s != null)
                    {
                        s.User     = s.User     ?? new List<PyAddonDef>();
                        s.Specs    = s.Specs    ?? new Dictionary<string, JObject>();
                        s.LastArgs = s.LastArgs ?? new Dictionary<string, JObject>();
                        s.User.RemoveAll(d => d == null || string.IsNullOrEmpty(d.Id));
                        return s;
                    }
                }
            }
            catch (Exception e)
            {
                PlayerLog.Add("PyAddon", $"設定を読めません（{FilePath}）: {e.Message}", PlayerLogLevel.Warning);
            }
            return new PyAddonSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception e)
            {
                PlayerLog.Add("PyAddon", $"設定を書けません（{FilePath}）: {e.Message}", PlayerLogLevel.Warning);
            }
        }

        /// <summary>公式に続けて利用者のもの。</summary>
        public List<PyAddonDef> All()
        {
            var list = new List<PyAddonDef>(OfficialAddons);
            foreach (var d in User)
                if (Find(d.Id, list) == null) list.Add(d);
            return list;
        }

        public PyAddonDef Find(string id) => Find(id, All());

        private static PyAddonDef Find(string id, List<PyAddonDef> list)
        {
            foreach (var d in list) if (d.Id == id) return d;
            return null;
        }

        /// <summary>使う python の実行ファイル（設定が空なら Root の .venv）。</summary>
        public string ResolvePython()
        {
            if (!string.IsNullOrEmpty(Python)) return Python;
            if (string.IsNullOrEmpty(Root)) return "";
            string win = Path.Combine(Root, ".venv", "Scripts", "python.exe");
            if (File.Exists(win)) return win;
            string nix = Path.Combine(Root, ".venv", "bin", "python");
            return File.Exists(nix) ? nix : win;
        }

        /// <summary>スクリプトの絶対パス。</summary>
        public string ResolveScript(PyAddonDef d)
        {
            if (d == null || string.IsNullOrEmpty(d.Script)) return "";
            if (Path.IsPathRooted(d.Script)) return d.Script;
            return string.IsNullOrEmpty(Root) ? d.Script : Path.GetFullPath(Path.Combine(Root, d.Script));
        }

        /// <summary>子プロセスの作業フォルダ（Root。空ならスクリプトのフォルダ）。</summary>
        public string ResolveWorkingDirectory(PyAddonDef d)
        {
            if (!string.IsNullOrEmpty(Root) && Directory.Exists(Root)) return Root;
            string script = ResolveScript(d);
            return string.IsNullOrEmpty(script) ? "" : Path.GetDirectoryName(script);
        }

        /// <summary>利用者のアドオンを足す（id はファイル名）。同じ id があれば理由を返す。</summary>
        public string AddUser(string scriptPath, string title = null)
        {
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath)) return "スクリプトがありません: " + scriptPath;
            string id = Path.GetFileNameWithoutExtension(scriptPath);
            if (Find(id) != null) return $"id \"{id}\" のアドオンは登録済みです";
            User.Add(new PyAddonDef { Id = id, Title = string.IsNullOrEmpty(title) ? id : title, Script = scriptPath });
            Save();
            return null;
        }

        /// <summary>利用者のアドオンを外す（公式は外せない）。</summary>
        public string RemoveUser(string id)
        {
            int n = User.RemoveAll(d => d.Id == id);
            if (n == 0) return $"利用者のアドオン \"{id}\" はありません（公式は外せません）";
            Specs.Remove(id);
            LastArgs.Remove(id);
            Save();
            return null;
        }
    }
}

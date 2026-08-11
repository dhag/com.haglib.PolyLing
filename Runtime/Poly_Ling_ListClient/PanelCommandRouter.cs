// PanelCommandRouter.cs
// パネルが発行する PanelCommand を、サーバの command プロトコル
// (RemoteServerCore.BuildPanelCommand が解釈する action/params) へ変換して送信する。
// サーバ未対応のコマンドは無視する。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Player;

namespace Poly_Ling.ListClient
{
    public sealed class PanelCommandRouter
    {
        private readonly PolyLingPlayerClient _client;

        /// <summary>
        /// (modelIndex, masterIndex) → 安定ObjectId を返す解決子。
        /// 設定すると masterIndex を持つ全コマンドに objectIds を添えて送る。
        /// サーバはこれを照合し、リスト構造がズレていればコマンドを拒否する。
        /// 未設定（null）なら従来通り index のみ送る。
        /// </summary>
        public System.Func<int, int, ulong> ResolveObjectId;

        public PanelCommandRouter(PolyLingPlayerClient client)
        {
            _client = client;
        }

        public void Send(PanelCommand cmd)
        {
            if (_client == null || cmd == null || !_client.IsConnected) return;

            switch (cmd)
            {
                // ── 編集者（担当）の取得・解放 ────────────────────────
                case SetObjectEditorCommand c:
                    P(c.ModelIndex, "setObjectEditor",
                        ("masterIndices", Csv(c.MasterIndices)),
                        ("objectIds",     IdCsv(c.ModelIndex, c.MasterIndices, c.ObjectIds)),
                        ("editorName",    c.EditorName ?? ""));
                    break;

                // ── 選択 ──────────────────────────────────────────────
                case SelectMeshCommand c:
                    P(c.ModelIndex, "selectMesh",
                        ("category", ((int)c.Category).ToString()),
                        ("indices",  Csv(c.Indices)));
                    break;

                // ── 属性変更 ──────────────────────────────────────────
                case ToggleVisibilityCommand c:
                    P(c.ModelIndex, "toggleVisibility",
                        ("masterIndex", c.MasterIndex.ToString()),
                        ("objectIds",   IdCsv(c.ModelIndex, new[] { c.MasterIndex }, null)));
                    break;

                case SetBatchVisibilityCommand c:
                    P(c.ModelIndex, "setBatchVisibility",
                        ("masterIndices", Csv(c.MasterIndices)),
                        ("objectIds",     IdCsv(c.ModelIndex, c.MasterIndices, null)),
                        ("visible",       c.Visible ? "true" : "false"));
                    break;

                case ToggleLockCommand c:
                    P(c.ModelIndex, "toggleLock",
                        ("masterIndex", c.MasterIndex.ToString()),
                        ("objectIds",   IdCsv(c.ModelIndex, new[] { c.MasterIndex }, null)));
                    break;

                case SetBatchLockCommand c:
                    P(c.ModelIndex, "setBatchLock",
                        ("masterIndices", Csv(c.MasterIndices)),
                        ("objectIds",     IdCsv(c.ModelIndex, c.MasterIndices, null)),
                        ("locked",        c.Locked ? "true" : "false"));
                    break;

                case CycleMirrorTypeCommand c:
                    P(c.ModelIndex, "cycleMirrorType",
                        ("masterIndex", c.MasterIndex.ToString()),
                        ("objectIds",   IdCsv(c.ModelIndex, new[] { c.MasterIndex }, null)));
                    break;

                case SetMirrorEnabledCommand c:
                    P(c.ModelIndex, "setMirrorEnabled",
                        ("masterIndices", Csv(c.MasterIndices)),
                        ("objectIds",     IdCsv(c.ModelIndex, c.MasterIndices, null)),
                        ("enabled",       c.Enabled ? "true" : "false"));
                    break;

                case SetBatchMirrorTypeCommand c:
                    P(c.ModelIndex, "setBatchMirrorType",
                        ("masterIndices", Csv(c.MasterIndices)),
                        ("objectIds",     IdCsv(c.ModelIndex, c.MasterIndices, null)),
                        ("mirrorType",    c.MirrorType.ToString()));
                    break;

                case RenameMeshCommand c:
                    P(c.ModelIndex, "renameMesh",
                        ("masterIndex", c.MasterIndex.ToString()),
                        ("objectIds",   IdCsv(c.ModelIndex, new[] { c.MasterIndex }, null)),
                        ("name",        c.NewName ?? ""));
                    break;

                // 名称一括変更。名前は CSV 1行にまとめるため、
                // カンマ・引用符・改行を含む名前をエスケープして送る。
                case RenameMeshesCommand c:
                    P(c.ModelIndex, "renameMeshes",
                        ("masterIndices", Csv(c.MasterIndices)),
                        ("objectIds",     IdCsv(c.ModelIndex, c.MasterIndices, null)),
                        ("names",         NameCsv(c.NewNames)));
                    break;

                // ── 選択辞書 ──────────────────────────────────────────
                case ApplySelectionDictionaryCommand c:
                    P(c.ModelIndex, "applySelectionDictionary",
                        ("setIndex",      c.SetIndex.ToString()),
                        ("addToExisting", c.AddToExisting ? "true" : "false"));
                    break;

                // ── リスト操作 ────────────────────────────────────────
                case AddMeshCommand c:
                    P(c.ModelIndex, "addMesh");
                    break;

                case DeleteMeshesCommand c:
                    P(c.ModelIndex, "deleteMeshes", ("masterIndices", Csv(c.MasterIndices)),
                        ("objectIds",     IdCsv(c.ModelIndex, c.MasterIndices, null)));
                    break;

                case DuplicateMeshesCommand c:
                    P(c.ModelIndex, "duplicateMeshes", ("masterIndices", Csv(c.MasterIndices)),
                        ("objectIds",     IdCsv(c.ModelIndex, c.MasterIndices, null)));
                    break;

                // ── BonePose ──────────────────────────────────────────
                case InitBonePoseCommand c:
                    P(c.ModelIndex, "initBonePose", ("masterIndices", Csv(c.MasterIndices)),
                        ("objectIds",     IdCsv(c.ModelIndex, c.MasterIndices, null)));
                    break;

                case SetBonePoseActiveCommand c:
                    P(c.ModelIndex, "setBonePoseActive",
                        ("masterIndices", Csv(c.MasterIndices)),
                        ("objectIds",     IdCsv(c.ModelIndex, c.MasterIndices, null)),
                        ("active",        c.Active ? "true" : "false"));
                    break;

                case ResetBonePoseLayersCommand c:
                    P(c.ModelIndex, "resetBonePoseLayers", ("masterIndices", Csv(c.MasterIndices)),
                        ("objectIds",     IdCsv(c.ModelIndex, c.MasterIndices, null)));
                    break;

                case BakePoseToBindPoseCommand c:
                    P(c.ModelIndex, "bakePoseToBindPose", ("masterIndices", Csv(c.MasterIndices)),
                        ("objectIds",     IdCsv(c.ModelIndex, c.MasterIndices, null)));
                    break;

                // ── モデル操作 ────────────────────────────────────────
                case SwitchModelCommand c:
                    P(c.ModelIndex, "switchModel", ("targetModelIndex", c.TargetModelIndex.ToString()));
                    break;

                case RenameModelCommand c:
                    P(c.ModelIndex, "renameModel", ("name", c.NewName ?? ""));
                    break;

                case DeleteModelCommand c:
                    P(c.ModelIndex, "deleteModel");
                    break;

                // サーバ未対応（morph変換/プレビュー、bone transform、material、folding 等）は無視
                default:
                    break;
            }
        }

        private void P(int modelIndex, string action, params (string key, string val)[] kv)
        {
            Dictionary<string, string> p = null;
            if (kv != null && kv.Length > 0)
            {
                p = new Dictionary<string, string>(kv.Length);
                foreach (var (key, val) in kv) p[key] = val;
            }
            _client.SendCommand(action, modelIndex, p);
        }

        /// <summary>
        /// masterIndices と同じ並びの ObjectId CSV を作る。
        /// explicitIds が与えられていればそれを優先し、無ければ ResolveObjectId で引く。
        /// 解決子が未設定なら空文字を返す（サーバ側は照合をスキップする）。
        /// </summary>
        private string IdCsv(int modelIndex, int[] masterIndices, ulong[] explicitIds)
        {
            if (explicitIds != null && explicitIds.Length > 0)
                return UlongCsv(explicitIds);
            if (ResolveObjectId == null || masterIndices == null || masterIndices.Length == 0)
                return "";

            var ids = new ulong[masterIndices.Length];
            for (int i = 0; i < masterIndices.Length; i++)
                ids[i] = ResolveObjectId(modelIndex, masterIndices[i]);
            return UlongCsv(ids);
        }

        private static string UlongCsv(ulong[] a)
        {
            if (a == null || a.Length == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(a[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 名前配列を CSV 1行にする。区切りと衝突する文字は
        /// MeshSelSetCsvHelper と同じ規則で二重引用符に包む。
        /// </summary>
        private static string NameCsv(string[] a)
        {
            if (a == null || a.Length == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(EscName(a[i]));
            }
            return sb.ToString();
        }

        private static string EscName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string Csv(int[] a)
        {
            if (a == null || a.Length == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(a[i]);
            }
            return sb.ToString();
        }
    }
}

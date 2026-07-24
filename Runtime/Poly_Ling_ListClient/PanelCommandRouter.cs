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

        public PanelCommandRouter(PolyLingPlayerClient client)
        {
            _client = client;
        }

        public void Send(PanelCommand cmd)
        {
            if (_client == null || cmd == null || !_client.IsConnected) return;

            switch (cmd)
            {
                // ── 選択 ──────────────────────────────────────────────
                case SelectMeshCommand c:
                    P(c.ModelIndex, "selectMesh",
                        ("category", ((int)c.Category).ToString()),
                        ("indices",  Csv(c.Indices)));
                    break;

                // ── 属性変更 ──────────────────────────────────────────
                case ToggleVisibilityCommand c:
                    P(c.ModelIndex, "toggleVisibility", ("masterIndex", c.MasterIndex.ToString()));
                    break;

                case SetBatchVisibilityCommand c:
                    P(c.ModelIndex, "setBatchVisibility",
                        ("masterIndices", Csv(c.MasterIndices)),
                        ("visible",       c.Visible ? "true" : "false"));
                    break;

                case ToggleLockCommand c:
                    P(c.ModelIndex, "toggleLock", ("masterIndex", c.MasterIndex.ToString()));
                    break;

                case CycleMirrorTypeCommand c:
                    P(c.ModelIndex, "cycleMirrorType", ("masterIndex", c.MasterIndex.ToString()));
                    break;

                case RenameMeshCommand c:
                    P(c.ModelIndex, "renameMesh",
                        ("masterIndex", c.MasterIndex.ToString()),
                        ("name",        c.NewName ?? ""));
                    break;

                // ── リスト操作 ────────────────────────────────────────
                case AddMeshCommand c:
                    P(c.ModelIndex, "addMesh");
                    break;

                case DeleteMeshesCommand c:
                    P(c.ModelIndex, "deleteMeshes", ("masterIndices", Csv(c.MasterIndices)));
                    break;

                case DuplicateMeshesCommand c:
                    P(c.ModelIndex, "duplicateMeshes", ("masterIndices", Csv(c.MasterIndices)));
                    break;

                // ── BonePose ──────────────────────────────────────────
                case InitBonePoseCommand c:
                    P(c.ModelIndex, "initBonePose", ("masterIndices", Csv(c.MasterIndices)));
                    break;

                case SetBonePoseActiveCommand c:
                    P(c.ModelIndex, "setBonePoseActive",
                        ("masterIndices", Csv(c.MasterIndices)),
                        ("active",        c.Active ? "true" : "false"));
                    break;

                case ResetBonePoseLayersCommand c:
                    P(c.ModelIndex, "resetBonePoseLayers", ("masterIndices", Csv(c.MasterIndices)));
                    break;

                case BakePoseToBindPoseCommand c:
                    P(c.ModelIndex, "bakePoseToBindPose", ("masterIndices", Csv(c.MasterIndices)));
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

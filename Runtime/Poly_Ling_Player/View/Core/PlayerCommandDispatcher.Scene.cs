// PlayerCommandDispatcher.Scene.cs
// 利用シーンのコマンド（PanelCommand.Scene.cs）の振り分けと実処理。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 手本（PlayerCommandDispatcher.Scenario.cs）と同じ扱い。
//   ・SceneLibrary はプロジェクトにもモデルにも属さないので、プロジェクトの null 門より前で捌く
//   ・Viewer 側に実体が無いので受け口のフックを通さず、ここで直に処理して ReportData する
//   ・記録の対象外

using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>利用シーンのコマンドなら処理して true を返す。</summary>
        private bool DispatchScene(PanelCommand cmd)
        {
            switch (cmd)
            {
                case QueryRevisionsCommand c: RunQueryRevisions(c); return true;
                case QueryScenesCommand c: RunQueryScenes(c); return true;
                case SetSceneCommand c:    RunSetScene(c);    return true;
                case DeleteSceneCommand c: RunDeleteScene(c); return true;
                default: return false;
            }
        }

        private void RunQueryRevisions(QueryRevisionsCommand cmd)
        {
            var project = _getProject();
            var model   = project?.CurrentModel;

            ReportData(CommandDataJson.New()
                .Int ("modelIndex",       project?.CurrentModelIndex ?? 0)
                .Int ("modelRevision",    model?.Revision ?? 0)
                .Int ("models",           project?.ModelCount ?? 0)
                .Text("schemaRevision",   PanelCommandFactory.ToolIndexRevision)
                .Int ("scenarioRevision", ScenarioLibrary.Revision)
                .Int ("sceneRevision",    SceneLibrary.Revision)
                .Build());
        }

        private void RunQueryScenes(QueryScenesCommand cmd)
        {
            if (cmd.Reload) SceneLibrary.Reload();

            var all          = SceneLibrary.GetAll();
            var names        = new List<string>(all.Count);
            var descriptions = new List<string>(all.Count);
            var commands     = new List<string>(all.Count);
            var categories   = new List<string>(all.Count);
            var tags         = new List<string>(all.Count);
            var tools        = new List<string>(all.Count);

            foreach (var s in all)
            {
                names.Add(s.Name);
                descriptions.Add(s.Description);
                commands.Add(string.Join(";", s.Commands));
                categories.Add(string.Join(";", s.Categories));
                tags.Add(string.Join(";", s.Tags));
                tools.Add(string.Join(";", s.Tools));
            }

            ReportData(CommandDataJson.New()
                .Int  ("count",        all.Count)
                .Text ("storePath",    SceneLibrary.StorePath)
                .Texts("names",        names)
                .Texts("descriptions", descriptions)
                .Texts("commands",     commands)
                .Texts("categories",   categories)
                .Texts("tags",         tags)
                .Texts("tools",        tools)
                .Build());
        }

        private void RunSetScene(SetSceneCommand cmd)
        {
            if (!SceneLibrary.Register(cmd.ToDefinition(), cmd.Overwrite, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",      cmd.Name.Trim())
                .Int ("count",     SceneLibrary.Count)
                .Text("storePath", SceneLibrary.StorePath)
                .Build());
        }

        private void RunDeleteScene(DeleteSceneCommand cmd)
        {
            if (!SceneLibrary.Remove(cmd.Name, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Flag("removed", true)
                .Int ("count",   SceneLibrary.Count)
                .Build());
        }
    }
}

// PlayerCommandDispatcher.Scene.cs
// 利用シーンのコマンド（PanelCommand.Scene.cs）と、版・モデル状態の照会の振り分けと実処理。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 手本（PlayerCommandDispatcher.Scenario.cs）と同じ扱い。
//   ・SceneLibrary はプロジェクトにもモデルにも属さないので、プロジェクトの null 門より前で捌く
//   ・Viewer 側に実体が無いので受け口のフックを通さず、ここで直に処理して ReportData する
//   ・記録の対象外

using System;
using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// Viewer 側が持つ一時的な状態（プレビュー中など）を返す受け口。
        /// PolyLingPlayerViewerCore.ActiveModes.cs が配線する。未配線なら Viewer 側の状態は返さない。
        /// </summary>
        public Func<IReadOnlyList<string>> OnQueryActiveModes;

        /// <summary>利用シーンのコマンドなら処理して true を返す。</summary>
        private bool DispatchScene(PanelCommand cmd)
        {
            switch (cmd)
            {
                case QueryRevisionsCommand c: RunQueryRevisions(c); return true;
                case QueryModelStateCommand c: RunQueryModelState(c); return true;
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

            // 一時的な状態。ディスパッチャ自身が持つものと、Viewer 側から受け取るもの。
            var modes = new List<string>();
            if (ScenarioRecorder.IsRecording) modes.Add("scenarioRecording");
            if (_scenarioRun != null)         modes.Add("scenarioRun");
            var fromViewer = OnQueryActiveModes?.Invoke();
            if (fromViewer != null) modes.AddRange(fromViewer);

            ReportData(CommandDataJson.New()
                .Texts("activeModes",     modes)
                .Int ("modelIndex",       project?.CurrentModelIndex ?? 0)
                .Int ("modelRevision",    model?.Revision ?? 0)
                .Int ("models",           project?.ModelCount ?? 0)
                .Text("schemaRevision",   PanelCommandFactory.ToolIndexRevision)
                .Int ("scenarioRevision", ScenarioLibrary.Revision)
                .Int ("sceneRevision",    SceneLibrary.Revision)
                .Build());
        }

        /// <summary>
        /// カレントモデルの状態を取り出す。queryModelState と、Editor 側の検索（PolyLingCommandGateway.ModelState）が使う。
        /// </summary>
        public ModelStateSnapshot CaptureModelState() => ModelStateSnapshot.Capture(_getProject()?.CurrentModel);

        private void RunQueryModelState(QueryModelStateCommand cmd)
        {
            var st = CaptureModelState();
            ReportData(CommandDataJson.New()
                .Flag("hasModel",          st.HasModel)
                .Flag("hasBones",          st.HasBones)
                .Flag("hasSkinWeights",    st.HasSkinWeights)
                .Flag("hasMorphs",         st.HasMorphs)
                .Flag("hasHumanoid",       st.HasHumanoid)
                .Flag("humanoidComplete",  st.HumanoidComplete)
                .Flag("hasCustomRig",      st.HasCustomRig)
                .Flag("hasSpringBones",    st.HasSpringBones)
                .Flag("hasMirrorRelation", st.HasMirrorRelation)
                .Flag("topologyLocked",    st.TopologyLocked)
                .Build());
        }

        private void RunQueryScenes(QueryScenesCommand cmd)
        {
            if (cmd.Reload) SceneLibrary.Reload();

            var all = SceneLibrary.GetAll();
            var names    = new List<string>(all.Count);
            var descs    = new List<string>(all.Count);
            var origins  = new List<string>(all.Count);
            var explicit_ = new List<string>(all.Count);
            var cats     = new List<string>(all.Count);
            var tags     = new List<string>(all.Count);
            var tools    = new List<string>(all.Count);
            var excludes = new List<string>(all.Count);
            var states   = new List<string>(all.Count);
            var hazards  = new List<string>(all.Count);
            var verifies = new List<string>(all.Count);
            var related  = new List<string>(all.Count);
            var notes    = new List<string>(all.Count);

            foreach (var s in all)
            {
                names.Add(s.Name);
                descs.Add(s.Description);
                origins.Add(SceneLibrary.OriginOf(s.Name));
                explicit_.Add(string.Join(";", s.ExplicitCommands));
                cats.Add(string.Join(";", s.IncludeCategories));
                tags.Add(string.Join(";", s.BoostTags));
                tools.Add(string.Join(";", s.Tools));
                excludes.Add(string.Join(";", s.ExcludeCommands));
                states.Add(string.Join(";", s.StateAssumptionItems()));
                hazards.Add(string.Join(";", s.HazardPolicyItems()));
                verifies.Add(string.Join(";", s.VerificationItems()));
                related.Add(string.Join(";", s.RelatedScenarios));
                notes.Add(s.Notes);
            }

            ReportData(CommandDataJson.New()
                .Int  ("count",              all.Count)
                .Text ("storePath",          SceneLibrary.StorePath)
                .Texts("names",              names)
                .Texts("descriptions",       descs)
                .Texts("origins",            origins)
                .Texts("explicitCommands",   explicit_)
                .Texts("includeCategories",  cats)
                .Texts("boostTags",          tags)
                .Texts("tools",              tools)
                .Texts("excludeCommands",    excludes)
                .Texts("stateAssumptions",   states)
                .Texts("hazardPolicy",       hazards)
                .Texts("verificationPolicy", verifies)
                .Texts("relatedScenarios",   related)
                .Texts("notes",              notes)
                .Build());
        }

        private void RunSetScene(SetSceneCommand cmd)
        {
            // categories は正典の分類か、その上位の区切りだけを受ける。
            // 綴りを誤ると候補が 0 件になり、気付かないまま絞り込みが効かなくなるため。
            var unknown = new List<string>();
            foreach (var c in cmd.IncludeCategories)
                if (!string.IsNullOrWhiteSpace(c) && !PLCommandCategories.IsValidRequest(c.Trim())) unknown.Add(c.Trim());
            if (unknown.Count > 0)
            {
                Fail($"正典（PLCommandCategories）に無い分類: {string.Join(", ", unknown)}");
                return;
            }

            var def = cmd.ToDefinition(out string parseError);
            if (def == null) { Fail(parseError); return; }

            if (!SceneLibrary.Register(def, cmd.Overwrite, out string error)) { Fail(error); return; }

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

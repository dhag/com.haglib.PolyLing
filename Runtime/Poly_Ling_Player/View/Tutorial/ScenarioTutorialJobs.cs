// ScenarioTutorialJobs.cs
// チュートリアルの作成の受付簿。buildId ごとに進行と結果を持つ。
// Runtime/Poly_Ling_Player/View/Tutorial/ に配置
//
// 【なぜ要るか】
//   作成はシナリオの実行と撮影を含み、MCP の応答待ちを超える。UiAutomationCaptureJobs と同じく、
//   受付番号を返し、状態は queryScenarioTutorialBuild で引かせる。
//
// 【BASE64 を持たない】
//   応答に載せないだけでなく、受付簿にも持たない。パス・枚数・バイト数だけを持つ。
//
// 【件数の上限】
//   取りに来られない受付が溜まり続けないよう、MaxJobs を超えたら古い順に捨てる。

using System;
using System.Collections.Generic;

namespace Poly_Ling.Player
{
    public sealed class ScenarioTutorialJobs
    {
        public enum JobStatus { Pending, Completed, Failed }

        public sealed class Job
        {
            public string    Id = "";
            public JobStatus Status = JobStatus.Pending;
            public string    Stage = "running";
            public string    CurrentItem = "";
            public int       ShotsDone;
            public int       ShotsTotal;
            public string    OutPath = "";
            public string    ImagesFolder = "";
            public readonly List<string> ImagePaths = new List<string>();
            /// <summary>作成は続けたが知らせること（入力欄へ値を入れられなかった欄など）。</summary>
            public readonly List<string> Warnings = new List<string>();
            public int       ImageCount;
            public long      ByteCount;
            public string    FailedItem = "";
            public string    Error = "";
        }

        private const int MaxJobs = 32;

        private readonly Dictionary<string, Job> _jobs  = new Dictionary<string, Job>(StringComparer.Ordinal);
        private readonly Queue<string>           _order = new Queue<string>();

        public Job Start()
        {
            var job = new Job { Id = Guid.NewGuid().ToString("N") };
            _jobs.Add(job.Id, job);
            _order.Enqueue(job.Id);

            while (_order.Count > MaxJobs)
                _jobs.Remove(_order.Dequeue());

            return job;
        }

        public bool TryGet(string id, out Job job)
        {
            job = null;
            return !string.IsNullOrEmpty(id) && _jobs.TryGetValue(id, out job);
        }

        public static void Fail(Job job, string item, string reason)
        {
            if (job == null || job.Status != JobStatus.Pending) return;
            job.Status     = JobStatus.Failed;
            job.Stage      = "done";
            job.FailedItem = item ?? "";
            job.Error      = string.IsNullOrEmpty(reason) ? "理由が取れませんでした" : reason;
        }

        public static string StatusText(JobStatus s)
        {
            switch (s)
            {
                case JobStatus.Completed: return "completed";
                case JobStatus.Failed:    return "failed";
                default:                  return "pending";
            }
        }
    }
}

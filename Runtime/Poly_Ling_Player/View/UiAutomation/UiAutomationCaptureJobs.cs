// UiAutomationCaptureJobs.cs
// UI 自動操作の画面キャプチャの受付簿。captureId ごとに完了状態を持つ。
// Runtime/Poly_Ling_Player/View/UiAutomation/ に配置
//
// 【なぜ要るか】
//   撮影は PlayerScreenCapture.Capture がフレーム終端（PlayerCaptureRunner の
//   WaitForEndOfFrame）で行う。MCP の応答は同期で返るため、uiCapture の応答で
//   PNG のパスは返せない。受付番号を返し、完了は uiCaptureStatus で引かせる。
//
// 【件数の上限】
//   取りに来られない受付が溜まり続けないよう、MaxJobs を超えたら古い順に捨てる。

using System;
using System.Collections.Generic;

namespace Poly_Ling.Player
{
    public sealed class UiAutomationCaptureJobs
    {
        public enum JobStatus { Pending, Completed, Failed }

        public sealed class Job
        {
            public string    Id;
            public JobStatus Status;
            public string    Path;
            public string    Error;
        }

        private const int MaxJobs = 100;

        private readonly Dictionary<string, Job> _jobs = new Dictionary<string, Job>(StringComparer.Ordinal);
        private readonly Queue<string>           _order = new Queue<string>();

        /// <summary>受付を 1 件作る。状態は Pending。</summary>
        public Job Start()
        {
            var job = new Job { Id = Guid.NewGuid().ToString("N"), Status = JobStatus.Pending };
            _jobs.Add(job.Id, job);
            _order.Enqueue(job.Id);

            while (_order.Count > MaxJobs)
                _jobs.Remove(_order.Dequeue());

            return job;
        }

        /// <summary>撮影結果を書く。ok なら message は保存パス、失敗なら理由。</summary>
        public void Complete(string id, bool ok, string message)
        {
            if (string.IsNullOrEmpty(id) || !_jobs.TryGetValue(id, out var job)) return;
            if (ok)
            {
                job.Status = JobStatus.Completed;
                job.Path   = message;
            }
            else
            {
                job.Status = JobStatus.Failed;
                job.Error  = string.IsNullOrEmpty(message) ? "capture failed" : message;
            }
        }

        public bool TryGet(string id, out Job job)
        {
            job = null;
            return !string.IsNullOrEmpty(id) && _jobs.TryGetValue(id, out job);
        }

        /// <summary>応答に載せる状態の文字列。</summary>
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

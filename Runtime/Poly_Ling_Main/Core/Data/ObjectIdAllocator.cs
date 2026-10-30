// ObjectIdAllocator.cs
// MeshContext.ObjectId（位置非依存の安定オブジェクトID）の発行と整合。
//
// 【設計方針】
// - ID は「サーバ（＝ProjectContext を保持する側）でのみ」発行される。
//   クライアントはコマンドを送るだけで ProjectContext を書き換えないため、
//   単調増加カウンタで衝突なく足りる。
// - 起動時刻(UTC Ticks)を初期値にする。別セッションで保存された
//   プロジェクトを読み込んだ場合も Observe() で追い越すため衝突しない。
// - 0 は「未割当」を意味する予約値。
//
// 【割当タイミング】（作り直し整備計画 段 C）
//   ModelContext の一覧へ入れる道（Add・Insert・ReplaceMeshContexts・SetMeshContextAt）が
//   ModelContext.Admit で未割当（0）のものにだけ Next() を振る。振るのはそこ 1 か所。
//   既に 0 以外が入っているものは触らない（＝IDは一度決まったら不変）。
//   読み込みは保存の ID を入れて Observe し、ResolveDuplicates で重複と 0 を振り直す。
//   リモートの受信側は振らない（送り手の ID をそのまま持つ）。
//   一覧の点検は ModelContext.CheckObjectIds（書き込みのあるコマンドの終わりに Dispatch が呼ぶ）。

using System;
using System.Collections.Generic;
using System.Threading;

namespace Poly_Ling.Data
{
    /// <summary>安定オブジェクトIDの発行器（プロセス内グローバル）。</summary>
    public static class ObjectIdAllocator
    {
        /// <summary>未割当を示す予約値。</summary>
        public const ulong Unassigned = 0UL;

        // Interlocked を使うため long で保持する（値域は十分）。
        private static long _next = DateTime.UtcNow.Ticks;

        /// <summary>新しいIDを1つ発行する。</summary>
        public static ulong Next() => (ulong)Interlocked.Increment(ref _next);

        /// <summary>
        /// 既存IDを観測してカウンタを追い越させる。
        /// 保存済みプロジェクトの読み込み時に、読み取った全IDへ適用する。
        /// </summary>
        public static void Observe(ulong id)
        {
            if (id == Unassigned) return;
            long v = unchecked((long)id);
            while (true)
            {
                long cur = Interlocked.Read(ref _next);
                if (cur >= v) return;
                if (Interlocked.CompareExchange(ref _next, v, cur) == cur) return;
            }
        }

        /// <summary>
        /// 重複IDを検出して後勝ちで振り直す。
        /// 旧形式ファイルの取り込みや、外部生成データの混入に対する保険。
        /// </summary>
        /// <returns>振り直した件数</returns>
        public static int ResolveDuplicates(IReadOnlyList<MeshContext> contexts)
        {
            if (contexts == null) return 0;
            var seen = new HashSet<ulong>();
            int fixedCount = 0;
            for (int i = 0; i < contexts.Count; i++)
            {
                var mc = contexts[i];
                if (mc == null) continue;
                if (mc.ObjectId == Unassigned || !seen.Add(mc.ObjectId))
                {
                    mc.ObjectId = Next();
                    seen.Add(mc.ObjectId);
                    fixedCount++;
                }
            }
            return fixedCount;
        }

        /// <summary>ObjectId から MeshContext のリスト内位置を引く（見つからなければ -1）。</summary>
        public static int IndexOfId(IReadOnlyList<MeshContext> contexts, ulong objectId)
        {
            if (contexts == null || objectId == Unassigned) return -1;
            for (int i = 0; i < contexts.Count; i++)
                if (contexts[i] != null && contexts[i].ObjectId == objectId) return i;
            return -1;
        }
    }
}

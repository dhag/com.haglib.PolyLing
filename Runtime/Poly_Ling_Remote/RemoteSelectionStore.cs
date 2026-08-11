// Remote/RemoteSelectionStore.cs
// 協働編集: ユーザーごとの選択状態と、コマンド実行時の一時差し替え（案B）。
//
// 【背景】
// 選択（どのメッシュ/ボーンを選んでいるか）は本来ユーザーごとの作業状態だが、
// 実体は共有の ModelContext.Selected*Indices 1組しかない。
// これを全員で共有すると、A が選択した瞬間 B の選択も飛ばされてしまい、
// オブジェクト単位で担当を分けても同時作業ができない。
//
// 【方針（案B: 選択スコープの一時差し替え）】
// PanelCommand の多く（PartsSet 系・SkinWeight 系・Morph 系など約48種）は
// MasterIndex を持たず「今の選択」を見て動く。これらすべてに明示ターゲットを
// 持たせる改修は広範なので、代わりに
//
//   1. 選択は共有 ModelContext ではなく、ユーザーごとのスロットに保持する
//   2. コマンド実行の直前だけ、そのユーザーの選択を ModelContext へ流し込む
//   3. 実行後にホストの選択へ戻す
//
// とする。差し替えは using スコープ1つで済み、今後追加されるコマンドも
// 自動的に正しい選択を見る。
//
// 【注意】
// 差し替え中に実行されるコマンドは NotifyPanels を呼ぶため、
// ホストのUIが一時的に他ユーザーの選択で描画されうる。
// 復帰後に RemoteServerCore.RequestPanelRefresh を呼んでホストUIを再同期する。

using System;
using System.Collections.Generic;
using System.Text;
using Poly_Ling.Context;

namespace Poly_Ling.Remote
{
    // ================================================================
    // ユーザー1人分の選択状態
    // ================================================================

    /// <summary>
    /// ユーザーごとの選択スナップショット。
    /// ModelContext から取り出せる選択情報のうち、作業対象を決めるものだけを持つ。
    /// 頂点/辺/面の要素選択（MeshContext.Selection）は含めない
    /// （所有権により1オブジェクト＝1編集者が保証されるため、共有のままで衝突しない）。
    /// </summary>
    public sealed class UserSelection
    {
        public int  ModelIndex = 0;
        public ModelContext.SelectionCategory Category = ModelContext.SelectionCategory.Mesh;

        public List<int> Drawable = new List<int>();
        public List<int> Bone     = new List<int>();
        public List<int> Morph    = new List<int>();

        /// <summary>現在の ModelContext の選択を写し取る。</summary>
        public static UserSelection Capture(ModelContext model, int modelIndex)
        {
            var s = new UserSelection { ModelIndex = modelIndex };
            if (model == null) return s;

            s.Category = model.ActiveCategory;
            s.Drawable = new List<int>(model.SelectedDrawableMeshIndices ?? new List<int>());
            s.Bone     = new List<int>(model.SelectedBoneIndices         ?? new List<int>());
            s.Morph    = new List<int>(model.SelectedMorphIndices        ?? new List<int>());
            return s;
        }

        /// <summary>この選択を ModelContext へ流し込む（通知は発生しない）。</summary>
        public void ApplyTo(ModelContext model)
        {
            if (model == null) return;

            // 範囲外インデックスは落とす。
            // 他ユーザーがメッシュを削除した後の古い選択が紛れ込みうるため。
            int count = model.MeshContextCount;
            model.SelectedDrawableMeshIndices = Filter(Drawable, count);
            model.SelectedBoneIndices         = Filter(Bone,     count);
            model.SelectedMorphIndices        = Filter(Morph,    count);
            model.SetActiveCategory(Category);
        }

        private static List<int> Filter(List<int> src, int count)
        {
            var dst = new List<int>(src?.Count ?? 0);
            if (src == null) return dst;
            foreach (int i in src)
                if (i >= 0 && i < count) dst.Add(i);
            return dst;
        }

        public UserSelection Clone() => new UserSelection
        {
            ModelIndex = ModelIndex,
            Category   = Category,
            Drawable   = new List<int>(Drawable),
            Bone       = new List<int>(Bone),
            Morph      = new List<int>(Morph),
        };

        /// <summary>差分検出用シグネチャ。</summary>
        public string Signature()
        {
            var sb = new StringBuilder();
            sb.Append(ModelIndex).Append('|').Append((int)Category).Append('|');
            Append(sb, Drawable); sb.Append('|');
            Append(sb, Bone);     sb.Append('|');
            Append(sb, Morph);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, List<int> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(list[i]);
            }
        }

        public static string Csv(List<int> list)
        {
            if (list == null || list.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(list[i]);
            }
            return sb.ToString();
        }

        public static List<int> ParseCsv(string csv)
        {
            var list = new List<int>();
            if (string.IsNullOrEmpty(csv)) return list;
            foreach (var part in csv.Split(','))
                if (int.TryParse(part.Trim(), out int n)) list.Add(n);
            return list;
        }
    }

    // ================================================================
    // ユーザー名 → 選択 の保管庫
    // ================================================================

    /// <summary>
    /// ユーザーごとの選択スロット。
    /// キーはユーザー名なので、同一ユーザーが複数パネル（meshList / modelList /
    /// materialList）を開いていても選択が共有される（＝同じ人の画面は連動する）。
    /// </summary>
    public sealed class RemoteSelectionStore
    {
        private readonly Dictionary<string, UserSelection> _slots
            = new Dictionary<string, UserSelection>(StringComparer.Ordinal);

        /// <summary>スロットを取得する。無ければ null。</summary>
        public UserSelection Find(string userName)
        {
            if (string.IsNullOrEmpty(userName)) return null;
            return _slots.TryGetValue(userName, out var s) ? s : null;
        }

        /// <summary>
        /// スロットを取得する。無ければ fallback を複製して作る。
        /// 新規接続ユーザーがホストと同じ位置から作業を始められるようにするため、
        /// fallback には接続時点のモデル選択を渡す。
        /// </summary>
        public UserSelection GetOrCreate(string userName, UserSelection fallback = null)
        {
            if (string.IsNullOrEmpty(userName)) return null;
            if (_slots.TryGetValue(userName, out var s)) return s;

            s = fallback?.Clone() ?? new UserSelection();
            _slots[userName] = s;
            return s;
        }

        public void Set(string userName, UserSelection selection)
        {
            if (string.IsNullOrEmpty(userName) || selection == null) return;
            _slots[userName] = selection;
        }

        public void Remove(string userName)
        {
            if (string.IsNullOrEmpty(userName)) return;
            _slots.Remove(userName);
        }

        public IEnumerable<KeyValuePair<string, UserSelection>> All => _slots;

        public int Count => _slots.Count;

        public void Clear() => _slots.Clear();
    }

    // ================================================================
    // 選択スコープ（一時差し替え）
    // ================================================================

    /// <summary>
    /// using ブロックの間だけ ModelContext の選択を差し替える。
    /// Dispose で必ず元へ戻す（例外時も戻る）。
    ///
    ///   using (SelectionScope.Apply(model, modelIndex, userSelection))
    ///   {
    ///       DispatchCommand(cmd);   // このコマンドは userSelection を見る
    ///   }
    ///   // ここでホストの選択に戻っている
    /// </summary>
    public sealed class SelectionScope : IDisposable
    {
        private readonly ModelContext  _model;
        private readonly UserSelection _saved;
        private bool _disposed;

        /// <summary>差し替えが実際に行われたか（同一内容なら false）。</summary>
        public bool Swapped { get; private set; }

        private SelectionScope(ModelContext model, UserSelection saved, bool swapped)
        {
            _model   = model;
            _saved   = saved;
            Swapped  = swapped;
        }

        /// <summary>
        /// target の選択を model へ適用する。
        /// model / target が null、または内容が同一なら何もしない no-op スコープを返す。
        /// </summary>
        public static SelectionScope Apply(ModelContext model, int modelIndex, UserSelection target)
        {
            if (model == null || target == null)
                return new SelectionScope(null, null, false);

            var current = UserSelection.Capture(model, modelIndex);

            // 内容が同じなら差し替え不要（ホスト＝要求者の場合など）
            if (current.Signature() == target.Signature())
                return new SelectionScope(null, null, false);

            target.ApplyTo(model);
            return new SelectionScope(model, current, true);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!Swapped || _model == null || _saved == null) return;
            _saved.ApplyTo(_model);
        }
    }
}

// UiControlAttribute.cs
// UI 自動操作で操作できる項目の宣言。サブパネルの VisualElement 型のフィールド・
// プロパティに付ける。UiAutomationRegistry.RegisterObject が集めて登録する。
// Runtime/Poly_Ling_Player/View/UiAutomation/ に配置
//
// 【PLParam と同じ考え方】
//   付いているものだけを外へ出す。出さないものにも Ignore = true を明示して付ける。
//   属性が無い＝付け忘れ、として queryUiAutomationAudit が数える。
//
// 【ID】
//   ここに書くのはパネル内の ID（例 "strength"、"lock.x"）。登録時に
//   "<パネル ID>." を前に付ける。フィールド名から作らないので、
//   フィールド名を変えても ID は変わらない。英字の camelCase を . で区切る。
//
// 【メソッド名で渡すもの】
//   Reveal / Getter / Setter は同じクラスのインスタンスメソッドの名前（nameof で書く）。
//     Reveal … bool M()        表示の下準備。表示を変えたら true、変えなければ false
//     Getter … string M()      既定の読み取り（UI 型ごとの value）で足りないとき
//     Setter … string M(string) 既定の書き込みで足りないとき。成功で null、失敗で理由
//   名前が見つからない・形が合わないときは登録を拒否し、検査の登録失敗に数える。

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>項目を外から操作してよい度合い。</summary>
    public enum UiSafety
    {
        /// <summary>未指定。値の項目は SafeWrite として扱う。ボタンは押さない（検査で数える）。</summary>
        Unspecified = 0,

        /// <summary>読むだけ。値の変更もボタンの押下もしない。</summary>
        ReadOnly = 1,

        /// <summary>
        /// UI 状態の変更、または通常の編集操作（移動・分割・結合など）。
        /// 削除・消去・初期化は Destructive にする。
        /// </summary>
        SafeWrite = 2,

        /// <summary>削除・消去・初期化、または Undo で戻せない変更。allowDestructive が要る。</summary>
        Destructive = 3,

        /// <summary>ファイルの読み書き。allowDestructive が要る。</summary>
        FileOperation = 4,

        /// <summary>
        /// 利用者の操作が要る（ファイルダイアログを開く等）。自動では押さない・変えない。
        /// 強調表示と値の読み取りはできる。
        /// </summary>
        UserOnly = 5,
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class UiControlAttribute : Attribute
    {
        /// <summary>パネル内の ID。登録時に "&lt;パネル ID&gt;." が前に付く。</summary>
        public string Id { get; }

        /// <summary>uiDescribe に出す説明。</summary>
        public string Description { get; set; } = "";

        /// <summary>操作してよい度合い。</summary>
        public UiSafety Safety { get; set; } = UiSafety.Unspecified;

        /// <summary>外へ出さない。付け忘れと区別するために明示する。</summary>
        public bool Ignore { get; set; }

        /// <summary>
        /// Ignore と一緒に使う。このコンテナの中身はデータに合わせて作り直す行
        /// （一覧の各行のボタン・チェックなど）で、固定の ID を付けられない。
        /// 検査（未登録の部品）はこのコンテナの中を見ない。
        /// </summary>
        public bool Rows { get; set; }

        /// <summary>表示の下準備をするメソッド名（bool M()）。</summary>
        public string Reveal { get; set; } = "";

        /// <summary>読み取りメソッド名（string M()）。</summary>
        public string Getter { get; set; } = "";

        /// <summary>書き込みメソッド名（string M(string)。成功で null）。</summary>
        public string Setter { get; set; } = "";

        public UiControlAttribute(string id)
        {
            Id = id ?? "";
        }

        /// <summary>Ignore 用。ID は持たない。</summary>
        public UiControlAttribute()
        {
            Id = "";
        }
    }

    /// <summary>
    /// UI 部品をまとめて持つ補助オブジェクト（TempMirrorControls など）のフィールドに付ける。
    /// RegisterObject はこのフィールドの中身も同じパネルの項目として取り込み、
    /// ID を "&lt;パネル ID&gt;.&lt;Prefix&gt;.&lt;中の Id&gt;" にする。
    /// 検査（付け忘れ）も中身まで見る。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class UiNestedAttribute : Attribute
    {
        public string Prefix { get; }

        /// <summary>
        /// 中身を表示する下準備のメソッド名（外側のクラスの bool M()）。
        /// 中身の各項目の Reveal より先に呼ぶ。表示を変えたら true。
        /// </summary>
        public string Reveal { get; set; } = "";

        /// <summary>
        /// 登録時に null でもよい（同じクラスの別インスタンスでは作らない、など）。
        /// false のとき null は登録失敗として数える。
        /// </summary>
        public bool Optional { get; set; }

        public UiNestedAttribute(string prefix)
        {
            Prefix = prefix ?? "";
        }
    }

    /// <summary>
    /// 作り直すたびに中身が変わる項目（モードごとの設定行など）の置き場。
    /// サブパネルがフィールドに持ち、作り直すときに Begin してから、行を作る補助関数が Add する。
    /// RegisterObject はこの型のフィールドを見つけると「動的な項目の出どころ」として登録簿へ渡し、
    /// 項目の検索・一覧・検査はその時点の中身を使う。
    ///
    /// 【組（Group）】1 つのセクションをモードで切り替えるパネル（書き出しの PMX / MQO など）は、
    /// モードごとに別のパネル ID で登録し、パネルごとにどの組を見るかを決める。
    /// Begin(group) で今回作る行の組を決める。
    ///
    /// 【ID】パネル ID の後ろに付く部分（例 "flipX"）。表示名や位置は使わない。
    /// </summary>
    public sealed class UiDynamicControls
    {
        public sealed class Entry
        {
            public string        Group;
            public string        Id;
            public VisualElement Element;
            public string        Description;
            public UiSafety      Safety;
            /// <summary>表示の下準備（UiControl の Reveal と同じ）。無ければ null。</summary>
            public Func<bool>    Reveal;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<VisualElement> _rows = new List<VisualElement>();
        private string _group = "";

        /// <summary>今ある項目。</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>今あるデータ行のコンテナ（固定の ID を付けられない行の入れ物。検査はこの中を見ない）。</summary>
        public IReadOnlyList<VisualElement> Rows => _rows;

        /// <summary>作り直しの始めに呼ぶ。前回の項目を捨て、今回の組を決める。</summary>
        public void Begin(string group)
        {
            _entries.Clear();
            _rows.Clear();
            _group = group ?? "";
        }

        /// <summary>データ行のコンテナを加える（UiControl の Rows と同じ意味）。作った要素をそのまま返す。</summary>
        public T AddRows<T>(T container) where T : VisualElement
        {
            if (container != null) _rows.Add(container);
            return container;
        }

        /// <summary>項目を加える。作った要素をそのまま返すので、生成式に挟んで使える。</summary>
        public T Add<T>(string id, T element, string description, UiSafety safety = UiSafety.Unspecified,
                        Func<bool> reveal = null)
            where T : VisualElement
        {
            if (element != null && !string.IsNullOrEmpty(id))
            {
                _entries.Add(new Entry
                {
                    Group       = _group,
                    Id          = id,
                    Element     = element,
                    Description = description ?? "",
                    Safety      = safety,
                    Reveal      = reveal,
                });
            }
            return element;
        }

        /// <summary>ID（と組）で探す。group が null なら組を問わない。</summary>
        public Entry Find(string id, string group)
        {
            foreach (var e in _entries)
                if (e.Id == id && (group == null || e.Group == group)) return e;
            return null;
        }
    }
}

// MotionClipHandler.Edit.cs
// 窓口 motionClip の編集の口（PolyLing_モーション編集構造計画.md 段 3）。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// 【流れ】口はどれも、今のクリップの写しに MotionEditOps.Run で操作を当て、成功したらそれを今のクリップにして
//   対応付けをやり直し、time 秒のフレームを当て直す。失敗したらクリップは変えず、Error に理由が入る。
//   edit の無いクリップは、最初の口で Studio の読込と同じ形（トラックごとにクリップ 1 本）の edit を作る。
// 【取り消し】口 1 回で、変える前と後の DTO（MotionClipSerializer.ToJson）を持つ記録を 1 件、
//   MeshUndoController のサブウインドウスタック "PlayerEdit/MotionClipEdit" に積む。既存の Undo / Redo で戻る。
//   読込・Clear・EditClip（createLipSyncFromVoicevox）で世代を進め、世代の違う記録の Undo / Redo は何もしない
//   （別のクリップを書き戻さない）。
// 【意味】Motion Studio の操作にそろえる（MotionEditOps の先頭のコメント）。

using System;
using Poly_Ling.Data;
using Poly_Ling.Motion;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public sealed partial class MotionClipHandler
    {
        /// <summary>取り消しの記録を積む先（ビューアが配線する）。無ければ記録しない。</summary>
        public Func<MeshUndoController> GetUndoController;

        /// <summary>編集の口・その取り消しでクリップが変わった後に呼ぶ（パネルの表示を合わせる）。</summary>
        public Action OnClipEdited;

        private const string EditUndoStackId = "PlayerEdit/MotionClipEdit";

        private double _loadedDuration;
        private int    _generation;
        private float  _lastTime;
        private UndoStack<EditUndoContext> _editUndoStack;
        private EditUndoContext            _editUndoCtx;

        [PLToolState(Description = "読み込んだ後に編集の口でクリップを変えたか（パネルの JSON 保存は、変えたときは窓口のクリップを書く）")]
        public bool EditedSinceLoad { get; private set; }

        [PLToolState(Description = "クリップが編集の構造（edit）を持つか")]
        public bool HasEdit => _dto?.edit != null;

        [PLToolState(Description = "直前の編集の口で作ったトラック・クリップ・バンドルの id（作らなかったときは空）")]
        public string LastCreatedId { get; private set; } = "";

        /// <summary>直前に当てた（またはモデルが無く当てられなかった）フレームの時刻（秒）。編集のコマンドの time 省略に使う。</summary>
        public float LastTime => _lastTime;

        // ================================================================
        // 取り消し
        // ================================================================

        public sealed class EditUndoContext { public MotionClipHandler Owner; }

        private sealed class EditRecord : IUndoRecord<EditUndoContext>
        {
            public UndoOperationInfo Info { get; set; }
            public int Generation;
            public string Before;
            public string After;
            public void Undo(EditUndoContext ctx) => ctx?.Owner?.RestoreFromRecord(Generation, Before);
            public void Redo(EditUndoContext ctx) => ctx?.Owner?.RestoreFromRecord(Generation, After);
        }

        private void RecordEdit(string desc, string before, string after)
        {
            var undo = GetUndoController?.Invoke();
            if (undo == null) return;
            if (_editUndoStack == null)
            {
                undo.RemoveSubWindowStack(EditUndoStackId);   // 作り直したときの重複 ID を避ける
                _editUndoCtx   = new EditUndoContext { Owner = this };
                _editUndoStack = undo.CreateSubWindowStack(EditUndoStackId, "モーションの編集", _editUndoCtx);
            }
            _editUndoStack.Record(new EditRecord { Generation = _generation, Before = before, After = after }, desc);
            undo.FocusSubWindow(EditUndoStackId);
        }

        private void RestoreFromRecord(int generation, string json)
        {
            if (generation != _generation || _dto == null)
            {
                UnityEngine.Debug.Log("[MotionClipHandler] 読み込み直す前のクリップの編集なので、取り消し・やり直しは何もしません");
                return;
            }
            try
            {
                _dto = MotionClipSerializer.FromJsonRaw(json);
                Rebind(_lastTime);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                UnityEngine.Debug.LogError($"[MotionClipHandler] {ex}");
                return;
            }
            OnClipEdited?.Invoke();
        }

        private void Rebind(float time)
        {
            EnsureApplier();
            _applier.SetClip(_dto);
            var model = GetModel?.Invoke();
            if (model != null) { _applier.BuildMapping(model); ApplyFrameCore(time); }
            else _lastTime = time;
        }

        // ================================================================
        // 流れ
        // ================================================================

        private bool RunEdit(string desc, float time, Func<MotionClipDTO, string> op)
        {
            Error = "";
            LastCreatedId = "";
            if (_dto == null) { Error = "クリップを読み込んでください"; return false; }
            string before = MotionClipSerializer.ToJson(_dto, indented: false);
            MotionClipDTO work;
            string err;
            try
            {
                work = MotionClipSerializer.FromJsonRaw(before);
                err = MotionEditOps.Run(work, _loadedDuration, op);
            }
            catch (Exception ex)
            {
                work = null;
                err = ex.Message;
                UnityEngine.Debug.LogError($"[MotionClipHandler] {ex}");
            }
            if (err != null) { Error = err; LastCreatedId = ""; return false; }

            string after = MotionClipSerializer.ToJson(work, indented: false);
            if (after == before) return true;
            _dto = work;
            EditedSinceLoad = true;
            try { Rebind(time); }
            catch (Exception ex)
            {
                Error = ex.Message;
                UnityEngine.Debug.LogError($"[MotionClipHandler] {ex}");
            }
            RecordEdit(desc, before, after);
            OnClipEdited?.Invoke();
            return true;
        }

        private Func<MotionClipDTO, string> Made(Func<MotionClipDTO, (string err, string id)> f)
            => d => { var r = f(d); LastCreatedId = r.id ?? ""; return r.err; };

        // ================================================================
        // トラック
        // ================================================================

        [PLToolAction(Description = "編集：トラックを足す（role: bones / bakedBones / body / lookAt / expressions / muscles。targetKind は bones だけ boneName / path、target は骨名・Humanoid 名・表情名・マッスル名）。作った id は lastCreatedId")]
        public void EditAddTrack(string role, string target, string targetKind = "", string name = "", float time = 0f)
            => RunEdit("トラックを足す", time, Made(d => (MotionEditOps.AddTrack(d, role, targetKind, target, name, out var id), id)));

        [PLToolAction(Description = "編集：トラックを除く（平らなトラックも除く）")]
        public void EditRemoveTrack(string trackId, float time = 0f)
            => RunEdit("トラックを除く", time, d => MotionEditOps.RemoveTrack(d, trackId));

        [PLToolAction(Description = "編集：トラックのラップ（pre / post: Clamp・Loop・PingPong など。空は未指定、post の未指定はループのクリップがあれば Loop）")]
        public void EditSetWrap(string trackId, string pre = "", string post = "", float time = 0f)
            => RunEdit("ラップを変える", time, d => MotionEditOps.SetWrap(d, trackId, pre, post));

        // ================================================================
        // クリップ
        // ================================================================

        [PLToolAction(Description = "編集：空のクリップを足す（start はフレーム 0 以上、len はフレーム 1 以上）。作った id は lastCreatedId")]
        public void EditAddClip(string trackId, int start, int len = 30, string name = "", float time = 0f)
            => RunEdit("クリップを足す", time, Made(d => (MotionEditOps.AddClip(d, trackId, start, len, name, out var id), id)));

        [PLToolAction(Description = "編集：クリップを除く")]
        public void EditRemoveClip(string clipId, float time = 0f)
            => RunEdit("クリップを除く", time, d => MotionEditOps.RemoveClip(d, clipId));

        [PLToolAction(Description = "編集：クリップの開始（フレーム）・長さ（フレーム）・ループ・重み（0～1）を変える")]
        public void EditSetClip(string clipId, int start, int len, bool loop, float weight, float time = 0f)
            => RunEdit("クリップを変える", time, d => MotionEditOps.SetClip(d, clipId, start, len, loop, weight));

        // ================================================================
        // キー
        // ================================================================

        [PLToolAction(Description = "編集：クリップの先頭からのフレーム f に、全チャンネルの今の評価値でキーを打つ（Studio の「キー追加」）")]
        public void EditCaptureKey(string clipId, int f, float time = 0f)
            => RunEdit("キーを打つ", time, d => MotionEditOps.CaptureKey(d, clipId, f));

        [PLToolAction(Description = "編集：チャンネル（rot / pos / scl / w）のフレーム f のキーの値を変える（回転は x,y,z,w、位置・拡大は x,y,z、w は 1 個）")]
        public void EditSetKeyValue(string clipId, string channel, int f, float[] value, float time = 0f)
            => RunEdit("キーの値を変える", time, d => MotionEditOps.SetKeyValue(d, clipId, channel, f, value));

        [PLToolAction(Description = "編集：チャンネル（rot / pos / scl / w）のフレーム f にキーを作る（あれば値だけ変える）。ほかのチャンネルは変えない。f は 0 以上、長さ未満")]
        public void EditSetChannelKey(string clipId, string channel, int f, float[] value, float time = 0f)
            => RunEdit("チャンネルのキーを打つ", time, d => MotionEditOps.SetChannelKey(d, clipId, channel, f, value));

        [PLToolAction(Description = "編集：チャンネル（rot / pos / scl / w）のフレーム f のキーだけを除く")]
        public void EditRemoveChannelKey(string clipId, string channel, int f, float time = 0f)
            => RunEdit("チャンネルのキーを除く", time, d => MotionEditOps.RemoveChannelKey(d, clipId, channel, f));

        [PLToolAction(Description = "編集：フレーム f のキーを全チャンネルで除く")]
        public void EditRemoveKey(string clipId, int f, float time = 0f)
            => RunEdit("キーを除く", time, d => MotionEditOps.RemoveKey(d, clipId, f));

        [PLToolAction(Description = "編集：フレーム f のキーを newF へ動かす（移り先にキーがあればエラー）")]
        public void EditMoveKey(string clipId, int f, int newF, float time = 0f)
            => RunEdit("キーを動かす", time, d => MotionEditOps.MoveKey(d, clipId, f, newF));

        // ================================================================
        // 接線
        // ================================================================

        [PLToolAction(Description = "編集：キーの入側（side in）か出側（out）の接線モード（Constant / Linear / Auto / ClampedAuto / Free）を全成分に当てる")]
        public void EditSetTangentMode(string clipId, string channel, int f, string side, string mode, float time = 0f)
            => RunEdit("接線のモードを変える", time, d => MotionEditOps.SetTangentMode(d, clipId, channel, f, side, mode));

        [PLToolAction(Description = "編集：キーの成分 comp の入側か出側の傾き（値/秒）を書く（Auto・ClampedAuto・Linear は Free になる）")]
        public void EditSetTangentSlope(string clipId, string channel, int f, int comp, string side, float slope, float time = 0f)
            => RunEdit("接線の傾きを変える", time, d => MotionEditOps.SetTangentSlope(d, clipId, channel, f, comp, side, slope));

        [PLToolAction(Description = "編集：キーの成分 comp の重み方式（None / In / Out / Both）を書く")]
        public void EditSetWeightedMode(string clipId, string channel, int f, int comp, string weightedMode, float time = 0f)
            => RunEdit("接線の重み方式を変える", time, d => MotionEditOps.SetWeightedMode(d, clipId, channel, f, comp, weightedMode));

        [PLToolAction(Description = "編集：キーの成分 comp の入側か出側の重み（0～1）を書く")]
        public void EditSetTangentWeight(string clipId, string channel, int f, int comp, string side, float weight, float time = 0f)
            => RunEdit("接線の重みを変える", time, d => MotionEditOps.SetTangentWeight(d, clipId, channel, f, comp, side, weight));

        // ================================================================
        // バンドル・レイヤー
        // ================================================================

        [PLToolAction(Description = "編集：クリップ（clipIds）とバンドル（bundleIds。入れ子にする）を束ねた新しいバンドルを作る。作った id は lastCreatedId")]
        public void EditMakeBundle(string[] clipIds = null, string[] bundleIds = null, string name = "", float time = 0f)
            => RunEdit("バンドルを作る", time, Made(d => (MotionEditOps.MakeBundle(d, clipIds, bundleIds, name, out var id), id)));

        [PLToolAction(Description = "編集：バンドルだけを解く（クリップと子は残し、子は親へ上げる）")]
        public void EditUngroup(string bundleId, float time = 0f)
            => RunEdit("バンドルを解く", time, d => MotionEditOps.Ungroup(d, bundleId));

        [PLToolAction(Description = "編集：バンドルを中身ごと複製し、クリップの開始を dStart フレームずらす。作った id は lastCreatedId")]
        public void EditCloneBundle(string bundleId, int dStart = 0, float time = 0f)
            => RunEdit("バンドルを複製する", time, Made(d => (MotionEditOps.CloneBundle(d, bundleId, dStart, out var id), id)));

        [PLToolAction(Description = "編集：バンドルを中身のクリップと子ごと除く")]
        public void EditDeleteBundle(string bundleId, float time = 0f)
            => RunEdit("バンドルを除く", time, d => MotionEditOps.DeleteBundle(d, bundleId));

        [PLToolAction(Description = "編集：バンドルのレイヤー（enabled false でレイヤーでなくする。mode override / additive、weight 0～1、mute、solo）")]
        public void EditSetLayer(string bundleId, bool enabled, string mode = "override", float weight = 1f, bool mute = false, bool solo = false, float time = 0f)
            => RunEdit("レイヤーを変える", time, d => MotionEditOps.SetLayer(d, bundleId, enabled, mode, weight, mute, solo));

        [PLToolAction(Description = "編集：レイヤーの重ねる順を、同じ入れ物の dir（-1 / 1）方向の隣のレイヤーと入れ替える")]
        public void EditSwapLayer(string bundleId, int dir, float time = 0f)
            => RunEdit("重ねる順を変える", time, d => MotionEditOps.SwapLayer(d, bundleId, dir));
    }
}

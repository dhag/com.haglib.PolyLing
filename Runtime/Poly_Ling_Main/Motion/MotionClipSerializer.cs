using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Poly_Ling.UnityClip;

namespace Poly_Ling.Motion
{
    // ================================================================
    // MotionClipSerializer
    // ----------------------------------------------------------------
    // MotionClipDTO（純POCO）の JSON 入出力・検証・ファイル入出力を担う。
    // 既存 VmdMotionSerializer に倣い
    // Newtonsoft.Json（com.unity.nuget.newtonsoft-json）を使用する。
    // エディタ専用の Unity.Plastic.Newtonsoft.Json はプレイヤービルドに含まれないため使わない。
    //
    // ■ 書き出し
    //   - 改行付き・不変文化圏・null のメンバーは書かない。
    //   - BOM なし UTF-8。
    //   - 同じフォルダの "<名前>.tmp" へ書き、読み戻して JSON として解析できたら置き換える。
    //     書き込み途中の失敗で既存ファイルを壊さないため。
    //   - 検証でエラーが 1 件でもあれば書かない。
    //
    // ■ 読込
    //   JSON 解析 → format / version の確認 → version 1 の引き上げ → DTO 化 → 検証
    //   → muscles が無く bakedBones だけなら正準骨格でマッスル＋body へ焼く（CanonBoneMotion.BakeMuscles）。
    //     このとき rotationSource を "bakedBones" にする（bakedBones が正本）。
    //   書出しで muscles があれば bakedBones をマッスルから作り直す。ただし rotationSource が
    //   "bakedBones" なら作り直さない。
    //   構文エラー・非対応形式・非対応版・検証エラーは失敗（Dto = null）。
    //   警告だけなら成功。
    //
    // ■ 検証が DTO を書き換えるもの
    //   - キーを秒昇順へ並べ替える（警告を出す）。
    //   - 回転キーの符号を隣と揃える（MotionCurveMath.EnsureQuaternionContinuity。警告を出す）。
    //   値の範囲外（マッスル -1～1、表情 0～1）は警告だけで、値は変えない。
    //
    // モデルを見る検査（表情・ボーンの解決、マッスルと補助ボーンの競合）は
    // MotionClipApplier.BindingReport が持つ。ここはファイルだけで決まる検査に限る。
    // ================================================================
    public static class MotionClipSerializer
    {
        private static JsonSerializerSettings Settings => new JsonSerializerSettings
        {
            Culture           = CultureInfo.InvariantCulture,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting        = Formatting.Indented,
        };

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // ------------------------------------------------------------
        // JSON 入出力
        // ------------------------------------------------------------

        /// <summary>DTO を JSON 文字列にする。検証はしない。</summary>
        public static string ToJson(MotionClipDTO dto, bool indented = true)
        {
            var s = Settings;
            s.Formatting = indented ? Formatting.Indented : Formatting.None;
            return JsonConvert.SerializeObject(dto, s);
        }

        /// <summary>JSON 文字列を解析・検証する。</summary>
        public static MotionClipLoadResult Parse(string json)
        {
            var result = new MotionClipLoadResult();

            JObject jo;
            try
            {
                jo = JObject.Parse(json ?? "");
            }
            catch (JsonReaderException ex)
            {
                result.Error(MotionFileCodes.InvalidJson, $"{ex.LineNumber} 行 {ex.LinePosition} 文字目: {ex.Message}");
                return result;
            }

            string format = jo.Value<string>("format");
            if (!string.Equals(format, MotionClipDTO.FormatName, StringComparison.Ordinal))
            {
                result.Error(MotionFileCodes.UnsupportedFormat,
                    $"format が \"{MotionClipDTO.FormatName}\" ではありません（\"{format}\"）");
                return result;
            }

            int version;
            var vtok = jo["version"];
            if (vtok == null || vtok.Type != JTokenType.Integer)
            {
                result.Error(MotionFileCodes.UnsupportedVersion, "version が整数ではありません");
                return result;
            }
            version = vtok.Value<int>();
            if (version < 1 || version > MotionClipDTO.CurrentVersion)
            {
                result.Error(MotionFileCodes.UnsupportedVersion,
                    $"version {version} は読めません（1～{MotionClipDTO.CurrentVersion}）");
                return result;
            }

            // version 1 → 2: "morphs" を "expressions" へ移す。キーの形は同じ（tan が無いだけ）。
            if (version == 1)
            {
                if (jo["morphs"] is JArray morphs && jo["expressions"] == null)
                    jo["expressions"] = morphs;
                jo.Remove("morphs");
            }
            // version 2 → 3: 形は同じ。Root を muscles に置いたもの（旧表現）は Validate がエラーにする。
            // version 3 → 4: sourceRest を廃止（下で捨てる）。bakedBones の意味は正準骨格上の
            //   ローカル回転に固定されたが、旧ファイルの値が正準かどうかはファイルから判別できない。
            //   旧ファイルの bakedBones は警告を出したうえで捨てる（再生の正本は muscles）。
            if (jo["sourceRest"] is JArray srArr)
            {
                if (srArr.Count > 0)
                    result.Warning(MotionFileCodes.DroppedField, $"sourceRest（{srArr.Count} 件）は version 4 で廃止したため読みません");
                jo.Remove("sourceRest");
            }
            if (version < 4 && jo["bakedBones"] is JArray bbArr)
            {
                if (bbArr.Count > 0)
                    result.Warning(MotionFileCodes.DroppedField,
                        $"version {version} の bakedBones（{bbArr.Count} 本）は意味が正準と限らないため読みません");
                jo.Remove("bakedBones");
            }
            // version 5：編集の構造 edit を足した。version 4 以前に edit があれば捨てる。
            if (version < 5 && jo["edit"] != null)
            {
                result.Warning(MotionFileCodes.DroppedField, $"version {version} の edit は version 5 の欄なので読みません");
                jo.Remove("edit");
            }
            if (version < MotionClipDTO.CurrentVersion) result.UpgradedFromVersion = version;

            MotionClipDTO dto;
            try
            {
                dto = jo.ToObject<MotionClipDTO>(JsonSerializer.Create(Settings));
            }
            catch (JsonException ex)
            {
                result.Error(MotionFileCodes.InvalidJson, $"項目の型が合いません: {ex.Message}");
                return result;
            }
            if (dto == null)
            {
                result.Error(MotionFileCodes.InvalidJson, "内容が空です");
                return result;
            }
            dto.version = MotionClipDTO.CurrentVersion;

            Validate(dto, result);
            if (result.ErrorCount > 0) return result;

            // muscles が無く bakedBones だけなら、正準骨格でマッスル＋body へ焼く（再生の正は muscles）。
            bool hadBody = dto.body != null;
            var ignored = new List<string>();
            if (!CanonBoneMotion.BakeMuscles(dto, ignored, out bool baked, out string bakeReason))
            {
                result.Error(MotionFileCodes.InvalidHeader, bakeReason);
                return result;
            }
            if (ignored.Count > 0)
                result.Warning(MotionFileCodes.UnknownBone,
                    $"bakedBones のうち正準骨格に無いボーンは使いません: {string.Join(", ", ignored)}");
            if (baked && hadBody)
                result.Warning(MotionFileCodes.DroppedField, "muscles が無いため、body は bakedBones から作り直しました");
            // bakedBones だけのファイルは bakedBones が正本（マッスルは Humanoid 向けに添えたもの）。
            if (baked) dto.rotationSource = MotionClipDTO.RotationSourceBakedBones;

            result.Dto = dto;
            return result;
        }

        /// <summary>JSON 文字列を読む。エラーがあれば例外。</summary>
        public static MotionClipDTO FromJson(string json)
        {
            var r = Parse(json);
            if (r.Dto == null) throw new InvalidDataException(r.FormatIssues(10));
            return r.Dto;
        }

        // ------------------------------------------------------------
        // ファイル入出力
        // ------------------------------------------------------------

        /// <summary>ファイルを読み、解析・検証する。</summary>
        public static MotionClipLoadResult Load(string path)
        {
            string text;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                var r = new MotionClipLoadResult();
                r.Error(MotionFileCodes.FileReadFailed, ex.Message);
                return r;
            }
            return Parse(text);
        }

        /// <summary>ファイルを読む。エラーがあれば例外。</summary>
        public static MotionClipDTO LoadJson(string path)
        {
            var r = Load(path);
            if (r.Dto == null) throw new InvalidDataException(r.FormatIssues(10));
            return r.Dto;
        }

        /// <summary>
        /// 検証してから書き出す。エラーがあれば書かない（戻り値の Dto は null）。
        /// 成功時の Dto は書き出した DTO（並べ替え・符号揃え済み）。
        /// </summary>
        public static MotionClipLoadResult Save(MotionClipDTO dto, string path)
        {
            var result = new MotionClipLoadResult();
            if (!PrepareForSave(dto, result)) return result;

            string json = ToJson(dto, indented: true);
            string tmp  = path + ".tmp";
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(tmp, json, Utf8NoBom);

                // 読み戻して解析できることを確かめてから置き換える。
                JObject.Parse(File.ReadAllText(tmp, Encoding.UTF8));

                if (File.Exists(path)) File.Replace(tmp, path, null);
                else                   File.Move(tmp, path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonReaderException)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
                result.Error(MotionFileCodes.FileWriteFailed, ex.Message);
                return result;
            }

            result.Dto = dto;
            return result;
        }

        /// <summary>
        /// Save がファイルに書くものと同じ JSON 文字列を作る（ファイルは書かない）。dto は書き換えない
        /// （写しを整えて書く）。エラーがあれば json は null で、理由は戻り値の Issues。
        /// 成功時の戻り値の Dto は整えた写し。Blender ブリッジのモーション取得（queryMotionClipJson）が使う。
        /// </summary>
        public static MotionClipLoadResult ToSaveJson(MotionClipDTO dto, out string json)
        {
            json = null;
            var result = new MotionClipLoadResult();
            if (dto == null)
            {
                result.Error(MotionFileCodes.InvalidHeader, "書き出す DTO がありません");
                return result;
            }
            var copy = JsonConvert.DeserializeObject<MotionClipDTO>(ToJson(dto, indented: false), Settings);
            if (!PrepareForSave(copy, result)) return result;
            json = ToJson(copy, indented: true);
            result.Dto = copy;
            return result;
        }

        /// <summary>
        /// 書き出す前の整え（Save と ToSaveJson が共用）。dto を書き換える。エラーがあれば false。
        /// </summary>
        private static bool PrepareForSave(MotionClipDTO dto, MotionClipLoadResult result)
        {
            if (dto == null)
            {
                result.Error(MotionFileCodes.InvalidHeader, "書き出す DTO がありません");
                return false;
            }

            dto.format  = MotionClipDTO.FormatName;
            dto.version = MotionClipDTO.CurrentVersion;

            // edit の bakedBones のトラックでキーが残らず平らなトラックの無いもの（Studio は書かない）は書かない。
            // BakeBones の後にもう一度行う（muscles が正本のクリップは bakedBones を作り直すため）。
            MotionEditOps.DropUnmatchedBakedTracks(dto);

            // muscles を持つなら bakedBones を必ず添える（マッスルから Unity で解いた正準ローカル回転。
            // PolyLing_VRMA整備計画.md 2.1）。Unity の外（JS など）はこれで骨を動かす。
            // rotationSource が "bakedBones" のクリップは bakedBones が正本なので作り直さない。
            if (dto.muscles != null && dto.muscles.Count > 0)
            {
                SortKeys(dto);
                if (!MotionClipDTO.BakedBonesArePrimary(dto)
                    && !CanonBoneMotion.BakeBones(dto, out string bakeReason))
                {
                    result.Error(MotionFileCodes.InvalidHeader, bakeReason);
                    return false;
                }
            }

            MotionEditOps.DropUnmatchedBakedTracks(dto);

            Validate(dto, result);
            if (result.ErrorCount > 0) return false;

            if (dto.duration <= 0f) dto.duration = MaxKeyTime(dto);
            return true;
        }

        /// <summary>
        /// DTO の写しを作る（ToJson を同じ設定で読み戻す。ToSaveJson と同じ写し方）。
        /// 窓口の編集の口が、変える前の状態を残すのと取り消しの記録に使う。
        /// </summary>
        public static MotionClipDTO Clone(MotionClipDTO dto)
            => dto == null ? null : FromJsonRaw(ToJson(dto, indented: false));

        /// <summary>ToJson で作った文字列を、検証・引き上げをせずに DTO へ戻す。</summary>
        public static MotionClipDTO FromJsonRaw(string json)
            => JsonConvert.DeserializeObject<MotionClipDTO>(json, Settings);

        /// <summary>書き出す。エラーがあれば例外。</summary>
        public static void SaveJson(MotionClipDTO dto, string path)
        {
            var r = Save(dto, path);
            if (r.Dto == null) throw new InvalidDataException(r.FormatIssues(10));
        }

        // ------------------------------------------------------------
        // 長さ
        // ------------------------------------------------------------

        /// <summary>全トラックのキー時刻の最大値。</summary>
        public static float MaxKeyTime(MotionClipDTO dto)
        {
            if (dto == null) return 0f;
            float max = 0f;
            max = Mathf.Max(max, MaxTimeTracks(dto.bones));
            max = Mathf.Max(max, MaxTimeTracks(dto.bakedBones));
            if (dto.body?.keys != null) foreach (var k in dto.body.keys) if (k != null) max = Mathf.Max(max, k.t);
            if (dto.lookAt?.keys != null) foreach (var k in dto.lookAt.keys) if (k != null) max = Mathf.Max(max, k.t);
            max = Mathf.Max(max, MaxTimeScalar(dto.muscles));
            if (dto.expressions != null)
                foreach (var tr in dto.expressions)
                    if (tr?.keys != null) foreach (var k in tr.keys) if (k != null) max = Mathf.Max(max, k.t);
            return max;
        }

        /// <summary>再生の長さ。duration とキー時刻の最大値の大きい方。</summary>
        public static float Length(MotionClipDTO dto)
            => dto == null ? 0f : Mathf.Max(dto.duration, MaxKeyTime(dto));

        private static float MaxTimeTracks(List<MotionTrackDTO> tracks)
        {
            float max = 0f;
            if (tracks == null) return 0f;
            foreach (var t in tracks)
                if (t?.keys != null) foreach (var k in t.keys) if (k != null) max = Mathf.Max(max, k.t);
            return max;
        }

        private static float MaxTimeScalar(List<MotionScalarTrackDTO> tracks)
        {
            float max = 0f;
            if (tracks == null) return 0f;
            foreach (var t in tracks)
                if (t?.keys != null) foreach (var k in t.keys) if (k != null) max = Mathf.Max(max, k.t);
            return max;
        }

        // ------------------------------------------------------------
        // キーの秒昇順ソート
        // ------------------------------------------------------------
        public static void SortKeys(MotionClipDTO dto)
        {
            if (dto == null) return;

            SortTracks(dto.bones);
            SortTracks(dto.bakedBones);
            if (dto.body != null) SortTrack(dto.body);
            if (dto.lookAt != null) SortTrack(dto.lookAt);

            if (dto.expressions != null)
                foreach (var t in dto.expressions)
                    if (t?.keys != null) t.keys.Sort((a, b) => CompareT(a?.t, b?.t));
            SortScalarTracks(dto.muscles);
        }

        private static int CompareT(float? a, float? b) => (a ?? 0f).CompareTo(b ?? 0f);

        private static void SortTracks(List<MotionTrackDTO> tracks)
        {
            if (tracks == null) return;
            foreach (var t in tracks) SortTrack(t);
        }

        private static void SortTrack(MotionTrackDTO track)
        {
            if (track?.keys == null) return;
            track.keys.Sort((a, b) => CompareT(a?.t, b?.t));
        }

        private static void SortScalarTracks(List<MotionScalarTrackDTO> tracks)
        {
            if (tracks == null) return;
            foreach (var t in tracks)
                if (t?.keys != null) t.keys.Sort((a, b) => CompareT(a?.t, b?.t));
        }

        // ================================================================
        // 検証
        // ================================================================

        private static HashSet<string> _muscleNames;

        private static bool IsKnownMuscle(string name)
        {
            if (_muscleNames == null)
                _muscleNames = new HashSet<string>(HumanTrait.MuscleName, StringComparer.Ordinal);
            return _muscleNames.Contains(name);
        }

        /// <summary>ファイルだけで決まる検査を行い、結果へ積む。キーの並べ替えと回転符号の揃えも行う。</summary>
        public static void Validate(MotionClipDTO dto, MotionClipLoadResult r)
        {
            if (dto == null) { r.Error(MotionFileCodes.InvalidHeader, "DTO がありません"); return; }

            if (!IsFinite(dto.frameRate) || dto.frameRate <= 0f)
                r.Error(MotionFileCodes.InvalidHeader, $"frameRate は正の数であること（{dto.frameRate}）");
            if (!IsFinite(dto.duration) || dto.duration < 0f)
                r.Error(MotionFileCodes.InvalidHeader, $"duration は 0 以上であること（{dto.duration}）");

            // ---- マッスル ----
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (dto.muscles != null)
            {
                foreach (var tr in dto.muscles)
                {
                    if (tr == null) { r.Error(MotionFileCodes.InvalidTrack, "muscles に空の要素があります"); continue; }
                    string label = $"muscles[{tr.name}]";
                    if (string.IsNullOrEmpty(tr.name)) { r.Error(MotionFileCodes.InvalidTrack, "muscles に名前の無いトラックがあります"); continue; }
                    if (!seen.Add(tr.name)) r.Error(MotionFileCodes.DuplicateTrack, $"{label} が重複しています");

                    if (UnityClipRootMotion.IsRootCurveName(tr.name))
                    {
                        r.Error(MotionFileCodes.InvalidTrack,
                            $"{label}：Root（RootT / RootQ）は muscles ではなく body トラック（pos / rot）に置くこと");
                        continue;
                    }
                    if (!IsKnownMuscle(tr.name))
                        r.Warning(MotionFileCodes.UnknownMuscle, $"{label} は Unity のマッスル名にありません");

                    ValidateScalarTrack(tr, label, r, -1f, 1f, MotionFileCodes.MuscleOutOfRange);
                }
            }

            // ---- 表情 ----
            seen.Clear();
            if (dto.expressions != null)
            {
                foreach (var tr in dto.expressions)
                {
                    if (tr == null) { r.Error(MotionFileCodes.InvalidTrack, "expressions に空の要素があります"); continue; }
                    string label = $"expressions[{tr.name}]";
                    if (string.IsNullOrEmpty(tr.name)) { r.Error(MotionFileCodes.InvalidTrack, "expressions に名前の無いトラックがあります"); continue; }
                    // 同じ名前でもパス（レンダラー）が違えば別のトラック。
                    if (!string.IsNullOrEmpty(tr.path)) label = $"expressions[{tr.path}:{tr.name}]";
                    if (!seen.Add((tr.path ?? "") + "\n" + tr.name)) r.Error(MotionFileCodes.DuplicateTrack, $"{label} が重複しています");
                    ValidateScalarTrack(tr, label, r, 0f, 1f, MotionFileCodes.ExpressionOutOfRange);
                }
            }

            // ---- ボーン ----
            seen.Clear();
            if (dto.bones != null)
            {
                foreach (var tr in dto.bones)
                {
                    if (tr == null) { r.Error(MotionFileCodes.InvalidTrack, "bones に空の要素があります"); continue; }
                    string label = $"bones[{tr.targetKind}:{tr.id}]";
                    if (tr.id == null) { r.Error(MotionFileCodes.InvalidTrack, "bones に id の無いトラックがあります"); continue; }
                    if (tr.targetKind != MotionClipDTO.TargetBoneName && tr.targetKind != MotionClipDTO.TargetPath)
                        r.Error(MotionFileCodes.InvalidTrack, $"{label} の targetKind は \"boneName\" か \"path\" であること");
                    if (!seen.Add(tr.targetKind + "\n" + tr.id)) r.Error(MotionFileCodes.DuplicateTrack, $"{label} が重複しています");
                    ValidateBoneTrack(tr, label, r);
                }
            }

            seen.Clear();
            if (dto.bakedBones != null)
            {
                foreach (var tr in dto.bakedBones)
                {
                    if (tr == null) { r.Error(MotionFileCodes.InvalidTrack, "bakedBones に空の要素があります"); continue; }
                    string label = $"bakedBones[{tr.id}]";
                    if (string.IsNullOrEmpty(tr.id)) { r.Error(MotionFileCodes.InvalidTrack, "bakedBones に id の無いトラックがあります"); continue; }
                    if (tr.targetKind != MotionClipDTO.TargetHumanoid)
                        r.Error(MotionFileCodes.InvalidTrack, $"{label} の targetKind は \"humanoid\" であること（\"{tr.targetKind}\"）");
                    if (!IsHumanoidName(tr.id))
                        r.Error(MotionFileCodes.InvalidTrack, $"{label} の id は HumanBodyBones の列挙名であること");
                    if (!seen.Add(tr.id)) r.Error(MotionFileCodes.DuplicateTrack, $"{label} が重複しています");
                    bool isHips = tr.id == nameof(HumanBodyBones.Hips);
                    if (tr.keys != null)
                        for (int i = 0; i < tr.keys.Count; i++)
                        {
                            var k = tr.keys[i];
                            if (k == null) continue;
                            if (k.scl != null)
                                r.Error(MotionFileCodes.InvalidCurve, $"{label} キー {i} に scl があります（bakedBones は拡大縮小を持たない）");
                            if (k.pos != null && !isHips)
                                r.Error(MotionFileCodes.InvalidCurve, $"{label} キー {i} に pos があります（pos を持てるのは Hips だけ）");
                        }
                    ValidateBoneTrack(tr, label, r);
                }
            }

            if (dto.body != null)
            {
                if (dto.body.targetKind != MotionClipDTO.TargetBody || dto.body.id != MotionClipDTO.BodyTrackId)
                    r.Error(MotionFileCodes.InvalidTrack,
                        $"body の id と targetKind はどちらも \"body\" であること（id \"{dto.body.id}\" / targetKind \"{dto.body.targetKind}\"）");
                ValidateBoneTrack(dto.body, "body", r);
            }

            // ---- 視線 ----
            if (dto.lookAt != null)
            {
                if (dto.lookAt.targetKind != MotionClipDTO.TargetLookAt || dto.lookAt.id != MotionClipDTO.LookAtTrackId)
                    r.Error(MotionFileCodes.InvalidTrack,
                        $"lookAt の id と targetKind はどちらも \"lookAt\" であること（id \"{dto.lookAt.id}\" / targetKind \"{dto.lookAt.targetKind}\"）");
                if (dto.lookAt.keys != null)
                    for (int i = 0; i < dto.lookAt.keys.Count; i++)
                    {
                        var k = dto.lookAt.keys[i];
                        if (k == null) continue;
                        if (k.pos != null || k.scl != null)
                            r.Error(MotionFileCodes.InvalidCurve, $"lookAt キー {i} に pos か scl があります（lookAt は rot だけ）");
                        if (k.rot == null)
                            r.Error(MotionFileCodes.InvalidCurve, $"lookAt キー {i} に rot がありません");
                    }
                ValidateBoneTrack(dto.lookAt, "lookAt", r);
            }
            if (dto.lookAtOffsetFromHeadBone != null && !IsFiniteArray(dto.lookAtOffsetFromHeadBone, 3))
                r.Error(MotionFileCodes.InvalidHeader, "lookAtOffsetFromHeadBone は null か数 3 個であること");
            if (dto.absentMuscles != null
                && dto.absentMuscles != MotionClipDTO.AbsentMusclesZero
                && dto.absentMuscles != MotionClipDTO.AbsentMusclesRest)
                r.Error(MotionFileCodes.InvalidHeader,
                    $"absentMuscles は \"{MotionClipDTO.AbsentMusclesZero}\" か \"{MotionClipDTO.AbsentMusclesRest}\" であること（\"{dto.absentMuscles}\"）");
            if (dto.rotationSource != null
                && dto.rotationSource != MotionClipDTO.RotationSourceMuscles
                && dto.rotationSource != MotionClipDTO.RotationSourceBakedBones)
                r.Error(MotionFileCodes.InvalidHeader,
                    $"rotationSource は \"{MotionClipDTO.RotationSourceMuscles}\" か \"{MotionClipDTO.RotationSourceBakedBones}\" であること（\"{dto.rotationSource}\"）");
            else if (dto.rotationSource == MotionClipDTO.RotationSourceBakedBones
                     && (dto.bakedBones == null || dto.bakedBones.Count == 0))
                r.Error(MotionFileCodes.InvalidHeader, "rotationSource が \"bakedBones\" なのに bakedBones がありません");

            if (dto.edit != null) ValidateEdit(dto, r);
        }

        // ================================================================
        // 編集の構造（edit）の検証
        // ================================================================

        private static void ValidateEdit(MotionClipDTO dto, MotionClipLoadResult r)
        {
            var e = dto.edit;
            var trackIds  = new HashSet<string>(StringComparer.Ordinal);
            var targets   = new HashSet<string>(StringComparer.Ordinal);
            var clipIds   = new HashSet<string>(StringComparer.Ordinal);

            if (e.tracks != null)
            {
                for (int ti = 0; ti < e.tracks.Count; ti++)
                {
                    var tr = e.tracks[ti];
                    if (tr == null) { r.Error(MotionFileCodes.InvalidEdit, $"edit.tracks[{ti}] が空です"); continue; }
                    string label = $"edit.tracks[{tr.id}]";
                    if (string.IsNullOrEmpty(tr.id)) r.Error(MotionFileCodes.InvalidEdit, $"edit.tracks[{ti}] に id がありません");
                    else if (!trackIds.Add(tr.id)) r.Error(MotionFileCodes.EditDuplicateId, $"{label} の id が重複しています");

                    bool bone = MotionEditDTO.IsBoneRole(tr.role), scalar = MotionEditDTO.IsScalarRole(tr.role);
                    if (!bone && !scalar)
                        r.Error(MotionFileCodes.InvalidEdit, $"{label} の role \"{tr.role}\" は読めません");
                    else
                    {
                        if (tr.target == null) r.Error(MotionFileCodes.InvalidEdit, $"{label} に target がありません");
                        else
                        {
                            if (!targets.Add(tr.role + "\n" + (tr.targetKind ?? "") + "\n" + tr.target))
                                r.Error(MotionFileCodes.EditDuplicateId, $"{label} の当たる平らなトラック（{tr.role}:{tr.target}）が重複しています");
                            if (!HasFlatTrack(dto, tr))
                                r.Error(MotionFileCodes.EditUnmatchedTrack, $"{label} の当たる平らなトラック（{tr.role}:{tr.targetKind}:{tr.target}）がありません");
                        }
                    }
                    CheckWrap(tr.preWrapMode,  label, "preWrapMode",  r);
                    CheckWrap(tr.postWrapMode, label, "postWrapMode", r);

                    if (tr.clips == null) continue;
                    for (int ci = 0; ci < tr.clips.Count; ci++)
                    {
                        var c = tr.clips[ci];
                        if (c == null) { r.Error(MotionFileCodes.InvalidEdit, $"{label} のクリップ {ci} が空です"); continue; }
                        string cl = $"{label}.clips[{c.id}]";
                        if (string.IsNullOrEmpty(c.id)) r.Error(MotionFileCodes.InvalidEdit, $"{label} のクリップ {ci} に id がありません");
                        else if (!clipIds.Add(c.id)) r.Error(MotionFileCodes.EditDuplicateId, $"{cl} の id が重複しています");
                        if (!IsFinite(c.start) || c.start < 0f) r.Error(MotionFileCodes.InvalidEdit, $"{cl} の start は 0 以上であること（{c.start}）");
                        if (!IsFinite(c.len) || c.len < 1f)     r.Error(MotionFileCodes.InvalidEdit, $"{cl} の len は 1 以上であること（{c.len}）");
                        if (!IsFinite(c.weight) || c.weight < 0f || c.weight > 1f)
                            r.Error(MotionFileCodes.InvalidEdit, $"{cl} の weight は 0～1 であること（{c.weight}）");

                        if (bone && c.w != null)
                            r.Error(MotionFileCodes.InvalidEdit, $"{cl} に w があります（{tr.role} のチャンネルは rot / pos / scl）");
                        if (scalar && (c.rot != null || c.pos != null || c.scl != null))
                            r.Error(MotionFileCodes.InvalidEdit, $"{cl} に rot / pos / scl があります（{tr.role} のチャンネルは w）");
                        ValidateEditKeys(c.rot, 4, true, cl, "rot", r);
                        ValidateEditKeys(c.pos, 3, false, cl, "pos", r);
                        ValidateEditKeys(c.scl, 3, false, cl, "scl", r);
                        ValidateEditScalarKeys(c.w, cl, r);
                    }
                }
            }

            // ---- バンドル ----
            if (e.bundles == null) return;
            var byId = new Dictionary<string, MotionEditBundleDTO>(StringComparer.Ordinal);
            for (int bi = 0; bi < e.bundles.Count; bi++)
            {
                var b = e.bundles[bi];
                if (b == null) { r.Error(MotionFileCodes.InvalidEdit, $"edit.bundles[{bi}] が空です"); continue; }
                if (string.IsNullOrEmpty(b.id)) { r.Error(MotionFileCodes.InvalidEdit, $"edit.bundles[{bi}] に id がありません"); continue; }
                if (byId.ContainsKey(b.id)) { r.Error(MotionFileCodes.EditDuplicateId, $"edit.bundles[{b.id}] の id が重複しています"); continue; }
                byId.Add(b.id, b);
                if (b.layer != null)
                {
                    if (b.layer.mode != MotionEditDTO.LayerOverride && b.layer.mode != MotionEditDTO.LayerAdditive)
                        r.Error(MotionFileCodes.InvalidEdit, $"edit.bundles[{b.id}] の layer.mode は \"override\" か \"additive\" であること（\"{b.layer.mode}\"）");
                    if (!IsFinite(b.layer.weight) || b.layer.weight < 0f || b.layer.weight > 1f)
                        r.Error(MotionFileCodes.InvalidEdit, $"edit.bundles[{b.id}] の layer.weight は 0～1 であること（{b.layer.weight}）");
                }
            }

            var clipOwner  = new Dictionary<string, string>(StringComparer.Ordinal);
            var childOwner = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var b in byId.Values)
            {
                string bl = $"edit.bundles[{b.id}]";
                if (b.clipIds != null)
                    foreach (var id in b.clipIds)
                    {
                        if (id == null || !clipIds.Contains(id)) { r.Error(MotionFileCodes.EditBundleLink, $"{bl} の clipIds に無いクリップ \"{id}\" があります"); continue; }
                        if (clipOwner.TryGetValue(id, out string other))
                            r.Error(MotionFileCodes.EditBundleLink, $"クリップ \"{id}\" が {other} と {b.id} の両方に直接属しています");
                        else clipOwner.Add(id, b.id);
                    }
                if (b.childIds != null)
                    foreach (var id in b.childIds)
                    {
                        if (id == null || !byId.TryGetValue(id, out var ch)) { r.Error(MotionFileCodes.EditBundleLink, $"{bl} の childIds に無いバンドル \"{id}\" があります"); continue; }
                        if (childOwner.TryGetValue(id, out string other))
                            r.Error(MotionFileCodes.EditBundleLink, $"バンドル \"{id}\" が {other} と {b.id} の両方の childIds にあります");
                        else childOwner.Add(id, b.id);
                        if (ch.parentId != b.id)
                            r.Error(MotionFileCodes.EditBundleLink, $"{bl} の childIds にある \"{id}\" の parentId が \"{ch.parentId}\" です");
                    }
                if (b.parentId != null)
                {
                    if (!byId.TryGetValue(b.parentId, out var par))
                        r.Error(MotionFileCodes.EditBundleLink, $"{bl} の parentId \"{b.parentId}\" のバンドルがありません");
                    else if (par.childIds == null || !par.childIds.Contains(b.id))
                        r.Error(MotionFileCodes.EditBundleLink, $"{bl} の parentId \"{b.parentId}\" の childIds に {b.id} がありません");
                }
            }

            // parentId の循環
            foreach (var b in byId.Values)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal) { b.id };
                var p = b.parentId;
                while (p != null && byId.TryGetValue(p, out var pb))
                {
                    if (!seen.Add(p)) { r.Error(MotionFileCodes.EditBundleCycle, $"edit.bundles[{b.id}] の parentId をたどると循環しています"); break; }
                    p = pb.parentId;
                }
            }
        }

        /// <summary>edit のトラックに当たる平らなトラックがあるか。</summary>
        private static bool HasFlatTrack(MotionClipDTO dto, MotionEditTrackDTO tr)
        {
            switch (tr.role)
            {
                case MotionEditDTO.RoleBones:
                    if (dto.bones != null) foreach (var t in dto.bones) if (t != null && t.id == tr.target && t.targetKind == tr.targetKind) return true;
                    return false;
                case MotionEditDTO.RoleBakedBones:
                    if (dto.bakedBones != null) foreach (var t in dto.bakedBones) if (t != null && t.id == tr.target) return true;
                    return false;
                case MotionEditDTO.RoleBody:
                    return dto.body != null && tr.target == MotionClipDTO.BodyTrackId;
                case MotionEditDTO.RoleLookAt:
                    return dto.lookAt != null && tr.target == MotionClipDTO.LookAtTrackId;
                case MotionEditDTO.RoleExpressions:
                    if (dto.expressions != null) foreach (var t in dto.expressions) if (t != null && t.name == tr.target && string.IsNullOrEmpty(t.path)) return true;
                    return false;
                case MotionEditDTO.RoleMuscles:
                    if (dto.muscles != null) foreach (var t in dto.muscles) if (t != null && t.name == tr.target) return true;
                    return false;
            }
            return false;
        }

        private static void ValidateEditKeys(List<MotionEditKeyDTO> keys, int n, bool isRot, string label, string ch, MotionClipLoadResult r)
        {
            if (keys == null) return;
            var frames = new HashSet<float>();
            bool sorted = true;
            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                string at = $"{label} {ch} キー {i}";
                if (k == null) { r.Error(MotionFileCodes.InvalidEdit, $"{at} が空です"); continue; }
                CheckEditFrame(k.f, k.t0, at, r);
                if (IsFinite(k.f) && !frames.Add(k.f)) r.Error(MotionFileCodes.EditDuplicateKey, $"{at} のフレーム {k.f} が重複しています");
                if (k.v == null || k.v.Length != n) { r.Error(MotionFileCodes.InvalidEdit, $"{at} の v は {n} 個であること"); }
                else
                {
                    bool fin = true;
                    foreach (var x in k.v) if (!IsFinite(x)) fin = false;
                    if (!fin) r.Error(MotionFileCodes.InvalidEdit, $"{at} の v に数でない値があります");
                    else if (isRot && k.v[0] * k.v[0] + k.v[1] * k.v[1] + k.v[2] * k.v[2] + k.v[3] * k.v[3] < 1e-8f)
                        r.Error(MotionFileCodes.InvalidEdit, $"{at} の回転の長さがほぼ 0 です");
                }
                if (k.tan != null)
                {
                    if (k.tan.Length != n) r.Error(MotionFileCodes.InvalidEdit, $"{at} の tan は {n} 個であること（{k.tan.Length} 個）");
                    else for (int c = 0; c < n; c++) CheckTangent(k.tan[c], $"{label} {ch}", i, $" tan[{c}]", r);
                }
                if (i > 0 && keys[i - 1] != null && keys[i - 1].f > k.f) sorted = false;
            }
            if (!sorted)
            {
                keys.Sort((a, b) => (a?.f ?? 0f).CompareTo(b?.f ?? 0f));
                r.Warning(MotionFileCodes.KeyOrder, $"{label} {ch} のキーをフレーム順に並べ替えました");
            }
        }

        private static void ValidateEditScalarKeys(List<MotionEditScalarKeyDTO> keys, string label, MotionClipLoadResult r)
        {
            if (keys == null) return;
            var frames = new HashSet<float>();
            bool sorted = true;
            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                string at = $"{label} w キー {i}";
                if (k == null) { r.Error(MotionFileCodes.InvalidEdit, $"{at} が空です"); continue; }
                CheckEditFrame(k.f, k.t0, at, r);
                if (IsFinite(k.f) && !frames.Add(k.f)) r.Error(MotionFileCodes.EditDuplicateKey, $"{at} のフレーム {k.f} が重複しています");
                if (!IsFinite(k.v)) r.Error(MotionFileCodes.InvalidEdit, $"{at} の v が数ではありません");
                CheckTangent(k.tan, $"{label} w", i, "", r);
                if (i > 0 && keys[i - 1] != null && keys[i - 1].f > k.f) sorted = false;
            }
            if (!sorted)
            {
                keys.Sort((a, b) => (a?.f ?? 0f).CompareTo(b?.f ?? 0f));
                r.Warning(MotionFileCodes.KeyOrder, $"{label} w のキーをフレーム順に並べ替えました");
            }
        }

        private static void CheckEditFrame(float f, float? t0, string at, MotionClipLoadResult r)
        {
            if (!IsFinite(f) || f < 0f) r.Error(MotionFileCodes.InvalidEdit, $"{at} の f は 0 以上であること（{f}）");
            if (t0.HasValue && (!IsFinite(t0.Value) || t0.Value < 0f)) r.Error(MotionFileCodes.InvalidEdit, $"{at} の t0 は 0 以上であること（{t0.Value}）");
        }

        private static HashSet<string> _humanoidNames;

        /// <summary>HumanBodyBones の列挙名（LastBone を除く）か。</summary>
        public static bool IsHumanoidName(string name)
        {
            if (_humanoidNames == null)
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))
                    if (b != HumanBodyBones.LastBone) set.Add(b.ToString());
                _humanoidNames = set;
            }
            return name != null && _humanoidNames.Contains(name);
        }

        private static bool IsFiniteArray(float[] a, int n)
        {
            if (a == null || a.Length != n) return false;
            foreach (var v in a) if (!IsFinite(v)) return false;
            return true;
        }

        private static void ValidateScalarTrack(MotionScalarTrackDTO tr, string label, MotionClipLoadResult r,
            float min, float max, string rangeCode)
        {
            CheckWrap(tr.preWrapMode,  label, "preWrapMode",  r);
            CheckWrap(tr.postWrapMode, label, "postWrapMode", r);
            if (tr.keys == null) return;

            bool sorted = true;
            int outOfRange = 0;
            for (int i = 0; i < tr.keys.Count; i++)
            {
                var k = tr.keys[i];
                if (k == null) { r.Error(MotionFileCodes.InvalidCurve, $"{label} キー {i} が空です"); continue; }
                CheckTime(k.t, label, i, r);
                if (!IsFinite(k.v)) r.Error(MotionFileCodes.InvalidCurve, $"{label} キー {i} の値が数ではありません");
                else if (k.v < min || k.v > max) outOfRange++;
                CheckTangent(k.tan, label, i, "", r);
                if (i > 0 && tr.keys[i - 1] != null && tr.keys[i - 1].t > k.t) sorted = false;
            }
            if (outOfRange > 0)
                r.Warning(rangeCode, $"{label} の {outOfRange} キーが {min}～{max} の外です");
            if (!sorted)
            {
                tr.keys.Sort((a, b) => CompareT(a?.t, b?.t));
                r.Warning(MotionFileCodes.KeyOrder, $"{label} のキーを時刻順に並べ替えました");
            }
        }

        private static void ValidateBoneTrack(MotionTrackDTO tr, string label, MotionClipLoadResult r)
        {
            CheckWrap(tr.preWrapMode,  label, "preWrapMode",  r);
            CheckWrap(tr.postWrapMode, label, "postWrapMode", r);
            if (tr.keys == null) return;

            bool sorted = true;
            for (int i = 0; i < tr.keys.Count; i++)
            {
                var k = tr.keys[i];
                if (k == null) { r.Error(MotionFileCodes.InvalidCurve, $"{label} キー {i} が空です"); continue; }
                CheckTime(k.t, label, i, r);
                CheckChannel(k.pos, k.posTan, 3, label, i, "pos", r);
                CheckChannel(k.rot, k.rotTan, 4, label, i, "rot", r);
                CheckChannel(k.scl, k.sclTan, 3, label, i, "scl", r);

                if (k.rot != null && k.rot.Length == 4 && IsFinite(k.rot[0]) && IsFinite(k.rot[1]) && IsFinite(k.rot[2]) && IsFinite(k.rot[3]))
                {
                    float len2 = k.rot[0] * k.rot[0] + k.rot[1] * k.rot[1] + k.rot[2] * k.rot[2] + k.rot[3] * k.rot[3];
                    if (len2 < 1e-8f)
                        r.Error(MotionFileCodes.InvalidQuaternion, $"{label} キー {i} の rot の長さがほぼ 0 です");
                }
                if (i > 0 && tr.keys[i - 1] != null && tr.keys[i - 1].t > k.t) sorted = false;
            }
            if (!sorted)
            {
                tr.keys.Sort((a, b) => CompareT(a?.t, b?.t));
                r.Warning(MotionFileCodes.KeyOrder, $"{label} のキーを時刻順に並べ替えました");
            }

            if (r.ErrorCount == 0)
            {
                int flipped = MotionCurveMath.EnsureQuaternionContinuity(tr);
                if (flipped > 0)
                    r.Warning(MotionFileCodes.QuaternionSignFlipped, $"{label} の回転 {flipped} キーの符号を反転して隣と揃えました");
            }
        }

        private static void CheckTime(float t, string label, int i, MotionClipLoadResult r)
        {
            if (!IsFinite(t))  r.Error(MotionFileCodes.InvalidCurve, $"{label} キー {i} の時刻が数ではありません");
            else if (t < 0f)   r.Error(MotionFileCodes.InvalidCurve, $"{label} キー {i} の時刻が負です（{t}）");
        }

        private static void CheckChannel(float[] values, MotionTangentDTO[] tans, int length,
            string label, int i, string ch, MotionClipLoadResult r)
        {
            if (values == null)
            {
                if (tans != null)
                    r.Error(MotionFileCodes.InvalidCurve, $"{label} キー {i} に {ch} が無いのに {ch}Tan があります");
                return;
            }
            if (values.Length != length)
            {
                r.Error(MotionFileCodes.InvalidCurve, $"{label} キー {i} の {ch} は {length} 個であること（{values.Length} 個）");
                return;
            }
            foreach (var v in values)
                if (!IsFinite(v)) { r.Error(MotionFileCodes.InvalidCurve, $"{label} キー {i} の {ch} に数でない値があります"); break; }

            if (tans == null) return;
            if (tans.Length != length)
            {
                r.Error(MotionFileCodes.InvalidCurve, $"{label} キー {i} の {ch}Tan は {length} 個であること（{tans.Length} 個）");
                return;
            }
            for (int c = 0; c < tans.Length; c++) CheckTangent(tans[c], label, i, $" {ch}Tan[{c}]", r);
        }

        private static void CheckTangent(MotionTangentDTO tn, string label, int i, string where, MotionClipLoadResult r)
        {
            if (tn == null) return;
            string at = $"{label} キー {i}{where}";
            if (!MotionCurveMath.IsKnownTangentMode(tn.leftTangentMode))
                r.Error(MotionFileCodes.InvalidCurve, $"{at} の leftTangentMode \"{tn.leftTangentMode}\" は読めません");
            if (!MotionCurveMath.IsKnownTangentMode(tn.rightTangentMode))
                r.Error(MotionFileCodes.InvalidCurve, $"{at} の rightTangentMode \"{tn.rightTangentMode}\" は読めません");
            if (!MotionCurveMath.IsKnownWeightedMode(tn.weightedMode))
                r.Error(MotionFileCodes.InvalidCurve, $"{at} の weightedMode \"{tn.weightedMode}\" は読めません");
            if (tn.inTangent.HasValue  && !IsFinite(tn.inTangent.Value))  r.Error(MotionFileCodes.InvalidCurve, $"{at} の inTangent が数ではありません");
            if (tn.outTangent.HasValue && !IsFinite(tn.outTangent.Value)) r.Error(MotionFileCodes.InvalidCurve, $"{at} の outTangent が数ではありません");
            if (tn.inWeight.HasValue   && (!IsFinite(tn.inWeight.Value)  || tn.inWeight.Value  < 0f || tn.inWeight.Value  > 1f))
                r.Error(MotionFileCodes.InvalidCurve, $"{at} の inWeight は 0～1 であること");
            if (tn.outWeight.HasValue  && (!IsFinite(tn.outWeight.Value) || tn.outWeight.Value < 0f || tn.outWeight.Value > 1f))
                r.Error(MotionFileCodes.InvalidCurve, $"{at} の outWeight は 0～1 であること");
        }

        private static void CheckWrap(string mode, string label, string field, MotionClipLoadResult r)
        {
            if (!MotionCurveMath.IsKnownWrapMode(mode))
                r.Error(MotionFileCodes.InvalidCurve, $"{label} の {field} \"{mode}\" は読めません");
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }

    // ================================================================
    // 結果とコード
    // ================================================================

    /// <summary>モーションファイルの検査コード。</summary>
    public static class MotionFileCodes
    {
        public const string InvalidJson           = "INVALID_JSON";
        public const string UnsupportedFormat     = "UNSUPPORTED_FORMAT";
        public const string UnsupportedVersion    = "UNSUPPORTED_VERSION";
        public const string InvalidHeader         = "INVALID_HEADER";
        public const string InvalidTrack          = "INVALID_TRACK";
        public const string DuplicateTrack        = "DUPLICATE_TRACK";
        public const string InvalidCurve          = "INVALID_CURVE";
        public const string KeyOrder              = "KEY_ORDER";
        public const string InvalidQuaternion     = "INVALID_QUATERNION";
        public const string QuaternionSignFlipped = "QUATERNION_SIGN_FLIPPED";
        public const string UnknownMuscle         = "UNKNOWN_MUSCLE";
        public const string MuscleOutOfRange      = "MUSCLE_OUT_OF_RANGE";
        public const string ExpressionOutOfRange  = "EXPRESSION_OUT_OF_RANGE";
        public const string UnknownExpression     = "UNKNOWN_EXPRESSION";
        public const string UnknownBone           = "UNKNOWN_BONE";
        public const string AuxiliaryBoneConflict = "AUXILIARY_BONE_CONFLICT";
        public const string FileReadFailed        = "FILE_READ_FAILED";
        public const string FileWriteFailed       = "FILE_WRITE_FAILED";
        public const string DroppedField          = "DROPPED_FIELD";
        public const string VrmaInvalid           = "VRMA_INVALID";
        public const string VrmaNotRead           = "VRMA_NOT_READ";
        public const string BvhInvalid            = "BVH_INVALID";
        public const string BvhNotRead            = "BVH_NOT_READ";
        public const string FbxInvalid            = "FBX_INVALID";
        public const string FbxNotRead            = "FBX_NOT_READ";
        public const string InvalidEdit           = "INVALID_EDIT";
        public const string EditDuplicateId       = "EDIT_DUPLICATE_ID";
        public const string EditUnmatchedTrack    = "EDIT_UNMATCHED_TRACK";
        public const string EditDuplicateKey      = "EDIT_DUPLICATE_KEY";
        public const string EditBundleLink        = "EDIT_BUNDLE_LINK";
        public const string EditBundleCycle       = "EDIT_BUNDLE_CYCLE";
    }

    /// <summary>検査で見つかった 1 件。</summary>
    public sealed class MotionFileIssue
    {
        public string Code;
        public string Message;
        public bool   IsError;

        public override string ToString() => $"{(IsError ? "エラー" : "警告")} {Code}: {Message}";
    }

    /// <summary>読込・書き出し・検証の結果。</summary>
    public sealed class MotionClipLoadResult
    {
        /// <summary>成功時の DTO。エラーがあれば null。</summary>
        public MotionClipDTO Dto;

        /// <summary>引き上げ前の版（引き上げていなければ 0）。</summary>
        public int UpgradedFromVersion;

        public readonly List<MotionFileIssue> Issues = new List<MotionFileIssue>();

        public int ErrorCount   { get; private set; }
        public int WarningCount { get; private set; }

        public bool Success => Dto != null && ErrorCount == 0;

        public void Error(string code, string message)
        {
            Issues.Add(new MotionFileIssue { Code = code, Message = message, IsError = true });
            ErrorCount++;
        }

        public void Warning(string code, string message)
        {
            Issues.Add(new MotionFileIssue { Code = code, Message = message, IsError = false });
            WarningCount++;
        }

        /// <summary>エラーを先に、最大 max 件を改行区切りで返す。</summary>
        public string FormatIssues(int max)
        {
            var sb = new StringBuilder();
            int shown = 0;
            foreach (bool errors in new[] { true, false })
            {
                foreach (var i in Issues)
                {
                    if (i.IsError != errors) continue;
                    if (shown >= max) { sb.Append("…ほか ").Append(Issues.Count - shown).Append(" 件"); return sb.ToString(); }
                    if (shown > 0) sb.Append('\n');
                    sb.Append(i);
                    shown++;
                }
            }
            return sb.ToString();
        }
    }
}

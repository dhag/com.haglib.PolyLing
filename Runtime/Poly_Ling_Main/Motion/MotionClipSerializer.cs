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
    // 既存 VmdMotionSerializer / UnityClipSerializer に倣い
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
    //   JSON 解析 → format / version の確認 → version 1 の引き上げ → DTO 化 → 検証。
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
                result.UpgradedFromVersion = 1;
            }

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
            if (result.ErrorCount == 0) result.Dto = dto;
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
            if (dto == null)
            {
                result.Error(MotionFileCodes.InvalidHeader, "書き出す DTO がありません");
                return result;
            }

            dto.format  = MotionClipDTO.FormatName;
            dto.version = MotionClipDTO.CurrentVersion;
            Validate(dto, result);
            if (result.ErrorCount > 0) return result;

            if (dto.duration <= 0f) dto.duration = MaxKeyTime(dto);

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

        private static bool IsRootCurveName(string name)
            => name == UnityClipRootMotion.NameTx || name == UnityClipRootMotion.NameTy || name == UnityClipRootMotion.NameTz
            || name == UnityClipRootMotion.NameQx || name == UnityClipRootMotion.NameQy
            || name == UnityClipRootMotion.NameQz || name == UnityClipRootMotion.NameQw;

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

                    bool root = IsRootCurveName(tr.name);
                    if (!root && !IsKnownMuscle(tr.name))
                        r.Warning(MotionFileCodes.UnknownMuscle, $"{label} は Unity のマッスル名にありません");

                    ValidateScalarTrack(tr, label, r, root ? float.NegativeInfinity : -1f, root ? float.PositiveInfinity : 1f,
                        MotionFileCodes.MuscleOutOfRange);
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
                    if (!seen.Add(tr.name)) r.Error(MotionFileCodes.DuplicateTrack, $"{label} が重複しています");
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
                    if (tr.targetKind != "boneName" && tr.targetKind != "path")
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
                    if (!seen.Add(tr.id)) r.Error(MotionFileCodes.DuplicateTrack, $"{label} が重複しています");
                    ValidateBoneTrack(tr, label, r);
                }
            }

            if (dto.body != null) ValidateBoneTrack(dto.body, "body", r);
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

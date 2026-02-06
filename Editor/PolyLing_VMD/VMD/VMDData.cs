// VMDData.cs
// VMDモーションデータのメインコンテナ
// HagLib.VMDMotion.VMDData から移植・Unity対応版

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// VMDモーションデータ
    /// </summary>
    [Serializable]
    public class VMDData
    {
        // ================================================================
        // ヘッダ情報
        // ================================================================

        /// <summary>VMDヘッダ文字列</summary>
        public string HeaderString = "Vocaloid Motion Data 0002";

        /// <summary>対象モデル名（カメラの場合は"カメラ・照明"）</summary>
        public string ModelName = "No Name";

        /// <summary>カメラ・照明用データかどうか</summary>
        public bool IsCamera { get; private set; }

        // ================================================================
        // フレームデータリスト（生データ）
        // ================================================================

        /// <summary>ボーンフレームリスト</summary>
        public List<BoneFrameData> BoneFrameList = new List<BoneFrameData>();

        /// <summary>モーフフレームリスト</summary>
        public List<MorphFrameData> MorphFrameList = new List<MorphFrameData>();

        /// <summary>カメラフレームリスト</summary>
        public List<CameraFrameData> CameraFrameList = new List<CameraFrameData>();

        /// <summary>ライトフレームリスト</summary>
        public List<LightFrameData> LightFrameList = new List<LightFrameData>();

        /// <summary>セルフシャドウフレームリスト</summary>
        public List<SelfShadowFrameData> SelfShadowFrameList = new List<SelfShadowFrameData>();

        /// <summary>IK表示フレームリスト</summary>
        public List<ShowIKFrameData> ShowIKFrameList = new List<ShowIKFrameData>();

        // ================================================================
        // ボーン名/モーフ名で分類された辞書
        // ================================================================

        /// <summary>ボーン名→フレームリストの辞書</summary>
        public Dictionary<string, List<BoneFrameData>> BoneFramesByName { get; private set; }
            = new Dictionary<string, List<BoneFrameData>>();

        /// <summary>モーフ名→フレームリストの辞書</summary>
        public Dictionary<string, List<MorphFrameData>> MorphFramesByName { get; private set; }
            = new Dictionary<string, List<MorphFrameData>>();

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>ボーン名一覧</summary>
        public IEnumerable<string> BoneNames => BoneFramesByName.Keys;

        /// <summary>モーフ名一覧</summary>
        public IEnumerable<string> MorphNames => MorphFramesByName.Keys;

        /// <summary>最大フレーム番号</summary>
        public uint MaxFrameNumber
        {
            get
            {
                uint max = 0;
                if (BoneFrameList.Count > 0)
                    max = Math.Max(max, BoneFrameList.Max(f => f.FrameNumber));
                if (MorphFrameList.Count > 0)
                    max = Math.Max(max, MorphFrameList.Max(f => f.FrameNumber));
                if (CameraFrameList.Count > 0)
                    max = Math.Max(max, CameraFrameList.Max(f => f.FrameNumber));
                return max;
            }
        }

        /// <summary>ボーンフレーム総数</summary>
        public int TotalBoneFrameCount => BoneFrameList.Count;

        /// <summary>モーフフレーム総数</summary>
        public int TotalMorphFrameCount => MorphFrameList.Count;

        // ================================================================
        // ファイル読み込み
        // ================================================================

        /// <summary>
        /// VMDファイルから読み込み
        /// </summary>
        public static VMDData LoadFromFile(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                return LoadFromStream(fs);
            }
        }

        /// <summary>
        /// バイト配列から読み込み
        /// </summary>
        public static VMDData LoadFromBytes(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            {
                return LoadFromStream(ms);
            }
        }

        /// <summary>
        /// ストリームから読み込み
        /// </summary>
        public static VMDData LoadFromStream(Stream stream)
        {
            var vmd = new VMDData();
            vmd.ReadFromStream(stream);
            return vmd;
        }

        /// <summary>
        /// ストリームからVMDデータを読み込み
        /// </summary>
        private void ReadFromStream(Stream stream)
        {
            // Shift-JISエンコーディングを取得
            Encoding sjis;
            try
            {
                sjis = Encoding.GetEncoding(932);
            }
            catch
            {
                sjis = Encoding.UTF8;
                Debug.LogWarning("Shift-JIS encoding not available, using UTF8");
            }

            using (var reader = new BinaryReader(stream, sjis, leaveOpen: true))
            {
                // ヘッダ読み込み (30 bytes)
                HeaderString = ReadFixedString(reader, 30, sjis);

                // モデル名読み込み (20 bytes)
                ModelName = ReadFixedString(reader, 20, sjis);

                // カメラ・照明データかどうか判定
                IsCamera = ModelName.Contains("カメラ") || ModelName.Contains("照明");

                if (IsCamera)
                {
                    ReadCameraData(reader, sjis);
                }
                else
                {
                    ReadModelData(reader, sjis);
                }

                // 後処理：辞書に分類
                BuildDictionaries();
            }
        }

        /// <summary>
        /// モデル用データの読み込み
        /// </summary>
        private void ReadModelData(BinaryReader reader, Encoding sjis)
        {
            // ボーンフレーム
            int boneCount = TryReadInt32(reader);
            for (int i = 0; i < boneCount; i++)
            {
                var frame = ReadBoneFrame(reader, sjis);
                if (frame != null)
                    BoneFrameList.Add(frame);
            }

            // モーフフレーム
            int morphCount = TryReadInt32(reader);
            for (int i = 0; i < morphCount; i++)
            {
                var frame = ReadMorphFrame(reader, sjis);
                if (frame != null)
                    MorphFrameList.Add(frame);
            }

            // カメラフレーム (通常のVMDには含まれないが念のため)
            int cameraCount = TryReadInt32(reader);
            for (int i = 0; i < cameraCount; i++)
            {
                var frame = ReadCameraFrame(reader);
                if (frame != null)
                    CameraFrameList.Add(frame);
            }

            // ライトフレーム
            int lightCount = TryReadInt32(reader);
            for (int i = 0; i < lightCount; i++)
            {
                var frame = ReadLightFrame(reader);
                if (frame != null)
                    LightFrameList.Add(frame);
            }

            // セルフシャドウフレーム
            int shadowCount = TryReadInt32(reader);
            for (int i = 0; i < shadowCount; i++)
            {
                var frame = ReadSelfShadowFrame(reader);
                if (frame != null)
                    SelfShadowFrameList.Add(frame);
            }

            // IK表示フレーム
            int ikCount = TryReadInt32(reader);
            for (int i = 0; i < ikCount; i++)
            {
                var frame = ReadShowIKFrame(reader, sjis);
                if (frame != null)
                    ShowIKFrameList.Add(frame);
            }
        }

        /// <summary>
        /// カメラ・照明用データの読み込み
        /// </summary>
        private void ReadCameraData(BinaryReader reader, Encoding sjis)
        {
            // カメラフレーム
            int cameraCount = TryReadInt32(reader);
            for (int i = 0; i < cameraCount; i++)
            {
                var frame = ReadCameraFrame(reader);
                if (frame != null)
                    CameraFrameList.Add(frame);
            }

            // ライトフレーム
            int lightCount = TryReadInt32(reader);
            for (int i = 0; i < lightCount; i++)
            {
                var frame = ReadLightFrame(reader);
                if (frame != null)
                    LightFrameList.Add(frame);
            }

            // ソート
            CameraFrameList.Sort();
            LightFrameList.Sort();
        }

        // ================================================================
        // フレームデータ読み込み
        // ================================================================

        private BoneFrameData ReadBoneFrame(BinaryReader reader, Encoding sjis)
        {
            try
            {
                var frame = new BoneFrameData();

                // ボーン名 (15 bytes)
                frame.BoneName = ReadFixedString(reader, 15, sjis);

                // フレーム番号
                frame.FrameNumber = reader.ReadUInt32();

                // 位置
                frame.Position = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle());

                // 回転（クォータニオン）
                frame.Rotation = new Quaternion(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle());

                // 補間データ (64 bytes)
                byte[] interpolation = reader.ReadBytes(64);
                ParseBoneInterpolation(frame, interpolation);

                return frame;
            }
            catch
            {
                return null;
            }
        }

        private void ParseBoneInterpolation(BoneFrameData frame, byte[] data)
        {
            // 補間データを4x4x4配列に変換
            int idx = 0;
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        frame.Interpolation[i][j][k] = data[idx++];
                    }
                }
            }

            // MMMフォーマット補正
            frame.Interpolation[0][0][2] = frame.Interpolation[1][0][1];
            frame.Interpolation[0][0][3] = frame.Interpolation[1][0][2];

            // ベジェ曲線に変換
            for (int i = 0; i < 4; i++)
            {
                frame.Curves[i] = new BezierCurve(
                    new Vector2(frame.Interpolation[0][0][i] / 128f, frame.Interpolation[0][1][i] / 128f),
                    new Vector2(frame.Interpolation[0][2][i] / 128f, frame.Interpolation[0][3][i] / 128f));
            }
        }

        private MorphFrameData ReadMorphFrame(BinaryReader reader, Encoding sjis)
        {
            try
            {
                var frame = new MorphFrameData();
                frame.MorphName = ReadFixedString(reader, 15, sjis);
                frame.FrameNumber = reader.ReadUInt32();
                frame.Weight = reader.ReadSingle();
                return frame;
            }
            catch
            {
                return null;
            }
        }

        private CameraFrameData ReadCameraFrame(BinaryReader reader)
        {
            try
            {
                var frame = new CameraFrameData();
                frame.FrameNumber = reader.ReadUInt32();
                frame.Distance = reader.ReadSingle();

                frame.Position = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle());

                frame.EulerRotation = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle());

                // 補間データ (24 bytes)
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 6; j++)
                    {
                        frame.Interpolation[i][j] = reader.ReadByte();
                    }
                }

                // ベジェ曲線に変換
                for (int i = 0; i < 6; i++)
                {
                    frame.Curves[i] = new BezierCurve(
                        new Vector2(frame.Interpolation[0][i] / 128f, frame.Interpolation[1][i] / 128f),
                        new Vector2(frame.Interpolation[2][i] / 128f, frame.Interpolation[3][i] / 128f));
                }

                frame.FieldOfView = reader.ReadUInt32();
                frame.Perspective = reader.ReadByte() == 0;

                return frame;
            }
            catch
            {
                return null;
            }
        }

        private LightFrameData ReadLightFrame(BinaryReader reader)
        {
            try
            {
                var frame = new LightFrameData();
                frame.FrameNumber = reader.ReadUInt32();

                frame.LightColor = new Color3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle());

                frame.LightPosition = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle());

                return frame;
            }
            catch
            {
                return null;
            }
        }

        private SelfShadowFrameData ReadSelfShadowFrame(BinaryReader reader)
        {
            try
            {
                var frame = new SelfShadowFrameData();
                frame.FrameNumber = reader.ReadUInt32();
                frame.Mode = reader.ReadByte();
                frame.Distance = reader.ReadSingle();
                return frame;
            }
            catch
            {
                return null;
            }
        }

        private ShowIKFrameData ReadShowIKFrame(BinaryReader reader, Encoding sjis)
        {
            try
            {
                var frame = new ShowIKFrameData();
                frame.FrameNumber = reader.ReadUInt32();
                frame.Show = reader.ReadByte() == 1;

                int ikCount = reader.ReadInt32();
                for (int i = 0; i < ikCount; i++)
                {
                    string boneName = ReadFixedString(reader, 20, sjis);
                    bool enabled = reader.ReadByte() == 1;
                    frame.IKList.Add(new IKInfo(boneName, enabled));
                }

                return frame;
            }
            catch
            {
                return null;
            }
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private int TryReadInt32(BinaryReader reader)
        {
            try
            {
                return reader.ReadInt32();
            }
            catch
            {
                return 0;
            }
        }

        private string ReadFixedString(BinaryReader reader, int length, Encoding encoding)
        {
            byte[] bytes = reader.ReadBytes(length);

            // null終端を探す
            int nullIndex = Array.IndexOf(bytes, (byte)0);
            int actualLength = nullIndex >= 0 ? nullIndex : length;

            return encoding.GetString(bytes, 0, actualLength);
        }

        // ================================================================
        // 辞書構築
        // ================================================================

        /// <summary>
        /// ボーン名・モーフ名で分類した辞書を構築
        /// </summary>
        private void BuildDictionaries()
        {
            // ボーンフレームを名前で分類
            BoneFramesByName.Clear();
            foreach (var frame in BoneFrameList)
            {
                if (!BoneFramesByName.ContainsKey(frame.BoneName))
                {
                    BoneFramesByName[frame.BoneName] = new List<BoneFrameData>();
                }
                BoneFramesByName[frame.BoneName].Add(frame);
            }

            // 各ボーンのフレームをソート
            foreach (var list in BoneFramesByName.Values)
            {
                list.Sort();
            }

            // モーフフレームを名前で分類
            MorphFramesByName.Clear();
            foreach (var frame in MorphFrameList)
            {
                if (!MorphFramesByName.ContainsKey(frame.MorphName))
                {
                    MorphFramesByName[frame.MorphName] = new List<MorphFrameData>();
                }
                MorphFramesByName[frame.MorphName].Add(frame);
            }

            // 各モーフのフレームをソート
            foreach (var list in MorphFramesByName.Values)
            {
                list.Sort();
            }
        }

        // ================================================================
        // フレーム取得
        // ================================================================

        /// <summary>
        /// 指定ボーンの指定フレームでの姿勢を取得
        /// </summary>
        public (Vector3 position, Quaternion rotation) GetBonePoseAtFrame(string boneName, float frameNumber)
        {
            if (!BoneFramesByName.TryGetValue(boneName, out var frames) || frames.Count == 0)
            {
                return (Vector3.zero, Quaternion.identity);
            }

            // 前後のキーフレームを探す
            BoneFrameData before = null;
            BoneFrameData after = null;

            foreach (var frame in frames)
            {
                if (frame.FrameNumber <= frameNumber)
                {
                    before = frame;
                }
                else
                {
                    after = frame;
                    break;
                }
            }

            if (before == null) before = frames[0];
            if (after == null) after = before;

            // 補間
            if (before == after || before.FrameNumber == after.FrameNumber)
            {
                return (before.Position, before.Rotation);
            }

            float t = (frameNumber - before.FrameNumber) / (after.FrameNumber - before.FrameNumber);
            return BoneFrameData.Interpolate(before, after, t);
        }

        /// <summary>
        /// 指定モーフの指定フレームでのウェイトを取得
        /// </summary>
        public float GetMorphWeightAtFrame(string morphName, float frameNumber)
        {
            if (!MorphFramesByName.TryGetValue(morphName, out var frames) || frames.Count == 0)
            {
                return 0f;
            }

            MorphFrameData before = null;
            MorphFrameData after = null;

            foreach (var frame in frames)
            {
                if (frame.FrameNumber <= frameNumber)
                {
                    before = frame;
                }
                else
                {
                    after = frame;
                    break;
                }
            }

            if (before == null) before = frames[0];
            if (after == null) after = before;

            if (before == after || before.FrameNumber == after.FrameNumber)
            {
                return before.Weight;
            }

            float t = (frameNumber - before.FrameNumber) / (after.FrameNumber - before.FrameNumber);
            return MorphFrameData.Interpolate(before, after, t);
        }

        // ================================================================
        // クローン
        // ================================================================

        /// <summary>
        /// ディープコピーを作成
        /// </summary>
        public VMDData Clone()
        {
            var clone = new VMDData
            {
                HeaderString = HeaderString,
                ModelName = ModelName,
                IsCamera = IsCamera
            };

            foreach (var f in BoneFrameList)
                clone.BoneFrameList.Add(f.Clone());
            foreach (var f in MorphFrameList)
                clone.MorphFrameList.Add(f.Clone());
            foreach (var f in CameraFrameList)
                clone.CameraFrameList.Add(f.Clone());
            foreach (var f in LightFrameList)
                clone.LightFrameList.Add(f.Clone());
            foreach (var f in SelfShadowFrameList)
                clone.SelfShadowFrameList.Add(f.Clone());
            foreach (var f in ShowIKFrameList)
                clone.ShowIKFrameList.Add(f.Clone());

            clone.BuildDictionaries();
            return clone;
        }
    }
}

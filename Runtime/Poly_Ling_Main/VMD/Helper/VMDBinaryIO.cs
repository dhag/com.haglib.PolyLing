// VMDBinaryIO.cs
// VMD読み込み用のバイナリI/Oヘルパー
// HagLib.Helper.HagDXBinaryReader/Writer から移植・Unity対応版

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// VMD用バイナリリーダー
    /// </summary>
    public class VMDBinaryReader : BinaryReader
    {
        public EncodeType Encode { get; set; } = EncodeType.UTF8;

        private string _peekStringBuffer = null;

        public VMDBinaryReader(Stream input) : base(input)
        {
        }

        public VMDBinaryReader(Stream input, Encoding encoding) : base(input, encoding)
        {
        }

        /// <summary>
        /// Color4を読み込み
        /// </summary>
        public Color4 ReadColor4()
        {
            return new Color4(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
        }

        /// <summary>
        /// Color3を読み込み
        /// </summary>
        public Color3 ReadColor3()
        {
            return new Color3(ReadSingle(), ReadSingle(), ReadSingle());
        }

        /// <summary>
        /// Vector4を読み込み
        /// </summary>
        public Vector4 ReadVector4()
        {
            return new Vector4(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
        }

        /// <summary>
        /// Quaternionを読み込み (X,Y,Z,W順)
        /// </summary>
        public Quaternion ReadQuaternion()
        {
            float x = ReadSingle();
            float y = ReadSingle();
            float z = ReadSingle();
            float w = ReadSingle();
            return new Quaternion(x, y, z, w);
        }

        /// <summary>
        /// Vector3を読み込み
        /// </summary>
        public Vector3 ReadVector3()
        {
            return new Vector3(ReadSingle(), ReadSingle(), ReadSingle());
        }

        /// <summary>
        /// Vector2を読み込み
        /// </summary>
        public Vector2 ReadVector2()
        {
            return new Vector2(ReadSingle(), ReadSingle());
        }

        /// <summary>
        /// floatを読み込み
        /// </summary>
        public float ReadFloat()
        {
            return ReadSingle();
        }

        /// <summary>
        /// 文字列をPeek（次回ReadUTFTextで返される）
        /// </summary>
        public string PeekString()
        {
            _peekStringBuffer = ReadUTFText();
            return _peekStringBuffer;
        }

        /// <summary>
        /// 4+N形式の文字列を読み込み
        /// </summary>
        public string ReadUTFText()
        {
            // Peek済みバッファがあればそれを返す
            if (_peekStringBuffer != null)
            {
                var s = _peekStringBuffer;
                _peekStringBuffer = null;
                return s;
            }

            int length = ReadInt32();
            if (length <= 0) return string.Empty;

            byte[] bytes = ReadBytes(length);

            switch (Encode)
            {
                case EncodeType.UTF16LE:
                    return Encoding.Unicode.GetString(bytes);
                case EncodeType.UTF8:
                    return Encoding.UTF8.GetString(bytes);
                case EncodeType.SJIS:
                    // Shift-JIS (Code Page 932)
                    try
                    {
                        return Encoding.GetEncoding(932).GetString(bytes);
                    }
                    catch
                    {
                        // Fallback to UTF8 if SJIS not available
                        return Encoding.UTF8.GetString(bytes);
                    }
                default:
                    return Encoding.UTF8.GetString(bytes);
            }
        }

        /// <summary>
        /// 固定長の文字列を読み込み（VMDヘッダー用）
        /// </summary>
        public string ReadFixedString(int byteLength, Encoding encoding = null)
        {
            byte[] bytes = ReadBytes(byteLength);
            
            // null終端を探す
            int nullIndex = Array.IndexOf(bytes, (byte)0);
            int actualLength = nullIndex >= 0 ? nullIndex : byteLength;

            if (encoding == null)
            {
                // SJIS (VMDのデフォルト)
                try
                {
                    encoding = Encoding.GetEncoding(932);
                }
                catch
                {
                    encoding = Encoding.UTF8;
                }
            }

            return encoding.GetString(bytes, 0, actualLength);
        }

        /// <summary>
        /// データ数を読み込み（Int32）
        /// </summary>
        public int ReadDataCountRaw()
        {
            return ReadInt32();
        }

        /// <summary>
        /// 文字列リストを読み込み
        /// </summary>
        public List<string> ReadStringList()
        {
            int count = ReadInt32();
            var list = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadUTFText());
            }
            return list;
        }

        /// <summary>
        /// Int32リストを読み込み
        /// </summary>
        public List<int> ReadInt32List()
        {
            int count = ReadInt32();
            var list = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadInt32());
            }
            return list;
        }

        /// <summary>
        /// floatリストを読み込み
        /// </summary>
        public List<float> ReadFloatList()
        {
            int count = ReadInt32();
            var list = new List<float>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadSingle());
            }
            return list;
        }

        /// <summary>
        /// Vector3リストを読み込み
        /// </summary>
        public List<Vector3> ReadVector3List()
        {
            int count = ReadInt32();
            var list = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadVector3());
            }
            return list;
        }

        /// <summary>
        /// Quaternionリストを読み込み
        /// </summary>
        public List<Quaternion> ReadQuaternionList()
        {
            int count = ReadInt32();
            var list = new List<Quaternion>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadQuaternion());
            }
            return list;
        }

        /// <summary>
        /// バイト配列を読み込み（長さプレフィックス付き）
        /// </summary>
        public byte[] ReadByteArray()
        {
            int count = ReadInt32();
            return ReadBytes(count);
        }
    }

    /// <summary>
    /// VMD用バイナリライター
    /// </summary>
    public class VMDBinaryWriter : BinaryWriter
    {
        public EncodeType Encode { get; set; } = EncodeType.UTF8;

        public VMDBinaryWriter(Stream output) : base(output)
        {
        }

        public VMDBinaryWriter(Stream output, Encoding encoding) : base(output, encoding)
        {
        }

        /// <summary>
        /// Vector2を書き込み
        /// </summary>
        public void Write(Vector2 v)
        {
            Write(v.x);
            Write(v.y);
        }

        /// <summary>
        /// Vector3を書き込み
        /// </summary>
        public void Write(Vector3 v)
        {
            Write(v.x);
            Write(v.y);
            Write(v.z);
        }

        /// <summary>
        /// Vector4を書き込み
        /// </summary>
        public void Write(Vector4 v)
        {
            Write(v.x);
            Write(v.y);
            Write(v.z);
            Write(v.w);
        }

        /// <summary>
        /// Quaternionを書き込み (X,Y,Z,W順)
        /// </summary>
        public void Write(Quaternion q)
        {
            Write(q.x);
            Write(q.y);
            Write(q.z);
            Write(q.w);
        }

        /// <summary>
        /// Color3を書き込み
        /// </summary>
        public void Write(Color3 c)
        {
            Write(c.Red);
            Write(c.Green);
            Write(c.Blue);
        }

        /// <summary>
        /// Color4を書き込み
        /// </summary>
        public void Write(Color4 c)
        {
            Write(c.Red);
            Write(c.Green);
            Write(c.Blue);
            Write(c.Alpha);
        }

        /// <summary>
        /// 4+N形式で文字列を書き込み
        /// </summary>
        public void WriteUTFText(string s)
        {
            if (s == null) s = string.Empty;

            byte[] bytes;
            switch (Encode)
            {
                case EncodeType.UTF16LE:
                    bytes = Encoding.Unicode.GetBytes(s);
                    break;
                case EncodeType.SJIS:
                    try
                    {
                        bytes = Encoding.GetEncoding(932).GetBytes(s);
                    }
                    catch
                    {
                        bytes = Encoding.UTF8.GetBytes(s);
                    }
                    break;
                default:
                    bytes = Encoding.UTF8.GetBytes(s);
                    break;
            }

            Write(bytes.Length);
            Write(bytes);
        }

        /// <summary>
        /// 固定長文字列を書き込み（VMDヘッダー用）
        /// </summary>
        public void WriteFixedString(string s, int byteLength, Encoding encoding = null)
        {
            if (s == null) s = string.Empty;

            if (encoding == null)
            {
                try
                {
                    encoding = Encoding.GetEncoding(932);
                }
                catch
                {
                    encoding = Encoding.UTF8;
                }
            }

            byte[] bytes = encoding.GetBytes(s);
            byte[] buffer = new byte[byteLength];
            
            int copyLength = Math.Min(bytes.Length, byteLength);
            Array.Copy(bytes, buffer, copyLength);
            
            Write(buffer);
        }

        /// <summary>
        /// データ数を書き込み
        /// </summary>
        public void WriteDataCountRaw(int count)
        {
            Write(count);
        }

        /// <summary>
        /// Vector3リストを書き込み
        /// </summary>
        public void WriteList(List<Vector3> list)
        {
            Write(list.Count);
            foreach (var v in list)
            {
                Write(v);
            }
        }

        /// <summary>
        /// Quaternionリストを書き込み
        /// </summary>
        public void WriteList(List<Quaternion> list)
        {
            Write(list.Count);
            foreach (var q in list)
            {
                Write(q);
            }
        }

        /// <summary>
        /// Int32リストを書き込み
        /// </summary>
        public void WriteInt32List(List<int> list)
        {
            Write(list.Count);
            foreach (var v in list)
            {
                Write(v);
            }
        }

        /// <summary>
        /// floatリストを書き込み
        /// </summary>
        public void WriteFloatList(List<float> list)
        {
            Write(list.Count);
            foreach (var v in list)
            {
                Write(v);
            }
        }

        /// <summary>
        /// バイト配列を書き込み（長さプレフィックス付き）
        /// </summary>
        public void WriteByteArray(byte[] bytes)
        {
            Write(bytes.Length);
            Write(bytes);
        }
    }
}

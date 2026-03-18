// Remote/RemoteImageSerializer.cs
// 画像リスト (PLRI) のバイナリシリアライザ
//
// 複数画像をサイズ違いで1フレームに格納し、バイナリフレームで送信する。
// 画像はPNG/JPEGのエンコード済みバイト列をそのまま格納するため、
// クライアント側でデコード不要（Blob→URLで直接表示可能）。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Poly_Ling.Remote
{
    /// <summary>
    /// 送信用画像エントリ
    /// </summary>
    public class ImageEntry
    {
        public ushort Id;
        public ImageFormat Format;
        public uint Width;
        public uint Height;
        public byte[] Data; // PNG/JPEGエンコード済みバイト列
    }

    /// <summary>
    /// 画像リストシリアライザ
    /// </summary>
    public static class RemoteImageSerializer
    {
        // ================================================================
        // Serialize: ImageEntry[] → byte[]
        // ================================================================

        /// <summary>
        /// 画像リストをバイナリにシリアライズ
        /// </summary>
        public static byte[] Serialize(IList<ImageEntry> images)
        {
            if (images == null || images.Count == 0) return null;

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                // ヘッダ 8B
                w.Write(RemoteMagic.Image);             // Magic 4B
                w.Write((byte)1);                       // Version 1B
                w.Write((ushort)images.Count);          // ImageCount 2B
                w.Write((byte)0);                       // Reserved 1B

                // 各画像エントリ
                for (int i = 0; i < images.Count; i++)
                {
                    var img = images[i];
                    w.Write(img.Id);                    // ImageId 2B
                    w.Write((byte)img.Format);          // Format 1B
                    w.Write(img.Width);                 // Width 4B
                    w.Write(img.Height);                // Height 4B
                    w.Write((uint)img.Data.Length);      // DataLength 4B
                    w.Write(img.Data);                  // ImageData
                }

                return ms.ToArray();
            }
        }

        /// <summary>
        /// 単一画像をリストとしてシリアライズ（便宜メソッド）
        /// </summary>
        public static byte[] SerializeSingle(ushort id, ImageFormat format,
            uint width, uint height, byte[] data)
        {
            return Serialize(new[] { new ImageEntry
            {
                Id = id, Format = format,
                Width = width, Height = height, Data = data
            }});
        }

        // ================================================================
        // Texture2Dからの変換
        // ================================================================

        /// <summary>
        /// Texture2DをPNGエンコードしてImageEntryに変換
        /// </summary>
        public static ImageEntry FromTexture2D(Texture2D tex, ushort id)
        {
            return new ImageEntry
            {
                Id = id,
                Format = ImageFormat.PNG,
                Width = (uint)tex.width,
                Height = (uint)tex.height,
                Data = tex.EncodeToPNG()
            };
        }

        /// <summary>
        /// Texture2DをJPEGエンコードしてImageEntryに変換
        /// </summary>
        public static ImageEntry FromTexture2DJPEG(Texture2D tex, ushort id, int quality = 75)
        {
            return new ImageEntry
            {
                Id = id,
                Format = ImageFormat.JPEG,
                Width = (uint)tex.width,
                Height = (uint)tex.height,
                Data = tex.EncodeToJPG(quality)
            };
        }

        // ================================================================
        // Deserialize: byte[] → ImageEntry[]
        // ================================================================

        /// <summary>
        /// バイナリデータから画像リストを復元
        /// </summary>
        public static ImageEntry[] Deserialize(byte[] data)
        {
            if (data == null || data.Length < ImageListHeader.Size) return null;

            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                uint magic = r.ReadUInt32();
                if (magic != RemoteMagic.Image) return null;

                byte version = r.ReadByte();
                ushort count = r.ReadUInt16();
                r.ReadByte(); // reserved

                var result = new ImageEntry[count];
                for (int i = 0; i < count; i++)
                {
                    ushort imgId = r.ReadUInt16();
                    ImageFormat fmt = (ImageFormat)r.ReadByte();
                    uint w = r.ReadUInt32();
                    uint h = r.ReadUInt32();
                    uint dataLen = r.ReadUInt32();
                    byte[] imgData = r.ReadBytes((int)dataLen);

                    result[i] = new ImageEntry
                    {
                        Id = imgId,
                        Format = fmt,
                        Width = w,
                        Height = h,
                        Data = imgData
                    };
                }

                return result;
            }
        }
    }
}

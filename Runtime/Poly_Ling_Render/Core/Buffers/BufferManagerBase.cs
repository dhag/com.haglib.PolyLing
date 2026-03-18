// Assets/Editor/Poly_Ling/Core/Buffers/BufferManagerBase.cs
// バッファ管理の基底クラス
// ComputeBufferのライフサイクル管理

using System;
using UnityEngine;

namespace Poly_Ling.Core
{
    /// <summary>
    /// ComputeBufferの管理基底クラス
    /// </summary>
    public abstract class BufferManagerBase : IDisposable
    {
        // ============================================================
        // 状態
        // ============================================================

        protected bool _isInitialized = false;
        protected bool _disposed = false;

        /// <summary>初期化済みか</summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>破棄済みか</summary>
        public bool IsDisposed => _disposed;

        // ============================================================
        // 抽象メソッド
        // ============================================================

        /// <summary>
        /// バッファを初期化
        /// </summary>
        public abstract void Initialize();

        /// <summary>
        /// バッファを再構築（サイズ変更時）
        /// </summary>
        public abstract void Rebuild();

        /// <summary>
        /// バッファをクリア
        /// </summary>
        public abstract void Clear();

        // ============================================================
        // ユーティリティ
        // ============================================================

        /// <summary>
        /// ComputeBufferを安全に作成
        /// </summary>
        protected ComputeBuffer CreateBuffer<T>(int count, ComputeBufferType type = ComputeBufferType.Default) where T : struct
        {
            if (count <= 0)
                return null;

            int stride = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            return new ComputeBuffer(count, stride, type);
        }

        /// <summary>
        /// ComputeBufferを安全に作成（ストライド指定）
        /// </summary>
        protected ComputeBuffer CreateBuffer(int count, int stride, ComputeBufferType type = ComputeBufferType.Default)
        {
            if (count <= 0 || stride <= 0)
                return null;

            return new ComputeBuffer(count, stride, type);
        }

        /// <summary>
        /// ComputeBufferを安全に解放
        /// </summary>
        protected void ReleaseBuffer(ref ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }
        }

        /// <summary>
        /// バッファサイズが足りない場合に再作成
        /// </summary>
        protected bool EnsureBufferCapacity<T>(ref ComputeBuffer buffer, int requiredCount, ComputeBufferType type = ComputeBufferType.Default) where T : struct
        {
            if (buffer != null && buffer.count >= requiredCount)
                return false; // 再作成不要

            ReleaseBuffer(ref buffer);
            buffer = CreateBuffer<T>(requiredCount, type);
            return true; // 再作成した
        }

        /// <summary>
        /// バッファサイズが足りない場合に再作成（ストライド指定）
        /// </summary>
        protected bool EnsureBufferCapacity(ref ComputeBuffer buffer, int requiredCount, int stride, ComputeBufferType type = ComputeBufferType.Default)
        {
            if (buffer != null && buffer.count >= requiredCount)
                return false;

            ReleaseBuffer(ref buffer);
            buffer = CreateBuffer(requiredCount, stride, type);
            return true;
        }

        // ============================================================
        // IDisposable
        // ============================================================

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Clear();
                }
                _disposed = true;
                _isInitialized = false;
            }
        }

        ~BufferManagerBase()
        {
            Dispose(false);
        }
    }

    /// <summary>
    /// 単一バッファ管理クラス
    /// </summary>
    /// <typeparam name="T">要素型</typeparam>
    public class SingleBufferManager<T> : BufferManagerBase where T : struct
    {
        private ComputeBuffer _buffer;
        private T[] _data;
        private int _capacity;
        private int _count;
        private ComputeBufferType _bufferType;

        /// <summary>バッファ</summary>
        public ComputeBuffer Buffer => _buffer;

        /// <summary>CPUデータ</summary>
        public T[] Data => _data;

        /// <summary>要素数</summary>
        public int Count => _count;

        /// <summary>容量</summary>
        public int Capacity => _capacity;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="initialCapacity">初期容量</param>
        /// <param name="bufferType">バッファタイプ</param>
        public SingleBufferManager(int initialCapacity = 1024, ComputeBufferType bufferType = ComputeBufferType.Default)
        {
            _capacity = initialCapacity;
            _bufferType = bufferType;
        }

        public override void Initialize()
        {
            if (_isInitialized)
                return;

            _data = new T[_capacity];
            _buffer = CreateBuffer<T>(_capacity, _bufferType);
            _count = 0;
            _isInitialized = true;
        }

        public override void Rebuild()
        {
            ReleaseBuffer(ref _buffer);
            _buffer = CreateBuffer<T>(_capacity, _bufferType);

            if (_data != null && _count > 0)
            {
                _buffer.SetData(_data, 0, 0, _count);
            }
        }

        public override void Clear()
        {
            ReleaseBuffer(ref _buffer);
            _data = null;
            _count = 0;
            _isInitialized = false;
        }

        /// <summary>
        /// 容量を確保
        /// </summary>
        public void EnsureCapacity(int requiredCapacity)
        {
            if (_capacity >= requiredCapacity)
                return;

            // 1.5倍または要求サイズの大きい方
            int newCapacity = Math.Max(_capacity * 3 / 2, requiredCapacity);
            Resize(newCapacity);
        }

        /// <summary>
        /// サイズ変更
        /// </summary>
        public void Resize(int newCapacity)
        {
            if (newCapacity <= 0)
                return;

            _capacity = newCapacity;

            // CPU配列リサイズ
            T[] newData = new T[_capacity];
            if (_data != null)
            {
                int copyCount = Math.Min(_count, _capacity);
                Array.Copy(_data, newData, copyCount);
            }
            _data = newData;

            // GPUバッファ再作成
            Rebuild();
        }

        /// <summary>
        /// 要素数を設定（容量内）
        /// </summary>
        public void SetCount(int count)
        {
            EnsureCapacity(count);
            _count = count;
        }

        /// <summary>
        /// データを設定
        /// </summary>
        public void SetData(T[] data, int count = -1)
        {
            if (data == null)
            {
                _count = 0;
                return;
            }

            int dataCount = count >= 0 ? count : data.Length;
            EnsureCapacity(dataCount);

            Array.Copy(data, _data, dataCount);
            _count = dataCount;
        }

        /// <summary>
        /// GPUにアップロード
        /// </summary>
        public void Upload()
        {
            if (_buffer == null || _data == null || _count == 0)
                return;

            _buffer.SetData(_data, 0, 0, _count);
        }

        /// <summary>
        /// 部分アップロード
        /// </summary>
        public void Upload(int start, int count)
        {
            if (_buffer == null || _data == null)
                return;

            int uploadCount = Math.Min(count, _count - start);
            if (uploadCount <= 0)
                return;

            _buffer.SetData(_data, start, start, uploadCount);
        }

        /// <summary>
        /// GPUからダウンロード
        /// </summary>
        public void Download()
        {
            if (_buffer == null || _data == null || _count == 0)
                return;

            _buffer.GetData(_data, 0, 0, _count);
        }
    }
}

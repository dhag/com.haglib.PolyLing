// CubicSpline.cs
// 自然3次スプライン（1次元）。NCSHAGLIB/Helper/Spline/Spline.cs の移植。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 媒介変数 t は 0 〜 (点数-1)。t=i のとき元の i 番目の点を通る。

using System.Collections.Generic;

namespace Poly_Ling.PrimitiveMesh
{
    public sealed class CubicSpline
    {
        private int     _num;
        private float[] _a, _b, _c, _d;

        public CubicSpline(IReadOnlyList<float> sp) { Init(sp); }

        /// <summary>制御点の最大媒介変数（点数-1）。</summary>
        public int MaxT => _num;

        public void Init(IReadOnlyList<float> sp)
        {
            int n = (sp == null) ? 0 : sp.Count;
            _a = new float[n < 1 ? 1 : n];
            _b = new float[n < 1 ? 1 : n];
            _c = new float[n < 1 ? 1 : n];
            _d = new float[n < 1 ? 1 : n];
            _num = n - 1;
            if (n == 0) { _num = 0; return; }

            var w = new float[n];

            for (int i = 0; i <= _num; i++) _a[i] = sp[i];

            // 2次係数
            _c[0] = _c[_num] = 0f;
            for (int i = 1; i < _num; i++)
                _c[i] = 3f * (_a[i - 1] - 2f * _a[i] + _a[i + 1]);

            w[0] = 0f;
            for (int i = 1; i < _num; i++)
            {
                float tmp = 4f - w[i - 1];
                _c[i] = (_c[i] - _c[i - 1]) / tmp;
                w[i]  = 1f / tmp;
            }
            for (int i = _num - 1; i > 0; i--)
                _c[i] = _c[i] - _c[i + 1] * w[i];

            // 1次・3次係数
            _b[_num] = _d[_num] = 0f;
            for (int i = 0; i < _num; i++)
            {
                _d[i] = (_c[i + 1] - _c[i]) / 3f;
                _b[i] = _a[i + 1] - _a[i] - _c[i] - _d[i];
            }
        }

        /// <summary>媒介変数 t（0〜点数-1）に対する値。</summary>
        public float GetValue(float t)
        {
            if (_a == null || _a.Length == 0) return 0f;
            if (_num <= 0) return _a[0];

            int j = (int)System.Math.Floor(t);
            if (j < 0) j = 0;
            else if (j >= _num) j = _num - 1;

            float dt = t - j;
            return _a[j] + (_b[j] + (_c[j] + _d[j] * dt) * dt) * dt;
        }
    }
}

// FbxMath.cs
// ============================================================
// FBX 読み込み（段 8）の倍精度の数学。ufbx 0.23.1 と同じ式（行番号は ufbx.c）。
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md 段 8（8.5）。
//   DMat … 3x4 の行列（列ベクトル。4 行目は 0 0 0 1）。ufbx_matrix と同じ。
//   DTransform … 移動・回転（四元数）・拡大。ufbx_transform と同じ。
//   euler_to_quat 31574、quat_to_euler 31631、quat_rotate_vec3 31562、mul_quat 22665、
//   transform_to_matrix 31836、matrix_to_transform 31862、matrix_mul 31731、invert 31764、slerp 31527。
// ============================================================

using System;
using UnityEngine;

namespace Poly_Ling.Fbx
{
    public struct DVec3
    {
        public double x, y, z;
        public DVec3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static readonly DVec3 Zero = new DVec3(0, 0, 0);
        public static readonly DVec3 One  = new DVec3(1, 1, 1);
        public bool IsZero => x == 0.0 && y == 0.0 && z == 0.0;
        public bool IsOne  => x == 1.0 && y == 1.0 && z == 1.0;
        public double this[int i]
        {
            get => i == 0 ? x : i == 1 ? y : z;
            set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; }
        }
        public static DVec3 operator +(DVec3 a, DVec3 b) => new DVec3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static DVec3 operator -(DVec3 a, DVec3 b) => new DVec3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static DVec3 Cross(DVec3 a, DVec3 b) => new DVec3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public double Length => Math.Sqrt(x * x + y * y + z * z);
    }

    public struct DQuat
    {
        public double x, y, z, w;
        public DQuat(double x, double y, double z, double w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static readonly DQuat Identity = new DQuat(0, 0, 0, 1);
        public bool IsIdentity => x == 0.0 && y == 0.0 && z == 0.0 && w == 1.0;
    }

    public struct DTransform
    {
        public DVec3 T;
        public DQuat Q;
        public DVec3 S;
        public static DTransform Identity => new DTransform { T = DVec3.Zero, Q = DQuat.Identity, S = DVec3.One };
        public bool IsIdentity => T.IsZero && Q.IsIdentity && S.IsOne;
    }

    /// <summary>3x4 行列（ufbx_matrix）。m[r][c] を mRC で持つ。</summary>
    public struct DMat
    {
        public double m00, m10, m20, m01, m11, m21, m02, m12, m22, m03, m13, m23;

        public static DMat Identity => new DMat { m00 = 1, m11 = 1, m22 = 1 };

        public bool AllZero =>
            m00 == 0 && m10 == 0 && m20 == 0 && m01 == 0 && m11 == 0 && m21 == 0 &&
            m02 == 0 && m12 == 0 && m22 == 0 && m03 == 0 && m13 == 0 && m23 == 0;

        /// <summary>16 個の倍精度（列優先、FBX の Matrix・Transform の並び）から。4 行目は捨てる（ufbx.c 13960）。</summary>
        public static DMat FromColumnMajor16(double[] a)
        {
            return new DMat
            {
                m00 = a[0], m10 = a[1], m20 = a[2],
                m01 = a[4], m11 = a[5], m21 = a[6],
                m02 = a[8], m12 = a[9], m22 = a[10],
                m03 = a[12], m13 = a[13], m23 = a[14],
            };
        }

        public static DMat Mul(in DMat a, in DMat b)
        {
            DMat d;
            d.m03 = a.m00 * b.m03 + a.m01 * b.m13 + a.m02 * b.m23 + a.m03;
            d.m13 = a.m10 * b.m03 + a.m11 * b.m13 + a.m12 * b.m23 + a.m13;
            d.m23 = a.m20 * b.m03 + a.m21 * b.m13 + a.m22 * b.m23 + a.m23;
            d.m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20;
            d.m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20;
            d.m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20;
            d.m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21;
            d.m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21;
            d.m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21;
            d.m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22;
            d.m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22;
            d.m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22;
            return d;
        }

        public double Determinant =>
            -m02 * m11 * m20 + m01 * m12 * m20 + m02 * m10 * m21
            - m00 * m12 * m21 - m01 * m10 * m22 + m00 * m11 * m22;

        public static DMat Invert(in DMat m)
        {
            double det = m.Determinant;
            if (Math.Abs(det) <= FbxMath.Epsilon) return default;
            double r = 1.0 / det;
            DMat o;
            o.m00 = (-m.m12 * m.m21 + m.m11 * m.m22) * r;
            o.m10 = (+m.m12 * m.m20 - m.m10 * m.m22) * r;
            o.m20 = (-m.m11 * m.m20 + m.m10 * m.m21) * r;
            o.m01 = (+m.m02 * m.m21 - m.m01 * m.m22) * r;
            o.m11 = (-m.m02 * m.m20 + m.m00 * m.m22) * r;
            o.m21 = (+m.m01 * m.m20 - m.m00 * m.m21) * r;
            o.m02 = (-m.m02 * m.m11 + m.m01 * m.m12) * r;
            o.m12 = (+m.m02 * m.m10 - m.m00 * m.m12) * r;
            o.m22 = (-m.m01 * m.m10 + m.m00 * m.m11) * r;
            o.m03 = (m.m03 * m.m12 * m.m21 - m.m02 * m.m13 * m.m21 - m.m03 * m.m11 * m.m22 + m.m01 * m.m13 * m.m22 + m.m02 * m.m11 * m.m23 - m.m01 * m.m12 * m.m23) * r;
            o.m13 = (m.m02 * m.m13 * m.m20 - m.m03 * m.m12 * m.m20 + m.m03 * m.m10 * m.m22 - m.m00 * m.m13 * m.m22 - m.m02 * m.m10 * m.m23 + m.m00 * m.m12 * m.m23) * r;
            o.m23 = (m.m03 * m.m11 * m.m20 - m.m01 * m.m13 * m.m20 - m.m03 * m.m10 * m.m21 + m.m00 * m.m13 * m.m21 + m.m01 * m.m10 * m.m23 - m.m00 * m.m11 * m.m23) * r;
            return o;
        }

        public DVec3 Col(int c) =>
            c == 0 ? new DVec3(m00, m10, m20) : c == 1 ? new DVec3(m01, m11, m21) : c == 2 ? new DVec3(m02, m12, m22) : new DVec3(m03, m13, m23);

        public DVec3 TransformPoint(DVec3 v) => new DVec3(
            m00 * v.x + m01 * v.y + m02 * v.z + m03,
            m10 * v.x + m11 * v.y + m12 * v.z + m13,
            m20 * v.x + m21 * v.y + m22 * v.z + m23);

        /// <summary>Unity の Matrix4x4 へ（以前の口と同じく float へ丸める）。</summary>
        public Matrix4x4 ToUnity()
        {
            var r = Matrix4x4.identity;
            r[0, 0] = (float)m00; r[1, 0] = (float)m10; r[2, 0] = (float)m20;
            r[0, 1] = (float)m01; r[1, 1] = (float)m11; r[2, 1] = (float)m21;
            r[0, 2] = (float)m02; r[1, 2] = (float)m12; r[2, 2] = (float)m22;
            r[0, 3] = (float)m03; r[1, 3] = (float)m13; r[2, 3] = (float)m23;
            return r;
        }
    }

    public static class FbxMath
    {
        /// <summary>UFBX_EPSILON（倍精度、ufbx.c 70）。</summary>
        public const double Epsilon = 1.4916681462400413e-154;
        private const double DegToRad = 3.14159265358979323846 / 180.0;
        private const double RadToDeg = 180.0 / 3.14159265358979323846;
        private const double Pi = 3.14159265358979323846;

        public static DQuat EulerToQuat(DVec3 v, int order)
        {
            double vx = v.x * (DegToRad * 0.5);
            double vy = v.y * (DegToRad * 0.5);
            double vz = v.z * (DegToRad * 0.5);
            double cx = Math.Cos(vx), sx = Math.Sin(vx);
            double cy = Math.Cos(vy), sy = Math.Sin(vy);
            double cz = Math.Cos(vz), sz = Math.Sin(vz);
            DQuat q;
            switch (order)
            {
                case 0: // XYZ
                    q.x = -cx * sy * sz + cy * cz * sx; q.y = cx * cz * sy + cy * sx * sz;
                    q.z = cx * cy * sz - cz * sx * sy;  q.w = cx * cy * cz + sx * sy * sz; break;
                case 1: // XZY
                    q.x = cx * sy * sz + cy * cz * sx;  q.y = cx * cz * sy + cy * sx * sz;
                    q.z = cx * cy * sz - cz * sx * sy;  q.w = cx * cy * cz - sx * sy * sz; break;
                case 2: // YZX
                    q.x = -cx * sy * sz + cy * cz * sx; q.y = cx * cz * sy - cy * sx * sz;
                    q.z = cx * cy * sz + cz * sx * sy;  q.w = cx * cy * cz + sx * sy * sz; break;
                case 3: // YXZ
                    q.x = -cx * sy * sz + cy * cz * sx; q.y = cx * cz * sy + cy * sx * sz;
                    q.z = cx * cy * sz + cz * sx * sy;  q.w = cx * cy * cz - sx * sy * sz; break;
                case 4: // ZXY
                    q.x = cx * sy * sz + cy * cz * sx;  q.y = cx * cz * sy - cy * sx * sz;
                    q.z = cx * cy * sz - cz * sx * sy;  q.w = cx * cy * cz + sx * sy * sz; break;
                case 5: // ZYX
                    q.x = cx * sy * sz + cy * cz * sx;  q.y = cx * cz * sy - cy * sx * sz;
                    q.z = cx * cy * sz + cz * sx * sy;  q.w = cx * cy * cz - sx * sy * sz; break;
                default:
                    q = DQuat.Identity; break;
            }
            return q;
        }

        public static DVec3 QuatToEuler(DQuat q, int order)
        {
            const double eps = 0.999999999;
            double vx, vy, vz, t;
            double qx = q.x, qy = q.y, qz = q.z, qw = q.w;
            switch (order)
            {
                case 0:
                    t = 2.0 * (qw * qy - qx * qz);
                    if (Math.Abs(t) < eps) { vy = Math.Asin(t); vz = Math.Atan2(2.0 * (qw * qz + qx * qy), 2.0 * (qw * qw + qx * qx) - 1.0); vx = -Math.Atan2(-2.0 * (qw * qx + qy * qz), 2.0 * (qw * qw + qz * qz) - 1.0); }
                    else { vy = CopySign(Pi * 0.5, t); vz = Math.Atan2(-2.0 * t * (qw * qx - qy * qz), t * (2.0 * qw * qy + 2.0 * qx * qz)); vx = 0.0; }
                    break;
                case 1:
                    t = 2.0 * (qw * qz + qx * qy);
                    if (Math.Abs(t) < eps) { vz = Math.Asin(t); vy = Math.Atan2(2.0 * (qw * qy - qx * qz), 2.0 * (qw * qw + qx * qx) - 1.0); vx = -Math.Atan2(-2.0 * (qw * qx - qy * qz), 2.0 * (qw * qw + qy * qy) - 1.0); }
                    else { vz = CopySign(Pi * 0.5, t); vy = Math.Atan2(2.0 * t * (qw * qx + qy * qz), -t * (2.0 * qx * qy - 2.0 * qw * qz)); vx = 0.0; }
                    break;
                case 2:
                    t = 2.0 * (qw * qz - qx * qy);
                    if (Math.Abs(t) < eps) { vz = Math.Asin(t); vx = Math.Atan2(2.0 * (qw * qx + qy * qz), 2.0 * (qw * qw + qy * qy) - 1.0); vy = -Math.Atan2(-2.0 * (qw * qy + qx * qz), 2.0 * (qw * qw + qx * qx) - 1.0); }
                    else { vz = CopySign(Pi * 0.5, t); vx = Math.Atan2(-2.0 * t * (qw * qy - qx * qz), t * (2.0 * qw * qz + 2.0 * qx * qy)); vy = 0.0; }
                    break;
                case 3:
                    t = 2.0 * (qw * qx + qy * qz);
                    if (Math.Abs(t) < eps) { vx = Math.Asin(t); vz = Math.Atan2(2.0 * (qw * qz - qx * qy), 2.0 * (qw * qw + qy * qy) - 1.0); vy = -Math.Atan2(-2.0 * (qw * qy - qx * qz), 2.0 * (qw * qw + qz * qz) - 1.0); }
                    else { vx = CopySign(Pi * 0.5, t); vz = Math.Atan2(2.0 * t * (qw * qy + qx * qz), -t * (2.0 * qy * qz - 2.0 * qw * qx)); vy = 0.0; }
                    break;
                case 4:
                    t = 2.0 * (qw * qx - qy * qz);
                    if (Math.Abs(t) < eps) { vx = Math.Asin(t); vy = Math.Atan2(2.0 * (qw * qy + qx * qz), 2.0 * (qw * qw + qz * qz) - 1.0); vz = -Math.Atan2(-2.0 * (qw * qz + qx * qy), 2.0 * (qw * qw + qy * qy) - 1.0); }
                    else { vx = CopySign(Pi * 0.5, t); vy = Math.Atan2(-2.0 * t * (qw * qz - qx * qy), t * (2.0 * qw * qx + 2.0 * qy * qz)); vz = 0.0; }
                    break;
                case 5:
                    t = 2.0 * (qw * qy + qx * qz);
                    if (Math.Abs(t) < eps) { vy = Math.Asin(t); vx = Math.Atan2(2.0 * (qw * qx - qy * qz), 2.0 * (qw * qw + qz * qz) - 1.0); vz = -Math.Atan2(-2.0 * (qw * qz - qx * qy), 2.0 * (qw * qw + qx * qx) - 1.0); }
                    else { vy = CopySign(Pi * 0.5, t); vx = Math.Atan2(2.0 * t * (qw * qz + qx * qy), -t * (2.0 * qx * qz - 2.0 * qw * qy)); vz = 0.0; }
                    break;
                default:
                    vx = vy = vz = 0.0; break;
            }
            return new DVec3(vx * RadToDeg, vy * RadToDeg, vz * RadToDeg);
        }

        private static double CopySign(double mag, double sign) => sign < 0 || (sign == 0 && double.IsNegative(sign)) ? -Math.Abs(mag) : Math.Abs(mag);

        public static DQuat MulQuat(DQuat a, DQuat b) => new DQuat(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        public static DVec3 QuatRotate(DQuat q, DVec3 v)
        {
            double xy = q.x * v.y - q.y * v.x;
            double xz = q.x * v.z - q.z * v.x;
            double yz = q.y * v.z - q.z * v.y;
            return new DVec3(
                2.0 * (+q.w * yz + q.y * xy + q.z * xz) + v.x,
                2.0 * (-q.x * xy - q.w * xz + q.z * yz) + v.y,
                2.0 * (-q.x * xz - q.y * yz + q.w * xy) + v.z);
        }

        public static DQuat Slerp(DQuat a, DQuat b, double t)
        {
            double dot = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
            if (dot < 0.0) { dot = -dot; b.x = -b.x; b.y = -b.y; b.z = -b.z; b.w = -b.w; }
            double omega = Math.Acos(Math.Min(Math.Max(dot, 0.0), 1.0));
            if (omega <= 1.175494351e-38f) return a;
            double rcp = 1.0 / Math.Sin(omega);
            double af = Math.Sin((1.0 - t) * omega) * rcp;
            double bf = Math.Sin(t * omega) * rcp;
            double x = af * a.x + bf * b.x, y = af * a.y + bf * b.y, z = af * a.z + bf * b.z, w = af * a.w + bf * b.w;
            double rl = 1.0 / Math.Sqrt(x * x + y * y + z * z + w * w);
            return new DQuat(x * rl, y * rl, z * rl, w * rl);
        }

        public static DMat ToMatrix(in DTransform t)
        {
            var q = t.Q;
            double sx = 2.0 * t.S.x, sy = 2.0 * t.S.y, sz = 2.0 * t.S.z;
            double xx = q.x * q.x, xy = q.x * q.y, xz = q.x * q.z, xw = q.x * q.w;
            double yy = q.y * q.y, yz = q.y * q.z, yw = q.y * q.w;
            double zz = q.z * q.z, zw = q.z * q.w;
            DMat m;
            m.m00 = sx * (-yy - zz + 0.5);
            m.m10 = sx * (+xy + zw);
            m.m20 = sx * (-yw + xz);
            m.m01 = sy * (-zw + xy);
            m.m11 = sy * (-xx - zz + 0.5);
            m.m21 = sy * (+xw + yz);
            m.m02 = sz * (+xz + yw);
            m.m12 = sz * (-xw + yz);
            m.m22 = sz * (-xx - yy + 0.5);
            m.m03 = t.T.x; m.m13 = t.T.y; m.m23 = t.T.z;
            return m;
        }

        public static DMat ToUnscaledMatrix(DTransform t) { t.S = DVec3.One; return ToMatrix(t); }

        public static DTransform ToTransform(in DMat m)
        {
            double det = m.Determinant;
            DTransform t;
            t.T = m.Col(3);
            t.S = new DVec3(m.Col(0).Length, m.Col(1).Length, m.Col(2).Length);
            double signX = 1, signY = 1, signZ = 1;
            if (det < 0.0)
            {
                if (t.S.x > 0.0) signX = -1;
                else if (t.S.y > 0.0) signY = -1;
                else if (t.S.z > 0.0) signZ = -1;
            }
            DVec3 x = Mul(m.Col(0), t.S.x > 0.0 ? signX / t.S.x : 0.0);
            DVec3 y = Mul(m.Col(1), t.S.y > 0.0 ? signY / t.S.y : 0.0);
            DVec3 z = Mul(m.Col(2), t.S.z > 0.0 ? signZ / t.S.z : 0.0);
            double trace = x.x + y.y + z.z;
            DQuat r;
            if (trace > 0.0)
            {
                double a = Math.Sqrt(Math.Max(0.0, trace + 1.0)), b = a != 0.0 ? 0.5 / a : 0.0;
                r = new DQuat((y.z - z.y) * b, (z.x - x.z) * b, (x.y - y.x) * b, 0.5 * a);
            }
            else if (x.x > y.y && x.x > z.z)
            {
                double a = Math.Sqrt(Math.Max(0.0, 1.0 + x.x - y.y - z.z)), b = a != 0.0 ? 0.5 / a : 0.0;
                r = new DQuat(0.5 * a, (y.x + x.y) * b, (z.x + x.z) * b, (y.z - z.y) * b);
            }
            else if (y.y > z.z)
            {
                double a = Math.Sqrt(Math.Max(0.0, 1.0 - x.x + y.y - z.z)), b = a != 0.0 ? 0.5 / a : 0.0;
                r = new DQuat((y.x + x.y) * b, 0.5 * a, (z.y + y.z) * b, (z.x - x.z) * b);
            }
            else
            {
                double a = Math.Sqrt(Math.Max(0.0, 1.0 - x.x - y.y + z.z)), b = a != 0.0 ? 0.5 / a : 0.0;
                r = new DQuat((z.x + x.z) * b, (z.y + y.z) * b, 0.5 * a, (x.y - y.x) * b);
            }
            double len = r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w;
            if (Math.Abs(len - 1.0) > Epsilon)
            {
                if (Math.Abs(len) <= Epsilon) r = DQuat.Identity;
                else { r.x /= len; r.y /= len; r.z /= len; r.w /= len; }   // ufbx と同じく二乗の長さで割る
            }
            t.Q = r;
            t.S = new DVec3(t.S.x * signX, t.S.y * signY, t.S.z * signZ);
            return t;
        }

        private static DVec3 Mul(DVec3 v, double s) => new DVec3(v.x * s, v.y * s, v.z * s);

        // ---- 変換の組み立て（ufbx.c 22631-22744）----

        public static void AddTranslate(ref DTransform t, DVec3 v) { t.T.x += v.x; t.T.y += v.y; t.T.z += v.z; }
        public static void SubTranslate(ref DTransform t, DVec3 v) { t.T.x -= v.x; t.T.y -= v.y; t.T.z -= v.z; }

        public static void MulScale(ref DTransform t, DVec3 v)
        {
            t.T.x *= v.x; t.T.y *= v.y; t.T.z *= v.z;
            t.S.x *= v.x; t.S.y *= v.y; t.S.z *= v.z;
        }

        public static void MulRotate(ref DTransform t, DVec3 v, int order)
        {
            if (v.IsZero) return;
            ApplyQuat(ref t, EulerToQuat(v, order));
        }

        public static void MulInvRotate(ref DTransform t, DVec3 v, int order)
        {
            if (v.IsZero) return;
            var q = EulerToQuat(v, order);
            q.x = -q.x; q.y = -q.y; q.z = -q.z;
            ApplyQuat(ref t, q);
        }

        public static void MulRotateQuat(ref DTransform t, DQuat q)
        {
            if (q.IsIdentity) return;
            ApplyQuat(ref t, q);
        }

        private static void ApplyQuat(ref DTransform t, DQuat q)
        {
            t.Q = t.Q.w != 1.0 ? MulQuat(q, t.Q) : q;
            if (!t.T.IsZero) t.T = QuatRotate(q, t.T);
        }
    }
}

// Assets/Editor/Poly_Ling_Main/Tools/Core/AxisGizmo.cs
// 軸ギズモ共有クラス
// MoveTool（頂点移動）とBoneInput（ボーン移動）で共通使用
// 描画、ヒットテスト、移動量計算を提供

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    public class AxisGizmo
    {
        // ================================================================
        // 軸タイプ
        // ================================================================

        public enum AxisType { None, X, Y, Z, Center }

        // ================================================================
        // 診断ログ（一時）
        //
        // ギズモの描画座標・マウス座標・判定結果が同じ空間にあるかを確認するための
        // 計測用スイッチ。既定 false。原因特定後に呼び出しごと削除する。
        // ================================================================

        public static bool GizmoDebugLog = false;

        // ================================================================
        // 設定
        // ================================================================

        public Vector2 ScreenOffset { get; set; } = new Vector2(60, -60);
        public float HandleHitRadius { get; set; } = 10f;
        /// <summary>軸線分（原点〜先端）の当たり幅。先端 HandleHitRadius より狭くして先端を優先させる。</summary>
        public float AxisLineHitRadius { get; set; } = 6f;
        public float HandleSize { get; set; } = 8f;
        public float CenterSize { get; set; } = 14f;
        public float ScreenAxisLength { get; set; } = 50f;

        // ================================================================
        // 状態（描画色制御用。呼び出し元が設定する）
        // ================================================================

        public AxisType HoveredAxis { get; set; } = AxisType.None;
        public AxisType DraggingAxis { get; set; } = AxisType.None;

        /// <summary>ギズモ中心のワールド座標</summary>
        public Vector3 Center { get; set; }

        /// <summary>
        /// 軸の向き。既定 identity のときは従来どおりワールド軸（X=right / Y=up / Z=forward）。
        /// 作業用ローカル軸など任意フレームで操作したい場合にここへ回転を設定する。
        /// 描画・ヒットテスト・移動量計算のすべてがこの向きに従う。
        /// </summary>
        public Quaternion Orientation { get; set; } = Quaternion.identity;

        /// <summary>Orientation を適用した軸方向。静的な GetAxisDirection と違い向きを反映する。</summary>
        public Vector3 GetOrientedAxisDirection(AxisType axis)
            => Orientation * GetAxisDirection(axis);

        // ================================================================
        // 描画（Repaintイベント中に呼び出す）
        // ================================================================

        /// <summary>
        /// 軸ギズモのスクリーン座標を返す。UIToolkit 等の独自描画に使う。
        /// 座標系は ctx.WorldToScreenPos が返す系（PlayerToolContext の場合は Y=0 が上）。
        /// </summary>
        public void GetScreenPositions(ToolContext ctx,
            out Vector2 origin,
            out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd)
        {
            origin = GetOriginScreen(ctx);
            xEnd   = GetAxisScreenEnd(ctx, GetOrientedAxisDirection(AxisType.X), origin);
            yEnd   = GetAxisScreenEnd(ctx, GetOrientedAxisDirection(AxisType.Y), origin);
            zEnd   = GetAxisScreenEnd(ctx, GetOrientedAxisDirection(AxisType.Z), origin);
        }

        public void Draw(ToolContext ctx)
        {
            Vector2 originScreen = GetOriginScreen(ctx);

            // 軸色
            Color xColor = (DraggingAxis == AxisType.X || HoveredAxis == AxisType.X)
                ? new Color(1f, 0.3f, 0.3f, 1f)
                : new Color(0.8f, 0.2f, 0.2f, 0.7f);

            Color yColor = (DraggingAxis == AxisType.Y || HoveredAxis == AxisType.Y)
                ? new Color(0.3f, 1f, 0.3f, 1f)
                : new Color(0.2f, 0.8f, 0.2f, 0.7f);

            Color zColor = (DraggingAxis == AxisType.Z || HoveredAxis == AxisType.Z)
                ? new Color(0.3f, 0.3f, 1f, 1f)
                : new Color(0.2f, 0.2f, 0.8f, 0.7f);

            Vector2 xEnd = GetAxisScreenEnd(ctx, GetOrientedAxisDirection(AxisType.X), originScreen);
            Vector2 yEnd = GetAxisScreenEnd(ctx, GetOrientedAxisDirection(AxisType.Y), originScreen);
            Vector2 zEnd = GetAxisScreenEnd(ctx, GetOrientedAxisDirection(AxisType.Z), originScreen);

            // 軸線
            float lineWidth = 2f;
            DrawAxisLine(originScreen, xEnd, xColor, lineWidth);
            DrawAxisLine(originScreen, yEnd, yColor, lineWidth);
            DrawAxisLine(originScreen, zEnd, zColor, lineWidth);

            // 軸先端ハンドル
            DrawAxisHandle(xEnd, xColor, HoveredAxis == AxisType.X, "X");
            DrawAxisHandle(yEnd, yColor, HoveredAxis == AxisType.Y, "Y");
            DrawAxisHandle(zEnd, zColor, HoveredAxis == AxisType.Z, "Z");

            // 中央四角
            bool centerHovered = (HoveredAxis == AxisType.Center);
            Color centerColor = centerHovered
                ? new Color(1f, 1f, 1f, 0.9f)
                : new Color(0.8f, 0.8f, 0.8f, 0.6f);

            float halfCenter = CenterSize / 2;
            Rect centerRect = new Rect(
                originScreen.x - halfCenter,
                originScreen.y - halfCenter,
                CenterSize,
                CenterSize);

            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
        }

        // ================================================================
        // ヒットテスト（MouseDownイベント中に呼び出す）
        // ================================================================

        public AxisType FindAxisAtScreenPos(Vector2 screenPos, ToolContext ctx)
        {
            Vector2 originScreen = GetOriginScreen(ctx);
            Vector2 xEnd = GetAxisScreenEnd(ctx, GetOrientedAxisDirection(AxisType.X), originScreen);
            Vector2 yEnd = GetAxisScreenEnd(ctx, GetOrientedAxisDirection(AxisType.Y), originScreen);
            Vector2 zEnd = GetAxisScreenEnd(ctx, GetOrientedAxisDirection(AxisType.Z), originScreen);

            // 中央四角（優先）
            float halfCenter = CenterSize / 2 + 2;
            if (Mathf.Abs(screenPos.x - originScreen.x) < halfCenter &&
                Mathf.Abs(screenPos.y - originScreen.y) < halfCenter)
            {
                return AxisType.Center;
            }

            // 先端ハンドル（従来の判定。軸線分より優先する）
            if (Vector2.Distance(screenPos, xEnd) < HandleHitRadius)
                return AxisType.X;
            if (Vector2.Distance(screenPos, yEnd) < HandleHitRadius)
                return AxisType.Y;
            if (Vector2.Distance(screenPos, zEnd) < HandleHitRadius)
                return AxisType.Z;

            // 軸線分（原点〜先端）。先端に当たらなかったときだけ評価し、最短の軸を採る。
            var bestAxis = AxisType.None;
            float bestDist = AxisLineHitRadius;

            float dx = DistanceToSegment(screenPos, originScreen, xEnd);
            if (dx < bestDist) { bestDist = dx; bestAxis = AxisType.X; }

            float dy = DistanceToSegment(screenPos, originScreen, yEnd);
            if (dy < bestDist) { bestDist = dy; bestAxis = AxisType.Y; }

            float dz = DistanceToSegment(screenPos, originScreen, zEnd);
            if (dz < bestDist) { bestDist = dz; bestAxis = AxisType.Z; }

            if (GizmoDebugLog)
            {
                Debug.Log(
                    $"[GizmoDbg/Axis] mouse={screenPos} origin={originScreen} " +
                    $"xEnd={xEnd} yEnd={yEnd} zEnd={zEnd} " +
                    $"hit={bestAxis} dSeg=({dx:F1},{dy:F1},{dz:F1}) " +
                    $"dTip=({Vector2.Distance(screenPos, xEnd):F1}," +
                    $"{Vector2.Distance(screenPos, yEnd):F1}," +
                    $"{Vector2.Distance(screenPos, zEnd):F1}) " +
                    $"tipR={HandleHitRadius} lineR={AxisLineHitRadius} " +
                    $"offset={ScreenOffset} rect={ctx?.PreviewRect} center={Center}");
            }

            return bestAxis;
        }

        /// <summary>
        /// 点 p と線分 a-b のスクリーン距離。軸線・リングの当たり判定で共有する。
        /// </summary>
        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        // ================================================================
        // 移動量計算
        // ================================================================

        // ----------------------------------------------------------------
        // 【px ↔ ワールド換算の原則】
        //
        // 換算係数を定数や画角の再計算で持たない。ctx.WorldToScreenPos に
        // Center 近傍の 2 点を通して実測する。投影行列そのものを通るため、
        // 画角・ビューポート高・アスペクト・カメラのロール・奥行き・
        // 透視／正投影の区別がすべて自動的に反映される。
        //
        // 旧実装は次の 2 点で破綻していた。再発させないこと。
        //   1. 軸ドラッグが worldScale = CameraDistance * 0.001f という
        //      画角にもビューポート高にも依存しない固定係数を使っていた。
        //   2. GetAxisScreenDirection が方向を正規化して返すため、
        //      「ワールド 1 単位が画面上で何 px か」の情報が失われ、
        //      視線に近い軸ほど短縮ぶんだけ移動量が不足していた。
        // ----------------------------------------------------------------

        /// <summary>
        /// Center 位置で worldDir 方向へ動かしたときの「ワールド 1 単位あたりの
        /// スクリーン px ベクトル」を中心差分で実測する。
        /// 戻り値の座標系は ctx.WorldToScreenPos 系（Y=0 が上、+Y が画面下）。
        /// </summary>
        private Vector2 ScreenPerWorldUnit(ToolContext ctx, Vector3 worldDir, float h)
        {
            if (h <= 0f) return Vector2.zero;

            Vector2 a = ctx.WorldToScreenPos(
                Center - worldDir * h, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 b = ctx.WorldToScreenPos(
                Center + worldDir * h, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            return (b - a) / (2f * h);
        }

        /// <summary>
        /// 視線方向に直交する正規直交基底 u, v と、その px/world ベクトル su, sv を返す。
        ///
        /// u, v は視線に直交するので、その平面内を動いても奥行き（clip.w）が変化しない。
        /// したがって透視投影でも投影が厳密に線形になり、h の取り方に依存せず誤差ゼロで
        /// 実測できる。正投影ではそもそも奥行き非依存なので同様。
        /// </summary>
        private bool TryGetScreenBasis(ToolContext ctx,
            out Vector3 u, out Vector3 v,
            out Vector2 su, out Vector2 sv, out float pxPerWorld)
        {
            u = v = Vector3.zero;
            su = sv = Vector2.zero;
            pxPerWorld = 0f;
            if (ctx == null || ctx.WorldToScreenPos == null) return false;

            Vector3 fwd = ctx.CameraTarget - ctx.CameraPosition;
            if (fwd.sqrMagnitude < 1e-12f) return false;
            fwd.Normalize();

            // fwd と平行になりにくい種ベクトルを選ぶ
            Vector3 seed = Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.9f
                ? Vector3.right : Vector3.up;
            u = Vector3.Cross(seed, fwd).normalized;
            v = Vector3.Cross(fwd, u).normalized;

            // 上記のとおり厳密に線形なので h は任意。桁落ちしない大きさを採る。
            float h = Mathf.Max(1e-4f, ctx.CameraDistance * 0.01f);
            su = ScreenPerWorldUnit(ctx, u, h);
            sv = ScreenPerWorldUnit(ctx, v, h);

            pxPerWorld = 0.5f * (su.magnitude + sv.magnitude);
            return pxPerWorld > 1e-6f;
        }

        /// <summary>
        /// 軸拘束ドラッグ時のフレーム移動量を計算（ワールド座標系）。
        /// DisplayMatrix逆変換適用済み。
        ///
        /// 軸上の点の画面移動量が、マウス移動量の軸方向成分に一致する量を返す
        /// （最小二乗解 t = dot(screenDelta, s) / |s|^2、s は px/world ベクトル）。
        ///
        /// 【引数の座標系に注意】screenDeltaYDown は <b>+Y が画面下</b>の差分。
        /// 内部で使う ScreenPerWorldUnit は ctx.WorldToScreenPos 系（Y=0 が上）なので、
        /// パネルの ToViewportCoord 系（+Y が画面上）の差分をそのまま渡すと Y だけ逆に動く。
        /// ToImgui 済み座標の差分をそのまま渡すか、パネル delta なら Y を反転して渡すこと。
        /// 同クラスの ComputeFreeDelta は逆に <b>+Y が画面上</b>を要求する。
        /// </summary>
        public Vector3 ComputeAxisDelta(Vector2 screenDeltaYDown, AxisType axis, ToolContext ctx)
        {
            if (screenDeltaYDown.sqrMagnitude < 0.001f || axis == AxisType.None || axis == AxisType.Center)
                return Vector3.zero;

            if (!TryGetScreenBasis(ctx, out _, out _, out _, out _, out float pxPerWorld))
                return Vector3.zero;

            Vector3 axisDir = GetOrientedAxisDirection(axis);
            if (axisDir.sqrMagnitude < 1e-12f) return Vector3.zero;

            // 軸は視線に直交するとは限らないため投影は厳密には線形でない。
            // 画面上 10px 相当の中心差分で接線を取る（曲率誤差は O((h/depth)^2)）。
            float h = 10f / pxPerWorld;
            Vector2 s = ScreenPerWorldUnit(ctx, axisDir, h);

            // 軸が視線とほぼ平行（画面上で潰れている）ときは動かさない。
            // s.magnitude / pxPerWorld は軸と視線のなす角の sin にあたる。
            if (s.magnitude < pxPerWorld * 0.05f)
                return Vector3.zero;

            float t = Vector2.Dot(screenDeltaYDown, s) / s.sqrMagnitude;
            Vector3 worldDelta = axisDir * t;

            if (ctx.DisplayMatrix != Matrix4x4.identity)
                worldDelta = ctx.DisplayMatrix.inverse.MultiplyVector(worldDelta);

            return worldDelta;
        }

        /// <summary>
        /// 自由移動（中央ドラッグ）のフレーム移動量を計算（ワールド座標系）。
        /// DisplayMatrix逆変換適用済み。
        ///
        /// Center を通る視線直交平面内で、投影がマウス移動量と厳密に一致する
        /// 移動量を返す（a*su + b*sv = screenDelta を解く）。
        /// ctx.ScreenDeltaToWorldDelta は使わない。同デリゲートは
        /// PolyLingCoreConfig.CreateStub でゼロを返す実装しか存在せず、
        /// また換算距離に注視点までの距離を使うため対象の奥行きと一致しない。
        ///
        /// 【引数の座標系に注意】screenDeltaYUp は <b>+Y が画面上</b>の差分。
        /// パネルの ToViewportCoord 系の差分はそのまま渡してよい。
        /// 同クラスの ComputeAxisDelta は逆に <b>+Y が画面下</b>を要求する。
        /// </summary>
        public Vector3 ComputeFreeDelta(Vector2 screenDeltaYUp, ToolContext ctx)
        {
            if (!TryGetScreenBasis(ctx, out Vector3 u, out Vector3 v,
                                   out Vector2 su, out Vector2 sv, out _))
                return Vector3.zero;

            // ScreenPerWorldUnit は Y 下系。引数は Y 上系なので合わせる。
            Vector2 d = new Vector2(screenDeltaYUp.x, -screenDeltaYUp.y);

            float det = su.x * sv.y - su.y * sv.x;
            if (Mathf.Abs(det) < 1e-9f) return Vector3.zero;

            float a = (d.x * sv.y - d.y * sv.x) / det;
            float b = (su.x * d.y - su.y * d.x) / det;

            Vector3 worldDelta = u * a + v * b;

            if (ctx.DisplayMatrix != Matrix4x4.identity)
                worldDelta = ctx.DisplayMatrix.inverse.MultiplyVector(worldDelta);

            return worldDelta;
        }

        // ================================================================
        // スケールドラッグセッション（共有）
        //
        // 「軸ハンドルのスクリーン方向を記録し、ドラッグ量をその方向へ内積して
        // 倍率へ変換する」手順を、軸ギズモでスケールする全ツールで共有する。
        // 引数の screenPos はスクリーン系（Y=0 が上、下方向が正）。
        // GetScreenPositions は ctx 系（Y 上）なので内部で符号を合わせる。
        // Center は呼び出し前に設定しておくこと。
        // ================================================================

        /// <summary>スケール倍率の感度（スクリーン 1px あたりの係数）。</summary>
        public float ScaleSensitivity { get; set; } = 0.01f;

        private AxisType _scaleDragAxis = AxisType.None;
        private Vector2  _scaleDragStartScreen;
        private Vector2  _scaleAxisScreenDir = Vector2.right;

        /// <summary>スケールドラッグ中か。</summary>
        public bool IsScaleDragging => _scaleDragAxis != AxisType.None;

        /// <summary>スケールドラッグを開始する。screenPos はスクリーン系（Y 下）。</summary>
        public bool BeginScaleDrag(ToolContext ctx, AxisType axis, Vector2 screenPos)
        {
            _scaleDragAxis = AxisType.None;
            if (ctx == null || axis == AxisType.None) return false;

            _scaleDragStartScreen = screenPos;

            if (axis == AxisType.Center)
            {
                _scaleAxisScreenDir = Vector2.right;   // Center では使わない
            }
            else
            {
                GetScreenPositions(ctx, out var o, out var xe, out var ye, out var ze);
                Vector2 end = axis == AxisType.X ? xe
                            : axis == AxisType.Y ? ye : ze;
                Vector2 dir = end - o;
                _scaleAxisScreenDir = dir.sqrMagnitude > 1e-4f
                    ? new Vector2(dir.x, -dir.y).normalized
                    : Vector2.right;
            }

            _scaleDragAxis = axis;
            return true;
        }

        /// <summary>
        /// 開始位置からの倍率を返す。BeginScaleDrag していなければ 1。
        /// screenPos はスクリーン系（Y 下）。
        /// </summary>
        public float ComputeScaleFactor(Vector2 screenPos)
        {
            if (_scaleDragAxis == AxisType.None) return 1f;

            Vector2 d = screenPos - _scaleDragStartScreen;
            float along = (_scaleDragAxis == AxisType.Center)
                ? (d.x - d.y)
                : Vector2.Dot(d, _scaleAxisScreenDir);

            return Mathf.Max(0.01f, 1f + along * ScaleSensitivity);
        }

        /// <summary>スケールドラッグを終了する。</summary>
        public void EndScaleDrag()
        {
            _scaleDragAxis = AxisType.None;
        }

        // ================================================================
        // 静的ユーティリティ
        // ================================================================

        /// <summary>
        /// ワールド軸方向。Orientation は反映しない。
        /// 向きを反映したいときはインスタンスメソッド GetOrientedAxisDirection を使うこと。
        /// </summary>
        public static Vector3 GetAxisDirection(AxisType axis)
        {
            switch (axis)
            {
                case AxisType.X: return Vector3.right;
                case AxisType.Y: return Vector3.up;
                case AxisType.Z: return Vector3.forward;
                default: return Vector3.zero;
            }
        }

        // ================================================================
        // 内部メソッド
        // ================================================================

        private Vector2 GetOriginScreen(ToolContext ctx)
        {
            Vector2 centerScreen = ctx.WorldToScreenPos(
                Center, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            return centerScreen + ScreenOffset;
        }

        private Vector3 GetAxisScreenDirection(ToolContext ctx, Vector3 worldAxis)
        {
            float scale = Mathf.Max(0.1f, ctx.CameraDistance * 0.1f);
            Vector3 axisEnd = Center + worldAxis * scale;

            Vector2 centerScreen = ctx.WorldToScreenPos(
                Center, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 axisEndScreen = ctx.WorldToScreenPos(
                axisEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            Vector2 diff = axisEndScreen - centerScreen;
            if (diff.magnitude < 0.001f)
                return Vector3.zero;

            Vector2 screenDir = diff.normalized;
            return new Vector3(screenDir.x, screenDir.y, 0);
        }

        private Vector2 GetAxisScreenEnd(ToolContext ctx, Vector3 worldAxis, Vector2 originScreen)
        {
            Vector3 screenDir = GetAxisScreenDirection(ctx, worldAxis);
            return originScreen + new Vector2(screenDir.x, screenDir.y) * ScreenAxisLength;
        }

        // ================================================================
        // 描画ヘルパー
        // ================================================================

        private static void DrawAxisLine(Vector2 from, Vector2 to, Color color, float lineWidth)
        {
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
        }

        private void DrawAxisHandle(Vector2 pos, Color color, bool hovered, string label)
        {
            float size = hovered ? HandleSize * 1.3f : HandleSize;
            Rect handleRect = new Rect(pos.x - size / 2, pos.y - size / 2, size, size);

            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み

#if UNITY_EDITOR
            GUIStyle style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = color;
            style.fontStyle = hovered ? FontStyle.Bold : FontStyle.Normal;
            GUI.Label(new Rect(pos.x + size / 2 + 2, pos.y - 8, 20, 16), label, style);
#endif
        }
    }
}

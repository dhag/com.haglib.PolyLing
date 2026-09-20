// SpringBoneColliderDisplayHandler.cs
// 揺れものの当たり判定を 3D 画面へ出す表示状態（ModelContext.SpringBoneCollider*）を受け持つ
// （操作経路統一計画.md E）。
//
// 表示状態は保存しない画面上の状態だが、置き場がモデルなので、パネルが直接書き換えないよう
// ツールの窓口の操作にした。データ（当たり判定そのもの）は変えない。

using System;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    [PLTool("springBoneColliderDisplay", Description = "揺れものの当たり判定の 3D 表示と強調")]
    public sealed class SpringBoneColliderDisplayHandler
    {
        public Func<ModelContext> GetModel;
        /// <summary>表示状態を書き換えた後に呼ぶ（ビューポートに線を作り直させる）。</summary>
        public Action OnDisplayChanged;

        [PLToolState(Description = "当たり判定を 3D 画面へ出しているか")]
        public bool IsDisplayed => GetModel?.Invoke()?.SpringBoneColliderDisplay ?? false;

        [PLToolAction(Description = "当たり判定を 3D 画面へ出し、指定した当たり判定を強調する（master・slot が -1 なら強調なし）")]
        public void Show(int master, int slot)
        {
            var model = GetModel?.Invoke();
            if (model == null) return;
            model.SpringBoneColliderDisplay         = true;
            model.SpringBoneColliderHighlightMaster = master;
            model.SpringBoneColliderHighlightSlot   = slot;
            OnDisplayChanged?.Invoke();
        }

        [PLToolAction(Description = "当たり判定の 3D 表示を消す（出していなければ何もしない）")]
        public void Clear()
        {
            var model = GetModel?.Invoke();
            if (model == null || !model.SpringBoneColliderDisplay) return;
            model.ClearSpringBoneColliderDisplay();
            OnDisplayChanged?.Invoke();
        }
    }
}

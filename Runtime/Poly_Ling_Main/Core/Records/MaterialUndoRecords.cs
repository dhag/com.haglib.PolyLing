// Assets/Editor/Poly_Ling/UndoSystem/MaterialUndoRecords.cs
// ============================================================
// マテリアルパラメータ（MaterialData）Undo/Redo 記録
// ============================================================
//
// 【役割】
//   MaterialReference.Data（MaterialData）のパラメータ変更を軽量に Undo/Redo する。
//   ModelContext.MaterialReferences / DefaultMaterialReferences のどちらも対象にできる。
//
// 【既存レコードとの棲み分け】
//   MeshListChangeRecord は OldMaterials/NewMaterials を List<Material>（Unity材質）で持ち、
//   マテリアル「リスト自体」の増減を戻すためのもの。本レコードは要素1件のパラメータ変更用で、
//   リストの長さは変えない。
//
// 【キャッシュ無効化】
//   MaterialReference は Data から生成した Material をキャッシュするため、
//   Data を差し替えたら必ず InvalidateCache() を呼ぶ。呼ばないと旧パラメータの
//   Material が描画に使われ続ける。
//   なお InvalidateCache は runtime 生成材質を Destroy しない（参照を外すのみ）。
//   これは GPU レンダラが Rebuild まで旧材質を参照するためで、
//   MaterialReference.InvalidateCache のコメントにある既知の残存リスクと同じ扱いとする。
//
// 【依存】
//   #if UNITY_EDITOR を含まない。
//
// ============================================================

using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Materials;

namespace Poly_Ling.UndoSystem
{
    /// <summary>
    /// マテリアルリストの種別。
    /// </summary>
    public enum MaterialListScope
    {
        /// <summary>ModelContext.MaterialReferences</summary>
        Material = 0,
        /// <summary>ModelContext.DefaultMaterialReferences</summary>
        DefaultMaterial = 1
    }

    // ============================================================
    // MaterialData スナップショット
    // ============================================================

    /// <summary>
    /// MaterialData 1件分のディープコピー。null も状態として保持する。
    /// </summary>
    public class MaterialDataSnapshot
    {
        /// <summary>マテリアルパラメータ（null = Data 未設定）。</summary>
        public MaterialData Data;

        /// <summary>
        /// 指定リスト・indexからスナップショットを作成。
        /// 対象が存在しない場合は null を返す。
        /// </summary>
        public static MaterialDataSnapshot Capture(ModelContext ctx, MaterialListScope scope, int index)
        {
            var list = GetList(ctx, scope);
            if (list == null || index < 0 || index >= list.Count) return null;

            var matRef = list[index];
            if (matRef == null) return null;

            return new MaterialDataSnapshot { Data = matRef.Data?.Clone() };
        }

        /// <summary>スナップショットを指定リスト・indexへ適用（ディープコピーで書き戻す）。</summary>
        public void ApplyTo(ModelContext ctx, MaterialListScope scope, int index)
        {
            var list = GetList(ctx, scope);
            if (list == null || index < 0 || index >= list.Count) return;

            var matRef = list[index];
            if (matRef == null) return;

            matRef.Data = Data?.Clone();

            // Data 差し替え後は必ずキャッシュを捨てる（旧パラメータの Material が残るため）
            matRef.InvalidateCache();
        }

        /// <summary>スナップショットの複製。</summary>
        public MaterialDataSnapshot Clone()
        {
            return new MaterialDataSnapshot { Data = Data?.Clone() };
        }

        internal static List<MaterialReference> GetList(ModelContext ctx, MaterialListScope scope)
        {
            if (ctx == null) return null;
            return scope == MaterialListScope.DefaultMaterial
                ? ctx.DefaultMaterialReferences
                : ctx.MaterialReferences;
        }
    }

    // ============================================================
    // 単一マテリアルのパラメータ変更レコード
    // ============================================================

    /// <summary>
    /// マテリアル1件のパラメータ変更を記録するレコード。
    /// </summary>
    public class MaterialDataChangeRecord : MeshListUndoRecord
    {
        /// <summary>対象リスト種別</summary>
        public MaterialListScope Scope = MaterialListScope.Material;

        /// <summary>対象リスト内のindex</summary>
        public int Index;

        /// <summary>変更前のスナップショット</summary>
        public MaterialDataSnapshot OldSnapshot;

        /// <summary>変更後のスナップショット</summary>
        public MaterialDataSnapshot NewSnapshot;

        public MaterialDataChangeRecord() { }

        public MaterialDataChangeRecord(MaterialListScope scope, int index,
            MaterialDataSnapshot oldSnapshot, MaterialDataSnapshot newSnapshot)
        {
            Scope = scope;
            Index = index;
            OldSnapshot = oldSnapshot;
            NewSnapshot = newSnapshot;
        }

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;
            OldSnapshot?.ApplyTo(ctx, Scope, Index);
            ctx.OnListChanged?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;
            NewSnapshot?.ApplyTo(ctx, Scope, Index);
            ctx.OnListChanged?.Invoke();
        }

        public override string ToString()
        {
            return $"MaterialDataChange: {Scope}[{Index}]";
        }
    }

    // ============================================================
    // 複数マテリアルのパラメータ一括変更レコード
    // ============================================================

    /// <summary>
    /// 複数マテリアルのパラメータ変更を一括で記録するレコード。
    /// </summary>
    public class MultiMaterialDataChangeRecord : MeshListUndoRecord
    {
        public struct Entry
        {
            public MaterialListScope Scope;
            public int Index;
            public MaterialDataSnapshot OldSnapshot;
            public MaterialDataSnapshot NewSnapshot;
        }

        public List<Entry> Entries = new List<Entry>();

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries)
                e.OldSnapshot?.ApplyTo(ctx, e.Scope, e.Index);
            ctx.OnListChanged?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;
            foreach (var e in Entries)
                e.NewSnapshot?.ApplyTo(ctx, e.Scope, e.Index);
            ctx.OnListChanged?.Invoke();
        }

        public override string ToString()
        {
            return $"MultiMaterialDataChange: {Entries.Count} materials";
        }
    }
}

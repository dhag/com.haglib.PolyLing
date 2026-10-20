// FbxNodeTable.cs
// ============================================================
// FBX のノードの分類とバインド姿勢（モデル読み込み FbxImporter とモーション読み込み FbxMotionReader で共通）
// ------------------------------------------------------------
// 規則の正本は FbxImporter.cs 冒頭「ノードの分類」「バインド姿勢」。
//   入れ物 … シーンの根から続く、メッシュ・属性・スケルトン・明示のバインド・クラスタの関節のどれも持たないノード。
//   ボーン … 入れ物でもシーンの根でもなく、メッシュを持たないノードと、スキンクラスタの関節ノード。
//   bw     … クラスタのリンク行列 → バインドポーズ → 親の bw · 親から見た今の変換（ファイルの座標）。
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Fbx
{
    public sealed class FbxNodeTable
    {
        public FbxScene.NodeInfo[] Nodes      { get; private set; }
        public List<int>[]          Children   { get; private set; }
        public bool[]               Container  { get; private set; }
        /// <summary>バインド時のワールド（ファイルの座標）。</summary>
        public Matrix4x4[]          BindWorld  { get; private set; }
        public HashSet<int>         JointNodes { get; } = new HashSet<int>();
        /// <summary>読み飛ばす入れ物の名前（DFS 順）。</summary>
        public List<string>         ContainerNames { get; } = new List<string>();

        public static FbxNodeTable Read(FbxScene scene)
        {
            var t = new FbxNodeTable();
            int n = scene.NodeCount;
            t.Nodes    = new FbxScene.NodeInfo[n];
            t.Children = new List<int>[n];
            for (int i = 0; i < n; i++) { t.Nodes[i] = scene.Node(i); t.Children[i] = new List<int>(); }
            for (int i = 0; i < n; i++)
                if (t.Nodes[i].Parent >= 0) t.Children[t.Nodes[i].Parent].Add(i);

            // スキンクラスタの関節
            for (int mi = 0; mi < scene.MeshCount; mi++)
            {
                var info = scene.Mesh(mi);
                if (info.SkinDeformerCount == 0) continue;
                for (int c = 0; c < info.ClusterCount; c++)
                {
                    int bone = scene.MeshCluster(mi, c, out _, out _);
                    if (bone >= 0) t.JointNodes.Add(bone);
                }
            }

            // 入れ物（親から順に決まるので DFS 順で見る）
            t.Container = new bool[n];
            foreach (int i in t.Traverse())
            {
                var nd = t.Nodes[i];
                bool parentOk = nd.Parent < 0 || t.Nodes[nd.Parent].IsRoot || t.Container[nd.Parent];
                bool empty = nd.Mesh < 0 && !nd.HasSkeleton && nd.ExplicitBind == 0
                          && (nd.AttribType == 0 /* UNKNOWN（属性なし）*/ || nd.AttribType == 6 /* EMPTY */)
                          && !t.JointNodes.Contains(i);
                t.Container[i] = parentOk && empty;
                if (t.Container[i]) t.ContainerNames.Add(nd.Name);
            }

            // バインド時のワールド
            t.BindWorld = new Matrix4x4[n];
            foreach (int i in t.Traverse())
            {
                var nd = t.Nodes[i];
                if (nd.ExplicitBind != 0 || t.IsTopLevel(i))
                    t.BindWorld[i] = nd.ExplicitBind != 0 ? nd.Bind : nd.World;
                else
                    t.BindWorld[i] = t.BindWorld[nd.Parent] * t.Nodes[nd.Parent].World.inverse * nd.World;
            }
            return t;
        }

        /// <summary>最上位か（親がシーンの根か入れ物）。</summary>
        public bool IsTopLevel(int n)
        {
            int p = Nodes[n].Parent;
            return p < 0 || Nodes[p].IsRoot || Container[p];
        }

        public bool IsBone(int n)
            => !Nodes[n].IsRoot && !Container[n] && (Nodes[n].Mesh < 0 || JointNodes.Contains(n));

        /// <summary>シーンの根から DFS（子はファイルの並び）。根は含めない。</summary>
        public IEnumerable<int> Traverse()
        {
            var stack = new Stack<int>();
            for (int i = 0; i < Nodes.Length; i++)
                if (Nodes[i].IsRoot) stack.Push(i);
            while (stack.Count > 0)
            {
                int n = stack.Pop();
                if (!Nodes[n].IsRoot) yield return n;
                var ch = Children[n];
                for (int k = ch.Count - 1; k >= 0; k--) stack.Push(ch[k]);
            }
        }
    }
}

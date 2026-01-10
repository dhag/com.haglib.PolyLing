// Assets/Editor/SimpleMeshFactory.cs
// 階層型Undoシステム統合済みメッシュエディタ
// MeshObject（Vertex/Face）ベース対応版
// DefaultMaterials対応版
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.UndoSystem;
using MeshFactory.Data;
using MeshFactory.Transforms;
using MeshFactory.Tools;
using MeshFactory.Serialization;
using MeshFactory.Selection;
using MeshFactory.Model;
using MeshFactory.Localization;
using static MeshFactory.Gizmo.GLGizmoDrawer;
using MeshFactory.Rendering;
using MeshFactory.Symmetry;




public partial class SimpleMeshFactory : EditorWindow
{
    public static float parm_ew = 1.0f;
}

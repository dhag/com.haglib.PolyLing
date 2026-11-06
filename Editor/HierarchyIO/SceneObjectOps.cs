// Editor/HierarchyIO/SceneObjectOps.cs
// ============================================================
// 開いているシーンのオブジェクトを操作する（Unity 初心者の代わりのちょっとした操作）
// ============================================================
//
// 【役割】
//   MCP の unity_scene_call から、PolyLingEditorControlServer の scene_* を経て呼ぶ。
//   MCP サーバは action の頭に "scene_" を付けて転送するだけで、操作の名前を知らない。
//   操作を足すときはここの Run の switch と Help の文を足すだけでよい（MCP サーバの再ビルド不要）。
//
// 【共通】
//   ・ダイアログを出さない（メインスレッドが止まると応答が返らない）。結果は文言で返す。
//   ・変更は Undo に登録し、シーンを変更済みにする。シーンの保存はしない。
//   ・Play 中は呼ばないこと（help を除く）。判定は呼び出し側が行う。
//
// 【対象の指定（objectPath / parentPath）】
//   "Root/Child" 形式のヒエラルキーパス（PrefabInstantiateOps.FindInLoadedScenes。非アクティブも対象）。
//   "/" を含まない名前だけで見つからないときは、読み込み済みシーン全体から同じ名前を探す
//   （1 つなら採る。複数ならパスを挙げて失敗する）。
//
// 【コンポーネントの指定（get_properties / set_property）】
//   対象に付いているコンポーネントから、完全名か短い名前で探す（Transform も可）。
//   同じ型が複数付いていれば componentIndex（0 始まり）で選ぶ。
//
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Poly_Ling.EditorIO
{
    /// <summary>開いているシーンのオブジェクト操作。</summary>
    public static class SceneObjectOps
    {
        /// <summary>hierarchy で返す行数の既定と上限。</summary>
        private const int DefaultMaxLines = 300;
        private const int LimitMaxLines   = 5000;

        /// <summary>hierarchy で並べる深さの既定（ルートが 1）。</summary>
        private const int DefaultDepth = 2;

        /// <summary>
        /// 操作を 1 つ実行する。
        /// </summary>
        /// <param name="action">"help" / "hierarchy" / "create_empty" など（"scene_" を除いた名前）。</param>
        /// <param name="args">平らな引数。null 可。</param>
        /// <param name="message">結果の文言。</param>
        public static bool Run(string action, Dictionary<string, string> args, out string message)
        {
            args ??= new Dictionary<string, string>();

            switch (action)
            {
                case "help":             message = Help(); return true;
                case "hierarchy":        return Hierarchy(args, out message);
                case "create_empty":     return CreateEmpty(args, out message);
                case "create_primitive": return CreatePrimitive(args, out message);
                case "add_component":    return AddComponent(args, out message);
                case "get_properties":   return GetProperties(args, out message);
                case "set_property":     return SetProperty(args, out message);
                case "delete":           return Delete(args, out message);
                default:
                    message = $"未知の action です: {action}。action=help で一覧を確認してください";
                    return false;
            }
        }

        // ================================================================
        // help
        // ================================================================

        private static string Help() =>
            "unity_scene_call の action 一覧（args は平らな JSON。値はすべて文字列）。\n"
          + "Play 中なら MCP サーバが先に Play を止める（help を除く）。変更は Undo に入り、シーンは保存しない。\n"
          + "\n"
          + "help\n"
          + "  この一覧。\n"
          + "hierarchy  args: rootPath（任意。その下だけ）, depth（任意。並べる深さ。rootPath かシーンのルートが 1。既定 2）,\n"
          + "           components（任意。true でコンポーネント名も）, maxLines（任意。既定 300）\n"
          + "  オブジェクトを \"Root/Child\" のパスで 1 行ずつ返す。非アクティブには (inactive)、深さの上限で止めた所には「…子孫 n」を付ける。\n"
          + "  深い所は rootPath で下りて見る。\n"
          + "create_empty  args: name（任意。既定 GameObject）, parentPath（任意）, position（任意。\"x,y,z\"。親からの位置）\n"
          + "  空の GameObject を作る。parentPath を省くとアクティブシーンのルート。\n"
          + "create_primitive  args: type（必須。Cube / Sphere / Capsule / Cylinder / Plane / Quad）, name, parentPath, position（任意）\n"
          + "  Unity 標準の形を作る。name の既定は type。\n"
          + "add_component  args: objectPath（必須）, componentType（必須。完全名か短い名前）, allowMultiple（任意。既定 false）\n"
          + "  コンポーネントを付ける。既に同じ型があれば allowMultiple=true のときだけ 2 つ目を付ける。\n"
          + "get_properties  args: objectPath（必須）, componentType（任意。省くと付いているコンポーネントの一覧）, componentIndex（任意。既定 0）\n"
          + "  プロパティのパス・型・今の値を返す。set_property の propertyPath にはここのパスを使う。\n"
          + "set_property  args: objectPath（必須）, componentType（必須）, propertyPath（必須）, value（必須）, componentIndex（任意）\n"
          + "  値を書く。対応する型と value の書き方:\n"
          + "    Integer / Float … 数値（小数点は .）  Boolean … true / false  String … そのまま\n"
          + "    Enum … 名前（表示名も可）か番号  Vector2 / Vector3 / Vector4 … \"x,y[,z[,w]]\"\n"
          + "    Color … \"r,g,b[,a]\"（0〜1）か \"#RRGGBB[AA]\"  Quaternion … \"x,y,z\"（オイラー角・度）か \"x,y,z,w\"\n"
          + "  propertyPath は \"m_LocalPosition\" の形。\"localPosition\" のように m_ を省いても探す。要素だけなら \"m_LocalPosition.y\"。\n"
          + "delete  args: objectPath（必須）\n"
          + "  オブジェクトを子ごと消す（Undo で戻せる）。プレファブインスタンスの途中の子は消せない（インスタンスのルートを指す）。\n"
          + "\n"
          + "objectPath / parentPath は \"Root/Child\" 形式。\"/\" の無い名前だけなら、シーン全体から同じ名前を 1 つ探す。";

        // ================================================================
        // hierarchy
        // ================================================================

        private static bool Hierarchy(Dictionary<string, string> args, out string message)
        {
            bool withComponents = false;
            if (!ReadBool(args, "components", ref withComponents, out message)) return false;

            int maxLines = DefaultMaxLines;
            if (Get(args, "maxLines", out string maxText))
            {
                if (!int.TryParse(maxText, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxLines) || maxLines < 1)
                {
                    message = "maxLines は 1 以上の整数で指定してください: " + maxText;
                    return false;
                }
                maxLines = Math.Min(maxLines, LimitMaxLines);
            }

            var roots = new List<Transform>();
            if (Get(args, "rootPath", out string rootPath))
            {
                if (!TryFindObject(rootPath, out Transform root, out message)) return false;
                roots.Add(root);
            }
            else
            {
                for (int s = 0; s < SceneManager.sceneCount; s++)
                {
                    var scene = SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded) continue;
                    roots.AddRange(scene.GetRootGameObjects().Select(g => g.transform));
                }
            }

            var sb = new StringBuilder();
            sb.Append("activeScene=").Append(SceneManager.GetActiveScene().path);

            int depth = DefaultDepth;
            if (Get(args, "depth", out string depthText)
                && (!int.TryParse(depthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out depth) || depth < 1))
            {
                message = "depth は 1 以上の整数で指定してください: " + depthText;
                return false;
            }

            int lines = 0, total = 0;
            var stack = new Stack<(Transform t, int level)>();
            for (int i = roots.Count - 1; i >= 0; i--) stack.Push((roots[i], 1));

            while (stack.Count > 0)
            {
                var (t, level) = stack.Pop();
                total++;
                if (lines >= maxLines) continue;
                lines++;

                sb.Append('\n').Append(PrefabInstantiateOps.GetHierarchyPath(t));
                if (!t.gameObject.activeInHierarchy) sb.Append(" (inactive)");
                if (withComponents)
                {
                    var names = t.GetComponents<Component>()
                                 .Select(c => c == null ? "(Missing)" : c.GetType().Name);
                    sb.Append("  [").Append(string.Join(", ", names)).Append(']');
                }

                if (level >= depth)
                {
                    // 深さの上限より下は数だけ示す。全部並べると人型 1 体で数千行になるため。
                    int below = t.GetComponentsInChildren<Transform>(true).Length - 1;
                    if (below > 0) sb.Append($"  …子孫 {below}");
                    continue;
                }

                for (int c = t.childCount - 1; c >= 0; c--) stack.Push((t.GetChild(c), level + 1));
            }

            sb.Append($"\nlisted={total}");
            if (total > lines) sb.Append($"（{lines} 行で打ち切り。rootPath で絞るか maxLines を増やす）");

            message = sb.ToString();
            return true;
        }

        // ================================================================
        // create_empty / create_primitive
        // ================================================================

        private static bool CreateEmpty(Dictionary<string, string> args, out string message)
        {
            Get(args, "name", out string name);
            if (!ReadPlacement(args, out Transform parent, out Vector3? position, out message)) return false;

            var go = new GameObject(string.IsNullOrEmpty(name) ? "GameObject" : name);
            return Place(go, parent, position, "PolyLing: Create Empty", out message);
        }

        private static bool CreatePrimitive(Dictionary<string, string> args, out string message)
        {
            if (!Get(args, "type", out string typeText))
            {
                message = "type がありません（Cube / Sphere / Capsule / Cylinder / Plane / Quad）";
                return false;
            }

            if (!Enum.TryParse(typeText, true, out PrimitiveType type) || !Enum.IsDefined(typeof(PrimitiveType), type))
            {
                message = "type は Cube / Sphere / Capsule / Cylinder / Plane / Quad のどれかです: " + typeText;
                return false;
            }

            Get(args, "name", out string name);
            if (!ReadPlacement(args, out Transform parent, out Vector3? position, out message)) return false;

            var go = GameObject.CreatePrimitive(type);
            go.name = string.IsNullOrEmpty(name) ? type.ToString() : name;
            return Place(go, parent, position, "PolyLing: Create " + type, out message);
        }

        private static bool ReadPlacement(
            Dictionary<string, string> args, out Transform parent, out Vector3? position, out string message)
        {
            parent   = null;
            position = null;
            message  = null;

            if (Get(args, "parentPath", out string parentPath)
                && !TryFindObject(parentPath, out parent, out message))
                return false;

            if (Get(args, "position", out string positionText))
            {
                if (!TryParseFloats(positionText, 3, 3, out float[] v))
                {
                    message = "position は \"x,y,z\" の形で指定してください: " + positionText;
                    return false;
                }
                position = new Vector3(v[0], v[1], v[2]);
            }

            return true;
        }

        private static bool Place(GameObject go, Transform parent, Vector3? position, string undoName, out string message)
        {
            if (parent != null) go.transform.SetParent(parent, false);
            if (position.HasValue) go.transform.localPosition = position.Value;

            Undo.RegisterCreatedObjectUndo(go, undoName);
            EditorSceneManager.MarkSceneDirty(go.scene);

            // Poly_Ling.Selection 名前空間と衝突するため完全修飾する。
            UnityEditor.Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            message = $"作りました: path={PrefabInstantiateOps.GetHierarchyPath(go.transform)}; "
                    + $"localPosition={Format(go.transform.localPosition)}; scene={go.scene.path}";
            return true;
        }

        // ================================================================
        // add_component
        // ================================================================

        private static bool AddComponent(Dictionary<string, string> args, out string message)
        {
            if (!RequireObject(args, out Transform target, out message)) return false;

            if (!Get(args, "componentType", out string typeName))
            {
                message = "componentType がありません";
                return false;
            }

            bool allowMultiple = false;
            if (!ReadBool(args, "allowMultiple", ref allowMultiple, out message)) return false;

            if (!PrefabComponentOps.TryResolveType(typeName, out Type type, out message)) return false;

            string where = PrefabInstantiateOps.GetHierarchyPath(target);

            int existing = target.GetComponents(type).Length;
            if (existing > 0 && !allowMultiple)
            {
                message = $"既に付いています: {type.FullName} → {where}（{existing} 個）。何も変えていません";
                return true;
            }

            var added = Undo.AddComponent(target.gameObject, type);
            if (added == null)
            {
                message = $"付けられませんでした: {type.FullName} → {where}（RequireComponent・DisallowMultipleComponent などで拒否された）";
                return false;
            }

            EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
            message = $"付けました: {type.FullName} → {where}（{existing + 1} 個目）";
            return true;
        }

        // ================================================================
        // delete
        // ================================================================

        private static bool Delete(Dictionary<string, string> args, out string message)
        {
            if (!RequireObject(args, out Transform target, out message)) return false;

            var go = target.gameObject;
            string where = PrefabInstantiateOps.GetHierarchyPath(target);

            // プレファブインスタンスの途中の子は、プレファブを開かずには消せない（Unity が拒否する）。
            if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsOutermostPrefabInstanceRoot(go))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                message = $"プレファブインスタンスの途中の子は消せません: {where}。"
                        + $"インスタンスごと消すならルート {(root != null ? PrefabInstantiateOps.GetHierarchyPath(root.transform) : "?")} を指定してください";
                return false;
            }

            int below = target.GetComponentsInChildren<Transform>(true).Length - 1;
            var scene = go.scene;

            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(scene);

            message = $"消しました: {where}（子孫 {below} 個も含む。Undo で戻せる）";
            return true;
        }

        // ================================================================
        // get_properties / set_property
        // ================================================================

        private static bool GetProperties(Dictionary<string, string> args, out string message)
        {
            if (!RequireObject(args, out Transform target, out message)) return false;

            string where = PrefabInstantiateOps.GetHierarchyPath(target);

            if (!Get(args, "componentType", out string typeName))
            {
                var comps = target.GetComponents<Component>();
                var sb0 = new StringBuilder($"object={where}; components={comps.Length}");
                foreach (var c in comps)
                    sb0.Append('\n').Append(c == null ? "(Missing script)" : c.GetType().FullName);
                message = sb0.ToString();
                return true;
            }

            if (!TryFindComponent(target, args, typeName, out Component comp, out message)) return false;

            var so = new SerializedObject(comp);
            var sb = new StringBuilder($"object={where}; component={comp.GetType().FullName}");

            var it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.propertyPath == "m_Script") continue;
                sb.Append('\n').Append(it.propertyPath)
                  .Append(" (").Append(it.propertyType).Append(") = ")
                  .Append(Describe(it));
            }

            message = sb.ToString();
            return true;
        }

        private static bool SetProperty(Dictionary<string, string> args, out string message)
        {
            if (!RequireObject(args, out Transform target, out message)) return false;

            if (!Get(args, "componentType", out string typeName))
            {
                message = "componentType がありません";
                return false;
            }
            if (!Get(args, "propertyPath", out string propertyPath))
            {
                message = "propertyPath がありません（get_properties で確かめる）";
                return false;
            }
            if (!args.TryGetValue("value", out string value) || value == null)
            {
                message = "value がありません";
                return false;
            }

            if (!TryFindComponent(target, args, typeName, out Component comp, out message)) return false;

            var so = new SerializedObject(comp);
            var prop = FindProperty(so, propertyPath);
            if (prop == null)
            {
                message = $"プロパティが見つかりません: {propertyPath}（{comp.GetType().Name}。get_properties でパスを確かめる）";
                return false;
            }

            string before = Describe(prop);
            if (!TryWrite(prop, value.Trim(), out message)) return false;

            if (!so.ApplyModifiedProperties())
            {
                message = $"値が変わりませんでした: {prop.propertyPath} = {before}";
                return true;
            }

            EditorSceneManager.MarkSceneDirty(target.gameObject.scene);

            so.Update();
            var after = so.FindProperty(prop.propertyPath);
            message = $"書きました: {PrefabInstantiateOps.GetHierarchyPath(target)} {comp.GetType().Name}.{prop.propertyPath}"
                    + $" = {(after != null ? Describe(after) : value)}（前 {before}）";
            return true;
        }

        /// <summary>そのままで無ければ m_ を付けて探す（"localPosition" → "m_LocalPosition"）。</summary>
        private static SerializedProperty FindProperty(SerializedObject so, string path)
        {
            var p = so.FindProperty(path);
            if (p != null) return p;

            if (path.StartsWith("m_", StringComparison.Ordinal) || path.Length == 0) return null;

            int dot = path.IndexOf('.');
            string head = dot < 0 ? path : path.Substring(0, dot);
            string tail = dot < 0 ? ""   : path.Substring(dot);
            return so.FindProperty("m_" + char.ToUpperInvariant(head[0]) + head.Substring(1) + tail);
        }

        private static bool TryWrite(SerializedProperty p, string text, out string message)
        {
            message = null;
            var inv = CultureInfo.InvariantCulture;

            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (!long.TryParse(text, NumberStyles.Integer, inv, out long l)) return Fail("整数", text, out message);
                    p.longValue = l;
                    return true;

                case SerializedPropertyType.Float:
                    if (!double.TryParse(text, NumberStyles.Float, inv, out double d)) return Fail("数値", text, out message);
                    p.doubleValue = d;
                    return true;

                case SerializedPropertyType.Boolean:
                    if (!bool.TryParse(text, out bool b)) return Fail("true / false", text, out message);
                    p.boolValue = b;
                    return true;

                case SerializedPropertyType.String:
                    p.stringValue = text;
                    return true;

                case SerializedPropertyType.Enum:
                {
                    int idx = Array.FindIndex(p.enumNames, n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0) idx = Array.FindIndex(p.enumDisplayNames, n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0 && int.TryParse(text, NumberStyles.Integer, inv, out int n0) && n0 >= 0 && n0 < p.enumNames.Length) idx = n0;
                    if (idx < 0)
                    {
                        message = $"{p.propertyPath} の値は次のどれかです: {string.Join(" / ", p.enumNames)}（指定: {text}）";
                        return false;
                    }
                    p.enumValueIndex = idx;
                    return true;
                }

                case SerializedPropertyType.Vector2:
                    if (!TryParseFloats(text, 2, 2, out float[] v2)) return Fail("\"x,y\"", text, out message);
                    p.vector2Value = new Vector2(v2[0], v2[1]);
                    return true;

                case SerializedPropertyType.Vector3:
                    if (!TryParseFloats(text, 3, 3, out float[] v3)) return Fail("\"x,y,z\"", text, out message);
                    p.vector3Value = new Vector3(v3[0], v3[1], v3[2]);
                    return true;

                case SerializedPropertyType.Vector4:
                    if (!TryParseFloats(text, 4, 4, out float[] v4)) return Fail("\"x,y,z,w\"", text, out message);
                    p.vector4Value = new Vector4(v4[0], v4[1], v4[2], v4[3]);
                    return true;

                case SerializedPropertyType.Color:
                {
                    if (text.StartsWith("#", StringComparison.Ordinal))
                    {
                        if (!ColorUtility.TryParseHtmlString(text, out Color hc)) return Fail("\"#RRGGBB[AA]\"", text, out message);
                        p.colorValue = hc;
                        return true;
                    }
                    if (!TryParseFloats(text, 3, 4, out float[] c)) return Fail("\"r,g,b[,a]\" か \"#RRGGBB[AA]\"", text, out message);
                    p.colorValue = new Color(c[0], c[1], c[2], c.Length == 4 ? c[3] : 1f);
                    return true;
                }

                case SerializedPropertyType.Quaternion:
                {
                    if (!TryParseFloats(text, 3, 4, out float[] q)) return Fail("\"x,y,z\"（オイラー角・度）か \"x,y,z,w\"", text, out message);
                    p.quaternionValue = q.Length == 3 ? Quaternion.Euler(q[0], q[1], q[2]) : new Quaternion(q[0], q[1], q[2], q[3]);
                    return true;
                }

                default:
                    message = $"この型はまだ書けません: {p.propertyPath} ({p.propertyType})。"
                            + "対応: Integer / Float / Boolean / String / Enum / Vector2 / Vector3 / Vector4 / Color / Quaternion";
                    return false;
            }
        }

        private static bool Fail(string expected, string text, out string message)
        {
            message = $"value は {expected} の形で指定してください: {text}";
            return false;
        }

        /// <summary>プロパティの今の値を 1 行で。</summary>
        private static string Describe(SerializedProperty p)
        {
            var inv = CultureInfo.InvariantCulture;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:   return p.longValue.ToString(inv);
                case SerializedPropertyType.Float:
                    // float を double で読むと 0.05 が 0.05000000074505806 になる。
                    return p.type == "double" ? p.doubleValue.ToString("R", inv) : p.floatValue.ToString("R", inv);
                case SerializedPropertyType.LayerMask: return p.intValue.ToString(inv);
                case SerializedPropertyType.Boolean:   return p.boolValue ? "true" : "false";
                case SerializedPropertyType.String:    return "\"" + p.stringValue + "\"";
                case SerializedPropertyType.Enum:
                    return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length
                        ? $"{p.enumNames[p.enumValueIndex]}（{string.Join(" / ", p.enumNames)}）"
                        : p.intValue.ToString(inv);
                case SerializedPropertyType.Vector2:   return Format(p.vector2Value);
                case SerializedPropertyType.Vector3:   return Format(p.vector3Value);
                case SerializedPropertyType.Vector4:   return Format(p.vector4Value);
                case SerializedPropertyType.Color:     return Format(p.colorValue);
                case SerializedPropertyType.Quaternion:
                    return $"{Format(p.quaternionValue)}（オイラー {Format(p.quaternionValue.eulerAngles)}）";
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue == null ? "None" : $"{p.objectReferenceValue.name} ({p.objectReferenceValue.GetType().Name})";
                default:
                    return p.isArray ? $"[{p.arraySize}]" : "-";
            }
        }

        // ================================================================
        // 対象・コンポーネントの解決
        // ================================================================

        private static bool RequireObject(Dictionary<string, string> args, out Transform target, out string message)
        {
            target = null;
            if (!Get(args, "objectPath", out string path))
            {
                message = "objectPath がありません（hierarchy でパスを確かめる）";
                return false;
            }
            return TryFindObject(path, out target, out message);
        }

        /// <summary>"Root/Child" で探し、無ければ "/" の無い名前をシーン全体から 1 つ探す。</summary>
        private static bool TryFindObject(string path, out Transform target, out string message)
        {
            message = null;
            string p = (path ?? string.Empty).Replace('\\', '/').Trim().Trim('/');

            target = PrefabInstantiateOps.FindInLoadedScenes(p);
            if (target != null) return true;

            if (p.Length > 0 && p.IndexOf('/') < 0)
            {
                var hits = new List<Transform>();
                for (int s = 0; s < SceneManager.sceneCount; s++)
                {
                    var scene = SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded) continue;
                    foreach (var root in scene.GetRootGameObjects())
                        hits.AddRange(root.GetComponentsInChildren<Transform>(true).Where(t => t.name == p));
                }

                if (hits.Count == 1) { target = hits[0]; return true; }
                if (hits.Count > 1)
                {
                    message = $"名前 {p} のオブジェクトが {hits.Count} 個あります。パスで指定してください: "
                            + string.Join(", ", hits.Select(t => PrefabInstantiateOps.GetHierarchyPath(t)));
                    return false;
                }
            }

            message = $"オブジェクトが見つかりません: {path}（\"Root/Child\" 形式。hierarchy で確かめる）";
            return false;
        }

        /// <summary>対象に付いているコンポーネントから型名で探す。同じ型が複数なら componentIndex で選ぶ。</summary>
        private static bool TryFindComponent(
            Transform target, Dictionary<string, string> args, string typeName, out Component comp, out string message)
        {
            comp    = null;
            message = null;

            var all = target.GetComponents<Component>().Where(c => c != null).ToList();
            var hits = all.Where(c => c.GetType().FullName == typeName).ToList();
            if (hits.Count == 0) hits = all.Where(c => c.GetType().Name == typeName).ToList();

            if (hits.Count == 0)
            {
                message = $"{PrefabInstantiateOps.GetHierarchyPath(target)} に {typeName} は付いていません。付いているもの: "
                        + string.Join(", ", all.Select(c => c.GetType().Name));
                return false;
            }

            if (hits.Select(c => c.GetType()).Distinct().Count() > 1)
            {
                message = $"同じ短い名前の型が複数あります: {typeName}。完全名で指定してください: "
                        + string.Join(", ", hits.Select(c => c.GetType().FullName).Distinct());
                return false;
            }

            int index = 0;
            if (Get(args, "componentIndex", out string indexText)
                && (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || index < 0))
            {
                message = "componentIndex は 0 以上の整数で指定してください: " + indexText;
                return false;
            }
            if (index >= hits.Count)
            {
                message = $"componentIndex={index} ですが、{typeName} は {hits.Count} 個です（0 始まり）";
                return false;
            }

            comp = hits[index];
            return true;
        }

        // ================================================================
        // 引数の読み取り
        // ================================================================

        /// <summary>空でない値があれば true。</summary>
        private static bool Get(Dictionary<string, string> args, string key, out string value)
        {
            value = null;
            if (!args.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value)) { value = null; return false; }
            value = value.Trim();
            return true;
        }

        /// <summary>キーが無ければ target を変えず true。解釈できなければ false。</summary>
        private static bool ReadBool(Dictionary<string, string> args, string key, ref bool target, out string message)
        {
            message = null;
            if (!Get(args, key, out string text)) return true;
            if (bool.TryParse(text, out bool v)) { target = v; return true; }
            message = $"{key} は true / false で指定してください: {text}";
            return false;
        }

        /// <summary>"a,b,c" を min〜max 個の数値として読む。</summary>
        private static bool TryParseFloats(string text, int min, int max, out float[] values)
        {
            values = null;
            string[] parts = (text ?? string.Empty).Split(',');
            if (parts.Length < min || parts.Length > max) return false;

            var result = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
                    return false;
            }
            values = result;
            return true;
        }

        private static string Format(Vector2 v)    => string.Format(CultureInfo.InvariantCulture, "{0},{1}", v.x, v.y);
        private static string Format(Vector3 v)    => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", v.x, v.y, v.z);
        private static string Format(Vector4 v)    => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", v.x, v.y, v.z, v.w);
        private static string Format(Color c)      => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", c.r, c.g, c.b, c.a);
        private static string Format(Quaternion q) => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", q.x, q.y, q.z, q.w);
    }
}

// ModelContext.Data.cs
// ModelContext：モデル単位の付帯データ（モーフ式・選択辞書・オブジェクトグループ・データストア・
// マテリアル・対称・ミラーペア・揺れもの・VRM・リターゲット・座標規約）。
// Runtime/Poly_Ling_Main/Core/Context/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.EditorBridge;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Ops;
using Poly_Ling.Tools;
using Poly_Ling.Symmetry;
using Poly_Ling.UndoSystem;
using Poly_Ling.Materials;

namespace Poly_Ling.Context
{
    public partial class ModelContext
    {
        // ================================================================
        // MorphExpressions（モーフグループ管理）
        // ================================================================

        /// <summary>モーフエクスプレッションリスト</summary>
        public List<MorphExpression> MorphExpressions { get; set; } = new List<MorphExpression>();

        /// <summary>モーフエクスプレッション数</summary>
        public int MorphExpressionCount => MorphExpressions?.Count ?? 0;

        /// <summary>モーフエクスプレッションがあるか</summary>
        public bool HasMorphExpressions => MorphExpressionCount > 0;

        /// <summary>モーフエクスプレッションを追加</summary>
        public MorphExpression AddMorphExpression(string name, MorphType type = MorphType.Vertex)
        {
            var set = new MorphExpression(name, type);
            MorphExpressions.Add(set);
            return set;
        }

        /// <summary>モーフエクスプレッションを削除</summary>
        public bool RemoveMorphExpression(MorphExpression set)
        {
            return MorphExpressions.Remove(set);
        }

        /// <summary>名前でモーフエクスプレッションを検索</summary>
        public MorphExpression FindMorphExpressionByName(string name)
        {
            return MorphExpressions.Find(s => s.Name == name);
        }

        /// <summary>メッシュインデックスでモーフエクスプレッションを検索</summary>
        public MorphExpression FindMorphExpressionByMesh(int meshIndex)
        {
            return MorphExpressions.Find(s => s.ContainsMesh(meshIndex));
        }

        /// <summary>一意なモーフエクスプレッション名を生成</summary>
        public string GenerateUniqueMorphExpressionName(string baseName = "Morph")
        {
            string name = baseName;
            int counter = 1;

            while (FindMorphExpressionByName(name) != null)
            {
                name = $"{baseName}_{counter}";
                counter++;
            }

            return name;
        }
        
        // ================================================================
        // MeshSelectionSets（メッシュ選択セット・名前ベース）
        // ================================================================

        /// <summary>メッシュ選択セットリスト</summary>
        public List<MeshSelectionSet> MeshSelectionSets { get; set; } = new List<MeshSelectionSet>();

        /// <summary>メッシュ選択セット数</summary>
        public int MeshSelectionSetCount => MeshSelectionSets?.Count ?? 0;

        /// <summary>メッシュ選択セットがあるか</summary>
        public bool HasMeshSelectionSets => MeshSelectionSetCount > 0;

        /// <summary>現在の選択をメッシュ選択セットとして保存</summary>
        public MeshSelectionSet SaveCurrentMeshSelectionAsSet(string name, SelectionCategory category)
        {
            var set = MeshSelectionSet.FromCurrentSelection(name, this, category);
            MeshSelectionSets.Add(set);
            return set;
        }

        /// <summary>メッシュ選択セットを削除</summary>
        public bool RemoveMeshSelectionSet(MeshSelectionSet set)
        {
            return MeshSelectionSets.Remove(set);
        }

        /// <summary>名前でメッシュ選択セットを検索</summary>
        public MeshSelectionSet FindMeshSelectionSetByName(string name)
        {
            return MeshSelectionSets.Find(s => s.Name == name);
        }

        /// <summary>一意なメッシュ選択セット名を生成</summary>
        public string GenerateUniqueMeshSelectionSetName(string baseName = "MeshSet")
        {
            string name = baseName;
            int counter = 1;
            while (FindMeshSelectionSetByName(name) != null)
            {
                name = $"{baseName}_{counter}";
                counter++;
            }
            return name;
        }

        // ================================================================
        // ObjectGroups（入力ソース＋生成パラメータ＋出力先のまとまり）
        //
        // 【索引の付け替えが要らない理由】
        //   ObjectGroup は参照を MeshContext.ObjectId で持つ。挿入・削除・並べ替えで
        //   値が変わらないので RemapIndexReferences の対象に入れない。
        //   MorphExpressions が索引参照ゆえに AdjustIndicesOnInsert/OnRemove という
        //   別経路を持っているのに対し、こちらはその経路自体が不要になる。
        //
        // 【削除時に勝手に消さない】
        //   参照先の描画オブジェクトが消えてもグループはここへ残す。
        //   RemoveAt の中で消すと、その削除は ObjectGroup の Undo レコードに
        //   載らないため Undo で戻せなくなる。参照切れは
        //   ModelInvariantChecker が報告し、後始末は明示操作
        //   （ObjectGroupOps.PurgeMissing）で行う。
        // ================================================================

        /// <summary>オブジェクトグループのリスト。</summary>
        public List<Poly_Ling.Data.ObjectGroup> ObjectGroups { get; set; }
            = new List<Poly_Ling.Data.ObjectGroup>();

        /// <summary>オブジェクトグループ数</summary>
        public int ObjectGroupCount => ObjectGroups?.Count ?? 0;

        /// <summary>オブジェクトグループがあるか</summary>
        public bool HasObjectGroups => ObjectGroupCount > 0;

        /// <summary>オブジェクトグループを追加する。</summary>
        public void AddObjectGroup(Poly_Ling.Data.ObjectGroup group)
        {
            if (group == null) return;
            if (ObjectGroups == null) ObjectGroups = new List<Poly_Ling.Data.ObjectGroup>();
            ObjectGroups.Add(group);
            IsDirty = true;
        }

        /// <summary>オブジェクトグループを削除する。</summary>
        public bool RemoveObjectGroup(Poly_Ling.Data.ObjectGroup group)
        {
            if (group == null || ObjectGroups == null) return false;
            bool ok = ObjectGroups.Remove(group);
            if (ok) IsDirty = true;
            return ok;
        }

        /// <summary>名前でオブジェクトグループを検索。見つからなければ null。</summary>
        public Poly_Ling.Data.ObjectGroup FindObjectGroupByName(string name)
        {
            if (ObjectGroups == null || string.IsNullOrEmpty(name)) return null;
            return ObjectGroups.Find(g => g != null && g.Name == name);
        }

        /// <summary>
        /// 出力先の ObjectId でオブジェクトグループを検索。見つからなければ null。
        /// どのステップの出力でも当たる（ステップ 0 に限らない）。
        /// </summary>
        public Poly_Ling.Data.ObjectGroup FindObjectGroupByOutput(ulong objectId)
        {
            if (ObjectGroups == null || objectId == 0UL) return null;
            return ObjectGroups.Find(g => g != null && g.ContainsOutput(objectId));
        }

        /// <summary>一意なオブジェクトグループ名を生成する（規則は選択セット名と同じ）。</summary>
        public string GenerateUniqueObjectGroupName(string baseName = "Group")
        {
            if (string.IsNullOrEmpty(baseName)) baseName = "Group";
            string name = baseName;
            int counter = 1;
            while (FindObjectGroupByName(name) != null)
            {
                name = $"{baseName}_{counter}";
                counter++;
            }
            return name;
        }

        // ================================================================
        // DataStore（コマンドが返した実データの置き場）
        //
        // 【なぜモデルが持つか】
        //   コマンドの戻り値は名前と件数だけにし、量のあるもの（頂点番号・
        //   境界ループ・計測値）はここへ書く。呼び出し側は名前を次のコマンドへ
        //   渡す。詳しくは PLDataStore.cs の冒頭注記。
        //
        // 【索引の付け替えが要らない理由】
        //   項目は対象を MasterIndex と ObjectId の両方で持つ。並べ替えの後は
        //   ObjectId で引き直せるので、ObjectGroups と同じく
        //   RemapIndexReferences の対象に入れない。
        //
        // 【差し替えない】
        //   読み込みは PLDataStore.ReplaceAll で中身だけを入れ替える。
        //   参照を持ち替えると、既に取得した側と食い違う。
        // ================================================================

        /// <summary>コマンドが返した実データの辞書。常に非 null。</summary>
        public Poly_Ling.Data.PLDataStore DataStore { get; } = new Poly_Ling.Data.PLDataStore();

        /// <summary>辞書の項目数。</summary>
        public int DataStoreCount => DataStore?.Count ?? 0;

        /// <summary>辞書に項目があるか。</summary>
        public bool HasDataStoreEntries => DataStoreCount > 0;

        /// <summary>名前で描画オブジェクト(MeshContext)を検索。見つからなければ null。</summary>
        public MeshContext FindMeshContextByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return MeshContextList.Find(mc => mc != null && mc.Name == name);
        }

        /// <summary>ObjectId で描画オブジェクトを検索。見つからなければ null。</summary>
        public MeshContext FindMeshContextByObjectId(ulong objectId)
        {
            if (objectId == 0UL || MeshContextList == null) return null;
            for (int i = 0; i < MeshContextList.Count; i++)
            {
                var mc = MeshContextList[i];
                if (mc != null && mc.ObjectId == objectId) return mc;
            }
            return null;
        }

        /// <summary>
        /// 一意な描画オブジェクト名を生成する。
        /// 既存名と衝突する場合のみ _1, _2 ... を付ける（規則は選択セット名と同じ）。
        /// </summary>
        public string GenerateUniqueMeshName(string baseName = "Mesh")
        {
            if (string.IsNullOrEmpty(baseName)) baseName = "Mesh";

            string name = baseName;
            int counter = 1;
            while (FindMeshContextByName(name) != null)
            {
                name = $"{baseName}_{counter}";
                counter++;
            }
            return name;
        }

        // ================================================================
        // Materials（モデル単位で実データを保持）
        // MaterialReference によるパラメータ＋キャッシュ管理
        // ================================================================
        
        // v1.2: 空リストで初期化（インポート時に自動追加されないように）
        private List<MaterialReference> _materialRefs = new List<MaterialReference>();
        private int _currentMaterialIndex = 0;
        
        /// <summary>
        /// マテリアル参照リスト（新API）
        /// パラメータデータ＋アセットパス＋キャッシュを管理
        /// </summary>
        public List<MaterialReference> MaterialReferences
        {
            get => _materialRefs;
            set => _materialRefs = value ?? new List<MaterialReference>();
        }
        
        /// <summary>
        /// マテリアルリスト（後方互換API）
        /// 内部的にはMaterialReferenceから取得/設定
        /// </summary>
        public List<Material> Materials
        {
            get => _materialRefs.Select(r => r?.Material).ToList();
            set
            {
                if (value == null || value.Count == 0)
                {
                    _materialRefs = new List<MaterialReference>();
                }
                else
                {
                    _materialRefs = value.Select(m => new MaterialReference(m)).ToList();
                }
            }
        }
        
        /// <summary>
        /// 現在選択中のマテリアルインデックス
        /// </summary>
        public int CurrentMaterialIndex
        {
            get => _currentMaterialIndex;
            set => _currentMaterialIndex = value;
        }
        
        // ================================================================
        // デフォルトマテリアル設定
        // ================================================================
        
        private List<MaterialReference> _defaultMaterialRefs = new List<MaterialReference> { new MaterialReference() };
        
        /// <summary>新規メッシュ作成時に適用されるデフォルトマテリアル参照リスト</summary>
        public List<MaterialReference> DefaultMaterialReferences
        {
            get => _defaultMaterialRefs;
            set => _defaultMaterialRefs = value ?? new List<MaterialReference> { new MaterialReference() };
        }
        
        /// <summary>新規メッシュ作成時に適用されるデフォルトマテリアルリスト（後方互換API）</summary>
        public List<Material> DefaultMaterials
        {
            get => _defaultMaterialRefs.Select(r => r?.Material).ToList();
            set
            {
                if (value == null || value.Count == 0)
                {
                    _defaultMaterialRefs = new List<MaterialReference> { new MaterialReference() };
                }
                else
                {
                    _defaultMaterialRefs = value.Select(m => new MaterialReference(m)).ToList();
                }
            }
        }
        
        /// <summary>新規メッシュ作成時に適用されるデフォルトカレントマテリアルインデックス</summary>
        public int DefaultCurrentMaterialIndex { get; set; } = 0;
        
        /// <summary>マテリアル変更時に自動でデフォルトに設定するか</summary>
        public bool AutoSetDefaultMaterials { get; set; } = true;
        
        // ================================================================
        // マテリアル操作ヘルパーメソッド
        // ================================================================
        
        /// <summary>マテリアル数</summary>
        public int MaterialCount => _materialRefs.Count;
        
        /// <summary>インデックスでマテリアルを取得</summary>
        public Material GetMaterial(int index)
        {
            if (index < 0 || index >= _materialRefs.Count)
                return null;
            return _materialRefs[index]?.Material;
        }
        
        /// <summary>インデックスでマテリアルを設定</summary>
        public void SetMaterial(int index, Material mat)
        {
            if (index < 0 || index >= _materialRefs.Count)
                return;
            _materialRefs[index] = new MaterialReference(mat);
        }
        
        /// <summary>マテリアルを追加</summary>
        public void AddMaterial(Material mat)
        {
            _materialRefs.Add(new MaterialReference(mat));
        }
        
        /// <summary>インデックスでマテリアルを削除</summary>
        public void RemoveMaterialAt(int index)
        {
            if (index < 0 || index >= _materialRefs.Count)
                return;
            if (_materialRefs.Count <= 1)
                return; // 最低1つは残す
            
            _materialRefs.RemoveAt(index);
            
            // CurrentMaterialIndexを調整
            if (_currentMaterialIndex >= _materialRefs.Count)
            {
                _currentMaterialIndex = _materialRefs.Count - 1;
            }
        }
        
        /// <summary>全マテリアルをクリア（1つのnullマテリアルにリセット）</summary>
        public void ClearMaterials()
        {
            _materialRefs.Clear();
            _materialRefs.Add(new MaterialReference());
            _currentMaterialIndex = 0;
        }
        
        /// <summary>インデックスでマテリアル参照を取得</summary>
        public MaterialReference GetMaterialReference(int index)
        {
            if (index < 0 || index >= _materialRefs.Count)
                return null;
            return _materialRefs[index];
        }
        
        /// <summary>マテリアルリストを一括設定</summary>
        public void SetMaterials(IList<Material> materials)
        {
            if (materials == null || materials.Count == 0)
            {
                _materialRefs = new List<MaterialReference> { new MaterialReference() };
            }
            else
            {
                _materialRefs = materials.Select(m => new MaterialReference(m)).ToList();
            }
        }
        
        /// <summary>オンメモリマテリアル（アセット未保存）があるか</summary>
        public bool HasOnMemoryMaterials()
        {
            return _materialRefs.Any(r => r != null && !r.HasAssetPath && r.Material != null);
        }
        
        /// <summary>オンメモリマテリアルをアセットとして保存</summary>
        /// <param name="saveDir">保存先ディレクトリ（Assets/...）</param>
        /// <returns>保存したマテリアル数</returns>
        public int SaveOnMemoryMaterialsAsAssets(string saveDir)
        {
            if (string.IsNullOrEmpty(saveDir))
                return 0;
            
            // ディレクトリを作成
            if (!System.IO.Directory.Exists(saveDir))
            {
                System.IO.Directory.CreateDirectory(saveDir);
                PLEditorBridge.I.Refresh();
            }
            
            int savedCount = 0;
            for (int i = 0; i < _materialRefs.Count; i++)
            {
                var matRef = _materialRefs[i];
                if (matRef == null || matRef.HasAssetPath || matRef.Material == null)
                    continue;
                
                // ファイル名を生成
                string matName = matRef.Name ?? $"Material_{i}";
                // 無効な文字を置換
                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                {
                    matName = matName.Replace(c, '_');
                }
                
                string savePath = $"{saveDir}/{matName}.mat";

                // 決定論パス：同名は同一 .mat に収束（_counter 廃止）。
                // 既存があれば SaveAsAsset 内で内容上書き（GUID/参照保持）＝増殖しない。
                if (matRef.SaveAsAsset(savePath))
                {
                    savedCount++;
                }
            }
            
            return savedCount;
        }

        // ================================================================
        // 対称設定
        // ================================================================

        /// <summary>対称モード設定</summary>
        public SymmetrySettings SymmetrySettings { get; } = new SymmetrySettings();

        // ================================================================
        // ミラーペア
        // ================================================================

        /// <summary>ミラーペアのリスト（実体側↔ミラー側の対応）</summary>
        public List<MirrorPair> MirrorPairs { get; set; } = new List<MirrorPair>();

        // ================================================================
        // スプリングボーン・コライダーグループ（モデルレベル：名前のみ保持）
        //   VRM SpringBone の colliderGroups に相当。ここには名前だけを持ち、
        //   index＝この並び順。コライダー本体は各ボーンの
        //   MeshObject.SpringBoneColliders が保持し、所属は index で参照する。
        // ================================================================

        /// <summary>スプリングボーン・コライダーグループ名リスト（index＝並び順）。</summary>
        public List<string> SpringBoneColliderGroupNames { get; set; } = new List<string>();

        // ================================================================
        // スプリングボーン・評価設定（モデルレベル）
        //   揺れ評価のマネージャ単位パラメータ。VRM 仕様外の実装依存値だが、
        //   モデルごとに揺れの見た目が変わるためモデルに帰属させる。
        // ================================================================

        /// <summary>
        /// 揺れ評価の固定タイムステップ[秒]。0=実時間（フレーム時間）を使用する。
        /// </summary>
        public float SpringBoneFixedDeltaTime { get; set; } = 0f;

        /// <summary>
        /// 揺れ評価開始直後に状態を安定化させるフレーム数。
        /// </summary>
        public int SpringBoneWarmupFrames { get; set; } = 3;

        // ================================================================
        // VRM 1.0 のモデルレベル設定
        //   出力にだけ使う値で、編集中の見た目には影響しない。
        //   どちらも null＝未設定で、その場合は VRM 側の既定が載る。
        //   一人称カメラでの見え方だけは描画オブジェクトごとに決まるため、
        //   MeshObject.VrmFirstPerson が持つ（ここには置かない）。
        // ================================================================

        /// <summary>VRM メタ情報（作者・ライセンス）。null=未設定。</summary>
        public VrmMetaData VrmMeta { get; set; } = null;

        /// <summary>VRM 視線設定。null=未設定（UniVRM の既定のまま出る）。</summary>
        public VrmLookAtData VrmLookAt { get; set; } = null;

        // ================================================================
        // Humanoid Avatar のリターゲット設定
        //   Avatar 生成（Editor のプレファブ書き出し）だけが使う値。
        //   Player 内の表示にも VRM 出力にも影響しない。
        //   VRM 1.0 にはこの設定を載せる場所が無い。
        // ================================================================

        /// <summary>Avatar リターゲット設定8項目。null=未設定（Unity の既定を使う）。</summary>
        public AvatarRetargetData AvatarRetarget { get; set; } = null;

        // ================================================================
        // PMX / MQO の座標規約
        //   倍率と軸反転。読み書きだけでなく VMD／統合モーションの位置スケールにも
        //   効く。表示状態ではないので editorstate.csv とは別に持つ。
        // ================================================================

        /// <summary>PMX / MQO の座標規約。null=未設定（読み手の既定値を使う）。</summary>
        public CoordinateConventionData CoordinateConvention { get; set; } = null;
    }
}

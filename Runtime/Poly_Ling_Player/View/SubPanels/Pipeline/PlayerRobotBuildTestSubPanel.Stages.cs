// PlayerRobotBuildTestSubPanel.Stages.cs
// ロボ組み立て自動検証の段。系統ごとに並びを組む。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【段の並び】
//   S1 図形生成      … 小判型と六角柱をコマンドで置く。回転は頂点へ焼き込む。
//   S2 原点          … ApplyObjectOriginsCommand で位置を入れる。
//   S3 階層          … ReorderMeshesCommand で親子を張る。
//   S4 仕切り        … センター下部を X 中央で 2 穴に割る（ブリッジ系統のみ）。
//   S5 面削除        … 上半身2 の中段側面と上蓋中央を落として穴を開ける（同上）。
//   S6 頂点数合わせ  … 穴つなぎの前提（両穴の頂点数が同じ）を満たす（同上）。
//   S7 穴つなぎ      … 関節どうしを面で橋渡しする（同上）。
//   S8 ミラー分岐    … 左半身から右半身を出す（片側系統以外）。
//   S9 スキンド変換  … メッシュからボーンとスキンを作る（スキン系統のみ）。
//   S10 Humanoid     … 自動割当。
//   S11 VRM 書き出し … 必須関節の補完を ON にして出す。
//
// 【穴の頂点数】
//   小判型（LengthSegments=3 / CapSegments=3）の開口は 12 点。
//   仕切ると 6 / 6 に割れる。上半身2 の中段側面を落とすと腕用が左右 8 点、
//   上蓋中央を落とすと首用が 14 点。六角柱の開口は 6 点。
//   差は MatchHoleRingCountCommand で吸収する。
//
// 【種頂点の決め方】
//   穴つなぎ・頂点数合わせは種頂点で穴を指す。頂点番号は生成順に依存するので、
//   番号を直に書かず「相手メッシュに最も近い境界頂点」を実データから選ぶ。
//   PlayerPipelineTestSubPanel の CreateOneBridge と同じ考え方。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Ops;
using Poly_Ling.Selection;   // VertexPair
using Poly_Ling.Tools;       // BoneMoveMode

namespace Poly_Ling.Player
{
    public partial class PlayerRobotBuildTestSubPanel
    {
        // ================================================================
        // 段の組み立て
        // ================================================================

        private void BuildStages(Variant v)
        {
            _stages.Clear();

            var parts = PartsOf(v);

            // ── 部位ごとに「生成 → 保存 → 姿勢 → 保存」を繰り返す ──
            //   生成は原点に置く。次の部位も原点に出るので、置いたものは
            //   姿勢で退かしてから次へ進む。重なったままだと目視で追えない。
            _stages.Add(new Stage { Name = "S1 図形生成", Group = true });

            // 空のモデルを用意する。これを飛ばすとモデルが無いままコマンドを送ることになり、
            // ディスパッチャが握り潰して何も作られない（保存も ActiveProject が無くて失敗する）。
            _stages.Add(new Stage
            {
                Name    = "モデル作成",
                Run     = StageCreateModel,
                Save    = false,
                Purpose = "空のモデルを 1 つ用意します。ここから部品を積み上げます。",
                HowTo   = new[] { "メニューから新規モデルを作る" },
                Note    = "前に作ったモデルが残っていると、保存したフォルダに関係ない\n"
                        + "モデルが一緒に入ってしまいます。先に全部捨ててから作ります。",
            });

            foreach (string name in parts)
            {
                string partName = name;
                var part = RobotBuildRecipe.Find(partName);
                bool stadium = part.Stadium;

                _stages.Add(new Stage
                {
                    Name    = $"{partName}_生成",
                    Run     = () => StageCreateOne(partName),
                    Purpose = $"「{partName}」の素材となる基本図形を、原点に作ります。",
                    HowTo   = stadium
                        ? new[]
                        {
                            "「基本図形」パネルを開く",
                            "形に「小判型」を選ぶ",
                            $"長さ {part.Length:0.###} / 高さ {part.Height:0.###} / 奥行 {part.Depth:0.###} を入れる",
                            $"分割は 長さ {RobotBuildRecipe.StadiumLengthSegments} / 丸み {RobotBuildRecipe.StadiumCapSegments} / 高さ {part.HeightSegments}",
                            $"上の蓋を{(part.CapTop ? "付ける" : "外す")}、下の蓋を外す",
                            "ピボットを下端にする",
                            $"名前を「{partName}」にして「生成」を押す",
                        }
                        : new[]
                        {
                            "「基本図形」パネルを開く",
                            "形に「円柱」を選ぶ",
                            $"半径 {part.Radius:0.###} / 高さ {part.Height:0.###} を入れる",
                            $"円周の分割を {RobotBuildRecipe.PrismRadialSegments} にする（六角柱になる）",
                            "上下の蓋を両方とも外す",
                            "ピボットを下端にする",
                            $"名前を「{partName}」にして「生成」を押す",
                        },
                    Note    = "位置も向きもここでは入れません。必ず原点に出します。\n"
                            + "次の「姿勢」で動かすまで、前に作った部品と重なった状態です。\n"
                            + "蓋を外すのは、あとで他の部品とつなぐ穴にするためです。",
                });

                Vector3 w = RobotBuildRecipe.WorldOrigin(partName);
                bool rot = part.BakeRotation != Vector3.zero;

                _stages.Add(new Stage
                {
                    Name    = $"{partName}_姿勢",
                    Run     = () => StagePlaceOne(partName),
                    Purpose = $"「{partName}」を体の正しい位置へ動かします。",
                    HowTo   = rot
                        ? new[]
                        {
                            $"オブジェクトリストで「{partName}」を選ぶ",
                            "「ボーンエディタ」で姿勢の欄を開く",
                            $"回転に ({part.BakeRotation.x:0.#}, {part.BakeRotation.y:0.#}, {part.BakeRotation.z:0.#}) を入れる",
                            $"位置に ({w.x:0.####}, {w.y:0.####}, {w.z:0.####}) を入れる",
                        }
                        : new[]
                        {
                            $"オブジェクトリストで「{partName}」を選ぶ",
                            "「ボーンエディタ」で姿勢の欄を開く",
                            $"位置に ({w.x:0.####}, {w.y:0.####}, {w.z:0.####}) を入れる",
                        },
                    Note    = "回転を先、位置をあとに入れます。逆にすると原点のまわりに振れます。\n"
                            + "まだ親子を組んでいないので、ここで入れる位置は原点からの絶対値です。\n"
                            + "親子を組むと、この値は親から見た相対値に置き換わります。",
                });
            }

            // ── 階層 ──
            _stages.Add(new Stage { Name = "S2 階層", Group = true });
            _stages.Add(new Stage
            {
                Name    = "親子関係",
                Run     = () => StageBuildHierarchy(parts),
                Purpose = "部品どうしの親子を組み、体の階層を作ります。",
                HowTo   = new[]
                {
                    "オブジェクトリストで子にしたい部品を選ぶ",
                    "親にしたい部品の下へドラッグして入れる",
                    "センター → 上半身 → 上半身2 → 首 → 頭 の順に入れ子にする",
                    "腕は上半身2 の下、脚はセンターの下へ入れる",
                },
                Note    = "親子を組むと、子の位置は親から見た相対値に置き換わります。\n"
                        + "見た目の位置は変わりません（親を動かすと子も付いてきます）。",
            });

            // ── ミラー分岐ルート → ブリッジ の順 ──
            //   手作業のフォルダも「_分岐ルートフラグ → _bridge → _skin」の順で切られている。
            //   分岐ルートを立ててからブリッジを張ると、分岐配下に出来たブリッジが
            //   スキンド変換のときに一緒にミラーされる。逆順だと、張ったあとに
            //   分岐を立てることになり、ブリッジが分岐配下と見なされない。
            if (UsesMirror(v))
            {
                _stages.Add(new Stage { Name = "S3 ミラー", Group = true });
                _stages.Add(new Stage
                {
                    Name    = "ミラー分岐ルート", Run     = StageMirrorBranchRoots,
                    Purpose = "左半身だけを作り、右半身を鏡写しで出すための印を付けます。",
                    HowTo   = new[]
                    {
                        "オブジェクトリストで「左腕」と「左足」を選ぶ",
                    "ミラーの設定で「分岐ルート」を有効にする",
                    },
                    Note    = "印を付けた部品から先が、左右の枝として扱われます。\n"
                        + "右側の実体は、あとのスキン変換のときに自動で作られます。\n"
                        + "ブリッジより先に印を付けます。順が逆だと、張った橋が枝の中と\n"
                        + "みなされず右側に写りません。",
                });
            }

            if (UsesBridge(v))
            {
                _stages.Add(new Stage { Name = "S4 ブリッジ", Group = true });
                _stages.Add(new Stage
                {
                    Name    = "仕切り_センター下部", Run     = StagePartitionCenter,
                    Purpose = "センターの下の穴を左右 2 つに割り、左右の脚をつなげるようにします。",
                    HowTo   = new[]
                    {
                        "センターを選び、選択モードを「辺」にする",
                    "下の開口の前側の辺を 1 本、後ろ側の辺を 1 本選ぶ（左右の中央）",
                    "「辺ブリッジ」で面を張る",
                    },
                    Note    = "センターは小判型なので下の開口が横長の輪 1 つです。そのままでは\n"
                        + "脚 2 本をつなげません。中央に面を 1 枚張って輪を 2 つに分けます。\n"
                        + "前後で離れた 2 本の辺を選ぶ必要があります（隣り合う辺だと張れません）。\n"
                        + "横の分割数を奇数にしてあるので、中央にちょうど辺が来ます。",
                });
                _stages.Add(new Stage
                {
                    Name    = "面削除_上半身2",     Run     = StageOpenChestHoles,
                    Purpose = "首と左右の腕をつなぐための穴を、胴の上部に開けます。",
                    HowTo   = new[]
                    {
                        "上半身2 を選び、選択モードを「面」にする",
                    "上面の中央の長方形（12 枚）を選ぶ",
                    "側面の高さ中段にある、左右の丸い部分（各 3 枚）を選ぶ",
                    "「削除」を押す",
                    },
                    Note    = "側面は高さ 3 段に分かれています。中段でなければいけません。\n"
                        + "最下段を消すと下の開口とつながって 1 つの穴になり、\n"
                        + "最上段を消すと上面の穴とつながります。",
                });
                _stages.Add(new Stage
                {
                    Name    = "穴点数合わせ",       Run     = StageMatchHoleCounts,
                    Purpose = "つなぎたい 2 つの穴の頂点数を揃えます。",
                    HowTo   = new[]
                    {
                        "「穴点数合わせ」パネルを開く",
                    "基準にする穴の縁の頂点を選び、「基準穴を取込」を押す",
                    "合わせたい穴の縁の頂点を選び、「対象穴を取込」を押す",
                    "「頂点数を合わせる」を押す",
                    },
                    Note    = "穴つなぎは 2 つの穴の頂点数が同じでないと使えません。\n"
                        + "胴側が 14 点や 8 点、六角柱側が 6 点なので、六角柱側を増やします。\n"
                        + "足りないときは長い辺から順に割り、多いときは短い辺から順に潰します。",
                });
                _stages.Add(new Stage
                {
                    Name    = "穴つなぎ",           Run     = StageBridgeJoints,
                    Purpose = "関節のすきまを面で橋渡しして、部品どうしをつなげます。",
                    HowTo   = new[]
                    {
                        "「基本図形」の「ブリッジ」を開く",
                    "つなぎたい 2 つの穴の縁の頂点をそれぞれ取り込む",
                    "分割数を 3 にする",
                    "「生成」を押す",
                    },
                    Note    = "分割を入れないと関節が面 1 枚でつながり、曲げたときに折れます。\n"
                        + "対応と面の向きは自動で決まります（手で反転させる必要はありません）。",
                });
            }

            if (UsesSkin(v))
            {
                _stages.Add(new Stage { Name = "S5 スキン", Group = true });
                _stages.Add(new Stage
                {
                    Name    = "スキンド変換", Run     = StageConvertToSkinned,
                    Purpose = "メッシュからボーンとスキンを作り、動かせる状態にします。",
                    HowTo   = new[]
                    {
                        "「メッシュからボーンとスキンの生成」を押す",
                    },
                    Note    = "各部品と同じ名前のボーンが作られ、メッシュ側の名前には _skinned が付きます。\n"
                        + "ミラーの印を付けた枝は、ここで右側の実体が作られます。",
                });

                if (UsesBridge(v))
                {
                    // 分岐ルートをまたぐ右側は、変換で右のメッシュが出来てから張る。
                    _stages.Add(new Stage
                {
                    Name    = "右側のブリッジ後付け", Run     = StageBridgePostSkinJoints,
                    Purpose = "鏡写しでは出てこない右側の橋を、あとから足します。",
                    HowTo   = new[]
                    {
                        "スキン変換のあとで「ブリッジ」を開く",
                    "センターの右下の穴と、右足の穴をつなぐ",
                    "上半身2 の右側面の穴と、右腕の穴をつなぐ",
                    },
                    Note    = "鏡写しの対象は「分岐ルートより先」だけです。センターや上半身2 は\n"
                        + "分岐ルートの手前にあるので、そこから右側へ渡る橋は写りません。\n"
                        + "右足・右腕の実体はスキン変換で初めて出来るので、それより前には張れません。",
                });

                    // ウェイトの相手はボーン。変換より後でないと塗れない。
                    _stages.Add(new Stage
                {
                    Name    = "ブリッジのウェイト塗り", Run     = StagePaintBridgeWeights,
                    Purpose = "橋の部分に、両側のボーンのウェイトを配ります。",
                    HowTo   = new[]
                    {
                        "橋のメッシュを選ぶ",
                    "「スキンW数値設定」を開く",
                    "輪ごとに頂点を選び、両側のボーンへ重みを入れる",
                    },
                    Note    = "端は本体側のボーン 1.0、反対の端は相手側 1.0、中央は 0.5 ずつ。\n"
                        + "分割 3 なら 1.00 / 0.75 / 0.50 / 0.25 / 0.00 と配ります。\n"
                        + "全部を 0.5 ずつにすると、本体との境目で食い違って折れます。",
                });
                }
            }

            _stages.Add(new Stage { Name = "S6 アバター", Group = true });
            _stages.Add(new Stage
                {
                    Name    = "Humanoid割当", Run     = StageHumanoidMapping,
                    Purpose = "体のどの部品がどの人体部位にあたるかを割り当てます。",
                    HowTo   = new[]
                    {
                        "「アバター用ヒューマンマッピング」を開く",
                    "自動割当を実行する",
                    },
                    Note    = "部品の名前から自動で決まります。センター＝腰、上半身＝背骨、\n"
                        + "上半身2＝胸、というように対応します。",
                });

            // VRM を出すのがこの検証の目的なので、常に書き出す。
            // プロジェクトフォルダではなくファイル 1 つなので、段としては保存しない。
            _stages.Add(new Stage
            {
                Name    = "VRM書き出し",
                Run     = StageExportVrm,
                Save    = false,
                Purpose = "完成したモデルを VRM 1.0 形式のファイルとして書き出します。",
                HowTo   = new[]
                {
                    "右のパネルで「書き出し」を開く",
                    "形式に VRM を選ぶ",
                    "「不足関節を補完」にチェックを入れる",
                    "保存先を指定して書き出す",
                },
                Note    = "VRM は Humanoid の必須ボーンが 1 つでも欠けていると、\n"
                        + "ビューアが読み込みを拒否します。上半身だけのモデルは脚が無いので\n"
                        + "必ず欠けます。「不足関節を補完」を入れると、足りない関節を\n"
                        + "空のノードで補ってから書き出します。",
            });
        }

        // ================================================================
        // S1 図形生成（部位ごと）
        // ================================================================

        /// <summary>
        /// 系統ごとに空のモデルを作ってアクティブにする。
        /// 以降の生成コマンドはこのモデルへ入る。
        /// </summary>
        private StageResult StageCreateModel()
        {
            // プロジェクトを空にしてモデルを 1 つだけ作り直す。
            //
            // モデルを足すだけだと前の系統のモデルが残り、保存したフォルダに
            // 関係ないモデルが同梱される（CsvProjectSerializer はプロジェクト内の
            // 全モデルをフォルダへ書く）。名前は常に StickRobot でよい。
            SendLogged(new ResetProjectCommand("StickRobot"));

            // モデルが立ったことを確かめてから次へ進む。
            // 立っていないまま生成コマンドを送ると、ディスパッチャが握り潰して
            // 「送れたのに何もできていない」状態になる。
            var model = GetModel?.Invoke();
            if (model == null)
            {
                AddNote("モデルを作れませんでした（コマンドが届いていない可能性）");
                return StageResult.Fail;
            }

            AddNote($"モデル「{model.Name}」を作りました");
            return StageResult.Ok;
        }

        /// <summary>
        /// 部位を 1 つ、原点に置く。
        /// 位置も回転もここでは入れない。次の姿勢の段で動かす。
        /// 手作業で「基本図形を作る」を押した直後と同じ状態にする。
        /// </summary>
        private StageResult StageCreateOne(string name)
        {
            var part = RobotBuildRecipe.Find(name);
            if (string.IsNullOrEmpty(part.Name)) return StageResult.Fail;

            int mi = ModelIndex();

            var placement = PrimitivePlacement.Default;
            placement.WorldPosition          = Vector3.zero;
            placement.PlaceRotation          = Vector3.zero;
            placement.PlaceScale             = Vector3.one;
            placement.BakeRotation           = true;
            placement.BakeScale              = true;
            placement.AddMode                = PrimitiveAddMode.NewObject;
            placement.AddTargetIndex         = -1;
            placement.MaterialIndex          = 0;
            placement.MergeDuplicateVertices = true;

            if (part.Stadium)
            {
                var p = StadiumBoxMeshGenerator.StadiumBoxParams.Default;
                p.MeshName       = part.Name;
                p.Length         = part.Length;
                p.Height         = part.Height;
                p.Depth          = part.Depth;
                p.RoundTopBottom = false;
                p.CapSegments    = RobotBuildRecipe.StadiumCapSegments;
                p.LengthSegments = RobotBuildRecipe.StadiumLengthSegments;
                p.HeightSegments = part.HeightSegments;
                p.CapTop         = part.CapTop;
                p.CapBottom      = false;
                p.FlipFaces      = false;
                p.Pivot          = part.Pivot;

                SendLogged(new CreateStadiumBoxCommand(mi, p, placement));
            }
            else
            {
                var p = CylinderMeshGenerator.CylinderParams.Default;
                p.MeshName       = part.Name;
                p.RadiusTop      = part.Radius;
                p.RadiusBottom   = part.Radius;
                p.Height         = part.Height;
                p.RadialSegments = RobotBuildRecipe.PrismRadialSegments;
                p.HeightSegments = 1;
                p.CapTop         = false;
                p.CapBottom      = false;
                p.EdgeRadius     = 0f;
                p.Pivot          = part.Pivot;

                SendLogged(new CreateCylinderCommand(mi, p, placement));
            }

            RefreshAfterTopologyChange?.Invoke();

            // 送っただけでは成否が分からない。実際に増えたかを見る。
            var model = GetModel?.Invoke();
            if (model == null || FindMeshIndex(model, part.Name) < 0) return StageResult.Fail;

            return StageResult.Ok;
        }

        /// <summary>
        /// 部位を 1 つ、オブジェクト姿勢で動かす。
        ///
        /// 階層を張る前なので、入れる値はワールド絶対位置。
        /// 階層の段で PreserveWorldTransform により親からの相対値へ組み直される。
        /// 回転が要る部位は回転を先に入れる（位置を入れてから回すと原点まわりに振れる）。
        /// </summary>
        private StageResult StagePlaceOne(string name)
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Fail;

            var part = RobotBuildRecipe.Find(name);
            if (string.IsNullOrEmpty(part.Name)) return StageResult.Fail;

            int idx = FindMeshIndex(model, part.Name);
            if (idx < 0) return StageResult.Fail;

            int mi = ModelIndex();
            var target = new[] { idx };

            // Begin / End で挟む。
            //
            // 【挟まないと何が起きるか】
            //   ディスパッチャは編集モードと「原点だけ移動」の状態をインスタンス変数で
            //   持っていて、Begin で仕込み End で解除する
            //   （PlayerCommandDispatcher.cs:50-55）。挟まずに Set だけ送ると、
            //   直前にボーン編集パネルで使ったモードがそのまま残る。
            //   ・OriginOnly が残っていると、原点を動かした分だけ頂点が逆へずれる
            //     （PlayerCommandDispatcher.cs:1116-1140）。見た目は変わらないので
            //     PolyLing 上では気づけないが、VRM へ出すと頂点が絶対座標のまま残る。
            //   ・PoseLayer が残っていると、回転が BonePoseData の "Manual" 層へ入り
            //     BoneTransform.Rotation に書かれない（同 :1091-1095）。
            //
            // ここで欲しいのは「オブジェクトごと動かす」ので、
            // Mode = BoneOnlyRebind / OriginOnly = false（どちらも既定値）を明示して送る。
            var begin = new BeginBoneTransformSliderDragCommand(mi, target)
            {
                Mode       = BoneMoveMode.BoneOnlyRebind,
                OriginOnly = false,
            };
            SendLogged(begin);

            void Set(SetBoneTransformValueCommand.Field f, float value)
                => SendLogged(new SetBoneTransformValueCommand(mi, target, f, value));

            // 回転 → 位置の順。位置を入れてから回すと原点まわりに振れる。
            if (part.BakeRotation != Vector3.zero)
            {
                Set(SetBoneTransformValueCommand.Field.RotationX, part.BakeRotation.x);
                Set(SetBoneTransformValueCommand.Field.RotationY, part.BakeRotation.y);
                Set(SetBoneTransformValueCommand.Field.RotationZ, part.BakeRotation.z);
            }

            Vector3 w = RobotBuildRecipe.WorldOrigin(part.Name);
            Set(SetBoneTransformValueCommand.Field.PositionX, w.x);
            Set(SetBoneTransformValueCommand.Field.PositionY, w.y);
            Set(SetBoneTransformValueCommand.Field.PositionZ, w.z);

            SendLogged(new EndBoneTransformSliderDragCommand(mi, $"{part.Name} 姿勢"));

            // 入った値を確かめる。頂点が逆へずれる不具合は見た目に出ないので、
            // BoneTransform の実値で判定しないと素通りする。
            var mc = model.GetMeshContext(idx);
            if (mc?.BoneTransform == null) return StageResult.Fail;

            if ((mc.BoneTransform.Position - w).sqrMagnitude > 1e-8f)
                return StageResult.Fail;

            if ((mc.BoneTransform.Rotation - part.BakeRotation).sqrMagnitude > 1e-6f)
                return StageResult.Fail;

            return StageResult.Ok;
        }

        // ================================================================
        // 階層
        // ================================================================

        /// <summary>
        /// 親子を張る。並びは定義表の順（親が先）なので、
        /// 各部位について「自分の索引」と「親の索引」を 1 件ずつ送る。
        /// </summary>
        private StageResult StageBuildHierarchy(string[] parts)
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Fail;

            var entries = new List<ReorderMeshesCommand.ReorderEntry>();

            foreach (string name in parts)
            {
                var part = RobotBuildRecipe.Find(name);
                if (string.IsNullOrEmpty(part.Name)) continue;
                if (string.IsNullOrEmpty(part.Parent)) continue;

                int self   = FindMeshIndex(model, part.Name);
                int parent = FindMeshIndex(model, part.Parent);
                if (self < 0 || parent < 0) continue;

                entries.Add(new ReorderMeshesCommand.ReorderEntry
                {
                    MasterIndex          = self,
                    NewParentMasterIndex = parent,
                    NewDepth             = RobotBuildRecipe.Depth(part.Name),
                });
            }

            if (entries.Count == 0) return StageResult.Fail;

            // 姿勢の段ではワールド絶対位置を入れてある。ここで親を張ると
            // PreserveWorldTransform により親からの相対値へ組み直される。
            SendLogged(new ReorderMeshesCommand(
                ModelIndex(), MeshCategory.Mesh, entries.ToArray(),
                preserveWorldTransform: true));

            RefreshAfterTopologyChange?.Invoke();
            return StageResult.Ok;
        }

        // ================================================================
        // 補助
        // ================================================================

        /// <summary>名前で描画オブジェクトの索引を引く。見つからなければ -1。</summary>
        private static int FindMeshIndex(ModelContext model, string name)
        {
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && mc.Name == name) return i;
            }
            return -1;
        }
    }
}

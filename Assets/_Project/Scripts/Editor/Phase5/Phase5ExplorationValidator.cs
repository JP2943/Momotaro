using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Scenes;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Input;
using Momotaro.Infrastructure.Navigation;
using Momotaro.Infrastructure.World;
using Momotaro.Presentation.Cameras;
using Momotaro.Presentation.Combat;
using Momotaro.Presentation.Diagnostics;
using Momotaro.Presentation.Hud;
using UnityEditor;
using UnityEngine;
using Unity.AI.Navigation;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Momotaro.Editor.Phase5
{
    /// <summary>
    /// P5 探索 Scene の静的検査（P5-09。仕様書 v1.1 §13.2 の「Scene」行、§13.3）。
    ///
    /// <b>AssetDatabase を混ぜない。</b> §13.2 が検査の入力で分けることを求めている。
    /// カタログ・Build Settings・Prefab 参照といった Asset 側は
    /// <see cref="Phase5AssetValidator"/> が持つ。混ぜると「Scene を渡せば検査できる」
    /// という性質が失われ、テストから素の Scene を流せなくなる。
    ///
    /// <b>ここが見るのは「テストが自分で配線してしまう」ぶんの穴。</b>
    /// 受入テストは必要な参照を自分で注入するので、<b>配線し忘れた Scene だけがテストの外に残る</b>。
    /// だから単一性・配線・混入禁止・初期状態を Scene 側で見る（P4 の Validator と同じ考え方）。
    ///
    /// すべての指摘を Warning に落とさない（§13.2 末尾）。直さないと試遊が成り立たないものは Error にする。
    /// </summary>
    public static class Phase5ExplorationValidator
    {
        /// <summary>検査対象の Scene（メニューの案内に使う）。</summary>
        public static readonly string[] AreaScenePaths =
        {
            Phase5AreaIds.AreaAScenePath,
            Phase5AreaIds.AreaBScenePath,
        };

        /// <summary>入口・復帰点・出現点の周りで、実体のある Collider を探す半径（m）。</summary>
        private const float ClearanceRadius = 0.45f;

        /// <summary>重なりを見る高さ（m）。Actor の胴のあたり。</summary>
        private const float ClearanceHeight = 0.9f;

        /// <summary>NavMesh 上の点を探す許容半径（m）。床の厚み・縁の丸めぶんを吸収する。</summary>
        private const float NavSampleRadius = 1.5f;

        /// <summary>門の肩を取る距離（m）。くり抜きの外側へ確実に出る。</summary>
        private const float DoorShoulderOffset = 1.2f;

        /// <summary>P5 の Area Scene を検査して追記する（AssetDatabase 非依存・純走査）。</summary>
        public static void Validate(Scene scene, List<string> errors, List<string> warnings)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                errors.Add("Scene が無効／未読込です。");
                return;
            }

            ValidateSingletons(scene, errors);
            ValidateForbidden(scene, errors);
            ValidateWiring(scene, errors);
            ValidateAreaRoot(scene, errors);
            ValidateInvestigation(scene, errors, warnings);
            ValidateCamera(scene, errors);
            ValidateNavigation(scene, errors, warnings);
            ValidateInitialState(scene, errors);
            ValidateEncounter(scene, errors, warnings);
            ValidateClearance(scene, errors);
            ValidateSceneHygiene(scene, errors);
        }

        // ---------------------------------------------------------------- 単一性（§13.3「1 つ」の行）

        private static void ValidateSingletons(Scene scene, List<string> errors)
        {
            RequireOne<AreaRoot>(scene, "エリアの根（AreaRoot）", errors);
            RequireOne<AreaContext>(scene, "エリアの初期化状態（AreaContext）", errors);
            RequireOne<AreaInitializer>(scene, "エリア初期化担当（AreaInitializer）", errors);
            RequireOne<AreaTransitionConditionsSource>(scene, "遷移の受付条件（AreaTransitionConditionsSource）", errors);
            RequireOne<AreaActorTransferPort>(scene, "Actor 値の窓口（AreaActorTransferPort）", errors);
            RequireOne<AreaExitGateDriver>(scene, "出入口の駆動（AreaExitGateDriver）", errors);

            // 「入力の Interact 消費者が 1 つ、進行の書込先が 1 つ、活動 Context が 1 つ」（§13.3）。
            RequireOne<AreaInteractInput>(scene, "Interact の入力仲介（AreaInteractInput）", errors);
            RequireOne<AreaInteractionController>(scene, "Interact の単一窓口（AreaInteractionController）", errors);
            RequireOne<PlayerProgressHolder>(scene, "進行の書込先（PlayerProgressHolder）", errors);
            RequireOne<InvestigationRecordHolder>(scene, "調査記録の保持先（InvestigationRecordHolder）", errors);
            RequireOne<CompanionActivityContext>(scene, "仲間の活動 Context（CompanionActivityContext）", errors);

            RequireOne<AreaCameraRig>(scene, "カメラの Rig（AreaCameraRig）", errors);
            RequireOne<AreaNavigationBinder>(scene, "経路 Adapter の配線役（AreaNavigationBinder）", errors);

            // 死亡再開（§9.1）。A にも B にも要る：死は戦闘の中だけで起きるものではない。
            RequireOne<CampaignRespawnRunner>(scene, "死亡再開の実行役（CampaignRespawnRunner）", errors);
            RequireOne<RespawnSubmitInput>(scene, "再開操作の仲介（RespawnSubmitInput）", errors);
            RequireOne<CampaignRespawnView>(scene, "再開操作の表示（CampaignRespawnView）", errors);
        }

        // ---------------------------------------------------------------- 混入禁止（§13.3 の 4 行目）

        private static void ValidateForbidden(Scene scene, List<string> errors)
        {
            // 自動 Exploration 適用を置くと、到着の許可を出す前に活動が始まる（§5.1 手順 7）。
            ForbidAll<GameplaySceneMode>(scene,
                "自動 Exploration 適用（GameplaySceneMode）は P5 Area に置かない", errors);
            ForbidAll<TrialStageController>(scene,
                "試遊の段取り（TrialStageController）は P5 Area に置かない", errors);

            // P4 の旧仲介と同居すると、同じ押下を 2 回消費する（§7.1）。
            ForbidAll<InvestigationInteractInput>(scene,
                "P4 の調査入力仲介（InvestigationInteractInput）は P5 Area に置かない", errors);

            // 旧試遊 Retry は「Scene をまるごと初期化するやり直し」で、本編型の再開とは別物（§9.1）。
            ForbidAll<CombatRetryInput>(scene,
                "旧試遊の Retry 入力（CombatRetryInput）は P5 Area に置かない", errors);
            ForbidAll<CombatOutcomeController>(scene,
                "旧試遊の勝敗・Retry 制御（CombatOutcomeController）は P5 Area に置かない", errors);
        }

        // ---------------------------------------------------------------- 配線

        private static void ValidateWiring(Scene scene, List<string> errors)
        {
            RequireWired<AreaTransitionConditionsSource>(scene, "遷移の受付条件", errors, x => x.IsWired);
            RequireWired<AreaActorTransferPort>(scene, "Actor 値の窓口", errors, x => x.IsWired);
            RequireWired<AreaInteractionController>(scene, "Interact の単一窓口", errors, x => x.IsWired);
            RequireWired<CompanionActivityContext>(scene, "仲間の活動 Context", errors, x => x.IsWired);
            RequireWired<AreaCameraRig>(scene, "カメラの Rig", errors, x => x.IsWired);
            RequireWired<AreaNavigationBinder>(scene, "経路 Adapter の配線役", errors, x => x.IsWired);
            RequireWired<CampaignRespawnRunner>(scene, "死亡再開の実行役", errors, x => x.IsWired);
            RequireWired<RespawnSubmitInput>(scene, "再開操作の仲介", errors, x => x.IsWired);

            // 出入口は<b>自分の Trigger で範囲を見る</b>ので、主人公の根が配線されていないと一度も反応しない。
            // 実際に試遊で A→B が動かず、原因がこの配線漏れだった（Gate を直接叩くテストでは気付けない）。
            RequireWired<AreaExitGate>(scene, "開放出入口（AreaExitGate）", errors, x => x.IsWired);
            RequireWired<CampaignRespawnView>(scene, "再開操作の表示", errors, x => x.IsWired);

            foreach (AreaInteractInput input in Components<AreaInteractInput>(scene))
            {
                if (input.Controller == null)
                {
                    errors.Add("Interact の入力仲介が窓口へ配線されていません（AreaInteractInput.Bind）。");
                }
            }
        }

        // ---------------------------------------------------------------- 入口・ID（§13.3 の 1〜2 行目）

        private static void ValidateAreaRoot(Scene scene, List<string> errors)
        {
            List<AreaRoot> roots = Components<AreaRoot>(scene);
            if (roots.Count != 1)
            {
                return; // 単一性は RequireOne が報告済み。
            }

            AreaRoot root = roots[0];
            AreaDefinition definition = root.Definition;
            if (definition == null)
            {
                errors.Add("エリアの根に AreaDefinition が割り当てられていません。");
                return;
            }

            if (definition.FloorId != AreaDefinition.SupportedFloorId)
            {
                errors.Add("P5 は Floor 0 のみ対応です（AreaDefinition の FloorId=" + definition.FloorId + "）。");
            }

            if (root.EntryPoints.Count == 0)
            {
                errors.Add("入口（AreaEntryPoint）が 1 つもありません。");
            }

            // Data 側の入口宣言と Scene 側の実体がそろっているか（正本と参照の一致。§13.3 の 2 行目）。
            var seen = new HashSet<string>();
            for (int i = 0; i < root.EntryPoints.Count; i++)
            {
                AreaEntryPoint point = root.EntryPoints[i];
                if (point == null)
                {
                    errors.Add("入口の一覧に空の要素があります。");
                    continue;
                }

                if (!point.EntryId.IsValid)
                {
                    errors.Add("入口の EntryId が不正です（" + point.name + "）。");
                    continue;
                }

                if (!seen.Add(point.EntryId.Value))
                {
                    errors.Add("入口 ID が重複しています: " + point.EntryId.Value);
                }

                if (!ContainsEntry(definition, point.EntryId))
                {
                    errors.Add("Scene の入口 " + point.EntryId.Value + " が AreaDefinition に宣言されていません。");
                }
            }

            for (int i = 0; i < definition.Entries.Count; i++)
            {
                StableId declared = definition.Entries[i].EntryId;
                if (declared.IsValid && !root.TryGetEntryPoint(declared, out _))
                {
                    errors.Add("AreaDefinition の入口 " + declared.Value + " に対応する実体が Scene にありません。");
                }
            }

            if (definition.DefaultEntryId.IsValid && !root.TryGetEntryPoint(definition.DefaultEntryId, out _))
            {
                errors.Add("既定入口 " + definition.DefaultEntryId.Value + " の実体が Scene にありません。");
            }

            // FlagId は門とレバーで<b>共有する</b>（同じ仕掛けの表と裏）。重複定義と混同しない（§13.3 の 2 行目）。
            ValidateFlagIds(scene, root, errors);
        }

        private static void ValidateFlagIds(Scene scene, AreaRoot root, List<string> errors)
        {
            var doorFlags = new HashSet<string>();
            foreach (AreaFlagDoor door in Components<AreaFlagDoor>(scene))
            {
                if (!door.FlagId.IsValid)
                {
                    errors.Add("門の FlagId が不正です（" + door.name + "）。");
                    continue;
                }

                if (!doorFlags.Add(door.FlagId.Value))
                {
                    errors.Add("門の FlagId が重複しています: " + door.FlagId.Value);
                }
            }

            foreach (AreaFlagLever lever in Components<AreaFlagLever>(scene))
            {
                if (lever.Door == null)
                {
                    errors.Add("レバーに門が配線されていません（" + lever.name + "）。");
                    continue;
                }

                // レバーは門と同じ FlagId を<b>参照する</b>。ここは重複ではなく一致を求める。
                if (lever.FlagId.Value != lever.Door.FlagId.Value)
                {
                    errors.Add("レバーと門の FlagId が一致しません: レバー=" + lever.FlagId.Value
                        + " 門=" + lever.Door.FlagId.Value);
                }
            }

            if (root.Levers.Count > 0 && doorFlags.Count == 0)
            {
                errors.Add("レバーがあるのに門がありません。");
            }
        }

        private static bool ContainsEntry(AreaDefinition definition, StableId entryId)
        {
            for (int i = 0; i < definition.Entries.Count; i++)
            {
                if (definition.Entries[i].EntryId.Value == entryId.Value)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- 調査（§13.3 の 6 行目）

        private static void ValidateInvestigation(Scene scene, List<string> errors, List<string> warnings)
        {
            List<CompanionInvestigationPoint> points = Components<CompanionInvestigationPoint>(scene);
            if (points.Count == 0)
            {
                warnings.Add("調査地点がありません（この Area に置かない構成なら想定どおり）。");
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < points.Count; i++)
            {
                CompanionInvestigationPoint point = points[i];
                if (!point.PointId.IsValid)
                {
                    errors.Add("調査地点の PointId が不正です（" + point.name + "）。");
                    continue;
                }

                if (!seen.Add(point.PointId.Value))
                {
                    errors.Add("調査地点の PointId が重複しています: " + point.PointId.Value);
                }

                if (point.SettingsData == null)
                {
                    errors.Add("調査地点 " + point.PointId.Value + " に設定 Data が割り当てられていません。");
                }

                if (!point.DiscoveryId.IsValid)
                {
                    errors.Add("調査地点 " + point.PointId.Value + " の DiscoveryId が不正です。");
                }
            }

            List<InvestigationCoordinator> coordinators = Components<InvestigationCoordinator>(scene);
            if (points.Count > 0 && coordinators.Count != 1)
            {
                errors.Add("調査の調停役（InvestigationCoordinator）は 1 つであるべきですが "
                    + coordinators.Count + " です。");
                return;
            }

            for (int i = 0; i < coordinators.Count; i++)
            {
                InvestigationCoordinator coordinator = coordinators[i];
                if (!coordinator.IsWired)
                {
                    errors.Add("調査の調停役が配線されていません（窓口・加入・記録・仲間の駆動）。");
                }

                // P5 は<b>指定地点モード</b>（§7.1）。引数なしの入口を許すと、狙っていない地点が選ばれる。
                if (!coordinator.ExplicitTargetOnly)
                {
                    errors.Add("P5 の調査は指定地点モードである必要があります（ExplicitTargetOnly が false）。");
                }
            }
        }

        // ---------------------------------------------------------------- カメラ（§11）

        private static void ValidateCamera(Scene scene, List<string> errors)
        {
            List<AreaCameraRegion> regions = Components<AreaCameraRegion>(scene);
            if (regions.Count == 0)
            {
                errors.Add("カメラ領域（AreaCameraRegion）がありません。");
                return;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < regions.Count; i++)
            {
                AreaCameraRegion region = regions[i];
                if (!region.Definition.RegionId.IsValid)
                {
                    errors.Add("カメラ領域の RegionId が不正です（" + region.name + "）。");
                    continue;
                }

                if (!seen.Add(region.Definition.RegionId.Value))
                {
                    errors.Add("カメラ領域の RegionId が重複しています: " + region.Definition.RegionId.Value);
                }
            }
        }

        // ---------------------------------------------------------------- 経路（§13.3 の 7 行目）

        private static void ValidateNavigation(Scene scene, List<string> errors, List<string> warnings)
        {
            // §10.1 が名指しで禁じている：長距離 Follow の経路に NavMeshAgent を置かない。
            int agents = Count<NavMeshAgent>(scene);
            if (agents != 0)
            {
                errors.Add("P5 の Scene に NavMeshAgent が " + agents + " 個あります（§10.1 で禁止）。");
            }

            // <b>焼いた NavMesh がそこにあるか。</b> 以前は Agent 禁止と配線しか見ておらず、
            // 「焼き忘れた Scene」がそのまま合格していた（GPT レビュー R6 の指摘 3）。
            // NavMeshSurface が居て data を持っているかは Scene だけで分かる。
            List<NavMeshSurface> surfaces = Components<NavMeshSurface>(scene);
            if (surfaces.Count == 0)
            {
                errors.Add("NavMeshSurface がありません（経路を焼く先が無い。§10.1）。");
                return;
            }

            int baked = 0;
            foreach (NavMeshSurface surface in surfaces)
            {
                if (surface == null)
                {
                    continue;
                }

                if (surface.navMeshData == null)
                {
                    errors.Add("NavMeshSurface に焼いた NavMesh がありません（" + surface.name
                        + "）。Momotaro / Phase 5 / Generate Exploration Trial で焼き直してください。");
                }
                else
                {
                    baked++;
                }
            }

            if (baked == 0)
            {
                return;
            }

            ValidateNavigationRoutes(scene, errors, warnings);
        }

        /// <summary>
        /// 所定経路が<b>実際に到達できる</b>かを見る（§13.3 の 7 行目。GPT レビュー R6 の指摘 3）。
        ///
        /// <b>静的検査と PlayMode の担当を分ける。</b> ここが見るのは「焼いた NavMesh の上で、
        /// 既定入口から出入口・扉・出現点・戦闘 Trigger まで経路が繋がっているか」という<b>地形の性質</b>。
        /// 実際に犬丸が追従して曲がり角で詰まらないか、ワープが頻発しないかという<b>挙動</b>は
        /// PlayMode（P5-06 の受入）の担当で、ここでは見ない。
        ///
        /// Editor で NavMesh の実体が有効化されていないことがあるので、
        /// 三角形が 1 枚も取れないときは<b>Warning に落として検査をやめる</b>。
        /// そこで Error にすると、焼けているのに検査できないだけで不合格になる。
        /// </summary>
        private static void ValidateNavigationRoutes(Scene scene, List<string> errors, List<string> warnings)
        {
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            if (triangulation.indices == null || triangulation.indices.Length == 0)
            {
                warnings.Add("NavMesh が Editor 上で有効化されていないため、経路の到達可否は検査できませんでした"
                    + "（焼いた data は在ります）。PlayMode の受入で確認してください。");
                return;
            }

            List<AreaRoot> roots = Components<AreaRoot>(scene);
            if (roots.Count != 1 || roots[0].Definition == null)
            {
                return;
            }

            AreaRoot root = roots[0];
            if (!root.TryGetEntryPoint(root.Definition.DefaultEntryId, out AreaEntryPoint start) || start == null)
            {
                return;
            }

            Vector3 from = start.ArrivalPosition;

            // 閉じた門は<b>地形の欠陥ではなく進行の鍵</b>。門はベイクから外して
            // NavMeshObstacle のくり抜きで塞いであるので、検査時には経路が切れて見える。
            // 「門の手前まで行けて、門の向こうから目的地まで行ける」なら、開通後に通れる道として扱う。
            List<AreaFlagDoor> closedDoors = new List<AreaFlagDoor>();
            foreach (AreaFlagDoor door in root.Doors)
            {
                if (door != null && !door.IsOpened)
                {
                    closedDoors.Add(door);
                }
            }

            foreach (AreaEntryPoint point in root.EntryPoints)
            {
                if (point != null)
                {
                    RequireReachable(from, point.ArrivalPosition, "入口 " + point.EntryId.Value, closedDoors, errors, warnings);
                }
            }

            foreach (AreaExitGate gate in root.ExitGates)
            {
                if (gate != null)
                {
                    RequireReachable(from, gate.transform.position, "開放出入口 " + gate.name, closedDoors, errors, warnings);
                }
            }

            foreach (AreaTransitionDoor door in Components<AreaTransitionDoor>(scene))
            {
                if (door != null)
                {
                    RequireReachable(from, door.InteractionAnchor, "扉 " + door.name, closedDoors, errors, warnings);
                }
            }

            foreach (AreaEncounterSpawner spawner in Components<AreaEncounterSpawner>(scene))
            {
                IReadOnlyList<Transform> points = spawner.SpawnPoints;
                for (int i = 0; points != null && i < points.Count; i++)
                {
                    if (points[i] != null)
                    {
                        RequireReachable(from, points[i].position, "出現点 " + i, closedDoors, errors, warnings);
                    }
                }
            }

            foreach (AreaEncounterTrigger trigger in Components<AreaEncounterTrigger>(scene))
            {
                if (trigger != null)
                {
                    RequireReachable(from, trigger.transform.position, "戦闘開始 Trigger", closedDoors, errors, warnings);
                }
            }
        }

        /// <summary>
        /// 既定入口からそこまで NavMesh 上で繋がっているか。
        ///
        /// <b>閉じた門の先は不合格にしない。</b> 門はベイクから外し、閉じている間だけ
        /// <c>NavMeshObstacle</c> でくり抜いてある（§10.2）。検査時は閉じているのが正しい状態なので、
        /// 素直に経路を引くと「門の向こう側は全部到達不能」になる。
        /// 門の両肩を経由して繋がるなら、開通後に通れる道として Warning で残す。
        /// </summary>
        private static void RequireReachable(
            Vector3 from, Vector3 to, string label,
            List<AreaFlagDoor> closedDoors, List<string> errors, List<string> warnings)
        {
            if (!NavMesh.SamplePosition(from, out NavMeshHit fromHit, NavSampleRadius, NavMesh.AllAreas))
            {
                errors.Add("既定入口 " + from + " が NavMesh の上にありません（焼き直しが要ります）。");
                return;
            }

            if (!NavMesh.SamplePosition(to, out NavMeshHit toHit, NavSampleRadius, NavMesh.AllAreas))
            {
                errors.Add(label + " の位置 " + to + " が NavMesh の上にありません（歩いて行けない場所です）。");
                return;
            }

            if (IsPathComplete(fromHit.position, toHit.position))
            {
                return;
            }

            for (int i = 0; i < closedDoors.Count; i++)
            {
                if (!TryGetDoorShoulders(closedDoors[i], out Vector3 near, out Vector3 far))
                {
                    continue;
                }

                bool viaDoor =
                    (IsPathComplete(fromHit.position, near) && IsPathComplete(far, toHit.position))
                    || (IsPathComplete(fromHit.position, far) && IsPathComplete(near, toHit.position));

                if (viaDoor)
                {
                    warnings.Add(label + " は閉じた門（" + closedDoors[i].name
                        + "）の先にあります。門の両側で経路は繋がっているので、開通すれば通れます。");
                    return;
                }
            }

            errors.Add(label + " まで既定入口から経路が繋がっていません（status は不完全で、"
                + "閉じた門の先でもありません）。地形か焼き直しを見直してください。");
        }

        private static bool IsPathComplete(Vector3 from, Vector3 to)
        {
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete;
        }

        /// <summary>
        /// 門の両肩（薄い軸の手前と向こう）を NavMesh 上の点として取る。
        /// くり抜きの真上は NavMesh が無いので、門そのものの位置では判定できない。
        /// </summary>
        private static bool TryGetDoorShoulders(AreaFlagDoor door, out Vector3 near, out Vector3 far)
        {
            near = default;
            far = default;
            if (door == null)
            {
                return false;
            }

            var box = door.GetComponent<BoxCollider>();
            if (box == null)
            {
                return false;
            }

            Bounds b = box.bounds;
            // 薄い方の軸が「通り抜ける向き」。
            bool thinIsZ = b.size.z <= b.size.x;
            float half = (thinIsZ ? b.size.z : b.size.x) * 0.5f;
            Vector3 axis = thinIsZ ? Vector3.forward : Vector3.right;
            Vector3 center = new Vector3(b.center.x, door.transform.position.y, b.center.z);
            float offset = half + DoorShoulderOffset;

            return NavMesh.SamplePosition(center - axis * offset, out NavMeshHit a, NavSampleRadius, NavMesh.AllAreas)
                && NavMesh.SamplePosition(center + axis * offset, out NavMeshHit c, NavSampleRadius, NavMesh.AllAreas)
                && Assign(a.position, c.position, out near, out far);
        }

        private static bool Assign(Vector3 a, Vector3 b, out Vector3 near, out Vector3 far)
        {
            near = a;
            far = b;
            return true;
        }

        // ---------------------------------------------------------------- 初期状態

        private static void ValidateInitialState(Scene scene, List<string> errors)
        {
            int enemies = Count<EnemyActor>(scene);
            if (enemies != 0)
            {
                errors.Add("初期状態の敵は 0 体であるべきですが " + enemies + " 体あります（§13.2）。");
            }

            foreach (AreaArenaBoundary arena in Components<AreaArenaBoundary>(scene))
            {
                if (arena.IsEnabled || arena.ActiveBlockerCount != 0)
                {
                    errors.Add("アリーナ境界が初期状態で有効になっています（探索中に通れない壁を残さない。§8.2）。");
                }
            }

            foreach (AreaFlagDoor door in Components<AreaFlagDoor>(scene))
            {
                if (door.IsOpened)
                {
                    errors.Add("門 " + door.FlagId.Value + " が初期状態で開通しています（記録から復元する。§4.3）。");
                }
            }
        }

        // ---------------------------------------------------------------- 遭遇戦（§8）

        private static void ValidateEncounter(Scene scene, List<string> errors, List<string> warnings)
        {
            List<AreaEncounterRunner> runners = Components<AreaEncounterRunner>(scene);
            if (runners.Count == 0)
            {
                warnings.Add("遭遇戦がありません（この Area に置かない構成なら想定どおり）。");
                return;
            }

            if (runners.Count != 1)
            {
                errors.Add("遭遇戦の調停（AreaEncounterRunner）は 1 つであるべきですが " + runners.Count + " です。");
                return;
            }

            RequireWired<AreaEncounterRunner>(scene, "遭遇戦の調停", errors, x => x.IsWired);
            RequireOne<AreaEncounterSpawner>(scene, "敵の生成役（AreaEncounterSpawner）", errors);
            RequireWired<AreaEncounterSpawner>(scene, "敵の生成役", errors, x => x.IsWired);
            RequireOne<AreaEncounterTrigger>(scene, "戦闘開始 Trigger（AreaEncounterTrigger）", errors);
            RequireWired<AreaEncounterTrigger>(scene, "戦闘開始 Trigger", errors, x => x.IsWired);
            RequireOne<AreaArenaBoundary>(scene, "アリーナ境界（AreaArenaBoundary）", errors);
            RequireWired<AreaArenaBoundary>(scene, "アリーナ境界", errors, x => x.IsWired);
            RequireOne<AreaEncounterConditionsSource>(scene, "戦闘の受付条件", errors);
            RequireWired<AreaEncounterConditionsSource>(scene, "戦闘の受付条件", errors, x => x.IsWired);
            RequireOne<EncounterInterruptRelay>(scene, "撤収の通知役（EncounterInterruptRelay）", errors);
            RequireWired<EncounterInterruptRelay>(scene, "撤収の通知役", errors, x => x.IsWired);

            // 手応え（§8.2 手順 7）。配信役だけ置いて購読し直しを忘れると、湧いた直後の命中を取りこぼす。
            RequireOne<CombatFeedbackDispatcher>(scene, "命中 Feedback の配信役", errors);
            RequireOne<CombatFeedbackPresenter>(scene, "手応え演出の調停役", errors);
            RequireOne<EncounterFeedbackBinder>(scene, "生成直後の購読し直し（EncounterFeedbackBinder）", errors);
            RequireWired<EncounterFeedbackBinder>(scene, "生成直後の購読し直し", errors, x => x.IsWired);
            RequireOne<AreaEncounterResultView>(scene, "結果の短文の表示", errors);
            RequireWired<AreaEncounterResultView>(scene, "結果の短文の表示", errors, x => x.IsWired);

            // 敵 Prefab 表（§13.3 の 8 行目）。表そのものが Scene 側にあるので、ここで解決を確かめる。
            foreach (AreaEncounterSpawner spawner in Components<AreaEncounterSpawner>(scene))
            {
                if (spawner.Table == null)
                {
                    errors.Add("敵 Prefab 表が割り当てられていません（AreaEncounterSpawner）。");
                    continue;
                }

                var issues = new List<string>();
                spawner.Table.Validate(issues);
                for (int i = 0; i < issues.Count; i++)
                {
                    errors.Add("敵 Prefab 表: " + issues[i]);
                }
            }

            foreach (CombatFeedbackPresenter presenter in Components<CombatFeedbackPresenter>(scene))
            {
                if (presenter.CameraShake == null)
                {
                    errors.Add("手応えの揺れ（CameraShakePresenter）が配線されていません（§11）。");
                }
            }
        }

        // ---------------------------------------------------------------- 重なり（§13.3 の 9 行目）

        private static void ValidateClearance(Scene scene, List<string> errors)
        {
            List<AreaRoot> roots = Components<AreaRoot>(scene);
            if (roots.Count != 1)
            {
                return;
            }

            foreach (AreaEntryPoint point in roots[0].EntryPoints)
            {
                if (point != null)
                {
                    RequireClear(scene, point.transform.position, "入口 " + point.EntryId.Value, errors);
                    RequireOutsideEncounterTriggers(
                        scene, point.ArrivalPosition, "入口 " + point.EntryId.Value, errors);
                }
            }

            // 戦闘復帰点も同じ扱い（§8.4 手順 7「入口と重ならない安全な復帰点」）。
            foreach (AreaArenaBoundary arena in Components<AreaArenaBoundary>(scene))
            {
                ValidateArenaInterior(scene, arena, errors);
            }

            foreach (AreaEncounterSpawner spawner in Components<AreaEncounterSpawner>(scene))
            {
                IReadOnlyList<Transform> spawnPoints = spawner.SpawnPoints;
                for (int i = 0; spawnPoints != null && i < spawnPoints.Count; i++)
                {
                    if (spawnPoints[i] != null)
                    {
                        RequireClear(scene, spawnPoints[i].position, "出現点 " + i, errors);
                    }
                }
            }
        }

        /// <summary>
        /// その場所に<b>実体のある</b>Collider が重なっていないことを求める（§13.3 の 9 行目）。
        ///
        /// 見るのは<b>胴の高さ</b>（<see cref="ClearanceHeight"/>）で、足元の床は数えない。
        /// 床は立つための実体なので、そこを重なりに数えるとすべての入口と出現点が不合格になる
        /// （最初にそう書いて実際に全件落ちた）。壁・柱・仕切りは同じ高さに来るので拾える。
        ///
        /// Trigger も数えない。この企画では「実体の壁は solid、Trigger は受付」と決めてあり
        /// （P5-07 の warp probe と同じ線引き）、入口 Trigger の上に立って到着するのは<b>正常</b>。
        /// </summary>
        private static void RequireClear(Scene scene, Vector3 position, string label, List<string> errors)
        {
            Vector3 body = position + Vector3.up * ClearanceHeight;

            foreach (Collider collider in Components<Collider>(scene))
            {
                if (collider == null || collider.isTrigger || !collider.enabled
                    || !collider.gameObject.activeInHierarchy || IsActor(collider))
                {
                    continue;
                }

                if (Vector3.Distance(collider.ClosestPoint(body), body) < ClearanceRadius)
                {
                    errors.Add(label + " の位置 " + position + " に実体の Collider が重なっています（"
                        + collider.name + "）。到着が即再遷移・即 Encounter にならないよう離してください。");
                    return; // 1 か所につき 1 件で十分。
                }
            }
        }

        /// <summary>
        /// 到着位置が<b>戦闘開始 Trigger</b>の中に入っていないことを求める（§8.2 手順 1。GPT レビュー R6 の指摘 3）。
        ///
        /// <see cref="RequireClear"/> は Trigger を一律で無視する。出入口の Trigger の上に立って
        /// 到着するのは正常だからで、そこは変えない。<b>戦闘開始 Trigger は別</b>で、
        /// 到着位置がその中にあると、着いた瞬間に遭遇戦が始まり、
        /// 「到着 → 即戦闘 → 復帰 → 即戦闘」が閉じない。Trigger 一般の扱いと分けて見る。
        /// </summary>
        private static void RequireOutsideEncounterTriggers(
            Scene scene, Vector3 position, string label, List<string> errors)
        {
            Vector3 body = position + Vector3.up * ClearanceHeight;

            foreach (AreaEncounterTrigger trigger in Components<AreaEncounterTrigger>(scene))
            {
                if (trigger == null || !trigger.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var collider = trigger.GetComponent<Collider>();
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                if (Vector3.Distance(collider.ClosestPoint(body), body) < ClearanceRadius)
                {
                    errors.Add(label + " の位置 " + position
                        + " が戦闘開始 Trigger の中（または縁 " + ClearanceRadius.ToString("0.##")
                        + "m 以内）にあります。到着した瞬間に遭遇戦が始まります（§8.2 手順 1）。");
                    return;
                }
            }
        }

        /// <summary>
        /// アリーナ境界を閉じたときに<b>内側で成立するか</b>を見る（§8.2 末尾／§8.4 手順 7。
        /// GPT レビュー R6 の指摘 3）。
        ///
        /// 封鎖 Collider は探索中 disable なので <see cref="RequireClear"/> では拾えず、
        /// 「閉じた瞬間に壁へめり込む配置」がそのまま合格していた。
        /// Collider を触らずに済むよう、<b>幾何だけ</b>で見る：出現点・戦闘開始 Trigger・復帰点が
        /// 余白ぶん狭めた内側（<see cref="AreaArenaBoundary.SafeBounds"/>）に収まっていること。
        /// </summary>
        private static void ValidateArenaInterior(Scene scene, AreaArenaBoundary arena, List<string> errors)
        {
            if (arena == null)
            {
                return;
            }

            Bounds safe = arena.SafeBounds;
            if (safe.size.x <= 0f || safe.size.z <= 0f)
            {
                errors.Add("アリーナが余白より狭く、閉じても内側がありません（" + arena.name + "）。");
                return;
            }

            foreach (AreaEncounterSpawner spawner in Components<AreaEncounterSpawner>(scene))
            {
                IReadOnlyList<Transform> points = spawner.SpawnPoints;
                for (int i = 0; points != null && i < points.Count; i++)
                {
                    if (points[i] != null)
                    {
                        RequireInsideArena(safe, points[i].position, "出現点 " + i, errors);
                    }
                }
            }

            foreach (AreaEncounterTrigger trigger in Components<AreaEncounterTrigger>(scene))
            {
                if (trigger != null)
                {
                    RequireInsideArena(safe, trigger.transform.position, "戦闘開始 Trigger", errors);
                }
            }
        }

        /// <summary>閉じたアリーナの内側（余白ぶん狭めた矩形）に収まっているか。</summary>
        private static void RequireInsideArena(Bounds safe, Vector3 position, string label, List<string> errors)
        {
            Vector3 min = safe.min;
            Vector3 max = safe.max;
            bool inside = position.x >= min.x && position.x <= max.x
                && position.z >= min.z && position.z <= max.z;

            if (!inside)
            {
                errors.Add(label + " の位置 " + position
                    + " がアリーナ境界の内側（" + safe.center + " ± " + (safe.size * 0.5f)
                    + "）にありません。閉じた瞬間に壁の外・壁の中になります（§8.2 末尾）。");
            }
        }

        /// <summary>
        /// 主人公・犬丸の当たりか。<b>到着位置に本人が立っているのは正常</b>なので地形の重なりに数えない
        /// （出荷 Scene は既定入口に置いた状態で保存してある）。
        /// </summary>
        private static bool IsActor(Collider collider)
        {
            return collider.GetComponentInParent<PlayerRoot>() != null
                || collider.GetComponentInParent<CompanionActor>() != null;
        }

        // ---------------------------------------------------------------- 衛生

        private static void ValidateSceneHygiene(Scene scene, List<string> errors)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                // <b>子まで見る。</b> GetMonoBehavioursWithMissingScriptCount はその GameObject 1 つしか
                // 数えないので、根だけを渡していた以前の検査は<b>子に付いた Missing を素通り</b>させていた
                // （GPT レビュー R6 の指摘 3）。P5 の Scene は中身がほぼ全部子なので、実質何も見ていなかった。
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (missing > 0)
                    {
                        errors.Add("Missing Script が " + missing + " 個あります（"
                            + GetHierarchyPath(t) + "）。");
                    }

                    Vector3 s = t.localScale;
                    if (s.x < 0f || s.y < 0f || s.z < 0f)
                    {
                        errors.Add("負のスケールがあります（" + GetHierarchyPath(t) + "）。");
                    }
                }
            }
        }

        /// <summary>指摘を直せるように、根からのパスで示す（名前だけだと同名が多くて探せない）。</summary>
        private static string GetHierarchyPath(Transform t)
        {
            string path = t.name;
            Transform parent = t.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        // ---------------------------------------------------------------- 走査ヘルパ（AssetDatabase 非依存）

        /// <summary>Scene の全 Component を拾う（検査とテストが同じ走査を使うために公開する）。</summary>
        public static List<T> Components<T>(Scene scene) where T : Component
        {
            var list = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                list.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return list;
        }

        /// <summary>Scene の Component 数（検査とテストが同じ走査を使うために公開する）。</summary>
        public static int Count<T>(Scene scene) where T : Component => Components<T>(scene).Count;

        internal static void RequireOne<T>(Scene scene, string label, List<string> errors) where T : Component
        {
            int n = Count<T>(scene);
            if (n != 1)
            {
                errors.Add(label + " は 1 つであるべきですが " + n + " です" + (n > 1 ? "（重複）" : "（欠落）") + "。");
            }
        }

        internal static void ForbidAll<T>(Scene scene, string label, List<string> errors) where T : Component
        {
            int n = Count<T>(scene);
            if (n != 0)
            {
                errors.Add(label + "（" + n + " 個あります）。");
            }
        }

        private static void RequireWired<T>(
            Scene scene, string label, List<string> errors, System.Func<T, bool> isWired) where T : Component
        {
            foreach (T component in Components<T>(scene))
            {
                if (!isWired(component))
                {
                    errors.Add(label + " が配線されていません（" + component.name + "）。");
                }
            }
        }

        [MenuItem("Momotaro/Phase 5/Validate Exploration Trial")]
        private static void ValidateActiveSceneMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            var errors = new List<string>();
            var warnings = new List<string>();

            bool isAreaScene = scene.path == Phase5AreaIds.AreaAScenePath
                || scene.path == Phase5AreaIds.AreaBScenePath;
            if (!isAreaScene)
            {
                warnings.Add("現在の Scene は P5 の Area Scene ではありません（"
                    + (string.IsNullOrEmpty(scene.path) ? "無題" : scene.path)
                    + "）。" + Phase5AreaIds.AreaAScenePath + " または "
                    + Phase5AreaIds.AreaBScenePath + " を開いて実行してください。");
            }

            Validate(scene, errors, warnings);

            string summary = "[Phase5] 探索 Scene Validator: "
                + (errors.Count == 0 ? "OK" : errors.Count + " 件のエラー")
                + (warnings.Count > 0 ? "（警告 " + warnings.Count + " 件）" : string.Empty);

            if (errors.Count > 0)
            {
                Debug.LogError(summary + "\n- " + string.Join("\n- ", errors)
                    + (warnings.Count > 0 ? "\n[警告]\n- " + string.Join("\n- ", warnings) : string.Empty));
            }
            else if (warnings.Count > 0)
            {
                Debug.LogWarning(summary + "\n[警告]\n- " + string.Join("\n- ", warnings));
            }
            else
            {
                Debug.Log(summary);
            }

            EditorUtility.DisplayDialog("Phase 5 探索 Scene Validator",
                summary + (errors.Count > 0 ? "\n\n" + errors[0] : string.Empty), "OK");
        }
    }
}

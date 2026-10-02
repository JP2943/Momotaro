using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Events;
using Momotaro.Data.World;
using Momotaro.Editor.Phase5;
using Momotaro.Editor.Phase55;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Session;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Editor.Phase6
{
    /// <summary>
    /// P6A の検証 campaign を検査する（P6 仕様 §11。工程 P6A-06）。
    ///
    /// 2 本立て：
    /// <list type="number">
    /// <item><b>配置</b>：A–B・B–C の 2 つの隣接を P5.5 の配置検査（<see cref="Phase55WorldValidator"/>）へそのまま通す。
    /// 境界・通路・並び・カメラ軸・覆い・接続 Data を P5.5 と同じ基準で見る（P5 の Scene 検査も内側で走る）。</item>
    /// <item><b>campaign</b>：カタログが実行時と同じ手順で組めること、お地蔵様の入口と ShrinePoint が実在して戦闘区域の外にあること、
    /// Area Data の保存対象一覧（Manifest）が<b>Scene に実在する内容と一致</b>すること、保存 ID が campaign 内で一意であること。</item>
    /// </list>
    ///
    /// 期待値は Builder の定数ではなく、生成された Scene と Data から採る（P5.5 の方針と同じ）。
    /// </summary>
    public static class Phase6WorldValidator
    {
        [MenuItem("Momotaro/Phase 6A/Validate Progression World")]
        private static void ValidateInteractive()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            Validate(errors, warnings);
            EditorUtility.DisplayDialog("P6A 検証ワールドの検査",
                errors.Count == 0
                    ? "検査を通りました（警告 " + warnings.Count + " 件）。"
                    : "エラー " + errors.Count + " 件。\n" + string.Join("\n", errors), "OK");
        }

        /// <summary>A–B・B–C の配置（P5.5 の検査へ渡す形）。</summary>
        public static IReadOnlyList<Phase55Arrangement> Arrangements() => new List<Phase55Arrangement>
        {
            new Phase55Arrangement
            {
                Label = "P6A A–B",
                ASide = Phase5SeamSide.East,
                AreaAScenePath = Phase6WorldIds.AreaAScenePath,
                AreaBScenePath = Phase6WorldIds.AreaBScenePath,
                CatalogDataPath = Phase6WorldIds.CatalogDataPath,
                ConnectionDataPath = Phase6WorldIds.ConnectionDataPath,
                AreaAId = Phase6WorldIds.AreaA,
                AreaBId = Phase6WorldIds.AreaB,
                ConnectionsId = Phase6WorldIds.Connections,
                ConnectionsDisplayName = "P6A エリア接続（東西 A–B–C）",
                ExitFromA = Phase6WorldIds.ExitAEast,
                ExitFromB = Phase6WorldIds.ExitBWest,
                EntryInA = Phase5AreaIds.AreaAFromB,
                EntryInB = Phase5AreaIds.AreaBFromA,
                ConnectionAToB = Phase6WorldIds.ConnectionAToB,
                ConnectionBToA = Phase6WorldIds.ConnectionBToA,
                ForwardDirection = AreaConnectionDirection.East,
                BackwardDirection = AreaConnectionDirection.West,
                PassageCenter = Phase6WorldLayout.SeamZ,
                PassageWidth = Phase6WorldLayout.PassageWidth,
                Targets = Phase6WorldBuilder.Targets,
                SharedConnectionList = true,
            },
            new Phase55Arrangement
            {
                Label = "P6A B–C",
                ASide = Phase5SeamSide.East,
                AreaAScenePath = Phase6WorldIds.AreaBScenePath,
                AreaBScenePath = Phase6WorldIds.AreaCScenePath,
                CatalogDataPath = Phase6WorldIds.CatalogDataPath,
                ConnectionDataPath = Phase6WorldIds.ConnectionDataPath,
                AreaAId = Phase6WorldIds.AreaB,
                AreaBId = Phase6WorldIds.AreaC,
                ConnectionsId = Phase6WorldIds.Connections,
                ConnectionsDisplayName = "P6A エリア接続（東西 A–B–C）",
                ExitFromA = Phase6WorldIds.ExitBEast,
                ExitFromB = Phase6WorldIds.ExitCWest,
                EntryInA = Phase6WorldIds.EntryBFromC,
                EntryInB = Phase6WorldIds.EntryCFromB,
                ConnectionAToB = Phase6WorldIds.ConnectionBToC,
                ConnectionBToA = Phase6WorldIds.ConnectionCToB,
                ForwardDirection = AreaConnectionDirection.East,
                BackwardDirection = AreaConnectionDirection.West,
                PassageCenter = Phase6WorldLayout.SeamZ,
                PassageWidth = Phase6WorldLayout.PassageWidth,
                Targets = Phase6WorldBuilder.Targets,
                SharedConnectionList = true,
            },
        };

        /// <summary>全部見る。</summary>
        public static void Validate(List<string> errors, List<string> warnings)
        {
            foreach (Phase55Arrangement arrangement in Arrangements())
            {
                int before = errors.Count;
                Phase55WorldValidator.Validate(arrangement, errors, warnings);
                for (int i = before; i < errors.Count; i++)
                {
                    errors[i] = "【" + arrangement.Label + "】" + errors[i];
                }
            }

            int campaignStart = errors.Count;
            ValidateCampaign(errors, warnings);
            for (int i = campaignStart; i < errors.Count; i++)
            {
                errors[i] = "【campaign】" + errors[i];
            }
        }

        /// <summary>Scene 1 枚から採った campaign の事実（値だけ。Scene を閉じても使える）。</summary>
        public sealed class AreaContentFacts
        {
            public string ScenePath;
            public readonly HashSet<string> Encounters = new HashSet<string>();
            public readonly HashSet<string> Bosses = new HashSet<string>();
            public readonly HashSet<string> FieldPlacements = new HashSet<string>();
            public readonly HashSet<string> Pickups = new HashSet<string>();
            public readonly HashSet<string> Flags = new HashSet<string>();
            public readonly HashSet<string> InvestigationPoints = new HashSet<string>();
            public readonly Dictionary<string, Vector3> Entries = new Dictionary<string, Vector3>();
            public readonly Dictionary<string, Vector3> Shrines = new Dictionary<string, Vector3>();
            public readonly List<Bounds> Arenas = new List<Bounds>();
            public readonly List<string> Duplicates = new List<string>();
        }

        private static void ValidateCampaign(List<string> errors, List<string> warnings)
        {
            var data = AssetDatabase.LoadAssetAtPath<AreaCatalogData>(Phase6WorldIds.CatalogDataPath);
            if (data == null)
            {
                errors.Add("カタログがありません: " + Phase6WorldIds.CatalogDataPath + "（build-phase6-world を先に実行する）。");
                return;
            }

            // 実行時と同じ手順で組めること（campaign の整合は TryBuild の中で見る）。
            if (!AreaCatalog.TryBuild(data, out AreaCatalog catalog, out IReadOnlyList<string> buildErrors))
            {
                foreach (string e in buildErrors)
                {
                    errors.Add("カタログを組めません: " + e);
                }

                return;
            }

            if (catalog.Campaign == null)
            {
                errors.Add("カタログが campaign として組まれていません（P6 の進行を持たない）。");
                return;
            }

            if (catalog.EncounterPolicy != EncounterClearPolicy.Permanent)
            {
                errors.Add("P6A campaign の遭遇戦クリア方針が Permanent ではありません（実際=" + catalog.EncounterPolicy + "）。");
            }

            var areaFacts = new Dictionary<string, AreaContentFacts>();
            foreach (AreaDefinition area in data.Areas)
            {
                if (area == null)
                {
                    continue;
                }

                if (!catalog.TryGetScenePath(area.Id, out string scenePath))
                {
                    errors.Add("エリア " + area.Id.Value + " の Scene をカタログから引けません。");
                    continue;
                }

                if (AssetDatabase.LoadAssetAtPath<Object>(scenePath) == null)
                {
                    errors.Add("Scene がありません: " + scenePath);
                    continue;
                }

                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                AreaContentFacts facts = Collect(scene);
                areaFacts[area.Id.Value] = facts;
                foreach (string dup in facts.Duplicates)
                {
                    errors.Add(scenePath + ": " + dup);
                }

                CompareManifest(area, facts, errors);
                ValidateCombatVfx(scene, errors);
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ValidateShrines(catalog.Campaign, areaFacts, errors);
            ValidateTitle(errors);
            ValidateUniqueness(data, areaFacts, errors);

            if (!catalog.Campaign.TryGetShrine(catalog.Campaign.InitialShrineId, out _))
            {
                errors.Add("初期お地蔵様 '" + catalog.Campaign.InitialShrineId.Value + "' がお地蔵様一覧にありません。");
            }

            if (catalog.Campaign.GrowthNodes.Count == 0)
            {
                warnings.Add("成長ノードが 0 件です（P6A の休息・成長の検証ができない）。");
            }
        }

        /// <summary>タイトルの起動役がこの campaign のカタログを指していること（実ビルドで null だった）。</summary>
        private static void ValidateTitle(List<string> errors)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(Phase6WorldIds.TitleScenePath) == null)
            {
                errors.Add("タイトル Scene がありません: " + Phase6WorldIds.TitleScenePath);
                return;
            }

            Scene title = EditorSceneManager.OpenScene(Phase6WorldIds.TitleScenePath, OpenSceneMode.Single);
            var launchers = Phase5ExplorationValidator.Components<Momotaro.Infrastructure.World.Phase6CampaignLauncher>(title);
            if (launchers.Count != 1)
            {
                errors.Add("タイトルの起動役（Phase6CampaignLauncher）が " + launchers.Count + " 個です（1 個であること）。");
            }

            foreach (var launcher in launchers)
            {
                if (launcher.Catalog == null
                    || AssetDatabase.GetAssetPath(launcher.Catalog) != Phase6WorldIds.CatalogDataPath)
                {
                    errors.Add("タイトルの起動役がこの campaign のカタログを指していません（実際="
                        + (launcher.Catalog != null ? AssetDatabase.GetAssetPath(launcher.Catalog) : "null") + "）。");
                }
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        /// <summary>Scene から保存対象を採る。</summary>
        public static AreaContentFacts Collect(Scene scene)
        {
            var facts = new AreaContentFacts { ScenePath = scene.path };

            foreach (AreaEncounterRunner runner in Phase5ExplorationValidator.Components<AreaEncounterRunner>(scene))
            {
                Add(facts, facts.Encounters, runner.EncounterId, "遭遇戦");
                var so = new SerializedObject(runner);
                if (so.FindProperty("_encounter")?.objectReferenceValue is EncounterData d && d.IsBossEncounter)
                {
                    facts.Bosses.Add(d.Id.Value);
                }
            }

            foreach (AreaFieldEnemyDirector director in Phase5ExplorationValidator.Components<AreaFieldEnemyDirector>(scene))
            {
                foreach (AreaFieldEnemyDirector.Placement p in director.Placements)
                {
                    Add(facts, facts.FieldPlacements, p.PlacementId, "普通敵の配置");
                }
            }

            foreach (AreaPickupPoint pickup in Phase5ExplorationValidator.Components<AreaPickupPoint>(scene))
            {
                Add(facts, facts.Pickups, pickup.PlacementId, "配置物");
            }

            // フラグは「立てるもの」と「見るもの」の和。同じフラグを両方が持つのは正常なので重複扱いしない。
            foreach (AreaFlagLever lever in Phase5ExplorationValidator.Components<AreaFlagLever>(scene))
            {
                if (!lever.FlagId.IsEmpty)
                {
                    facts.Flags.Add(lever.FlagId.Value);
                }
            }

            foreach (AreaFlagDoor door in Phase5ExplorationValidator.Components<AreaFlagDoor>(scene))
            {
                if (!door.FlagId.IsEmpty)
                {
                    facts.Flags.Add(door.FlagId.Value);
                }
            }

            foreach (CompanionInvestigationPoint point in Phase5ExplorationValidator.Components<CompanionInvestigationPoint>(scene))
            {
                Add(facts, facts.InvestigationPoints, point.PointId, "調査地点");
            }

            foreach (AreaEntryPoint entry in Phase5ExplorationValidator.Components<AreaEntryPoint>(scene))
            {
                facts.Entries[entry.EntryId.Value] = entry.ArrivalPosition;
            }

            foreach (ShrinePoint shrine in Phase5ExplorationValidator.Components<ShrinePoint>(scene))
            {
                if (facts.Shrines.ContainsKey(shrine.ShrineId.Value))
                {
                    facts.Duplicates.Add("お地蔵様の ID が Scene 内で重複しています: " + shrine.ShrineId.Value);
                }

                facts.Shrines[shrine.ShrineId.Value] = shrine.InteractionAnchor;

                // 受付の錨が固体の中・すぐ脇にあると、遮蔽判定（Default 層へ半径 0.25m の球）が像や壁に当たって
                // 一度も調べられない（実際に踏んだ）。錨の高さ 0.5m の点から球の半径＋余裕の範囲に固体が無いこと。
                Vector3 probePoint = shrine.InteractionAnchor + Vector3.up * 0.5f;
                foreach (Collider c in Phase5ExplorationValidator.Components<Collider>(scene))
                {
                    if (c == null || c.isTrigger)
                    {
                        continue;
                    }

                    if (c.gameObject.layer != Momotaro.Gameplay.Combat.CombatLayers.WallLayer)
                    {
                        continue;
                    }

                    Bounds b = BoundsOf(c);
                    if (b.size == Vector3.zero)
                    {
                        continue;
                    }

                    if (Vector3.Distance(b.ClosestPoint(probePoint), probePoint) < 0.3f)
                    {
                        facts.Duplicates.Add("お地蔵様 " + shrine.ShrineId.Value + " の受付の錨が固体 '" + c.name
                            + "' に近すぎます（遮蔽判定に当たって調べられない）");
                    }
                }
            }

            foreach (AreaArenaBoundary arena in Phase5ExplorationValidator.Components<AreaArenaBoundary>(scene))
            {
                facts.Arenas.Add(arena.SafeBounds);
            }

            return facts;
        }

        /// <summary>
        /// Collider の範囲。活動ゲートが無効化した Collider は <c>bounds</c> が空になるので、
        /// Box は寸法から組み立てる（P5.5 の検査が踏んだのと同じ）。
        /// </summary>
        private static Bounds BoundsOf(Collider c)
        {
            if (c is BoxCollider box)
            {
                Transform t = box.transform;
                Vector3 center = t.TransformPoint(box.center);
                Vector3 size = Vector3.Scale(box.size, t.lossyScale);
                return new Bounds(center, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
            }

            return c.enabled ? c.bounds : new Bounds(c.transform.position, Vector3.zero);
        }

        private static void Add(AreaContentFacts facts, HashSet<string> set, StableId id, string label)
        {
            if (id.IsEmpty)
            {
                facts.Duplicates.Add("保存 ID が空です: " + label);
                return;
            }

            if (!set.Add(id.Value))
            {
                facts.Duplicates.Add("保存 ID が Scene 内で重複しています: " + label + " " + id.Value);
            }
        }

        /// <summary>
        /// 戦闘 VFX（P6A 27）。P5 系の Builder は剣閃を置いていなかった（試遊で攻撃の VFX が出なかった原因）。
        /// 置かれていること・素材が全段全方向に入っていること・<b>この Area の主人公</b>を見ていることを確かめる
        /// （未割当だと全 Scene 検索へ落ち、2 Area 在留中に隣の主人公を拾いうる）。
        /// </summary>
        private static void ValidateCombatVfx(Scene scene, List<string> errors)
        {
            List<Momotaro.Presentation.Combat.PlayerSlashVfxPresenter> slashes =
                Phase5ExplorationValidator.Components<Momotaro.Presentation.Combat.PlayerSlashVfxPresenter>(scene);
            if (slashes.Count != 1)
            {
                errors.Add(scene.path + ": 主人公の剣閃（PlayerSlashVfxPresenter）が " + slashes.Count + " 個です（1 個であること。P6A 27）。");
            }

            foreach (var slash in slashes)
            {
                CheckSet(scene, "1 段目", slash.Stage1Frames, errors);
                CheckSet(scene, "2 段目", slash.Stage2Frames, errors);
                CheckSet(scene, "3 段目", slash.Stage3Frames, errors);
                CheckSet(scene, "必殺技", slash.SpecialFrames, errors);
                var so = new SerializedObject(slash);
                Object player = so.FindProperty("_player")?.objectReferenceValue;
                if (!(player is Component c) || c.gameObject.scene != scene)
                {
                    errors.Add(scene.path + ": 剣閃がこの Area の主人公を見ていません（_player 未割当または別 Scene）。");
                }
            }

            RequireExactlyOne<Momotaro.Presentation.Combat.EnemySlashVfxPresenter>(scene, "敵の剣閃", errors);
            RequireExactlyOne<Momotaro.Presentation.Combat.EnemyUnblockableWarningPresenter>(scene, "ガード不能の警告", errors);
            RequireExactlyOne<Momotaro.Presentation.Combat.JustGuardVfxPresenter>(scene, "ジャストガードの閃光", errors);
        }

        private static void CheckSet(Scene scene, string label,
            Momotaro.Presentation.Combat.PlayerSlashVfxPresenter.SlashFrameSet set, List<string> errors)
        {
            if (set == null || Empty(set.down) || Empty(set.up) || Empty(set.left) || Empty(set.right))
            {
                errors.Add(scene.path + ": 剣閃の " + label + " の素材が欠けています（方向のどれかが空）。");
            }
        }

        private static bool Empty(Sprite[] frames)
        {
            if (frames == null || frames.Length == 0)
            {
                return true;
            }

            foreach (Sprite f in frames)
            {
                if (f == null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void RequireExactlyOne<T>(Scene scene, string label, List<string> errors) where T : Component
        {
            int n = Phase5ExplorationValidator.Components<T>(scene).Count;
            if (n != 1)
            {
                errors.Add(scene.path + ": " + label + "（" + typeof(T).Name + "）が " + n + " 個です（1 個であること。P6A 27）。");
            }
        }

        /// <summary>Area Data の保存対象一覧が Scene の実在と一致すること（読み込みの検査がこの一覧を正本にするため）。</summary>
        private static void CompareManifest(AreaDefinition area, AreaContentFacts facts, List<string> errors)
        {
            AreaContentManifest m = area.Content;
            CompareSet(area, "遭遇戦", m.Encounters, facts.Encounters, errors);
            CompareSet(area, "ボス", m.Bosses, facts.Bosses, errors);
            CompareSet(area, "普通敵の配置", m.FieldPlacements, facts.FieldPlacements, errors);
            CompareSet(area, "配置物", m.Pickups, facts.Pickups, errors);
            CompareSet(area, "フラグ", m.Flags, facts.Flags, errors);
            CompareSet(area, "調査地点", m.InvestigationPoints, facts.InvestigationPoints, errors);
        }

        private static void CompareSet(AreaDefinition area, string label, IReadOnlyList<StableId> declared,
            HashSet<string> actual, List<string> errors)
        {
            var declaredSet = new HashSet<string>();
            foreach (StableId id in declared)
            {
                declaredSet.Add(id.Value);
                if (!actual.Contains(id.Value))
                {
                    errors.Add(area.Id.Value + ": 保存対象一覧の" + label + " '" + id.Value
                        + "' が Scene にありません（保存しても復元先が無い）。");
                }
            }

            foreach (string id in actual)
            {
                if (!declaredSet.Contains(id))
                {
                    errors.Add(area.Id.Value + ": Scene の" + label + " '" + id
                        + "' が保存対象一覧にありません（読み込みで未知の ID として断られる）。");
                }
            }
        }

        /// <summary>お地蔵様：入口と像が実在し、近く、戦闘区域の外にある（休息・再開の安全地点）。</summary>
        private static void ValidateShrines(CampaignCatalog campaign, Dictionary<string, AreaContentFacts> areas,
            List<string> errors)
        {
            var declared = new HashSet<string>();
            foreach (ShrineInfo shrine in campaign.Shrines)
            {
                declared.Add(shrine.ShrineId.Value);
                if (!areas.TryGetValue(shrine.AreaId.Value, out AreaContentFacts facts))
                {
                    errors.Add("お地蔵様 " + shrine.ShrineId.Value + " のエリア '" + shrine.AreaId.Value + "' を検査できません。");
                    continue;
                }

                if (!facts.Shrines.TryGetValue(shrine.ShrineId.Value, out Vector3 statue))
                {
                    errors.Add("お地蔵様 " + shrine.ShrineId.Value + " の ShrinePoint が " + facts.ScenePath + " にありません。");
                    continue;
                }

                if (!facts.Entries.TryGetValue(shrine.Entry.EntryId.Value, out Vector3 entry))
                {
                    errors.Add("お地蔵様 " + shrine.ShrineId.Value + " の入口 '" + shrine.Entry.EntryId.Value
                        + "' が " + facts.ScenePath + " にありません。");
                    continue;
                }

                float distance = Vector2.Distance(new Vector2(statue.x, statue.z), new Vector2(entry.x, entry.z));
                if (distance > 3f)
                {
                    errors.Add("お地蔵様 " + shrine.ShrineId.Value + " の入口が像から離れすぎています（" + distance + "m）。");
                }

                foreach (Bounds arena in facts.Arenas)
                {
                    var flat = new Vector3(entry.x, arena.center.y, entry.z);
                    if (arena.Contains(flat))
                    {
                        errors.Add("お地蔵様 " + shrine.ShrineId.Value + " の入口が戦闘区域の中にあります（再開直後に戦闘へ入る）。");
                    }
                }
            }

            foreach (KeyValuePair<string, AreaContentFacts> area in areas)
            {
                foreach (string shrineId in area.Value.Shrines.Keys)
                {
                    if (!declared.Contains(shrineId))
                    {
                        errors.Add(area.Value.ScenePath + ": ShrinePoint '" + shrineId + "' がカタログのお地蔵様一覧にありません。");
                    }
                }
            }
        }

        /// <summary>保存 ID が campaign 内で一意（別エリアの同じ ID は保存データ上で区別できない）。</summary>
        private static void ValidateUniqueness(AreaCatalogData data, Dictionary<string, AreaContentFacts> areas,
            List<string> errors)
        {
            var owner = new Dictionary<string, string>();
            foreach (KeyValuePair<string, AreaContentFacts> area in areas)
            {
                AreaContentFacts f = area.Value;
                foreach (HashSet<string> set in new[] { f.Encounters, f.FieldPlacements, f.Pickups, f.InvestigationPoints })
                {
                    foreach (string id in set)
                    {
                        if (owner.TryGetValue(id, out string other) && other != area.Key)
                        {
                            errors.Add("保存 ID '" + id + "' が " + other + " と " + area.Key + " の両方にあります。");
                        }

                        owner[id] = area.Key;
                    }
                }
            }
        }
    }
}

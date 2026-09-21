using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Exploration;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Infrastructure.Input;
using Momotaro.Presentation.Companion;
using Momotaro.Presentation.Hud;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Editor.Phase4
{
    /// <summary>
    /// 探索の層（地点・加入供給元・記録・調停役・試遊段階・入力仲介・マーカー・短文 UI）を Scene へ組む共通処理（P4-07A／07B／08R。v1.0 §13.2）。
    /// 検証 Scene と試遊 Scene の両方が同じ配線を使う（2 か所に持たない）。ダイアログは出さない。
    /// </summary>
    public static class Phase4InvestigationLayerBuilder
    {
        /// <summary>出荷される探索設定 Data のパス（§10.2 の試遊初期値）。</summary>
        public const string SettingsAssetPath = "Assets/_Project/Data/Exploration/SO_Investigation_Trial.asset";

        /// <summary>未加入の検証経路に使う、存在しない仲間の StableId（猿若は P8 まで加入しない）。</summary>
        public static readonly StableId UnrecruitedCompanionId = new StableId("companion_saruwaka");

        /// <summary>地点 1 つの仕様。</summary>
        public readonly struct PointSpec
        {
            public string Name { get; }
            public Vector3 Position { get; }
            public StableId PointId { get; }
            public StableId RequiredCompanion { get; }
            public StableId DiscoveryId { get; }

            /// <summary>主人公側からの直線を遮る壁を置くか（到達不能の検証地点）。</summary>
            public bool WallInFront { get; }

            public PointSpec(string name, Vector3 position, string pointId, StableId requiredCompanion, string discoveryId, bool wallInFront = false)
            {
                Name = name;
                Position = position;
                PointId = new StableId(pointId);
                RequiredCompanion = requiredCompanion;
                DiscoveryId = new StableId(discoveryId);
                WallInFront = wallInFront;
            }
        }

        /// <summary>組んだ層の主要参照。</summary>
        public sealed class Result
        {
            public CompanionRosterContext Roster;
            public InvestigationRecordHolder Record;
            public InvestigationCoordinator Coordinator;
            public TrialStageController Stage;
            public InvestigationInteractInput Input;
            public InvestigationPromptHud Hud;
            public TrialCombatStartInput StartInput;
            public List<CompanionInvestigationPoint> Points = new List<CompanionInvestigationPoint>();
            public List<InvestigationPointMarker> Markers = new List<InvestigationPointMarker>();
        }

        /// <summary>
        /// 主人公（Z=-6）の周囲に置く地点の標準配置。正常 ×2、壁で到達できない検証用 ×1、未加入条件の確認用 ×1（§13.1）。
        /// 敵の湧きは +Z 側なので、地点は −Z 側へ寄せる。
        /// </summary>
        public static PointSpec[] StandardPoints(StableId recruitedCompanion)
        {
            return new[]
            {
                new PointSpec("InvestigationPoint_Left", new Vector3(-3.5f, 0f, -8f), "point_trial_left", recruitedCompanion, "discovery_trial_left"),
                new PointSpec("InvestigationPoint_Right", new Vector3(3.5f, 0f, -8f), "point_trial_right", recruitedCompanion, "discovery_trial_right"),
                new PointSpec("InvestigationPoint_Walled", new Vector3(0f, 0f, -11f), "point_trial_walled", recruitedCompanion, "discovery_trial_walled", wallInFront: true),
                new PointSpec("InvestigationPoint_Unrecruited", new Vector3(7f, 0f, -6f), "point_trial_unrecruited", UnrecruitedCompanionId, "discovery_trial_unrecruited"),
            };
        }

        /// <summary>出荷される設定 Data を読む（無ければ null。Builder 側で失敗として扱う。テストは一時 Asset を渡せる）。</summary>
        public static InvestigationSettingsData LoadSettings(string path = SettingsAssetPath)
        {
            return AssetDatabase.LoadAssetAtPath<InvestigationSettingsData>(path);
        }

        /// <summary>
        /// 探索の層を組む。<paramref name="parent"/> の下に Investigation ノードを作り、地点・供給元・記録・調停役・段階を置く。
        /// </summary>
        /// <param name="parent">親（Phase4Systems 等）。</param>
        /// <param name="player">主人公。</param>
        /// <param name="companions">依頼を受ける仲間（Scene に置いた実体）。</param>
        /// <param name="recruited">加入済みとして注入する StableId。</param>
        /// <param name="points">地点の仕様。</param>
        /// <param name="settings">距離・時間の設定 Data。</param>
        /// <param name="context">活動 Context（試遊段階を配線する）。</param>
        /// <param name="waves">明示開始で起動する Wave（無い Scene では null）。</param>
        public static Result Build(
            Transform parent,
            PlayerStateController player,
            IList<CompanionActor> companions,
            StableId[] recruited,
            PointSpec[] points,
            InvestigationSettingsData settings,
            CompanionActivityContext context,
            WaveRunner waves)
        {
            var result = new Result();

            var rootGo = new GameObject("Investigation");
            rootGo.transform.SetParent(parent, false);

            // 加入資格の供給元（§5.1）。
            var rosterGo = new GameObject("CompanionRoster");
            rosterGo.transform.SetParent(rootGo.transform, false);
            result.Roster = rosterGo.AddComponent<CompanionRosterContext>();
            result.Roster.SetRecruited(recruited);

            // Scene 単位の調査済み記録（§10.3）。
            var recordGo = new GameObject("InvestigationRecord");
            recordGo.transform.SetParent(rootGo.transform, false);
            result.Record = recordGo.AddComponent<InvestigationRecordHolder>();

            // 地点（§10.1）。
            var pointsGo = new GameObject("InvestigationPoints");
            pointsGo.transform.SetParent(rootGo.transform, false);
            foreach (PointSpec spec in points)
            {
                var pointGo = new GameObject(spec.Name);
                pointGo.transform.SetParent(pointsGo.transform, false);
                pointGo.transform.position = spec.Position;
                var point = pointGo.AddComponent<CompanionInvestigationPoint>();
                point.Configure(spec.PointId, spec.RequiredCompanion, spec.DiscoveryId, settings);
                result.Points.Add(point);

                if (spec.WallInFront)
                {
                    // 主人公側（+Z）から地点への直線を遮る壁。主人公は壁の手前（地点から 1.5m 以内）に立てるが、
                    // 調査位置へは到達できない＝受付前の到達不能として拒否される（P02 前半）。
                    Vector3 wallPos = spec.Position + new Vector3(0f, 0.5f, 0.9f);
                    CreateWall(spec.Name + "_Wall", wallPos, new Vector3(2.5f, 1f, 0.3f), pointsGo.transform);
                }
            }

            // 探索の調停役（§6.3）。
            var drivers = new List<CompanionInvestigationController>();
            foreach (CompanionActor actor in companions)
            {
                CompanionInvestigationController driver = actor != null ? actor.GetComponent<CompanionInvestigationController>() : null;
                if (driver != null)
                {
                    drivers.Add(driver);
                }
            }

            var coordinatorGo = new GameObject("InvestigationCoordinator");
            coordinatorGo.transform.SetParent(rootGo.transform, false);
            result.Coordinator = coordinatorGo.AddComponent<InvestigationCoordinator>();
            result.Coordinator.Bind(player, result.Roster, result.Record, drivers.ToArray());

            // 試遊段階（P4-08R）。起動直後は自由探索、明示開始で戦闘へ。
            var stageGo = new GameObject("TrialStage");
            stageGo.transform.SetParent(rootGo.transform, false);
            result.Stage = stageGo.AddComponent<TrialStageController>();
            result.Stage.Bind(waves, result.Coordinator);
            context?.BindStage(result.Stage);

            // 入力仲介（P4-07B。§4.2）。既存 Interact（E／南ボタン）を 1 回消費して調停役へ渡す。
            var inputGo = new GameObject("InvestigationInput");
            inputGo.transform.SetParent(rootGo.transform, false);
            result.Input = inputGo.AddComponent<InvestigationInteractInput>();
            result.Input.Bind(result.Coordinator);

            // 地点マーカー（§11）。輪と文字で「？／調べる／理由／調査中／済」を示す。Gizmos に依存しない。
            Sprite ring = Phase4PlaceholderText.ResolveRingSprite();
            foreach (CompanionInvestigationPoint point in result.Points)
            {
                result.Markers.Add(CreateMarker(point, result.Record, result.Coordinator, ring));
            }

            // 短文 UI（§4.1・§11）。案内・通知・開始行。候補の結果をマーカーへ配る。
            var hudGo = new GameObject("InvestigationHud");
            hudGo.transform.SetParent(rootGo.transform, false);
            result.Hud = hudGo.AddComponent<InvestigationPromptHud>();
            result.Hud.Bind(result.Coordinator, result.Stage, result.Markers.ToArray());

            // 明示的な戦闘開始入力（P4-08R。Wave の無い Scene には置かない）。
            if (waves != null)
            {
                var startGo = new GameObject("TrialCombatStartInput");
                startGo.transform.SetParent(rootGo.transform, false);
                result.StartInput = startGo.AddComponent<TrialCombatStartInput>();
                result.StartInput.Bind(result.Stage);
            }

            return result;
        }

        /// <summary>地点 1 つにマーカー（輪＋頭上の文字）を付ける。</summary>
        public static InvestigationPointMarker CreateMarker(
            CompanionInvestigationPoint point, InvestigationRecordHolder record, InvestigationCoordinator coordinator, Sprite ring)
        {
            Transform parent = point.transform;

            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(parent, false);
            ringGo.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            ringGo.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            ringGo.transform.localScale = new Vector3(3f, 3f, 1f); // Knob は 32px（0.32m）。約 1m の輪にする。
            var ringRenderer = ringGo.AddComponent<SpriteRenderer>();
            ringRenderer.sprite = ring;
            ringRenderer.color = new Color(0.45f, 0.75f, 1f, 0.85f);

            TextMesh label = Phase4PlaceholderText.CreateLabel(
                "Label", parent, new Vector3(0f, 1.1f, 0f), Color.white, 0.07f);
            label.text = InvestigationTexts.UnknownMark;

            var marker = point.gameObject.AddComponent<InvestigationPointMarker>();
            marker.Bind(point, record, coordinator, ringRenderer, label);
            return marker;
        }

        /// <summary>WaveRunner の自動開始を切る（P3.5 の既定は変えず、この Scene のインスタンスだけ）。</summary>
        public static void DisableAutoStart(WaveRunner waves)
        {
            if (waves == null)
            {
                return;
            }

            var so = new SerializedObject(waves);
            SerializedProperty auto = so.FindProperty("_autoStart");
            if (auto != null)
            {
                auto.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void CreateWall(string name, Vector3 center, Vector3 size, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.layer = Momotaro.Gameplay.Combat.CombatLayers.WallLayer >= 0 ? Momotaro.Gameplay.Combat.CombatLayers.WallLayer : 0;
        }
    }
}

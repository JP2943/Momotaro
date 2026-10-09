using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Progression;
using UnityEngine;

namespace Momotaro.Data.World
{
    /// <summary>
    /// P5 のエリアカタログ（P5-02。仕様書 v1.1 §13.3「カタログから A／B の Scene と全入口を解決できる」）。
    ///
    /// 死亡再開点はここが正本で、P5 では変更できない固定の試作再開点（A の開始点。§3.1）。
    /// お地蔵様の保存・休息機能は先行実装しない。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_AreaCatalog_New", menuName = "Momotaro/World/Area Catalog")]
    public sealed class AreaCatalogData : GameDataAsset
    {
        [Header("エリア")]
        [SerializeField] private List<AreaDefinition> _areas = new List<AreaDefinition>();

        [Header("死亡再開点（P5 では固定）")]
        [SerializeField] private StableId _respawnAreaId;
        [SerializeField] private StableId _respawnEntryId;

        [Header("P6 campaign（P6A。P5／P5.5 の Data は既定のまま）")]
        [Tooltip("遭遇戦のクリア規則。P5 既定は再出現周期つき、P6 は恒久。")]
        [SerializeField] private EncounterClearPolicy _encounterClearPolicy = EncounterClearPolicy.PerRespawnCycle;

        [Tooltip("保存の内容版。Data の互換を壊す変更で上げる（保存 Envelope の contentVersion）。")]
        [SerializeField] private int _contentVersion = 1;

        [Tooltip("お地蔵様（死亡再開点・旅立ち先・中断の復帰点）。")]
        [SerializeField] private List<ShrineDefinition> _shrines = new List<ShrineDefinition>();

        [Tooltip("New Game の初期お地蔵様（開始時の技術用初期値。P6 仕様 §5 末尾）。")]
        [SerializeField] private StableId _initialShrineId;

        [Tooltip("きびだんごの基本補充上限（仮値。成長で増える分は別）。")]
        [SerializeField] private int _kibidangoBaseCapacity = 3;

        [Tooltip("きびだんご 1 個の基礎回復量（P6B。成長なしの基礎最大 HP の半分を切り上げた固定値。0 は使用動作なし＝P6A）。")]
        [SerializeField] private int _kibidangoBaseHeal;

        [Tooltip("きびだんご使用の全動作（Gameplay 秒。P6B 仕様 §6）。")]
        [SerializeField] private float _kibidangoUseSeconds = 2f;

        [Tooltip("きびだんご使用の確定時刻（開始からの Gameplay 秒。回復と残数 -1 を同じ更新で一回だけ）。")]
        [SerializeField] private float _kibidangoCommitSeconds = 1.5f;

        [Tooltip("きびだんご使用中の移動速度倍率（歩行速度への倍率。ノックバックには掛けない）。")]
        [SerializeField] private float _kibidangoMoveSpeedMultiplier = 0.2f;

        [Header("払い戻し（P6B 仕様 §5）")]
        [Tooltip("初期の払い戻し権利。")]
        [SerializeField] private int _refundRightsInitial = 3;

        [Tooltip("章クリア 1 回で追加する権利。")]
        [SerializeField] private int _refundRightsPerChapter = 3;

        [Tooltip("払い戻し権利の保有上限。")]
        [SerializeField] private int _refundRightsMax = 6;

        [Tooltip("権利を追加しうる章の ID（P6B は接続 fixture だけ。本番の章進行は作らない）。")]
        [SerializeField] private List<StableId> _chapterIds = new List<StableId>();

        [Header("保存")]
        [Tooltip("保存スロット名（campaign ごとに分ける。P6A は既定の slot0）。")]
        [SerializeField] private string _saveSlotName = "slot0";

        [Tooltip("一般消耗品の定義（保存の検証で未知 ID を拒否する）。")]
        [SerializeField] private List<ItemDefinition> _items = new List<ItemDefinition>();

        [Tooltip("成長項目（P6A は検証用の仮項目だけ）。")]
        [SerializeField] private List<SkillNodeData> _growthNodes = new List<SkillNodeData>();

        [Tooltip("この campaign で付与されうる一度きり（GrantOnce）報酬の ID。保存の検証で未知 ID を拒否する（初到達報酬は自動で含む）。")]
        [SerializeField] private List<StableId> _grantOnceRewardIds = new List<StableId>();

        [Tooltip("この campaign で加入しうる仲間の ID。保存の検証で未知 ID を拒否する。")]
        [SerializeField] private List<StableId> _companionIds = new List<StableId>();

        [Tooltip("この campaign の保存が段階を持ちうるクエストの ID（P7 の接続口。P6A は接続 fixture だけ）。")]
        [SerializeField] private List<StableId> _questIds = new List<StableId>();

        [Header("テスト専用の調整（P6 の検証 campaign だけ。本編の数値ではない）")]
        [Tooltip("敵の攻撃力の倍率（死亡を何度も試すための措置）。0 以下は未設定＝1 として扱う。")]
        [SerializeField] private float _testEnemyAttackScale = 1f;

        [Tooltip("主人公の基礎最大 HP の倍率（成長の加算より前に掛ける）。0 以下は未設定＝1 として扱う。")]
        [SerializeField] private float _testPlayerMaxHpScale = 1f;

        /// <summary>登録エリア（読み取り専用）。</summary>
        public IReadOnlyList<AreaDefinition> Areas => _areas;

        /// <summary>死亡再開するエリアの ID。</summary>
        public StableId RespawnAreaId => _respawnAreaId;

        /// <summary>死亡再開する入口の ID。</summary>
        public StableId RespawnEntryId => _respawnEntryId;

        /// <summary>遭遇戦のクリア規則（P6A）。</summary>
        public EncounterClearPolicy EncounterClearPolicy => _encounterClearPolicy;

        /// <summary>保存の内容版（P6A）。</summary>
        public int ContentVersion => _contentVersion;

        /// <summary>お地蔵様（P6A）。</summary>
        public IReadOnlyList<ShrineDefinition> Shrines => _shrines;

        /// <summary>New Game の初期お地蔵様（P6A）。</summary>
        public StableId InitialShrineId => _initialShrineId;

        /// <summary>きびだんごの基本補充上限（P6A の仮値）。</summary>
        public int KibidangoBaseCapacity => _kibidangoBaseCapacity;

        /// <summary>テスト専用：敵の攻撃力の倍率（未設定は 1）。</summary>
        public float TestEnemyAttackScale => _testEnemyAttackScale > 0f ? _testEnemyAttackScale : 1f;

        /// <summary>テスト専用：主人公の基礎最大 HP の倍率（未設定は 1）。</summary>
        public float TestPlayerMaxHpScale => _testPlayerMaxHpScale > 0f ? _testPlayerMaxHpScale : 1f;

        /// <summary>一般消耗品の定義（P6A）。</summary>
        public IReadOnlyList<ItemDefinition> Items => _items;

        public int KibidangoBaseHeal => _kibidangoBaseHeal;

        public float KibidangoUseSeconds => _kibidangoUseSeconds;

        public float KibidangoCommitSeconds => _kibidangoCommitSeconds;

        public float KibidangoMoveSpeedMultiplier => _kibidangoMoveSpeedMultiplier;

        public int RefundRightsInitial => _refundRightsInitial;

        public int RefundRightsPerChapter => _refundRightsPerChapter;

        public int RefundRightsMax => _refundRightsMax;

        public IReadOnlyList<StableId> ChapterIds => _chapterIds;

        public string SaveSlotName => string.IsNullOrEmpty(_saveSlotName) ? "slot0" : _saveSlotName;

        /// <summary>成長項目（P6A の仮項目）。</summary>
        public IReadOnlyList<SkillNodeData> GrowthNodes => _growthNodes;

        /// <summary>この campaign で付与されうる GrantOnce 報酬の ID（初到達報酬は別に自動で含む）。</summary>
        public IReadOnlyList<StableId> GrantOnceRewardIds => _grantOnceRewardIds;

        /// <summary>この campaign で加入しうる仲間の ID。</summary>
        public IReadOnlyList<StableId> CompanionIds => _companionIds;

        /// <summary>この campaign の保存が段階を持ちうるクエストの ID（P7 の接続口）。</summary>
        public IReadOnlyList<StableId> QuestIds => _questIds;

        /// <summary>保存・お地蔵様を持つ P6 campaign か（恒久クリア規則で判定する）。</summary>
        public bool IsP6Campaign => _encounterClearPolicy == EncounterClearPolicy.Permanent;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);

            if (_areas.Count == 0)
            {
                report.Error(name + ": Catalog has no areas.");
                return;
            }

            var seen = new HashSet<string>();
            AreaDefinition respawnArea = null;
            for (int i = 0; i < _areas.Count; i++)
            {
                AreaDefinition a = _areas[i];
                if (a == null)
                {
                    report.Error(name + ": Areas[" + i + "] is null.");
                    continue;
                }

                if (!a.Id.IsValid)
                {
                    report.Error(name + ": Areas[" + i + "] has invalid Stable ID.");
                    continue;
                }

                if (!seen.Add(a.Id.Value))
                {
                    report.Error(name + ": Duplicate area id '" + a.Id.Value + "'.");
                }

                if (a.Id.Equals(_respawnAreaId))
                {
                    respawnArea = a;
                }
            }

            if (_respawnAreaId.IsEmpty || _respawnEntryId.IsEmpty)
            {
                report.Error(name + ": Respawn area/entry id is empty (P5 requires a fixed respawn point).");
                return;
            }

            if (respawnArea == null)
            {
                report.Error(name + ": Respawn area '" + _respawnAreaId.Value + "' is not in the catalog.");
                return;
            }

            bool found = false;
            foreach (AreaEntryDefinition e in respawnArea.Entries)
            {
                if (e != null && e.EntryId.Equals(_respawnEntryId))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                report.Error(name + ": Respawn entry '" + _respawnEntryId.Value
                    + "' is not an entry of area '" + _respawnAreaId.Value + "'.");
            }

            if (IsP6Campaign)
            {
                ValidateCampaign(report);
            }
        }

        /// <summary>
        /// P6 campaign の検査（P6A）。お地蔵様の入口が実在すること、初期お地蔵様が一覧にあること、
        /// ID の一意、アイテム上限・成長項目の健全さ。<b>Gameplay の <c>AreaCatalog.TryBuild</c> と同じ条件</b>。
        /// </summary>
        private void ValidateCampaign(DataValidationReport report)
        {
            if (!Id.IsValid)
            {
                report.Error(name + ": P6 campaign needs a valid catalog Stable ID (it is the campaign id of saves).");
            }

            if (_contentVersion < 1)
            {
                report.Error(name + ": ContentVersion must be >= 1.");
            }

            if (_kibidangoBaseCapacity < 0)
            {
                report.Error(name + ": KibidangoBaseCapacity must be >= 0.");
            }

            if (_kibidangoBaseHeal < 0)
            {
                report.Error(name + ": KibidangoBaseHeal must be >= 0.");
            }

            if (_kibidangoBaseHeal > 0
                && !(_kibidangoCommitSeconds > 0f && _kibidangoUseSeconds >= _kibidangoCommitSeconds
                     && !float.IsInfinity(_kibidangoUseSeconds)
                     && _kibidangoMoveSpeedMultiplier >= 0f && _kibidangoMoveSpeedMultiplier <= 1f))
            {
                report.Error(name + ": kibidango use timing must satisfy 0 < commit <= use (finite) and 0 <= move <= 1.");
            }

            if (_refundRightsInitial < 0 || _refundRightsPerChapter < 0 || _refundRightsMax < 0
                || _refundRightsInitial > _refundRightsMax)
            {
                report.Error(name + ": refund rights must satisfy 0 <= initial <= max and perChapter >= 0.");
            }

            var chapters = new HashSet<string>();
            for (int i = 0; i < _chapterIds.Count; i++)
            {
                if (!_chapterIds[i].IsValid || !chapters.Add(_chapterIds[i].Value))
                {
                    report.Error(name + ": ChapterIds[" + i + "] is invalid or duplicated.");
                }
            }

            if (float.IsNaN(_testEnemyAttackScale) || float.IsInfinity(_testEnemyAttackScale)
                || float.IsNaN(_testPlayerMaxHpScale) || float.IsInfinity(_testPlayerMaxHpScale))
            {
                report.Error(name + ": test tuning scales must be finite.");
            }

            var shrineIds = new HashSet<string>();
            bool initialFound = false;
            for (int i = 0; i < _shrines.Count; i++)
            {
                ShrineDefinition shrine = _shrines[i];
                if (shrine == null || !shrine.ShrineId.IsValid)
                {
                    report.Error(name + ": Shrines[" + i + "] has an invalid Stable ID.");
                    continue;
                }

                if (!shrineIds.Add(shrine.ShrineId.Value))
                {
                    report.Error(name + ": Duplicate shrine id '" + shrine.ShrineId.Value + "'.");
                }

                if (shrine.ShrineId.Equals(_initialShrineId))
                {
                    initialFound = true;
                }

                if (!HasEntry(shrine.AreaId, shrine.EntryId))
                {
                    report.Error(name + ": Shrine '" + shrine.ShrineId.Value + "' entry '"
                        + shrine.AreaId.Value + "/" + shrine.EntryId.Value + "' is not an entry in the catalog.");
                }
            }

            if (!initialFound)
            {
                report.Error(name + ": Initial shrine '" + _initialShrineId.Value + "' is not among the shrines.");
            }

            var itemIds = new HashSet<string>();
            for (int i = 0; i < _items.Count; i++)
            {
                ItemDefinition item = _items[i];
                if (item == null || !item.ItemId.IsValid)
                {
                    report.Error(name + ": Items[" + i + "] has an invalid Stable ID.");
                    continue;
                }

                if (!itemIds.Add(item.ItemId.Value))
                {
                    report.Error(name + ": Duplicate item id '" + item.ItemId.Value + "'.");
                }

                if (item.MaxStack < 1)
                {
                    report.Error(name + ": Item '" + item.ItemId.Value + "' MaxStack must be >= 1.");
                }
            }

            var growthIds = new HashSet<string>();
            for (int i = 0; i < _growthNodes.Count; i++)
            {
                SkillNodeData node = _growthNodes[i];
                if (node == null || !node.Id.IsValid)
                {
                    report.Error(name + ": GrowthNodes[" + i + "] is null or has an invalid Stable ID.");
                    continue;
                }

                if (!growthIds.Add(node.Id.Value))
                {
                    report.Error(name + ": Duplicate growth id '" + node.Id.Value + "'.");
                }

                if (!node.HasAnyEffect)
                {
                    report.Error(name + ": Growth '" + node.Id.Value + "' has no effect (zero-effect nodes are not allowed).");
                }
            }

            var graphErrors = new List<string>();
            if (!SkillGraphCheck.Check(_growthNodes, graphErrors))
            {
                for (int i = 0; i < graphErrors.Count; i++)
                {
                    report.Error(name + ": " + graphErrors[i]);
                }
            }
        }

        private bool HasEntry(StableId areaId, StableId entryId)
        {
            for (int i = 0; i < _areas.Count; i++)
            {
                AreaDefinition area = _areas[i];
                if (area == null || !area.Id.Equals(areaId))
                {
                    continue;
                }

                foreach (AreaEntryDefinition e in area.Entries)
                {
                    if (e != null && e.EntryId.Equals(entryId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

#if UNITY_EDITOR
        /// <summary>Builder から組み立てるための設定入口（Editor 専用）。</summary>
        public void EditorSet(List<AreaDefinition> areas, StableId respawnAreaId, StableId respawnEntryId)
        {
            _areas = areas ?? new List<AreaDefinition>();
            _respawnAreaId = respawnAreaId;
            _respawnEntryId = respawnEntryId;
        }

        /// <summary>P6 campaign の設定入口（Editor 専用。P6A の Builder）。</summary>
        public void EditorSetCampaign(
            EncounterClearPolicy policy, int contentVersion, List<ShrineDefinition> shrines, StableId initialShrineId,
            int kibidangoBaseCapacity, List<ItemDefinition> items, List<SkillNodeData> growthNodes)
        {
            _encounterClearPolicy = policy;
            _contentVersion = contentVersion;
            _shrines = shrines ?? new List<ShrineDefinition>();
            _initialShrineId = initialShrineId;
            _kibidangoBaseCapacity = kibidangoBaseCapacity;
            _items = items ?? new List<ItemDefinition>();
            _growthNodes = growthNodes ?? new List<SkillNodeData>();
        }

        /// <summary>保存で扱う既知 ID（一度きり報酬・仲間）の設定入口（Editor 専用。レビュー R4）。</summary>
        public void EditorSetKnownIds(List<StableId> grantOnceRewardIds, List<StableId> companionIds,
            List<StableId> questIds = null)
        {
            _grantOnceRewardIds = grantOnceRewardIds ?? new List<StableId>();
            _companionIds = companionIds ?? new List<StableId>();
            _questIds = questIds ?? new List<StableId>();
        }

        /// <summary>P6B の設定入口（Editor 専用。きびだんご使用・払い戻し・章・保存スロット）。</summary>
        public void EditorSetP6B(int kibidangoBaseHeal, float useSeconds, float commitSeconds, float moveSpeedMultiplier,
            int refundInitial, int refundPerChapter, int refundMax, List<StableId> chapterIds, string saveSlotName)
        {
            _kibidangoBaseHeal = kibidangoBaseHeal;
            _kibidangoUseSeconds = useSeconds;
            _kibidangoCommitSeconds = commitSeconds;
            _kibidangoMoveSpeedMultiplier = moveSpeedMultiplier;
            _refundRightsInitial = refundInitial;
            _refundRightsPerChapter = refundPerChapter;
            _refundRightsMax = refundMax;
            _chapterIds = chapterIds ?? new List<StableId>();
            _saveSlotName = saveSlotName;
        }

        /// <summary>テスト専用の調整の設定入口（Editor 専用。P6A の Builder）。</summary>
        public void EditorSetTestTuning(float enemyAttackScale, float playerMaxHpScale)
        {
            _testEnemyAttackScale = enemyAttackScale;
            _testPlayerMaxHpScale = playerMaxHpScale;
        }
#endif
    }
}

using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Session;

namespace Momotaro.Gameplay.Save
{
    /// <summary>
    /// 保存内容の検証（P6A-02。仕様 §10）。<b>Scene を読まずに</b>、許可された campaign カタログだけで全 ID を解決する。
    ///
    /// 必須欠損・未知 ID・重複・負数・NaN・範囲違反・所有 Area 不一致・取得済み効果の矛盾・無効な復帰点を拒否する。
    /// <b>黙って直さない</b>——1 件でもあれば Load しない（黙って New Game にしない）。理由は全件返す。
    /// 書式（JSON の欠損・重複プロパティ・型違い）は Infrastructure の読み手が先に落とす。
    /// </summary>
    public static class SaveSnapshotValidator
    {
        /// <summary>検証する。エラーが 0 件なら true。</summary>
        public static bool Validate(SaveSnapshot snapshot, AreaCatalog catalog, List<string> errors)
        {
            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            int before = errors.Count;
            if (snapshot == null)
            {
                errors.Add("保存内容がありません。");
                return false;
            }

            CampaignCatalog campaign = catalog?.Campaign;
            if (campaign == null)
            {
                errors.Add("保存を扱える campaign カタログがありません（P6 campaign ではない）。");
                return false;
            }

            // ---- 冒険の同一性 ----
            if (!string.Equals(snapshot.CampaignId, campaign.CampaignId.Value, StringComparison.Ordinal))
            {
                errors.Add("別の campaign の保存です（保存 " + snapshot.CampaignId + "／現在 " + campaign.CampaignId.Value + "）。");
            }

            if (snapshot.ContentVersion != campaign.ContentVersion)
            {
                errors.Add("内容版が違います（保存 " + snapshot.ContentVersion + "／現在 " + campaign.ContentVersion + "）。");
            }

            if (string.IsNullOrEmpty(snapshot.AdventureId))
            {
                errors.Add("冒険 ID がありません。");
            }

            if (snapshot.Revision < 0)
            {
                errors.Add("世界の版が負です。");
            }

            if (snapshot.RespawnCycle < 0)
            {
                errors.Add("再出現周期が負です。");
            }

            // ---- 徳と成長 ----
            if (snapshot.TotalVirtue < 0 || snapshot.SpentVirtue < 0 || snapshot.SpentVirtue > snapshot.TotalVirtue)
            {
                errors.Add("徳の会計が不正です（累計 " + snapshot.TotalVirtue + "／使用済み " + snapshot.SpentVirtue + "）。");
            }

            CheckIdList(snapshot.GrantedRewards, "付与済み報酬", campaign.IsKnownGrantOnceReward, errors);

            long growthSum = 0;
            var growthIds = new HashSet<string>();
            foreach (KeyValuePair<string, int> pair in snapshot.Growth)
            {
                if (!campaign.TryGetGrowth(new StableId(pair.Key), out _))
                {
                    errors.Add("未知の成長 ID '" + pair.Key + "'。");
                    continue;
                }

                if (!growthIds.Add(pair.Key))
                {
                    errors.Add("成長 ID '" + pair.Key + "' が重複しています。");
                }

                if (pair.Value < 0)
                {
                    errors.Add("成長 '" + pair.Key + "' の支出が負です。");
                }

                growthSum += pair.Value;
            }

            foreach (KeyValuePair<string, int> pair in snapshot.Growth)
            {
                if (!campaign.TryGetGrowth(new StableId(pair.Key), out GrowthInfo info))
                {
                    continue;
                }

                for (int i = 0; i < info.Prerequisites.Count; i++)
                {
                    if (!growthIds.Contains(info.Prerequisites[i].Value))
                    {
                        errors.Add("成長 '" + pair.Key + "' の前提 '" + info.Prerequisites[i].Value + "' が未取得です。");
                    }
                }

                for (int i = 0; i < info.Exclusives.Count; i++)
                {
                    if (growthIds.Contains(info.Exclusives[i].Value))
                    {
                        errors.Add("成長 '" + pair.Key + "' と排他の '" + info.Exclusives[i].Value + "' が両方取得されています。");
                    }
                }
            }

            if (growthSum != snapshot.SpentVirtue)
            {
                errors.Add("使用済み徳（" + snapshot.SpentVirtue + "）と成長の実支出合計（" + growthSum + "）が一致しません。");
            }

            // ---- 訪問・加入 ----
            CheckIdList(snapshot.VisitedAreas, "訪問済みエリア", id => catalog.TryGetScenePath(id, out _), errors);
            CheckIdList(snapshot.Recruited, "加入済み仲間", campaign.IsKnownCompanion, errors);

            // ---- Area ごとの記録 ----
            var areaIds = new HashSet<string>();
            foreach (AreaSaveRecord area in snapshot.Areas)
            {
                if (area == null)
                {
                    errors.Add("Area 記録が null です。");
                    continue;
                }

                if (!areaIds.Add(area.AreaId))
                {
                    errors.Add("Area '" + area.AreaId + "' の記録が重複しています。");
                }

                if (!campaign.TryGetContent(new StableId(area.AreaId), out AreaContentIds content))
                {
                    errors.Add("未知のエリア '" + area.AreaId + "' の記録です。");
                    continue;
                }

                string where = "（" + area.AreaId + "）";
                CheckIdList(area.Investigated, "調査点" + where, id => content.InvestigationPoints.Contains(id.Value), errors);
                CheckIdList(area.OpenedFlags, "開通" + where, id => content.Flags.Contains(id.Value), errors);
                CheckIdList(area.ClearedEncounters, "クリア済み遭遇戦" + where, id => content.Encounters.Contains(id.Value), errors);
                CheckIdList(area.DefeatedBosses, "撃破済みボス" + where, id => content.Bosses.Contains(id.Value), errors);
                CheckIdList(area.PickedPlacements, "取得済み配置物" + where, id => content.Pickups.Contains(id.Value), errors);

                var placements = new HashSet<string>();
                foreach (KeyValuePair<string, int> defeat in area.FieldDefeats)
                {
                    if (!content.FieldPlacements.Contains(defeat.Key ?? string.Empty))
                    {
                        errors.Add("未知の普通敵配置 '" + defeat.Key + "'" + where + "。");
                    }

                    if (!placements.Add(defeat.Key ?? string.Empty))
                    {
                        errors.Add("普通敵配置 '" + defeat.Key + "' が重複しています" + where + "。");
                    }

                    if (defeat.Value < 0 || defeat.Value > snapshot.RespawnCycle)
                    {
                        errors.Add("普通敵配置 '" + defeat.Key + "' の周期 " + defeat.Value + " が範囲外です" + where + "。");
                    }
                }
            }

            // ---- 所持品・きびだんご ----
            var items = new HashSet<string>();
            foreach (KeyValuePair<string, int> item in snapshot.Inventory)
            {
                if (!campaign.TryGetItemCapacity(new StableId(item.Key), out int maxStack))
                {
                    errors.Add("未知のアイテム '" + item.Key + "'。");
                    continue;
                }

                if (!items.Add(item.Key))
                {
                    errors.Add("アイテム '" + item.Key + "' が重複しています。");
                }

                if (item.Value < 1 || item.Value > maxStack)
                {
                    errors.Add("アイテム '" + item.Key + "' の個数 " + item.Value + " が範囲外です（1〜" + maxStack + "）。");
                }
            }

            // ---- クエストの段階（P7 の接続口。受入 P6A 08）----
            var quests = new HashSet<string>();
            foreach (KeyValuePair<string, int> quest in snapshot.QuestStages)
            {
                var id = new StableId(quest.Key);
                if (!id.IsValid || !campaign.IsKnownQuest(id))
                {
                    errors.Add("未知のクエスト '" + quest.Key + "'。");
                    continue;
                }

                if (!quests.Add(quest.Key))
                {
                    errors.Add("クエスト '" + quest.Key + "' が重複しています。");
                }

                if (quest.Value < 0)
                {
                    errors.Add("クエスト '" + quest.Key + "' の段階が負です（" + quest.Value + "）。");
                }
            }

            int capacity = campaign.KibidangoBaseCapacity;
            if (snapshot.Kibidango < 0 || snapshot.Kibidango > capacity)
            {
                errors.Add("きびだんごの残数 " + snapshot.Kibidango + " が範囲外です（0〜" + capacity + "）。");
            }

            // ---- お地蔵様と復帰点 ----
            CheckIdList(snapshot.RegisteredShrines, "登録済みお地蔵様",
                id => campaign.TryGetShrine(id, out _), errors);

            var registered = new HashSet<string>(snapshot.RegisteredShrines);
            if (!campaign.TryGetShrine(new StableId(snapshot.Checkpoint), out _))
            {
                errors.Add("死亡用の再開地点 '" + snapshot.Checkpoint + "' を解決できません。");
            }
            else if (!registered.Contains(snapshot.Checkpoint))
            {
                errors.Add("死亡用の再開地点 '" + snapshot.Checkpoint + "' が登録済みではありません。");
            }

            ValidateResume(snapshot, catalog, campaign, registered, errors);

            // ---- Actor ----
            ValidateParty(snapshot.Party, errors);

            return errors.Count == before;
        }

        private static void ValidateResume(SaveSnapshot snapshot, AreaCatalog catalog, CampaignCatalog campaign,
            HashSet<string> registered, List<string> errors)
        {
            var areaId = new StableId(snapshot.ResumeAreaId);
            var pointId = new StableId(snapshot.ResumePointId);
            switch (snapshot.ResumeKind)
            {
                case ResumeAnchorKind.Entry:
                    if (!catalog.TryGetEntry(areaId, pointId, out _))
                    {
                        errors.Add("中断用の復帰位置（入口 " + snapshot.ResumeAreaId + "/" + snapshot.ResumePointId + "）を解決できません。");
                    }

                    break;

                case ResumeAnchorKind.Shrine:
                    if (!campaign.TryGetShrine(pointId, out ShrineInfo shrine))
                    {
                        errors.Add("中断用の復帰位置（お地蔵様 " + snapshot.ResumePointId + "）を解決できません。");
                    }
                    else if (!shrine.AreaId.Equals(areaId))
                    {
                        errors.Add("中断用の復帰位置のエリアがお地蔵様の所属と一致しません（"
                            + snapshot.ResumeAreaId + "／" + shrine.AreaId.Value + "）。");
                    }
                    else if (!registered.Contains(snapshot.ResumePointId))
                    {
                        errors.Add("中断用の復帰位置のお地蔵様 '" + snapshot.ResumePointId + "' が登録済みではありません。");
                    }

                    break;

                default:
                    errors.Add("中断用の復帰位置がありません。");
                    break;
            }
        }

        private static void ValidateParty(PartySaveValues party, List<string> errors)
        {
            PlayerSaveValues p = party.Player;
            if (p.Hp < 1)
            {
                errors.Add("主人公の HP " + p.Hp + " は通常保存として不正です（HP 0 の保存は作らない）。");
            }

            CheckRemaining(p.Stamina, "主人公のスタミナ", errors);
            CheckRemaining(p.StaminaRegenDelay, "主人公のスタミナ回復待ち", errors);
            CheckRemaining(p.InvincibleRemaining, "主人公の被弾後無敵", errors);

            if (!party.HasCompanion)
            {
                return;
            }

            CompanionSaveValues c = party.Companion;
            if (c.CompanionId.IsEmpty || !c.CompanionId.IsValid)
            {
                errors.Add("仲間の ID が不正です。");
            }

            if (c.Hp < 0)
            {
                errors.Add("仲間の HP が負です。");
            }

            if (c.IsDown != (c.Hp == 0))
            {
                errors.Add("仲間の Down と HP が矛盾しています（HP " + c.Hp + "／Down " + c.IsDown + "）。");
            }

            CheckRemaining(c.RecoveryRemaining, "仲間の復帰待ち", errors);
            CheckRemaining(c.InvincibleRemaining, "仲間の被弾後無敵", errors);
            CheckRemaining(c.AttackCooldown, "仲間の攻撃 CD", errors);
            CheckRemaining(c.GuardCooldown, "仲間の構え CD", errors);
            CheckRemaining(c.EvadeCooldown, "仲間の回避 CD", errors);
            CheckRemaining(c.GuardianCooldown, "仲間の守護 CD", errors);
        }

        private static void CheckRemaining(float value, string label, List<string> errors)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                errors.Add(label + " の値 " + value + " が不正です（有限かつ 0 以上）。");
            }
        }

        private static void CheckIdList(IReadOnlyList<string> ids, string label, Func<StableId, bool> known,
            List<string> errors)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                string value = ids[i];
                var id = new StableId(value);
                if (!id.IsValid)
                {
                    errors.Add(label + " の ID '" + value + "' の書式が不正です。");
                    continue;
                }

                if (!seen.Add(value))
                {
                    errors.Add(label + " の ID '" + value + "' が重複しています。");
                    continue;
                }

                if (known != null && !known(id))
                {
                    errors.Add("未知の" + label + " ID '" + value + "'。");
                }
            }
        }
    }
}

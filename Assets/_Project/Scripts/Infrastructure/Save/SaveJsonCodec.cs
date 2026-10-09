using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Save;
using Momotaro.Gameplay.Session;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Momotaro.Infrastructure.Save
{
    /// <summary>保存ファイルの外枠（Envelope）の値（P6A-02。仕様 §10）。</summary>
    public readonly struct SaveEnvelopeInfo
    {
        public SaveEnvelopeInfo(int schemaVersion, int contentVersion, string campaignId, string adventureId,
            long generation, string savedAtUtc, string checksum)
        {
            SchemaVersion = schemaVersion;
            ContentVersion = contentVersion;
            CampaignId = campaignId ?? string.Empty;
            AdventureId = adventureId ?? string.Empty;
            Generation = generation;
            SavedAtUtc = savedAtUtc ?? string.Empty;
            Checksum = checksum ?? string.Empty;
        }

        public int SchemaVersion { get; }
        public int ContentVersion { get; }
        public string CampaignId { get; }
        public string AdventureId { get; }

        /// <summary>単調な世代。<b>時刻で世代順を判定しない</b>（仕様 §10）。</summary>
        public long Generation { get; }

        /// <summary>保存時刻（表示用。順序判定には使わない）。</summary>
        public string SavedAtUtc { get; }

        /// <summary>payload の sha256（16 進小文字）。</summary>
        public string Checksum { get; }
    }

    /// <summary>
    /// 保存の JSON 化と<b>厳格な</b>読み取り（P6A-02。仕様 §10）。Newtonsoft の JObject を手で組み・手で読む。
    ///
    /// <b>なぜ自動の直列化を使わないか。</b> 仕様は「必須欠損と 0、重複プロパティを区別できること」を求めている。
    /// 既定の直列化は欠けた欄を既定値で埋め、重複は後勝ちで黙って通す。ここでは
    /// <list type="bullet">
    /// <item><description>重複プロパティ：<c>DuplicatePropertyNameHandling.Error</c> で読み取りごと失敗</description></item>
    /// <item><description>必須欠損・型違い・未知の欄：欄ごとに確かめて失敗</description></item>
    /// </list>
    /// を行う。書き手も同じ欄の並びで書くので、同じ Snapshot からは同じ文字列が出る（決定的）。
    ///
    /// <b>スレッド。</b> 純粋関数で、Unity の API を使わない。書込担当スレッドから呼んでよい。
    /// </summary>
    public static class SaveJsonCodec
    {
        private static readonly string[] EnvelopeKeys =
            { "schemaVersion", "contentVersion", "campaignId", "adventureId", "generation", "savedAtUtc", "checksum", "payload" };

        private static readonly string[] PayloadKeys =
        {
            "revision", "respawnCycle", "virtue", "grantedRewards", "growth", "visitedAreas", "recruited", "areas",
            "inventory", "kibidango", "shrines", "resume", "party", "questStages", "refund",
        };

        // 版 2：払い戻し（P6B）の欄が無い。
        private static readonly string[] PayloadKeysV2 =
        {
            "revision", "respawnCycle", "virtue", "grantedRewards", "growth", "visitedAreas", "recruited", "areas",
            "inventory", "kibidango", "shrines", "resume", "party", "questStages",
        };

        /// <summary>版 1 の payload の欄（questStages が無い）。読むときだけ使う。</summary>
        private static readonly string[] PayloadKeysV1 =
        {
            "revision", "respawnCycle", "virtue", "grantedRewards", "growth", "visitedAreas", "recruited", "areas",
            "inventory", "kibidango", "shrines", "resume", "party",
        };

        /// <summary>Envelope 付きの JSON 文字列を作る。</summary>
        public static string Serialize(SaveSnapshot snapshot, long generation, DateTime savedAtUtc)
        {
            JObject payload = BuildPayload(snapshot);
            string canonical = payload.ToString(Formatting.None);
            var envelope = new JObject
            {
                ["schemaVersion"] = SaveSnapshot.CurrentSchemaVersion,
                ["contentVersion"] = snapshot.ContentVersion,
                ["campaignId"] = snapshot.CampaignId,
                ["adventureId"] = snapshot.AdventureId,
                ["generation"] = generation,
                ["savedAtUtc"] = savedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                ["checksum"] = Sha256Hex(canonical),
                ["payload"] = payload,
            };
            return envelope.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Envelope だけを読む（世代比較用）。checksum も確かめる。失敗なら理由を返す。
        /// </summary>
        public static bool TryReadEnvelope(string json, out SaveEnvelopeInfo info, out JObject payload, out string error)
        {
            info = default;
            payload = null;
            JObject root;
            try
            {
                root = Parse(json);
            }
            catch (Exception e)
            {
                error = "JSON として読めません: " + e.Message;
                return false;
            }

            var r = new Reader(root, "envelope");
            r.ExpectExactly(EnvelopeKeys);
            int schema = r.Int("schemaVersion");
            int content = r.Int("contentVersion");
            string campaign = r.Str("campaignId");
            string adventure = r.Str("adventureId");
            long generation = r.Long("generation");
            string savedAt = r.Str("savedAtUtc");
            string checksum = r.Str("checksum");
            payload = r.Obj("payload");
            if (r.Failed)
            {
                error = r.Error;
                return false;
            }

            if (schema < SaveSnapshot.OldestReadableSchemaVersion || schema > SaveSnapshot.CurrentSchemaVersion)
            {
                error = "未対応の保存形式の版です（" + schema + "）。";
                return false;
            }

            if (generation < 1)
            {
                error = "世代が不正です（" + generation + "）。";
                return false;
            }

            string actual = Sha256Hex(payload.ToString(Formatting.None));
            if (!string.Equals(actual, checksum, StringComparison.Ordinal))
            {
                error = "checksum が一致しません（内容が壊れています）。";
                return false;
            }

            info = new SaveEnvelopeInfo(schema, content, campaign, adventure, generation, savedAt, checksum);
            error = null;
            return true;
        }

        /// <summary>
        /// 全体を読む。書式・checksum・Envelope と payload の整合まで見る（ID の解決は <see cref="SaveSnapshotValidator"/>）。
        /// </summary>
        public static bool TryDeserialize(string json, out SaveEnvelopeInfo info, out SaveSnapshot snapshot, out string error)
        {
            snapshot = null;
            if (!TryReadEnvelope(json, out info, out JObject payload, out error))
            {
                return false;
            }

            var r = new Reader(payload, "payload");

            // 版 1 は questStages を持たない（クエスト段階の接続口は版 2 から）。欠けた欄を黙って補わず、版で分ける。
            // 版 2 は払い戻しの欄を持たない（P6B）。どちらも版ごとの鍵の一覧で厳密に見る。
            bool v1 = info.SchemaVersion == 1;
            bool v2 = info.SchemaVersion == 2;
            r.ExpectExactly(v1 ? PayloadKeysV1 : (v2 ? PayloadKeysV2 : PayloadKeys));
            long revision = r.Long("revision");
            int cycle = r.Int("respawnCycle");

            Reader virtue = r.Child("virtue", "total", "spent");
            int total = virtue.Int("total");
            int spent = virtue.Int("spent");

            string[] granted = r.Strings("grantedRewards");
            KeyValuePair<string, int>[] growth = r.Pairs("growth", "id", "spent");
            string[] visited = r.Strings("visitedAreas");
            string[] recruited = r.Strings("recruited");
            AreaSaveRecord[] areas = ReadAreas(r);
            KeyValuePair<string, int>[] inventory = r.Pairs("inventory", "itemId", "count");
            int kibidango = r.Int("kibidango");

            Reader shrines = r.Child("shrines", "registered", "checkpoint");
            string[] registered = shrines.Strings("registered");
            string checkpoint = shrines.Str("checkpoint");

            Reader resume = r.Child("resume", "kind", "areaId", "pointId");
            string kindText = resume.Str("kind");
            string resumeArea = resume.Str("areaId");
            string resumePoint = resume.Str("pointId");

            PartySaveValues party = ReadParty(r);
            KeyValuePair<string, int>[] quests = v1
                ? new KeyValuePair<string, int>[0]
                : r.Pairs("questStages", "questId", "stage");

            bool hasRefund = !v1 && !v2;
            int refundRights = 0;
            string[] chapters = Array.Empty<string>();
            Reader refund = null;
            if (hasRefund)
            {
                refund = r.Child("refund", "rights", "chapters");
                refundRights = refund.Int("rights");
                chapters = refund.Strings("chapters");
            }

            if (r.Failed || virtue.Failed || shrines.Failed || resume.Failed || (refund != null && refund.Failed))
            {
                error = refund != null && refund.Failed && !(r.Failed || virtue.Failed || shrines.Failed || resume.Failed)
                    ? refund.Error
                    : FirstError(r, virtue, shrines, resume);
                return false;
            }

            if (!TryParseKind(kindText, out ResumeAnchorKind kind))
            {
                error = "resume.kind が不正です（" + kindText + "）。";
                return false;
            }

            snapshot = new SaveSnapshot(info.CampaignId, info.ContentVersion, info.AdventureId, revision, cycle,
                total, spent, granted, growth, visited, recruited, areas, inventory, kibidango,
                registered, checkpoint, kind, resumeArea, resumePoint, party, quests,
                hasRefund, refundRights, chapters);
            error = null;
            return true;
        }

        /// <summary>payload の sha256（16 進小文字）。</summary>
        public static string Sha256Hex(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        // ================================================================ 書き手

        private static JObject BuildPayload(SaveSnapshot s)
        {
            var areas = new JArray();
            foreach (AreaSaveRecord a in s.Areas)
            {
                var defeats = new JArray();
                foreach (KeyValuePair<string, int> d in a.FieldDefeats)
                {
                    defeats.Add(new JObject { ["placementId"] = d.Key, ["cycle"] = d.Value });
                }

                areas.Add(new JObject
                {
                    ["areaId"] = a.AreaId,
                    ["investigated"] = new JArray(a.Investigated),
                    ["openedFlags"] = new JArray(a.OpenedFlags),
                    ["clearedEncounters"] = new JArray(a.ClearedEncounters),
                    ["defeatedBosses"] = new JArray(a.DefeatedBosses),
                    ["pickedPlacements"] = new JArray(a.PickedPlacements),
                    ["fieldDefeats"] = defeats,
                });
            }

            PlayerSaveValues p = s.Party.Player;
            JToken companion = JValue.CreateNull();
            if (s.Party.HasCompanion)
            {
                CompanionSaveValues c = s.Party.Companion;
                companion = new JObject
                {
                    ["companionId"] = c.CompanionId.Value,
                    ["hp"] = c.Hp,
                    ["isDown"] = c.IsDown,
                    ["recoveryRemaining"] = (double)c.RecoveryRemaining,
                    ["invincibleRemaining"] = (double)c.InvincibleRemaining,
                    ["attackCooldown"] = (double)c.AttackCooldown,
                    ["guardCooldown"] = (double)c.GuardCooldown,
                    ["evadeCooldown"] = (double)c.EvadeCooldown,
                    ["guardianCooldown"] = (double)c.GuardianCooldown,
                };
            }

            return new JObject
            {
                ["revision"] = s.Revision,
                ["respawnCycle"] = s.RespawnCycle,
                ["virtue"] = new JObject { ["total"] = s.TotalVirtue, ["spent"] = s.SpentVirtue },
                ["grantedRewards"] = new JArray(s.GrantedRewards),
                ["growth"] = Pairs(s.Growth, "id", "spent"),
                ["visitedAreas"] = new JArray(s.VisitedAreas),
                ["recruited"] = new JArray(s.Recruited),
                ["areas"] = areas,
                ["inventory"] = Pairs(s.Inventory, "itemId", "count"),
                ["kibidango"] = s.Kibidango,
                ["shrines"] = new JObject
                {
                    ["registered"] = new JArray(s.RegisteredShrines),
                    ["checkpoint"] = s.Checkpoint,
                },
                ["resume"] = new JObject
                {
                    ["kind"] = s.ResumeKind.ToString(),
                    ["areaId"] = s.ResumeAreaId,
                    ["pointId"] = s.ResumePointId,
                },
                ["party"] = new JObject
                {
                    ["player"] = new JObject
                    {
                        ["hp"] = p.Hp,
                        ["stamina"] = (double)p.Stamina,
                        ["staminaRegenDelay"] = (double)p.StaminaRegenDelay,
                        ["invincibleRemaining"] = (double)p.InvincibleRemaining,
                    },
                    ["companion"] = companion,
                },
                ["questStages"] = Pairs(s.QuestStages, "questId", "stage"),
                ["refund"] = new JObject
                {
                    ["rights"] = s.RefundRights,
                    ["chapters"] = new JArray(s.ProcessedChapters),
                },
            };
        }

        private static JArray Pairs(IReadOnlyList<KeyValuePair<string, int>> pairs, string keyName, string valueName)
        {
            var array = new JArray();
            foreach (KeyValuePair<string, int> pair in pairs)
            {
                array.Add(new JObject { [keyName] = pair.Key, [valueName] = pair.Value });
            }

            return array;
        }

        // ================================================================ 読み手

        private static JObject Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new JsonReaderException("空のファイルです。");
            }

            var settings = new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                CommentHandling = CommentHandling.Ignore,
                LineInfoHandling = LineInfoHandling.Load,
            };

            using (var text = new System.IO.StringReader(json))
            using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
            {
                JToken token = JToken.ReadFrom(reader, settings);
                if (reader.Read())
                {
                    throw new JsonReaderException("JSON の後ろに余計な内容があります。");
                }

                if (!(token is JObject obj))
                {
                    throw new JsonReaderException("最上位がオブジェクトではありません。");
                }

                return obj;
            }
        }

        private static AreaSaveRecord[] ReadAreas(Reader r)
        {
            JArray array = r.Arr("areas");
            if (array == null)
            {
                return Array.Empty<AreaSaveRecord>();
            }

            var list = new List<AreaSaveRecord>();
            for (int i = 0; i < array.Count; i++)
            {
                if (!(array[i] is JObject o))
                {
                    r.Fail("areas[" + i + "] がオブジェクトではありません。");
                    continue;
                }

                var a = new Reader(o, "areas[" + i + "]");
                a.ExpectExactly(new[] { "areaId", "investigated", "openedFlags", "clearedEncounters", "defeatedBosses", "pickedPlacements", "fieldDefeats" });
                string areaId = a.Str("areaId");
                string[] investigated = a.Strings("investigated");
                string[] flags = a.Strings("openedFlags");
                string[] cleared = a.Strings("clearedEncounters");
                string[] bosses = a.Strings("defeatedBosses");
                string[] picked = a.Strings("pickedPlacements");
                KeyValuePair<string, int>[] defeats = a.Pairs("fieldDefeats", "placementId", "cycle");
                if (a.Failed)
                {
                    r.Fail(a.Error);
                    continue;
                }

                list.Add(new AreaSaveRecord(areaId, investigated, flags, cleared, bosses, picked, defeats));
            }

            return list.ToArray();
        }

        private static PartySaveValues ReadParty(Reader r)
        {
            Reader party = r.Child("party", "player", "companion");
            Reader player = party.Child("player", "hp", "stamina", "staminaRegenDelay", "invincibleRemaining");
            var p = new PlayerSaveValues(player.Int("hp"), player.Float("stamina"), player.Float("staminaRegenDelay"),
                player.Float("invincibleRemaining"));
            if (party.Failed || player.Failed)
            {
                r.Fail(FirstError(party, player));
                return default;
            }

            JToken companionToken = party.Raw("companion");
            if (companionToken == null || companionToken.Type == JTokenType.Null)
            {
                return new PartySaveValues(p, false, default);
            }

            Reader c = party.Child("companion", "companionId", "hp", "isDown", "recoveryRemaining", "invincibleRemaining",
                "attackCooldown", "guardCooldown", "evadeCooldown", "guardianCooldown");
            var companion = new CompanionSaveValues(new StableId(c.Str("companionId")), c.Int("hp"), c.Bool("isDown"),
                c.Float("recoveryRemaining"), c.Float("invincibleRemaining"), c.Float("attackCooldown"),
                c.Float("guardCooldown"), c.Float("evadeCooldown"), c.Float("guardianCooldown"));
            if (c.Failed)
            {
                r.Fail(c.Error);
                return default;
            }

            return new PartySaveValues(p, true, companion);
        }

        private static bool TryParseKind(string text, out ResumeAnchorKind kind)
        {
            switch (text)
            {
                case "Entry":
                    kind = ResumeAnchorKind.Entry;
                    return true;
                case "Shrine":
                    kind = ResumeAnchorKind.Shrine;
                    return true;
                default:
                    kind = ResumeAnchorKind.None;
                    return false;
            }
        }

        private static string FirstError(params Reader[] readers)
        {
            foreach (Reader r in readers)
            {
                if (r.Failed)
                {
                    return r.Error;
                }
            }

            return "不明な読み取りエラー。";
        }

        /// <summary>欄ごとに型と有無を確かめる読み手。最初の失敗を覚え、以後は既定値を返す。</summary>
        private sealed class Reader
        {
            private readonly JObject _obj;
            private readonly string _path;

            public Reader(JObject obj, string path)
            {
                _obj = obj;
                _path = path;
                if (obj == null)
                {
                    Fail(path + " がありません。");
                }
            }

            public bool Failed { get; private set; }

            public string Error { get; private set; }

            public void Fail(string message)
            {
                if (!Failed)
                {
                    Failed = true;
                    Error = message;
                }
            }

            /// <summary>欄の集合がちょうど一致すること（欠損・未知の欄を拒否）。</summary>
            public void ExpectExactly(IReadOnlyList<string> keys)
            {
                if (_obj == null)
                {
                    return;
                }

                var expected = new HashSet<string>(keys);
                foreach (JProperty prop in _obj.Properties())
                {
                    if (!expected.Contains(prop.Name))
                    {
                        Fail(_path + " に未知の欄 '" + prop.Name + "' があります。");
                    }
                }

                foreach (string key in keys)
                {
                    if (_obj.Property(key) == null)
                    {
                        Fail(_path + " に必須の欄 '" + key + "' がありません。");
                    }
                }
            }

            public JToken Raw(string key) => _obj?.Property(key)?.Value;

            private JToken Need(string key)
            {
                JToken token = Raw(key);
                if (token == null)
                {
                    Fail(_path + "." + key + " がありません。");
                }

                return token;
            }

            public int Int(string key)
            {
                long v = Long(key);
                if (v < int.MinValue || v > int.MaxValue)
                {
                    Fail(_path + "." + key + " が範囲外です。");
                    return 0;
                }

                return (int)v;
            }

            public long Long(string key)
            {
                JToken t = Need(key);
                if (t == null)
                {
                    return 0;
                }

                if (t.Type != JTokenType.Integer)
                {
                    Fail(_path + "." + key + " が整数ではありません（" + t.Type + "）。");
                    return 0;
                }

                return t.Value<long>();
            }

            public float Float(string key)
            {
                JToken t = Need(key);
                if (t == null)
                {
                    return 0f;
                }

                if (t.Type != JTokenType.Float && t.Type != JTokenType.Integer)
                {
                    Fail(_path + "." + key + " が数値ではありません（" + t.Type + "）。");
                    return 0f;
                }

                double d = t.Value<double>();
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    Fail(_path + "." + key + " が有限ではありません。");
                    return 0f;
                }

                return (float)d;
            }

            public bool Bool(string key)
            {
                JToken t = Need(key);
                if (t == null)
                {
                    return false;
                }

                if (t.Type != JTokenType.Boolean)
                {
                    Fail(_path + "." + key + " が真偽値ではありません。");
                    return false;
                }

                return t.Value<bool>();
            }

            public string Str(string key)
            {
                JToken t = Need(key);
                if (t == null)
                {
                    return string.Empty;
                }

                if (t.Type != JTokenType.String)
                {
                    Fail(_path + "." + key + " が文字列ではありません。");
                    return string.Empty;
                }

                return t.Value<string>() ?? string.Empty;
            }

            public JObject Obj(string key)
            {
                JToken t = Need(key);
                if (t == null)
                {
                    return null;
                }

                if (!(t is JObject o))
                {
                    Fail(_path + "." + key + " がオブジェクトではありません。");
                    return null;
                }

                return o;
            }

            public JArray Arr(string key)
            {
                JToken t = Need(key);
                if (t == null)
                {
                    return null;
                }

                if (!(t is JArray a))
                {
                    Fail(_path + "." + key + " が配列ではありません。");
                    return null;
                }

                return a;
            }

            public Reader Child(string key, params string[] keys)
            {
                JObject o = Obj(key);
                var child = new Reader(o, _path + "." + key);
                if (o == null)
                {
                    child.Fail(_path + "." + key + " がありません。");
                }
                else
                {
                    child.ExpectExactly(keys);
                }

                return child;
            }

            public string[] Strings(string key)
            {
                JArray a = Arr(key);
                if (a == null)
                {
                    return Array.Empty<string>();
                }

                var list = new string[a.Count];
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i].Type != JTokenType.String)
                    {
                        Fail(_path + "." + key + "[" + i + "] が文字列ではありません。");
                        return Array.Empty<string>();
                    }

                    list[i] = a[i].Value<string>();
                }

                return list;
            }

            public KeyValuePair<string, int>[] Pairs(string key, string keyName, string valueName)
            {
                JArray a = Arr(key);
                if (a == null)
                {
                    return Array.Empty<KeyValuePair<string, int>>();
                }

                var list = new KeyValuePair<string, int>[a.Count];
                for (int i = 0; i < a.Count; i++)
                {
                    if (!(a[i] is JObject o))
                    {
                        Fail(_path + "." + key + "[" + i + "] がオブジェクトではありません。");
                        return Array.Empty<KeyValuePair<string, int>>();
                    }

                    var item = new Reader(o, _path + "." + key + "[" + i + "]");
                    item.ExpectExactly(new[] { keyName, valueName });
                    string k = item.Str(keyName);
                    int v = item.Int(valueName);
                    if (item.Failed)
                    {
                        Fail(item.Error);
                        return Array.Empty<KeyValuePair<string, int>>();
                    }

                    list[i] = new KeyValuePair<string, int>(k, v);
                }

                return list;
            }
        }
    }
}

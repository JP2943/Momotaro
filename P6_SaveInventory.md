# P6 保存台帳（Save Inventory）

作成：2026-10-01（P6A-00）　正本仕様：`桃太郎プロジェクト P6先行実装仕様 v1.0 承認用.md`（以下「仕様」）§3・§8・§10
コアループ：`桃太郎プロジェクト コアループ定義 v1.0.md`

**保存してよいもの・いけないもの**を、正本・ID・初期値・更新契機・復元順・非初期値テストで 1 行ずつ固定する。
保存形式（DTO）はこの表から作り、表にない値を DTO へ足さない。足すときは先にこの表を更新する。

> **この表は凍結しない。** P6B（成長ツリー・きびだんご使用）と P7（クエスト・章）で行が増える。
> 増えたら行を足し、`P6ASaveRoundTripTests` に非初期値の往復を足す。

---

## 0. 原則（仕様 §3）

| 原則 | 内容 |
|---|---|
| 正本は Session | 可変の進行の正本は常駐の `GameSessionState`（とその配下）。DTO はもう一つの可変正本にしない |
| 流れは一方向 | Session → **不変 Snapshot**（メインスレッドで採取）→ DTO → JSON（書込担当スレッド）。逆向きは Load のときだけ |
| Actor 値は活動 Area から 1 回 | HP 等は**活動中の Area の Actor から Snapshot 境界で 1 回だけ**採る。Session 内にコピーを並行して持たない |
| 論理進行は全 Area | 門・調査・撃破・クリア等は**非活動・未ロードの Area の分も含めて**Session から全部書く（P5.5 引き継ぎ書 §4） |
| 位置は ID で | ワールド座標を書かない。復帰は AreaId ＋ 入口 ID（またはお地蔵様 ID）から `PlaceArrivals` で置き直す |
| 採取は非破壊 | 保存のための採取で攻撃中断・回復・付与・入力消費を起こさない（遷移用の `AreaActorTransferPort.Capture` は呼ばない） |

## 1. 保存する：Session（論理進行）

| 項目 | 正本 | ID | 初期値 | 更新契機 | 復元 | 非初期値テスト |
|---|---|---|---|---|---|---|
| 累計徳 | `PlayerProgressState.TotalVirtue` | — | 0 | 報酬付与 | 候補 Session 構築時 | RoundTrip |
| 使用済み徳 | `PlayerProgressState.SpentVirtue` | — | 0 | 成長購入の成功 | 同上（0 ≤ 使用済み ≤ 累計を検証） | RoundTrip／Validator |
| GrantOnce 付与記録 | `PlayerProgressState` の付与済み集合 | RewardData の StableId（**campaign の既知報酬**＝初到達報酬＋`AreaCatalogData.GrantOnceRewardIds`。2026-10-02 レビュー R4） | 空 | 初到達・初回クリア・発見の付与 | 同上。未知 ID は Load 拒否 | RoundTrip／Validator |
| 取得済み成長と実支出 | `PlayerProgressState` の成長記録 | SkillNodeData の StableId ＋ 支出額 | 空 | 成長購入の成功 | 同上。効果は**基礎値と取得 ID から再計算**（加算を繰り返さない） | RoundTrip／効果非累積 |
| 訪問済み Area | `GameSessionState` 訪問集合 | AreaId | 空 | 遷移 Commit（`NoteArrival`）／直開き | 同上。**Load では到着報酬を出さない** | RoundTrip |
| 加入済み仲間 | `GameSessionState` 加入集合 | CompanionId（**campaign の既知仲間**＝`AreaCatalogData.CompanionIds`。R4） | 空 | 加入（P8） | 同上。未知 ID は Load 拒否 | RoundTrip／Validator |
| クエストの段階（**P7 の接続口**） | `GameSessionState` のクエスト段階（ID → 0 以上の整数） | QuestId（campaign の既知クエスト＝`AreaCatalogData.QuestIds`。P6A は接続 fixture `quest_p6a_fixture` だけ） | 空（未設定は 0） | P7 の進行確定（`TrySetQuestStage`。保存要求つき）。**死亡・休息・周期では戻らない** | 同上。未知 ID・負数は Load 拒否 | RoundTrip／Validator／PlayMode（死亡を跨いで保持・保存） |
| 普通敵の復活周期 | `GameSessionState.RespawnCycle` | — | 0 | 休息・成長・死亡・旅立ちの成功（各 1 回） | 同上 | RoundTrip |
| 調査済み地点 | `AreaRuntimeState` の調査記録 | 調査点 StableId（Area ごと） | 空 | 調査完了 | 同上。到着時に Holder へ Bind | RoundTrip |
| 開通済みの仕掛け | `AreaRuntimeState` の FlagId 集合 | FlagId（Area ごと） | 空 | レバー・遭遇戦クリアの開通 | 同上。到着時に門へ `TryApplyOpened` | RoundTrip |
| クリア済み遭遇戦（恒久） | `AreaRuntimeState` の恒久クリア集合 | EncounterId | 空 | 遭遇戦の最終撃破（報酬・開通と同時） | 同上。到着時に `RestoreFromRecord` | RoundTrip |
| 撃破済みボス・中ボス（恒久） | `AreaRuntimeState` の恒久撃破集合 | ボス対象 StableId | 空 | 撃破確定 | 同上 | RoundTrip |
| 普通敵の撃破 | `AreaRuntimeState` の撃破記録（配置 ID → 周期） | 配置 ID（敵種 ID ではない） | 空 | 撃破確定（報酬と同時） | 同上。**現在周期と一致するものだけ**非出現 | RoundTrip／周期 |
| 取得済み配置物 | `AreaRuntimeState` の取得集合 | 配置物 StableId | 空 | 取得成功（所持数と同時） | 同上 | RoundTrip |
| 所持品 | `InventoryState` | ItemId → 個数 | 空 | 取得・消費（原子的） | 同上。未知 ID・負数・上限超過は拒否 | RoundTrip／Validator |
| きびだんご残数 | `RecoveryStockState.Current` | — | 上限 | 使用（P6B）・休息等で上限へ | 同上。上限は定義と成長から算出（保存しない） | RoundTrip |
| 登録済みお地蔵様 | `ShrineProgressState` の登録集合 | お地蔵様 StableId | campaign の初期お地蔵様 1 件 | 調べる・旅立ち到着 | 同上 | RoundTrip |
| 死亡用再開地点（Checkpoint） | `ShrineProgressState.Checkpoint` | お地蔵様 StableId | campaign の初期お地蔵様 | 調べる・旅立ち到着 | 同上。catalog で解決できなければ Load 拒否 | RoundTrip／Validator |
| 中断用復帰位置（ResumeAnchor） | `GameSessionState.Resume` | 種別（入口／お地蔵様）＋ AreaId ＋ 点 ID | campaign の初期お地蔵様 | 通常到着・お地蔵様操作・死亡復帰・旅立ち到着 | Continue の行き先 | RoundTrip／Validator |
| 冒険 ID | `GameSessionState.AdventureId` | GUID 文字列 | New Game で発行 | New Game のみ | Envelope と一致を検証 | Validator |
| 世界の版 | `GameSessionState.Revision` | — | 0 | 上記の変更すべて | 保存済み版の比較にだけ使う | 書込担当テスト |

## 2. 保存する：Actor（活動 Area から 1 回だけ採取）

P5 の `P5_ActorTransferInventory.md` で「保持」に分類した値を土台にし、**中断用の投影**を掛ける（仕様 §8）。

| 項目 | 採取元 | 保存値 | 投影 | 復元 |
|---|---|---|---|---|
| 主人公 HP | `PlayerVitalsHolder.Vitals.Health.Current` | 現在値 | そのまま。**HP 0 の通常保存は作らない**（死亡解決を優先） | 到着時に値で置く。最大値は Data ＋ 成長から再計算 |
| 主人公スタミナ | 同 `Stamina` | 現在値・回復待ち残り | Break 中なら Break を 0（中断の投影）。現在値はそのまま | 同上 |
| 主人公の被弾後無敵 | `PlayerHitReaction` | 残り秒 | Hurt 硬直は 0（一時動作） | 同上 |
| 犬丸 HP・Down・復帰残り | `CompanionHitReceiver.Vitals` | そのまま | ひるみ（蓄積・各残り）は 0（一時動作） | 同上。Down なら Down のまま |
| 犬丸の攻撃・構え・回避・守護 CD | 各 Controller の Export（**非破壊**） | 残り秒 | 攻撃 Plan・構え中・回避中は保存しない | 同上。オフライン時間は減算しない |
| 犬丸の被弾後無敵 | 同上 | 残り秒 | — | 同上 |

採取 API は `AreaActorTransferPort.ExportForSave()`（P6A-02 で追加）。**`Capture` は呼ばない**——
`Capture` は `CancelAttack`／`Release`／`Interrupt`／`ResetArbitration` を行い、攻撃を止める（仕様 §8 末尾）。

## 3. 保存しない（実行時の事情）

| 項目 | 理由 |
|---|---|
| Scene handle・`AreaInstanceHandle`・在留台帳 | ロードごとに変わる。Load は在留を一から作る |
| Unity オブジェクト参照・InstanceId・`DamageableId` | プロセスごとに変わる |
| 主人公・犬丸のワールド座標・移動経路 | 地形を作り直した日に壁の中へ復帰しない（入口 ID から置き直す） |
| カメラ位置・スライドの進行度・表示代理・待機表示の通算・前面判定 | 演出の途中状態 |
| 攻撃判定・飛び道具・入力ラッチ・通知キュー | 一時動作 |
| 挑戦途中の遭遇戦（Wave・残敵 HP・撃破個体・挑戦 ID） | 中断・撤退・敗北後は最初の Wave から（コアループ） |
| 中ボス・章ボスの途中戦闘 | 撃破確定までは記録しない |
| 遷移の持ち越し（`AreaTransferSnapshot`） | 遷移のための値。保存は Commit 後に Session ＋ 活動 Area から採る |
| `ReleaseFailed` 等の Scene 操作の失敗状態 | Scene 操作の事情。Load 後は台帳が空から始まる |
| 死亡再開の段階（`CampaignRespawnCoordinator`） | 死亡確定フレームは通常保存を作らない（仕様 §8）。復帰成功後に保存 |

## 4. 復元順（Continue。仕様 §10）

1. Envelope 検証（版・checksum・campaign・冒険 ID・世代）→ 2. DTO 検証（catalog で全 ID を解決）→
3. 候補 Session を構築（まだ提供点へ差さない）→ 4. 候補を Load 用に差し、ResumeAnchor の Area を Single で準備 →
5. 最大値計算（Data ＋ 成長）→ 6. Actor 資源の復元 → 7. 安全位置へ配置 → 8. 門・撃破・調査・Trigger 同期 →
9. 活動許可（所有者＝遷移サービス）→ 10. 候補を採用。失敗したら候補を捨て、提供点を元へ戻し、ファイルは変更しない。

Load では回復・周期更新・到着報酬・獲得演出を**発生させない**。

## 5. DTO 対応（schemaVersion 2）

| DTO の欄 | 表の行 |
|---|---|
| `virtue.total`／`virtue.spent` | 累計徳／使用済み徳 |
| `grantedRewards[]` | GrantOnce 付与記録 |
| `growth[] {id, spent}` | 取得済み成長と実支出 |
| `visitedAreas[]`／`recruited[]` | 訪問済み Area／加入済み仲間 |
| `respawnCycle` | 普通敵の復活周期 |
| `areas[] {areaId, investigated[], openedFlags[], clearedEncounters[], defeatedBosses[], pickedPlacements[], fieldDefeats[] {placementId, cycle}}` | Area ごとの論理進行 |
| `inventory[] {itemId, count}`／`kibidango` | 所持品／きびだんご残数 |
| `shrines.registered[]`／`shrines.checkpoint` | 登録済みお地蔵様／Checkpoint |
| `resume {kind, areaId, pointId}` | ResumeAnchor |
| `party.player {...}`／`party.companion {...}` | §2 |
| `questStages[] {questId, stage}` | クエストの段階（版 2 で追加） |
| Envelope `schemaVersion`／`contentVersion`／`campaignId`／`adventureId`／`generation`／`savedAtUtc`／`checksum` | 仕様 §10 |

**版 1 → 2 の読み替え**（2026-10-02、受入 P6A 08 の接続 fixture）：版 1 は `questStages` を持たない。読むときは欄の一覧を版で分け
（版 1 に `questStages` があれば未知の欄として拒否）、**クエスト段階が空**として候補 Session を作る。書くのは常に版 2。
版 3 以上・0 以下は「未対応の保存形式の版」で拒否する。それより前の形式は無い（P5 までは保存なし。記録 001 で確認）。

**保存先の退避（New Game）**：スロットの両側を `archive/<日時>` へ**写して読み直して一致を確かめてから**、古い世代 → 最新の世代の順にスロットから消す。
最新を消せずに止まったら、消した古い側を写しから戻す（戻せなくても最新の側は残る）。どの段の失敗でも New Game は始めない（2026-10-02 レビュー R2）。

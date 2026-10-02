# P6A 統合受入結果

仕様の正本は `桃太郎プロジェクト P6先行実装仕様 v1.0 承認用.md`（2026-10-01 オーナー承認）と
`桃太郎プロジェクト コアループ定義 v1.0.md`。旧 P6 詳細仕様だけを正本にしない（仕様 §15）。
保存項目の台帳は `P6_SaveInventory.md`。

親ブランチ：`main`／**親 SHA `8e7d3c9`**（P5／P5.5 受入完了。記録 061 の commit。`phase/5.5-area-slide` の先端と同一）。
作業ブランチ：`phase/6-progression-save`（`8e7d3c9` から分岐済み）。

---

## 記録 001：P6A-00（基点・棚卸し・再利用先の確認）

### 1. 基点

| 確認 | 結果 |
|---|---|
| 仕様が挙げた受入参照点 `fbb4ba4` | `8e7d3c9` の祖先。**15f（記録 060）と記録 061 を含まない**ので基点にしない。仕様 §1「受入済み変更を含む最新基点を選んで親 SHA を記録」に従う |
| `main` | `8e7d3c9`。P5.5 は統合済み |
| 作業ブランチ | `phase/6-progression-save` が `8e7d3c9` を指す |
| 作業ツリー | コード・アセット定義のパス（Scripts／Tests／Data／Prefabs／Scenes／ProjectSettings）に差分なし。PNG 等の `M` は PC 側シェルに git-lfs が無いことによる見かけ（CLAUDE.md） |
| ブリッジ | 応答あり（Unity 6000.3.20f1） |

### 2. 既存実装の棚卸し（再利用先）

**新しい第二系統は作らない。** 下表の既存所有者を拡張する。

| P6A の要素 | 既存の所有者 | 扱い |
|---|---|---|
| 進行の正本 | `GameSessionState`（常駐 `GameSessionBootService` が 1 個だけ所有） | 拡張（お地蔵様・所持品・きびだんご・復帰位置・版） |
| 徳 | `PlayerProgressState`（`Virtue` と GrantOnce 集合） | 累計・使用済み・成長記録へ拡張。`Virtue` は「使用可能」の別名として残す（既存 HUD・テストの互換） |
| Area ごとの記録 | `AreaRuntimeState`（調査・開通・Encounter クリア〔周期つき〕） | 恒久クリア・ボス撃破・配置物・普通敵撃破を足す |
| 遭遇戦のクリア規則 | `GameSessionState.AdvanceRespawnCycle` が**通常 Encounter のクリア記録を初期化**（P5 §9.1） | **campaign ごとの規則**にする（下記 §3-1） |
| 死亡再開 | `CampaignRespawnCoordinator`（要求 ID につき周期 1 回）＋ `CampaignRespawnRequestProcedure` ＋ `AreaTransitionService.TryRespawnTravel`（Single／Actor 値を運ばない） | 行き先を「カタログ固定」から「Checkpoint のお地蔵様」へ。到着の全回復は既存 `RestoreForCampaignRespawn` |
| 遠隔移動（旅立ち） | なし | 死亡再開と同じ Single 経路（`TravelRoutine`）に「旅立ち」種別を足す。Camera・SceneFlow の第二系統は作らない |
| 3 エリア目の準備 | `AreaPreloader.Request`：別候補なら**解放してから**読む（P5.5 §5） | 既存の契約で「無関係な非活動 Area を先に解放」が成立している。P6A-04 は検証を足す |
| Actor 値の採取 | `AreaActorTransferPort.Capture`（**行動を止めてから採る**） | 保存には使わない。非破壊の `ExportForSave` を足す |
| 撃破報酬 | `CombatRewardCollector` ← `CombatSessionController.EnemyDefeated`（DamageableId で初回だけ） | 遭遇戦の個別撃破はこのまま（敵の RewardData は `GrantOnce=0`。挑戦ごとに新しい個体なので再挑戦で再獲得できる） |
| 普通敵（遭遇戦の外） | **なし**（P5／P5.5 の敵はすべて遭遇戦が生成） | 新設：配置 ID つきの野外配置と、その Area の生成・撃破記録 |
| 成長資産 | `SkillNodeData`（費用・階層・前提・排他） | 再利用。検証用の効果欄（最大 HP 加算）を足す |
| JSON | 既存の自前保存は無い（`PlayerPrefs` は入力の再割当てだけ）。`com.unity.nuget.newtonsoft-json 3.2.1` が依存として解決済み | Newtonsoft を使う（重複プロパティ・必須欠損を区別できる）。`manifest.json` へ同じ版で明示する |
| 旧保存形式 | **無い** | 旧版移行は作らない（仕様 §10「架空の旧版移行を作らない」） |

### 3. 設計上の判断（P6A-00 で固定）

1. **遭遇戦のクリア規則は campaign の Data で選ぶ。** P5 は「死亡で通常 Encounter が復活」を受入要求（P5-E21 ほか）にしている。
   P6 のコアループは「クリア済み遭遇戦は恒久」。`AreaCatalogData` に規則（`PerRespawnCycle`＝P5 既定／`Permanent`＝P6）を持たせ、
   **既存の P5／P5.5 Data は P5 のまま**、P6 専用 campaign だけ `Permanent` にする（仕様 §2「既存試遊 Data 非汚染」）。
2. **普通敵の周期と恒久記録を別の集合に置く。** 周期の更新は「普通敵の撃破記録」にだけ効き、
   恒久クリア・ボス・配置物・開通・調査・訪問・徳・成長には触れない（P6A-01）。
3. **世界の版（Revision）を Session が持つ。** 論理進行を変える操作はすべて版を進める。
   保存は「どの版を書いたか」を持ち、古い版の完了で新しい dirty を消さない（仕様 §9）。
4. **保存要求は Session への通知。** 報酬・撃破・クリア・登録・休息など確定点で `RequestAutosave(理由)` を呼ぶ。
   書込担当（Infrastructure）はそれを集約し、メインスレッドで Snapshot を採ってから別スレッドで書く。
5. **お地蔵様は入口と同じ「点」で解決する。** 各お地蔵様は所属 Area の入口（`AreaEntryDefinition`）を 1 つ持ち、
   死亡再開・旅立ち・Continue はすべて既存の (AreaId, EntryId) 経路で着く。名前だけの空 EntryId で代用しない。

---

## 記録 002：P6A-01（恒久進行と普通敵周期の分離、初回報酬と撃破の原子性）

### 1. 変えたもの

| 対象 | 変更 |
|---|---|
| `PlayerProgressState` | 累計・使用済み・成長記録（ID → 実支出）。`Virtue` は使用可能の別名。`TryPurchaseGrowth` は不成立で全体無変更。`Changed` 通知 |
| `PlayerProgressHolder` | State の `Changed` を中継（Holder を経由しない付与・支出でも HUD が追従）。自分の操作中は重ねない |
| `SessionChangeLog`（新） | 世界の版・保存要求・まとめ（まとめの間は要求を溜め、最後に 1 件） |
| `AreaRuntimeState` | 恒久クリア／ボス撃破／配置物／普通敵撃破（配置 ID → 周期）。調査・開通は版を進めて保存を要求 |
| `GameSessionState` | campaign の規則、`CommitArrival`／`CommitEncounterClear`／`TryRecordFieldDefeat`／`TryRecordBossDefeat`／`TryPickPlacement`／`GrantDiscovery`。周期更新は普通敵（と P5 規則の遭遇戦）だけ |
| `InventoryState`（新） | 所持数。上限超過・不足・未知・負数は全体拒否 |
| `AreaFieldEnemyDirector`（新） | 普通敵の生成（現在周期で未撃破の配置だけ、準備では起こさない）と撃破記録（配置 ID） |
| `AreaEncounterRunner` | 最終撃破で `CommitEncounterClear`（記録・初回ボーナス・開通を 1 回で）。ボス指定は恒久規則の campaign だけ許し、ボス撃破も記録 |
| `AreaTransitionService.NoteArrival` | P6 campaign では訪問と初到達報酬を同時に確定（`ArrivalCommitted` 通知）。P5／P5.5 は従来どおり |
| Data | `EncounterClearPolicy`、`AreaCatalogData` の campaign 欄（お地蔵様・初期お地蔵様・きびだんご基本上限・アイテム・成長項目・内容版）、`AreaDefinition.ArrivalReward`、`EncounterData.ClearReward／UnlockFlagId`、`SkillNodeData.MaxHpBonus`、`ShrineDefinition`／`ItemDefinition`。`CampaignCatalog`（不変 Snapshot） |

**既存 Data は 1 件も書き換えていない。** 足した欄は既定値で P5 と同じ挙動になる（規則は `PerRespawnCycle`、報酬は未設定）。

### 2. テスト

`P6AProgressionTests`（EditMode 15 件）。遭遇戦は本物の `AreaEncounterRunner`＋`CombatSessionController`＋`CombatRewardCollector`、
普通敵は本物の `AreaFieldEnemyDirector` と `EnemyActor` の撃破チャネルで繋いだ。

| 実行 | 結果 |
|---|---|
| `p6a01b_t`（P6AProgressionTests） | 15／15 |
| `p6a01_e2`（EditMode 全件） | **1835／1835**（1820 ＋ 新規 15）。83.0 秒 |

### 3. 途中で見つかった環境の破損（コード変更とは無関係）

最初の全件実行（`p6a01_e1`）で 13 件が落ちた。原因は **NavMesh の資産がバイナリなのに `.gitattributes` の `*.asset text` に当たっていた**こと。
main 統合の checkout で Windows の改行変換（LF→CRLF）が掛かり、作業ツリーの `NavMesh_P55*.asset` が壊れていた
（"Invalid serialized file header"。作業ツリーの CR 17 個／blob 4 個）。**blob そのものは正しい**——
`build-phase55-world`／`build-exploration-trial` で焼き直すと blob とバイト一致した。

対処：`.gitattributes` の末尾に `NavMesh_*.asset binary` を足した（次の checkout から壊れない）。
**オーナーへ：** このファイルの変更はコミット対象。他の `.asset` はすべて YAML で、バイナリは NavMesh の 6 件だけ。

### 4. 次

P6A-02（非破壊 Snapshot、DTO 検証、二世代ストレージ、候補 Load）へ進む。

---

## 記録 003：P6A-02（保存）・P6A-03（お地蔵様）・P6A-04 の論理部分

### 1. 保存（P6A-02）

| 部品 | 層 | 役割 |
|---|---|---|
| `SaveSnapshot`／`AreaSaveRecord`／`PartySaveValues` | Gameplay/Save | 不変 Snapshot。集合は序数順（同じ世界から同じ JSON） |
| `SaveSnapshotValidator` | Gameplay/Save | campaign カタログだけで全 ID を解決（未知・重複・負数・NaN・範囲・所有 Area 不一致・成長の前提と排他・徳の会計・HP 0・無効復帰点） |
| `SessionRestorer` | Gameplay/Save | 検証済みの保存から候補 Session を作る（通知・版・保存要求を出さない） |
| `AreaContentManifest` | Data | Area に置いた保存対象の ID 一覧（Builder が Scene と同時に書く。Scene を読まずに検証するため） |
| `SaveJsonCodec` | Infrastructure/Save | Newtonsoft の JObject を手で組み・手で読む。重複プロパティ・必須欠損・未知の欄・型違い・checksum 不一致・余計な後続を拒否 |
| `SaveFileStore` | Infrastructure/Save | 1 スロット二世代。一時ファイル → flush → 読み直し検証 → 置換 → 再検証。片側破損は復旧して通知、両側破損・別冒険混在・同一世代競合は Load 不可。排他ロック。New Game 前の退避 |
| `SaveCoordinator` | Infrastructure/Save | 要求の集約（書込中の要求は完了後に最新で）、版（古い完了で dirty を消さない）、epoch（前の Session の完了を捨てる）、失敗の扱い（dirty 維持・自動再試行なし・再試行は最新から） |
| `ThreadSaveExecutor`／`ManualSaveExecutor` | Infrastructure/Save | 単一の書込担当スレッド／テスト用の手回し |
| `CampaignSaveService` | Infrastructure/Save | 常駐ホスト。LateUpdate で採取、遷移中・死亡中・GameOver・Loading は採らない。終了要求（wantsToQuit）とゲーム内の終了・タイトル復帰で保存完了を待つ。失敗時は「もう一度／ゲームへ戻る／保存せずに終了」 |
| `CampaignAdventureFlow` | Infrastructure/Save | New Game（退避→新 Session→初期お地蔵様）と Continue（検証→候補→`TryLoadTravel`→採用／破棄） |
| `AreaActorTransferPort.ExportForSave`／`TryApplySaveValues` | Gameplay | 非破壊の採取と、遷移の復元と同じ検証付き窓口での適用 |
| `AreaTransitionService.TryLoadTravel` | Infrastructure | 保存からの再開（Actor 値を運ばない・失敗は復旧へ流さず終端失敗）。到着を進行として確定しない |

`com.unity.nuget.newtonsoft-json 3.2.1` を `Packages/manifest.json` に明示した（同じ版が依存として解決済みだった。`packages-lock.json` は Unity が更新する）。

### 2. お地蔵様・休息・死亡再開（P6A-03）

- `ShrinePoint`（Interact）→ 常駐 `CampaignShrineService`（`IShrineOperations`）。調べると登録・死亡地点・中断位置を更新して保存、メニューを開く（GameMode を Paused にして入力を UI へ）。
- `ShrineProcedures.Rest`：周期 1 回・全回復（死亡再開と同じ中身）・きびだんご補充・活動 Area の普通敵の作り直し・登録、保存要求 1 件。**休息・成長・旅立ちはこれを 1 回だけ使う。**
- `ShrineProcedures.PurchaseGrowth`：不成立は全体無変更で休息しない。成立したら支出 → 効果の置き直し（休息より前）→ 休息。
- 最大 HP は `PlayerVitalsHolder.ApplyMaxHpBonus`（基礎値 ＋ 加算で置き直し。何度呼んでも同じ）。入場のたびに `AreaInitializer` が値の復元より前に呼ぶ。
- 死亡再開点は `CampaignRespawnPoint`（P6 は Checkpoint のお地蔵様、P5 は固定点）。復帰の完了で補充・中断位置（そのお地蔵様）・保存。周期は既存どおり要求 ID につき 1 回。
- 通常到着は中断位置を到着した入口へ（Checkpoint は変えない）。

### 3. 撤退・複数遭遇戦・旅立ち（P6A-04 の論理）

- 遭遇戦に `AllowRetreat`。撤退できる戦闘中は、遷移の受付にとって探索中と同じ（`AreaTransitionConditionsSource`）。アリーナは `SealsExits=false` で出口を塞がない。
- **撤退の確定は再入場の準備**（`AbandonChallenge`）：生成物を登録解除してから破棄（撃破として報酬化しない）、戦闘セッションを Preparing へ。クリア済みは戻さない。移動が失敗したら出発側の挑戦が続く。
- `AreaEncounterGroup`：1 エリアに複数の遭遇戦（独立 ID・独立の戦闘セッション）。受付条件・Interact・仲間の活動 Context に同じ契約で見せる。
- 旅立ち：`AreaTransitionService.TryFastTravel`（既存の Single／Fade 経路）。`FastTravelCompleted` の後に休息・登録・保存。失敗（復旧・終端）では何もしない。

### 4. テスト

| 実行 | 結果 |
|---|---|
| `p6a03c_t`（EditMode `P6A`） | 43／43（Progression 15・Save 18・Shrine 10） |
| `p6a03_e1`→`p6a03d_t` | EditMode 全件 1863 中 1862 → 落ちた 1 件は P5 の持ち越し台帳（E28）が新しい欄 `PlayerVitalsHolder.MaxHpBonus` を検出したもの。台帳に「再構築」で追記して通過 |
| `p6a03_p1`／`p2`／`p3` | PlayMode P5.5：64／13／72（回帰なし） |
| `p6a03_p4`＋`p4b` | PlayMode その他：113＋3。**分割 4 の名前一覧を誤った**（ファイル名で書いて `CompanionActivityStopPlayTests` を落とし、テストの無い `SamplePlayModeTest` を入れた）。P5.5 の一覧（26 クラス）に戻して補った |

**欠陥注入の確認**（CLAUDE.md）：`SaveCoordinator` の保存済み版を「完了時に Session の最新版」へ書き換えると、
書込中の要求・失敗後の再試行の 2 件が落ちることを確かめてから戻した。

### 5. 次

P6A 専用の 3 エリア（A・B・C）の Builder と Validator。PlayMode の受入（非破壊採取・Continue・旅立ち・撤退・3 エリア往復）はこの世界で書く。

---

## 記録 004：P6A-04〜06（検証 campaign・実 Scene の受入・実ビルド・性能・攻撃 VFX）

### 1. 検証 campaign（P6A-06。仕様 §11）

| 部品 | 内容 |
|---|---|
| `Editor/Phase6/Phase6WorldBuilder` | A・B は `Phase5ExplorationBuilder.Build(targets)` に設定と拡張（`Phase5AreaExtension`）を渡して作る（地形生成器を複製しない）。C は同じ部品で組む。A：初期お地蔵様・普通敵 2 体。B：独立した遭遇戦 2 つ（撤退可）・北のクリアで開く小部屋（発見）・強壮薬の配置物。C：お地蔵様・調査地点・仮ボス（既存の精鋭敵を Data で）。A–B–C は東西スライド、タイトル（New Game／Continue）。Build Settings に 4 Scene |
| `Editor/Phase6/Phase6WorldValidator` | A–B・B–C を P5.5 の配置検査へそのまま通す（境界・通路・並び・カメラ軸・覆い・接続 Data。P5 の Scene 検査も内側で走る）＋ campaign（カタログが実行時と同じ手順で組める・Permanent 方針・お地蔵様の像／入口／戦闘区域外／受付の錨の遮蔽・保存対象一覧と Scene の一致・保存 ID の一意性・戦闘 VFX・タイトルのカタログ参照） |
| ブリッジ op | `build-phase6-world`／`validate-phase6-world`／`p6a-player-smoke`（下記 §4） |
| `Phase5BuildTargets` の拡張 | `IncludeLegacyEncounter`・`AreaBExtraSeam`・`ExtraEntriesA/B`・`ExtendA/B`・`IncludeCombatVfx`。**既定値は P5／P5.5 の出力を変えない** |

**P5／P5.5 の Builder 出力が変わらないこと**：`build-exploration-trial`／`build-phase55-world` で作り直し、HEAD と
fileID を伏せて行単位で比べた。差は新しい直列化項目の既定値（`_allowRetreat: 0`・`_sealsExits: 1`・`_fieldEnemies`／
`_encounterGroup`／`_areaEncounterGroup`／`_rewardOverride` の空参照・遭遇戦の `_areaRoot`）だけ。`validate-exploration-trial`／
`validate-phase55-world` は合格。

### 2. 実 Scene の受入テスト（`P6AWorldPlayTests`、PlayMode 9 本）

タイトルの手順（`CampaignAdventureFlow`）で New Game／Continue し、保存は一時ディレクトリへ実スレッドで書き、
「再起動」は常駐と static を捨てて作り直す。敵は実 Hitbox、お地蔵様は実キー E、境界は実キーで渡る。

| テスト | 見ているもの（要求） |
|---|---|
| `NewGame_Progress_Restart_Continue_RestoresEverything` | 初到達 1 回・普通敵の個別報酬と非復活・調べるだけで回復しない・犬丸 Down と残時間・再起動後の Continue で全部戻る（01／02／07／09／13／16） |
| `Encounter_SaveMidFight_Continue_Retreat_Rechallenge_ClearCommitsAtomically` | 戦闘中の撃破が保存され、Continue は安全な入口・未クリア。撤退で徳保持・再挑戦は最初から・クリアで撃破／ボーナス／開通が同じ世代に載る（04／05／15） |
| `ABC_FastTravel_Death_EachAdvancesCycleOnce` | A–B–C で Scene・在留 2 以下、旅立ち・死亡で周期 1 回ずつ、未ロードの A の普通敵が戻る（03／08／11／12） |
| `Capture_DuringAttack_DoesNotDisturbThePlayer` | 攻撃中の採取・保存で攻撃・HP・スタミナが変わらない（14） |
| `FailedTravelAndFastTravel_CommitNothing` | 読込失敗の移動で初到達なし、旅立ち失敗で休息・登録・保存なし（01／11／12） |
| `AttackVfx_ShowsInA_AndAfterSlideInB` | 実キー J の攻撃で剣閃が画面内に出て遮られない。スライド後の B でも出る（27） |
| `ReturnToTitle_FromMenu_SavesLatestThenLoadsTitle` | Esc→T で、保存契機の無い最新値（HP）まで保存してからタイトルへ（22） |
| `ReturnToTitle_SaveFails_OffersChoices_RetryWritesLatest` | 保存失敗で成功表示をせず選択肢、前の世代が残り、再試行は最新（19／22） |
| `ReturnToTitle_WhileDead_ResolvesRespawnBeforeSaving` | 同じフレームの致死と報酬のあと終了を求めても、HP 0 の通常保存を作らず、再開してから報酬込みで保存（17） |

### 3. 実 Scene のテストで見つけて直した欠陥

部品の単体テストはすべて緑だった。**繋いだら動かなかった**ものを、ここで 8 件拾った。

| # | 欠陥 | 原因 | 修正 | 再発防止 |
|---|---|---|---|---|
| 1 | 普通敵に攻撃が一切当たらない | 生成先が AreaRoot の下で、主人公の命中判定が「同じ根の相手」を自分として除外する | 生成根を Scene 直下へ（親を外しても Scene は変わらない。活動ゲートの開閉は配置役の OnEnable／OnDisable で合わせる） | 実 Hitbox で倒す PlayMode |
| 2 | お地蔵様を調べられない | 受付の錨が像の Collider の中。遮蔽判定（Default 層＝壁と同じ層）に像そのものが当たる | 錨を像の手前 0.7m へ | Validator：錨の近くに固体が無いこと（旧 Scene で落ちることを確認） |
| 3 | 遭遇戦が未配線のまま保存された | Builder が Data 参照を Scene の開き直しをまたいで持ち回り、破棄済みになっていた | 使うたびにパスから読み直す | Validator（調停の配線） |
| 4 | タイトルのカタログが null（実ビルドで New Game 不可） | 同上 | 同上 | Validator：タイトルのカタログ参照（旧 Scene で落ちることを確認） |
| 5 | 同じ物体に `EncounterFeedbackBinder` を 2 つ足して例外 | `DisallowMultipleComponent` | 遭遇戦ごとの物体へ | 生成の例外を Builder が報告（スタックの先頭を添えるようにした） |
| 6 | 通常のタイトル復帰ができない | 既存の退避は「終端失敗」が前提 | `AreaTransitionService.TryBeginReturnToTitle`、ゲーム内メニュー（Esc→T タイトルへ／Q 終了） | PlayMode |
| 7 | 失敗した終了前の保存が「保存済み」に見える | 書込担当が手を離してから完了を取り込むまでの間、`IsWriting` が false。版の変わらない保存では dirty も false | 完了を取り込むまで書込中 | EditMode `Coordinator_UntakenCompletion_IsStillWriting`＋PlayMode（旧コードで落ちることを確認） |
| 8 | 実ビルドで保存が「置換されるファイルを削除できません」で失敗 | Windows で置換先を他プロセス（ウイルス対策・インデクサ）が一瞬開いている | 置換・移動は書込担当の中で最大 5 回やり直す（20→160ms。合計 0.3 秒）。尽きたら従来どおり失敗（前の世代は残る） | EditMode `TransientIo_RetriesSharingViolations_ThenGivesUp`、実ビルド計測を 3 回連続で通過 |

### 4. 実ビルド（P6A 23）

ブリッジ op `p6a-player-smoke`：Windows 64bit の試遊ビルド（Mono・Development なし）を `Builds/P6A` に作り、
<b>別プロセス</b>で起動して確かめる（ビルド側 `Phase6SmokeDriver` がコマンドライン引数でだけ動く。保存先は `_bridge/p6a_smoke`）。

1. プロセス 1：New Game → 到着の保存 → きびだんごを 1 つ使う（保存契機ではない変化）→ 正常終了の保存 → 終了
2. プロセス 2：Continue → 採用 → 状態を書き出して終了

| 確認 | 結果（実行 s6・s7・s8 とも同じ） |
|---|---|
| 同じ冒険 ID | OK |
| 正常終了で保存した残数（2）が戻る | OK |
| 正常終了の保存が成功 | OK（Saved） |
| 再開エリア | OK（area_p6_a） |
| Continue 直後に未保存が無い（Load で保存要求を出さない） | OK |
| プロセス ID が別 | OK |

### 5. 性能（P6A 25）

測定は `P6A_性能測定記録.md`。要旨：採取のメインスレッド時間 p95 0.02ms／最大 0.9ms（目標 2ms／8ms）、
要求から完了まで p95 50ms（通常 I/O）・450ms（200ms の遅い I/O）（目標 1 秒）、フレーム時間 p95 16.67ms。
各 165 要求（書込中の追加要求 15 を含む）× 通常／遅い I/O × 3 回。**連続撃破・通常移動の最中の計測は含めていない**（同記録 §4）。

### 6. 攻撃 VFX（P6A 27）

| 項目 | 結果 |
|---|---|
| 再現 | P5／P5.5 の試遊 Scene では主人公の剣閃が出ない（実装の確認で再現条件を特定） |
| 対象 | 主人公の剣閃（通常 1〜3 段・必殺技）、敵の剣閃、ガード不能の頭上警告、ジャストガードの閃光、スイング SE |
| 原因 | **表示役が一度も置かれていなかった。** これらは P3.5 の Builder（`Phase35CombatTrialBuilder.BuildVfx`）が Scene に置く部品で、主人公 Prefab には付いていない。P5 の Builder は Area の常駐系を作り直したときにこの配線を持ち越さず、P5 の Scene 検査も VFX を見ていなかったので、受入を通り抜けた |
| 修正（P6A） | `BuildVfx` を P5 系の Builder から呼べるようにし（`internal`）、`Phase5BuildTargets.IncludeCombatVfx` が true のとき Area の `AreaSystems` に置く。Camera は Area に無いので Main Camera（常駐 Rig）へ任せ、主人公は同じ Scene の主人公を明示参照。素材の不足は P3.5 と同じ検査で生成前に止める。**P6A の検証 campaign だけ true** |
| 機械検査 | `validate-phase6-world`：各 Area に剣閃・敵の剣閃・警告・JG 閃光が 1 つずつ、剣閃の全段全方向の素材、主人公の参照が同じ Scene。旧 Scene（修正前）で 12 件落ちることを確認 |
| 表示確認（自動） | `AttackVfx_ShowsInA_AndAfterSlideInB`：実キー J で再生され、Main Camera の画面内・描画層内、Camera との間に遮蔽なし。スライド後の B でも同じ。旧 Scene で落ちることを確認 |
| 表示確認（人） | **未実施**（人間確認へ渡す。README の確認項目） |
| 残件 | ① P5／P5.5 の試遊 Scene には入れていない（受入済み Scene を作り直すため。旗を true にすれば同じ修正が入る。オーナー判断）。② 深度の逃がし 0.5 は 45° 前提で、55° では北壁際で一部欠ける可能性がある（未計測。P5.5 でキャラは 1.5 へ上げた。P10b の見た目調整）。③ ヒット SE・足音・敵スイング SE・撃破フェードは P3.5 の別の配線で、P5 系にはまだ無い。いずれも `P6A_後続課題.md` |

### 7. ブリッジの事故と対策

2026-10-01 16:00 ごろ、PlayMode 実行中に Editor が約 8 時間無応答になった。原因は **Test Runner の「Scene を保存しますか」モーダル**
（中断で残った未保存の InitTestScene が開いたまま次の PlayMode を始めた）。翌日も同じモーダルを画面で確認し、「保存しない」で閉じた。
対策として、ブリッジは PlayMode を始める前に開いている Scene を確かめ、Test Runner の一時 Scene（InitTestScene）だけが未保存なら破棄し、
それ以外の未保存 Scene があれば開始せず理由を返す（Scene を触る run-op の前も同じ破棄を行う）。中断で `Assets/` 直下に残った
InitTestScene*.unity は `_to_delete/inittest/` へ退避した。

### 8. 最終の実行（すべて作業ツリーの最終版で）

| 実行 | 内容 | 結果 |
|---|---|---|
| `b9`→`v11` | `build-phase6-world` → `validate-phase6-world` | 合格（警告 1：A に遭遇戦が無い——構成どおり） |
| `v13` | `validate-exploration-trial`（P5） | 合格 |
| `ef3` | EditMode 全件 | 1866／1866（予定 1866） |
| `pm1`／`pm2`／`pm3`／`pm4` | PlayMode 全件を 4 分割（P5.5 スライド 64／往復 13／P5.5 その他 72／その他 115） | 64／13／72／115 |
| `pm6` | PlayMode `P6AWorldPlayTests` | 9／9（同じ版で `pm5`・`pm6`・`pm7` の 3 回連続） |
| `vrP4`／`vrP5`／`vrP55`／`vrP6A` | `verify-required-tests`（`ef3,pm1,pm2,pm3,pm4,pm6`） | P4 226／226・P5 93／93・P5.5 293／293・P6A 56／56、未説明の Skip なし、未対応要求なし |
| `s6`／`s7`／`s8` | `p6a-player-smoke`（実ビルド・別プロセス・性能） | 3 回とも合格 |

PlayMode とScene を置き換える実行（EditMode 全件・Builder）は、オーナーの常時許可（2026-10-01／02）のもとで事前の告知なしに行った。

### 9. 変更した既存の契約

- `SaveCoordinator.IsWriting`：完了を取り込むまで true（以前は書込担当が空けば false）。
- `RealSaveFileSystem.Replace`／`Move`：一時的な共有違反を書込担当の中で最大 5 回やり直す。
- `AreaFieldEnemyDirector`：生成根を Scene 直下へ（以前は配置役の子）。
- `AreaTransitionService`：通常のタイトル復帰 `TryBeginReturnToTitle`／`CanReturnToTitle` を追加（終端失敗からの退避は従来どおり）。
- `CampaignSaveService`：ゲーム内メニュー（Esc）、`TestDirectoryOverride`（テストと実ビルド確認の保存先）。
- `Phase6CampaignLauncher`：タイトル復帰の行き先を自分の Scene にする。実ビルド確認の引数があるときだけ自動操作を起動。
- `Phase35CombatTrialBuilder.BuildVfx`／`ValidateVfx`：`internal` にして P5 系の Builder から呼べるように（中身は不変）。
- `Phase5ExplorationValidator`：普通敵の配置役の検査、1 エリアに複数の遭遇戦があるときは境界ごとに自分の出現点・Trigger だけを見る。
- `Phase55WorldValidator`／`Phase55Arrangement`：接続一覧を共有する配置（`SharedConnectionList`）では、その 2 エリア間のレコードだけを往復として数える。
- ブリッジ：PlayMode 開始前と Scene を触る run-op の前に、Test Runner の一時 Scene だけを破棄（他の未保存 Scene があれば開始しない）。op `build-phase6-world`・`validate-phase6-world`・`p6a-player-smoke`、必須一覧の鍵 `P6A`。

### 10. 状態

- **P6A の実装と自動受入は完了。** 人間確認（README §4）は未実施——P6A の受入はその結果を待つ。P6（P6B 以降）は未着手。
- オーナー判断が要るもの：`P6A_後続課題.md` F01（P5／P5.5 の試遊 Scene へ VFX を入れるか）。

---

## 記録 005：試遊のフィードバックへの対応（2026-10-02）

オーナーの試遊で「現環境ではテストしづらい」とされた 2 点を直した。

### 1. 仮 UI

| 指摘 | 対応 |
|---|---|
| タイトル・お地蔵様の UI が小さく読みにくい | 共通部品 `PadMenuNavigator` で **1.5 倍**に描く（`GUI.matrix` の拡大。文字・枠・余白とも）。タイトル・お地蔵様に加え、同じ系統のゲーム内メニュー・保存表示・保存失敗の選択肢・お地蔵様の通知も揃えた |
| 選択肢をパッドで選べない | 十字キー・左スティックの上下で選択、A（South）で決定、B（East）で戻る。キーボードの ↑↓・Enter も同じ。ゲーム内メニューはパッドの Start でも開く。選択中は「▶」と色で示す。既存のショートカット（N／C／Y、数字、T／Q、Esc）とマウスは残した |

- 決定・戻るのボタンは入力定義（`IA_Momotaro`）の UI Submit／Cancel と同じ割り当て（South／East）。
- **開いた直後のフレームは入力を見ない**。お地蔵様を調べるボタン（Interact＝South）がそのまま 1 つ目の選択肢（休息）を決定しないため。
- タイトルは保存があれば「つづきから」、New Game の確認は「いいえ」を選んだ状態で開く（うっかり上書きしない）。
- 描画と入力は同じ選択肢の並びを使う（お地蔵様は休息・成長・旅立ち・閉じる）。入力は Update で 1 回だけ読む。

### 2. 死にやすいテスト環境（P6 のテスト専用）

| 指示 | 実装 |
|---|---|
| 敵の攻撃力を倍に | `EnemyActor.AttackPowerScale`（実行時だけ・既定 1）を攻撃の命中（近接・飛び道具）で使う。遭遇戦の生成役・普通敵の配置役が生成時に設定し、`AreaInitializer` が campaign の値を渡す |
| 初期体力を半分に | `PlayerVitalsHolder.MaxHpScale`（実行時だけ・既定 1）。最大 HP は「基礎値 × 倍率 ＋ 成長の加算」で置き直す（累積しない）。New Game は半分の最大値で満タン |
| P6 のテスト専用 | 値は campaign の Data `AreaCatalogData` の「テスト専用の調整」（`TestEnemyAttackScale`＝2・`TestPlayerMaxHpScale`＝0.5）。campaign のカタログだけが読み、P6A の検証 campaign だけが 1 以外を持つ。Data（敵・主人公）は書き換えない |

- 持ち越し台帳（`P5_ActorTransferInventory.md`）へ `MaxHpScale` を「再構築」で追記（E28 の検査が新しい欄を検出した）。

### 3. テスト

| 実行 | 内容 | 結果 |
|---|---|---|
| `b10`→`v14` | `build-phase6-world`→`validate-phase6-world` | 合格 |
| `e4` | EditMode `P6A`（`TestTuning_OnlyInTheP6ACampaign`：出荷カタログで P6A は 2／0.5、P5・P5.5 東西・南北は 1／1。`MaxHpScale_AppliesBeforeGrowthBonus_NotCumulative`） | 47／47 |
| `e5` | EditMode：敵・主人公の Vitals・P6A・遭遇戦・Validator 網羅・ブリッジ・MonoScript 名（Scene を置き換えない範囲） | 320／320 |
| `e6`→`e7` | 持ち越し台帳 E28 | 追記前は `MaxHpScale` の未分類で失敗（想定どおりの検出）→ 追記後 1／1 |
| `vr2` | `verify-required-tests`（P6A、`e4,e5,p25`） | 61／61 Passed、未対応要求なし |
| `p25` | PlayMode `P6AWorldPlayTests`（追加 3 本：`Title_PadDecideStartsNewGame`・`ShrineAndGameMenu_PadNavigation`・`TestTuning_HalfPlayerHp_DoubleEnemyAttack`。既存 9 本は敵 2 倍・HP 半分の下でも通る） | 12／12 |

**全件の回帰（EditMode 全件・PlayMode 全件）は今回は走らせていない。** 試遊中で Editor を使っている可能性があり、
EditMode 全件は開いている Scene を置き換えるため（CLAUDE.md）。敵の攻撃と主人公の最大 HP は既定値 1 で従来と同じ計算になる。
次の全件実行の機会に回帰を確かめる。

**UI の見た目（1.5 倍の大きさ・選択の印）は画面で確認していない。** パッドでの操作はテストで確かめた。

---

## 記録 006：GPT レビュー（`Momotaro_P6A_Review_03e9f94.md`）への対応（2026-10-02）

対象は `03e9f94`。指摘 R1〜R4・D1 をコードで確かめてから直し（オーナー回答：「すべて着手」「出発 Area を保持する形に直す」）、
未達とされた証跡（P6A 08・12・17・25・27、F06）を足し、対応表から未確認の「合格」を外した。

### 1. R1：通常の終了要求が「未保存なし」で素通りしていた

| 確かめたこと | 直したこと |
|---|---|
| `OnWantsToQuit` は `IsDirty` が false なら終了を許していた。HP・スタミナ・犬丸の Down 残時間は版を進めないので、**直前の保存より後の値が黙って失われた** | 冒険を結んでいる間は、明示の承認（保存済みの終了・「保存せずに終了」）が無い限り**必ずいったん断り**、`SaveBeforeExit` で最新を採ってから自分で終了する。保存の途中・失敗後の選択中にもう一度閉じられたら、行き先を「終了」へ向け直す |
| 版の変わらない保存（終了前など）が失敗しても、版の差が無いので `IsDirty` が false に戻っていた | `SaveCoordinator` に「直近の書込が失敗して、まだ次の成功が無い」印を持たせ、`IsDirty` に含める（`HasUnsavedFailure`）。次の成功・結び直しでだけ消える |

証跡：`Coordinator_FailedSaveWithUnchangedRevision_StaysDirtyUntilNextSuccess`（E）、`WindowClose_HpOnlyChange_IsNotPassedThrough_FailureStaysUnsaved`（P。Editor の再生では
`wantsToQuit` が来ないので同じ入口を呼ぶ。成功するとアプリが終わるため失敗側を通す）、実ビルド `close`（下の §4）。

### 2. R2：New Game の退避が途中で失敗すると旧冒険の最新世代がスロットから消えていた

`TryArchiveCurrent` は A・B を順に退避先へ**移動**していた。2 つ目で失敗すると、スロットには古い片側だけが残り、最新は退避先にしか無かった。
手順を「**写す → 読み直して一致を確かめる → 古い世代から消す → 最新を消せなければ古い側を写しから戻す**」に変えた。どの段で失敗しても
スロットには「読める旧冒険（最新の世代）」か「空」しか残らず、New Game は始まらない（`P6_SaveInventory.md` §5 末尾）。

証跡：`Store_ArchivePartialFailure_KeepsLatestLoadable_AcrossRestart`（2 つ目の写し・2 つ目の削除・削除後の戻しの失敗をそれぞれ注入し、新しい Store＝再起動でも世代 2 を読む）。

### 3. R3：失敗後の「もう一度保存」が常に終了へ向かっていた

タイトルへ戻ろうとして失敗したのに、選択肢の「もう一度」は `ChooseRetry(thenQuit: true)` 固定だった。行き先（`ExitDestination`：タイトル／終了）を覚え、
「もう一度保存して…」「保存せずに…」は元の行き先へ向かう（ラベルも行き先で変わる）。

証跡：`ExitChoice_RetryByDecideInput_KeepsTheTitleDestination`（失敗中に **Enter の決定**で再試行 → また失敗して行き先はタイトルのまま → 直して **パッドの決定（South）** → タイトルへ戻り、最新の HP が保存されている）。

### 4. R4：付与済み報酬・加入済み仲間の ID を検証していなかった

`SaveSnapshotValidator` は両者を書式だけで通していた。campaign のカタログに既知の一度きり報酬（初到達報酬＋Data の一覧）と既知の仲間を持たせ、
未知 ID を拒否する。Validator（`validate-phase6-world`）は、この campaign の Data にある一度きり報酬がすべて既知であること・一覧に Data の無い ID が無いこと・犬丸が既知であることを見る。

証跡：`Validator_RejectsWellFormedUnknownRewardAndCompanionIds`。

### 5. D1：旅立ちが出発 Area を先に捨てていた（仕様 §6 との不一致）

旅立ちは P5 の Fade／Single 経路を使っていたので、到着先の準備より前に出発 Area が破棄されていた（失敗すると「出発 Area を読み直して戻る」）。
仕様 §6「活動 Area は到着準備成功まで保持…解放失敗時は遷移を確定せず、出発側を維持して再試行可能」に合わせ、**スライドと同じ排他・世代・在留台帳・先読みの口**で運ぶ
`AreaSlideTransitionRunner.TryFastTravel` を足した。到着先を Additive で読み、隔離したまま配置と復元を済ませ、確定してから出発側を手放す（預かり）。
読込開始・読込・準備・配置・預かっていた旧 Area の解放のどこで失敗しても、出発側を開け直してその場に留まる。カメラは送らず、確定時に到着 Area へ結び直して合わせる。
同じ Area の中の旅立ちだけは同じ Scene を 2 枚載せられないので従来の経路（`P6A_後続課題.md` F11。検証 campaign には無い）。

証跡：`ABC_FastTravel_Death_EachAdvancesCycleOnce`（到着先 C が載った時点で出発 A もまだ載っている。Single 読込を使わない）、
`FastTravel_KeepsDeparture_UntilPrepared_FailuresStayAndRetry`、`FailedTravelAndFastTravel_CommitNothing`（読込開始の失敗）。

### 6. P6A 12 の複合異常（旅立ち × 保存）

`FastTravel_KeepsDeparture_UntilPrepared_FailuresStayAndRetry` で、いずれも在留・Scene 2 枚以下を毎フレーム確かめる：

1. 読み終えたあとの**準備・配置の失敗**（到着先の準備完了後・確定前に失敗を注入）→ 出発側の主人公が**同じ実体のまま**操作に戻る。周期・補充・回復・登録・進行・保存は 1 つも起きない。到着先は手放す
2. **遅いロード**（読込の開始を保留）→ 待つ間は出発 A が活動のまま、保存要求は採取されない（遷移の前後をまたがない）。着いてから 1 回だけ休息し、周期・再開位置・死亡再開点を同じ保存へ載せる
3. **預かっていた旧 Area の解放失敗と重なる旅立ち**（B から A へ。A を読むには預かっている C を手放す必要がある）→ 旅立たず B に留まる（3 枚目を読まない）。直れば同じ操作で旅立てる

### 7. P6A 08：クエスト接続 fixture

本番のクエストランナーは作らず（P7）、仕様 §3「将来のクエストと章進行：安定 ID 付き状態を保存へ接続できる境界」だけを置いた：
`GameSessionState` のクエスト段階（ID → 0 以上の整数、`TrySetQuestStage` で保存要求）、Snapshot・DTO（`questStages`、保存形式の版 2）、
検証（campaign の既知クエスト ID。P6A は `quest_p6a_fixture` だけ）、候補 Session への復元。死亡・休息・周期では戻らない。
版 1 の保存は「クエスト段階が空」として読む（版で欄の一覧を分ける。欠けた欄を黙って補わない）。

証跡：`ABC_*`（段階 3 にしてから実際に死に、死亡後のメモリと保存ファイルの両方で 3）、`RoundTrip_*`、`Validator_RejectsUnknownIdsRangesAndContradictions`、`Codec_ReadsSchemaVersion1_AsNoQuestStages`、`Codec_RejectsMissingDuplicateUnknownWrongTypeAndTamper`（版 1 に questStages があれば拒否・版 3 は拒否）。

### 8. P6A 17：実経路の同フレーム致死と報酬、死亡再開の失敗と終了要求

`SameFrameKillAndDeath_RespawnFailsDuringExit_NoHpZeroSave_ThenRecovers`：普通敵を**実の被弾窓口**（`EnemyActor.ReceiveHit` → 撃破 → 報酬 → 保存要求）で倒し、
同じフレームで主人公も倒れる。倒れている間にタイトル復帰を求め、**死亡再開の遷移を失敗させる**。保存は時間切れの選択になり（成功表示をしない）、
倒れてから 1 度も採取していない（HP 0 の通常保存なし）、ファイルは倒れる前の一貫した世代のまま。ゲームへ戻り再開を直すと、報酬・周期 +1（1 回だけ）・全回復が同じ保存に載る。

### 9. P6A 27：実際に表示された VFX の記録、F06

`AttackVfx_RecordsShownTiersAndDirections`：A で下上左右それぞれ実キー J を押し続け、**実際に再生された絵**を素材の組と照合して記録した
（`_bridge/p6a27_vfx_observed.json`）。観測：**通常 1・2・3 段 × 下・上・左・右の 12 通りすべて**。

| 対象 | 実際の表示を確認 | 素材・配置の検査だけ |
|---|---|---|
| 主人公の剣閃 通常 1〜3 段 × 4 方向 | ✔（上の記録。画面内・遮蔽なし・遷移後は `AttackVfx_ShowsInA_AndAfterSlideInB` で 1 段目） | — |
| 主人公の剣閃 特殊攻撃 | — | ✔（この検証 campaign に特殊攻撃の実入力の経路が無い） |
| 敵の剣閃・ガード不能の頭上警告・ジャストガードの閃光 | — | ✔（`validate-phase6-world` で 1 つずつ・素材あり） |

F06（2 Area 在留での配信元の選択）：`JustGuardVfxPresenter`・`CombatFeedbackPresenter`・`PlayerHitSePresenter`（と配信元の主人公探索）は
`FindFirstObjectByType` で**載っているどれか**を掴んでいた。今の開閉順（出発側を閉じてから到着側を開ける）では正しいものを掴むが、順序に頼っていた。
「自分と同じ Scene のものを優先」（`SceneLocalLookup`）へ変えた。証跡：`FeedbackPresenters_BindTheirOwnScenesDispatcher_WithTwoAreasLoaded`
（配信元が 2 Scene で同時に有効でも各表示役は自分の Scene のものを見る。実 Area で A を預かったまま B が活動しているとき B の表示役は B の配信元）。
修正前のコードでこの検査が落ちることを確かめた（§11）。

### 10. P6A 25：実プレイ中の保存性能

`P6A_性能測定記録.md` §5。実キーで歩き、実攻撃で普通敵を 16 体倒し、8 回休息（保存 32 回）を 3 回。保存の採取 p95 は 1ms 未満、歩行中・撃破中にフレームは伸びない。
**1 回目の休息で 1 フレームだけ 281〜326ms 止まる**（3 回とも）。休息の呼び出しは 4ms・採取は 1ms 未満で、保存連打の計測には現れない——休息側の初回の処理と見ているが
切り分けていない。P6A 25 は「保存の基準は満たす。休息の停止は F04 として残す」とし、全面合格とは書かない。

### 11. 修正を外すとテストが落ちることの確認（CLAUDE.md）

修正だけを一時的に外した版をビルドして実行し、新しい検査が落ちることを確かめた（確認後に元へ戻し、元の版と一致することを `cmp` で確認）：

| 外した修正 | 落ちた検査（核心の判定で落ちたか） |
|---|---|
| R1：失敗の印を `IsDirty` から外す | `Coordinator_FailedSaveWithUnchangedRevision_*`・`WindowClose_*`（「版の差が無くても、失敗した保存のあとは未保存」） |
| R2：退避を移動版（`03e9f94`）へ戻す | `Store_ArchivePartialFailure_*`（「写しの失敗を成功にした」） |
| R4：既知 ID の照合を外す | `Validator_RejectsWellFormedUnknownRewardAndCompanionIds`（「未知の付与済み報酬を受け付けた」） |
| D1：旅立ちを Single 経路へ戻す | `ABC_*`（「到着先が載った時点で出発 Area もまだ載っていた」）・`FastTravel_KeepsDeparture_*` |
| F06：同じ Scene 優先を外す | `FeedbackPresenters_*`（「2 つ目の Scene の表示役は 2 つ目の配信元を見る」） |

R3 は外すと Editor の再生が止まる（終了へ向かう）ので、この方法では確かめていない。

### 12. 実ビルド（P6A 22・23・25）

ブリッジ op `p6a-player-smoke`（`s13`。`s10`・`s12` も同じ結果）：

| 確認 | 結果 |
|---|---|
| New Game → ゲーム内の終了 → 別プロセスで Continue | 同じ冒険・残数・エリア、Continue 直後に未保存なし |
| **版を進めない変化だけ（HP −7、犬丸を Down・復帰待ち 5 秒）→ `Application.Quit`（通常の終了要求）→ 別プロセスで Continue** | 終了要求の前は未保存に見えない（以前ならここで素通り）。Continue で HP 43・犬丸 Down・復帰待ち 4.967 秒（終了時 4.983 秒以下＝無料回復なし） |
| 実プレイ中の性能 | §10 |
| 保存連打の性能（通常・200ms の遅い I/O） | 採取 p95 0.02ms、要求→完了 p95 50ms／450ms、フレーム最大 17.6ms |

### 13. 変更した既存の契約

- `SaveCoordinator.IsDirty`：直近の書込失敗（次の成功まで）を含む。`HasUnsavedFailure` を追加。
- `CampaignSaveService`：通常の終了要求は冒険中なら必ず保存を通す。`ExitDestination`／`PendingExitDestination`、`ChooseRetry()`（引数なし。元の行き先へ）、`ChooseLeaveWithoutSaving()`。`ChooseQuitWithoutSaving()` は自動確認の後始末用に残した。
- `SaveFileStore.TryArchiveCurrent`：写して確かめてから消す。`RealSaveFileSystem.Delete` も一時的な共有違反をやり直す。
- `SaveSnapshot`：保存形式の版 2（`questStages`）。版 1 も読める。
- `AreaCatalogData`／`CampaignCatalog`：既知の一度きり報酬・仲間・クエストの ID。`GameSessionState`：クエスト段階。
- `AreaTransitionService.TryFastTravel`：別 Area へは `AreaSlideTransitionRunner.TryFastTravel`（出発保持）。`AreaConnectionSnapshot.ForFastTravel`（接続 Data に現れない擬似接続）。
- 表示役の配信元探索：同じ Scene 優先（`SceneLocalLookup`）。
- 実ビルド確認：`-p6a-smoke close`・`play` を追加。

### 14. 最終の実行（すべて作業ツリーの最終版で）

| 実行 | 内容 | 結果 |
|---|---|---|
| `b2`→`v2` | `build-phase6-world` → `validate-phase6-world` | 合格（警告 1：A に遭遇戦が無い——構成どおり） |
| `v3`／`v4` | `validate-exploration-trial`（P5）／`validate-phase55-world`（P5.5） | 合格（警告 1／2。従来どおり） |
| `ef4` | EditMode 全件 | 1872／1872（予定 1872） |
| `pq1`／`pq2`／`pq3`／`pq4` | PlayMode 全件を分割（P5.5 スライド 64／往復 13／P5.5 その他 72／その他 115） | 64／13／72／115 |
| `pq5` | PlayMode `P6AWorldPlayTests` | 18／18 |
| `vqP4`／`vqP5`／`vqP55`／`vqP6A` | `verify-required-tests`（`ef4,pq1,pq2,pq3,pq4,pq5`） | P4 226／226・P5 93／93・P5.5 293／293・P6A 71／71、未説明の Skip なし、未対応要求なし |
| `s13` | `p6a-player-smoke`（実ビルド・別プロセス・通常の終了要求・実プレイ中と連打の性能） | 合格（`s10`・`s12` も同じ。`s11` は実プレイの計測が 180 秒で打ち切られた——撃破の待ちを短くし全体の上限を置いて直した） |

途中の実行（`e1`、`p1`〜`p9`、`f1`〜`f7`）は §11 の確認と、テストの書き方の誤り（保存要求の無い変化を待った・入力の処理と同じフレームに当たらない 1 回押し・
1 撃では倒れない被弾後無敵）の直しに使った。製品コードの誤りはこの段階では見つかっていない。

PlayMode と Scene を置き換える実行（EditMode 全件・Builder）は、オーナーの常時許可のもとで事前の告知なしに行った。

### 15. 状態

- レビューの R1〜R4・D1 と、証跡の不足（P6A 08・12・17・25・27、F06）に対応した。対応表は未確認の「合格」を外し、P6A 25（休息の初回停止）と P6A 27（特殊攻撃・敵側の VFX の表示）は条件付きで記録した。
- 人間確認（README §4）は未実施。
- オーナー判断が要るもの：F01（P5／P5.5 の試遊 Scene へ VFX を入れるか）。

---

## 記録 007：GPT 再レビュー（`Momotaro_P6A_Review_720161d.md`）への対応（2026-10-02）

対象は `720161d`（`459cd11` にオーナーの素材・レビュー文書を足したもの。コードは同じ）。R1〜R4 は解消と判定され、残件として
① 同じ Area の中の旅立ち、② P6A 27 の未確認 VFX と 12 通りの必須化、③ 初回休息の停止の切り分け、④ 最終 SHA に紐づく証跡一式、を求められた。

### 1. 同じ Area の中の旅立ち（指摘 1）

同じ Scene を 2 枚載せる必要はないので、**いま載っている Area の中で到着を準備・検証してから置き直す**経路を足した
（`AreaSlideTransitionRunner.TryFastTravelWithinArea`。排他・世代・確定の通知は別 Area の旅立ち・スライドと共有）。

1. 受理：受付条件・排他・入口の解決。受理しただけでは何も変えない（入力と Gameplay 時計を止めるだけ）
2. 準備：入口が Scene にあること・Actor を置く窓口があること（`AreaInitializer.CanPlaceWithinArea`）、テスト用の故障注入。**ここまで何も動かさない**。失敗したら入力と時計を戻し、`FastTravelFailed`
3. 確定（同じフレーム）：置き直し（行動の中立化・入口と遭遇 Trigger の測り直し・跳ね返り防止）→ 確定 → 到着の記録 → カメラを合わせる → `FastTravelCompleted`（休息・登録・周期・保存は購読側のお地蔵様が 1 回だけ行う）

従来の Single 経路への切り替えは無くなった（F11 解消）。fixture として検証 campaign の C に 2 つ目のお地蔵様（`shrine_p6_c_2`）を置いた。

証跡：`FastTravel_WithinTheSameArea_FailureKeepsEverything_SuccessCommitsOnce`——準備の失敗で、主人公は同じ実体・同じ位置（5cm 未満）、HP・周期・補充・死亡再開点・進行の版・保存の成功回数がすべて不変、Scene を読まない。成功で、Scene を読まず（Single 読込も使わない）、周期 +1（2 フレーム後も +1 のまま）・補充・全回復・死亡再開点が 2 つ目のお地蔵様、2 つ目のお地蔵様の前に居て、保存ファイルに周期・死亡再開点・再開位置が載る。

### 2. P6A 27：未確認だった VFX の実表示（指摘 2）

| 対象 | 実際の表示の確かめ方 | 結果 |
|---|---|---|
| 主人公の剣閃 1〜3 段 × 4 方向 | 実キー J を押し直す。**12 通りを必須**にした（以前は 1 段目 × 4 方向だけが必須） | 12／12 |
| 主人公の必殺技 × 4 方向 | 実キー L（入力定義 SpecialAttack）を溜まるまで押し続けて離す | 4／4。「検証 campaign に特殊攻撃の実入力の経路が無い」という前回の記述は**誤り**だった（経路はあり、溜めて離す操作をしていなかっただけ）。訂正する |
| 敵の剣閃 | A の普通敵・C の仮ボスの実際の攻撃（気付かせてから待つ）。画面内にあることも見る | A・C（C はスライド 2 回の後） |
| ジャストガードの閃光 | 敵の攻撃の判定が出る直前に実キー K を押す（受付の窓の中） | A・C |
| ガード不能の頭上警告 | C の仮ボスの実際のガード不能攻撃 | C |
| 遷移後の主人公の必殺技 | C で実キー L | 出た |

**この検査で欠陥を見つけて直した**：A では JG が実際に 10 回成立していたのに閃光が 1 度も出なかった。原因は、命中 Feedback の配信役
（`CombatFeedbackDispatcher`）を**遭遇戦の部品と一緒にしか置いていなかった**こと。遭遇戦が無く普通敵だけが居る A では、JG 閃光・
ヒットストップ・点滅・カメラ揺れの表示役に購読する相手が無かった。戦闘 VFX を置く構成では遭遇戦の有無に関係なく配信役を置くようにし、
`validate-phase6-world` に「各 Area に配信役が 1 つ」を足した（修正前の Scene で A が落ちることを確認）。P5／P5.5 の試遊 Scene は対象外（F13。F01 と同じ判断）。

観測の記録：`p6a27_vfx_observed.json`（主人公の 16 通り）・`p6a27_enemy_vfx_observed.json`（敵側。診断として主人公への命中の種類も残す）。証跡一式に入れた。
敵の攻撃力だけ下げて主人公を倒さないようにしている（判定と演出の経路は変えない）。犬丸は Down させておく（敵を先に倒したり狙いを引き受けたりするため）。

### 3. 初回休息の停止の切り分け（指摘 3）

前回の対応表の「原因は休息の同期処理側」は性能記録の「切り分けはしていない」と矛盾していた。**撤回する**。そのうえで切り分けた
（`P6A_性能測定記録.md` §6）：

- 止まるのは**お地蔵様のメニューを開いたフレーム**で、休息のフレームではない
- **保存の採取を止めても同じだけ止まる**（保存ではない）。GC の全回収は 2ms、一時停止の切り替えだけでは止まらない
- メニューと同じ文字を IMGUI で先に描くと**そこで止まり**、本物のメニューの停止は減る → **仮 UI（IMGUI）が日本語を初めて描く準備**が原因

対処として、タイトルで仮 UI の文字（`PadMenuNavigator.PrewarmCharacters`）を見えない色で先に描くようにした。対処後の実プレイ中の計測で
**33ms を超えるフレームは 0**（`s18`、最大 32.5ms。最終の `us` では初回メニューの 1 フレームが 33.9ms）、保存の基準も満たす。F04 は解消として閉じた（本 UI でも同じ準備を読み込み時に）。

計測の道具の不具合も直した：自動確認の仮想キーボードは、窓が前面に無いと Input System の既定で止まり、撃破が 0 になる回があった。
自動確認のときだけ前面を問わない設定にし、撃破に失敗した回があれば計測を不合格にする。

### 4. 証跡一式（指摘 4）

`Evidence/P6A_<コードの SHA>/`（下の §7）。最終の実行は、**コードと記録のコミット（§6 の対象 SHA）を作ったあと、作業ツリーがそのコミットと一致する状態で**行い、
生ログ（葉テスト全件）・必須一覧の照合結果・実ビルドの結果・VFX の観測を、そのあとのコミットで足す。

### 5. 変えた既存の契約

- `AreaTransitionService.TryFastTravel`：同じ Area は `TryFastTravelWithinArea`、別 Area は `TryFastTravel`（どちらも出発を保持）。旧 Single 経路は旅立ちでは使わない。
- `AreaInitializer.CanPlaceWithinArea`／`TryPlaceWithinArea` を追加。
- `Phase5ExplorationBuilder`：`IncludeCombatVfx` のとき、遭遇戦の無い Area にも命中 Feedback の配信役・ヒットストップ・点滅・手応えを置く。`Phase6WorldValidator`：各 Area に配信役 1 つ。
- `PadMenuNavigator.PrewarmCharacters`／`DrawPrewarm`、`Phase6CampaignLauncher` がタイトルで先に描く。
- 検証 campaign：C に 2 つ目のお地蔵様。
- 実ビルド確認：`-p6a-no-save`・`-p6a-play-probe`、自動確認では入力を前面に依らせない、撃破の失敗を不合格に。

### 6. 最終の実行（対象：`937ad6e`。作業ツリーがそのコミットと一致する状態で）

| 実行 | 内容 | 結果 |
|---|---|---|
| `u1`→`u2` | `build-phase6-world` → `validate-phase6-world` | 合格（警告 1：A に遭遇戦なし＝構成どおり） |
| `u3`／`u4` | `validate-exploration-trial`（P5）／`validate-phase55-world`（P5.5） | 合格（警告 1／2。従来どおり） |
| `ue` | EditMode 全件 | 1872／1872（予定 1872） |
| `up1`／`up2`／`up3`／`up4` | PlayMode 全件を分割（P5.5 スライド 64／往復 13／P5.5 その他 72／その他 115） | 64／13／72／115 |
| `up5` | PlayMode `P6AWorldPlayTests` | 20／20 |
| `uvP4`／`uvP5`／`uvP55`／`uvP6A` | `verify-required-tests`（`ue,up1,up2,up3,up4,up5`） | P4 226／226・P5 93／93・P5.5 293／293・P6A 73／73、未説明の Skip なし |
| `us` | `p6a-player-smoke` | 合格 |

`us` の要点：通常の終了要求 → 別プロセスで HP 43・犬丸 Down・復帰待ち 4.967 秒（終了時 4.983 秒以下）。実プレイ中（撃破 16・休息 8・保存 32）は採取 p95 0.22ms・最大 0.59ms、
フレーム p95 16.67ms・最大 33.9ms（1 フレーム。初回のお地蔵様メニュー。対処前の約 300ms から。33.4ms の線をわずかに超えた）。保存なしの比較は最大 31.4ms、
段階の先起こしありは 24.9ms。保存連打は採取 p95 0.021ms・要求→完了 p95 50ms／遅い I/O 450ms、フレーム最大 17.1ms／16.8ms。

PlayMode と Scene を置き換える実行（EditMode 全件・Builder）は、オーナーの常時許可のもとで事前の告知なしに行った。途中の実行（`t1`〜`t31`、`s14`〜`s18`、`pre1`・`pre2`）は
上の §1〜§3 の確認・切り分けと、テストの書き方の直しに使った（敵に当てて倒してしまう・犬丸が敵を引き受ける・溜めが被弾で取り消される）。

### 7. 証跡一式

`Evidence/P6A_937ad6e/`（`README.md` に中身と手順）：各実行の `result.json`、テスト実行の葉テスト全件の記録、必須一覧の照合結果、実ビルド確認の出力と各プロセスの結果、
VFX の観測 2 件。

### 8. 状態

- 指摘 1〜4 に対応した。P6A の自動受入の範囲で未確認として残るものは無い（対応表）。人間確認（README §4）は未実施。
- オーナー判断が要るもの：F01・F13（P5／P5.5 の試遊 Scene へ戦闘 VFX と配信役を入れるか）。

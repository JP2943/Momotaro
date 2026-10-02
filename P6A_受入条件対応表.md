# P6A 受入条件対応表

仕様 §13 の要求 ID と、確かめた手段・証跡の対応。テストの完全名の正本は `P6ARequiredTests.json`
（ブリッジ `verify-required-tests`、`manifest: "P6A"` で照合）。ここは人が読むための要約。

凡例：E＝EditMode、P＝PlayMode（実 Scene・実入力）、B＝実ビルド（別プロセス）、V＝Builder 再生成と Validator、H＝人間確認、R＝既存必須一覧の再照合。

| ID | 確認内容 | 手段 | 主な証跡 | 状態 |
|---|---|---|---|---|
| P6A 01 | 初到達は Commit で一度。失敗遷移と Load で徳を得ない | E・P | `P6AProgressionTests.Arrival_*`、`P6ASaveTests.Restore_DoesNotGrantOrRequestSaves`、`P6AWorldPlayTests.NewGame_Progress_Restart_Continue_RestoresEverything`・`FailedTravelAndFastTravel_CommitNothing` | 合格 |
| P6A 02 | 普通敵二体の個別報酬、重複通知の排除、通常移動で非復活 | E・P | `FieldEnemy_PerPlacementRewards_DuplicateIgnored_NoRevivalOnMove`、`FieldDirector_*`、PlayMode（実 Hitbox で倒し A↔B を往復） | 合格 |
| P6A 03 | 休息・成長・死亡・旅立ちが各一周期。過去章と未ロード地域へ反映 | E・P | `Rest_AdvancesOnce_*`、`Growth_Success_*`、`RespawnCycle_*`、`ABC_FastTravel_Death_EachAdvancesCycleOnce`（未ロードの A の普通敵が戻る） | 合格（章は P7 以降。未ロード地域への反映で代える） |
| P6A 04 | 撤退で徳保持、再挑戦は Wave1、再撃破報酬可。移動失敗で撤退未確定 | E・P | `Retreat_AllowedInP6_*`、`Retreat_NotAllowedInP5`、`RealRunner_RetryAfterDefeat_*`、`Encounter_SaveMidFight_*` | 合格 |
| P6A 05 | 複数遭遇戦を独立記録。最終撃破・ボーナス・開通を同時保存 | E・P | `EncounterClear_*`、`Group_TwoEncountersIndependent`、`Encounter_SaveMidFight_*`（保存ファイルの同じ世代にクリア・開通・徳） | 合格 |
| P6A 06 | 遭遇戦・中ボス・章ボス・配置物が死亡と休息後も復活しない | E | `RespawnCycle_RevivesFieldEnemiesOnly_PermanentRecordsKept`、`PlacementPick_*`、`LegacyPolicy_*`（P5 の規則は維持） | 合格（中ボス・章ボスは仮ボスの Data で代える） |
| P6A 07 | 調べるだけで登録と保存、回復なし。休息で補充、一般消耗品は戻さない | E・P | `Register_OnlyRecordsAndSaves_NoRecovery`、`Rest_*_NotItems`、PlayMode（実キー E で HP 不変） | 合格 |
| P6A 08 | 死亡で徳・成長・**クエスト接続 fixture**・開通を保持し、登録地点へ全回復 | E・P | `RespawnPoint_IsLastRegisteredShrine_*`、`ABC_FastTravel_Death_*`（C で倒れて C のお地蔵様へ全回復。**クエスト段階の接続口 `quest_p6a_fixture` を段階 3 にしてから死に、死亡後もメモリ・保存ファイルの両方で 3**）、`RoundTrip_*`（クエスト段階の往復）、`Validator_RejectsUnknownIdsRangesAndContradictions`（未知クエスト・負の段階を拒否）、`Codec_ReadsSchemaVersion1_AsNoQuestStages` | 合格（記録 006 §7。クエストランナーは作っていない＝P7） |
| P6A 09 | Checkpoint と ResumeAnchor の分離。報酬が復帰位置を上書きしない | E・P | `RespawnPoint_*_RewardsDoNotMoveAnchors`、`NewGame_*`（登録後の Resume が Continue で戻る） | 合格 |
| P6A 10 | 成長の支出・取得・休息・保存。不成立は無変更。再適用非累積 | E | `Growth_*`、`MaxHpBonus_ReappliedIsNotCumulative`、`GrowthPurchase_FailuresChangeNothing` | 合格 |
| P6A 11 | 未登録・場所外・同一地点の旅立ち拒否。到着後のみ登録と休息 | E・P | `FastTravel_RejectsSameUnregisteredAndOutsideShrine`、`ABC_*`（受理時点で周期不変）、`FailedTravelAndFastTravel_CommitNothing`、`FastTravel_KeepsDeparture_*`、**`FastTravel_WithinTheSameArea_FailureKeepsEverything_SuccessCommitsOnce`**（同じ Area の別のお地蔵様。受理時点で周期不変、着いてから 1 回だけ） | 合格 |
| P6A 12 | A B C 往復、遠隔旅立ち、旧 Area 解放失敗・遅延ロードで二重活動・多重 Scene 操作なし | P・R | `ABC_*`（Scene・在留とも最大 2。到着先が載った時点で出発 Area も載っている）、`FastTravel_KeepsDeparture_UntilPrepared_FailuresStayAndRetry`（別 Area への旅立ち × 保存：準備・配置の失敗／遅いロード中の保存要求の見送り／預かっていた旧 Area の解放失敗）、`FailedTravelAndFastTravel_CommitNothing`（読込開始の失敗）、**`FastTravel_WithinTheSameArea_*`**（**同じ Area の中の旅立ち**：Scene を読まない。準備の失敗では出発位置・HP・進行・保存が何も変わらず、成功では休息・登録・周期・保存が 1 回だけ）。P5.5 の既存必須（`P55RequiredTests.json`） | 合格（記録 006 §5・§6、記録 007 §1） |
| P6A 13 | 全保存項目を非初期値で RoundTrip。Snapshot と Runtime の可変参照非共有 | E | `RoundTrip_AllFieldsNonDefault_ProduceIdenticalSnapshot`（クエスト段階を含む）、`Snapshot_DoesNotShareMutableStateWithRuntime` | 合格 |
| P6A 14 | Snapshot 採取で攻撃中断・回復・付与・入力消費なし | P | `Capture_DuringAttack_DoesNotDisturbThePlayer` | 合格 |
| P6A 15 | 戦闘中の保存から安全入口へ再開、資源保持、遭遇戦のみ未クリア | P | `Encounter_SaveMidFight_*` | 合格 |
| P6A 16 | 犬丸 Down と残時間保持、Load で無料回復・通知・暴発なし | P・B | `NewGame_*`（Down で保存 → 再起動 → Down・HP 0・残時間 ≤ 保存値 → 時間で復帰）、実ビルド `close`→`continue_after_close`（**通常の終了要求だけで**保存した Down と残時間が別プロセスで戻る） | 合格 |
| P6A 17 | 同フレーム致死と報酬、死亡復帰失敗、終了要求の競合で HP0 保存や報酬不整合なし | E・P | `SameFrameKillAndDeath_RespawnFailsDuringExit_NoHpZeroSave_ThenRecovers`（**実の被弾窓口**で普通敵を倒し同じフレームで主人公も倒れる → 倒れている間のタイトル復帰 → **死亡再開の遷移が失敗** → 時間切れの選択でゲームへ → 採取 0 回・ファイルは倒れる前の世代 → 再開を直すと報酬・周期・全回復が同じ保存に載る）、`ReturnToTitle_WhileDead_ResolvesRespawnBeforeSaving`、`Coordinator_DefersCaptureWhenUnsettled` | 合格（記録 006 §8） |
| P6A 18 | 書込中の新要求を後続保存、古い Revision で dirty を消さない | E・B | `Coordinator_RequestDuringWrite_*`、`Coordinator_CompletionFromOldSessionIsIgnored`、`Coordinator_UntakenCompletion_IsStillWriting`、実ビルド計測（重ね要求 15 回で最新が保存済み） | 合格 |
| P6A 19 | 書込・flush・検証・置換の失敗で前の世代保持。再試行は最新 | E・P | `Store_FaultAtAnyStage_*`（4 段）、`Coordinator_FailureKeepsDirty_*`、**`Coordinator_FailedSaveWithUnchangedRevision_StaysDirtyUntilNextSuccess`**（版の変わらない保存の失敗を「保存済み」と読まない＝レビュー R1）、`TransientIo_*`、`ReturnToTitle_SaveFails_*` | 合格 |
| P6A 20 | 破損・未知 ID・重複・版違い・欠損・無効復帰点を拒否、黙って New Game にしない | E | `Codec_Rejects*`、`Validator_Rejects*`、**`Validator_RejectsWellFormedUnknownRewardAndCompanionIds`**（付与済み報酬・加入済み仲間も campaign の既知 ID で解決＝レビュー R4）、`Codec_ReadsSchemaVersion1_AsNoQuestStages`、`Store_AlternatesSides_*_RecoversFromOneCorruptSide` | 合格 |
| P6A 21 | New Game 確認と退避失敗、旧冒険混在、複数プロセス競合 | E・B | `Store_ArchiveBeforeNewGame`、**`Store_ArchivePartialFailure_KeepsLatestLoadable_AcrossRestart`**（2 つ目の写し・2 つ目の削除・削除後の戻しの失敗 → 再起動しても最新の世代で旧冒険を読める＝レビュー R2）、`Store_RefusesMixedAdventures*`、`Store_SecondWriterCannotTakeTheLock` | 合格（確認の表示は H） |
| P6A 22 | 正常終了・タイトル復帰で最新を保存。失敗時の選択と未保存表示 | P・B | `ReturnToTitle_FromMenu_*`、`ReturnToTitle_SaveFails_*`、**`WindowClose_HpOnlyChange_IsNotPassedThrough_FailureStaysUnsaved`**（HP だけの変化でも通常の終了要求を素通ししない＝R1）、**`ExitChoice_RetryByDecideInput_KeepsTheTitleDestination`**（失敗後の「もう一度」を Enter とパッドの決定で選ぶと元の行き先＝タイトルへ＝R3）、実ビルド `close`（版を進めない変化だけ → `Application.Quit` → 別プロセスで HP・犬丸の Down が戻る） | 合格（表示の分かりやすさは H） |
| P6A 23 | Windows の別プロセスで保存と Continue | B | ブリッジ op `p6a-player-smoke`（記録 007 の最終実行：New Game → 正常終了 → Continue、通常の終了要求 → Continue。同じ冒険 ID・残数・エリア・HP・犬丸の Down・未保存なし・プロセス ID が別） | 合格 |
| P6A 24 | Builder 再生成、ID 一意性、安全復帰点、catalog、入力と Camera 単一性 | V・E | `build-phase6-world`→`validate-phase6-world` 合格（**既知報酬・既知仲間の検査を追加**）、`Phase6World_TargetsExist`、`Phase5ValidatorCoverageTests`。入力と Camera の単一性は P5 の Scene 検査と P5.5 の既存必須 `P55UniquenessPlayTests` | 合格 |
| P6A 25 | 保存性能。I/O 完了待ちで凍結しない | B・H | `P6A_性能測定記録.md`：保存契機の連打（p95 採取 0.02ms・要求→完了 50ms／遅い I/O 450ms）と実プレイ中（実キーで歩行・実攻撃で 16 体撃破・休息 8 回、保存 32 回：採取 p95 1ms 未満、フレーム最大 33.9ms（初回メニューの 1 フレーム。対処前は約 300ms））。初回の停止は**原因を特定**（保存ではなく仮 UI の日本語の初回描画。保存なし・GC・入力の切り替えで比較。§6）して**タイトルで先に描く対処**をした | 合格（体感は H） |
| P6A 26 | P3.5 Retry、P4、P5、P5.5 の回帰。既存試遊 Data 非汚染 | R | 記録 007 §6：EditMode 全件・PlayMode 全件・`verify-required-tests`（P4／P5／P5.5／P6A）・P5／P5.5 の検査。生ログと照合結果は証跡一式（記録 007 §7） | 合格（記録 007 §6） |
| P6A 27 | 既存攻撃 VFX の特定・必須参照・再生成・実攻撃からの表示・遷移後の表示 | V・P・H | `validate-phase6-world`（VFX の表示役・素材・**各 Area に配信役が 1 つ**）、`AttackVfx_ShowsInA_AndAfterSlideInB`、**`AttackVfx_RecordsShownTiersAndDirections`**（実キー J と L で**主人公の剣閃 1〜3 段・必殺技 × 下上左右の 16 通りすべてを必須**として実際の表示を確認）、**`EnemySideVfx_ShowFromRealAttacks_InAAndAfterTransitionInC`**（実際の敵の攻撃から**敵の剣閃**、実キー K のジャストガードから**JG 閃光**を A と、スライド 2 回の後の C で。C では仮ボスの**ガード不能の頭上警告**と主人公の必殺技も）、`FeedbackPresenters_*`（配信元の選択＝F06）。観測の記録：`p6a27_vfx_observed.json`・`p6a27_enemy_vfx_observed.json`（証跡一式） | P6A の検証 campaign は合格（**A に配信役が無く JG 閃光等が出なかった欠陥をこの検査で見つけて直した**＝記録 007 §2）。人の目での見栄えの確認は H。P5／P5.5 は未修正（F01・F13、オーナー判断） |

## 人間確認へ渡すもの（未実施）

`README_Phase6A_進行保存試遊.md` §4：歩行・連戦中の引っ掛かり、保存表示（終了の選択肢の行き先）、死亡と中断の説明の分かりやすさ、剣閃・敵の剣閃・JG 閃光・警告の見栄え（表示されることは自動で確認済み）、撤退の感触、同じ Area の中の旅立ち。

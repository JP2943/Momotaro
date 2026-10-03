# P6B 受入条件対応表

仕様 §11 の要件 ID と、実テストの完全名・ログ・実ビルド・人間確認の対応。テストの正本は `P6BRequiredTests.json`（29 名）で、
`verify-required-tests`（manifest `P6B`）が欠落・非 Passed・未許可 Skip を不合格にする。結果と対象 SHA は `P6B_統合受入結果.md`。

区分：**自動**＝テストランナー、**実ビルド**＝`p6b-player-smoke`、**人間**＝README §5（未実施）。

| ID | 確認内容 | 自動テスト | その他の証跡 |
|---|---|---|---|
| P6B01 | 9ノードの前提と費用、三方向混合、未知ID・取得済み・徳不足・前提不足・場所外の無変更 | `EditMode.P6BGrowthTests.ShippedTree_NineNodes_ThreeDirections_CostsAndPrerequisites`（EditMode）<br>`EditMode.P6BGrowthTests.Purchase_MixedDirections_AndRejectionsChangeNothing`（EditMode）<br>`EditMode.P6BGrowthTests.Purchase_InsufficientVirtue_ChangesNothing`（EditMode）<br>`PlayMode.P6BWorldPlayTests.Tree_RealInput_AcquireAllRefundLeaf_EffectsOnPlayer_ContinueRestores`（PlayMode） |  |
| P6B02 | 各効果が設定値どおり。全取得の合計、刀と仲間・通常体幹とジャスガ等への適用範囲 | `EditMode.P6BGrowthTests.Effects_EachNodeAndFullTree_AddUp`（EditMode）<br>`EditMode.P6BGrowthTests.Effects_AppliedToPlayerVitals_RecomputedFromBase`（EditMode）<br>`EditMode.P6BKibidangoUseTests.GrowthMultipliers_DoNotTouchJustGuardReflection`（EditMode）<br>`PlayMode.P6BWorldPlayTests.Tree_RealInput_AcquireAllRefundLeaf_EffectsOnPlayer_ContinueRestores`（PlayMode）<br>`PlayMode.P6BWorldPlayTests.GrowthMultipliers_RealHitWindow_SwordAndSpecialScaled_PoiseOnlyNormal`（PlayMode） | 補足：仲間・環境に掛からないことは構造（倍率は主人公の命中窓口だけ）で担保（後続課題 G11）。計算例は記録 001 §2。 |
| P6B03 | 取得後に新上限で休息・補充・普通敵周期更新・保存が各一回 | `EditMode.P6BGrowthTests.Purchase_RestsOnceAtNewCaps_RefillsAndSavesOnce`（EditMode）<br>`PlayMode.P6BWorldPlayTests.Tree_RealInput_AcquireAllRefundLeaf_EffectsOnPlayer_ContinueRestores`（PlayMode） |  |
| P6B04 | 基礎値からの再計算、取得と取消の反復、遷移・非活動Area再入場・死亡・Loadで非累積 | `EditMode.P6BGrowthTests.Effects_AppliedToPlayerVitals_RecomputedFromBase`（EditMode）<br>`EditMode.P6BGrowthTests.AcquireRefundRepeated_EffectsAlwaysRebuiltFromBase`（EditMode）<br>`PlayMode.P6BWorldPlayTests.Tree_RealInput_AcquireAllRefundLeaf_EffectsOnPlayer_ContinueRestores`（PlayMode）<br>`PlayMode.P6BWorldPlayTests.Kibidango_HitInterrupts_DeathTakesPrecedence`（PlayMode） |  |
| P6B05 | 末端のみ取消可能。複数子を持つテスト用前提グラフでも親を誤って取り消せない | `EditMode.P6BGrowthTests.Refund_OnlyLeaves_WithBranchingFixture`（EditMode） |  |
| P6B06 | 実支出返還、累計不変、使用済み減少、権利一回減少、価格変更後の返還と再取得 | `EditMode.P6BGrowthTests.Refund_ReturnsActualSpend_PriceChangeReacquireAtNewPrice`（EditMode） |  |
| P6B07 | 払い戻し後の上限低下と休息、残数上限、普通敵一周期。不成立・取消・連打時の無変更または一回処理 | `EditMode.P6BGrowthTests.Refund_LowersCaps_RestsOnce_RejectionsAndDoubleInputChangeNothing`（EditMode）<br>`PlayMode.P6BWorldPlayTests.Tree_RealInput_AcquireAllRefundLeaf_EffectsOnPlayer_ContinueRestores`（PlayMode） |  |
| P6B08 | 初期3・上限6・章ごと＋3。上限で追加0の章も処理済み。Load・再通知で重複追加なし | `EditMode.P6BGrowthTests.ChapterRights_Initial3_PlusThreePerChapter_Cap6_NoDuplicates`（EditMode）<br>`EditMode.P6BGrowthTests.ChapterRights_JoinTheCallersSaveUnit`（EditMode） |  |
| P6B09 | 使用可能条件と禁止状態、HP満タン・残数0、同時入力、長押しで連続使用なし | `EditMode.P6BKibidangoUseTests.Start_RejectsFullHpEmptyStockSameFrameActionAndBusy_HoldDoesNotRepeat`（EditMode）<br>`PlayMode.P6BWorldPlayTests.Kibidango_RealKeyAndTrigger_CommitSaves_ContinueRestoresWithoutReheal`（PlayMode） |  |
| P6B10 | 2秒動作、1.5秒で回復と1個消費を同時に一回確定。0.5秒後隙、20％移動、禁止行動・無敵付与なし | `EditMode.P6BKibidangoUseTests.Use_CommitsOnceAt1_5_EndsAt2_0_SlowMove_NoInvulnerability_DropsForbiddenInputs`（EditMode）<br>`PlayMode.P6BWorldPlayTests.Kibidango_RealKeyAndTrigger_CommitSaves_ContinueRestoresWithoutReheal`（PlayMode） |  |
| P6B11 | 確定前後の実被弾、中断、死亡、無敵中の無効接触、かばうとの整合。同フレーム競合の規則 | `EditMode.P6BKibidangoUseTests.Hits_InterruptBeforeCommit_KeepAfter_SameFrameHitWins_IgnoredContactsContinue`（EditMode）<br>`PlayMode.P6BWorldPlayTests.Kibidango_HitInterrupts_DeathTakesPrecedence`（PlayMode） |  |
| P6B12 | Pause・ヒットストップ・長いフレームで時間と一回確定が正しい。開始後HP満タンの扱い | `EditMode.P6BKibidangoUseTests.Time_PauseAndFreezeStop_LongFrameCommitsOnce_FullHpAfterStartClamps_StolenStockAborts`（EditMode） |  |
| P6B13 | 使用中の通常遷移禁止と場外防止、終了後に離れ直さず外向き入力で遷移可能 | `EditMode.P6BKibidangoUseTests.ExitGate_NoRequestWhileUsing_ThenRequestsWithoutRelease`（EditMode）<br>`PlayMode.P6BWorldPlayTests.Kibidango_AtBoundary_NoTransitionWhileUsing_ThenTransitionsWithoutRelease`（PlayMode） | 補足：場外防止は既存の境界（AreaSeamBarrier）のまま。PlayMode で使用中に外向き入力を 2 秒押しても遷移・場外なし。 |
| P6B14 | 使用確定の保存要求、HPと残数の整合、使用中Snapshot非破壊、確定前後の正常終了とContinue | `EditMode.P6BKibidangoUseTests.Commit_RequestsOneSave_WithHpAndStockConsistent`（EditMode）<br>`PlayMode.P6BWorldPlayTests.Kibidango_RealKeyAndTrigger_CommitSaves_ContinueRestoresWithoutReheal`（PlayMode） | 補足：使用中 Snapshot 非破壊は「保存の採取後も使用が続く」で確認（PlayMode）。 |
| P6B15 | 保存失敗から復帰しても使用を巻き戻さず、権利・徳・取得の成立を維持。追加要求を取りこぼさない | `PlayMode.P6BWorldPlayTests.SaveFailure_GrowthRefundAndUseStay_RetryWritesLatest`（PlayMode） |  |
| P6B16 | 新規保存項目を非初期値でRoundTrip、旧版移行、欠損・未知ID・前提矛盾拒否、P6A保存維持 | `EditMode.P6BGrowthTests.Save_V3_RoundTripsRightsChaptersGrowthAndStock`（EditMode）<br>`EditMode.P6BGrowthTests.Save_V1AndV2_MigrateToInitialRightsAndEmptyChapters`（EditMode）<br>`EditMode.P6BGrowthTests.Save_V3_RejectsBadRefundGrowthAndStock`（EditMode）<br>`PlayMode.P6BWorldPlayTests.Tree_RealInput_AcquireAllRefundLeaf_EffectsOnPlayer_ContinueRestores`（PlayMode） |  |
| P6B17 | Builder再生成、StableId・前提循環・未知参照・値検査。生成後もUIと入力とVFXが接続 | `EditMode.P6BGrowthTests.ShippedTree_NineNodes_ThreeDirections_CostsAndPrerequisites`（EditMode）<br>`EditMode.P6BGrowthTests.GraphCheck_RejectsCycleForeignReferenceZeroEffectAndNegative`（EditMode）<br>`PlayMode.P6BWorldPlayTests.Tree_RealInput_AcquireAllRefundLeaf_EffectsOnPlayer_ContinueRestores`（PlayMode） | 補足：Builder／Validator（`build-phase6b-world`／`validate-phase6b-world`）。Data 検証（`validate-project-data`）で StableId 一意。 |
| P6B18 | UIの効果・費用・返還表示、ゲームパッド操作、キャンセル、解放待ちとGameplayへの入力漏れなし | `PlayMode.P6BWorldPlayTests.Tree_RealInput_AcquireAllRefundLeaf_EffectsOnPlayer_ContinueRestores`（PlayMode） | 補足：新規文字の初回描画は `PadMenuNavigator.PrewarmCharacters` に追加（タイトルで先描き）。 |
| P6B19 | 実ビルドの別プロセスで成長・権利・使用後残数・HP を復元。変更共有経路と既存必須ゲートの回帰 | （ランナー外） | **実ビルド** `p6b-player-smoke`（growth → continue。13 項目の一致）。共有経路の回帰：EditMode 全件・PlayMode 全 6 分割、`verify-required-tests` P4／P5／P5.5／P6A／P6B、`p6a-player-smoke` |

## 人間確認（未実施）

README_Phase6B_成長回復試遊.md §5 の 4 点（成長の分かりやすさ／回復中の低速移動と行動制限／1.5 秒確定と被弾中断／境界での動き）。
自動検査・ログ確認・実表示確認と区別し、結果はオーナーの試遊後に記録する。

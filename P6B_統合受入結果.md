# P6B 統合受入結果

仕様の正本は `桃太郎プロジェクト P6B 成長ときびだんご実装仕様 v1.0.md`（2026-10-03）。P6A の基盤契約
（`桃太郎プロジェクト P6先行実装仕様 v1.0 承認用.md`、`P6A_統合受入結果.md` 記録 001〜008）を継承する。
保存項目の台帳は `P6_SaveInventory.md`。

親ブランチ：`phase/6-progression-save`（オーナー指示「既存ブランチを継続」）／**親 SHA `6162620`**
（P6A 受入記録 008。オーナー commit `9401d7b`「P6A修正2」＝マテリアルと ProjectSettings のみ、を含む）。
`main` は P6A を含まないので基点にしない（仕様 §1）。main への統合は別指示を待つ。

---

## 記録 001：P6B 00（基点・既存契約・入力・保存台帳の確認）

### 1. 基点

| 確認 | 結果 |
|---|---|
| 仕様作成時のリモート先端 | `6162620ae1af73d5869c06aa8e4e13840afc9b42` |
| 作業ブランチの先端 | `6162620`（直前 `9401d7b`、`86e1b70`） |
| 作業ツリー | Scripts／Tests／Data／Scenes に差分なし。音声・画像の `M` は PC 側シェルに git-lfs が無いことによる見かけ（CLAUDE.md） |
| 実行権限 | PlayMode・EditMode 全件・Builder は確認なしで実行してよい（オーナー回答 2026-10-03） |

### 2. 基礎値（`SO_Player_Momotaro`・既存の攻撃 Data。**書き換えない**）

| 項目 | 値 | 出所 |
|---|---|---|
| 最大 HP | 100 | `PlayerData._maxHp` |
| 最大スタミナ | 100（回復 25/秒、待ち 1.0 秒） | `PlayerData._maxStamina` |
| 攻撃力／防御 | 100／20 | `CharacterData` |
| 通常 1〜3 段の HP 倍率 | 1.0／1.1／1.5 → 攻撃側寄与 10／11／15 | `SO_Player_Attack_0x`（寄与＝攻撃力×倍率×0.1） |
| 必殺の HP 倍率 | 7.0 → 寄与 70（防御 50% 無視・スタン 1.5） | `SO_Special_Momotaro` |
| 通常 1〜3 段の体幹 | 8／8／15（状況補正前） | 同上 |
| 必殺・JG の体幹 | 30（必殺）／JG 反射は防御側設定値 | 対象外 |

**計算例（全取得：刀 1.20・体幹 1.10）。** 攻撃側寄与に倍率を 1 回だけ掛け、整数化は既存どおり
対象側 `HpDamageCalculator.ResolveFinal`（四捨五入）だけで行う。

| 攻撃 | 基礎の寄与 → 防御 20 の対象 | 1.20 の寄与 → 防御 20 の対象 |
|---|---|---|
| 1 段 | 10 → 8.33 → **8** | 12 → 10.0 → **10** |
| 2 段 | 11 → 9.17 → **9** | 13.2 → 11.0 → **11** |
| 3 段 | 15 → 12.5 → **13** | 18 → 15.0 → **15** |
| 体幹 1／3 段 | 8／15 | 8.8／16.5（体幹は float のまま。既存どおり） |

### 3. P6B campaign の仮値

| 項目 | 値 | 理由 |
|---|---|---|
| きびだんご初期最大数 | 3 | 仕様 §3 |
| きびだんご基礎回復量 | **50**（＝ceil(100/2)。Data の固定値） | 仕様 §3。成長後の最大 HP に連動しない |
| テスト用の HP・敵攻撃倍率 | **1.0（無効）** | P6A campaign は死亡試験用に主人公 HP×0.5・敵攻撃×2 を持つが、P6B は効果を実定義で測るため掛けない。P6A の値は変更しない |
| 試遊の徳 | 一度きり報酬の合計 300 以上 | 仕様 §10 |
| 保存スロット | P6B 専用（`p6b_slot0`）。P6A は従来の `slot0` | 仕様 §9「専用 campaign・保存領域」 |

### 4. 入力の割当

既存の `IA_Momotaro.inputactions`（Gameplay マップ）を調べた結果：

| 既存 | キーボード | パッド |
|---|---|---|
| Move | WASD | 左スティック・十字キー |
| Attack／Guard／Step／Special | J／K／Space／L | 西／RB／東／北 |
| CompanionSkill／SwitchCompanion | U／Tab | LB／十字上 |
| UseKintan（未使用の予約） | Q | 十字下（Move の十字キーと重なる） |
| Interact／Map／Pause | E／M／Esc | 南／Select／Start |

**採用：新規アクション `UseKibidango` ＝ キーボード `F`、パッド `左トリガー（LT）`。** どちらも Gameplay・UI・
Dialogue マップ、および直接ポーリングしている既存箇所（R＝P3.5 の Retry、Y／N／C／T／Q＝タイトル・終了メニュー、
数字＝お地蔵様メニュー）と重ならない。`UseKintan` の十字下は移動と競合するので流用しない。入力は既存の
`PlayerInputAdapter` → `PlayerInputState`（押下エッジのラッチ・解放待ち）へ統合し、直接ポーリングは足さない。

### 5. 変更対象（新しい第二系統は作らない）

| 要素 | 既存の所有者 | 扱い |
|---|---|---|
| 成長ノード | `SkillNodeData`（費用・前提・排他・最大 HP） | 効果欄を拡張（攻撃倍率・最大スタミナ・体幹倍率・回復量・最大数）。P6A ノードは最大 HP だけのまま |
| 効果の再計算 | `CampaignCatalog.MaxHpBonusOf` | 取得 ID から全効果をまとめて作り直す `GrowthEffectsOf` へ一般化（最大 HP 加算は同じ値を返す） |
| 能力への反映 | `IRestTarget.ApplyMaxHpBonus` → `PlayerVitalsHolder` | 効果一式を置き直す口へ一般化。攻撃・体幹倍率は `PlayerStateController` の命中窓口で 1 回だけ掛ける |
| 取得と休息 | `ShrineProcedures.PurchaseGrowth`／`Rest` | 再利用。払い戻しを同じ形で追加 |
| 払い戻し権利・章 | なし | `PlayerProgressState` に追加（徳・成長と同じ所有者） |
| きびだんご残数 | `GameSessionState.Kibidango` | 上限は `KibidangoCapacityOf` を成長込みに。使用の確定は Session の 1 更新 |
| 使用動作 | なし | `PlayerStateController` に状態 `UseItem` を足す（遷移受付 `IsFreeToTravel` は Idle／Move だけなので自動で拒否側） |
| 保存 | schemaVersion 2 | 3 へ。版 1・2 は権利 3・章集合空へ明示移行 |
| 検証ワールド | `Phase6WorldBuilder`（P6A 専用のパス・ID） | 設定（プロファイル）で P6A／P6B を切り替えて再利用。P6A の生成物は変えない |

---

## 記録 002：P6B 01〜04（実装）と途中の確認

### 1. 実装したもの

| 工程 | 内容 | 主な場所 |
|---|---|---|
| P6B 01 | `SkillNodeData` に効果欄（刀倍率・最大スタミナ・通常体幹倍率・回復量・最大数）を追加。9 ノードの Data。前提の循環・campaign 外参照・ゼロ効果・負値の検査を Data 検証とカタログ構築の**同じ判定**（`SkillGraphCheck`）で行う。効果は `CampaignCatalog.GrowthEffectsOf`（基礎 0 から取得 ID で毎回作る）→ `IRestTarget.ApplyGrowthEffects` → 主人公の最大 HP・最大スタミナ（`PlayerVitalsHolder.ApplyGrowth`）と命中窓口の倍率（`PlayerStateController.SetGrowthMultipliers`）。取得は `ShrineProcedures.CanPurchase`（UI と処理の共通判定）→ `PurchaseGrowth`（効果 → 休息 1 回 → 保存 1 件） | `Data/Progression/SkillNodeData.cs`・`SkillGraphCheck.cs`、`Gameplay/Progression/GrowthEffects.cs`、`Gameplay/Session/CampaignCatalog.cs`・`ShrineProcedures.cs` |
| P6B 02 | 払い戻し（末端だけ・実支出返還・権利 −1・効果再計算・休息・保存を 1 まとめ）、権利（初期 3・上限 6）、章の接続口（未処理の既知章で min(6, 現在＋3)、増加 0 でも処理済み）。保存形式 3（`refund {rights, chapters[]}`）、版 1・2 は `HasRefundData=false` として読み、候補構築で明示移行（権利＝campaign 初期値・章＝空）。検証：権利 0〜上限、既知章・重複、**成長込みの上限**と残数 | `PlayerProgressState.cs`、`ShrineProcedures.cs`、`SaveSnapshot.cs`・`SaveSnapshotValidator.cs`・`SessionRestorer.cs`・`SaveJsonCodec.cs` |
| P6B 03 | 入力 `Gameplay/UseKibidango`（F／LT）を既存の入力ラッチへ統合（`IItemUseInput`）。主人公の状態 `PlayerState.UseItem`：開始条件・2.0 秒・1.5 秒の確定予約（**確定は LateUpdate**＝同フレームの被弾・死亡が先に解決される）・20% 移動・禁止行動の押下を捨てる・ガード／必殺は解放待ち。有効な被弾（`ISpecialChargeCancel` と同じ同期の入口）で確定前なら中断。確定は `GameSessionState.TryCommitKibidangoUse`（残数 −1 と回復を 1 まとめ、保存要求 1 件）。出入口は使用中は要求を出さない（`AreaExitGate.Tick(..., requestAllowed)`） | `PlayerStateController.cs`、`PlayerInputState.cs`、`PlayerInputAdapter.cs`、`Gameplay/Session/KibidangoUse.cs`、`AreaExitGate.cs`・`AreaExitGateDriver.cs` |
| P6B 04 | 成長の木の仮 UI（三列・状態の札・効果／費用／前提／取得後の能力値・権利と徳の常時表示・確認は既定「いいえ」・理由表示・キーボードとパッド）、きびだんご HUD（残数／上限・HP・使用中のバー）、新規文字の先描き。Builder／Validator は P6A の構成を**プロファイル**（`Phase6Profile.P6A／P6B`）で再利用し、専用 campaign・Data・Scene・保存スロットを生成。ブリッジ op `build-phase6b-world`／`validate-phase6b-world`／`p6b-player-smoke`、manifest 鍵 `P6B` | `Infrastructure/World/GrowthTreeScreen.cs`・`CampaignShrineService.cs`、`Editor/Phase6/*`、`Editor/Bridge/*` |

### 2. 変更した既存契約（P6A からの差分）

| 契約 | 変更 | 影響 |
|---|---|---|
| `IRestTarget.ApplyMaxHpBonus(int)` | **`ApplyGrowthEffects(in GrowthEffects)` へ置換**（最大 HP は同じ値） | 実装は `AreaActorTransferPort` だけ。P6A のテスト用 Fake を追従 |
| `CampaignCatalog.KibidangoCapacityOf` | 基本値 → 基本値＋成長 | P6A のノードは最大数効果を持たないので P6A の値は不変 |
| 保存形式 | 版 2 → **3**。版 1・2 は読める（移行） | P6A の codec テストの「未対応の版」を 4 へ、版 2 に `refund` は未知の欄 |
| 保存スロット | campaign ごと（`AreaCatalogData.SaveSlotName`。P6A は `slot0` のまま） | `CampaignSaveService.UseSlot`。冒険を結んでいる間は切り替えない |
| `GameSessionState.InitializeNewAdventure` | 引数に初期権利（既定 0）。New Game の経路は campaign の初期値を渡す | 既存呼び出しは不変 |
| `StaminaState` の最大値 | 読み取り専用 → `SetMax`（上限超過分だけ切り詰め） | 現在値は回復しない（休息が回復） |
| `Phase6WorldIds` | 定数 → プロファイル依存のプロパティ。**P6A の値は従来と同じ** | P6A の Builder 出力は不変（後述） |
| P6A のカタログ・ノード | 新しい欄が既定値で書かれた（回復量 0＝使用動作なし、`slot0` 等） | 挙動は不変 |

### 3. 判断（仕様の範囲内の通常判断）

- **P6B の Area・遭遇戦の StableId は P6A と別**（`area_p6b_*`／`encounter_p6b_*`）。最初は共通にしたが、実ビルド前の Data 検証が
  プロジェクト全体の StableId 重複（Area・遭遇戦は Data 資産）で拒否した。入口・出入口・お地蔵様・配置物は Area 内の ID なので共通のまま。
- テスト用の倍率（主人公 HP×0.5・敵攻撃×2）は P6B の campaign に**掛けない**（記録 001 §3）。
- 使用中の主人公の絵は待機の絵のまま（専用クリップなし。後続課題 G10）。
- 犬丸がかばって主人公に被弾が成立しなかった場合は継続（仕様 §7）。PlayMode の被弾テストは犬丸を Down させてから主人公へ当てる。

### 4. 途中で見つけて直したもの

| 事象 | 原因 | 対応 |
|---|---|---|
| 実ビルドが Data 検証で失敗（重複 6 件） | 上記 §3 の StableId | P6B 側の ID を分けた。`validate-project-data` 合格 |
| PlayMode：保存失敗のテストの次に成長の木のテストを走らせると New Game の到着が失敗（「主人公の生存値を復元できません」） | テストの後片付けが P6B の A を読み直しており、その主人公が**前のテストの成長込み**の値で作られ、次の New Game の移動がその値を「運ばれてきた値」として新しい冒険の主人公（基礎値）へ渡そうとして値域で断られた。本番の New Game はタイトル（主人公なし）から始まるので起きない | P6B の PlayMode の後片付けを**主人公の居ない空の Scene**へ移す形にした |
| `P5ContractTests.TransferInventory_*` 失敗 | `PlayerVitalsHolder.MaxStaminaBonus` が持ち越し台帳に未分類 | `P5_ActorTransferInventory.md` に「再構築」で追加 |

### 5. テストが欠陥を捕まえることの確認（一時的に修正を外した版）

| 外した修正 | 落ちたテスト |
|---|---|
| 払い戻しの末端判定（`HasAcquiredDependent`） | `Refund_OnlyLeaves_WithBranchingFixture`（親を払い戻せた）、`Refund_LowersCaps_*`（非末端） |
| 同フレームの被弾優先（確定直前の被弾チェック） | `Hits_InterruptBeforeCommit_*`（同フレームで確定してしまう） |
| 使用中は出入口が要求しない | `Kibidango_AtBoundary_*`（使用中に要求して断られ、離れ直し待ちになる） |

いずれも元へ戻してコンパイル・再実行済み（`MUTATION` の残りが無いことを確認）。

### 6. ここまでの実行（すべて記録。最終の確認は記録 003）

- コンパイル：都度 `compile-status`（最終 0 警告）。
- `build-phase6b-world` 3 回、`validate-phase6b-world` 3 回（合格。警告 1 件は P6A と同じ「この Area に遭遇戦なし」）。
- `build-phase6-world`／`validate-phase6-world` 各 1 回（P6A：合格。P6A の Scene は内部 ID の並び替えだけだったので HEAD へ戻した。
  P6A の Data は新しい欄の既定値の書き込みだけ）。`validate-project-data` 合格。
- EditMode：P6A 3 クラス 51 件、P6B 2 クラス（22 → 23 件）、`P5ContractTests` 36 件、全件 1 回（1894／1895。上記の台帳 1 件を直して再実行済み）。
- PlayMode：`P6BWorldPlayTests` を段階的に 9 回（最終 6／6）。
- 実ビルド `p6b-player-smoke` 3 回（1・2 回目はビルド前の Data 検証で失敗、3 回目で 13 項目すべて一致）。

---

## 記録 003：P6B 05（必須受入・回帰・実ビルド・証跡）

**対象コード SHA `b79448e`**（親 `6162620`）。証跡は `Evidence/P6B_b79448e/`（README に実行順と各ファイル）。
実行はすべて Unity 6000.3.20f1 の Editor 常駐ブリッジから、`b79448e` を作業ツリーに置いた状態で行った（コンパイル 0 警告を確認してから）。

### 1. 結果

| 実行 | 内容 | 結果 |
|---|---|---|
| `fe1` | EditMode 全件 | **1895／1895 Passed**、Skip 0（予定 1895、完走） |
| `fp1` | PlayMode：`P55SlideTransitionPlayTests` | 64／64 |
| `fp2` | PlayMode：`P55RoundTripPlayTests` | 13／13 |
| `fp3` | PlayMode：P5.5 その他 12 クラス | 72／72 |
| `fp4` | PlayMode：その他 22 クラス（split4） | 115／115 |
| `fp5` | PlayMode：`P6AWorldPlayTests` | 20／20 |
| `fp6` | PlayMode：`P6BWorldPlayTests` | 6／6 |
| `vbP4`／`vbP5`／`vbP55`／`vbP6A`／`vbP6B` | `verify-required-tests`（上の 7 実行と照合） | 必須 226／226・93／93・293／293・73／73・**29／29**、未説明の Skip なし |
| `sb1` | 実ビルド `p6b-player-smoke`（別プロセス：成長・払い戻し・実入力 F の使用 → 正常終了 → Continue） | **13 項目すべて一致**（成長 4 件・権利 2・徳 190・上限 4・残数 3・HP 90・最大 HP 110・刀 1.20 ほか） |
| `sa1` | 実ビルド `p6a-player-smoke`（共有経路の回帰：New Game／終了要求／Continue・実プレイ・保存性能） | すべて OK。実プレイ 16 撃破・フレーム p99 16.7ms・最大 22.8ms・採取最大 0.56ms。保存契機の連打（通常／遅い I/O 200ms）フレーム最大 17.1／17.4ms |

PlayMode の分割は P6A と同じ 5 本に `P6BWorldPlayTests` を足した 6 本（計 290）。

### 2. 区別（仕様 §11）

- **自動検査**：上表のテストランナー実行と照合（必須 29 名は `P6BRequiredTests.json`）。
- **実ビルド**：`sb1`（P6B 19）、`sa1`（P6A の共有経路）。
- **ログ確認**：Builder／Validator（`build-phase6b-world`／`validate-phase6b-world` 合格）、`validate-project-data` 合格（記録 002 §6）。
- **実表示確認**：成長の木・HUD は PlayMode で実入力から状態を確かめたが、**画面の見た目は人が見ていない**。
- **人間確認：未実施**（README §5 の 4 点）。

### 3. 残るもの

`P6B_後続課題.md`（G01〜G12）。P6B の完了は**成長要素全体の完成ではない**（全ツリー・新技・最深部・ジャスト回避・本番数値は未完。
第一章の試遊後に成長と戦闘の調和を検討する）。main への統合は別指示を待つ。

---

## 記録 004：レビュー `ddb2d19` への対応（R1・D1）

対象：`Momotaro_P6B_Review_ddb2d19.md`。**対象コード SHA `47b223e`**（親 `ddb2d19`。`ddb2d19` はオーナーの commit で、仕様書と Recovery Scene の追加のみ）。
証跡は `Evidence/P6B_47b223e/`。

### 1. R1：使用が終わるフレームの禁止入力

**指摘どおりの欠陥だった。** `TickItemUse` が 2 秒到達で使用を終えたフレームは `ApplyItemUseFrame` を通らず、そのフレームに届いた
F／J／Space／E の押下ラッチが残っていた（次のフレームに追加使用・攻撃・回避・Interact として発火しうる）。

- 修正：`PlayerStateController` で、**フレーム開始時に使用中だった**なら、使用がそのフレームで終わっても禁止行動の押下を捨てる
  （`DiscardForbiddenPresses`。使用中の各フレームと同じ処理を 1 か所にまとめた）。移動は通常処理へ渡し、速度は `EndItemUse` が戻す。ガード・必殺の解放待ちは従来どおり。
- テスト：`P6BKibidangoUseTests.EndFrame_PressesAtTheBoundaryAreDropped_RepressIsAccepted`。確定済みの終了直前（経過 1.96〜1.98 秒）から 2 秒を跨ぐフレームに
  F／J／Space／E をそれぞれ押したまま進め、そのフレームと次の 2 フレームで発火しないこと、離して押し直せば受け付けることを見る（攻撃は実際の 3 段コンボ Data を繋いで確認）。
- **修正を外した版でこのテストが落ちることを確認**：レビューの再現手順と同じく、F の押しっぱなしで次のフレームに 2 回目の使用が始まった
  （「次のフレームにも発火しない」で失敗）。元へ戻して再コンパイル済み。長いフレームで確定と終了を同時に跨ぐ既存検査（`Time_*`）はそのまま緑。

### 2. D1：使用中の正常終了・終了保存の失敗からの復帰

コードの変更は無し（証跡の追加）。**対応表では「正常中断（製品の終了経路）」と「自動保存からの再起動（テスト用の再起動）」を分けて書いた。**

| 確認 | 経路 | 結果 |
|---|---|---|
| `P6BWorldPlayTests.Kibidango_NormalExitMidUse_BeforeAndAfterCommit_ContinueMatchesThatMoment` | 使用中（0.6 秒＝確定前／1.7 秒＝確定後の後隙）に**実キー Esc → T**（ゲーム内メニューのタイトル復帰＝`SaveBeforeExit`）→ タイトル → Continue | 保存と Continue 後が各時点の値（0.6 秒：HP 40・残数 3／1.7 秒：HP 90・残数 2）。2.2 秒待っても遅れて回復・消費しない。使用は持ち越さない |
| `P6BWorldPlayTests.Kibidango_ExitSaveFailsMidUse_BackToGame_ResumesSameUseState` | 同じ 2 時点で終了前の保存を失敗させ、選択中 1 秒待ってから**実キー ↓ → Enter で「ゲームへ戻る」** | 選択中は経過が止まり（差 0）、確定状態・HP・残数も不変。戻ると同じ経過から続く（やり直さない・飛ばさない）。確定前の側はその後 1 回だけ確定・1 個消費、確定済みの側は再消費しない。復旧後の再試行で使用後の最新（HP 90・残数 2）を書く |
| 実ビルド `p6b-player-smoke` の `useclose_0_6`／`useclose_1_7` | 使用中に**通常の終了要求**（`Application.Quit` → wantsToQuit → 保存 → 終了）→ 別プロセスで continue | 0.6 秒：終了時 未確定・HP 40・残数 3 → 別プロセスでも HP 40・残数 3、使用なし。1.7 秒：確定済み・HP 90・残数 2 → 同じ。8 項目×2 すべて一致 |

### 3. 最終確認（`47b223e`）

| 実行 | 内容 | 結果 |
|---|---|---|
| `ge` | EditMode 全件 | **1896／1896**（+1 は R1 のテスト） |
| `gp1`〜`gp6` | PlayMode 6 分割 | 64・13・72・115・20・**8**（+2 は D1）＝ 292／292 |
| `vgP4`〜`vgP6B` | `verify-required-tests`（`ge,gp1,…,gp6`） | 226／226・93／93・293／293・73／73・**32／32**、未説明の Skip なし |
| `gsb` | 実ビルド `p6b-player-smoke` | 成長の復元 13 項目＋使用中の終了 16 項目、すべて一致 |
| `gsa` | 実ビルド `p6a-player-smoke`（スモークの駆動ファイルを変えたので回帰） | OK 12／NG 0。実プレイ 16 撃破・p99 16.7ms・最大 23.6ms |

ほかに途中で実行したもの：コンパイル都度（0 警告）、`P6BKibidangoUseTests` 3 回（修正外しの確認 1 回を含む）、
D1 の PlayMode 3 回（1・2 回目はテスト側の待ち方の誤り：再試行の送出前に状態を読んだ・戻った直後の数フレームの進みを許していなかった。製品の挙動は期待どおり）、
`P6BWorldPlayTests` 全 8 件 1 回、`p6b-player-smoke` 1 回（追加後の初回）。EditMode 全件が作り直す P5 の Scene は内部 ID の並び替えだけなので HEAD へ戻した。

人間確認（README §5 の 4 点）は未実施のまま。

## 記録 005：人間試遊の報告「きびだんご使用中に被弾しても HP が回復した」の切り分け

対象：オーナー経由の GPT 依頼（人間試遊での観測）。**対象コード SHA `30bd6a6`**（親 `d4309a4`→`f9d5929`。製品コードは `47b223e` から変更なし。
`d4309a4`・`30bd6a6` はテストと記録だけ）。証跡は `Evidence/P6B_30bd6a6/`。

### 1. 結論

**再現しなかった。** 配置された敵の通常の攻撃経路で、確定（1.5 秒）前に主人公へ実ダメージが入った試行は**すべて**その場で中断し、
元の終了時刻（2.0 秒）を過ぎても回復も消費も起きなかった。確定後の被弾・犬丸の完全なかばい・被弾後無敵中の接触は、いずれも仕様どおりの挙動だった。
依頼の指示に従い、**推測による製品コードの変更はしていない**（変更したのはテストと診断出力だけ）。

人間試遊で「被弾したのに回復した」と見えうる、仕様どおりの経路は次の 3 つ（実機の時系列で各々を観測。どれが当たったかはこちらでは決められない）。

| 見え方 | 実際に起きていること | 観測例（時刻は使用開始からの秒） |
|---|---|---|
| 敵の攻撃が当たったように見えたが回復した | **犬丸がかばった**（主人公に被弾が成立していない → 継続して確定） | かばい 0.24 → 確定 1.52（HP 100）→ 終了 2.02 |
| 被弾したが回復は取り消されなかった | **確定（1.5 秒）の後の被弾**。回復と消費は 1.5 秒で済んでおり、被弾で残り 0.5 秒の動作だけ中断 | 確定 1.52（HP 100・残数 2）→ 被弾 1.65（HP 99）→ 終了（Hurt）。確定 1 回 |
| 攻撃に触れたが減らずに回復した | **被弾後無敵の間の接触**（Evade として解決され、被弾が成立しない） | 被弾結果=Evade・適用 HP 0（確定前の別の被弾の直後に観測） |

### 2. 確認した場面

| 項目 | 内容 |
|---|---|
| Scene | `SCN_Phase6B_AreaA`（P6B の A）。遠距離は A 東の出口から実キーで B へ移り、B 南の遭遇戦を開始 |
| 敵 | A：`PF_Enemy_Melee_Prototype_field_p6_a_01`（配置された普通敵・近接）。B：`PF_Enemy_Ranged_Prototype_1`（遭遇戦の遠距離敵） |
| 攻撃の経路 | 近接：敵の攻撃機械 `EnemyAttackController` → 実 Hitbox の物理判定 → 主人公の被弾入口 `PlayerVitalsHolder`。遠距離：`EnemyProjectile` の飛翔と接触 → 同じ被弾入口。**テストからの直接の被弾呼び出しは使っていない** |
| 入力 | Input System の仮想キーボードで**実キー F を 3.2 秒押し続ける**（使用開始 → 押しっぱなしのまま元の終了時刻を越える） |
| 条件 | ①A 近接・犬丸 Down・敵の攻撃の後隙で開始 ②A 近接・犬丸 Down・敵の予備動作中に開始 ③A 近接・犬丸あり・予備動作中に開始 ④B 遠距離・犬丸 Down・予備動作中に開始。各条件で被弾 3 回まで（最大 8 試行） |
| 試行ごとの準備 | HP 60・残数 3（上限まで補充）。敵の攻撃力を 0.3 倍（被弾後に死なず、元の終了時刻後まで観測を続けるため。判定・時機は変えない）。生きている敵がいなければ `AreaFieldEnemyDirector.RebuildNow()` |
| 記録 | 試行ごとに、使用開始・犬丸のかばい・被弾結果（Damage／Evade 等と適用 HP）・確定・使用終了の時刻（使用開始からの経過とフレーム番号）、HP、残数。`_bridge/p6b_kibidango_hit_timeline.txt` |

### 3. 実機の時系列（最終確認 `lp` の記録。全文は証跡 `p6b_kibidango_hit_timeline.txt`）

| 条件 | 有効な試行 | 最初の実ダメージ（経過秒） | 確定 | 残数 | 中断 |
|---|---|---|---|---|---|
| ① A 近接・Down・後隙 | 3 | 0.450・0.619・0.259 | 0 | 3→3 | 被弾と同じ／次のフレームに Hurt で終了 |
| ② A 近接・Down・予備動作 | 3 | 0.869・0.262・0.901（うち 2 試行は先に犬丸のかばいあり） | 0 | 3→3 | 同上 |
| ③ A 近接・犬丸あり・予備動作 | 2（試行 3 は敵の攻撃が始まらず、4〜8 は敵が残っていない） | 0.263・0.259 | 0 | 3→3 | 同上 |
| ④ B 遠距離（飛び道具）・Down | 3 | 0.418・0.487・0.498 | 0 | 3→3 | 同上 |

- どの試行も、**被弾後に F を押し続けたまま 2.0 秒（元の終了時刻）を越えて 3.2 秒まで見ても HP は増えず、残数も減らない**（その後の HP の動きは後続の被弾による減少だけ）。
- 押し続けても中断後に使用が再開しない（開始は各試行 1 回）。
- 「犬丸 Down」の条件でかばいが記録されているのは、犬丸が Down から自然に起き上がったため。かばいの後に主人公へ実ダメージが入ると、その時点で中断している（②の試行 1・3）。
- 飛び道具（④）の直後に `Evade`（被弾後無敵による拒否、適用 HP 0）を観測。これは中断理由にならないが、どの試行もそれより先の Damage で既に中断していた。
- 確定後の被弾（§1 の表 2 行目）は途中の実行 `h3p` で観測した（そのときの記録ファイルは次の実行で上書きされたので、数値はここに書き写したものが残り）。
  最終版では確定後の被弾が出た場合も「確定 1 回・残数 −1・残り動作の中断」を判定する。

### 4. コード上の経路（なぜ遅れて回復しないか）

1. 敵の Hitbox／飛び道具の接触 → `PlayerVitalsHolder` の被弾解決。回避（被弾後無敵を含む）・ジャストガード・ガード・**かばう**が成立したら、そこで返る（主人公に被弾は成立しない）。
2. どれも成立しなければ `DamageApplication.ApplyHpDamage`（主人公の HP を減らす唯一の経路）の**直後に同期して** `CancelSpecialChargeOnHit` を呼ぶ。
   `PlayerStateController` はここで使用中なら `_itemHitDuringUse` を立てる。**Hurt（被弾硬直）の起動を条件にしていない**ので、硬直が起きない被弾でも立つ。
3. 使用の確定 `ResolveItemUseCommit` は `LateUpdate` で、`_itemHitDuringUse`・死亡・Hurt のいずれかがあれば確定せずに中断する。敵の命中は Update 側なので、**同じフレームの被弾は確定より先に効く**。
4. 中断で使用状態を畳むので、元の 2.0 秒の時点に確定処理が走る経路は残らない。確定済みなら `_itemCommitted` で二重確定しない。

受入の各点との対応：

| 依頼の受入条件 | 確認 |
|---|---|
| 1.5 秒前の有効な被弾で中断、元の終了時刻後も回復・消費なし | 本記録 §3（実攻撃・実キー）。EditMode `Hits_InterruptBeforeCommit_*` の 1) |
| 確定境界の同フレームは被弾・死亡が優先 | EditMode `Hits_InterruptBeforeCommit_*` の 4)。死亡は PlayMode `Kibidango_HitInterrupts_DeathTakesPrecedence`（実の致死被弾で確定 0） |
| 確定後の被弾は残り動作を中断し、二重確定しない | EditMode `Hits_*` の 3)。実攻撃で `h3p` に観測、最終版テストに判定あり |
| 無効な接触・犬丸の完全なかばいでは継続、主人公も被弾すれば中断 | EditMode `Hits_*` の 2)・5)。実攻撃の ②（かばいの後の実ダメージで中断）・§1 の表 1 行目 |
| 押し続けても中断後に自動で再使用しない | 本記録 §3（F を 3.2 秒押し続け、開始 1 回）。EditMode `Start_Rejects*_HoldDoesNotRepeat` |
| 終了・保存・再開の既存修正を保つ | 製品コード変更なし。`lp` で `Kibidango_NormalExitMidUse_*`・`Kibidango_ExitSaveFailsMidUse_*` 再実行 Passed |

### 5. 既存テストが何を見ていなかったか

欠陥は見つからなかったので「見逃した」ではないが、**実攻撃の経路での確認が無かった**のは事実で、今回の報告を既存テストだけでは否定できなかった。

- EditMode `Hits_*` は `PlayerVitalsHolder` の被弾入口へ直接 Hit を渡す（敵の攻撃機械・物理判定・飛び道具を通らない）。
- PlayMode `Kibidango_HitInterrupts_*` は実 Scene だが、確定前の被弾はテストの `HitPlayer`（被弾入口への直接呼び出し）で与えていた。致死だけが実被弾。
- どちらも、**敵の攻撃が実際に当たる時機**（予備動作・後隙・犬丸のかばいとの前後・飛び道具の到達）を含んでいない。

これを埋めるため `PlayMode.P6BWorldPlayTests.Kibidango_RealEnemyAttack_Timeline_InterruptsBeforeCommit` を足した（`d4309a4`、試行の数え方を `30bd6a6` で補強）。
P6B11 の必須に登録（`P6BRequiredTests.json` 33 件）。判定：確定前の実ダメージ → 確定 0・残数不変・被弾後の HP 最大値が被弾直後以下・使用終了／
確定後 → 確定 1・残数 −1・使用終了／かばいだけ → 確定 1／全試行で開始 1 回以下。**確定前の実被弾を 1 回も観測できなければ不合格**（何も検証していない緑を防ぐ）。

製品に欠陥が無いので「修正を外して落ちることの確認」はできない。代わりに、判定が中断を見ていることは、確定前の被弾後に `ResolveItemUseCommit` を通る
既存の EditMode（同フレーム競合）で担保している。

### 6. 実行の記録（すべて Unity 6000.3.20f1 の Editor 常駐ブリッジ）

| 実行 | 対象 | 結果 |
|---|---|---|
| `h1p`・`h2p`・`h3p` | 実攻撃テスト単体（作成中の版） | 3 回とも Passed。初期の版では敵が倒れて観測数が減ったため、条件の順序と敵の作り直しを追加。h3p で確定後の被弾を観測 |
| `ke` | EditMode `P6B(Growth\|KibidangoUse)Tests` | 24／24 |
| `kp` | PlayMode `P6BWorldPlayTests` 全 9 件 | 8／9。`Kibidango_AtBoundary_*` が 1 回落ちた（「終了後は離れ直さずに遷移する」で遷移要求 0）。§7 |
| `kb1`・`kb2`・`kr1`〜`kr4` | `Kibidango_AtBoundary_*`（＋直前の `GrowthMultipliers_*`） | 6 回とも Passed（再現せず）。`kr5` はブリッジの命令を上書きしてしまい実行記録なし、`kf1` は未実行 |
| `kf2`・`kg1`・`kg2`・`kh1` | `P6BWorldPlayTests` 全 9 件（診断を足した版） | 4 回とも 9／9 |
| `kh2` | 同上 | 8／9。実攻撃テストの「犬丸あり 試行 3」で使用開始 0 回（敵の攻撃と重なり開始できなかった）。テストが「開始 1 回」を要求していたため。**開始 0 の試行は数えずに記録だけ残す**よう直した（`30bd6a6`） |
| `ki1`〜`ki3` | 同上（`30bd6a6` の内容） | 3 回とも 9／9 |
| `le` | EditMode `P6B(Growth\|KibidangoUse)Tests`（最終） | **24／24** |
| `lp` | PlayMode `P6BWorldPlayTests`（最終） | **9／9**（§3 の時系列はこの実行のもの） |
| `vlP6B` | `verify-required-tests` P6B（`le,lp`） | **33／33**、未説明の Skip なし |

コンパイルは変更の都度（0 警告）。EditMode 全件・PlayMode 6 分割・実ビルドは、製品コードが `47b223e` から変わっていないため再実行していない（記録 004 の結果がそのまま有効）。

### 7. 未解明の 1 件

`kp` で `Kibidango_AtBoundary_NoTransitionWhileUsing_ThenTransitionsWithoutRelease` が 1 回だけ「使用終了後、外向きを押し続けているのに遷移要求 0（出口の範囲内=True）」で落ちた。
以後の 13 回（単体 6・全体 7）では再現せず、原因は特定できていない。推測で直さず、次に起きたときに切り分けられるよう失敗時の出力へ
遷移の受付口の要求数・待機状態・スライド調整役と先読みの最後の拒否理由・主人公の状態・モード・押し続けた秒数を足した（`30bd6a6`。製品コードは変えていない）。
この試遊報告（使用中の被弾）とは無関係の箇所。

人間確認（README §5 の 4 点）は未実施のまま。3 点目（「1.5 秒確定と被弾中断」が分かるか）について、今回の切り分けから**確定後の被弾と犬丸のかばいが
人間には「被弾したのに回復した」と見えやすい**ことが分かった。表示（1.5 秒の印・かばいの演出）の分かりやすさは G05 の素材と合わせて判断してほしい。

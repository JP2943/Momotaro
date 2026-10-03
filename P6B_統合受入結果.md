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

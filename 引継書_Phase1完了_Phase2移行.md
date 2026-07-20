# 引継書 — 桃太郎アクションRPG（Phase 1 完了 → Phase 2 移行）

> 読み手：新しい AI セッション（Claude / GPT 等）。このファイルを最初に読み込み、文脈を復元してから作業を再開すること。
> 作成日：2026-07-20 / 対象：Unity 6.3 LTS プロジェクト `momotaro_action_rpg`（GitHub: `JP2943/Momotaro`）

---

## 0. まず最初に（新セッションの立ち上げ手順）

1. **正本ドキュメントを読み込む**（`制作開始パック/` 内、いずれもこのリポジトリに同梱）
   - `桃太郎アクションRPG_ゲーム仕様書_v1.4.docx` — 企画・技術の**正本**。Phase 2 以降の作業はこれに従う。
   - `Claude_Cowork_制作開始ガイド.docx` — Claude に毎回適用する**共通規則**。
   - `命名・データ規約.docx` — C# / Asset / Stable ID / Prefab / SO 命名規約。
   - `Phase0_タスク票.docx` ほかフェーズ別タスク票 — 作業単位と受入条件。
   - `プロジェクト台帳.xlsx` — Package / 外部Asset / Stable ID / 章Content / Bug の記録簿。
   - `実装・検証チェックリスト.xlsx` / `試遊記録.xlsx` — DoD・レビュー・試遊記録。
2. **ブランチと統合状態を確認する**（§7 の未解決事項を必ず参照）。作業対象ブランチを正す。
3. **ベースライン緑を確認する**：Unity で Test Runner（EditMode/PlayMode）と `Momotaro/Validation/Validate Project Data` を実行し、Console Error 0 を確認してから着手。
4. **作業規約（§2）を厳守する**。特にフェーズ・スコープ規律と無断変更禁止。

---

## 1. 環境

| 項目 | 値 |
|---|---|
| Unity | **6000.3.20f1**（6.3 LTS） |
| Render Pipeline | URP |
| リポジトリ | GitHub `JP2943/Momotaro` |
| プロジェクト名 | `momotaro_action_rpg` |
| 入力 | Input System（新）。Active Input Handling は新 System 有効 |
| VCS 設定 | Asset Serialization = **Force Text**、Version Control = **Visible Meta Files**、**Git LFS** 構成済み（`.gitattributes` / `.gitignore` あり） |
| ワークスペース | `C:\Users\naked\Momotaro`（＝選択フォルダ。ここが作業対象） |

---

## 2. 作業規約（厳守・最優先）

- **最新のユーザー指示が最優先。** 本書や仕様書と矛盾する場合は最新のユーザー指示に従う。
- **フェーズ・スコープ規律。** そのフェーズの範囲外を先回り実装しない（例：Phase 0 では Gameplay を実装しない、Phase 1 では戦闘/被弾/死亡を実装しない）。
- **無断変更の禁止。** Unity 版・Render Pipeline・Package・**Stable ID**・Save フィールドをユーザー承認なく変更しない。
- **完了条件（Definition of Done）**：
  1. コンパイル/Console **Error 0**
  2. 対象テスト＋既存テストが**全緑**
  3. **手動確認手順**（Editor 操作）を明示して報告
  4. 追加/変更した `.cs`・Asset には**必ず `.meta` を対で用意**
- **層依存を破らない**（§3 のルール）。
- **`GameObject.Find` 系を Scripts で使わない**（参照は SerializeField 注入 or 静的 Provider 経由）。
- **ScriptableObject 原本を実行時に書き換えない**（可変状態は Runtime State クラスへ）。
- 変更後は **meta 整合・Stable ID 重複・Missing 参照・層依存**の静的確認を行い、結果を報告する。

---

## 3. アーキテクチャ（8 asmdef と層依存）

```
Core  ←  Data  ←  Gameplay  ←  Presentation
                      ↑            ↑
                Infrastructure ────┘
Editor（Editor専用）      Tests.EditMode / Tests.PlayMode
```

| asmdef | 依存 | 主な内容 | 制約 |
|---|---|---|---|
| **Momotaro.Core** | なし | Logging（GameLog / ILogSink / UnityLogSink / LogLevel / LogCategory）、Identification（StableId / Format / Registry） | UnityEditor 不参照 |
| **Momotaro.Data** | Core | GameDataAsset 基底、キャラ/戦闘/イベント/進行の SO 型、Validation IF | UnityEditor 不参照 |
| **Momotaro.Gameplay** | Core, Data | Modes、Player（移動/向き/状態/ガード/Vitals）、Scenes、Vitals、View | **InputSystem 不参照**・Infrastructure/Presentation 不参照 |
| **Momotaro.Infrastructure** | Core, Data, Gameplay | Bootstrap、**Input（InputSystem はここに集約）**、SceneFlow | UnityEditor 不参照 |
| **Momotaro.Presentation** | Core, Data, Gameplay | Cameras、Player 見た目（Visual Adapter） | UnityEditor 不参照 |
| **Momotaro.Editor** | 各Runtime | Validation（Window / Validator / Build Hook） | Editor 専用 |
| **Momotaro.Tests.EditMode** | 各Runtime | EditMode テスト（約104件） | — |
| **Momotaro.Tests.PlayMode** | Core, Data, Gameplay, Presentation, Infrastructure | PlayMode テスト | — |

**重要な設計原則**：Gameplay は InputSystem を直接触らない。Infrastructure 側の `PlayerInputAdapter` が入力を読み、`PlayerInputProvider`（Gameplay 内の静的注入点）へ `IPlayerInput` を注入する。これにより Gameplay は入力実装から分離される。

---

## 4. 実装済みシステム（Phase 0 / 0.5 / 1）

### 4.1 Bootstrap（Infrastructure/Bootstrap）
- `BootstrapRoot`（常駐・多重防止、`HasInstance`）、`ServiceRegistry`、`IGameService`、`ServiceInitResult`、`BootstrapInitializer`。
- 常駐サービス：`GameModeBootService`、`InputBootService`。
- どのシーンから直開きしても Bootstrap が用意される設計（例：VS_Field 直開きでも動く）。

### 4.2 GameMode（Gameplay/Modes）
- `GameMode` enum 7 種：**Exploration, Combat, Dialogue, Event, Paused, Loading, GameOver**。
- `GameModeCatalog`：モード → **ActionMap**（Gameplay / UI / Dialogue）＋ canPause / showsHud を対応付け。
  - Gameplay マップ：Exploration・Combat／Dialogue マップ：Dialogue／UI マップ：Event・Paused・Loading・GameOver。
- `GameModeService`（`IGameModeService`）、`GameModeProvider`（静的アクセス点）、`IGameModeListener`、`GameModeChanged`。
- **P0.5-A 修正**：リスナー通知はスナップショット走査（通知中の追加/削除で例外を出さない）。

### 4.3 Input（Infrastructure/Input ＋ Settings/Input）
- `Settings/Input/IA_Momotaro.inputactions`：マップ = Gameplay / UI / Dialogue / Debug、スキーム = Keyboard&Mouse / Gamepad。
- `InputService`：モードに応じ ActionMap を切替、直近デバイスから Control Scheme 追跡、リバインド保存/読込（`IRebindStore` / `PlayerPrefsRebindStore`）。
- `InputBootService` が配線。`PlayerInputAdapter` が Gameplay/Move・Guard を読み `PlayerInputState`（`IPlayerInput`）へ反映 → `PlayerInputProvider.Current` に注入。
- **モードゲート**：GameMode が Gameplay 以外のとき、Move=0・Guard 解除（`PlayerInputState.SetActive(false)`）＋ ActionMap も Gameplay から切替。→ Dialogue/UI 中は Player が動かない（検証済み）。
- Move の割当：`<Gamepad>/leftStick` ＋ `<Gamepad>/dpad` ＋ Keyboard WASD（2DVector composite）。

### 4.4 Data / StableId（Core/Identification ＋ Data）
- `StableId`（値型、`_value`、**小文字 snake_case**）、`StableIdFormat`、`StableIdRegistry`（重複検出）。
- `GameDataAsset` 基底：Id / DisplayName / Description / Version / DebugNote ＋ `Validate`。
- 型階層：`CharacterData`（`_maxHp`=100, `_moveSpeed`=5）→ `PlayerData`（`_maxStamina`=100）/ `CompanionData` / `EnemyData`。ほか Combat（Attack/Guard/Step/SpecialAttack/StatusEffect/AttackTelegraph）、Events（Encounter/EventSequence）、Progression（Reward/SkillNode）、`PlayerMovementData`。
- **データ資産（実体）**：
  - `SO_Player_Movement.asset` — Stable ID `player_movement_default`（moveSpeed=5, guardSpeedMultiplier=0.4）
  - `SO_Player_Momotaro.asset` — Stable ID `player_momotaro`（MaxHP=100, MaxStamina=100, MoveSpeed=5, DisplayName=Momotaro）
- 検証：メニュー **`Momotaro/Validation/Validate Project Data`**（`ProjectDataValidator` / `DataValidationWindow` / `ValidateOnBuild` フック）。Error 0 が受入基準。

### 4.5 SceneFlow（Infrastructure/SceneFlow ＋ Scenes）
- 4 シーン：`SCN_System_Bootstrap` / `SCN_System_Launcher` / `SCN_System_Loading` / `SCN_VS_Field`。
- `SceneFlowManager`、`SceneNames`、`TransitionGuard`（多重遷移防止）、`IScreenFader` / `NullScreenFader`（フェードは現状 Null 実装）。
- `GameplaySceneMode`（Gameplay/Scenes）：シーン進入時に指定 `GameMode` を要求（VS_Field は既定 Exploration）。

### 4.6 Player（Phase 1 本体・Gameplay/Presentation）
- 構造：`PlayerRoot`（PhysicsRoot / VisualRoot / ShadowRoot、Rigidbody 回転固定、Collider と Visual を別 GameObject に分離）。
- 移動：`PlayerMovementData`(SO) / `PlayerMovementCalculator`（XZ 平面・斜め正規化で速度一定）/ `PlayerMotor` / `PlayerInputProvider`。
- 向き：`FacingDirection`（4方向）/ `FacingResolver` / `FacingUpdate` / `PlayerFacing`（deadzone、微小入力で震えない）。
- 衝突：壁貫通なし・壁沿いスライド。
- 状態機械：`PlayerState`（Idle / Move）/ `PlayerStateMachine` / `PlayerStateController`（moveThreshold、**Disable 時にリセット** — P1受入#3 修正）。
- ガード：`GuardMovement`（40% 速度・向き固定・ガード表示）。
- カメラ：`TopDownCameraFollow` ＋ `CameraFollowMath`（Orthographic 追従）。
- 見た目：`PlayerVisualAdapter` ＋ `PlayerVisualNames`（Presentation）。仮スプライトシート＋Animator。
- Vitals：`Vital`（0..Max へ Clamp、`VitalChanged` 通知）/ `PlayerVitals`（`FromData` で SO 最大値から生成、**原本不変**）/ `PlayerVitalsHolder`（Awake で PlayerData から生成）。
- **Prefab**：`Prefabs/Player/PF_Player_Momotaro.prefab` に PlayerRoot / PlayerMotor / PlayerFacing / PlayerStateController / PlayerVisualAdapter / **PlayerVitalsHolder（`SO_Player_Momotaro` 割当）** ＋ Rigidbody / CapsuleCollider / SpriteRenderer / Animator。

---

## 5. テストと検証

- **EditMode**：約 104 件（StableId / Data 検証 / GameMode / Bootstrap / 移動計算 / 向き / 状態 / Prefab 構造 など）。
- **PlayMode**：`VsFieldSmokeTests`（Player/Camera/入力供給）、`PlayerVitalsSmokeTests`（Holder 存在・Vitals 非null・HP/Stamina 最大開始・SO 原本不変）。
- **データ検証**：`Momotaro/Validation/Validate Project Data` → Error 0。ビルド時フックあり。
- **受入チェックリスト**：`Phase1_制作パック/Phase1_統合受入チェックリスト.md`（A:自動テスト / B:起動経路 / C:操作(KB+Gamepad) / D:モード遮断 / E:データ・参照健全性）。Phase 1 は全項目 Pass 済み（ユーザー確認済み）。
- 実行の起点：`SCN_System_Bootstrap`（通常起動）または `SCN_VS_Field`（直開きスモーク）。

---

## 6. Phase 1 完了状況（サマリ）

- **Phase 0**（P0-01〜12）：Git/LFS、フォルダ＋8 asmdef、Log、Bootstrap、StableId/Data 基盤＋13 SO 雛形、GameMode、Input 基盤、4 Scene＋SceneFlow、Sample Test、Data Validator。**完了**。
- **Phase 0.5**：GameMode 通知のスナップショット化、InputService の Bootstrap 接続。**完了**。
- **Phase 1**（P1-01〜12）：Player Prefab、入力抽象、XZ 移動、4方向、衝突/壁沿い、状態機械、ガード、トップダウンカメラ、仮スプライト、Vitals、VS_Field スモーク、統合受入。**完了**。
- **Phase 1 追加受入修正**（本セッション）：`SO_Player_Momotaro` 作成、Prefab に VitalsHolder 追加・SO 割当、`PlayerVitalsSmokeTests` 追加、チェックリストの Vitals 必須化、Gamepad の Move に dpad 追加、`SO_Player_Movement` の DisplayName 補完。

---

## 7. 未解決事項・注意（次セッションで最初に片付ける）

1. **main への Phase 1 統合の確認が未完了。**
   - Phase 1 の作業は **ブランチ `phase/1-player-foundation`**（先端コミット `0c766ea`）に載っている。ユーザーは GitHub 上で main へマージ済みと報告。
   - ただしローカルの `main` と、ローカルが把握している `origin/main` はともに **`29bc3b8「Phase 0.5完了」`**（＝Phase 1 未反映の状態）。
   - **作業ツリーは現在 `main`（Phase 0.5）にチェックアウトされており、Phase 1 のファイルが物理的に存在しない**点に注意（本書執筆時点）。作業再開時は、統合済み main を pull するか、`phase/1-player-foundation` を checkout して、Phase 1 ファイルがある状態にすること。
   - 手順例（ネットワークが通る環境で）：
     ```
     git checkout main
     git fetch origin && git log --oneline -3 origin/main   # Phase1のマージが載っているか
     git pull origin main
     git cat-file -e origin/main:Assets/_Project/Data/Player/SO_Player_Momotaro.asset && echo OK
     ```
2. **URP.png の LFS 差分。** `Assets/TutorialInfo/Icons/URP.png` に未コミット差分（HEAD=130Bポインタ / 作業ツリー=24KB実体）。URP テンプレート由来で**無害・Phase 1 と無関係**。破棄してよければ `git restore Assets/TutorialInfo/Icons/URP.png`（不要なら TutorialInfo ごと削除も検討、ただし無断削除はしない）。
3. **Cowork サンドボックスから GitHub へ直接アクセス不可。** ワークスペースの `git`（egress プロキシ localhost:3128）は `github.com` を **403 で遮断**（ホスト単位）。Claude の「アクセス可能ドメイン（Web閲覧）」設定とは**別系統**のため、Web 許可を足しても `git fetch/pull` は通らない。サンドボックスから push/pull したい場合はサンドボックスの egress 許可設定が別途必要。**当面は git 操作をユーザー端末側で行う前提**。

---

## 8. Phase 2 への接続点（具体タスクは仕様書に委ねる）

> Phase 2 の作業範囲・順序・受入条件は**正本の `ゲーム仕様書_v1.4.docx`（Phase 2 章）**に従う。ここでは現状コードから「既に足場があり、次に繋がる箇所」だけを列挙する（先回り実装はしない）。

- **戦闘データは雛形済み・挙動未実装**：`AttackData` / `GuardData` / `StepData` / `SpecialAttackData` / `StatusEffectData` / `AttackTelegraph` は SO 型として存在するが、Gameplay に戦闘挙動は無い。`GameMode.Combat` は Gameplay ActionMap を使う定義のみ。
- **入力は定義済み・未消費**：IA_Momotaro に Attack / Step / SpecialAttack / CompanionSkill / SwitchCompanion / UseKintan / Interact / Map / Pause 等を定義済みだが、現状消費しているのは **Move / Guard のみ**。
- **Vitals は増減 API あり・未使用**：`Vital.Change/SetCurrent/SetMax` は実装済みだが Phase 1 では被ダメージ/回復/死亡を扱わない。
- **キャラ型の足場**：`CompanionData` / `EnemyData` は型のみ、挙動なし。
- **HUD 未実装**：`GameModeProfile.showsHud` フラグはあるが HUD 表示は未実装。
- **画面フェード未実装**：`IScreenFader` は `NullScreenFader` のみ。
- **View 枠**：`Gameplay/View` に `IScreenFader` 等の抽象あり。

---

## 9. 参照（ファイルの所在）

- スクリプト：`Assets/_Project/Scripts/{Core,Data,Gameplay,Infrastructure,Presentation,Editor,Tests}/…`
- 入力アセット：`Assets/_Project/Settings/Input/IA_Momotaro.inputactions`
- データ資産：`Assets/_Project/Data/Player/SO_Player_Movement.asset` / `SO_Player_Momotaro.asset`
- Prefab：`Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab`
- シーン：`Assets/_Project/Scenes/SCN_*.unity`
- テスト：`Assets/_Project/Tests/{EditMode,PlayMode}/…`
- 受入チェックリスト：`Phase1_制作パック/Phase1_統合受入チェックリスト.md`（※ `phase/1-player-foundation` ブランチ上）
- 正本ドキュメント：`制作開始パック/`（仕様書・ガイド・規約・タスク票・台帳）

---

*本書は Phase 1 完了時点のスナップショット。矛盾が生じた場合の優先順位は「最新のユーザー指示 ＞ ゲーム仕様書 v1.4 ＞ 本書」。*

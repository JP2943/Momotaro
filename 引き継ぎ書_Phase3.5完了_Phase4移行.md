# 引き継ぎ書：Phase 3.5 完了 → Phase 4 移行

このスレッドが長大化したため、次スレッドへ引き継ぐ。**Phase 3.5（戦闘縦切り試遊版）は受入完了し main へ統合済み**。次は **Phase 4（本編システム：報酬付与・仲間・アイテム・探索/会話 ほか）** に着手する。詳細タスクは別途「Phase 4 タスク仕様書」が正本になる想定。本書は現状の到達点・再利用契約・作法・申し送りをまとめる。

作業分担（従来どおり）：ユーザー＝判定/最終受入（native commit）、GPT＝仕様/レビュー、Claude＝実装/テスト/静的検証。Claude はファイルを配置し、変更ファイル一覧＋コミットメッセージ案を提示する。

---

## 1. これまでの到達点

- **Phase 1**：Data 型階層・入力アセット（`IA_Momotaro`）・移動の足場。
- **Phase 2**：主人公アクション（コンボ攻撃・ガード・ジャストガード・ステップ回避・必殺技チャージ）、被弾解決の基盤。
- **Phase 3**：敵 AI・敵戦闘（知覚/ヘイト/攻撃スロット/遠距離/強敵＝侍骸骨/敵防御・回避・撃破/仮UI・Debug/統合受入）。
- **Phase 3.5（本スレッドで完了）**：戦闘の縦切り試遊版。主人公 Hurt/Defeated、戦闘セッション基盤、試遊 HUD、戦闘フィードバック（Flash/HitStop/CameraShake/SE）、試遊 Scene 生成ツール、4 連続ウェーブ、勝敗・リトライ統合、バランス/安定性調整、配布・統合受入。

### 1.1 Phase 3.5 の後半（P3.5-08A〜10）で本スレッドが追加・確定したこと

- **戦闘リアクション（P3.5-08A）**：`HitReaction`（Hitback/Guardback 距離・秒・Projectile 判別）を `AttackSnapshot` に載せて Snapshot 化。純粋モデル `ExternalReactionMotion`（距離÷時間の一定速度供給）＋`IReactionMotor`。近接攻撃者への強制ひるみ `IForcedFlinchReceiver`。JG 成立で体幹反射＋近接強制ひるみ（`ForcedFlinchSeconds=0.35`）。
- **必殺技の使用感調整（P3.5-09）**：`SpecialAttackData` を試遊調整。タメ `2.0→1.0`、`ActiveSeconds 0.15→0.35`、専用射程（`HitboxForwardOffset=1.2`／`HitboxHeight=0.5`／`HitboxHalfExtents=(0.9,0.6,1.1)`）、**Active 中に前方へ進む** `HitboxTravelDistance=1.2`（判定中心 `SpecialHitboxCenter()` を判定・剣閃 VFX で共有）。**後隙は攻撃(J)/ステップ(Space)でキャンセル可**（`IsSpecialActive` 中は不可＝出し切り。`IsSpecialRecovery` のみキャンセル）。剣閃 VFX（`SlashVfxInstance.SetPose`）が判定へ追従。
- **ジャスト回避（Just Evade。P3.5-09）**：ジャストガードの回避版。`StepState` 開始直後のタイト窓（`_justEvadeWindowSeconds=0.12`）＋無敵で成立。契約 `IJustEvadeState`。`HitResultKind.JustEvade` 追加。成立時に攻撃者の体幹へ反射（`PlayerStateController._justEvadeCounterPoise=20`・共通 `ReflectPoiseCounter`）＋近接強制ひるみ＋専用フィードバック（寒色 Flash＋控えめ Shake＋HitStop＋`SE_JustEvade`）。ガード不能を含む Steppable 全攻撃で成立（＝「回避が正解」の報酬窓）。
- **ガード不能予告の視認性（P3.5-09）**：頭上 HP/体幹バーは IMGUI（`EnemyOverheadBars`）で常に最前面のため、**位置と透過**で回避。予告 `EnemyUnblockableWarningPresenter._height 2.0→2.9`／`sortingOrder 60→100`、バー `_height 2.0→1.6`（3 敵 Prefab）／背景α`0.85→0.45`・前景α`0.95→0.7`。
- **SE 群**：主人公スイング（`PlayerAttackSwingSePresenter`・**Startup 先行が正式仕様**）、敵スイング（`EnemyAttackSwingSePresenter`・音量控えめ）、ヒット音（`PlayerHitSePresenter`・1/2 段=Hit_01、3 段/必殺=Hit_02）、ステップ（`PlayerStepSePresenter`）、Guard/JustGuard/JustEvade（結果 SE）。**`CombatSePlayer` は役割別に複数**（結果/敵スイング/ヒット/ステップ/主人公スイング）。
- **撃破フェード**：`EnemyDefeatFadePresenter` は Down を約 1 秒保持後にフェード（`DownHoldSeconds=1`／`FadeSeconds=0.6`）。`Tick` は「フェード更新 → 保持満了判定」の順（保持満了を跨ぐ Tick で即完了しないよう修正済み）。
- **統合受入 Validator（P3.5-10）**：`Phase35CombatTrialValidator`（メニュー **Momotaro → Phase 3.5 → Validate Combat Trial**）。試遊 Scene の単一性・混入禁止（重複 HUD/Session・Debug HUD）・必須配線・衛生を機械検査。AssetDatabase 非依存で EditMode テスト可（`Phase35CombatTrialValidatorTests`）。
- **配布ドキュメント（P3.5-10）**：`README_Phase3.5_試遊版.md`／`フィードバック票_Phase3.5.md`。
- **テスト**：EditMode 178 本／PlayMode 18 本、**全緑**を確認済み。

---

## 2. 現在の戦闘システムの主要契約（Phase 4 で再利用すべきもの）

新規アクター（仲間＝犬猿雉など）や報酬付与を作る際、既存の契約・解決順を壊さず接続すること。

- **命中解決の順序**（`PlayerVitalsHolder.ReceiveHit`）：死亡後は無視 → 被弾後無敵(Hurt 由来 I-frame)で Evade → **無敵(ステップ I-frame)∩Steppable**（さらにジャスト窓なら JustEvade、窓外なら Evade）→ ジャストガード → 通常ガード → 被弾。優先度は「無敵＞ガード＞JG＞被弾」。
- **共通契約**：`IDamageable.ReceiveHit(in HitInfo)`／型付き結果 `HitResult`＋`HitResultChannel`（`HitResultKind`＝Damage/Guard/JustGuard/Evade/Rejected/**JustEvade**）。攻撃者同定 `ICombatActor`。防御状態 `IGuardState`／`IJustGuardState`／`IEvadeState`／`IJustEvadeState`。強制ひるみ `IForcedFlinchReceiver`。反応適用 `IReactionMotor`。
- **フィードバック配管**：`CombatFeedbackDispatcher`（Player/Dummy/Enemy の結果チャネルを購読）→ `CombatFeedbackMap.Resolve(kind)` で Cue（VFX ID/SE ID/HitStop 秒）→ `CombatFeedbackChannel` → `CombatFeedbackPresenter`（HitStop/Flash/CameraShake/`CombatSePlayer`）。**新しい結果種別を足すときは `HitResultKind`＋`HitResult` ファクトリ＋`CombatFeedbackMap` の case＋（必要なら）Presenter の分岐＋SE スロット**をセットで。
- **敵側の Phase 4 契約（Phase 3 から継続）**：撃破は `EnemyActor.Defeats`（`EnemyDefeatChannel`）が `EnemyDefeatedEvent`＋`EnemyRewardRequest`（敵ID・役割・任意 `RewardData`・位置）を 1 回発行 → **実付与は購読側（Phase 4）で実装**。ヘイトは `IThreatTarget`／`EnemyThreatTracker.CurrentTarget`（犬を足しても AI を書き換えない設計）。状態は `EnemyActor.States`。防御観測は `IAttackThreatSource`（攻撃中/ガード不能/攻撃方向）を新規アクターが実装すれば敵防御 AI が反応。
- **試遊シーンのシステム構成**：`CombatSessionController`（Preparing/Playing/Intermission/Victory/Defeat/Reloading）／`WaveRunner`（4 Wave 固定・Prefab から runtime 生成）／`CombatOutcomeController`／`CombatSceneReloader`／`CombatRetryInput`／`CombatPlayHud`（試遊 HUD）。Scene 生成は `Phase35CombatTrialBuilder`（メニュー **Momotaro → Phase 3.5 → Generate Combat Trial** → `Assets/_Project/Scenes/Tests/SCN_Phase35_CombatTrial.unity`）。**手動編成の `SCN_VS_Field` と `SCN_Phase3_EnemyTest` は変更しない**。

---

## 3. アセット取り込み・規約（踏襲）

- **スプライト**：Phase 3 引き継ぎ書「スプライト取り込みパイプライン」の寸法・PPU 規約を踏襲。シート型は `.meta` のスライス矩形どおり再パック、個別型は 1 枚上書き。`Special` の meta は PPU100/BottomCenter へ正規化済み。
- **効果音**：ユーザーが `ArtSource/Prototype/Audio/SE/...` に配置 → Claude が `Assets/_Project/Audio/SE/...` へ **OGG 変換**（`ffmpeg -c:a libvorbis -q:a 5`）してミラー。`.ogg.meta`（AudioImporter）＋フォルダ `.meta` を生成。SE の鍵は `CombatFeedbackMap` の SeId／各 Presenter のスロット seId と一致させる。
- **.meta の GUID**：`md5(assetパス)[:32]`（決定的）。新規 `.cs`／フォルダ／アセットを追加したら必ず meta を生成（`.cs`＝MonoImporter、フォルダ＝folderAsset、`.ogg`＝AudioImporter）。
- **Data 駆動**：数値は原則 ScriptableObject（`GameDataAsset` 派生）へ。試遊調整値（必殺技/ジャスト回避/SE 音量/予告高さ/Down 保持 等）はコード初期化子とアセット両方を一致させる（欠落フィールドは C# 初期化子へフォールバックする点に注意）。

---

## 4. 実装・環境の作法（この Cowork＋デバイスブリッジ環境）

- **Unity は本環境で実行不可**。コンパイル・TestRunner・Editor ツール実行・Scene 生成・メタ再インポートはすべて**ユーザーが Unity 上で実施**。Claude は静的検証（brace/using 整合・型/名前空間・meta/GUID・参照）まで。推測で「成功」と報告しない。
- **ファイル授受**：ユーザーのプロジェクトはデバイス側マウント（`~/Momotaro` ＝ `$HOME/mnt/Momotaro`）。読むときは対象ファイルを cloud へ stage して Read/Edit。書き戻しは **cloud で tar → SendUserFile → device_commit_files で `~/Momotaro/_transfer/` → device_bash で展開 → `cp -f` で本体へ**。`_transfer/`・`_to_delete/` は `.gitignore` 済み。
- **削除の制約**：device 側 `rm`/`unlink` は不可（`Operation not permitted`）。上書き（`cp -f`／ffmpeg -y）は可。削除が必要なら `_to_delete/` へ退避しユーザーに削除依頼（今回の一時ファイルはユーザー削除済み）。tar 再展開時は `_transfer/Assets/...` が残ると衝突するので**毎回新しいサブフォルダへ展開**する。
- **層規約**：`Core ← Data ← Gameplay ← Presentation`（＋ `Infrastructure`／`Editor`／`Tests`）。Gameplay は Animator/Canvas/Scene API/Camera を直接触らない（Adapter/Provider/interface 経由）。static 万能マネージャ禁止。純粋状態機械（`StepState`/`JustGuardState`/`SpecialChargeState`/`ExternalReactionMotion` 等）＋ MonoBehaviour で deltaTime 外部注入 → テスト決定性。
- **テスト分担**：EditMode（AssetDatabase 可）＝Prefab/Data 数値検証・純粋ロジック・Scene 生成検証。PlayMode（UnityEditor 非参照）＝実行時ライフサイクル/例外なし。時間境界は「直前/一致/直後」を明示し、`yield return null` の偶然に依存しない。**浮動小数点の累積境界は吸収閾値**（例：`ExternalReactionMotion.TimeEpsilon=1e-5`）。EditMode の `Time.deltaTime` は最大 ~0.333 まで振れるので、境界テストはこれを跨がない値にする。
- **後始末**：Disable/Scene 離脱/Retry で購読・Coroutine・Hitbox・Projectile・Slot・Feedback・Reaction を残さない。
- **Git**：Push/PR/ブランチ削除はユーザー判断。Phase 3.5 は受入・main 統合済み。

---

## 5. 既知の制約・技術的負債・申し送り

- **頭上 HP/体幹バーは仮 UI**（`EnemyOverheadBars`・IMGUI）。常に最前面に描かれるため、他のワールド VFX とのレイヤー競合は sorting では解けない（今回は予告の高さ＋バーの透過で回避）。完成 HUD 設計は Phase 4 以降。
- **撃破演出は暫定**（Down 保持→フェード）。最終的な消滅・死体表現・報酬エフェクトは未実装（報酬は `EnemyRewardRequest` の発行のみ、受け手＝Phase 4）。
- **一部 VFX/SE は試遊用の暫定**。未割当は無音・非表示で安全継続。
- **各種の数値は試遊調整中**（必殺技のタメ/射程/持続/前進、ジャスト回避の窓/反射量、JG/ステップ受付、SE 音量、予告高さ、Down 保持）。Data／調整値に集約済み。実機フィードバック（`フィードバック票_Phase3.5.md`）を受けて再調整の可能性。
- **Player 体幹（ポーズ）ゲージは未導入**（敵側のみ体幹あり）。完成死亡演出も未実装（仮：Hurt 最終 Frame 保持＋低彩度）。
- **ボス不在・敵 3 種のみ**（骸骨剣士/骸骨弓兵/侍骸骨）、Wave 編成は 4 固定。
- **入力は定義済み・未消費**：`IA_Momotaro` に `CompanionSkill`／`SwitchCompanion`／`UseKintan`／`Interact`／`Map` を定義済みだが未使用（Phase 4 で消費）。旧 `UnityEngine.Input` は使わない（Input System パッケージ）。
- **対象外だった領域（Phase 3.5 §0.3）**：犬猿雉/仲間 AI、本編マップ/会話/成長/装備、完成セーブ・UI・VFX・SE・BGM、製品版 Encounter。これらが Phase 4 の主戦場。

---

## 6. Phase 4 タスク候補（ロードマップの目安。正本は別途タスク仕様書）

既存契約・未消費入力・未実装領域から想定される着手対象：

- **報酬付与の受け手**：`EnemyRewardRequest`（徳/Item）を購読して実付与。`RewardData`／`Progression`（`Reward`/`SkillNode`）型の活用。
- **仲間（犬猿雉）**：`CompanionData`（型のみ存在）を実装。ヘイト基盤（`IThreatTarget`）へ味方として接続、`CompanionSkill`／`SwitchCompanion` 入力の消費、仲間 AI。既存の敵 AI・命中解決契約を再利用。
- **アイテム**：`UseKintan`（きびだんご/金団）等の消費アイテム。回復/バフの命中・効果解決。
- **探索・会話・進行**：Gameplay マップ（Exploration/Combat）と Dialogue マップの接続、`Encounter`/`EventSequence` 駆動、`Interact`/`Map` 入力の消費。
- **成長・装備**：`SkillNode`／装備の反映（`CharacterData` 系への上書き/合成）。
- **完成 UI/VFX/SE/BGM・セーブ**：仮 HUD/仮 Fade/仮 SE の本実装、完成死亡演出、Player 体幹ゲージ。
- **ボス・敵種追加**：新規敵は `EnemyActor`＋`IAttackThreatSource` 実装で既存 AI に載る。

---

## 7. 次スレッドの最初の一手（推奨）

1. **Phase 4 タスク仕様書**（GPT/ユーザー）で最初のタスク（例：報酬付与の受け手、または仲間 1 体の最小実装）を 1 つに絞る。既存契約（§2）に載せる形で、AI・命中解決・フィードバック配管を再利用する方針を確認。
2. 実装は 1 タスク＝1 まとまりで、変更ファイル一覧＋コミットメッセージ案を提示。新規 `.cs` は meta を必ず生成。数値は Data 化。
3. EditMode/PlayMode テストを併走（純粋ロジックは EditMode、実行時ライフサイクルは PlayMode）。全緑を維持。
4. 戦闘系に触れる場合は、締めに **Momotaro → Phase 3.5 → Validate Combat Trial** と Data 検証（**Momotaro → Validation → Validate Project Data**）でリグレッションを確認。

---

### 付録：主要ファイルの所在（抜粋）

- 命中/解決：`Scripts/Gameplay/Combat/`（`HitInfo`/`HitResult`/`HitResultKind`/`HitReaction`/`ExternalReactionMotion`/`I*State`/`CombatFeedback`）、`Scripts/Gameplay/Player/`（`PlayerStateController`/`PlayerVitalsHolder`/`StepState`/`JustGuardState`/`SpecialChargeState`）。
- 敵：`Scripts/Gameplay/Enemy/`（`EnemyActor`/AI/Defense/Combat）。
- セッション/Wave：`Scripts/Gameplay/Scenes/`（`CombatSessionController`/`WaveRunner`/`CombatOutcomeController`）。
- 表示：`Scripts/Presentation/Combat/`（VFX/SE/フィードバック各 Presenter）、`Scripts/Presentation/Hud/`（`CombatPlayHud`）、`Scripts/Presentation/Diagnostics/`（`CombatFeedbackDispatcher`/`EnemyOverheadBars`/`CombatDebugHud`）。
- Editor：`Scripts/Editor/Phase35/`（`Phase35CombatTrialBuilder`/`Phase35CombatTrialValidator`）、`Scripts/Editor/Validation/`（`ProjectDataValidator` 他）。
- Data：`Data/Combat/`（`SpecialAttackData` 他）、`Data/`（`SO_*` アセットは `Assets/_Project/Data/...`）。
- Scene：`Assets/_Project/Scenes/Tests/SCN_Phase35_CombatTrial.unity`。
- 入力：`Assets/_Project/Settings/Input/IA_Momotaro.inputactions`。
- 配布物：`README_Phase3.5_試遊版.md`／`フィードバック票_Phase3.5.md`。

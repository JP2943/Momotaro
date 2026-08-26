# Phase 4 既存契約マップ（実装着手前の下調べ）

対象：Phase 4 で新規実装が接続する既存コードの「呼び出し関係・拡張点・落とし穴」。  
読み方：§2 が今回の主タスク（報酬付与の受け手）の主線、§3〜§6 は今後の仲間・アイテム・進行が乗る土台、§9 が**着手前に決めるべき事項**。  
根拠：main（`74acde1`）時点の実コードを読んで作成。引き継ぎ書の記述と食い違う点は §10 に明記した。

---

## 1. 層とアセンブリ（新規コードの置き場所）

| asmdef | 参照先 | 置いてよいもの |
|---|---|---|
| `Momotaro.Core` | （なし） | `StableId` 等の基盤。UnityEngine は可 |
| `Momotaro.Data` | Core | `GameDataAsset` 派生の SO 型 |
| `Momotaro.Gameplay` | Core, Data | ルール・状態・純粋モデル・MonoBehaviour（Animator/Canvas/Camera/Scene API に触らない） |
| `Momotaro.Infrastructure` | Core, Data, Gameplay, **Unity.InputSystem** | 入力 Adapter・Bootstrap・SceneFlow |
| `Momotaro.Presentation` | Core, Data, Gameplay, **UnityEngine.UI** | VFX/SE/HUD/Debug の各 Presenter |
| `Momotaro.Editor` | 上記すべて（Editor 限定） | Builder / Validator |

**帰結**：報酬の付与ロジックは Gameplay、徳の表示は Presentation。Gameplay から Presentation は参照できないので、表示は **チャネル or event を Presentation 側が購読する**形にする（既存の `HitResultChannel` / `EnemyDefeatChannel` と同じ作法）。

---

## 2. 撃破 → 報酬要求の経路（今回の主線）

### 2.1 発行までのシーケンス

```
攻撃側 Hitbox
  └→ EnemyActor.ReceiveHit(in HitInfo)                     [Gameplay/Enemy/EnemyActor.cs:212]
       ├ _vitals.IsDefeated → return                       （撃破後の追撃は無視）
       ├ _defense.IsEvadeInvulnerable → return
       ├ ガード軽減（前方180°/Special貫通）
       ├ app = _vitals.Apply(hit, hpScale, poiseScale)
       └ if (app.NewlyDefeated)
            ├ _machine.ForceHitState(Down, Defeated)
            └ HandleDefeated()                             [EnemyActor.cs:319]
                 ├ _defeatHandled = true                   ← 1回性はここだけで担保
                 ├ ReactionMotor.ClearReaction()
                 ├ IEnemyDefeatCleanup[].OnOwnerDefeated() （攻撃中断・Slot 解放）
                 ├ 自身の Collider を全無効化
                 └ Defeats.Publish(new EnemyDefeatedEvent(
                        DamageableId,
                        new EnemyRewardRequest(DamageableId, role, _archetype?.Reward, WorldPosition)))
```

- `EnemyRewardRequest` = `{ EnemyId, Role, RewardData Reward, Vector3 Position }`（`Gameplay/Enemy/Defense/EnemyDefeat.cs`）。
- `role` は `_archetype.Role`、Archetype 未設定時は `EnemyRole.Melee` にフォールバック。
- **`Reward` は `EnemyArchetypeData.Reward`（`RewardData` 参照）をそのまま載せるだけ**。付与は一切していない。

### 2.2 現在の購読者（＝空いている口の確認）

`EnemyDefeatChannel` の購読者は現時点で 2 つだけ：

| 購読者 | 目的 | Reward を見ているか |
|---|---|---|
| `CombatSessionController`（Gameplay/Scenes） | 生存数カウント → `AllEnemiesDefeated` 発火 | **見ていない**（EnemyId のみ） |
| `EnemyDefeatFadePresenter`（Presentation/Combat） | Down 保持 → スプライトフェード | 見ていない |

→ **報酬を実付与する購読者は存在しない。`EnemyRewardRequest.Reward` は現状どこからも読まれていない。**

### 2.3 実装前に潰しておくべき落とし穴

1. **`RewardData` が全敵で null**  
   `SO_Enemy_Melee_Prototype` / `_Ranged_` / `_Elite_` の `.asset` には `_reward` フィールドが**そもそも直列化されていない**（後から追加されたフィールドで、C# 初期化子 = null にフォールバック）。`SO_Enemy_GuardVariant` / `_EvadeVariant` は `_reward: {fileID: 0}` = null。  
   → 受け手は **`Reward == null` を正常系として扱う**必要がある。かつ、動作確認するには `SO_Reward_*` アセットを新規作成し、各 Archetype に割り当てる作業が要る（＝今回の実装に含める）。

2. **`RewardData.ItemId` に public アクセサが無い**  
   `_itemId` は `[SerializeField] private` のまま。`Validate` 内でしか使われていない。アイテム付与を扱うなら `public StableId ItemId => _itemId;` の追加が必須（`Data` 層の変更）。

3. **`GrantOnce` のセマンティクスが未定義**  
   `RewardData.GrantOnce`（既定 true）は宣言されているだけで、どこにも解釈がない。「この RewardData アセットを一度きり」なのか「敵1体につき一度」なのかは**未決**（§9-D3）。

4. **`EnemyId` は `GetInstanceID()`**  
   Prefab から runtime 生成される（`WaveRunner.SpawnKind` → `Instantiate`）ので、**Wave をまたぐと同じ敵種でも ID が変わる**。ID を永続キーにしてはいけない。重複排除に使うのは同一 Wave 内に限る。  
   なお `EnemyActor.ResetState()` は `_defeatHandled = false` に戻すので、テストで同一インスタンスを再撃破させれば同じ ID で 2 回発行される。

5. **チャネルは非 Serialized**  
   `Defeats` は `new` で生成されるインスタンスフィールド。**Scene に焼けない**ので、購読の結線は必ず runtime（`WaveRunner.Subscribe` が `BindPlayerDefeat` を runtime で繋いでいるのと同じ理由）。

6. **Publish はスナップショット反復**  
   `EnemyDefeatChannel.Publish` は `_listeners.ToArray()` してから回すので、通知中の購読追加・解除は安全。ただし**通知中に自分を解除しても、その回の通知は届く**。

---

## 3. 命中解決の順序（変更してはいけない優先度）

### 3.1 主人公側 `PlayerVitalsHolder.ReceiveHit`（`Gameplay/Player/PlayerVitalsHolder.cs:324`）

```
死亡後 → 無視
→ 被弾後無敵（Hurt 由来 I-frame） → Evade
→ 無敵（ステップ I-frame）∩ Steppable
     ├ ジャスト窓内 → JustEvade（体幹反射 + 近接強制ひるみ + 専用FB）
     └ 窓外        → Evade
→ ジャストガード → JustGuard
→ 通常ガード     → Guard
→ 被弾           → Damage
```
優先度は **無敵 > ガード > JG > 被弾**。

### 3.2 敵側 `EnemyActor.ReceiveHit`

```
撃破済み → 無視
→ 回避無敵 → 無視（結果も発行しない）
→ ガード中：前方180° かつ Special 非貫通なら HP×0.1 / 被体幹×1.5
→ Vitals.Apply → Down > Stunned > Stagger の順で状態強制
→ 撃破でなければヒットバック（HitReaction 由来）
→ Results.Publish(HitResult.Damage(...))
```

### 3.3 共通契約

- `IDamageable.ReceiveHit(in HitInfo)` / 攻撃者同定 `ICombatActor`（Faction / FloorId / WorldPosition / Forward）
- 結果：`HitResult`（Kind, HitId, Attacker, Target, AppliedDamage, HitPoint, AttackDirection）＋ `HitResultChannel`
- 種別：`HitResultKind` = Damage / Guard / JustGuard / Evade / Rejected / JustEvade
- 防御状態：`IGuardState` / `IJustGuardState` / `IEvadeState` / `IJustEvadeState`
- 強制ひるみ：`IForcedFlinchReceiver.ForceFlinch(float)`／反応適用：`IReactionMotor.PushReaction(dir, distance, seconds)` / `ClearReaction()`

**新アクター（仲間）を被弾側にする場合**：`IDamageable` + `ICombatActor` を実装し、自前の `HitResultChannel` を公開すれば、既存の攻撃側・フィードバック側は無改造で載る。

---

## 4. フィードバック配管（新しい結果種別を足すときの手順）

```
各アクターの HitResultChannel
  → CombatFeedbackDispatcher（Presentation/Diagnostics。Player/Dummy/Enemy を購読）
  → CombatFeedbackMap.Resolve(kind) → CombatFeedbackCue { VfxId, SeId, HitStopSeconds }
  → CombatFeedbackChannel
  → CombatFeedbackPresenter（HitStop / Flash / CameraShake / CombatSePlayer）
```

現在の Cue 表（`Gameplay/Combat/CombatFeedback.cs`）：

| Kind | VfxId | SeId | HitStop |
|---|---|---|---|
| Damage | VFX_Hit_Normal | SE_Hit_Normal | 0.05 |
| Guard | VFX_Guard | SE_Guard | 0.03 |
| JustGuard | VFX_JustGuard | SE_JustGuard | 0.09 |
| Evade | （なし） | SE_Evade | 0 |
| JustEvade | VFX_JustEvade | SE_JustEvade | 0.07 |
| その他 | None | | |

**追加時のセット**：`HitResultKind` に追加 → `HitResult` にファクトリ追加 → `CombatFeedbackMap` に case 追加 → 必要なら Presenter に分岐 → SE スロット（`CombatSePlayer` は役割別に複数インスタンス）。

※ 報酬（撃破ポップ等）はこの配管とは**別系統**。命中結果ではないので `HitResultKind` を増やさず、報酬側で専用の通知を出すのが筋。

---

## 5. ヘイト基盤（仲間追加の口。今回は触らないが確認済み）

`Gameplay/Enemy/Threat/IThreatTarget.cs` は既に汎用化されている：

```csharp
public interface IThreatTarget : IPerceptionTarget
{
    bool  IsDown { get; }
    float BaseThreat { get; }                 // 主人公 = 50、仲間は 0 が基本
    float AcquiredThreatMultiplier { get; }   // 犬×1.5 / 猿×1.2 / 雉×0.5 を想定済み
}
```

→ **仲間は `IThreatTarget` を実装するだけで敵 AI の候補に入る。敵 AI 側の改造は不要**（コメントに Phase 4 の意図が明記されている）。関連：`EnemyThreatTracker`（`CurrentTarget`）、`EnemyThreatTable`、`ThreatSettings`、`ThreatSource`。防御 AI に反応させたいなら `IAttackThreatSource`（攻撃中／ガード不能／攻撃方向）も実装する。

---

## 6. セッション / Wave のライフサイクル（**報酬の保持先を決める上で最重要**）

### 6.1 状態機

`CombatSessionController`：Preparing → Playing → Intermission → Victory / Defeat → Reloading。  
`RequestReload()` は `ICombatSceneReloader.ReloadCurrent()` が true を返したときだけ Reloading へ遷移（失敗時は Victory/Defeat のまま＝Retry 再試行可）。

### 6.2 敵の登録経路は 1 本

`WaveRunner.SpawnKind` → `Instantiate` → `_session.RegisterEnemy(actor)` の**ここだけ**。  
`RegisterEnemy` が `enemy.Defeats.AddListener(this)` を行い、`OnEnemyDefeated` で `_deadIds` による重複排除と生存数管理をしている。

### 6.3 Wave 間・Retry で消えるもの

- `OnIntermissionEntered`：`CleanupSpawned()`（生成敵を Destroy ＋ `_session.ClearEnemies()`）、`EnemyProjectileRegistry.DespawnAll()`、`RecoverPlayer()`（HP/Stamina 全回復・中立化・Special 0）。
- Retry＝**Scene 再読込**。したがって Scene 上の MonoBehaviour が持つ runtime 状態は**全部消える**。

→ **徳の累計をどこに置くかで挙動が変わる**：Scene 常駐コンポーネントに置けば Retry でリセット、`DontDestroyOnLoad` や Infrastructure の保存層に置けば継続。試遊としてどちらが正しいかは §9-D2 で決める必要がある。

### 6.4 購読の作法（踏襲すること）

`OnEnable` / `OnDisable` で対称に Add/Remove、`Bind*` は同一参照の再 Bind を無視、`Clear*` は二重呼び出し安全。新規の購読コンポーネントも同じ形にする。

---

## 7. Data 基盤

- すべての SO は `GameDataAsset` 派生：`StableId _id`（lowercase snake_case 必須）・`_displayName`・`_description`・`_version`（>=1）・`_debugNote` ＋ `Validate(DataValidationReport)`。
- `ProjectDataValidator` は `AssetDatabase.FindAssets("t:GameDataAsset")` で**全 SO を自動収集**して `Validate` を呼ぶ。→ **新しい `SO_Reward_*` を置けば自動で検証対象**になる（Validator 側の改造不要）。
- `RewardData`（`Data/Progression/RewardData.cs`）現状：`_virtueAmount`（>=0 検証あり）／`_itemId`（StableId・書式検証あり・**アクセサ無し**）／`_grantOnce`（既定 true・**解釈無し**）。
- `SkillNodeData` も `Data/Progression/` に存在（今回は未使用）。
- `CompanionData`：`CharacterData` 派生。`_switchCooldownSeconds`(3) / `_leaveRecoverySeconds`(5)。**後者は private のままで公開アクセサ無し**（仲間タスク着手時に要追加）。
- SO の実アセットは `Assets/_Project/Data/{Combat, Enemies, Player}` のみ。**`Progression/` フォルダはまだ存在しない**（新規作成＋フォルダ `.meta` が要る）。

---

## 8. Editor ツール（触る場所）

- `Phase35CombatTrialBuilder`（**Momotaro → Phase 3.5 → Generate Combat Trial**）が `SCN_Phase35_CombatTrial.unity` を機械生成。試遊 Scene に新コンポーネントを常駐させるなら**ここに追加**する（手で Scene を編集しても再生成で消える）。
- `Phase35CombatTrialValidator`（**Momotaro → Phase 3.5 → Validate Combat Trial**）：Scene 単一性・重複禁止（HUD/Session/Debug HUD）・必須配線・衛生を検査。`ValidateFeedbackWiring` / `ValidateWave` / `ValidateCameraShake` / `ValidateVfxFrames` / `ValidateSceneHygiene`。AssetDatabase 非依存で EditMode テスト可。  
  → 新コンポーネントを必須配線にするなら Validator にも検査を足す（＋ `Phase35CombatTrialValidatorTests`）。
- `ProjectDataValidator`（**Momotaro → Validation → Validate Project Data**）／`ValidateOnBuild` ／ `DataValidationWindow`。
- `Phase3EnemyTestFieldBuilder` は Phase 3 用。`SCN_VS_Field` / `SCN_Phase3_EnemyTest` は**手動編成なので変更しない**（引き継ぎ書の申し送りどおり）。

---

## 9. 報酬付与の受け手：着手前に決めるべきこと

実装に入る前に、以下は仕様として確定が要る。**太字が私の推奨**。

**D1. 徳・アイテムの保持先（器）**
- (a) **純粋 C# の `PlayerProgressState`（Gameplay）を、Scene 常駐の `PlayerProgressHolder : MonoBehaviour` が保持**。将来のセーブは Infrastructure が Holder を読む。← 既存の `PlayerVitals` / `PlayerVitalsHolder` と同じ形で、テスト決定性も取れる
- (b) SO に直接書く（Runtime に元アセットを書き換えるのは規約違反。**不可**）
- (c) static マネージャ（**規約で禁止**）

**D2. Retry（Scene 再読込）で徳をリセットするか**
- (a) **リセットする**（試遊は 1 周ごとに 0 から。Scene 常駐で自然にそうなる。実装が最小）
- (b) 継続する（`DontDestroyOnLoad` か Infrastructure の永続層が要る＝スコープ増）

**D3. `GrantOnce` の意味**
- (a) **同じ `RewardData` アセットは 1 セッション中 1 回だけ付与**（＝ボス撃破報酬・イベント報酬向け。雑魚が同じ Reward を共有していると 2 体目以降 0 になる点に注意）
- (b) 敵インスタンス 1 体につき 1 回（＝実質「重複通知の排除」であり、`_defeatHandled` で既に担保済みなのでフラグとして無意味になる）
- (c) 今回は解釈せず素通し（フィールドは休眠のまま）

→ 雑魚の周回で徳が貯まる想定なら、**雑魚用 Reward は `GrantOnce = false` で作る**運用が要る。SO 作成時の既定値に影響するので先に決めたい。

**D4. アイテムの扱い（今回のスコープ）**
- (a) **今回は徳のみ実付与。`ItemId` は public 化だけ行い、付与は次タスク（アイテム器）に回す**（ドロップは記録だけ残す/ログ）
- (b) 最小インベントリ（`Dictionary<StableId,int>`）まで今回作る

**D5. 表示**
- (a) **今回は HUD 非表示（付与ロジックとテストのみ）**。`CombatDebugHud` に累計を出す程度に留める
- (b) `CombatHudViewModel` に `Virtue` を足して試遊 HUD に常時表示
- (c) 撃破位置に数値ポップ（Presentation の新規 Presenter。スコープ大）

**D6. 受け手の設置と敵の登録経路**
- (a) **`CombatSessionController` に `event Action<EnemyDefeatedEvent> EnemyDefeated` を足し、受け手はそれを購読**（敵の登録経路が `RegisterEnemy` 1 本のまま。重複排除も Session の `_deadIds` に相乗りできる。Session の変更は数行）
- (b) 受け手が自前で `IEnemyDefeatSource` を探索・購読（`EnemyDefeatFadePresenter` と同じ Rescan 方式。Session に触らないが、重複排除と再スキャンを自前で持つ必要がある）
- (c) `WaveRunner` から受け手へも直接 `RegisterEnemy` する（登録経路が 2 本になる。非推奨）

**D7. 徳の数値を Data 化する範囲**  
役割別の既定報酬（近接/遠距離/強敵）を `SO_Reward_*` として 3 つ作り、各 `EnemyArchetypeData._reward` に割り当てる、で良いか。値の目安（例：近接 10 / 遠距離 12 / 強敵 40）を仕様側で決めてほしい。

---

## 10. 引き継ぎ書との差分・補足

- 引き継ぎ書 §2 の「実付与は購読側（Phase 4）で実装」は正確。コード上も**購読者ゼロ**であることを確認した。
- 引き継ぎ書は `RewardData` を「型として存在」と書いているが、実際には**アクセサ欠落（ItemId）**と**未解釈フィールド（GrantOnce）**があり、受け手の実装と同時に Data 層の小改修が要る。
- 同様に `CompanionData` も `LeaveRecoverySeconds` が非公開のまま（仲間タスク時の申し送り）。
- 敵 Archetype の `_reward` は全て未割当（3 種は直列化すらされていない）。**報酬が動くことを確認するには SO 新規作成が必須**。
- 引き継ぎ書の「EditMode 178 本／PlayMode 18 本」は**テストファイル数**（実測一致）。テストメソッド数は EditMode が `[Test]` 1046 ＋ `[TestCase]` 13、PlayMode が `[UnityTest]` 27。全緑の維持コストを見積もる際はこちらの規模で考える。

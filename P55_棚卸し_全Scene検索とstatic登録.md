# P5.5 棚卸し：全 Scene 検索と static 登録（仕様書 §4.3）

- 日付：2026-09-26／工程 P55-01／親 SHA `2f10372`（P5 先端）
- 目的：Area を 2 つ同時に読み込む（Active 1 ＋ Staged／Prepared／Retiring 1）前提で、
  **非活動 Area のものがゲームから見えてしまう**経路を先に全部挙げる。
- 範囲：`Assets/_Project/Scripts`（Editor を除く）全件を機械走査し、一件ずつ判定した。

## 1. static Provider（単一の `Current` を持つもの）：11 件

| Provider | 書込元 | 層 | 所有者一致の解除 | P5.5 での扱い |
|---|---|---|---|---|
| `GameModeProvider` | `GameModeBootService` | Infrastructure | あり | 常駐が所有。変更なし |
| `GameSessionProvider` | `GameSessionBootService` | Infrastructure | あり | 常駐が所有。変更なし |
| `GameplayClockProvider` | `AreaTransitionService` | Infrastructure | あり | 常駐が所有。変更なし |
| `PlayerInputProvider` | `InputBootService` | Infrastructure | あり | 常駐が所有。変更なし |
| `InputReleaseGateProvider` | `InputBootService` | Infrastructure | あり | 常駐が所有。変更なし |
| `RespawnSubmitProvider` | `InputBootService` | Infrastructure | あり | 常駐が所有。変更なし |
| `RespawnSubmitOwnerProvider` | `CampaignRespawnResidentView` | Infrastructure | あり | 常駐が所有。変更なし |
| `CampaignRespawnTravelProvider` | `AreaTransitionService` | Infrastructure | あり | 常駐が所有。変更なし |
| **`CompanionActivityProvider`** | **`CompanionActivityContext`** | Gameplay | あり | **Area 所有。要対処（下記）** |
| **`OffscreenWarningProvider`** | **`EnemyEdgeWarningView`** | Presentation | あり | **Area 所有。要対処** |
| **`ScreenBoundsProvider`** | **`CameraScreenBoundsProbe`** | Presentation | あり | **Area 所有。要対処** |

**8 件は常駐が所有**しており、Area が 2 つになっても取り合いは起きない。

**残り 3 件は Area Scene の部品が書いている。** 解除は所有者一致なので「出ていく側が入ってきた側を消す」事故は起きないが、
**Staged／Prepared の Area が先に有効化されると、活動中 Area の `Current` を奪う**。
対処は §4.2 の構造ゲート——これらの部品を含む GameplayRoot／Presentation を
**Scene 保存時から非 Active** にし、Commit でだけ有効化する。Validator で保存状態を検査する（§10.2）。

> 「読み込んでから Find して無効化する」では `Awake`／`OnEnable` の副作用に間に合わない（仕様書 §4.2）。
> 保存状態そのものを非 Active にするのが唯一の確実な手。

## 2. static Registry（複数を登録するもの）：5 件

| Registry | 登録元 | 登録の契機 | 非活動 Area が混ざると |
|---|---|---|---|
| `AreaInteractableRegistry` | `AreaFlagLever`／`AreaTransitionDoor`／`InvestigationInteractable` | `OnEnable` | **境界の向こうのレバー・扉・調査対象が Interact 候補に挙がる** |
| `InvestigationPointRegistry` | `CompanionInvestigationPoint` | `OnEnable` | 犬丸が隣 Area の地点へ調査に行こうとする |
| `PerceptionTargetRegistry` | `EnemyActor`／`PerceptionTargetBinder`／`CompanionThreatBinder` | `OnEnable` | **境界越しの索敵**。仕様書が対象外と明記した挙動が起きる |
| `EnemyProjectileRegistry` | `EnemyProjectile` | 生成時 | 境界越しの Projectile |
| `CompanionFeedbackRegistry` | `CompanionFeedbackBinder` | `OnEnable` | 非活動 Area の仲間の演出が混ざる |

**5 件すべてが Area Scene の部品から `OnEnable` で登録する。** ここが P5.5 最大の危険箇所。

### 2.1 構造ゲートだけでは足りない理由

Staged の間は GameplayRoot が非 Active なので登録は起きない。
しかし **Prepared は「到着先 Actor を復元・配線し、活動ゲートは閉じたまま」**（仕様書 §4.1）。
Actor を復元するには Actor を有効化する必要があり、そこで
`PerceptionTargetBinder`／`CompanionThreatBinder`／`CompanionFeedbackBinder` が登録してしまう。

したがって仕様書 §4.3 のとおり、**登録に Area の帰属を持たせ、利用者が非活動 Area を除外できる契約**が要る。

### 2.2 採る方針

1. 登録時に**所属 Area（Scene handle と世代）**を一緒に渡す。
2. Registry は「活動中 Area のものだけ」を既定で返す列挙を提供する。全件列挙は診断用に残す。
3. 活動中 Area の指定は常駐（`CurrentArea`）が持ち、Commit で切り替える。
4. 既存の呼び出し側（索敵・Interact・調査・演出）は既定の列挙へ寄せる。

`EnemyProjectileRegistry` だけは生成時登録で、Projectile は活動中 Area にしか生まれない。
ただし遷移の瞬間に残存する可能性があるので、Retiring 時に所属ぶんを一括解除する。

## 3. 全 Scene 検索（`FindFirstObjectByType` 等）

常駐（DontDestroyOnLoad）から Scene 側を探している箇所は 3 つで、いずれも P5.5 で**Scene 限定 or 明示 Binding へ変える必要がある**。

| 箇所 | 現在 | P5.5 |
|---|---|---|
| `AreaTransitionService.CaptureActors` → `AreaActorTransferPort` | 全 Scene 検索 | **出発 Area の Bundle から取る** |
| `AreaTransitionService.FindCurrentContext` → `AreaContext` | 全 Scene 検索 | **CurrentArea の Bundle から取る** |
| `CampaignRespawnResidentView.ShouldTakeOver` → `RespawnSubmitInput` | 全 Scene 検索 | **CurrentArea の Bundle から取る**（無ければ肩代わり） |

Presentation 側の `FindFirstObjectByType`（`CombatPlayHud`／各 Presenter／`CombatFeedbackDispatcher` 等）は
**同じ Scene 内の依存解決**で、寿命をまたがない。ただし 2 Area 同時読込では「同じ Scene 内」の保証が崩れるものがあるため、
P55-02 で Area Scene ごとの Bundle 経由へ寄せる。

## 4. 仕様書 §4.3 が名指しした部品の現状

| 部品 | 現在の解決方法 | P5.5 |
|---|---|---|
| `AreaActorTransferPort` | 全 Scene 検索 | Bundle |
| `AreaContext` | 全 Scene 検索 | Bundle |
| `AreaInitializer` | Scene 内 | Bundle（世代つき） |
| `CampaignRespawnRunner` | 全 Scene 検索（常駐の肩代わり判定） | Bundle。無ければ常駐が肩代わり（P5 の契約を維持） |
| `AreaEncounterRunner` | `AreaInitializer` が明示 Bind | 変更なし（Area 所有） |
| `AreaCameraRegion` | `AreaCameraRig` が Scene 内で収集 | **単一常駐 Rig へ変更**（§4.3）。領域は Area 所有のまま Rig が参照 |

## 5. この棚卸しから決まる P55-01 の成果物

1. **`AreaRuntimeBundle`**：その Area Scene の参照集合（Context／Port／Initializer／Runner／
   CameraRegion／ExitGate 等）。常駐は必ずこれ経由で触る。
2. **Area ハンドル（Scene handle ＋ ロード世代）**：同じ AreaId の別インスタンスを区別する。
3. **Registry の Area 帰属**：上記 §2.2。
4. **活動段階（`Unloaded`／`Staged`／`Prepared`／`Active`／`Suspended`／`Retiring`）** の純粋ロジック。

## 6. 判定できなかったもの・後続

- `CompanionFeedbackRegistry` の `ClearAll` は試験用。Retiring の一括解除と役割が重なるので、P55-02 で整理する。
- P3.5／P4 の Scene は単一 Area 前提のまま。仕様書 §1.2 のとおり Camera を一括改造しないので、
  これらは従来経路で動かす（互換回帰で確認）。

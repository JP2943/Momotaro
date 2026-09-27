
---

## 記録 005：P55-02c（活動ゲート／保存状態そのものを閉じる）

### 1. なぜ「保存状態を閉じる」なのか

仕様書 §4.2 の要点は一行で、**「読み込んでから Find して無効化する」では間に合わない**。
Scene が読み終わった時点で `Awake`／`OnEnable` はもう走り終わっていて、
Provider の奪い合いも登録簿への混入も済んでいる。あとから無効化しても後の祭り。

そこで `AreaActivityGate` を置き、**Builder が閉じた状態で保存する**ようにした。

### 2. 閉じ方は 3 通り（見た目は残す）

§4.1 の Staged は「**地形・仕掛けの表示準備だけ済んだ**。Gameplay・物理・購読はすべて無効」。
つまり見えていなければならない。根こそぎ非 Active にすると地形が消えるので、3 通りに分けた。

| 閉じ方 | 対象 | 効果 |
|---|---|---|
| 根を非 Active | Gameplay の実体を抱える AreaRoot 直下（AreaSystems・主人公・犬丸） | 中身が丸ごと動かない |
| Collider を無効 | 地形・仕掛けの当たり | **見えるが当たらない** |
| 部品を無効 | 仕掛けの登録（レバー・扉・調査対象・出入口） | **見えるが `OnEnable` が走らない** |

### 3. 既定は「読み込んだらすぐ開ける」

閉じたまま待つのは、常駐が `AreaStagingRequest` で**名指しで頼んだ Area だけ**。
それ以外は `Awake` で開ける。だから P3.5／P4／P5 の単一 Area 構成・直開き・既存テストは
従来どおり動く（実際、この工程で既存 1718 件・124 件に 1 件の後退も出ていない）。

**要求に宛先を持たせた。** 無条件フラグにすると、要求したのに Scene が読まれなかったとき
（開始失敗・タイムアウト）にフラグが残り、**次に直開きした Scene が閉じたまま起動する。**
「何も動かないゲーム」は原因が最も追いにくい壊れ方なので、宛先違いなら素通りさせて要求は取っておく。

### 4. テストが実際の欠陥を捕まえた

最初の実装では `AreaSystems` だけを閉じていた。PlayMode の
「先読みでは何も起きない」テストが **`PerceptionTargetRegistry.Count` が 2** で落ちた——
**主人公と犬丸は AreaRoot の直下**に居るので、AreaSystems を閉じても
索敵対象として登録され、AI と物理が動き出していた。

登録簿（`AreaInteractableRegistry`）が空だったので、仕掛け側の閉じ込みは効いていた。
つまり「半分だけ閉じている」状態で、**見た目には静かなのに隣 Area の主人公が索敵される**という、
最も気付きにくい形になっていた。

直し方は名前ではなく**部品で見分ける**形にした（`AreaContext`／`PlayerRoot`／`CompanionActor` の
いずれかを抱える AreaRoot 直下を閉じる）。Prefab 名や配置が変わっても漏れないようにするため。

### 5. 出荷状態を検査する

実行時は `Awake` が開けてしまうので、「保存時から閉じているか」は**Scene を開いた状態でしか見られない**。
`Phase5ExplorationValidator` の初期状態検査に `IsClosedAsSaved` を足し、
`P5ValidatorTests.AnOpenActivityGate_FailsValidation` が**3 通りの閉じ方をそれぞれ独立に崩して**
検査が気付くことを確かめている。どれか一つを見落とす実装だと落ちる。

### 6. 欠陥注入で確かめたこと

| # | 注入 | 結果 |
|---|---|---|
| I | 要求の重複を許す／宛先照合を外す／空のゲートを配線済みと見る／保存状態の Collider・部品検査を外す | EditMode 4 本＋Validator 1 本が失敗（検知） |
| J | 開閉が Collider・部品に効かない／`Awake` が先読み要求を無視する | EditMode 2 本＋PlayMode 3 本が失敗（検知） |

`Open_IsIdempotentAndSurvivesBrokenReferences` は「壊れた参照が 1 つ混ざっているだけで
Area が丸ごと起動しない」ことを防ぐための検査で、null 保護を外せば例外で落ちる。

### 7. 追加したテスト

- **EditMode `P55ActivityGateTests`（7 本）**：先読み要求の単一性・宛先照合・取り下げ、
  配線検査、保存状態の判定（どれが開いていたかを名指しする）、3 通りの開閉、壊れた参照への耐性。
- **EditMode `P5ValidatorTests.AnOpenActivityGate_FailsValidation`（1 本）**：出荷 Scene を
  3 通りに崩して検査が落ちること。
- **PlayMode `P55ActivityGatePlayTests`（3 本）**：
  - 要求が無ければ読み込んだその場で開き、初期化も登録も従来どおり走る。
  - **要求があれば読み込んでも何も起きない**——Gameplay は止まり、
    `AreaInteractableRegistry` も `PerceptionTargetRegistry` も**空のまま**。
    束は活動ゲートの外なので、閉じている間も索引から引ける（§4.3）。開ければ動き出す。
  - 宛先違いの要求は素通りし、要求はそのまま残る。

### 8. 検証

| id | 内容 | 結果 | 所要 |
|---|---|---|---|
| ac01〜ac22 | compile-status（force） | 警告 0 件 | — |
| ac10 | run-op `build-exploration-trial` | Scene 3 件を閉じた状態で再生成 | — |
| ac11 | run-op `validate-exploration-trial` | 通過（既知の警告 3 件のみ） | — |
| ac16・ac20・ac21 | 欠陥注入 2 通り | 10 本が想定どおり失敗 | — |
| **ac23** | **EditMode 全件** | **成功 1726 / 失敗 0 / 予定 1726** | **75.1 秒** |
| **ac24** | **PlayMode 全件** | **成功 127 / 失敗 0 / 予定 127** | **196.2 秒** |
| ac27 | run-op `validate-project-data` | すべて通過 | — |
| ac26 | run-op `verify-required-tests`（**P5**・ac23,ac24） | **P5 必須 64/64 Passed**（互換回帰） | — |
| ac25 | run-op `verify-required-tests`（**P5.5**・ac23,ac24） | **P5.5 必須 11/11 Passed** | — |

EditMode 1718 → 1726 件、PlayMode 124 → 127 件。

### 9. 工程の状態

| 工程 | 状態 |
|---|---|
| P55-00・P55-01・P55-02a・P55-02b | **完了** |
| P55-02c | **完了**（活動ゲート・出荷時閉鎖・先読み要求・Scene 検査） |
| P55-02d | 次。Additive 先読み（直列化は台帳が持つ）。ここで §11 の P04 を通す |
| P55-03〜06 | 未着手 |

### 10. P55-02d で最初に手を付けること

1. **Additive の読込口**。いまの `IAreaSceneLoader` は `LoadSceneMode.Single` 前提で、
   どの Scene を読んだかも返さない。先読みには「追加で読む・読んだ Scene を返す・撤去する」が要る。
   P5 の口はそのまま残す（互換回帰のため）。
2. **直開き時の現行指定**。いまは遷移の到着時にしか `CurrentAreaProvider` へ入らない。
   単一 Area なら絞り込みが不要なので困らないが、2 枚目が載る瞬間から必要になる。
3. **先読み要求と台帳の結線**。直列化（同時に 1 つの Scene 操作）は `AreaResidencyLedger` が既に持つ。
4. 残る 3 件の Provider（`CompanionActivityProvider`／`OffscreenWarningProvider`／
   `ScreenBoundsProvider`）は、活動ゲートで塞げたかを 2 Area 同時読込の実地で確かめる。
   `CompanionActivityContext` は AreaSystems の下なので閉じるが、**実地での確認は P55-02d**。

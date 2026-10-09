# P6C 統合受入結果

仕様の正本：`桃太郎プロジェクト P6C ジャスト回避と防御反撃実装仕様 v1.0.md`（2026-10-09、v1.0）。
受入条件との対応は `P6C_受入条件対応表.md`、必須テストの正本は `P6CRequiredTests.json`、触りかたは `README_Phase6C_防御反撃試遊.md`、
後続へ送る事項は `P6C_後続課題.md`。

---

## 記録 001：P6C 00（基点・既存実装の棚卸し・変更する契約）

### 1. 基点

- ブランチ `phase/6-progression-save`。**親 SHA `8bb112c73966d5ba0f2e9390c52690186e9c6e17`**（P6B 記録 005。origin と一致を確認）。
  仕様 §1 の作成時点の値と同じ。main（`8e7d3c9`）は P6B を含まないので基点にしない。
- 着手時に読んだもの：`CLAUDE.md`、P6B の修正・受入記録（`P6B_統合受入結果.md` 記録 001〜005、`Momotaro_P6B_Review_ddb2d19.md`）。
  P6B の受入完了はここで宣言しない（P6B 記録 005 のとおり、最終受入はオーナー判断待ち）。
- 実行の許可：オーナーから「PlayMode・EditMode 全件・Scene Builder・Windows 実ビルドを都度確認なしで実行してよい」（2026-10-09）。

### 2. 既存のジャスト回避（置換前）

| 箇所 | 置換前の実装 | P6C での扱い |
|---|---|---|
| `StepState` | `CanJustEvade`＝ステップ中・未成功・経過 < 受付窓。`NotifyJustEvadeSuccess` で同じステップ中は閉じる。`Begin` で開き直す | **そのまま再利用**（1 ステップ 1 回の成功管理） |
| `PlayerStateController` | `IJustEvadeState` を実装。受付窓 `_justEvadeWindowSeconds=0.12` と体幹反射 `_justEvadeCounterPoise=20` を**主人公 Prefab のコンポーネント**に持つ（Prefab・Scene には直列化されておらず、コード既定値で動いていた） | 2 つの項目を撤去し、**受付窓・倍率・有効時間は `StepData` に集約**（正本を 1 つに）。成功で反撃強化を付与 |
| `PlayerVitalsHolder.ReceiveHit` | 被弾後無敵 → ステップ無敵（Steppable）→ その中で `CanJustEvade` なら**攻撃者へ体幹反射 20 ＋ 近接攻撃者へ強制ひるみ 0.35 秒**、`JustEvade` を通知 | 体幹反射と強制ひるみを**削除**し、強化付与（`NotifyJustEvadeSuccess`）へ置換。成功の対象を「敵の攻撃」に限定 |
| `IJustEvadeState` | `CanJustEvade`・`JustEvadeCounterPoise`・`NotifyJustEvadeSuccess` | `JustEvadeCounterPoise` を撤去 |
| `HitInfo` | 攻撃の出所（敵の攻撃か・環境か）を持たない。`Guardable`／`JustGuardable`／`Steppable` は独立 | 「敵の攻撃である」を命中インスタンスに載せる（射手が退場した矢でも判断できる） |
| `EnemyHitFactory.Build` | Snapshot の `Guardable`・`JustGuardable`・`Steppable` をそのまま HitInfo へ | 敵の攻撃である印を付ける。可否は上書きしない（現状も上書きしていない） |
| `EnemyAttackData.Validate` | `Unblockable` は `Guardable=false` かつ `JustGuardable=false` を要求、`Steppable=true` を要求 | **`JustGuardable=false` の要求を撤去**。原則 false／true／true。JG も不可にするのは理由を明記した例外だけ |
| 通知と音 | `HitResultKind.JustEvade` → `CombatFeedbackMap`（`VFX_JustEvade`・`SE_JustEvade`・ヒットストップ 0.07）→ `CombatFeedbackPresenter`（点滅＋小さい揺れ）。音素材 `SE_JustEvade` は P3.5 の Builder だけが配線。**P6 の Scene の CombatFeedback には SE の再生役が無い** | 通知と表示を再利用。P6C の Scene に SE の再生役を置く。Presenter は反射・ひるみをしていない（確認済み） |

### 3. 実値（着手時）

- `SO_Step_Momotaro`：移動 0.20 秒・後硬直 0.10 秒・**無敵 [0.05, 0.20)**・消費 25・連続窓 0.12・距離 3。コード既定値と同じ。
- 受付終端 0.12（コード既定）→ **ジャスト成功が起こりうるのは [0.05, 0.12) の 0.07 秒**。無敵開始前（0〜0.05）は無敵でないので回避自体が起きない。
- 主人公 Prefab `PF_Player_Momotaro` は `_stepData` を参照。`_justEvadeWindowSeconds`／`_justEvadeCounterPoise` は Prefab にも Scene にも直列化されていない（全 `.unity`／`.prefab` を検索して 0 件）。

### 4. 防御可否 Data の棚卸し

| アセット | 分類 | Guardable／JustGuardable／Steppable | 種別 |
|---|---|---|---|
| `SO_EnemyAttack_Melee_Normal` | Normal | 1／1／1 | 斬撃 |
| `SO_EnemyAttack_Elite_Normal` | Normal | 1／1／1 | 斬撃 |
| `SO_EnemyAttack_Elite_Heavy` | Heavy | 1／1／1 | 斬撃 |
| `SO_EnemyAttack_Elite_Charge` | Charge | 1／1／1 | 突進斬り |
| `SO_EnemyAttack_Ranged_Shot` | Projectile | 1／1／1 | 矢 |
| **`SO_EnemyAttack_Elite_Unblockable`** | Unblockable | **0／0／1** | 刀の突き（表示名 `UnguardableThrust`）＝打撃・斬撃系 → **0／1／1 へ変更** |

コードで攻撃 Data を生成する Builder は無い（Editor 以下に `_justGuardable` の書き込みなし）。仲間（犬丸）の被弾解決は `Guardable` だけを見て JG を持たない
（`CompanionHitReceiver`）ので、共有 Data の変更は犬丸の挙動に及ばない。敵側の JG は主人公の攻撃（`AttackData`）が対象で、敵攻撃 Data とは無関係。

### 5. 入力・時間・中立化

- 回避は既存のステップ（Space／パッドの回避）。新ボタンなし。
- `PlayerStateController.Tick` は遷移凍結（`GameplayClockProvider.IsFrozen`）で止まる。ヒットストップは `timeScale` を下げるので `deltaTime` も縮む。
  Pause・メニュー中は入力が閉じる（`Active=false`）。
- 入場・死亡再開・休息（成長・払い戻し・旅立ちの成功を含む）はすべて `PlayerStateController.ResetForAreaEntry` を通る
  （`AreaInitializer` の入場・同 Area 内の旅立ち、`AreaActorTransferPort.RestoreForCampaignRespawn`／`RestoreForRest`）。強化の消去はここ 1 か所に置く。

### 6. 置換で期待値が変わる既存テスト

| テスト | 旧期待 | 新期待 |
|---|---|---|
| `PlayerEvadeTests.JustEvade_Window_ReflectsPoise_ForcesFlinch_AndPublishesJustEvade` | 体幹反射 25・強制ひるみ 0.35 | 反射・ひるみなし、成功通知 1 回、`JustEvade` 通知 1 回（名前も変える） |
| `PlayerEvadeTests.Invincible_ButOutsideJustWindow_IsPlainEvade_NoCounter` | 反射・ひるみなし | 同じ（Fake の `JustEvadeCounterPoise` を撤去するだけ） |
| `EliteAttackDataTests.Unblockable_NotGuardable_NotJustGuardable_ButSteppable` | 出荷アセットが JG 不可 | JG 可（名前も変える） |
| `EnemyDataValidationTests.Attack_Unblockable_MustDisableGuardAndJustGuard` | Guardable=true でエラー | 同じ（Guardable=true はエラーのまま）。JG 可は合格、理由なしの JG 不可はエラーを追加 |

`HitInfoTests`・`AttackSnapshotTests`・`HitBuilderTests` の `JustGuardable=false` は**フラグが保持されること**の検査で、分類と無関係なので変更しない。

---

## 記録 002：P6C 01〜04（実装・試遊・必須受入・回帰・実ビルド）

**対象コード SHA `dcd6346`**（親 `8bb112c`）。証跡は `Evidence/P6C_dcd6346/`。実行はすべて Unity 6000.3.20f1 の Editor 常駐ブリッジから、
作業ツリーを `dcd6346` と同じ内容にした状態で行った（コミットの前後で中身が一致することを md5 で照合）。

### 1. 実装の要点

| 工程 | 内容 |
|---|---|
| P6C 01 | `PlayerVitalsHolder.ReceiveHit` のジャスト回避の経路から**体幹反射・強制ひるみを撤去**。成功は「敵の攻撃（`HitInfo.IsEnemyAttack`。敵の近接・矢の生成経路が付ける）・`Steppable`・ステップ無敵中・受付中（1 ステップ 1 回）」のときだけで、被弾後無敵は従来どおり先に評価。回避した一撃は守護へ転送しない。`IJustEvadeState` から反射量を撤去し、`NotifyJustEvadeSuccess` が受付を閉じて強化を付与する。受付 0.12・倍率 1.5・有効 2.0 は `StepData`（`SO_Step_Momotaro` に値を書き込み済み）に集約し、`StepData.TryValidateJustEvade` で「無敵開始 < 受付終端 ≤ 無敵終了」・有限・時間 > 0・倍率 ≥ 1 を検査 |
| P6C 02 | `JustEvadeCounterState`（1 回分・残時間の更新のみ・満了時刻未満が有効）。通常攻撃の**段が実際に開始したフレーム**で消費して、その段の `HitId` の命中だけ攻撃側寄与へ「成長倍率 × 反撃倍率」を掛ける（体幹・ひるみ・必殺には掛けない）。時計は Gameplay 時計（凍結中は止まる、ヒットストップは刻みが縮む、Pause は入力が閉じて止まる、被弾硬直中も減る）。死亡・`ResetForAreaEntry`（入場・休息・成長・払い戻し・旅立ち・死亡再開）・Disable で消去。保存しない |
| P6C 03 | `EnemyAttackData.Validate`：ガード不能は `Guardable=false`・`Steppable=true` を要求し、`JustGuardable=false` は理由（`_justGuardExceptionReason`）とガード不能の予兆つきの例外だけ。`SO_EnemyAttack_Elite_Unblockable` を 0／0／1 → **0／1／1**（JG 体幹反射 0 → **18。仮値**、強攻撃と同じ値）。文言は `EnemyAttackDefenseNotice`（「通常ガード不可・ジャスガ可能」／例外は「ガード不可・ジャスガ不可（理由）」と別記号） |
| P6C 04 | P6C プロファイル（P6B 構成の再利用・専用 campaign `campaign_p6c`・ID `area_p6c_*` 等・成長ノード ID `growth_p6c_*`・保存 `p6c_slot0`）、`JustEvadeHudPresenter`（同じ Scene の主人公・予告にだけ結び、活動中の Area のときだけ描く）、P6C の Scene の手応え演出に結果 SE（JG・ガード・**SE_JustEvade**）を接続、ブリッジ op `build-phase6c-world`／`validate-phase6c-world`／`p6c-player-smoke`、実ビルドの自動操作 `justevade` |

旧報酬を除去した経路：`PlayerVitalsHolder.ReceiveHit` のステップ無敵の分岐（反射・ひるみの呼び出しを削除。JG の反射・ひるみは不変）、
`IJustEvadeState.JustEvadeCounterPoise` と `PlayerStateController._justEvadeCounterPoise`／`_justEvadeWindowSeconds`（Prefab・Scene に直列化なし）。
Presenter・通知の購読先は反射・ひるみをしていなかった（変更なし）。共通の戦闘コードなので、P3.5 などの旧試遊でも新しい報酬になる（旧試遊用の反射モードは作っていない）。

### 2. 実敵・実キーの確認（PlayMode。時系列 `p6c_just_evade_timeline.txt`）

| 確認 | 結果 |
|---|---|
| A の普通敵（近接）の通常の攻撃に Space＋方向キー | ステップ開始から **0.081 秒**で JustEvade（1 回）。敵の攻撃は継続（中断なし）。SE_JustEvade・点滅・「ジャスト回避！」・保有表示。方向キー＋J の段の開始で消費 → 敵へ **14**（攻撃側寄与 15、防御 10） |
| 同じ攻撃を早い回避（判定の 0.17 秒前） | 0.168 秒で通常の回避（Evade）→ 反撃 **9**（強化なし） |
| B 南の遠距離敵の矢 | 0.092 秒で JustEvade。射手の体幹 34.0 → 34.0（反射なし）、矢は消滅 |
| C の仮ボス（精鋭）の突き（ガード不能）| K 押しっぱなし → **Damage**。次の突きに直前で K → **JustGuard**、精鋭の体幹 200 → 182（反射 18）。予告中「通常ガード不可・ジャスガ可能」を表示 |
| 保存・移動・休息・Continue・きびだんご・二つの Area | 採取で消えない、存在しない入口への移動要求（失敗）で消えない、A→B のスライド成功で消える、B 活動中も A は在留し A の表示・手応えは増えない、B→A 再入場後は A の表示が A の主人公に結び直る、F の使用中は Space・K が効かず使用は中断されず強化も消費されない、休息・死亡で消える、Continue 後は強化なし・徳は保持 |

観察：近接の敵へ向かって回避すると、敵の体の手前（0.9m）で止まって前に抜けない（後続課題 H08。今回の判定・強化には影響しない）。

### 3. 修正を外して落ちることの確認

`PlayerVitalsHolder` に旧報酬（反射・ひるみ）を戻し敵の攻撃の条件を外す、`PlayerStateController` の段の開始での消費と入場での消去を外す、を一時的に入れた版で
`P6CJustEvadeTests`／`PlayerEvadeTests` を実行（`em1`）：**10 件が失敗**（境界・環境接触・反射なし・消費・倍率・満了・複数対象・消去）。元へ戻して 19／19（`em2`）。

### 4. 最終確認（`dcd6346`）

| 実行 | 内容 | 結果 |
|---|---|---|
| `ge2` | EditMode 全件 | **1916／1916**（+20 は P6C） |
| `qp1`〜`qp7` | PlayMode 7 分割（P55 スライド・往復・その他、既存 115、P6A、P6B、P6C） | 64・13・72・115・20・9・**4** ＝ 297／297 |
| `vfP4`〜`vfP6C` | `verify-required-tests`（`ge2,qp1,…,qp7`） | 226・93・293・73・33・**29**、すべて Passed、未説明の Skip なし |
| `v-validate-phase6c-world`・`-6b-`・`-6-` | 世界検査（P6C と、ID の作り方を変えた P6A・P6B の回帰） | すべて合格（警告 1 件は既存の「遭遇戦の無い Area」） |
| `p6c-player-smoke` | P6C の Windows 実ビルド（36 秒）→ 画面ありで `justevade` → 別プロセス `continue` | 実キーで JE 1 回・強化された段 1 回・反撃 **14 対 通常 9**・強化を持ったまま正常終了（Saved）→ 別プロセスで同じ冒険・**強化なし**・徳 300・HP 100・未保存なし。11 項目すべて OK |
| `p6b-player-smoke`・`p6a-player-smoke` | 共有のスモーク駆動を変えたので回帰 | P6B：成長の復元・使用中の終了すべて OK。P6A：OK、実プレイ 16 撃破・p99 16.7ms・最大 22.8ms |

途中で実行したもの：コンパイル都度（0 警告）。EditMode の P6C 関連（`e1` 108／110：期待値の書き方の誤り 2 件 → `e2` 14／14）。
PlayMode の P6C を個別に `p1`〜`p19`・`pc1`（作成中の版。敵が持ち場へ戻り続ける置き方・犬丸が敵を倒す／かばう・ガードの押し戻し・
表示用のスタミナ値だけを満たしていた・出入口の近くで後ろ向きに回避した、などテスト側の手順の誤りを直した。製品の判定・強化は途中で変えていない）。
EditMode 全件 `ge` は 1915／1916（Builder の部品の検査漏れ：`JustEvadeHudPresenter` を P6C 世界検査が見ることを理由に免除へ追加 → `ge2` で合格）。
Builder の生成（`build-phase6c-world`）1 回。EditMode 全件が作り直す P5 の Scene と、Builder が書き換えた P5 の素材（浮動小数の揺れだけ）は HEAD へ戻した。

### 5. 仮数値・変更した防御可否・未実施

- 仮数値：受付 0.12 秒・1.5 倍・2.0 秒（仕様の試作初期設定）、精鋭の突きの JG 体幹反射 **18**（新規に置いた値）。
- 防御可否を変更した攻撃：`SO_EnemyAttack_Elite_Unblockable`（精鋭の刀の突き）1 件だけ。ほかの攻撃は変更なし（記録 001 §4）。
- 未実施：人間確認 3 点（README §5）。仕様の試遊項目の「複数対象・成長ありの反撃・空振り・時間切れ」は EditMode と実ビルドで確認、実敵の複数対象は未確認。

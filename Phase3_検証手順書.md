# Phase 3 検証手順書（敵AI・敵戦闘 / P3-12 統合受入）

Phase 3（敵AI・敵戦闘）を Phase 2 と接続した戦闘ループとして受け入れるための検証手順・構成・既知の制約・Phase 4 への引き継ぎ契約をまとめる。

## 1. 検証 Scene / Prefab

検証は既存の垂直スライス Scene **`SCN_VS_Field`** 上で行う（Plane・壁・Camera 境界・Player・敵配置が既にある）。この Scene に P3-11/P3-12 で以下を結線済み。

- **固定シナリオ Launcher**：`PF_EnemyScenarioLauncher`（`EnemyScenarioLauncher`）… 近接1／遠距離1／強敵1／3体混成（近接2＋遠距離1）。
- **性能ハーネス**：`PF_EnemyPerformanceHarness`（`EnemyPerformanceHarness`）… 近接6／近接4＋遠距離2／最大8。
- 近接・遠距離・強敵の各プロトタイプ Prefab には **頭上 HP/体幹バー**（`EnemyOverheadBars`）と **AI デバッグオーバレイ**（`EnemyAiDebugOverlay`、既定 OFF）を組み込み済み。
- **デバッグ一括切替**：`PF_EnemyPerformanceHarness` 上の `EnemyDebugToggle`。

Phase 5 の Encounter System は先取りしない（単純な生成のみ）。Scene 名・配置は既存規約に従う。

## 2. シナリオの開始方法（明示手順）

Play 中に対象コンポーネントを右クリックし、コンテキストメニューから実行する（Input 依存なし）。

- 近接1／遠距離1／強敵1／3体混成 → `PF_EnemyScenarioLauncher` の `EnemyScenarioLauncher` → 「Launch / 近接1・遠距離1・強敵1・3体混成」。「Clear / 全破棄」で撤収。
- 性能 3 分岐 → `PF_EnemyPerformanceHarness` の `EnemyPerformanceHarness` → 「Spawn / 近接6・近接4+遠2・最大8」。「Clear / 全破棄」で撤収。

いずれもリング状に生成し、切替時に前回分を自動破棄する（累積しない）。

## 3. デバッグ表示

- **頭上バー**：雑魚は HP を常時表示、体幹は被弾で削れている間だけ表示。強敵（`AlwaysShowPoise`）は体幹を常時表示。
- **AI オーバレイ**（Development 限定・オプトイン）：State／Target／Threat／選択 Attack／Score／Slot／LOS／活動範囲（Gizmo 円）を頭上表示。切替は各 `EnemyAiDebugOverlay._display`、または `EnemyDebugToggle` の「Debug Overlays / ON・OFF」で一括。
- **Profiler Marker**：`Momotaro.Enemy.Perception / Selection / Threat / Slot / Projectile` を Profiler で確認できる（計測専用・挙動不変）。

## 4. 自動テスト

- EditMode／PlayMode を Test Runner で全件実行する（Ignore は理由・Issue・解除条件がない限り不可）。
- 主な P3-12 追加：`Phase3IntegrationTests`（敵 Prefab の必須 Component、仮 UI 結線、Missing Script 無し、シナリオ編成、敵 Archetype Data の検証と Stable ID 重複）、`EnemyScenarioLauncherPlayTests`（各シナリオの実生成）、`EnemyPerformanceSpawnPlayTests`（性能分岐の実生成）。
- 既存の Data Validator（`ProjectDataValidator`／`ValidateOnBuild`）で Stable ID 重複・必須値・参照欠落を検査する。

## 5. 手動受入チェック（Test Runner 外）

- 視覚・聴覚・被弾の各経路で Alert する。LOS 喪失→最終確認位置→3秒継続→Return。
- 近接攻撃を Guard／JG／Step、背後反撃、Stun、Special 撃破。
- 遠距離弾を壁／Guard／JG／Step で処理し、JG が発射者 Poise へ返る。
- 強敵4攻撃を予兆で識別、ガード不能は Guard／JG 不可・Step 可。突進は横 Step、Stun 中に Special が間に合う。
- 3体混成で Slot 上限・Reposition・画面外抑制・警告。
- **最大8体を Target 60fps 環境で 60 秒以上動作**させ、Console Error 0／継続的な異常 GC Spike／Slot 詰まり／不可視弾が無いこと（計測環境と結果を報告）。
- Pause／Resume、Actor Disable、Scene 再読込後も AI Timer／Projectile／Slot／UI が正常。

## 6. 既知の制約

- 完成 HUD デザイン・完成撃破演出・最終最適化保証は対象外（仮 UI・仮演出）。
- 撃破時の徳／Item は「型付き報酬要求（`EnemyRewardRequest`）」を発行するのみで、実付与は行わない（受け手＝Phase 4 以降）。
- 危険観測（`PhysicsEnemyDangerSense`）はガード不能判定を攻撃側契約 `IAttackThreatSource` から読む。未実装のアクターは `ICombatActivityState` にフォールバックし、ガード不能は false 扱い。
- 検証 Prefab（Guard/Evade Variant、Harness、Launcher）は検証専用で本番フローには含めない。

## 7. クリーンアップ確認

- Test 専用の本番分岐（`#if TEST` 等）は無し。
- 命中経路は `IDamageable.ReceiveHit` の単一経路（`EnemyActor`／`CombatDummy` が共通 `EnemyVitals` を使用。重複した Damage Resolver は無し）。
- 雛形 `EnemyData` は `CombatDummy`（Phase 2 検証ダミー）の設定源として意図的に保持。

## 8. Phase 4 への引き継ぎ契約

- **撃破／報酬**：`EnemyActor.Defeats`（`EnemyDefeatChannel`）が Down 確定時に `EnemyDefeatedEvent`＋`EnemyRewardRequest`（敵ID・役割・任意 `RewardData`・位置）を1回発行。徳／Item の実付与を購読側で実装する。
- **ヘイト**：`IThreatTarget`／`EnemyThreatTracker.CurrentTarget` で対象を公開。仲間 AI（Phase 4）は読み取りで購読できる。
- **状態**：`EnemyActor.States`（`EnemyStateChannel`）が型付き遷移を通知。
- **防御観測**：`IAttackThreatSource`（攻撃側が「攻撃中／ガード不能／攻撃方向」を晒す）を新規アクターも実装すれば、敵の防御 AI が反応する。

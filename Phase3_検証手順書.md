# Phase 3 検証手順書（敵AI・敵戦闘 / P3-12 統合受入）

Phase 3（敵AI・敵戦闘）を Phase 2 と接続した戦闘ループとして受け入れるための検証手順・構成・既知の制約・Phase 4 への引き継ぎ契約をまとめる。検証は **Phase 3 専用 Scene**（`SCN_Phase3_EnemyTest`）で行う。既存の `SCN_VS_Field`（ユーザー手動配置）は変更しない。

## 1. 専用検証 Scene の生成（Editor ツール）

Scene は Unity Editor 上でツールから決定的に生成する（Scene YAML は手書きしない）。

1. Unity でコンパイル完了を確認する。
2. メニュー **`Momotaro > Phase 3 > Generate Enemy Test Field`** を実行する。
   - 既存の検証 Scene がある場合は上書き確認ダイアログが出る。キャンセルすると一切変更しない。
   - 必要な Prefab（Player／近接／遠距離／強敵）が欠けている場合は、壊れた Scene を保存せずエラー表示で失敗する。
3. 生成先：**`Assets/_Project/Scenes/Tests/SCN_Phase3_EnemyTest.unity`**
4. 生成後、Scene が開き `EnemyTestFieldController` が選択され、Console に生成先と構成が 1 回表示される。
5. 生成 Scene を保存・確認してコミットする。

ツール本体は Editor 専用アセンブリ（`Momotaro.Editor`）にあり、Player Build には含まれない。起動時・コンパイル時・Import 時には自動生成しない（メニュー実行時のみ）。同じプロジェクト状態なら毎回同等の構成になる。

## 2. 生成される Scene 階層

```
SCN_Phase3_EnemyTest
├─ Environment
│  ├─ Floor          (上面 Y=0、正スケール)
│  ├─ Wall_North / Wall_South / Wall_East / Wall_West  (正スケール)
├─ Player            (PF_Player_Momotaro、ルート Y=0)
├─ Main Camera       (Orthographic、Player 追従)
├─ Directional Light
├─ SceneMode         (GameplaySceneMode = Exploration。Player 操作可能)
├─ SpawnCenter       (生成中心)
└─ Phase3TestSystems
   ├─ EnemyTestFieldController  (編成の一元管理。近接/遠距離/強敵 Prefab 割当済み)
   └─ EnemyDebugToggle          (デバッグ表示の一括 ON/OFF)
```

初期状態：有効な `EnemyActor` は 0 体、Player 1 体、Main Camera 1 台。生成時に敵を自動 Spawn しない。

## 3. 編成の切替（明示操作）

編成は **`EnemyTestFieldController` の 1 箇所で一元管理**する（複数 Launcher の併用による重複は起きない）。Play 開始後、`Phase3TestSystems/EnemyTestFieldController` を右クリックし、Context Menu から選ぶ。

- `Formation / Clear`（0 体）
- `Formation / 近接1`（近接1）
- `Formation / 遠距離1`（遠距離1）
- `Formation / 強敵1`（強敵1）
- `Formation / 3体混成`（近接2＋遠距離1＝3）
- `Formation / 近接6`（近接6）
- `Formation / 混成6`（近接4＋遠距離2＝6）
- `Formation / 最大8`（近接8）

編成変更時は前回の検証敵を即時 非アクティブ化してから破棄し、`SpawnedEnemies` 子 Transform 配下へリング状（壁非接触・ルート Y=0）に生成する。手動配置物には触れない。

## 4. デバッグ表示

- **頭上バー**：雑魚は HP 常時、体幹は被弾中のみ、強敵は体幹常時。
- **AI オーバレイ**（Development 限定・既定 OFF）：State／Target／Threat／選択 Attack／Score／Slot／LOS／活動範囲。切替は `Phase3TestSystems/EnemyDebugToggle` の Context Menu「Debug Overlays / ON・OFF」で一括、または各敵の `EnemyAiDebugOverlay._display`。
- **Profiler Marker**：`Momotaro.Enemy.Perception / Selection / Threat / Slot / Projectile` を Profiler ウィンドウで確認。

## 5. 自動テスト

Test Runner で EditMode／PlayMode を全件実行する（Ignore は理由・Issue・解除条件がない限り不可）。P3-12 の主な追加：

- EditMode `Phase3EnemyTestFieldBuilderTests`：Editor ツールが一時パスへ生成、開ける、Missing Script 無し、Player1／Camera1／Controller1／初期敵0、Prefab 割当、Floor 上面 Y=0、Player ルート Y=0、負スケール無し、再生成で増殖しない、不正出力先は保存せず失敗。
- EditMode `EnemyTestFieldControllerTests`：実 Prefab で各編成の Scene 実総数一致（0/1/1/1/3/6/6/8）、切替で残留なし、Clear で 0、ルート Y=0。
- PlayMode `EnemyTestFieldPlayTests`：実 `EnemyActor` で編成の Scene 実総数一致、遅延破棄でも残留なし、数フレーム動作で例外なし。
- EditMode `Phase3IntegrationTests`：敵 Prefab 必須 Component、仮 UI 結線、Missing Script 無し、編成内訳、敵 Archetype Data 検証＋Stable ID 重複。

## 6. 手動受入チェック

- 視覚・聴覚・被弾で Alert、LOS 喪失→最終確認位置→3秒継続→Return。
- 近接攻撃を Guard／JG／Step、背後反撃、Stun、Special 撃破。
- 遠距離弾を壁／Guard／JG／Step、JG が発射者 Poise へ返る。
- 強敵4攻撃を予兆で識別、ガード不能は Guard／JG 不可・Step 可、突進は横 Step、Stun 中に Special が間に合う。
- 3体混成で Slot 上限・Reposition・画面外抑制・警告。
- **最大8体を Target 60fps 環境で 60 秒以上**動作（下表に記録）。
- Pause／Resume、Actor Disable、Scene 再読込後も AI Timer／Projectile／Slot／UI 正常。Clear→再編成が正常。

### 最大8体 性能確認 結果欄

| 項目 | 結果 |
|---|---|
| 計測環境（OS/CPU/GPU/Unity版） | |
| 平均 FPS | |
| 最低 FPS | |
| 継続的な異常 GC Spike | 有 / 無 |
| Console Error 数 | |
| Slot 詰まり | 有 / 無 |
| 不可視弾 | 有 / 無 |
| 計測時間（≥60秒） | |

Profiler Marker（Perception/Selection/Threat/Slot/Projectile）の負荷内訳も併記する。

## 7. 既知の制約

- 完成 HUD デザイン・完成撃破演出・最終最適化保証は対象外（仮 UI・仮演出）。
- 撃破時の徳／Item は型付き報酬要求（`EnemyRewardRequest`）を発行するのみで実付与しない（受け手＝Phase 4）。
- 危険観測（`PhysicsEnemyDangerSense`）はガード不能を攻撃側契約 `IAttackThreatSource` から読む。未実装アクターは `ICombatActivityState` にフォールバックし Unblockable=false。
- 専用 Scene の Player 操作は `GameplaySceneMode`（Exploration）に依存する。常駐サービス（入力等）が必要な場合は既存の Bootstrap 起動フローに従う。
- Phase 5 の Encounter System は先取りしない。

## 8. クリーンアップ

- P3-11/P3-12 で `SCN_VS_Field` へ一時追加した検証専用 Prefab インスタンス（`PF_EnemyPerformanceHarness`／`PF_EnemyScenarioLauncher`）は除去し、専用 Scene 側へ移行した。ユーザー手動配置物（Player 経路・敵・床・壁・Camera・既存の近接/遠距離/強敵配置）は不変。
- 重複していた `EnemyPerformanceHarness`／`EnemyScenarioLauncher` は削除し、編成正本を `EnemyTestFieldController` 1 つへ統合した。
- Test 専用の本番分岐は無し。命中経路は `IDamageable.ReceiveHit` の単一経路。雛形 `EnemyData` は `CombatDummy` 用に意図的保持。

## 9. Phase 4 への引き継ぎ契約

- **撃破／報酬**：`EnemyActor.Defeats`（`EnemyDefeatChannel`）が Down 確定時に `EnemyDefeatedEvent`＋`EnemyRewardRequest`（敵ID・役割・任意 `RewardData`・位置）を 1 回発行。実付与を購読側で実装。
- **ヘイト**：`IThreatTarget`／`EnemyThreatTracker.CurrentTarget`。
- **状態**：`EnemyActor.States`（`EnemyStateChannel`）。
- **防御観測**：`IAttackThreatSource`（攻撃中／ガード不能／攻撃方向）を新規アクターが実装すれば敵防御 AI が反応。

## 10. ユーザーが Unity 上で行う操作（必須）

1. Unity でコンパイル完了を確認。
2. `Momotaro > Phase 3 > Generate Enemy Test Field` を実行。
3. 生成 Scene を保存・確認。
4. Test Runner で EditMode／PlayMode 全件実行し、結果を確認。
5. `SCN_Phase3_EnemyTest` を Play し、上記手動受入（特に最大8体60秒性能）を実施・記録。
6. 結果と生成 Scene をコミット。

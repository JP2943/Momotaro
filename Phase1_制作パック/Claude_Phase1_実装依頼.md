# Claude Cowork：Phase 1 実装依頼

## 共通指示

- リポジトリ：`JP2943/Momotaro`
- 作業ブランチ：`phase/1-player-foundation`（存在しなければ`main`から作成）
- Unity：`6000.3.20f1`
- 正本：ゲーム仕様書v1.4、制作開始ガイド、`Phase1_プレイヤー基本操作_タスク票.docx`
- 1回の依頼では、指定したTaskだけを実装する。
- Package、Render Pipeline、公開API、Stable ID、Save field、既存Sceneを無断変更しない。
- 既存のPhase 0/0.5基盤を流用し、別系統のBootstrap・Input・GameModeを作らない。
- Compile Error 0、Console Error 0、対象テストと既存テスト成功を完了条件とする。
- Unity Editor操作が必要なAsset変更は、変更内容と手動確認方法を明記する。
- 作業終了時に、変更ファイル、実装結果、テスト結果、Console状態、手動確認、既知問題を報告する。
- 仮Spriteの取込は`Sprite_Handoff_Phase1.md`を正本とし、画像を生成・補間・再描画しない。

## 実行順

`P1-01 → P1-02 → P1-03 → P1-04 → P1-05 → P1-06 → P1-07 → P1-08 → P1-09 → P1-10 → P1-11 → P1-12`

各Taskが受入済みになるまで次へ進まないでください。

---

## P1-01 Player基礎Prefab

### 目的

後続の移動・戦闘・表示を安全に追加できるPlayer Root階層を作成します。

### 実装

- `PF_Player_Momotaro`を作成する。
- `CharacterRoot / PhysicsRoot / VisualRoot / ShadowRoot`の責務を分離する。
- 3D `Rigidbody`と`CapsuleCollider`を使用し、Playerの不要な回転を固定する。
- Visualは仮Primitiveまたは単純な仮表示でよい。
- Prefab参照を保持する薄いComponentは、既存asmdefの依存方向に従って配置する。
- `SCN_VS_Field`へPlayerを1体だけ配置する。

### 対象外

- 移動処理、入力処理、Animator、攻撃、ガード判定、Step、完成Sprite。

### テスト・受入

- PrefabにMissing Script/Referenceがない。
- Rigidbodyの回転が固定されている。
- ColliderとVisualのRootが分離されている。
- Sceneを再生してConsole Error 0。
- 可能なら必須Componentと階層のEdit Mode検査を追加する。

---

## P1-02 Player入力読取

- `IA_Momotaro/Gameplay/Move`と`Guard`を使用する。
- Player Gameplayコードが`InputActionAsset`やSceneを直接探索しない構成にする。
- 入力InterfaceとInfrastructure側Adapterを分ける。
- GameModeまたはAction MapがGameplayでない場合、MoveをゼロにしGuardを解除状態へ戻す。
- Move、Guard押下・保持・解除、Disable、購読解除をテストする。
- 攻撃等は入力を定義済みのままにし、Gameplay処理を実装しない。

---

## P1-03 XZ平面移動

- `Vector2`入力を`Vector3(x, 0, y)`へ変換する。
- 斜め入力を正規化し、斜めだけ速くならないようにする。
- `Rigidbody`をFixedUpdate系の処理で移動する。
- 通常移動にTransform直接書換えを使用しない。
- 移動速度をData化し、Magic NumberをPlayer Componentへ散在させない。
- Y方向移動なし、速度0、斜め速度一定、時間刻み依存をテストする。

---

## P1-04 4方向の向き

- `FacingDirection`を上下左右の4値で定義する。
- 無入力時は最後の有効方向を保持する。
- 斜め入力は優勢軸で決定し、完全同値の場合の規則を固定する。
- Stickの微小入力で向きが震えないようDeadzone後の値を利用する。
- Facing決定は純粋ロジックとしてテスト可能にする。

---

## P1-05 衝突と壁沿い移動

- 検証用の壁、角、狭い通路を配置する。
- 壁へのめり込み、押し続けたときの振動、Playerの回転を防ぐ。
- 斜め入力時、移動可能な接線方向へ壁沿い移動できることを確認する。
- 段差、坂、水、敵との押し合いは実装しない。

---

## P1-06 Player状態機械

- Phase 1では`Idle`と`Move`を実動作させる。
- 後から`GuardMove`等を追加できる小さな状態機械とする。
- Animator StateをGameplay状態の正本にしない。
- 状態とMotorの責務を分離する。
- Idle↔Move、同一状態への再入抑制、Disable時Idleをテストする。
- 攻撃・被弾・死亡状態を先回り実装しない。

---

## P1-07 ガード移動の基礎

- Guard押下時のFacingを固定する。
- Guard保持中は通常速度の`40%`で移動できる。
- 移動入力が別方向でも、Guard解除までFacingを変更しない。
- Guard解除後は現在入力に従って方向転換できる。
- 180度防御、スタミナ消費、Just Guard、防御成功処理は実装しない。
- 速度倍率と向き固定・解除を自動テストする。

---

## P1-08 トップダウンCamera

- Orthographic Cameraを固定した見下ろし角でPlayerへ追従させる。
- Player移動後にCameraを更新し、目立つJitterを避ける。
- Target消失時に例外を出さない。
- 16:9、1920×1080を基準に確認する。
- Camera境界、Boss Camera、演出Cameraは実装しない。
- 新規Packageを追加せず、既存機能で最小実装する。

---

## P1-09 仮Visualと4方向表示

- 正式採用版`momotaro_idle_4dir_4frame.png`（伸縮±1px）のIdleを上下左右へ接続する。
- `momotaro_idle_4dir_4frame_v2.png`（伸縮±3px）は不採用のため使用しない。
- 各方向4フレーム、4fps、Loop Time有効とする。
- `momotaro_move_4dir_6frame.png`のMoveを上下左右へ接続する。
- Moveは各方向6フレーム、初期値10fps、Loop Time有効とする。試遊時は8～12fpsの範囲を目安に調整可能とする。
- `momotaro_guard_4dir_4frame.png`のGuardを上下左右へ接続する。
- Guardは各方向4フレーム、初期値4fps、Loop Time有効とする。
- Idle／Moveの切替はGameplay状態または移動量からVisual Adapterが決定し、Animator StateをGameplay状態の正本にしない。
- Guard開始時に固定されたFacingに対応するGuard Clipを、Guard解除まで維持する。Guard移動中も表示方向を変えない。
- Guard中であることを仮表示で識別可能にする。
- Gameplay LogicがSprite名やAnimator State名へ直接依存しないVisual Adapter構成にする。
- 本番Spriteへ差し替える手順を報告する。
- 攻撃Animationと完成VFXは実装しない。

---

## P1-10 HP・スタミナRuntime基礎

- PlayerData等の最大値からRuntime Vitalsを生成する。
- Current HP/Staminaは0～最大値へClampする。
- 値変更通知を型付きで提供する。
- ScriptableObject原本を実行時に変更しない。
- 初期化、増減、Clamp、最大値変更規則、通知をテストする。
- 被ダメージ、自然回復、死亡、完成HUDは実装しない。

---

## P1-11 Phase 1検証Scene

- `SCN_VS_Field`をPhase 1の操作検証Sceneとして整える。
- 平地、長い壁、角、狭い通路を用意する。
- 通常移動、4方向、衝突、ガード移動、Camera、Vitalsを1分以内で確認できる配置にする。
- Scene直開き時のBootstrap自動生成を維持する。
- Player、Camera、Input参照のSmoke Testを追加する。
- 本編美術、敵、探索ギミックは追加しない。

---

## P1-12 統合受入

- 全Edit Mode／Play Modeテストを実行する。
- Bootstrap起動とVS_Field直開きの双方を確認する。
- KeyboardとGamepadで確認する。
- Dialogue/UI Mapへ切り替えたときPlayerが移動しないことを確認する。
- Missing Script/Reference、Stable ID重複、Compile Error、Console Errorを確認する。
- 新機能は追加せず、Phase 1の不具合修正と数値調整だけを行う。
- 最終報告に各受入項目のPass/Failを列挙する。

# Phase 1 統合受入チェックリスト（P1-12）

対象：`JP2943/Momotaro` / Unity 6000.3.20f1 / ブランチ `phase/1-player-foundation`
新機能は追加せず、不具合修正と数値調整のみ。各項目に Pass / Fail を記入してください。

## A. 自動テスト

| # | 確認項目 | 手順 | 結果 |
|---|---|---|---|
| A1 | EditMode 全緑 | Test Runner → EditMode → Run All（約104件） | ☐ |
| A2 | PlayMode 全緑 | Test Runner → PlayMode → Run All（約5件、VsFieldSmoke 含む） | ☐ |

## B. 起動経路

| # | 確認項目 | 手順 | 結果 |
|---|---|---|---|
| B1 | Bootstrap 起動 | `SCN_System_Bootstrap` を開いて Play → Launcher へ遷移（Console の `[Scene]` ログ） | ☐ |
| B2 | VS_Field 直開き | `SCN_VS_Field` を開いて Play → Bootstrap 自動生成、Exploration へ切替、操作可能 | ☐ |

## C. 操作（Keyboard / Gamepad 双方）

| # | 確認項目 | 手順 | 結果 |
|---|---|---|---|
| C1 | 通常移動 | WASD / 左スティックで XZ 平面移動 | ☐ |
| C2 | 斜め速度一定 | 斜め移動が単軸より速くならない | ☐ |
| C3 | 4方向表示 | 進行方向で Idle/Move の絵が上下左右に切替 | ☐ |
| C4 | 傾かない | 移動・衝突で Player が回転/傾斜しない | ☐ |
| C5 | 壁・角・狭所 | 貫通・振動なし、壁沿いスライド可 | ☐ |
| C6 | ガード移動 | K（KB）/ R1（Pad）保持で 40% 速度・向き固定・ガード絵、離すと解除 | ☐ |
| C7 | Camera 追従 | Orthographic が目立つ揺れなく追従 | ☐ |
| C8 | 微小入力 | スティック微小入力で向きが震えない | ☐ |

## D. モード遮断（Dialogue/UI で移動停止）

| # | 確認項目 | 手順 | 結果 |
|---|---|---|---|
| D1 | 非Gameplayで停止 | VS_Field の `SceneMode` の Mode を一時的に **Dialogue**（または UI）に変更 → Play → **Player が動かない/入力を受けない**ことを確認 → 確認後 **Exploration に戻す** | ☐ |

※ D1 は新機能を足さず、既存の `GameplaySceneMode` の設定変更だけで確認できます。確認後は必ず Exploration に戻してください。

## E. データ・参照・ビルド健全性

| # | 確認項目 | 手順 | 結果 |
|---|---|---|---|
| E1 | Compile Error 0 | Console にコンパイルエラーが無い | ☐ |
| E2 | Console Error 0 | B・C の操作中に赤エラーが出ない | ☐ |
| E3 | Missing なし | Player/Camera/Scene に Missing Script/Reference が無い | ☐ |
| E4 | Stable ID 検証 | メニュー `Momotaro/Validation/Validate Project Data` → Error 0（重複/不正なし） | ☐ |
| E5 | Vitals 健全 | （任意）PlayerVitalsHolder を載せた場合、最大値生成・Clamp が動作、SO 原本不変 | ☐ |

## F. Claude 側 静的監査（実施済み・参考）

- .cs / .meta 整合（欠損・orphan なし）：**OK**
- 波括弧・namespace：**OK**
- 層依存（Core←Data←Gameplay、Presentation/Infrastructure→Gameplay、Runtime非UnityEditor、Gameplay非InputSystem）：**OK**
- Scripts の `Find` 系不使用：**OK**
- asmdef 8 本整合：**OK**
- データ資産の Stable ID 重複：**なし**（`player_movement_default` 1件）
- Gameplay に攻撃/被弾/死亡等の先回り実装：**なし**（Phase 1 スコープ順守）

---

## 最終判定

- すべて Pass → Phase 1 受入完了。`phase/1-player-foundation` を `main` へ統合。
- Fail があれば、該当項目のみ不具合修正・数値調整で対応（新機能は追加しない）。

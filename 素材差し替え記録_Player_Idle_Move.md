# 素材差し替え記録：Player 仮スプライト Idle／Move（手描き修正版の取り込み）

- 日付：2026-09-25
- 依頼：`ArtSource/Prototype/Player/Momotaro/Idle`／`Move` に格納した手描き修正版を確認し、Unity へ取り込んでモーションへ反映する。
  コマ数が増えているので新しいコマ数に合わせて調整する。再生時間は伸びてもよく、適切と思われる配分で設定する（試遊で違和感があれば別途調整）。
- 位置づけ：Phase 5 の受入検査とは独立した素材差し替え作業。P5 の成果物・必須テストには手を入れていない。

## 1. 受領素材の確認

| 区分 | 枚数 | 寸法 | 内容 |
| --- | --- | --- | --- |
| Idle | 6 コマ × 4 方向 = 24 | 192×192 | 立ち呼吸。全 24 枚が相異なる（md5 で確認） |
| Move | 6 コマ × 4 方向 = 24 | 192×192 | 走行 1 周期。全 24 枚が相異なる（md5 で確認） |

コンタクトシート（6 列 × 4 行＝方向 down／left／right／up）を生成して目視確認した。Idle は微細な呼吸、Move は
接地・蹴り出し・空中の揃った走行サイクルで、いずれも 01→06 の順で連続する。

### 表示サイズが変わらないことの確認

新素材は 192×192 だが、**キャラクター本体の描画高は 117〜122px・下端余白は一律 4px**で、
旧 Sheet のセル（128×128・本体 118px・下端余白 4px）と同じ実寸で描かれている。
横中心も概ね canvas 中心（96px）に揃う。したがって PPU100・BottomCenter Pivot のまま取り込めば、
**画面上の大きさも足元位置も従来と変わらない**（192 側の余剰はすべて頭上の余白）。

## 2. 取り込み方針

従来 Idle／Move だけが 4dir Sheet（Multiple mode・128px セル）だった。新素材の 192px は
Attack／Step／Hurt／GuardBreak／Special が使う**個別 PNG 方式**の寸法に一致するため、
これらと同じ方式へ統一した。

- 配置：`Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites/Idle/`、`.../Move/`（各 24 枚）
- Import 設定：`Step/` と同一（Sprite／Single／PPU100／BottomCenter Pivot (0.5, 0)／Full Rect／
  Bilinear／Compression None／Alpha Is Transparency／Read Write 無効／Mip 無効／Wrap Clamp）
- `.meta` の guid：Unity 慣行どおりランダム生成。プロジェクト内の既存 1747 件と衝突しないことを確認済み

## 3. 再生時間の設定

| Clip | 変更前 | 変更後 | 根拠 |
| --- | --- | --- | --- |
| `AN_Player_Idle_{Down,Left,Right,Up}` | 4 コマ／Sample Rate 4／1.0 秒 | **6 コマ／Sample Rate 5／1.2 秒** | コマ増分（4→6）をそのまま尺へ回し、1 コマ 0.2 秒の等間隔。呼吸としてやや落ち着いた周期になる |
| `AN_Player_Move_{Down,Left,Right,Up}` | 6 コマ／Sample Rate 12／0.5 秒 | **6 コマ／Sample Rate 12／0.5 秒（据え置き）** | Move は元から 6 コマでコマ数が増えていない。移動速度と整合済みの周期を変えると足の滑りが出るため維持した |

いずれも Loop 有のまま。違和感があれば Sample Rate の変更だけで調整できる。

## 4. 併せて行った差し替え

- `PF_Player_Momotaro.prefab` の SpriteRenderer 既定スプライトが旧 Idle Sheet のサブスプライトを
  指していたため、新 `Idle/momotaro_idle_down_01.png` へ付け替えた。
- 旧 Sheet（`momotaro_idle_4dir_4frame.png` / `momotaro_move_4dir_6frame.png`）は
  **どこからも参照されなくなった**が、比較用に残してある。不要なら削除する（削除は要許可）。

## 5. 付随して直した不具合（素材とは別件）

全件 PlayMode 回帰で `CompanionAggroReproPlayTests` が 1 件落ち、
`InvalidOperationException: Cannot query value of control '/AggroReproKeyboard/j' before ... has been added to system!` を
`PlayerInputAdapter.IsStillPressed` から送出していた。

以前に同種の例外を `IsPressed` へガード（`device != null && device.added`）して直したが、
**ガードを通らない生の `isPressed` 読み出しが 3 箇所残っていた**（L53 診断文字列・L206 canceled の代替経路・
L243 Map 復帰時の判定）。3 箇所すべてを `IsPressed` 経由へ揃えた。

この例外は入力 callback の中で飛ぶため、以後の入力が丸ごと止まり無関係なテストが一斉に無反応になる。
ただし**発現は仮想デバイスの取り外し順に依存し非決定的**で、欠陥を戻した状態で全件を流し直しても
再現しなかった（sp18：104/104 Passed）。したがって本修正は「同型の例外源を塞ぐ防御的修正」であり、
欠陥注入による証明は取れていない。再発監視の対象として `P5_後続課題.md` 相当の扱いとする。

## 6. 欠陥注入による検証（新規テスト）

新規 `PlayerIdleMoveSpriteImportTests`（9 件）が実際に欠陥を捕まえることを 3 通りで確認した。

| # | 注入内容 | 落ちたテスト | 結果 |
| --- | --- | --- | --- |
| INJ-1 | `AN_Player_Idle_Down` のコマ 02／03 を入れ替え | `Idle_FourClips_SixFrames_LoopOn_5Fps_1_2Seconds` | 8/1 失敗（frame 1 の参照先不一致を指摘） |
| INJ-2 | `Move/momotaro_move_up_04.png.meta` の Pivot を Center へ | `MovePngs_SingleMode_BottomCenter_192_Ppu100` | 8/1 失敗（alignment 期待 7／実際 0） |
| INJ-3 | プレハブ既定スプライトを旧 Sheet へ戻す | `LegacySheets_NoLongerReferenced_ByClipsOrPrefab`／`PlayerPrefab_DefaultSprite_IsNewIdleDownFirstFrame` | 7/2 失敗 |

各注入は復元を別コマンドで行い、復元後に `diff` または `grep` で元に戻ったことを確認した。

## 7. 実行記録

| id | 内容 | 結果 | 所要 |
| --- | --- | --- | --- |
| sp01 | refresh | ok | — |
| sp03 | compile-status（force） | 警告 0 件で成功 | — |
| sp04 | EditMode `PlayerIdleMoveSpriteImportTests` | 成功 9 / 失敗 0 | 1.0 秒 |
| sp06 | 同上（INJ-1 注入中） | 成功 8 / 失敗 1（想定どおり） | 1.0 秒 |
| sp08 | 同上（INJ-2 注入中） | 成功 8 / 失敗 1（想定どおり） | 1.0 秒 |
| sp10 | 同上（INJ-3 注入中） | 成功 7 / 失敗 2（想定どおり） | 1.0 秒 |
| sp12 | EditMode 全件（入力修正前） | 成功 1679 / 失敗 0 | 78.3 秒 |
| sp13 | PlayMode 全件（入力修正前） | 成功 103 / 失敗 1 → §5 の不具合を検出 | 146.3 秒 |
| sp15 | PlayMode 全件（入力修正後） | 成功 104 / 失敗 0 | 144.8 秒 |
| sp17 | PlayMode `CompanionAggroReproPlayTests`（欠陥再注入） | 成功 3 / 失敗 0（単独では再現せず） | 21.1 秒 |
| sp18 | PlayMode 全件（欠陥再注入） | 成功 104 / 失敗 0（全件でも再現せず） | 145.8 秒 |
| sp19 | compile-status（force・最終形） | 警告 0 件で成功 | — |
| **sp20** | **EditMode 全件（最終形）** | **成功 1679 / 失敗 0 / 予定 1679** | **71.6 秒** |
| **sp21** | **PlayMode 全件（最終形）** | **成功 104 / 失敗 0 / 予定 104** | **146.2 秒** |
| sp22 | run-op `validate-project-data` | Data 検証はすべて通過 | — |
| sp24 | run-op `verify-required-tests`（P5・sp20,sp21） | 必須 54/54 すべて Passed。未説明の Skip なし | — |

EditMode は 1670 → 1679 件（新規 9 件ぶん）。P5 の必須 54 件は引き続き全通過で、素材差し替えによる後退はない。

## 8. 補足

- 本 VM には git-lfs が入っておらず、LFS 管理の PNG／MP3 が常に「変更あり」に見えていた。
  今回 git-lfs 3.5.1 を VM のユーザー領域へ置き、`filter.lfs.*` を**コマンド単位の `-c` で与えて**
  コミットした（`.git/config` は変更していない）。これで変更検出は 1313 件 → 67 件に正常化した。
- `Assets/_Project/Scenes/Tests/Phase5/` の 3 Scene が「変更あり」と出るが、fileID の振り直しだけで
  内容は HEAD と同一（fileID を正規化した比較で一致を確認）。本コミットには含めていない。
- `_to_delete/spritecheck/` に確認用コンタクトシート 2 枚を置いた。削除して差し支えない。

## 9. 変更ファイルの sha256（本記録の最終編集後に採取）

| ファイル | sha256 |
| --- | --- |
| `AN_Player_Idle_Down.anim` | 33696f43152b915d08696b19a178773e960592f3b2de25dc225624c3d182a7e5 |
| `AN_Player_Idle_Left.anim` | 7058c7f23b7639555a6b2ca9651c04bdaeebc30b473fa1475239a877b9b321ed |
| `AN_Player_Idle_Right.anim` | da6f0a078f96f092247cf60326b60fb6b3ce38ba0fbbeb4ef4ef294d70d70ae4 |
| `AN_Player_Idle_Up.anim` | d1fa01116113e0839e8dd3b7fb436e1a6094f5771955fe766e14913a89fdf709 |
| `AN_Player_Move_Down.anim` | 58ce3cc9435a8e923b4b8f49b016c28b31638d98a3e7685d6845e79fb706491f |
| `AN_Player_Move_Left.anim` | 41e90919f2b0d51667aab084eb924c07b579f79ad785a07af4bba486c38a07a9 |
| `AN_Player_Move_Right.anim` | 25f3260352db1bd62c33ebc09ef6cdfe0ecbc7c8868054bbde94103286c1ebac |
| `AN_Player_Move_Up.anim` | c97df2fc836204277da0b00b14abe33873f86e62c039974e2cdc47ba07d3e8fe |
| `PF_Player_Momotaro.prefab` | a835cb0ea86ee250e6dd7c943f9f5a41115768d8ee20bde79ec93211e060e8df |
| `PlayerInputAdapter.cs` | 520f71ed99a41435305a268721b64607da0a44f9b1363f1ab05e185f3e34e917 |
| `PlayerIdleMoveSpriteImportTests.cs` | e4a398cfdaf156adc69ad628e36e4cef3ae64e742382d5dde84992c31362283b |

| 新規 PNG 48 枚（Idle/ + Move/）の連結 sha256 | 952976f71b338ae450bda19f1811eefde1bf9d3adc2a097871b634ad13213e92 |
| 新規 .meta 48 枚の連結 sha256 | 5cc27c8f744e90770744513890c981c88b07c1eb1704a954dc42df6c51017534 |

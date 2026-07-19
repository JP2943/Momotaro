# Phase 1 仮スプライト受入仕様（Idle／Move／Guard）

## 1. 目的

桃太郎の仮Idle 16枚、仮Move 24枚、仮Guard 16枚を、画質劣化や並び間違いなくUnityへ取り込みます。Phase 1ではIdle／Move／Guardを実素材で表示し、後日の本番素材への差し替えを妨げる依存は作りません。

Idleは伸縮±1px版を正式採用します。不採用の`momotaro_idle_4dir_4frame_v2.png`およびその元フレームは使用しません。

## 2. ユーザーが行う配置

リポジトリのルートに次のフォルダを作り、個別PNGだけを配置してください。

- `ArtSource/Prototype/Player/Momotaro/Idle/`：Idle 16枚
- `ArtSource/Prototype/Player/Momotaro/Move/`：Move 24枚
- `ArtSource/Prototype/Player/Momotaro/Guard/`：Guard 16枚

ファイル名は次の形式を維持します。

- `momotaro_idle_<down|left|right|up>_<01-04>.png`
- `momotaro_move_<down|left|right|up>_<01-06>.png`
- `momotaro_guard_<down|left|right|up>_<01-04>.png`

`ArtSource`は`Assets`の外に置きます。Unityが元フレームを個別Importすることを防ぎつつ、Git LFSで原本を保持するためです。

## 3. Claudeへ依頼する作業

```text
Phase 1仮スプライトの素材受入だけを行ってください。

正本：Sprite_Handoff_Phase1.md
入力：
- ArtSource/Prototype/Player/Momotaro/Idle内のPNG 16枚
- ArtSource/Prototype/Player/Momotaro/Move内のPNG 24枚
- ArtSource/Prototype/Player/Momotaro/Guard内のPNG 16枚

実施内容：
1. 全56ファイルが128×128px、RGBA透過PNGで、命名と枚数が正しいか検証する。
2. 各画像をリサイズ・補間・再描画せず、IdleとGuardは4列×4行、Moveは6列×4行へ機械的に配置する。
3. 全シートとも上からDown、Left、Right、Up、左からフレーム番号順とする。
4. 完成シートを次へ保存する。
   Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites/momotaro_idle_4dir_4frame.png
   Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites/momotaro_move_4dir_6frame.png
   Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites/momotaro_guard_4dir_4frame.png
5. Unity Import設定とSprite分割を仕様どおり設定する。
6. Idle／Move／Guardそれぞれ4方向、計12個のAnimation Clipを作成する。
7. 素材の内容自体は変更しない。P1-09のVisual接続は別タスクとして扱う。

完了時に、検証結果、生成した各シートのサイズ、Sprite名一覧、Clip設定、変更ファイルを報告してください。
```

## 4. 共通Unity Import設定

| 項目 | 値 |
|---|---|
| Texture Type | Sprite (2D and UI) |
| Sprite Mode | Multiple |
| Pixels Per Unit | 100 |
| Filter Mode | Bilinear |
| Compression | None |
| Generate Mip Maps | Off |
| Mesh Type | Full Rect |
| Wrap Mode | Clamp |
| Alpha Is Transparency | On |
| Pivot | Bottom Center (0.5, 0.0) |

## 5. IdleシートとClip

| 項目 | 値 |
|---|---|
| シート | 512×512px、4列×4行、透過PNG |
| セル | 128×128px |
| 行（上から） | Down、Left、Right、Up |
| 列（左から） | 01、02、03、04 |
| Motion | 表示高100→99→100→101px相当（±1px） |
| Animation | 4fps、約1秒、Loop Time有効 |

UnityのSprite Rect座標は、x＝0、128、256、384、y＝Down 384／Left 256／Right 128／Up 0です。各Rectは128×128px、Sprite名は`momotaro_idle_<direction>_<01-04>`とします。

Clip名：

- `AN_Player_Idle_Down`
- `AN_Player_Idle_Left`
- `AN_Player_Idle_Right`
- `AN_Player_Idle_Up`

## 6. MoveシートとClip

添付素材は、4方向×6枚＝24枚、全ファイル128×128pxの透過PNGとして検査済みです。

| 項目 | 値 |
|---|---|
| シート | 768×512px、6列×4行、透過PNG |
| セル | 128×128px |
| 行（上から） | Down、Left、Right、Up |
| 列（左から） | 01、02、03、04、05、06 |
| Animation | 初期値10fps、約0.6秒、Loop Time有効 |
| 試遊調整 | 速度感に応じ8～12fpsの範囲を目安に変更可能 |

UnityのSprite Rect座標は、x＝0、128、256、384、512、640、y＝Down 384／Left 256／Right 128／Up 0です。各Rectは128×128px、Sprite名は`momotaro_move_<direction>_<01-06>`とします。

Clip名：

- `AN_Player_Move_Down`
- `AN_Player_Move_Left`
- `AN_Player_Move_Right`
- `AN_Player_Move_Up`

## 7. P1-09での接続規則

- Idle、Move、Guardは今回の実素材を4方向すべてに接続します。
- Idle／Move／Guardの判定はGameplay状態または移動量をVisual Adapterへ渡して決定します。Animator StateをGameplay状態の正本にしません。
- Guard開始時に固定したFacingに対応するGuard Clipを、Guard解除まで維持します。Guard移動入力で表示方向を変更しません。
- Gameplay LogicからSprite名、Clip名、解像度、4／6というフレーム数を直接参照しません。
- 本番素材への交換はVisual Adapter、Animator ControllerまたはAnimator Override側で完結できる構成にします。

## 8. GuardシートとClip

添付素材は、4方向×4枚＝16枚、全ファイル128×128pxの透過PNGとして検査済みです。

| 項目 | 値 |
|---|---|
| シート | 512×512px、4列×4行、透過PNG |
| セル | 128×128px |
| 行（上から） | Down、Left、Right、Up |
| 列（左から） | 01、02、03、04 |
| Animation | 初期値4fps、約1秒、Loop Time有効 |
| Motion | 刀を両手で斜めに構えた姿勢を維持する小さな待機動作 |

UnityのSprite Rect座標は、x＝0、128、256、384、y＝Down 384／Left 256／Right 128／Up 0です。各Rectは128×128px、Sprite名は`momotaro_guard_<direction>_<01-04>`とします。

Clip名：

- `AN_Player_Guard_Down`
- `AN_Player_Guard_Left`
- `AN_Player_Guard_Right`
- `AN_Player_Guard_Up`

## 9. 既知の許容事項

- IdleのRight方向のみ、鞘の重なり順に軽微な不整合があります。仮素材なので修正しません。
- Moveの走行動作に伴う意図した上下動や接地足の変化は許容します。
- Guardの構えを維持するための小さな呼吸・重心移動は許容します。
- Bottom Centerはセル共通の基準点です。方向切替やループ境界で不自然な全体位置の跳びがないことはUnity上で確認します。
- `momotaro_idle_4dir_4frame_v2.png`（±3px版）はUnityへImportしません。

## 10. 受入条件

- Idle 16枚、Move 24枚、Guard 16枚がすべて128×128pxのRGBA透過PNG。
- Idle入力ファイル名に`_v2`が含まれない。
- Idle／Guardシートが厳密に512×512px、Moveシートが厳密に768×512px。
- リサイズ、再描画、フレーム補間なし。
- 行・列・方向・Sprite名の対応が正しい。
- Idle 4 ClipとGuard 4 Clipが4fps、Move 4 Clipが初期10fpsでループする。
- 各方向のループ境界とIdle／Move／Guard切替で、不自然な位置跳びがない。
- Missing Asset、Import Warning、Console Errorがない。

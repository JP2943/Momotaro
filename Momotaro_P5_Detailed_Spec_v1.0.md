# 桃太郎プロジェクト P5 マップと探索基盤 詳細仕様書 v1.0

作成日：2026-09-21  
宛先：Claude（実装・Unity自動検証担当）  
対象：Unity 6000.3.20f1、URP Universal Renderer、XZ平面のPhysics 3D  
目的：正式スプライトを待たず、2つの仮エリアを移動し、犬丸の調査・仕掛け・遭遇戦・探索復帰・死亡再開までを成立させる。

本書はP5の実装仕様案と作業依頼を兼ねる。新たに選んだP5の限定仕様は本文で明示する。機能の終了条件まで定義しており、単なる検討項目一覧ではない。既存のP3.5／P4戦闘を再実装する依頼ではない。

## 0. 基準と着手条件

### 0.1 確認した現在地

- P4ブランチ：phase/4-companions-foundation
- 確認コミット：a520a1c6d79f769dd67989fc960409bc1132edc4（P4統合）
- 確認時点のmain：135f26d5ca6e0f91e146f362851277593a33c852（P4設計）。P4の実装先端とは異なる。
- 前回レビュー：Momotaro_P4_Review_a520a1c.md。R3-01〜05の解消を、この時点では確認できていない。
- GPTは今回、仕様書・関連コードを確認した。P4のUnityテストを再実行したものではない。

実装開始時に最新のブランチ・コミットを再確認する。本書の古いSHAへ作業ツリーを戻さない。

### 0.2 P4との工程の切り分け

P5の仕様整理、純粋データ契約、仮マップ制作は先行可能。P4に依存する統合工程へ入る前に、次を完了する。

| 前提 | 解消対象 |
|---|---|
| P4 R3-01 | 調査完了通知からの再入で旧依頼を再操作しない |
| P4 R3-02 | Body／Proxy双方へ実命中による同期中断が届く |
| P4 R3-03 | 代理探索中の自然復帰で本体の自動行動を再開しない |
| P4 R3-04 | 調査で使う移動速度を開始時に固定する |
| P4 R3-05 | 守護の直撃先行／転送先行の契約と統合テストを一致させる |

各修正が既に存在するなら、コード・対応テスト・実行結果を再照合し、同じ修正を重ねない。P4の技術的完了とユーザー受入は別に記録する。P5の着手をもってP4受入済みとはしない。

作業ブランチ名は phase/5-world-exploration。最新mainにP4修正が統合済みならそこから分岐する。未統合なら、修正済みP4先端を親とする依存ブランチとして開始し、親SHAと依存関係を記録する。P4未反映のmainから同名実装を作り直さない。mainへの統合は本書の実装作業に含めない。

### 0.3 仕様の優先順位

本書のP5限定決定 → P4の確定仕様・裁定 → 改訂ロードマップv2.2 → ゲーム仕様書v1.4。適用範囲外の項目では元の仕様を維持する。

特に仲間Downからの時間経過復帰は、元のゲーム仕様書と異なるがP4で確定しているため維持する。P5で旧仕様へ戻さない。

## 1. P5の到達点と対象外

### 1.1 一周できる体験

1. エリアAで主人公と加入済み犬丸が活動開始する。
2. 壁・水場・通路を歩き、Interactで犬丸に調査を依頼する。
3. レバーで通路の門を開く。
4. 開放出入口からエリアBへ移動する。
5. 遭遇範囲へ入り、骸骨剣士1体＋骸骨弓兵1体と戦う。
6. 撃破報酬を受け取り、その場で探索へ戻る。
7. 扉からAへ戻る。徳・調査済み・門の開通が残る。
8. Bへ再訪しても、死亡再開していなければ倒した遭遇敵は再出現しない。
9. 敗北時は既得の徳・探索進行を保ち、Aの試作再開地点で全回復して再開する。

### 1.2 対象範囲

- 2つのグレーボックスエリア、床・壁・水場・段差境界。
- AreaId／EntryId／FloorIdの最小契約、入口と死亡再開地点。
- 追従カメラと部屋境界。
- Sceneをまたぐruntime進行状態、HP・CD等の引渡し。
- Interactの一元選択、犬丸調査、扉、単純な一方向レバー。
- 1種類の通常遭遇戦、勝利後探索復帰、死亡再開。
- 犬丸の長距離追従経路、失敗検知、安全なワープ。
- Scene Builder、Validator、受入テスト一覧、操作README。

### 1.3 今回追加しないもの

ディスクSave／Load、Inventory、Item実付与、成長、徳の章別獲得上限、完成版お地蔵様メニュー、ファストトラベル、会話・EventSequence、章移動、猿・雉・仲間切替、新敵・ボス、ランダム遭遇、時間式Wave、防衛戦、複数階層の重なり、ジャンプ・泳ぎ・落下ダメージ、完成マップ美術、完成スプライト、汎用パズル編集環境。

元仕様にある地図UI・霧・屋根透過はP5の必須にせず、P9以降のマップ／表示タスクに残件登録する。P5の小マップは低い壁と開放天井で見えるように作る。

## 2. 今回固定する設計判断

| 項目 | P5の選択 |
|---|---|
| 地形 | 3Dプリミティブ＋単色Material。Physics 2D／TilemapCollider2Dへ移行しない |
| エリア | 1エリア＝1Scene、同時に活動するエリアは1つ |
| Sceneロード | 既存SceneFlowManagerを拡張するSingleロード。常駐進行サービスを使用 |
| 遭遇戦 | エリア内で開始・終了。別戦闘Sceneへの転送なし |
| 経路 | 導入済みAI Navigationを使う探索時の長距離追従。Motorは既存を維持 |
| カメラ | 既存TopDownCameraFollowを拡張。P5ではCinemachine移行を必須にしない |
| 階層 | プレイ用エリアはFloorId=0のみ。高台・段差は通行境界／エリア接続で表す |
| 本編進行 | Game SessionがPlayerProgressStateを1個所有。Scene上のHolderへ注入 |
| 試遊進行 | P3.5／P4試遊Sceneは従来のローカルState。Retryで初期化 |
| 通常エリア移動 | 徳・探索進行・HP・残りCD・Down等を保持。移動自体では回復しない |
| 本編型死亡再開 | 徳・調査・開通・加入は保持。全回復・CD解除・通常敵再出現 |
| 終了 | Play終了／アプリ終了／明示的な新規開始でruntime状態を破棄。保存機能なし |

Cinemachine、複数Floor、完成版地図等を永続的に廃止する決定ではない。既存基盤を使ってP5の終了条件に範囲を絞る。

## 3. 仮マップと配置仕様

### 3.1 Sceneと安定ID

新規出力は Assets/_Project/Scenes/Tests/Phase5/ 以下にまとめる。

| 用途 | Scene名 | AreaId |
|---|---|---|
| 統合起動 | SCN_Phase5_ExplorationTrial | なし。初期化後Aへ移動 |
| エリアA | SCN_Phase5_AreaA | area_p5_a |
| エリアB | SCN_Phase5_AreaB | area_p5_b |

EntryIdはarea_p5_a_start、area_p5_a_from_b、area_p5_b_from_aを使う。死亡再開点はAのarea_p5_a_start。P5では変更できない固定の試作再開点とし、お地蔵様の保存・休息機能を先行実装しない。

各Entryは到着位置・4方向の向き・同Floorの代替配置候補を持つ。Actorは目的地Entryの向きを採用し、復旧時だけ採取した元の向きへ戻す。

Scene名・表示名・Unity InstanceIDを永続的な識別子にしない。StableIdは既存の小文字snake_case・64文字以内の規約を継承する。AreaId、各地点のStableId、EncounterIdはP5カタログ内で一意。実行回数を示すRunId／TransitionIdはruntimeの世代番号であり、StableIdとは別。

### 3.2 配置の最低条件

| エリア | 必須配置 |
|---|---|
| A | 開始点・死亡再開点、戻り入口、正常な犬丸調査地点、壁越し調査拒否地点、レバー、開通する門、開放出入口、L字通路、水場境界、2つのカメラ領域 |
| B | Aからの入口、Aへ戻るInteract扉、正常調査地点、遭遇Trigger、アリーナ境界、敵2体の出現点、入口と重ならない安全な戦闘復帰点 |

Aは約24×18、Bは約28×22 Unity unitを配置開始時の目安とする。実寸はカメラ画角・既存の移動速度・Collider径に合わせてBuilder内で調整してよい。地形や戦闘の最終バランス値にはしない。

- 正常ルートの通路幅は最大通行Actorの直径＋0.4以上。犬丸のためだけに主人公が押し戻されない。
- 入口、Actor生成点、ワープ候補は壁・水・門・遭遇Triggerに重ねない。
- L字通路は直線追従では壁に当たり、NavMesh経路なら迂回できる配置にする。
- 「エリアA／B」「門」「レバー」「調査」「戦闘区域」を文字・形で識別できる。Gizmosだけを表示に使わない。
- P5用Placeholderは専用フォルダへ作る。既存Prototypeを一律に移動・変更しない。

### 3.3 通行とFloorId

平面移動はX／Z、高さはY。床面Y、見た目の接地、Collider位置は既存Prefabと整合させる。

- 床：歩行可能。
- 壁・閉じた門：主人公・敵・仲間・ステップ・押し出しを阻止。
- 水場・崖：P5では通行不可。透明の通行境界を配置し、落下や水泳は実装しない。
- 開いた門：通行Colliderを外し、経路情報も更新する。
- 見た目の段差：同一平面表現に留める。坂の物理移動や階層間の攻撃を追加しない。
- 攻撃判定・飛び道具の遮蔽は既存戦闘契約を維持する。水面の見た目だけで新しい命中ルールを作らない。

FloorIdは「論理的な同じ階層か」を示すintであり、Y座標やSceneのbuildIndexから推定しない。P5のプレイ用エリアは0だけを許可し、Actor／入口／調査／Interact／経路の整合をValidatorで検査する。FloorIdが異なる対象を自動補正して同じ階とみなさず、不整合として拒否・検出する。

P5では上下に重なるFloorを実装済みとは報告しない。非0の実エリア、多層認識・攻撃の統合は別タスク。既存ICombatActor.FloorIdの互換性を維持する。

## 4. 状態の所有者と生存期間

### 4.1 状態の分類

| 値 | 正本／保持先 | 通常A↔B | 本編型死亡再開 | P3.5／P4 Retry |
|---|---|---|---|---|
| 徳・GrantOnce済みRewardId | PlayerProgressState | 保持 | 保持 | 初期化 |
| 犬丸加入・出撃対象 | Sessionの仲間進行 | 保持 | 保持 | Scene初期構成へ |
| 調査済みPointId | AreaRuntimeStateの調査記録 | 保持 | 保持 | 初期化 |
| レバー・門開通 | AreaRuntimeStateの世界状態 | 保持 | 保持 | 該当なし／初期化 |
| 訪問済みAreaId | Session | 保持 | 保持 | 該当なし |
| 通常遭遇のクリア記録 | AreaRuntimeStateの再出現周期別記録 | 保持 | 全エリア分をリセット | Scene初期構成へ |
| Player HP・スタミナ | 活動中ActorのRuntime値 | 値を引渡す | 全回復 | 従来どおり |
| 仲間HP・Down・Away・復帰残り時間・CD | 活動中ActorのRuntime値 | 値を引渡す | 加入済み対象を全回復・CD解除 | 従来どおり |
| 被弾後無敵・ひるみ等の残り時間 | 活動中ActorのRuntime値 | 残り値を引渡す | 解除 | 従来どおり |
| 攻撃中／探索中／防御中の行動所有権 | Scene内Controller | 同期中断し破棄 | 破棄 | 破棄 |
| 攻撃判定・Projectile・ヘイト・攻撃スロット | Scene／Encounter | 破棄 | 破棄 | 破棄 |
| GameObject、Transform、Coroutine、購読 | Scene | 破棄して再配線 | 同左 | 同左 |

Game Sessionは純粋な状態を保持する。Scene由来のActorやPresenterをDontDestroyOnLoadして運ばない。

### 4.2 PlayerProgressStateの単一性

既存PlayerProgressStateを唯一の可変な徳・GrantOnce記録の正本として使う。World状態用クラスにVirtueを複製しない。

Bootstrap／Game Session層が本編型Session開始時に1個生成する。InfrastructureからGameplayへ注入し、GameplayからBootstrap具象を参照しない。

PlayerProgressHolderにはBind(PlayerProgressState)相当を追加する。

- 未注入時は従来どおりローカルStateを自前生成する。P3.5／P4の挙動を維持。
- P5では報酬購読・Actor活動開始より先に外部Stateを注入する。
- 同一参照の再Bindは冪等。
- 別参照へのBindは、初期化が未確定かつGrant／Reset等の使用前だけ許可する。初期化完了後は拒否して既存Stateを保持する。
- 初期化前のHUD読取りだけでBind不能にしない。表示開始時に現在値を読ませる。
- Dispose／OnDisableで外部StateをResetしない。新規Sessionの破棄・再作成はSession所有者だけが行う。
- ResetProgressを本編型の死亡・Scene変更から呼ばない。初期化済みの共有StateをScene側からリセットできないようにする。
- CombatRewardCollectorは常にHolderを使う。試遊／本編を判定する分岐をCollectorへ追加しない。

Bind後のHUDは現在値を一度同期表示する。VirtueChangedは既存の加算通知契約を維持し、Sceneをまたいで古いHUD購読が残らない。遷移中は報酬等のState変更を停止するので、旧・新Holderが同時に書き込まない。

### 4.3 探索と世界状態

SessionはAreaIdをキーにAreaRuntimeStateを持つ。各Areaは調査記録、開通済みの仕掛けID、通常Encounterの当該再出現周期でのクリア記録を持つ。

P4のInvestigationRecordHolderはローカル記録を既定とし、P5だけ外部のArea記録へBindする。具体型固定が注入を妨げる場合は、既存IInvestigationRecordに加えて狭い書込み契約を整理してよい。巨大な全世界StateをCoordinatorへ渡さない。

P5では調査が成功した瞬間にArea記録へ反映する。Scene離脱時だけまとめて保存する方式にしない。未完了の依頼は持ち越さず、成功済みで代理が帰還中なら成功を保持して表示だけ撤収する。

扉の開通とレバー使用済みを別々に可変管理して不一致を作らない。開通FlagIdを正本とし、両表示・Colliderをそこから復元する。P7の汎用シナリオフラグ基盤は作らない。

### 4.4 Actor値の引渡し

AreaTransferSnapshot（名称は変更可）は、遷移中だけ使う不変の値コピーとする。活動中のActor値と並行更新する第2の正本にはしない。

含める値：

- PlayerのHP・スタミナ、既存の回復待ち・被弾後無敵・必要な短時間状態の残り値。
- 犬丸のHP、Down／Away、復帰残り時間、ひるみ残り・蓄積、被弾後無敵残り、通常攻撃／守護／防御等で現在実装されているCD残り値。
- 引渡し対象の仲間ID。位置は目的地入口と安全な隊列位置へ置換。
- 遷移前の復旧用エリア、位置、向き。

Export／Importは各Runtimeの明示APIで実装する。Reflectionによるprivate値の書換えを本番経路に使わない。Data未実装の仲間スタミナ等を新設しない。

行動の中断によりCDが開始される場合は、**中断完了後の値**を採取する。ロード中は時計を停止し、残り時間から実ロード秒数を引かない。Import後にAwake／OnEnableの初期化で値が上書きされない構造にする。Snapshotの値範囲は検証し、不正値を黙って全回復に置換しない。

## 5. 起動とSceneの構成

### 5.1 構成と初期化順

既存BootstrapRoot、GameMode、Input、SceneFlowを拡張する。別の常駐GameManagerを並設しない。

各Area Sceneは次の構成を持つ。

- AreaRoot：AreaId、入口・カメラ領域・仕掛け・Encounter定義の明示参照。
- 初期化担当：起動とSession取得を行う薄いInfrastructure層。
- GameplayRoot：Player、犬丸、HUD、探索、Encounter。初期化前は活動しない。
- 地形、NavMesh、Camera、Canvas等。初期化順と表示の順を管理する。

起動順を明示する。

1. Bootstrapのサービス初期化完了を確認する。
2. Session種別を決定し、P5なら既存Sessionを再利用、なければ新規Sessionを生成。
3. Area定義と必須参照を検証する。
4. Progress／Area記録／Roster／活動Context等を注入する。
5. 入口位置・向き、Actor値、門、クリア済みEncounterを復元する。
6. 報酬・Feedback・HUD・入力の購読を接続する。
7. カメラを即時配置する。
8. AreaReadyを確定して活動・入力を許可する。

Script Execution Orderや「1フレーム待てば大丈夫」だけに依存しない。初期化前のActor更新・報酬購読を止める。InactiveなRootで組む場合も、参照Bind時の初期化とAwakeの両方が安全に共存するようにする。

### 5.2 起動経路

- 統合起動SceneからPlay：新規P5 Session、Aの開始点。
- AまたはBを直開きしてPlay：Bootstrap補助を使い、既存P5 Sessionがなければ新規作成し、そのAreaの既定入口。死亡再開点はA開始点。
- 既存P5 Sessionによる遷移：新規Sessionを作らない。
- P3.5／P4試遊Scene：本編Sessionを自動注入しない。残存Sessionがあっても試遊Holderはローカル運用。
- Play終了／明示New Game：Sessionを破棄。Domain Reload無効時も前のPlay状態を次回へ残さない。

Bootstrap重複を後発破棄しても、正本サービスの購読・Providerを解除しない。Providerの解除は所有者一致で行う。

## 6. エリア遷移と失敗処理

### 6.1 遷移の入口

- 開放出入口：範囲内で出口方向へ0.15秒連続入力したら要求する。接触・押し出し・Triggerに立っているだけでは遷移しない。
- 扉：候補表示中のInteract押下1回で要求する。
- 許可：AreaReady、Exploration、主人公生存、Playerの移動可能な平常状態。
- 拒否：戦闘開始予約中・戦闘中・勝敗処理中・Pause／Dialogue／Event／Loading／GameOver、主人公の攻撃・Guard・Step・Hurt・GuardBreak等。
- 犬丸がDown／Away／調査中でも主人公の遷移を妨げない。受理時に探索を同期撤収する。
- 目的地に到着したら入力エッジを破棄し、入口Triggerから一度退出するまで自動帰還を抑止する。別SceneのTriggerへ押しっぱなしを持ち込まない。

### 6.2 遷移手順

状態はIdle → Preparing → Loading → Binding → Ready → Idle。失敗は原因と復旧状態を別に持つ。

1. 目的地AreaId／Sceneパス／EntryId、登録状態を事前検証する。
2. 遷移排他を取得し、TransitionIdを確定する。外部通知より先に再入を拒否する。
3. 入力・新規探索・新規Encounterを閉じ、GameModeをLoadingへ変更する。
4. 調査・代理表示・進行中の仲間行動・押し出し・Hitbox・Projectile等を同期停止。敵対象、攻撃スロットを解放する。
5. 中断後のActor値と復旧元情報を不変Snapshotへ採取する。継続進行Stateは同一実体を保持する。
6. 暗転し、既存SceneFlowの単一ロード経路で目的地を非同期ロードする。P5は中間Loading Sceneを必須にせず、常駐の暗転／読込表示を使う。
7. 目的地で§5.1のBind・復元を完了する。sceneLoadedだけでReady扱いにしない。
8. 位置・カメラ・HUD・Contextを確認し、CurrentAreaを確定する。
9. 入力を初期化し、Explorationへ戻して暗転解除。遷移完了を一度通知する。

古いTransitionId／Scene世代のコールバックを無視する。旧SceneのOnDestroyが新しいContextやProviderを消さない。完了通知から次の遷移が要求されても、前のfinallyが新しい排他を解除しない。

同一Areaへの通常入口移動はP5では不要。死亡再開だけは現在がAでも明示Reloadできる。

### 6.3 読込失敗と復旧

| 失敗時点 | 処理 |
|---|---|
| 受理前：ID不正・Scene未登録 | その場に留まり、進行を変更せず理由表示 |
| 旧Sceneが利用可能な時点 | 初期化済み状態を確認して元の活動を再開。中断済み調査を自動再開しない |
| 旧Scene破棄後：入口不在・Bind失敗等 | 新Sceneを活動させず、保存した元Areaを1回だけ再ロードして復旧する |
| 復旧先も失敗 | Error表示に留め、既存Launcherへ戻る操作を提示。無限再試行しない |

Runtimeの入口位置が塞がっている場合は、指定された同Floorの安全な代替入口候補だけを使う。すべて不適切なら遷移失敗。Vector3.zero、壁内、別Floorへ無条件に配置しない。

ロード監視の初期値は30秒（unscaled）。非同期ロードが完了していない場合、Unityの処理をキャンセルできたと偽って排他を解除しない。タイムアウトは停止表示・診断とし、古い操作が終端するまで新たなロードを開始しない。復旧要求は古い操作の終端後に1回だけ実行する。正常系に固定の待ち時間を加えない。

暗転とError UIはunscaled時間で動かし、Gameplay時計・CDは進めない。Missing素材による無表示許容と、進行に必要な入口／State未配線の失敗を混同しない。

## 7. Interactと仕掛け

### 7.1 入力消費は1か所

P5ではAreaInteractionController等の単一選択窓口を設け、扉・レバー・犬丸調査を同じ押下から選ぶ。各対象が独自にInputSystemを読む方式は禁止。

P4のInvestigationInteractInputはP4 Sceneで維持してよい。P5 Sceneでは同時に動かさず、調査Adapterから既存Coordinatorへ渡す。Infrastructureが入力を1回消費し、Gameplayは入力デバイスに依存しない。

候補条件と順位：

1. 同じArea・Floor、対象が有効、水平距離がInteractionRadius以内。
2. 主人公から対象のInteractionAnchorまで遮蔽物がない。壁越しは除外。
3. 水平距離の小さいものを選択。同距離ならStableIdの辞書順で固定。
4. 表示中の候補と実行候補を一致させる。押下時に有効性を再検査する。

InteractionRadius初期値は1.6。調査地点固有の受付距離がさらに短い場合は小さい方を用い、候補表示と受付が同じ値を参照する。向きの厳密な円錐条件はP5で追加しない。距離・遮蔽の境界条件をテストする。

調査Adapterは選択されたPointIdを指定して依頼する。現在のCoordinator.TryRequest()が内部で最近傍を選び直す構造なら、指定地点を再検証する狭い入口を追加する。共通選択の後に別の地点へ依頼がすり替わる構造にしない。P4の引数なし入口は既存互換として残せる。

対象の内部条件（既に調査済み、未加入、ロック中等）で拒否されても、同じ押下を次点の対象へ流さない。対象がなければ押下を捨てる。長押し、フレームをまたぐ古い入力、ゲームパッドSouthとの二重処理でStepを発動しない。

探索中の再Interactは既存のBusy拒否。別の扉への要求は、主人公の条件を満たす場合に遷移として受理し、旧探索を同期撤収する。

### 7.2 犬丸の調査

P4の依頼・Snapshot・完了・中断・加入資格・Proxy・命中割込みをそのまま利用する。P5で自動調査へ戻さない。

Area記録を注入する変更以外に、調査結果からアイテム・徳・会話を発生させない。短文「調査済み」と発見ID通知まで。Scene往復で成功通知を再発行しない。

追従のNavMesh対応を理由に、調査の直線到達ルールを勝手に変更しない。壁越し地点はP4どおり拒否する。

### 7.3 レバーと門

1つのレバーが1つのFlagIdをfalse→trueにする。反転・時間制限・複数条件なし。開通済みなら状態変更と開通通知を再発火せず、「開通済み」と表示する。

内部Flagを先に確定し、その後に表示・Collider・経路更新と通知を行う。コールバックからの再入でも変更は1回。正常に更新できなかった場合はエラーを表面化し、見た目だけ開いて通行不能な状態を受入にしない。

Triggerによる戦闘アリーナ封鎖と、恒久開通の門は別所有者にする。戦闘終了時にレバーで開けた門を閉じ戻さない。P5では閉じる操作がないため、Actorを挟む扉の処理は追加しない。


## 8. Encounterと探索への復帰

### 8.1 P5で実装するEncounter

エリアBに通常Encounterを1つ配置する。EncounterIdはencounter_p5_b_road。構成は骸骨剣士1体＋骸骨弓兵1体、1グループ。全予定生成完了・生存敵0・主人公生存で勝利。開始時回復・Wave幕間・勝利時全回復は行わない。

既存EncounterDataは雛形であり、EnemyIds、SpawnPointId、Boss指定を持つ。これを最小限拡張・公開し、新しい同義の戦闘Dataを並設しない。

- EncounterDataのIDとEnemyIdsを正本にする。
- EnemyId→既存Enemy Prefabの解決表、SpawnPointId→Scene内出現点集合を明示配線する。
- 出現点はEnemyIds順に対応させる。P5では敵数以上の安全な出現点を要求し、同じ点への重複生成をしない。
- Arena、Trigger、戦闘復帰点はScene内のEncounterBindingに持つ。Transformを共有Dataや常駐Sessionへ保存しない。
- P5の勝利条件はAllEnemiesDefeated、敗北条件はPlayerDefeated、逃走不可。Boss指定や未対応条件は専用Validatorで拒否する。
- Encounter開始時に敵構成・参照・使用値をSnapshot化する。実行中のData編集で予定生成数を変えない。

既存WaveRunnerはP3.5／P4の4 Wave用として維持する。P5は小さなEncounterRunnerがCombatSessionControllerへの敵登録・勝敗遷移を利用する。4 Waveを無理に1 Waveに書き換えて共用したり、敵AI・命中処理を複製したりしない。

### 8.2 開始の排他と同期順

Area内のEncounter状態はDormant、Starting、Playing、Resolving、Cleared、Defeated、Failedとする。これは探索との外側の引渡しであり、CombatSessionControllerの戦闘内状態を置き換えるものではない。

1. Player本人のTrigger進入を検知する。仲間・敵・Projectileの進入は無視する。
2. AreaReady、Exploration、Player生存、遷移なし、未クリアを確認する。
3. StartingとRunIdを先に確定して、再入・別遷移・追加Interactを閉じる。
4. 調査と代理表示を同期撤収し、探索の行動・移動所有権を解放する。
5. アリーナ境界を有効化する。犬丸が境界外なら安全な内部候補へ再配置し、HP・Down・CD等を保持する。
6. GameModeをCombatへ変更し、Contextへ「開始待ちの戦闘」を供給する。
7. 敵を非活動状態で全数生成・登録し、報酬とFeedbackの購読を接続する。
8. 全数成功を確認してCombatSessionController.StartWaveを呼び、敵のAI・攻撃を許可する。

開始前にPlayerが封鎖Colliderへ重ならず内部にいることを確認し、戦闘開始Triggerは境界より十分内側へ置く。犬丸も含め安全な内部配置ができなければ封鎖・生成を開始せず失敗として返す。

初期生存数0だけで勝利にしない。必須Prefab・出現点欠落、部分生成失敗はFailedとし、生成済み敵を破棄、登録・境界・モードを元に戻す。報酬・クリア記録は付けない。失敗後はTrigger退出→再進入で再試行できる。

RuntimeにSpawn失敗の可能性がある以上、Spawn中の敵が先に活動・死亡しないことを保証する。

### 8.3 調停の優先順位

同じフレームの開始候補はArea側の1つの調停窓口で処理し、MonoBehaviourのUpdate順で結果を変えない。

| 競合 | 採用する処理 |
|---|---|
| 主人公死亡と勝利候補 | 死亡。成功記録と勝利復帰は行わない |
| 戦闘開始候補と通常エリア移動／Interact | 戦闘開始を先に確定。移動／Interactを拒否 |
| 受理済みLoading中の古いTrigger | 遷移側を維持。旧世代の通知を捨てる |
| 調査未確定と戦闘開始 | 調査を中断して戦闘へ |
| 調査成功確定後と戦闘開始 | 成功は保持して表示を撤収 |
| Pauseと新規操作 | 新規操作を受理しない。解除後に新しい入力が必要 |

新規Actor行動の停止は、少なくとも敵生成・命中解決・次の物理移動より前に反映する。排他の解除は取得したRunId／TransitionIdと所有者の一致を確認する。

勝利候補は最後の敵の通知で即座に外部へ確定せず、同じシミュレーション刻みの死亡通知を集約した後にPlayer生存を再確認する。処理順だけで相打ちを勝利にしない。

### 8.4 勝利と復帰

1. 登録敵の撃破をCombatSessionControllerが一度だけ受理する。
2. EnemyDefeated→既存CombatRewardCollector→PlayerProgressHolderの順で敵撃破報酬を付与する。
3. 全生成完了と生存数0から勝利候補を出し、§8.3の死亡優先を満たしたらVictoryへ。
4. Areaの当該再出現周期にEncounterクリアを記録する。
5. AI・残留攻撃・Projectile・攻撃スロット・ヘイト・一時境界を解放する。
6. Contextを「活動中Encounterなし」へ戻し、Explorationへ復帰する。
7. 主人公は原則現在位置に留める。地形上無効な場合だけ指定の安全な復帰点を使う。犬丸は状態を保持して追従を再開。
8. 短文「戦闘終了」を表示する。結果パネルやEnter待ちで止めない。

既存SessionのVictoryを活動Contextへ残して、犬丸が永久停止する実装にしない。P5用の明示的なEncounter活動供給を追加し、開始前・解放後は活動中Sessionなし、Starting〜Resolvingは戦闘として解釈する。未配線を「戦闘なし」とみなすFallbackは作らない。

一度クリアしたEncounterのTriggerは通常往復では開始しない。敵の死亡フェードを残す場合も、Gameplay上の攻撃・対象・購読を先に解放し、次Sceneへ死体を持ち越さない。

### 8.5 報酬と死亡の扱い

現在のMelee=10、Ranged=12を使い、初回完勝では徳が22増える。敵2体が同一RewardDataを共有するケースでも、GrantOnce=falseなら敵ごとに付与される。GrantOnce=trueは引き続きRewardId単位。

- Encounterクリア報酬はP5では追加しない。敵撃破報酬と二重加算しない。
- 撃破した瞬間の徳は取得済みとし、その戦闘で後から死亡しても取り消さない。
- 未撃破の敵、開始失敗、単なるScene再表示からは報酬を発行しない。
- 死亡再開で通常敵が新しく再出現し、再撃破した場合は既存GrantOnce=falseのルールで再度付与する。

最後の項目はP5の試作経済仕様であり、完成版の無制限取得を承認するものではない。章別戦闘徳上限は成長・経済を扱うP6の残件とし、P6のタスクへ明記する。役割共通RewardをGrantOnce=trueにして、同じ種類の敵すべての報酬を一度限りに変える回避策は禁止。

## 9. 死亡再開と終了

### 9.1 本編型死亡再開

P5はP3.5／P4の「Sceneをまるごと初期化するRetry」を流用しない。表示ラベルは「再開する」とし、既存試遊版との違いをREADMEに説明する。

PlayerのDefeatedをArea単位で購読する。CombatSessionがPreparing／未開始でも死亡を処理できるようにし、戦闘中のSession.StateChangedだけへ依存しない。

1. 死亡を一度受理し、探索・戦闘・移動を停止、GameOverにする。
2. 進行Stateを保持する。未完了探索、現在の敵・一時オブジェクトを終了する。
3. 再開操作を1回受理する。UIのSubmitを使用し、キー／ゲームパッドの二重実行を防ぐ。
4. Loadingへ入り、Aのarea_p5_a_startをロードする。
5. 通常Encounterの再出現周期を進め、全Areaの通常戦クリア記録を初期化する。
6. Playerと加入済み犬丸を全回復・CD解除・短時間状態解除して再配置する。
7. 徳、GrantOnce記録、調査済み、門開通、訪問済み、加入を復元して探索を再開する。

再出現周期の更新は再開要求IDにつき一度とし、読込失敗やボタン連打で何度も更新しない。失敗時は再開画面から再試行可能にする。

P5には通常Encounterしか配置しない。将来のボス撃破・一度限りイベント完了をこのリセットに混ぜない。通常敵の再出現記録と恒久進行のコンテナを区別する。

### 9.2 新規開始とアプリ終了

明示的なNew Gameだけが新規Sessionを生成し、徳・世界状態・訪問・加入設定を初期値へ戻す。P5試遊の初期加入は犬丸1体。Sessionを捨てる前に進行中のロード・Actor・購読を閉じる。

P5のデータはPlay終了／アプリ終了で失われる。SaveData、PlayerPrefs、ScriptableObject書換えによる隠れた保存を追加しない。P6ではPlayerProgressState等から保存専用DTOへコピーする予定であり、今はそのDTOを可変正本として先に作らない。

## 10. 犬丸の経路とワープ

### 10.1 経路の責務

導入済みcom.unity.ai.navigation 2.0.13を使う。P5のためにUnityやPackageのバージョンを変更しない。

NavMeshは長距離Followの次の移動目標を供給するだけ。位置・速度・向きの実書込みはCompanionMovementArbiter→CompanionMotorを維持する。NavMeshAgentとRigidbodyを両方の書き手にしない。

- 直線で通れる近距離は既存追従を使える。
- 壁に遮られた追従は経路のCornerへ移動要求を出す。
- 再探索の初期最短間隔0.5秒、目標変化1 unit以上、門の開通、経路無効化時に更新。
- 経路候補の状態はComplete／Partial／Invalid／Pendingを区別する。Partialを成功扱いにしない。
- 新たな停止・停滞判定を既存FollowModelと二重に競合させない。単一の失敗判定の入力へ統合する。
- 受入用の初期値は停滞3秒、再探索最大2回。その後に既存ワープ資格を評価する。既存距離超過閾値は維持する。
- Pause／Loading中の時間を停滞時間へ加算しない。

長距離追従以外のChase・近接攻撃・防御・調査移動をNavMeshAgentへ全面移譲しない。P4の探索占有中は経路要求もWarpも出さない。

### 10.2 ワープ先の安全性

主人公近傍の隊列候補を固定順で検査し、同Area・同Floor、床／NavMesh上、Colliderが壁・水・閉門に重ならない候補を使う。

- 経路計算が失敗したという理由だけで、閉じた門の未開通側へ先回りさせない。
- ワープ候補は主人公と同じ通行可能側に置く。主人公への短い接続経路も検査する。
- 攻撃Active・防御行動・被弾・Down・Away・探索占有中の通常Follow Warpを禁止する。
- 安全候補がない場合は停止し、間隔を空けて再評価する。壁内へ強制ワープしない。
- Scene遷移時の再配置は通常Follow Warpとは別の初期化処理。Down／Away等の状態を保持したまま安全な入口へ配置する。

NavMeshはBuilderでベイク・保存し、プレイ開始時の毎回ベイクを前提にしない。門の通行状態変更をNavigationへ反映し、更新後の通行可否を実経路テストで確認する。

## 11. カメラと表示

既存TopDownCameraFollowを拡張し、固定俯角・Orthographic・4方向表示を維持する。P5ではLook Ahead、ズーム演出、視覚的最終調整を追加しない。

CameraRegionはXZの軸平行矩形、RegionId、優先度を持つ。主人公の所属領域が1つならそれを使用する。重複時はPriority降順→RegionId辞書順。どの領域にも入らない場合はArea既定領域を使う。

- 画面四隅の視線と床面の交点から表示範囲を考慮して、Cameraの基準位置を制限する。斜めカメラで単純にZ幅=orthographicSizeとしない。
- 部屋が表示範囲より小さい軸は中央固定。無理なmin/max clampや自動ズームをしない。外側は仮背景で埋める。
- 部屋切替時の位置補間は0.15秒を初期値とし、途中のフレームも有効範囲内へ制限する。Scene到着・死亡再開では補間せず即時配置。
- 基準追従とCameraShakeの書込み先を分ける。例：Rig親に追従・境界、Camera子に揺れ。既存ShakePresenterを使い、独自HitStopは追加しない。
- SceneのCamera・AudioListener・入力・HUDは活動中各1つ。旧Sceneから残さない。
- 16:9を基準にしつつ、4:3・21:9の計算もテストする。最終カメラサイズはP10bで決める。

必須UIは現在Area、徳、HP／スタミナ、犬丸状態、Interact候補、短い成功／拒否文、Loading、再開操作。勝利は短文のみ。新しい完成UI素材は要求しない。

## 12. 層と変更箇所

以下の新規名称は設計上の責務名。既存の適切な型へ統合してよいが、責務とテストの対応を保つ。

| 層 | 責務 |
|---|---|
| Core | 既存StableId、狭い値・契約。Scene APIを入れない |
| Data | Area定義、入口参照定義、Interaction設定、EncounterDataの最小拡張 |
| Gameplay | Session／Areaの純粋State、遷移要求と結果の契約、Interact選択、Encounter調停、Actor値Export／Import |
| Infrastructure | Bootstrap注入、SceneFlow、入力仲介、UnityロードAdapter、NavMeshへのAdapter |
| Presentation | Camera境界・追従、暗転、HUD、候補・拒否・結果表示 |
| Editor | 専用Builder、Data／Scene／Asset検証、Bridge公開操作 |
| Tests | 純粋ロジック、ライフサイクル、実Scene・実入力・実経路・回帰 |

GameplayからInfrastructureを参照しない。Scene遷移要求の狭いインターフェースはGameplayまたは既存の適切な内側の層に置き、Infrastructureが実装する。

### 12.1 既存コードの具体的な拡張点

| 現在の型 | P5で行うこと |
|---|---|
| BootstrapRoot／ServiceRegistry | Game Sessionの所有と初期化を追加。既存のInput／GameMode正本を維持 |
| SceneFlowManager／TransitionGuard | 完了・失敗・世代付き排他、AreaReady待ち、復旧。既存Launcher経路を維持 |
| PlayerProgressHolder | 使用前の外部Bind、ローカルFallback、共有State保護 |
| PlayerProgressState | 徳の唯一の正本。GrantOnceの意味を変更しない |
| CombatRewardCollector | Holder経由の既存付与を維持。新Sceneで購読開始前にBind |
| InvestigationRecordHolder／Coordinator | Area記録注入と既存の一度確定契約 |
| InvestigationInteractInput | P4専用の利用を維持し、P5の共通消費窓口との二重配線を禁止 |
| CompanionActivityContext | P5のEncounter開始予約〜終了の正本に接続。配線不明なら停止 |
| CombatSessionController | 既存登録・撃破・勝敗契約を利用。P5のScene再開を試遊Retryへ結び付けない |
| GameplaySceneMode | P5ではLoading中にExplorationへ戻す自動適用をしない。Area初期化担当がReady後にモードを決める |
| TopDownCameraFollow／CameraShakePresenter | 追従基準と揺れを分離し、部屋境界を追加 |
| ActorのRuntime・Motor | 値のExport／Importと停止を追加。通常移動の単一書き手を維持 |

### 12.2 DataとRuntimeの規則

開始・受付で使う値をSnapshot化する。Data原本へ実行時の開通・クリア・HPを書かない。DictionaryやListを外部へそのまま可変参照で公開せず、更新APIを通す。

通知の基本順は「内部状態を確定→外部通知」。通知から戻った後は、元の依頼・実行世代がまだ有効か確認する。Disable、Scene離脱、自然復帰、再開、通知からの再入を終了経路として扱う。

P4の守護到達順別結果、無敵・防御の優先度、攻撃Snapshot、HitStopの単一調停を変更しない。


## 13. BuilderとValidator

### 13.1 出力と再生成

生成メニューは Momotaro → Phase 5 → Generate Exploration Trial、検査は Validate Exploration Trial とする。名称変更時はREADMEとBridgeの対応も更新する。

P5専用の生成先を使う。

- Assets/_Project/Scenes/Tests/Phase5/
- Assets/_Project/Data/Tests/Phase5/
- Assets/_Project/Prefabs/Tests/Phase5/
- Assets/_Project/Art/Environment/Placeholder/Phase5/

既存Player・犬丸・敵PrefabとRewardは参照して使う。必要なScene上書きはP5のSceneインスタンス／専用Wrapperに限定し、再生成で共有Prefabや本番AttackDataを上書きしない。

Builderは3 Scene、カタログ、地形、NavMesh、各入口・出現点、Context、Progress／Record注入先、入力、HUD、Feedback、カメラ、境界、門、復帰UIまで再構築できること。初回だけ手で接続する工程を残さない。

未保存Sceneの変更があれば、自動生成を実行せず理由付きで終了する。実処理はダイアログなしのpublic static等へ分け、メニューは薄いラッパーにする。生成対象の追加・削除は出力一覧を基に行い、親フォルダを再帰削除しない。

Build Settingsへの3 Scene登録は既存項目を維持して追記・更新する。既存Bootstrapや試遊Sceneの順番を不用意に変えない。

### 13.2 検査を分ける

| Validator | 入力と検査 |
|---|---|
| Data／契約 | ID書式・重複、Scene／入口の参照、敵ID、未対応Floor／条件、数値範囲 |
| Scene | 渡されたSceneと明示参照を検査。単一性、必須配線、初期敵0、非活動の初期化、Missing Script、入口・Triggerの重なり |
| Asset／Build | Editor専用。Scene登録、Prefab／Data参照、NavMeshデータ、出力パス、必要素材、旧Asset汚染 |

Scene ValidatorにAssetDatabase依存を混ぜない。P3.5の既存Validatorの設計を変更しない。P5のAsset／Build検査は別クラスとして実装する。

必須の仮素材・UI・方向表示は割当必須。Runtimeの未割当フォールバックがあることを理由に統合Validatorを緩めない。すべての問題を「Warningなので合格」としない。

### 13.3 必須の静的検査

- カタログからA／BのSceneと全入口を解決できる。A↔Bと死亡再開の経路が存在する。
- AreaId、PointId、FlagId、EncounterId等の正本と参照が一致する。FlagIdの参照共有は重複定義とは区別する。
- P5で非0のFloor、未対応Boss、空の敵構成を通さない。
- P5 AreaにGameplaySceneModeの自動Exploration適用、TrialStageController、P4入力仲介、旧試遊Retryの誤配線がない。
- 入力のInteract消費者が1つ、進行の書込先が1つ、活動Contextが1つ。
- 入口・復帰点・Actor生成点にColliderの重なりがなく、到着が即Encounter／即再遷移を発生させない。
- Arena境界の有効化後も内部の安全配置が可能。
- L字通路・開通門のNavMeshが存在し、到達可否が配置と一致する。
- P5の全報酬が意図した既存Rewardを参照し、初回Encounter報酬合計が22。

検査のうち動的なNavMesh更新・Scene往復・物理挙動は、静的Validatorだけで保証したと扱わずPlayModeで補う。

## 14. Claudeの実装工程

| 工程 | 内容 | 先行条件と工程ゲート |
|---|---|---|
| P5-00 | P4残件照合、正本・ブランチ・変更対象・テスト一覧の登録 | R3修正は別コミットで追跡。未解消を隠さない |
| P5-01 | Session／Area State、Holder／Record注入、ローカル互換 | E01〜E05。徳の二重保持なし |
| P5-02 | 仮地形、Area／Entryカタログ、最小Builder | E25、V01〜V02の該当部分。A／Bを単独検査可能 |
| P5-03 | SceneFlow拡張、Actor値引渡し、Ready・復旧 | P4 R3解消後。E06〜E10、P01〜P04、P12〜P13 |
| P5-04 | Interact単一選択、調査Adapter、扉・レバー | E11〜E14、P05の探索部分、P10 |
| P5-05 | NavMesh Follow、門更新、安全Warp | E22〜E23、P06〜P07、P11 |
| P5-06 | カメラ境界・Rig／Shake分離・仮UI | E24、P17 |
| P5-07 | Encounter開始・登録・勝敗・報酬・探索復帰 | E15〜E19、P05・P08 |
| P5-08 | 本編型死亡再開、New Game、試遊との分離 | E20〜E21、P09・P15 |
| P5-09 | Builder／Validator／Bridge公開操作を統合 | V01〜V04、再生成で全配線復元 |
| P5-10 | 全件回帰、連続往復、README、受入記録 | E26〜E27、P14・P16と全必須、最後に人間試遊 |

表のE／P／V番号は§15のP5番号。複数工程にまたがる通しテストは必要な実装が揃った時点で実行し、それまでは未実施と記録する。未実施をSkip成功に置き換えない。工程順は依存を満たせば一部前後してよい。各工程で関連するコンパイル・絞込みテストを通して連続実装し、人間のメニュー操作待ちを工程ごとに挟まない。

工程ごとに変更・検証結果を記録する。タスク規模が大きくなった場合は内部コミットを分け、未実装部分をPassedにしない。P4の受入済み範囲を全面再設計することを工程の前提にしない。

## 15. 必須受入テスト

### 15.1 一覧の運用

次表のテスト名は**新規に実装する予定名**であり、現時点で存在・成功するという意味ではない。

- EditModeの完全名は Momotaro.Tests.EditMode.P5ContractTests.{表のメソッド名}。
- PlayModeの完全名は Momotaro.Tests.PlayMode.P5ExplorationPlayTests.{表のメソッド名}。
- Validatorの完全名は Momotaro.Tests.EditMode.P5ValidatorTests.{表のメソッド名}。
- 要求IDはP5-E01、P5-P01、P5-V01の形式で機械一覧に記載する。
- P5RequiredTests.jsonへ、工程、mode、要求ID、完全名を1件ずつ登録する。クラス名の正規表現だけで照合しない。
- パラメータ化して実行結果の完全名が変わる場合は、展開後の葉テストを列挙する。実装上の改名は本表・JSONを同時更新する。
- 結果から一覧を自動生成して、消えたテストを見逃す運用は禁止。
- 同じテストが複数要求を満たしてよいが、対象分岐の具体的なAssertが必要。

### 15.2 EditMode

| ID | メソッド名 | 必須の判定 |
|---|---|---|
| E01 | BoundProgress_PreservesIdentityVirtueAndGrantOnce | 外部Stateの参照同一性、徳、GrantOnce記録を維持 |
| E02 | ProgressBind_IsIdempotentAndRejectsLateReplacement | 同一参照は冪等、使用後の別参照と共有Resetを拒否 |
| E03 | LocalProgressFallback_RemainsIsolated | 未注入Holderは互いに独立。共有Sessionへ誤付与しない |
| E04 | AreaRecords_SurviveRebindWithoutRepeatingCompletion | 調査・開通・訪問の復元で成功イベントを再発行しない |
| E05 | WorldRespawn_ResetsOnlyNormalEncounterRecords | 全Areaの通常戦記録だけ初期化。徳・GrantOnce・調査・門・加入は不変 |
| E06 | TransferSnapshot_CapturesAfterCancellationAndRestoresVitals | 中断で開始したCD、HP、Down、短時間状態を値で復元 |
| E07 | LoadingGate_FreezesClocksAndStopsDirectTicks | 直接Tickでも時計・Move・Warp・攻撃を進めない |
| E08 | AreaTransition_RejectsInvalidModesAndActions | 戦闘・Pause等、Playerの攻撃／防御／被弾中を表どおり拒否 |
| E09 | Transition_ReentryAndStaleCompletionCannotReleaseNewRun | 二重要求・古い完了・通知再入・旧finallyから新世代を守る |
| E10 | TransitionFailure_HasBoundedRecoveryWithoutDuplicateLoads | 事前不備・ロード後不備・タイムアウトを区別。未完了中の重複ロードなし |
| E11 | Interaction_SelectsNearestThenStableId | 対象と表示が一致、同距離も決定的 |
| E12 | Interaction_RejectsOccludedWrongAreaOrFloor | 壁、距離境界、異なるArea／Floorを拒否 |
| E13 | Interaction_ConsumesOneEdgeWithoutFallbackToSecondTarget | 長押し・拒否・対象なしで次候補や次フレームへ入力を流さない |
| E14 | Lever_CommitsOnceAndRestoresDoorFromSameFlag | 再入・再表示でも開通1回、門とレバーの正本が同じ |
| E15 | Encounter_StartGatePrecedesInvestigationCallbacks | 探索中断通知から再要求してもStartingを上書きしない |
| E16 | Encounter_SpawnFailureDoesNotWinOrGrantRewards | 0体・部分生成・必須欠落で勝利しない。生成途中に敵が活動しない |
| E17 | Encounter_VictoryRequiresAllSpawnedAndPlayerAlive | 予定生成完了・生存0・主人公生存の全条件 |
| E18 | Encounter_SameTickPlayerDeathWinsOverClear | 通知順の両方で死亡優先、勝利記録なし |
| E19 | EnemyRewards_PrecedeVictoryAndRespectExistingGrantOnce | 敵ごとの重複排除、10＋12、RewardId単位の一度付与を維持 |
| E20 | CampaignRespawn_PreservesEarnedProgressAndResetsVitals | 死亡後の徳保持、HP／CD初期化、加入済み犬丸復帰 |
| E21 | RespawnRequest_IsOnceEvenWhenLoadingFails | 連打・失敗再試行で再出現周期・通知を重複更新しない |
| E22 | FollowPath_UsesOneMovementWriterAndBoundedRetries | Complete／Partial／Invalid、停止・再試行・所有権を検査 |
| E23 | SafeWarp_RejectsClosedSideBlockedAndWrongFloor | 危険候補を拒否。候補なしで壁内Warpせず停止 |
| E24 | CameraBounds_HandlePitchAspectAndSmallRooms | 斜め投影、16:9／4:3／21:9、小部屋、領域重複を検査 |
| E25 | AreaCatalog_ResolvesEntriesAndRejectsUnsupportedFloor | 安定ID・入口・既定／復旧点の解決、Floor=0制限 |
| E26 | NewGame_ClearsSessionButAreaLoadDoesNot | Sessionの破棄権限と通常移動の違いを固定 |
| E27 | Lifecycle_DisposeOnlyUnregistersOwnedProviders | 後発破棄・旧Scene解除で新しい正本を消さない |

### 15.3 PlayMode

以下は実Scene／Prefab／Input／Colliderを通す。純粋関数テストをPlayModeに置いただけで代替しない。

| ID | メソッド名 | 必須の判定 |
|---|---|---|
| P01 | BootstrapAndDirectOpen_CreateOneReadySession | 統合起動・A直開き・B直開きで唯一の正本とReady後の活動 |
| P02 | FirstRewardAfterLoad_ReachesInjectedStateAndHud | 到着直後の最初の報酬が注入先へ1回入り、HUDへ反映 |
| P03 | AreaRoundTrip_PreservesProgressVitalsAndDownTimers | A→B→Aで同一Progressと開通・調査、負傷HP、Down残り・CDを保持 |
| P04 | HeldExitInput_DoesNotBounceBackOrStartEncounter | 方向押しっぱなし・入口滞在で即帰還／即戦闘しない |
| P05 | FullRoute_InvestigateOpenTravelFightAndReturn | 実入力で調査→門→移動→実敵撃破→探索復帰、徳22、再訪敵0 |
| P06 | MovementStepAndHitback_RespectWallsAndWaterBoundary | 実Colliderで通常移動・Step・押し出しを阻止、通れる床は通る |
| P07 | PauseAndLoading_StopActorsAndResumeWithoutResidue | 実行中の経路・探索・戦闘を停止／復帰。Loadingへ旧速度・Hitboxを残さない |
| P08 | SpawnAndCombatCleanup_LeaveNoProjectileOrBoundary | 敵生成失敗と勝利後に残留物なし。探索へ戻り犬丸が再活動 |
| P09 | DeathAfterOneKill_PreservesVirtueAndReopensNormalEncounter | 近接撃破で10取得→死亡再開で10維持→再戦全滅で32。門・調査は保持 |
| P10 | KeyboardAndGamepadInteract_ActOnceWithoutStep | EとゲームパッドSouthの実Action経路で各1回。長押し・複数対象でも二重なし |
| P11 | NavMeshFollow_DetoursAndUpdatesAfterDoorOpens | 実L字通路を迂回。閉門を抜けず、開通後の経路更新で追従 |
| P12 | InvalidDestination_RecoversWithoutLosingSession | 存在しない入口等をテスト専用設定で注入。旧Area復旧とState保持 |
| P13 | CompletionCallback_CanRequestTravelWithoutOldRunCorruption | Body／Proxy完了通知中の遷移、古い完了通知から新依頼を守る |
| P14 | RepeatedTravelAndRespawn_LeaveOneOwnerAndNoOldColliders | A↔Bを3往復＋死亡再開。旧Scene、Collider、Context、購読、Actorが残らない |
| P15 | NewGameAndLegacyTrial_UseCorrectProgressScope | P5新規開始0、P5往復保持、既存試遊への切替・Retryはローカル0 |
| P16 | RebuiltScenes_RunWithoutManualWiring | Builder出力から起動し、注入・HUD・調査・Encounterまで手動配線不要 |
| P17 | CameraTransition_PreservesBoundsAndShakeBase | 領域移動・Scene到着・揺れ後に基準位置がずれず、Camera／Listener各1つ |

P03はロード中に時計が進まないことを、ロード前後の残り値と許容するGameplay tick数で検査する。現実のロード時間そのものを厳密な秒数でAssertしない。

P05は少なくとも敵への攻撃・撃破を実Hitboxで通す。P09の敗北条件設定やP12の不備注入はテスト専用の公開Adapter／Fixtureで行ってよいが、処理結果を直接セットして命中・再開・ロード経路を飛ばさない。

P14ではDestroy予定になっただけで成功とせず、Scene unload完了と破棄後に検査する。P15はP4の技術的基準を組み込んだ状態で実行する。

### 15.4 Validatorと再生成

| ID | メソッド名 | 必須の判定 |
|---|---|---|
| V01 | GeneratedAreas_PassSceneDataAndAssetValidators | 全必須配線・ID・素材・Build Settings・NavMeshが揃う |
| V02 | BrokenFixtures_FailWithSpecificDiagnostics | 重複ID、入口欠落、未配線Context、Floor不整合、空Encounter、入力二重を各検出 |
| V03 | Rebuild_IsRepeatableAndDoesNotModifySharedAssets | 2回生成で重複なし、共有Player／敵／Reward／Attack原本の内容不変 |
| V04 | DirtyScene_BlocksDestructiveGeneration | 未保存Sceneを保持して失敗。勝手に保存・破棄しない |

予定必須数はEditMode 27＋Validator 4＋PlayMode 17＝48件。これは48件あれば合格という下限判定ではなく、名前付き要求の開始一覧である。

### 15.5 既存回帰と実行記録

- P4RequiredTests.jsonを維持し、修正後P4の必須一覧も照合する。P5の一覧へ置換してP4の要求を消さない。
- P3.5の4 Wave、徳94、勝敗、Retry、Guard／JG／JustEvade、守護の順序別結果を維持する。
- 書込み→refresh／compile-status→絞込みテストの順を守る。古いアセンブリを検査しない。
- 最終ゲートはEditMode／PlayMode全件、Data／Scene／Asset検証、P4／P5必須照合。
- 必須の欠落、非Passed、Skipは不合格。一致0件、中断、完走不明は合否不明であり成功ではない。
- 完走はtermination=completedと葉結果の整合で確認する。minPassedだけで代替しない。
- 全件の件数は実装後に記録する。以前の1614／71等を期待値として固定しない。
- 記録には対象コミットまたは検証対象ファイルの内容ハッシュ、runId、mode、filter、成功・失敗・Skip、終端結果、必須照合結果を残す。
- Sceneやコードを再生成・変更した後は、その変更に必要な検証を更新する。古いSceneの成功を最新版の証拠として扱わない。
- 最終検証後にisPlaying=falseを確認する。

すべてのSceneテストはfinally／TearDownでロードしたScene、常駐Session、Inputデバイス、Provider、購読、NavMesh登録、timeScaleを復元・解放する。失敗したテストでも後続を汚さない。

## 16. Bridge運用と人間受入

### 16.1 自動実行

既存CLAUDE.mdのUnityテスト常時許可・未保存Scene保護を維持する。本書は新たな確認待ち工程を増やすものではない。

BridgeへP5生成・検査を登録する場合は、許可リスト付きのrun-opとして追加する。例：build-exploration-trial、validate-exploration-trial。任意コード実行口を増やさない。

必須照合の既存RunnerがP4RequiredTests.json固定なら、P4／P5の既知manifestを明示選択できるようにする。ユーザー入力の任意パスをそのまま読み込む仕組みにしない。既存P4照合を壊さない。

### 16.2 最後に行う人間試遊

技術的ゲートが通ってから、5〜10分程度の一周確認を依頼する。

1. 壁・水・通路・門が見分けられ、歩ける場所が理解できるか。
2. Interact候補が狙った対象になり、調査・レバー・扉の操作が理解できるか。
3. 犬丸が曲がり角・門で長く引っ掛からず、ワープが不自然に頻発しないか。
4. Scene移動の到着位置、暗転、カメラ境界に違和感がないか。
5. 遭遇開始→戦闘終了→探索復帰が明確で、移動不能が残らないか。
6. 徳・開通・調査済みが往復／死亡後も残ることが画面から分かるか。

正式スプライトの作画、完成マップ美術、最終カメラサイズ、最終戦闘バランスはP5の評価対象にしない。

## 17. 完了報告と後続への引渡し

Claudeは次の成果物を揃える。

- 本書を実装時の正本としてリポジトリへ配置し、変更判断があれば履歴を追記。
- P5の実装、Data、3 Scene、Builder／Validator。
- P5RequiredTests.jsonと既存P4一覧の維持。
- README_Phase5_探索試遊版.md：起動、操作、2エリアの導線、徳22／死亡後の挙動、データはアプリ終了で失われること。
- P5_統合受入結果.md：実装工程、対象コミット、実行結果、手動受入の未実施／実施済み、既知残件。
- P5_後続課題.md：P6以降へ渡す限定事項。実装済みと未実装を明記。

P5終了条件は、§1.1の一周、通常往復での進行・Actor値保持、死亡時の進行保持とActor再初期化、P3.5／P4互換、48件の要求と既存回帰の合格、再生成可能性、人間による短い試遊受入が揃うこと。

### 後続へ残す事項

| Phase | 引渡し |
|---|---|
| P6 | ディスク保存、PlayerProgressSaveData等のDTO、欠損／旧版、通常敵再出現と保存の関係、章別戦闘徳上限、Item／成長、完成版休息・再開点 |
| P7 | 会話・EventSequence、恒久イベント進行、加入演出、条件付き扉、章移行 |
| P8 | 猿・雉、切替、複数仲間の状態持ち越し、役割別経路 |
| P9以降 | 第一章の量産導線、地図UI、屋根・壁透過、必要な複数Floor、実コンテンツのEncounter種別 |
| P10b | 正式素材に合わせたカメラ・VFX・見た目上の間合い・演出値 |

仕様判断が必要な差分が見つかった場合は、該当箇所・現行契約・影響範囲・推奨案を明記する。無関係な先行可能工程まで停止しない。一方、既得徳を死亡で失わせる、Proxy探索を自動戦闘で中断させる等の既存裁定を、実装都合で黙って変更しない。

## 18. 参照資料

以下は設計時に確認した一次資料。コード参照はa520a1cに固定している。実装時にはP4修正後の最新内容も確認する。

- [改訂ロードマップv2.2](https://github.com/JP2943/Momotaro/blob/a520a1c6d79f769dd67989fc960409bc1132edc4/桃太郎プロジェクト_改訂ロードマップ_v2.2.md)：P5の対象、P6との分割。
- [ゲーム仕様書v1.4](https://github.com/JP2943/Momotaro/blob/a520a1c6d79f769dd67989fc960409bc1132edc4/制作開始パック/桃太郎アクションRPG_ゲーム仕様書_v1.4.docx)：§8.3、8.7、8.8、11.1〜11.9、13.8。
- [PlayerProgressHolder](https://github.com/JP2943/Momotaro/blob/a520a1c6d79f769dd67989fc960409bc1132edc4/Assets/_Project/Scripts/Gameplay/Progression/PlayerProgressHolder.cs)：現在はローカルreadonly State。
- [PlayerProgressState](https://github.com/JP2943/Momotaro/blob/a520a1c6d79f769dd67989fc960409bc1132edc4/Assets/_Project/Scripts/Gameplay/Progression/PlayerProgressState.cs)：徳・RewardId単位GrantOnce。
- [CombatRewardCollector](https://github.com/JP2943/Momotaro/blob/a520a1c6d79f769dd67989fc960409bc1132edc4/Assets/_Project/Scripts/Gameplay/Progression/CombatRewardCollector.cs)：既存の報酬経路。
- [BootstrapRoot](https://github.com/JP2943/Momotaro/blob/a520a1c6d79f769dd67989fc960409bc1132edc4/Assets/_Project/Scripts/Infrastructure/Bootstrap/BootstrapRoot.cs)：常駐・初期化の拡張点。
- [SceneFlowManager](https://github.com/JP2943/Momotaro/blob/a520a1c6d79f769dd67989fc960409bc1132edc4/Assets/_Project/Scripts/Infrastructure/SceneFlow/SceneFlowManager.cs)：現在のSingleロード・遷移排他。
- [CompanionActivityContext](https://github.com/JP2943/Momotaro/blob/a520a1c6d79f769dd67989fc960409bc1132edc4/Assets/_Project/Scripts/Gameplay/Companion/CompanionActivityContext.cs)：活動許可の正本接続。
- [InvestigationRecordHolder](https://github.com/JP2943/Momotaro/blob/a520a1c6d79f769dd67989fc960409bc1132edc4/Assets/_Project/Scripts/Gameplay/Companion/Investigation/InvestigationRecordHolder.cs)：現在のScene記録。
- [EncounterData](https://github.com/JP2943/Momotaro/blob/a520a1c6d79f769dd67989fc960409bc1132edc4/Assets/_Project/Scripts/Data/Events/EncounterData.cs)：既存雛形を拡張する。
- [Packages manifest](https://github.com/JP2943/Momotaro/blob/a520a1c6d79f769dd67989fc960409bc1132edc4/Packages/manifest.json)：AI Navigation導入済み、Cinemachine未導入。
- Momotaro_P4_Review_a520a1c.md：直前のレビュー文書。P4 R3-01〜05の前提条件。


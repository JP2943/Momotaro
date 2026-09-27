# 桃太郎プロジェクト P5.5 エリア接続・スライド遷移 詳細仕様書 v1.0

作成：2026-09-25  
宛先：Claude（実装・Unity検証担当）  
環境：Unity 6000.3.20f1／URP／Orthographic／XZ平面・Physics 3D  
位置づけ：P5修正の後、P6およびマップ量産の前に実施する追加フェーズ。

## 0. 依頼の目的・基準

地続きのエリアを移動するとき、前のエリアと次のエリアの位置関係が分かるよう、両方の実マップを描画しながらカメラをスライドさせる。正常な地続き遷移では全画面暗転を行わない。家・洞窟等の扉、死亡再開は暗転を維持する。

ユーザー承認済み方針を本書で実装契約へ具体化する。0.45秒等の初期演出値と下記の実装方式は今回の設計決定であり、既存コードに実装済みという意味ではない。

設計時のコード確認：`phase/5-world-exploration`、`65e82f1d4417ec4d217eb9f48a096a5dc555568a`。P5仕様の正本は `Momotaro_P5_Detailed_Spec_v1.1.md`。関連するInitializer・TransitionService・Builderを確認した。GPTがUnityを再実行したものではない。

実装開始時には最新HEADとP5修正を確認する。上記SHAへ作業ツリーを戻さない。推奨ブランチは `phase/5.5-area-slide`。P5がmain未統合なら修正済みP5先端から依存ブランチとして分岐し、親SHAを記録する。mainへの統合は本依頼に含めない。

### 0.1 着手順序

先に直前のP5レビューを再照合し、次を修正・検証する。既に修正済みなら重複実装しない。

| 項目 | P5で閉じる内容 |
|---|---|
| 再開完了の早期確定 | 到着初期化の途中でRespawnをIdleにしない。失敗・遅延到着を含め、常駐側が要求ID／遷移IDを照合して確定 |
| 再開失敗経路 | Actor持ち越しがない死亡再開でも、同じ要求IDで再試行可能。SceneのRunner消滅に依存しない |
| 犬丸の全CD解除 | 攻撃・ガード・回避・守護の非ゼロCDから解除を確認 |
| Validatorの不足 | NavMesh・安全配置・戦闘Triggerとの重なり・子ObjectのMissing Scriptを担当検査へ反映 |
| 主人公の到着向き | 入口の向きを適用する。P5.5ではスライドの進行方向と整合必須 |

P5.5のData設計・仮地形準備は先行可。上記に依存する統合は修正後。P5の人間受入が未完なら未完と記録し、P5.5着手を受入済みの代わりにしない。

## 1. スコープと完成する体験

### 1.1 必須体験

1. Aを操作中、東の出入口に近づくとBの表示を先読みする。
2. 既存の「範囲内で出口方向へ0.15秒入力」で移動を受理する。
3. Aを表示したまま必要な準備を終え、Aの東にBがつながった景色を用意する。
4. 主人公・犬丸の表示が境界を渡り、カメラが右へスライドする。
5. Bで操作を再開。徳・HP・スタミナ・犬丸の状態・残時間・探索進行をP5契約どおり保持する。
6. Bの西の開放出入口からAへ戻ると左へスライドする。
7. 別のInteract扉でB→Aへ戻る経路は暗転を維持する。
8. Bで死亡した場合は暗転してAの再開点へ戻り、P5の死亡再開契約を維持する。

### 1.2 実装範囲

- 東西の実A↔Bを先に完成させ、共通処理を南北にも対応させる。
- 南北は小さなテスト用エリア対で物理入力から到着まで検証する。新しい本編コンテンツは増やさない。
- 先読み先は最大1エリア、読み込まれたAreaは最大2つ（活動中＋待機／撤去中）。常駐System Sceneは別枠。
- 通常のエリア内追従カメラは維持する。固定一画面の部屋しか作れない構造にはしない。
- 正式スプライト完成は前提としない。既存Moveを使用する。

対象外：全世界常時読込、無停止シームレス戦闘、境界越し索敵・攻撃・Projectile、複数階、斜め／回転接続、巨大ストリーミング基盤、Addressables導入、完成版Save／Load、全面的なActor常駐化。

## 2. 採用方式

**実SceneをAdditiveで先読みし、表示準備状態で保持する。移動受理後に両Areaの活動を止め、単一のカメラを移動させる。成功確定後に旧Areaを解放する。**

RenderTextureのスクリーンショット2枚をスライドさせる方式は本フェーズの標準方式にしない。マップは実Rendererで描画し、地形の接続・門の状態・前景との重なりを確認できるようにする。

UnityのScene読込完了と、ゲーム上の活動許可を区別する。`allowSceneActivation=false`で0.9待機する方式を常時先読みに使わない。Sceneは読込完了させ、後述の非活動状態で保持する。Sceneロード／unloadの発行は単一の管理者が直列化する。

P5のSingle読込を単純にAdditiveへ置き換える実装は不可。現行の全Scene対象`FindFirstObjectByType`、AreaごとのCamera、Initializerの自動活動許可を先に整理する。

## 3. 接続Dataと座標

### 3.1 新しい契約（名称は推奨、意味は必須）

| 項目 | 意味 |
|---|---|
| ConnectionId | 接続のStableId。一方向レコードごとに一意 |
| FromAreaId／ExitId | 出発Areaと出入口 |
| ToAreaId／EntryId | 到着Areaと入口 |
| TransitionStyle | Fade / Slide。既存未指定はFadeにして既存Dataの挙動を保持 |
| Direction | East / West / North / South。Slideでは必須 |
| ReverseConnectionId | 対になる逆方向Slide。往復整合の検査に使う |
| SeamAnchor／PassageWidth | 両エリアで共有する接続境界と通路幅 |
| ArrivalPose | 主人公・犬丸の安全な到着位置と向き |
| CameraArrivalPose | 到着時の追従・clampから決まるカメラ位置。手入力した別の不整合な正本を増やさない |
| SlideDuration | 初期値0.45秒。0.30〜0.70秒を試遊調整範囲とする |
| PreloadDistance | 出入口からのXZ距離。初期値6 world units |

AreaId／EntryIdは既存を再利用し、接続のために進行IDを振り直さない。Scene内Transformの参照をDataへ入れない。DataのIDとScene内の明示Bindingで解決する。Runtimeでは受理時に接続・演出設定をSnapshot化し、途中でSOの値を読み直さない。

### 3.2 座標の決定

P5.5の実マップは**固定の共通ワールド座標**へ配置する。Aの東にBを接続し、Sceneを読み込むたびにrootを動かす方式を避ける。

- BuilderでAreaごとのAuthoring原点を定め、床・壁・水・入口・仕掛け・スポーン・Camera領域をまとめて配置した後、その配置でNavMeshをベイクする。
- P5のローカル配置定数を使う場合、local→world変換を一か所に集め、CameraBoundsやSpawnだけオフセットを忘れない。
- 先読み時・スライド中・到着時にNavMesh付きrootを移動しない。往復で座標を累積加算しない。
- 東西はX軸、南北はZ軸。画面上の上下は既存Camera投影に従う。地形の高さYは同じFloorを維持。
- 接続する通路の中心・幅・床高が両側で一致し、重複床のちらつき、隙間、壁の食い込みを出さない。
- 出入口受理位置→接続境界→到着位置が進行方向へ並ぶ。戻る動きや長距離の不自然な自動歩行を必要とする入口位置はBuilderで修正する。

P5.5用Scene／Dataを `Scenes/Tests/Phase55/`・`Data/Tests/Phase55/` に作成する。P5の受入用Sceneを保存し、P5.5の配置へ黙って作り替えない。既存仕様を持つAreaの別レイアウトなので、同じAreaIdのP5／P5.5カタログは別の試遊構成として選択し、両方を同じSessionへ混在させない。

## 4. 表示と活動の分離

### 4.1 Areaの状態

| 状態 | 描画 | Gameplay／物理／購読 | 進行State |
|---|---|---|---|
| Unloaded | なし | なし | Sessionに保持 |
| Staged | 地形・仕掛けの表示準備済み | 無効 | 読取だけ。訪問記録も書かない |
| Prepared | 到着先Actor復元・配線済み | 停止中。外部へ未公開 | 更新予定を保持 |
| Active | 通常描画 | そのAreaだけ有効 | 通常更新可 |
| Suspended | 旧Areaの地形を表示 | 同期停止、遷移中は外部活動なし | 中断後の値を保持 |
| Retiring | 原則画面外 | 無効、所有購読を解除 | 更新なし |

ロード時の既定はStaged。AreaContextは未準備・非活動を既定にし、参照未割当を活動許可にしない。

Preparedへの移行では、必要なAwake／OnEnable／初期化を活動ゲートが閉じた状態で完了し、その後にSnapshotを適用する。Snapshot適用後にStart等が初期値で上書きしないことを最初のUpdateまで検査する。旧Areaの停止にOnDisableのResetが伴う場合、Rollbackでは保存済みSnapshotから値と状態を復元してから活動を戻す。旧Sceneを残すだけで値も残ると仮定しない。

### 4.2 Scene構造

推奨構造は以下。名称より責務を優先する。

- AreaDescriptor：AreaId・入口・接続等のメタデータ。ロードしたSceneを指定して取得。
- VisualRoot：床・壁・門等のRenderer。Start/OnEnableでゲーム状態を変更しない。
- GameplayRoot：Actor、AI、Trigger、Interactor、報酬、HUD購読等。**Scene保存時から非Active**。
- PhysicsRoot：Collider／Rigidbody／NavMesh登録／Obstacle等。Stagedでは無効。Visualと同じ位置を共有するが活動許可を別管理。

実装上複数rootへ分けなくても同等の明示ゲートは可。ただし「読込後にFindして無効化する」方式ではAwake／OnEnableの副作用に間に合わないため不可。Awakeはローカル初期化のみ、Provider登録・Session生成・Gameplay購読は所有者の認可後。

StagedにMainCamera・AudioListener・Bootstrap・入力消費者・有効なライトの重複を作らない。RendererとColliderが同一Objectの既存Prefabは、その分離方法を実装工程で明示する。

門開通・調査済みの見た目はSessionを読んで表示へ反映するだけとし、レバー操作や報酬APIを呼ばない。先読み後に出発Areaで進行が変わる可能性があるため、受理後に最新値で表示を再同期する。先読みしただけで敵生成・訪問登録・加入・GameMode変更をしない。

### 4.3 常駐所有者・検索

- CurrentAreaは、AreaIdだけでなく実際のScene handle／ロード世代を保持する。
- 同じAreaIdでも以前のSceneインスタンスを現行と認識しない。
- TransferPort／Context／Initializer／RespawnRunner／Encounter／CameraRegionの取得はScene限定または明示Bindingにする。
- HUD、FeedbackDispatcher、索敵、Threat、調査、Interact、Projectile等について全Scene検索とstatic登録を棚卸しする。
- 二つのSceneが存在しても、ゲームから利用可能なActor／入力／購読は活動中Areaだけ。準備用Actorを公開する必要がある実装では、その登録をArea別に隔離し、非活動Areaを全利用者が除外できる契約にする。
- PhysicsRootとNavMesh登録もArea所有とする。Preparedの検証で到着側を有効化する際は出発側を先に閉じる。Rollback時は逆順に戻し、NavMeshDataInstanceの二重登録・残留を作らない。門のObstacle更新後に経路検証を行い、固定1フレーム待ちだけで成功としない。
- P5.5は単一常駐CameraRig・Camera・AudioListener・入力サービスを使用。直開きも同じ起動経路へ合流する。P3.5／P4の既存SceneのCameraを一括改造しない。
- 準備Areaの初期化で遷移サービスの「現行受付条件」を差し替えない。出発／到着のBundleを別々に保持し、成功確定時に交換する。
- 解除はScene・世代・所有者一致。旧AreaのOnDisable／unloadが新AreaのProviderを消さない。

## 5. 先読み

出入口の6 units以内で、活動中Areaの有効なSlide接続を候補にする。距離→ConnectionIdのOrdinal順で一つを選ぶ。先読み自体は操作・時計・探索を止めない。実際の遷移受理条件は変更しない。

一度読み始めた先は、同じAreaで活動している間は境界から離れても保持する。距離境界でロード／unloadを反復しない。別候補への切替は距離差2 units以上が0.5秒継続した場合に限り、旧先読みの終端・解放後に行う。P5.5実試遊では候補一つでも、この順序は守る。

- 読込済みの同一Sceneへ重複ロードを発行しない。
- 先読みRequestIdと受理後のTransitionIdを区別する。遷移は対応する先読み結果を引き取り、二度ロードしない。
- 読込完了だけでReadyとしない。地形Renderer・必要資産・表示用状態反映まで完了後にStagedReadyとする。
- 正常時、スライド開始前に描画準備完了後のフレームを一度通す。固定秒数の待機では準備確認を代用しない。
- 受理時に準備不足なら、Aを描画したまま操作を止めて待つ。0.3秒以上待つ場合だけ控えめな「読み込み中」を表示。全画面を黒くしない。
- 先読み失敗は出発側Gameplayへ影響を与えない。自動で毎フレーム再試行しない。次の新しい遷移操作で一度だけ再試行できる。
- Scene操作が走っている間は別Scene操作を重ねない。先読み中の死亡・暗転扉は先読みを不要扱いにし、終端して隔離Areaをunloadしてからロードを発行する。

## 6. 遷移トランザクション

### 6.1 受理条件と競合

開放出入口のTriggerは主人公本人を物理経由で検出し、出口方向への0.15秒入力で要求する。敵・犬丸・押し出しだけでは発動しない。扉は既存Interactの単一窓口から要求する。

P5の優先関係を維持：死亡確定／戦闘開始が通常遷移に優先。Startingを含むEncounter、Player行動中、Pause／Dialogue／Event、未AreaReadyでは通常遷移を受理しない。受理済み遷移中の要求はAlreadyTransitioningで拒否。先読み中であることだけでは拒否しない。

受理は命中・死亡・Encounter開始の同一フレームの結果を解決した後の調停点で確定する。単にLateUpdateへ移して順序依存を残さず、明示的な調停呼出順または一元処理で優先度を固定する。

### 6.2 正常手順

1. 接続・Area／Scene世代・到着位置を検証し、排他を確保する。受理前の不正DataではStateを変えない。
2. 出発AreaをSuspendedへ。GameMode=Loading、Gameplay時計を凍結。入力ラッチ、探索・戦闘行動を同期中断する。
3. 中断により発生するCDを含めActorSnapshotを採取する。Rollback用に出発位置・向き・Camera poseも保持する。Sessionの進行は複製しない。
4. 対応するStagedReadyを取得。最新の門・調査等の表示状態を再反映する。
5. 出発側のGameplay・登録・NavMesh／物理を閉じ、到着側を隔離されたPrepared状態へ初期化する。Snapshot復元と安全到着位置、Cameraの終点を検証する。この段階では訪問登録や完了通知を出さない。
6. 表示専用Actorを使ってスライドを実行する（§7）。両方の実ActorのRendererは隠し、ゲーム上の活動は停止したまま。
7. 終点へ正確に配置。到着Actorの復元値、配線、安全位置、世代を再確認する。
8. 成功確定：CurrentArea／受付条件／Scene active指定／HUD・Feedback・入力対象／Camera追従先を到着側へ切り替える。旧側をRetiringへ。実Actor表示を戻し、表示代理を除去する。
9. ここで初めて訪問済みを記録し、到着側の活動を許可する。旧ラッチ破棄と出口の再入防止を設定、時計とGameplay入力を復帰する。
10. 遷移の排他・旧要求の共有情報を片付けてからArrivalCompletedを一度だけ通知する。通知内の新規要求を拒否せず、旧後始末が新要求を消さない。
11. 旧Areaをunloadする。unload完了前の逆方向要求は受理後の待機として扱えるが、旧Sceneを二重ロードせず、その解放終了後に次の先読み／ロードを開始する。

**成功確定点は8〜9の同期区間。** そこにyieldを挟まない。実行前に失敗しうる作業を済ませ、外部通知は区間後に出す。前半の初期化成功を全体の成功と混同しない。

### 6.3 持ち越す値

P5のActorTransferInventoryと同じ全値を保持する。HP、スタミナ、無敵・Hurt・ひるみ残時間、犬丸Down復帰待ち、攻撃／防御／守護CDは受理後の待機・スライド中に減らさない。攻撃・防御・調査の途中動作は再開しない。徳・GrantOnce・門・調査・通常戦クリア・加入はSessionの同一正本を使う。

主人公は接続進行方向を到着向きとする。犬丸のDown／Stagger／AwayはP5の復元表を維持。Awayを描画・出撃させない。遷移を死亡再開扱いにせず、HP回復・CD解除・通常敵再出現を発生させない。

## 7. カメラとActor表示

### 7.1 カメラ

- スライド開始位置は受理時の実CameraRig位置。事前に境界位置へ瞬間移動させない。
- 終点は到着Actor位置に対して、到着Areaの通常追従・clampが算出する位置。
- 初期値0.45秒、unscaled時間、補間曲線 `s(t)=3t²−2t³`（tは0〜1）。開始／終了位置を厳密に固定。
- 期間中は通常追従・通常clampの書込を停止し、スライド担当だけがRig位置を書く。
- 揺れはCamera子Transformの既存分離を維持し、開始時に残留揺れをゼロへ。遷移中に新しい揺れ・HitStopを開始しない。
- 回転・orthographicSize・投影を途中で変更しない。完了後の追従内部状態も終点へ同期し、翌フレームに跳ね返らない。
- 東西ならCameraの移動はXだけ、南北ならZだけとなるよう入口とCamera領域を配置する。接続軸以外にずれる設計はValidatorで不合格。

0.45秒は演出時間でありSceneロード時間を含まない。アプリ非フォーカス中はスライドの表示時間を進めず、復帰時に巨大deltaで飛ばさない。ロード監視は別のunscaled／実時間で継続する。受理後のPause要求は捨て、到着後に古いPauseを発火させない。

### 7.2 主人公・犬丸の表示代理

本フェーズはActorをScene間で永続移動させず、P5の到着Actor再生成＋Snapshot復元を維持する。遷移中だけ、表示専用代理で連続した移動を見せる。

- 表示代理はSpriteRenderer等の描画部品だけを持つ。ActorのPrefab全体を複製して後からスクリプトを外す方式は使わない。
- Rigidbody／Collider／Hitbox／Vitals／AI／報酬／Gameplayイベント／レジストリ登録を持たない。
- 開始時に実Actorと同じSprite・足元位置・縮尺・色・Sortingをコピーし、同じフレームに実Rendererを隠す。二重表示や一瞬の欠落を作らない。
- 主人公の表示位置は出発位置から到着位置へ、カメラと同じ補間進行度で移動する。通路を実際に渡る見た目を作り、壁を横切る配置を許さない。
- Moveの既存6コマ周期を表示専用のunscaled時計で再生する。AnimationEventや攻撃／足音等のゲーム通知を発火しない。末尾では実Actorへ同じ姿勢または自然なIdleで引き継ぐ。
- 健常犬丸は追従の見た目で同様に移動。Down／Staggerは既存姿勢を保持して位置だけ運ぶ。回復演出を勝手に再生しない。Awayは代理も作らない。
- 犬丸が遠くにいて開始位置が画面外なら、そのまま画面外から運ぶ。見えている犬丸を開始時に主人公の足元へ瞬間移動させない。障害物を横切らない表示経路を選び、Downを通常Follow Warpへ通さない。
- 表示経路を安全に作れない接続は準備失敗とする。無断で暗転へ切り替えて成功扱いにしない。

### 7.3 地形と画面端

スライドの全区間でCameraが見る範囲を、両AreaのVisualRootと背景装飾で覆う。接続部の穴、黒い帯、画面全体を覆う壁を出さない。境界のColliderを演出のために通過する場合でも、その区間の見た目は通路として開いていること。

試遊基準は16:9（1280×720／1920×1080）。同じ投影サイズで確認する。その他比率の完成版対応は後続だが、画面サイズ変更で例外を出さない。演出中は始点・終点の計画を固定し、次の遷移から新しい表示寸法を反映する。

## 8. 失敗・解除・暗転との共存

| 発生地点 | 振る舞い |
|---|---|
| 受理前の不正接続／先読み失敗 | Aをそのまま操作可能。理由を記録。入力連打によるロード連発なし |
| 受理後、スライド開始前の準備失敗 | Bを隔離・解放し、旧ActorとCameraを出発位置で復帰。進行と中断後Snapshotを維持 |
| スライド途中／成功確定前の失敗 | 世界は停止したまま同じ描画経路を逆向きに最大0.25秒で戻す。旧Actor表示と活動を復帰 |
| 成功確定後の旧Area unload失敗 | Bの到着成功を取り消さない。旧Areaは非活動・非物理・購読解除済みで隔離。新たなAreaロードを止め、理由と再試行手段を表示 |
| ロード監視タイムアウト | 旧Areaへの復帰を優先。古い操作はキャンセルできたと扱わず保持し、終端後に遅延到着SceneをStagedのままunload |
| 旧Areaも復帰不能 | Error表示と既存Launcherへの退避操作。進行Stateは破棄しない。無限自動再試行なし |

ロード監視30秒、到着準備10秒を初期値とする。タイムアウトで既存Scene操作の排他を失わない。遅延完了は世代・Scene handleを照合し、訪問登録・CurrentArea更新・活動開始・成功通知を一切起こさない。旧操作終端までは次のScene操作を発行しないが、旧Areaを安全に復帰できた場合の通常操作は再開してよい。

RollbackはHPや徳を巻き戻す機能ではない。受理後は活動停止しているため、中断後Snapshotと同じ値で旧側へ戻る。探索は自動再開しない。失敗した出入口は一度入力を離してから再操作する。エラー表示に「再試行」と「Launcherへ戻る」の意味を区別する。

FadeとSlideは同じ遷移排他・世代・Scene操作管理を共有し、別サービスが同時ロードを発行しない。扉／死亡再開のFadeは既存契約を維持し、P5.5ではStaged Areaを放置しない。死亡再開の成功／失敗確定はP5修正済みの契約へ合流する。

ArrivalCompleted内から新しい遷移を要求できる。旧Areaの撤去待ちはロード実行の待ちとして扱い、受理済みの新しい要求を旧finallyで消さない。New Game／Launcher退避は全トランザクションを失効させ、進行中の操作の終端を確認してから次の世界を起動する。

## 9. 責務と既存コードの変更箇所

| 層 | 責務 |
|---|---|
| Data | 接続、方向、入口、演出設定。Scene内実体の参照なし |
| Gameplay | 受理・競合・世代・Area活動段階・持ち越し契約・成功確定の純粋ロジック |
| Infrastructure | Additive Load／Unload、Scene handle管理、起動、明示Binding、NavMesh／物理登録管理 |
| Presentation | Cameraスライド、表示代理、待機／失敗UI。進行値・成功判定を変更しない |
| Editor | Builder、Scene検査、Asset／Build検査、Bridge、受入一覧 |

主な改修候補：AreaTransitionService／Coordinator／Contracts、AreaInitializer、AreaPendingArrival、AreaExitGate／Driver、AreaActorTransferPort、AreaCameraRig、AreaNavigationBinder、P5系Builder／Validator、HUD／Feedback／静的Registryの接続点。

名前は実装に合わせてよいが、既存サービスに無関係な全機能を押し込めない。純粋な遷移判断はScene APIに依存させない。AreaRuntimeBundle等で「そのSceneの参照集合」を受け渡す。PlayerやCompanionの戦闘契約は再設計しない。

### 9.1 P5から意図的に更新する検査前提

- 読込済みArea数が常に1、という検査はP5.5には適用しない。**Activeは最大1、Staged／Prepared／Retiringとの合計は最大2**を検査する。
- 全SceneのComponent数ではなく、活動可能数・所有者・Scene handleでCamera／入力／報酬の唯一性を確認する。
- P5.5ではArea初期化だけで訪問済みや活動許可を確定しない。Commit時に行う。
- P5のSingle方式の旧Scene喪失復旧と、P5.5の旧Scene保持Rollbackを混同しない。

P5の54要求のうち挙動が変わる項目は対応表に「置換理由とP5.5の検査ID」を示す。単純にFailする旧テストをSkipへ変更しない。P3.5／P4とP5互換モードは引き続き既存検査を通す。

## 10. Builder・Validator

生成物：P5.5統合起動Scene、A／B、南北検査用の小規模Scene対、接続Data、必要なNavMesh、受入用設定。既存Enemy／Reward／Player／犬丸Prefabを参照し、本番Assetを複製して数値の別正本を作らない。

メニュー案：`Momotaro / Phase 5.5 / Generate Area Slide Trial`、`Validate Area Slide Trial`。Bridgeへ同じ処理を公開し、許可リストへ追加する。未保存Sceneの保護・旧Asset非汚染を維持。

### 10.1 Asset／Build検査

接続ID一意、Area／Exit／Entry解決、逆接続の方向反転、方向の軸整合、Fade既定値、SlideDurationの正値、Sceneの有効Build登録、カタログ混在なし、NavMesh資産の存在、設定の対応、生成先以外の意図しない更新なしを検査する。

### 10.2 Scene検査

Staged保存状態、GameplayRoot／Physics初期無効、初期化担当の自動活動なし、Camera／Listener重複禁止、到着Bundleの配線、入口と境界の位置・幅・高さ、到着直後のEncounterなし、犬丸の安全配置、全階層Missing Script、移動可能通路・Camera掃引範囲の表示範囲を検査する。

NavMeshの到達性・Collider実接触・最終描画はPlayModeと録画で補う。静的検査を通っただけで実際に移動したとは扱わない。Scene検査とAssetDatabase依存検査は分離する。

## 11. 必須受入テスト一覧

`P55RequiredTests.json` を作成し、以下IDを実際の完全修飾テスト名に対応させる。各IDは複数の方向／条件ケースを含めてよいが、未実行ケースやSkipを合格に数えない。件数だけでなく、要求したテスト名の存在とPassedを検査する。

| ID | 必須検査・観測点 |
|---|---|
| E01 | 接続解決・逆方向・不正ID・軸不一致・既存DataのFade既定 |
| E02 | 死亡／Starting優先、Pause拒否、遷移再入拒否。先読みだけでは行動を止めない |
| E03 | 受理・成功・Rollbackの世代照合。古い完了／解除が新要求へ作用しない |
| E04 | 先読みの同一先再利用、候補切替、最大2Area、ロード直列化 |
| E05 | 開始／中間／終了のCameraとActor補間、所要時間・向き・終点精度 |
| E06 | Preparedでは未訪問・未完了、Commitだけが訪問／成功通知を一度確定 |
| E07 | 失敗・タイムアウト・遅延完了・Rollbackで進行値を変更しない |
| E08 | 成功通知から次要求、unload待機、新要求と旧後始末の分離 |
| P01 | 実Sceneと実移動入力でA→B。Gate／Driver／完了通知をテストから直接操作せず右スライド到着 |
| P02 | 同様にB→Aの左スライド。押しっぱなしで逆戻りせず、離して再入力すると移動可能 |
| P03 | 実Scene南→北／北→南で上下スライド。画面の方向と実ワールドの方向が一致 |
| P04 | 先読み中にAで移動・調査可能、Bの敵・登録・報酬・訪問・Bootstrap副作用なし |
| P05 | 非ゼロHP差分・スタミナ消費・全CD・無敵・復帰待ちを使い、待機＋スライドで値が進まず持ち越される |
| P06 | 健常／Down／Stagger／Awayの犬丸。意図せぬ復帰・出撃なし、不正遷移0。表示代理から命中も登録も発生しない |
| P07 | 先読み未完で要求。Aを表示した待機→準備後スライド。重複ロードなし |
| P08 | 実サービスへロード開始失敗・初期化失敗・スライド途中失敗を注入。旧Areaで再操作でき、要求ID／値を保つ |
| P09 | 遅延ロード・タイムアウト・古いScene到着。活動・訪問・成功通知なし、終端後だけunload／次ロード |
| P10 | Commit後unload失敗。Bは維持、旧登録・物理は無効、追加ロードの上限を守り撤去再試行可能 |
| P11 | 先読み中の死亡→暗転再開、およびInteract扉のFade。先読みを安全に破棄、Submitの到着Interact化なし |
| P12 | Camera／AudioListener／入力／HUDの唯一性。移動中と翌フレームのCamera飛び・通常Followとの二重書込なし |
| P13 | 同じ実プレイ導線でA↔Bを5往復→遭遇戦→死亡再開。進行保持、初回徳22、不要な再出現なし |
| P14 | 全Area破棄後、Clear前に全対象Registry・Provider所有・表示代理・Scene残留を確認。旧Scene由来参照0 |
| P15 | 出発・中間・到着の実描画確認。地形接続、主人公の重複／消失なし、通常Slideの全画面暗転なし |
| V01 | 正規生成物がScene／Asset／Data検査通過、二度生成して共有原本非汚染 |
| V02 | Gameplay初期Active、接続ズレ、NavMesh欠落、子のMissing Script、入口の戦闘Trigger重複を壊したFixtureで検出 |
| V03 | Bridgeから検査・必須manifest検証が同じ経路へ接続、未実装／Skip／0件成功を失敗扱い |

P15は自動的な存在・表示判定に加え、16:9の実録画を残す。録画だけで数値契約を合格にせず、数値だけで見た目を受入済みにしない。

static対象はPerception／Threat／Investigation／Interact／Projectile等を実コードから列挙し、非活動Areaのものが利用可能になっていないことを確認する。Actor数が多いだけで失敗にせず活動段階と所有者を検査する。

欠陥注入は「Stagedを活動させる」「古い完了を通す」「未完了ロードを二重発行」「実Trigger配線を外す」「Preparedで成功確定」の主要境界で代表例を一度確認する。後始末でScene・Input設定・仮想デバイス・static状態を残さない。

## 12. 実装工程

| 工程 | 完了内容 |
|---|---|
| P55-00 | P5修正の再照合・必要修正、最新HEADと互換対象の記録 |
| P55-01 | 接続・状態・世代・受理／Commit／Rollback契約、全Scene検索と登録の棚卸し、E系基盤 |
| P55-02 | Staged Scene構造・Scene限定Bundle・Additive先読み・活動隔離。まずP04を通す |
| P55-03 | 単一Camera所有・東西の実地形接続・主人公／犬丸の表示代理・A↔B実入力 |
| P55-04 | Snapshot持ち越し・Commit・Rollback・遅延完了・unload失敗・Fade共存 |
| P55-05 | 南北テスト配置、Builder／Validator／Bridge／manifest、互換回帰 |
| P55-06 | 連続往復・録画・試遊README・受入記録・後続課題、ユーザーの短い見た目受入 |

各工程でClaude自身がUnityを実行し、既存の常時許可とCLAUDE.mdに従って進める。機械検査可能な項目を人間へ都度返さない。新たな仕様矛盾が見つかった場合は、該当契約・影響・推奨裁定を具体的に示す。正常な実装選択は本書の範囲内で自律的に行う。

テスト件数の固定値を期待値として使わない。現時点の実テスト一覧とmanifestで判定する。技術的合格とユーザー受入を分けて記録する。

## 13. 納品と終了条件

納品：実装・生成Scene／Data・メニュー／Bridge・必須manifest・`README_Phase55_エリア接続試遊.md`・`P55_統合受入結果.md`・残件一覧・東西南北の録画。P5仕様から変更した契約の対応表を受入記録へ含める。

終了条件：必須全項目Passed、対象回帰合格、暗転を使わない東西／南北の実移動、Fade扉・死亡再開の共存、進行／値保持、失敗時の戻り道、再生成可能、長時間のScene残留なし、ユーザーの見た目受入。

人間確認はA→B→Aの短い試遊と録画で、次を確認する。

1. 「東にBがある／西へ戻る」が説明なしに分かる。
2. 地形がつながり、黒い帯・壁横断・Actorの突然消失がない。
3. 開始と終了でカメラが跳ねず、スライドが速すぎ／遅すぎない。
4. 到着後、自然に操作できる。持ち越した押下が勝手な攻撃・Interact・逆遷移を起こさない。

正式素材や全世界マップの完成を待つ必要はない。P5.5完了後にP6保存設計へ進み、保存対象は演出位置やStaged Sceneではなく、既存の論理進行と安定IDを基準にする。

## 参考資料

- 設計基準コミット：https://github.com/JP2943/Momotaro/tree/65e82f1d4417ec4d217eb9f48a096a5dc555568a
- Unity Additive読込：https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.LoadSceneMode.Additive.html
- Unity allowSceneActivation：https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AsyncOperation-allowSceneActivation.html

公式API資料は仕組みの確認に使用したもの。本書の状態管理・演出値・受入条件は桃太郎プロジェクト向けの設計であり、Unityが自動で保証する機能ではない。

---

# 付録 A：単一常駐 CameraRig（案 A）の起動・所有・依存関係

- 追記日：2026-09-27／工程 P55-03b の着手前
- 経緯：§4.3 の「P5.5 は単一常駐 CameraRig・Camera・AudioListener・入力サービスを使用」について、
  実装形が二通り解釈できたため裁定を仰ぎ、**案 A（単一常駐 Rig）で確定**した。
  本付録はその裁定を実装条件まで落としたもので、v1.0 本文の変更ではなく**補足**である。
  本文と食い違う記述があれば本文（§4.3／§7.1／§9.1）が優先する。

## A.1 所有の境界

| 要素 | 所有先 | 備考 |
|---|---|---|
| CameraRig・Camera・AudioListener | **P5.5 セッションの常駐側**（`DontDestroyOnLoad`） | 一度だけ生成する |
| 共通の基準照明 | 常駐側 | Area 間で重複させない |
| `AreaCameraRegion`・領域設定・追従対象 | **各 Area** | 常駐化しない |
| スライド中の Camera 座標更新 | 常駐 Rig の専用制御 | 書き込みは 1 系統 |
| 通常追従 | 同じ常駐 Rig | スライド中は停止 |

**`AreaCameraRegion` 自体を常駐化するわけではない。** Area が領域を提供し、
常駐 Rig が**活動 Area に応じて参照先を切り替える**。切り替えの契機は Commit。

## A.2 起動経路（直開きも合流する）

常駐 Rig は**常駐の起動処理が一度だけ生成する**。P5.5 の A／B を直接開いた場合も同じ経路を通る。
先読み Scene のロードでは再生成も再 Bind もしない。

- 生成主体：**Area 所有の領域集合部品（Presentation）が、常駐 Rig の存在を保証する**。
  部品は Rig の Prefab 参照を明示的に持ち、まだ常駐 Rig が無いときにだけ生成して
  `DontDestroyOnLoad` する（ensure-create）。すでにあれば何もしないので、
  **先読み Scene のロードでは再生成されない**。
- この位置に置いた理由。当初 `BootstrapRoot`（Infrastructure）が Prefab 参照を持つ案を考えたが、
  `BootstrapRoot` はテストが `AddComponent` で直接作る経路があり、
  **その場合に Prefab 参照が空になる**（既存の `InputActionAsset` と同じ問題）。
  Resources からの読み込みを導入すれば回避できるが、このリポジトリにない規約を
  カメラ 1 件のために増やすのは重い。Area 側に置けば、生成経路は 1 本のままで
  直開き・統合起動・先読みのすべてが同じコードを通る。
- **Infrastructure → Presentation の参照は追加しない。** 上の形なら Infrastructure は Rig に
  一切関与しない。遷移サービスが Commit 時に Rig へ用がある場合は、
  共通層（Gameplay）の狭いインターフェース（所有者一致の Provider 付き）を通す。
  Infrastructure は `AreaCameraRig` の具体型を知らない。
- Area Scene は Camera・AudioListener・基準照明を**持たない**。
  領域と追従対象だけを、Area 所有の部品として提供する。
- P3.5／P4／従来 P5 互換モードの Scene は**一括変更しない**。常駐化は P5.5 の起動経路に限定する。

## A.3 準備と適用を分ける

§7.1 は「スライド開始位置は受理時の実 CameraRig 位置。事前に境界位置へ瞬間移動させない」と定める。
現行 `AreaCameraRig` は `AreaContext.Prepared` の通知で `SnapToTarget()` を呼ぶため、
**そのまま流用するとスライド前に跳ぶ**。

- **Prepared 時点でカメラを移動先へ Snap しない。**
- 到着点の**計算**（到着 Actor 位置に対する通常追従・clamp の結果）と、
  **実カメラへの適用**を別の入口に分ける。Prepared では計算だけを行う。
- 適用するのは、スライド担当（演出中）と Commit 後の追従復帰だけ。

## A.4 書き込みの一系統化

- スライド中は通常追従・通常 clamp の書込を停止し、スライド担当だけが Rig 位置を書く。
- 終了時は**追従の内部状態も終点へ同期**する。翌フレームに跳ね返らせない。
- 揺れは Camera 子 Transform の既存分離を維持する。開始時に残留揺れをゼロへ。
- 回転・`orthographicSize`・投影は途中で変更しない。

## A.5 Commit で Camera を交換しない

**同じ Camera インスタンスを維持**し、領域・追従対象の**参照だけ**を切り替える。
Commit で別の Camera へ渡すと、同じ Transform を保証しても
Projection・後処理・Audio の担当が入れ替わる瞬間が生まれる。

## A.6 §9.1 に従って置き換える P5 の検査前提

§9.1 は「全 Scene の Component 数ではなく、活動可能数・所有者・Scene handle で
Camera／入力／報酬の唯一性を確認する」と定める。常駐化に伴い、次の検査前提を置き換える。
**単純に Fail する旧テストを Skip へ変更しない**（§9.1 末尾）。置換理由と P5.5 の検査 ID を対応表に残す。

| 旧前提 | 置換後 | 置換理由 |
|---|---|---|
| Area Scene に `AreaCameraRig` が 1 つ | Area Scene に Camera・AudioListener・`AreaCameraRig` は**0 個**。常駐側に 1 つ | 常駐が所有するため、Scene 内の個数は唯一性の根拠にならない |
| Area Scene に有効な Camera が 1 台 | **活動可能な Camera が 1 台**（所有者＝常駐） | 2 Area 同時読込では「Scene 内 1 台」でも合計 2 台になりうる |
| `AreaCameraRig.IsWired` を Scene 検査で見る | 常駐 Rig が**活動 Area の領域集合と Bind できているか**を実行時に見る | Bind 先が Scene ごとに入れ替わるため、静的配線検査では表せない |
| Prepared で `SnapToTarget()` が呼ばれる | Prepared では**到着点の計算だけ**が起きる（`SnapCount` は増えない） | §7.1「事前に境界位置へ瞬間移動させない」 |

対応する P5.5 の検査は §11 の **P12**（Camera／AudioListener／入力／HUD の唯一性、
移動中と翌フレームの Camera 飛び、通常 Follow との二重書込なし）と **E05**（補間の開始／中間／終了）。

## A.7 受入で確かめること（§11 P12 に含める）

- Camera・AudioListener の**数**（活動可能数）が常に 1。
- A→B→A で**同一 Camera インスタンスが維持**される。
- **Commit 翌フレームに位置が跳ばない**。
- **A／B の直開きが成立する**（常駐 Rig が一度だけ生成され、Area の領域と Bind される）。
- 先読み Scene のロードで Rig が再生成・再 Bind されない。

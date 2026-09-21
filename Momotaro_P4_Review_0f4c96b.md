# 桃太郎プロジェクト P4再レビュー・修正依頼
## 対象：0f4c96b／P4-07・P4修正アップロード後

レビュー日：2026-09-10  
リポジトリ：JP2943/Momotaro  
ブランチ：`phase/4-companions-foundation`  
対象SHA：`0f4c96b240d406adb6a03288981c9738f32d4af2`  
比較元：`c8c0ddf48f821b99da9ef8cd51dce566fe927e15`

## 1. 判定

**P4は修正継続。現状を「実装・自動受入完了、人間試遊だけ待ち」とは扱えない。**

前回から6コミット、118ファイル（metaを含む）の差分を確認した。専用Scene／Builder／Validator、活動・移動・状態の調停、Feedbackの登録解除、必須テスト照合が進んでいる。

一方、合意済みP4-07の「地点へ近づいてInteractで調査を依頼する」が、今回「犬丸の自動調査＋待機／追従指示」に置き換わっている。加入資格、Down／Away中の探索表示、依頼ID・完了通知、探索と戦闘の引渡し、試遊経路も不足している。さらに、守護成立後の行動所有権に通常行動へ復帰できなくなる経路がある。

判定の仕様正本は次の2文書とする。

- `Momotaro_P4_Claude_Implementation_Request_v1.0.md`：合意済み詳細仕様と修正依頼。
- `Momotaro_P4_Review_and_Next_Instructions_c8c0ddf.md`：前回レビューと追加裁定。特に§2.4、§4.4、§5、§6。

改訂ロードマップv2.2は全体計画として維持するが、その後に決めた詳細仕様を上書きする根拠にはしない。別途オーナーが具体的な仕様変更を承認している場合は、その承認と変更項目を対応付けてから正本へ反映する。今回確認できた「P4完了まで進める」「数値の体感確認を後にする」という記録から、InteractやDown探索の廃止まで承認されたとは判断していない。

本レビューはコード・Scene・Prefab・テスト・文書の静的照合。GPT側ではUnityを再実行していない。リポジトリ変更は行っていない。

## 2. 受入記録と、進んだ修正

[P4_統合受入結果.md](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/P4_統合受入結果.md#L1336)の最終記録は以下。

| 検証 | 記録された結果 |
|---|---|
| EditMode全件 | 1559成功／0失敗／0Skip |
| PlayMode全件 | 59成功／0失敗／0Skip |
| 必須テスト照合 | 147／147 Passed |
| Data検証・専用Scene生成／検証 | 成功 |

これはClaudeによる実行記録として扱う。個別runの生ログを今回再取得して独立検証したものではない。記録対象13ファイルは、取得テキストの末尾改行を統一すると記載された内容ハッシュと一致した。ここからテスト結果の虚偽や、別内容をテストしたという指摘はしていない。

以下の改善はコードで確認できる。

- 守護中の同一HitId再入を外側へ統合し、別HitIdは主人公の通常解決へ流す処理。転送拒否時は通常Damageへ戻し、finallyで再入情報を解放する。[PlayerVitalsHolder](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Gameplay/Player/PlayerVitalsHolder.cs#L351)
- 仲間FeedbackをBinder／Registryで登録・解除し、既存Dispatcherへ接続する仕組み。[CompanionFeedbackBinder](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Presentation/Companion/CompanionFeedbackBinder.cs), [CombatFeedbackDispatcher](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Presentation/Diagnostics/CombatFeedbackDispatcher.cs#L101)
- 状態の実行ID付き所有権、移動の調停、AttackActive中の自動防御禁止、転送で生じたDown／StaggerをProtectで上書きしない分岐。
- 攻撃中断時に開始時SnapshotのCDを使う処理。
- P4専用Scene・Builder・Validatorと再生成テスト。
- FullName単位で必須テストの欠落・非Passedを検出し、重複結果で件数を水増ししない照合。[Phase4RequiredTestGate](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Editor/Bridge/Phase4RequiredTestGate.cs#L85)

ただし、これらの仕組みの存在と、合意した受入条件の充足は分ける。以下の指摘を解消する必要がある。

## 3. 要修正

### R2-01［P1］P4-07の入力と機能が合意仕様と異なる

**根拠**：[CompanionInvestigationController](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Gameplay/Companion/CompanionInvestigationController.cs#L145)はTickから自動的に最近傍地点を探して調査を開始する。新設の[CompanionOrderInput](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Infrastructure/Input/CompanionOrderInput.cs)は待機／追従の切替用であり、地点に対する既存Interact接続ではない。READMEも「立ち止まると調査」「Tで待機」と説明している。

**影響**：E／南ボタンによる1押下1依頼、選んだ地点の調査、受付拒否の理由通知という基本体験を試せない。詳細仕様§3.2で対象外とした「待て／ついてこい」がP4-07Bの成果になっている。

**修正**：詳細仕様§4に戻す。既存Interactを入力仲介で1回消費し、対象地点を決定して調査依頼へ渡す。自動調査と待機／追従指示はP4完了要件から外し、承認済みの探索入力を代替するものとして配線しない。既存クラスの再利用可否は実装側で判断してよい。

**受入**：E／南ボタン1押下で1地点・1依頼。未加入／距離外／戦闘中は理由付き拒否、予約なし。InteractからStep等を二重起動しない。関連：E02、E04〜E06、P01、P03。

### R2-02［P1］加入資格とDown／Away中の独立した探索表示がない

**根拠**：[CompanionActionRules](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Gameplay/Companion/CompanionActionRules.cs#L63)はDown／Away／Recovering等からの全行動を拒否する。調査は戦闘Actor自身をInvestigateへ遷移させて進む。今回の探索Controller／Builderに、加入資格を別に判定して戦闘Actorの状態を維持したまま探索表示へ引き渡す接続がない。

**影響**：合意した「加入済みならDown／Away／離脱CD中も探索できる」を満たさない。未加入と、加入済みで戦闘参加できない状態も区別できない。

**修正**：加入資格と戦闘上の生存・参加状態を分離する。必要な場合は戦闘Actorを持たない探索表示代理を使い、HP・Down・復帰時計・CDを初期化しない。Downを解除してInvestigateへ遷移させる修正は不可。

**受入**：Down／Away／離脱CD中に調査でき、探索のための回復が発生しない。自然復帰の時刻が調査中に来ても二重表示にならない。関連：E03、E20、P04。

### R2-03［P1］地点ID・依頼ID・完了記録の契約が未実装

**根拠**：[CompanionInvestigationPoint](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Gameplay/Companion/CompanionInvestigationPoint.cs#L27)のPointIdはGetInstanceID。完了は同コンポーネントのboolとカウンタで、OnInvestigatedは呼ばれるたびカウントを増やす。[完了処理](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Gameplay/Companion/CompanionInvestigationController.cs#L261)にはRequestId、Sceneの完了ID記録、成功・拒否・中断の型付き通知がない。

**影響**：同じ配置地点の再生成や重複ID検査、古い依頼からの完了排除、再入時の1回確定、P5へ渡す発見通知を保証できない。「調査済みのboolがある」だけでは詳細仕様§6の契約を満たさない。

**修正**：配置ごとに安定したPointIdを持ち、Scene内で重複検査する。RequestIdと終端状態を持つ依頼モデル、Scene内の完了記録、成功／拒否／中断と理由の通知を実装する。完了を確定してから外部通知し、同一依頼・同一地点の再入でも1回だけ成功する。アイテムや徳の新規付与は追加しない。

**受入**：同一RequestId再送、完了後再要求、Disable／Enable、古い完了、通知からの再入、地点ID重複を検査。関連：E06〜E08、E11、E12、E23。

### R2-04［P1］調査移動が壁で終わらず、進行中の条件も固定されない

**根拠**：[Advance](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Gameplay/Companion/CompanionInvestigationController.cs#L224)は到達していなければ移動要求を出してreturnする。到達前タイムアウトと障害物による受付拒否がない。主人公から地点までの距離は開始時の選定でしか見ず、進行中の継続範囲を再確認しない。またMoveSpeed・停止距離・InvestigateSeconds・CDを実行中にDataから読み直す。

**再現条件**：範囲内の地点を実壁の向こうへ置くと、到達できない間、探索が移動所有権を握り続ける。開始後に主人公が遠ざかっても、その理由で中断しない。調査途中でSOの所要時間を変えると現在の調査へ反映される。

**修正**：受付前の直線経路確認、受付後の移動タイムアウト、継続範囲外の中断を追加する。探索開始時に依頼で使う値をSnapshot化する。汎用経路探索の追加は不要。

**受入**：開始前の実壁は拒否、開始後の実壁は壁抜けせず中断して再依頼可能。継続範囲外で未完了のまま解放。SO編集は次の依頼から反映。関連：E08、E09、E21、P02。

### R2-05［P1］探索中の競合表が前回裁定と逆になっている

**根拠**：[CompanionActionRules](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Gameplay/Companion/CompanionActionRules.cs#L116)はInvestigate中のAutoAttack、AutoGuard、AutoEvadeをInterruptとして許可する。対応テスト名も `InvestigationTable_EveryCombatActionInterrupts` となっている。探索中断の検出は[TickInvestigation](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Gameplay/Companion/CompanionInvestigationController.cs#L117)での所有権不一致／活動判定が中心で、主人公への実命中前に探索を同期解放する接続がない。

**確定契約**：前回レビュー§5では、探索中の自動Follow／Chase／Attack／Guard／Evadeを拒否する。実際の戦闘開始や主人公・犬丸戦闘本体への命中は、同期的に探索を中断・解放してから元の処理へ進める。危険情報だけで別系統の戦闘開始を作らない。主人公への実命中は、解放後の同じReceiveHit呼び出し内で通常の守護資格を評価できる。

**修正**：表・状態／移動の所有権・探索中断入口・テストを一緒に直す。単に優先度を上げ下げする修正に留めない。実命中／戦闘開始の引渡し前に、旧探索完了が確定しない構造にする。

**受入**：危険候補だけでは探索継続。実命中では次Tickを待たず探索を解放し、その同じ呼び出し内で既存の命中解決へ進む。完了と戦闘開始が競合した場合も合意した優先順を維持。関連：E10、E14、P05、前回§5。

### R2-06［P1］守護が旧行動の中断前に命中を解決し、Protect所有権も解放されない

**根拠A：順序**：[PlayerVitalsHolder](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Gameplay/Player/PlayerVitalsHolder.cs#L518)はTryReceiveTransferredHitの後にNotifyTransferredを呼ぶ。[犬丸の受け口](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Gameplay/Companion/CompanionHitReceiver.cs#L138)は、この時点で残っている旧ガード／回避能力を評価する。[NotifyTransferred](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Gameplay/Companion/CompanionGuardianController.cs#L114)で初めてProtectへの割込みを要求する。前回§2.4の「受理確定→旧攻撃／防御を中断→被害解決」の順になっていない。

**影響A**：ガード中の守護で、解除すべき旧ガードが転送命中を防ぐ。被害結果の同期通知中にも、旧攻撃の中断がまだ済んでいない経路がある。

**根拠B：終了**：NotifyTransferredはGuardian所有権を取得して `out _` でハンドルを捨てる。TickGuardianはCDを減らすだけで、TryComplete／Releaseがない。[StateArbiter](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Gameplay/Companion/CompanionStateArbiter.cs#L253)はGuardianより弱い所有者の要求を拒否する。

**再現条件B**：Down／Staggerを生じない命中の肩代わりを成立させ、以後追加の強制状態遷移を起こさず通常Tickを進める。ProtectとGuardian所有権が残り、通常攻撃・防御・調査への復帰を阻む。現行コードではガードによる転送受理もこの経路に入る。CD満了だけでは解放されない。これは静的に追跡した経路であり、Unityで再現実行したとの主張ではない。

**修正**：

1. 受理予約、または受理確定直後・被害解決前の狭いコールバックで、成立する場合だけ旧攻撃／防御を同期中断する。
2. 拒否時の旧行動・CD・通知を変えない。同じHitIdを二度受理しない。
3. Protectを継続状態として使うなら、所有するハンドルと終了・Disable・中断の解放処理を持つ。一瞬の取引として扱うなら、その同期取引内で所有権を完結させる。持続演出を新規仕様として増やす必要はない。
4. 被害で生じたDown／Staggerは維持し、古い終了通知で上書きしない。

**受入**：ガード中の成立、Active中の成立／拒否、非Stagger成立後の通常行動再開、致死／Stagger、Disable、再入を実Controllerと実受け口で検査。既裁定の「直撃先行なら転送不成立→主人公通常Damage、転送先行なら犬丸直撃を重複排除」という順序別結果は維持する。関連：E17〜E19、P06、R03、前回§2.4。

### R2-07［P2］Guard→Evadeでガード能力が残る

**根拠**：[CompanionDefenseController](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Gameplay/Companion/CompanionDefenseController.cs#L164)はガード不能危険に対してEvade状態を開始し、_evade.TryStartしてreturnする。この経路で_guard.Releaseが呼ばれない。BeginDefenseActionも姿勢・所有権の取得だけを行う。

**再現条件**：ガード持続中、ガードが自然終了する前にガード不能危険へ切り替え、回避がReadyの状態でTickする。IsGuardingとIsEvadingが同時に残り得る。命中解決は状態名ではなく能力値を読むため、無敵時間外に旧ガードが働く余地がある。

**修正**：回避が成立する場合に旧ガード能力とその所有権を正しい順序で終了する。終了処理が新しい回避ハンドルを消さないようにする。単に状態をEvadeと表示するだけで完了としない。

**受入**：Guard中から実際の危険入力でEvadeを開始し、IsGuarding=false、IsEvading=true、回避終了後に正常復帰することを確認。関連：v1.0§8.3。

### R2-08［P1］活動ContextがないとRuntimeが許可側へ戻る

**根拠**：[CompanionActivityProvider](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Gameplay/Companion/CompanionActivityProvider.cs#L39)はCurrentがnullならFallbackへ進み、GameModeの供給もなければFreeRoam、ExplorationならEncounterのない自由探索として返す。コメントに「既存テストを直すコストを避けて残した」と明記されている。

**影響**：Context欠落・無効化・切替時に、仲間の行動と探索が許可され得る。開始時のScene Validatorや、配線済みRigのFallbackCount=0は、未注入Runtimeを停止させる代わりにならない。

**テストの不足**：[MissingContext_DisallowsActions](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Tests/EditMode/CompanionActivityContextTests.cs#L174)はContextを実際に作って、その内部参照が欠けたケースを検査する。Provider.Current自体がnullの経路を押さえていない。[FallbackのPlayModeテスト](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Tests/PlayMode/CompanionActivityFallbackPlayTests.cs#L163)も、実Scene読込ではなく手組み構成で、初期1フレーム後にカウンタをリセットする。

**修正**：前回N17／§6.1どおり、未注入Runtimeは安全側へ停止する。テスト共通Fixtureへ明示Fakeを注入し、実装の許可条件をテスト都合で緩めない。再配線時は古い状態や移動所有権の再利用も検査する。

**受入**：Provider.Current=null、ContextのDisable／Destroy、Scene切替・再配線で行動／移動／時計／命中送出／新規探索が契約どおり止まる。正常な明示Contextを注入したテストは動く。N17をこの入口に対する検査へ補強する。

### R2-09［P1］P4専用Sceneが探索を試せる開始経路になっていない

**根拠**：[Phase4CompanionTrialBuilder](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Editor/Phase4/Phase4CompanionTrialBuilder.cs#L163)は「PlayするとWave1」としてP3.5の自動開始を引き継ぐ。探索地点は[地点生成](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/Assets/_Project/Scripts/Editor/Phase4/Phase4CompanionTrialBuilder.cs#L183)で空GameObjectへPointを追加するだけ。Pointの見た目はOnDrawGizmosのみである。READMEの「青い輪」は通常のRuntime描画としては用意されていない。

**影響**：本来の「自由探索→Interactで調査→明示的な戦闘開始→4 Wave→Retryで自由探索」という通し受入ができない。別のFieldで自動調査を見ても、この引渡しの代替にはならない。Gizmosを切ったGameビューやビルドで地点を識別できず、成功・拒否・中断の短文表示もない。

**修正**：P3.5の既定自動開始は維持したまま、P4側だけ明示開始にする。起動・Retryは敵なしの自由探索。正常地点・壁のある地点・未加入の確認経路、入力、探索表示、短文UIをBuilderで復元する。戦闘開始は探索解放後に敵生成する。仮表示は方向マーカーと文字／形で識別できるものにし、Gizmosや色だけに依存しない。新しい完成素材は不要。

**受入**：Builder生成Sceneを通常のGame表示で操作し、調査から4 Wave・徳94・Retryまで通す。P3.5の開始動作は従来どおり。関連：P01、P05、P09、P10、R01。

### R2-10［P1］147件PassedをP4最終受入へ結び付ける対応が不足

**根拠**：[P4RequiredTests.json](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/P4RequiredTests.json)のrequirementIdにはE01〜E24・P01〜P11がなく、R系列もR01／R02／R06／R10の対応がない。[P4_受入条件対応表.md](https://github.com/JP2943/Momotaro/blob/0f4c96b240d406adb6a03288981c9738f32d4af2/P4_受入条件対応表.md)はロードマップv2.2と現行N系列を中心に説明し、詳細仕様の受入条件への対応を完了していない。

これはIDの表記だけの指摘ではない。R2-01〜R2-09の未実装動作が実際に残り、一方で自動調査の割込み・待機／追従・許可Fallbackなど、合意と異なる動作をテストが肯定している。N117も実Sceneの入力から完了まで通すテストではない。

**修正**：前回§4.4の最終条件に戻す。E01〜E24、P01〜P11、R01〜R10について「何をassertするか」と実テストのFullNameを対応付ける。同じ実テストで複数要求を満たしてよい。既存N系列も再利用してよいが、名前を付け替えるだけでは不足。

最終ゲートは、次を別々に検査する。

- 合意済み要求が一覧上で全て対応済みであること。
- 対応テストの実行結果がすべてPassedであること。
- 必須テストの欠落／Skip／Inconclusive／失敗を合格にしないこと。
- 実PrefabとBuilder生成Sceneの入力・物理・探索引渡し・再読込を、手組みRigだけで代替していないこと。

既存の全件実行・FullName照合の仕組みは活かす。受入記録の「機械検証は全て完了、人間判断のみ」の記載は、追記で本レビュー時点の未完了項目を示す。過去に実行した結果そのものは削除しない。

## 4. Claudeへの次作業指示

継続実装・Unityテストの既存許可を維持する。以下をコミット単位の確認点に分けて進め、各確認点で人間操作待ちにする必要はない。

| 順序 | 作業 | 終了条件 |
|---|---|---|
| 0 | 詳細仕様v1.0＋c8c0ddf追加裁定と現行実装の対応を正す | 本書R2-01〜R2-10を現行コードで再照合。P4-07A/Bの意味を地点依頼／Interact・表示接続へ戻す。要求ID一覧を確定し、未対応は未完了とする |
| 1 | 守護・防御・活動Contextの修正 | R2-06〜R2-08。非Staggerの守護後に通常行動へ復帰。受理確定後・被害前に旧行動中断。Guard→Evadeに旧ガードなし。未注入は停止 |
| 2 | P4-07A：探索の依頼・記録モデル | R2-01〜R2-05のロジック。加入、PointId、RequestId、理由通知、Snapshot、完了一度、中断、競合表の対象テスト合格 |
| 3 | P4-07B：入力・移動・表示 | Interact、表示代理、Down／Away探索、実壁、継続範囲、帰還、短文UIを接続 |
| 4 | P4-08R：専用Sceneと統合検証 | 自由探索から明示戦闘開始、4 Wave、Retry、Builder復元、実Scene／実PrefabのPlayMode検証 |
| 5 | 全件回帰と受入記録 | E／P／Rの未対応0、コンパイル・EditMode・PlayMode・Data／Scene Validator成功。対象コード識別・結果・保留台帳を更新 |

詳細仕様文書が実装側の作業環境にない場合は、実装開始前に2文書を配置し、CLAUDE.mdから参照できるようにする。ロードマップの見出しや今回の短い要約だけから詳細を再設計しない。

追加テスト名はClaudeが決めてよい。本書の受入条件を満たす実テストの完全名を、実装と同じ変更で必須一覧へ登録する。実行結果から成功した名前だけを集めて必須一覧を再定義しない。

## 5. 維持する確定事項・保留事項

- P3.5の受入済み戦闘、4 Wave、自動開始の既定値、主人公SEのStartup開始仕様を維持する。
- 徳のScene内保持とRetryリセット、本編持越しはP5、保存はP6という工程を維持する。
- 守護の直撃先行／転送先行による既裁定の結果を維持する。ここで全攻撃の一括判定方式へ拡張しない。
- P4のDown待ち5秒・HP50%復帰を維持し、Recoveringを使うためだけの改造をしない。
- 仲間スタミナ、能動「かばう」、猿・雉、本編アイテム／報酬・保存は今回追加しない。
- 元の保留台帳D01〜D08と、オーナーが了承した見た目・数値の後日調整は維持する。ただし、入力・依頼・終端処理・行動所有権・Runtime停止・受入対応の未実装を素材待ちへ移さない。
- 主人公スプライトの手描き修正を待つ必要はない。仮表示と短文UIで本書の修正と自動検証を進める。

GPT側の今回の判定は「P4基盤は前進、詳細仕様に対する修正・実装が残る」。修正後の自動受入が揃ってから、人間による短い試遊確認へ進める。


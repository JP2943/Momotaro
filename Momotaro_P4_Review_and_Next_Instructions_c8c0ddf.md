# 桃太郎プロジェクト P4 実装レビューと次工程指示

P4統合実装依頼書v1.0への追加指示／2026-09-10

- 確認対象：`phase/4-companions-foundation`
- 今回の先端：`c8c0ddf48f821b99da9ef8cd51dce566fe927e15`（コミット名：P4-07）
- 比較元：`aa80e05c0ec906ac9d2211ce90d6c446635ee1b1`
- 差分：1コミット、26ファイル。コード・テスト・文書の変更であり、Scene／Prefab／Dataアセットの変更はない。
- GPTの実施範囲：GitHub上の差分、関連コード、テストコード、CLAUDE.md、受入記録の静的レビュー。Unityテストの再実行はしていない。
- 本書は前回の統合実装依頼書v1.0と併用する。今回明記する仕様の補足・訂正・工程分割は本書を優先し、それ以外の詳細仕様・正式保留はv1.0を維持する。

## 1. 結論と現在地

**次は「検証の不足を補う → F05（活動Context） → F02a（移動の調停）」の順で進める。** これを次の確認単位 `P4-FIX-02A` とする。その後はF02b、F02c、P4-07A/B、P4-08Rへ進む。

今回のPushは、P4-07探索機能の完成ではなく、統合依頼の基盤修正の途中である。Claudeの残件申告とリポジトリの内容は一致している。

### 1.1 実装の評価

| 対象 | 確認結果 | 今回の判定 |
|---|---|---|
| F07：本番Asset汚染 | Buildの3引数が必須化され、一時AttackDataパスを渡す | 元の誤出力経路は修正済み。本番内容不変の受入検査を補う |
| F08：0件成功 | BridgeTestOutcome、error／failed、expected／minPassedを確認 | 0件・全Skip対策は妥当。必須名照合と中断判定は未完了 |
| F03：守護 | 所有者一致解除、受理可否の返却、try/finallyの再入ガードを確認 | 方針は採用。同一HitIdの再入とF02の中断契約は追加対応が必要 |
| F06：Snapshot | CompanionAttackPlanと攻撃中の確定値使用を確認 | 今回の修正範囲は妥当。威力は以前から凍結済みだった点を訂正 |
| F04：Feedback | 犬丸Resultsから既存Cue経路への接続を確認 | 基本配信は実装済み。犬丸自身の購読寿命と即時接続は追加対応 |
| F09：証跡 | P4_統合受入結果.mdを追加 | 形式は前進。テスト名・Skip内訳・検証対象内容の識別を補う |
| F01・F02・F05 | 専用Scene、単一Driver、活動Contextは未完成 | Claude申告どおり残作業 |
| P4-07A/B・P4-08R | 探索依頼・専用試遊環境は未実装 | 今回のコミット名を根拠に完了扱いにしない |

### 1.2 実行記録の確認

リポジトリ内の記録は次のとおり。これはClaudeが実行して記録した結果であり、GPTが再実行した値ではない。

| 実行 | 予定 | Pass | Fail | Skip | 所要 |
|---|---:|---:|---:|---:|---:|
| compile-status | — | — | 0 | — | 2.6秒 |
| EditMode全件 | 1421 | 1408 | 0 | 13 | 30.2秒 |
| PlayMode全件 | 51 | 51 | 0 | 0 | 16.6秒 |

この表だけでは、13件がどのテストで、本当に非必須だったかまでは確認できない。Data Validator未実行、P4専用Validator未作成、実Prefab全体の統合未実施も記録に明記されており、その区別は維持する。

「新規29本」は軽微な集計訂正が必要。コミット差分で追加された通常の `[Test]` メソッドは **28本**（Bridge11、Feedback5、Combat4、Guardian3、Builder3、PlayerGuardian2）。対応表にある既存の `TransferredHit_IsAcceptedOnlyOnce` は新規本数に含めない。実行時の別の数え方があるなら、実際のテスト名で内訳を示す。

## 2. 守護の同一HitId：仕様判断

### 2.1 新規受理できなければ転送不成立

Claudeが採用した **「同一HitIdを既に受理済みなら転送不成立 → 主人公の通常Damageへ戻す」** を、次の実装方針として採用する。

前回B.4の「既存の確定契約を維持」は、転送成功の定義まで確定しているように書いた点が不正確だった。既存コードが成功扱いしていたことと、それが意図された仕様として確定していることは別である。本節で成功・不成功を定義する。

- `CanTakeOver` は予備的な資格判定。
- 転送が成立するのは、そのHitIdを犬丸が新しく受理し、通常の命中解決を実行した場合。
- 重複受理排除、無効化、Down等で受け口が拒否した場合は不成立。
- 不成立なら、主人公の今回の通常Damage処理へ一度だけ進む。犬丸の追加被害、守護CD消費、Protect割込み、GuardianTransfer通知は発生させない。
- 「受理」はHPが実際に減ったことと同義ではない。既存の合法な防御・無敵によって通常の命中解決結果がGuard／EvadeやHP減少0になっても、単にHP減少0という理由で後から転送不成立へ変えない。
- ただし、行動上の資格は別途守る。Evade動作中の守護開始など、v1.0§8.3で禁止された開始をこの規則で許可しない。
- 元HitIdの保持、犬丸基準のHitPoint／方向再計算、主人公側のHitResultKindを増やさない方針は維持する。

### 2.2 到達順の保証範囲を明記する

同一HitIdに対して、犬丸の受理回数は最大1回。主人公と犬丸それぞれに向けられた命中の逐次解決を採用する。

以下の表は、防御なし・非致死・途中の状態変化なしという比較条件での期待結果である。

| 命中の到達順 | 犬丸 | 主人公 | 守護CD・成立通知 |
|---|---|---|---|
| 犬丸への直撃 → 主人公から転送 | 直撃を1回受理。転送は重複として拒否 | 通常Damageを1回受ける | なし |
| 主人公から転送 → 犬丸への直撃 | 転送を1回受理。後の直撃は重複として拒否 | 転送された命中のDamageなし | 1回 |
| 主人公だけに命中し、犬丸が転送可能 | 転送を1回受理 | Damageなし | 1回 |
| 既受理でないがDown／距離外／CD等で転送不可 | この転送からの受理なし | 通常Damage | なし |

したがって、**「どちらの順でも犬丸が二重被弾しない」は保証するが、「どちらの順でも主人公のHPが同じ」は保証しない。** 現行のEnemyAttackControllerはOverlapBoxの収集順に逐次配送しており、同じ判定で双方を巻き込む場合にもこの差が出得る。

P4試遊ではこの逐次解決を維持し、同時命中全体をまとめて再配分する仕組みや、敵の全命中経路の並べ替えは追加しない。この差は既知の試遊仕様として記録し、P8前の本編守護契約見直しで評価する。「到達順非依存」とだけ書かず、何が非依存なのかを限定する。

### 2.3 再入は「同じ命中の再送」と「別命中」に分ける

現在の `ReentrantHitDuringTransfer_DoesNotTransferTwice` は、内側を「別の命中」と説明しているが、ヘルパが常に `HitId.Single(1)` を生成している。そのため、実際には同一HitIdの再入で主人公HPを減らす挙動を固定している。

次のように修正する。

| 転送処理中に主人公へ再入したもの | 処理 |
|---|---|
| 外側と同一の有効HitId・同一主人公への再送 | 外側の処理に統合して無視。主人公の通常Damageへ落とさず、二重転送・二重通知も出さない |
| 異なるHitIdの新しい命中 | 守護の再入だけを拒否。主人公の既存の無敵・防御・Damage解決に従う |
| 外側が転送拒否で戻る場合 | 外側の主人公Damageを一度だけ実行。再入分を重ねない |

変更範囲は進行中の転送のHitIdと対象を識別する小さなガードに限定する。主人公の全命中追跡を作り直さない。外側の成功・拒否・例外のいずれでも、try/finallyで進行中情報を解放する。

既存テストは内外で異なるIDを渡すよう修正し、HP95という期待値を維持する。同一IDの再送テストを別に追加し、外側の転送が成功した場合は主人公HP100、犬丸受理1回、CD・通知1回を確認する。HitId無効値の扱いは既存のHitId契約に従い、無効値同士を一律に同じ攻撃とはみなさない。

### 2.4 F02cで完了させる転送取引

今回のbool返却だけでは、v1.0§8.5の「成立する場合だけ旧行動を止め、その後に犬丸へ命中を解決する」までは完成していない。

F02cでは、同じ同期処理の中で以下を保証する。

1. 活動・距離・状態・CD・同一HitIdの受理可否を確定する。
2. 受理が確定した場合だけ、旧攻撃／防御を中断する。
3. 元HitIdの命中を通常の犬丸受け口で解決する。
4. 結果のDown／Staggerを保ち、CDと通知を一度だけ確定する。

単なる「重複していなければ先に攻撃を止める」という二度判定では、間の同期コールバックで条件が変わり得る。必要なら受理予約トークン、または受け口が受理確定直後・被害解決前に呼ぶ狭いコールバックを使う。方式はClaudeが選べるが、「拒否で旧行動を止めない」「受理した攻撃を取り落とさない」「受理記録は1回だけ」をテストで固定する。

## 3. 今回見つかった追加修正

### 3.1 F08：minPassedは中断・必須実行の証明にならない

成功件数の下限だけでは、次のケースを検出できない。

- 必須テストAがSkipし、別のテストBが増えたため成功総数が同じ。
- フィルタ実行がminPassed到達後に中断した。
- フィルタ実行に成功とInconclusive等が混在し、Pass／Fail／Skip以外の結果が集計から落ちた。
- 全体結果の異常と、葉テストの成功件数が食い違った。

現行 `EditorBridgeTestRun.RunFinished` はPassCount／FailCount／SkipCountを渡し、Collectも失敗した葉だけを拾う。nullの全体結果への明示処理もない。今回の追加対応では、全体の終端状態、葉のFullName・結果・理由を保存し、欠落・中断・不明な状態を成功へ丸めない。

Unityから得られるAPIの詳細は、プロジェクトが参照するTest Frameworkの型で確認して実装する。特定のプロパティ名を推測で使わない。

`expected`を全件実行だけで使う今回の修正は維持する。`minPassed`も件数減少を検出する補助として維持する。ただしCLAUDE.mdと生成READMEの「絞り込み実行の中断検出はminPassedで行う」は、「minPassedは不足の一部を検出する補助。完走は終端結果と名前付き必須テスト結果で判定する」へ訂正する。

### 3.2 F04：犬丸自身の有効化・無効化を検査する

現行 `CompanionOnDisable_Unsubscribes` が呼んでいるのは犬丸のOnDisableではなく、DispatcherのOnDisableである。Dispatcher停止時の解除テストとしては有用だが、犬丸自身のDisableを検査したことにはならない。

また、犬丸の追加経路も `FindObjectsByType` による周期Rescanであり、以下の不足がある。

- 生成・有効化直後から次回Rescanまで未購読になる可能性。
- 犬丸自身の無効化時に、その瞬間の解除を保証しない。
- 破棄済みのUnity Objectをnull判定で飛ばすため、保持済みの旧チャネルから解除したことまでは保証しない。
- 現在のCLAUDE.mdにある「Find*を使わない」という新規実装の規約とも整合しない。

P4の仲間経路は、Presentation側の小さな登録Binder等で、明示参照から既存Dispatcherへ登録・解除する形へ補強する。GameplayからPresentationを参照させない。保持するチャネルと登録所有者を使い、生成・Bind順序が違っても一度だけ接続し、Disable／Destroy／Scene離脱で一度だけ解除する。

既存P3.5の敵・ダミー等の購読方式を今回全面改修する必要はない。P4の新規仲間経路を直し、既存の単一FeedbackPresenterへ流す。

必要な検査：

- 犬丸自身をDisableして旧Resultsへ通知しても演出されない。
- Enable直後、待ち時間なしの最初の命中が届く。
- 破棄前に保持したResultsへ通知しても新Scene／現Dispatcherへ流れない。
- Dispatcher自体の停止も従来どおり解除できる。
- 再Enable／再Bindで重複しない。

現行テスト名は「Dispatcher停止時の仲間購読解除」と分かる名称へ変更してよい。その際は必須名一覧と受入記録も同時に更新する。

### 3.3 F07：参照検査と内容不変検査を区別する

現在の `Build_DoesNotReferenceProductionAssets` は、一時Prefabの参照先を調べている。元の誤出力防止はコード上直っているが、本番アセットのファイル内容・GUIDが前後で変わらないことの直接検査ではない。

既存の一時生成テストに、本番Prefab／CompanionData／AttackDataとmetaの前後比較を追加する。本番が存在しない場合は勝手に生成されていないことを確認する。新しい本番アセットをテストの都合で作らない。共有の固定一時パスへ既存ファイルがある場合は上書き・削除せず、テスト所有の一時領域を使う。

### 3.4 F09：証跡の粒度

記録001の「dirty＋変更ファイル名一覧」だけでは、検証したファイル内容を一意に特定できない。あとから同じファイルが変更されても一覧が同じだからである。

次回から次を記録する。

- 検証開始時のHEAD、コード／テスト／asmdef／関連Asset等の内容ハッシュ一覧、または相当するtree／差分の識別情報。
- 一連の検証中に対象が変わっていないこと。途中変更した場合は影響する検証をやり直し、対応する結果を区別する。
- runId、開始・終了時刻、mode、filter、全体結果、葉テスト結果への参照。
- Skip13件の具体的なFullNameと理由。「以前と同数」だけで非必須と判定しない。
- 大量ログを本文へ埋め込む必要はないが、上書きされない結果XML／JSON等を残す。

PCマウント障害時は、許可済みの `device_stage_files` で取得した実ファイルからハッシュを作ってもよい。端末側の実体を確認せず、クラウド側の予定ファイルだけを検証対象の証拠にしない。

過去の記録001に、当時取得していなかったハッシュや必須テスト合格を後付けしない。「今回のPush先はc8c0ddf、記録001の検証対象は当時のdirty状態で内容ハッシュ未記録」と補足し、次の検証から対応を明確にする。

## 4. 必須テストを機械で照合する

### 4.1 正本と判定規則

リポジトリ内に `P4RequiredTests.json` 相当の機械可読な正本を1つ置く。Markdownだけの対応表や、テストクラス名の正規表現だけでは代替しない。

各項目は少なくとも、要求ID（R03等）、完了を要求する工程、EditMode／PlayMode、**実行結果に現れる正確なFullName**を持つ。Parameterized Testを使う場合は、必要な引数ケースごとの名前またはそれを欠落なく展開できる明示規則を持たせる。

判定は以下に固定する。

1. 選択した工程で期限を迎える必須テストがすべて存在する。
2. 各テストが対象の実行結果に1回以上の正式な結果として存在し、すべてPassedである。
3. Skip／Ignored／Inconclusive／未実行／不明は必須テストの合格にしない。
4. 同じrunId内の同じ葉の重複結果など、不整合を成功件数の水増しに使わない。
5. 必須以外にもFailedがあれば全体を合格にしない。全体が中断・異常・欠落なら葉が一部Passedでも合格にしない。
6. 非必須SkipはFullNameと理由を明記した許容一覧と照合する。新しいSkipを件数だけで自動許容しない。
7. 通常の開発用フィルタ実行と、工程の受入実行を区別する。フィルタ実行のokだけで工程を合格にしない。
8. 必須テスト名の変更は、テストと一覧を同じ変更で更新し、要求IDの対応を残す。結果から一覧を自動生成して消えたテストを見逃す運用にしない。

ブリッジは一般的な実行結果の取得と異常判定、P4受入側はP4固有の必須一覧の照合を担当してよい。両方を通過した結果を一つの受入出力に残し、責務を分けたことで検査が抜けないようにする。

### 4.2 現時点から必須にする既存テスト名

以下の28本を今回の修正回帰として名前で登録する。いずれも今回のPushで追加されたテストである。名前変更時は前節の規則に従う。

```text
Momotaro.Tests.EditMode.BridgeTestOutcomeTests.AllPassed_IsOk
Momotaro.Tests.EditMode.BridgeTestOutcomeTests.NoTestsMatched_IsError_NotOk
Momotaro.Tests.EditMode.BridgeTestOutcomeTests.AllSkipped_IsFailed_NotOk
Momotaro.Tests.EditMode.BridgeTestOutcomeTests.AnyFailure_IsFailed
Momotaro.Tests.EditMode.BridgeTestOutcomeTests.Incomplete_IsError_EvenWithoutFailures
Momotaro.Tests.EditMode.BridgeTestOutcomeTests.Complete_WithExpectedCount_IsOk
Momotaro.Tests.EditMode.BridgeTestOutcomeTests.Incomplete_WithFailures_ReportsFailedAndSaysSoInReason
Momotaro.Tests.EditMode.BridgeTestOutcomeTests.BelowMinPassed_IsFailed
Momotaro.Tests.EditMode.BridgeTestOutcomeTests.AtMinPassed_IsOk
Momotaro.Tests.EditMode.BridgeTestOutcomeTests.MinPassedZero_MeansUnspecified
Momotaro.Tests.EditMode.BridgeTestOutcomeTests.ExpectedZero_MeansUnknown_DoesNotBlock
Momotaro.Tests.EditMode.CombatFeedbackDispatcherTests.Rescan_SubscribesToCompanionResults
Momotaro.Tests.EditMode.CombatFeedbackDispatcherTests.CompanionHitResult_PublishesFeedbackCue
Momotaro.Tests.EditMode.CombatFeedbackDispatcherTests.RepeatedRescan_NoDuplicateCompanionNotification
Momotaro.Tests.EditMode.CombatFeedbackDispatcherTests.CompanionOnDisable_Unsubscribes
Momotaro.Tests.EditMode.CombatFeedbackDispatcherTests.CompanionDestroyed_Rescan_SafeUnsubscribe
Momotaro.Tests.EditMode.CompanionCombatControllerTests.MidAttackDataEdit_DoesNotChangeReach
Momotaro.Tests.EditMode.CompanionCombatControllerTests.MidAttackCooldownEdit_DoesNotChangeCooldownAtFinish
Momotaro.Tests.EditMode.CompanionCombatControllerTests.FinishedAttack_ClearsThePlan
Momotaro.Tests.EditMode.CompanionCombatControllerTests.CancelledAttack_ClearsThePlan
Momotaro.Tests.EditMode.CompanionGuardianControllerTests.DisablingAnOlderGuardian_KeepsTheCurrentRegistration
Momotaro.Tests.EditMode.CompanionGuardianControllerTests.DisablingTheCurrentGuardian_ClearsTheRegistration
Momotaro.Tests.EditMode.CompanionGuardianControllerTests.DroppedTransfer_ReportsNotAccepted
Momotaro.Tests.EditMode.Phase4CompanionBuilderTests.Build_WritesAttackDataToGivenPath
Momotaro.Tests.EditMode.Phase4CompanionBuilderTests.Build_DoesNotReferenceProductionAssets
Momotaro.Tests.EditMode.Phase4CompanionBuilderTests.Build_RequiresAllOutputPathsExplicitly
Momotaro.Tests.EditMode.PlayerGuardianTransferTests.GuardianThatDropsTheHit_FallsBackToPlayerDamage
Momotaro.Tests.EditMode.PlayerGuardianTransferTests.ReentrantHitDuringTransfer_DoesNotTransferTwice
```

加えて、既存の次の1本もR04回帰として維持する。

```text
Momotaro.Tests.EditMode.CompanionGuardianControllerTests.TransferredHit_IsAcceptedOnlyOnce
```

`BridgeTestOutcomeTests.AllPassed_IsOk` は実際には「成功あり・失敗なし・非必須Skipあり」の例を含む。名前を改善する場合も同時更新する。上記Builderテストは仮素材未ImportでSkipし得るため、今回の受入環境では素材をImportして通す。必須から外して済ませない。

### 4.3 次の工程で追加する必須ケース

以下は**新設するテスト名の指定**であり、現コミットに存在するという主張ではない。実装上の事情で名称を変える場合は、要求IDとの対応と機械一覧を同時に更新する。

表のEditModeクラスの名前空間は `Momotaro.Tests.EditMode`、PlayModeは `Momotaro.Tests.PlayMode` を前提とする。

| 要求ID | mode／クラス.メソッド | 合格条件 |
|---|---|---|
| N01 | EditMode／PlayerGuardianTransferTests.SameHitReentry_DoesNotDamagePlayerTwice | 同じ有効HitIdの再入で追加被害・成立通知なし |
| N02 | EditMode／GuardianHitOrderTests.DirectThenTransfer_DamagesPlayerWithoutGuardianCooldown | 実Player受け口＋実犬丸受け口で§2.2の直撃先行を固定 |
| N03 | EditMode／GuardianHitOrderTests.TransferThenDirect_DoesNotDamageCompanionTwice | 同じ構成で転送先行を固定。双方のHPとCD・通知を検査 |
| N04 | EditMode／Phase4RequiredTestGateTests.MissingRequiredTest_FailsGate | 必須名の消失を総数一致でも検出 |
| N05 | EditMode／Phase4RequiredTestGateTests.RequiredSkip_FailsGateDespiteOtherPasses | 別テストの成功が増えても必須Skipは不合格 |
| N06 | EditMode／Phase4RequiredTestGateTests.UnlistedSkip_FailsGate | 新規の説明されていないSkipを検出 |
| N07 | EditMode／BridgeRunCompletionTests.CancelledFilteredRun_IsNotOkAfterMinPassed | minPassed到達後の中断も成功にしない |
| N08 | EditMode／BridgeRunCompletionTests.InconclusiveOrMissingResult_IsNotOk | Pass混在の不明結果・全体結果欠落も成功にしない。必要なら引数ケースへ分割 |
| N09 | EditMode／CompanionFeedbackLifecycleTests.CompanionDisable_RemovesSubscription | 犬丸自身をDisableし、旧チャネル通知が届かない |
| N10 | EditMode／CompanionFeedbackLifecycleTests.CompanionDestroy_RemovesOldChannelSubscription | 破棄前に保持したチャネルから通知しても届かない |
| N11 | EditMode／Phase4CompanionBuilderTests.Build_LeavesProductionAssetBytesUnchanged | 本番Asset＋metaの内容・不在状態が不変 |
| N12 | PlayMode／CompanionFeedbackLifecyclePlayTests.EnabledCompanion_FirstHitReachesFeedbackOnce | 登録直後の実命中が待ち時間なしで一度だけ届く |
| N13 | PlayMode／CompanionActivityGatePlayTests.PauseAtTimeScaleOne_StopsBodyAndClocks | 追従中・攻撃中・Down待ち・防御CDで位置と時計が停止 |
| N14 | PlayMode／CompanionActivityGatePlayTests.Resume_KeepsSwingAndDoesNotHitAgain | 同じ段・HitId・既命中集合を保持し同一対象への再被害なし |
| N15 | PlayMode／CompanionActivityGatePlayTests.Dialogue_CancelsAttackAndDoesNotResumeOldActive | timeScaleに頼らず中断。復帰時に古いActiveを再開しない |
| N16 | EditMode／CompanionActivityContextTests.IntermissionAndPendingEncounter_DisallowInvestigation | GameModeがExplorationでもEncounter継続中は探索不可 |
| N17 | EditMode／CompanionActivityContextTests.MissingContext_DisallowsActions | 未注入を黙って許可しない |
| N18 | EditMode／CompanionMotorArbitrationTests.OnlyCurrentOwnerCanWriteMovement | Follow／Combatの競合と古い所有者の書込みを排除 |

N02／N03は命中配送順のロジック検証であり、実Collider経路のP4-08R統合テストを代替しない。N07／N08は収集・判定境界へ結果を注入して決定的に再現し、UIで偶然の中断を狙う不安定なテストにしない。

### 4.4 将来のR01〜R10を未実装のまま合格にしない

各工程の必須集合は明確に分ける。

| 工程 | その時点の受入要求 |
|---|---|
| P4-FIX-02A | §4.2の既存回帰＋N01〜N18。修正後の既存P3.5/P4テストも回帰確認 |
| F02b | 上記を継承し、状態の単一所有・正常終了・古い終了通知排除を追加 |
| F02c | 上記を継承し、v1.0§8.3の全許可表、成立時だけの守護中断、Down／Stagger優先を追加 |
| P4-07A/B | 上記を継承し、v1.0 E01〜E24・関連Pテストの実装段階に応じた必須名を追加 |
| P4-08R／最終 | v1.0 E01〜E24・P01〜P11・R01〜R10のすべてを名前付き実テストへ対応させ、未対応0で合格 |

F02b以降のテストは、各工程の実装開始時に実際の名前を機械一覧へ追加し、終了時には欠落なく通す。まだ開始していない工程の項目を今回Skipにして合格したようには見せない。最終受入ではR01〜R10全項目の対応を必須とする。

## 5. 探索中の行動競合：v1.0§8.3への追加

探索のMoving／Investigating／Returning中は、健康な犬丸も通常AIの行動所有権を探索へ渡している。Down／Awayの犬丸は戦闘状態を維持したまま別表示が動く。これらを分けたうえで、以下を追加する。

| 要求・出来事 | Moving／Investigating中 | 成功済みReturning中 |
|---|---|---|
| 自動Follow／Chase | 戦闘本体の移動・向き・Warp指示を拒否 | 同左。引き渡し完了後に再判断 |
| 自動攻撃開始 | 拒否・予約しない | 拒否。引き渡し後に再判断 |
| 自動Guard／Evade開始 | 拒否。探索表示と並行起動しない | 同左 |
| 守護要求 | 探索所有中はそのまま転送開始しない。実命中の割込み手順は下記 | 表示を即撤収して通常の守護可否を再判断 |
| 明示的な戦闘開始 | 依頼を中断・表示所有権解除してから敵生成／攻撃許可 | 成功記録を保持して表示撤収 |
| 主人公への実命中 | 同期的に探索中断→所有権解除→元の命中解決を続ける | 成功を維持して表示撤収→元の命中解決 |
| 犬丸戦闘本体への実命中 | 同期的に探索中断→元の命中解決。代理が被害を吸わない | 表示撤収→元の命中解決 |
| 防御候補を作る危険情報だけ | 自動防御を起動しない。戦闘開始を勝手に別系統で判定しない | 同左 |
| Pause | 所有権と依頼を保持して凍結 | 帰還表示の進行を凍結 |
| 会話／イベント／Scene離脱 | 中断・参照と表示を解放 | 成功記録を戻さず表示解放 |
| 加入済み犬丸の自然復帰タイマー満了 | 戦闘値だけ通常どおり復帰。探索は続行し、二重表示しない | 同左 |

主人公への実命中で探索を中断した後、その**同じReceiveHit呼び出し内**で通常の順序を続ける。無敵／Step／JG／Guard等で解決されなければ、探索所有権を返した後の犬丸の資格で守護を評価してよい。単なる自動Guard要求を受けただけでは探索を解除しない。

探索中断は表示・所有権の解放であり、主人公や犬丸のHP・CDのリセットではない。Pause／会話中に時計を止める規則と、平常探索中の自然復帰を止めない規則を混同しない。

この表に加え、通常状態についてはv1.0§8.3を維持する。AttackActive中の自動防御不可、守護成立時の攻撃中断可、Evade全動作中の守護不可を変更しない。

## 6. F05・F02の工程分割

### 6.1 F05：活動Contextを先に作る

ゲーム全体のGameModeと、戦闘セッション継続、仲間の行動所有権を読み取る小さな契約を設ける。GameModeがExplorationだから非戦闘とは判定しない。

必要な軸：

- 新規のGameplay行動を許可するか。
- 時計を進めるか／Pauseで凍結するか。
- 古い行動を中断・破棄すべきか。
- Encounterが開始要求済み・継続中か。
- 主人公の生存・Interact開始資格。
- 探索／イベントなどローカルな所有権。

`CombatSessionController.StateChanged`等の既存正本から供給する。Wave幕間と開始待ちEncounterも戦闘扱い。自由探索区画は「Encounter未開始」と明示する。新しい全敵探索で戦闘判定を複製しない。

未配線は安全側に停止し、テストは明示Fakeを注入する。今回の受入記録にある「各UpdateはIsGameplayActiveで止まる」は少し広い。現行FollowのUpdateにはそのゲート自体がないため、MotorだけでなくFollow・向き・Warp・TargetTrackerも対象にする。

**停止条件は公開Tickや命中送出入口にも適用する。** Updateだけreturnしても、別ControllerからTick／TryApplyHitが呼ばれれば抜け道になる。Pause中にdt=0でActiveのPollHitboxだけ動くことも防ぐ。

### 6.2 F02a：Motorと向きの書き手を一本化する

次の確認単位はここまで。

- Follow／Combatは移動意図を供給する。
- 最終的にMove／Stop／Warp・論理的な向きを選ぶ場所を1つにする。
- Motorはその結果だけを物理適用し、FixedUpdateでも活動停止を尊重する。
- 状態通知経由の緊急停止も同じ経路へ集める。通常の移動決定と強制停止が競合したら停止を優先する。
- 古い所有者によるStop／Warpや解除が、新しい所有者を上書きしない。
- 既存の速度・追従距離・Warp閾値等のData値は変更しない。
- Time.timeScale=1のPauseでもRigidbody速度をゼロにし、復帰後は古い移動命令で飛び出さない。

F02aでまだ状態要求を完全統合していない場合は、その残件を明示する。「Motorが一本になったから全行動の排他も完了」とは扱わない。

### 6.3 F02b：状態要求と正常終了を一本化する

- 通常の状態要求の受付先を1つにする。
- 開始／外部割込み／正常終了／命中由来強制遷移を区別する。
- 正常終了は所有者・実行IDが一致する場合だけ受理する。
- AttackActive→Recovery、Stagger終了→再判断等の正常な復帰を通す。
- HP0・Stagger等は同期的な強制遷移として処理し、次のUpdateまで古い判定を残さない。
- ActorのForceHitStateを無条件に各Controllerが書く構造から、調停役への同期通知へ集約する。
- 既存モデルを再利用し、同じモデルを1フレームに複数回Tickしない。

### 6.4 F02c：割込み表と守護取引を実装する

- v1.0§8.3と本書§5の全組合せを明示的な規則として実装する。
- §2.4の守護受理・旧行動中断・被害解決・通知の順序を守る。
- 攻撃キャンセル時に開始時Snapshotの通常CDを開始する。現在のCancelAttackはCDを開始していないため、この工程で変更する。
- Protect通知でDown／Staggerを上書きせず、旧攻撃Activeを再開しない。
- Guard方向をFollowが上書きしない。
- Evadeは無敵時間だけでなく動作全体の所有権を保持する。
- 新しい防御倍率・守護倍率・硬直秒数・移動回避は追加しない。

ここまで通れば、P4-07A/Bの探索機能を安全に接続できる。

## 7. Claudeへ渡す次の実行指示

以下を次の作業として実施してください。

1. 最新ブランチとCLAUDE.mdを読み、c8c0ddf以降の差分を確認する。
2. 本書の仕様判断をリポジトリのP4仕様・タスク表へ反映する。前回文書の不明確だった点をコードコメントだけで済ませない。
3. §2.3の再入テスト・処理、§3のFeedback購読と検証不足、§4の必須テスト一覧・受入判定を補う。
4. F05を実装し、F02aまで進める。
5. `compile-status`後、必要なEditMode／PlayMode・機械可読の必須判定を実行する。途中は影響範囲を絞り、確認単位の終了時に既存回帰を通す。
6. `P4_統合受入結果.md`へ「記録002：P4-FIX-02A」を追記する。記録001を書き換えて今回の結果と混ぜない。
7. 完了後は、同じ契約に沿ってF02b→F02c→P4-07A/B→P4-08Rへ進む。各段階を別の確認単位として記録する。

既に与えられているP4の連続実装・PlayMode常時許可の範囲で継続する。小工程ごとに同じ許可を取り直して停止する必要はない。未保存Sceneの損失や新しい仕様判断が必要な場合は、その具体的な問題だけを伝える。Commit／Pushは現行CLAUDE.mdどおりオーナーの担当とする。

PCマウント障害時のstage／commit経路への切替は、今回追記された手順を利用してよい。実ファイルの更新時刻照合を維持し、`result.json`は送ったcommandのid・mode・時刻と照合して古い結果を読まない。

新しい `.meta` の作成規則を、既存アセットのGUIDを再計算する指示と解釈しない。既存GUIDは保持し、新規GUIDの衝突を確認する。

### 今回の変更対象

- Guardianの受理・再入境界と関連テスト。
- Bridgeの全体／葉結果の取得・必須判定・証跡。
- 犬丸Feedbackの登録／解除と実寿命テスト。
- Builderテストの本番内容不変確認。
- 活動Context、Follow／Combat／Defense／Guardian／HitReceiver／TargetTrackerの停止・駆動接続。
- Motorと論理方向の調停、必要なPrefab生成配線・テスト。
- 仕様、CLAUDE.mdの該当補足、受入記録。

### 維持する範囲

- P3.5の主人公戦闘、攻撃SEタイミング、4 Wave、勝敗・Retry。
- 徳94とRetryでのリセット。探索による徳／アイテム付与は追加しない。
- P4のDown全体で復帰待ちを表す方式。Recoveringを使うためだけの段階追加はしない。
- 現行の時間経過復帰5秒・50%というP4限定Data。
- 主人公の正式素材修正、猿・雉、本編保存、能動かばう、仲間スタミナ。
- 本書が限定して変更する転送拒否・再入以外の主人公命中解決。

## 8. 次の完了報告に必要なもの

- P4-FIX-02Aの実装範囲と、F02b/c・P4-07A/B等の残件。
- 守護の直撃先行／転送先行／同一ID再入／別ID再入の4ケースの結果。
- 必須テスト一覧の実パス、実行コマンド、必須Passed／Skipped／Missing件数。
- Skip全件のFullName・理由と許容一覧との対応。
- 検証対象コードの識別情報、実行日時、Unity版、実行結果ファイル。
- F05のtimeScale=1停止・再開と、実Feedback最初の命中の結果。
- Data／Scene Validatorについて、実施したものとまだ未実施のもの。
- リポジトリ内のREADME／仕様更新と、追加した既知の試遊制約。

この段階で人間へ完成スプライトや探索の体感確認は依頼しない。最終の短い人間試遊は、P4-07とP4-08Rの統合後に行う。

## 9. レビュー根拠

以下はすべて今回の確認コミットに固定した一次資料。

- [今回のコミット](https://github.com/JP2943/Momotaro/commit/c8c0ddf48f821b99da9ef8cd51dce566fe927e15)
- [受入記録](https://github.com/JP2943/Momotaro/blob/c8c0ddf48f821b99da9ef8cd51dce566fe927e15/P4_統合受入結果.md)
- [PlayerVitalsHolder](https://github.com/JP2943/Momotaro/blob/c8c0ddf48f821b99da9ef8cd51dce566fe927e15/Assets/_Project/Scripts/Gameplay/Player/PlayerVitalsHolder.cs)
- [PlayerGuardianTransferTests](https://github.com/JP2943/Momotaro/blob/c8c0ddf48f821b99da9ef8cd51dce566fe927e15/Assets/_Project/Tests/EditMode/PlayerGuardianTransferTests.cs)
- [CompanionHitReceiver](https://github.com/JP2943/Momotaro/blob/c8c0ddf48f821b99da9ef8cd51dce566fe927e15/Assets/_Project/Scripts/Gameplay/Companion/CompanionHitReceiver.cs)
- [EnemyAttackController](https://github.com/JP2943/Momotaro/blob/c8c0ddf48f821b99da9ef8cd51dce566fe927e15/Assets/_Project/Scripts/Gameplay/Enemy/Combat/EnemyAttackController.cs)
- [EditorBridgeTestRun](https://github.com/JP2943/Momotaro/blob/c8c0ddf48f821b99da9ef8cd51dce566fe927e15/Assets/_Project/Scripts/Editor/Bridge/EditorBridgeTestRun.cs)
- [BridgeTestOutcome](https://github.com/JP2943/Momotaro/blob/c8c0ddf48f821b99da9ef8cd51dce566fe927e15/Assets/_Project/Scripts/Editor/Bridge/BridgeTestOutcome.cs)
- [CombatFeedbackDispatcher](https://github.com/JP2943/Momotaro/blob/c8c0ddf48f821b99da9ef8cd51dce566fe927e15/Assets/_Project/Scripts/Presentation/Diagnostics/CombatFeedbackDispatcher.cs)
- [CombatFeedbackDispatcherTests](https://github.com/JP2943/Momotaro/blob/c8c0ddf48f821b99da9ef8cd51dce566fe927e15/Assets/_Project/Tests/EditMode/CombatFeedbackDispatcherTests.cs)
- [CompanionCombatController](https://github.com/JP2943/Momotaro/blob/c8c0ddf48f821b99da9ef8cd51dce566fe927e15/Assets/_Project/Scripts/Gameplay/Companion/CompanionCombatController.cs)
- [Phase4CompanionBuilderTests](https://github.com/JP2943/Momotaro/blob/c8c0ddf48f821b99da9ef8cd51dce566fe927e15/Assets/_Project/Tests/EditMode/Phase4CompanionBuilderTests.cs)
- [CLAUDE.md](https://github.com/JP2943/Momotaro/blob/c8c0ddf48f821b99da9ef8cd51dce566fe927e15/CLAUDE.md)


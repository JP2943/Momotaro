# 桃太郎プロジェクト — Claude 作業ルール

このファイルは Claude が作業開始時に自動で読む。会話が長くなって前半が要約されても、ここに書いた前提は失われない。

## 役割分担

| 担当 | 範囲 |
|---|---|
| 猫噛ねず四郎（オーナー） | 判断・最終受入・native の git commit・数値バランスの決定 |
| GPT | 仕様策定・レビュー |
| Claude | 実装・テスト・静的検証 |

数値（ダメージ・射程・秒数・ヘイト）は Data（ScriptableObject）が正本で、コードへ直書きしない。
バランス調整は**オーナーの判断領域**なので、Claude は分析と選択肢を出して判断を仰ぐ。実装上の不整合
（例：判定の届く距離 < 攻撃開始距離）はバランスではなく欠陥なので、Claude が直す。

## 仕様正本（P4）

P4 の受入判定に使う仕様は次の 2 文書で、どちらもプロジェクト直下にある。**ロードマップ v2.2 の見出しや会話の要約から
詳細を再設計しない**（実際にやって、P4-07 を丸ごと作り直す羽目になった）。

- `Momotaro_P4_Claude_Implementation_Request_v1.0.md` — 合意済み詳細仕様。§3 スコープ、§4 体験・入力・受付条件、
  §5 加入資格と表示代理、§6 依頼の進行と原子性、§7 戦闘判定と停止、§8 調停（8.3 競合表、8.5 守護取引）、
  §10 Data・ID、§13 P4-08R、§14 自動受入条件（E01〜E24・P01〜P11）、C 追加受入（R01〜R10）、D 保留台帳（D01〜D08）
- `Momotaro_P4_Review_and_Next_Instructions_c8c0ddf.md` — 追加裁定。§2.4 守護取引の順序、§4.4 最終ゲート、
  **§5 探索中の競合表（探索中の自動 Follow／Chase／Attack／Guard／Evade は拒否。実命中・戦闘開始が同期的に探索を解放）**、§6 工程分割
- `Momotaro_P4_Review_0f4c96b.md` — 上記に対する現行実装の差分指摘（R2-01〜R2-10）と作業順序

受入要求 E／P／R と実テストの対応は `P4RequiredTests.json` の `requirements` と各テストの `requirementId`／`requirementIds` で持ち、
`verify-required-tests` が**対応テストの無い要求**を不合格にする。

## 設計の約束（Phase 1〜3.5 で確定）

- 層構造 `Core ← Data ← Gameplay ← Presentation`（+ Infrastructure / Editor / Tests）を asmdef で強制
- Gameplay は Animator・Canvas・Scene API・Camera を直接触らない
- static な万能マネージャを作らない。通知は型付きチャネル＋インタフェースで、購読は OnEnable/OnDisable 対称
- **時間は外部注入する。** すべての駆動系は `Tick(float deltaTime)` を公開し、`Update()` は
  `Tick(Time.deltaTime)` を呼ぶだけにする。EditMode テストが Editor の描画間隔に左右されないため
- SO 原本は実行時に書き換えない。開始時に不変 Snapshot へ複製する
- `Find*` を使わない。レジストリと使い回しバッファで集める
- 未使用機能の実処理は先回りして作らない（語彙・契約だけ先に置くのは可）

## テストの書き方（この方針は事故から学んだもの）

**部品の単体テストが全部緑でも、繋ぎ目が切れていれば実機は動かない。** 実際に 2 回起きた。

1. 敵が索敵レジストリへ未登録 → 仲間の候補が常に 0 件。仲間側テストは敵役を手で登録していたため検出できず
2. 判定 Box の到達距離 < 攻撃開始距離 → 常に空振り。テストは `TryApplyHit` を直接呼び、物理判定を通していなかった

したがって、**外部レジストリ・物理判定・実アセットを経由する機能は、本物同士を繋いだテストを必ず 1 本置く**。

- レジストリ経由 → 本物のコンポーネントを生成して有効化しただけで繋がることを固定する
- 物理判定 → 実際に Collider を置き、`Update`／`Tick` 経由で判定を出させる（ヘルパを直接呼ばない）
- 実アセット → 出荷される SO・Prefab の配線を `AssetDatabase` で読んで検査する

## Editor 常駐ブリッジ

Claude が Unity Editor を直接動かすための仕組み。`Assets/_Project/Scripts/Editor/Bridge/`。

- プロジェクト直下 `_bridge/` に `command.json` を置くと、開いたままの Editor が実行し `result.json` を書く
- 使えるのは `ping` / `refresh` / `compile-status` / `run-tests` / `run-op` の 5 つだけ
- 有効化はメニュー `Momotaro / Bridge / Enabled`（既定は無効）
- 詰まった場合は `Momotaro / Bridge / Reset Busy Flag`

### 実行時の作法

- ブリッジから `run-op` で「ダイアログを出さない編集操作」を実行できる。**メニューを人が押すために
  作業が止まらないようにするための口**。
  - `build-inumaru`＝犬丸 Prefab の再生成
  - `validate-project-data`＝全 Data 検証
  - `verify-required-tests`＝実行記録と必須テスト一覧の照合（工程の受入判定）
  - `build-companion-field`＝仲間の検証 Scene（`SCN_Phase4_CompanionField`）の再生成と検査
- **Scene を作り直す操作を載せてよいのは、未保存の変更があるとき自分で断る場合だけ。**
  `build-companion-field` は開いている Scene に未保存の変更があれば実行せずエラーを返す。
  この条件を満たさない Scene 操作は載せない（手で加えた変更を黙って消すため）
- 新しく Editor 操作を足すときは、`[MenuItem]` のラッパーにダイアログを閉じ込め、**実処理はダイアログを出さない
  公開 static メソッド**に分ける。この形なら後からブリッジの許可リストに載せられる

- **ファイルを書いたら必ず `compile-status` を挟んでから `run-tests` する。** Unity は外部からの変更を自動では
  取り込まないため、挟まないと<b>古いアセンブリのまま</b>テストが走り、新しいテストが存在しないのに緑になる
  （実際にやらかした。件数が想定と違うときはこれを疑う）

- **既定は絞り込み実行**（例 `.*Companion.*`）。フル実行は Scene を触るテストを含むため事前に知らせる
- EditMode のフル実行は `EditorSceneManager.NewScene(..., Single)` を含み、**開いている Scene の未保存編集を黙って破棄する**
- **PlayMode テストは既定では許可を取ってから実行する。** ただし下記の常時許可がある場合は待たない

### result.json の status を読み違えない

**失敗 0 件は成功と同じではない。** フィルタの綴りが実装と食い違って 1 件も一致しなかった実行が
「成功 0 / 失敗 0」で緑に見え、何も検証していないのに緑と報告した事故があった。ブリッジ側で塞いである。

| status | 意味 | 取るべき行動 |
|---|---|---|
| `ok` | 実行が成立し、結果も合格 | 次へ進める |
| `failed` | 実行は成立したが不合格（失敗あり・**全スキップ**・`minPassed` 割れ） | `details` を読んで直す |
| `error` | 実行が成立していない（**一致 0 件**・中断・開始できず） | 合否は**不明**。原因を潰して再実行 |

- `run-tests` に `minPassed`（成功件数の下限）を付けられる。**これは不足の一部を検出する補助であって、
  完走の証明ではない**（下限を超えた直後に中断しても件数は満たされる）。件数が分かっている実行では入れておく
- 完走は **`termination`（全体の終端結果）** で判定する。`completed` 以外（`cancelled` / `missing` / `unknown`）は
  すべて `error`。Inconclusive の混在、集計件数と実際の葉の数の食い違いも `error`
- 必須テストが実行されたかは **名前付きの必須一覧との照合**で判定する（下記）
- `expected`（予定件数）は **`filter` を付けない全件実行のときだけ**入る。Unity が開始時に渡してくる件数は
  絞り込み後ではなく**スイート全体**のため、絞り込み実行では完走判定に使えない（実測で確認済み）

### 実行ごとの記録と必須テストの照合

- `result.json` は次の実行で上書きされる。**葉テスト全件**（名前・状態・Skip 理由）は
  `_bridge/runs/<コマンド id>.json` に残り、上書きされない。`result.json` の `runLogPath` がそこを指す
- 必須テストの正本はプロジェクト直下の `P4RequiredTests.json`。要求 ID・工程・mode・
  **実行結果に現れる完全名**を持つ。Markdown の対応表やクラス名の正規表現では代替しない
  （そのクラスからテストが 1 本消えても気付けないため）
- 照合は `run-op` の `verify-required-tests`（`stage` と `runIds` を指定）。
  必須の欠落・必須の非 Passed・**許容一覧に無い Skip** を不合格にする
- **Skip を件数で許容しない。** 説明済みの Skip は `allowedSkips` に実名と理由を書く。
  「以前と同数だから非必須だろう」は当てにならない（実際に、13 件の Skip の性質を取り違えて記録した）
- **現在 `allowedSkips` は空**（F01 で撤回した）。Phase3／Phase3.5 の Scene 生成・検査テスト 13 件は
  「無題 Scene が開いていると自ら Skip する」ガードのせいで長期間 1 度も走っていなかった。
  ガードを**未保存の変更があるときだけ Skip** へ緩めた結果、全件が実行され Passed になった。
  以後 Skip が出たら「その実行では検証できていない」を意味する。Scene を保存して再実行すること
- **Scene を置換するテストのガードは「未保存の変更があるときだけ Skip」に揃える。**
  無題 Scene というだけで止めると、Editor に空の無題 Scene が開いているのが常態なので永久に走らない
- テストの実行と工程の受入判定は分ける。判定を実行中のテストの中でやると、自分の結果を見ることになって成立しない

### PC 側シェルが落ちているときのブリッジ操作

`device_bash` のマウントが落ちても（`no Plan9 drive shares mounted` エラー）、ブリッジは使える。
`device_stage_files` / `device_commit_files` はマウントに依存しないため、次の手順に切り替える。

1. `command.json` をクラウド側で書く → `SendUserFile` → `device_commit_files` で `_bridge/command.json` へ
   （`force: true`。既存を上書きするため）
2. クラウド側で `sleep` して待つ
3. `device_stage_files` で `_bridge/result.json` を取り、読む

`status.json` の `aliveAt` が現在時刻に近ければ Editor は生きている。マウントが落ちていても
`device_list_dir` は動くので、まずそれで生存を確かめる。

### Scene・Prefab へ保存されるクラスの置き場（実際に踏んだもの）

- **MonoBehaviour／ScriptableObject は、クラス名と同じ名前のファイルに置く。** 別名ファイルの中のクラスは AddComponent も
  Builder 直後の Validator も通るが、**保存した Scene／Prefab を読み直すと Missing Script になる**（`InvestigationRecordHolder` と
  `CompanionRosterContext` で起きた。実 Scene を読み直す PlayMode テストで発覚）。`MonoScriptFileNameTests` が出荷コードを検査する
- **Builder の出力物（犬丸 Prefab・2 つの Scene）はコードを変えたら作り直す。** ソースからコンポーネントを撤去しても Prefab には
  Missing Script が残り、Scene テストが「Missing Script があります」で全滅する。順に `build-inumaru` → `build-companion-field` →
  `build-companion-trial`
- **試遊 Scene は Build Settings に登録されていること**（Retry の再読込・PlayMode の実 Scene 検証が依存）。Trial Builder が
  出荷パスだけ登録し、`ProjectSettings/EditorBuildSettings.asset` が変わる（コミット対象）

### 直列化項目を新しく足すとき（実際に踏みかけたもの）

- **`[Serializable]` の struct／クラスへ項目を足すと、既存の Prefab・Asset は YAML に項目が無いので Unity が 0 で読む。**
  コード側の既定値は「未設定のときだけ使う」形のフォールバックしか無いことが多く、既存アセットが値を持っている限り出番が無い。
  結果、**Data に足したのに実機では常に無効**という、テストでも Validator でも気付きにくい状態になる
  （`ThreatSettings._firstStrikeThreat` で踏みかけた。敵 Prefab 5 つが値を直列化していた）
- 対策は 2 つセットで。**既存アセットへ値を書き込む**ことと、**出荷アセットから読んで関係を検査する EditMode テストを置く**こと。
  検査は「この値でその機能が成立するか」という関係だけを見て、数値の上限は縛らない（オーナーの調整を妨げないため）

### 「解決より先に」を確かめるテストの書き方（レビュー R3-02 で足した）

「被弾したら探索を解放する」のような契約は、**解放されたかどうかだけを後から見ても検証にならない**。
確かめたいのは「同じ呼び出しの中で、解決より<b>先に</b>終わっているか」なので、
結果通知（`HitResultChannel`）の購読者の中で状態を読む（`BusyAtResult`）。あとから見れば当然解放済みで、
順序が入れ替わっていても気付けない。

同じ理由で、**解決の前に何かをする必要があるなら、結果チャネルではなく入口の契約を作る**
（`IIncomingHitObserver`／`IIncomingHitSource`）。結果チャネルは解決が終わってから配られる。

### 「状態」で判定できない並行状態に注意（レビュー R3-03 で踏んだ）

表示代理（Proxy）の探索は戦闘本体の状態を変えない。そのため本体が Down から自然復帰すると、
状態も所有権も「通常どおり動いてよい」に見えるのに、表示は代理に抑制されたまま——
**見えない本体が歩き出して殴り始める**、という経路ができていた。

並行して走る仕組みを足したら、**その仕組みの「利用中」を状態とは別に公開して、各駆動の入口で見る**
（`ICompanionInvestigationState`）。状態機の状態だけで代用できると考えたところが穴になる。

### ハッシュ表は記録を書き終えた最後に採り直す（レビュー §4 で指摘された）

受入記録の末尾に置く sha256 表を、作業の途中で採ってしまい、そのあと入れた修正が反映されずに
「記録とコミットが一致しない」と読まれた。表は**その記録の最後の編集より後**に採る。

### テストが本当に欠陥を捕まえるかを 1 回確かめる

「条件を足す」型の修正は、入れ忘れても既存テストが緑のままになりやすい。新しいテストを書いたら、
**修正だけを一時的に外した版をビルドして、そのテストが落ちることを実機で確認する**。
落ち方が末尾の副次的な判定だったら、判定を核心へ寄せ直す（R3-03 で実際にやり直した）。

### テストの活動 Context

- 仲間の駆動は活動 Context（`CompanionActivityProvider`）が無いと**停止する**（未注入を許可側へ戻さない。R2-08）。
  仲間を動かすテストは `Momotaro.Tests.Support` の `CompanionActivityFixture` を**継承**する（[SetUp] で自由探索の Fake を差す）。
  アセンブリ属性の `ITestAction` は Unity のランナーではテストごとに適用されないので使わない（試して確認済み）

### PlayMode テストを書くときの落とし穴（実際に踏んだもの）

- **静的状態は前のテストから持ち越される。** 特に `GameModeProvider.Current` が Exploration／Combat 以外だと、
  戦闘・被弾・防御の Update がまるごと止まり「索敵は動いているのに何もしない」という紛らわしい失敗になる。
  PlayMode テストの SetUp で `GameModeProvider.Current = null` と `PerceptionTargetRegistry.Clear()` を必ず行う
- **PlayMode では `AddComponent` の時点で `Awake` が走る**（EditMode では走らない）。リフレクションで Data を
  差し込む構成は、GameObject を `SetActive(false)` で組み立ててから起こす。さもないと Runtime が既定値で確定する
- **`Time.timeScale` で加速するときは、1 フレームの経過が判定時間（Active）を超えないこと。** 超えると判定段を
  跨いでしまい、実機とは違う条件を検証することになる
- **実 Scene を読む PlayMode テストは常駐サービス（BootstrapRoot）を自前で作り直す。** 前のテストが破棄・提供点を null に
  していることがある。終わったら消して `GameModeProvider.Current`／`PlayerInputProvider.Current` を null に戻す
- **実入力は Input System に仮想デバイスを足して流す**（`InputSystem.AddDevice<Keyboard>()` → `QueueStateEvent(new KeyboardState(Key.E))`）。
  IA_Momotaro → Adapter → ラッチ → 入力仲介 → 調停役という本物の経路を通る。終わったら `RemoveDevice`
- **敵は湧いただけでは襲ってこない**（索敵は視界＋音）。実攻撃を受ける検証では主人公を近づけて J（攻撃）で音を出す

### PlayMode テストと Scene 破壊の常時許可

オーナーが指示の冒頭で「PlayMode テストを許可なしで実行してよい」と明示した場合、その作業のあいだ
Claude は**都度の確認を取らずに PlayMode テストを実行してよい**。離席中に作業を進めてもらうための取り決め。

**この許可が出ているあいだ、オーナーは Unity Editor を触らない**とオーナー本人が明言している。
したがって同じあいだは、Scene を置換する実行（EditMode フル実行・Scene Builder）も
**事前告知や確認を取らずに実行してよい**。実行したことは報告に残す。

その場合も次は守る。

- 実行したこと・結果・かかった時間は報告に必ず残す
- 再生モードから戻れなくなる事故を防ぐため、ブリッジ側に 600 秒の上限と強制終了を実装済み
- 許可が明示されていない指示では、従来どおり確認を取る

## ファイル受け渡し

Claude のクラウド環境と PC は別。`_transfer/` 経由で tar を渡し、`cp -f` で配置する
（`SendUserFile` で tar の uuid を取り、`device_commit_files` で `_transfer/<batch>.tar` へ置き、PC 側シェルで展開する。
新規 `.cs` の `.meta` は tar に同梱する。guid は `md5(アセットパス)`）。
PC → クラウドは、PC 側シェルで tar を `_transfer/` に作り `device_stage_files` で 1 ファイルとして取る。
PC 側シェルはファイルを削除できないため、消す場合は `_to_delete/` へ移す。
`_transfer/` `_to_delete/` `_bridge/` は `.gitignore` 済み。

**PC 側シェルが落ちている場合**は tar が展開できないので、`device_commit_files` で
**1 ファイルずつ最終パスへ直接置く**（`SendUserFile` で uuid を取ってから渡す）。既存ファイルには
staging 時の `mtimeMs` を `expectedMtimeMs` に入れ、オーナーの編集を踏み潰さないようにする。
新規 `.cs` には `.meta` も同時に置くこと（guid は `md5(アセットパス)`）。忘れると Unity が別 guid を振り、
あとで Prefab の参照が切れる。

## git の見え方について（誤解しないための注記）

PC 側シェル（Linux VM）の git には **git-lfs が入っていない**。本プロジェクトはバイナリ素材を LFS で
追跡しているため、そこから `git status` を見ると **PNG などが軒並み `M`（変更あり）に見える**。
これは実体との比較ではなく LFS ポインタとの比較によるもので、実際には変更されていない。
Windows 側の git（LFS あり）では正常に clean と表示される。

Claude が `git status` を見るときは、`Assets/_Project/Scripts` `Tests` `Data` `Prefabs` のように
**コードとアセット定義のパスへ限定する**こと（全体を見ると時間もかかり、上記の誤解も招く）。

**`git --no-optional-locks status` を使う。** 素の `git status` は索引を更新しようとして `.git/index.lock` を作るが、
PC 側シェルはファイルを削除できないため**ロックが残り、Windows 側の git がすべて止まる**（P5-02 で実際に踏んだ）。
`--no-optional-locks` なら索引を書かないのでロックを作らない。うっかり残した場合は `_to_delete/` へ `mv` して退避する
（`rm` は使えない）。`git log` `git show` `git diff` は索引を書かないので、そのままでよい。

## コミットメッセージ

末尾に必ず付ける。**2 行とも、そのスレッドの実際の値に置き換えること**（下は書式の例であり、固定値ではない）。

```
Co-Authored-By: Claude <モデル名> <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_<そのスレッドのセッション ID>
```

- `<モデル名>` は**その作業を行っているモデルの表示名**。過去のコミットに合わせて古いモデル名を書かない
  （P4 までは `Claude Fable 5.1`、P5 着手時点は `Claude Opus 5`）。
- `Claude-Session:` は**そのスレッドのセッション URL**。前スレッドの URL を引き継がない。
  スレッドを切り替えたら Claude が新しい URL に差し替える。値はセッション開始時に Claude 側へ渡される。

P5 着手時点（2026-09-21、スレッド 2 本目）の実値：

```
Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UzhKiBSFP45B6JTvYhy3cV
```

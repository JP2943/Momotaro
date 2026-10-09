# 証跡：P6B（対象コード `b79448e`）

P6B（成長・払い戻し・きびだんご使用）の最終確認の記録。記録の本文は `P6B_統合受入結果.md` 記録 003。

- 実行はすべて Unity 6000.3.20f1 の Editor 常駐ブリッジ（`_bridge/command.json`）から、作業ツリーを `b79448e` にした状態で。
  順に `fe1`→`fp1`〜`fp6`→`vbP4`〜`vbP6B`→`sb1`→`sa1`。
- `runs/` は各テスト実行の葉テスト全件（名前・状態・Skip 理由）。`results/` はブリッジの結果（照合・実ビルド）。

| ファイル | 内容 |
|---|---|
| `runs/fe1.json` | EditMode 全件 1895／1895 |
| `runs/fp1.json`〜`fp6.json` | PlayMode 6 分割（64・13・72・115・20・6） |
| `results/vbP4.json`〜`vbP6B.json` | 必須テスト一覧と `fe1,fp1,…,fp6` の照合（226・93・293・73・29、未説明 Skip なし） |
| `results/sb1.json`・`smoke/result_growth.json`・`smoke/result_continue_after_growth.json` | P6B 実ビルド：別プロセスの成長・権利・使用後の残数と HP の復元（13 項目一致） |
| `results/sa1.json`・`smoke/p6a/*` | P6A 実ビルド（共有経路の回帰）：New Game／終了要求／Continue、実プレイ、保存性能 |

## sha256（この README の最後の編集の後に採取）

| ファイル | sha256 |
|---|---|
| `results/sa1.json` | `7c773b3783d4b23460fb59167e3d3935398ab9d0f3923af8b3326cd92e2c07e2` |
| `results/sb1.json` | `2c9e9993d8c0c1d21887612667ebf2594e2b16b137b25ab3dac462e26e8a568f` |
| `results/vbP4.json` | `440e6007e13644d4674077a604169d6d647596e82ad87bf0995d4d87d7308c11` |
| `results/vbP5.json` | `e7bcc07a591ab8d764ee72cbf3d7fec12d2905ef14b55eabeaf08c3fcc23599f` |
| `results/vbP55.json` | `377d0e2a0843e3230dd8fa0d133b56b2198c3fa37593a323f33b70a24bc37076` |
| `results/vbP6A.json` | `25ecfc11052499e42c858a010caec69bb74fa36157d1a5a45dff43cf9151cd8f` |
| `results/vbP6B.json` | `ea610b14da33f656da5bb78437e7035d5b565d6912c4be8af4a06346ce476a03` |
| `runs/fe1.json` | `46b6c902f5865d2f3eb7acaddf95d50d2e49e15fd72dcfdee9ef40ef7f150732` |
| `runs/fp1.json` | `f56294e3a30dc44fbe98ab3043c283d4fc37ec29511b0fdaf2bc803d800222f1` |
| `runs/fp2.json` | `8607cbe4bd10e28f5637165d8eb86cb638d2e8276577cea6671b8a23515e0787` |
| `runs/fp3.json` | `0e632be767524e9307957ad1586c23a21352964201a1ab7c7e12b1f8b94b91a9` |
| `runs/fp4.json` | `503567d329f4dee744a333daed2ef2ae8e701a4b52746efd7d5e5752f84c8faa` |
| `runs/fp5.json` | `e57e98d46f9ce505d9c6dce21c4a8b8f84845010025c689abd32b939ef74638d` |
| `runs/fp6.json` | `e54a546ff9d1d2b38ed52f63f145156559439906b00f629a9e3fc2883a7897aa` |
| `smoke/result_continue_after_growth.json` | `19c12a1975d0e652284d3c7147d9dbd7d52a01152cee20f99af396e1eb5671d7` |
| `smoke/result_growth.json` | `7ffd6420779cd894a20e3255cdd1c161800576e5b463366f1cf903fc15af125a` |
| `smoke/p6a/result_close.json` | `7719b0b54d5a0d4a14b053c77e77a38f8f3502cd3db912cd230740e86f70fc92` |
| `smoke/p6a/result_continue.json` | `0b0b1c29aa5df8c648f865d0fd0ab252f0055d7eb498a7aa782da9f456902c81` |
| `smoke/p6a/result_continue_after_close.json` | `bb7283c646ae40d527b998fd5d844c1f8af7b756957946f5978fc0f0925ac70b` |
| `smoke/p6a/result_new.json` | `381a449ad384045a939d228c3904a4ba23a77ea16a4506b30aaa5660ae005440` |
| `smoke/p6a/result_perf.json` | `d30dec46937d2df74741785cd9fdd93f727826123e564a1fca667c7c9d562323` |
| `smoke/p6a/result_perf_slow200.json` | `346128e17fe818a05450732f166a839070d91962fca93891409e1cdcf91a3251` |
| `smoke/p6a/result_play.json` | `2dafeba55715e9288ba2bf05bcc0bc7054ff76c2f0d611b18c3b1530a8f9308e` |
| `smoke/p6a/result_play_nosave.json` | `7572842e00a81dcd86eaffa795acc96e8647d549c47e6d5e6249387e41ede188` |
| `smoke/p6a/result_play_probe.json` | `c00fcde83b8b0fe27aeb8d87a7ffbb07edf70e86bd6deb89fc934af8bf7be2fd` |

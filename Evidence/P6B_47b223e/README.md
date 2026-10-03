# 証跡：P6B レビュー ddb2d19 対応（対象コード `47b223e`）

記録の本文は `P6B_統合受入結果.md` 記録 004。前回の証跡は `Evidence/P6B_b79448e/`（記録 003）。

- 実行はすべて Unity 6000.3.20f1 の Editor 常駐ブリッジから、作業ツリーを `47b223e` にした状態で。
  順に `ge`→`gp1`〜`gp6`→`vgP4`〜`vgP6B`→`gsb`→`gsa`。
- `runs/` は各テスト実行の葉テスト全件。`results/` はブリッジの結果（照合・実ビルド。結果の details は先頭 30 行で切れるので、実ビルドの値は `smoke/` の各 JSON を正とする）。

| ファイル | 内容 |
|---|---|
| `runs/ge.json` | EditMode 全件 1896／1896 |
| `runs/gp1.json`〜`gp6.json` | PlayMode 6 分割（64・13・72・115・20・8） |
| `results/vgP4.json`〜`vgP6B.json` | 必須一覧の照合（226・93・293・73・32、未説明 Skip なし） |
| `results/gsb.json`・`smoke/result_*.json` | P6B 実ビルド：成長の復元（growth → continue）、使用中の通常の終了要求（useclose 0.6／1.7 → continue） |
| `results/gsa.json`・`smoke/p6a/*` | P6A 実ビルド（共有のスモーク駆動の回帰） |

## sha256（この README の最後の編集の後に採取）

| ファイル | sha256 |
|---|---|
| `results/gsa.json` | `30d9b61bce90180c69ba0bbd5910f5ed952a4a53e89863fc9c10fd0c3d73bb48` |
| `results/gsb.json` | `bbe75e5860efaecc7ac1f29801ab0f0eead864dfa7653865bb6df6d90de23a9d` |
| `results/vgP4.json` | `aff91bc02dba34ff0ae22d92396d7521410fa511d41c3eed9be4d704c7f9b41b` |
| `results/vgP5.json` | `700a3943cb57a9d6f6caf2c3956d4a54b11e7b3a79e57c841e79c660e9a04b41` |
| `results/vgP55.json` | `0a0051e7ab2d0526dc43744766942708fd3fd8b5d79d33593a6fdc65a4fff915` |
| `results/vgP6A.json` | `9d9941f493bb5f5cc7efa7923a2fdb9218e9d9895bdc1514ad3532d20fc6a751` |
| `results/vgP6B.json` | `95dfef0d1a65132f5732053381b3f68681fc1d07b5708b42369728092c2e6148` |
| `runs/ge.json` | `a4fe32c176074a1c1c9e99c22d2dba90961c4290fb4a29e55eae8e9d6d8abd54` |
| `runs/gp1.json` | `7fc0f65eadc3e64511a6c49f804676c0e7b1f3e9fffab004a347f74b05d15969` |
| `runs/gp2.json` | `429ccd9986459a0c4d82c1de56eadb80abd43ee75fe0ebb264ec61b317f772d4` |
| `runs/gp3.json` | `d8a817dda4cd97d01c017bc5f992bb1dec3800df8bb1a41953ab3359adda56b4` |
| `runs/gp4.json` | `ec93746c0aca38e7e9a838df255e555df24a8baa6d55f0e7cccc410672a3b413` |
| `runs/gp5.json` | `7b8389c9f7da813e3ed5fa12db259b03470cd152a97af95177e489845157cea3` |
| `runs/gp6.json` | `fa9168950faa7fa71312249b97aba385f402eea62a963fa362b5108d7548eb43` |
| `smoke/result_continue_after_growth.json` | `48e896f4c7fb65eac3f64dbcd412362882d4d6c15c0ae41ab9a22d9d5b9d8c38` |
| `smoke/result_continue_after_useclose_0_6.json` | `73ab68876164221fd612fd2d611594d7fd761b8823e34dd57296db0a8f31375b` |
| `smoke/result_continue_after_useclose_1_7.json` | `2c23512032d5887d6ba240a506e1ba9ba768cf0ccf61d188b1a8345587b62a37` |
| `smoke/result_growth.json` | `72c421537d14cb670fbdc611948c393e8afff589a6e1953133b880705e34a58b` |
| `smoke/result_useclose_0_6.json` | `84668b34b2a171ad1e9122370cf68bc05b4069a2078bf4cd31676bdf5a0eb8e6` |
| `smoke/result_useclose_1_7.json` | `88ac451c1e321b5bbdf54cb05818e91e71c9057839e89033389897665ba49859` |
| `smoke/p6a/result_close.json` | `36d1adfeee581f6cef4d911fc8b611c6b12c9409834306a8381b2dc14cf69337` |
| `smoke/p6a/result_continue.json` | `ffa5d23e8ec51341d3a6aa4178d8e32d5894d4816b154b65ecfafd8d5febe942` |
| `smoke/p6a/result_continue_after_close.json` | `fcf6cf87261e83e9a61de1c0e45c9807329cbac69c27bdaecce18c0c5ecda2b7` |
| `smoke/p6a/result_new.json` | `4b14a1b3faa5fc8d8cfcd0075848dfa184d1660b9c351c4cba971498a0675e65` |
| `smoke/p6a/result_perf.json` | `b0df5732799bd16e787f623444f20cc7049a49127864974e5d12919540413bd8` |
| `smoke/p6a/result_perf_slow200.json` | `4d635f39cd921380e9c93bfe0843e2cc430e890d6d09ce6d8cb170b3713142e3` |
| `smoke/p6a/result_play.json` | `b56864848fe00d65c89ff7796003349bcd2d87be2a283c81b91ef873b2bd4823` |
| `smoke/p6a/result_play_nosave.json` | `c77af5bb463301512144d55387006649e9d06564ec41f4e01eef151a08cbb12c` |
| `smoke/p6a/result_play_probe.json` | `07085d7952695b9937b77a7fb9c99f6a1e4f684e2714e18234e133d3dab0ab67` |

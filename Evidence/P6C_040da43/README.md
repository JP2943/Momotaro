# 証跡：P6C レビュー a24d92c の R1・R2 修正（対象コード `040da43`）

記録の本文は `P6C_統合受入結果.md` の記録 003。実行はすべて Unity 6000.3.20f1 の Editor 常駐ブリッジから行った。作業ツリーは `040da43` と同じ内容にした状態で実行した。

| ファイル | 内容 |
|---|---|
| `runs/he.json` | EditMode 全件 1918／1918 |
| `runs/hp1.json`〜`hp7.json` | PlayMode 7 分割（64・13・72・115・20・9・6） |
| `runs/re1.json`・`rp2.json` | P6C 関連の EditMode 41／41、直した旅立ちのテスト 1／1 |
| `runs/me1.json`・`mp1.json`・`mp2.json` | 修正を外した版（EditMode 3 件失敗、PlayMode 2 件失敗、Commit で消す処理だけ外した版で 2 件失敗） |
| `results/vfP4.json`〜`vfP6C.json` | 必須一覧の照合（226・93・293・73・33・33、未説明の Skip なし） |
| `results/validate_phase6c_world.json` ほか 2 件 | 世界検査（P6C・P6B・P6A） |
| `results/smoke_p6c.json`・`smoke_p6b.json`・`smoke_p6a.json` | 実ビルドのスモーク（ブリッジの結果） |
| `smoke/p6c/*`・`smoke/p6b/*`・`smoke/p6a/*` | 実ビルドのプロセスごとの結果 |
| `p6c_just_evade_timeline.txt` | `hp7` の時系列（スライド失敗・旅立ち失敗・実攻撃） |

**改行について**：`p6c_just_evade_timeline.txt` は PC で書かれた CRLF から CR を除き、**LF で保存**した。`.gitattributes` で `text` 扱いのため、リポジトリの blob と作業ツリーは同じバイトになる。下の sha256 はこのバイトで採った値。JSON はブリッジが書いたバイトのまま置いた。

## sha256（この README の最後の編集の後に採取）

| ファイル | sha256 |
|---|---|
| `p6c_just_evade_timeline.txt` | `e430d5ef5f4e0ba3cd8929709498ea158bcecd205e197bbfe96a81b6efd621c9` |
| `results/smoke_p6a.json` | `0517aa6dca660097c80708ef73a0ceb070d64bcd203f310046de0221cad9df49` |
| `results/smoke_p6b.json` | `1963bb4caad8fbb01a5952ad207ebf8189a89d50827c26c47f6eb29ff341332c` |
| `results/smoke_p6c.json` | `d64ada1f351032be66f2403ef57b72b1fdf99c04129537701be77d25e90830a0` |
| `results/validate_phase6_world.json` | `dfd0a8fd321bf99842d12b8812d259761b39a7e544986976f5171eb50f1d60c0` |
| `results/validate_phase6b_world.json` | `fc588c4a19885e25805c30d0e560e81934ac815f02bb8b00f14c1bdb470e9163` |
| `results/validate_phase6c_world.json` | `6544d390cf55cc9f04b6c1ad73ffe104fc16fdfa4a86b183413f96c967e9e10e` |
| `results/vfP4.json` | `8ea09644da45cb8ff0f0cb92b069cc0e91ee31ae628c96db861b962b9b14ac47` |
| `results/vfP5.json` | `ca6bb50512959c3d5cb7eb6df79765cca44b9c163308ca3f5f5274dd64f4ed29` |
| `results/vfP55.json` | `a4775c15e0f8567ef333376af7be89feb6788311ee26e939ae578571fa6c7d5b` |
| `results/vfP6A.json` | `026748a6fc4ec7f44c75abff18a35b0c0d873e73b091e190c615ec5476dc587a` |
| `results/vfP6B.json` | `d0189f674a50983419c6d484481392e1a758db6ecada00f1a00c1bf6d910451d` |
| `results/vfP6C.json` | `cf06b6aba604a4a8f35e85cf4da0f3fb19726f98b3406eddbb1809bbffed1f9c` |
| `runs/he.json` | `63f41bd0272e5077b81c9d1297ba3379068a2a5c2fcccfea5e5ab3f6aa5ffd6a` |
| `runs/hp1.json` | `3dc97834c4fa1a730a074ec340250f8f7c27d3eb9812246239ad180a30a95347` |
| `runs/hp2.json` | `ef782c35e885a497c1f02f0f69f9bbc5871494d7b397e29b527460dfa759ddfa` |
| `runs/hp3.json` | `d2b06df90751e1e03fde360749886ff1d33562164da63e686b1e1facce81b720` |
| `runs/hp4.json` | `005f7078121792104aace0da3336bc318f2aa82b2c3500f05373b13cb7d4b58c` |
| `runs/hp5.json` | `2b1abb9536af5be963dd0e123d01db794748c620f23ae729e0fa5adaeff35430` |
| `runs/hp6.json` | `84316c0a3181441be5318c94c2cd3c88998f5f1f28d1254ff0f30a6a63c9edad` |
| `runs/hp7.json` | `9cc513132fd6aa2a8dc0fa6da0a1c7e4e4f8549bf5b60cd32f307d4a00d02cb5` |
| `runs/me1.json` | `3a1a243c694862a26a3d7a9c3b2e0913e2070e10dfa67a5c59a39c3d831c355a` |
| `runs/mp1.json` | `6b2588cfa5867947f89c54ecd7d765408f9d05a6625a2a715883c03b4f9aa539` |
| `runs/mp2.json` | `ffe37725094102f31a5768fc06c9a2423823be10b4193e7306dcbbc5ce38f82b` |
| `runs/re1.json` | `685c2ba3cab583963d610d23e784fa040bd45b586dd6c25fe3017a5ce5966f22` |
| `runs/rp2.json` | `94aa249868b80ad35522dc2afd7fc8e0423b16d01154422b11b6c0aba6a154c3` |
| `smoke/p6a/result_close.json` | `832de94c65481630f05381e54c412d812dd935b6b115bae86d9133295ee0d2ee` |
| `smoke/p6a/result_continue.json` | `5028bd2c8b84901f9aa0e24e1aedf2eb3c56e225075eeab6c16eb18baece29a2` |
| `smoke/p6a/result_continue_after_close.json` | `3dbfdb753224a841f14d9af4480276a2e752c95093e8e01666ca394352f5448a` |
| `smoke/p6a/result_new.json` | `65bec406d0bbcb60321db2553b9c99f7daffc60d47f04454fdb32516ebb194ae` |
| `smoke/p6a/result_perf.json` | `5eb665f3522fc33ea95237bcfa3f45590fe015ab6e4315498229a5e7c54e6b7e` |
| `smoke/p6a/result_perf_slow200.json` | `b19e7059a63e0afdd4419e8339f85bdc2f8da4958927600e83db2a97f51f07b0` |
| `smoke/p6a/result_play.json` | `618e8aca28e582e03fd40397e34aaa244b7cd36ba0df12c76ea2d64cb675dbeb` |
| `smoke/p6a/result_play_nosave.json` | `bfcae37ccf2507092f615a781259afe152eea21a100972b112717389637e196b` |
| `smoke/p6a/result_play_probe.json` | `bd0603a8b5af8931c5c53d85c906af17d0f2fac9de5a085f838c12492cffc30a` |
| `smoke/p6b/result_continue_after_growth.json` | `1fb964224a5b29252ae65e5b413b9f90850131c81a0724f27af387f40340693a` |
| `smoke/p6b/result_continue_after_useclose_0_6.json` | `988516fade7e5978f7ea5493ef8ddf04e269ac8c8c2ce6cd807fecc9a0a33d60` |
| `smoke/p6b/result_continue_after_useclose_1_7.json` | `d6c4b596b90fd0b4b4b3eb134eb958879960bfd4efb0e999793ec1f4cc1adf7c` |
| `smoke/p6b/result_growth.json` | `8485d596e116ce9f0a3914808bc3164a5a5f3bf07962c505fdc541c29b490cae` |
| `smoke/p6b/result_useclose_0_6.json` | `022b8c078d7e397ee5ec2a4adf78df348ed20a43e12c52852321503afec91bee` |
| `smoke/p6b/result_useclose_1_7.json` | `d71fd04060a41706935865a470f51d1a9000bdecd7e3b7631e275ed4040f7e4c` |
| `smoke/p6c/result_continue_after_justevade.json` | `cda5463b11e2f0bb0749d9dd6930852c12aec95193153af123fb9b0a063d6cf9` |
| `smoke/p6c/result_justevade.json` | `5a203a2db872c780a4dd4ab49d5f19ad97128e6d9fdb96e88135728f2ff54771` |

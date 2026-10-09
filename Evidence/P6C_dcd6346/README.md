# 証跡：P6C ジャスト回避と防御反撃（対象コード `dcd6346`）

記録の本文は `P6C_統合受入結果.md` 記録 002。実行はすべて Unity 6000.3.20f1 の Editor 常駐ブリッジから、作業ツリーを `dcd6346` と同じ内容にした状態で。

| ファイル | 内容 |
|---|---|
| `runs/ge2.json` | EditMode 全件 1916／1916 |
| `runs/qp1.json`〜`qp7.json` | PlayMode 7 分割（64・13・72・115・20・9・4） |
| `runs/pc1.json` | P6C PlayMode 4 件（最終版の初回まとめ実行） |
| `runs/em1.json`・`em2.json` | 修正を外した版（10 件失敗）と戻した版（19／19） |
| `results/vfP4.json`〜`vfP6C.json` | 必須一覧の照合（226・93・293・73・33・29、未説明 Skip なし） |
| `results/v-validate-phase6c-world.json` ほか 2 件 | 世界検査（P6C・P6B・P6A） |
| `p6c_just_evade_timeline.txt` | `qp7` の実攻撃の時系列（近接・矢・ガード不能・二つの Area） |
| `smoke/p6c/*` | P6C 実ビルド：`justevade` → 別プロセス `continue` |
| `smoke/p6b/*`・`smoke/p6a/*` | 共有スモーク駆動の回帰（P6B・P6A 実ビルド） |

## sha256（この README の最後の編集の後に採取）

| ファイル | sha256 |
|---|---|
| `p6c_just_evade_timeline.txt` | `57a6b65af72d6647f204afeea20ed9cdb59ea0f1247261cb4f25e031618c7e83` |
| `results/v-validate-phase6-world.json` | `16463e8e9c16647f92dcfa1228f667528c549e52d7aba9431b6e525a2e2dcee2` |
| `results/v-validate-phase6b-world.json` | `0431625a0dc9a6fd5c68ff0235185dc4c1cec073b8a2c8b79ebb42c4ec78482e` |
| `results/v-validate-phase6c-world.json` | `31f688f9d5b037d2e5a9d9ef8d603d8ff6d6c51a243dd6148aca666b3363212d` |
| `results/vfP4.json` | `cc9955724d24c5ac922288a962f6456a7f1d44fe8def2dacbe57ee374dc69ef9` |
| `results/vfP5.json` | `efeb53046b1a0d32d76010ea6b3d6aac6ca48af4d1099746e2525451c34231fb` |
| `results/vfP55.json` | `e729923aebde67db5e153e057c3ce4efdbda9861d7ef31e36f6d652aca46e037` |
| `results/vfP6A.json` | `ba9f4585033a8c6f7bd542504ec88670b315aa9220dadb7aec746e7c64d55267` |
| `results/vfP6B.json` | `5996b344e5bc075ade0c471ad5cdb16c27d8fa579ffe2c4f078544019ced2317` |
| `results/vfP6C.json` | `f62a19ac74c1b03249b31491670a95ec828523a360810ba2ad9ea0f8ac452981` |
| `runs/em1.json` | `9d9d7116332222941829d070b434c1e4d3c19b57451e65c367d90960e5de42ff` |
| `runs/em2.json` | `cfa8c3092583c5701c5bf4ac41ad732e87123d2c4ee2292007ba22374d008128` |
| `runs/ge2.json` | `a4afb8cece337864bf621e44d0127688857abdcd85b72f164415c7737ffea50c` |
| `runs/pc1.json` | `157bd5658229555e9432325b1645287cb76be74509541ee40890e5cc1aae49d5` |
| `runs/qp1.json` | `8d50fd266f6669759cd58d21aad3592b60b9a3d84e12558b2ddd508f92b8e857` |
| `runs/qp2.json` | `054d7fc5f89f77e19b2ac934ead59827a96aa512992d50dc3eb67f926ddf3b59` |
| `runs/qp3.json` | `7476bab869aa05d99acc254d71ea9fa46f2a3d3ad7825d6c98d6086035058974` |
| `runs/qp4.json` | `73069aef0c6747da692b170e066d85de868347dd9542486de0ac6a65921313a3` |
| `runs/qp5.json` | `fe67df6fdb41354d1c6f8dd2e5e95d753a7a120c536f73223d2419ee2deac875` |
| `runs/qp6.json` | `0bcfa149f980929c59c45f867d3964c1b5c0914db9a3d766a480034dffffd9fd` |
| `runs/qp7.json` | `078ff4e170a434c134d4d8da7bf26bae165c1427af23887d9efca995dcf80b3f` |
| `smoke/p6a/result_close.json` | `6f8e6f4eb8a605e779283179166aa09b631866c2c7d795675c7dd0fa5cc1738b` |
| `smoke/p6a/result_continue.json` | `f5e4f2aa9688d427cd8adada9b15f09ea5f26bc9cc21d5a70e0e06937669f290` |
| `smoke/p6a/result_continue_after_close.json` | `8ac9e1d35d3f4b081ce85e6eda12d144b6702f23e111d0bfe7b7d3e69d8e9d1e` |
| `smoke/p6a/result_new.json` | `3bd16c72d60eb3cb204b4d609b9683a92b8f9c4114619e81585274e15213a63b` |
| `smoke/p6a/result_perf.json` | `476e8bc435b05a94ede6cb09679b93832513b026d39189bf74a2decb393e2a2d` |
| `smoke/p6a/result_perf_slow200.json` | `31159da5491f5856aa8fa663adf89c5545aef796a79ed1b0ab599b6fc27b1f38` |
| `smoke/p6a/result_play.json` | `d306f9cbc1b6f7a0d9c6a11017605c54ba6abd2452476976d2392570eb580fd7` |
| `smoke/p6a/result_play_nosave.json` | `8e5bfd63857f7ad100a462b99000ee26a60ddce2e85da15494b28dd689969719` |
| `smoke/p6a/result_play_probe.json` | `f492654b6e29409bf5dca4c2355827553f410013a1a6ef36edc41de9780f39d0` |
| `smoke/p6b/result_continue_after_growth.json` | `79c0ed722afda986717b302a2d6f96542eca00e0ef1c857d981d4c964e3c4344` |
| `smoke/p6b/result_continue_after_useclose_0_6.json` | `1f8a7c33d38debc4ad02fc82f48cc466dd5f49e78715ab48e60843d67db08540` |
| `smoke/p6b/result_continue_after_useclose_1_7.json` | `5eaea6738cfa84f9ede6590b4fd8f730f6fe60db9fbb4fe5d1017643b1400351` |
| `smoke/p6b/result_growth.json` | `b55b33f031351ecf2ee12be0265367bb38c20daf39c0a26a46216bd8ef38d3f6` |
| `smoke/p6b/result_useclose_0_6.json` | `ffe0897be000c0ae5cd4ac53174e48dea1eb08f932b337e48511ef129fbc3003` |
| `smoke/p6b/result_useclose_1_7.json` | `876c91a12dd59904b65da6baf6d66d55b3b8c1e8e2a5cd5864457e95c84ef01a` |
| `smoke/p6c/result_continue_after_justevade.json` | `1e517ba876bc37bb049c5d70061078a0edfd1c4abf0adbb4c6a98cf886db6c92` |
| `smoke/p6c/result_justevade.json` | `139dc859700b7c043466d7db606e34e1335a0e07521e08a4ba5ae6c9a815babf` |

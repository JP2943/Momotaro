# 証跡：P7 レビュー 8d78416 の R1〜R3 修正（対象コード `0174cf4`）

記録の本文は `P7_統合受入結果.md` の記録 003。実行はすべて Unity 6000.3.20f1 の Editor 常駐ブリッジから行い、回帰（`fe1`・`fq1`〜`fq8`・照合・世界検査・実ビルド）は
すべて `0174cf4` のコードで走らせた。

| ファイル | 内容 |
|---|---|
| `runs/fe1.json` | EditMode 全件 1944／1944 |
| `runs/fq1.json`〜`fq8.json` | PlayMode 8 分割（64・13・72・115・20・9・6・10＝309） |
| `runs/rt1.json` | P7 の EditMode 26／26（R3 のテストを含む） |
| `runs/rp16.json`・`rp17.json` | R1・R2 の新しい PlayMode 2／2、修正を戻した後の `P7WorldPlayTests` 10／10 |
| `runs/ie1.json` | R3 の修正を外した版（新しいテストが失敗、7／8） |
| `runs/ip1.json` | R1 の修正を外した版（GUI のクリップの対が崩れたエラーで失敗） |
| `runs/ip4.json` | R2 の修正を外した版（保存待ちのクリックで会話が閉じて失敗） |
| `results/vfP4.json`〜`vfP7.json` | 必須一覧の照合（226・93・293・73・33・33・36。未説明の Skip なし） |
| `results/validate_phase6_world.json` ほか 3 件 | 世界検査（P6A・P6B・P6C・P7） |
| `results/smoke_p6a.json`・`smoke_p6b.json`・`smoke_p6c.json` | P6 の実ビルド確認（回帰） |
| `results/smoke_p7_sg7a.json`〜`sg7c.json` | P7 の実ビルド確認 3 回（すべて 24 項目 OK） |
| `smoke/p7/sg7a/*`〜`sg7c/*` | P7 の実ビルド確認のプロセスごとの結果（3 回とも残した） |
| `smoke/p6a/*`・`smoke/p6b/*`・`smoke/p6c/*` | P6 の実ビルド確認のプロセスごとの結果 |

**改行について**：JSON はブリッジ・実ビルドが書いたバイトのまま置いた（CR は含まない）。`.gitattributes` で `text` 扱いのため、リポジトリの blob と作業ツリーは同じバイトになる。

## sha256（この README の最後の編集の後に採取）

| ファイル | sha256 |
|---|---|
| `results/smoke_p6a.json` | `8c046f53e0aa5a0bed16996b63812b2f1078df82c0f043d37e0f85bbd083616e` |
| `results/smoke_p6b.json` | `306bc447aa4a1007e24aba57628d97e756e13de65999991539b28e6996ec25ae` |
| `results/smoke_p6c.json` | `032864b2fd44053e06077a87975e478fe414254d098ccdfb0b378f862a742896` |
| `results/smoke_p7_sg7a.json` | `62ed4c444cab0b1abe9f57688f1bca6f793396906eb37e8fb96cee0ff74c1dda` |
| `results/smoke_p7_sg7b.json` | `b644b34de5d8c59184f76c766bd9e8921aeba96318e82af554b616b16d275470` |
| `results/smoke_p7_sg7c.json` | `e0cd5dd4ec4b0bb1ce43fd9b3e43c9797f50fcca3e41eabfaedc0ac624647c6f` |
| `results/validate_phase6_world.json` | `3200796ea91288a49ee1f9baef98ada9d14e6f1c5cfb1f5678147445db8547a5` |
| `results/validate_phase6b_world.json` | `0d077bdfcf0f6844831d89628c51801695821ad4fee48edfb00c0710ab1d58b3` |
| `results/validate_phase6c_world.json` | `4f8520e982324c9878538ef00c631f19c2158e0935bbbab7fbbf11bbc4afa845` |
| `results/validate_phase7_world.json` | `0a7b063727bb0cd49a0ebc0b57d3780d9d48c5931b950a6eb2080b45e20b8707` |
| `results/vfP4.json` | `7abc74f2580bf7300c86848a42e22542bb6d7beca1a4a185f37ea56dad192389` |
| `results/vfP5.json` | `d6d41552ca3f7dec6cc656f37eba4c8be5bc57704cb25f08aa15734bdaef9500` |
| `results/vfP55.json` | `01944daf0f51d535f46a62d85c06583ee5a1bcea64af549c6a5c2fd5be576f53` |
| `results/vfP6A.json` | `15a266d73bb735e9c70787aa36cb5d939a19e087ee8b4ad321c9956478a55029` |
| `results/vfP6B.json` | `b0f89c32f3a76c038ba3a07d4f7a2f638e00b9f56860d3b9162948f7814cc9e9` |
| `results/vfP6C.json` | `8467f920f6c29521d296f98ca2f2ed67313302f3575c2160f76cfa83b96135a4` |
| `results/vfP7.json` | `c13fdb0bcd5a195ce13fd23e732519c5959106782b2d1ff13470d9ca3d54b3c8` |
| `runs/fe1.json` | `955221b5c3b4d22951b846f6dcf822fffabed0f86523852475d15dcc0dab3b11` |
| `runs/fq1.json` | `f192966ab303f0ae6d03d343a2fa9225ed9745ffe575f632f10d78eb8b9793d1` |
| `runs/fq2.json` | `fa9b0f942d5a68eb2301e3e11a8eb5eda6e71883ba1ff85df5432157f8ba6f65` |
| `runs/fq3.json` | `ea7cface51a7743ea946cc528c8b54d06dafe57c1d549aa32e5b30b73a501e8c` |
| `runs/fq4.json` | `5f405517aa830b7458f6ac5c337faf122531e71e60179f13541ea4aa9b3d5ffb` |
| `runs/fq5.json` | `e095efea89016791a59f3312454d641024c72bb224b59eab2cc110aa66c33d72` |
| `runs/fq6.json` | `6a34cfc3a6e8353e577f557d4cf1cd4e62ff7799f453a399c6d37d02ce6402d0` |
| `runs/fq7.json` | `e4d84c1f601f55f1b6f3e6d0b6f45c0f399e5eec8f7a0cd975136a513365e270` |
| `runs/fq8.json` | `f734796a02b70a98e87509bfa0908d21e9c9f318f1d3743bdfb61b84acb5712e` |
| `runs/ie1.json` | `544460a967a03d577149a49b09d55861f45ec878705e73031be0964dc88c3eb2` |
| `runs/ip1.json` | `bb910d4c2f833a2f6f444fb4027d68e792da3f2510517af8cabdd3425aa70324` |
| `runs/ip4.json` | `00665a5109eeea7c6d78094f67d456ee4b4a091004ed882a9de971bba47722a2` |
| `runs/rp16.json` | `961e8469b894584744e9da6a72c702ed381585800c6d382a948d782b795aed63` |
| `runs/rp17.json` | `d5299bff472aa38d74eb4911d7a544d6e117a1803c51a9045a9bdfea0299cfdb` |
| `runs/rt1.json` | `e1d13d0fc0bac7087e0431ba213053d30b87ba162fe04fd479302941d0d7f9d1` |
| `smoke/p6a/result_close.json` | `466303130913840cc9f11bcde9df8a3be896a7b9eef0e3921e830dbe3ad74b63` |
| `smoke/p6a/result_continue.json` | `b848323b53920aae64128224ff500f1e343eece40c66b16383a1a17963566d9e` |
| `smoke/p6a/result_continue_after_close.json` | `7c65fc7f258c0c45030da2f8a3712a5341c758e7bd694cc5a119defba7ecd629` |
| `smoke/p6a/result_new.json` | `b745b0fea4949c7a4a2f24e32e0a98f7f40af78973c3bfae7567beb0bb6e7191` |
| `smoke/p6a/result_perf.json` | `6aa8c339ba313d555c21d5cf933d15650d420e23cd2a817ee1fcfe129a92faf5` |
| `smoke/p6a/result_perf_slow200.json` | `2753732419c960a9b8c95e076260dc1c8cf9f8398d49d835e37e106d14ec1960` |
| `smoke/p6a/result_play.json` | `36a348f7a764a8ccd78aa0f595248e51253520913931f9e5e4923400c2c562ac` |
| `smoke/p6a/result_play_nosave.json` | `2dc71cfc99f7ba214a64b39384deb2347b41f7b8aa05296b7bd7360efe474d33` |
| `smoke/p6a/result_play_probe.json` | `b92011e86cd36d733c41e32ea05ddb1f2e3e85e8fb79a5d855329eef5a343ee7` |
| `smoke/p6b/result_continue_after_growth.json` | `d7290781e08adbfa374c1b348140d007af2dab2289160ca05c2d14be98e04422` |
| `smoke/p6b/result_continue_after_useclose_0_6.json` | `f10a30f1c7c859a77dec0501f74914609c9f99d9a29a6db2758cd5dee21bb27c` |
| `smoke/p6b/result_continue_after_useclose_1_7.json` | `0a81f1184e2b715cda76d682744991e3393fb23d08016a3c98add30185feb072` |
| `smoke/p6b/result_growth.json` | `4be6ff68c93947038a0b48cc80630202b889841a8d63e7864d66995430e53ff7` |
| `smoke/p6b/result_useclose_0_6.json` | `5b5ef5d8e05a1cdd159d1bba9d873310edd333bca2645d387ec57e931a5337eb` |
| `smoke/p6b/result_useclose_1_7.json` | `0b7247e69f5cd569992c18a2cd7b786097e1a4a41359161827c0f8af00312948` |
| `smoke/p6c/result_continue_after_justevade.json` | `e21ccdb30a2f686adfdd4e8d1376552e7d089fb489f5c5ebcebf44b84c9cfec0` |
| `smoke/p6c/result_justevade.json` | `6de81534159cb6d00f11ccec75108fa87a3ee4aaa726f62edb3f8dacdb9258b3` |
| `smoke/p7/sg7a/result_continue_after_story.json` | `445f754e66c9ca8a914966783309c91363c3453be138188b9ae1044e7ce308a4` |
| `smoke/p7/sg7a/result_story.json` | `3ea5e7ccaeeb56d611f3bb490805fd353a302d8ba0a4a570d9cd76905ddec488` |
| `smoke/p7/sg7b/result_continue_after_story.json` | `2eeb6dcbe992c7132178d20e129347e7364690665b3e4d87c91afa465463cf16` |
| `smoke/p7/sg7b/result_story.json` | `167850d6f846909345b00dbd5b4b07c27b91c8c8ef00d4a2660e4beea2161b0c` |
| `smoke/p7/sg7c/result_continue_after_story.json` | `01b52340f3d86a583cc6ada9385e52667b2a954584e8177471443377aa255000` |
| `smoke/p7/sg7c/result_story.json` | `3fde35f0d6aad91e6dbf4c2e4316b2abd276866f0f3ce647c068efc2c91ce7fa` |

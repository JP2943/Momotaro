# 証跡：P7 会話と依頼と章進行（対象コード `d3c75b4`）

記録の本文は `P7_統合受入結果.md` の記録 002。実行はすべて Unity 6000.3.20f1 の Editor 常駐ブリッジから行った。
`hq1`〜`hq7` と `p6a`〜`p6c` の実ビルド確認は `1cd09a7` のコードで走らせた（`d3c75b4` との差は実ビルド確認の失敗時の書き出しと P7 の Scene の再生成だけ。記録 002 §6）。

| ファイル | 内容 |
|---|---|
| `runs/p7e0full.json` | 着手時の EditMode 全件 1918／1918（記録 001） |
| `runs/ge9.json` | EditMode 全件 1943／1943（`d3c75b4`） |
| `runs/hq1.json`〜`hq7.json`・`hq2b.json` | PlayMode 7 分割（64・13・72・115・20・9・6）。`hq2b` は `P55RoundTripPlayTests` の再実行 |
| `runs/hq2.json`・`hq2r.json` | `P55RoundTripPlayTests` の 1 本の間欠失敗（12／13）と、その 1 本だけの再実行（1／1）。後続課題 J02 |
| `runs/hq8b.json`・`hq8c.json` | `P7WorldPlayTests` 8／8（ワールド再生成の後・`d3c75b4`） |
| `runs/t2.json` | P7 の EditMode 25／25 |
| `runs/inj1.json`〜`inj3.json` | 修正を外した版（同時死亡の分岐、会話の保持と同フレーム被弾の監視、押下の解放待ち）。それぞれ対応するテストが失敗 |
| `results/vfP4.json`〜`vfP7.json` | 必須一覧の照合（226・93・293・73・33・33・33。`ge9,hq1,hq2b,hq3,hq4,hq5,hq6,hq7,hq8c`。未説明の Skip なし） |
| `results/validate_phase6_world.json` ほか 3 件 | 世界検査（P6A・P6B・P6C・P7） |
| `results/smoke_p7_sq7b.json`・`sq7e`・`sq7f` | `p7-player-smoke`（24 項目すべて OK。`sq7c`・`sq7d` も OK だったが結果を残していない） |
| `results/smoke_p7_sq7_failed.json` | `p7-player-smoke` の 1 回の NG（A→B のスライドが不成立。J02） |
| `results/smoke_p6a.json`・`smoke_p6b.json`・`smoke_p6c.json` | P6 の実ビルド確認（回帰） |
| `smoke/p7/*` | `sq7f` のプロセスごとの結果（story・continue_after_story）と、`sq7` の失敗した story |
| `smoke/p6a/*`・`smoke/p6b/*`・`smoke/p6c/*` | P6 の実ビルド確認のプロセスごとの結果 |

**改行について**：JSON はブリッジ・実ビルドが書いたバイトのまま置いた（CR は含まない）。`.gitattributes` で `text` 扱いのため、リポジトリの blob と作業ツリーは同じバイトになる。

## sha256（この README の最後の編集の後に採取）

| ファイル | sha256 |
|---|---|
| `results/smoke_p6a.json` | `9f4294058c0976556ae13866fa7de212c10445719fac52186f22ea67b6219351` |
| `results/smoke_p6b.json` | `381ea64eb5c52bc73c4b1ce346ace8a4276c758689c3d8549be773c0b0bf76e5` |
| `results/smoke_p6c.json` | `46979a66b4bf88ee83d6f8f72a8fb2fcd4d0ee1a49184228d10fe77927fb589b` |
| `results/smoke_p7_sq7_failed.json` | `b8aaf9e1e23557151aac9322ba36db9679ff99fb3a13b2b0a63e00a8e6f8726c` |
| `results/smoke_p7_sq7b.json` | `919777865adbd7958810571735e09fa0f88f47aeccc913ecdfe364bbeca34509` |
| `results/smoke_p7_sq7e.json` | `cb6fe396298298ef99a72be8b8fe5a98b090a6ba3ae3883f0f649187f557dd99` |
| `results/smoke_p7_sq7f.json` | `0bcc4453befef6ce238d89a0ce129c1b217e1e70f52b2f2198c896f6500f8133` |
| `results/validate_phase6_world.json` | `4e6b5f076295eb7de47bdd12d6cc45ecd143856c7e528c0338d21fa36543ebf1` |
| `results/validate_phase6b_world.json` | `a1922da56a7e72c53a78b4f44b7d49e501a27d95ba3dbffba9120a7790a0dee5` |
| `results/validate_phase6c_world.json` | `280bc90cdb3b6310acd1d9fc3aac5554ab4427127b6dd60bae88d4de05ab59cd` |
| `results/validate_phase7_world.json` | `e09c613d3499dc3da888205d45d2147f2be9a8dbd4d39962b7526c1b2682265a` |
| `results/vfP4.json` | `3318813e038c14a966dcad6c8fcc278ad3c27c057012ae511ebfb5920ebee9c7` |
| `results/vfP5.json` | `0c54dd5a4e2d72229c81271e97fa8f74af1ef41612cc4fe1f5a65cff19bbc38b` |
| `results/vfP55.json` | `8ea3d811689e024d3d4e67918c0f01d1315ca1f428b12376d2a2d3ca1fd8ea41` |
| `results/vfP6A.json` | `1a48540bded154695bc7dcab6d75054eb639535dc564d471a1db6d4fbac75ed8` |
| `results/vfP6B.json` | `10c3e0e65adbe96a9b45a3864473af6d14725b7a1e452f652d8962ca56041418` |
| `results/vfP6C.json` | `638174e810243098b35c21eb44e7bfd8170eff7aba096510cdbf4427e0265180` |
| `results/vfP7.json` | `84779ae2821f189e132fa9c7ac3b5e5595809768044751d69215bcbee02ff75b` |
| `runs/ge9.json` | `b63fbc88698c9258c14ce9ae25414ba49a7084ef49e1b66f62f493e6869eff0e` |
| `runs/hq1.json` | `2073fd8dab9db39b18f3f0d83fad7b22e4487b1919f680c9c911278a4d3d9bc0` |
| `runs/hq2.json` | `85be70926829b50bf2a88b9f5364c22eb300d1a5a656d08a60f43059eb185738` |
| `runs/hq2b.json` | `8d4b6310ff61ca73160f24685b6d1e0ad1b2d48dec622a473a006fc52bbd1f2e` |
| `runs/hq2r.json` | `53762e1b0eefcb5a257be688b701b127c61cd0617edf0f06077c7cde2e2c08d7` |
| `runs/hq3.json` | `9e866068eef952212614ae9f7a5b385b9c8aeeb435841db4e1b24c201d3ef5b7` |
| `runs/hq4.json` | `93b7d21f20c3007d97fb4b6ea694a57a426bc42291795eaac7fc40d2624456e4` |
| `runs/hq5.json` | `fb7c4ca3a9d25bfade855112317556a222c69cbcb3bbe85362b76673b74f0fa3` |
| `runs/hq6.json` | `98eb7ebfa6f411bf05114a90a7b5536be3c0406c49fbdbda54a3e36885605688` |
| `runs/hq7.json` | `64785f005d4f9e8cf2ad22efdc2a200f7d238ca89141a89c9086c3803c6a50d3` |
| `runs/hq8b.json` | `71f9c85c1bdab2747ff26e77d515c3080540a7b01b74e9010d4e2ce1f54dd425` |
| `runs/hq8c.json` | `d9c28416a1a66d0a86389325a91cd239e3d4b4b07eafb1bd62d7435e92b8dbe6` |
| `runs/inj1.json` | `116ce853a3533e81ad3c65b5b08b5b63d4ac6790234a7912de8ffb529346a9fa` |
| `runs/inj2.json` | `c39d2e46f37e3b8d0331768c15ccbff5fe7ef1f1e7e26be285b328b748a2d751` |
| `runs/inj3.json` | `ee347b67bebd89f0f7a05ff12a53adcf3f14e00e1e7293b9be697d41e479ef0b` |
| `runs/p7e0full.json` | `115ec250c112f578fec95d55f6e49cd3fcc06043ad828c52a5151f6fcb5e13ab` |
| `runs/t2.json` | `c74da8cd6a8a1b35087532f0cdc54792dc2a266a7b29f14ad195804dd0c830e3` |
| `smoke/p6a/result_close.json` | `19bb4a2c6dee9126e0ef549c3feb86c76a9f26411427c994698cad86e8439aa8` |
| `smoke/p6a/result_continue.json` | `eee76eeaa1a1b931315d78434ed582077bfc2c36a9fa276a53a1a9193241a513` |
| `smoke/p6a/result_continue_after_close.json` | `7edebbf81bcb4b0294014ade1569c6a346758321b734462c340d2e4466f2fe14` |
| `smoke/p6a/result_new.json` | `25bb1108ab5694e1648fd808626d929e532498af5cab3c61149c4a2456f35ff6` |
| `smoke/p6a/result_perf.json` | `24ba42a358bef4f0c90d9dff1541f7399d921a8e44c3d6dd05f5d72668a00c76` |
| `smoke/p6a/result_perf_slow200.json` | `dc0edecf152cab26880ea6f4f579facc5a9df006366368be4fe6b327eb388f28` |
| `smoke/p6a/result_play.json` | `2ca4a0a4f5c5ae8f28a6c6e60c06898c76cf03fa70e93783b1422810e0ac03e8` |
| `smoke/p6a/result_play_nosave.json` | `eae56babfe408d42d8090f78aeef5a982b2523b64aa0c2cfdaf94cda3420fac5` |
| `smoke/p6a/result_play_probe.json` | `cd22f98a216d59a5dfe9abac0121bf8e776790f2d0d8c124cc95549461481c7f` |
| `smoke/p6b/result_continue_after_growth.json` | `62d2ed0993526b25e9b918e40663c0dc447c27db1a7bd10aee6b6afe534aeabd` |
| `smoke/p6b/result_continue_after_useclose_0_6.json` | `cba7b361de483f8672fb6fb14a229048f93d6ec209f92b2b93731345b72a9c67` |
| `smoke/p6b/result_continue_after_useclose_1_7.json` | `75725b1f24008d24ed2f43fa97848ee9930625914bafd9c3eb1b979914ab02d1` |
| `smoke/p6b/result_growth.json` | `73d1f40f87f1b4cac0962ec5f785243cddab3df0b57db0c2d85c21af3b935281` |
| `smoke/p6b/result_useclose_0_6.json` | `83d0803537cf6b69c9b1eb9dc9392cb95d0898ec4f973ee49da6ac227d06b575` |
| `smoke/p6b/result_useclose_1_7.json` | `ef4221d4863e19251e38f2a047c903a97c4d26b2b4a7a07268e3ff6a30531fad` |
| `smoke/p6c/result_continue_after_justevade.json` | `49c2cbac56b3a58ddc81cb7231bd5dbfa0be6fbfd79a24f3ee985576026fcf86` |
| `smoke/p6c/result_justevade.json` | `8b1e0bcc8dbe9600ef6f24dbf03bc84df5f7ec8ffca886bcfec339626a1e0d6c` |
| `smoke/p7/result_continue_after_story.json` | `e48ff48deb886e3092c7a770567399b61bef00b134c001bde1a0b3c5771ce9f4` |
| `smoke/p7/result_story.json` | `cae77b5c96cc75c9e96d65fa26d4640e9a16f43bbf719712a8509e20b0b38a6a` |
| `smoke/p7/result_story_sq7_failed.json` | `ee621d30f89e3667becba4764ab3adfe320b93f3772349e921a7beffd0688755` |

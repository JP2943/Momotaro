using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-05（第1弾）：<see cref="PlayerSlashVfxPresenter"/> が通常攻撃 1 段目の判定区間に同期して剣閃 VFX を表示することを検証する。
    /// 立ち上がりで生成（空振りでも）、Facing で方向選択、Hitbox 中心へ配置、VFX に Collider が無い、段2は非表示、Active 終了で消灯、
    /// プール再利用、素材未割当でも例外なく継続、StopAll で残留なし。P3.5-06：表示位置の高さオフセット・カメラ正対（billboard）・DepthOffset も検証する。
    /// </summary>
    public sealed class PlayerSlashVfxPresenterTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        private sealed class FakeSwing : IAttackSwingSource
        {
            public bool IsSwingHitboxActive { get; set; }
            public int SwingStage { get; set; } = 1;
            public Vector3 SwingCenter { get; set; }
            public Vector3 SwingHalfExtents { get; set; } = Vector3.one;
            public Vector3 SwingForward { get; set; } = Vector3.right;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
        }

        private Sprite MakeSprite(string name)
        {
            var tex = new Texture2D(4, 4);
            _spawned.Add(tex);
            var s = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            s.name = name;
            _spawned.Add(s);
            return s;
        }

        private PlayerSlashVfxPresenter.SlashFrameSet MakeFrameSet(string tag)
        {
            return new PlayerSlashVfxPresenter.SlashFrameSet
            {
                down = new[] { MakeSprite("d0" + tag), MakeSprite("d1" + tag), MakeSprite("d2" + tag) },
                up = new[] { MakeSprite("u0" + tag), MakeSprite("u1" + tag), MakeSprite("u2" + tag) },
                left = new[] { MakeSprite("l0" + tag), MakeSprite("l1" + tag), MakeSprite("l2" + tag) },
                right = new[] { MakeSprite("r0" + tag), MakeSprite("r1" + tag), MakeSprite("r2" + tag) },
            };
        }

        private PlayerSlashVfxPresenter NewPresenter(out FakeSwing src, bool assignFrames = true)
        {
            var go = new GameObject("Presenter");
            _spawned.Add(go);
            var p = go.AddComponent<PlayerSlashVfxPresenter>();
            if (assignFrames)
            {
                p.Stage1Frames = MakeFrameSet("a");
                p.Stage2Frames = MakeFrameSet("b");
                p.Stage3Frames = MakeFrameSet("c");
                p.SpecialFrames = MakeFrameSet("s");
            }

            src = new FakeSwing();
            p.Bind(src);
            return p;
        }

        private static SlashVfxInstance Playing(SlashVfxPool pool)
        {
            for (int i = 0; i < pool.Instances.Count; i++)
            {
                if (pool.Instances[i].IsPlaying) return pool.Instances[i];
            }

            return null;
        }

        [Test]
        public void RisingEdge_SpawnsSlash_AtCenter_ForRightFacing()
        {
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            // 基準配置＝SwingCenter を検証するため補正を 0 に（高さ0・深度0・引き戻し0なら位置は SwingCenter に一致：カメラ有無に非依存）。
            p.SlashHeightOffset = 0f;
            p.DepthOffset = 0f;
            p.VfxForwardPull = 0f;
            src.IsSwingHitboxActive = true;
            src.SwingStage = 1;
            src.SwingCenter = new Vector3(1f, 0.5f, 0f);
            src.SwingForward = Vector3.right;

            p.Tick(0.01f);

            Assert.AreEqual(1, p.Pool.ActiveCount, "判定立ち上がりで剣閃を 1 つ生成。");
            SlashVfxInstance inst = Playing(p.Pool);
            Assert.IsNotNull(inst);
            Assert.AreEqual(new Vector3(1f, 0.5f, 0f), inst.transform.position, "補正0では Hitbox 中心へ配置。");
            Assert.AreEqual("r0a", inst.CurrentSprite.name, "1段目・Right 方向の素材を選択。");
        }

        [Test]
        public void Spawn_BillboardsToCamera_AppliesHeightAndDepth()
        {
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            var camGo = new GameObject("Cam");
            _spawned.Add(camGo);
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 12f, -14f), Quaternion.Euler(45f, 0f, 0f));
            var cam = camGo.AddComponent<Camera>();
            p.SetCamera(cam);
            p.SlashHeightOffset = 0.9f;
            p.DepthOffset = 0.5f;
            p.VfxForwardPull = 0f; // billboard/高さ/深度のみ検証（引き戻しは別テスト）。

            src.IsSwingHitboxActive = true;
            src.SwingCenter = new Vector3(1f, 0.2f, 3f);
            src.SwingForward = Vector3.right;

            p.Tick(0.01f);

            SlashVfxInstance inst = Playing(p.Pool);
            Assert.IsNotNull(inst);

            Vector3 anchor = new Vector3(1f, 0.2f + 0.9f, 3f);
            Vector3 expected = anchor - cam.transform.forward * 0.5f;
            Assert.AreEqual(expected.x, inst.transform.position.x, 1e-4f, "高さ＋カメラ側 DepthOffset を反映(x)。");
            Assert.AreEqual(expected.y, inst.transform.position.y, 1e-4f, "高さ＋カメラ側 DepthOffset を反映(y)。");
            Assert.AreEqual(expected.z, inst.transform.position.z, 1e-4f, "高さ＋カメラ側 DepthOffset を反映(z)。");
            Assert.Less(Quaternion.Angle(inst.transform.rotation, cam.transform.rotation), 0.01f, "カメラへ正対（billboard）。");
        }

        [Test]
        public void Spawn_ForwardPull_MovesVfxTowardPlayer()
        {
            // 判定は不変で、表示だけ攻撃方向へ引き戻して手元へ寄せる（P3.5-06。Down 以外が離れて見える症状の対策）。
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            p.SlashHeightOffset = 0f;
            p.DepthOffset = 0f;
            p.VfxForwardPull = 0.3f;
            src.IsSwingHitboxActive = true;
            src.SwingCenter = new Vector3(1f, 0f, 0f);
            src.SwingForward = Vector3.right; // +X

            p.Tick(0.01f);

            SlashVfxInstance inst = Playing(p.Pool);
            Assert.IsNotNull(inst);
            // 判定中心(1,0,0) から Forward(+X) へ 0.3 引き戻し → (0.7,0,0)。
            Assert.AreEqual(0.7f, inst.transform.position.x, 1e-4f, "攻撃方向へ引き戻して手元へ寄る。");
            Assert.AreEqual(0f, inst.transform.position.z, 1e-4f);
        }

        [Test]
        public void HoldThroughRecovery_KeepsVfx_AfterActiveEnds_UntilDuration()
        {
            // 3段目のジャンプ切り下ろし相当：判定(Active)終了後も自前 duration まで表示継続し、着地モーション後に消える。
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            p.Stage3Frames.holdThroughRecovery = true;
            p.Stage3Frames.duration = 0.5f;
            src.SwingStage = 3;
            src.IsSwingHitboxActive = true;
            p.Tick(0.01f);
            Assert.AreEqual(1, p.Pool.ActiveCount, "判定立ち上がりで生成。");

            src.IsSwingHitboxActive = false; // 判定終了（頂点付近）。
            p.Tick(0.05f);
            Assert.AreEqual(1, p.Pool.ActiveCount, "判定終了後も継続（着地まで残す）。");

            p.Tick(0.6f); // 自前 duration 満了。
            Assert.AreEqual(0, p.Pool.ActiveCount, "duration 満了で消灯（着地後）。");
        }

        [Test]
        public void NormalAttack_StopsAtActiveEnd_NotHeld()
        {
            // 通常攻撃（hold なし）は従来どおり判定終了で消灯する（着地継続は 3 段目専用）。
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            src.SwingStage = 1; // Stage1 は holdThroughRecovery=false（既定）。
            src.IsSwingHitboxActive = true;
            p.Tick(0.01f);
            Assert.AreEqual(1, p.Pool.ActiveCount);

            src.IsSwingHitboxActive = false;
            p.Tick(0.01f);
            Assert.AreEqual(0, p.Pool.ActiveCount, "hold なしは判定終了で即消灯。");
        }

        [Test]
        public void Whiff_SpawnsSlash_IndependentOfHit()
        {
            // 命中の有無に依存しない（Presenter は HitResult を参照しない）。空振り相当でも表示する。
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            src.IsSwingHitboxActive = true;
            p.Tick(0.01f);
            Assert.AreEqual(1, p.Pool.ActiveCount);
        }

        [Test]
        public void Vfx_HasNoColliderNorDamage()
        {
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            src.IsSwingHitboxActive = true;
            p.Tick(0.01f);

            SlashVfxInstance inst = Playing(p.Pool);
            Assert.IsNotNull(inst);
            Assert.IsNull(inst.GetComponent<Collider>(), "剣閃 VFX に Collider を付けない。");
            Assert.IsNull(inst.GetComponentInChildren<Collider>(true), "子にも Collider を持たない。");
        }

        [Test]
        public void UpAndDownFacing_SelectCorrectFrames()
        {
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            src.SwingForward = Vector3.forward; // +Z = Up
            src.IsSwingHitboxActive = true;
            p.Tick(0.01f);
            Assert.AreEqual("u0a", Playing(p.Pool).CurrentSprite.name, "Up=+Z。");

            // 判定終了→再度立ち上げ（Down）。
            src.IsSwingHitboxActive = false;
            p.Tick(0.01f);
            src.SwingForward = Vector3.back; // -Z = Down
            src.IsSwingHitboxActive = true;
            p.Tick(0.01f);
            Assert.AreEqual("d0a", Playing(p.Pool).CurrentSprite.name, "Down=-Z。");
        }

        [Test]
        public void Stage2_SpawnsWithStage2Frames()
        {
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            src.IsSwingHitboxActive = true;
            src.SwingStage = 2; // 2 段目（Slash_Small_B）。
            src.SwingForward = Vector3.right;

            p.Tick(0.01f);

            Assert.AreEqual(1, p.Pool.ActiveCount, "2 段目でも剣閃を表示する。");
            Assert.AreEqual("r0b", Playing(p.Pool).CurrentSprite.name, "2 段目は 2 段目用素材を選択。");
        }

        [Test]
        public void Stage3_SpawnsWithStage3Frames()
        {
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            src.IsSwingHitboxActive = true;
            src.SwingStage = 3; // 3 段目（Slash_Small_C）。
            src.SwingForward = Vector3.right;

            p.Tick(0.01f);

            Assert.AreEqual(1, p.Pool.ActiveCount, "3 段目でも剣閃を表示する。");
            Assert.AreEqual("r0c", Playing(p.Pool).CurrentSprite.name, "3 段目は 3 段目用素材を選択。");
        }

        [Test]
        public void SpecialSwing_SpawnsWithSpecialFrames()
        {
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            src.IsSwingHitboxActive = true;
            src.SwingStage = AttackSwing.SpecialStage; // 必殺技（Slash_Special_A）。
            src.SwingForward = Vector3.right;

            p.Tick(0.01f);

            Assert.AreEqual(1, p.Pool.ActiveCount, "必殺技でも剣閃を表示する。");
            Assert.AreEqual("r0s", Playing(p.Pool).CurrentSprite.name, "必殺技は必殺技用素材を選択。");
        }

        [Test]
        public void Stage4_DoesNotSpawn_AssetsInProduction()
        {
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            src.IsSwingHitboxActive = true;
            src.SwingStage = 4; // 4 段目以降・必殺技は素材制作中／存在しない。

            p.Tick(0.01f);

            Assert.AreEqual(0, p.Pool.ActiveCount, "4 段目以降は表示しない。");
            Assert.AreEqual(0, p.Pool.TotalCount, "生成もしない。");
        }

        [Test]
        public void ActiveEnd_StopsSlash_NoLingering()
        {
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            src.IsSwingHitboxActive = true;
            p.Tick(0.01f);
            Assert.AreEqual(1, p.Pool.ActiveCount);

            src.IsSwingHitboxActive = false; // 判定終了。
            p.Tick(0.01f);
            Assert.AreEqual(0, p.Pool.ActiveCount, "判定終了で剣閃を消灯（残留なし）。");
        }

        [Test]
        public void CompletedSlash_IsReused_NoNewInstance()
        {
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            src.IsSwingHitboxActive = true;
            p.Tick(0.01f);
            Assert.AreEqual(1, p.Pool.TotalCount);

            p.Tick(0.12f); // 表示時間満了で完了。
            Assert.AreEqual(0, p.Pool.ActiveCount);

            src.IsSwingHitboxActive = false;
            p.Tick(0.01f);
            src.IsSwingHitboxActive = true; // 次の攻撃。
            p.Tick(0.01f);

            Assert.AreEqual(1, p.Pool.ActiveCount);
            Assert.AreEqual(1, p.Pool.TotalCount, "完了インスタンスを再利用し新規生成しない。");
        }

        [Test]
        public void UnassignedFrames_NoSpawn_NoException()
        {
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src, assignFrames: false);
            src.IsSwingHitboxActive = true;

            Assert.DoesNotThrow(() => p.Tick(0.01f), "素材未割当でも例外なく継続。");
            Assert.AreEqual(0, p.Pool.ActiveCount);
        }

        [Test]
        public void StopAll_ClearsActive()
        {
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            src.IsSwingHitboxActive = true;
            p.Tick(0.01f);
            Assert.AreEqual(1, p.Pool.ActiveCount);

            p.StopAll();
            Assert.AreEqual(0, p.Pool.ActiveCount, "Disable/Scene 離脱相当で全消灯。");
        }

        [Test]
        public void Spawn_AppliesPlayerSlashColor_AsTint()
        {
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            var color = new Color(0.2f, 0.4f, 0.9f, 0.8f);
            p.PlayerSlashColor = color;
            src.IsSwingHitboxActive = true;

            p.Tick(0.01f);

            SlashVfxInstance inst = Playing(p.Pool);
            Assert.IsNotNull(inst);
            Assert.AreEqual(color, inst.CurrentColor, "主人公の剣閃色（Tint）をインスタンスへ適用する。");
        }

        [Test]
        public void ReusedInstance_ResetsColor_NoResidualTint()
        {
            // 別色で 2 回発生させ、2 回目に前回色が残らない（再利用時に毎回色を設定する）ことを確認する。
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            p.PlayerSlashColor = new Color(1f, 0f, 0f, 1f);
            src.IsSwingHitboxActive = true;
            p.Tick(0.01f);
            p.Tick(0.2f); // 満了して完了→プールへ戻る。
            src.IsSwingHitboxActive = false;
            p.Tick(0.01f);

            var second = new Color(0f, 1f, 0f, 1f);
            p.PlayerSlashColor = second;
            src.IsSwingHitboxActive = true;
            p.Tick(0.01f);

            Assert.AreEqual(1, p.Pool.TotalCount, "同じインスタンスを再利用。");
            Assert.AreEqual(second, Playing(p.Pool).CurrentColor, "再利用時に前回色が残らない。");
        }

        [Test]
        public void PerSetDuration_ControlsPlaybackLength()
        {
            // 段ごとに再生時間を変えられる：Stage1 を 0.3 秒に設定すると 0.12 秒では完了しない。
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            p.Stage1Frames.duration = 0.3f;
            src.IsSwingHitboxActive = true;
            src.SwingStage = 1;
            p.Tick(0.01f);
            Assert.AreEqual(1, p.Pool.ActiveCount);

            p.Tick(0.12f); // 既定(0.12)なら完了する時間。0.3 設定では継続。
            Assert.AreEqual(1, p.Pool.ActiveCount, "素材セットごとの再生時間で長さを制御する。");

            p.Tick(0.3f); // 0.13+0.3 > 0.3 → 完了。
            Assert.AreEqual(0, p.Pool.ActiveCount, "設定時間の満了で完了する。");
        }

        [Test]
        public void ZeroDuration_DoesNotPlayForever_NoException()
        {
            // Duration<=0 でも無限再生・例外にならない（極小時間へ丸めて 1 フレームで完了）。
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            p.Stage1Frames.duration = 0f;
            src.IsSwingHitboxActive = true;
            src.SwingStage = 1;
            p.Tick(0.01f);

            Assert.DoesNotThrow(() => p.Tick(0.01f));
            Assert.AreEqual(0, p.Pool.ActiveCount, "Duration<=0 は無限再生せず即完了する。");
        }
    }
}

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
    /// プール再利用、素材未割当でも例外なく継続、StopAll で残留なし（仕様書 §6/§7.2、テスト §5 [219]/[220]/[223]）。
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

        private PlayerSlashVfxPresenter.SlashFrameSet MakeFrameSet()
        {
            return new PlayerSlashVfxPresenter.SlashFrameSet
            {
                down = new[] { MakeSprite("d0"), MakeSprite("d1"), MakeSprite("d2") },
                up = new[] { MakeSprite("u0"), MakeSprite("u1"), MakeSprite("u2") },
                left = new[] { MakeSprite("l0"), MakeSprite("l1"), MakeSprite("l2") },
                right = new[] { MakeSprite("r0"), MakeSprite("r1"), MakeSprite("r2") },
            };
        }

        private PlayerSlashVfxPresenter NewPresenter(out FakeSwing src, bool assignFrames = true)
        {
            var go = new GameObject("Presenter");
            _spawned.Add(go);
            var p = go.AddComponent<PlayerSlashVfxPresenter>();
            if (assignFrames)
            {
                p.Stage1Frames = MakeFrameSet();
            }

            p.SlashDuration = 0.12f;
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
            src.IsSwingHitboxActive = true;
            src.SwingStage = 1;
            src.SwingCenter = new Vector3(1f, 0.5f, 0f);
            src.SwingForward = Vector3.right;

            p.Tick(0.01f);

            Assert.AreEqual(1, p.Pool.ActiveCount, "判定立ち上がりで剣閃を 1 つ生成。");
            SlashVfxInstance inst = Playing(p.Pool);
            Assert.IsNotNull(inst);
            Assert.AreEqual(new Vector3(1f, 0.5f, 0f), inst.transform.position, "Hitbox 中心へ配置。");
            Assert.AreEqual("r0", inst.CurrentSprite.name, "Right 方向の素材を選択。");
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
            Assert.AreEqual("u0", Playing(p.Pool).CurrentSprite.name, "Up=+Z。");

            // 判定終了→再度立ち上げ（Down）。
            src.IsSwingHitboxActive = false;
            p.Tick(0.01f);
            src.SwingForward = Vector3.back; // -Z = Down
            src.IsSwingHitboxActive = true;
            p.Tick(0.01f);
            Assert.AreEqual("d0", Playing(p.Pool).CurrentSprite.name, "Down=-Z。");
        }

        [Test]
        public void Stage2_DoesNotSpawn_AssetsInProduction()
        {
            PlayerSlashVfxPresenter p = NewPresenter(out FakeSwing src);
            src.IsSwingHitboxActive = true;
            src.SwingStage = 2; // 2 段目は素材制作中。

            p.Tick(0.01f);

            Assert.AreEqual(0, p.Pool.ActiveCount, "1 段目以外は表示しない。");
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
    }
}

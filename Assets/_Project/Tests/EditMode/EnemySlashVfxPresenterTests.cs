using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-05：<see cref="EnemySlashVfxPresenter"/> が敵（近接骸骨）の攻撃判定区間に同期して剣閃を表示することを検証する。
    /// 複数体の同時攻撃、判定立ち上がりでの生成（空振りでも）、通常/強/ガード不能の段別（未割当は無処理）、突進/投射（段0）非表示、
    /// Collider 無し、Active 終了消灯、プール共有・再利用、撃破（破棄）観測元の追跡解除、StopAll での残留なしを確認する。
    /// </summary>
    public sealed class EnemySlashVfxPresenterTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        private sealed class FakeSwing : IAttackSwingSource
        {
            public bool IsSwingHitboxActive { get; set; }
            public int SwingStage { get; set; } = AttackSwing.EnemyMeleeNormal;
            public Vector3 SwingCenter { get; set; }
            public Vector3 SwingHalfExtents { get; set; } = Vector3.one;
            public Vector3 SwingForward { get; set; } = Vector3.right;
        }

        private sealed class FakeSwingBehaviour : MonoBehaviour, IAttackSwingSource
        {
            public bool IsSwingHitboxActive { get; set; }
            public int SwingStage { get; set; } = AttackSwing.EnemyMeleeNormal;
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

        private EnemySlashVfxPresenter.SlashFrameSet MakeFrameSet()
        {
            return new EnemySlashVfxPresenter.SlashFrameSet
            {
                down = new[] { MakeSprite("d0"), MakeSprite("d1"), MakeSprite("d2") },
                up = new[] { MakeSprite("u0"), MakeSprite("u1"), MakeSprite("u2") },
                left = new[] { MakeSprite("l0"), MakeSprite("l1"), MakeSprite("l2") },
                right = new[] { MakeSprite("r0"), MakeSprite("r1"), MakeSprite("r2") },
            };
        }

        private EnemySlashVfxPresenter NewPresenter(bool assignFrames = true)
        {
            var go = new GameObject("EnemyPresenter");
            _spawned.Add(go);
            var p = go.AddComponent<EnemySlashVfxPresenter>();
            if (assignFrames)
            {
                p.NormalFrames = MakeFrameSet();
            }

            return p;
        }

        private static SlashVfxInstance FirstPlaying(SlashVfxPool pool)
        {
            for (int i = 0; i < pool.Instances.Count; i++)
            {
                if (pool.Instances[i].IsPlaying) return pool.Instances[i];
            }

            return null;
        }

        [Test]
        public void RisingEdge_SpawnsSlash_AtCenter_ForFacing()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var src = new FakeSwing { IsSwingHitboxActive = true, SwingCenter = new Vector3(2f, 0.5f, 0f), SwingForward = Vector3.right };
            p.Bind(new IAttackSwingSource[] { src });

            p.Tick(0.01f);

            Assert.AreEqual(1, p.Pool.ActiveCount);
            SlashVfxInstance inst = FirstPlaying(p.Pool);
            Assert.AreEqual(new Vector3(2f, 0.5f, 0f), inst.transform.position);
            Assert.AreEqual("r0", inst.CurrentSprite.name, "Right 方向の素材を選択。");
        }

        [Test]
        public void TwoEnemies_EachGetOwnSlash()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var a = new FakeSwing { IsSwingHitboxActive = true };
            var b = new FakeSwing { IsSwingHitboxActive = true };
            p.Bind(new IAttackSwingSource[] { a, b });

            p.Tick(0.01f);

            Assert.AreEqual(2, p.Pool.ActiveCount, "複数体それぞれに剣閃を出す。");
        }

        [Test]
        public void OneEnds_OtherContinues()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var a = new FakeSwing { IsSwingHitboxActive = true };
            var b = new FakeSwing { IsSwingHitboxActive = true };
            p.Bind(new IAttackSwingSource[] { a, b });
            p.Tick(0.01f);
            Assert.AreEqual(2, p.Pool.ActiveCount);

            a.IsSwingHitboxActive = false; // 一体の判定終了。
            p.Tick(0.01f);
            Assert.AreEqual(1, p.Pool.ActiveCount, "終了した敵の剣閃のみ消灯。");
        }

        [Test]
        public void ChargeOrProjectile_Stage0_DoesNotSpawn()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var src = new FakeSwing { IsSwingHitboxActive = true, SwingStage = 0 }; // 突進/投射相当。
            p.Bind(new IAttackSwingSource[] { src });

            p.Tick(0.01f);

            Assert.AreEqual(0, p.Pool.ActiveCount, "非スラッシュ攻撃は剣閃を出さない。");
        }

        [Test]
        public void HeavyUnassigned_DoesNotSpawn_AssetsInProduction()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var src = new FakeSwing { IsSwingHitboxActive = true, SwingStage = AttackSwing.EnemyMeleeHeavy };
            p.Bind(new IAttackSwingSource[] { src });

            Assert.DoesNotThrow(() => p.Tick(0.01f));
            Assert.AreEqual(0, p.Pool.ActiveCount, "強は素材未割当のため表示しない（無処理継続）。");
        }

        [Test]
        public void Vfx_HasNoCollider()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var src = new FakeSwing { IsSwingHitboxActive = true };
            p.Bind(new IAttackSwingSource[] { src });
            p.Tick(0.01f);

            SlashVfxInstance inst = FirstPlaying(p.Pool);
            Assert.IsNull(inst.GetComponentInChildren<Collider>(true), "敵剣閃も Collider を持たない。");
        }

        [Test]
        public void SharedPool_ReusesInstances()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var src = new FakeSwing { IsSwingHitboxActive = true };
            p.Bind(new IAttackSwingSource[] { src });

            p.Tick(0.01f);
            Assert.AreEqual(1, p.Pool.TotalCount);
            p.Tick(0.12f); // 完了。
            Assert.AreEqual(0, p.Pool.ActiveCount);

            src.IsSwingHitboxActive = false;
            p.Tick(0.01f);
            src.IsSwingHitboxActive = true; // 次の攻撃。
            p.Tick(0.01f);

            Assert.AreEqual(1, p.Pool.ActiveCount);
            Assert.AreEqual(1, p.Pool.TotalCount, "完了インスタンスを再利用し新規生成しない。");
        }

        [Test]
        public void DestroyedSource_IsUntracked_AndSlashStopped()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var go = new GameObject("Enemy");
            _spawned.Add(go);
            var b = go.AddComponent<FakeSwingBehaviour>();
            b.IsSwingHitboxActive = true;
            p.Bind(new IAttackSwingSource[] { b });
            p.Tick(0.01f);
            Assert.AreEqual(1, p.Pool.ActiveCount);

            Object.DestroyImmediate(go); // 撃破・破棄。

            Assert.DoesNotThrow(() => p.Tick(0.01f));
            Assert.AreEqual(0, p.Pool.ActiveCount, "破棄された敵の剣閃は消灯し追跡解除（残留なし）。");
        }

        [Test]
        public void StopAll_ClearsActive()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var src = new FakeSwing { IsSwingHitboxActive = true };
            p.Bind(new IAttackSwingSource[] { src });
            p.Tick(0.01f);
            Assert.AreEqual(1, p.Pool.ActiveCount);

            p.StopAll();
            Assert.AreEqual(0, p.Pool.ActiveCount);
        }
    }
}

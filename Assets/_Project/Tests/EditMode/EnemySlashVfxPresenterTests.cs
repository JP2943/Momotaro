using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-05：<see cref="EnemySlashVfxPresenter"/> が敵の攻撃判定区間に同期して剣閃を表示することを検証する。
    /// 敵タイプ鍵（Small/Medium）×攻撃分類（通常/強/ガード不能）での素材選択、複数体の同時攻撃、判定立ち上がりでの生成（空振りでも）、
    /// 未登録鍵・未割当分類・突進/投射（段0）の非表示、Collider 無し、Active 終了消灯、プール共有・再利用、撃破（破棄）追跡解除、StopAll を確認する。
    /// P3.5-06：表示位置の高さオフセット・カメラ正対（billboard）・DepthOffset も検証する。
    /// </summary>
    public sealed class EnemySlashVfxPresenterTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        private sealed class FakeSwing : IAttackSwingSource, IEnemySlashVisual
        {
            public bool IsSwingHitboxActive { get; set; }
            public int SwingStage { get; set; } = AttackSwing.EnemyMeleeNormal;
            public Vector3 SwingCenter { get; set; }
            public Vector3 SwingHalfExtents { get; set; } = Vector3.one;
            public Vector3 SwingForward { get; set; } = Vector3.right;
            public string SlashVfxKey { get; set; } = "Small";
        }

        private sealed class FakeSwingBehaviour : MonoBehaviour, IAttackSwingSource, IEnemySlashVisual
        {
            public bool IsSwingHitboxActive { get; set; }
            public int SwingStage { get; set; } = AttackSwing.EnemyMeleeNormal;
            public Vector3 SwingCenter { get; set; }
            public Vector3 SwingHalfExtents { get; set; } = Vector3.one;
            public Vector3 SwingForward { get; set; } = Vector3.right;
            public string SlashVfxKey { get; set; } = "Small";
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

        private EnemySlashVfxPresenter.SlashFrameSet MakeFrameSet(string tag)
        {
            return new EnemySlashVfxPresenter.SlashFrameSet
            {
                down = new[] { MakeSprite("d0" + tag), MakeSprite("d1" + tag), MakeSprite("d2" + tag) },
                up = new[] { MakeSprite("u0" + tag), MakeSprite("u1" + tag), MakeSprite("u2" + tag) },
                left = new[] { MakeSprite("l0" + tag), MakeSprite("l1" + tag), MakeSprite("l2" + tag) },
                right = new[] { MakeSprite("r0" + tag), MakeSprite("r1" + tag), MakeSprite("r2" + tag) },
            };
        }

        // Small=通常のみ、Medium=通常/強/ガード不能 を登録。
        private EnemySlashVfxPresenter NewPresenter(bool assign = true)
        {
            var go = new GameObject("EnemyPresenter");
            _spawned.Add(go);
            var p = go.AddComponent<EnemySlashVfxPresenter>();
            if (assign)
            {
                p.Entries = new[]
                {
                    new EnemySlashVfxPresenter.EnemySlashEntry { key = "Small", normal = MakeFrameSet("s") },
                    new EnemySlashVfxPresenter.EnemySlashEntry { key = "Medium", normal = MakeFrameSet("m"), heavy = MakeFrameSet("mh"), unblockable = MakeFrameSet("mu") },
                };
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
        public void SmallKey_UsesSmallFrames_AtCenterForFacing()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            // 基準配置＝SwingCenter を検証するため補正を 0 に（高さ0・深度0なら位置は SwingCenter に一致：カメラ有無に非依存）。
            p.SlashHeightOffset = 0f;
            p.DepthOffset = 0f;
            var src = new FakeSwing { IsSwingHitboxActive = true, SlashVfxKey = "Small", SwingCenter = new Vector3(2f, 0.5f, 0f), SwingForward = Vector3.right };
            p.Bind(new IAttackSwingSource[] { src });

            p.Tick(0.01f);

            Assert.AreEqual(1, p.Pool.ActiveCount);
            SlashVfxInstance inst = FirstPlaying(p.Pool);
            Assert.AreEqual(new Vector3(2f, 0.5f, 0f), inst.transform.position, "補正0では Hitbox 中心へ配置。");
            Assert.AreEqual("r0s", inst.CurrentSprite.name, "Small・Right の素材を選択。");
        }

        [Test]
        public void Spawn_BillboardsToCamera_AppliesHeightAndDepth()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var camGo = new GameObject("Cam");
            _spawned.Add(camGo);
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 12f, -14f), Quaternion.Euler(45f, 0f, 0f));
            var cam = camGo.AddComponent<Camera>();
            p.SetCamera(cam);
            p.SlashHeightOffset = 0.8f;
            p.DepthOffset = 0.4f;

            var src = new FakeSwing { IsSwingHitboxActive = true, SlashVfxKey = "Small", SwingCenter = new Vector3(2f, 0.3f, 1f), SwingForward = Vector3.right };
            p.Bind(new IAttackSwingSource[] { src });

            p.Tick(0.01f);

            SlashVfxInstance inst = FirstPlaying(p.Pool);
            Assert.IsNotNull(inst);
            Vector3 anchor = new Vector3(2f, 0.3f + 0.8f, 1f);
            Vector3 expected = anchor - cam.transform.forward * 0.4f;
            Assert.AreEqual(expected.x, inst.transform.position.x, 1e-4f);
            Assert.AreEqual(expected.y, inst.transform.position.y, 1e-4f);
            Assert.AreEqual(expected.z, inst.transform.position.z, 1e-4f);
            Assert.Less(Quaternion.Angle(inst.transform.rotation, cam.transform.rotation), 0.01f, "敵剣閃もカメラへ正対（billboard）。");
        }

        [Test]
        public void MediumKey_UsesMediumFrames()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var src = new FakeSwing { IsSwingHitboxActive = true, SlashVfxKey = "Medium", SwingForward = Vector3.right };
            p.Bind(new IAttackSwingSource[] { src });

            p.Tick(0.01f);

            Assert.AreEqual(1, p.Pool.ActiveCount);
            Assert.AreEqual("r0m", FirstPlaying(p.Pool).CurrentSprite.name, "侍骸骨(Medium)は Medium 素材を選択。");
        }

        [Test]
        public void MixedEnemies_UseOwnTypeFrames()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var small = new FakeSwing { IsSwingHitboxActive = true, SlashVfxKey = "Small", SwingForward = Vector3.right };
            var medium = new FakeSwing { IsSwingHitboxActive = true, SlashVfxKey = "Medium", SwingForward = Vector3.right };
            p.Bind(new IAttackSwingSource[] { small, medium });

            p.Tick(0.01f);

            Assert.AreEqual(2, p.Pool.ActiveCount, "小型・中型それぞれに剣閃を出す。");
            var names = new HashSet<string>();
            for (int i = 0; i < p.Pool.Instances.Count; i++)
            {
                if (p.Pool.Instances[i].IsPlaying) names.Add(p.Pool.Instances[i].CurrentSprite.name);
            }

            Assert.IsTrue(names.Contains("r0s") && names.Contains("r0m"), "各敵タイプが自分の素材を使う。");
        }

        [Test]
        public void UnknownKey_DoesNotSpawn()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var src = new FakeSwing { IsSwingHitboxActive = true, SlashVfxKey = "Boss" }; // 未登録タイプ。
            p.Bind(new IAttackSwingSource[] { src });

            Assert.DoesNotThrow(() => p.Tick(0.01f));
            Assert.AreEqual(0, p.Pool.ActiveCount, "未登録の敵タイプは表示しない（無処理）。");
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

            a.IsSwingHitboxActive = false;
            p.Tick(0.01f);
            Assert.AreEqual(1, p.Pool.ActiveCount);
        }

        [Test]
        public void ChargeOrProjectile_Stage0_DoesNotSpawn()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var src = new FakeSwing { IsSwingHitboxActive = true, SwingStage = 0 };
            p.Bind(new IAttackSwingSource[] { src });

            p.Tick(0.01f);
            Assert.AreEqual(0, p.Pool.ActiveCount, "非スラッシュ攻撃は剣閃を出さない。");
        }

        [Test]
        public void HeavyUnassigned_DoesNotSpawn()
        {
            // Small タイプは強を未割当のため表示しない（未割当分類は無処理）。
            EnemySlashVfxPresenter p = NewPresenter();
            var src = new FakeSwing { IsSwingHitboxActive = true, SlashVfxKey = "Small", SwingStage = AttackSwing.EnemyMeleeHeavy };
            p.Bind(new IAttackSwingSource[] { src });

            Assert.DoesNotThrow(() => p.Tick(0.01f));
            Assert.AreEqual(0, p.Pool.ActiveCount, "Small は強を未割当のため表示しない。");
        }

        [Test]
        public void HeavyAssigned_SpawnsHeavyFrames()
        {
            // Medium(侍骸骨)は強を割当済み（Slash_Enemy_Heavy_A 相当）→ 強攻撃で強用素材を表示する。
            EnemySlashVfxPresenter p = NewPresenter();
            var src = new FakeSwing { IsSwingHitboxActive = true, SlashVfxKey = "Medium", SwingStage = AttackSwing.EnemyMeleeHeavy, SwingForward = Vector3.right };
            p.Bind(new IAttackSwingSource[] { src });

            p.Tick(0.01f);

            Assert.AreEqual(1, p.Pool.ActiveCount, "強攻撃も割り当て済みなら表示する。");
            Assert.AreEqual("r0mh", FirstPlaying(p.Pool).CurrentSprite.name, "強は強用素材を選択。");
        }

        [Test]
        public void UnblockableAssigned_SpawnsUnblockableFrames()
        {
            // Medium(侍骸骨)はガード不能(突き)を割当済み（Thrust_Enemy_Unguardable_A 相当）→ ガード不能攻撃で専用素材を表示。
            EnemySlashVfxPresenter p = NewPresenter();
            var src = new FakeSwing { IsSwingHitboxActive = true, SlashVfxKey = "Medium", SwingStage = AttackSwing.EnemyMeleeUnblockable, SwingForward = Vector3.right };
            p.Bind(new IAttackSwingSource[] { src });

            p.Tick(0.01f);

            Assert.AreEqual(1, p.Pool.ActiveCount, "ガード不能攻撃も割り当て済みなら表示する。");
            Assert.AreEqual("r0mu", FirstPlaying(p.Pool).CurrentSprite.name, "ガード不能は専用素材を選択。");
        }

        [Test]
        public void Vfx_HasNoCollider()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var src = new FakeSwing { IsSwingHitboxActive = true };
            p.Bind(new IAttackSwingSource[] { src });
            p.Tick(0.01f);

            Assert.IsNull(FirstPlaying(p.Pool).GetComponentInChildren<Collider>(true), "敵剣閃も Collider を持たない。");
        }

        [Test]
        public void SharedPool_ReusesInstances()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            var src = new FakeSwing { IsSwingHitboxActive = true };
            p.Bind(new IAttackSwingSource[] { src });

            p.Tick(0.01f);
            Assert.AreEqual(1, p.Pool.TotalCount);
            p.Tick(0.12f);
            Assert.AreEqual(0, p.Pool.ActiveCount);

            src.IsSwingHitboxActive = false;
            p.Tick(0.01f);
            src.IsSwingHitboxActive = true;
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

            Object.DestroyImmediate(go);

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

        [Test]
        public void PerClassColors_AreAppliedAsTint()
        {
            // Medium は通常/強/ガード不能すべて割当済み。攻撃分類ごとに異なる色（Tint）を適用する。
            var nColor = new Color(1f, 0.5f, 0.3f, 1f);
            var hColor = new Color(1f, 0.4f, 0.2f, 1f);
            var uColor = new Color(1f, 0.2f, 0.2f, 1f);
            var go = new GameObject("EnemyPresenter");
            _spawned.Add(go);
            var p = go.AddComponent<EnemySlashVfxPresenter>();
            p.Entries = new[]
            {
                new EnemySlashVfxPresenter.EnemySlashEntry
                {
                    key = "Medium",
                    normal = MakeFrameSet("m"), heavy = MakeFrameSet("mh"), unblockable = MakeFrameSet("mu"),
                    normalColor = nColor, heavyColor = hColor, unblockableColor = uColor,
                },
            };

            // 各分類は前の剣閃が残っていない状態で単独に検証する（StopAll で直前の再生を消してから次を出す）。
            var normal = new FakeSwing { IsSwingHitboxActive = true, SlashVfxKey = "Medium", SwingStage = AttackSwing.EnemyMeleeNormal };
            p.Bind(new IAttackSwingSource[] { normal });
            p.Tick(0.01f);
            Assert.AreEqual(nColor, FirstPlaying(p.Pool).CurrentColor, "通常攻撃は通常色。");
            p.StopAll();

            var heavy = new FakeSwing { IsSwingHitboxActive = true, SlashVfxKey = "Medium", SwingStage = AttackSwing.EnemyMeleeHeavy };
            p.Bind(new IAttackSwingSource[] { heavy });
            p.Tick(0.01f);
            Assert.AreEqual(hColor, FirstPlaying(p.Pool).CurrentColor, "強攻撃は強色。");
            p.StopAll();

            var unblockable = new FakeSwing { IsSwingHitboxActive = true, SlashVfxKey = "Medium", SwingStage = AttackSwing.EnemyMeleeUnblockable };
            p.Bind(new IAttackSwingSource[] { unblockable });
            p.Tick(0.01f);
            Assert.AreEqual(uColor, FirstPlaying(p.Pool).CurrentColor, "ガード不能攻撃はガード不能色。");
        }

        [Test]
        public void PerSetDuration_ControlsPlaybackLength()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            // Small の通常セットを 0.3 秒に。0.12 秒では完了しない。
            p.Entries[0].normal.duration = 0.3f;
            var src = new FakeSwing { IsSwingHitboxActive = true, SlashVfxKey = "Small" };
            p.Bind(new IAttackSwingSource[] { src });
            p.Tick(0.01f);
            Assert.AreEqual(1, p.Pool.ActiveCount);

            p.Tick(0.12f);
            Assert.AreEqual(1, p.Pool.ActiveCount, "素材セットごとの再生時間で長さを制御する。");

            p.Tick(0.3f);
            Assert.AreEqual(0, p.Pool.ActiveCount, "設定時間の満了で完了する。");
        }

        [Test]
        public void ZeroDuration_DoesNotPlayForever_NoException()
        {
            EnemySlashVfxPresenter p = NewPresenter();
            p.Entries[0].normal.duration = 0f;
            var src = new FakeSwing { IsSwingHitboxActive = true, SlashVfxKey = "Small" };
            p.Bind(new IAttackSwingSource[] { src });
            p.Tick(0.01f);

            Assert.DoesNotThrow(() => p.Tick(0.01f));
            Assert.AreEqual(0, p.Pool.ActiveCount, "Duration<=0 は無限再生せず即完了する。");
        }
    }
}

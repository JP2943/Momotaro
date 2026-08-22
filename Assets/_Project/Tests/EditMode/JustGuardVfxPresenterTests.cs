using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-08B：<see cref="JustGuardVfxPresenter"/> が「ジャストガード結果だけ」に反応して閃光を接触点へ 1 回表示し、
    /// 他種別・素材未割当では表示せず、Tick で完了して残留を残さないことを検証する。位置は <see cref="SlashVfxPlacement"/> と
    /// 一致する（接触点＝<see cref="HitResult.HitPoint"/> を billboard・深度補正した点）ことを確認する。
    /// </summary>
    public sealed class JustGuardVfxPresenterTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        private sealed class FakeDamageable : MonoBehaviour, IDamageable
        {
            public int DamageableId { get; set; } = 1;
            public void ReceiveHit(in HitInfo hit) { }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
            Time.timeScale = 1f;
        }

        private Sprite[] MakeFrames(int n)
        {
            var frames = new Sprite[n];
            var tex = new Texture2D(4, 4);
            _spawned.Add(tex);
            for (int i = 0; i < n; i++)
            {
                frames[i] = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                _spawned.Add(frames[i]);
            }

            return frames;
        }

        private JustGuardVfxPresenter NewPresenter(out CombatFeedbackChannel channel, Camera camera, Sprite[] frames)
        {
            var go = new GameObject("JustGuardVfx");
            _spawned.Add(go);
            var p = go.AddComponent<JustGuardVfxPresenter>();
            p.SetCamera(camera);
            p.FlashFrames = frames;
            p.Duration = 0.2f;
            channel = new CombatFeedbackChannel();
            p.Bind(channel);
            return p;
        }

        private static void Publish(CombatFeedbackChannel channel, HitResultKind kind, Vector3 hitPoint)
        {
            HitResult result;
            switch (kind)
            {
                case HitResultKind.JustGuard:
                    result = HitResult.JustGuard(default, null, null, HitDamage.None, hitPoint, Vector3.right);
                    break;
                case HitResultKind.Guard:
                    result = HitResult.Guard(default, null, null, HitDamage.None, hitPoint, Vector3.right);
                    break;
                case HitResultKind.Damage:
                    result = HitResult.Damage(default, null, null, HitDamage.None, hitPoint, Vector3.right);
                    break;
                default:
                    result = HitResult.Evade(default, null, null, hitPoint, Vector3.right);
                    break;
            }

            channel.Publish(new CombatFeedbackEvent(result, CombatFeedbackMap.Resolve(kind)));
        }

        [Test]
        public void JustGuard_SpawnsOneFlash_AtContactPoint()
        {
            var camObj = new GameObject("Cam", typeof(Camera));
            _spawned.Add(camObj);
            camObj.transform.position = new Vector3(0f, 10f, -10f);
            camObj.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            Camera cam = camObj.GetComponent<Camera>();

            JustGuardVfxPresenter p = NewPresenter(out CombatFeedbackChannel ch, cam, MakeFrames(4));
            var hitPoint = new Vector3(3f, 1.2f, 5f);

            Publish(ch, HitResultKind.JustGuard, hitPoint);

            Assert.AreEqual(1, p.ActiveCount, "JG 成立で閃光を 1 つ表示する。");

            SlashVfxPlacement.Compute(hitPoint, cam, 0f, 0.5f, out Vector3 expected, out _);
            SlashVfxInstance inst = null;
            foreach (SlashVfxInstance s in p.Pool.Instances)
            {
                if (s.IsPlaying) { inst = s; break; }
            }

            Assert.IsNotNull(inst, "再生中インスタンスが存在する。");
            Assert.That(Vector3.Distance(inst.transform.position, expected), Is.LessThan(1e-3f),
                "閃光は接触点を billboard・深度補正した表示位置に出る。");
        }

        [Test]
        public void NonJustGuard_DoesNotSpawn()
        {
            JustGuardVfxPresenter p = NewPresenter(out CombatFeedbackChannel ch, null, MakeFrames(4));

            Publish(ch, HitResultKind.Damage, Vector3.zero);
            Publish(ch, HitResultKind.Guard, Vector3.zero);
            Publish(ch, HitResultKind.Evade, Vector3.zero);

            Assert.AreEqual(0, p.ActiveCount, "ダメージ・通常ガード・回避では閃光を出さない（JG 専用）。");
        }

        [Test]
        public void NoFrames_NoSpawn_NoException()
        {
            JustGuardVfxPresenter p = NewPresenter(out CombatFeedbackChannel ch, null, null);

            Assert.DoesNotThrow(() => Publish(ch, HitResultKind.JustGuard, Vector3.zero), "素材未割当でも無例外。");
            Assert.AreEqual(0, p.ActiveCount, "素材未割当では表示しない。");
        }

        [Test]
        public void Tick_CompletesFlash_NoResidual()
        {
            JustGuardVfxPresenter p = NewPresenter(out CombatFeedbackChannel ch, null, MakeFrames(4));
            p.Duration = 0.2f;

            Publish(ch, HitResultKind.JustGuard, Vector3.zero);
            Assert.AreEqual(1, p.ActiveCount);

            p.Tick(0.25f); // 再生時間を超えて進める。

            Assert.AreEqual(0, p.ActiveCount, "再生時間経過で閃光は完了し残留しない。");
            Assert.AreEqual(1, p.Pool.TotalCount, "インスタンスはプールされ再利用される（破棄しない）。");
        }

        [Test]
        public void Reused_NotReallocated_OnSecondJustGuard()
        {
            JustGuardVfxPresenter p = NewPresenter(out CombatFeedbackChannel ch, null, MakeFrames(4));

            Publish(ch, HitResultKind.JustGuard, Vector3.zero);
            p.Tick(0.25f); // 1 回目完了。
            Publish(ch, HitResultKind.JustGuard, new Vector3(1f, 0f, 0f)); // 2 回目。

            Assert.AreEqual(1, p.ActiveCount);
            Assert.AreEqual(1, p.Pool.TotalCount, "2 回目は空きインスタンスを再利用する（新規生成しない）。");
        }

        [Test]
        public void Unbind_StopsReceiving()
        {
            JustGuardVfxPresenter p = NewPresenter(out CombatFeedbackChannel ch, null, MakeFrames(4));

            p.Bind(null); // 購読解除。
            Publish(ch, HitResultKind.JustGuard, Vector3.zero);

            Assert.AreEqual(0, p.ActiveCount, "購読解除後は反応しない。");
        }
    }
}

using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-09：<see cref="PlayerStepSePresenter"/> がステップ（回避）開始の立ち上がりでステップ SE を 1 回鳴らし、
    /// 継続中は再発火せず、終了→再ステップで再発火、SE 未割当でも無例外なことを検証する。
    /// </summary>
    public sealed class PlayerStepSePresenterTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        private sealed class FakeStep : IStepObserver
        {
            public bool Stepping;
            public bool IsStepping => Stepping;
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

        private PlayerStepSePresenter New(out CombatSePlayer se, out FakeStep src, bool withSe = true)
        {
            var go = new GameObject("PlayerStepSe");
            _spawned.Add(go);
            var p = go.AddComponent<PlayerStepSePresenter>();

            se = null;
            if (withSe)
            {
                var seGo = new GameObject("StepSePlayer");
                _spawned.Add(seGo);
                se = seGo.AddComponent<CombatSePlayer>();
                p.Se = se;
            }

            src = new FakeStep();
            p.Bind(src);
            return p;
        }

        [Test]
        public void StepStart_PlaysOnce_HeldDoesNotRefire()
        {
            PlayerStepSePresenter p = New(out CombatSePlayer se, out FakeStep src);

            p.Tick();
            Assert.AreEqual(0, p.PlayCount, "ステップ前は鳴らさない。");

            src.Stepping = true;
            p.Tick();
            Assert.AreEqual(1, p.PlayCount, "ステップ開始で 1 回。");
            Assert.AreEqual("SE_Player_Step", se.LastRequestedSeId);

            p.Tick();
            p.Tick();
            Assert.AreEqual(1, p.PlayCount, "ステップ継続中は再発火しない。");
        }

        [Test]
        public void EndThenStepAgain_Refires()
        {
            PlayerStepSePresenter p = New(out CombatSePlayer se, out FakeStep src);

            src.Stepping = true; p.Tick();
            src.Stepping = false; p.Tick();
            src.Stepping = true; p.Tick();

            Assert.AreEqual(2, p.PlayCount, "終了→再ステップで再発火。");
        }

        [Test]
        public void NoSePlayer_NoException_StillCounts()
        {
            PlayerStepSePresenter p = New(out CombatSePlayer se, out FakeStep src, withSe: false);

            src.Stepping = true;
            Assert.DoesNotThrow(() => p.Tick(), "SE 未割当でも無例外。");
            Assert.AreEqual(1, p.PlayCount);
        }
    }
}

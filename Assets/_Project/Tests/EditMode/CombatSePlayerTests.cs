using System.Collections.Generic;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-05B：<see cref="CombatSePlayer"/> が SeId から差し替えスロットを引き当てて再生要求を出し、未登録・空・Clip 未割当でも
    /// 無音で無例外に継続することを検証する。実 SE 素材が無い前提のため、要求記録（LastRequestedSeId/LastPlayedSeId）で検証する。
    /// </summary>
    public sealed class CombatSePlayerTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
        }

        private CombatSePlayer New()
        {
            var go = new GameObject("Se");
            _spawned.Add(go);
            return go.AddComponent<CombatSePlayer>();
        }

        private static AudioClip MakeClip()
        {
            return AudioClip.Create("t", 64, 1, 8000, false);
        }

        [Test]
        public void Play_WithClip_RecordsPlayed_NoException()
        {
            CombatSePlayer p = New();
            p.Slots = new[]
            {
                new CombatSePlayer.SeSlot { seId = "SE_Hit_Normal", clip = MakeClip(), volume = 1f },
            };

            Assert.DoesNotThrow(() => p.Play("SE_Hit_Normal"));
            Assert.AreEqual("SE_Hit_Normal", p.LastRequestedSeId);
            Assert.AreEqual("SE_Hit_Normal", p.LastPlayedSeId, "Clip 割当済みは再生記録を更新。");
        }

        [Test]
        public void Play_SlotWithoutClip_NoPlayed_NoException()
        {
            CombatSePlayer p = New();
            p.Slots = new[]
            {
                new CombatSePlayer.SeSlot { seId = "SE_Guard", clip = null, volume = 1f },
            };

            Assert.DoesNotThrow(() => p.Play("SE_Guard"));
            Assert.AreEqual("SE_Guard", p.LastRequestedSeId, "要求は記録。");
            Assert.IsNull(p.LastPlayedSeId, "Clip 未割当は無音（再生記録なし）。");
        }

        [Test]
        public void Play_UnknownId_NoException()
        {
            CombatSePlayer p = New();
            p.Slots = new[]
            {
                new CombatSePlayer.SeSlot { seId = "SE_Hit_Normal", clip = MakeClip() },
            };

            Assert.DoesNotThrow(() => p.Play("SE_Unknown"));
            Assert.AreEqual("SE_Unknown", p.LastRequestedSeId);
            Assert.IsNull(p.LastPlayedSeId, "未登録は無処理。");
        }

        [Test]
        public void Play_EmptyOrNull_NoException()
        {
            CombatSePlayer p = New();
            Assert.DoesNotThrow(() => p.Play(string.Empty));
            Assert.DoesNotThrow(() => p.Play(null));
            Assert.IsNull(p.LastPlayedSeId);
        }
    }
}

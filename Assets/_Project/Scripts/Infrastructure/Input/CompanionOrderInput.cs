using System.Collections.Generic;
using Momotaro.Gameplay.Companion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Momotaro.Infrastructure.Input
{
    /// <summary>
    /// 仲間への指示（ついて来い／ここで待て）を試遊中に切り替える入力（P4-08R）。
    ///
    /// P4-07B では指示を出す口が Inspector の Context Menu しか無く、実機で試すことができなかった。
    /// 操作の形（専用キーか、メニューか、方向キー＋モディファイアか）は本編で決める話なので、
    /// ここでは<b>試遊で確かめるための最小の口</b>だけを用意する。1 キーで全員をまとめて切り替える。
    ///
    /// <see cref="CombatRetryInput"/> と同じく Input System の低レベル Device を直接読む。
    /// 会話・Pause で Gameplay Action Map が閉じても入力の形が変わらず、Action 資産を触らずに済むため。
    ///
    /// <b>対象は Scene 構築時に注入する</b>（<c>Find*</c> で探し回らない）。
    /// 探し回る作りにすると、動的に湧いた仲間を拾えるかどうかが実行順に依存する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionOrderInput : MonoBehaviour
    {
        [Tooltip("指示を出す相手（Scene 構築時に注入する）。")]
        [SerializeField] private List<CompanionOrders> _companions = new List<CompanionOrders>();

        [Tooltip("待機／追従を切り替えるキー。")]
        [SerializeField] private Key _toggleKey = Key.T;

        /// <summary>指示の相手の数（テスト・診断用）。</summary>
        public int CompanionCount => _companions.Count;

        /// <summary>これまでに切り替えた回数（テスト・診断用）。</summary>
        public int ToggleCount { get; private set; }

        /// <summary>切り替えキー（Scene 検査・診断用）。</summary>
        public Key ToggleKey => _toggleKey;

        /// <summary>指示の相手を注入する（Scene 構築・テスト。null と重複は無視）。</summary>
        public void Bind(CompanionOrders companion)
        {
            if (companion != null && !_companions.Contains(companion))
            {
                _companions.Add(companion);
            }
        }

        /// <summary>指示の相手を全部外す（Scene 再構築）。</summary>
        public void ClearCompanions() => _companions.Clear();

        /// <summary>
        /// 待機と追従を切り替える（入力を伴わない公開口。テストはここを直接呼べる）。
        ///
        /// <b>まとめて同じ指示にする。</b>1 体ずつ違う状態にできると、キー 1 つでは戻せなくなる。
        /// 誰か 1 人でも追従していれば「全員待機」へ、全員待機なら「全員追従」へ倒す。
        /// </summary>
        public void ToggleAll()
        {
            CompanionOrder next = AnyFollowing() ? CompanionOrder.Wait : CompanionOrder.Follow;
            SetAll(next);
        }

        /// <summary>全員へ同じ指示を出す。1 体でも変わったら true。</summary>
        public bool SetAll(CompanionOrder order)
        {
            bool changed = false;
            for (int i = 0; i < _companions.Count; i++)
            {
                CompanionOrders companion = _companions[i];
                if (companion != null && companion.SetOrder(order))
                {
                    changed = true;
                }
            }

            if (changed)
            {
                ToggleCount++;
            }

            return changed;
        }

        /// <summary>誰か 1 人でも追従しているか。</summary>
        public bool AnyFollowing()
        {
            for (int i = 0; i < _companions.Count; i++)
            {
                CompanionOrders companion = _companions[i];
                if (companion != null && companion.Current == CompanionOrder.Follow)
                {
                    return true;
                }
            }

            return false;
        }

        private void Update()
        {
            // Pause・会話中は指示を受け付けない（行動できない相手へ命令だけ通っても混乱するだけ）。
            // 「いま行動できるか」の正本は活動 Context。ここで別の判定を作らない。
            if (!CompanionActivityProvider.Activity.CanAct)
            {
                return;
            }

            if (TogglePressed())
            {
                ToggleAll();
            }
        }

        private bool TogglePressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb[_toggleKey].wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gp = Gamepad.current;
            return gp != null && gp.buttonNorth.wasPressedThisFrame;
        }
    }
}

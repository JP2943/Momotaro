using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Presentation.Companion
{
    /// <summary>
    /// 仲間の被弾結果チャネルの増減を受け取る側の契約（演出側が実装する）。
    ///
    /// C# の <c>event</c> にしなかったのは、<b>破棄済みの購読者を落とせない</b>ため。
    /// static なイベントはデリゲート経由で相手を掴み続けるので、Scene 破棄や
    /// <c>OnDisable</c> を経ない破棄で解除し損ねると、次の Scene まで生き残って通知を受ける。
    /// 型付きの購読者にしておけば、通知のたびに破棄済みかを見て捨てられる。
    /// </summary>
    public interface ICompanionFeedbackObserver
    {
        /// <summary>チャネルが増えた。</summary>
        void OnCompanionChannelAdded(HitResultChannel channel);

        /// <summary>チャネルが外れた。</summary>
        void OnCompanionChannelRemoved(HitResultChannel channel);
    }

    /// <summary>
    /// 生きている仲間の被弾結果チャネルを集める登録所（P4-FIX F04）。
    ///
    /// 仲間を <c>FindObjectsByType</c> の周期スキャンで拾っていたときは、次の 3 つが保証できなかった。
    /// <list type="number">
    /// <item><description>生成・有効化した直後から次のスキャンまで<b>未購読</b>になる（最初の一撃の演出が出ない）。</description></item>
    /// <item><description>仲間自身を無効化した<b>その瞬間</b>には解除されない。</description></item>
    /// <item><description>破棄済みの GameObject は null 判定で飛ばされるため、<b>保持されたままの旧チャネル</b>から
    /// 購読を外せない。そのチャネルへ誰かが通知すると、次の Scene の演出として流れてしまう。</description></item>
    /// </list>
    ///
    /// そこで「探す」のをやめ、仲間側から明示的に登録・解除する。保持するのはチャネル（ただの C# オブジェクト）で、
    /// Unity の破棄済み判定に左右されない。これは <c>Find*</c> を使わないという規約とも揃う。
    ///
    /// <b>解除は二段構え。</b>正規の経路は登録した側の <c>OnDisable</c>／<c>OnDestroy</c>。
    /// それに加えて、登録時に持ち主（Unity Object）を控えておき、持ち主が破棄されていた項目は
    /// <see cref="Prune"/> で落とす。片方だけに頼らないのは、Scene 破棄やエディタ上の破棄など、
    /// 対称な解除が走らない経路が実在するため（EditMode では <c>OnDestroy</c> が走らない）。
    /// </summary>
    public static class CompanionFeedbackRegistry
    {
        private readonly struct Entry
        {
            /// <summary>被弾結果のチャネル。</summary>
            public HitResultChannel Channel { get; }

            /// <summary>登録した持ち主（破棄検出用。null なら持ち主なしとして落とさない）。</summary>
            public Object Owner { get; }

            public Entry(HitResultChannel channel, Object owner)
            {
                Channel = channel;
                Owner = owner;
            }

            /// <summary>持ち主が破棄済みか（持ち主を渡していない登録は対象外）。</summary>
            public bool IsOrphaned => !ReferenceEquals(Owner, null) && Owner == null;
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static readonly List<ICompanionFeedbackObserver> _observers = new List<ICompanionFeedbackObserver>();

        /// <summary>いま登録されているチャネル（呼ぶたびに新しい配列を作らないよう、内部バッファへ詰め直す）。</summary>
        public static IReadOnlyList<HitResultChannel> Channels
        {
            get
            {
                _channelView.Clear();
                for (int i = 0; i < _entries.Count; i++)
                {
                    // 非活動 Area の仲間の演出を混ぜない（P5.5 §4.3）。
                    // 持ち主を渡さない登録（テストの Fake 等）は Scene に属さないので絞らない。
                    if (!Momotaro.Gameplay.Session.AreaScope.IsVisible(_entries[i].Owner))
                    {
                        continue;
                    }

                    _channelView.Add(_entries[i].Channel);
                }

                return _channelView;
            }
        }

        private static readonly List<HitResultChannel> _channelView = new List<HitResultChannel>();

        /// <summary>登録件数（テスト・診断用）。</summary>
        public static int Count => _entries.Count;

        /// <summary>購読者の数（テスト・診断用。破棄済みは通知時に落ちる）。</summary>
        public static int ObserverCount => _observers.Count;

        /// <summary>増減の通知を受け取る（重複登録はしない）。</summary>
        public static void AddObserver(ICompanionFeedbackObserver observer)
        {
            if (observer == null || _observers.Contains(observer))
            {
                return;
            }

            _observers.Add(observer);
        }

        /// <summary>通知の受け取りをやめる（未登録は無視。二重呼び出し安全）。</summary>
        public static void RemoveObserver(ICompanionFeedbackObserver observer)
        {
            if (observer != null)
            {
                _observers.Remove(observer);
            }
        }

        /// <summary>登録する（同じチャネルの二重登録はしない）。</summary>
        /// <param name="channel">被弾結果のチャネル。</param>
        /// <param name="owner">登録した持ち主。破棄されたら <see cref="Prune"/> で落とすために控える。</param>
        public static void Register(HitResultChannel channel, Object owner = null)
        {
            if (channel == null || IndexOf(channel) >= 0)
            {
                return;
            }

            _entries.Add(new Entry(channel, owner));
            Notify(channel, added: true);
        }

        /// <summary>解除する（未登録は無視。二重呼び出し安全）。</summary>
        public static void Unregister(HitResultChannel channel)
        {
            int index = IndexOf(channel);
            if (index < 0)
            {
                return;
            }

            _entries.RemoveAt(index);
            Notify(channel, added: false);
        }

        /// <summary>
        /// 持ち主が破棄済みの登録を落とす（安全網）。落とした分は解除として通知する。
        /// 購読側は再取得のたびにこれを呼べば、対称な解除が走らなかった経路も回収できる。
        /// </summary>
        public static void Prune()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (!_entries[i].IsOrphaned)
                {
                    continue;
                }

                HitResultChannel channel = _entries[i].Channel;
                _entries.RemoveAt(i);
                Notify(channel, added: false);
            }

            PruneDeadObservers();
        }

        /// <summary>
        /// すべて外す（Scene 再読込・テストの後始末）。
        /// 購読者が残っていても解除通知が飛ぶので、旧 Scene のチャネルを抱えたままにならない。
        /// </summary>
        public static void Clear()
        {
            if (_entries.Count > 0)
            {
                var copy = _entries.ToArray();
                _entries.Clear();

                foreach (Entry entry in copy)
                {
                    Notify(entry.Channel, added: false);
                }
            }

            PruneDeadObservers();
        }

        /// <summary>チャネルも購読者も捨てる（テストの完全初期化用）。</summary>
        public static void ClearAll()
        {
            Clear();
            _observers.Clear();
        }

        private static int IndexOf(HitResultChannel channel)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (ReferenceEquals(_entries[i].Channel, channel))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 購読者へ通知する。<b>破棄済みの購読者はその場で落とす。</b>
        /// 解除し忘れ（Scene 破棄・OnDisable を経ない破棄）で通知が旧 Scene へ流れる事故を、
        /// 登録所の側でも塞いでおく。購読者側の対称な解除に全面的に頼らない。
        /// </summary>
        private static void Notify(HitResultChannel channel, bool added)
        {
            for (int i = _observers.Count - 1; i >= 0; i--)
            {
                ICompanionFeedbackObserver observer = _observers[i];

                if (observer == null || (observer is Object unityObject && unityObject == null))
                {
                    _observers.RemoveAt(i);
                    continue;
                }

                if (added)
                {
                    observer.OnCompanionChannelAdded(channel);
                }
                else
                {
                    observer.OnCompanionChannelRemoved(channel);
                }
            }
        }

        private static void PruneDeadObservers()
        {
            for (int i = _observers.Count - 1; i >= 0; i--)
            {
                ICompanionFeedbackObserver observer = _observers[i];
                if (observer == null || (observer is Object unityObject && unityObject == null))
                {
                    _observers.RemoveAt(i);
                }
            }
        }
    }
}

using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// プレイヤーが仲間へ出す指示（P4-07B）。
    ///
    /// <b>指示は「何をしに行くか」を決めるもので、「殴られたときどうするか」ではない。</b>
    /// だから待機を命じても自衛（ガード・回避）と主人公を庇うことは止めない。
    /// 止めてしまうと、置いていかれた仲間が黙って殴られ続けることになる。
    /// </summary>
    public enum CompanionOrder
    {
        /// <summary>ついて来い（既定）。追従し、敵を見つけたら寄って戦い、暇なら調べに行く。</summary>
        Follow = 0,

        /// <summary>ここで待て。追従・探索・敵への接近をやめてその場に留まる。自衛と守護はする。</summary>
        Wait = 1,
    }

    /// <summary>
    /// 指示が何を許すかの表（P4-07B）。純粋関数なので EditMode で全組合せを確かめられる。
    ///
    /// <b>表を 1 か所に置く理由は F02c と同じ。</b>「待機中は探索しない」を探索側に、
    /// 「待機中は寄っていかない」を戦闘側に散らして書くと、次に指示が増えたとき必ずどこかが抜ける。
    /// 各駆動は「してよいか」だけを尋ね、可否の理由は持たない。
    /// </summary>
    public static class CompanionOrderRules
    {
        /// <summary>隊列へ追従してよいか。</summary>
        public static bool MayFollowLeader(CompanionOrder order) => order != CompanionOrder.Wait;

        /// <summary>暇なときに調べに行ってよいか（P4-07A）。</summary>
        public static bool MayInvestigate(CompanionOrder order) => order != CompanionOrder.Wait;

        /// <summary>敵へ<b>寄っていって</b>よいか。間合いに入ってきた敵への攻撃はこれとは別（下記）。</summary>
        public static bool MayApproachEnemies(CompanionOrder order) => order != CompanionOrder.Wait;

        /// <summary>
        /// 自衛（ガード・回避・間合いの敵への反撃）してよいか。<b>どの指示でも常に true。</b>
        /// 待機を命じられたからといって殴られるに任せるのは指示の意味ではない。
        /// </summary>
        public static bool MayDefendSelf(CompanionOrder order) => true;

        /// <summary>
        /// 主人公を庇ってよいか（P4-05）。<b>どの指示でも常に true。</b>
        /// 待機で離れていれば守護の距離条件で自然に成立しなくなるので、指示側で禁じる必要が無い。
        /// </summary>
        public static bool MayGuardPlayer(CompanionOrder order) => true;
    }

    /// <summary>
    /// 仲間が受けている指示の保持（P4-07B）。1 体に 1 つ置き、各駆動はここへ「してよいか」を尋ねる。
    ///
    /// <b>指示は自動判断より強い。</b>待機中は追従も探索も敵への接近も起きない。
    /// ただし自衛と守護は止めない（<see cref="CompanionOrderRules"/> の表がその境界を持つ）。
    ///
    /// <b>指示の出し方は Phase3 の敵編成ツールに合わせ、Play 中の Context Menu にしてある。</b>
    /// 入力への割り当ては専用試遊環境（P4-08R）で決めることで、
    /// 「まだ形の決まっていない操作」をここで先に固めない（設計の約束）。
    ///
    /// このコンポーネントが付いていない仲間は<b>常に <see cref="CompanionOrder.Follow"/> 扱い</b>になる。
    /// 指示の仕組みを入れる前の構成・テストを、そのまま従来どおり動かすため。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionOrders : MonoBehaviour
    {
        [Tooltip("初期の指示。Scene 構築時にここで決める（Play 中は Context Menu から切り替える）。")]
        [SerializeField] private CompanionOrder _order = CompanionOrder.Follow;

        /// <summary>いま受けている指示。</summary>
        public CompanionOrder Current => _order;

        /// <summary>指示が変わった回数（テスト・診断用）。</summary>
        public int ChangeCount { get; private set; }

        /// <summary>隊列へ追従してよいか。</summary>
        public bool MayFollowLeader => CompanionOrderRules.MayFollowLeader(_order);

        /// <summary>暇なときに調べに行ってよいか。</summary>
        public bool MayInvestigate => CompanionOrderRules.MayInvestigate(_order);

        /// <summary>敵へ寄っていってよいか。</summary>
        public bool MayApproachEnemies => CompanionOrderRules.MayApproachEnemies(_order);

        /// <summary>自衛してよいか（常に true）。</summary>
        public bool MayDefendSelf => CompanionOrderRules.MayDefendSelf(_order);

        /// <summary>主人公を庇ってよいか（常に true）。</summary>
        public bool MayGuardPlayer => CompanionOrderRules.MayGuardPlayer(_order);

        /// <summary>指示を出す。変わったら true（同じ指示の再送は false）。</summary>
        public bool SetOrder(CompanionOrder order)
        {
            if (_order == order)
            {
                return false;
            }

            _order = order;
            ChangeCount++;
            return true;
        }

        /// <summary>指示を既定（ついて来い）へ戻す（加入・Retry・Scene 再構築）。</summary>
        public void ResetOrders()
        {
            _order = CompanionOrder.Follow;
            ChangeCount = 0;
        }

        [ContextMenu("Order: ついて来い")]
        private void OrderFollow() => SetOrder(CompanionOrder.Follow);

        [ContextMenu("Order: ここで待て")]
        private void OrderWait() => SetOrder(CompanionOrder.Wait);
    }
}

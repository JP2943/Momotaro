using Momotaro.Gameplay.Combat;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 守護の転送を受け口が<b>受理確定した直後・被害を解決する前</b>に呼ぶ狭い契約（P4-FIX-R2。c8c0ddf §2.4）。
    ///
    /// 転送の成立は「受け口がその HitId を新しく受理した」ことで決まる。そこで初めて旧攻撃／防御を止めてよく、
    /// 拒否（二重受理・退場・ダウン）なら止めてはいけない。判断と中断を 2 回に分けると、あいだの同期コールバックで
    /// 条件が変わり得るので、受け口が受理を確定したその場所から呼ぶ。
    /// 呼ばれた側は旧行動の中断（状態の割込み）だけを行い、CD・通知は転送が解決し終わってから確定する。
    /// </summary>
    public interface ICompanionTransferAcceptanceHook
    {
        /// <summary>転送された命中を受理した（これから解決する）。</summary>
        void OnTransferAccepted(in HitInfo transferred);
    }
}

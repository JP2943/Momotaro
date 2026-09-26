using Momotaro.Gameplay.Modes;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 遷移の受付条件（仕様書 v1.1 §6.1／P5.5 仕様書 §6.1）を<b>1 か所だけ</b>に置く。
    ///
    /// P5 の <see cref="AreaTransitionCoordinator"/> と P5.5 の <see cref="AreaSlideCoordinator"/> が
    /// 同じ表を使う。<b>条件を 2 か所へ写すと必ず食い違う</b>——しかも食い違いは
    /// 「片方の経路でだけ遷移できる／できない」という、再現条件の分かりにくい形で出る。
    /// P5.5 仕様書 §6.1 は「P5 の優先関係を維持」と明記しているので、写すのではなく共有する。
    ///
    /// UnityEngine に依存しないので EditMode で決定的に検証できる。
    /// </summary>
    public static class AreaTransitionAdmission
    {
        /// <summary>
        /// 受け付けてよいか。<see cref="AreaTransitionRejection.None"/> なら受理してよい。
        ///
        /// <b>排他（遷移中）はここでは見ない。</b> それは調停役ごとの状態で、条件表の話ではない。
        /// </summary>
        /// <param name="conditions">受付条件の供給元。null は安全側（受け付けない）。</param>
        /// <param name="isRespawn">死亡再開か（§9.1 手順 4。通常とは別の条件表を使う）。</param>
        public static AreaTransitionRejection Evaluate(IAreaTransitionConditions conditions, bool isRespawn)
        {
            if (conditions == null)
            {
                // 条件が分からない＝安全側。未配線で遷移させない。
                return AreaTransitionRejection.NotReady;
            }

            // 死亡再開だけは別の受付条件を使う（§9.1 手順 4／§6.1 末尾）。
            //
            // 通常の移動は「探索中・生存・行動していない」を求めるが、再開はその 3 つが
            // <b>すべて成り立たないときにだけ</b>行う操作である。同じ条件表を当てると、
            // 再開が自分の前提で拒否されて永久に死んだままになる。
            if (isRespawn)
            {
                // <b>AreaReady は見ない。</b> 死亡を受理した時点で活動を閉じている（§9.1 手順 1）ので、
                // 通常の移動と同じ「準備できているか」を求めると、閉じた自分の状態を理由に
                // 再開が断られ、死んだまま動けなくなる（実際に踏んだ）。
                if (conditions.Mode != GameMode.GameOver)
                {
                    return AreaTransitionRejection.WrongMode;
                }

                return conditions.IsEncounterActive
                    ? AreaTransitionRejection.EncounterActive
                    : AreaTransitionRejection.None;
            }

            if (!conditions.IsAreaReady)
            {
                return AreaTransitionRejection.NotReady;
            }

            if (conditions.Mode != GameMode.Exploration)
            {
                return AreaTransitionRejection.WrongMode;
            }

            if (!conditions.IsPlayerAlive)
            {
                return AreaTransitionRejection.PlayerDefeated;
            }

            // 戦闘開始が移動より先（§8.3 の競合表）。主人公の行動中より先に見る。
            if (conditions.IsEncounterActive)
            {
                return AreaTransitionRejection.EncounterActive;
            }

            return conditions.IsPlayerBusy
                ? AreaTransitionRejection.PlayerBusy
                : AreaTransitionRejection.None;
        }
    }
}

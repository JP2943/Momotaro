using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Scenes;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 活動許可の供給元（P4-FIX F05）。Scene に 1 つ置き、既存の正本から現在の許可を組み立てる。
    ///
    /// 読む先は 2 つだけ。
    /// <list type="bullet">
    /// <item><description><see cref="GameModeProvider"/>：Pause・会話・イベントの区別。</description></item>
    /// <item><description><see cref="CombatSessionController"/>：Encounter が開始待ち・継続中か
    /// （<b>Wave の幕間も戦闘中</b>）。</description></item>
    /// </list>
    ///
    /// 戦闘中かどうかを敵の数から数え直したりはしない。判定を作り直すと必ず食い違うので、既存の正本をそのまま使う。
    ///
    /// <b>未配線なら停止を返す。</b>「分からないから通す」にすると配線漏れが最後まで表に出ず、
    /// Pause 中に仲間が動くといった形で実機でだけ露見する。停止していればすぐ気付く。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionActivityContext : MonoBehaviour, ICompanionActivitySource
    {
        [Tooltip("戦闘セッションの正本（未設定なら同じ Scene の探索はせず、Encounter 無しとして扱う）。")]
        [SerializeField] private CombatSessionController _session;

        [Tooltip("戦闘セッションを持たない区画（自由探索など）であることを明示する。")]
        [SerializeField] private bool _noEncounterInThisArea;

        /// <inheritdoc />
        public CompanionActivity Current
        {
            get
            {
                IGameModeService modes = GameModeProvider.Current;
                if (modes == null)
                {
                    // モードの正本が居ない＝まだ何も初期化されていない。安全側に止める。
                    return CompanionActivity.Stopped;
                }

                if (_session == null && !_noEncounterInThisArea)
                {
                    // セッションを繋ぎ忘れたのか、本当に無い区画なのかを区別できない。黙って通さない。
                    return CompanionActivity.Stopped;
                }

                CombatSessionState? session = _session != null ? _session.State : (CombatSessionState?)null;
                return CompanionActivityResolver.Resolve(modes.Current, session);
            }
        }

        /// <summary>戦闘セッションを注入する（Scene 構築・テスト）。</summary>
        public void Bind(CombatSessionController session)
        {
            if (session != null)
            {
                _session = session;
            }
        }

        /// <summary>この区画には戦闘セッションが無いことを明示する（自由探索区画）。</summary>
        public void MarkAreaWithoutEncounter()
        {
            _noEncounterInThisArea = true;
        }

        private void OnEnable()
        {
            CompanionActivityProvider.Current = this;
        }

        private void OnDisable()
        {
            // 自分が差さっている場合だけ外す（別の Context に差し替わっていたら触らない）。
            if (ReferenceEquals(CompanionActivityProvider.Current, this))
            {
                CompanionActivityProvider.Current = null;
            }
        }
    }
}

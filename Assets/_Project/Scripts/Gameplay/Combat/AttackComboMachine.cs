using System;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 通常攻撃 3 段コンボの純粋状態機械（Phase2 P2-03B）。段（1..N）とフェーズ（予備/判定/後隙）を
    /// 段別 <see cref="StageTiming"/> から時間駆動する。Input System・Unity 型・Scene に依存せず、
    /// deltaTime と「次段が先行入力されているか」を外部から受け取るためテスト可能。
    ///
    /// 連鎖規則（仕様書 §4.2）：次段は「判定終了後」に、先行入力があれば実行する。判定終了〜段終了の間に
    /// 先行入力があれば次段へ進み、無ければ後隙を経て Idle へ戻る。3 段目からは連鎖しない。
    /// Hitbox・踏み込み・ダメージ適用は本クラスの外（駆動側 MonoBehaviour）で扱う。
    /// </summary>
    public sealed class AttackComboMachine
    {
        private readonly StageTiming[] _stages;
        private int _stage;      // 0 = 非攻撃、1..N
        private float _elapsed;  // 現在段の開始からの経過秒
        private bool _stageJustStarted;

        /// <param name="stages">段別タイミング（1 段目から順に）。1 要素以上必須。</param>
        public AttackComboMachine(StageTiming[] stages)
        {
            if (stages == null || stages.Length == 0)
            {
                throw new ArgumentException("stages must contain at least one stage.", nameof(stages));
            }

            _stages = stages;
        }

        /// <summary>段数。</summary>
        public int StageCount => _stages.Length;

        /// <summary>攻撃中か。</summary>
        public bool IsActive => _stage >= 1;

        /// <summary>現在段（1..N、非攻撃時 0）。</summary>
        public int Stage => _stage;

        /// <summary>現在段の開始からの経過秒。</summary>
        public float StageElapsed => _elapsed;

        /// <summary>このフレームで段が開始したか（踏み込み・向き再確定・新 Swing Token の合図）。</summary>
        public bool StageJustStarted => _stageJustStarted;

        /// <summary>現在のフェーズ。</summary>
        public AttackPhase Phase
        {
            get
            {
                if (!IsActive)
                {
                    return AttackPhase.None;
                }

                StageTiming t = _stages[_stage - 1];
                if (_elapsed < t.Startup)
                {
                    return AttackPhase.Startup;
                }

                if (_elapsed < t.ActiveEnd)
                {
                    return AttackPhase.Active;
                }

                return AttackPhase.Recovery;
            }
        }

        /// <summary>Hitbox が有効か（判定中のみ）。</summary>
        public bool HitboxActive => Phase == AttackPhase.Active;

        /// <summary>現在段の総時間を満了したか（連鎖しなければ終了すべき）。</summary>
        public bool IsComplete => IsActive && _elapsed >= _stages[_stage - 1].Total;

        /// <summary>次段への連鎖を受け付ける窓か（判定終了後、かつ最終段でない）。</summary>
        public bool AcceptingChain => IsActive && _stage < _stages.Length && _elapsed >= _stages[_stage - 1].ActiveEnd;

        /// <summary>Guard/Step キャンセルが許可される時点か（段別のキャンセル許可開始秒以降）。</summary>
        public bool CanCancel => IsActive && _elapsed >= _stages[_stage - 1].EffectiveCancelStart;

        /// <summary>
        /// 時間を進める。<see cref="StageJustStarted"/> はこの呼び出しでクリアされる。
        /// 終了・連鎖の判断（<see cref="TryAdvance"/> / <see cref="End"/>）は駆動側が行う。
        /// </summary>
        public void Tick(float deltaTime)
        {
            _stageJustStarted = false;
            if (!IsActive)
            {
                return;
            }

            _elapsed += deltaTime;
        }

        /// <summary>非攻撃状態から 1 段目を開始する。開始できたら true。</summary>
        public bool TryStart()
        {
            if (IsActive)
            {
                return false;
            }

            _stage = 1;
            _elapsed = 0f;
            _stageJustStarted = true;
            return true;
        }

        /// <summary>連鎖窓であれば次段へ進む。進めたら true。</summary>
        public bool TryAdvance()
        {
            if (!AcceptingChain)
            {
                return false;
            }

            _stage++;
            _elapsed = 0f;
            _stageJustStarted = true;
            return true;
        }

        /// <summary>コンボを終了して非攻撃へ戻す（後隙満了時など）。</summary>
        public void End()
        {
            _stage = 0;
            _elapsed = 0f;
        }

        /// <summary>中断する（Disable・モード遮断・ひるみ・キャンセル）。Hitbox 消去は駆動側で行う。</summary>
        public void Interrupt()
        {
            _stage = 0;
            _elapsed = 0f;
            _stageJustStarted = false;
        }
    }
}

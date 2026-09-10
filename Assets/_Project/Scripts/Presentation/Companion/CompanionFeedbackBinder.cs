using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using UnityEngine;

namespace Momotaro.Presentation.Companion
{
    /// <summary>
    /// 仲間の被弾結果を演出側へ繋ぐ登録役（P4-FIX F04）。仲間 Prefab のルートに載る。
    ///
    /// <b>なぜ Gameplay 側ではなくここに置くか</b>：被弾の受け口（<see cref="CompanionHitReceiver"/>）は Gameplay の
    /// 部品で、演出を知ってはいけない。逆に演出側が仲間を探し回るのもやめたい（周期スキャンでは
    /// 生成直後の一撃を取りこぼし、破棄後の旧チャネルを外せない）。そこで<b>Presentation 側の小さな登録役</b>を
    /// 仲間 Prefab に同居させ、受け口のチャネルを登録所へ差し出す。層の向きは Presentation → Gameplay のまま。
    ///
    /// 有効化で 1 回だけ登録し、無効化・破棄で 1 回だけ解除する。登録するのはチャネル（ただの C# オブジェクト）なので、
    /// GameObject が破棄されたあとでも確実に外せる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionFeedbackBinder : MonoBehaviour
    {
        [Tooltip("被弾結果の供給元（未設定なら同じ GameObject から自動取得）。")]
        [SerializeField] private CompanionHitReceiver _receiver;

        /// <summary>いま登録しているチャネル（未登録なら null。テスト・診断用）。</summary>
        public HitResultChannel RegisteredChannel { get; private set; }

        /// <summary>登録済みか（テスト・診断用）。</summary>
        public bool IsRegistered => RegisteredChannel != null;

        /// <summary>受け口を注入する（Prefab 構築・テスト。null は無視）。有効中に差し替えたら登録も張り替える。</summary>
        public void Bind(CompanionHitReceiver receiver)
        {
            if (receiver == null || ReferenceEquals(_receiver, receiver))
            {
                return;
            }

            bool wasRegistered = IsRegistered;
            if (wasRegistered)
            {
                Detach();
            }

            _receiver = receiver;

            if (wasRegistered)
            {
                Attach();
            }
        }

        /// <summary>登録する（冪等）。</summary>
        public void Attach()
        {
            if (IsRegistered)
            {
                return;
            }

            if (_receiver == null)
            {
                _receiver = GetComponent<CompanionHitReceiver>();
            }

            if (_receiver == null)
            {
                return; // 受け口が無い構成では何もしない（演出が出ないだけで、Gameplay は動く）。
            }

            RegisteredChannel = _receiver.Results;

            // 持ち主として自分を渡す。OnDisable／OnDestroy が走らない経路（Scene 破棄・EditMode）でも、
            // 登録所の側で破棄済みとして落とせるようにするため。
            CompanionFeedbackRegistry.Register(RegisteredChannel, this);
        }

        /// <summary>解除する（冪等）。</summary>
        public void Detach()
        {
            if (!IsRegistered)
            {
                return;
            }

            CompanionFeedbackRegistry.Unregister(RegisteredChannel);
            RegisteredChannel = null;
        }

        private void OnEnable()
        {
            Attach();
        }

        private void OnDisable()
        {
            Detach();
        }

        private void OnDestroy()
        {
            // 無効化を経ずに破棄される経路（Scene 破棄など）でも、旧チャネルを登録所へ残さない。
            Detach();
        }
    }
}

using System.Collections.Generic;
using Momotaro.Gameplay.Enemy;
using UnityEngine;

namespace Momotaro.Presentation.Enemy
{
    /// <summary>
    /// 敵の Gameplay 状態・向きを見た目へ接続する Visual Adapter（Phase3 敵スプライト受入。PlayerVisualAdapter に倣う敵専用責務）。
    /// <see cref="EnemyActor.State"/> と前方（<see cref="EnemyActor.Forward"/>）から <see cref="EnemyVisualNames"/> でクリップ名を解決し、
    /// 変化時のみ <see cref="Animator"/> を再生する（毎フレーム無条件 Play しない）。State 未登録・Animator 欠落時は毎フレーム警告を出さず、
    /// 不足クリップは 1 度だけ警告する。Animator／Animation Event を Gameplay 時間・命中判定の正本にしない。Presentation が
    /// 欠けても Gameplay は進行する（本 Adapter は読み取りのみ）。被弾・スタン・Down の表示優先は EnemyState 自体の優先度に従う。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyVisualAdapter : MonoBehaviour
    {
        [Tooltip("状態・向きの取得元（未指定なら親から取得）。")]
        [SerializeField] private EnemyActor _actor;

        [Tooltip("再生する Animator（Sprite の GameObject 上）。")]
        [SerializeField] private Animator _animator;

        [Tooltip("再生対象の Animator Layer 名（既定 Base Layer）とその index。")]
        [SerializeField] private string _layerName = "Base Layer";
        [SerializeField] private int _layerIndex = 0;

        private string _currentClip;
        private readonly HashSet<string> _warnedMissingStates = new HashSet<string>();

        private void Awake()
        {
            if (_actor == null)
            {
                _actor = GetComponentInParent<EnemyActor>();
            }
        }

        private void LateUpdate()
        {
            if (_actor == null || _animator == null)
            {
                return; // Presentation 欠落でも Gameplay は進行する。
            }

            EnemyVisualFacing facing = EnemyFacingResolver.FromForward(_actor.Forward);
            string clip = EnemyVisualNames.StateName(_actor.State, facing);
            if (clip == _currentClip)
            {
                return; // 変化時のみ再生（毎フレーム Play しない）。
            }

            _currentClip = clip;

            int stateHash = Animator.StringToHash(_layerName + "." + clip);
            if (_animator.HasState(_layerIndex, stateHash))
            {
                _animator.Play(stateHash, _layerIndex, 0f);
            }
            else if (_warnedMissingStates.Add(clip))
            {
                Debug.LogWarning(
                    "[EnemyVisualAdapter] Animator の Layer '" + _layerName + "'(index " + _layerIndex + ") に State '" + clip
                    + "' が無いため再生をスキップしました。Animator Controller に該当 State を追加してください。", this);
            }
        }
    }
}

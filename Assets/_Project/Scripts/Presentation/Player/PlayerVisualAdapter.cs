using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Presentation.Player
{
    /// <summary>
    /// Player の Gameplay 状態・向きを見た目へ接続する Visual Adapter（Phase1 P1-09）。
    /// <see cref="PlayerStateController"/> の状態と <see cref="PlayerFacing"/> の向きから
    /// クリップ名を解決し、<see cref="Animator"/> を再生する。Animator State を Gameplay 状態の正本にしない。
    ///
    /// 本番 Sprite への差し替えは、同名クリップの中身（Sprite 参照）を差し替えるか、
    /// Animator Override Controller を割り当てることで、この Adapter を変更せず完結できる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVisualAdapter : MonoBehaviour
    {
        [SerializeField] private PlayerStateController _state;
        [SerializeField] private PlayerFacing _facing;
        [SerializeField] private Animator _animator;

        private string _currentClip;

        private void LateUpdate()
        {
            if (_state == null || _facing == null || _animator == null)
            {
                return;
            }

            string clip = PlayerVisualNames.ClipName(_state.Current, _facing.Current);
            if (clip == _currentClip)
            {
                return;
            }

            _currentClip = clip;
            _animator.Play(clip);
        }
    }
}

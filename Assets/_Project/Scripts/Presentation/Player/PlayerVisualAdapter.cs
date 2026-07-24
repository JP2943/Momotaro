using System.Collections.Generic;
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

        [Tooltip("再生対象の Animator Layer 名（既定 Base Layer）とその index。")]
        [SerializeField] private string _layerName = "Base Layer";
        [SerializeField] private int _layerIndex = 0;

        private string _currentClip;
        private readonly HashSet<string> _warnedMissingStates = new HashSet<string>();

        private void LateUpdate()
        {
            if (_state == null || _facing == null || _animator == null)
            {
                return;
            }

            string clip = PlayerVisualNames.ClipName(_state.Current, _facing.Current, _state.AttackStage);
            if (clip == _currentClip)
            {
                return;
            }

            _currentClip = clip;

            // Layer index を明示し、完全 State パスのハッシュで存在確認してから再生する。State 名だけの
            // Play(string) は未定義 State のとき "Invalid Layer Index '-1'" / "State could not be found" を毎フレーム出す。
            int stateHash = Animator.StringToHash(_layerName + "." + clip);
            if (_animator.HasState(_layerIndex, stateHash))
            {
                _animator.Play(stateHash, _layerIndex, 0f);
            }
            else if (_warnedMissingStates.Add(clip))
            {
                // 設定不備を黙って Idle へ落とさず、State 不足を 1 度だけ明示する。
                Debug.LogWarning(
                    $"[PlayerVisualAdapter] Animator の Layer '{_layerName}'(index {_layerIndex}) に State '{clip}' が無いため再生をスキップしました。" +
                    "Animator Controller に該当 State を追加してください。", this);
            }
        }
    }
}

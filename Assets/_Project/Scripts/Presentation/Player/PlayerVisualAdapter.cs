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

        [Tooltip("死亡（Defeated）仮表示の乗算色。彩度／明度を落とした暗い灰色（Phase3.5 P3.5-02 仮仕様）。完成版死亡演出は対象外。")]
        [SerializeField] private Color _defeatedTint = new Color(0.45f, 0.45f, 0.5f, 1f);

        private string _currentClip;
        private readonly HashSet<string> _warnedMissingStates = new HashSet<string>();

        private SpriteRenderer _renderer;
        private bool _rendererResolved;
        private Color _originalColor = Color.white;
        private bool _defeatTintApplied;

        private void LateUpdate()
        {
            if (_state == null || _facing == null || _animator == null)
            {
                return;
            }

            // 死亡仮表示：現 Facing の Hurt クリップ（PlayerVisualNames が Defeated→Hurt へ写像）を再生し、非ループなので最終 Frame を
            // 保持する。加えて Sprite を低彩度・低明度へ落とす（仕様書 §4.2）。色替えはクリップ変化の有無に依らず毎 LateUpdate で判定する。
            ApplyDefeatTint(_state.Current == PlayerState.Defeated);

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

        /// <summary>
        /// 死亡仮表示の低彩度化を適用／解除する（Presentation 専用）。Renderer が無ければ Gameplay を止めず黙って無視する
        /// （警告連打しない）。適用前の色を保持し、非死亡へ戻る場合は元の色へ復元する（本 Phase では死亡は恒久だが、
        /// テストや将来の復帰に備えて対称にする）。
        /// </summary>
        private void ApplyDefeatTint(bool defeated)
        {
            SpriteRenderer sr = ResolveRenderer();
            if (sr == null)
            {
                return;
            }

            if (defeated && !_defeatTintApplied)
            {
                _originalColor = sr.color;
                sr.color = _defeatedTint;
                _defeatTintApplied = true;
            }
            else if (!defeated && _defeatTintApplied)
            {
                sr.color = _originalColor;
                _defeatTintApplied = false;
            }
        }

        private SpriteRenderer ResolveRenderer()
        {
            if (!_rendererResolved)
            {
                // Animator と同じ GameObject（Sprite ノード）に SpriteRenderer が同居する構成を前提に、追加の Inspector 配線なしで解決する。
                _renderer = _animator != null ? _animator.GetComponent<SpriteRenderer>() : null;
                _rendererResolved = true;
            }

            return _renderer;
        }
    }
}

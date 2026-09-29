using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Presentation.Transition
{
    /// <summary>
    /// 遷移中だけ Actor の<b>見た目だけ</b>を運ぶ代理（P5.5 仕様書 §7.2）。
    ///
    /// <b>Actor を Scene 間で永続移動させない</b>のが本フェーズの前提で、到着側では
    /// P5 どおり Actor を作り直して Snapshot から復元する（§7.2 冒頭）。
    /// その間の「通路を実際に渡る見た目」をこれが作る。
    ///
    /// <b>描画部品しか持たない。</b> Prefab を丸ごと複製してあとからスクリプトを外す方式は
    /// 使わない（§7.2）——外し忘れが 1 つあるだけで、代理から命中や登録が発生する。
    /// ここは <see cref="SpriteRenderer"/> 1 枚だけを自分で足す物体として作り、
    /// Rigidbody・Collider・Hitbox・Vitals・AI・報酬・Gameplay イベント・登録簿を持たない。
    /// <b>持っていないことは検査で見る</b>（§11 P06「表示代理から命中も登録も発生しない」）。
    ///
    /// <b>時計は表示専用の unscaled。</b> Move の既存 6 コマ周期をこの時計で回す。
    /// <c>Animator</c> を載せないので AnimationEvent は発火しない——攻撃・足音などの
    /// ゲーム通知が代理から飛ぶ経路を最初から作らない（§7.2）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaTransitionDisplayProxy : MonoBehaviour
    {
        /// <summary>Move の 1 周期のコマ数（§7.2「既存 6 コマ周期」）。</summary>
        public const int MoveFrameCount = 6;

        /// <summary>Move の 1 周期の秒数（既存クリップは 1/12 秒刻みの 6 コマ）。</summary>
        public const float MoveCycleSeconds = MoveFrameCount / 12f;

        private readonly List<Sprite> _frames = new List<Sprite>();
        private SpriteRenderer _renderer;
        private Vector3 _from;
        private Vector3 _to;
        private float _cycleSeconds = MoveCycleSeconds;
        private float _clock;
        private bool _frozen;

        /// <summary>この代理が描く <see cref="SpriteRenderer"/>（テスト・検査用）。</summary>
        public SpriteRenderer Renderer => _renderer;

        /// <summary>いまの進行度（0〜1）。</summary>
        public float Progress { get; private set; }

        /// <summary>出発位置。</summary>
        public Vector3 From => _from;

        /// <summary>到着位置。</summary>
        public Vector3 To => _to;

        /// <summary>コマ送りを止めているか（Down／Stagger は姿勢を保つ。§7.2）。</summary>
        public bool IsFrozen => _frozen;

        /// <summary>いま出しているコマの番号（診断・テスト用）。</summary>
        public int FrameIndex { get; private set; }

        /// <summary>表示専用時計の経過秒（診断・テスト用）。</summary>
        public float DisplaySeconds => _clock;

        /// <summary>
        /// 見た目を写して作る（§7.2「同じ Sprite・足元位置・縮尺・色・Sorting」）。
        ///
        /// <b>足元位置で合わせる。</b> Actor の根と描画ノードには高さのずれがあるので、
        /// 根の位置ではなく<b>描画ノードの位置</b>をそのまま使う。根で合わせると
        /// 代理だけ地面に沈む／浮く。
        /// </summary>
        public void Capture(SpriteRenderer source)
        {
            if (source == null)
            {
                return;
            }

            _renderer = GetComponent<SpriteRenderer>();
            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<SpriteRenderer>();
            }

            _renderer.sprite = source.sprite;
            _renderer.color = source.color;
            _renderer.flipX = source.flipX;
            _renderer.flipY = source.flipY;
            _renderer.sharedMaterial = source.sharedMaterial;
            _renderer.sortingLayerID = source.sortingLayerID;
            _renderer.sortingOrder = source.sortingOrder;
            _renderer.drawMode = source.drawMode;

            Transform from = source.transform;
            transform.position = from.position;
            transform.rotation = from.rotation;
            transform.localScale = from.lossyScale;

            _from = from.position;
            _to = from.position;
            Progress = 0f;
            FrameIndex = 0;
            _clock = 0f;
        }

        /// <summary>運ぶ区間を決める（出発位置から到着位置へ。§7.2）。</summary>
        public void SetRoute(Vector3 from, Vector3 to)
        {
            _from = from;
            _to = to;
            ApplyProgress();
        }

        /// <summary>
        /// コマ送りの素材を渡す（§7.2「Move の既存 6 コマ周期」）。
        ///
        /// <b>渡されなければコマ送りしない。</b> 写した 1 枚を出し続ける——
        /// 手元に無いコマを推測で作るより、止まった絵の方が嘘が小さい。
        ///
        /// <b>渡した瞬間に絵を替えない</b>（工程 P55-09b）。§7.2 は「開始時に実 Actor と
        /// <b>同じ Sprite</b>・足元位置・縮尺・色・Sorting をコピーし」と定める。
        /// ここで 1 コマ目を当てると、写した直後に絵が飛ぶ。
        ///
        /// <b>写した絵がコマの中にあれば、その位置から続ける。</b> 実際の遷移は
        /// 主人公が<b>歩いている最中</b>に受理されるので、写した絵はたいてい Move の途中のコマである。
        /// 0 コマ目へ巻き戻すと、そこで歩きが 1 回つまずく。
        /// 見つからなければ（立ち止まりの絵など）写した 1 枚のまま——
        /// 時計が進んだところで周期に乗る。
        /// </summary>
        public void SetMoveFrames(IReadOnlyList<Sprite> frames, float cycleSeconds = MoveCycleSeconds)
        {
            _frames.Clear();
            if (frames != null)
            {
                for (int i = 0; i < frames.Count; i++)
                {
                    if (frames[i] != null)
                    {
                        _frames.Add(frames[i]);
                    }
                }
            }

            _cycleSeconds = cycleSeconds > 0f ? cycleSeconds : MoveCycleSeconds;

            int startIndex = IndexOfShownSprite();
            FrameIndex = startIndex < 0 ? 0 : startIndex;
            _clock = _frames.Count > 0 && startIndex > 0
                ? startIndex * (_cycleSeconds / _frames.Count)
                : 0f;

            // 絵は替えない（写した 1 枚のまま）。周期に乗るのは時計が進んでから。
        }

        /// <summary>いま出している絵がコマの何番目か（無ければ −1）。</summary>
        private int IndexOfShownSprite()
        {
            if (_renderer == null || _renderer.sprite == null)
            {
                return -1;
            }

            for (int i = 0; i < _frames.Count; i++)
            {
                if (ReferenceEquals(_frames[i], _renderer.sprite))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// コマ送りを止める／戻す（§7.2「Down／Stagger は既存姿勢を保持して位置だけ運ぶ」）。
        /// 止めている間も <see cref="SetProgress"/> は効く——運ぶのは位置だけ。
        /// </summary>
        public void Freeze(bool frozen)
        {
            _frozen = frozen;
        }

        /// <summary>
        /// 進行度を与える（<b>カメラと同じ値</b>を使う。§7.2）。
        ///
        /// 自前の時計で位置を進めない。カメラと別の時計で動かすと、
        /// 同じ 0.45 秒でも端でずれて「主人公だけ先に着く」ように見える。
        /// </summary>
        public void SetProgress(float progress)
        {
            Progress = Mathf.Clamp01(progress);
            ApplyProgress();
        }

        /// <summary>
        /// 表示専用時計を進める（unscaled を渡す。§7.2）。
        ///
        /// ヒットストップや Pause で止まっている時計は渡さない——遷移中の見た目は
        /// ゲームの時間ではなく演出の時間で動く。
        /// </summary>
        public void TickDisplayClock(float unscaledDeltaTime)
        {
            if (_frozen || _frames.Count == 0 || unscaledDeltaTime <= 0f)
            {
                return;
            }

            _clock += unscaledDeltaTime;
            float perFrame = _cycleSeconds / _frames.Count;
            FrameIndex = perFrame > 0f
                ? Mathf.FloorToInt(_clock / perFrame) % _frames.Count
                : 0;
            ApplyFrame();
        }

        /// <summary>
        /// 実 Actor へ引き継いで消える（§7.2「末尾では実 Actor へ同じ姿勢または自然な Idle で」）。
        ///
        /// <b>隠した描画を戻すのは呼び出し側の仕事</b>（<see cref="AreaTransitionDisplayProxySet"/>）。
        /// ここで戻すと、代理を個別に捨てたときに戻し漏れが起きる。
        /// </summary>
        public void Release()
        {
            if (Application.isPlaying)
            {
                Destroy(gameObject);
                return;
            }

            DestroyImmediate(gameObject);
        }

        private void ApplyProgress()
        {
            transform.position = Vector3.Lerp(_from, _to, Progress);
        }

        private void ApplyFrame()
        {
            if (_renderer != null && _frames.Count > 0)
            {
                _renderer.sprite = _frames[Mathf.Clamp(FrameIndex, 0, _frames.Count - 1)];
            }
        }
    }
}

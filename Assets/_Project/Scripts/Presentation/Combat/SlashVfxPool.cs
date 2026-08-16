using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 剣閃 VFX（<see cref="SlashVfxInstance"/>）のプール（Phase3.5 P3.5-05）。生成を再利用し、毎回の Instantiate/Destroy を避ける。
    /// static な万能マネージャは作らず、親 Transform 配下にインスタンスを保持する。時間駆動は <see cref="TickActive"/> に集約し、
    /// インスタンス側は自前 Update を持たない（駆動点を一つにしてテストを決定的にする）。
    /// </summary>
    public sealed class SlashVfxPool
    {
        private readonly Transform _parent;
        private readonly List<SlashVfxInstance> _all = new List<SlashVfxInstance>();
        private readonly Stack<SlashVfxInstance> _free = new Stack<SlashVfxInstance>();
        private int _active;

        /// <summary>再生中インスタンス数（テスト・検証用）。</summary>
        public int ActiveCount => _active;

        /// <summary>生成済みインスタンス総数（再利用検証用）。</summary>
        public int TotalCount => _all.Count;

        /// <summary>生成済みインスタンス一覧（検証用・読み取り専用）。</summary>
        public IReadOnlyList<SlashVfxInstance> Instances => _all;

        public SlashVfxPool(Transform parent)
        {
            _parent = parent;
        }

        /// <summary>再生用インスタンスを取得する（空きを再利用、無ければ生成）。</summary>
        public SlashVfxInstance Get()
        {
            SlashVfxInstance inst;
            if (_free.Count > 0)
            {
                inst = _free.Pop();
            }
            else
            {
                var go = new GameObject("SlashVfx", typeof(SpriteRenderer));
                if (_parent != null)
                {
                    go.transform.SetParent(_parent, false);
                }

                inst = go.AddComponent<SlashVfxInstance>();
                inst.Completed = Recycle;
                _all.Add(inst);
            }

            _active++;
            return inst;
        }

        private void Recycle(SlashVfxInstance inst)
        {
            if (_active > 0)
            {
                _active--;
            }

            _free.Push(inst);
        }

        /// <summary>再生中インスタンスの時間を進める（Presenter がスケール時間で毎フレーム呼ぶ）。</summary>
        public void TickActive(float deltaTime)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                SlashVfxInstance inst = _all[i];
                if (inst != null && inst.IsPlaying)
                {
                    inst.Tick(deltaTime);
                }
            }
        }

        /// <summary>全再生を打ち切る（Disable・Scene 離脱・Retry。残留を残さない）。</summary>
        public void StopAll()
        {
            for (int i = 0; i < _all.Count; i++)
            {
                SlashVfxInstance inst = _all[i];
                if (inst != null && inst.IsPlaying)
                {
                    inst.Stop();
                }
            }
        }
    }
}

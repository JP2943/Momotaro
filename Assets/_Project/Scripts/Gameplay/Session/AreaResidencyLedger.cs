using System.Collections.Generic;
using Momotaro.Core.Identification;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 読み込まれている Area の在留台帳（P5.5 仕様書 §4.1／§9.1／§11 の E04）。
    ///
    /// <b>P5 の「読込済み Area は常に 1」を置き換える検査の正本。</b>
    /// §9.1 が定めるのは「<b>Active は最大 1</b>、Staged／Prepared／Retiring との<b>合計は最大 2</b>」。
    /// 数を数えるだけでなく、どの実体（<see cref="AreaInstanceHandle"/>）がどの段階かを持つ。
    ///
    /// <b>Scene 操作の直列化もここで持つ。</b> §2「Scene ロード／unload の発行は単一の管理者が直列化する」、
    /// §5「Scene 操作が走っている間は別 Scene 操作を重ねない」。
    /// 直列化を Infrastructure の都合に散らすと、先読みと遷移と撤去が別々に発行して二重ロードになる。
    ///
    /// UnityEngine に依存しないので EditMode で決定的に検証できる。
    /// </summary>
    public sealed class AreaResidencyLedger
    {
        /// <summary>同時に読み込んでよい Area の上限（§9.1）。</summary>
        public const int MaxResidentAreas = 2;

        private readonly Dictionary<AreaInstanceHandle, AreaActivationPhase> _areas =
            new Dictionary<AreaInstanceHandle, AreaActivationPhase>();

        private int _loadGeneration;

        /// <summary>読み込まれている Area の数。</summary>
        public int ResidentCount => _areas.Count;

        /// <summary>いま Scene 操作（ロード／unload）が走っているか。</summary>
        public bool IsSceneOperationInFlight { get; private set; }

        /// <summary>重ねようとして断った Scene 操作の数（診断・テスト用）。</summary>
        public int BlockedSceneOperationCount { get; private set; }

        /// <summary>上限に達して断った受け入れの数（診断・テスト用）。</summary>
        public int BlockedAdmissionCount { get; private set; }

        /// <summary>活動中の Area（無ければ無効ハンドル）。</summary>
        public AreaInstanceHandle ActiveArea
        {
            get
            {
                foreach (KeyValuePair<AreaInstanceHandle, AreaActivationPhase> kv in _areas)
                {
                    if (kv.Value == AreaActivationPhase.Active)
                    {
                        return kv.Key;
                    }
                }

                return AreaInstanceHandle.None;
            }
        }

        /// <summary>次のロード世代を発行する。<b>同じ AreaId でも別の実体として扱う</b>ための番号。</summary>
        public AreaInstanceHandle NextHandle(StableId areaId) =>
            new AreaInstanceHandle(areaId, ++_loadGeneration);

        /// <summary>その実体の段階（未登録なら <see cref="AreaActivationPhase.Unloaded"/>）。</summary>
        public AreaActivationPhase PhaseOf(AreaInstanceHandle handle) =>
            _areas.TryGetValue(handle, out AreaActivationPhase phase) ? phase : AreaActivationPhase.Unloaded;

        /// <summary>その段階の Area の数。</summary>
        public int CountOf(AreaActivationPhase phase)
        {
            int n = 0;
            foreach (KeyValuePair<AreaInstanceHandle, AreaActivationPhase> kv in _areas)
            {
                if (kv.Value == phase)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// Scene 操作を始めてよいか。走っていれば断る（§5 末尾）。
        /// 断った回数は <see cref="BlockedSceneOperationCount"/> に数える。
        /// </summary>
        public bool TryBeginSceneOperation()
        {
            if (IsSceneOperationInFlight)
            {
                BlockedSceneOperationCount++;
                return false;
            }

            IsSceneOperationInFlight = true;
            return true;
        }

        /// <summary>Scene 操作が終端した（成功・失敗を問わず必ず呼ぶ）。</summary>
        public void EndSceneOperation()
        {
            IsSceneOperationInFlight = false;
        }

        /// <summary>
        /// 新しく読み込んだ Area を Staged として受け入れる。
        /// <b>上限を超えるなら断る</b>——断らずに 3 つ目を載せると、撤去待ちの Area が
        /// 積み上がってメモリと購読が膨らむ（§1.2「読み込まれた Area は最大 2」）。
        /// </summary>
        public bool TryAdmitStaged(AreaInstanceHandle handle)
        {
            if (!handle.IsValid || _areas.ContainsKey(handle))
            {
                return false;
            }

            if (_areas.Count >= MaxResidentAreas)
            {
                BlockedAdmissionCount++;
                return false;
            }

            _areas[handle] = AreaActivationPhase.Staged;
            return true;
        }

        /// <summary>
        /// 段階を進める。<b>Active は最大 1</b>（§9.1）。
        /// 2 つ目を Active にしようとしたら断る——見えている Area が両方動くと、
        /// 境界越しの索敵・Interact がそのまま起きる。
        /// </summary>
        public bool TrySetPhase(AreaInstanceHandle handle, AreaActivationPhase phase)
        {
            if (!handle.IsValid || !_areas.ContainsKey(handle))
            {
                return false;
            }

            if (phase == AreaActivationPhase.Active)
            {
                AreaInstanceHandle active = ActiveArea;
                if (active.IsValid && !active.Equals(handle))
                {
                    return false;
                }
            }

            if (phase == AreaActivationPhase.Unloaded)
            {
                _areas.Remove(handle);
                return true;
            }

            _areas[handle] = phase;
            return true;
        }

        /// <summary>撤去して台帳から外す。</summary>
        public bool Remove(AreaInstanceHandle handle) => _areas.Remove(handle);

        /// <summary>読み込まれている実体の一覧（診断・検査用）。</summary>
        public IEnumerable<KeyValuePair<AreaInstanceHandle, AreaActivationPhase>> Residents => _areas;

        /// <summary>
        /// 台帳が §9.1 の不変条件を満たしているか（検査・テスト用）。
        /// 満たさない状態は作れないようにしてあるが、<b>作れないことを検査でも固定する</b>。
        /// </summary>
        public bool SatisfiesResidencyRules(out string violation)
        {
            if (_areas.Count > MaxResidentAreas)
            {
                violation = "読み込まれた Area が " + _areas.Count + " 個あります（上限 " + MaxResidentAreas + "）。";
                return false;
            }

            int active = CountOf(AreaActivationPhase.Active);
            if (active > 1)
            {
                violation = "活動中の Area が " + active + " 個あります（上限 1）。";
                return false;
            }

            violation = null;
            return true;
        }
    }
}

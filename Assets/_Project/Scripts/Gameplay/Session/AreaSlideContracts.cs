using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.World;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// Area の活動段階（P5.5 仕様書 §4.1）。
    ///
    /// <b>「読み込まれている」と「遊べる」を分ける。</b> P5 は Single 読込だったので
    /// 「Scene が在る＝その Area で遊んでいる」が成り立っていたが、P5.5 では 2 つ同時に在る。
    /// 読込の有無ではなく、この段階だけがゲーム上の可否を決める。
    /// </summary>
    public enum AreaActivationPhase
    {
        /// <summary>読み込まれていない。進行は Session が保持している。</summary>
        Unloaded = 0,

        /// <summary>地形・仕掛けの表示準備だけ済んだ。Gameplay・物理・購読はすべて無効。</summary>
        Staged = 1,

        /// <summary>到着 Actor を復元・配線した。まだ停止中で、外部へ公開していない。</summary>
        Prepared = 2,

        /// <summary>通常どおり遊べる。<b>同時に 1 つだけ</b>。</summary>
        Active = 3,

        /// <summary>遷移のため止めた出発側。地形は見えているが活動しない。</summary>
        Suspended = 4,

        /// <summary>撤去中。無効・購読解除済み。</summary>
        Retiring = 5,
    }

    /// <summary>
    /// 読み込まれた Area の識別子（§4.3）。
    ///
    /// <b>AreaId だけでは足りない。</b> 同じ AreaId を往復すると、前の Scene インスタンスと
    /// 新しいインスタンスが同じ名前で並ぶ瞬間がある。遅れて届いた通知がどちらのものかを
    /// 取り違えると、撤去済みの Area を現行として扱ってしまう。
    /// ロード世代（単調増加）を添えて、同じ AreaId の別インスタンスを必ず区別する。
    /// </summary>
    public readonly struct AreaInstanceHandle : System.IEquatable<AreaInstanceHandle>
    {
        /// <summary>無効なハンドル。</summary>
        public static readonly AreaInstanceHandle None = default;

        /// <summary>作る。</summary>
        public AreaInstanceHandle(StableId areaId, int loadGeneration)
        {
            AreaId = areaId;
            LoadGeneration = loadGeneration;
        }

        /// <summary>どのエリアか。</summary>
        public StableId AreaId { get; }

        /// <summary>何回目に読み込んだ実体か（1 以上。0 は無効）。</summary>
        public int LoadGeneration { get; }

        /// <summary>有効なハンドルか。</summary>
        public bool IsValid => LoadGeneration > 0 && AreaId.IsValid;

        /// <inheritdoc />
        public bool Equals(AreaInstanceHandle other) =>
            LoadGeneration == other.LoadGeneration && AreaId.Equals(other.AreaId);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is AreaInstanceHandle o && Equals(o);

        /// <inheritdoc />
        public override int GetHashCode() =>
            (AreaId.Value?.GetHashCode() ?? 0) * 397 ^ LoadGeneration;

        /// <inheritdoc />
        public override string ToString() =>
            IsValid ? AreaId.Value + "#" + LoadGeneration : "(none)";
    }

    /// <summary>接続を引けなかった理由（§11 の E01）。</summary>
    public enum AreaConnectionRejection
    {
        /// <summary>拒否ではない。</summary>
        None = 0,

        /// <summary>そんな接続 ID は無い。</summary>
        UnknownConnection = 1,

        /// <summary>その出発エリア・出入口の組に接続が無い。</summary>
        NoConnectionFromExit = 2,

        /// <summary>Slide なのに向きが無い。</summary>
        MissingDirection = 3,

        /// <summary>逆接続が見つからない・対になっていない。</summary>
        BrokenReverse = 4,
    }

    /// <summary>
    /// 接続の引き当て（§3.1／§11 の E01）。UnityEngine に依存しない。
    ///
    /// <b>Data をそのまま配らない。</b> 受理時に必要な値を Snapshot して渡す（§3.1 末尾
    /// 「Runtime では受理時に接続・演出設定を Snapshot 化し、途中で SO の値を読み直さない」）。
    /// 遷移の途中で Asset を触られても、走っている遷移の演出値は変わらない。
    /// </summary>
    public sealed class AreaConnectionCatalog
    {
        private readonly Dictionary<string, AreaConnectionSnapshot> _byId =
            new Dictionary<string, AreaConnectionSnapshot>();

        private readonly Dictionary<string, AreaConnectionSnapshot> _byExit =
            new Dictionary<string, AreaConnectionSnapshot>();

        private AreaConnectionCatalog()
        {
        }

        /// <summary>登録件数（診断・テスト用）。</summary>
        public int Count => _byId.Count;

        /// <summary>
        /// Data から作る。<b>不整合があれば作らない</b>——半端なカタログを配ると、
        /// 壊れた接続だけ実行時に失敗して原因が分かりにくくなる。
        /// </summary>
        public static bool TryBuild(
            AreaConnectionData data, out AreaConnectionCatalog catalog, out List<string> errors)
        {
            catalog = null;
            errors = new List<string>();

            if (data == null)
            {
                errors.Add("接続 Data が未設定です。");
                return false;
            }

            List<string> local = errors;
            foreach (AreaConnectionDefinition c in data.Connections)
            {
                if (c == null)
                {
                    local.Add("接続に null が混ざっています。");
                    continue;
                }

                AreaConnectionRules.Validate(c, local.Add, data.name);
            }

            AreaConnectionRules.ValidateReversePairs(data.Connections, local.Add, data.name);

            if (errors.Count > 0)
            {
                return false;
            }

            var built = new AreaConnectionCatalog();
            foreach (AreaConnectionDefinition c in data.Connections)
            {
                var snapshot = new AreaConnectionSnapshot(c);
                built._byId[c.ConnectionId.Value] = snapshot;
                built._byExit[ExitKey(c.FromAreaId, c.ExitId)] = snapshot;
            }

            catalog = built;
            return true;
        }

        /// <summary>接続 ID から引く。</summary>
        public bool TryGet(StableId connectionId, out AreaConnectionSnapshot snapshot)
        {
            snapshot = default;
            return connectionId.IsValid
                && _byId.TryGetValue(connectionId.Value, out snapshot);
        }

        /// <summary>出発エリアと出入口から引く（出入口が要求を出すときの経路）。</summary>
        public bool TryGetFromExit(StableId fromAreaId, StableId exitId, out AreaConnectionSnapshot snapshot)
        {
            snapshot = default;
            return fromAreaId.IsValid && exitId.IsValid
                && _byExit.TryGetValue(ExitKey(fromAreaId, exitId), out snapshot);
        }

        /// <summary>逆方向の接続を引く。</summary>
        public bool TryGetReverse(StableId connectionId, out AreaConnectionSnapshot snapshot)
        {
            snapshot = default;
            return TryGet(connectionId, out AreaConnectionSnapshot forward)
                && TryGet(forward.ReverseConnectionId, out snapshot);
        }

        private static string ExitKey(StableId areaId, StableId exitId) =>
            areaId.Value + "/" + exitId.Value;
    }

    /// <summary>
    /// 受理時に固定した接続の値（§3.1 末尾）。走っている遷移はこれだけを見る。
    /// </summary>
    public readonly struct AreaConnectionSnapshot
    {
        internal AreaConnectionSnapshot(AreaConnectionDefinition c)
        {
            ConnectionId = c.ConnectionId;
            FromAreaId = c.FromAreaId;
            ExitId = c.ExitId;
            ToAreaId = c.ToAreaId;
            EntryId = c.EntryId;
            Style = c.Style;
            Direction = c.Direction;
            ReverseConnectionId = c.ReverseConnectionId;
            SlideDuration = c.SlideDuration;
            PreloadDistance = c.PreloadDistance;
        }

        /// <summary>接続の安定 ID。</summary>
        public StableId ConnectionId { get; }

        /// <summary>出発エリア。</summary>
        public StableId FromAreaId { get; }

        /// <summary>出発側の出入口。</summary>
        public StableId ExitId { get; }

        /// <summary>到着エリア。</summary>
        public StableId ToAreaId { get; }

        /// <summary>到着側の入口。</summary>
        public StableId EntryId { get; }

        /// <summary>見せ方。</summary>
        public AreaTransitionStyle Style { get; }

        /// <summary>接続の向き。</summary>
        public AreaConnectionDirection Direction { get; }

        /// <summary>逆方向の接続 ID。</summary>
        public StableId ReverseConnectionId { get; }

        /// <summary>スライドの所要秒。</summary>
        public float SlideDuration { get; }

        /// <summary>先読みを始める距離。</summary>
        public float PreloadDistance { get; }

        /// <summary>スライドで見せる接続か。</summary>
        public bool IsSlide => Style == AreaTransitionStyle.Slide;

        /// <summary>この接続が使う軸（東西＝X、南北＝Z）。</summary>
        public AreaConnectionRules.Axis Axis => AreaConnectionRules.AxisOf(Direction);
    }
}

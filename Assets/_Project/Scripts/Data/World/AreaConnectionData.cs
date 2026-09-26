using System.Collections.Generic;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Data.World
{
    /// <summary>エリア遷移の見せ方（P5.5 仕様書 §3.1）。</summary>
    public enum AreaTransitionStyle
    {
        /// <summary>全画面暗転。<b>既定</b>。家・洞窟の扉、死亡再開はこちら（§0）。</summary>
        Fade = 0,

        /// <summary>地続きのスライド。両 Area の実マップを描画したままカメラを送る。</summary>
        Slide = 1,
    }

    /// <summary>接続の向き（§3.1）。Slide では必須。</summary>
    public enum AreaConnectionDirection
    {
        /// <summary>未指定。Slide では不正。</summary>
        None = 0,

        East = 1,
        West = 2,
        North = 3,
        South = 4,
    }

    /// <summary>
    /// 一方向のエリア接続レコード（P5.5 仕様書 §3.1）。
    ///
    /// <b>Scene 内の Transform を持たない。</b> 解決は「Data の ID」と「Scene 側の明示 Binding」で行う（§3.1）。
    /// 到着位置そのものは Scene の入口が持ち、ここは<b>どの入口か</b>だけを指す。
    ///
    /// <b>往復は 2 レコードで表す。</b> 逆方向は <see cref="ReverseConnectionId"/> で結び、
    /// 向きが反転していることを検査する（§10.1）。1 レコードを双方向に使い回すと、
    /// 方向・演出時間・到着入口を片側だけ変えたいときに必ず破綻する。
    /// </summary>
    [System.Serializable]
    public sealed class AreaConnectionDefinition
    {
        [Header("識別")]
        [Tooltip("接続の安定 ID。一方向レコードごとに一意。")]
        [SerializeField] private StableId _connectionId;

        [Header("出発")]
        [Tooltip("出発エリア。")]
        [SerializeField] private StableId _fromAreaId;

        [Tooltip("出発側の出入口。")]
        [SerializeField] private StableId _exitId;

        [Header("到着")]
        [Tooltip("到着エリア。")]
        [SerializeField] private StableId _toAreaId;

        [Tooltip("到着側の入口。AreaDefinition の入口 ID を指す。")]
        [SerializeField] private StableId _entryId;

        [Header("見せ方")]
        [Tooltip("Fade（暗転）か Slide（地続き）か。既定は Fade。")]
        [SerializeField] private AreaTransitionStyle _style = AreaTransitionStyle.Fade;

        [Tooltip("接続の向き。Slide では必須。")]
        [SerializeField] private AreaConnectionDirection _direction = AreaConnectionDirection.None;

        [Tooltip("対になる逆方向の接続 ID（往復整合の検査に使う）。")]
        [SerializeField] private StableId _reverseConnectionId;

        [Header("演出値")]
        [Tooltip("スライドの所要秒。初期値 0.45。試遊調整範囲は 0.30〜0.70。")]
        [SerializeField] private float _slideDuration = DefaultSlideDuration;

        [Tooltip("先読みを始める出入口からの XZ 距離（world units）。初期値 6。")]
        [SerializeField] private float _preloadDistance = DefaultPreloadDistance;

        /// <summary>スライドの既定所要秒（§3.1）。</summary>
        public const float DefaultSlideDuration = 0.45f;

        /// <summary>試遊で調整してよい下限（§3.1）。</summary>
        public const float MinSlideDuration = 0.30f;

        /// <summary>試遊で調整してよい上限（§3.1）。</summary>
        public const float MaxSlideDuration = 0.70f;

        /// <summary>先読み距離の既定（§3.1）。</summary>
        public const float DefaultPreloadDistance = 6f;

        /// <summary>接続の安定 ID。</summary>
        public StableId ConnectionId => _connectionId;

        /// <summary>出発エリア。</summary>
        public StableId FromAreaId => _fromAreaId;

        /// <summary>出発側の出入口。</summary>
        public StableId ExitId => _exitId;

        /// <summary>到着エリア。</summary>
        public StableId ToAreaId => _toAreaId;

        /// <summary>到着側の入口。</summary>
        public StableId EntryId => _entryId;

        /// <summary>見せ方。</summary>
        public AreaTransitionStyle Style => _style;

        /// <summary>接続の向き。</summary>
        public AreaConnectionDirection Direction => _direction;

        /// <summary>対になる逆方向の接続 ID。</summary>
        public StableId ReverseConnectionId => _reverseConnectionId;

        /// <summary>スライドの所要秒。</summary>
        public float SlideDuration => _slideDuration;

        /// <summary>先読みを始める距離。</summary>
        public float PreloadDistance => _preloadDistance;

        /// <summary>Builder・テストから組み立てる。</summary>
        public void Configure(
            StableId connectionId, StableId fromAreaId, StableId exitId,
            StableId toAreaId, StableId entryId,
            AreaTransitionStyle style, AreaConnectionDirection direction,
            StableId reverseConnectionId,
            float slideDuration = DefaultSlideDuration,
            float preloadDistance = DefaultPreloadDistance)
        {
            _connectionId = connectionId;
            _fromAreaId = fromAreaId;
            _exitId = exitId;
            _toAreaId = toAreaId;
            _entryId = entryId;
            _style = style;
            _direction = direction;
            _reverseConnectionId = reverseConnectionId;
            _slideDuration = slideDuration;
            _preloadDistance = preloadDistance;
        }
    }

    /// <summary>
    /// エリア接続の一覧（P5.5 仕様書 §3.1）。
    ///
    /// <b>P5 のカタログとは別の Asset にする。</b> 同じ AreaId の P5／P5.5 レイアウトは
    /// 別の試遊構成として選ぶもので、同じ Session へ混在させない（§3.2）。
    /// カタログを拡張して両方を持たせると、どちらの配置で動いているのかが実行時まで分からなくなる。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_AreaConnections_New", menuName = "Momotaro/World/Area Connections")]
    public sealed class AreaConnectionData : GameDataAsset
    {
        [Header("接続（一方向レコード）")]
        [SerializeField] private List<AreaConnectionDefinition> _connections = new List<AreaConnectionDefinition>();

        /// <summary>登録されている接続（読み取り専用）。</summary>
        public IReadOnlyList<AreaConnectionDefinition> Connections => _connections;

        /// <summary>Builder・テストから差し替える。</summary>
        public void SetConnections(IEnumerable<AreaConnectionDefinition> connections)
        {
            _connections = new List<AreaConnectionDefinition>();
            if (connections == null)
            {
                return;
            }

            foreach (AreaConnectionDefinition c in connections)
            {
                if (c != null)
                {
                    _connections.Add(c);
                }
            }
        }

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);

            if (_connections.Count == 0)
            {
                report.Error(name + ": 接続が 1 件もありません。");
                return;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < _connections.Count; i++)
            {
                AreaConnectionDefinition c = _connections[i];
                if (c == null)
                {
                    report.Error(name + ": Connections[" + i + "] が null です。");
                    continue;
                }

                if (!c.ConnectionId.IsValid)
                {
                    report.Error(name + ": Connections[" + i + "] の ConnectionId が不正です。");
                    continue;
                }

                if (!seen.Add(c.ConnectionId.Value))
                {
                    report.Error(name + ": ConnectionId が重複しています: " + c.ConnectionId.Value);
                }

                AreaConnectionRules.Validate(c, report.Error, name);
            }

            AreaConnectionRules.ValidateReversePairs(_connections, report.Error, name);
        }
    }
}

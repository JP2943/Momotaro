using System.Collections.Generic;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// Area の活動ゲート（P5.5 仕様書 §4.2）。<b>閉じた状態で出荷される。</b>
    ///
    /// <b>「読み込んでから Find して無効化する」では間に合わない。</b> Scene が読み終わった時点で
    /// <c>Awake</c>／<c>OnEnable</c> はもう走っており、Provider の奪い合いも Registry への登録も
    /// 済んでしまっている。だから<b>保存状態そのものを閉じておく</b>のが唯一の確実な手（§4.2）。
    ///
    /// 閉じ方は 3 通り。地形と仕掛けの<b>見た目は残したまま</b>、活動だけを止めるため。
    /// <list type="bullet">
    /// <item><description><b>根を非 Active に</b>：Gameplay 一式（AreaSystems）。中身は丸ごと動かない。</description></item>
    /// <item><description><b>Collider を無効に</b>：地形・仕掛けの物理。見た目はそのままで、当たらなくなる。</description></item>
    /// <item><description><b>部品を無効に</b>：仕掛けの登録（レバー・扉・調査対象）。見た目はそのままで、
    /// <c>OnEnable</c> が走らないので登録簿に載らない。</description></item>
    /// </list>
    ///
    /// <b>既定は「読み込んだらすぐ開ける」。</b> 先読みを頼まれていないときは <c>Awake</c> で開ける。
    /// これで P3.5／P4／P5 の単一 Area 構成・直開き・既存のテストは従来どおり動く
    /// （§1.2「Camera を一括改造しない」と同じ考え方で、既存経路を条件分岐で守る）。
    /// 閉じたままにするのは、常駐が <see cref="AreaStagingRequest"/> でこの Area を名指しで頼んだときだけ。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaActivityGate : MonoBehaviour
    {
        [Tooltip("この Area の根。先読み要求の宛先照合に使う。")]
        [SerializeField] private AreaRoot _areaRoot;

        [Tooltip("閉じている間は非 Active にする根（Gameplay 一式）。保存時から非 Active。")]
        [SerializeField] private List<GameObject> _gatedRoots = new List<GameObject>();

        [Tooltip("閉じている間は無効にする Collider（地形・仕掛けの物理）。保存時から無効。")]
        [SerializeField] private List<Collider> _gatedColliders = new List<Collider>();

        [Tooltip("閉じている間は無効にする部品（仕掛けの登録など）。保存時から無効。")]
        [SerializeField] private List<Behaviour> _gatedBehaviours = new List<Behaviour>();

        [Tooltip("先読みのときだけ無効にする表示系（Camera／AudioListener／Light）。<b>保存時は有効</b>。")]
        [SerializeField] private List<Behaviour> _stagedOnlyBehaviours = new List<Behaviour>();

        [Tooltip("出荷時（閉じる直前）の有効状態。**初回 Open はこれを復元する**。工程 P55-14a。")]
        [SerializeField] private List<bool> _initialRootState = new List<bool>();

        [Tooltip("出荷時（閉じる直前）の Collider の有効状態。")]
        [SerializeField] private List<bool> _initialColliderState = new List<bool>();

        [Tooltip("出荷時（閉じる直前）の部品の有効状態。")]
        [SerializeField] private List<bool> _initialBehaviourState = new List<bool>();

        [Tooltip("出荷時（閉じる直前）の表示系の有効状態。")]
        [SerializeField] private List<bool> _initialStagedOnlyState = new List<bool>();

        [Tooltip("出荷時の有効状態を持っているか（持っていない古い Scene は一律有効化へ落ちる）。")]
        [SerializeField] private bool _hasInitialState;

        private readonly List<bool> _rootState = new List<bool>();
        private readonly List<bool> _colliderState = new List<bool>();
        private readonly List<bool> _behaviourState = new List<bool>();
        private readonly List<bool> _stagedOnlyState = new List<bool>();
        private bool _hasRestoreState;

        /// <summary>いま開いているか。</summary>
        public bool IsOpen { get; private set; }

        /// <summary>開けた回数（診断・テスト用）。</summary>
        public int OpenCount { get; private set; }

        /// <summary>すでに開いているのに <see cref="Open"/> を呼ばれて無操作とした回数（診断・テスト用）。</summary>
        public int RedundantOpenCount { get; private set; }

        /// <summary>閉めた回数（診断・テスト用）。</summary>
        public int CloseCount { get; private set; }

        /// <summary>読み込み時にその場で開けたか（診断・テスト用）。先読みなら false。</summary>
        public bool OpenedOnLoad { get; private set; }

        /// <summary>出荷時の有効状態を持っているか（Validator・テスト用。工程 P55-14a）。</summary>
        public bool HasInitialState => _hasInitialState;

        /// <summary>
        /// <b>復元すべき状態が無く、一律に有効化した回数</b>（診断・テスト用。工程 P55-14a）。
        ///
        /// 0 でない Scene は、<b>持ち主が別に居る Collider まで有効にしている</b>——
        /// 戦闘中だけ有効なアリーナ封鎖や、開通して自分を無効にした門がそれに当たる。
        /// 黙って落ちる代わりに数えるのは、**出荷状態の記録が抜けた Scene を外から見分ける**ため。
        /// </summary>
        public int UniformOpenCount { get; private set; }

        /// <summary>この Area の安定 ID（根が未配線なら空）。</summary>
        public StableId AreaId => _areaRoot != null ? _areaRoot.AreaId : default;

        /// <summary>閉じる対象（読み取り専用。Validator・テスト用）。</summary>
        public IReadOnlyList<GameObject> GatedRoots => _gatedRoots;

        /// <summary>無効にする Collider（読み取り専用）。</summary>
        public IReadOnlyList<Collider> GatedColliders => _gatedColliders;

        /// <summary>無効にする部品（読み取り専用）。</summary>
        public IReadOnlyList<Behaviour> GatedBehaviours => _gatedBehaviours;

        /// <summary>
        /// 先読みのときだけ無効にする表示系（読み取り専用）。
        ///
        /// <b>これだけは保存時に有効のままにする。</b> Camera・AudioListener・Light には
        /// 登録や購読の副作用が無い——「描く」「聞く」だけなので、
        /// <c>Awake</c> で切れば 1 フレームも描かれずに済む。保存時から切っておくと
        /// Editor で Scene を開いたときに Game ビューが真っ黒になるので、
        /// <b>作業中の見やすさを壊さない側に寄せた</b>。
        /// </summary>
        public IReadOnlyList<Behaviour> StagedOnlyBehaviours => _stagedOnlyBehaviours;

        /// <summary>
        /// 配線が揃っているか。<b>根が 1 つも無いゲートは配線漏れ</b>——
        /// 「閉じているつもりで何も閉じていない」が一番危ない状態なので、空を通さない。
        /// Collider・部品は構成によって無い場合があるので必須に含めない。
        /// </summary>
        public bool IsWired
        {
            get
            {
                if (_areaRoot == null || _gatedRoots == null || _gatedRoots.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < _gatedRoots.Count; i++)
                {
                    if (_gatedRoots[i] == null)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// 保存状態が閉じているか（Validator の検査対象。§10.2）。
        /// <b>Edit モードで見ることに意味がある。</b> 実行時は <c>Awake</c> が開けてしまうので、
        /// 「出荷時に閉じているか」は Scene を開いた状態でしか確かめられない。
        /// </summary>
        public bool IsClosedAsSaved(out string reason)
        {
            for (int i = 0; i < _gatedRoots.Count; i++)
            {
                GameObject go = _gatedRoots[i];
                if (go != null && go.activeSelf)
                {
                    reason = "根が Active のまま保存されています（" + go.name + "）。";
                    return false;
                }
            }

            for (int i = 0; i < _gatedColliders.Count; i++)
            {
                Collider c = _gatedColliders[i];
                if (c != null && c.enabled)
                {
                    reason = "Collider が有効のまま保存されています（" + c.name + "）。";
                    return false;
                }
            }

            for (int i = 0; i < _gatedBehaviours.Count; i++)
            {
                Behaviour b = _gatedBehaviours[i];
                if (b != null && b.enabled)
                {
                    reason = "部品が有効のまま保存されています（" + b.name + " / " + b.GetType().Name + "）。";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private void Awake()
        {
            // 名指しで先読みを頼まれている Area だけ、閉じたまま待つ。
            //
            // 宛先を照合するのは、要求が漏れ残ったときの壊れ方を軽くするため。
            // 無条件フラグにすると、要求したのに読まれなかった場合、
            // <b>次に直開きした Scene が閉じたまま起動する</b>（＝何も動かないゲームに見える）。
            if (AreaStagingRequest.TryConsumeFor(AreaId))
            {
                // 表示系はここで切る。<b>最初の描画の前</b>なので、
                // 2 枚目の Camera と AudioListener が一瞬でも活きることはない。
                ApplyStagedOnly(false);
                IsOpen = false;
                return;
            }

            Open();
            OpenedOnLoad = true;
        }

        /// <summary>
        /// 活動を許可する（§4.2）。二度呼んでも同じ状態に落ち着く。
        ///
        /// <b>一律に有効化しない。</b> 閉める直前の有効状態を覚えていればそれを復元する
        /// （GPT レビュー R11 の指摘 2）。
        ///
        /// 一律に有効化すると、<b>開通済みの門が再び塞がる</b>——門は開通したときに
        /// 自分の Collider と Obstacle を無効にしているので、ゲートがそれを有効に戻すと
        /// 「見た目は開いているのに通れない」になる。遷移失敗から出発側を再開する経路で起きる。
        ///
        /// <b>初回起動は別扱い。</b> 保存状態は「すべて停止」で、復元すべき前の状態がないので、
        /// そのときだけ一律に有効化する。
        /// </summary>
        public void Open()
        {
            // <b>すでに開いているなら何も書き換えない</b>（GPT レビュー R12）。
            //
            // 重複呼び出しで状態を押し直すと、開いている間に仕掛けが自分で変えた状態を壊す。
            // 残っていた経路は二つ。
            // （1）初回起動→門開通→重複 Open：復元記録が無いので、一律に有効化して
            //     門の Collider／Obstacle を再有効化してしまう。
            // （2）門が閉じた状態で Close→Open→門開通→重複 Open：
            //     過去の「門が閉じていた状態」を復元して再び塞ぐ。
            // 明示的な再同期が必要なときは別の入口で扱う
            // （門なら <c>AreaFlagDoor.TryReapplyOpened</c>）。
            if (IsOpen)
            {
                RedundantOpenCount++;
                return;
            }

            if (_hasRestoreState)
            {
                // 二度目以降：閉める直前の状態へ戻す。
                Restore();
            }
            else if (_hasInitialState)
            {
                // <b>初回：出荷時の状態へ戻す</b>（工程 P55-14a。GPT 受入 2）。
                //
                // 以前はここで一律に有効化していた。「復元すべき前の状態がない」と考えたが、
                // **出荷状態こそが復元すべき初期状態だった**。
                // 一律に有効化すると、<b>わざと無効で出荷した Collider まで有効になる</b>——
                // 実測で、到着した Area のアリーナ封鎖 4 枚が
                // <c>IsEnabled=false</c>（封鎖していない）のまま
                // <c>ActiveBlockerCount=4</c>（実体は壁）になっていた。
                // 戦闘区域へ歩いて入れなくなる。
                RestoreInitial();
            }
            else
            {
                // 出荷状態の記録が無い（この工程より前に作られた Scene・手組み）。
                // 黙って落ちないように数える。
                UniformOpenCount++;
                Apply(true);
            }

            IsOpen = true;
            OpenCount++;
        }

        /// <summary>
        /// 活動を止める（撤去・先読みのやり直し）。
        /// <b>止める前の有効状態を覚えておく</b>——次の <see cref="Open"/> でそれを戻す。
        /// </summary>
        public void Close()
        {
            // <b>開いているときだけ覚える。</b> 既に閉じている状態を覚えると
            // 「すべて停止」が復元対象になり、次の Open() で Area が永久に目覚めなくなる
            // （出荷状態の直後に Close を呼ばれる経路で実際に踏んだ）。
            // 「止める前の状態」は「最後に動いていたときの状態」のこと。
            if (IsOpen)
            {
                CaptureRestoreState();
            }

            Apply(false);
            IsOpen = false;
            CloseCount++;
        }

        /// <summary>閉める前の有効状態を覚えているか（診断・テスト用）。</summary>
        public bool HasRestoreState => _hasRestoreState;

        private readonly List<bool> _probeState = new List<bool>();
        private bool _probing;

        /// <summary>障害物検査のために当たりを戻した回数（診断・テスト用。工程 P55-07c）。</summary>
        public int ObstacleProbeCount { get; private set; }

        /// <summary>
        /// <b>閉じたまま、地形の当たりだけを検査のあいだ戻す</b>（工程 P55-07c。GPT 再修正②）。
        ///
        /// スライドの表示経路検査（§7.2）は <c>Physics.SphereCast</c> で壁を見るが、
        /// その時点で出発側は既に閉じており、<b>地形の Collider は無効</b>である。
        /// そのままでは<b>出発側の壁が 1 枚も見えない</b>——犬丸が壁の向こうに居ても
        /// 経路が通っていることになり、代理が壁を突き抜ける絵を許してしまう。
        ///
        /// <b>Gameplay は再開しない。</b> 戻すのは Collider だけで、根（AreaSystems・主人公・犬丸）も
        /// 仕掛けの部品（登録・Trigger・NavMesh）も止めたままにする。検査のために
        /// 1 フレームでも活動を再開させたら、それは「閉じている」という約束の破棄になる。
        ///
        /// <b>一律に有効化はしない。</b> 戻すのは<see cref="Close"/> が覚えた
        /// 「止める直前の有効状態」である。一律に有効化すると、<b>開通済みの門が壁として映る</b>
        /// ——門は開通したときに自分の Collider を無効にしているので、
        /// 検査だけが「通れない」と言い出す（<see cref="Open"/> と同じ理由）。
        /// </summary>
        public void BeginObstacleProbe()
        {
            if (_probing || IsOpen || !_hasRestoreState)
            {
                return;
            }

            _probeState.Clear();
            for (int i = 0; i < _gatedColliders.Count; i++)
            {
                Collider c = _gatedColliders[i];
                _probeState.Add(c != null && c.enabled);
                if (c != null && i < _colliderState.Count)
                {
                    c.enabled = _colliderState[i];
                }
            }

            _probing = true;
            ObstacleProbeCount++;
        }

        /// <summary>検査のあいだ戻していた当たりを、元の（閉じた）状態へ戻す。</summary>
        public void EndObstacleProbe()
        {
            if (!_probing)
            {
                return;
            }

            for (int i = 0; i < _gatedColliders.Count && i < _probeState.Count; i++)
            {
                Collider c = _gatedColliders[i];
                if (c != null)
                {
                    c.enabled = _probeState[i];
                }
            }

            _probing = false;
        }

        /// <summary>いま検査のために当たりを戻している最中か（診断・テスト用）。</summary>
        public bool IsProbingObstacles => _probing;

        /// <summary>
        /// 出荷状態（閉じる直前の有効状態）を記録する（Editor 専用の入口から呼ばれる）。
        ///
        /// <b>閉じたあとに呼んではいけない。</b> 全部無効になった状態を出荷状態として覚えると、
        /// 初回 Open で Area が永久に目覚めない（<see cref="Close"/> が
        /// 同じ理由で「開いているときだけ覚える」と書いているのと同じ罠）。
        /// </summary>
        public void CaptureInitialState()
        {
            _initialRootState.Clear();
            for (int i = 0; i < _gatedRoots.Count; i++)
            {
                _initialRootState.Add(_gatedRoots[i] != null && _gatedRoots[i].activeSelf);
            }

            _initialColliderState.Clear();
            for (int i = 0; i < _gatedColliders.Count; i++)
            {
                _initialColliderState.Add(_gatedColliders[i] != null && _gatedColliders[i].enabled);
            }

            _initialBehaviourState.Clear();
            for (int i = 0; i < _gatedBehaviours.Count; i++)
            {
                _initialBehaviourState.Add(_gatedBehaviours[i] != null && _gatedBehaviours[i].enabled);
            }

            _initialStagedOnlyState.Clear();
            for (int i = 0; i < _stagedOnlyBehaviours.Count; i++)
            {
                _initialStagedOnlyState.Add(
                    _stagedOnlyBehaviours[i] != null && _stagedOnlyBehaviours[i].enabled);
            }

            _hasInitialState = true;
        }

        /// <summary>出荷時の Collider の有効状態（Validator・テスト用）。</summary>
        public IReadOnlyList<bool> InitialColliderState => _initialColliderState;

        private void CaptureRestoreState()
        {
            _rootState.Clear();
            for (int i = 0; i < _gatedRoots.Count; i++)
            {
                _rootState.Add(_gatedRoots[i] != null && _gatedRoots[i].activeSelf);
            }

            _colliderState.Clear();
            for (int i = 0; i < _gatedColliders.Count; i++)
            {
                _colliderState.Add(_gatedColliders[i] != null && _gatedColliders[i].enabled);
            }

            _behaviourState.Clear();
            for (int i = 0; i < _gatedBehaviours.Count; i++)
            {
                _behaviourState.Add(_gatedBehaviours[i] != null && _gatedBehaviours[i].enabled);
            }

            _stagedOnlyState.Clear();
            for (int i = 0; i < _stagedOnlyBehaviours.Count; i++)
            {
                _stagedOnlyState.Add(_stagedOnlyBehaviours[i] != null && _stagedOnlyBehaviours[i].enabled);
            }

            _hasRestoreState = true;
        }

        /// <summary>
        /// <b>出荷時の有効状態へ戻す</b>（初回 Open。工程 P55-14a）。
        ///
        /// 順序は <see cref="Apply"/> と同じ規律（物理・NavMesh が先、Gameplay が後）。
        /// 記録が足りない要素は<b>有効にする</b>——地形が通れない Area を作るより、
        /// 記録の抜けを <see cref="UniformOpenCount"/> と Validator で捕まえるほうが軽い。
        /// </summary>
        private void RestoreInitial()
        {
            for (int i = 0; i < _gatedColliders.Count; i++)
            {
                Collider c = _gatedColliders[i];
                if (c != null)
                {
                    c.enabled = i < _initialColliderState.Count ? _initialColliderState[i] : true;
                }
            }

            for (int i = 0; i < _gatedBehaviours.Count; i++)
            {
                Behaviour b = _gatedBehaviours[i];
                if (b != null)
                {
                    b.enabled = i < _initialBehaviourState.Count ? _initialBehaviourState[i] : true;
                }
            }

            for (int i = 0; i < _stagedOnlyBehaviours.Count; i++)
            {
                Behaviour b = _stagedOnlyBehaviours[i];
                if (b != null)
                {
                    b.enabled = i < _initialStagedOnlyState.Count ? _initialStagedOnlyState[i] : true;
                }
            }

            for (int i = 0; i < _gatedRoots.Count; i++)
            {
                GameObject go = _gatedRoots[i];
                if (go != null)
                {
                    go.SetActive(i < _initialRootState.Count ? _initialRootState[i] : true);
                }
            }
        }

        /// <summary>覚えている有効状態を戻す。順序は <see cref="Apply"/> と同じ規律。</summary>
        private void Restore()
        {
            for (int i = 0; i < _gatedColliders.Count && i < _colliderState.Count; i++)
            {
                if (_gatedColliders[i] != null)
                {
                    _gatedColliders[i].enabled = _colliderState[i];
                }
            }

            for (int i = 0; i < _gatedBehaviours.Count && i < _behaviourState.Count; i++)
            {
                if (_gatedBehaviours[i] != null)
                {
                    _gatedBehaviours[i].enabled = _behaviourState[i];
                }
            }

            for (int i = 0; i < _stagedOnlyBehaviours.Count && i < _stagedOnlyState.Count; i++)
            {
                if (_stagedOnlyBehaviours[i] != null)
                {
                    _stagedOnlyBehaviours[i].enabled = _stagedOnlyState[i];
                }
            }

            for (int i = 0; i < _gatedRoots.Count && i < _rootState.Count; i++)
            {
                if (_gatedRoots[i] != null)
                {
                    _gatedRoots[i].SetActive(_rootState[i]);
                }
            }
        }

        /// <summary>
        /// 開閉を適用する。<b>順序が意味を持つ。</b>
        ///
        /// 開けるときは<b>物理・NavMesh が先、Gameplay が後</b>。
        /// 逆にすると、NavMesh が登録される前に NavMeshAgent が目覚めて
        /// 「近くに NavMesh がない」で警告を出し、主人公や犬丸が動かない。
        /// 閉めるときはその逆で、Gameplay を先に止めてから土台を外す
        /// （§4.3「到着側を有効化する際は出発側を先に閉じる」と同じ規律）。
        /// </summary>
        private void Apply(bool active)
        {
            if (active)
            {
                ApplyColliders(true);
                ApplyBehaviours(true);
                ApplyStagedOnly(true);
                ApplyRoots(true);
                return;
            }

            ApplyRoots(false);
            ApplyBehaviours(false);
            ApplyColliders(false);
            ApplyStagedOnly(false);
        }

        private void ApplyRoots(bool active)
        {
            for (int i = 0; i < _gatedRoots.Count; i++)
            {
                GameObject go = _gatedRoots[i];
                if (go != null)
                {
                    go.SetActive(active);
                }
            }
        }

        private void ApplyColliders(bool active)
        {
            for (int i = 0; i < _gatedColliders.Count; i++)
            {
                Collider c = _gatedColliders[i];
                if (c != null)
                {
                    c.enabled = active;
                }
            }
        }

        private void ApplyBehaviours(bool active)
        {
            for (int i = 0; i < _gatedBehaviours.Count; i++)
            {
                Behaviour b = _gatedBehaviours[i];
                if (b != null)
                {
                    b.enabled = active;
                }
            }
        }

        /// <summary>表示系の開閉（保存状態は有効なので、閉めるのは先読みのときだけ）。</summary>
        private void ApplyStagedOnly(bool active)
        {
            for (int i = 0; i < _stagedOnlyBehaviours.Count; i++)
            {
                Behaviour b = _stagedOnlyBehaviours[i];
                if (b != null)
                {
                    b.enabled = active;
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>Builder から組み立てるための設定入口（Editor 専用）。</summary>
        public void EditorSet(
            AreaRoot areaRoot,
            List<GameObject> gatedRoots,
            List<Collider> gatedColliders = null,
            List<Behaviour> gatedBehaviours = null,
            List<Behaviour> stagedOnlyBehaviours = null)
        {
            _areaRoot = areaRoot;
            _gatedRoots = gatedRoots ?? new List<GameObject>();
            _gatedColliders = gatedColliders ?? new List<Collider>();
            _gatedBehaviours = gatedBehaviours ?? new List<Behaviour>();
            _stagedOnlyBehaviours = stagedOnlyBehaviours ?? new List<Behaviour>();
        }

        /// <summary>
        /// 出荷状態（閉じた状態）にして保存させる（Editor 専用）。
        /// <b>NavMesh の焼き込みが終わったあとに呼ぶ。</b> 先に閉じると Collider が
        /// 収集対象から外れて、経路の無い NavMesh が焼ける。
        /// </summary>
        public void EditorCloseForShipping()
        {
            // <b>閉じる前に、いまの有効状態を出荷状態として残す</b>（工程 P55-14a）。
            //
            // これが無いと初回 Open が一律に有効化するしかなくなり、
            // **わざと無効で置いた Collider（アリーナ封鎖など）まで有効になる**。
            CaptureInitialState();

            Apply(false);

            // <b>表示系は有効のまま出荷する。</b> 保存時に切ると
            // Editor で Scene を開いたときに Game ビューが真っ黒になる。
            // 副作用が無いので、先読みのときに Awake で切れば間に合う。
            ApplyStagedOnly(true);
            IsOpen = false;
        }
#endif
    }

    /// <summary>
    /// 「次に読む Area は閉じたまま待たせる」という常駐からの申し入れ（P5.5 §4.2／§5）。
    ///
    /// <b>Scene 側に判断させない。</b> 先読みかどうかを知っているのは常駐だけで、
    /// Area Scene は自分が先読みされているのか直接開かれたのかを知らない。
    ///
    /// <b>宛先を持たせている。</b> 無条件のフラグにすると、要求したのに Scene が読まれなかった場合
    /// （開始失敗・タイムアウト）にフラグが残り、次に直開きした Scene が閉じたまま起動する。
    /// 「何も動かないゲーム」は原因が最も追いにくい壊れ方なので、宛先違いなら素通りさせる。
    /// </summary>
    public static class AreaStagingRequest
    {
        /// <summary>いま要求があるか。</summary>
        public static bool IsRequested { get; private set; }

        /// <summary>要求の宛先（無ければ空）。</summary>
        public static StableId AreaId { get; private set; }

        /// <summary>要求した回数（診断・テスト用）。</summary>
        public static int RequestedCount { get; private set; }

        /// <summary>宛先が一致して消費された回数（診断・テスト用）。</summary>
        public static int ConsumedCount { get; private set; }

        /// <summary>宛先が違って素通りさせた回数（診断・テスト用）。</summary>
        public static int MismatchCount { get; private set; }

        /// <summary>要求する。<b>重ねて要求しない</b>（直列化は在留台帳が持つ）。</summary>
        public static bool TryRequest(StableId areaId)
        {
            if (!areaId.IsValid || IsRequested)
            {
                return false;
            }

            IsRequested = true;
            AreaId = areaId;
            RequestedCount++;
            return true;
        }

        /// <summary>取り下げる（読込の開始失敗・タイムアウト・テストの後始末）。</summary>
        public static void Clear()
        {
            IsRequested = false;
            AreaId = default;
        }

        /// <summary>
        /// 宛先が一致すれば消費して true（＝閉じたまま待つ）。
        /// 一致しなければ要求はそのまま残し、false を返す（その Scene は通常どおり開く）。
        /// </summary>
        public static bool TryConsumeFor(StableId areaId)
        {
            if (!IsRequested)
            {
                return false;
            }

            if (!areaId.IsValid || !areaId.Equals(AreaId))
            {
                MismatchCount++;
                return false;
            }

            IsRequested = false;
            AreaId = default;
            ConsumedCount++;
            return true;
        }

        /// <summary>診断値ごと戻す（テストの後始末）。</summary>
        public static void ResetForTests()
        {
            IsRequested = false;
            AreaId = default;
            RequestedCount = 0;
            ConsumedCount = 0;
            MismatchCount = 0;
        }
    }
}

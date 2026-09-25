namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 再開操作（UI の Submit）の押下を運ぶ狭い契約（P5-08。仕様書 v1.1 §9.1 手順 3）。
    /// </summary>
    public interface IRespawnSubmitInput
    {
        /// <summary>押下が溜まっているか。</summary>
        bool SubmitPressed { get; }

        /// <summary>押下を 1 回取り出す。連打しても押下エッジの数しか出ない。</summary>
        bool ConsumeSubmitPressed();

        /// <summary>溜まっている押下を捨てる（Map 切替・到着時）。</summary>
        void DiscardSubmitPressed();
    }

    /// <summary>
    /// <see cref="IRespawnSubmitInput"/> の状態を保持する純粋クラス（P5-08）。
    ///
    /// <b>押下エッジだけを数える。</b> 再開は 1 回しか起きない操作なので、長押しで何度も
    /// 受理されてはいけない（§9.1 手順 3「キー／ゲームパッドの二重実行を防ぐ」）。
    ///
    /// <b>「離した」の判定を入力ソースに委ねる。</b> Action Map を切り替えると、押しっぱなしの
    /// まま canceled が飛んでくる。これを離したと解釈すると、Map が戻った瞬間に押下エッジが
    /// もう一度立って、再開ボタンが到着先の操作へ化ける（§9.1 末尾が禁じている挙動）。
    /// だから <see cref="SetSubmit"/> には<b>物理的に離れたときだけ</b> false を渡す約束にしてある。
    /// </summary>
    public sealed class RespawnSubmitState : IRespawnSubmitInput
    {
        private bool _heldRaw;
        private bool _latched;

        /// <inheritdoc />
        public bool SubmitPressed => _latched;

        /// <summary>押下している物理ボタンがあるか（診断・テスト用）。</summary>
        public bool IsHeld => _heldRaw;

        /// <summary>押下エッジを数えた回数（診断・テスト用）。</summary>
        public int PressCount { get; private set; }

        /// <summary>入力ソースから生の押下状態を設定する。押下エッジ（false→true）でのみラッチする。</summary>
        public void SetSubmit(bool pressed)
        {
            bool rising = pressed && !_heldRaw;
            _heldRaw = pressed;

            if (rising)
            {
                _latched = true;
                PressCount++;
            }
        }

        /// <inheritdoc />
        public bool ConsumeSubmitPressed()
        {
            if (!_latched)
            {
                return false;
            }

            _latched = false;
            return true;
        }

        /// <inheritdoc />
        public void DiscardSubmitPressed()
        {
            _latched = false;
        }
    }

    /// <summary>
    /// 「一度離すまで使わせない」を<b>実デバイスに照らして</b>解く窓口（P5-08。§9.1 末尾）。
    ///
    /// Button 型の Action は、押しっぱなしのまま Map を開き直しても<b>何も通知しない</b>
    /// （initialStateCheck が無い）。つまり通知だけを見ていると、解放待ちを立てたあと
    /// それを解く合図が永久に来ず、最初の 1 回を飲み込む（実際に踏んだ）。
    /// 実際のボタンの状態を毎フレーム確かめて解くのは Infrastructure の仕事なので、
    /// Gameplay 側はこの狭い契約だけを知る。
    /// </summary>
    public interface IInputReleaseGate
    {
        /// <summary>解放待ちが立っているか。</summary>
        bool RequiresRelease { get; }

        /// <summary>
        /// 実デバイスを見て、押下状態と解放待ちを整える。
        ///
        /// Map を閉じている間の離しは通知が来ないので、これを呼ばないと
        /// 「押しっぱなし」と記録したまま固まり、次の押下が押下エッジにならない。
        /// </summary>
        void PollHeldControls();
    }

    /// <summary>解放待ちの提供点（P5-08）。</summary>
    public static class InputReleaseGateProvider
    {
        /// <summary>現在の解放待ち窓口（未配線なら null）。</summary>
        public static IInputReleaseGate Current { get; set; }
    }

    /// <summary>
    /// 再開操作の提供点（P5-08）。<see cref="PlayerInputProvider"/> と同じ方針で、
    /// Infrastructure が差し、Scene 側はここから取る。
    /// </summary>
    public static class RespawnSubmitProvider
    {
        /// <summary>現在の再開操作入力（未配線なら null）。</summary>
        public static IRespawnSubmitInput Current { get; set; }
    }

    /// <summary>
    /// 再開操作の<b>受付所有権</b>（GPT レビュー R9 の指摘）。
    ///
    /// Scene 側の受付（<c>RespawnSubmitInput</c>）と、Scene が使えないときに肩代わりする
    /// 常駐側の受付（<c>CampaignRespawnResidentView</c>）は、どちらも同じ押下を見る。
    /// <b>「消費する側」だけを切り替えても足りない。</b> Scene 側は実行役が居ないと押下を
    /// <b>捨てる</b>ので、Scene 側が先に動いた順序では、常駐側が読む前に押下が消える。
    /// どちらの実行順でも成立させるには、<b>捨てる側も同じ所有権に従う</b>必要がある。
    ///
    /// 所有者は 1 人だけ。所有していない側は<b>消費も破棄もしない</b>ので、
    /// どちらが先に Update されても「1 押下＝1 受理」になる。
    /// </summary>
    public interface IRespawnSubmitOwner
    {
        /// <summary>いま常駐側が受付を所有しているか。</summary>
        bool OwnsRespawnSubmit { get; }
    }

    /// <summary>
    /// 受付所有権の窓口（同上）。常駐側が自分を差し、Scene 側はここを見てから動く。
    /// 解除は所有者一致で行う（§5.2）。
    /// </summary>
    public static class RespawnSubmitOwnerProvider
    {
        /// <summary>現在の所有権判定（未設定なら null＝Scene 側が従来どおり動く）。</summary>
        public static IRespawnSubmitOwner Current { get; set; }

        /// <summary>常駐側が所有しているか。未設定なら false（Scene 側が扱う）。</summary>
        public static bool ResidentOwnsSubmit => Current != null && Current.OwnsRespawnSubmit;

        /// <summary>自分が差したものだけを外す（所有者一致）。</summary>
        public static void ReleaseIfOwner(IRespawnSubmitOwner owned)
        {
            if (owned != null && ReferenceEquals(Current, owned))
            {
                Current = null;
            }
        }
    }
}

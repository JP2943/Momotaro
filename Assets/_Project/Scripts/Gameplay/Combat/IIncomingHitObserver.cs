namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 被弾者が<b>実命中を受け取った瞬間</b>を観測する契約（R3-02。c8c0ddf §5）。
    ///
    /// <see cref="HitResultChannel"/> は解決<b>結果</b>を配るチャネルなので、
    /// 「解決より先に済ませておくこと」には使えない。結果が出たときにはもう
    /// 無敵・Step・JG・ガード・守護・通常 Damage のいずれかが確定している。
    /// 「実命中が届いた」という<b>入口</b>だけを、解決の前に同期で知らせるのがこの契約。
    ///
    /// 呼ばれるのは、その被弾者が「この命中を自分のものとして解決する」と決めた直後
    /// （撃破後の追撃・同一命中の再入など、受け付けない命中では呼ばれない）。
    /// したがって観測側から見た意味は「防がれたかどうかに関わらず、いま実際に一撃が届いた」で、
    /// <b>危険を感知しただけの候補とは区別される</b>。
    ///
    /// 観測者は同じ呼び出しの中で完結する処理だけを行う（探索の解放など）。
    /// ここから被弾者の状態を変えたり、同じ命中を再投入したりしない。
    /// </summary>
    public interface IIncomingHitObserver
    {
        /// <summary>実命中が届いた（解決の前。同期）。</summary>
        void OnIncomingHit(in HitInfo hit);
    }

    /// <summary>
    /// 実命中の観測者を登録できる被弾者（R3-02）。登録は <c>OnEnable</c>／<c>OnDisable</c> 対称で行う。
    /// 万能 static にはせず、被弾者がインスタンスとして購読者を持つ（<see cref="HitResultChannel"/> と同系統）。
    /// </summary>
    public interface IIncomingHitSource
    {
        /// <summary>観測者を追加する（null・重複は無視）。</summary>
        void AddIncomingHitObserver(IIncomingHitObserver observer);

        /// <summary>観測者を外す（未登録なら何もしない）。</summary>
        void RemoveIncomingHitObserver(IIncomingHitObserver observer);
    }
}

namespace AndroidTvPcIsoBuilder.Infrastructure.Drivers;

/// <summary>
/// Implémentation de <see cref="IProgress{T}"/> qui invoque le callback de façon
/// strictement synchrone, sur le thread appelant. Contrairement à <see cref="Progress{T}"/>,
/// qui capture le SynchronizationContext ambiant et peut poster ses rapports de façon
/// différée (utile pour retourner sur un thread UI, mais indésirable ici : ce type sert
/// à chaîner un flux de lignes de sortie process vers un second IProgress&lt;T&gt; sans
/// perdre l'ordre ni introduire de latence).
/// </summary>
internal sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SynchronousProgress(Action<T> handler)
    {
        _handler = handler;
    }

    public void Report(T value) => _handler(value);
}

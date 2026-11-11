using System.Threading;

internal sealed class SdkBusyException : Exception
{
    public SdkBusyException() : base("MSI Mystic Light занят другим действием. Повторите попытку через несколько секунд.") { }
}

internal static class SdkSession
{
    // UI callers run this on a worker. A longer bound lets an in-flight read or a
    // scheduled hotkey finish without turning an ordinary click into a failure.
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    public static T Run<T>(Func<T> operation)
    {
        using var mutex = NamedMutexGate.Acquire(WaitTimeout);
        var initialize = Api.Initialize();
        if (initialize != Status.Ok) throw new SdkOperationException($"SDK не подключился ({initialize}). Проверьте MSI Center / Mystic Light.", initialize);

        Exception? operationError = null;
        try { return operation(); }
        catch (Exception ex) { operationError = ex; throw; }
        finally
        {
            var release = Api.Release();
            if (release != Status.Ok)
            {
                var releaseError = new SdkOperationException($"SDK завершил работу с ошибкой Release={release}.", release);
                if (operationError is null) throw releaseError;
                throw new AggregateException("Операция SDK и Release завершились с ошибками.", operationError, releaseError);
            }
        }
    }
}

internal static class NamedMutexGate
{
    internal const string MutexName = @"Global\Luma.MysticLight.SDK.v1";

    internal static IDisposable Acquire(TimeSpan timeout)
    {
        var mutex = new Mutex(false, MutexName);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(timeout); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new SdkBusyException();
            return new Lease(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private sealed class Lease(Mutex mutex) : IDisposable
    {
        private Mutex? mutex = mutex;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref mutex, null);
            if (current is null) return;
            try { current.ReleaseMutex(); }
            finally { current.Dispose(); }
        }
    }
}

internal sealed class SdkOperationException(string message, Status status) : Exception(message)
{
    public Status Status { get; } = status;
}

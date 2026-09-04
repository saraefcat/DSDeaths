using System;
using System.Threading;

namespace DSDeaths {
    public sealed class SingleInstanceGuard : IDisposable {
        public const string MutexName = @"Local\DSDeaths.Counter.Instance";

        private Mutex mutex;
        private bool ownsMutex;

        private SingleInstanceGuard(Mutex mutex, bool ownsMutex) {
            this.mutex = mutex;
            this.ownsMutex = ownsMutex;
        }

        public static bool TryAcquire(out SingleInstanceGuard guard) {
            bool createdNew;
            var instanceMutex = new Mutex(true, MutexName, out createdNew);
            guard = new SingleInstanceGuard(instanceMutex, createdNew);
            return createdNew;
        }

        public void Dispose() {
            if (mutex == null) {
                return;
            }

            if (ownsMutex) {
                try {
                    mutex.ReleaseMutex();
                } catch (ApplicationException) {
                }
            }

            ownsMutex = false;
            mutex.Dispose();
            mutex = null;
        }
    }
}

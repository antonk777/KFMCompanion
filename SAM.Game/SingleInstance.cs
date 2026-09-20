using System;
using System.Threading;

namespace SAM.Game
{
    internal sealed class SingleInstance : IDisposable
    {
        private const string MutexName = @"Local\KFMCompanion.SingleInstance";
        private const string ActivateName = @"Local\KFMCompanion.Activate";

        private readonly Mutex _mutex;
        private bool _ownsMutex;

        public EventWaitHandle Activate { get; }

        private SingleInstance(Mutex mutex, EventWaitHandle activate)
        {
            this._mutex = mutex;
            this._ownsMutex = true;
            this.Activate = activate;
        }

        public static SingleInstance TryAcquire()
        {
            var mutex = new Mutex(true, MutexName, out var createdNew);
            var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
            if (createdNew == true)
            {
                return new SingleInstance(mutex, activate);
            }

            activate.Set();
            activate.Dispose();
            mutex.Dispose();
            return null;
        }

        public void Dispose()
        {
            if (this._ownsMutex == true)
            {
                try
                {
                    this._mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }

                this._ownsMutex = false;
            }

            this._mutex.Dispose();
            this.Activate.Dispose();
        }
    }
}

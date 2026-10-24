using System;
using System.Threading;
using Arkyva.Core.Interfaces;

namespace Arkyva
{
    /// <summary>Optional application-wide access to one configured service per AppDomain.</summary>
    /// <remarks>Use ArkyvaBuilder instead for dependency injection or independent instances. No shutdown disposal is required.</remarks>
    public static class ArkyvaHost
    {
        private static readonly object Gate = new object();
        private static IArkyvaService current;
        private static bool configuring;

        /// <summary>Gets the successfully configured service without creating a new instance.</summary>
        /// <exception cref="InvalidOperationException">Configure has not completed successfully.</exception>
        public static IArkyvaService Current
        {
            get
            {
                var service = Volatile.Read(ref current);
                if (service == null)
                    throw new InvalidOperationException("Arkyva is not configured. Call ArkyvaHost.Configure before accessing Current.");
                return service;
            }
        }

        /// <summary>Synchronously configures and builds the shared service once. A failed attempt may be retried.</summary>
        /// <param name="configure">Synchronous configuration callback; do not use async void or wait for another Configure call.</param>
        /// <exception cref="ArgumentNullException">The callback is null.</exception>
        /// <exception cref="InvalidOperationException">Already configured, recursively configuring, or required configuration is missing.</exception>
        /// <remarks>Serializes configuration only, not service operations. Builder exceptions propagate unchanged. Build may create the base directory but does not connect to SQL.</remarks>
        public static void Configure(Action<ArkyvaHostOptions> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            lock (Gate)
            {
                if (current != null)
                    throw new InvalidOperationException("ArkyvaHost is already configured for this AppDomain. Use ArkyvaBuilder for independent instances.");
                if (configuring)
                    throw new InvalidOperationException("Recursive ArkyvaHost.Configure calls are not allowed.");

                configuring = true;
                var options = new ArkyvaHostOptions();
                try
                {
                    configure(options);
                    var service = options.Build();
                    Volatile.Write(ref current, service);
                }
                finally
                {
                    options.Close();
                    configuring = false;
                }
            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Backend_SistReservas.Services
{
    // ==========================================================================
    // Bloqueo en memoria por clave (ej. nombre de equipo, o matrícula).
    //
    // Reemplaza el "SELECT ... FOR UPDATE" que se usaba antes contra la tabla
    // `equipos` en base de datos: como ahora el catálogo de equipos vive en
    // memoria (no hay fila de BD que bloquear), la forma de evitar que dos
    // peticiones de entrada concurrentes para el MISMO equipo (o la misma
    // matrícula) se cuelen las dos es serializarlas con un semáforo en
    // memoria, dentro del propio proceso del backend.
    //
    // Nota: esto protege correctamente mientras el backend corra como UN
    // solo proceso (el caso normal de este proyecto, "dotnet run" único).
    // Si en el futuro se despliega con varias instancias del backend detrás
    // de un balanceador de carga, este bloqueo en memoria ya no sería
    // suficiente y habría que volver a un mecanismo a nivel de base de datos.
    // ==========================================================================
    public class KeyedAsyncLock
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        public async Task<IDisposable> AcquireAsync(string key)
        {
            var semaforo = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await semaforo.WaitAsync();
            return new Liberador(semaforo);
        }

        private sealed class Liberador : IDisposable
        {
            private readonly SemaphoreSlim _semaforo;
            private bool _liberado;

            public Liberador(SemaphoreSlim semaforo) => _semaforo = semaforo;

            public void Dispose()
            {
                if (_liberado) return;
                _liberado = true;
                _semaforo.Release();
            }
        }
    }
}

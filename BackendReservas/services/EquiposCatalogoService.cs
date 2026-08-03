using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Backend_SistReservas.Models;

namespace Backend_SistReservas.Services
{
    // Forma en que cada equipo se define en appsettings.json, sección
    // "Laboratorio:Equipos". Ejemplo:
    //
    //   "Laboratorio": {
    //     "Equipos": [
    //       { "Nombre": "Equipo 1", "Ip": "192.168.1.101", "Activo": true },
    //       { "Nombre": "Equipo 2", "Ip": null,              "Activo": true }
    //     ]
    //   }
    public class EquipoConfig
    {
        public string Nombre { get; set; } = string.Empty;
        public string? Ip { get; set; }
        public bool Activo { get; set; } = true;
    }

    // ==========================================================================
    // Catálogo de equipos del laboratorio, en memoria.
    //
    // Se siembra UNA vez al arrancar el backend, leyendo
    // appsettings.json -> "Laboratorio:Equipos". Si esa sección no está
    // configurada (o está vacía), se generan por defecto "Equipo 1".."Equipo 46"
    // sin IP (para no perder el comportamiento actual mientras se configuran
    // las IPs reales).
    //
    // IMPORTANTE (léelo antes de usar en producción):
    // - Para que la IP de un equipo, o la lista de equipos, sobreviva un
    //   reinicio del backend, debe configurarse aquí, en appsettings.json.
    // - Los endpoints PUT/POST/DELETE de EquiposController SÍ funcionan y
    //   modifican este catálogo en caliente (útil para pruebas, o para un
    //   ajuste rápido durante el día), pero esos cambios se pierden al
    //   reiniciar el backend: en el próximo arranque se vuelve a sembrar
    //   exactamente lo que diga appsettings.json.
    // - El campo "Encendido" es naturalmente transitorio (es solo el
    //   resultado del último ping), así que no tiene sentido persistirlo de
    //   todos modos; se recalcula solo en el siguiente ciclo de ping.
    // ==========================================================================
    public class EquiposCatalogoService
    {
        private readonly object _bloqueo = new();
        private readonly List<Equipo> _equipos;
        private int _siguienteId;

        public EquiposCatalogoService(IOptions<LaboratorioOptions> opciones)
        {
            var configurados = opciones.Value.Equipos;

            if (configurados == null || configurados.Count == 0)
            {
                // Comportamiento por defecto: mismos 46 equipos que antes
                // vivían hardcodeados en el código, ahora sin IP asignada
                // (estado "No disponible") hasta que se configuren en
                // appsettings.json.
                _equipos = Enumerable.Range(1, 46)
                    .Select(i => new Equipo { Nombre = $"Equipo {i}", Activo = true, Ip = null })
                    .ToList();
            }
            else
            {
                _equipos = configurados
                    .Select(c => new Equipo
                    {
                        Nombre = c.Nombre,
                        Activo = c.Activo,
                        Ip = string.IsNullOrWhiteSpace(c.Ip) ? null : c.Ip.Trim()
                    })
                    .ToList();
            }

            for (int i = 0; i < _equipos.Count; i++)
            {
                _equipos[i].Id = i + 1;
            }
            _siguienteId = _equipos.Count + 1;
        }

        public List<Equipo> ObtenerTodos()
        {
            lock (_bloqueo)
            {
                // Copia defensiva: quien reciba la lista no debe poder mutar
                // el catálogo interno sin pasar por los métodos de este servicio.
                return _equipos.Select(Clonar).ToList();
            }
        }

        public Equipo? ObtenerPorNombre(string nombre)
        {
            lock (_bloqueo)
            {
                var equipo = _equipos.FirstOrDefault(e => e.Nombre == nombre);
                return equipo == null ? null : Clonar(equipo);
            }
        }

        public Equipo? ObtenerPorId(int id)
        {
            lock (_bloqueo)
            {
                var equipo = _equipos.FirstOrDefault(e => e.Id == id);
                return equipo == null ? null : Clonar(equipo);
            }
        }

        public (bool ok, string? error, Equipo? equipo) Crear(string nombre, string? ip, bool activo)
        {
            lock (_bloqueo)
            {
                if (_equipos.Any(e => e.Nombre == nombre))
                    return (false, $"Ya existe un equipo registrado con el nombre '{nombre}'.", null);

                var nuevo = new Equipo
                {
                    Id = _siguienteId++,
                    Nombre = nombre,
                    Activo = activo,
                    Ip = string.IsNullOrWhiteSpace(ip) ? null : ip.Trim(),
                    Encendido = false
                };
                _equipos.Add(nuevo);
                return (true, null, Clonar(nuevo));
            }
        }

        public Equipo? ActualizarActivo(int id, bool activo)
        {
            lock (_bloqueo)
            {
                var equipo = _equipos.FirstOrDefault(e => e.Id == id);
                if (equipo == null) return null;
                equipo.Activo = activo;
                return Clonar(equipo);
            }
        }

        public Equipo? ActualizarIp(int id, string? ip)
        {
            lock (_bloqueo)
            {
                var equipo = _equipos.FirstOrDefault(e => e.Id == id);
                if (equipo == null) return null;
                equipo.Ip = string.IsNullOrWhiteSpace(ip) ? null : ip.Trim();
                equipo.Encendido = false;
                equipo.UltimoPing = null;
                return Clonar(equipo);
            }
        }

        public Equipo? ActualizarEncendido(int id, bool encendido)
        {
            lock (_bloqueo)
            {
                var equipo = _equipos.FirstOrDefault(e => e.Id == id);
                if (equipo == null) return null;
                equipo.Encendido = encendido;
                return Clonar(equipo);
            }
        }

        // Usado internamente por el servicio de ping en background.
        public void ActualizarEstadoPing(string nombre, bool encendido, DateTime momento)
        {
            lock (_bloqueo)
            {
                var equipo = _equipos.FirstOrDefault(e => e.Nombre == nombre);
                if (equipo == null) return;
                equipo.Encendido = encendido;
                equipo.UltimoPing = momento;
            }
        }

        public bool Eliminar(int id)
        {
            lock (_bloqueo)
            {
                var equipo = _equipos.FirstOrDefault(e => e.Id == id);
                if (equipo == null) return false;
                return _equipos.Remove(equipo);
            }
        }

        private static Equipo Clonar(Equipo e) => new()
        {
            Id = e.Id,
            Nombre = e.Nombre,
            Activo = e.Activo,
            Ip = e.Ip,
            Encendido = e.Encendido,
            UltimoPing = e.UltimoPing
        };
    }
}

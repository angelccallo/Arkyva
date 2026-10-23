namespace Arkyva.Core.Models
{
    /// <summary>Estados tecnicos; el valor inicial no hace visible un archivo incompleto.</summary>
    public enum FileStatus
    {
        /// <summary>Operacion aun no publicada.</summary>
        Pending = 0,
        /// <summary>Contenido completo y disponible.</summary>
        Active = 1,
        /// <summary>Eliminado logicamente.</summary>
        Deleted = 2,
        /// <summary>Operacion fallida que requiere revision.</summary>
        Failed = 3
    }
}

using System.IO;

namespace Arkyva.Core.Interfaces
{
    /// <summary>Optional synchronous physical I/O capability, without task blocking or asynchronous fallback.</summary>
    public interface ISynchronousStorageProvider : IStorageProvider
    {
        /// <summary>Streams to a new key and publishes only complete content. Leaves input open; never overwrites.</summary>
        void Save(string key, Stream content);
        /// <summary>Opens content at its beginning. Caller owns the returned stream.</summary>
        Stream Read(string key);
        /// <summary>Checks physical existence; access failures propagate.</summary>
        bool Exists(string key);
        /// <summary>Physically deletes; missing content is accepted.</summary>
        void Delete(string key);
    }
}

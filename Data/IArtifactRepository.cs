using openstig_save_api.Models;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace openstig_save_api.Data {
    public interface IArtifactRepository
    {
        Task<IEnumerable<Artifact>> GetAllArtifacts();
        Task<Artifact> GetArtifact(string id);

        // query after multiple parameters
        Task<IEnumerable<Artifact>> GetArtifact(string bodyText, DateTime updatedFrom, long headerSizeLimit);

        // add new note document
        Task AddArtifact(Artifact item);

        // update just a single document
        Task<bool> UpdateArtifact(string id, Artifact body);
    }
}
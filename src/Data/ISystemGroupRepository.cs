using openrmf_save_api.Models;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace openrmf_save_api.Data {
    public interface ISystemGroupRepository
    {
        Task<IEnumerable<SystemGroup>> GetAllSystemGroups();
        
        Task<SystemGroup> GetSystemGroup(string id);

        Task<SystemGroup> AddSystemGroup(SystemGroup item);

        Task<bool> DeleteSystemGroup(string id);

        // update just a single system document
        Task<bool> UpdateSystemGroup(string id, SystemGroup body);
    }
}
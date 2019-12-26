// Copyright (c) Cingulara 2019. All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE Version 3, 29 June 2007 license. See LICENSE file in the project root for full license information.
using openrmf_save_api.Models;
using System.Collections.Generic;
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
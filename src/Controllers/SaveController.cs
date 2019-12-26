// Copyright (c) Cingulara 2019. All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE Version 3, 29 June 2007 license. See LICENSE file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using openrmf_save_api.Models;
using System.Text;
using System.IO;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using NATS.Client;

using openrmf_save_api.Data;

namespace openrmf_save_api.Controllers
{
    [Route("/")]
    public class SaveController : Controller
    {
	    private readonly IArtifactRepository _artifactRepo;
        private readonly ISystemGroupRepository _systemGroupRepo;
        private readonly ILogger<SaveController> _logger;
        private readonly IConnection _msgServer;

        public SaveController(IArtifactRepository artifactRepo, ISystemGroupRepository systemGroupRepo, ILogger<SaveController> logger, IOptions<NATSServer> msgServer)
        {
            _logger = logger;
            _artifactRepo = artifactRepo;
            _msgServer = msgServer.Value.connection;
            _systemGroupRepo = systemGroupRepo;
        }
                
        // DELETE and then publish the delete message
        [HttpDelete("artifact/{id}")]
        [Authorize(Roles = "Administrator,Editor")]
        public async Task<IActionResult> DeleteArtifact(string id, [FromForm] Artifact newArtifact)
        {
            try {
                Artifact art = _artifactRepo.GetArtifact(id).Result;
                if (art != null) {
                    _logger.LogInformation("Deleting Checklist {0}", id);
                    var deleted = await _artifactRepo.DeleteArtifact(id);
                    if (deleted)  {
                        // publish to the openrmf delete realm the new ID passed in to remove the score
                        _logger.LogInformation("Publishing the openrmf.checklist.delete message for {0}", id);
                        _msgServer.Publish("openrmf.checklist.delete", Encoding.UTF8.GetBytes(id));
                        _msgServer.Flush();
                        // decrement the system # of checklists by 1
                        _logger.LogInformation("Publishing the openrmf.system.count.delete message for {0}", id);
                        _msgServer.Publish("openrmf.system.count.delete", Encoding.UTF8.GetBytes(art.systemGroupId));
                        _msgServer.Flush();                        
                        return Ok();
                    }
                    else {
                        _logger.LogWarning("Checklist id {0} not deleted correctly", id);
                        return NotFound();
                    }
                }
                else {
                    _logger.LogWarning("Checklist id {0} not found", id);
                    return NotFound();
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error Deleting Checklist {0}", id);
                return BadRequest();
            }
        }
        
        // DELETE and then publish the delete message
        [HttpDelete("system/{id}")]
        [Authorize(Roles = "Administrator,Editor")]
        public async Task<IActionResult> DeleteSystem(string id)
        {
            try {
                SystemGroup sys = _systemGroupRepo.GetSystemGroup(id).Result;
                if (sys != null) {
                    _logger.LogInformation("Deleting System {0} and all checklists", id);
                    var deleted = await _systemGroupRepo.DeleteSystemGroup(id);
                    if (deleted)  {
                        // get all checklists for this system and delete each one at a time, then run the publish on score delete
                        var checklists = await _artifactRepo.GetSystemArtifacts(id);
                        foreach (Artifact a in checklists) {
                            _logger.LogInformation("Deleting Checklist {0} from System {1}", a.InternalId.ToString(), id);
                            var checklistDeleted = await _artifactRepo.DeleteArtifact(a.InternalId.ToString());
                            if (checklistDeleted)  {
                                // publish to the openrmf delete realm the new ID passed in to remove the score
                                _logger.LogInformation("Publishing the openrmf.checklist.delete message for {0}", a.InternalId.ToString());
                                _msgServer.Publish("openrmf.checklist.delete", Encoding.UTF8.GetBytes(a.InternalId.ToString()));
                                _msgServer.Flush();
                            }
                        }
                        _logger.LogInformation("Finished deleting cleanup for System {0}", id);
                        return Ok();
                    }
                    else {
                        _logger.LogWarning("System id {0} not deleted correctly", id);
                        return NotFound();
                    }
                }
                else {
                    _logger.LogWarning("System id {0} not found", id);
                    return NotFound();
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error Deleting System {0}", id);
                return BadRequest();
            }
        }


        // DELETE system checklists only, not the system, and then publish the delete message
        [HttpDelete("system/{id}/artifacts")]
        [Authorize(Roles = "Administrator,Editor")]
        public async Task<IActionResult> DeleteSystemChecklists(string id, [FromForm] string checklistIds)
        {
            try {
                SystemGroup sys = _systemGroupRepo.GetSystemGroup(id).Result;
                if (sys != null) {
                    string[] ids;
                    _logger.LogInformation("Deleting System {0} checklists only", id);
                    if (string.IsNullOrEmpty(checklistIds)){
                        // get all checklists for this system and delete each one at a time, then run the publish on score delete
                        var checklists = await _artifactRepo.GetSystemArtifacts(id);
                        List<string> lstChecklistIds = new List<string>();
                        foreach (Artifact a in checklists) {
                            // add the ID as a string to the list
                            lstChecklistIds.Add(a.InternalId.ToString());
                        }
                        // push the list to an array
                        ids = lstChecklistIds.ToArray();         
                    }
                    else {
                        // split on the command and get back an array to cycle through
                        ids = checklistIds.Split(",");
                    }

                    // now cycle through all the IDs and run with it
                    foreach (string checklist in ids) {
                        _logger.LogInformation("Deleting Checklist {0} from System {1}", checklist, id);
                        var checklistDeleted = await _artifactRepo.DeleteArtifact(checklist);
                        if (checklistDeleted)  {
                            // publish to the openrmf delete realm the new ID passed in to remove the score
                            _logger.LogInformation("Publishing the openrmf.checklist.delete message for {0}", checklist);
                            _msgServer.Publish("openrmf.checklist.delete", Encoding.UTF8.GetBytes(checklist));
                            _msgServer.Flush();
                            // decrement the system # of checklists by 1
                            _logger.LogInformation("Publishing the openrmf.system.count.delete message for {0}", id);
                            _msgServer.Publish("openrmf.system.count.delete", Encoding.UTF8.GetBytes(id));
                            _msgServer.Flush();
                        }
                    }

                    _logger.LogInformation("Finished deleting checklists for System {0}", id);
                    return Ok();
                }
                else {
                    _logger.LogWarning("System id {0} not found", id);
                    return NotFound();
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error Deleting System Checklists {0}", id);
                return BadRequest();
            }
        }

        // POST add a new system 
        [HttpPost("system")]
        [Authorize(Roles = "Administrator,Editor")]
        public async Task<IActionResult> CreateChecklist(string title, string description, IFormFile nessusFile)
        {
          try {
                string rawNessusFile =  string.Empty;
                var claim = this.User.Claims.Where(x => x.Type == System.Security.Claims.ClaimTypes.NameIdentifier).FirstOrDefault();

                // create the record to use
                SystemGroup sg = new SystemGroup();
                sg.created = DateTime.Now;

                if (!string.IsNullOrEmpty(title)) {
                    sg.title = title;
                }
                else {
                    _logger.LogInformation("No title passed so returning a 404");
                    BadRequest("You must enter a title.");
                }

                // get the file for Nessus if there is one
                if (nessusFile != null) {
                    if (nessusFile.FileName.ToLower().EndsWith(".nessus")) {
                        _logger.LogInformation("Reading the System {0} Nessus ACAS file", title);
                        using (var reader = new StreamReader(nessusFile.OpenReadStream()))
                        {
                            rawNessusFile = reader.ReadToEnd();  
                        }
                        rawNessusFile = SanitizeData(rawNessusFile);
                    }
                    else {
                        // log this is a bad Nessus ACAS scan file
                        return BadRequest();
                    }
                }

                // add the information
                if (!string.IsNullOrEmpty(description)) {
                    sg.description = description;
                }
                if (!string.IsNullOrEmpty(rawNessusFile)) {
                    // save the XML to use later on
                    sg.rawNessusFile = rawNessusFile;
                }
                
                // grab the user/system ID from the token if there which is *should* always be
                if (claim != null) { // get the value
                    sg.createdBy = Guid.Parse(claim.Value);
                }

                // save the new record
                _logger.LogInformation("Saving the System {0}", title);
                await _systemGroupRepo.AddSystemGroup(sg);
                // we are finally done
                return Ok();
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error Creating the System {0}", title);
                return BadRequest();
            }
        }

        // PUT a system update
        [HttpPut("system/{systemGroupId}")]
        [Authorize(Roles = "Administrator,Editor")]
        public async Task<IActionResult> UpdateSystem(string systemGroupId, string title, string description, IFormFile nessusFile)
        {
          try {
                string rawNessusFile =  string.Empty;
                var claim = this.User.Claims.Where(x => x.Type == System.Security.Claims.ClaimTypes.NameIdentifier).FirstOrDefault();

                // get the file for Nessus if there is one
                if (nessusFile != null) {
                    if (nessusFile.FileName.ToLower().EndsWith(".nessus")) {
                        _logger.LogInformation("Reading the the System {0} Nessus ACAS file", systemGroupId);
                        using (var reader = new StreamReader(nessusFile.OpenReadStream()))
                        {
                            rawNessusFile = reader.ReadToEnd();  
                        }
                        rawNessusFile = SanitizeData(rawNessusFile);
                    }
                    else {
                        // log this is a bad Nessus ACAS scan file
                        return BadRequest();
                    }
                }

                // see if this is a valid system
                // update and fill in the same info
                SystemGroup sg = _systemGroupRepo.GetSystemGroup(systemGroupId).GetAwaiter().GetResult();
                if (sg == null) {
                    // not a valid system group ID passed in
                    return BadRequest(); 
                }
                sg.updatedOn = DateTime.Now;

                // if it is update the information
                if (!string.IsNullOrEmpty(description)) {
                    sg.description = description;
                }
                if (!string.IsNullOrEmpty(rawNessusFile)) {
                    // save the XML to use later on
                    sg.rawNessusFile = rawNessusFile;
                }
                if (!string.IsNullOrEmpty(title)) {
                    if (sg.title.Trim() != title.Trim()) {
                        // change in the title so update it
                        _logger.LogInformation("Updating the System Title for {0} to {1}", systemGroupId, title);
                        sg.title = title;
                        // if the title is different, it should change across all other checklist files
                        // publish to the openrmf update system realm the new title we can use it
                        _msgServer.Publish("openrmf.system.update." + systemGroupId.Trim(), Encoding.UTF8.GetBytes(title));
                        _msgServer.Flush();                
                    }
                }
                // grab the user/system ID from the token if there which is *should* always be
                if (claim != null) { // get the value
                    sg.updatedBy = Guid.Parse(claim.Value);
                }
                // save the new record
                _logger.LogInformation("Updating the System {0}", systemGroupId);
                await _systemGroupRepo.UpdateSystemGroup(systemGroupId, sg);
                // we are finally done
                return Ok();
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error Updating the System {0}", systemGroupId);
                return BadRequest();
            }
        }

        private string SanitizeData (string rawdata) {
            return rawdata.Replace("\t","").Replace(">\n<","><");
        }
    }
}

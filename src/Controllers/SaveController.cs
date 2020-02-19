// Copyright (c) Cingulara LLC 2019 and Tutela LLC 2019. All rights reserved.
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
using Newtonsoft.Json;

using openrmf_save_api.Data;
using openrmf_save_api.Classes;

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

        /// <summary>
        /// DELETE Called from the OpenRMF UI (or external access) to delete a checklist by its ID.
        /// Also deletes all scores for those checklists.
        /// </summary>
        /// <param name="id">The ID of the artifact passed in</param>
        /// <returns>
        /// HTTP Status showing it was deleted or that there is an error.
        /// </returns>
        /// <response code="200">Returns the newly created item</response>
        /// <response code="400">If the item did not delete correctly</response>
        /// <response code="404">If the ID was not found</response>
        [HttpDelete("artifact/{id}")]
        [Authorize(Roles = "Administrator,Editor")]
        public async Task<IActionResult> DeleteArtifact(string id)
        {
            try {
                _logger.LogInformation("Calling DeleteArtifact({0})", id);
                Artifact art = _artifactRepo.GetArtifact(id).Result;
                if (art != null) {
                    _logger.LogInformation("Deleting Checklist {0}", id);
                    var deleted = await _artifactRepo.DeleteArtifact(id);
                    if (deleted)  {
                        var claim = this.User.Claims.Where(x => x.Type == System.Security.Claims.ClaimTypes.NameIdentifier).FirstOrDefault();
                        // publish to the openrmf delete realm the new ID passed in to remove the score
                        _logger.LogInformation("Publishing the openrmf.checklist.delete message for {0}", id);
                        _msgServer.Publish("openrmf.checklist.delete", Encoding.UTF8.GetBytes(id));
                        _msgServer.Flush();
                        // decrement the system # of checklists by 1
                        _logger.LogInformation("Publishing the openrmf.system.count.delete message for {0}", id);
                        _msgServer.Publish("openrmf.system.count.delete", Encoding.UTF8.GetBytes(art.systemGroupId));
                        _msgServer.Flush();
                        _logger.LogInformation("Called DeleteArtifact({0}) successfully", id);                   
         
                        // publish an audit event
                        _logger.LogInformation("DeleteArtifact() publish an audit message on a deleted checklist {0}.", id);
                        Audit newAudit = GenerateAuditMessage(claim, "delete checklist");
                        newAudit.message = string.Format("DeleteArtifact() delete a single checklist {0}.", id);
                        newAudit.url = string.Format("DELETE /artifact/{0}",id);
                        _msgServer.Publish("openrmf.audit.save", Encoding.UTF8.GetBytes(Compression.CompressString(JsonConvert.SerializeObject(newAudit))));
                        _msgServer.Flush();
                        return Ok();
                    }
                    else {
                        _logger.LogWarning("DeleteArtifact() Checklist id {0} not deleted correctly", id);
                        return NotFound();
                    }
                }
                else {
                    _logger.LogWarning("DeleteArtifact() Checklist id {0} not found", id);
                    return NotFound();
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "DeleteArtifact() Error Deleting Checklist {0}", id);
                return BadRequest();
            }
        }


        /// <summary>
        /// PUT Updating a checklist record from the UI or external that can update the checklist asset information.
        /// </summary>
        /// <param name="artifactId">The ID of the checklist passed in</param>
        /// <param name="systemGroupId">The ID of the system passed in</param>
        /// <param name="hostname">The hostname of the checklist machine</param>
        /// <param name="domainname">The full domain name of the checklist machine</param>
        /// <param name="techarea">The technology area of the checklist machine</param>
        /// <param name="assettype">The asset type of the checklist machine</param>
        /// <param name="machinerole">The role of the checklist machine</param>
        /// <param name="checklistFile">A new Checklist or SCAP scan results file, if any</param>
        /// <returns>
        /// HTTP Status showing it was updated or that there is an error.
        /// </returns>
        /// <response code="200">Returns the newly created item</response>
        /// <response code="400">If the item did not create correctly</response>
        /// <response code="404">If the system ID was not found</response>
        [HttpPut("artifact/{artifactId}")]
        [Authorize(Roles = "Administrator,Editor")]
        public async Task<IActionResult> UpdateChecklist(string artifactId, string systemGroupId, string hostname, string domainname, 
            string techarea, string assettype, string machinerole, IFormFile checklistFile)
        {
          try {
                _logger.LogInformation("Calling UpdateChecklist(system: {0}, checklist: {1})", systemGroupId, artifactId);
                // see if this is a valid system
                // update and fill in the same info
                Artifact checklist = _artifactRepo.GetArtifactBySystem(systemGroupId, artifactId).GetAwaiter().GetResult();
                // the new raw checklist string
                string newChecklistString = "";

                if (checklist == null) {
                    // not a valid system group Id or checklist Id passed in
                    _logger.LogWarning("UpdateChecklist() Error with the System: {0}, Checklist: {1}} not a valid system Id or checklist Id", systemGroupId, artifactId);
                    return NotFound(); 
                }
                checklist.updatedOn = DateTime.Now;

                var claim = this.User.Claims.Where(x => x.Type == System.Security.Claims.ClaimTypes.NameIdentifier).FirstOrDefault();
                // grab the user/system ID from the token if there which is *should* always be
                if (claim != null) { // get the value
                    checklist.updatedBy = Guid.Parse(claim.Value);
                }
                // get the raw checklist, put into the classes, update the asset information, then save the checklist back to a string
                CHECKLIST chk = ChecklistLoader.LoadChecklist(checklist.rawChecklist);
                // hostname
                if (!string.IsNullOrEmpty(hostname)){
                    // set the checklist asset hostname
                    chk.ASSET.HOST_NAME = hostname;
                    // set the artifact record metadata hostname field
                    checklist.hostName = hostname;
                }
                else 
                    chk.ASSET.HOST_NAME = "";
                // domain name
                if (!string.IsNullOrEmpty(domainname))
                    chk.ASSET.HOST_FQDN = domainname;
                else 
                    chk.ASSET.HOST_FQDN = "";
                // tech area
                if (!string.IsNullOrEmpty(techarea))
                    chk.ASSET.TECH_AREA = techarea;
                else 
                    chk.ASSET.TECH_AREA = "";
                // asset type
                if (!string.IsNullOrEmpty(assettype))
                    chk.ASSET.ASSET_TYPE = assettype;
                else 
                    chk.ASSET.ASSET_TYPE = "";
                // role
                if (!string.IsNullOrEmpty(assettype))
                    chk.ASSET.ASSET_TYPE = assettype;
                else 
                    chk.ASSET.ASSET_TYPE = "";

                // serialize into a string again
                System.Xml.Serialization.XmlSerializer xmlSerializer = new System.Xml.Serialization.XmlSerializer(chk.GetType());
                using(StringWriter textWriter = new StringWriter())                
                {
                    xmlSerializer.Serialize(textWriter, chk);
                    newChecklistString = textWriter.ToString();
                }
                // strip out all the extra formatting crap and clean up the XML to be as simple as possible
                System.Xml.Linq.XDocument xDoc = System.Xml.Linq.XDocument.Parse(newChecklistString, System.Xml.Linq.LoadOptions.None);
                // save the new serialized checklist record to the database
                checklist.rawChecklist = xDoc.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
                
                // now save the new record
                _logger.LogInformation("UpdateChecklist() Saving the updated system: {0}, checklist: {1}", systemGroupId, artifactId);
                await _artifactRepo.UpdateArtifact(artifactId, checklist);
                _logger.LogInformation("Called UpdateChecklist(system:{0}, checklist:{1}) successfully", systemGroupId, artifactId);
                // we are finally done

                // publish an audit event
                _logger.LogInformation("UpdateChecklist() publish an audit message on updating a system: {0}, checklist: {1}.", systemGroupId, artifactId);
                Audit newAudit = GenerateAuditMessage(claim, "update checklist data");
                newAudit.message = string.Format("UpdateChecklist() update the system: {0}, checklist: {1}.", systemGroupId, artifactId);
                newAudit.url = string.Format("PUT /artifact/{0}", artifactId);
                _msgServer.Publish("openrmf.audit.save", Encoding.UTF8.GetBytes(Compression.CompressString(JsonConvert.SerializeObject(newAudit))));
                _msgServer.Flush();

                return Ok();
            }
            catch (Exception ex) {
                _logger.LogError(ex, "UpdateChecklist() Error Updating the Checklist Data: {0}, checklist: {1}", systemGroupId, artifactId);
                return BadRequest();
            }
        }

        /// <summary>
        /// DELETE Called from the OpenRMF UI (or external access) to delete an entire system
        /// and all its checklists and scores by its ID.
        /// </summary>
        /// <param name="id">The ID of the system passed in</param>
        /// <returns>
        /// HTTP Status showing it was deleted or that there is an error.
        /// </returns>
        /// <response code="200">Returns the newly created item</response>
        /// <response code="400">If the item did not delete correctly</response>
        /// <response code="404">If the ID was not found</response>
        [HttpDelete("system/{id}")]
        [Authorize(Roles = "Administrator,Editor")]
        public async Task<IActionResult> DeleteSystem(string id)
        {
            try {
                _logger.LogInformation("Calling DeleteSystem({0})", id);
                SystemGroup sys = _systemGroupRepo.GetSystemGroup(id).Result;
                if (sys != null) {
                    var claim = this.User.Claims.Where(x => x.Type == System.Security.Claims.ClaimTypes.NameIdentifier).FirstOrDefault();
                    _logger.LogInformation("DeleteSystem() Deleting System {0} and all checklists", id);
                    var deleted = await _systemGroupRepo.DeleteSystemGroup(id);
                    if (deleted) {
                        // publish an audit event
                        _logger.LogInformation("DeleteSystem() publish an audit message on a deleted system {0}.", id);
                        Audit newAudit = GenerateAuditMessage(claim, "delete system");
                        newAudit.message = string.Format("DeleteSystem() delete the entire system {0}.", id);
                        newAudit.url = string.Format("DELETE /system/{0}",id);
                        _msgServer.Publish("openrmf.audit.save", Encoding.UTF8.GetBytes(Compression.CompressString(JsonConvert.SerializeObject(newAudit))));
                        _msgServer.Flush();

                        // get all checklists for this system and delete each one at a time, then run the publish on score delete
                        var checklists = await _artifactRepo.GetSystemArtifacts(id);
                        foreach (Artifact a in checklists) {
                            _logger.LogInformation("DeleteSystem() Deleting Checklist {0} from System {1}", a.InternalId.ToString(), id);
                            var checklistDeleted = await _artifactRepo.DeleteArtifact(a.InternalId.ToString());
                            if (checklistDeleted)  {
                                // publish to the openrmf delete realm the new ID passed in to remove the score
                                _logger.LogInformation("DeleteSystem() Publishing the openrmf.checklist.delete message for {0}", a.InternalId.ToString());
                                _msgServer.Publish("openrmf.checklist.delete", Encoding.UTF8.GetBytes(a.InternalId.ToString()));
                                _msgServer.Flush();

                                // publish an audit event
                                _logger.LogInformation("DeleteSystem() publish an audit message on a deleted checklist {0} on system {1}.", a.InternalId.ToString(), id);
                                // reset the object
                                newAudit = new Audit();
                                newAudit = GenerateAuditMessage(claim, "delete checklist from a system delete");
                                newAudit.message = string.Format("DeleteSystem() delete the checklist {0} from the system {1}.", a.InternalId.ToString(), id);
                                newAudit.url = string.Format("DELETE /system/{0}",id);
                                _msgServer.Publish("openrmf.audit.save", Encoding.UTF8.GetBytes(Compression.CompressString(JsonConvert.SerializeObject(newAudit))));
                                _msgServer.Flush();
                            }
                        }
                        _logger.LogInformation("DeleteSystem() Finished deleting cleanup for System {0}", id);
                        _logger.LogInformation("Called DeleteSystem({0}) successfully", id);
                        return Ok();
                    }
                    else {
                        _logger.LogWarning("DeleteSystem() System id {0} not deleted correctly", id);
                        return NotFound();
                    }
                }
                else {
                    _logger.LogWarning("DeleteSystem() System id {0} not found", id);
                    return NotFound();
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "DeleteSystem() Error Deleting System {0}", id);
                return BadRequest();
            }
        }

        /// <summary>
        /// DELETE Called from the OpenRMF UI (or external access) to delete all checklist found with 
        /// the system ID. Also deletes all their scores.
        /// </summary>
        /// <param name="id">The ID of the artifact passed in</param>
        /// <param name="checklistIds">The IDs in an array of all checklists to delete</param>
        /// <returns>
        /// HTTP Status showing it was deleted or that there is an error.
        /// </returns>
        /// <response code="200">Returns the newly created item</response>
        /// <response code="400">If the item did not delete correctly</response>
        /// <response code="404">If the ID was not found</response>
        [HttpDelete("system/{id}/artifacts")]
        [Authorize(Roles = "Administrator,Editor")]
        public async Task<IActionResult> DeleteSystemChecklists(string id, [FromForm] string checklistIds)
        {
            try {
                _logger.LogInformation("Calling DeleteSystemChecklists({0})", id);
                SystemGroup sys = _systemGroupRepo.GetSystemGroup(id).Result;
                if (sys != null) {
                    string[] ids;
                    _logger.LogInformation("DeleteSystemChecklists() Deleting System {0} checklists only", id);
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
                    var claim = this.User.Claims.Where(x => x.Type == System.Security.Claims.ClaimTypes.NameIdentifier).FirstOrDefault();
                    Audit newAudit;
                    foreach (string checklist in ids) {
                        _logger.LogInformation("DeleteSystemChecklists() Deleting Checklist {0} from System {1}", checklist, id);
                        var checklistDeleted = await _artifactRepo.DeleteArtifact(checklist);
                        if (checklistDeleted)  {
                            // publish to the openrmf delete realm the new ID passed in to remove the score
                            _logger.LogInformation("DeleteSystemChecklists() Publishing the openrmf.checklist.delete message for {0}", checklist);
                            _msgServer.Publish("openrmf.checklist.delete", Encoding.UTF8.GetBytes(checklist));
                            _msgServer.Flush();
                            // decrement the system # of checklists by 1
                            _logger.LogInformation("DeleteSystemChecklists() Publishing the openrmf.system.count.delete message for {0}", id);
                            _msgServer.Publish("openrmf.system.count.delete", Encoding.UTF8.GetBytes(id));
                            _msgServer.Flush();

                            // publish an audit event
                            _logger.LogInformation("DeleteSystemChecklists() publish an audit message on a deleted checklist {0} on system {1}.", checklist, id);
                            newAudit = new Audit();
                            newAudit = GenerateAuditMessage(claim, "delete checklist from list of checklists selected");
                            newAudit.message = string.Format("DeleteSystemChecklists() delete the checklist {0} from the system {1}.", checklist, id);
                            newAudit.url = string.Format("DELETE system/{0}/artifacts",id);
                            _msgServer.Publish("openrmf.audit.save", Encoding.UTF8.GetBytes(Compression.CompressString(JsonConvert.SerializeObject(newAudit))));
                            _msgServer.Flush();
                        }
                    }

                    _logger.LogInformation("DeleteSystemChecklists() Finished deleting checklists for System {0}", id);
                    _logger.LogInformation("Called DeleteSystemChecklists({0}) successfully", id);
                    return Ok();
                }
                else {
                    _logger.LogWarning("DeleteSystemChecklists() System id {0} not found", id);
                    return NotFound();
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "DeleteSystemChecklists() Error Deleting System Checklists {0}", id);
                return BadRequest();
            }
        }

        /// <summary>
        /// POST Creating a system record from the UI or external that can set the title, description, 
        /// and attach a Nessus file.
        /// </summary>
        /// <param name="title">The title/name of the system</param>
        /// <param name="description">The description of the system</param>
        /// <param name="nessusFile">A Nessus scan file, if any</param>
        /// <returns>
        /// HTTP Status showing it was created or that there is an error.
        /// </returns>
        /// <response code="200">Returns the newly created item</response>
        /// <response code="400">If the item did not create correctly</response>
        /// <response code="404">If the system ID was not found</response>
        [HttpPost("system")]
        [Authorize(Roles = "Administrator,Editor")]
        public async Task<IActionResult> CreateSystemGroup(string title, string description, IFormFile nessusFile)
        {
          try {
                _logger.LogInformation("Calling CreateSystemGroup({0})", title);
                string rawNessusFile =  string.Empty;
                var claim = this.User.Claims.Where(x => x.Type == System.Security.Claims.ClaimTypes.NameIdentifier).FirstOrDefault();

                // create the record to use
                SystemGroup sg = new SystemGroup();
                sg.created = DateTime.Now;

                if (!string.IsNullOrEmpty(title)) {
                    sg.title = title;
                }
                else {
                    _logger.LogInformation("CreateSystemGroup() No title passed so returning a 404");
                    BadRequest("You must enter a title.");
                }

                // get the file for Nessus if there is one
                if (nessusFile != null) {
                    if (nessusFile.FileName.ToLower().EndsWith(".nessus")) {
                        _logger.LogInformation("CreateSystemGroup() Reading the System {0} Nessus ACAS file", title);
                        using (var reader = new StreamReader(nessusFile.OpenReadStream()))
                        {
                            rawNessusFile = reader.ReadToEnd();  
                        }
                        rawNessusFile = SanitizeData(rawNessusFile);
                        sg.nessusFilename = nessusFile.FileName; // keep a copy of the file name that was uploaded
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
                _logger.LogInformation("CreateSystemGroup() Saving the System {0}", title);
                var record = await _systemGroupRepo.AddSystemGroup(sg);
                _logger.LogInformation("Called CreateSystemGroup({0}) successfully", title);
                // we are finally done
                
                // publish an audit event
                _logger.LogInformation("CreateSystemGroup() publish an audit message on creating a new system {0}.", sg.title);
                Audit newAudit = GenerateAuditMessage(claim, "create system");
                newAudit.message = string.Format("CreateSystemGroup() create a new system {0}.", sg.title);
                newAudit.url = string.Format("POST /system");
                _msgServer.Publish("openrmf.audit.save", Encoding.UTF8.GetBytes(Compression.CompressString(JsonConvert.SerializeObject(newAudit))));
                _msgServer.Flush();
                return Ok(record);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "CreateSystemGroup() Error Creating the System {0}", title);
                return BadRequest();
            }
        }

        /// <summary>
        /// PUT Updating a system record from the UI or external that can update the title, description, 
        /// and attach a Nessus file.
        /// </summary>
        /// <param name="systemGroupId">The ID of the system passed in</param>
        /// <param name="title">The title/name of the system</param>
        /// <param name="description">The description of the system</param>
        /// <param name="nessusFile">A Nessus scan file, if any</param>
        /// <returns>
        /// HTTP Status showing it was updated or that there is an error.
        /// </returns>
        /// <response code="200">Returns the newly created item</response>
        /// <response code="400">If the item did not create correctly</response>
        /// <response code="404">If the system ID was not found</response>
        [HttpPut("system/{systemGroupId}")]
        [Authorize(Roles = "Administrator,Editor")]
        public async Task<IActionResult> UpdateSystem(string systemGroupId, string title, string description, IFormFile nessusFile)
        {
          try {
                _logger.LogInformation("Calling UpdateSystem({0})", systemGroupId);
                // see if this is a valid system
                // update and fill in the same info
                SystemGroup sg = _systemGroupRepo.GetSystemGroup(systemGroupId).GetAwaiter().GetResult();
                if (sg == null) {
                    // not a valid system group ID passed in
                    _logger.LogWarning("UpdateSystem() Error with the System {0} not a valid system Id", systemGroupId);
                    return NotFound(); 
                }
                sg.updatedOn = DateTime.Now;

                string rawNessusFile =  string.Empty;
                var claim = this.User.Claims.Where(x => x.Type == System.Security.Claims.ClaimTypes.NameIdentifier).FirstOrDefault();

                // get the file for Nessus if there is one
                if (nessusFile != null) {
                    if (nessusFile.FileName.ToLower().EndsWith(".nessus")) {
                        _logger.LogInformation("UpdateSystem() Reading the the System {0} Nessus ACAS file", systemGroupId);
                        using (var reader = new StreamReader(nessusFile.OpenReadStream()))
                        {
                            rawNessusFile = reader.ReadToEnd();  
                        }
                        rawNessusFile = SanitizeData(rawNessusFile);
                        sg.nessusFilename = nessusFile.FileName; // keep a copy of the file name that was uploaded
                    }
                    else {
                        // log this is a bad Nessus ACAS scan file
                        _logger.LogWarning("UpdateSystem() Error with the Nessus uploaded file for System {0}", systemGroupId);
                        return BadRequest("Invalid Nessus file");
                    }
                }

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
                        _logger.LogInformation("UpdateSystem() Updating the System Title for {0} to {1}", systemGroupId, title);
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
                _logger.LogInformation("UpdateSystem() Saving the updated system {0}", systemGroupId);
                await _systemGroupRepo.UpdateSystemGroup(systemGroupId, sg);
                _logger.LogInformation("Called UpdateSystem({0}) successfully", systemGroupId);
                // we are finally done

                // publish an audit event
                _logger.LogInformation("UpdateSystem() publish an audit message on creating a new system {0}.", sg.title);
                Audit newAudit = GenerateAuditMessage(claim, "create system");
                newAudit.message = string.Format("UpdateSystem() update the system ({0}) {1}.", sg.InternalId.ToString(), sg.title);
                newAudit.url = string.Format("PUT /system/{0}",systemGroupId);
                _msgServer.Publish("openrmf.audit.save", Encoding.UTF8.GetBytes(Compression.CompressString(JsonConvert.SerializeObject(newAudit))));
                _msgServer.Flush();

                return Ok();
            }
            catch (Exception ex) {
                _logger.LogError(ex, "UpdateSystem() Error Updating the System {0}", systemGroupId);
                return BadRequest();
            }
        }

        private string SanitizeData (string rawdata) {
            return rawdata.Replace("\t","").Replace(">\n<","><");
        }
        
        private Audit GenerateAuditMessage(System.Security.Claims.Claim claim, string action) {
            Audit audit = new Audit();
            audit.program = "Save API";
            audit.created = DateTime.Now;
            audit.action = action;
            if (claim != null) {
            audit.userid = claim.Value;
            var fullname = claim.Subject.Claims.Where(x => x.Type == "name").FirstOrDefault();
            if (fullname != null) 
                audit.fullname = fullname.Value;
            var username = claim.Subject.Claims.Where(x => x.Type == "preferred_username").FirstOrDefault();
            if (username != null) 
                audit.username = username.Value;
            var useremail = claim.Subject.Claims.Where(x => x.Type.Contains("emailaddress")).FirstOrDefault();
            if (useremail != null) 
                audit.email = useremail.Value;
            }
            return audit;
        }
    }
}

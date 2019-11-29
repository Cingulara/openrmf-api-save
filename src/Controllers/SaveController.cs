using System;
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
                var deleted = await _artifactRepo.DeleteArtifact(id);
                // publish to the openrmf delete realm the new ID passed in
                if (deleted)  {
                    _msgServer.Publish("openrmf.checklist.delete", Encoding.UTF8.GetBytes(id));
                    _msgServer.Flush();
                    return Ok();
                }
                else
                    return NotFound();
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error Deleting {0}", id);
                return BadRequest();
            }
        }

        // POST a system update
        [HttpPost("system/{systemGroupId}")]
        [Authorize(Roles = "Administrator,Editor")]
        public async Task<IActionResult> UpdateChecklist(string systemGroupId, string title, string description, IFormFile nessusFile)
        {
          try {
                string rawNessusFile =  string.Empty;
                var claim = this.User.Claims.Where(x => x.Type == System.Security.Claims.ClaimTypes.NameIdentifier).FirstOrDefault();

                // get the file for Nessus if there is one
                if (nessusFile != null) {
                    if (nessusFile.FileName.ToLower().EndsWith(".nessus")) {
                        using (var reader = new StreamReader(nessusFile.OpenReadStream()))
                        {
                            rawNessusFile = reader.ReadToEnd();  
                        }
                        rawNessusFile = SanitizeData(rawNessusFile);
                    }
                    else {
                        // log this is a bad checklistFile
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

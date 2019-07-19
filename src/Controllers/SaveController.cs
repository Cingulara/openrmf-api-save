using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using openrmf_save_api.Models;
using System.Text;
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
        private readonly ILogger<SaveController> _logger;
        private readonly IConnection _msgServer;

        public SaveController(IArtifactRepository artifactRepo, ILogger<SaveController> logger, IOptions<NATSServer> msgServer)
        {
            _logger = logger;
            _artifactRepo = artifactRepo;
            _msgServer = msgServer.Value.connection;
        }
                
        // DELETE and then publish the delete message
        [HttpDelete("{id}")]
        [Authorize(Roles = "Administrator,Editor")]
        public async Task<IActionResult> DeleteArtifact(string id, [FromForm] Artifact newArtifact)
        {
            try {
                var deleted = await _artifactRepo.DeleteArtifact(id);
                // publish to the openrmf delete realm the new ID passed in
                if (deleted)  {
                    _msgServer.Publish("openrmf.delete", Encoding.UTF8.GetBytes(id));
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
    }
}

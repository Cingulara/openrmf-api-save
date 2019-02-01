
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using openstig_save_api.Models;
using System.IO;
using System.Text;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using System.Xml.Serialization;
using System.Xml;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Newtonsoft.Json;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using NATS.Client;

using openstig_save_api.Data;

namespace openstig_save_api.Controllers
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

        // POST as new
        [HttpPost]
        public async Task<IActionResult> SaveArtifact([FromForm] Artifact newArtifact)
        {
            try {
                Guid newId = Guid.NewGuid();
                await _artifactRepo.AddArtifact(new Artifact () {
                    id = newId,
                    title = newArtifact.title,
                    description = newArtifact.description,
                    created = DateTime.Now,
                    type = newArtifact.type,
                    rawChecklist = newArtifact.rawChecklist
                });
                // publish to the openstig save new realm the new ID we can use
                _msgServer.Publish("openstig.save.new", Encoding.UTF8.GetBytes(newId.ToString()));
                return Ok();
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error Saving");
                return BadRequest();
            }
        }

        // PUT as new
        [HttpPut]
        public async Task<IActionResult> UpdateArtifact([FromForm] Artifact newArtifact)
        {
            try {
                await _artifactRepo.UpdateArtifact(newArtifact.id.ToString(), new Artifact () {
                    title = newArtifact.title,
                    description = newArtifact.description,
                    created = newArtifact.created,
                    type = newArtifact.type,
                    rawChecklist = newArtifact.rawChecklist,
                    updatedOn = DateTime.Now,
                    id = newArtifact.id,
                    InternalId = newArtifact.InternalId
                });
                // publish to the openstig save new realm the new ID we can use
                _msgServer.Publish("openstig.save.update", Encoding.UTF8.GetBytes(newArtifact.id.ToString()));
                return Ok();
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error Saving");
                return BadRequest();
            }
        }
        
    }
}

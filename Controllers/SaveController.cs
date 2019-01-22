
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using openstig_save_api.Classes;
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

using openstig_save_api.Data;

namespace openstig_save_api.Controllers
{
    [Route("api/[controller]")]
    public class SaveController : Controller
    {
	    private readonly IArtifactRepository _artifactRepo;
        private readonly ILogger<SaveController> _logger;
        const string exampleSTIG = "/examples/asd-example.ckl";

        public SaveController(IArtifactRepository artifactRepo, ILogger<SaveController> logger)
        {
            _logger = logger;
            _artifactRepo = artifactRepo;
        }

        // GET api/values
        [HttpPost]
        public async Task<IActionResult> SaveArtifact([FromForm] Artifact newArtifact)
        {
            try {
                await _artifactRepo.AddArtifact(newArtifact);
                return Ok();
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error Saving");
                return BadRequest();
            }
        }
        
    }
}

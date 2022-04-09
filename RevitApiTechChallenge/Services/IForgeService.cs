using RevitApiTechChallenge.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitApiTechChallenge.Services
{
    public interface IForgeService
    {
        Task<ForgeResult> TriggerJob(string[] paths, string version, string url);        
    }
}

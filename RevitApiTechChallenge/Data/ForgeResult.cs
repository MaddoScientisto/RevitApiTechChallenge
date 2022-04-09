using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitApiTechChallenge.Data
{
    public class ForgeResult
    {
        public bool Success { get; set; }
        public string Urn { get; set; }
        public string Error { get; set; }
    }
}

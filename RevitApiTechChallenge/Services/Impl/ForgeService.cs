using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RevitApiTechChallenge.Data;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RevitApiTechChallenge.Services.Impl
{
    public class ForgeService : IForgeService
    {
        private readonly ILogger<ForgeService> _logger;
        private readonly IConfiguration _config;

        private readonly string _baseUrl = "https://developer.api.autodesk.com";
        private readonly string _authPath = "/authentication/v1/authenticate";
        private readonly string _bucketsPath = "/oss/v2/buckets";
        private readonly string _daPath = "/da/us-east/v3/workitems";
        public ForgeService(ILogger<ForgeService> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public async Task<ForgeResult> TriggerJob(string[] paths, string targetVersion, string outputUrl)
        {
            var tokenResult = await GetToken();
            if (!tokenResult.Success)
            {
                return new ForgeResult() { Error = tokenResult.Error, Success = false };
            }
            string token = tokenResult.Token;

            string bucketKey = "revitapitechchallenge";

            // Create the bucket
            using (var client = new HttpClient())
            {

                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                client.DefaultRequestHeaders.Add("x-ads-region", "US");
                dynamic requestData = new
                {
                    bucketKey = "revitapitechchallenge",
                    policyKey = "transient"
                };

                var dataJson = JsonSerializer.Serialize(requestData);

                var requestContent = new StringContent(dataJson, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(String.Concat(_baseUrl, _bucketsPath), requestContent);

                if (!response.IsSuccessStatusCode)
                {
                    // If it's response 409 it means the bucket already exists
                    if (response.StatusCode != System.Net.HttpStatusCode.Conflict)
                    {
                        return new ForgeResult()
                        {
                            Success = false,
                            Error = $"{response.StatusCode.ToString()} {await response.Content.ReadAsStringAsync()}"
                        };

                    }

                }
                else
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    dynamic content = JsonSerializer.Deserialize<ExpandoObject>(jsonResponse);

                    bucketKey = Convert.ToString(content.bucketKey);
                }


            }

            var uploadedFiles = new List<Tuple<string, string, string>>();

            foreach (var path in paths)
            {
                // Upload files to bucket
                using (var client = new HttpClient())
                {
                    if (!File.Exists(path))
                    {

                        return new ForgeResult()
                        {
                            Success = false,
                            Error = $"File [{path}] not found."
                        };
                    }

                    var fileBytes = await File.ReadAllBytesAsync(path);

                    using var form = new MultipartFormDataContent();
                    using var fileContent = new ByteArrayContent(fileBytes);
                    string fileName = Path.GetFileName(path);
                    
                    form.Add(fileContent, "file", fileName);
                    
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);                    

                    var response = await client.PutAsync(string.Concat(_baseUrl, _bucketsPath, "/", bucketKey, "/objects/", fileName), fileContent);
                    if (!response.IsSuccessStatusCode)
                    {
                        string res = await response.Content.ReadAsStringAsync();
                        return new ForgeResult()
                        {
                            Success = false,
                            Error = $"{response.StatusCode} {res}"
                        };


                    }
                    response.EnsureSuccessStatusCode();
                    var responseContent = await response.Content.ReadAsStringAsync();
                    dynamic result = JsonSerializer.Deserialize<ExpandoObject>(responseContent);

                    string objectId = Convert.ToString(result.objectId);
                    var base64ObjectId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(objectId));
                    var location = Convert.ToString(result.location);
                    uploadedFiles.Add(new(base64ObjectId, location, fileName));
                }


            }

            // Start the job
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                // I'm assuming there's only two files here since it was specified it's either rvt/rfa or optional csv/txt
                var rvtFile = uploadedFiles.FirstOrDefault(x => x.Item3.EndsWith(".rvt") || x.Item3.EndsWith(".rfa"));
                var optFile = uploadedFiles.FirstOrDefault(x => !x.Item3.EndsWith(".rvt") && !x.Item3.EndsWith(".rfa"));

                var bodyData = new
                {
                    activityId = "FileUpgraderAppActivity+dev",
                    arguments = new
                    {
                        rvtFile = new
                        {
                            url = rvtFile.Item2,
                            Headers = new
                            {
                                Authorization = $"Bearer {token}"
                            }
                        },
                        optionalFile = new
                        {
                            url = optFile.Item2,
                            Headers = new
                            {
                                Authorization = $"Bearer {token}"
                            }
                        },
                        targetVersion = new
                        {
                            version = targetVersion
                        },
                        resultrvt = new
                        {
                            url = outputUrl,
                            verb = "put",
                            Headers = new
                            {
                                Authorization = $"Bearer {token}"
                            }
                        },
                        resulturn = new
                        {
                            url = outputUrl,
                            verb = "post",
                            Headers = new
                            {
                                Authorization = $"Bearer {token}"
                            }
                        }
                    }
                };

                var response = await client.PostAsync(string.Concat(_baseUrl, _daPath), new StringContent(JsonSerializer.Serialize(bodyData), Encoding.UTF8, "application/json"));
                if (!response.IsSuccessStatusCode)
                {
                    string res = await response.Content.ReadAsStringAsync();
                    return new ForgeResult()
                    {
                        Success = false,
                        Error = $"{response.StatusCode} {res}"
                    };

                }
                response.EnsureSuccessStatusCode();
                
                // Returning the URN
                return new ForgeResult()
                {
                    Success = true,
                    Urn = rvtFile.Item1 // The urn of the rvt file
                };
            }

        }

        public async Task<TokenResult> GetToken()
        {
            string clientId = _config["Forge:ClientId"];
            string clientSecret = _config["Forge:ClientSecret"];
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret)) return new TokenResult()
            {
                Success = false,
                Error = "No keys for the app client secret"
            };

            string token = string.Empty;

            // Get auth token
            using (var client = new HttpClient())
            {
                var data = new Dictionary<string, string>()
                {
                    { "client_id", clientId},
                    { "client_secret", clientSecret},
                    { "grant_type", "client_credentials" },
                    { "scope", "bucket:read bucket:create data:write data:create code:all" }

                };

                var response = await client.PostAsync(String.Concat(_baseUrl, _authPath), new FormUrlEncodedContent(data));
                if (!response.IsSuccessStatusCode)
                {
                    string res = await response.Content.ReadAsStringAsync();
                    return new TokenResult()
                    {
                        Success = false,
                        Error = $"{response.StatusCode} {res}"
                    };
                }
                response.EnsureSuccessStatusCode();
                var jsonResponse = await response.Content.ReadAsStringAsync();
                dynamic content = JsonSerializer.Deserialize<ExpandoObject>(jsonResponse);
                token = Convert.ToString(content.access_token);

                return new TokenResult()
                {
                    Success = true,
                    Token = token
                };
            }
        }

        
      

    }
}

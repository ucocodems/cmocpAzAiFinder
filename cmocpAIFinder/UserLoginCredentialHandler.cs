using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace cmocpAIFinder
{
    public class UserLoginCredentialHandler : ServiceClientCredentials
    {
        public string AccessToken { get; set; }
        public string TenentId { get; set; }

        public AzureCredentials FluentCredential { get; set; }

        public UserLoginCredentialHandler()
        {
        }

        public UserLoginCredentialHandler(string existingToken)
        {
            this.AccessToken = existingToken;
        }

        public override void InitializeServiceClient<T>(ServiceClient<T> client)
        {
            if(this.AccessToken == null)
            {
                AuthenticateUser();
            }
        }

        public void AuthenticateUser()
        {
            var ctx = new AuthenticationContext("https://login.microsoftonline.com/common");

            //These are the coordniates for the Powershell components.  They should be replaced by an ISV specific AAD location
            var mainAuthRes = ctx.AcquireToken(
                "https://management.azure.com",
                "1950a258-227b-4e31-a9cf-717495945fc2",
                new Uri("urn:ietf:wg:oauth:2.0:oob"),
                PromptBehavior.Auto);

            AccessToken = mainAuthRes.AccessToken;
            TenentId = mainAuthRes.TenantId;

            var tokenCredentials = new TokenCredentials(mainAuthRes.AccessToken);

            FluentCredential = new AzureCredentials(
                tokenCredentials,
                tokenCredentials,
                mainAuthRes.TenantId,
                AzureEnvironment.AzureGlobalCloud);
        }

        public override async Task ProcessHttpRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if(request == null)
            {
                throw new ArgumentNullException("request");
            }

            if (AccessToken == null)
            {
                throw new InvalidOperationException("Token Provider Cannot Be Null");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            //request.Version = new Version(apiVersion);
            await base.ProcessHttpRequestAsync(request, cancellationToken);
        }
    }
}

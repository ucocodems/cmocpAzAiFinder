using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.OpenApi.Readers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;

namespace cmocpAIFinder
{

    public class AzureExplorerViewModel : INotifyPropertyChanged
    {
        private UserLoginCredentialHandler authContext = new UserLoginCredentialHandler();

        public event PropertyChangedEventHandler PropertyChanged;

        #region Discovery Properties

        public Dictionary<string,string> Subscriptions { get; set; }

        private string _selectedSubscription;
        public string SelectedSubscription
        {
            get { return _selectedSubscription; }

            set
            {
                _selectedSubscription = value;
                GetResourceGroups();
            }
        }

        public Dictionary<string,string> ResourceGroups { get; set; }
        private string _selectedResourceGroup { get; set; }
        public string SelectedResourceGroup
        {
            get { return _selectedResourceGroup; }

            set
            {
                _selectedResourceGroup = value;
                if(_selectedResourceGroup != null)
                    GetAMLWebServices();
            }
        }
        
        //TODO replace poor tuple data packing with typed struct
        public Dictionary<string,Tuple<string,string>> AMLStudioWebServices { get; set; }
        private Tuple<string, string> _selectedAMLStudioWebService { get; set; }

        public Tuple<string, string> SelectedAMLStudioWebService
        {
            get { return _selectedAMLStudioWebService; }

            set
            {
                _selectedAMLStudioWebService = value;
                if(_selectedAMLStudioWebService != null)
                    GetEndPointDetails();
            }
        }

        #endregion

        #region Stored Service Properties

        private string _aMLStudioWebServiceSwagger;
        public string AMLStudioWebServiceSwagger
        {
            get { return _aMLStudioWebServiceSwagger; }

            set
            {
                _aMLStudioWebServiceSwagger = value;
                OnNotify("AMLStudioWebServiceSwagger");
            }
        }

        private string _serviceURL;

        public string ServiceURL
        {
            get { return _serviceURL; }
            set { _serviceURL = value; OnNotify("ServiceURL"); }
        }

        private string _serviceKeys;
        public string ServiceKeys
        {
            get { return _serviceKeys; }
            set { _serviceKeys = value; OnNotify("ServiceKeys"); }
        }

        private ObservableCollection<string> _inputParams;
        public ObservableCollection<string> InputParams
        {
            get { return _inputParams; }
            set { _inputParams = value; OnNotify("InputParams"); }
        }

        private ObservableCollection<string> _outputParams;
        public ObservableCollection<string> OutputParams
        {
            get { return _outputParams; }
            set { _outputParams = value; OnNotify("OutputParams"); }
        }

        #endregion

        private void OnNotify(string prop)
        {
            if(PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(prop));
            }
        }


        public void Start()
        {
            //Intiate the first two non-user steps of authentication and subscriptions
            Authenticate();
            GetSubscriptions();
        }

        private void Authenticate()
        {
            //Use the multi-tenent endpoint so that we can authenticate for multiple customers
            authContext.AuthenticateUser();
            
        }

        public void GetSubscriptions()
        {
            //Build the base fluent API client 
            var client = RestClient
                .Configure()
                .WithEnvironment(AzureEnvironment.AzureGlobalCloud)
                .WithLogLevel(HttpLoggingDelegatingHandler.Level.None)
                .WithCredentials(authContext.FluentCredential)
                .Build();

            //Connect to the Azure tenent
            var azureAuthSubs = Azure
               .Authenticate(client, authContext.TenentId);

            Subscriptions = new Dictionary<string, string>();

            //List out the available subscriptions for selection
            foreach (var subscription in azureAuthSubs.Subscriptions.List())
            {
                Subscriptions.Add(subscription.SubscriptionId, subscription.DisplayName);
            }

            OnNotify("Subscriptions");
            
        }

        public void GetResourceGroups()
        {
            //Build the base fluent API client again so that we can use a specific subscription
            var clientSub = RestClient
                .Configure()
                .WithEnvironment(AzureEnvironment.AzureGlobalCloud)
                .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                .WithCredentials(authContext.FluentCredential)
                .Build();

            //Connect to specific Azure subscription
            var azureAuthTargetSub = Azure
               .Authenticate(clientSub, authContext.TenentId)
               .WithSubscription(SelectedSubscription);

            ResourceGroups = new Dictionary<string, string>();

            //List all resource groups in the subscription we have permissions to see
            foreach (var resourceGroup in azureAuthTargetSub.ResourceGroups.List())
            {
                Debug.WriteLine($"Resource Group: {resourceGroup.Name} - {resourceGroup.RegionName}");
                ResourceGroups.Add(resourceGroup.Id, resourceGroup.Name);
            }

            OnNotify("ResourceGroups");
        }

        private async void GetAMLWebServices()
        {

            //Build REST proxy for AML Studio Web Services
            var mlClient = new Microsoft.Azure.Management.MachineLearning.WebServices.AzureMLWebServicesManagementClient(authContext, new System.Net.Http.DelegatingHandler[0]);
            mlClient.SubscriptionId = SelectedSubscription;

            //Get all the services in the resource group we selected
            var services = await mlClient.WebServices.ListByResourceGroupWithHttpMessagesAsync(_selectedResourceGroup);

            //Read the response and parse the JSON
            var serviceResponse = await services.Response.Content.ReadAsStringAsync();

            dynamic wsServicesDetails = JsonConvert.DeserializeObject(serviceResponse);

            AMLStudioWebServices = new Dictionary<string, Tuple<string,string>>();

            //Populate the view model with information
            foreach (var service in wsServicesDetails.value)
            {
                //TODO replace poor tuple data packing with typed struct
                AMLStudioWebServices.Add((string)service.properties.title, new Tuple<string,string>((string)service.properties.swaggerLocation,(string)service.name));
            }

            OnNotify("AMLStudioWebServices");


        }

        private async void GetEndPointDetails()
        {
            

            InputParams = new ObservableCollection<string>();
            OutputParams = new ObservableCollection<string>();

            using (var client = new HttpClient())
            {
                var stream = await client.GetStreamAsync(SelectedAMLStudioWebService.Item1);

                var openApiDoc = new OpenApiStreamReader().Read(stream, out var diagnostic);

                //Build Service URL
                ServiceURL = openApiDoc.Servers[0].Url + "/execute?api-version=2.0&format=swagger";

                //Get Keys
                var mlClient = new Microsoft.Azure.Management.MachineLearning.WebServices.AzureMLWebServicesManagementClient(authContext, new System.Net.Http.DelegatingHandler[0]);
                mlClient.SubscriptionId = SelectedSubscription;

                var keys = await mlClient.WebServices.ListKeysWithHttpMessagesAsync(SelectedResourceGroup, SelectedAMLStudioWebService.Item2);

                var keysResponse = await keys.Response.Content.ReadAsStringAsync();

                dynamic wskeysDetails = JsonConvert.DeserializeObject(keysResponse);

                ServiceKeys = wskeysDetails.primary;

                //Get Input schema
                foreach (var endPointInputParams in openApiDoc.Paths["/execute?api-version=2.0&format=swagger"].Operations[Microsoft.OpenApi.Models.OperationType.Post].RequestBody.Content["application/json"].Schema.Properties)
                {
                    foreach(var props in endPointInputParams.Value.Properties)
                    {
                        foreach(var inputProps in props.Value.Items.Properties)
                        {
                            InputParams.Add($"'{inputProps.Key}' of type '{inputProps.Value.Type}'");
                        }
                    }
                }

                //Get Output Schema

                foreach (var endPointOutputResponse in openApiDoc.Paths["/execute?api-version=2.0&format=swagger"].Operations[Microsoft.OpenApi.Models.OperationType.Post].Responses)
                {
                    foreach(var endPointOutputProps in endPointOutputResponse.Value.Content["application/json"].Schema.Properties)
                    {
                        foreach(var endPointOutputPropsVal in endPointOutputProps.Value.Properties)
                        {
                            OutputParams.Add($"'{endPointOutputPropsVal.Key}' of type '{endPointOutputPropsVal.Value.Type}'");
                        }
                    }
                }

            }
        }
    }
}

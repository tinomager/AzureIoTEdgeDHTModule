﻿using System;

namespace AzureIoTEdgeDHTModule
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;

    class Program
    {
        static void Main(string[] args)
        {
            // The Edge runtime gives us the connection string we need -- it is injected as an environment variable
            string connectionString = Environment.GetEnvironmentVariable("EdgeHubConnectionString");

            // Cert verification is not yet fully functional when using Windows OS for the container
            bool bypassCertVerification = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            if (!bypassCertVerification) InstallCert();
            Init(connectionString, bypassCertVerification).Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Add certificate in local cert store for use by client for secure connection to IoT Edge runtime
        /// </summary>
        static void InstallCert()
        {
            string certPath = Environment.GetEnvironmentVariable("EdgeModuleCACertificateFile");
            if (string.IsNullOrWhiteSpace(certPath))
            {
                // We cannot proceed further without a proper cert file
                Console.WriteLine($"Missing path to certificate collection file: {certPath}");
                throw new InvalidOperationException("Missing path to certificate file.");
            }
            else if (!File.Exists(certPath))
            {
                // We cannot proceed further without a proper cert file
                Console.WriteLine($"Missing path to certificate collection file: {certPath}");
                throw new InvalidOperationException("Missing certificate file.");
            }
            X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(new X509Certificate2(X509Certificate2.CreateFromCertFile(certPath)));
            Console.WriteLine("Added Cert: " + certPath);
            store.Close();
        }


        /// <summary>
        /// Initializes the DeviceClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init(string connectionString, bool bypassCertVerification = false)
        {
            Console.WriteLine("Connection String {0}", connectionString);

            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            // During dev you might want to bypass the cert verification. It is highly recommended to verify certs systematically in production
            if (bypassCertVerification)
            {
                mqttSetting.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            }
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            DeviceClient ioTHubModuleClient = DeviceClient.CreateFromConnectionString(connectionString, settings);

            // Attach callback for Twin desired properties updates
            await ioTHubModuleClient.SetDesiredPropertyUpdateCallbackAsync(onDesiredPropertiesUpdate, ioTHubModuleClient);

            // Execute callback method for Twin desired properties updates
            var twin = await ioTHubModuleClient.GetTwinAsync();
            await onDesiredPropertiesUpdate(twin.Properties.Desired, ioTHubModuleClient);

            await ioTHubModuleClient.OpenAsync();

            Console.WriteLine("DHT Edge module client initialized.");

            var thread = new Thread(() => ThreadBody(ioTHubModuleClient));
            thread.Start();

        }

        private static async void ThreadBody(object userContext)
        {
            var url = LocalhostUrl;
            Console.WriteLine($"Connecting to DHT sensor via url {url}");
            DataLoader = new DHTDataLoader(url);
            while (true)
            {
                var deviceClient = userContext as DeviceClient;

                if (deviceClient == null)
                {
                    throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
                }
             
                var data = DataLoader.GetDHTData();

                DHTMessageBody dhttMessageBody;
                if(data == null){
                    Console.WriteLine("Error: Cannot read DHT Data from local machine");
                }
                else{
                    dhttMessageBody = new DHTMessageBody
                                {
                                    timeCreated = DateTime.Now.ToString("hh:mm:ss"),
                                    humidity = data.Humidity,
                                    temperature = data.Temperature
                                };
                
                    var jsonMessage = JsonConvert.SerializeObject(dhttMessageBody);

                    var pipeMessage = new Message(Encoding.UTF8.GetBytes(jsonMessage));

                    pipeMessage.Properties.Add("content-type", "application/json");

                    await deviceClient.SendEventAsync("output1", pipeMessage);

                    Console.WriteLine($"DHT data sent {dhttMessageBody.timeCreated}: {dhttMessageBody.temperature} |  {dhttMessageBody.humidity}");
                }

                Thread.Sleep(Interval);
            }
        }

        private static int Interval { get; set; } = 5000;

        private static string LocalhostUrl { get; set; } = "http://172.17.0.1:3000/";

        private static DHTDataLoader DataLoader { get; set; }
        private static Task onDesiredPropertiesUpdate(TwinCollection desiredProperties, object userContext)
        {
            if (desiredProperties.Count == 0)
            {
                return Task.CompletedTask;
            }

            try
            {
                Console.WriteLine("Desired property change:");
                Console.WriteLine(JsonConvert.SerializeObject(desiredProperties));

                var deviceClient = userContext as DeviceClient;

                if (deviceClient == null)
                {
                    throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
                }

                var reportedProperties = new TwinCollection();

                if (desiredProperties.Contains("interval") && desiredProperties["interval"] != null)
                {
                    Interval = desiredProperties["interval"];

                    reportedProperties["interval"] = Interval;
                }

                if (desiredProperties.Contains("localhusturl") && !string.IsNullOrEmpty(desiredProperties["localhosturl"]))
                {
                    LocalhostUrl = desiredProperties["localhosturl"];

                    reportedProperties["localhosturl"] = LocalhostUrl;
                    DataLoader = new DHTDataLoader(LocalhostUrl);
                }

                if (reportedProperties.Count > 0)
                {
                    deviceClient.UpdateReportedPropertiesAsync(reportedProperties).ConfigureAwait(false);
                }
            }
            catch (AggregateException ex)
            {
                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine();
                    Console.WriteLine("Error when receiving desired property: {0}", exception);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Error when receiving desired property: {0}", ex.Message);
            }

            return Task.CompletedTask;
        }

        private class DHTMessageBody
        {
            public string timeCreated { get; set; }

            public double temperature{ get; set;}

            public double humidity { get; set; }
        }
    }

}

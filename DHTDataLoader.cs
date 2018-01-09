using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AzureIoTEdgeDHTModule
{
    internal class DHTDataLoader
    {
        private string url;

        public DHTDataLoader(string url)
        {
            this.url = url;
        }

        public DHTData GetDHTData()
        {
            return this.LoadData().Result;
        }

        private async Task<DHTData> LoadData()
        {
            try{
                var client = new HttpClient();
                var jsonStringTask = client.GetStringAsync(this.url);

                var jsonString = await jsonStringTask;
                var jobj = JObject.Parse(jsonString);
                
                return new DHTData() { Humidity = jobj["hum"].ToObject<double>(), Temperature = jobj["temp"].ToObject<double>()};
            }
            catch(Exception ex){
                return null;
            }
        }
    }
}
using System;
using System.Diagnostics;
using RestSharp;

namespace HidWizards.UCR.Utilities
{
    internal class HidGuardianClient : IDisposable
    {
        private const string HidGuardianUrl = "http://localhost:26762/api/v1/hidguardian";
        private readonly RestClient _client;

        public HidGuardianClient()
        {
            // HidGuardian is a localhost service. If it is absent or unhealthy, do not let a
            // network timeout make UCR appear to hang during startup/shutdown.
            _client = new RestClient(HidGuardianUrl)
            {
                Timeout = 1000,
                ReadWriteTimeout = 1000
            };
        }

        public void WhitelistProcess()
        {
            var request = new RestRequest("whitelist/add/{id}", Method.GET);
            request.AddUrlSegment("id", Process.GetCurrentProcess().Id.ToString());
            var response = _client.Execute(request);
        }

        public void RemoveWhitelistProcess()
        {
            var request = new RestRequest("whitelist/remove/{id}", Method.GET);
            request.AddUrlSegment("id", Process.GetCurrentProcess().Id.ToString());
            _client.Execute(request);
        }

        public void Dispose()
        {
            RemoveWhitelistProcess();
        }
    }
}

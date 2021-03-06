using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AldursLab.WurmApi.Modules.Wurm.Servers.WurmServersModel;

namespace AldursLab.WurmApi.Modules.Wurm.Servers
{
    class WebFeeds
    {
        private readonly IWurmServerList wurmServerList;
        private readonly IWurmApiLogger logger;

        readonly WebFeedExtractor extractor;

        readonly Dictionary<ServerName, TimeDetails> dataCache = new Dictionary<ServerName, TimeDetails>();

        private DateTimeOffset lastSync = DateTimeOffset.MinValue;

        public WebFeeds(IHttpWebRequests httpWebRequests, IWurmServerList wurmServerList, IWurmApiLogger logger)
        {
            if (httpWebRequests == null) throw new ArgumentNullException(nameof(httpWebRequests));
            if (wurmServerList == null) throw new ArgumentNullException(nameof(wurmServerList));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            this.wurmServerList = wurmServerList;
            this.logger = logger;

            extractor = new WebFeedExtractor(httpWebRequests);
        }

        public TimeDetails GetForServer(ServerName serverName)
        {
            UpdateWebData();

            TimeDetails details;
            if (dataCache.TryGetValue(serverName, out details))
            {
                return new TimeDetails() {ServerDate = details.ServerDate, ServerUptime = details.ServerUptime};
            }
            
            return new TimeDetails();
        }

        public void UpdateWebData()
        {
            if (lastSync < Time.Get.LocalNowOffset.AddHours(-6))
            {
                SyncWebData();
            }
        }

        private void SyncWebData()
        {
            var allServers = wurmServerList.All.ToArray();

            List<KeyValuePair<WurmServerInfo, Task<WebDataExtractionResult>>> jobs = new List<KeyValuePair<WurmServerInfo, Task<WebDataExtractionResult>>>();
            foreach (var wurmServerInfo in allServers)
            {
                var info = wurmServerInfo;
                var task = Task.Factory.StartNew(() => extractor.Extract(info));
                jobs.Add(new KeyValuePair<WurmServerInfo, Task<WebDataExtractionResult>>(wurmServerInfo, task));
            }
            foreach (var job in jobs)
            {
                try
                {
                    var result = job.Value.Result;
                    dataCache[result.ServerName] = new TimeDetails()
                    {
                        ServerDate = new ServerDateStamped()
                        {
                            WurmDateTime = result.WurmDateTime,
                            Stamp = result.LastUpdated
                        },
                        ServerUptime = new ServerUptimeStamped()
                        {
                            Uptime = result.ServerUptime,
                            Stamp = result.LastUpdated
                        }
                    };
                }
                catch (Exception exception)
                {
                    logger.Log(
                        LogLevel.Warn,
                        "WurmServer-LiveLogs Error at web data extraction for server: " + job.Key,
                        this,
                        exception);
                }
            }
            lastSync = Time.Get.LocalNowOffset;
        }
    }
}